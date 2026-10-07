using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    private sealed partial class DeveloperCaptureSource : IDeveloperProjectOriginalDirectoryRegistrationSource
    {
        private readonly HashSet<DirectoryRegistrationPreparation> _directoryRegistrations = [];
        private sealed class DirectoryRegistrationPreparation(DeveloperCaptureSource owner, DirectoryPreparation actual,
            DirectoryPreparation originalObserved) : IDeveloperProjectOriginalDirectoryRegistrationPreparation
        {
            internal DeveloperCaptureSource Owner => owner;
            internal DirectoryPreparation Actual => actual;
            internal DirectoryPreparation OriginalObserved => originalObserved;
            internal RegistrationOperation? Operation;
            public string OriginalDirectoryPath => actual.DirectoryPath;
            public Task<T> RunOriginalDirectoryRegistrationAsync<T>(IDeveloperProjectOriginalSetupStepEntry sameEntry,
                Func<Task<T>> originalMetadataFactory, CancellationToken token)
                => owner.RunDirectoryRegistration(this, sameEntry, originalMetadataFactory, token);
            public ValueTask<bool> CheckOriginalDirectoryCurrentAsync(IDeveloperProjectOriginalSetupStepEntry sameEntry,
                CancellationToken token) => new(owner.CheckDirectoryRegistration(this, sameEntry, token));
            public void RunOriginalDirectorySourceScope(Action originalCallback)
                => owner.RunDirectoryRegistrationSourceScope(this, originalCallback);
            public void RetainOriginalDirectoryTask(Task sameActualTask)
                => owner.RetainDirectoryRegistrationTask(this, sameActualTask);
            public Task CloseAndDrainAsync()
            { owner.DemandExternalDirectoryPreparationJoin(actual); return owner.CloseDirectory(actual); }
            public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
        }
        private sealed class RegistrationOperation(IDeveloperProjectOriginalSetupStepEntry entry)
        {
            internal IDeveloperProjectOriginalSetupStepEntry Entry => entry;
            internal Original Original = null!;
            internal Task Driver = null!;
            internal Task? Metadata;
            internal object? Result;
            internal bool Confirmed;
        }

        public void DemandExternalOriginalDirectoryRegistrationJoin() => DemandExternalOriginalJoin();

        public Task<IDeveloperProjectOriginalDirectoryRegistrationPreparation> PrepareOriginalDirectoryRegistrationAsync(
            DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
            IDeveloperProjectOriginalSetupPermission samePermission, DeveloperProjectSetupStep sameStep,
            IDeveloperProjectOriginalDirectoryPreparation originalObservationPreparation,
            Task originalObservationTask, IDeveloperProjectOriginalDirectoryObservation originalObservation,
            CancellationToken token)
        {
            var observed = RequireDirectoryOutcome(originalObservationPreparation, originalObservationTask, originalObservation);
            if (!ReferenceEquals(observed.Intent, sameIntent) || !ReferenceEquals(observed.Capture, sameCapture) ||
                !ReferenceEquals(observed.Permission, samePermission) || sameStep.Kind != DeveloperProjectSetupStepKind.RegisterProjectFolder ||
                sameStep.FolderId != sameIntent.ProjectFolderId || !sameIntent.Steps.Any(value => ReferenceEquals(value, sameStep)) ||
                observed.Step.Kind != DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory)
                throw new UnauthorizedAccessException("Retain the SAME reviewed project and genuine original directory observation; no copied row/path owns registration.");
            var capture = observed.Capture;
            return Start<IDeveloperProjectOriginalDirectoryRegistrationPreparation>(capture.Physical, async () =>
            {
                token.ThrowIfCancellationRequested();
                var selection = Invoke(selections);
                await Observe(capture.Physical, () => selection.RevalidateOriginalAsync(capture.Logical, sameIntent.OriginalActor, token)).ConfigureAwait(false);
                await Observe(capture.Physical, () => observed.Issuer.ValidateOriginalAsync(sameIntent, samePermission, token)).ConfigureAwait(false);
                await Observe(capture.Physical, () => RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
                var actual = new DirectoryPreparation(this, capture, sameIntent, sameStep, samePermission,
                    observed.Issuer, observed.DirectoryPath);
                var registration = new DirectoryRegistrationPreparation(this, actual, observed);
                TaskCompletionSource start;
                lock (_gate)
                {
                    if (_retiring || capture.Physical.Sealed) throw new UnauthorizedAccessException("The original captured root retired before registration lease acquisition.");
                    if (_directoryPreparations.Count >= DeveloperProjectSetupIntent.MaximumSteps)
                        throw new InvalidOperationException("Original directory lease custody is full; no descriptor was acquired.");
                    start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    actual.PreparationOwner = _executing.Value ?? throw new InvalidOperationException("No published original registration preparation exists.");
                    actual.OriginalLifetime = DrainDirectory(start.Task, actual);
                    _directoryPreparations.Add(actual); _directoryRegistrations.Add(registration);
                }
                start.TrySetResult(); var errors = new List<Exception>();
                try
                {
                    Invoke(() =>
                    {
                        token.ThrowIfCancellationRequested(); DemandCurrent(capture.Physical);
                        actual.Handle = capture.Physical.Root.OpenDirectory(actual.DirectoryPath);
                        DemandLinuxDescriptorPath(actual.Handle, actual.DirectoryPath);
                        actual.Identity = ReadLinuxIdentity(actual.Handle);
                        if (!actual.Identity.IsDirectory || !actual.Identity.SameReadVersion(observed.Identity))
                            throw new IOException("The exact observed project directory changed before the registration lease.");
                        return true;
                    });
                    return registration;
                }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                var close = ReleaseDirectory(actual);
                try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
                ThrowOriginalErrors(errors); throw new InvalidOperationException("No original directory registration lease was issued.");
            });
        }

        private bool CheckDirectoryRegistration(DirectoryRegistrationPreparation preparation,
            IDeveloperProjectOriginalSetupStepEntry entry, CancellationToken token) => Invoke(() =>
        {
            token.ThrowIfCancellationRequested();
            RegistrationOperation operation;
            lock (_gate)
            {
                if (!_directoryRegistrations.Contains(preparation) || !ReferenceEquals(preparation.Owner, this) ||
                    preparation.Operation is not { } admitted || !ReferenceEquals(admitted.Entry, entry))
                    throw new UnauthorizedAccessException("No SAME original registration metadata admission owns this native predicate.");
                operation = admitted;
            }
            if (!preparation.Actual.Permission.IsIssuedOriginalStepEntry(preparation.Actual.Step, entry))
                throw new UnauthorizedAccessException("The SAME genuine permission did not issue this held registration entry.");
            entry.DemandOriginalStepEntry(preparation.Actual.Step);
            DemandCurrent(preparation.Actual.Capture.Physical);
            var handle = preparation.Actual.Handle;
            if (handle is null || handle.IsClosed || operation.Confirmed)
                throw new UnauthorizedAccessException("The original directory metadata operation or descriptor is no longer active.");
            DemandLinuxDescriptorPath(handle, preparation.Actual.DirectoryPath);
            if (!ReadLinuxIdentity(handle).SameReadVersion(preparation.Actual.Identity))
                throw new IOException("The held original project directory changed before Files binding publication.");
            return true;
        });

        private Task<T> RunDirectoryRegistration<T>(DirectoryRegistrationPreparation preparation,
            IDeveloperProjectOriginalSetupStepEntry entry, Func<Task<T>> originalMetadataFactory, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(originalMetadataFactory);
            lock (_gate)
            {
                if (!_directoryRegistrations.Contains(preparation) || preparation.Actual.Sealed || preparation.Operation is not null)
                    throw new UnauthorizedAccessException("The original registration lease retired or was already attempted; no metadata replay is admitted.");
                var operation = new RegistrationOperation(entry);
                preparation.Operation = operation;
                return Start(preparation.Actual.Capture.Physical, async () =>
                {
                    Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
                    try
                    {
                        CheckDirectoryRegistration(preparation, entry, token);
                        Invoke(() =>
                        {
                            actual = originalMetadataFactory() ?? throw new InvalidOperationException("Original Files metadata factory returned no Task.");
                            operation.Metadata = actual;
                            Retain(preparation.Actual.Capture.Physical, actual); return true;
                        });
                    }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    if (actual is not null)
                        try { value = await actual.ConfigureAwait(false); }
                        catch (Exception error) { AddOriginalErrors(errors, actual, error); }
                    ThrowOriginalErrors(errors, actual?.IsCanceled == true && errors.Count == 1);
                    lock (_gate) { operation.Result = value; operation.Confirmed = true; }
                    return value;
                }, (original, driver) =>
                {
                    operation.Original = original; operation.Driver = driver;
                    preparation.Actual.OriginalRegistrations.Add(operation);
                });
            }
        }

        private RegistrationOperation RequireLiveDirectoryRegistrationOperation(DirectoryRegistrationPreparation preparation)
        {
            lock (_gate)
                if (_directoryRegistrations.Contains(preparation) && ReferenceEquals(preparation.Owner, this) &&
                    preparation.Operation is { } operation && Volatile.Read(ref operation.Original.Live))
                    return operation;
            throw new UnauthorizedAccessException("No live SAME original registration owns this nested source callback/Task.");
        }

        private void RunDirectoryRegistrationSourceScope(DirectoryRegistrationPreparation preparation, Action originalCallback)
        {
            ArgumentNullException.ThrowIfNull(originalCallback);
            RequireLiveDirectoryRegistrationOperation(preparation);
            Invoke(() => { originalCallback(); return true; });
        }

        private void RetainDirectoryRegistrationTask(DirectoryRegistrationPreparation preparation, Task sameActualTask)
        {
            ArgumentNullException.ThrowIfNull(sameActualTask);
            // The explicit source-issued operation survives restored execution contexts.
            // The caller enrolls the actual Task inside its finite source callback.
            var operation = RequireLiveDirectoryRegistrationOperation(preparation);
            lock (_gate) operation.Original.Sources.Add(sameActualTask);
        }

        private DirectoryRegistrationPreparation RequireDirectoryRegistration(
            IDeveloperProjectOriginalDirectoryRegistrationPreparation samePreparation,
            Task sameRegistrationTask, Task sameMetadataTask, object? sameResult)
        {
            lock (_gate)
                if (samePreparation is DirectoryRegistrationPreparation preparation && ReferenceEquals(preparation.Owner, this) &&
                    _directoryRegistrations.Contains(preparation) && preparation.Operation is { Confirmed: true } operation &&
                    ReferenceEquals(operation.Driver, sameRegistrationTask) && ReferenceEquals(operation.Metadata, sameMetadataTask) &&
                    ReferenceEquals(operation.Result, sameResult) && operation.Driver.IsCompletedSuccessfully &&
                    preparation.Actual.OriginalLifetime.IsCompletedSuccessfully)
                    return preparation;
            throw new UnauthorizedAccessException("No SAME privately retained original directory registration metadata task/result/native cleanup exists.");
        }

        public bool IsIssuedOriginalDirectoryRegistrationOutcome(IDeveloperProjectOriginalDirectoryRegistrationPreparation samePreparation,
            Task sameRegistrationTask, Task sameMetadataTask, object? sameResult)
        { try { RequireDirectoryRegistration(samePreparation, sameRegistrationTask, sameMetadataTask, sameResult); return true; }
          catch (UnauthorizedAccessException) { return false; } }

        public Task ValidateOriginalDirectoryRegistrationOutcomeAsync(IDeveloperProjectOriginalDirectoryRegistrationPreparation samePreparation,
            Task sameRegistrationTask, Task sameMetadataTask, object? sameResult, CancellationToken token)
        {
            var preparation = RequireDirectoryRegistration(samePreparation, sameRegistrationTask, sameMetadataTask, sameResult);
            return Start(preparation.Actual.Capture.Physical, async () =>
            {
                await Observe(preparation.Actual.Capture.Physical, () => sameRegistrationTask).ConfigureAwait(false);
                await Observe(preparation.Actual.Capture.Physical, () => sameMetadataTask).ConfigureAwait(false);
                await Observe(preparation.Actual.Capture.Physical, () => preparation.Actual.OriginalLifetime).ConfigureAwait(false);
                var selection = Invoke(selections);
                await Observe(preparation.Actual.Capture.Physical, () => selection.RevalidateOriginalAsync(
                    preparation.Actual.Capture.Logical, preparation.Actual.Intent.OriginalActor, token)).ConfigureAwait(false);
                await Observe(preparation.Actual.Capture.Physical, () => RevalidateOriginalCaptureAsync(preparation.Actual.Capture, token)).ConfigureAwait(false);
                SafeFileHandle? current = null; var errors = new List<Exception>();
                try
                {
                    Invoke(() =>
                    {
                        token.ThrowIfCancellationRequested(); DemandCurrent(preparation.Actual.Capture.Physical);
                        current = preparation.Actual.Capture.Physical.Root.OpenDirectory(preparation.Actual.DirectoryPath);
                        DemandLinuxDescriptorPath(current, preparation.Actual.DirectoryPath);
                        if (!ReadLinuxIdentity(current).SameReadVersion(preparation.Actual.Identity))
                            throw new IOException("The original registered directory no longer matches the retained native identity/version.");
                        RequireDirectoryRegistration(samePreparation, sameRegistrationTask, sameMetadataTask, sameResult); return true;
                    });
                }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                try { Invoke(() => { current?.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                ThrowOriginalErrors(errors); return true;
            });
        }
    }
}
