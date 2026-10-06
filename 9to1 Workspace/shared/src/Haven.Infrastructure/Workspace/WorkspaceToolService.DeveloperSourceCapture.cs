using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>Root composes this SAME kernel owner with the actual Home read-admission issuer
    /// and Files selection source. No source content is read before their original validation.
    /// This initial implementation requires the maintained Linux openat2/statx boundary;
    /// Windows selection is explicitly unsupported here, while existing Windows tools remain intact.</summary>
    public IDeveloperProjectOriginalPhysicalCaptureSource CreateOriginalDeveloperCaptureSource(
        IDeveloperProjectOriginalReadAdmissionSource originalReadAdmissionSource,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> configuredOriginalSelections)
        => new DeveloperCaptureSource(originalReadAdmissionSource, configuredOriginalSelections);

    /// <summary>Additive actual setup-issuer composition; the original read-only factory's
    /// CLR signature and behavior remain available. Missing setup composition refuses only
    /// the new existing-directory setup observation; it never enables mutations.</summary>
    public IDeveloperProjectOriginalPhysicalCaptureSource CreateOriginalDeveloperCaptureSource(
        IDeveloperProjectOriginalReadAdmissionSource originalReadAdmissionSource,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> configuredOriginalSelections,
        Func<IDeveloperProjectOriginalSetupPermissionSource> configuredOriginalSetupPermissions)
        => new DeveloperCaptureSource(originalReadAdmissionSource, configuredOriginalSelections, configuredOriginalSetupPermissions);

    private sealed partial class DeveloperCaptureSource(
        IDeveloperProjectOriginalReadAdmissionSource admissions,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> selections,
        Func<IDeveloperProjectOriginalSetupPermissionSource>? setupPermissions) : IDeveloperProjectOriginalPhysicalCaptureSource
    {
        private readonly AsyncLocal<Original?> _executing = new();
        [ThreadStatic] private static Dictionary<DeveloperCaptureSource, int>? _physicalSources;
        private readonly object _gate = new();
        private readonly HashSet<PhysicalSelection> _issued = [];
        private readonly HashSet<Capture> _captures = [];
        private bool _retiring;
        private Task? _close;
        private sealed class Original(PhysicalSelection owner, Original? parent)
        {
            public PhysicalSelection Owner => owner;
            public Original? Parent => parent;
            public Task Driver = null!;
            public readonly List<Task> Sources = [];
            public bool Live;
            public bool Healthy;
        }

        private sealed class PhysicalSelection(DeveloperCaptureSource issuer, string configuredRoot, string projectRoot)
            : IDeveloperProjectOriginalPhysicalSelection
        {
            public DeveloperCaptureSource Issuer => issuer;
            public string OriginalProjectRoot => projectRoot;
            public string ConfiguredRoot => configuredRoot;
            public OriginalLinuxPathLease Root = null!;
            public SafeFileHandle Project = null!;
            public LinuxIdentity ProjectIdentity;
            public readonly HashSet<Original> Originals = [];
            public readonly List<Task> OriginalCleanupTasks = [];
            public Task? Close;
            public bool Sealed;
            public bool CaptureAttempted;
            public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
            public Task CloseAndDrainAsync() => issuer.Close(this);
        }
        private sealed class Capture(DeveloperCaptureSource issuer, PhysicalSelection physical,
            IDeveloperProjectOriginalReadSelection logical, ImmutableArray<string> folders,
            ImmutableArray<DeveloperProjectCapturedSourceFile> files, IReadOnlyList<HeldFile> held)
            : IDeveloperProjectOriginalExistingSourceCapture
        {
            public DeveloperCaptureSource Issuer => issuer;
            public PhysicalSelection Physical => physical;
            public IDeveloperProjectOriginalReadSelection Logical => logical;
            public IReadOnlyList<HeldFile> Held => held;
            public bool Closed;
            public string OriginalCaptureReference { get; } = "dev-source-capture:" + Guid.NewGuid().ToString("D");
            public string OriginalCaptureDigest { get; } = Convert.ToHexString(SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(new { projectRoot = physical.OriginalProjectRoot, folders, files }))).ToLowerInvariant();
            public ImmutableArray<string> OriginalFolderPaths => folders;
            public ImmutableArray<DeveloperProjectCapturedSourceFile> OriginalFiles => files;
            public string OriginalExistingProjectRoot => physical.OriginalProjectRoot;
        }
        private static bool SafeLeaf(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 255 &&
            value is not ("." or "..") && !value.Any(c => c is '/' or '\\' or ':' || char.IsControl(c));
        private sealed record HeldFile(string RelativePath, FileStream OriginalStream, SafeFileHandle OriginalHandle, LinuxIdentity Identity);

        public void DemandExternalOriginalJoin()
        {
            if (_physicalSources?.ContainsKey(this) == true)
                throw new InvalidOperationException("An original source callback cannot join its kernel capture owner.");
            for (var actual = _executing.Value; actual is not null; actual = actual.Parent)
                if (Volatile.Read(ref actual.Live)) throw new InvalidOperationException("A live original capture cannot join its own retirement.");
            if (admissions is not IDeveloperProjectOriginalReadAdmissionJoinGuard homeGuard)
                throw new InvalidOperationException("The genuine Home read-owner join guard is unavailable.");
            Invoke(() => { homeGuard.DemandExternalOriginalReadAdmissionJoin(); selections().DemandExternalOriginalReadSelectionJoin(); return true; });
            DemandOriginalDirectoryDependencies();
            DemandOriginalWorkspaceMetadataDependencies();
            DemandOriginalSavedRootDependencies();
        }
        private T Invoke<T>(Func<T> source)
        {
            var depths = _physicalSources ??= []; depths.TryGetValue(this, out var old); depths[this] = old + 1;
            try { return source(); }
            catch (OperationCanceledException cause) { throw new AggregateException("Synchronous kernel/source fault returned no canceled original Task.", cause); }
            finally { if (old == 0) depths.Remove(this); else depths[this] = old; }
        }
        private PhysicalSelection Require(IDeveloperProjectOriginalPhysicalSelection actual)
        {
            if (actual is not PhysicalSelection original || !ReferenceEquals(original.Issuer, this)) throw new UnauthorizedAccessException("Foreign physical selection.");
            lock (_gate) if (!_issued.Contains(original) || original.Sealed) throw new UnauthorizedAccessException("Original physical selection retired.");
            return original;
        }
        public bool IsIssuedOriginalSelection(IDeveloperProjectOriginalPhysicalSelection actual)
        { lock (_gate) return actual is PhysicalSelection original && ReferenceEquals(original.Issuer, this) && _issued.Contains(original) && !original.Sealed; }
        private void Retain(PhysicalSelection owner, Task actual)
        {
            var original = _executing.Value;
            if (original is null || !ReferenceEquals(original.Owner, owner)) throw new InvalidOperationException("No actual source original owns this returned Task.");
            lock (_gate) original.Sources.Add(actual);
        }
        private Task<T> Start<T>(PhysicalSelection original, Func<Task<T>> body, Action<Original, Task<T>>? publishOriginalMetadata = null)
        {
            TaskCompletionSource start; Task<T> actual;
            lock (_gate)
            {
                if (_retiring || original.Sealed) throw new UnauthorizedAccessException("Original physical selection retired before admission.");
                original.Originals.RemoveWhere(value => value.Healthy && value.Driver.IsCompletedSuccessfully);
                if (original.Originals.Count >= 128) throw new InvalidOperationException("Unresolved kernel source originals are full.");
                var marker = new Original(original, _executing.Value);
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = Run(start.Task, marker, body); marker.Driver = actual; original.Originals.Add(marker);
                // Private metadata-only assignment, before the start gate; no domain/source
                // callback executes here. Actual whole driver identity is visible to close.
                publishOriginalMetadata?.Invoke(marker, actual);
            }
            start.TrySetResult(); return actual;
        }
        private async Task<T> Run<T>(Task start, Original marker, Func<Task<T>> body)
        {
            await start.ConfigureAwait(false); var previous = _executing.Value;
            _executing.Value = marker; Volatile.Write(ref marker.Live, true);
            Task<T>? actual = null; var errors = new List<Exception>(); T value = default!;
            try
            {
                try { actual = Invoke(body); Retain(marker.Owner, actual); value = await actual.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalErrors(errors, actual, error); }
                Task[] sources; lock (_gate) sources = marker.Sources.ToArray();
                foreach (var source in sources)
                    try { await source.ConfigureAwait(false); }
                    catch (Exception error) { AddOriginalErrors(errors, source, error); }
                if (actual?.IsCanceled == true && errors.Count != 0 && errors.All(error => error is OperationCanceledException))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
                ThrowOriginalErrors(errors);
                marker.Healthy = true; return value;
            }
            finally { Volatile.Write(ref marker.Live, false); _executing.Value = previous; }
        }
        private async Task<T> Observe<T>(PhysicalSelection original, Func<Task<T>> source)
        {
            var actual = Invoke(source); Retain(original, actual);
            try { return await actual.ConfigureAwait(false); }
            catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
        }
        private async Task Observe(PhysicalSelection original, Func<Task> source)
        {
            var actual = Invoke(source); Retain(original, actual);
            try { await actual.ConfigureAwait(false); }
            catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
        }
        private async Task<T> ObserveRead<T>(PhysicalSelection original, IDeveloperProjectOriginalReadAdmission admission,
            Func<Task<T>> source, CancellationToken token)
        {
            Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
            try { _ = Invoke(() => admission.RunOriginalRead(() =>
                { actual = source(); Retain(original, actual); return actual; }, token)); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (actual is not null)
                try { value = await actual.ConfigureAwait(false); }
                catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            ThrowOriginalErrors(errors, actual?.IsCanceled == true && errors.Count == 1);
            return value;
        }
        public Task<IDeveloperProjectOriginalPhysicalSelection> OpenOriginalSelectionAsync(string configuredRoot, string selectedRoot, CancellationToken token)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Original developer source capture requires the supported Linux kernel boundary.");
            configuredRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot));
            selectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedRoot));
            if (selectedRoot == configuredRoot || !IsWithinRoot(configuredRoot, selectedRoot, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("Select an existing project strictly beneath the genuine Files root.");
            PhysicalSelection original;
            lock (_gate)
            {
                if (_retiring) throw new InvalidOperationException("Original source capture owner is retiring.");
                if (_issued.Count >= 128) throw new InvalidOperationException("Original physical selection custody is full; no root was opened.");
                original = new(this, configuredRoot, selectedRoot); _issued.Add(original);
            }
            return Start<IDeveloperProjectOriginalPhysicalSelection>(original, () =>
            {
                token.ThrowIfCancellationRequested();
                original.Root = Invoke(() => new OriginalLinuxPathLease(configuredRoot));
                original.Project = Invoke(() => original.Root.OpenDirectory(selectedRoot));
                original.ProjectIdentity = Invoke(() => ReadLinuxIdentity(original.Project));
                DemandCurrent(original); return Task.FromResult<IDeveloperProjectOriginalPhysicalSelection>(original);
            });
        }
        private void DemandCurrent(PhysicalSelection original)
        {
            if (original.Sealed) throw new UnauthorizedAccessException("Original developer root retired.");
            original.Root.DemandCurrent(); DemandLinuxDescriptorPath(original.Project, original.OriginalProjectRoot);
            if (!ReadLinuxIdentity(original.Project).SameFile(original.ProjectIdentity)) throw new UnauthorizedAccessException("Original project directory changed.");
        }
        public Task RevalidateOriginalSelectionAsync(IDeveloperProjectOriginalPhysicalSelection actual, CancellationToken token)
        { var original = Require(actual); return Start(original, () => { token.ThrowIfCancellationRequested(); Invoke(() => { DemandCurrent(original); return true; }); return Task.FromResult(true); }); }

        public Task<IDeveloperProjectOriginalExistingSourceCapture> CaptureOriginalAsync(IDeveloperProjectOriginalPhysicalSelection actual,
            IDeveloperProjectOriginalReadSelection logical, IDeveloperProjectOriginalReadAdmission admission, CancellationToken token)
        {
            var original = Require(actual);
            return Start<IDeveloperProjectOriginalExistingSourceCapture>(original, async () =>
            {
                var configured = Invoke(selections);
                if (!Invoke(() => configured.IsIssuedOriginalPhysicalBinding(logical, actual))) throw new UnauthorizedAccessException("Logical selection does not own this original kernel root.");
                await Observe(original, () => admissions.ValidateOriginalAsync(logical, admission, token)).ConfigureAwait(false);
                lock (_gate)
                {
                    if (original.CaptureAttempted) throw new InvalidOperationException("This actual source selection already attempted a manifest; inspect its original outcome, never silently repeat it.");
                    original.CaptureAttempted = true;
                }
                var folders = new List<string>(); var files = new List<DeveloperProjectCapturedSourceFile>(); var held = new List<HeldFile>();
                long total = 0; var errors = new List<Exception>();
                try
                {
                    await Observe(original, () => Visit(original.OriginalProjectRoot, "", 0)).ConfigureAwait(false);
                    if (files.Count == 0) throw new InvalidOperationException("Select a nonempty bounded source project; empty capture was not published.");
                    await Observe(original, () => admissions.ValidateOriginalAsync(logical, admission, token)).ConfigureAwait(false);
                    Invoke(() => { DemandCurrent(original); return true; });
                    var capture = new Capture(this, original, logical, folders.ToImmutableArray(), files.ToImmutableArray(), held.ToArray());
                    lock (_gate) _captures.Add(capture);
                    return capture;
                }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                foreach (var file in held)
                {
                    Task? close = null;
                    try { close = Invoke(() => file.OriginalStream.DisposeAsync().AsTask()); Retain(original, close); await close.ConfigureAwait(false); }
                    catch (Exception error) { AddOriginalErrors(errors, close, error); }
                }
                ThrowOriginalErrors(errors); throw new InvalidOperationException("No original source capture was produced.");

                async Task Visit(string path, string relative, int depth)
                {
                    SafeFileHandle? directory = null; IEnumerator<string>? enumerator = null;
                    var originalCauses = new List<Exception>();
                    try
                    {
                        token.ThrowIfCancellationRequested(); if (depth > 10) throw new InvalidOperationException("Original source folder depth exceeds 10; incomplete capture refused.");
                        await Observe(original, () => admissions.ValidateOriginalAsync(logical, admission, token)).ConfigureAwait(false);
                        directory = Invoke(() => admission.RunOriginalRead(() => original.Root.OpenDirectory(path), token));
                        var directoryIdentity = Invoke(() => ReadLinuxIdentity(directory));
                        // The directory enumeration is anchored to the SAME retained descriptor.
                        var anchored = $"/proc/{Environment.ProcessId}/fd/{LinuxDescriptor(directory)}";
                        enumerator = Invoke(() => admission.RunOriginalRead(() => Directory.EnumerateFileSystemEntries(anchored).GetEnumerator(), token));
                        var names = new List<string>();
                        while (Invoke(() => admission.RunOriginalRead(enumerator.MoveNext, token)))
                        {
                            var name = Invoke(() => admission.RunOriginalRead(() => Path.GetFileName(enumerator.Current), token));
                            if (!SafeLeaf(name)) throw new InvalidDataException("Unsupported original source entry name; no partial manifest was returned.");
                            if (names.Count >= 128) throw new InvalidOperationException("Original directory scan exceeds the bounded source limit; incomplete capture refused.");
                            names.Add(name);
                        }
                        foreach (var name in names.Order(StringComparer.Ordinal))
                        {
                            var child = Path.Combine(path, name); var childRelative = relative.Length == 0 ? name : relative + "/" + name;
                            SafeFileHandle? candidate = null; FileStream? stream = null;
                            try
                            {
                                // O_PATH never blocks on a FIFO. Kind is observed before opening content.
                                candidate = Invoke(() => admission.RunOriginalRead(() => OpenLinuxAt(directory, name, LinuxPath | LinuxCloseOnExec), token));
                                var identity = Invoke(() => ReadLinuxIdentity(candidate));
                                if (identity.IsDirectory)
                                {
                                    if (folders.Count >= DeveloperProjectSetupIntent.MaximumFolders) throw new InvalidOperationException("Original source folder count exceeded; incomplete capture refused.");
                                    folders.Add(childRelative); await Observe(original, () => Visit(child, childRelative, depth + 1)).ConfigureAwait(false); continue;
                                }
                                if (!identity.IsRegular || identity.Links != 1) throw new UnauthorizedAccessException("Original source entry is not an unaliased regular file.");
                                if (files.Count >= DeveloperProjectSetupIntent.MaximumFiles || identity.Size > (ulong)DeveloperProjectSetupIntent.MaximumFileBytes)
                                    throw new InvalidOperationException("Original source file limit exceeded; incomplete capture refused.");
                                var originalIdentity = identity;
                                candidate.Dispose(); candidate = Invoke(() => admission.RunOriginalRead(() => original.Root.OpenRead(child), token));
                                identity = Invoke(() => ReadLinuxIdentity(candidate));
                                if (!identity.SameReadVersion(originalIdentity)) throw new IOException("Original source changed before its content read.");
                                stream = Invoke(() => new FileStream(candidate, FileAccess.Read, 4096, isAsync: false));
                                held.Add(new(childRelative, stream, candidate, identity));
                                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[16384]; long bytes = 0;
                                while (true)
                                {
                                    await Observe(original, () => admissions.ValidateOriginalAsync(logical, admission, token)).ConfigureAwait(false);
                                    var count = await ObserveRead(original, admission, () => stream.ReadAsync(buffer.AsMemory(), token).AsTask(), token).ConfigureAwait(false);
                                    if (count == 0) break; bytes += count; total += count;
                                    if (bytes > DeveloperProjectSetupIntent.MaximumFileBytes || total > DeveloperProjectSetupIntent.MaximumTotalBytes)
                                        throw new InvalidOperationException("Original source byte limit exceeded; incomplete capture refused.");
                                    hash.AppendData(buffer, 0, count);
                                }
                                var after = Invoke(() => ReadLinuxIdentity(candidate));
                                Invoke(() => { DemandCurrent(original); DemandLinuxDescriptorPath(candidate, child); return true; });
                                if (!identity.SameReadVersion(after) || (ulong)bytes != after.Size) throw new IOException("Original source file changed during capture.");
                                files.Add(new(childRelative, bytes, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), "dev-source-file:" + Guid.NewGuid().ToString("D")));
                                stream = null; candidate = null; // exact stream/handle retained by capture until independent close
                            }
                            finally { if (stream is not null && !held.Any(item => ReferenceEquals(item.OriginalStream, stream))) stream.Dispose(); if (candidate is not null && !held.Any(item => ReferenceEquals(item.OriginalHandle, candidate))) candidate.Dispose(); }
                        }
                        Invoke(() => { DemandCurrent(original); DemandLinuxDescriptorPath(directory, path); return true; });
                        if (!ReadLinuxIdentity(directory).SameReadVersion(directoryIdentity)) throw new IOException("Original source directory changed during enumeration.");
                    }
                    catch (Exception error) { AddOriginalErrors(originalCauses, null, error); }
                    finally
                    {
                        try { Invoke(() => { enumerator?.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(originalCauses, null, error); }
                        try { Invoke(() => { directory?.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(originalCauses, null, error); }
                    }
                    if (originalCauses.Count != 0 && originalCauses.All(error => error is OperationCanceledException))
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalCauses[0]).Throw();
                    ThrowOriginalErrors(originalCauses);
                }
            });
        }
        public bool IsIssuedOriginalCapture(IDeveloperProjectOriginalSourceCapture value, IDeveloperProjectOriginalPhysicalSelection physical, IDeveloperProjectOriginalReadSelection logical)
        { lock (_gate) return value is Capture actual && ReferenceEquals(actual.Issuer, this) && _captures.Contains(actual) && !actual.Closed && ReferenceEquals(actual.Physical, physical) && ReferenceEquals(actual.Logical, logical) && !actual.Physical.Sealed; }
        public Task RevalidateOriginalCaptureAsync(IDeveloperProjectOriginalSourceCapture value, CancellationToken token)
        {
            if (value is not Capture actual || !IsIssuedOriginalCapture(actual, actual.Physical, actual.Logical)) throw new UnauthorizedAccessException("Foreign or retired original capture.");
            return Start(actual.Physical, () =>
            {
                token.ThrowIfCancellationRequested(); Invoke(() =>
                {
                    DemandCurrent(actual.Physical);
                    foreach (var file in actual.Held)
                    {
                        DemandLinuxDescriptorPath(file.OriginalHandle, Path.Combine(actual.OriginalExistingProjectRoot, file.RelativePath));
                        if (!ReadLinuxIdentity(file.OriginalHandle).SameReadVersion(file.Identity)) throw new IOException("Captured original source changed before setup.");
                    }
                    return true;
                }); return Task.FromResult(true);
            });
        }
        public Task CloseOriginalCaptureAsync(IDeveloperProjectOriginalSourceCapture value)
        { if (value is not Capture actual || !ReferenceEquals(actual.Issuer, this)) throw new UnauthorizedAccessException("Foreign original capture."); return Close(actual.Physical); }
        public void RequestOriginalCaptureRetirement() { lock (_gate) _retiring = true; }
        public Task CloseAndDrainOriginalCapturesAsync()
        {
            DemandExternalOriginalJoin(); TaskCompletionSource start; Task result; PhysicalSelection[] originals;
            lock (_gate)
            {
                if (_close is not null) return _close;
                _retiring = true; originals = _issued.ToArray(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                result = DrainAll(start.Task, originals); _close = result;
            }
            start.TrySetResult(); return result;
        }
        private async Task DrainAll(Task start, PhysicalSelection[] originals)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>(); var closes = new List<Task>();
            await DrainOriginalSavedRootAcquisitions(errors).ConfigureAwait(false);
            // Every actual acquired root, including failed acquisition, is retained privately.
            foreach (var original in originals) try { closes.Add(Close(original)); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            foreach (var close in closes) try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
            ThrowOriginalErrors(errors);
        }
        private Task Close(PhysicalSelection actual)
        {
            DemandExternalOriginalJoin(); TaskCompletionSource start; Task result;
            lock (_gate)
            {
                if (actual.Close is not null) return actual.Close;
                actual.Sealed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                result = Drain(start.Task, actual); actual.Close = result;
            }
            start.TrySetResult(); return result;
        }
        private async Task Drain(Task start, PhysicalSelection actual)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>(); Original[] originals;
            await DrainOriginalSavedRootAcquisitions(errors).ConfigureAwait(false);
            lock (_gate) originals = actual.Originals.Concat(OriginalDirectoryOwners(actual)).Concat(OriginalFileRegistrationOwners(actual)).Concat(OriginalWorkspaceMetadataOwners(actual)).Concat(OriginalSavedRootOwners(actual)).Distinct().ToArray();
            foreach (var original in originals) try { await original.Driver.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, original.Driver, error); }
            // Lease cleanup follows every admitted original before captured streams/root
            // descriptors retire. Request signals alone never prove cleanup completion.
            await DrainOriginalDirectoryPreparations(actual, errors).ConfigureAwait(false);
            await DrainOriginalFileRegistrations(actual, errors).ConfigureAwait(false);
            await DrainOriginalWorkspaceMetadata(actual, errors).ConfigureAwait(false);
            await DrainOriginalSavedRoots(actual, errors).ConfigureAwait(false);
            Capture[] captures; lock (_gate) captures = _captures.Where(value => ReferenceEquals(value.Physical, actual)).ToArray();
            foreach (var capture in captures)
            {
                foreach (var held in capture.Held)
                {
                    Task? close = null;
                    try
                    {
                        Invoke(() =>
                        {
                            close = held.OriginalStream.DisposeAsync().AsTask();
                            lock (_gate) actual.OriginalCleanupTasks.Add(close);
                            return true;
                        });
                    }
                    catch (Exception error) { AddOriginalErrors(errors, null, error); }
                    // Join the exact acquired cleanup even if enrollment/source exit failed.
                    if (close is not null)
                        try { await close.ConfigureAwait(false); }
                        catch (Exception error) { AddOriginalErrors(errors, close, error); }
                }
                capture.Closed = true;
            }
            try { actual.Project?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { actual.Root?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            // Parent drivers settle before collecting their complete admitted child inventory.
            Task[] finalSources; lock (_gate) finalSources = actual.Originals.Concat(OriginalDirectoryOwners(actual)).Concat(OriginalFileRegistrationOwners(actual)).Concat(OriginalWorkspaceMetadataOwners(actual)).Concat(OriginalSavedRootOwners(actual)).Distinct()
                .SelectMany(value => value.Sources).Concat(actual.OriginalCleanupTasks)
                .Concat(_fileRegistrations.Where(value => ReferenceEquals(value.Capture.Physical, actual)).SelectMany(value => value.OriginalCleanup)).ToArray();
            foreach (var source in finalSources) try { await source.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, source, error); }
            ThrowOriginalErrors(errors);
            lock (_gate) { _issued.Remove(actual); foreach (var capture in captures) _captures.Remove(capture); }
        }
    }
}
