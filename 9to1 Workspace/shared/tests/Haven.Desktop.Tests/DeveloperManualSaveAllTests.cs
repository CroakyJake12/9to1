using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

// Focused orchestration/reconciliation fixtures for the SAME batch driver used
// by the real owner. Local text delegates prove no native readiness, Home grant,
// Files identity, physical write, installer or full workbench acceptance.
public sealed class DeveloperManualSaveAllTests
{
    [Fact]
    public async Task Each_reviewed_file_is_verified_while_unreviewed_and_conflicting_drafts_are_preserved()
    {
        var a = Request("a.cui", "before a", "after a");
        var unreviewed = Request("b.cui", "before b", "after b") with { Reviewed = false };
        var conflict = Request("c.cui", "before c", "after c");
        var d = Request("d.cui", "before d", "after d");
        var files = new Dictionary<string, string> { [a.RelativePath] = a.BaselineText, [unreviewed.RelativePath] = unreviewed.BaselineText,
            [conflict.RelativePath] = "outside edit", [d.RelativePath] = d.BaselineText };
        var writes = new List<string>();
        var outcomes = await Run([a, unreviewed, conflict, d],
            (request, _) => { writes.Add(request.RelativePath); files[request.RelativePath] = request.DraftText; return Task.FromResult(Hash(request.DraftText)); },
            (request, _) => Task.FromResult((files[request.RelativePath], Hash(files[request.RelativePath]))));
        Assert.Equal(new[] { DeveloperManualSaveState.Saved, DeveloperManualSaveState.NeedsReview, DeveloperManualSaveState.Conflict, DeveloperManualSaveState.Saved }, outcomes.Select(value => value.State));
        Assert.Equal(new[] { a.RelativePath, d.RelativePath }, writes);
        Assert.Equal(unreviewed.BaselineText, files[unreviewed.RelativePath]); Assert.Equal("outside edit", files[conflict.RelativePath]);
        Assert.Equal(a.AfterSha256, outcomes[0].SavedSha256); Assert.Equal(d.AfterSha256, outcomes[3].SavedSha256);
        Assert.All(outcomes, value => Assert.All(value.OriginalSources, source => Assert.True(source.IsCompleted)));
    }

    [Fact]
    public async Task Per_file_failure_retains_same_original_cause_and_does_not_erase_other_successes()
    {
        var a = Request("a.cui", "before a", "after a"); var b = Request("b.cui", "before b", "after b");
        var files = new Dictionary<string, string> { [a.RelativePath] = a.BaselineText, [b.RelativePath] = b.BaselineText };
        var failure = new IOException("Original individual write refused"); var writes = new List<string>();
        var outcomes = await Run([a, b], (request, _) =>
        {
            writes.Add(request.RelativePath);
            if (request == a) return Task.FromException<string>(failure);
            files[request.RelativePath] = request.DraftText; return Task.FromResult(request.AfterSha256);
        }, (request, _) => Task.FromResult((files[request.RelativePath], Hash(files[request.RelativePath]))));
        Assert.Equal(DeveloperManualSaveState.Failed, outcomes[0].State); Assert.True(outcomes[0].WriteStarted);
        Assert.Null(outcomes[0].SavedSha256); Assert.Contains(outcomes[0].OriginalFailures, value => ReferenceEquals(value, failure));
        Assert.Equal(DeveloperManualSaveState.Saved, outcomes[1].State); Assert.Equal(2, writes.Count);
        Assert.Equal(a.BaselineText, files[a.RelativePath]); Assert.Equal(b.DraftText, files[b.RelativePath]);
        Assert.Null(DeveloperManualSourceEditOwner.ReconcileSavedDraft(outcomes[0], a.BaselineText, a.BeforeSha256, a.DraftText, a.DraftRevision));
        Assert.All(outcomes[0].OriginalSources, source => Assert.True(source.IsCompleted));
    }

    [Fact]
    public async Task Applied_but_different_reopened_content_is_never_reported_saved_or_automatically_replayed()
    {
        var request = Request("a.cui", "before", "after"); var reads = 0; var writes = 0;
        var outcome = Assert.Single(await Run([request], (_, _) => { writes++; return Task.FromResult(request.AfterSha256); },
            (_, _) => { reads++; var text = reads == 1 ? request.BaselineText : "changed after write"; return Task.FromResult((text, Hash(text))); }));
        Assert.Equal(DeveloperManualSaveState.Failed, outcome.State); Assert.True(outcome.WriteStarted);
        Assert.Null(outcome.SavedSha256); Assert.Equal(1, writes); Assert.Equal(2, reads);
        Assert.Contains("inspection", outcome.Detail); Assert.NotEmpty(outcome.OriginalFailures);
        Assert.Null(DeveloperManualSourceEditOwner.ReconcileSavedDraft(outcome, request.BaselineText, request.BeforeSha256, request.DraftText, request.DraftRevision));
    }

    [Fact]
    public async Task Acknowledged_wrong_hash_does_not_reopen_or_advance_the_saved_baseline()
    {
        var request = Request("a.cui", "before", "after"); var reads = 0;
        var outcome = Assert.Single(await Run([request], (_, _) => Task.FromResult(Hash("different acknowledgement")),
            (_, _) => { reads++; return Task.FromResult((request.BaselineText, request.BeforeSha256)); }));
        Assert.Equal(DeveloperManualSaveState.Failed, outcome.State); Assert.Null(outcome.SavedSha256); Assert.Equal(1, reads);
        Assert.All(outcome.OriginalSources, source => Assert.True(source.IsCompleted));
    }

    [Fact]
    public async Task Original_source_issued_before_postcallback_refusal_is_joined_before_batch_terminal_and_no_write_follows()
    {
        var request = Request("a.cui", "before", "after"); var writes = 0; var refused = false;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<(string Text, string Sha256)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new IOException("Original source callback refused after acquisition");
        void Scope(Action body) { body(); if (entered.Task.IsCompleted && !refused) { refused = true; throw failure; } }
        var batch = Run([request], (_, _) => { writes++; return Task.FromResult(request.AfterSha256); },
            (_, _) => { entered.TrySetResult(); return raw.Task; }, Scope);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(batch.IsCompleted); Assert.Equal(0, writes);
        raw.SetResult((request.BaselineText, request.BeforeSha256));
        var outcome = Assert.Single(await batch.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(DeveloperManualSaveState.Failed, outcome.State); Assert.False(outcome.WriteStarted);
        Assert.Contains(raw.Task, outcome.OriginalSources); Assert.True(raw.Task.IsCompletedSuccessfully); Assert.Equal(0, writes);
        Assert.Contains(outcome.OriginalFailures, value => ReferenceEquals(value, failure));
    }

    [Fact]
    public async Task Canceled_business_token_never_starts_new_file_sources_or_manufactures_saved_results()
    {
        var request = Request("a.cui", "before", "after"); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var calls = 0;
        var outcome = Assert.Single(await DeveloperManualSourceEditOwner.ExecuteReviewedBatchAsync([request],
            (_, _) => { calls++; return Task.FromResult(request.AfterSha256); },
            (_, _) => { calls++; return Task.FromResult((request.BaselineText, request.BeforeSha256)); }, action => action(), cancellation.Token));
        Assert.Equal(DeveloperManualSaveState.Cancelled, outcome.State); Assert.False(outcome.WriteStarted);
        Assert.Null(outcome.SavedSha256); Assert.Empty(outcome.OriginalSources); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Batch_capacity_or_duplicate_identity_refuses_before_original_source_calls()
    {
        var request = Request("a.cui", "before", "after"); var calls = 0;
        Task<IReadOnlyList<DeveloperManualSaveOutcome>> Execute(IReadOnlyList<DeveloperManualSaveRequest> requests) => Run(requests,
            (_, _) => { calls++; return Task.FromResult(request.AfterSha256); },
            (_, _) => { calls++; return Task.FromResult((request.BaselineText, request.BeforeSha256)); });
        await Assert.ThrowsAsync<ArgumentException>(() => Execute([request, request]));
        await Assert.ThrowsAsync<ArgumentException>(() => Execute(Enumerable.Range(0, 9).Select(value => Request(value + ".cui", "before", "after")).ToArray()));
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Saved_snapshot_advances_only_actual_baseline_and_preserves_newer_typing_and_revision()
    {
        var request = Request("a.cui", "before", "reviewed after"); var current = request.BaselineText;
        var outcome = Assert.Single(await Run([request], (_, _) => { current = request.DraftText; return Task.FromResult(request.AfterSha256); },
            (_, _) => Task.FromResult((current, Hash(current)))));
        var reconciled = DeveloperManualSourceEditOwner.ReconcileSavedDraft(outcome, request.BaselineText, request.BeforeSha256,
            "newer unreviewed typing", request.DraftRevision + 2);
        Assert.NotNull(reconciled); Assert.Equal(request.DraftText, reconciled.Baseline);
        Assert.Equal(request.AfterSha256, reconciled.BeforeSha256); Assert.Equal("newer unreviewed typing", reconciled.Draft);
        Assert.Equal(request.DraftRevision + 3, reconciled.DraftRevision);
        Assert.Null(DeveloperManualSourceEditOwner.ReconcileSavedDraft(outcome, "a different newer baseline", Hash("a different newer baseline"), "newer draft", request.DraftRevision + 3));
        Assert.Null(DeveloperManualSourceEditOwner.ReconcileSavedDraft(outcome with { SavedSha256 = Hash("forged") }, request.BaselineText, request.BeforeSha256, request.DraftText, request.DraftRevision));
    }

    private static DeveloperManualSaveRequest Request(string path, string baseline, string draft) =>
        new(Guid.NewGuid(), path, baseline, Hash(baseline), draft, Hash(draft), 3, 3, true);
    private static string Hash(string text) => DeveloperManualSourceEditOwner.Sha256(text);
    private static Task<IReadOnlyList<DeveloperManualSaveOutcome>> Run(IReadOnlyList<DeveloperManualSaveRequest> requests,
        Func<DeveloperManualSaveRequest, CancellationToken, Task<string>> apply,
        Func<DeveloperManualSaveRequest, CancellationToken, Task<(string Text, string Sha256)>> read, Action<Action>? scope = null) =>
        DeveloperManualSourceEditOwner.ExecuteReviewedBatchAsync(requests, apply, read, scope ?? (body => body()), CancellationToken.None);
}
