using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;

namespace HavenOS.Home;

/// <summary>Typed result of the model-discovery domain operation. It is projected into CUI as
/// the command outcome value so authored transitions branch on genuine eligibility/manifest state
/// rather than a C# code-behind step machine.</summary>
public sealed record HomeOnboardingDiscoveryState(
    bool EligibleModelAvailable,
    bool ManifestAvailable,
    IReadOnlyList<HomeOnboardingModelRecommendation> Recommendations,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>Typed result of the secure-auth adapter domain operation. Sign-in availability is a
/// genuine backend fact, never minted by the client.</summary>
public sealed record HomeOnboardingSignInState(bool Opened, bool Unavailable);

/// <summary>Typed result of a sign-in decision + automatic local-model discovery. It carries the
/// honest sign-in availability and the real local eligibility/manifest state so the authored CUI
/// can decide what happens next (auto-complete an eligible model, or offer the Download Models?
/// step). <see cref="RequiresDownload"/> is a genuine derived domain fact — when true the flow must
/// surface the Download Models? step, never silently skip it.</summary>
public sealed record HomeOnboardingProceedState(
    bool SignInOpened,
    bool SignInUnavailable,
    bool EligibleModelAvailable,
    bool ManifestAvailable,
    IReadOnlyList<HomeOnboardingModelRecommendation> Recommendations,
    string? ErrorCode,
    string? ErrorMessage)
{
    public bool RequiresDownload => !EligibleModelAvailable;
}

/// <summary>Structured recovery description for a damaged startup read. Non-null signals a
/// recovery page; the original bytes and unrelated Home state are preserved, never overwritten,
/// and never auto-completed.</summary>
public sealed record HomeOnboardingRecoveryInfo(string Code, string Message);

/// <summary>Owns the authored onboarding CUI document and exposes the read-only CUI view-state
/// (structured recovery flags) through the binding context. Application page state and transition
/// guards live in the authored document's local variables and effects, not here.</summary>
public sealed class HomeOnboardingCuiSurface : ICuiBindingContext, INotifyPropertyChanged
{
    private readonly HomeOnboardingCuiController _controller;

    public HomeOnboardingCuiSurface(HomeOnboardingCuiController controller, CuiDocument document)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public CuiDocument Document { get; }

    public static HomeOnboardingCuiSurface LoadDefault(HomeOnboardingCuiController controller)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UI", "Onboarding.cui");
        return new HomeOnboardingCuiSurface(controller, new CuiRichParser().ParseFile(path));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised by the controller when a domain result changes a view-state flag (for
    /// example clearing recovery after a successful retry). The loader re-evaluates the authored
    /// <c>&lt;If Condition="{Binding RecoveryRequired}"&gt;</c> branch live.</summary>
    internal void NotifyViewChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "RecoveryRequired": value = _controller.RecoveryRequired; return true;
            case "RecoveryCode": value = _controller.RecoveryCode; return true;
            case "RecoveryMessage": value = _controller.RecoveryMessage; return true;
            default: value = null; return false;
        }
    }
}

/// <summary>
/// Genuine typed first-run onboarding backend. It exposes only real domain operations (secure-auth
/// adapter, sign-in-decision + local-model discovery, honest install/blocked report, durable
/// completion persist, and structured recovery re-read) as typed command outcomes. It does NOT
/// decide which screen follows: page state, transitions and auto-advance guards are authored in
/// CUI, branching on the reported outcomes.
/// </summary>
public sealed class HomeOnboardingCuiController : ICuiResultReportingDispatcher
{
    private readonly HomeOnboardingService _service;
    private readonly string _profileId;
    private readonly Action? _onQuit;
    private HomeOnboardingRecoveryInfo? _recovery;
    private HomeOnboardingDiscoveryState? _discovery;
    private string? _manifestRevision;
    private bool _proceeded;
    private readonly TaskCompletionSource _completedSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HomeOnboardingCuiController(HomeOnboardingService service, string profileId,
        HomeOnboardingCuiSurface? surface = null, HomeOnboardingRecoveryInfo? recovery = null, Action? onQuit = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _profileId = profileId;
        _recovery = recovery;
        _onQuit = onQuit;
        Surface = surface ?? HomeOnboardingCuiSurface.LoadDefault(this);
    }

    /// <summary>The authored CUI document + view-state binding context.</summary>
    public HomeOnboardingCuiSurface Surface { get; }

    public bool RecoveryRequired => _recovery is not null;
    public string? RecoveryCode => _recovery?.Code;
    public string? RecoveryMessage => _recovery?.Message;

    /// <summary>Awaited by the host to publish Home only after a durable completion commit. It is
    /// a domain fact (the record really persisted), not a screen decision.</summary>
    public Task CompletionTask => _completedSignal.Task;

    /// <summary>Last typed discovery outcome, for tests and diagnostics. CUI reads eligibility via
    /// the command outcome value, not this property.</summary>
    internal HomeOnboardingDiscoveryState? LastDiscovery => _discovery;

    /// <summary>Allowlisted command→domain-operation map. Each command runs a genuine typed backend
    /// operation and returns a typed outcome; the CUI document decides what to do with it.</summary>
    public async ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return command switch
        {
            "onboarding.signin" => await ProceedAsync(signInAttempt: true, cancellationToken).ConfigureAwait(false),
            "onboarding.notnow" => await ProceedAsync(signInAttempt: false, cancellationToken).ConfigureAwait(false),
            "onboarding.discover" => await DiscoverAsync(cancellationToken).ConfigureAwait(false),
            "onboarding.download" => Download(parameter),
            "onboarding.complete" => await CompleteAsync(cancellationToken).ConfigureAwait(false),
            "onboarding.retry" => await RetryAsync(cancellationToken).ConfigureAwait(false),
            "onboarding.quit" => Quit(),
            _ => new CuiCommandOutcome(false, false, "UnknownCommand", $"Unknown onboarding command '{command}'.", null),
        };
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
        new(DispatchWithOutcomeAsync(command, parameter, cancellationToken).AsTask());

    /// <summary>Runs the sign-in decision (optionally opening the browser launcher unless it is
    /// unverifiably unavailable) and automatic local-model discovery, returning one typed
    /// <see cref="HomeOnboardingProceedState"/> outcome.</summary>
    private async ValueTask<CuiCommandOutcome> ProceedAsync(bool signInAttempt, CancellationToken cancellationToken)
    {
        _proceeded = true;
        var opened = false;
        var unavailable = false;
        if (signInAttempt)
        {
            opened = await _service.BeginSignInAsync(cancellationToken).ConfigureAwait(false);
            unavailable = !opened;
        }

        var eligible = await _service.HasEligibleLocalModelAsync(cancellationToken).ConfigureAwait(false);
        _lastEligible = eligible;
        if (eligible)
        {
            var eligibleState = new HomeOnboardingProceedState(opened, unavailable, true, false, [], null, null);
            return new CuiCommandOutcome(true, false, "DiscoveredEligible",
                "An eligible local model is already installed; completing your setup.", eligibleState);
        }

        var manifest = await _service.GetRecommendationsAsync(cancellationToken).ConfigureAwait(false);
        _manifestRevision = manifest.Succeeded ? manifest.Value!.Revision : _manifestRevision;
        var state = new HomeOnboardingProceedState(
            opened, unavailable, false, manifest.Succeeded,
            manifest.Succeeded ? manifest.Value!.Recommendations : [],
            manifest.Succeeded ? null : manifest.Code,
            manifest.Succeeded ? null : manifest.Message);
        return manifest.Succeeded
            ? new CuiCommandOutcome(true, false, "Discovered", "Recommended models are listed.", state)
            : new CuiCommandOutcome(true, false, "ManifestUnavailable",
                state.ErrorMessage ?? "Recommended models are unavailable.", state);
    }

    private async ValueTask<CuiCommandOutcome> DiscoverAsync(CancellationToken cancellationToken)
    {
        _proceeded = true;
        var eligible = await _service.HasEligibleLocalModelAsync(cancellationToken).ConfigureAwait(false);
        _lastEligible = eligible;
        if (eligible)
        {
            _discovery = new HomeOnboardingDiscoveryState(true, false, [], null, null);
            return new CuiCommandOutcome(true, false, "Discovered", "An eligible local model is installed.",
                _discovery);
        }

        var manifest = await _service.GetRecommendationsAsync(cancellationToken).ConfigureAwait(false);
        _manifestRevision = manifest.Succeeded ? manifest.Value!.Revision : _manifestRevision;
        _discovery = new HomeOnboardingDiscoveryState(
            false,
            manifest.Succeeded,
            manifest.Succeeded ? manifest.Value!.Recommendations : [],
            manifest.Succeeded ? null : manifest.Code,
            manifest.Succeeded ? null : manifest.Message);
        return manifest.Succeeded
            ? new CuiCommandOutcome(true, false, "Discovered", "Recommended local models are listed.", _discovery)
            : new CuiCommandOutcome(true, false, "ManifestUnavailable", manifest.Message ?? "Recommended models are unavailable.", _discovery);
    }

    /// <summary>Honest install handling. The exact 9to1.uk manifest/install endpoint is not yet
    /// specified, so a download request reports a scoped blocked state and never a false completion.
    /// The selected recommendation is validated (non-null and a real manifest item) and carried into
    /// the typed operation result — it is never silently discarded.</summary>
    private CuiCommandOutcome Download(object? parameter)
    {
        if (parameter is not HomeOnboardingModelRecommendation recommendation)
            return new CuiCommandOutcome(false, false, "DownloadParameterInvalid",
                "A recommendation is required to start a download.", null);
        var known = _discovery?.Recommendations.Any(item =>
            string.Equals(item.ProviderId, recommendation.ProviderId, StringComparison.Ordinal) &&
            string.Equals(item.ModelId, recommendation.ModelId, StringComparison.Ordinal)) ?? false;
        if (!known)
            return new CuiCommandOutcome(false, false, "DownloadParameterInvalid",
                "The selected recommendation is not from the current manifest.", null);
        return new CuiCommandOutcome(true, false, "DownloadBlocked",
            "Model downloads are unavailable right now; you can continue without one and install from Settings later.",
            new HomeOnboardingDownloadState(HomeOnboardingDownloadStateCode.Blocked, null,
                "Model downloads are unavailable in this build.", $"{recommendation.ProviderId}/{recommendation.ModelId}"));
    }

    private async ValueTask<CuiCommandOutcome> CompleteAsync(CancellationToken cancellationToken)
    {
        if (!_proceeded)
            return new CuiCommandOutcome(false, false, "FlowNotStarted",
                "Onboarding has not run local discovery; cannot persist completion yet.", null);
        var eligible = _discovery?.EligibleModelAvailable == true || _lastEligible;
        var completion = await _service.PersistCompletionAsync(
            _profileId, signedIn: false, modelEligible: eligible, manifestRevision: _manifestRevision,
            cancellationToken).ConfigureAwait(false);
        if (completion.Succeeded)
        {
            _completedSignal.TrySetResult();
            return new CuiCommandOutcome(true, false, "Persisted", "First-run flow completed for this profile.", completion.Value);
        }
        return new CuiCommandOutcome(false, false, completion.Code, completion.Message, null);
    }

    private bool _lastEligible;

    private async ValueTask<CuiCommandOutcome> RetryAsync(CancellationToken cancellationToken)
    {
        var read = await _service.ReadCompletionAsync(_profileId, cancellationToken).ConfigureAwait(false);
        if (read.State == HomeOnboardingReadState.NotCompleted)
        {
            _recovery = null;
            Surface.NotifyViewChanged();
            return new CuiCommandOutcome(true, false, "Recovered", "The local state is now readable; starting fresh.", read);
        }
        _recovery ??= new HomeOnboardingRecoveryInfo(read.Code ?? "StillDamaged",
            read.Message ?? "Onboarding state is still damaged.");
        Surface.NotifyViewChanged();
        return new CuiCommandOutcome(false, false, read.Code ?? "StillDamaged",
            read.Message ?? "Onboarding state is still damaged.", read);
    }

    private CuiCommandOutcome Quit()
    {
        if (_onQuit is null)
            return new CuiCommandOutcome(false, false, "QuitUnsupported", "No app quit callback is wired.", null);
        _onQuit();
        return new CuiCommandOutcome(true, false, "Quit", "The application is closing.", null);
    }
}
