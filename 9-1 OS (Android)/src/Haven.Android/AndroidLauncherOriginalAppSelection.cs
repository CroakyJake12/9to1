using System.Globalization;
using System.Runtime.CompilerServices;
using Haven.Application.Go;
using NineToOne.Launcher;
namespace Haven.Android;

/// <summary>Additional original native-host lifetime check; this predicate is not Home or installed application authority.</summary>
public interface IAndroidGoOriginalHostInvocation : IGoOriginalActorInvocation
{
    Task InvokeForOriginalHostAsync(GoCanonicalReference reference, string actionId,
        Haven.Application.AuthenticatedResourceActor originalActor, Func<bool> originalHostCurrent, CancellationToken ct);
}

/// <summary>Private original native-view selection metadata; central Home/Go authority is still required at invocation.</summary>
public sealed class AndroidLauncherOriginalAppSelection<TView> where TView : class
{
    internal AndroidLauncherOriginalAppSelection(AndroidLauncherOriginalAppActions<TView> issuer,
        LauncherSessionSnapshot original, TView view, GoCanonicalReference reference, Func<bool> viewIsCurrent)
    { Issuer = issuer; Original = original; View = view; Reference = reference; ViewIsCurrent = viewIsCurrent; }
    internal AndroidLauncherOriginalAppActions<TView> Issuer { get; }
    internal LauncherSessionSnapshot Original { get; }
    internal TView View { get; }
    internal GoCanonicalReference Reference { get; }
    internal Func<bool> ViewIsCurrent { get; }
}

public sealed class AndroidLauncherOriginalAppActions<TView>(HomeLauncherSession sessions,
    IGoOriginalActorInvocation originalOwner) where TView : class
{
    private readonly ConditionalWeakTable<AndroidLauncherOriginalAppSelection<TView>, object> issued = new();
    public AndroidLauncherOriginalAppSelection<TView> Issue(LauncherSessionSnapshot original, TView originalView,
        GoCanonicalReference reference, Func<bool> viewIsCurrent)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(originalView);
        ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(viewIsCurrent);
        if (originalOwner.ProviderId != AndroidInstalledApplicationsGoProvider.Id || reference is not { Owner: "Home", Kind: "os.installed-application" } ||
            !Guid.TryParse(reference.Id, out var id) || id == Guid.Empty ||
            !long.TryParse(reference.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
            throw new UnauthorizedAccessException("The original canonical Android app selection is invalid.");
        var selection = new AndroidLauncherOriginalAppSelection<TView>(this, original, originalView, reference, viewIsCurrent);
        issued.Add(selection, new object()); return selection;
    }
    public async Task InvokeAsync(AndroidLauncherOriginalAppSelection<TView> originalSelection, TView originalView, CancellationToken ct)
    {
        Require(originalSelection, originalView); ct.ThrowIfCancellationRequested();
        var originalActor = await sessions.RequireOriginalActorAsync(originalSelection.Original, ct);
        Require(originalSelection, originalView); ct.ThrowIfCancellationRequested();
        if (originalOwner is not IAndroidGoOriginalHostInvocation originalHostOwner)
            throw new UnauthorizedAccessException("The original Android owner cannot retain this native host lifetime.");
        bool Current() => ReferenceEquals(originalSelection.Issuer, this) && issued.TryGetValue(originalSelection, out _) &&
            ReferenceEquals(originalSelection.View, originalView) && originalSelection.ViewIsCurrent();
        await originalHostOwner.InvokeForOriginalHostAsync(originalSelection.Reference, "Open", originalActor, Current, ct);
        // Native activation is external; no rollback or final principal-through-Android atomic lease is claimed.
    }
    private void Require(AndroidLauncherOriginalAppSelection<TView> selection, TView view)
    {
        ArgumentNullException.ThrowIfNull(selection); ArgumentNullException.ThrowIfNull(view);
        if (!ReferenceEquals(selection.Issuer, this) || !issued.TryGetValue(selection, out _) ||
            !ReferenceEquals(selection.View, view) || !selection.ViewIsCurrent())
            throw new UnauthorizedAccessException("The actual original Android app tile is unavailable.");
    }
}
