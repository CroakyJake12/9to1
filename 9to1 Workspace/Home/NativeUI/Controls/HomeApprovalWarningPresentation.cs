using Avalonia.Controls;
using Avalonia.Platform;
using CakeOS.Cui.Runtime;
using Haven.Application;

namespace HavenOS.Home.NativeUI;

/// <summary>One actual native warning presentation, captured by the owning approval surface.
/// The frame is observation data, never permission, trust, execution or a readiness grant.</summary>
public sealed class HomeApprovalWarningFrame
{
    internal HomeApprovalWarningFrame(HomeApprovalCuiSurface surface, CuiSceneHost host,
        Control root, TextBlock warning, WindowBase window, ITopLevelImpl platform,
        AuthenticatedResourceActor actor, string requestId, string digest,
        string warningText, long generation, Func<bool> isOriginalCurrent)
    {
        Surface = surface; Host = host; Root = root; Warning = warning; Window = window;
        Platform = platform; Actor = actor; RequestId = requestId; Digest = digest;
        WarningText = warningText; Generation = generation;
        _originalIsCurrent = isOriginalCurrent ?? throw new ArgumentNullException(nameof(isOriginalCurrent));
    }

    private readonly Func<bool> _originalIsCurrent;

    /// <summary>Owning surface observation on its live UI dispatcher only. A false result denies
    /// publication; true supplies no permission, native draw or presentation receipt.</summary>
    public bool IsOriginalCurrent() => _originalIsCurrent();

    public HomeApprovalCuiSurface Surface { get; }
    public CuiSceneHost Host { get; }
    public Control Root { get; }
    public TextBlock Warning { get; }
    public WindowBase Window { get; }
    public ITopLevelImpl Platform { get; }
    public AuthenticatedResourceActor Actor { get; }
    public string RequestId { get; }
    public string Digest { get; }
    public string WarningText { get; }
    public long Generation { get; }
}

/// <summary>A maintained native-owner implementation must join the original target drawing
/// and checked SAME native-window submission, then return the exact bound observation.
/// Loaded, layout, visibility, a headless/offscreen target, and a batch's Rendered task alone
/// cannot issue this receipt. No such implementation or registration is supplied here.</summary>
public abstract class HomeApprovalOriginalWarningPresentationSource
{
    // Only an explicitly reviewed implementation in the native owner assembly may issue observations.
    internal HomeApprovalOriginalWarningPresentationSource() { }

    public abstract Task<HomeApprovalWarningPresentation?> AcquireOriginalAsync(
        HomeApprovalWarningFrame originalFrame, CancellationToken cancellationToken);
}

/// <summary>Historical successful observation from the original maintained native source.
/// This receipt does not establish current authority, OS scanout, absence of occlusion or user attention.</summary>
public sealed class HomeApprovalWarningPresentation
{
    internal HomeApprovalWarningPresentation(HomeApprovalOriginalWarningPresentationSource source,
        HomeApprovalWarningFrame frame, object originalCheckedNativeSubmission)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(originalCheckedNativeSubmission);
        Source = source; Frame = frame; OriginalCheckedNativeSubmission = originalCheckedNativeSubmission;
    }

    internal HomeApprovalOriginalWarningPresentationSource Source { get; }
    internal HomeApprovalWarningFrame Frame { get; }
    internal object OriginalCheckedNativeSubmission { get; }

    internal bool IsBoundTo(HomeApprovalOriginalWarningPresentationSource originalSource,
        HomeApprovalWarningFrame originalFrame) =>
        ReferenceEquals(Source, originalSource) && ReferenceEquals(Frame, originalFrame);
}
