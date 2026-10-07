using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    private sealed partial class DeveloperCaptureSource : IDeveloperWorkspaceOriginalExecutionScopedBindingSource
    {
        private readonly AsyncLocal<SavedRootParentSource?> _savedRootParentSource = new();
        private readonly HashSet<SavedRootParentSource> _savedRootParentOriginals = [];
        // Only fixed privately owned stream/descriptor cleanup uses this path. A parent
        // refusal remains a reported fault, but cannot strand an already acquired handle.
        // No productive factory or unknown returned object may use the fallback.
        private T InvokeSavedRootOwnedCleanup<T>(Func<T> cleanup)
        {
            if (_savedRootParentSource.Value is not { } parent) return Invoke(cleanup);
            var attempted = false; T value = default!; var errors = new List<Exception>();
            try { parent.Invoke(() => { attempted = true; return value = cleanup(); }); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (!attempted)
            {
                var depths = _physicalSources ??= []; depths.TryGetValue(this, out var before); depths[this] = before + 1;
                try { value = cleanup(); }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                finally { if (before == 0) depths.Remove(this); else depths[this] = before; }
            }
            ThrowOriginalErrors(errors); return value;
        }
        private sealed class SavedRootParentSource(DeveloperCaptureSource owner, SavedRootParentSource? parent,
            Action<Action> callerScope, Action<Task> callerRetain)
        {
            internal SavedRootParentSource? Parent => parent;
            internal Task Driver = null!;
            internal bool Live;
            internal bool CallbackFailed;
            internal readonly List<Task> Sources = [];
            internal readonly List<SavedRootPin> Pins = [];
            internal readonly List<Exception> PublicationCauses = [];
            internal void Retain(Task actual)
            {
                lock (owner._gate) Sources.Add(actual);
                Invoke(() => { callerRetain(actual); return true; });
            }
            internal void Scope(Action source) => Invoke(() => { source(); return true; });
            internal T Invoke<T>(Func<T> source)
            {
                var depths = _physicalSources ??= []; depths.TryGetValue(owner, out var before); depths[owner] = before + 1;
                var gate = new object(); var errors = new List<Exception>();
                var thread = Environment.CurrentManagedThreadId; var phase = 1; var invoked = 0; T value = default!;
                void Record(Exception error) { lock (gate) AddOriginalErrors(errors, null, error); }
                try
                {
                    try
                    {
                        callerScope(() =>
                        {
                            if (Volatile.Read(ref phase) != 1 || Environment.CurrentManagedThreadId != thread ||
                                Interlocked.CompareExchange(ref invoked, 1, 0) != 0)
                            {
                                var refusal = new InvalidOperationException("Original kernel parent callback is expired, repeated or foreign-thread.");
                                Record(refusal); throw refusal;
                            }
                            try
                            {
                                value = source();
                                if (value is Task actual) Retain(actual);
                            }
                            catch (Exception error) { Record(error); throw; }
                        });
                    }
                    catch (Exception error) { Record(error); }
                    finally { Volatile.Write(ref phase, 0); }
                    if (Volatile.Read(ref invoked) == 0) Record(new InvalidOperationException("Original kernel parent scope did not invoke its callback."));
                    Exception[] causes; lock (gate) causes = errors.ToArray();
                    if (causes.Length != 0) { Volatile.Write(ref CallbackFailed, true); throw new AggregateException("Original kernel parent factory failed.", causes); }
                    return value;
                }
                finally { if (before == 0) depths.Remove(owner); else depths[owner] = before; }
            }
            internal void Publish(Task actual)
            {
                try { Invoke(() => { callerRetain(actual); return true; }); }
                catch (Exception error) { AddOriginalErrors(PublicationCauses, null, error); }
            }
        }
        private sealed class ParentSavedRootIssuer(IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource actual,
            IDeveloperWorkspaceOriginalExecutionScopedBindingSource scoped, SavedRootParentSource source)
            : IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource
        {
            public bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding binding) => source.Invoke(() => actual.IsIssuedOriginalBinding(binding));
            public Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, AuthenticatedResourceActor actor,
                CancellationToken token) => scoped.RevalidateOriginalWithinSourceAsync(binding, actor, source.Scope, source.Retain, token);
            public IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding binding)
                => source.Invoke(() => actual.GetOriginalExecutionScopes(binding));
            public void DemandExternalOriginalExecutionBindingJoin() => actual.DemandExternalOriginalExecutionBindingJoin();
            public IDeveloperWorkspaceOriginalExecutionDescriptorEvidence GetOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding binding)
                => source.Invoke(() => actual.GetOriginalDescriptorEvidence(binding));
            public bool IsIssuedOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding binding,
                IDeveloperWorkspaceOriginalExecutionDescriptorEvidence evidence) => source.Invoke(() => actual.IsIssuedOriginalDescriptorEvidence(binding, evidence));
        }
        public Task RevalidateOriginalWithinSourceAsync(IDeveloperWorkspaceOriginalExecutionBinding binding,
            AuthenticatedResourceActor actor, Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
            CancellationToken token) => StartSavedRootParent(async source =>
        {
            var actual = source.Invoke(() => _originalExecutionBindings?.Invoke())
                ?? throw new InvalidOperationException("The genuine saved-root issuer is unavailable.");
            if (actual is not IDeveloperWorkspaceOriginalExecutionScopedBindingSource scoped)
                throw new NotSupportedException("The original saved-root issuer lacks parent-source custody.");
            Task? raw = null; var errors = new List<Exception>();
            try { _ = source.Invoke(() => raw = scoped.RevalidateOriginalWithinSourceAsync(binding, actor, source.Scope, source.Retain, token)); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (raw is not null) try { await raw.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, raw, error); }
            if (raw?.IsCanceled == true && !Volatile.Read(ref source.CallbackFailed) && errors.Count != 0 && errors.All(ParentCancellation))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            ThrowOriginalErrors(errors); return true;
        }, originalSynchronousScope, retainOriginalTask);

        public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinWithinSourceAsync(
            IDeveloperWorkspaceOriginalExecutionBinding binding, Action<Action> originalSynchronousScope,
            Action<Task> retainOriginalTask, CancellationToken token)
            => StartSavedRootParent(source => BeginSavedRootWithinSource(binding, source, token), originalSynchronousScope, retainOriginalTask);

        private Task<IDeveloperWorkspaceOriginalExecutionCommitPin> BeginSavedRootWithinSource(
            IDeveloperWorkspaceOriginalExecutionBinding binding, SavedRootParentSource source, CancellationToken token)
        {
            var actual = Invoke(() => _originalExecutionBindings?.Invoke())
                ?? throw new InvalidOperationException("The genuine saved-root issuer is unavailable.");
            if (actual is not IDeveloperWorkspaceOriginalExecutionScopedBindingSource scoped)
                throw new NotSupportedException("The original saved-root issuer lacks nested parent custody.");
            var issuer = new ParentSavedRootIssuer(actual, scoped, source);
            if (!Invoke(() => issuer.IsIssuedOriginalBinding(binding)))
                throw new UnauthorizedAccessException("A copied/retired binding cannot acquire native custody.");
            var evidence = Invoke(() => issuer.GetOriginalDescriptorEvidence(binding));
            if (!Invoke(() => issuer.IsIssuedOriginalDescriptorEvidence(binding, evidence)))
                throw new UnauthorizedAccessException("The original descriptor evidence was substituted.");
            var preparation = RequireWorkspaceMetadataOutcome(evidence.OriginalMetadataPreparation,
                evidence.OriginalMetadataWriteTask, evidence.OriginalMetadataObservation);
            if (preparation.OriginalClose?.IsCompletedSuccessfully != true ||
                binding.WorkspaceId != preparation.Intent.WorkspaceId || binding.ProjectId != preparation.Intent.ProjectId ||
                binding.RootId != preparation.Intent.RootId || binding.WorkspaceRevision != 1 ||
                binding.CanonicalRoot != preparation.Capture.Physical.OriginalProjectRoot ||
                evidence.OriginalFilesRoot != preparation.Capture.Physical.ConfiguredRoot)
                throw new UnauthorizedAccessException("The acknowledged original root/native binding changed.");
            var pin = new SavedRootPin(this, binding, evidence, preparation);
            return Start<IDeveloperWorkspaceOriginalExecutionCommitPin>(preparation.Capture.Physical,
                () => AcquireSavedRoot(pin, issuer, token), (original, task) =>
                {
                    pin.Original = original; pin.Acquisition = task; _originalSavedRootPins.Add(pin); source.Pins.Add(pin);
                });
        }
        private Task<T> StartSavedRootParent<T>(Func<SavedRootParentSource, Task<T>> body,
            Action<Action> callerScope, Action<Task> callerRetain)
        {
            ArgumentNullException.ThrowIfNull(callerScope); ArgumentNullException.ThrowIfNull(callerRetain);
            TaskCompletionSource start; Task<T> driver; SavedRootParentSource source;
            lock (_gate)
            {
                if (_retiring || _savedRootParentOriginals.Count >= 512)
                    throw new InvalidOperationException("Original parent-scoped kernel custody is sealed or full.");
                source = new(this, _savedRootParentSource.Value, callerScope, callerRetain);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                driver = RunSavedRootParent(start.Task, source, body); source.Driver = driver; _savedRootParentOriginals.Add(source);
            }
            source.Publish(driver); start.TrySetResult(); return driver;
        }
        private async Task<T> RunSavedRootParent<T>(Task start, SavedRootParentSource source, Func<SavedRootParentSource, Task<T>> body)
        {
            await start.ConfigureAwait(false); var before = _savedRootParentSource.Value;
            _savedRootParentSource.Value = source; Volatile.Write(ref source.Live, true);
            Task<T>? actual = null; T result = default!; var errors = new List<Exception>();
            try
            {
                foreach (var error in source.PublicationCauses) AddOriginalErrors(errors, null, error);
                if (errors.Count == 0)
                {
                    try { _ = source.Invoke(() => actual = body(source)); }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    if (actual is not null) try { result = await actual.ConfigureAwait(false); }
                        catch (Exception error) { AddOriginalErrors(errors, actual, error); }
                }
                Task[] sources; lock (_gate) sources = source.Sources.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
                foreach (var raw in sources) try { await raw.ConfigureAwait(false); }
                    catch (Exception error) { AddOriginalErrors(errors, raw, error); }
                if (errors.Count != 0)
                {
                    // A real pin may have been acquired before parent scope propagation
                    // failed. It remains privately enrolled and is independently retired.
                    SavedRootPin[] pins; lock (_gate) pins = source.Pins.ToArray();
                    var closes = new List<Task>();
                    foreach (var pin in pins) try { var close = CloseSavedRoot(pin); closes.Add(close); source.Retain(close); }
                        catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    foreach (var close in closes) try { await close.ConfigureAwait(false); }
                        catch (Exception error) { AddOriginalErrors(errors, close, error); }
                }
                if (actual?.IsCanceled == true && !Volatile.Read(ref source.CallbackFailed) &&
                    sources.All(task => !task.IsFaulted) && errors.Count != 0 && errors.All(ParentCancellation))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
                ThrowOriginalErrors(errors); return result;
            }
            finally { Volatile.Write(ref source.Live, false); _savedRootParentSource.Value = before; }
        }
        private static bool ParentCancellation(Exception error) => error is AggregateException group
            ? group.InnerExceptions.Count != 0 && group.InnerExceptions.All(ParentCancellation) : error is OperationCanceledException;
        private void DemandSavedRootParentJoin()
        {
            for (var source = _savedRootParentSource.Value; source is not null; source = source.Parent)
                if (Volatile.Read(ref source.Live)) throw new InvalidOperationException("An original parent-scoped kernel driver cannot join its owner.");
        }
        private async Task DrainSavedRootParentOriginals(List<Exception> errors)
        {
            SavedRootParentSource[] sources; lock (_gate) sources = _savedRootParentOriginals.ToArray();
            foreach (var source in sources) try { await source.Driver.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalErrors(errors, source.Driver, error); }
            Task[] raw; lock (_gate) raw = sources.SelectMany(source => source.Sources).Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var actual in raw) try { await actual.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalErrors(errors, actual, error); }
        }
    }
}
