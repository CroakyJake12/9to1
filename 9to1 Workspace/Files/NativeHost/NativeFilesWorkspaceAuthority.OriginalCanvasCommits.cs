using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

public sealed partial class NativeFilesWorkspaceAuthority
{
    /// <summary>Expose the SAME existing private claimed-capability/configuration/owner fence to
    /// the Canvas window borrowed by the genuine Home process. This issues no claim or grant.</summary>
    public ValueTask<HomeClaimedResourceCommitFence> CaptureOriginalCanvasCommitFenceAsync(
        NativeFilesWorkspace sameOriginal, HomeResourceOperationBroker sameBroker,
        HomeResourceExecutionCapability sameClaimedCapability, Func<bool> originalLifetime,
        CancellationToken cancellationToken = default)
        => CaptureBrowserCommitFenceAsync(sameOriginal, sameBroker, sameClaimedCapability,
            originalLifetime, cancellationToken);
}
