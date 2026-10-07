using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    private sealed partial class DeveloperCaptureSource : IDeveloperProjectOriginalDirectoryObservationSource
    {
        internal DeveloperCaptureSource(IDeveloperProjectOriginalReadAdmissionSource originalReads,
            Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> originalSelections)
            : this(originalReads, originalSelections, null) { }

        [ThreadStatic] private static HashSet<DeveloperCaptureSource>? _directoryDependencyVisits;
        private readonly HashSet<DirectoryPreparation> _directoryPreparations = [];

        private sealed class DirectoryPreparation(DeveloperCaptureSource owner, Capture capture,
            DeveloperProjectSetupIntent intent, DeveloperProjectSetupStep step,
            IDeveloperProjectOriginalSetupPermission permission, IDeveloperProjectOriginalSetupPermissionSource issuer,
            string directoryPath) : IDeveloperProjectOriginalDirectoryPreparation
        {
            internal DeveloperCaptureSource Owner => owner;
            internal Capture Capture => capture;
            internal DeveloperProjectSetupIntent Intent => intent;
            internal DeveloperProjectSetupStep Step => step;
            internal IDeveloperProjectOriginalSetupPermission Permission => permission;
            internal IDeveloperProjectOriginalSetupPermissionSource Issuer => issuer;
            internal string DirectoryPath => directoryPath;
            internal SafeFileHandle? Handle;
            internal LinuxIdentity Identity;
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task OriginalLifetime = null!;
            internal Task? OriginalPublicClose;
            internal Original PreparationOwner = null!;
            internal Original? ObservationOwner;
            internal Task<IDeveloperProjectOriginalDirectoryObservation>? OriginalObservationTask;
            internal DirectoryObservation? Observation;
            internal bool Sealed, Attempted, Confirmed;
            internal readonly List<RegistrationOperation> OriginalRegistrations = [];
            public Task<IDeveloperProjectOriginalDirectoryObservation> ObserveOriginalDirectoryAsync(
                IDeveloperProjectOriginalSetupStepEntry sameEntry, CancellationToken token) => owner.ObserveDirectory(this, sameEntry, token);
            public Task CloseAndDrainAsync()
            {
                owner.DemandExternalOriginalDirectoryJoin();
                return owner.CloseDirectory(this);
            }
            public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
        }

        private sealed class DirectoryObservation(DirectoryPreparation preparation) : IDeveloperProjectOriginalDirectoryObservation
        {
            internal DirectoryPreparation Preparation => preparation;
            public string OriginalDirectoryPath => preparation.DirectoryPath;
        }

        public void DemandExternalOriginalDirectoryJoin() => DemandExternalOriginalJoin();

        private void DemandOriginalDirectoryDependencies()
        {
            // The whole owner's live/physical guard has already run. Recursive dependency
            // traversal may visit each issuer once, never skip that genuine owner guard.
            var visited = _directoryDependencyVisits ??= [];
            if (!visited.Add(this)) return;
            try
            {
                IDeveloperProjectOriginalSetupPermissionSource[] issuers;
                lock (_gate) issuers = _directoryPreparations.Select(value => value.Issuer).Concat(_fileRegistrations.Select(value => value.Issuer)).Distinct().ToArray();
                foreach (var issuer in issuers) Invoke(() => { issuer.DemandExternalOriginalSetupJoin(); return true; });
            }
            finally { visited.Remove(this); }
        }

        public Task<IDeveloperProjectOriginalDirectoryPreparation> PrepareOriginalDirectoryAsync(
            DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
            IDeveloperProjectOriginalSetupPermission samePermission, DeveloperProjectSetupStep sameStep,
            CancellationToken token)
        {
            if (sameCapture is not Capture capture || !IsIssuedOriginalCapture(capture, capture.Physical, capture.Logical))
                throw new UnauthorizedAccessException("Retain the SAME genuine kernel source capture.");
            return Start<IDeveloperProjectOriginalDirectoryPreparation>(capture.Physical, async () =>
            {
                token.ThrowIfCancellationRequested();
                if (sameIntent.Validate() is not null || sameIntent.Mode != DeveloperProjectSetupMode.RegisterExisting ||
                    sameIntent.OriginalExistingProjectRoot != capture.OriginalExistingProjectRoot ||
                    sameIntent.OriginalSourceCaptureReference != capture.OriginalCaptureReference ||
                    sameIntent.OriginalSourceDigest != capture.OriginalCaptureDigest ||
                    !sameIntent.Steps.Any(value => ReferenceEquals(value, sameStep)))
                    throw new UnauthorizedAccessException("The once-reviewed existing-directory plan does not own this capture.");
                string relative;
                if (sameStep.Kind == DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory && sameStep.FolderId == sameIntent.ProjectFolderId)
                    relative = "";
                else if (sameStep.Kind == DeveloperProjectSetupStepKind.ObserveExistingChildDirectory && sameStep.FolderId is { } child)
                {
                    relative = sameIntent.Folders.Single(value => value.FolderId == child).RelativePath;
                    if (!capture.OriginalFolderPaths.Contains(relative, StringComparer.Ordinal))
                        throw new UnauthorizedAccessException("No actual captured child directory owns this step.");
                }
                else throw new NotSupportedException("Only exact existing-directory observation steps are supported; no create or copy was attempted.");
                var issuer = Invoke(() => setupPermissions?.Invoke()) ??
                    throw new InvalidOperationException("The SAME configured genuine Home setup issuer is required.");
                // These actual Home/Files/profile reads precede the held Home entry. No
                // Home lease is held while the owning selection source revalidates.
                var selection = Invoke(selections);
                await Observe(capture.Physical, () => selection.RevalidateOriginalAsync(capture.Logical, sameIntent.OriginalActor, token)).ConfigureAwait(false);
                await Observe(capture.Physical, () => issuer.ValidateOriginalAsync(sameIntent, samePermission, token)).ConfigureAwait(false);
                await Observe(capture.Physical, () => RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
                var preparation = new DirectoryPreparation(this, capture, sameIntent, sameStep, samePermission, issuer,
                    relative.Length == 0 ? capture.OriginalExistingProjectRoot : Path.Combine(capture.OriginalExistingProjectRoot, relative));
                TaskCompletionSource start;
                lock (_gate)
                {
                    if (_retiring || capture.Physical.Sealed) throw new UnauthorizedAccessException("The genuine captured root is retiring.");
                    if (_directoryPreparations.Count >= DeveloperProjectSetupIntent.MaximumSteps)
                        throw new InvalidOperationException("Original directory handle/result custody is full; no descriptor was acquired.");
                    start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    preparation.PreparationOwner = _executing.Value ?? throw new InvalidOperationException("No actual published original owns directory preparation.");
                    preparation.OriginalLifetime = DrainDirectory(start.Task, preparation);
                    _directoryPreparations.Add(preparation); // publish whole owner before acquisition
                }
                start.TrySetResult();
                var errors = new List<Exception>();
                try
                {
                    Invoke(() =>
                    {
                        DemandCurrent(capture.Physical);
                        preparation.Handle = capture.Physical.Root.OpenDirectory(preparation.DirectoryPath);
                        preparation.Identity = ReadLinuxIdentity(preparation.Handle);
                        DemandLinuxDescriptorPath(preparation.Handle, preparation.DirectoryPath);
                        if (!preparation.Identity.IsDirectory) throw new UnauthorizedAccessException("The actual captured path is not a directory.");
                        return true;
                    });
                    return preparation;
                }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                var close = ReleaseDirectory(preparation);
                try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
                ThrowOriginalErrors(errors); throw new InvalidOperationException("No original directory preparation was issued.");
            });
        }

        private Task CloseDirectory(DirectoryPreparation preparation)
        {
            TaskCompletionSource start; Task result;
            lock (_gate)
            {
                if (!_directoryPreparations.Contains(preparation) || !ReferenceEquals(preparation.Owner, this))
                    throw new UnauthorizedAccessException("Foreign original directory preparation.");
                if (preparation.OriginalPublicClose is not null) return preparation.OriginalPublicClose;
                preparation.Sealed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                result = DrainDirectoryPublicClose(start.Task, preparation,
                    (preparation.OriginalObservationTask is { } observation ? new Task[] { observation } : [])
                        .Concat(preparation.OriginalRegistrations.Select(value => value.Driver)).ToArray());
                preparation.OriginalPublicClose = result;
            }
            start.TrySetResult(); return result;
        }

        private async Task DrainDirectoryPublicClose(Task start, DirectoryPreparation preparation, Task[] actualObservations)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>();
            // Public whole close retains/joins the admitted observation before retiring
            // its handle. Internal observation cleanup joins only native lifetime below.
            foreach (var actualObservation in actualObservations)
                try { await actualObservation.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalErrors(errors, actualObservation, error); }
            Task? actualCleanup = null;
            try { actualCleanup = ReleaseDirectory(preparation); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (actualCleanup is not null)
                try { await actualCleanup.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalErrors(errors, actualCleanup, error); }
            ThrowOriginalErrors(errors);
        }

        private Task ReleaseDirectory(DirectoryPreparation preparation)
        {
            lock (_gate)
            {
                if (!_directoryPreparations.Contains(preparation) || !ReferenceEquals(preparation.Owner, this))
                    throw new UnauthorizedAccessException("Foreign original directory preparation.");
                preparation.Sealed = true;
            }
            preparation.Release.TrySetResult();
            return preparation.OriginalLifetime;
        }

        private async Task DrainDirectory(Task start, DirectoryPreparation preparation)
        {
            await start.ConfigureAwait(false);
            await preparation.Release.Task.ConfigureAwait(false);
            // Actual native handle disposal runs under the same finite physical guard.
            // This original lifetime task, not the request TCS, is the joined cleanup.
            Invoke(() => { preparation.Handle?.Dispose(); return true; });
        }

        private Task<IDeveloperProjectOriginalDirectoryObservation> ObserveDirectory(DirectoryPreparation preparation,
            IDeveloperProjectOriginalSetupStepEntry entry, CancellationToken token)
        {
            lock (_gate)
            {
                if (!_directoryPreparations.Contains(preparation) || preparation.Sealed || preparation.Attempted)
                    throw new UnauthorizedAccessException("The original directory preparation is retired or already attempted.");
                preparation.Attempted = true;
                return Start<IDeveloperProjectOriginalDirectoryObservation>(preparation.Capture.Physical, async () =>
                {
                var errors = new List<Exception>(); DirectoryObservation? observation = null;
                try
                {
                    token.ThrowIfCancellationRequested();
                    Invoke(() =>
                    {
                        if (!preparation.Permission.IsIssuedOriginalStepEntry(preparation.Step, entry))
                            throw new UnauthorizedAccessException("The SAME previously validated Home permission did not issue this held entry.");
                        entry.DemandOriginalStepEntry(preparation.Step); return true;
                    });
                    if (!await Observe(preparation.Capture.Physical, () => entry.CheckOriginalStepCommitAsync(preparation.Step, token).AsTask()).ConfigureAwait(false))
                        throw new UnauthorizedAccessException("The actual already-held Home entry is no longer current.");
                    Invoke(() =>
                    {
                        token.ThrowIfCancellationRequested(); entry.DemandOriginalStepEntry(preparation.Step);
                        // Public close seals future admission but joins this already
                        // admitted original before releasing its retained descriptor.
                        if (preparation.Handle is null || preparation.Handle.IsClosed)
                            throw new UnauthorizedAccessException("The actual directory descriptor retired before native observation.");
                        DemandCurrent(preparation.Capture.Physical);
                        DemandLinuxDescriptorPath(preparation.Handle, preparation.DirectoryPath);
                        if (!ReadLinuxIdentity(preparation.Handle).SameReadVersion(preparation.Identity))
                            throw new IOException("The exact existing directory changed before registration observation.");
                        observation = new(preparation); return true;
                    });
                }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                var close = ReleaseDirectory(preparation);
                try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
                ThrowOriginalErrors(errors);
                lock (_gate) { preparation.Observation = observation; preparation.Confirmed = true; }
                return observation!;
                }, (original, driver) =>
                {
                    preparation.ObservationOwner = original;
                    preparation.OriginalObservationTask = driver;
                });
            }
        }

        private DirectoryPreparation RequireDirectoryOutcome(IDeveloperProjectOriginalDirectoryPreparation samePreparation,
            Task sameActual, IDeveloperProjectOriginalDirectoryObservation sameObservation)
        {
            lock (_gate)
                if (samePreparation is DirectoryPreparation preparation && ReferenceEquals(preparation.Owner, this) &&
                    _directoryPreparations.Contains(preparation) && preparation.Confirmed &&
                    ReferenceEquals(preparation.OriginalObservationTask, sameActual) &&
                    ReferenceEquals(preparation.Observation, sameObservation) && preparation.OriginalLifetime.IsCompletedSuccessfully)
                    return preparation;
            throw new UnauthorizedAccessException("No privately acknowledged SAME kernel directory Task/result/cleanup exists.");
        }

        public bool IsIssuedOriginalDirectoryOutcome(IDeveloperProjectOriginalDirectoryPreparation samePreparation,
            Task sameActual, IDeveloperProjectOriginalDirectoryObservation sameObservation)
        { try { RequireDirectoryOutcome(samePreparation, sameActual, sameObservation); return true; } catch (UnauthorizedAccessException) { return false; } }

        public Task ValidateOriginalDirectoryOutcomeAsync(IDeveloperProjectOriginalDirectoryPreparation samePreparation,
            Task sameActual, IDeveloperProjectOriginalDirectoryObservation sameObservation, CancellationToken token)
        {
            var preparation = RequireDirectoryOutcome(samePreparation, sameActual, sameObservation);
            return Start(preparation.Capture.Physical, async () =>
            {
                await Observe(preparation.Capture.Physical, () => preparation.OriginalObservationTask!).ConfigureAwait(false);
                await Observe(preparation.Capture.Physical, () => preparation.OriginalLifetime).ConfigureAwait(false);
                var selection = Invoke(selections);
                await Observe(preparation.Capture.Physical, () => selection.RevalidateOriginalAsync(preparation.Capture.Logical, preparation.Intent.OriginalActor, token)).ConfigureAwait(false);
                await Observe(preparation.Capture.Physical, () => RevalidateOriginalCaptureAsync(preparation.Capture, token)).ConfigureAwait(false);
                SafeFileHandle? current = null; var errors = new List<Exception>();
                try
                {
                    Invoke(() =>
                    {
                        token.ThrowIfCancellationRequested(); DemandCurrent(preparation.Capture.Physical);
                        current = preparation.Capture.Physical.Root.OpenDirectory(preparation.DirectoryPath);
                        DemandLinuxDescriptorPath(current, preparation.DirectoryPath);
                        if (!ReadLinuxIdentity(current).SameReadVersion(preparation.Identity))
                            throw new IOException("The SAME directory observation no longer matches the original native identity/version.");
                        RequireDirectoryOutcome(samePreparation, sameActual, sameObservation); return true;
                    });
                }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                try { Invoke(() => { current?.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                ThrowOriginalErrors(errors); return true;
            });
        }

        private Original[] OriginalDirectoryOwners(PhysicalSelection physical)
            => _directoryPreparations.Where(value => ReferenceEquals(value.Capture.Physical, physical))
                .SelectMany(value => (value.ObservationOwner is { } observation ? new[] { value.PreparationOwner, observation } : [value.PreparationOwner])
                    .Concat(value.OriginalRegistrations.Select(registration => registration.Original)))
                .Distinct().ToArray();

        private async Task DrainOriginalDirectoryPreparations(PhysicalSelection physical, List<Exception> errors)
        {
            DirectoryPreparation[] preparations;
            lock (_gate) preparations = _directoryPreparations.Where(value => ReferenceEquals(value.Capture.Physical, physical)).ToArray();
            var closes = new List<Task>();
            foreach (var preparation in preparations)
                try { closes.Add(CloseDirectory(preparation)); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            foreach (var close in closes)
                try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
        }
    }
}
