using System.Text.Json;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>Same physical capture/metadata owner with the configured private saved-root
    /// issuer. No alternate registry/store/root is constructed. The issuer is lazy solely to
    /// close the real source composition cycle; absence refuses original execution pins.</summary>
    public IDeveloperProjectOriginalPhysicalCaptureSource CreateOriginalDeveloperCaptureSource(
        IDeveloperProjectOriginalReadAdmissionSource reads,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> selections,
        Func<IDeveloperProjectOriginalSetupPermissionSource> permissions,
        Func<IDeveloperProjectOriginalWorkspaceMetadataStore> store,
        Func<IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource> bindings)
        => new DeveloperCaptureSource(reads, selections, permissions, store, bindings);

    private sealed partial class DeveloperCaptureSource : IDeveloperWorkspaceOriginalExecutionCommitBindingSource
    {
        private readonly Func<IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource>? _originalExecutionBindings;
        private readonly HashSet<SavedRootPin> _originalSavedRootPins = [];
        private readonly HashSet<SavedRootAcquisition> _originalSavedRootAcquisitions = [];
        private readonly AsyncLocal<SavedRootAcquisition?> _savedRootAcquiring = new();
        [ThreadStatic] private static HashSet<DeveloperCaptureSource>? _checkingSavedRootDependencies;
        internal DeveloperCaptureSource(IDeveloperProjectOriginalReadAdmissionSource reads,
            Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> selections,
            Func<IDeveloperProjectOriginalSetupPermissionSource> permissions,
            Func<IDeveloperProjectOriginalWorkspaceMetadataStore> store,
            Func<IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource> bindings)
            : this(reads, selections, permissions, store)
            => _originalExecutionBindings = bindings ?? throw new ArgumentNullException(nameof(bindings));

        private sealed class SavedRootAcquisition(SavedRootAcquisition? parent)
        {
            internal SavedRootAcquisition? Parent => parent;
            internal bool Live;
            internal Task<IDeveloperWorkspaceOriginalExecutionCommitPin> Driver = null!;
            internal readonly List<Task> Sources = [];
        }

        private sealed class SavedRootPin(DeveloperCaptureSource owner, IDeveloperWorkspaceOriginalExecutionBinding binding,
            IDeveloperWorkspaceOriginalExecutionDescriptorEvidence evidence, WorkspaceMetadataPreparation preparation)
            : IDeveloperWorkspaceOriginalExecutionCommitPin
        {
            internal DeveloperCaptureSource Owner => owner;
            internal IDeveloperWorkspaceOriginalExecutionBinding Binding => binding;
            internal IDeveloperWorkspaceOriginalExecutionDescriptorEvidence Evidence => evidence;
            internal WorkspaceMetadataPreparation Preparation => preparation;
            internal Original Original = null!;
            internal Task<IDeveloperWorkspaceOriginalExecutionCommitPin> Acquisition = null!;
            internal bool Published, Sealed;
            internal Task? Close;
            internal readonly List<Task> OriginalCleanup = [];
            internal OriginalLinuxPathLease? MetadataRoot, FilesRoot;
            internal DeveloperWindowsRootLease? WindowsMetadataRoot, WindowsFilesRoot;
            internal DeveloperWindowsIdentity? WindowsMetadataIdentity, WindowsRegistrationIdentity, WindowsWorkingIdentity;
            internal string? WindowsSid;
            internal SafeFileHandle? MetadataHandle, RegistrationHandle, WorkingRoot;
            internal LinuxIdentity MetadataIdentity, RegistrationIdentity, WorkingIdentity;
            internal readonly object NativeGate = new();
            public void DemandOriginalExecutionBinding() => owner.DemandSavedRoot(this);
            public ValueTask DisposeAsync() => new(owner.CloseSavedRoot(this));
        }

        public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalExecutionPinAsync(
            IDeveloperWorkspaceOriginalExecutionBinding binding, CancellationToken token)
        {
            TaskCompletionSource start; Task<IDeveloperWorkspaceOriginalExecutionCommitPin> driver;
            lock (_gate)
            {
                if (_retiring || _originalSavedRootAcquisitions.Count >= 512)
                    throw new InvalidOperationException("Original saved-root acquisition custody is sealed or full.");
                var original = new SavedRootAcquisition(_savedRootAcquiring.Value);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                driver = RunSavedRootAcquisition(start.Task, original, binding, token);
                original.Driver = driver; _originalSavedRootAcquisitions.Add(original);
            }
            start.TrySetResult(); return driver;
        }
        private async Task<IDeveloperWorkspaceOriginalExecutionCommitPin> RunSavedRootAcquisition(Task start,
            SavedRootAcquisition original, IDeveloperWorkspaceOriginalExecutionBinding binding, CancellationToken token)
        {
            await start.ConfigureAwait(false); var previous = _savedRootAcquiring.Value;
            _savedRootAcquiring.Value = original; Volatile.Write(ref original.Live, true);
            Task<IDeveloperWorkspaceOriginalExecutionCommitPin>? actual = null;
            try
            {
                Invoke(() => { actual = BeginSavedRoot(binding, token); lock (_gate) original.Sources.Add(actual); return true; });
                try { return await actual!.ConfigureAwait(false); }
                catch (Exception) when (actual!.IsFaulted) { throw actual.Exception!; }
            }
            finally { Volatile.Write(ref original.Live, false); _savedRootAcquiring.Value = previous; }
        }
        private Task<IDeveloperWorkspaceOriginalExecutionCommitPin> BeginSavedRoot(
            IDeveloperWorkspaceOriginalExecutionBinding binding, CancellationToken token)
        {
            var issuer = Invoke(() => _originalExecutionBindings?.Invoke())
                ?? throw new InvalidOperationException("The genuine saved-root issuer is unavailable.");
            if (!Invoke(() => issuer.IsIssuedOriginalBinding(binding)))
                throw new UnauthorizedAccessException("A public/copied binding cannot acquire original descriptor custody.");
            var evidence = Invoke(() => issuer.GetOriginalDescriptorEvidence(binding));
            if (!Invoke(() => issuer.IsIssuedOriginalDescriptorEvidence(binding, evidence)))
                throw new UnauthorizedAccessException("The original saved-root descriptor evidence was substituted.");
            var preparation = RequireWorkspaceMetadataOutcome(evidence.OriginalMetadataPreparation,
                evidence.OriginalMetadataWriteTask, evidence.OriginalMetadataObservation);
            if (preparation.OriginalClose?.IsCompletedSuccessfully != true ||
                binding.WorkspaceId != preparation.Intent.WorkspaceId || binding.ProjectId != preparation.Intent.ProjectId ||
                binding.RootId != preparation.Intent.RootId || binding.WorkspaceRevision != 1 ||
                binding.CanonicalRoot != preparation.Capture.Physical.OriginalProjectRoot ||
                evidence.OriginalFilesRoot != preparation.Capture.Physical.ConfiguredRoot)
                throw new UnauthorizedAccessException("The exact acknowledged workspace/root/native cleanup binding changed.");
            var pin = new SavedRootPin(this, binding, evidence, preparation);
            return Start<IDeveloperWorkspaceOriginalExecutionCommitPin>(preparation.Capture.Physical,
                () => AcquireSavedRoot(pin, issuer, token), (original, task) =>
                {
                    pin.Original = original; pin.Acquisition = task; _originalSavedRootPins.Add(pin);
                });
        }
        private async Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireSavedRoot(SavedRootPin pin,
            IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource issuer, CancellationToken token)
        {
            var errors = new List<Exception>(); var actual = pin.Preparation.Capture.Physical; Task? observedStage = null;
            try
            {
                await Observe(actual, () => observedStage = issuer.RevalidateOriginalAsync(pin.Binding, pin.Binding.OriginalActor, token)).ConfigureAwait(false);
                Invoke(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(_originalWorkspaceStore?.Invoke(), pin.Preparation.Store) ||
                        !issuer.IsIssuedOriginalDescriptorEvidence(pin.Binding, pin.Evidence))
                        throw new UnauthorizedAccessException("The actual saved store/root source changed before descriptor acquisition.");
                    // Every acquired native product is assigned to its retained private pin
                    // before the next acquisition/effect can throw.
                    if (actual.WindowsRoot is not null) { CaptureWindowsSavedRoot(pin); return true; }
                    pin.MetadataRoot = new OriginalLinuxPathLease(pin.Preparation.Ancestor);
                    pin.FilesRoot = new OriginalLinuxPathLease(pin.Evidence.OriginalFilesRoot);
                    pin.MetadataHandle = pin.MetadataRoot.OpenRead(SavedMetadataPath(pin));
                    pin.MetadataIdentity = ReadLinuxIdentity(pin.MetadataHandle);
                    if (!pin.MetadataIdentity.SameReadVersion(((WorkspaceMetadataObservation)pin.Evidence.OriginalMetadataObservation).Identity))
                        throw new IOException("The current saved workspace inode/version differs from its original native acknowledgement.");
                    pin.RegistrationHandle = pin.FilesRoot.OpenRead(pin.Evidence.OriginalRegistrationStatePath);
                    pin.RegistrationIdentity = ReadLinuxIdentity(pin.RegistrationHandle);
                    pin.WorkingRoot = pin.FilesRoot.OpenDirectory(pin.Binding.CanonicalRoot);
                    pin.WorkingIdentity = ReadLinuxIdentity(pin.WorkingRoot);
                    if (!pin.WorkingIdentity.IsDirectory || !pin.WorkingIdentity.SameFile(actual.ProjectIdentity))
                        throw new UnauthorizedAccessException("The actual registered working root was replaced.");
                    return true;
                });
                var metadata = await Observe(actual, () => { var task = ReadPinnedDocument(pin, pin.MetadataHandle!, token); observedStage = task; return task; }).ConfigureAwait(false);
                if (!metadata.AsSpan().SequenceEqual(((WorkspaceMetadataObservation)pin.Evidence.OriginalMetadataObservation).Operation.Document))
                    throw new IOException("The complete actual saved document differs from its original metadata bytes.");
                var registration = await Observe(actual, () => { var task = ReadPinnedDocument(pin, pin.RegistrationHandle!, token); observedStage = task; return task; }).ConfigureAwait(false);
                using (var document = JsonDocument.Parse(registration))
                using (var expected = JsonDocument.Parse(pin.Evidence.OriginalRegistrationStateJson))
                    if (!document.RootElement.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1 ||
                        !document.RootElement.TryGetProperty("state", out var state) || !JsonElement.DeepEquals(state, expected.RootElement))
                        throw new UnauthorizedAccessException("The current native registration document differs from the observed actual Files state.");
                await Observe(actual, () => observedStage = issuer.RevalidateOriginalAsync(pin.Binding, pin.Binding.OriginalActor, token)).ConfigureAwait(false);
                Invoke(() =>
                {
                    token.ThrowIfCancellationRequested(); DemandSavedRootNative(pin);
                    if (!issuer.IsIssuedOriginalDescriptorEvidence(pin.Binding, pin.Evidence))
                        throw new UnauthorizedAccessException("The original saved-root binding retired before pin publication.");
                    lock (_gate)
                    {
                        if (_retiring || actual.Sealed) throw new UnauthorizedAccessException("The kernel root retired before saved-pin publication.");
                        pin.Published = true;
                    }
                    return true;
                });
                return pin;
            }
            catch (Exception error) { AddOriginalErrors(errors, observedStage, error); }
            // Internal acquisition cleanup must not join the acquisition driver itself.
            Task? close = null;
            try { close = CloseSavedRoot(pin, acquisitionCleanup: true); Retain(actual, close); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
            ThrowOriginalErrors(errors, observedStage?.IsCanceled == true); throw new InvalidOperationException("No actual saved-root descriptor pin was published.");
        }
        private async Task<byte[]> ReadPinnedDocument(SavedRootPin pin, SafeFileHandle handle, CancellationToken token)
        {
            var original = pin.Original; FileStream? stream = null; SafeFileHandle? borrowed = null;
            byte[]? bytes = null; Task? actual = null; var errors = new List<Exception>();
            try
            {
                Invoke(() =>
                {
                    if (pin.WindowsSid is not null)
                    {
                        var windows = ReadDeveloperWindowsIdentity(handle); DemandDeveloperWindowsOwner(handle, pin.WindowsSid);
                        if (!windows.IsRegular || windows.Links != 1 || windows.Size is 0 or > 1024 * 1024)
                            throw new InvalidDataException("The original Windows saved/registration document exceeds its bounded physical contract.");
                        bytes = new byte[checked((int)windows.Size)];
                    }
                    else
                    {
                    var identity = ReadLinuxIdentity(handle);
                    if (!identity.IsRegular || identity.Links != 1 || identity.Size is 0 or > 1024 * 1024)
                        throw new InvalidDataException("The original saved/registration document exceeds the bounded regular-file contract.");
                    bytes = new byte[checked((int)identity.Size)];
                    }
                    borrowed = new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false);
                    stream = new FileStream(borrowed, FileAccess.Read, 16 * 1024, isAsync: false); return true;
                });
                var offset = 0;
                while (offset < bytes!.Length)
                {
                    Task<int>? read = null;
                    Invoke(() => { read = stream!.ReadAsync(bytes.AsMemory(offset), token).AsTask(); lock (_gate) original.Sources.Add(read); _savedRootParentSource.Value?.Retain(read); return true; });
                    actual = read; var count = await read!.ConfigureAwait(false);
                    if (count == 0) throw new EndOfStreamException("The actual descriptor read ended before the retained document size."); offset += count;
                }
            }
            catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            finally
            {
                Task? close = null;
                try { InvokeSavedRootOwnedCleanup(() => { if (stream is not null) { close = stream.DisposeAsync().AsTask(); lock (_gate) original.Sources.Add(close); _savedRootParentSource.Value?.Retain(close); } return true; }); }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
                try { borrowed?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors, actual?.IsCanceled == true);
            return bytes ?? throw new InvalidDataException("No original saved-root bytes were observed.");
        }
        private static string SavedMetadataPath(SavedRootPin pin) => Path.Combine(pin.Preparation.DirectoryPath,
            pin.Binding.WorkspaceId.ToString("N") + ".json");
        private static void DemandSavedRootNative(SavedRootPin pin)
        {
            lock (pin.NativeGate)
            {
                if (pin.Sealed) throw new ObjectDisposedException("original saved-root descriptor pin");
                if (pin.WindowsSid is not null) { DemandWindowsSavedRoot(pin); return; }
                pin.MetadataRoot!.DemandCurrent(); pin.FilesRoot!.DemandCurrent();
                DemandLinuxDescriptorPath(pin.MetadataHandle!, SavedMetadataPath(pin));
                DemandLinuxDescriptorPath(pin.RegistrationHandle!, pin.Evidence.OriginalRegistrationStatePath);
                DemandLinuxDescriptorPath(pin.WorkingRoot!, pin.Binding.CanonicalRoot);
                var metadata = ReadLinuxIdentity(pin.MetadataHandle!); var registration = ReadLinuxIdentity(pin.RegistrationHandle!);
                var working = ReadLinuxIdentity(pin.WorkingRoot!);
                if (!metadata.IsRegular || metadata.Links != 1 || !metadata.SameReadVersion(pin.MetadataIdentity) ||
                    !registration.IsRegular || registration.Links != 1 || !registration.SameReadVersion(pin.RegistrationIdentity) ||
                    !working.IsDirectory || !working.SameFile(pin.WorkingIdentity))
                    throw new IOException("The actual saved document/registration version or working-root identity changed before native Start.");
            }
        }
        private void DemandSavedRoot(SavedRootPin pin)
        {
            lock (_gate)
                if (_retiring || !pin.Published || !_originalSavedRootPins.Contains(pin))
                    throw new UnauthorizedAccessException("The actual kernel saved-root pin is unavailable.");
            DemandSavedRootNative(pin);
        }
        public bool IsIssuedOriginalExecutionPin(IDeveloperWorkspaceOriginalExecutionBinding binding,
            IDeveloperWorkspaceOriginalExecutionCommitPin value)
        {
            lock (_gate) return !_retiring && value is SavedRootPin pin && ReferenceEquals(pin.Owner, this) &&
                ReferenceEquals(pin.Binding, binding) && pin.Published && !pin.Sealed && _originalSavedRootPins.Contains(pin);
        }
        private Task CloseSavedRoot(SavedRootPin pin, bool acquisitionCleanup = false)
        {
            if (!acquisitionCleanup)
            {
                if (_physicalSources?.ContainsKey(this) == true)
                    throw new InvalidOperationException("A saved-root source callback cannot join its own pin cleanup.");
                for (var original = _executing.Value; original is not null; original = original.Parent)
                    if (Volatile.Read(ref original.Live) && ReferenceEquals(original, pin.Original))
                        throw new InvalidOperationException("The actual saved-pin acquisition cannot join itself.");
            }
            else if (!ReferenceEquals(_executing.Value, pin.Original))
                throw new InvalidOperationException("Only the SAME private acquiring original may request its internal cleanup.");
            TaskCompletionSource start; Task close;
            lock (_gate)
            {
                if (pin.Close is not null) return pin.Close;
                lock (pin.NativeGate) pin.Sealed = true;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                pin.Close = close = DrainSavedRoot(start.Task, pin);
            }
            start.TrySetResult(); return close;
        }
        private async Task DrainSavedRoot(Task start, SavedRootPin pin)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>();
            // These are the actual SafeFileHandle/lease synchronous close originals, not
            // fake asynchronous notifications. Read FileStream Dispose Tasks were already
            // independently joined and retained on the admitted acquisition original.
            foreach (var owned in new IDisposable?[] { pin.WorkingRoot, pin.RegistrationHandle, pin.MetadataHandle, pin.FilesRoot, pin.MetadataRoot, pin.WindowsFilesRoot, pin.WindowsMetadataRoot })
                if (owned is not null)
                    try { InvokeSavedRootOwnedCleanup(() => { owned.Dispose(); return true; }); }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
            Task[] tasks; lock (_gate) tasks = pin.OriginalCleanup.ToArray();
            foreach (var raw in tasks) try { await raw.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, raw, error); }
            ThrowOriginalErrors(errors);
        }
        private void DemandOriginalSavedRootDependencies()
        {
            DemandSavedRootParentJoin();
            for (var original = _savedRootAcquiring.Value; original is not null; original = original.Parent)
                if (Volatile.Read(ref original.Live)) throw new InvalidOperationException("An actual saved-root acquisition cannot join its kernel owner.");
            var visited = _checkingSavedRootDependencies ??= [];
            if (!visited.Add(this)) return;
            try
            {
                var issuer = Invoke(() => _originalExecutionBindings?.Invoke());
                issuer?.DemandExternalOriginalExecutionBindingJoin();
            }
            finally { visited.Remove(this); }
        }
        private async Task DrainOriginalSavedRootAcquisitions(List<Exception> errors)
        {
            await DrainSavedRootParentOriginals(errors).ConfigureAwait(false);
            SavedRootAcquisition[] originals; lock (_gate) originals = _originalSavedRootAcquisitions.ToArray();
            foreach (var original in originals)
                try { await original.Driver.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalErrors(errors, original.Driver, error); }
            Task[] tasks; lock (_gate) tasks = originals.SelectMany(value => value.Sources).ToArray();
            foreach (var actual in tasks) try { await actual.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, actual, error); }
        }
        private Original[] OriginalSavedRootOwners(PhysicalSelection physical)
            => _originalSavedRootPins.Where(value => ReferenceEquals(value.Preparation.Capture.Physical, physical))
                .Select(value => value.Original).Distinct().ToArray();
        private async Task DrainOriginalSavedRoots(PhysicalSelection physical, List<Exception> errors)
        {
            SavedRootPin[] pins; lock (_gate) pins = _originalSavedRootPins.Where(value => ReferenceEquals(value.Preparation.Capture.Physical, physical)).ToArray();
            var closes = new List<Task>();
            foreach (var pin in pins) try { closes.Add(CloseSavedRoot(pin)); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            foreach (var raw in closes) try { await raw.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, raw, error); }
        }
    }
}
