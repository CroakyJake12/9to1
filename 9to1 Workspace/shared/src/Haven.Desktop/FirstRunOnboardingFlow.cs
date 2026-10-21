using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop;

/// <summary>A mounted first-run onboarding CUI session. It owns the real loader (action/disposal
/// pipeline) and the typed controller/surface, and exposes the mounted root + the durable completion
/// signal. <see cref="RetireAsync"/> is the single real drain used by BOTH the production host gate
/// and the normal Desktop owning tests — never a fabricated success task.</summary>
public sealed class FirstRunOnboardingSession
{
    private readonly CuiControlLoader _loader;

    internal FirstRunOnboardingSession(HomeOnboardingCuiController controller, CuiControlLoader loader, Control root)
    {
        Controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    /// <summary>Typed first-run backend/controller (sign-in decision + discovery, durable persist, recovery).</summary>
    public HomeOnboardingCuiController Controller { get; }

    /// <summary>The mounted authored CUI root control (the Welcome/SignIn/Download flow).</summary>
    public Control Root { get; }

    /// <summary>Completes only after a genuine durable completion commit.</summary>
    public Task CompletionTask => Controller.CompletionTask;

    /// <summary>Public snapshot of the loader's retained action/disposal pipeline, used by tests to
    /// settle async domain ops and the declarative effect follow-ups they schedule.</summary>
    public Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();

    /// <summary>Retires the session: closes action admission (dispose) then joins the real retained
    /// action/disposal pipeline. The same original drain is awaited by the host before Home
    /// publication and by tests after assertions. Dispose and drain faults are both retained and
    /// aggregated — the drain is ALWAYS joined even if dispose throws, so no admitted action is
    /// ever silently abandoned.</summary>
    public async Task RetireAsync()
    {
        var failures = new List<Exception>();
        try { _loader.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        try { await _loader.WhenActionsIdleAsync(); }
        catch (Exception exception) { failures.Add(exception); }
        if (failures.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
            throw new AggregateException("Onboarding session retirement failed.", failures);
    }
}

/// <summary>Thin typed production mount/gate helper. It is the SINGLE helper used by the actual
/// <see cref="App.UnifiedWorkspaceHost"/> first-run gate and by the normal portable Desktop owning
/// tests, so the tested path is the production path. It only mounts the authored flow and exposes
/// the session; the caller owns publishing/clearing Content and awaits <see cref="CompletionTask"/>
/// before publication. Must be invoked on the UI thread.</summary>
public static class FirstRunOnboardingFlow
{
    /// <summary>Mounts the authored Onboarding.cui over the real loader/controller/surface.
    /// Throws <see cref="InvalidDataException"/> when the canonical document fails to parse or
    /// produce a usable control (the created loader is disposed before rethrow).</summary>
    public static FirstRunOnboardingSession Mount(
        HomeOnboardingService service,
        string profileId,
        HomeOnboardingRecoveryInfo? recovery = null,
        Action? onQuit = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        Dispatcher.UIThread.VerifyAccess();

        var controller = new HomeOnboardingCuiController(service, profileId, recovery: recovery, onQuit: onQuit);

        var application = Avalonia.Application.Current;
        if (application is not null && !application.Resources.TryGetResource("CuiBackgroundBrush", application.ActualThemeVariant, out _))
            CuiNativeHost.InitialisePrimitiveTheme(application, "Home", CakeOS.Cui.Themes.CuiAppearance.Dark);

        var loader = new CuiControlLoader();
        loader.SetBindingContext(controller.Surface);
        loader.SetActionDispatcher(controller);
        try
        {
            var parser = new CuiRichParser();
            var document = parser.ParseFile(Path.Combine(AppContext.BaseDirectory, "UI", "Onboarding.cui"));
            if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            {
                throw new InvalidDataException("The canonical first-run onboarding CUI document is invalid: " +
                    string.Join("; ", parser.Diagnostics.Diagnostics));
            }

            var loaded = loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            {
                throw new InvalidDataException("The canonical first-run onboarding CUI did not produce a usable control: " +
                    string.Join("; ", loaded.Diagnostics));
            }

            loader.WireBindings(loaded.Root);
            return new FirstRunOnboardingSession(controller, loader, loaded.Root!);
        }
        catch
        {
            // Failure path: dispose the loader AND join its real retained drain so nothing is leaked
            // and no admitted action is abandoned. The loader never reaches a usable mounted state
            // here, so the drain joins immediately; faults are aggregated and never swallowed. The
            // original parse/load failure is rethrown when cleanup is clean.
            var failures = new List<Exception>();
            try { loader.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
            try { loader.WhenActionsIdleAsync().GetAwaiter().GetResult(); }
            catch (Exception exception) { failures.Add(exception); }
            if (failures.Count == 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("Onboarding mount cleanup failed.", failures);
            throw;
        }
    }
}
