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

    [Fact]
    public void Explicit_merge_combines_user_text_against_current_basis_without_replacing_the_retained_original()
    {
        var saved = new DeveloperDraftRecoveryTab("Main.cui", Hash("old source"), "protected unsaved text", true);
        const string current = "externally saved text";
        const string merged = "externally saved text plus the user's retained changes";
        var adopted = DeveloperProjectWorkbenchPage.PrepareExplicitManualRecovery(saved, Hash(current), current, merged);
        Assert.NotNull(adopted);
        Assert.Equal(merged, adopted.RetainedDraftText);
        Assert.Equal(current, adopted.CurrentSourceText);
        Assert.Equal(Hash(current), adopted.CurrentSourceSha256);
        Assert.Equal(Hash("old source"), adopted.SavedBaselineSha256);
        Assert.Equal("protected unsaved text", saved.DraftText);
        Assert.Null(DeveloperProjectWorkbenchPage.PrepareExplicitManualRecovery(saved, Hash(current), "changed again", merged));
        Assert.Equal("protected unsaved text", saved.DraftText);
    }

    [Fact]
    public void Explicit_empty_merge_is_not_replaced_by_retained_text_and_binary_or_oversized_merges_are_refused()
    {
        var saved = new DeveloperDraftRecoveryTab("Main.cui", Hash("old source"), "keep original protected text", true);
        const string current = "current source";
        var empty = DeveloperProjectWorkbenchPage.PrepareExplicitManualRecovery(saved, Hash(current), current, "");
        Assert.NotNull(empty); Assert.Equal("", empty.RetainedDraftText);
        Assert.Throws<ArgumentException>(() => DeveloperProjectWorkbenchPage.PrepareExplicitManualRecovery(
            saved, Hash(current), current, "binary\0merge"));
        Assert.Throws<ArgumentException>(() => DeveloperProjectWorkbenchPage.PrepareExplicitManualRecovery(
            saved, Hash(current), current, new string('x', DeveloperSourceReviewSession.MaximumTextCharacters + 1)));
        Assert.Equal("keep original protected text", saved.DraftText);
    }

    private static string Hash(string text) => DeveloperManualSourceEditOwner.Sha256(text);
}
