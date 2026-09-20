using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
using Xunit;

namespace CakeOS.Apps.Boards.Hui.Tests;

public sealed class HavenBoardCollaborationCoordinatorTests
{
    [Fact]
    public async Task Default_deny_policy_rejects_remote_mutation_without_save()
    {
        var root = TempBoardDirectory();
        try
        {
            using var inner = new JsonFileHavenBoardStore(root);
            var store = new CountingStore(inner);
            await using var session = await HavenBoardsHuiSession.OpenAsync(store);
            store.ResetSaveCount();
            var coordinator = new HavenBoardCollaborationCoordinator();
            var batch = HavenBoardCollaborationBatch.Create(
                "board-main",
                session.Snapshot.Version,
                "remote",
                [new RenameGroupCommand("todo", "Remote rename")]);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                coordinator.ApplyInboundAsync(session, batch));

            Assert.Equal("To do", session.Snapshot.Groups[0].Title);
            Assert.Equal(0, store.SaveCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Authorised_structural_batch_commits_once_and_survives_reopen()
    {
        var root = TempBoardDirectory();
        try
        {
            using (var inner = new JsonFileHavenBoardStore(root))
            {
                var store = new CountingStore(inner);
                await using var session = await HavenBoardsHuiSession.OpenAsync(store);
                store.ResetSaveCount();
                var coordinator = new HavenBoardCollaborationCoordinator(
                    new OwnerOnlyHavenBoardPermissionProvider("owner"));
                var batch = HavenBoardCollaborationBatch.Create(
                    "board-main",
                    session.Snapshot.Version,
                    "owner",
                    [
                        new CreateCardCommand("todo", "remote-card", "Remote card"),
                        new SetFreeformCardFrameCommand("remote-card", 240, 160, 320, 180, 5)
                    ]);

                var applied = await coordinator.ApplyInboundAsync(session, batch);

                Assert.Equal(1, store.SaveCount);
                Assert.Equal(batch.MutationId, applied.MutationId);
                Assert.Contains(session.Snapshot.Groups[0].Cards, card => card.Id == "remote-card");
                Assert.Contains(session.Snapshot.Freeform!.Items, item => item.CardId == "remote-card");
            }

            using var reopenedStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await HavenBoardsHuiSession.OpenAsync(reopenedStore);
            Assert.Contains(reopened.Snapshot.Groups[0].Cards, card => card.Id == "remote-card");
            Assert.Contains(reopened.Snapshot.Freeform!.Items, item => item.CardId == "remote-card");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Stale_batch_is_conflict_and_never_persists()
    {
        var root = TempBoardDirectory();
        try
        {
            using var inner = new JsonFileHavenBoardStore(root);
            var store = new CountingStore(inner);
            await using var session = await HavenBoardsHuiSession.OpenAsync(store);
            var staleVersion = session.Snapshot.Version;
            await session.ExecuteAsync(new RenameGroupCommand("todo", "Local edit"));
            store.ResetSaveCount();

            var coordinator = new HavenBoardCollaborationCoordinator(
                new OwnerOnlyHavenBoardPermissionProvider("owner"));
            var batch = HavenBoardCollaborationBatch.Create(
                "board-main",
                staleVersion,
                "owner",
                [new RenameGroupCommand("todo", "Stale remote")]);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ApplyInboundAsync(session, batch));

            Assert.Contains("conflict", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Local edit", session.Snapshot.Groups[0].Title);
            Assert.Equal(0, store.SaveCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Caller_constructed_attachment_batch_is_revalidated_and_rejected_before_save()
    {
        var root = TempBoardDirectory();
        try
        {
            using var inner = new JsonFileHavenBoardStore(root);
            var store = new CountingStore(inner);
            await using var session = await HavenBoardsHuiSession.OpenAsync(store);
            store.ResetSaveCount();
            var coordinator = new HavenBoardCollaborationCoordinator(
                new OwnerOnlyHavenBoardPermissionProvider("owner"));
            var attachment = new HavenBoardAttachment(
                "att-remote",
                "remote.bin",
                "sha256:" + new string('b', 64));
            var forged = new HavenBoardCollaborationBatch(
                Guid.NewGuid(),
                "board-main",
                session.Snapshot.Version,
                "owner",
                [new AddAttachmentCommand("card-1", attachment)]);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ApplyInboundAsync(session, forged));

            Assert.Null(session.Snapshot.Groups[0].Cards[0].Attachments);
            Assert.Equal(0, store.SaveCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Applied_mutation_id_is_replay_protected()
    {
        var root = TempBoardDirectory();
        try
        {
            using var inner = new JsonFileHavenBoardStore(root);
            var store = new CountingStore(inner);
            await using var session = await HavenBoardsHuiSession.OpenAsync(store);
            var coordinator = new HavenBoardCollaborationCoordinator(
                new OwnerOnlyHavenBoardPermissionProvider("owner"));
            var mutationId = Guid.NewGuid();
            var first = HavenBoardCollaborationBatch.Create(
                "board-main",
                session.Snapshot.Version,
                "owner",
                [new RenameGroupCommand("todo", "Applied once")],
                mutationId);

            await coordinator.ApplyInboundAsync(session, first);
            store.ResetSaveCount();

            var replay = new HavenBoardCollaborationBatch(
                mutationId,
                "board-main",
                session.Snapshot.Version,
                "owner",
                [new RenameGroupCommand("todo", "Replay must not apply")]);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                coordinator.ApplyInboundAsync(session, replay));

            Assert.Contains("already applied", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Applied once", session.Snapshot.Groups[0].Title);
            Assert.Equal(0, store.SaveCount);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string TempBoardDirectory() =>
        Path.Combine(Path.GetTempPath(), "cakeos-boards-collab-" + Guid.NewGuid().ToString("N"));

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
