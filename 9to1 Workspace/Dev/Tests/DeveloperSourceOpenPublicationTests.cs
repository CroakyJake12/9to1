using System.Reflection;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Actual Dev/coordinator/runtime fixture. Original issuer/file provider are synthetic;
// these are source-owner regression checks, not physical Home/native acceptance.
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [Fact]
    public async Task Eight_refused_preparations_do_not_consume_editors_and_ninth_valid_open_preserves_dirty_owner()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var first = (await review.OpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        var dirty = review.UpdateDraft(first.EditorId, first.DraftRevision, "preexisting unsaved source");
        for (var i = 0; i < 8; i++)
        {
            var document = f.Document with { FileId = Guid.NewGuid(), CanonicalResourceId = "refused-" + i, RelativePath = "src/refused-" + i + ".cs" };
            var pending = (await review.PrepareOpenAsync(f.Reference, f.Context(), document, OriginalTestBodyToken)).Value!;
            Assert.Throws<KeyNotFoundException>(() => review.GetSnapshot(pending.Snapshot.EditorId));
            review.AbandonPreparedOpen(pending);
            Assert.Same(dirty, review.GetSnapshot(dirty.EditorId));
            Assert.Single(OpenOwnerSnapshots(review)); Assert.Equal(0, PendingOpenCount(review));
        }
        var ninth = f.Document with { FileId = Guid.NewGuid(), CanonicalResourceId = "valid-ninth", RelativePath = "src/valid.cs" };
        var prepared = (await review.PrepareOpenAsync(f.Reference, f.Context(), ninth, OriginalTestBodyToken)).Value!;
        var accepted = review.AcceptPreparedOpen(prepared); review.AbandonPreparedOpen(prepared);
        Assert.True(accepted.Succeeded); Assert.Equal(ninth, accepted.Value!.Document);
        Assert.Equal(2, OpenOwnerSnapshots(review).Count); Assert.Same(dirty, review.GetSnapshot(dirty.EditorId));
        Assert.Equal(10, f.Tools.ReadCalls); Assert.Equal(0, f.Tools.WriteCalls);
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Full_owner_can_atomically_replace_exact_clean_preview_and_refocus_dirty_existing_document()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var owners = new List<DeveloperEditorSnapshot>();
        for (var i = 0; i < 8; i++)
        {
            var doc = f.Document with { FileId = Guid.NewGuid(), CanonicalResourceId = "pinned-" + i, RelativePath = "src/pinned-" + i + ".cs" };
            owners.Add((await review.OpenAsync(f.Reference, f.Context(), doc, OriginalTestBodyToken)).Value!);
        }
        var dirty = review.UpdateDraft(owners[0].EditorId, owners[0].DraftRevision, "dirty pinned draft");
        var next = f.Document with { FileId = Guid.NewGuid(), CanonicalResourceId = "replacement", RelativePath = "src/replacement.cs" };
        var pending = (await review.PrepareOpenAsync(f.Reference, f.Context(), next, OriginalTestBodyToken)).Value!;
        Assert.Equal(8, OpenOwnerSnapshots(review).Count); Assert.Single(OpenOwnerSnapshots(review), item => item.EditorId == owners[7].EditorId);
        var result = review.AcceptPreparedOpen(pending, owners[7]);
        Assert.True(result.Succeeded); Assert.Equal(8, OpenOwnerSnapshots(review).Count);
        Assert.Throws<KeyNotFoundException>(() => review.GetSnapshot(owners[7].EditorId));
        for (var i = 1; i < 7; i++) Assert.Same(owners[i], review.GetSnapshot(owners[i].EditorId));
        Assert.Same(dirty, review.GetSnapshot(dirty.EditorId));
        var focus = (await review.PrepareOpenAsync(f.Reference, f.Context(), dirty.Document, OriginalTestBodyToken)).Value!;
        var reopened = review.AcceptPreparedOpen(focus);
        Assert.Equal(dirty.EditorId, reopened.Value!.EditorId); Assert.Equal(dirty.DraftText, reopened.Value.DraftText);
        Assert.Equal(8, OpenOwnerSnapshots(review).Count); Assert.Equal(0, PendingOpenCount(review));
        Assert.Throws<InvalidOperationException>(() => review.AcceptPreparedOpen(pending));
        Assert.Equal(0, f.Tools.WriteCalls); await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Changed_or_foreign_replacement_cannot_discard_existing_draft_or_consume_prepared_editor()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var before = (await review.OpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Value!;
        var next = f.Document with { FileId = Guid.NewGuid(), CanonicalResourceId = "next", RelativePath = "src/next.cs" };
        var pending = (await review.PrepareOpenAsync(f.Reference, f.Context(), next, OriginalTestBodyToken)).Value!;
        var dirty = review.UpdateDraft(before.EditorId, before.DraftRevision, "edited during pending read");
        Assert.Throws<InvalidOperationException>(() => review.AcceptPreparedOpen(pending, before));
        Assert.Same(dirty, review.GetSnapshot(dirty.EditorId)); Assert.Single(OpenOwnerSnapshots(review));
        review.AbandonPreparedOpen(pending); Assert.Equal(0, PendingOpenCount(review));
        await using var other = new DeveloperSourceReviewSession(f.Dev);
        var foreign = (await other.PrepareOpenAsync(f.Reference, f.Context(), next, OriginalTestBodyToken)).Value!;
        Assert.Throws<InvalidOperationException>(() => review.AcceptPreparedOpen(foreign));
        Assert.Single(OpenOwnerSnapshots(review)); other.AbandonPreparedOpen(foreign);
        Assert.Equal(0, f.Tools.WriteCalls); await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Original_held_read_is_joined_and_retired_preparation_never_enrolls_an_editor()
    {
        var f = await Fixture.CreateAsync(); var review = new DeveloperSourceReviewSession(f.Dev);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Tools.BeforeFileRead = release.Task; f.Tools.ReadEntered = () => entered.TrySetResult();
        var original = review.PrepareOpenAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken);
        Task? close = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), OriginalTestBodyToken);
            Assert.False(original.IsCompleted); Assert.Empty(OpenOwnerSnapshots(review));
            close = review.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
        }
        finally { release.TrySetResult(); }
        var prepared = (await original).Value!; await close!;
        Assert.Throws<ObjectDisposedException>(() => review.AcceptPreparedOpen(prepared));
        review.AbandonPreparedOpen(prepared);
        Assert.Empty(OpenOwnerSnapshots(review)); Assert.Equal(0, PendingOpenCount(review));
        Assert.Equal(0, f.Tools.WriteCalls); await f.Dev.CloseAndDrainAsync();
    }

    private static IReadOnlyList<DeveloperEditorSnapshot> OpenOwnerSnapshots(DeveloperSourceReviewSession review)
    {
        var gate = typeof(DeveloperSourceReviewSession).GetField("_gate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(review)!;
        lock (gate)
        {
            var rows = (System.Collections.IDictionary)typeof(DeveloperSourceReviewSession).GetField("_editors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(review)!;
            return rows.Values.Cast<object>().Select(row => (DeveloperEditorSnapshot)row.GetType().GetField("Snapshot")!.GetValue(row)!).ToArray();
        }
    }
    private static int PendingOpenCount(DeveloperSourceReviewSession review)
    {
        var table = (System.Collections.IDictionary)typeof(DeveloperSourceReviewSession).GetField("_preparedOpens", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(review)!;
        return table.Count;
    }
}
