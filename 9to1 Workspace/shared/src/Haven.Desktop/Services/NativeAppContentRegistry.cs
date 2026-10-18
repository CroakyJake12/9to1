using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>
/// Binds a real native surface owner to an existing canonical app registration.
/// This is an implementation binding, not a second app catalogue or a grant.
/// </summary>
internal sealed record NativeAppContentRegistration(Guid AppId, string AppKey,
    HavenAppRouteKind RouteKind, IBrowseAppContentFactory Factory);

/// <summary>
/// Native tabs and standalone presentations use this same registry. The existing
/// IModeRegistry and HavenAppRoutePolicy remain authoritative. Unknown routes have
/// no generic Chat/ModeWorkspace fallback and cannot instantiate a web edition.
/// </summary>
internal sealed class NativeAppContentRegistry : IBrowseAppContentFactory
{
    private readonly IModeRegistry _apps;
    private readonly Func<bool> _hostIsCurrent;
    private readonly IReadOnlyDictionary<string, NativeAppContentRegistration> _bindings;

    internal NativeAppContentRegistry(IModeRegistry apps, IEnumerable<NativeAppContentRegistration> bindings,
        Func<bool> hostIsCurrent)
    {
        _apps = apps ?? throw new ArgumentNullException(nameof(apps));
        _hostIsCurrent = hostIsCurrent ?? throw new ArgumentNullException(nameof(hostIsCurrent));
        var registered = new Dictionary<string, NativeAppContentRegistration>(StringComparer.Ordinal);
        foreach (var binding in bindings)
        {
            ArgumentNullException.ThrowIfNull(binding.Factory);
            if (binding.AppId == Guid.Empty || binding.AppKey != binding.AppKey.ToLowerInvariant()
                || !AppContentAddress.TryParse("9to1://" + binding.AppKey, out var route)
                || route!.Authority != binding.AppKey || !registered.TryAdd(binding.AppKey, binding))
                throw new ArgumentException("Native app bindings require unique canonical app keys and identities.", nameof(bindings));
        }
        _bindings = registered;
    }

    internal bool HasImplementation(ModeDefinition app) => app.IsEnabled
        && _bindings.TryGetValue(app.Key.ToLowerInvariant(), out var binding)
        && IsCurrentApp(app, binding);

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        DemandHost();
        if (request.TabId == Guid.Empty || request.Address.Namespace != AppContentNamespace.NativeApp
            || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("A native app requires an original tab and Home profile context.");
        if (!_bindings.TryGetValue(request.Address.Authority, out var binding))
            throw new NotSupportedException("This app does not have a registered native surface in the current host.");
        return new RegisteredSession(this, request, binding);
    }

    private void DemandHost()
    {
        if (!_hostIsCurrent()) throw new ObjectDisposedException(nameof(NativeAppContentRegistry), "The native host is retiring.");
    }

    private async Task<ModeDefinition> ResolveAsync(NativeAppContentRegistration binding, CancellationToken token)
    {
        DemandHost(); token.ThrowIfCancellationRequested();
        var app = await _apps.GetModeByIdAsync(binding.AppId, token);
        DemandHost(); token.ThrowIfCancellationRequested();
        if (app is null || !IsCurrentApp(app, binding))
            throw new UnauthorizedAccessException("The app registration is unavailable, disabled or changed. Refresh Apps to continue.");
        return app;
    }

    private static bool IsCurrentApp(ModeDefinition app, NativeAppContentRegistration binding) =>
        app.Id == binding.AppId && app.IsEnabled && Enum.IsDefined(app.InstallState)
        && string.Equals(app.Key, binding.AppKey, StringComparison.OrdinalIgnoreCase)
        && HavenAppRoutePolicy.Resolve(app).Kind == binding.RouteKind;

    private sealed class RegisteredSession : IBrowseNativeAppContentSession, IBrowseAppContentClosePreparationParticipant
    {
        private readonly NativeAppContentRegistry _registry;
        private readonly BrowseAppContentRequest _request;
        private readonly NativeAppContentRegistration _binding;
        private readonly object _gate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private IBrowseAppContentSession? _createdOwner;
        private IBrowseNativeAppContentSession? _inner;
        private Task? _initialization, _close;
        private Task<bool>? _preparation;
        private Task? _actualInitialization, _actualClose;
        private readonly HashSet<Task> _actualTasks = [];
        private IBrowseAppContentSession? _closeStartedFor;
        private volatile bool _retiring;
        private volatile bool _published;
        private volatile string _title = "Opening app";
        private volatile string _icon = "apps";

        public RegisteredSession(NativeAppContentRegistry registry, BrowseAppContentRequest request,
            NativeAppContentRegistration binding)
        { _registry = registry; _request = request; _binding = binding; }

        public AppContentAddress Address
        {
            get
            {
                if (_inner is { IsPublicationCurrent: true } inner)
                { var address = inner.Address; if (IsOwnedAddress(address)) return address; }
                return _request.Address;
            }
        }
        public string Title => IsPublicationCurrent ? _inner!.Title : _title;
        public string IconKey => IsPublicationCurrent ? _inner!.IconKey : _icon;
        public event EventHandler? StateChanged;
        public bool IsPublicationCurrent => _published && !_retiring && _registry._hostIsCurrent()
            && OriginalInitialization is { IsCompletedSuccessfully: true }
            && _inner is { IsPublicationCurrent: true } native && IsOwnedAddress(native.Address);
        public bool CanRetireOriginalView => _close is { IsCompletedSuccessfully: true }
            && (_inner is null || _inner.CanRetireOriginalView);
        public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
        public Task? OriginalClose { get { lock (_gate) return _close; } }
        public Control OriginalNativeView
        {
            get
            {
                Dispatcher.UIThread.VerifyAccess();
                if (!IsPublicationCurrent || _initialization is not { IsCompletedSuccessfully: true } || _inner is null)
                    throw new InvalidOperationException("The original native app view is not ready for publication.");
                return _inner.OriginalNativeView;
            }
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_initialization is not null) return _initialization;
                if (_retiring) throw new ObjectDisposedException(nameof(RegisteredSession));
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _initialization = InitializeDriverAsync(start.Task, cancellationToken);
                start.SetResult(); return _initialization;
            }
        }

        private async Task InitializeDriverAsync(Task start, CancellationToken token)
        {
            await start.ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            token = linked.Token;
            await OnUiAsync(async () =>
            {
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                try
                {
                var app = await WithinOriginal(() => _registry.ResolveAsync(_binding, token));
                if (_retiring) throw new ObjectDisposedException(nameof(RegisteredSession));
                _title = app.Name; _icon = app.IconKey;
                var actual = WithinOriginal(() => _binding.Factory.CreateSession(_request));
                _createdOwner = actual;
                if (actual is not IBrowseNativeAppContentSession native)
                {
                    throw new InvalidOperationException("The registered app did not supply its actual native CUI view.");
                }
                _inner = native;
                if (native.Address != _request.Address) throw new UnauthorizedAccessException("The native app changed its admitted resource address.");
                WithinOriginal(() => { native.StateChanged += OnInnerChanged; return true; });
                _actualInitialization = Retain(WithinOriginal(() => native.InitializeAsync(token)));
                var reported = native.OriginalInitialization;
                if (reported is not null) _ = Retain(reported);
                if (!ReferenceEquals(_actualInitialization, reported))
                    throw new InvalidOperationException("Retain the original native initialization task.");
                await _actualInitialization;
                await WithinOriginal(() => _registry.ResolveAsync(_binding, token));
                if (_retiring || !native.IsPublicationCurrent || !IsOwnedAddress(native.Address))
                    throw new UnauthorizedAccessException("The original app admission changed before publication.");
                _published = true;
                PublishChanged();
                }
                catch (Exception failure)
                {
                    _published = false;
                    try { if (_inner?.OriginalInitialization is { } reported) _ = Retain(reported); }
                    catch (Exception observation) { throw new AggregateException("Native initialization and original task observation failed.", failure, observation); }
                    throw;
                }
            }).ConfigureAwait(false);
        }

        // The optional token belongs to this exact wrapper and inner owner. It is
        // never an app registration, a permission grant or a reusable bool receipt.
        private Task<IBrowseAppContentClosePreparation?>? _strongPreparation;
        private RegisteredPreparation? _activePreparation;
        private Task? _acceptedOwnerClose;

        public bool SupportsReversibleClosePreparation
        {
            get
            {
                lock (_gate)
                    return _inner is { } inner && IsExactOwnerCurrent(inner, WithinOriginal(() => inner.Address))
                        && WithinOriginal(() => inner is IBrowseAppContentClosePreparationParticipant { SupportsReversibleClosePreparation: true });
            }
        }

        private bool IsExactOwnerCurrent(IBrowseNativeAppContentSession inner, AppContentAddress address) =>
            !_retiring && _published && _close is null && _registry._hostIsCurrent()
            && _initialization is { IsCompletedSuccessfully: true }
            && ReferenceEquals(_inner, inner) && ReferenceEquals(_createdOwner, inner)
            && WithinOriginal(() => inner.IsPublicationCurrent && inner.Address == address && IsOwnedAddress(address));

        public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default)
        {
            DemandExternalJoin();
            lock (_gate)
            {
                if (_strongPreparation is { } existing && (!existing.IsCompletedSuccessfully || _activePreparation is not null))
                    return existing;
                if (!SupportsReversibleClosePreparation)
                    throw new InvalidOperationException("The exact initialized native owner does not support reversible preparation.");
                var inner = _inner!;
                var attempt = new RegisteredPreparation(this, inner, WithinOriginal(() => inner.Address));
                _activePreparation = attempt;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _strongPreparation = PrepareRegisteredAsync(start.Task, attempt, cancellationToken);
                Retain(_strongPreparation); start.SetResult(); return _strongPreparation;
            }
        }

        private async Task<IBrowseAppContentClosePreparation?> PrepareRegisteredAsync(Task start,
            RegisteredPreparation attempt, CancellationToken token)
        {
            await start.ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            token = linked.Token;
            var failures = new List<Exception>();
            try
            {
                return await OnUiAsync(async () =>
                {
                    using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                    token.ThrowIfCancellationRequested();
                    lock (_gate)
                        if (!ReferenceEquals(_activePreparation, attempt) || !IsExactOwnerCurrent(attempt.Inner, attempt.Address))
                            throw new InvalidOperationException("The exact native owner changed before preparation.");
                    var participant = (IBrowseAppContentClosePreparationParticipant)attempt.Inner;
                    var actual = WithinOriginal(() => participant.PrepareCloseAsync(token))
                        ?? throw new InvalidOperationException("No actual inner preparation task was returned.");
                    attempt.Preparation = actual; _ = Retain(actual);
                    try { attempt.InnerLease = await actual; }
                    catch (Exception error) { AddOriginalCauses(failures, actual, error); ThrowRegisteredCauses(failures); }
                    if (attempt.InnerLease is null)
                    {
                        lock (_gate) if (ReferenceEquals(_activePreparation, attempt)) _activePreparation = null;
                        return null;
                    }
                    // Register the acquired lease before inspecting any of its getters.
                    token.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (!ReferenceEquals(_activePreparation, attempt) || !IsExactOwnerCurrent(attempt.Inner, attempt.Address)
                            || !WithinOriginal(() => ReferenceEquals(attempt.InnerLease.Session, attempt.Inner) && attempt.InnerLease.IsCurrent))
                            throw new InvalidOperationException("The acquired preparation is foreign or no longer current.");
                    }
                    return (IBrowseAppContentClosePreparation)attempt;
                }).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                if (!failures.Any(cause => ReferenceEquals(cause, error))) failures.Add(error);
                try { await AbortRegistered(attempt).ConfigureAwait(false); }
                catch (Exception cleanup) { AddOriginalCauses(failures, attempt.Abort, cleanup); }
                ThrowRegisteredCauses(failures); throw;
            }
        }

        private sealed class RegisteredPreparation(RegisteredSession owner, IBrowseNativeAppContentSession inner,
            AppContentAddress address) : IBrowseAppContentClosePreparation
        {
            internal readonly RegisteredSession Owner = owner;
            internal readonly IBrowseNativeAppContentSession Inner = inner;
            internal readonly AppContentAddress Address = address;
            internal IBrowseAppContentClosePreparation? InnerLease;
            internal Task? Preparation, Abort;
            internal readonly List<Task> CloseSources = [];
            internal readonly List<Exception> BeginFailures = [];
            internal bool Aborting, Accepted;
            public IBrowseAppContentSession Session => Owner;
            public bool IsCurrent { get { lock (Owner._gate) return Owner.IsPreparationCurrent(this); } }
            public Task AbortAsync() { Owner.DemandExternalJoin(); return Owner.AbortRegistered(this); }
            public Task BeginClose() { Owner.DemandExternalJoin(); return Owner.BeginRegistered(this); }
        }
        private bool IsPreparationCurrent(RegisteredPreparation attempt) =>
            ReferenceEquals(_activePreparation, attempt) && !attempt.Aborting && !attempt.Accepted
            && _strongPreparation is { IsCompletedSuccessfully: true }
            && IsExactOwnerCurrent(attempt.Inner, attempt.Address)
            && attempt.InnerLease is { } inner && WithinOriginal(() => ReferenceEquals(inner.Session, attempt.Inner) && inner.IsCurrent);

        private Task AbortRegistered(RegisteredPreparation attempt)
        {
            lock (_gate)
            {
                if (attempt.Abort is { } original) return original;
                if (attempt.Accepted) throw new InvalidOperationException("An accepted native retirement cannot be rolled back.");
                attempt.Aborting = true;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                attempt.Abort = AbortRegisteredAsync(start.Task, attempt);
                Retain(attempt.Abort); start.SetResult(); return attempt.Abort;
            }
        }
        private async Task AbortRegisteredAsync(Task start, RegisteredPreparation attempt)
        {
            await start.ConfigureAwait(false);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (attempt.InnerLease is { } inner)
            {
                var actual = WithinOriginal(inner.AbortAsync)
                    ?? throw new InvalidOperationException("No actual inner abort task was returned.");
                _ = Retain(actual); var failures = new List<Exception>();
                try { await actual.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalCauses(failures, actual, error); }
                ThrowRegisteredCauses(failures);
            }
            lock (_gate) if (ReferenceEquals(_activePreparation, attempt)) _activePreparation = null;
        }

        private Task BeginRegistered(RegisteredPreparation attempt)
        {
            lock (_gate)
            {
                if (attempt.Accepted) return _close!;
                if (!IsPreparationCurrent(attempt)) throw new InvalidOperationException("The exact registered preparation is stale or aborted.");
                // This async source executes acquisition synchronously before its first
                // await. Its original phase stays live for actual asynchronous child
                // callbacks, without leaking that phase into the external caller.
                _acceptedOwnerClose = BeginPreparedOwnerAsync(attempt);
                Retain(_acceptedOwnerClose);
                if (!attempt.Accepted)
                {
                    ThrowRegisteredCauses(attempt.BeginFailures);
                    throw new InvalidOperationException("The native owner did not accept retirement.");
                }
                _closeStartedFor = attempt.Inner;
                _retiring = true; _published = false;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseDriverAsync(start.Task); start.SetResult(); return _close;
            }
        }
        private async Task BeginPreparedOwnerAsync(RegisteredPreparation attempt)
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            Task? returned = null, reported = null;
            void Capture(Task task) { attempt.CloseSources.Add(task); Retain(task); attempt.Accepted = true; }
            try
            {
                returned = WithinOriginal(attempt.InnerLease!.BeginClose)
                    ?? throw new InvalidOperationException("No actual prepared inner close task was returned.");
                Capture(returned); _actualClose = returned;
            }
            catch (Exception failure) { attempt.BeginFailures.Add(failure); }
            try { reported = WithinOriginal(() => attempt.Inner.OriginalClose); if (reported is not null) Capture(reported); }
            catch (Exception observation)
            {
                attempt.BeginFailures.Add(observation);
                // Retain any original exposed by a subsequent supported observation;
                // the first getter failure remains a failure, never a clean receipt.
                try { reported = WithinOriginal(() => attempt.Inner.OriginalClose); if (reported is not null) Capture(reported); }
                catch (Exception recoveryObservation) { attempt.BeginFailures.Add(recoveryObservation); }
            }
            if (!ReferenceEquals(returned, reported))
                attempt.BeginFailures.Add(new InvalidOperationException("Retain the same actual prepared inner close task."));
            var failures = new List<Exception>(attempt.BeginFailures);
            foreach (var actual in attempt.CloseSources.Distinct())
                try { await actual.ConfigureAwait(false); } catch (Exception error) { AddOriginalCauses(failures, actual, error); }
            ThrowRegisteredCauses(failures);
        }
        private static void AddOriginalCauses(List<Exception> failures, Task? actual, Exception caught)
        {
            foreach (var cause in actual?.Exception is { } raw ? raw.InnerExceptions : [caught])
                if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause);
        }
        private static void ThrowRegisteredCauses(List<Exception> failures)
        {
            if (failures.Count > 0) throw new AggregateException("Actual registered preparation, abort or close did not settle cleanly.", failures);
        }

        public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
        {
            DemandExternalJoin();
            lock (_gate)
            {
                if (_close is not null) return _preparation ??= Task.FromResult(false);
                if (_preparation is not null && !_preparation.IsCompletedSuccessfully) return _preparation;
                if (_activePreparation is not null) return Task.FromResult(false);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _preparation = PrepareDriverAsync(start.Task, cancellationToken);
                start.SetResult(); return _preparation;
            }
        }

        private async Task<bool> PrepareDriverAsync(Task start, CancellationToken token)
        {
            await start.ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            token = linked.Token;
            if (SupportsReversibleClosePreparation)
            {
                var lease = await PrepareCloseAsync(token).ConfigureAwait(false);
                if (lease is null) return false;
                // A bool caller cannot own a retained token. Join its real abort
                // before reporting compatibility success, leaving edits admitted.
                await lease.AbortAsync().ConfigureAwait(false);
                return true;
            }
            return await OnUiAsync(async () =>
            {
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                token.ThrowIfCancellationRequested();
                if (_retiring || OriginalInitialization is { IsCompleted: false }) return false;
                if (_inner is not { } native) return true;
                var actual = WithinOriginal(() => native.TryPrepareToCloseAsync(token));
                _ = Retain(actual);
                return await actual;
            }).ConfigureAwait(false);
        }

        public Task CloseAndDrainAsync()
        {
            DemandExternalJoin();
            lock (_gate)
            {
                if (_close is not null) return _close;
                _retiring = true; _published = false;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CloseDriverAsync(start.Task); start.SetResult(); return _close;
            }
        }

        private async Task CloseDriverAsync(Task start)
        {
            await start.ConfigureAwait(false);
            await OnUiAsync(async () =>
            {
                var failures = new List<Exception>();
                async Task Join(Task? task)
                {
                    if (task is null) return;
                    try { await task; }
                    catch (Exception error) { AddOriginalCauses(failures, task, error); }
                }
                try { WithinOriginal(() => { _lifetime.Cancel(); return true; }); }
                catch (Exception error) { failures.Add(error); }
                // The owner's close cancels its lifetime. Joining initialization or save work
                // before starting that close can wait forever on the cancellation we withheld.
                BeginOwnerClose();
                await Join(_preparation);
                await Join(_strongPreparation);
                if (_activePreparation is { Accepted: false } abandoned)
                {
                    try { await Join(AbortRegistered(abandoned)); }
                    catch (Exception error) { AddOriginalCauses(failures, abandoned.Abort, error); }
                }
                await Join(_initialization);
                // A factory already entering acquisition when retirement began may publish
                // its owner late. It is still retained and must receive its original close.
                BeginOwnerClose();
                if (_inner is { } native)
                    try { WithinOriginal(() => { native.StateChanged -= OnInnerChanged; return true; }); }
                    catch (Exception error) { failures.Add(error); }
                Task[] originals; lock (_gate) originals = _actualTasks.Concat(_acceptedOwnerClose is { } accepted ? [accepted] : []).Distinct().ToArray();
                foreach (var actual in originals) await Join(actual);
                try { _lifetime.Dispose(); } catch (Exception error) { failures.Add(error); }
                if (failures.Count > 0) throw new AggregateException("Native app initialization or retirement did not complete cleanly.", failures);

                void BeginOwnerClose()
                {
                    if (_createdOwner is not { } owner || ReferenceEquals(_closeStartedFor, owner)) return;
                    _closeStartedFor = owner;
                    var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    Retain(CloseOwnerAsync(begin.Task, owner)); begin.SetResult();
                }
            }).ConfigureAwait(false);
        }

        private async Task CloseOwnerAsync(Task start, IBrowseAppContentSession owner)
        {
            await start;
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            List<Exception> failures = [];
            HashSet<Task> originals = [];
            void Capture(Task actual) { originals.Add(Retain(actual)); }
            try
            {
                _actualClose = WithinOriginal(owner.CloseAndDrainAsync); Capture(_actualClose);
                var reported = owner.OriginalClose;
                if (reported is not null) Capture(reported);
                if (!ReferenceEquals(_actualClose, reported))
                    throw new InvalidOperationException("Retain the original native close task.");
            }
            catch (Exception error)
            {
                failures.Add(error);
                // A throwing callback can still have acquired and published owner work.
                try { if (owner.OriginalClose is { } reported) Capture(reported); }
                catch (Exception observation) { failures.Add(observation); }
            }
            foreach (var actual in originals)
                try { await actual; } catch (Exception error) { AddOriginalCauses(failures, actual, error); }
            if (failures.Count > 0) throw new AggregateException("The native owner's original close did not settle cleanly.", failures);
        }

        private void OnInnerChanged(object? sender, EventArgs args)
        {
            if (!_published || _retiring || !ReferenceEquals(sender, _inner)) return;
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            PublishChanged();
        }

        private void PublishChanged()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Retain(completion.Task);
            try
            {
                if (_inner is null || !IsOwnedAddress(_inner.Address))
                    throw new UnauthorizedAccessException("The native owner changed its admitted app namespace or authority.");
                WithinOriginal(() => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
                if (!IsOwnedAddress(_inner.Address))
                    throw new UnauthorizedAccessException("The native owner changed its admitted app during publication.");
                completion.SetResult();
            }
            catch (Exception error) { _published = false; completion.SetException(error); throw; }
        }

        private bool IsOwnedAddress(AppContentAddress address) => address.Namespace == AppContentNamespace.NativeApp
            && address.Authority == _request.Address.Authority;
        private T WithinOriginal<T>(Func<T> callback) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, callback);
        private void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        private Task Retain(Task original)
        {
            ArgumentNullException.ThrowIfNull(original);
            lock (_gate)
            {
                _actualTasks.RemoveWhere(actual => actual.IsCompletedSuccessfully);
                _actualTasks.Add(original);
            }
            return original;
        }

        private static async Task OnUiAsync(Func<Task> action)
        {
            if (Dispatcher.UIThread.CheckAccess()) { await action(); return; }
            var actual = await Dispatcher.UIThread.InvokeAsync<Task>(action).GetTask();
            await actual.ConfigureAwait(false);
        }
        private static async Task<T> OnUiAsync<T>(Func<Task<T>> action)
        {
            if (Dispatcher.UIThread.CheckAccess()) return await action();
            var actual = await Dispatcher.UIThread.InvokeAsync<Task<T>>(action).GetTask();
            return await actual.ConfigureAwait(false);
        }
    }
}
