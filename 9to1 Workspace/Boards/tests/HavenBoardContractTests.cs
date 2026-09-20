using System.Text;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

public sealed class HavenBoardReducerTests
{
    [Fact]
    public void Move_within_group_matches_appflowy_remove_then_insert_semantics()
    {
        var snapshot = new HavenBoardSnapshot(
            "board-main",
            "Board",
            7,
            [new HavenBoardGroup(
                "todo",
                "To do",
                [
                    new HavenBoardCard("a", "A"),
                    new HavenBoardCard("b", "B"),
                    new HavenBoardCard("c", "C")
                ])]);

        var updated = HavenBoardReducer.Apply(
            snapshot,
            AppFlowyBoardEventAdapter.MoveCardWithinGroup("todo", 0, 1));

        Assert.Equal(new[] { "b", "a", "c" }, updated.Groups[0].Cards.Select(card => card.Id).ToArray());
        Assert.Equal(8, updated.Version);
    }

    [Fact]
    public void Move_between_groups_inserts_at_requested_appflowy_index()
    {
        var snapshot = new HavenBoardSnapshot(
            "board-main",
            "Board",
            1,
            [
                new HavenBoardGroup("todo", "To do", [new HavenBoardCard("a", "A")]),
                new HavenBoardGroup("done", "Done", [new HavenBoardCard("b", "B")])
            ]);

        var updated = HavenBoardReducer.Apply(
            snapshot,
            AppFlowyBoardEventAdapter.MoveCardBetweenGroups("todo", 0, "done", 1));

        Assert.Empty(updated.Groups[0].Cards);
        Assert.Equal(new[] { "b", "a" }, updated.Groups[1].Cards.Select(card => card.Id).ToArray());
    }

    [Fact]
    public void Move_group_reorders_without_changing_card_identity()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var cardIds = snapshot.Groups
            .SelectMany(group => group.Cards)
            .Select(card => card.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var updated = HavenBoardReducer.Apply(snapshot, AppFlowyBoardEventAdapter.MoveGroup(0, 2));

        Assert.Equal("doing", updated.Groups[0].Id);
        Assert.Equal("done", updated.Groups[1].Id);
        Assert.Equal("todo", updated.Groups[2].Id);
        Assert.Equal(
            cardIds,
            updated.Groups
                .SelectMany(group => group.Cards)
                .Select(card => card.Id)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Duplicate_card_ids_are_rejected()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var error = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(snapshot, new CreateCardCommand("todo", "card-2", "Duplicate")));

        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Card_parent_can_be_set_across_groups_and_cleared()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var nested = HavenBoardReducer.Apply(snapshot, new SetCardParentCommand("card-3", "card-1"));
        Assert.Equal("card-1", FindCard(nested, "card-3").ParentCardId);

        var cleared = HavenBoardReducer.Apply(nested, new SetCardParentCommand("card-3", null));
        Assert.Null(FindCard(cleared, "card-3").ParentCardId);
    }

    [Fact]
    public void Missing_and_self_parent_assignments_are_rejected()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();

        var missing = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(snapshot, new SetCardParentCommand("card-3", "missing-card")));
        Assert.Contains("does not exist", missing.Message, StringComparison.OrdinalIgnoreCase);

        var self = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(snapshot, new SetCardParentCommand("card-3", "card-3")));
        Assert.Contains("own parent", self.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parent_cycle_is_rejected_without_publishing_mutated_snapshot()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var first = HavenBoardReducer.Apply(snapshot, new SetCardParentCommand("card-2", "card-1"));
        var second = HavenBoardReducer.Apply(first, new SetCardParentCommand("card-3", "card-2"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            HavenBoardReducer.Apply(second, new SetCardParentCommand("card-1", "card-3")));

        Assert.Contains("cycle", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(FindCard(second, "card-1").ParentCardId);
    }

    [Fact]
    public void Moving_nested_card_between_groups_preserves_parent_identity()
    {
        var nested = HavenBoardReducer.Apply(
            HavenBoardSnapshot.CreateDefault(),
            new SetCardParentCommand("card-3", "card-1"));

        var moved = HavenBoardReducer.Apply(
            nested,
            new MoveCardCommand("done", 0, "doing", 1));

        var card = FindCard(moved, "card-3");
        Assert.Equal("card-1", card.ParentCardId);
        Assert.Equal(new[] { "card-2", "card-3" }, moved.Groups.Single(group => group.Id == "doing").Cards.Select(candidate => candidate.Id));
    }

    [Fact]
    public void Snapshot_validator_rejects_missing_parent_existing_cycle_and_duplicate_ids()
    {
        var missingParent = new HavenBoardSnapshot(
            "board-main",
            "Invalid",
            1,
            [new HavenBoardGroup("todo", "To do", [new HavenBoardCard("child", "Child", "missing")])]);
        Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Validate(missingParent));

        var cycle = new HavenBoardSnapshot(
            "board-main",
            "Invalid",
            1,
            [new HavenBoardGroup(
                "todo",
                "To do",
                [
                    new HavenBoardCard("a", "A", "b"),
                    new HavenBoardCard("b", "B", "a")
                ])]);
        var cycleError = Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Validate(cycle));
        Assert.Contains("cycle", cycleError.Message, StringComparison.OrdinalIgnoreCase);

        var duplicate = new HavenBoardSnapshot(
            "board-main",
            "Invalid",
            1,
            [
                new HavenBoardGroup("todo", "To do", [new HavenBoardCard("same", "One")]),
                new HavenBoardGroup("done", "Done", [new HavenBoardCard("same", "Two")])
            ]);
        var duplicateError = Assert.Throws<InvalidOperationException>(() => HavenBoardReducer.Validate(duplicate));
        Assert.Contains("duplicated", duplicateError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Attachment_metadata_is_added_and_removed_through_typed_commands()
    {
        var snapshot = HavenBoardSnapshot.CreateDefault();
        var attachment = new HavenBoardAttachment(
            "att-one",
            "brief.txt",
            "sha256:" + new string('a', 64));

        var attached = HavenBoardReducer.Apply(snapshot, new AddAttachmentCommand("card-1", attachment));
        var card = attached.Groups[0].Cards.Single(candidate => candidate.Id == "card-1");
        Assert.Equal(attachment, Assert.Single(card.Attachments!));

        var removed = HavenBoardReducer.Apply(attached, new RemoveAttachmentCommand("card-1", "att-one"));
        var reloadedCard = removed.Groups[0].Cards.Single(candidate => candidate.Id == "card-1");
        Assert.Empty(reloadedCard.Attachments!);
    }

    private static HavenBoardCard FindCard(HavenBoardSnapshot snapshot, string cardId) =>
        snapshot.Groups.SelectMany(group => group.Cards).Single(card => card.Id == cardId);
}

public sealed class JsonFileHavenBoardStoreTests
{
    [Fact]
    public async Task Round_trip_preserves_order_hierarchy_and_attachments()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var snapshot = new HavenBoardSnapshot(
                "board-main",
                "Local board",
                42,
                [new HavenBoardGroup(
                    "todo",
                    "To do",
                    [
                        new HavenBoardCard("parent", "Parent"),
                        new HavenBoardCard(
                            "child",
                            "Nested",
                            ParentCardId: "parent",
                            Attachments:
                            [
                                new HavenBoardAttachment(
                                    "attachment-1",
                                    "brief.txt",
                                    "sha256:" + new string('a', 64),
                                    HavenBoardAttachmentAvailability.Available)
                            ])
                    ])]);

            HavenBoardReducer.Validate(snapshot);
            await store.SaveAsync(snapshot);
            var loaded = await store.LoadAsync("board-main");

            Assert.NotNull(loaded);
            HavenBoardReducer.Validate(loaded);
            AssertSnapshotsEquivalent(snapshot, loaded);
        });
    }

    [Fact]
    public async Task Corrupt_primary_recovers_previous_durable_backup()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var first = HavenBoardSnapshot.CreateDefault() with { Title = "Previous", Version = 10 };
            var second = first with { Title = "Current", Version = 11 };

            await store.SaveAsync(first);
            await store.SaveAsync(second);
            await File.WriteAllTextAsync(Path.Combine(root, "board-main.json"), "{ not-valid-json");

            var loaded = await store.LoadAsync("board-main");

            Assert.NotNull(loaded);
            Assert.Equal("Previous", loaded.Title);
            Assert.Equal(10, loaded.Version);
        });
    }

    [Fact]
    public async Task Unsafe_board_id_is_rejected_before_path_resolution()
    {
        await WithStoreAsync(async (store, _) =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.LoadAsync("../outside"));
        });
    }

    [Fact]
    public async Task Command_service_reduces_then_persists()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var service = new HavenBoardCommandService(store);

            var updated = await service.ExecuteAsync(new CreateCardCommand("todo", "card-4", "Persist me"));
            var reloaded = await store.LoadAsync("board-main");

            Assert.NotNull(reloaded);
            AssertSnapshotsEquivalent(updated, reloaded);
            Assert.Contains(reloaded.Groups[0].Cards, card => card.Id == "card-4" && card.Title == "Persist me");
        });
    }

    private static void AssertSnapshotsEquivalent(HavenBoardSnapshot expected, HavenBoardSnapshot actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.Groups.Count, actual.Groups.Count);

        for (var groupIndex = 0; groupIndex < expected.Groups.Count; groupIndex++)
        {
            var expectedGroup = expected.Groups[groupIndex];
            var actualGroup = actual.Groups[groupIndex];
            Assert.Equal(expectedGroup.Id, actualGroup.Id);
            Assert.Equal(expectedGroup.Title, actualGroup.Title);
            Assert.Equal(expectedGroup.Cards.Count, actualGroup.Cards.Count);

            for (var cardIndex = 0; cardIndex < expectedGroup.Cards.Count; cardIndex++)
            {
                var expectedCard = expectedGroup.Cards[cardIndex];
                var actualCard = actualGroup.Cards[cardIndex];
                Assert.Equal(expectedCard.Id, actualCard.Id);
                Assert.Equal(expectedCard.Title, actualCard.Title);
                Assert.Equal(expectedCard.ParentCardId, actualCard.ParentCardId);

                var expectedAttachments = expectedCard.Attachments ?? [];
                var actualAttachments = actualCard.Attachments ?? [];
                Assert.Equal(expectedAttachments.Count, actualAttachments.Count);
                for (var attachmentIndex = 0; attachmentIndex < expectedAttachments.Count; attachmentIndex++)
                    Assert.Equal(expectedAttachments[attachmentIndex], actualAttachments[attachmentIndex]);
            }
        }
    }

    private static async Task WithStoreAsync(Func<JsonFileHavenBoardStore, string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-boards-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await test(store, root);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // Test cleanup must not hide the tested assertion result.
        }
        catch (UnauthorizedAccessException)
        {
            // Test cleanup must not hide the tested assertion result.
        }
    }
}

public sealed class ContentAddressedHavenBoardAttachmentStoreTests
{
    [Fact]
    public async Task Import_is_content_addressed_deduplicated_and_display_name_cannot_escape_storage()
    {
        await WithAttachmentStoreAsync(async (store, root) =>
        {
            var bytes = Encoding.UTF8.GetBytes("local attachment payload");
            await using var firstInput = new MemoryStream(bytes);
            await using var secondInput = new MemoryStream(bytes);

            var first = await store.ImportAsync("board-main", "../../outside.txt", firstInput, "att-one");
            var second = await store.ImportAsync("board-main", "same.txt", secondInput, "att-two");

            Assert.Equal("outside.txt", first.DisplayName);
            Assert.StartsWith("sha256:", first.LocalReference, StringComparison.Ordinal);
            Assert.Equal(first.LocalReference, second.LocalReference);

            var boardDirectory = Path.Combine(root, "board-main");
            Assert.Single(Directory.GetFiles(boardDirectory, "*.blob"));
            Assert.False(File.Exists(Path.Combine(root, "outside.txt")));

            await using var opened = await store.OpenReadAsync("board-main", first);
            Assert.NotNull(opened);
            using var copy = new MemoryStream();
            await opened.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        });
    }

    [Fact]
    public async Task Import_over_size_limit_is_rejected_and_does_not_publish_blob()
    {
        await WithAttachmentStoreAsync(async (_, root) =>
        {
            var store = new ContentAddressedHavenBoardAttachmentStore(root, maxAttachmentBytes: 3);
            await using var input = new MemoryStream([1, 2, 3, 4]);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.ImportAsync("board-main", "too-large.bin", input, "att-large"));

            var boardDirectory = Path.Combine(root, "board-main");
            Assert.True(Directory.Exists(boardDirectory));
            Assert.Empty(Directory.GetFiles(boardDirectory));
        });
    }

    [Fact]
    public async Task Unsafe_ids_and_malformed_local_references_are_rejected()
    {
        await WithAttachmentStoreAsync(async (store, _) =>
        {
            await using var input = new MemoryStream([1, 2, 3]);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                store.ImportAsync("../outside", "file.bin", input, "att-one"));

            var malformed = new HavenBoardAttachment("att-one", "file.bin", "../../outside");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.OpenReadAsync("board-main", malformed));
        });
    }

    [Fact]
    public async Task Existing_deduplicated_blob_must_still_match_its_digest()
    {
        await WithAttachmentStoreAsync(async (store, root) =>
        {
            var bytes = Encoding.UTF8.GetBytes("trusted bytes");
            await using var firstInput = new MemoryStream(bytes);
            var attachment = await store.ImportAsync("board-main", "file.bin", firstInput, "att-one");

            var digest = attachment.LocalReference["sha256:".Length..];
            var blobPath = Path.Combine(root, "board-main", digest + ".blob");
            await File.WriteAllTextAsync(blobPath, "tampered");

            await using var secondInput = new MemoryStream(bytes);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.ImportAsync("board-main", "file.bin", secondInput, "att-two"));
        });
    }

    private static async Task WithAttachmentStoreAsync(
        Func<ContentAddressedHavenBoardAttachmentStore, string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-boards-attachments", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ContentAddressedHavenBoardAttachmentStore(root);
            await test(store, root);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Test cleanup must not hide the tested assertion result.
            }
            catch (UnauthorizedAccessException)
            {
                // Test cleanup must not hide the tested assertion result.
            }
        }
    }
}
