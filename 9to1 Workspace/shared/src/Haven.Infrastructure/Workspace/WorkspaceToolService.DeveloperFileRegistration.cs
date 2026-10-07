using System.Security.Cryptography;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    private sealed partial class DeveloperCaptureSource : IDeveloperProjectOriginalFileRegistrationSource
    {
        private readonly HashSet<FileRegistrationPreparation> _fileRegistrations = [];
        private sealed class FileRegistrationPreparation(DeveloperCaptureSource owner, Capture capture,
            DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSetupPermission permission,
            IDeveloperProjectOriginalSetupPermissionSource issuer, DeveloperProjectSetupStep step,
            DeveloperProjectSetupFile file, HeldFile originalCapturedFile)
            : IDeveloperProjectOriginalFileRegistrationPreparation
        {
            internal DeveloperCaptureSource Owner => owner;
            internal Capture Capture => capture;
            internal DeveloperProjectSetupIntent Intent => intent;
            internal IDeveloperProjectOriginalSetupPermission Permission => permission;
            internal IDeveloperProjectOriginalSetupPermissionSource Issuer => issuer;
            internal DeveloperProjectSetupStep Step => step;
            internal DeveloperProjectSetupFile File => file;
            internal HeldFile Captured => originalCapturedFile;
            internal SafeFileHandle? Handle;
            internal FileStream? Stream;
            internal LinuxIdentity Identity;
            internal Original PreparingOriginal = null!;
            internal Task OriginalLifetime = null!;
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly List<Task> OriginalCleanup = [];
            internal RegistrationOperation? Operation;
            internal bool Sealed, Attempted;
            internal Task? OriginalClose;
            public string OriginalFilePath => Path.Combine(capture.OriginalExistingProjectRoot, file.RelativePath);
            public Task<T> RunOriginalFileRegistrationAsync<T>(IDeveloperProjectOriginalSetupStepEntry entry,
                Func<FileStream, Task<T>> factory, CancellationToken token) => owner.RunFileRegistration(this, entry, factory, token);
            public ValueTask<bool> CheckOriginalFileCurrentAsync(IDeveloperProjectOriginalSetupStepEntry entry,
                CancellationToken token) => new(owner.CheckFileRegistration(this, entry, token));
            public void RunOriginalFileSourceScope(Action callback)
            { ArgumentNullException.ThrowIfNull(callback); owner.RequireLiveFileOperation(this); owner.Invoke(() => { callback(); return true; }); }
            public void RetainOriginalFileTask(Task actual)
            {
                ArgumentNullException.ThrowIfNull(actual);
                var operation = owner.RequireLiveFileOperation(this);
                lock (owner._gate) operation.Original.Sources.Add(actual);
            }
            public Task CloseAndDrainAsync()
            { owner.DemandExternalFilePreparationJoin(this); return owner.CloseFileRegistration(this); }
            public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
        }

        public void DemandExternalOriginalFileRegistrationJoin() => DemandExternalOriginalJoin();
        private void DemandExternalFilePreparationJoin(FileRegistrationPreparation preparation)
        {
            // This leaf joins only its own preparation/registration driver, retained
            // cleanup and native lifetime; no encompassing saved-root parent is joined.
            if (_physicalSources?.ContainsKey(this) == true)
                throw new InvalidOperationException("An actual file source callback cannot join the same preparation.");
            lock (_gate)
            {
                if (!ReferenceEquals(preparation.Owner, this) || !_fileRegistrations.Contains(preparation))
                    throw new UnauthorizedAccessException("Foreign original file registration preparation.");
                for (var current = _executing.Value; current is not null; current = current.Parent)
                    if (Volatile.Read(ref current.Live) && (ReferenceEquals(current, preparation.PreparingOriginal) ||
                        ReferenceEquals(current, preparation.Operation?.Original)))
                        throw new InvalidOperationException("A live actual file original cannot join its own preparation.");
            }
        }
        public Task<IDeveloperProjectOriginalFileRegistrationPreparation> PrepareOriginalFileRegistrationAsync(
            DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture value,
            IDeveloperProjectOriginalSetupPermission permission, DeveloperProjectSetupStep step, CancellationToken token)
        {
            if (value is not Capture capture || !IsIssuedOriginalCapture(capture, capture.Physical, capture.Logical) ||
                intent.Validate() is not null || intent.Mode != DeveloperProjectSetupMode.RegisterExisting ||
                intent.OriginalExistingProjectRoot != capture.OriginalExistingProjectRoot ||
                intent.OriginalSourceCaptureReference != capture.OriginalCaptureReference || intent.OriginalSourceDigest != capture.OriginalCaptureDigest ||
                !intent.Steps.Any(actual => ReferenceEquals(actual, step)) ||
                step.Kind is not (DeveloperProjectSetupStepKind.RegisterExistingFileMetadata or DeveloperProjectSetupStepKind.RegisterMaterialization))
                throw new UnauthorizedAccessException("Retain the SAME original existing-source capture and reviewed metadata/materialization step.");
            var file = intent.Files.SingleOrDefault(actual => actual.FileId == step.FileId && actual.ParentFolderId == step.FolderId)
                ?? throw new UnauthorizedAccessException("This original step has no exact once-created source file.");
            var captured = capture.OriginalFiles.SingleOrDefault(actual => actual.RelativePath == file.RelativePath);
            var held = capture.Held.SingleOrDefault(actual => actual.RelativePath == file.RelativePath);
            if (captured is null || held is null || captured.SizeBytes != file.SizeBytes || captured.ContentSha256 != file.ContentSha256 ||
                captured.OriginalSourceReference != file.OriginalSourceReference)
                throw new UnauthorizedAccessException("The reviewed file differs from the privately captured source; no replacement source is admitted.");
            return Start<IDeveloperProjectOriginalFileRegistrationPreparation>(capture.Physical, async () =>
            {
                var configured = Invoke(selections);
                await Observe(capture.Physical, () => configured.RevalidateOriginalAsync(capture.Logical, intent.OriginalActor, token)).ConfigureAwait(false);
                var issuer = Invoke(() => setupPermissions?.Invoke()) ?? throw new InvalidOperationException("The actual setup permission source is unavailable.");
                await Observe(capture.Physical, () => issuer.ValidateOriginalAsync(intent, permission, token)).ConfigureAwait(false);
                await Observe(capture.Physical, () => RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
                var preparation = new FileRegistrationPreparation(this, capture, intent, permission, issuer, step, file, held);
                TaskCompletionSource begin;
                lock (_gate)
                {
                    if (_retiring || capture.Physical.Sealed || _fileRegistrations.Count >= DeveloperProjectSetupIntent.MaximumSteps)
                        throw new InvalidOperationException("Original file lease admission is closed or full; no source handle was acquired.");
                    preparation.PreparingOriginal = _executing.Value ?? throw new InvalidOperationException("No actual original owns the file lease.");
                    begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    preparation.OriginalLifetime = DrainFileRegistration(begin.Task, preparation); _fileRegistrations.Add(preparation);
                }
                begin.TrySetResult(); var errors = new List<Exception>(); Task<byte[]>? hash = null;
                try
                {
                    Invoke(() =>
                    {
                        token.ThrowIfCancellationRequested(); DemandCurrent(capture.Physical);
                        preparation.Handle = capture.Physical.Root.OpenRead(preparation.OriginalFilePath);
                        preparation.Identity = ReadLinuxIdentity(preparation.Handle);
                        if (!preparation.Identity.SameReadVersion(held.Identity) || preparation.Identity.Size != (ulong)file.SizeBytes)
                            throw new IOException("The exact captured source changed before its metadata lease.");
                        preparation.Stream = new FileStream(preparation.Handle, FileAccess.Read, 4096, isAsync: false); return true;
                    });
                    Invoke(() => { hash = SHA256.HashDataAsync(preparation.Stream!, token).AsTask(); Retain(capture.Physical, hash); return true; });
                    var actualHash = await hash!.ConfigureAwait(false);
                    Invoke(() =>
                    {
                        DemandCurrent(capture.Physical); DemandLinuxDescriptorPath(preparation.Handle!, preparation.OriginalFilePath);
                        if (!ReadLinuxIdentity(preparation.Handle!).SameReadVersion(preparation.Identity) ||
                            Convert.ToHexString(actualHash).ToLowerInvariant() != file.ContentSha256)
                            throw new IOException("The actual source identity/hash changed during original metadata preparation.");
                        preparation.Stream!.Position = 0; return true;
                    });
                    return preparation;
                }
                catch (Exception error) { AddOriginalErrors(errors, hash, error); }
                var cleanup = ReleaseFileRegistration(preparation);
                try { await cleanup.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, cleanup, error); }
                ThrowOriginalErrors(errors); throw new InvalidOperationException("No original file registration lease was issued.");
            });
        }

        private RegistrationOperation RequireLiveFileOperation(FileRegistrationPreparation preparation)
        {
            lock (_gate)
                if (_fileRegistrations.Contains(preparation) && ReferenceEquals(preparation.Owner, this) &&
                    preparation.Operation is { } actual && Volatile.Read(ref actual.Original.Live)) return actual;
            throw new UnauthorizedAccessException("No live SAME original file metadata operation owns this callback/Task.");
        }
        private bool CheckFileRegistration(FileRegistrationPreparation preparation,
            IDeveloperProjectOriginalSetupStepEntry entry, CancellationToken token) => Invoke(() =>
        {
            token.ThrowIfCancellationRequested(); var operation = RequireLiveFileOperation(preparation);
            if (!ReferenceEquals(operation.Entry, entry) || operation.Confirmed ||
                !preparation.Permission.IsIssuedOriginalStepEntry(preparation.Step, entry))
                throw new UnauthorizedAccessException("The SAME privately held entry/file operation is no longer active.");
            entry.DemandOriginalStepEntry(preparation.Step); DemandCurrent(preparation.Capture.Physical);
            var handle = preparation.Handle;
            if (handle is null || handle.IsClosed) throw new UnauthorizedAccessException("The actual source handle retired.");
            DemandLinuxDescriptorPath(handle, preparation.OriginalFilePath);
            if (!ReadLinuxIdentity(handle).SameReadVersion(preparation.Identity))
                throw new IOException("The exact original file changed before metadata publication.");
            return true;
        });
        private Task<T> RunFileRegistration<T>(FileRegistrationPreparation preparation,
            IDeveloperProjectOriginalSetupStepEntry entry, Func<FileStream, Task<T>> factory, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(factory);
            lock (_gate)
            {
                if (!_fileRegistrations.Contains(preparation) || preparation.Sealed || preparation.Attempted)
                    throw new UnauthorizedAccessException("The original file lease retired or already attempted metadata; no replay is admitted.");
                preparation.Attempted = true; var operation = new RegistrationOperation(entry);
                return Start(preparation.Capture.Physical, async () =>
                {
                    Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
                    try
                    {
                        CheckFileRegistration(preparation, entry, token);
                        Invoke(() =>
                        {
                            actual = factory(preparation.Stream ?? throw new InvalidOperationException("No exact retained source stream exists."));
                            operation.Metadata = actual ?? throw new InvalidOperationException("Original file metadata factory returned no Task.");
                            Retain(preparation.Capture.Physical, actual); return true;
                        });
                    }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    if (actual is not null)
                        try { value = await actual.ConfigureAwait(false); }
                        catch (Exception error) { AddOriginalErrors(errors, actual, error); }
                    if (errors.Count == 0)
                        try { CheckFileRegistration(preparation, entry, token); }
                        catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    ThrowOriginalErrors(errors, actual?.IsCanceled == true && errors.Count == 1);
                    lock (_gate) { operation.Result = value; operation.Confirmed = true; } return value;
                }, (original, driver) =>
                { operation.Original = original; operation.Driver = driver; preparation.Operation = operation; });
            }
        }
        private Task CloseFileRegistration(FileRegistrationPreparation preparation)
        {
            TaskCompletionSource begin; Task actual;
            lock (_gate)
            {
                if (!_fileRegistrations.Contains(preparation) || !ReferenceEquals(preparation.Owner, this)) throw new UnauthorizedAccessException("Foreign file preparation.");
                if (preparation.OriginalClose is not null) return preparation.OriginalClose;
                preparation.Sealed = true; begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = DrainFileRegistrationClose(begin.Task, preparation, preparation.Operation?.Driver); preparation.OriginalClose = actual;
            }
            begin.TrySetResult(); return actual;
        }
        private async Task DrainFileRegistrationClose(Task begin, FileRegistrationPreparation preparation, Task? operation)
        {
            await begin.ConfigureAwait(false); var errors = new List<Exception>();
            if (operation is not null) try { await operation.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, operation, error); }
            var cleanup = ReleaseFileRegistration(preparation);
            try { await cleanup.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, cleanup, error); }
            ThrowOriginalErrors(errors);
        }
        private Task ReleaseFileRegistration(FileRegistrationPreparation preparation)
        { lock (_gate) preparation.Sealed = true; preparation.Release.TrySetResult(); return preparation.OriginalLifetime; }
        private async Task DrainFileRegistration(Task begin, FileRegistrationPreparation preparation)
        {
            await begin.ConfigureAwait(false); await preparation.Release.Task.ConfigureAwait(false);
            var errors = new List<Exception>(); Task? close = null;
            try
            {
                Invoke(() =>
                {
                    if (preparation.Stream is not null)
                    {
                        close = preparation.Stream.DisposeAsync().AsTask();
                        lock (_gate) preparation.OriginalCleanup.Add(close);
                    }
                    return true;
                });
            }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
            try { Invoke(() => { preparation.Handle?.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            ThrowOriginalErrors(errors);
        }
        private FileRegistrationPreparation RequireFileRegistrationOutcome(IDeveloperProjectOriginalFileRegistrationPreparation value,
            Task driver, Task metadata, object? result)
        {
            lock (_gate)
                if (value is FileRegistrationPreparation preparation && ReferenceEquals(preparation.Owner, this) && _fileRegistrations.Contains(preparation) &&
                    preparation.Operation is { Confirmed: true } operation && ReferenceEquals(operation.Driver, driver) && ReferenceEquals(operation.Metadata, metadata) &&
                    ReferenceEquals(operation.Result, result) && driver.IsCompletedSuccessfully && preparation.OriginalLifetime.IsCompletedSuccessfully)
                    return preparation;
            throw new UnauthorizedAccessException("No SAME original file metadata Task/result/handle cleanup acknowledgement exists.");
        }
        public bool IsIssuedOriginalFileRegistrationOutcome(IDeveloperProjectOriginalFileRegistrationPreparation value,
            Task driver, Task metadata, object? result)
        { try { RequireFileRegistrationOutcome(value, driver, metadata, result); return true; } catch (UnauthorizedAccessException) { return false; } }
        public Task ValidateOriginalFileRegistrationOutcomeAsync(IDeveloperProjectOriginalFileRegistrationPreparation value,
            Task driver, Task metadata, object? result, CancellationToken token)
        {
            var preparation = RequireFileRegistrationOutcome(value, driver, metadata, result);
            return Start(preparation.Capture.Physical, async () =>
            {
                var selection = Invoke(selections);
                await Observe(preparation.Capture.Physical, () => selection.RevalidateOriginalAsync(preparation.Capture.Logical, preparation.Intent.OriginalActor, token)).ConfigureAwait(false);
                await Observe(preparation.Capture.Physical, () => RevalidateOriginalCaptureAsync(preparation.Capture, token)).ConfigureAwait(false);
                Invoke(() => { token.ThrowIfCancellationRequested(); RequireFileRegistrationOutcome(value, driver, metadata, result); return true; }); return true;
            });
        }
        private Original[] OriginalFileRegistrationOwners(PhysicalSelection selection)
            => _fileRegistrations.Where(value => ReferenceEquals(value.Capture.Physical, selection))
                .SelectMany(value => value.Operation is { } operation ? new[] { value.PreparingOriginal, operation.Original } : [value.PreparingOriginal]).Distinct().ToArray();
        private async Task DrainOriginalFileRegistrations(PhysicalSelection selection, List<Exception> errors)
        {
            FileRegistrationPreparation[] preparations; lock (_gate) preparations = _fileRegistrations.Where(value => ReferenceEquals(value.Capture.Physical, selection)).ToArray();
            var closes = new List<Task>();
            foreach (var preparation in preparations) try { closes.Add(CloseFileRegistration(preparation)); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            foreach (var close in closes) try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
        }
    }
}
