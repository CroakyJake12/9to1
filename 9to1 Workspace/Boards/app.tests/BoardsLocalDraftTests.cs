using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsLocalDraftTests
{
    [Fact]
    public async Task New_draft_is_durable_without_a_destination_and_reopens_in_the_same_profile()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            Guid id; string pageId, blockId;
            using (var documents = new BoardsLocalDocuments("local-profile", root))
            {
                id = await documents.CreateAsync();
                await using var session = await documents.OpenAsync(id);
                var page = session.Document.Sections[0].Pages[0];
                pageId = page.Id;
                var block = new RichBoardBlock { Text = "Draft before choosing any file" };
                blockId = block.Id; page.Blocks.Add(block); session.MarkDirty();
                await session.SaveAsync();
                Assert.True(session.IsLocalDraft);
                Assert.Equal("Saved locally — draft", session.Status);
                Assert.Equal(id, (await documents.Store.LoadDocumentAtPathAsync(documents.PathFor(id)))!.DocumentId);
            }
            using (var documents = new BoardsLocalDocuments("local-profile", root))
            {
                Assert.Equal(id, await documents.LatestOrCreateAsync());
                await using var session = await documents.OpenAsync(id);
                Assert.Equal(pageId, session.Document.Sections[0].Pages[0].Id);
                Assert.Contains(session.Document.Sections[0].Pages[0].Blocks,
                    block => block.Id == blockId && block.Text == "Draft before choosing any file");
            }
            using var other = new BoardsLocalDocuments("another-profile", root);
            await Assert.ThrowsAsync<FileNotFoundException>(() => other.OpenAsync(id));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Optional_save_as_outside_the_private_folder_retains_identity_and_recovery()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "chosen-anywhere.9to1board");
            Guid id;
            using (var documents = new BoardsLocalDocuments("profile", root))
            {
                id = await documents.CreateAsync();
                await using var session = await documents.OpenAsync(id);
                var vm = new BoardsViewModel(session);
                var originalPath = session.FilePath;
                vm.SaveAsBoardAsync = _ => Task.CompletedTask; // chooser cancellation
                await vm.DispatchAsync("SaveAsBoard", null);
                Assert.Equal(originalPath, session.FilePath);
                Assert.True(session.IsLocalDraft);
                await session.SaveAsAsync(destination);
                Assert.Equal(destination, session.FilePath);
                Assert.False(session.IsLocalDraft);
                session.Document.Title = "Changed after Save As";
                session.MarkDirty(); await session.SaveAsync();
                Assert.Equal(id, (await documents.Store.LoadDocumentAtPathAsync(destination))!.DocumentId);
                Assert.Equal(id, (await documents.Store.LoadDocumentAtPathAsync(documents.PathFor(id)))!.DocumentId);
            }
            using var reopenedDocuments = new BoardsLocalDocuments("profile", root);
            await using var reopened = await reopenedDocuments.OpenAsync(id);
            Assert.Equal(destination, reopened.FilePath);
            Assert.Equal("Changed after Save As", reopened.Document.Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Failed_destination_save_keeps_the_newest_private_revision_and_detects_external_changes()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            using var documents = new BoardsLocalDocuments("profile", root);
            var id = await documents.CreateAsync();
            await using var session = await documents.OpenAsync(id);
            var destination = Path.Combine(root, "external.9to1board");
            await session.SaveAsAsync(destination);
            var external = (await documents.Store.LoadDocumentAtPathAsync(destination))!;
            var canonicalTitle = external.RichNotes!.Title;
            external.RichNotes!.Title = "Outside edit";
            await documents.Store.SaveDocumentAtPathAsync(external, destination);
            session.Document.Title = "Recovered local edit";
            session.MarkDirty();
            await Assert.ThrowsAsync<IOException>(() => session.SaveAsync().AsTask());
            var recovery = (await documents.Store.LoadDocumentAtPathAsync(documents.PathFor(id)))!;
            Assert.Equal(canonicalTitle, recovery.RichNotes!.Title);
            Assert.NotNull(session.ConflictRecoveryPath);
            Assert.Equal("Recovered local edit", (await documents.Store.LoadDocumentAtPathAsync(session.ConflictRecoveryPath!))!.RichNotes!.Title);
            Assert.Equal("Outside edit", (await documents.Store.LoadDocumentAtPathAsync(destination))!.RichNotes!.Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Stale_tab_cannot_replace_either_head_and_keeps_a_distinct_conflict_copy()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            var destination = Path.Combine(root, "external.9to1board");
            Guid id;
            using (var seed = new BoardsLocalDocuments("profile", root))
            {
                id = await seed.CreateAsync();
                await using var initial = await seed.OpenAsync(id);
                await initial.SaveAsAsync(destination);
            }
            using (var firstOwner = new BoardsLocalDocuments("profile", root))
            using (var staleOwner = new BoardsLocalDocuments("profile", root))
            {
                await using var first = await firstOwner.OpenAsync(id);
                await using var stale = await staleOwner.OpenAsync(id);
                first.Document.Title = "Accepted A";
                first.MarkDirty(); await first.SaveAsync();
                stale.Document.Title = "Unsaved stale B";
                stale.MarkDirty();
                await Assert.ThrowsAsync<IOException>(() => stale.SaveAsync().AsTask());
                Assert.Equal("Accepted A", (await firstOwner.Store.LoadDocumentAtPathAsync(firstOwner.PathFor(id)))!.RichNotes!.Title);
                Assert.Equal("Accepted A", (await firstOwner.Store.LoadDocumentAtPathAsync(destination))!.RichNotes!.Title);
                var conflict = stale.ConflictRecoveryPath;
                Assert.NotNull(conflict);
                Assert.NotEqual(firstOwner.PathFor(id), conflict);
                Assert.Equal("Unsaved stale B", (await firstOwner.Store.LoadDocumentAtPathAsync(conflict!))!.RichNotes!.Title);
                await Assert.ThrowsAsync<IOException>(() => stale.SaveAsAsync(Path.Combine(root, "stale-as.9to1board")).AsTask());
                Assert.Equal(conflict, stale.ConflictRecoveryPath);
            }
            using var reopenedOwner = new BoardsLocalDocuments("profile", root);
            await using var reopened = await reopenedOwner.OpenAsync(id);
            Assert.Equal("Accepted A", reopened.Document.Title);
            reopened.Document.Title = "Accepted after reopen";
            reopened.MarkDirty(); await reopened.SaveAsync();
            Assert.Equal("Accepted after reopen", (await reopenedOwner.Store.LoadDocumentAtPathAsync(destination))!.RichNotes!.Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Simultaneous_tabs_commit_one_head_and_preserve_the_loser_separately()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            using var firstOwner = new BoardsLocalDocuments("profile", root);
            using var secondOwner = new BoardsLocalDocuments("profile", root);
            var id = await firstOwner.CreateAsync();
            await using var first = await firstOwner.OpenAsync(id);
            await using var second = await secondOwner.OpenAsync(id);
            first.Document.Title = "A"; first.MarkDirty();
            second.Document.Title = "B"; second.MarkDirty();
            static async Task<bool> Save(ContractSessionAdapter session)
            {
                try { await session.SaveAsync(); return true; }
                catch (IOException) { return false; }
            }
            var results = await Task.WhenAll(Save(first), Save(second));
            Assert.Single(results, result => result);
            var winner = results[0] ? "A" : "B";
            var loser = results[0] ? second : first;
            Assert.Equal(winner, (await firstOwner.Store.LoadDocumentAtPathAsync(firstOwner.PathFor(id)))!.RichNotes!.Title);
            Assert.NotNull(loser.ConflictRecoveryPath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Postcommit_cancellation_keeps_the_acknowledged_destination_retryable()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            using var owner = new BoardsLocalDocuments("profile", root);
            var id = await owner.CreateAsync();
            await using var session = await RichBoardSession.OpenRecoverableAsync(owner.Store, owner.PathFor(id), null, null);
            using var cancellation = new CancellationTokenSource();
            session.LocalFileSaved += (_, _) => cancellation.Cancel();
            var destination = Path.Combine(root, "committed.9to1board");
            await session.SaveAsAsync(destination, cancellation.Token);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(destination, session.FilePath);
            await session.MutateAsync(rich => rich.Title = "Retry succeeds");
            await session.SaveAsync();
            Assert.Equal("Retry succeeds", (await owner.Store.LoadDocumentAtPathAsync(destination))!.RichNotes!.Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Same_profile_in_isolated_app_roots_never_shares_drafts()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            using var first = new BoardsLocalDocuments("same-profile", Path.Combine(root, "first-app-root", "Boards"));
            using var second = new BoardsLocalDocuments("same-profile", Path.Combine(root, "second-app-root", "Boards"));
            var id = await first.CreateAsync();
            var otherId = await second.LatestOrCreateAsync();
            Assert.NotEqual(id, otherId);
            Assert.NotEqual(first.PathFor(id), second.PathFor(id));
            Assert.False(File.Exists(second.PathFor(id)));
            await Assert.ThrowsAsync<FileNotFoundException>(() => second.OpenAsync(id));
            Assert.Equal(id, await first.LatestOrCreateAsync());
            Assert.Equal(otherId, await second.LatestOrCreateAsync());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Catalogue_created_before_save_as_refreshes_the_active_destination_when_opening()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            using var firstOwner = new BoardsLocalDocuments("profile", root);
            using var earlyCatalogue = new BoardsLocalDocuments("profile", root);
            var id = await firstOwner.CreateAsync();
            var destination = Path.Combine(root, "selected.9to1board");
            await using (var first = await firstOwner.OpenAsync(id))
                await first.SaveAsAsync(destination);
            await using (var later = await earlyCatalogue.OpenAsync(id).WaitAsync(TimeSpan.FromSeconds(5)))
            {
                Assert.Equal(destination, later.FilePath);
                later.Document.Title = "Saved through refreshed destination";
                later.MarkDirty(); await later.SaveAsync();
            }
            using var freshOwner = new BoardsLocalDocuments("profile", root);
            await using var fresh = await freshOwner.OpenAsync(id);
            Assert.Equal(destination, fresh.FilePath);
            Assert.Equal("Saved through refreshed destination", (await freshOwner.Store.LoadDocumentAtPathAsync(destination))!.RichNotes!.Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Backup_only_draft_restores_a_primary_under_the_opening_lease()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            using var owner = new BoardsLocalDocuments("profile", root);
            var id = await owner.CreateAsync();
            await using (var session = await owner.OpenAsync(id))
            {
                session.Document.Title = "Recover the complete backup";
                session.MarkDirty(); await session.SaveAsync();
            }
            var path = owner.PathFor(id);
            File.Copy(path, path + ".bak", true);
            File.Delete(path);
            Assert.Equal(id, await owner.LatestOrCreateAsync());
            await using (var recovered = await owner.OpenAsync(id).WaitAsync(TimeSpan.FromSeconds(5)))
            {
                Assert.Equal("Recover the complete backup", recovered.Document.Title);
                Assert.True(File.Exists(path));
                Assert.Equal(id, (await owner.Store.LoadDocumentAtPathAsync(path))!.DocumentId);
            }
            File.Copy(path, path + ".bak", true);
            File.Delete(path);
            await using var direct = await RichBoardSession.OpenRecoverableAsync(owner.Store, path, null, null)
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("Recover the complete backup", direct.Rich.Title);
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Concurrent_create_of_one_identity_commits_once_without_overwriting_the_winner()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            using var first = new BoardsLocalDocuments("profile", root);
            using var second = new BoardsLocalDocuments("profile", root);
            var original = HavenBoardDocumentCodec.CreateNew();
            var firstDoc = HavenBoardDocumentCodec.Deserialize(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
            var secondDoc = HavenBoardDocumentCodec.Deserialize(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(original,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
            firstDoc.RichNotes!.Title = "First"; secondDoc.RichNotes!.Title = "Second";
            static async Task<bool> Create(BoardsLocalDocuments owner, HavenBoardDocument document)
            {
                try
                {
                    await owner.CreateAsync(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(document,
                        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
                    return true;
                }
                catch (IOException) { return false; }
            }
            var results = await Task.WhenAll(Create(first, firstDoc), Create(second, secondDoc));
            Assert.Single(results, result => result);
            Assert.Equal(results[0] ? "First" : "Second",
                (await first.Store.LoadDocumentAtPathAsync(first.PathFor(original.DocumentId)))!.RichNotes!.Title);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Concurrent_import_does_not_replace_existing_private_identity_and_external_changes_refuse_save()
    {
        var root = TempRoot(); Directory.CreateDirectory(root);
        try
        {
            using var first = new BoardsLocalDocuments("profile", root);
            using var second = new BoardsLocalDocuments("profile", root);
            var document = HavenBoardDocumentCodec.CreateNew();
            document.RichNotes!.Title = "Imported bytes";
            var path = Path.Combine(root, "source.9to1board");
            await first.Store.SaveDocumentAtPathAsync(document, path);
            var ids = await Task.WhenAll(first.ImportAsync(path), second.ImportAsync(path));
            Assert.All(ids, id => Assert.Equal(document.DocumentId, id));
            await using var opened = await first.OpenAsync(document.DocumentId);
            document.RichNotes.Title = "External changed after import";
            await first.Store.SaveDocumentAtPathAsync(document, path);
            opened.Document.Title = "Local edit"; opened.MarkDirty();
            await Assert.ThrowsAsync<IOException>(() => opened.SaveAsync().AsTask());
            Assert.Equal("Imported bytes", (await first.Store.LoadDocumentAtPathAsync(first.PathFor(document.DocumentId)))!.RichNotes!.Title);
            Assert.Equal("External changed after import", (await first.Store.LoadDocumentAtPathAsync(path))!.RichNotes!.Title);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TempRoot() => Path.Combine(Path.GetTempPath(), "boards-local-drafts-" + Guid.NewGuid().ToString("N"));
}
