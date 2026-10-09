namespace Haven.Application;

public sealed partial class TaskRunConfiguredCloudAdmissionSource
{
    private sealed partial class ConfiguredLease : ITaskRunOriginalScopedCloudAdmissionLease
    {
        private sealed class Live(ConfiguredLease owner, Live? parent)
        {
            internal readonly ConfiguredLease Owner = owner;
            internal readonly Live? Parent = parent;
            internal volatile bool Active = true;
        }
        private sealed class Validation(Action<Action> scope, Action<Task> retain)
        {
            internal readonly Action<Action> Scope = scope;
            internal readonly Action<Task> Retain = retain;
            internal Task Driver = null!;
            internal readonly List<Task> Sources = [];
        }
        private static readonly AsyncLocal<Live?> CurrentScoped = new();
        [ThreadStatic] private static Live? PhysicalScoped;
        private readonly List<Validation> _originalScopedValidations = [];
        private Task? _originalScopedPermissionClose;

        public ValueTask RevalidateWithinOriginalSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            TaskCompletionSource start;
            Validation original;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                _originalScopedValidations.RemoveAll(item => item.Driver.IsCompletedSuccessfully);
                if (_originalScopedValidations.Count >= 128)
                    throw new InvalidOperationException("Retained configured-cloud validation originals require inspection.");
                original = new(scope, retain);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                original.Driver = ValidateScopedAsync(start.Task, original, token);
                _originalScopedValidations.Add(original);
            }
            start.SetResult();
            return new(original.Driver);
        }

        private async Task ValidateScopedAsync(Task start, Validation original, CancellationToken token)
        {
            await start.ConfigureAwait(false);
            var previous = CurrentScoped.Value;
            var live = new Live(this, previous);
            CurrentScoped.Value = live;
            try
            {
                token.ThrowIfCancellationRequested();
                var callbacks = new Callbacks(this, original);
                callbacks.Invoke(() => { original.Retain(original.Driver); return true; });
                await DemandConfiguredWithinSourceAsync(callbacks, token).ConfigureAwait(false);
                var permission = _permission as ITaskRunOriginalScopedCloudUsePermissionLease
                    ?? throw new InvalidOperationException("The SAME original central cloud-use lease has no scoped source port.");
                await callbacks.ReadAsync(() => permission.RevalidateWithinOriginalSourceAsync(
                    callbacks.EnterPhysicalScope, callbacks.Retain, token).AsTask()).ConfigureAwait(false);
                // Actor/configuration/privacy are freshly read after central policy validation.
                await DemandConfiguredWithinSourceAsync(callbacks, token).ConfigureAwait(false);
                callbacks.Invoke(() => true); // No final disclosure after a supported scope closes this lease.
            }
            finally { live.Active = false; CurrentScoped.Value = previous; }
        }

        private async Task DemandConfiguredWithinSourceAsync(Callbacks source, CancellationToken token)
        {
            async Task ActorAsync()
            {
                var actor = await source.ReadAsync(() => _source._actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
                if (actor != new AuthenticatedResourceActor(_owner.ActorId, _owner.ProfileId, _owner.AccountId,
                    _owner.OrganisationId, _owner.AuthenticationRevision))
                    throw new UnauthorizedAccessException("The original authenticated task owner changed.");
                token.ThrowIfCancellationRequested();
            }
            await ActorAsync().ConfigureAwait(false);
            source.Invoke(() => { if (_source._privacy.Current.LocalOnlyMode)
                throw new UnauthorizedAccessException("Current privacy policy requires local processing."); return true; });
            var current = await source.ReadAsync(() => _source._configurations.GetAsync(_configuration.Id, token)).ConfigureAwait(false);
            if (current is null || !current.IsEnabled || current.IsLocal || ConfigurationDigest(current) != ConfigurationDigest(_configuration))
                throw new UnauthorizedAccessException("The actual cloud provider configuration changed or is disabled.");
            if (current.Kind is not (Haven.Core.ModelProviderKind.OpenAI or Haven.Core.ModelProviderKind.OpenAICompatible
                or Haven.Core.ModelProviderKind.Anthropic or Haven.Core.ModelProviderKind.Gemini or Haven.Core.ModelProviderKind.OpenRouter))
                throw new NotSupportedException("This provider has no maintained configured credential-presence adapter.");
            if (!string.IsNullOrWhiteSpace(current.Endpoint) && (!Uri.TryCreate(current.Endpoint, UriKind.Absolute, out var endpoint)
                || endpoint.Scheme is not ("https" or "http") || endpoint.Scheme == "http" && !endpoint.IsLoopback))
                throw new UnauthorizedAccessException("The actual configured provider transport is invalid.");
            // The same native vault only. Never retain the secret outside this awaited local value.
            if (string.IsNullOrWhiteSpace(await source.ReadAsync(() => _source._secrets.GetAsync(current.Id, "api-key", token)).ConfigureAwait(false)))
                throw new InvalidOperationException("The actual provider credential is unavailable.");
            var final = await source.ReadAsync(() => _source._configurations.GetAsync(_configuration.Id, token)).ConfigureAwait(false);
            if (final is null || ConfigurationDigest(final) != ConfigurationDigest(_configuration))
                throw new UnauthorizedAccessException("Provider configuration changed during credential observation.");
            await ActorAsync().ConfigureAwait(false);
            source.Invoke(() => { if (_source._privacy.Current.LocalOnlyMode)
                throw new UnauthorizedAccessException("Privacy policy changed during configured cloud admission."); return true; });
        }

        private void DemandExternalOriginalScopedCloudJoin()
        {
            for (var current = PhysicalScoped; current is not null; current = current.Parent)
                if (current.Active && ReferenceEquals(current.Owner, this))
                    throw new InvalidOperationException("An original configured-cloud source cannot join its own lease close.");
            for (var current = CurrentScoped.Value; current is not null; current = current.Parent)
                if (current.Active && ReferenceEquals(current.Owner, this))
                    throw new InvalidOperationException("An original configured-cloud source cannot join its own lease close.");
        }

        private async Task CloseScopedPermissionAsync(Task start)
        {
            await start.ConfigureAwait(false);
            Validation[] originals;
            lock (_gate) originals = _originalScopedValidations.ToArray();
            var errors = new List<Exception>();
            foreach (var original in originals)
            {
                try { await original.Driver.ConfigureAwait(false); } catch (Exception cause) { AddTask(errors, original.Driver, cause); }
                Task[] sources; lock (_gate) sources = original.Sources.ToArray();
                foreach (var actual in sources)
                    try { await actual.ConfigureAwait(false); } catch (Exception cause) { AddTask(errors, actual, cause); }
            }
            Task? permissionClose = null;
            var previous = CurrentScoped.Value;
            var cleanup = new Live(this, previous);
            CurrentScoped.Value = cleanup;
            try
            {
                // The SAME actual central permission owns metadata-only Dispose. Owed
                // cleanup must not reuse a productive validation gate after it sealed.
                // Its actual Task remains retained/joined under this lease's own live
                // and physical cleanup guard; no successful-close inference is made.
                try
                {
                    var previousPhysical = PhysicalScoped;
                    var physical = new Live(this, previousPhysical);
                    PhysicalScoped = physical;
                    try
                    {
                        permissionClose = _permission.DisposeAsync().AsTask();
                        lock (_gate) _originalScopedPermissionClose = permissionClose;
                    }
                    finally { physical.Active = false; PhysicalScoped = previousPhysical; }
                }
                catch (Exception cause) { AddTask(errors, permissionClose, cause); }
                if (permissionClose is not null)
                {
                    Task sameClose;
                    lock (_gate) sameClose = _originalScopedPermissionClose
                        ?? throw new InvalidOperationException("The actual central permission close was not retained.");
                    if (!ReferenceEquals(sameClose, permissionClose))
                        throw new InvalidOperationException("The retained central permission close changed.");
                    try { await sameClose.ConfigureAwait(false); } catch (Exception cause) { AddTask(errors, sameClose, cause); }
                }
            }
            catch (Exception cause) { AddTask(errors, permissionClose, cause); }
            finally
            {
                cleanup.Active = false;
                CurrentScoped.Value = previous;
            }
            if (errors.Count != 0) throw new AggregateException("The actual scoped cloud validation and permission close failed.", errors);
        }

        private sealed class Callbacks(ConfiguredLease owner, Validation original)
        {
            internal void Retain(Task actual)
            {
                lock (owner._gate)
                    if (!original.Sources.Any(known => ReferenceEquals(known, actual))) original.Sources.Add(actual);
                original.Retain(actual); // Raw custody already enrolled before this external callback can fail.
            }
            internal void EnterPhysicalScope(Action callback)
            {
                var previous = PhysicalScoped;
                var live = new Live(owner, previous);
                PhysicalScoped = live;
                try
                {
                    original.Scope(() =>
                    {
                        lock (owner._gate) ObjectDisposedException.ThrowIf(owner._closed, owner);
                        callback();
                    });
                    lock (owner._gate) ObjectDisposedException.ThrowIf(owner._closed, owner);
                }
                finally { live.Active = false; PhysicalScoped = previous; }
            }
            internal T Invoke<T>(Func<T> callback)
            {
                var thread = Environment.CurrentManagedThreadId;
                var active = 1; var invoked = 0; T result = default!;
                var failures = new List<Exception>();
                void Record(Exception cause)
                { lock (failures) if (!failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
                Exception[] Snapshot() { lock (failures) return failures.ToArray(); }
                try
                {
                    EnterPhysicalScope(() =>
                    {
                        if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread
                            || Interlocked.Exchange(ref invoked, 1) != 0)
                        {
                            var refusal = new InvalidOperationException("The cloud source callback is inactive, foreign-thread or already consumed.");
                            Record(refusal); throw refusal;
                        }
                        try
                        {
                            lock (owner._gate) ObjectDisposedException.ThrowIf(owner._closed, owner);
                            result = callback();
                        }
                        catch (Exception cause) { Record(cause); throw; }
                    });
                    if (Snapshot() is { Length: > 0 } retained)
                        throw new AggregateException("Every actual finite cloud callback cause remains retained.", retained);
                    if (Volatile.Read(ref invoked) == 0) throw new InvalidOperationException("The finite cloud source callback was not invoked.");
                    lock (owner._gate) ObjectDisposedException.ThrowIf(owner._closed, owner);
                    return result;
                }
                catch (Exception cause)
                {
                    // A supported scope can swallow several distinct guard/factory
                    // errors or replace them with its own exception. Keep every exact
                    // original, never overwrite a slot with only the last cause.
                    Record(cause);
                    throw new AggregateException("The actual cloud callbacks and scope failed.", Snapshot());
                }
                finally { Interlocked.Exchange(ref active, 0); }
            }
            internal async Task<T> ReadAsync<T>(Func<Task<T>> factory)
            {
                Task<T>? actual = null; var errors = new List<Exception>(); T result = default!;
                try { Invoke(() => { actual = factory() ?? throw new InvalidOperationException("No actual cloud source Task was returned."); Retain(actual); return true; }); }
                catch (Exception cause) { errors.Add(cause); }
                if (actual is not null)
                    try { result = await actual.ConfigureAwait(false); }
                    catch (Exception cause)
                    {
                        if (errors.Count == 0) { if (actual.IsFaulted && actual.Exception is { } group) throw group; throw; }
                        AddTask(errors, actual, cause);
                    }
                if (errors.Count != 0) throw new AggregateException("The actual cloud source and finite scope failed.", errors);
                return actual is null ? throw new InvalidOperationException("No actual cloud source was acquired.") : result;
            }
            internal async Task ReadAsync(Func<Task> factory)
            {
                Task? actual = null; var errors = new List<Exception>();
                try { Invoke(() => { actual = factory() ?? throw new InvalidOperationException("No actual cloud policy Task was returned."); Retain(actual); return true; }); }
                catch (Exception cause) { errors.Add(cause); }
                if (actual is not null)
                    try { await actual.ConfigureAwait(false); }
                    catch (Exception cause)
                    {
                        if (errors.Count == 0) { if (actual.IsFaulted && actual.Exception is { } group) throw group; throw; }
                        AddTask(errors, actual, cause);
                    }
                if (errors.Count != 0) throw new AggregateException("The actual cloud policy and finite scope failed.", errors);
                if (actual is null) throw new InvalidOperationException("No actual cloud policy source was acquired.");
            }
        }
    }
}
