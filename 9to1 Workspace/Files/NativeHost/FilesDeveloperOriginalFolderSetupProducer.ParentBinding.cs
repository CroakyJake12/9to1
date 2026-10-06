using Haven.Application;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalFolderSetupProducer
    : IDeveloperWorkspaceOriginalExecutionScopedBindingSource
{
    public Task RevalidateOriginalWithinSourceAsync(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        AuthenticatedResourceActor sameActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
        => StartWithinOriginalSource(async sources =>
        {
            var binding = sources.Invoke(() => RequireOriginalExecutionBinding(sameBinding));
            if (sameActor != binding.OriginalActor) throw new UnauthorizedAccessException("The original execution actor changed.");
            await sources.ObserveVoid(() => scopes.RevalidateOriginalDestinationWithinSourceAsync(binding.Destination,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                if (!ReferenceEquals(_originalWorkspaceStore?.Invoke(), binding.Store) ||
                    !ReferenceEquals(_originalDirectories?.Invoke(), binding.Native))
                    throw new UnauthorizedAccessException("The configured original saved-root sources changed.");
                return true;
            });
            var readStore = sources.Invoke(() => binding.Store as HavenOS.Apps.Dev.IDeveloperOriginalWorkspaceSourceReadStore)
                ?? throw new NotSupportedException("The SAME saved workspace store lacks original raw read custody.");
            var current = await sources.Observe(() => readStore.GetWithinOriginalSourceAsync(binding.WorkspaceId,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            sources.Invoke(() => { RequireSameSavedWorkspace(binding.Saved, current); return true; });
            var registration = await sources.Observe(() => binding.Destination.Workspace.Directories.ObserveOriginalExecutionRegistrationAsync(
                Guid.Parse(binding.Destination.Workspace.Configuration.ProfileId), binding.Registration.Binding.OwningAppId,
                binding.Registration.Binding.FolderId, binding.Destination.Workspace.Provider,
                binding.Physical.Intent.OriginalFilesStoreId, binding.CanonicalRoot,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                if (registration.StatePath != binding.Registration.StatePath || registration.StateJson != binding.Registration.StateJson ||
                    registration.Binding != binding.Registration.Binding)
                    throw new UnauthorizedAccessException("The exact original registration changed.");
                return true;
            });
            await sources.ObserveVoid(() => scopes.RevalidateOriginalDestinationWithinSourceAsync(binding.Destination,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            sources.Invoke(() => { token.ThrowIfCancellationRequested(); RequireOriginalExecutionBinding(binding); return true; });
            return true;
        }, originalSynchronousScope, retainOriginalTask);

    public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinWithinSourceAsync(
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
        => StartWithinOriginalSource<IDeveloperWorkspaceOriginalExecutionCommitPin>(async sources =>
        {
            var binding = sources.Invoke(() => RequireOriginalExecutionBinding(sameBinding));
            if (binding.Native is not IDeveloperWorkspaceOriginalExecutionScopedBindingSource native)
                throw new NotSupportedException("The SAME kernel pin owner lacks original parent-source custody.");
            await sources.ObserveVoid(() => RevalidateOriginalWithinSourceAsync(binding, binding.OriginalActor,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            ExecutionPin? retained = null; Task<IDeveloperWorkspaceOriginalExecutionCommitPin>? actual = null;
            var errors = new List<Exception>(); var independentFault = false;
            try
            {
                // Capture the exact raw acquisition before parent-scope propagation.
                try { sources.Invoke(() => { actual = native.AcquireOriginalExecutionPinWithinSourceAsync(binding,
                    sources.OriginalSynchronousScope, sources.RetainOriginalTask, token);
                    sources.RetainOriginalTask(actual); return true; }); }
                catch (Exception error) { independentFault = true; Add(errors, error); }
                if (actual is not null)
                    try
                    {
                        var product = await actual.ConfigureAwait(false);
                        // Historical private recognition permits only owed cleanup. This
                        // callback-free capture happens before any parent can refuse it.
                        if (binding.Native is not IDeveloperWorkspaceOriginalExecutionPinCustodySource custody ||
                            !custody.IsOwnedOriginalExecutionPin(binding, product))
                            throw new UnauthorizedAccessException("The configured native source did not own its returned pin.");
                        var original = _executing.Value ?? throw new InvalidOperationException("No actual original owns this returned pin.");
                        retained = new(this, binding, product, original);
                        lock (_gate) _originalExecutionPins.Add(retained);
                    }
                    catch (Exception error) { independentFault |= !actual.IsCanceled; AddTask(errors, actual, error); }
                if (errors.Count != 0) throw new AggregateException(errors);
                sources.Invoke(() =>
                {
                    if (retained is null || !binding.Native.IsIssuedOriginalExecutionPin(binding, retained.Native))
                        throw new UnauthorizedAccessException("No SAME privately issued native pin exists.");
                    token.ThrowIfCancellationRequested(); RequireOriginalExecutionBinding(binding); return true;
                });
                return retained!;
            }
            catch (Exception error) { if (actual is not null) AddTask(errors, actual, error); else Add(errors, error); }
            if (retained is not null)
            {
                // Request the SAME private owed close even if the productive parent scope
                // now refuses entry. Capture before retainer/publication errors; never
                // dispose an unknown interface product or reacquire execution permission.
                Task? owed = null;
                try { sources.Invoke(() => { owed = CloseOriginalExecutionPin(retained, true); Retain(owed); return true; }); }
                catch (Exception error) { independentFault = true; Add(errors, error); }
                if (owed is null)
                    try { Scope(() => { owed = CloseOriginalExecutionPin(retained, true); Retain(owed); }); }
                    catch (Exception error) { independentFault = true; Add(errors, error); }
                if (owed is not null)
                {
                    try { sources.Invoke(() => { retainOriginalTask(owed); return true; }); }
                    catch (Exception error) { independentFault = true; Add(errors, error); }
                    try { await owed.ConfigureAwait(false); }
                    catch (Exception error) { independentFault = true; AddTask(errors, owed, error); }
                }
            }
            if (actual?.IsCanceled == true && !independentFault && errors.All(value => value is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Original parent-scoped pin acquisition did not settle cleanly.", errors);
        }, originalSynchronousScope, retainOriginalTask);

    private Task<T> StartWithinOriginalSource<T>(Func<FilesOriginalReadSourceScope, Task<T>> body,
        Action<Action> parentScope, Action<Task> parentRetain)
    {
        ArgumentNullException.ThrowIfNull(parentScope); ArgumentNullException.ThrowIfNull(parentRetain);
        TaskCompletionSource start; Task<T> driver; FilesOriginalParentOperation pair;
        lock (_gate)
        {
            var parent = _executing.Value;
            if (_retiring && (parent is null || !Volatile.Read(ref parent.Live)))
                throw new InvalidOperationException("The original saved-root producer is retiring.");
            _originals.RemoveWhere(value => value.Healthy && value.Driver.IsCompletedSuccessfully);
            if (_originals.Count >= 128) throw new InvalidOperationException("Original saved-root custody is full.");
            var original = new Original(parent);
            pair = new(Scope, actual => { lock (_gate) original.Sources.Add(actual); }, parentScope, parentRetain);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            driver = Run(start.Task, original, _ => { pair.DemandPublication(); return body(pair.Sources); });
            original.Driver = driver; _originals.Add(original);
        }
        pair.Publish(driver); start.TrySetResult(); return driver;
    }
}
