using CakeOS.Cui;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>IX-BUILD-004: breaking a CUI binding while its prior preview shows a completed checklist must display Last valid, reject stale
/// preview writes to the live Task, and preserve unknown authored source. Proves failed recompile keeps Last valid marked stale, a stale preview
/// cannot write to the live task, and matching fresh text restores Current.</summary>
public sealed class DeveloperCuiDraftPreviewStaleTests
{
    private const string Valid = "<Cui version=\"2\"><Page><Text text=\"Done\"/></Page></Cui>";
    // A binding expression is rejected by the static preview preflight, simulating a broken CUI binding.
    private const string BrokenBinding = "<Cui version=\"2\"><Page><Text text=\"{Task.Title}\"/></Page></Cui>";

    [Fact]
    public void ValidCompileProducesCurrentPreview()
    {
        var attempt = DeveloperCuiDraftPreviewCompiler.Compile("page.cui", Valid);
        Assert.True(attempt.Succeeded);

        var retained = DeveloperCuiDraftPreviewCompiler.Observe(null, attempt);
        Assert.NotNull(retained);
        Assert.True(retained!.IsCurrent);
    }

    [Fact]
    public void FailedRecompileKeepsLastValidMarkedStale()
    {
        var current = DeveloperCuiDraftPreviewCompiler.Observe(null, DeveloperCuiDraftPreviewCompiler.Compile("page.cui", Valid));
        Assert.NotNull(current);

        // Break the binding: the compile fails but the prior good rendering is retained and marked stale.
        var broken = DeveloperCuiDraftPreviewCompiler.Compile("page.cui", BrokenBinding);
        Assert.False(broken.Succeeded);
        var stale = DeveloperCuiDraftPreviewCompiler.Observe(current, broken);

        Assert.NotNull(stale);
        Assert.False(stale!.IsCurrent);
        Assert.Equal(DeveloperCuiDraftPreviewCompiler.DeveloperCuiPreviewCurrency.Stale, stale.Currency);
        // The last valid document is preserved for display.
        Assert.NotNull(stale.Document);
    }

    [Fact]
    public void StalePreviewCannotWriteToLiveTask()
    {
        var current = DeveloperCuiDraftPreviewCompiler.Observe(null, DeveloperCuiDraftPreviewCompiler.Compile("page.cui", Valid));
        var stale = DeveloperCuiDraftPreviewCompiler.Observe(current, DeveloperCuiDraftPreviewCompiler.Compile("page.cui", BrokenBinding));

        // Applying the stale preview to the live task is refused; authored source is untouched.
        var result = DeveloperCuiDraftPreviewCompiler.ApplyToTask(stale, current!.SourceSha256);
        Assert.False(result.Applied);
        Assert.Contains("stale", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MatchingFreshTextRestoresCurrentAndApplies()
    {
        var current = DeveloperCuiDraftPreviewCompiler.Observe(null, DeveloperCuiDraftPreviewCompiler.Compile("page.cui", Valid));
        Assert.NotNull(current);

        // The current draft still matches the retained preview's identity and is Current.
        var result = DeveloperCuiDraftPreviewCompiler.ApplyToTask(current, current!.SourceSha256);
        Assert.True(result.Applied);
    }

    [Fact]
    public void PreviewWhoseIdentityNoLongerMatchesDraftIsRefused()
    {
        var current = DeveloperCuiDraftPreviewCompiler.Observe(null, DeveloperCuiDraftPreviewCompiler.Compile("page.cui", Valid));
        Assert.NotNull(current);

        // A different draft hash (unknown authored source) must not be overwritten by the retained preview.
        var result = DeveloperCuiDraftPreviewCompiler.ApplyToTask(current, new string('a', 64));
        Assert.False(result.Applied);
        Assert.Contains("does not identify", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoRetainedPreviewRefusesApply()
    {
        var result = DeveloperCuiDraftPreviewCompiler.ApplyToTask(null, new string('b', 64));
        Assert.False(result.Applied);
        Assert.Contains("No valid preview", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedCompileWithNoPriorPreviewRetainsNothing()
    {
        var broken = DeveloperCuiDraftPreviewCompiler.Compile("page.cui", BrokenBinding);
        var retained = DeveloperCuiDraftPreviewCompiler.Observe(null, broken);
        Assert.Null(retained);
    }
}
