using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Reuses the SAME real Dev/coordinator/runtime fixture from owning action controls. The issuer
// and file provider remain synthetic; this is not physical Home/installed acceptance.
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [Fact]
    public async Task Source_editor_uses_full_original_read_and_retains_exact_pass_before_after()
    {
        var f = await Fixture.CreateAsync(); f.Tools.TextValue = new string('a', 150_000) + "\nlast source";
        await using var review = new DeveloperSourceReviewSession(f.Dev);
        var originalRead = f.Context(); var opened = await review.OpenAsync(f.Reference, originalRead, f.Document);
        Assert.True(opened.Succeeded); var editor = Assert.IsType<DeveloperEditorSnapshot>(opened.Value);
        Assert.Equal(f.Tools.TextValue, editor.OriginalText); Assert.Equal(editor.OriginalText, editor.DraftText); Assert.False(editor.IsDirty);
        var draft = review.UpdateDraft(editor.EditorId, editor.DraftRevision, "edited whole source");
        var preview = await review.PreviewAsync(editor.EditorId, draft.DraftRevision, f.Context());
        Assert.True(preview.Succeeded); var pass = Assert.IsType<DeveloperSourceChangePass>(preview.Value);
        Assert.Equal(editor.OriginalText, pass.BeforeText); Assert.Equal("edited whole source", pass.AfterText);
        Assert.Equal(originalRead.ActionId, pass.OriginalReadActionId); Assert.Equal(DeveloperSourceChangeState.Reviewed, pass.State);
        Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(editor.OriginalText, f.Tools.TextValue);
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Source_review_apply_and_exact_pass_revert_use_same_task_hash_checked_executor()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var opened = (await review.OpenAsync(f.Reference, f.Context(), f.Document)).Value!;
        var draft = review.UpdateDraft(opened.EditorId, 1, "pass one source");
        var prepared = (await review.PreviewAsync(opened.EditorId, draft.DraftRevision, f.Context())).Value!;
        var applied = (await review.ApplyAsync(opened.EditorId, prepared.ChangeSetId, draft.DraftRevision, f.Context())).Value!;
        Assert.Equal(DeveloperSourceChangeState.Applied, applied.State); Assert.Equal("pass one source", f.Tools.TextValue); Assert.Equal(1, f.Tools.WriteCalls);
        Assert.Equal(f.Current.TaskId, applied.TaskId); Assert.Equal(f.Current.ExecutionId, applied.ExecutionId);
        var inverseDraft = review.PrepareRevert(opened.EditorId, applied.ChangeSetId, draft.DraftRevision);
        var inverse = (await review.PreviewAsync(opened.EditorId, inverseDraft.DraftRevision, f.Context())).Value!;
        Assert.Equal(applied.AfterText, inverse.BeforeText); Assert.Equal(applied.BeforeText, inverse.AfterText); Assert.Equal(applied.ChangeSetId, inverse.RevertsChangeSetId);
        var reverted = (await review.ApplyAsync(opened.EditorId, inverse.ChangeSetId, inverseDraft.DraftRevision, f.Context())).Value!;
        Assert.Equal(DeveloperSourceChangeState.Applied, reverted.State); Assert.Equal("original source", f.Tools.TextValue); Assert.Equal(2, f.Tools.WriteCalls);
        Assert.Equal(2, review.GetPasses().Count); await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Later_external_source_edit_refuses_revert_and_preserves_the_other_change()
    {
        var f = await Fixture.CreateAsync(); var review = new DeveloperSourceReviewSession(f.Dev);
        var opened = (await review.OpenAsync(f.Reference, f.Context(), f.Document)).Value!;
        var draft = review.UpdateDraft(opened.EditorId, 1, "our pass");
        var prepared = (await review.PreviewAsync(opened.EditorId, draft.DraftRevision, f.Context())).Value!;
        var applied = (await review.ApplyAsync(opened.EditorId, prepared.ChangeSetId, draft.DraftRevision, f.Context())).Value!;
        f.Tools.TextValue = "later external working tree";
        var inverse = review.PrepareRevert(opened.EditorId, applied.ChangeSetId, draft.DraftRevision);
        await Assert.ThrowsAnyAsync<Exception>(() => review.PreviewAsync(opened.EditorId, inverse.DraftRevision, f.Context()));
        Assert.Equal("later external working tree", f.Tools.TextValue); Assert.Equal(1, f.Tools.WriteCalls);
        await Assert.ThrowsAnyAsync<Exception>(() => review.CloseAndDrainAsync()); await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Held_apply_preserves_newer_typed_draft_and_view_close_joins_original_without_cancel()
    {
        var f = await Fixture.CreateAsync(); var review = new DeveloperSourceReviewSession(f.Dev);
        var opened = (await review.OpenAsync(f.Reference, f.Context(), f.Document)).Value!;
        var draft = review.UpdateDraft(opened.EditorId, 1, "reviewed source");
        var prepared = (await review.PreviewAsync(opened.EditorId, draft.DraftRevision, f.Context())).Value!;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); f.Tools.BeforeWrite = release.Task;
        var actual = review.ApplyAsync(opened.EditorId, prepared.ChangeSetId, draft.DraftRevision, f.Context()); Task? close = null;
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (f.Owner.ExecuteCalls < 3) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5)); await Task.Yield(); }
            var newer = review.UpdateDraft(opened.EditorId, draft.DraftRevision, "newer draft not yet reviewed");
            Assert.Equal(draft.DraftRevision + 1, newer.DraftRevision);
            close = review.CloseAndDrainAsync(); Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted);
        }
        finally { release.TrySetResult(); try { await actual; } finally { if (close is not null) await close; } }
        Assert.Equal("reviewed source", f.Tools.TextValue); Assert.Equal(1, f.Tools.WriteCalls);
        // Inspect actual retained owner metadata after terminal close, never a withdrawn public cache.
        var ownerGate = typeof(DeveloperSourceReviewSession).GetField("_gate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(review)!;
        lock (ownerGate)
        {
            var entries = (System.Collections.IDictionary)typeof(DeveloperSourceReviewSession).GetField("_editors", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(review)!;
            var entry = entries[opened.EditorId]!;
            var retained = (DeveloperEditorSnapshot)entry.GetType().GetField("Snapshot")!.GetValue(entry)!;
            Assert.Equal("reviewed source", retained.OriginalText);
            Assert.Equal("newer draft not yet reviewed", retained.DraftText);
        }
        Assert.Throws<ObjectDisposedException>(() => review.GetSnapshot(opened.EditorId));
        // Withheld presentation caches do not withdraw the canonical business owner.
        Assert.NotNull(await f.Tasks.GetIssuedAttemptAsync(f.Current.TaskId, f.Current.ExecutionId, f.Attempt.AttemptId, default));
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Source_session_cannot_disclose_cached_editor_to_retired_actor_by_copied_context()
    {
        var f = await Fixture.CreateAsync(); var review = new DeveloperSourceReviewSession(f.Dev);
        var opened = (await review.OpenAsync(f.Reference, f.Context(), f.Document)).Value!;
        await f.Attempt.Lease.DisposeAsync();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => review.OpenAsync(f.Reference with { }, f.Context() with { }, f.Document with { }));
        Assert.True(ContainsType<UnauthorizedAccessException>(error)); Assert.Equal(1, f.Tools.ReadCalls);
        await Assert.ThrowsAnyAsync<Exception>(() => review.CloseAndDrainAsync()); await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Draft_revision_and_reviewed_pass_cannot_be_reused_for_a_different_edit()
    {
        var f = await Fixture.CreateAsync(); await using var review = new DeveloperSourceReviewSession(f.Dev);
        var opened = (await review.OpenAsync(f.Reference, f.Context(), f.Document)).Value!;
        var draft = review.UpdateDraft(opened.EditorId, 1, "first draft");
        var prepared = (await review.PreviewAsync(opened.EditorId, draft.DraftRevision, f.Context())).Value!;
        var newer = review.UpdateDraft(opened.EditorId, draft.DraftRevision, "second draft");
        Assert.Throws<InvalidOperationException>(() => { review.ApplyAsync(opened.EditorId, prepared.ChangeSetId, newer.DraftRevision, f.Context()); });
        Assert.Equal("original source", f.Tools.TextValue); Assert.Equal(0, f.Tools.WriteCalls);
        Assert.Equal("second draft", review.GetSnapshot(opened.EditorId).DraftText); await f.Dev.CloseAndDrainAsync();
    }
}
