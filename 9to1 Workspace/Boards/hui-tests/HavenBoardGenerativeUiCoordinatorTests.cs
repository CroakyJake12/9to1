using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using Xunit;

namespace CakeOS.Apps.Boards.Hui.Tests;

public sealed class HavenBoardGenerativeUiCoordinatorTests
{
    [Fact]
    public async Task Returned_preview_tampering_cannot_replace_coordinator_retained_plan()
    {
        var root = TempBoardDirectory();
        try
        {
            using var inner = new JsonFileHavenBoardStore(root);
            var store = new CountingStore(inner);
            await using var session = await HavenBoardsHuiSession.OpenAsync(store);
            store.ResetSaveCount();
            var coordinator = new HavenBoardGenerativeUiCoordinator();
            var plan = coordinator.PreparePlan(
                session.Snapshot,
                [new CreateCardCommand("todo", "generated", "Generated")]);

            var exposedGroups = Assert.IsType<HavenBoardGroup[]>(plan.Preview.Groups);
            exposedGroups[0] = exposedGroups[0] with { Title = "FORGED PREVIEW" };

            var applied = await coordinator.ApplyAsync(session, plan.Id);

            Assert.Equal("To do", session.Snapshot.Groups[0].Title);
            Assert.Contains(session.Snapshot.Groups[0].Cards, card => card.Id == "generated");
            Assert.Equal(1, store.SaveCount);
            Assert.Equal(plan.Id, applied.PlanId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Stale_plan_is_rejected_without_publishing_or_persisting_generated_state()
    {
        var root = TempBoardDirectory();
        try
        {
            using var inner = new JsonFileHavenBoardStore(root);
            var store = new CountingStore(inner);
            await using var session = await HavenBoardsHuiSession.OpenAsync(store);
            var coordinator = new HavenBoardGenerativeUiCoordinator();
            var plan = coordinator.PreparePlan(
                session.Snapshot,
                [new CreateCardCommand("todo", "stale-generated", "Stale generated")]);

            await session.ExecuteAsync(new RenameGroupCommand("todo", "User changed this"));
            store.ResetSaveCount();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ApplyAsync(session, plan.Id));

            Assert.Equal("User changed this", session.Snapshot.Groups[0].Title);
            Assert.DoesNotContain(
                session.Snapshot.Groups.SelectMany(group => group.Cards),
                card => card.Id == "stale-generated");
            Assert.Equal(0, store.SaveCount);

            var persisted = await inner.LoadAsync("board-main");
            Assert.NotNull(persisted);
            Assert.DoesNotContain(
                persisted.Groups.SelectMany(group => group.Cards),
                card => card.Id == "stale-generated");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Invalid_batch_is_atomic_and_never_saves_partial_reduction()
    {
        var root = TempBoardDirectory();
        try
        {
            using var inner = new JsonFileHavenBoardStore(root);
            var store = new CountingStore(inner);
            await using var session = await HavenBoardsHuiSession.OpenAsync(store);
            store.ResetSaveCount();
            var version = session.Snapshot.Version;

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.ExecuteBatchAsync(
                [
                    new CreateCardCommand("todo", "half-applied", "Must never appear"),
                    new SetCardParentCommand("half-applied", "missing-parent")
                ],
                version));

            Assert.Equal(version, session.Snapshot.Version);
            Assert.DoesNotContain(
                session.Snapshot.Groups.SelectMany(group => group.Cards),
                card => card.Id == "half-applied");
            Assert.Equal(0, store.SaveCount);

            var persisted = await inner.LoadAsync("board-main");
            Assert.NotNull(persisted);
            Assert.Equal(version, persisted.Version);
            Assert.DoesNotContain(
                persisted.Groups.SelectMany(group => group.Cards),
                card => card.Id == "half-applied");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Apply_is_one_shot_and_undo_uses_private_checkpoint_then_survives_reopen()
    {
        var root = TempBoardDirectory();
        try
        {
            using (var inner = new JsonFileHavenBoardStore(root))
            {
                var store = new CountingStore(inner);
                await using var session = await HavenBoardsHuiSession.OpenAsync(store);
                var coordinator = new HavenBoardGenerativeUiCoordinator();
                var beforeVersion = session.Snapshot.Version;
                var plan = coordinator.PreparePlan(
                    session.Snapshot,
                    [
                        new CreateCardCommand("todo", "generated-undo", "Generated then undo"),
                        new SetFreeformCardFrameCommand("generated-undo", 200, 140, 320, 180, 3)
                    ]);

                store.ResetSaveCount();
                var applied = await coordinator.ApplyAsync(session, plan.Id);
                Assert.Equal(1, store.SaveCount);
                Assert.Equal(beforeVersion + 2, session.Snapshot.Version);
                Assert.Contains(session.Snapshot.Groups[0].Cards, card => card.Id == "generated-undo");
                Assert.Contains(session.Snapshot.Freeform!.Items, item => item.CardId == "generated-undo");

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    coordinator.ApplyAsync(session, plan.Id));

                // Mutate the caller-visible return value. Undo must use the coordinator's detached copy.
                var exposedBeforeGroups = Assert.IsType<HavenBoardGroup[]>(applied.Before.Groups);
                exposedBeforeGroups[0] = exposedBeforeGroups[0] with { Title = "FORGED UNDO" };

                store.ResetSaveCount();
                var restored = await coordinator.UndoAsync(session, plan.Id);
                Assert.Equal(1, store.SaveCount);
                Assert.Equal(beforeVersion + 3, restored.Version);
                Assert.Equal("To do", restored.Groups[0].Title);
                Assert.DoesNotContain(restored.Groups.SelectMany(group => group.Cards), card => card.Id == "generated-undo");
                Assert.Null(restored.Freeform);

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    coordinator.UndoAsync(session, plan.Id));
            }

            using var reopenedStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);
            Assert.Equal("To do", reopened.Snapshot.Groups[0].Title);
            Assert.DoesNotContain(
                reopened.Snapshot.Groups.SelectMany(group => group.Cards),
                card => card.Id == "generated-undo");
            Assert.Null(reopened.Snapshot.Freeform);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Undo_is_rejected_after_intervening_user_edit()
    {
        var root = TempBoardDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await using var session = await HavenBoardsHuiSession.OpenAsync(store);
            var coordinator = new HavenBoardGenerativeUiCoordinator();
            var plan = coordinator.PreparePlan(
                session.Snapshot,
                [new CreateCardCommand("todo", "generated-stale-undo", "Generated")]);

            await coordinator.ApplyAsync(session, plan.Id);
            await session.ExecuteAsync(new RenameGroupCommand("todo", "Edited after generation"));

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.UndoAsync(session, plan.Id));

            Assert.Equal("Edited after generation", session.Snapshot.Groups[0].Title);
            Assert.Contains(session.Snapshot.Groups[0].Cards, card => card.Id == "generated-stale-undo");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string TempBoardDirectory() =>
        Path.Combine(Path.GetTempPath(), "cakeos-boards-generative-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class CountingStore(IHavenBoardStore inner) : IHavenBoardStore
    {
        public int SaveCount { get; private set; }

        public Task<HavenBoardSnapshot?> LoadAsync(
            string boardId,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(boardId, cancellationToken);

        public async Task SaveAsync(
            HavenBoardSnapshot snapshot,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            await inner.SaveAsync(snapshot, cancellationToken);
        }

        public void ResetSaveCount() => SaveCount = 0;
    }
}
