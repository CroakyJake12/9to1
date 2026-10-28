// BF-10 (ACCEPTANCE.md) — persistence failures, delayed completions and duplicate requests.
// Derived from the contract of the owning session, not from current behaviour:
//   IRichBoardSession.SaveAsync: "Sets Status to 'Saved HH:MM:SS' on success,
//   'Save failed: …' on error, and raises StatusChanged in both cases."
//   boards-typing rule 4: "Saving… is distinct from Saved locally. A production editor must
//   update that label from actual persistence state."
//   boards-recovery rules 1-3: a failed save preserves the live edits, the last durable
//   revision and the recovery draft; Retry re-attempts the same save.
// V3-BOARDS-PER-02 / V3-PERSISTENCE-PER-01: a stale completion must never mark newer edits
// as saved, and no revision regression or false "Saved locally" is allowed.
// All fixtures are generated in a temporary directory.

using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsSaveDurabilityRegression
{
    [Fact]
    public async Task A_delayed_save_that_completes_late_must_not_mark_the_newer_edit_saved()
    {
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var durableTitles = new List<string>();
        var released = false;

        async ValueTask<bool> SlowSave(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            durableTitles.Add(Title(HavenBoardDocumentCodec.Deserialize(bytes)));
            await gate.Task.WaitAsync(token).ConfigureAwait(false);
            return true;
        }

        var session = RichBoardSession.OpenExternallyWritable(HavenBoardDocumentCodec.CreateNew("Delayed"), SlowSave);
        var pageId = session.Rich.Sections[0].Pages[0].Id;
        try
        {
            await session.MutateAsync(rich => HavenRichNotesOps.RenamePage(rich, pageId, "first"));
            var inFlight = session.SaveAsync();
            await Task.Delay(60);
            Assert.False(inFlight.IsCompleted);
            Assert.Equal("Saving…", session.Status);
            Assert.True(session.HasUnsavedChanges);

            // A newer edit is admitted while the old completion is still pending.
            var queuedEdit = session.MutateAsync(rich => HavenRichNotesOps.RenamePage(rich, pageId, "second"));
            await Task.Delay(60);
            Assert.False(queuedEdit.IsCompleted);

            released = true;
            gate.SetResult(true);
            await inFlight;
            await queuedEdit;

            // The late completion covers only the revision it captured. The newer edit is
            // still not durable, so the real state must never read as saved.
            Assert.Equal(new[] { "first" }, durableTitles);
            Assert.True(session.HasUnsavedChanges);
            Assert.DoesNotContain("Saved", session.Status);

            await session.SaveAsync();
            Assert.Equal(new[] { "first", "second" }, durableTitles);
            Assert.False(session.HasUnsavedChanges);
            Assert.StartsWith("Saved", session.Status, StringComparison.Ordinal);
        }
        finally
        {
            if (!released) gate.TrySetResult(true);
            await session.DisposeAsync();
        }
    }

    [Fact]
    public async Task Duplicate_save_requests_serialize_without_regressing_the_durable_revision()
    {
        var attempts = 0;
        var written = new List<HavenBoardDocument>();
        ValueTask<bool> Save(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            attempts++;
            if (attempts > 4) throw new InvalidOperationException("Duplicate save requests fanned out.");
            written.Add(HavenBoardDocumentCodec.Deserialize(bytes));
            return ValueTask.FromResult(true);
        }

        var session = RichBoardSession.OpenExternallyWritable(HavenBoardDocumentCodec.CreateNew("Duplicate"), Save);
        var pageId = session.Rich.Sections[0].Pages[0].Id;
        try
        {
            await session.MutateAsync(rich => HavenRichNotesOps.RenamePage(rich, pageId, "only revision"));
            var first = session.SaveAsync();
            var second = session.SaveAsync();
            var third = session.SaveAsync();
            await Task.WhenAll(first, second, third);

            Assert.InRange(attempts, 1, 3);
            Assert.False(session.HasUnsavedChanges);
            // No revision regression and no duplicated entity: every written copy carries the
            // same page identity and the newest title, and no page or block ID repeats.
            foreach (var document in written)
            {
                var notes = document.RichNotes!;
                Assert.Equal("only revision", notes.Sections[0].Pages[0].Title);
                var ids = notes.Sections.SelectMany(section => section.Pages)
                    .SelectMany(page => page.Blocks.Select(block => page.Id + "/" + block.Id));
                Assert.Equal(ids.Count(), ids.Distinct().Count());
            }
            Assert.True(written.All(document => document.DocumentId == written[0].DocumentId));
        }
        finally { await session.DisposeAsync(); }
    }

    [Fact]
    public async Task A_failed_save_keeps_the_live_edits_and_reports_the_real_state_then_retry_succeeds()
    {
        var accept = false;
        var durable = new List<HavenBoardDocument>();
        var statusSeen = new List<string>();
        ValueTask<bool> Save(ReadOnlyMemory<byte> bytes, CancellationToken token)
        {
            if (!accept) return ValueTask.FromResult(false);
            durable.Add(HavenBoardDocumentCodec.Deserialize(bytes));
            return ValueTask.FromResult(true);
        }

        var session = RichBoardSession.OpenExternallyWritable(HavenBoardDocumentCodec.CreateNew("Recovery"), Save);
        var pageId = session.Rich.Sections[0].Pages[0].Id;
        session.StatusChanged += (_, status) => statusSeen.Add(status);
        try
        {
            await session.MutateAsync(rich => HavenRichNotesOps.RenamePage(rich, pageId, "kept work"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.SaveAsync());

            Assert.True(session.HasUnsavedChanges);
            Assert.Contains("Save failed", session.Status);
            Assert.Contains(statusSeen, status => status.Contains("Save failed", StringComparison.Ordinal));
            Assert.DoesNotContain(durable, document => Title(document) == "kept work");
            // The live document survives the failed attempt unchanged.
            Assert.Equal("kept work", session.Rich.Sections[0].Pages[0].Title);
            Assert.Contains("Saving…", statusSeen);

            // Retry keeps the same intended target and persists the live work.
            accept = true;
            await session.SaveAsync();
            Assert.False(session.HasUnsavedChanges);
            Assert.StartsWith("Saved", session.Status, StringComparison.Ordinal);
            Assert.Equal("kept work", Title(durable[^1]));
        }
        finally { await session.DisposeAsync(); }
    }

    [Fact]
    public async Task Save_As_writes_editable_bytes_at_the_chosen_path_and_reopens_with_the_same_ids()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-saveas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new JsonFileHavenBoardStore(root);
        try
        {
            await using var session = await RichBoardSession.CreateNewAsync(store, "Save As target");
            var pageId = session.Rich.Sections[0].Pages[0].Id;
            await session.MutateAsync(rich =>
            {
                HavenRichNotesOps.RenamePage(rich, pageId, "Chosen title");
                HavenRichNotesOps.AddBlock(rich, pageId, HavenRichBlockKind.Heading, "Headline");
                HavenRichNotesOps.AddInkStroke(rich, pageId, new HavenRichInkStroke
                {
                    Points = [new HavenRichInkPoint { X = 12, Y = 20 }, new HavenRichInkPoint { X = 40, Y = 44 }],
                    Width = 3,
                });
            });

            var destination = Path.Combine(root, "nested", "chosen copy.9to1board");
            await session.SaveAsAsync(destination);
            Assert.True(File.Exists(destination));
            var written = File.ReadAllText(destination);
            Assert.Contains("Chosen title", written);

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, destination);
            var restoredPage = reopened.Rich.Sections[0].Pages[0];
            Assert.Equal(pageId, restoredPage.Id);
            Assert.Equal("Chosen title", restoredPage.Title);
            Assert.Equal(2, restoredPage.Blocks.Count);
            Assert.Single(restoredPage.Ink);
            Assert.Equal(new double[] { 12, 20 },
                new[] { restoredPage.Ink[0].Points[0].X, restoredPage.Ink[0].Points[0].Y });
        }
        finally { store.Dispose(); Directory.Delete(root, recursive: true); }
    }

    private static string Title(HavenBoardDocument document) => document.RichNotes!.Sections[0].Pages[0].Title;
}
