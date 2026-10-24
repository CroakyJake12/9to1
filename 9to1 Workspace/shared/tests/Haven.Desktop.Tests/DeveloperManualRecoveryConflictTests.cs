using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using Xunit;

namespace Haven.Desktop.Tests;

// Authored NOT RUN. These actual data/adoption comparisons are used by the real driver after
// genuine READ; they confer no Home/Files authority and make no native/UI success claim.
public sealed class DeveloperManualRecoveryConflictTests
{
    [Fact]
    public void Changed_readable_source_keeps_protected_draft_separate_and_explicit_adoption_is_unmerged()
    {
        const string oldSource = "source before closure";
        const string currentSource = "someone else's new source";
        const string retainedText = "the user's unsaved draft";
        var saved = new DeveloperDraftRecoveryTab("Main.cui", Hash(oldSource), retainedText, true);
        var observed = new DeveloperRestoredDirtyEditorTab(saved, currentSource);
        Assert.False(observed.BaselineMatches);
        Assert.Equal(retainedText, observed.Draft.DraftText);
        Assert.Equal(currentSource, observed.FreshBaselineText);
        var adopted = DeveloperProjectWorkbenchPage.PrepareExplicitManualRecovery(saved, observed.FreshSourceSha256, currentSource);
        Assert.NotNull(adopted);
        Assert.Equal(currentSource, adopted.CurrentSourceText);
        Assert.Equal(Hash(currentSource), adopted.CurrentSourceSha256);
        Assert.Equal(retainedText, adopted.RetainedDraftText); // No implicit merge/overwrite of the user's text.
        Assert.Equal(Hash(oldSource), adopted.SavedBaselineSha256); // Provenance does not become the write basis.
        Assert.Equal("Main.cui", adopted.RelativePath);
    }

    [Fact]
    public async Task Source_changing_while_reconciliation_read_is_held_requires_reinspection_and_retains_draft()
    {
        var saved = new DeveloperDraftRecoveryTab("Main.cui", Hash("old baseline"), "retained complete draft", true);
        const string inspected = "first external revision";
        const string changedAgain = "second external revision";
        var actualRead = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DeveloperManualRecoveryAdoption?> FinishExplicitChoiceAsync() =>
            DeveloperProjectWorkbenchPage.PrepareExplicitManualRecovery(saved, Hash(inspected), await actualRead.Task);
        var choice = FinishExplicitChoiceAsync();
        Assert.False(choice.IsCompleted);
        actualRead.SetResult(changedAgain);
        Assert.Null(await choice.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("retained complete draft", saved.DraftText);
        var afterReinspection = DeveloperProjectWorkbenchPage.PrepareExplicitManualRecovery(saved, Hash(changedAgain), changedAgain);
        Assert.NotNull(afterReinspection);
        Assert.Equal(saved.DraftText, afterReinspection.RetainedDraftText);
        Assert.Equal(Hash(changedAgain), afterReinspection.CurrentSourceSha256);
    }

    private static string Hash(string text) => DeveloperManualSourceEditOwner.Sha256(text);
}
