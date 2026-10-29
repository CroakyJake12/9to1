#if !ANDROID
using Avalonia.Threading;
using HavenOS.Apps.Browse;
using Haven.Application;

namespace Haven.Desktop;

public sealed partial class App
{
    internal sealed partial class UnifiedWorkspaceHost
    {
        internal sealed class CloseVetoException : InvalidOperationException
        {
            internal CloseVetoException() : base("The prepared workspace owner is no longer current or declined retirement.") { }
        }
        private Task<ClosePreparation?>? _preparation;
        private ClosePreparation? _activePreparation, _acceptedPreparation;
        private readonly List<Task> _acceptedChromeSources = [];
        private readonly List<Exception> _acceptedPreparationFailures = [];

        // Legacy bool owners have no reversible edit fence. Their existing second
        // decision is preserved; only an actual Chrome lease carries strong custody.
        internal sealed class ClosePreparation
        {
            private readonly UnifiedWorkspaceHost _owner;
            private readonly BrowseChrome? _chrome;
            private readonly BrowseNativeWorkspace? _workspace;
            private readonly Task? _initialization, _creation;
            private readonly bool _startup, _strong;
            private readonly (Guid Id, object? Owner, object? Engine, object Address)[] _legacyOwners;
            internal BrowseChrome.ClosePreparation? Chrome;
            internal Task? AbortOriginal;
            internal bool Aborting, Accepted;
            internal Task? AcceptedOriginal;
            internal bool ChromeAccepted;
            internal readonly List<Exception> BeginFailures = [];
            internal ClosePreparation(UnifiedWorkspaceHost owner, bool captureOwners = true)
            {
                _owner = owner; _chrome = owner._chrome; _workspace = owner._workspace;
                _initialization = owner._initialization; _creation = owner._chromeCreation;
                _startup = owner._initialization is null or { IsCompleted: false } || owner._chrome is null;
                _legacyOwners = captureOwners ? Capture(_chrome) : []; _strong = captureOwners && HasStrongOwner(_chrome);
            }
            internal bool IsStrong => Chrome is not null;
            internal bool Startup => _startup;
            internal bool IsCurrent { get { lock (_owner._gate) return Current; } }
            private bool Current => ReferenceEquals(_owner._activePreparation, this) && !Aborting && !Accepted
                && _owner.IsCurrent && ReferenceEquals(_chrome, _owner._chrome) && ReferenceEquals(_workspace, _owner._workspace)
                && ReferenceEquals(_initialization, _owner._initialization) && ReferenceEquals(_creation, _owner._chromeCreation)
                && _startup == (_owner._initialization is null or { IsCompleted: false } || _owner._chrome is null)
                && (Chrome?.IsCurrent ?? (_startup || _strong == HasStrongOwner(_chrome) && _legacyOwners.SequenceEqual(Capture(_chrome))));
            internal bool IsAcceptedOriginal => Accepted && ReferenceEquals(_owner._acceptedPreparation, this)
                && AcceptedOriginal is not null && ReferenceEquals(AcceptedOriginal, _owner.OriginalClose);
            internal Task AbortAsync() { _owner.DemandExternalOriginalRetirementJoin(); return _owner.AbortPreparation(this); }
            internal Task BeginClose() { _owner.DemandExternalOriginalRetirementJoin(); return _owner.BeginPreparedClose(this); }
            private static (Guid, object?, object?, object)[] Capture(BrowseChrome? chrome) => chrome is null ? [] :
                chrome.State.Tabs.Select(tab => (tab.Id, (object?)chrome.ObserveOriginalAppContent(tab.Id),
                    (object?)chrome.ObserveOriginalEngine(tab.Id), (object)tab.Address)).ToArray();
        }
        private static bool HasStrongOwner(BrowseChrome? chrome) => chrome is not null && chrome.State.Tabs.Any(tab =>
            chrome.ObserveOriginalAppContent(tab.Id) is IBrowseAppContentClosePreparationParticipant { SupportsReversibleClosePreparation: true });

        // Names the tab owners a close is waiting behind, by their actual content type AND their real
        // session state. Measured on 29 October: the abort line said only "chrome returned no close
        // preparation", so the participant that declined was invisible. Initialization/publication/
        // retirement state is exactly what the close decision already reads; this prepares, joins or
        // vetoes nothing.
        private static string DescribeCloseCandidates(BrowseChrome? chrome) => chrome is null ? "no chrome" :
            string.Join(",", chrome.State.Tabs.Select(tab =>
            {
                var content = chrome.ObserveOriginalAppContent(tab.Id);
                var participant = content as IBrowseAppContentClosePreparationParticipant;
                return (content?.GetType().Name ?? "no-content") + "[reversible=" +
                    (participant?.SupportsReversibleClosePreparation.ToString() ?? "not-a-participant") + "]";
            }));

        private static string DescribeCloseParticipants(BrowseChrome? chrome) => chrome is null ? "no chrome" :
            string.Join(",", chrome.State.Tabs.Select(tab =>
            {
                var content = chrome.ObserveOriginalAppContent(tab.Id);
                var init = content?.OriginalInitialization;
                var initState = init is null ? "unstarted"
                    : init.IsFaulted ? "FAULTED:" + (init.Exception?.InnerExceptions?.FirstOrDefault()?.GetType().Name ?? "unknown")
                    : init.IsCompletedSuccessfully ? "started"
                    : init.IsCanceled ? "canceled" : "PENDING";
                var closing = content?.OriginalClose is { } actualClose
                    ? (actualClose.IsCompletedSuccessfully ? "closed" : actualClose.IsFaulted ? "close-FAULTED" : "closing")
                    : "not-closing";
                var address = tab.Address.ToString();
                return (content is { } owner ? owner.GetType().Name : "no-content") +
                    $"[init={initState},pub={content?.IsPublicationCurrent.ToString() ?? "n/a"}," +
                    $"retirable={content?.CanRetireOriginalView.ToString() ?? "n/a"},{closing}]" +
                    (address.Length > 0 ? "@" + (address.Length <= 42 ? address : address[..42]) + "…" : string.Empty);
            }));

        internal Task<ClosePreparation?> PrepareCloseAsync(CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess(); DemandExternalOriginalRetirementJoin();
            return AcquirePreparation(token);
        }
        private Task<ClosePreparation?> AcquirePreparation(CancellationToken token)
        {
            lock (_gate)
            {
                if (_acceptedPreparation is { } accepted)
                {
                    if (_preparation is { IsCompletedSuccessfully: true } actual && ReferenceEquals(actual.GetAwaiter().GetResult(), accepted)) return actual;
                    return _preparation = Task.FromResult<ClosePreparation?>(accepted); // Observes the real already accepted original.
                }
                if (_preparation is { IsCompleted: false } || _activePreparation is not null) return _preparation!;
                var attempt = new ClosePreparation(this); _activePreparation = attempt;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _preparation = Track(PrepareCloseDriverAsync(start.Task, attempt, token)); start.SetResult(); return _preparation;
            }
        }
        private async Task<ClosePreparation?> PrepareCloseDriverAsync(Task start, ClosePreparation attempt, CancellationToken token)
        {
            await start; using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var failures = new List<Exception>();
            try
            {
                token.ThrowIfCancellationRequested();
                if (!attempt.IsCurrent) throw new CloseVetoException();
                if (attempt.Startup) return attempt;
                if (_workspace is not null && !_workspace.CanAdmitClose())
                { WriteOriginalCloseDiagnostic("unified driver: workspace refuses close admission; preparation aborts"); await AbortPreparation(attempt); return null; }
                // Named participants and elapsed time, so a close that never finishes says which
                // owner it is waiting for. This is the stage the retained 28 October evidence stops
                // inside: after "unified workspace close preparation acquired" nothing further was
                // recorded, and the window stayed open with no report to the user.
                WriteOriginalCloseDiagnostic($"unified driver: strong close owner={HasStrongOwner(_chrome)} participants=[{DescribeCloseParticipants(_chrome)}]");
                if (HasStrongOwner(_chrome))
                {
                    var actual = Track(_chrome!.PrepareCloseAsync(token));
                    await TimeOriginalCloseStageAsync("unified driver: reversible chrome close preparation", JoinOriginalAppTaskAsync(actual, failures));
                    ThrowAppCauses(failures);
                    attempt.Chrome = actual.GetAwaiter().GetResult(); // SAME completed actual source, before later getters.
                    if (attempt.Chrome is null)
                    {
                        // Name the decliner instead of reporting only the aggregate. Each tab's close
                        // capability is exactly what the chrome decision routes on, so this records
                        // which owners are reversible participants, which are one-shot, and which are
                        // not owners at all - plus the same live session state as before the call.
                        WriteOriginalCloseDiagnostic("unified driver: chrome returned no close preparation; aborts. decliner candidates=[" +
                            DescribeCloseCandidates(_chrome) + "] state after=[" + DescribeCloseParticipants(_chrome) + "]");
                        await AbortPreparation(attempt);
                        return null;
                    }
                }
                else if (_chrome is not null)
                {
                    var actual = Track(_chrome.TryPrepareToCloseAsync(token));
                    await TimeOriginalCloseStageAsync("unified driver: one-shot chrome close attempt", JoinOriginalAppTaskAsync(actual, failures));
                    ThrowAppCauses(failures);
                    if (!actual.GetAwaiter().GetResult()) { WriteOriginalCloseDiagnostic("unified driver: chrome vetoed the one-shot close attempt; aborts"); await AbortPreparation(attempt); return null; }
                }
                token.ThrowIfCancellationRequested();
                if (!attempt.IsCurrent || _workspace is not null && !_workspace.CanAdmitClose())
                { await AbortPreparation(attempt); return null; }
                return attempt;
            }
            catch (Exception failure)
            {
                AddAppCause(failures, failure);
                try { await JoinOriginalAppTaskAsync(AbortPreparation(attempt), failures); }
                catch (Exception cleanup) { AddAppCause(failures, cleanup); }
                ThrowAppCauses(failures); throw;
            }
        }
        private Task AbortPreparation(ClosePreparation attempt)
        {
            lock (_gate)
            {
                if (attempt.AbortOriginal is { } original) return original;
                if (attempt.Accepted) throw new InvalidOperationException("Accepted host retirement cannot be rolled back.");
                attempt.Aborting = true;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                attempt.AbortOriginal = AbortPreparationAsync(start.Task, attempt); _sources.Add(attempt.AbortOriginal);
                start.SetResult(); return attempt.AbortOriginal;
            }
        }
        private async Task AbortPreparationAsync(Task start, ClosePreparation attempt)
        {
            await start; using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (attempt.Chrome is { } chrome)
            {
                var actual = chrome.AbortAsync(); lock (_gate) _sources.Add(actual);
                var failures = new List<Exception>(); await JoinOriginalAppTaskAsync(actual, failures); ThrowAppCauses(failures);
            }
            lock (_gate) if (ReferenceEquals(_activePreparation, attempt)) _activePreparation = null;
        }
        private Task BeginPreparedClose(ClosePreparation attempt)
        {
            Dispatcher.UIThread.VerifyAccess();
            lock (_gate)
            {
                if (attempt.IsAcceptedOriginal) return attempt.AcceptedOriginal!;
                if (!attempt.IsCurrent) throw new CloseVetoException();
                if (_workspace is not null && !_workspace.CanAdmitClose()) throw new CloseVetoException();
                if (attempt.Chrome is not { } chrome)
                {
                    // A bool owner must still make its current real close decision.
                    // No irreversible or frozen-editor witness is issued here.
                    return CloseAndDrainAsync();
                }
                var acquisition = BeginChromeAsync(attempt); _sources.Add(acquisition);
                if (!attempt.ChromeAccepted)
                {
                    ThrowAppCauses(attempt.BeginFailures); throw new CloseVetoException();
                }
                if (_pendingClose is null)
                {
                    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingClose = CloseCoreAsync(start.Task); _closeAttempts.Add(_pendingClose);
                    AcceptPreparation(attempt); start.SetResult();
                }
                else AcceptPreparation(attempt);
                return _close!;
            }
        }
        private async Task BeginChromeAsync(ClosePreparation attempt)
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            Task? returned = null, reported = null; var originals = new HashSet<Task>();
            void Capture(Task actual) { originals.Add(actual); _acceptedChromeSources.Add(actual); attempt.ChromeAccepted = true; }
            try { returned = attempt.Chrome!.BeginClose(); Capture(returned); }
            catch (Exception failure) { AddAppCause(attempt.BeginFailures, failure); }
            try { reported = _chrome!.OriginalClose; if (reported is not null) Capture(reported); }
            catch (Exception observation) { AddAppCause(attempt.BeginFailures, observation); }
            if (!ReferenceEquals(returned, reported))
                AddAppCause(attempt.BeginFailures, new InvalidOperationException("Retain the SAME prepared Chrome original close."));
            foreach (var cause in attempt.BeginFailures) AddAppCause(_acceptedPreparationFailures, cause);
            var failures = new List<Exception>(attempt.BeginFailures);
            foreach (var actual in originals) await JoinOriginalAppTaskAsync(actual, failures);
            ThrowAppCauses(failures);
        }
        private void AcceptPreparation(ClosePreparation attempt)
        {
            _close ??= _pendingClose ?? throw new InvalidOperationException("No retained original host drain was acquired.");
            attempt.Accepted = true; attempt.AcceptedOriginal = _close; _acceptedPreparation = attempt;
        }
    }
}
#endif
