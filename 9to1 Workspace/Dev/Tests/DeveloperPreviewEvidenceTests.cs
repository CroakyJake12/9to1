using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperPreviewEvidenceTests
{
    [Fact]
    public void RetainedPreviewNamesItsActualRevisionButNeverClaimsAChangedOrFailedDraftIsCurrent()
    {
        Assert.Equal("Preview updated. Showing Pages/First.cui · draft revision 27.",
            DeveloperPreviewEvidence.Describe("Preview updated.", "Pages/First.cui", 27, true, true));
        var stale = DeveloperPreviewEvidence.Describe("This draft could not be previewed.",
            "Pages/First.cui", 27, true, false);
        Assert.Contains("Last valid (stale): Pages/First.cui · draft revision 27.", stale);
        Assert.Contains("Not a preview of the current draft.", stale);
        Assert.DoesNotContain("Showing", stale);
    }

    [Fact]
    public void NoMountedSceneDoesNotClaimAnySourceIsShowing()
    {
        var unmounted = DeveloperPreviewEvidence.Describe("Preview unavailable.",
            "Pages/First.cui", 27, false, true);
        Assert.Contains("not currently visible", unmounted);
        Assert.DoesNotContain("Showing", unmounted);
        Assert.Equal("Not yet previewed.",
            DeveloperPreviewEvidence.Describe("Not yet previewed.", null, 0, false, false));
    }
}
