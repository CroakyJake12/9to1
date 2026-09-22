using System.Text;
using System.Text.Json;
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
            await File.WriteAllTextAsync(Path.Combine(root, "board-main.9to1board"), "{ not-valid-json");

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
    public async Task Physical_document_has_identity_version_metadata_and_complete_snapshot()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var snapshot = new HavenBoardSnapshot(
                "board-main",
                "A-Level Maths",
                42,
                [new HavenBoardGroup("pure", "Pure Mathematics", [
                    new HavenBoardCard("trigonometry", "Trigonometry", Attachments: [
                        new HavenBoardAttachment("formula-sheet", "formulae.pdf", "sha256:" + new string('b', 64))])])],
                new HavenBoardFreeformLayout([new HavenBoardFreeformItem("trigonometry", 12, 24, 320, 200, 1)]));

            await store.SaveAsync(snapshot);

            var path = store.GetDocumentPath("board-main");
            Assert.Equal(Path.Combine(root, "board-main.9to1board"), path);
            Assert.True(File.Exists(path));
            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(HavenBoardDocument.FormatIdentity, json.RootElement.GetProperty("format").GetString());
            Assert.Equal(HavenBoardDocument.CurrentSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("documentId").GetGuid());
            Assert.Equal("A-Level Maths", json.RootElement.GetProperty("snapshot").GetProperty("title").GetString());

            var reopened = await store.LoadAsync("board-main");
            Assert.NotNull(reopened);
            AssertSnapshotsEquivalent(snapshot, reopened!);
            var frame = Assert.Single(reopened.Freeform!.Items);
            Assert.Equal(new HavenBoardFreeformItem("trigonometry", 12, 24, 320, 200, 1), frame);
        });
    }

    [Fact]
    public async Task Legacy_json_is_imported_without_deleting_source_and_written_as_physical_document()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var legacy = HavenBoardSnapshot.CreateDefault() with { Title = "Old notes", Version = 9 };
            var legacyPath = Path.Combine(root, "board-main.json");
            var originalBytes = JsonSerializer.Serialize(legacy, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await File.WriteAllTextAsync(legacyPath, originalBytes);

            var imported = await store.LoadAsync("board-main");

            Assert.NotNull(imported);
            Assert.Equal("Old notes", imported!.Title);
            Assert.Equal(HavenBoardLoadDisposition.MigratedLegacyJson, store.LastLoadDisposition);
            Assert.Equal(originalBytes, await File.ReadAllTextAsync(legacyPath));
            Assert.True(File.Exists(store.GetDocumentPath("board-main")));
        });
    }

    [Fact]
    public async Task Unsupported_future_schema_fails_safely_instead_of_returning_empty_board()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var future = new
            {
                format = HavenBoardDocument.FormatIdentity,
                schemaVersion = HavenBoardDocument.CurrentSchemaVersion + 1,
                documentId = Guid.NewGuid(),
                createdUtc = DateTimeOffset.UtcNow,
                modifiedUtc = DateTimeOffset.UtcNow,
                snapshot = HavenBoardSnapshot.CreateDefault()
            };
            await File.WriteAllTextAsync(store.GetDocumentPath("board-main"), JsonSerializer.Serialize(future));

            var error = await Assert.ThrowsAsync<UnsupportedHavenBoardDocumentVersionException>(() => store.LoadAsync("board-main"));

            Assert.Equal(HavenBoardDocument.CurrentSchemaVersion + 1, error.ActualVersion);
        });
    }

    [Fact]
    public async Task Invalid_save_leaves_previous_valid_physical_document_usable()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var valid = HavenBoardSnapshot.CreateDefault() with { Title = "Known good" };
            await store.SaveAsync(valid);
            var invalid = valid with { Groups = [new HavenBoardGroup("bad", "Bad", [new HavenBoardCard("a", "A", "missing")])] };

            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(invalid));

            var reopened = await store.LoadAsync("board-main");
            Assert.NotNull(reopened);
            Assert.Equal("Known good", reopened!.Title);
        });
    }

    [Fact]
    public async Task Corrupt_primary_reports_backup_recovery_without_replacing_corrupt_bytes()
    {
        await WithStoreAsync(async (store, _) =>
        {
            await store.SaveAsync(HavenBoardSnapshot.CreateDefault() with { Title = "Backup" });
            await store.SaveAsync(HavenBoardSnapshot.CreateDefault() with { Title = "Primary" });
            var path = store.GetDocumentPath("board-main");
            const string corrupt = "{ this is corrupt";
            await File.WriteAllTextAsync(path, corrupt);

            var recovered = await store.LoadAsync("board-main");

            Assert.NotNull(recovered);
            Assert.Equal("Backup", recovered!.Title);
            Assert.Equal(HavenBoardLoadDisposition.RecoveredFromBackup, store.LastLoadDisposition);
            Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
        });
    }

    [Fact]
    public async Task Schema_zero_migration_is_in_memory_until_explicit_save_and_preserves_original_on_failure()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var path = store.GetDocumentPath("board-main");
            var schemaZero = JsonSerializer.Serialize(new
            {
                format = HavenBoardDocument.FormatIdentity,
                schemaVersion = 0,
                snapshot = HavenBoardSnapshot.CreateDefault() with { Title = "Migrated" }
            });
            await File.WriteAllTextAsync(path, schemaZero);

            var migrated = await store.LoadAsync("board-main");
            Assert.NotNull(migrated);
            Assert.Equal("Migrated", migrated!.Title);
            Assert.Equal(schemaZero, await File.ReadAllTextAsync(path));

            await store.SaveAsync(migrated);
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(HavenBoardDocument.CurrentSchemaVersion, saved.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.True(File.Exists(path + ".bak"));
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

    [Fact]
    public async Task Rename_preserves_stable_document_identity_and_content()
    {
        await WithStoreAsync(async (store, _) =>
        {
            await store.SaveAsync(HavenBoardSnapshot.CreateDefault() with { Title = "Before" });
            var beforeId = JsonDocument.Parse(await File.ReadAllTextAsync(store.GetDocumentPath("board-main")))
                .RootElement.GetProperty("documentId").GetGuid();

            var current = await store.LoadAsync("board-main");
            Assert.NotNull(current);
            await store.SaveAsync(current! with { Title = "A-Level Maths" });

            var after = await store.LoadAsync("board-main");
            Assert.NotNull(after);
            Assert.Equal("board-main", after!.Id);
            Assert.Equal("A-Level Maths", after.Title);
            var afterId = JsonDocument.Parse(await File.ReadAllTextAsync(store.GetDocumentPath("board-main")))
                .RootElement.GetProperty("documentId").GetGuid();
            Assert.Equal(beforeId, afterId);
        });
    }

    [Fact]
    public async Task Reordered_groups_survive_close_and_reopen()
    {
        await WithStoreAsync(async (store, _) =>
        {
            await store.SaveAsync(HavenBoardSnapshot.CreateDefault());
            var current = await store.LoadAsync("board-main");
            Assert.NotNull(current);
            var reordered = HavenBoardReducer.Apply(current!, new MoveGroupCommand(0, 2));
            await store.SaveAsync(reordered);

            using var reopenedStore = new JsonFileHavenBoardStore(Path.GetDirectoryName(store.GetDocumentPath("board-main"))!);
            var reopened = await reopenedStore.LoadAsync("board-main");

            Assert.NotNull(reopened);
            Assert.Equal(new[] { "doing", "done", "todo" }, reopened!.Groups.Select(group => group.Id));
        });
    }

    [Fact]
    public async Task Copied_physical_document_opens_with_identical_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-boards-tests", Guid.NewGuid().ToString("N"));
        var copyRoot = Path.Combine(Path.GetTempPath(), "cakeos-boards-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(copyRoot);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var snapshot = HavenBoardSnapshot.CreateDefault() with { Title = "Copy me" };
            await store.SaveAsync(snapshot);

            File.Copy(store.GetDocumentPath("board-main"), Path.Combine(copyRoot, "board-main.9to1board"));

            using var copyStore = new JsonFileHavenBoardStore(copyRoot);
            var copied = await copyStore.LoadAsync("board-main");

            Assert.NotNull(copied);
            Assert.Equal("Copy me", copied!.Title);
            AssertSnapshotsEquivalent(snapshot with { Version = copied.Version }, copied);
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(copyRoot);
        }
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
