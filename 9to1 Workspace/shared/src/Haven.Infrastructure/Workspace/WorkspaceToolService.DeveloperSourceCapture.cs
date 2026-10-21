using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>Root composes this SAME kernel owner with the actual Home read-admission issuer
    /// and Files selection source. No source content is read before their original validation.
    /// Physical selection requires the maintained Linux or Windows retained native boundary;
    /// missing native capability refuses before capture, while ordinary tools remain intact.</summary>
    public IDeveloperProjectOriginalPhysicalCaptureSource CreateOriginalDeveloperCaptureSource(
        IDeveloperProjectOriginalReadAdmissionSource originalReadAdmissionSource,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> configuredOriginalSelections)
    {
        var source = new DeveloperCaptureSource(originalReadAdmissionSource, configuredOriginalSelections);
        return source;
    }

    /// <summary>Additive actual setup-issuer composition; the original read-only factory's
    /// CLR signature and behavior remain available. Missing setup composition refuses only
    /// the new existing-directory setup observation; it never enables mutations.</summary>
    public IDeveloperProjectOriginalPhysicalCaptureSource CreateOriginalDeveloperCaptureSource(
        IDeveloperProjectOriginalReadAdmissionSource originalReadAdmissionSource,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> configuredOriginalSelections,
        Func<IDeveloperProjectOriginalSetupPermissionSource> configuredOriginalSetupPermissions)
    {
        var source = new DeveloperCaptureSource(originalReadAdmissionSource, configuredOriginalSelections, configuredOriginalSetupPermissions);
        source.BindOriginalService(this);
        return source;
    }

    /// <summary>The manual (no-AI) source-edit composition also supplies the SAME Home WRITE
    /// admission issuer, so the kernel can revalidate both the READ and EDIT admissions and apply
    /// the reviewed replacement through the shared atomic engine. Missing composition refuses edits;
    /// it never enables any mutation.</summary>
    public IDeveloperProjectOriginalPhysicalCaptureSource CreateOriginalDeveloperCaptureSource(
        IDeveloperProjectOriginalReadAdmissionSource originalReadAdmissionSource,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> configuredOriginalSelections,
        Func<IDeveloperProjectOriginalSetupPermissionSource> configuredOriginalSetupPermissions,
        IDeveloperProjectOriginalEditAdmissionSource configuredOriginalEditAdmissions)
    {
        var source = new DeveloperCaptureSource(originalReadAdmissionSource, configuredOriginalSelections,
            configuredOriginalSetupPermissions, configuredOriginalEditAdmissions);
        source.BindOriginalService(this);
        return source;
    }

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
        private readonly IDeveloperProjectOriginalEditAdmissionSource? _edits;
        private WorkspaceToolService? _service;
        internal void BindOriginalService(WorkspaceToolService service) => _service = service ?? throw new ArgumentNullException(nameof(service));

        internal DeveloperCaptureSource(IDeveloperProjectOriginalReadAdmissionSource reads,
            Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> selections,
            Func<IDeveloperProjectOriginalSetupPermissionSource>? setupPermissions,
            IDeveloperProjectOriginalEditAdmissionSource? edits)
            : this(reads, selections, setupPermissions)
            => _edits = edits;
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
            public DeveloperWindowsRootLease? WindowsRoot;
            public SafeFileHandle Project = null!;
            public LinuxIdentity ProjectIdentity;
            public DeveloperWindowsIdentity? WindowsProjectIdentity;
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
        private sealed record HeldFile(string RelativePath, FileStream OriginalStream, SafeFileHandle OriginalHandle, LinuxIdentity Identity,
            DeveloperWindowsIdentity? WindowsIdentity = null);

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
            if (_savedRootParentSource.Value is { } parent) return parent.Invoke(source);
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
            _savedRootParentSource.Value?.Retain(actual);
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
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Original developer source capture requires a supported retained native kernel boundary.");
            configuredRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(configuredRoot));
            selectedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedRoot));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(selectedRoot, configuredRoot, comparison) || !IsWithinRoot(configuredRoot, selectedRoot, comparison))
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
                if (OperatingSystem.IsWindows())
                {
                    original.WindowsRoot = Invoke(() => new DeveloperWindowsRootLease(configuredRoot));
                    original.Project = Invoke(() => original.WindowsRoot.OpenDirectory(selectedRoot));
                    original.WindowsProjectIdentity = Invoke(() => ReadDeveloperWindowsIdentity(original.Project));
                    DemandCurrent(original); return Task.FromResult<IDeveloperProjectOriginalPhysicalSelection>(original);
                }
                original.Root = Invoke(() => new OriginalLinuxPathLease(configuredRoot));
                original.Project = Invoke(() => original.Root.OpenDirectory(selectedRoot));
                original.ProjectIdentity = Invoke(() => ReadLinuxIdentity(original.Project));
                DemandCurrent(original); return Task.FromResult<IDeveloperProjectOriginalPhysicalSelection>(original);
            });
        }
        private void DemandCurrent(PhysicalSelection original)
        {
            if (original.Sealed) throw new UnauthorizedAccessException("Original developer root retired.");
            if (original.WindowsRoot is { } windows)
            {
                windows.DemandCurrent(); DemandDeveloperWindowsPath(original.Project, original.OriginalProjectRoot, true);
                if (original.WindowsProjectIdentity is not { } identity || !ReadDeveloperWindowsIdentity(original.Project).SameFile(identity))
                    throw new UnauthorizedAccessException("Original Windows project directory changed.");
                return;
            }
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
                    if (original.WindowsRoot is not null)
                        await Observe(original, () => VisitWindows(original, logical, admission, folders, files, held, token)).ConfigureAwait(false);
                    else await Observe(original, () => Visit(original.OriginalProjectRoot, "", 0)).ConfigureAwait(false);
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
                        if (file.WindowsIdentity is { } windows)
                        {
                            DemandDeveloperWindowsPath(file.OriginalHandle, Path.Combine(actual.OriginalExistingProjectRoot, file.RelativePath), false);
                            if (!ReadDeveloperWindowsIdentity(file.OriginalHandle).SameReadVersion(windows)) throw new IOException("Captured original Windows source changed before setup.");
                            continue;
                        }
                        DemandLinuxDescriptorPath(file.OriginalHandle, Path.Combine(actual.OriginalExistingProjectRoot, file.RelativePath));
                        if (!ReadLinuxIdentity(file.OriginalHandle).SameReadVersion(file.Identity)) throw new IOException("Captured original source changed before setup.");
                    }
                    return true;
                }); return Task.FromResult(true);
            });
        }
        public Task CloseOriginalCaptureAsync(IDeveloperProjectOriginalSourceCapture value)
        { if (value is not Capture actual || !ReferenceEquals(actual.Issuer, this)) throw new UnauthorizedAccessException("Foreign original capture."); return Close(actual.Physical); }
        /// <summary>Synchronous, pure native-metadata probe on the retained captured file handle.
        /// It refuses when the SAME capture is not issued or the exact project-relative retained handle
        /// no longer matches the captured native identity. Never reenters async/public Home/Files/store
        /// APIs, so it is safe beneath a held Home entry.</summary>
        public bool IsOriginalCapturedFileCurrentNative(IDeveloperProjectOriginalExistingSourceCapture sameCapture, string projectRelativePath)
        {
            if (sameCapture is not Capture actual || !IsIssuedOriginalCapture(actual, actual.Physical, actual.Logical))
                return false;
            DemandSafeProjectRelativePath(projectRelativePath);
            HeldFile? held;
            lock (_gate)
            {
                var matches = actual.Held.Where(value => StringComparer.Ordinal.Equals(value.RelativePath, projectRelativePath)).Take(2).ToArray();
                if (matches.Length != 1) return false;
                held = matches[0];
            }
            try
            {
                if (held.WindowsIdentity is { } windows)
                    return ReadDeveloperWindowsIdentity(held.OriginalHandle).SameReadVersion(windows);
                return ReadLinuxIdentity(held.OriginalHandle).SameReadVersion(held.Identity);
            }
            catch { return false; }
        }
        /// <summary>Genuine confined re-read of one captured source file's complete text. It requires
        /// the SAME returned admission validated at capture and revalidates it before reading, then
        /// reads the retained captured handle under <see cref="IDeveloperProjectOriginalReadAdmission.RunOriginalRead"/>
        /// — never raw path IO. Returns null when the exact project-relative file is not part of this
        /// capture; a stale/mutated captured source is refused on its recorded size/hash, never truncated.</summary>
        public Task<string?> ReadOriginalCapturedFileTextAsync(IDeveloperProjectOriginalExistingSourceCapture sameCapture,
            IDeveloperProjectOriginalReadAdmission sameReadAdmission, string projectRelativePath, CancellationToken token)
        {
            if (sameCapture is not Capture actual || !IsIssuedOriginalCapture(actual, actual.Physical, actual.Logical))
                throw new UnauthorizedAccessException("Foreign or retired original capture.");
            DemandSafeProjectRelativePath(projectRelativePath);
            DeveloperProjectCapturedSourceFile? entry = null;
            foreach (var candidate in actual.OriginalFiles)
            {
                if (StringComparer.Ordinal.Equals(candidate.RelativePath, projectRelativePath))
                {
                    if (entry is not null) throw new InvalidDataException("The captured source contains duplicate relative identities.");
                    entry = candidate;
                }
            }
            if (entry is null) return Task.FromResult<string?>(null);
            return Start(actual.Physical, async () =>
            {
                await Observe(actual.Physical, () => admissions.ValidateOriginalAsync(actual.Logical, sameReadAdmission, token)).ConfigureAwait(false);
                HeldFile held;
                lock (_gate)
                {
                    var matches = actual.Held.Where(value => StringComparer.Ordinal.Equals(value.RelativePath, projectRelativePath)).Take(2).ToArray();
                    if (matches.Length != 1) throw new InvalidOperationException("The captured source handle is missing or duplicated.");
                    held = matches[0];
                }
                Invoke(() =>
                {
                    DemandCurrent(actual.Physical);
                    var full = Path.Combine(actual.OriginalExistingProjectRoot, held.RelativePath);
                    if (held.WindowsIdentity is { } windows)
                    {
                        DemandDeveloperWindowsPath(held.OriginalHandle, full, false);
                        if (!ReadDeveloperWindowsIdentity(held.OriginalHandle).SameReadVersion(windows)) throw new IOException("Captured original Windows source changed before its confined read.");
                    }
                    else DemandLinuxDescriptorPath(held.OriginalHandle, full);
                    return true;
                });
                await Observe(actual.Physical, () => admissions.ValidateOriginalAsync(actual.Logical, sameReadAdmission, token)).ConfigureAwait(false);
                held.OriginalStream.Seek(0, SeekOrigin.Begin);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var body = new MemoryStream();
                var buffer = new byte[16384]; long bytes = 0;
                while (true)
                {
                    await Observe(actual.Physical, () => admissions.ValidateOriginalAsync(actual.Logical, sameReadAdmission, token)).ConfigureAwait(false);
                    var count = await ObserveRead(actual.Physical, sameReadAdmission,
                        () => held.OriginalStream.ReadAsync(buffer.AsMemory(), token).AsTask(), token).ConfigureAwait(false);
                    if (count == 0) break;
                    bytes += count; body.Write(buffer, 0, count);
                    if (bytes > DeveloperProjectSetupIntent.MaximumFileBytes)
                        throw new InvalidOperationException("The captured source file exceeds the bounded read size; it is not truncated.");
                    hash.AppendData(buffer, 0, count);
                }
                var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (bytes != entry.SizeBytes || !string.Equals(digest, entry.ContentSha256, StringComparison.Ordinal))
                    throw new IOException("Captured source changed after its original read; no stale text was disclosed.");
                return (string?)DecodeBoundedText(body.ToArray());
            });
        }
        /// <summary>Genuine confined reviewed WRITE of one captured source file under a SAME WRITE
        /// admission (never the READ admission alone). It revalidates the SAME read + edit admissions
        /// and capture, refuses unless the current captured text hash equals <paramref name="expectedSha256"/>,
        /// applies the exact replacement through the shared atomic engine under the edit admission, then
        /// re-reads the committed inode under the read admission and verifies the resulting hash equals the
        /// after hash. It returns the post-write SHA-256.</summary>
        public Task<string> ApplyOriginalCapturedFileEditAsync(IDeveloperProjectOriginalExistingSourceCapture sameCapture,
            IDeveloperProjectOriginalReadAdmission sameReadAdmission,
            IDeveloperProjectOriginalEditAdmission sameEditAdmission,
            string projectRelativePath, string expectedSha256, string newText, CancellationToken token)
        {
            if (sameCapture is not Capture actual || !IsIssuedOriginalCapture(actual, actual.Physical, actual.Logical))
                throw new UnauthorizedAccessException("Foreign or retired original capture.");
            DemandSafeProjectRelativePath(projectRelativePath);
            if (!IsHexDigest(expectedSha256)) throw new ArgumentException("The exact reviewed current SHA-256 revision is required.", nameof(expectedSha256));
            var newBytes = Encoding.UTF8.GetBytes(newText);
            if (!string.Equals(DecodeBoundedText(newBytes), newText, StringComparison.Ordinal))
                throw new InvalidOperationException("The replacement text must be canonical bounded UTF-8 without NUL bytes.");
            var afterHash = Convert.ToHexString(SHA256.HashData(newBytes)).ToLowerInvariant();
            DeveloperProjectCapturedSourceFile? entry = null;
            foreach (var candidate in actual.OriginalFiles)
            {
                if (StringComparer.Ordinal.Equals(candidate.RelativePath, projectRelativePath))
                {
                    if (entry is not null) throw new InvalidDataException("The captured source contains duplicate relative identities.");
                    entry = candidate;
                }
            }
            if (entry is null) throw new UnauthorizedAccessException("The exact captured source file is not part of this selected project.");
            return Start(actual.Physical, async () =>
            {
                var errors = new List<Exception>(); Task? write = null;
                try
                {
                    // Fresh SAME read + edit admission validation before any disclosure/effect.
                    await Observe(actual.Physical, () => admissions.ValidateOriginalAsync(actual.Logical, sameReadAdmission, token)).ConfigureAwait(false);
                    await Observe(actual.Physical, () => Edits().ValidateOriginalEditAsync(actual.Logical, sameCapture,
                        projectRelativePath, expectedSha256, sameEditAdmission, token)).ConfigureAwait(false);
                    HeldFile held;
                    lock (_gate)
                    {
                        var matches = actual.Held.Where(value => StringComparer.Ordinal.Equals(value.RelativePath, projectRelativePath)).Take(2).ToArray();
                        if (matches.Length != 1) throw new InvalidOperationException("The captured source handle is missing or duplicated.");
                        held = matches[0];
                    }
                    Invoke(() =>
                    {
                        DemandCurrent(actual.Physical);
                        var full = Path.Combine(actual.OriginalExistingProjectRoot, held.RelativePath);
                        if (held.WindowsIdentity is { } windows)
                        {
                            DemandDeveloperWindowsPath(held.OriginalHandle, full, false);
                            if (!ReadDeveloperWindowsIdentity(held.OriginalHandle).SameReadVersion(windows))
                                throw new IOException("Captured original Windows source changed before its confined edit.");
                        }
                        else DemandLinuxDescriptorPath(held.OriginalHandle, full);
                        return true;
                    });
                    // Read the exact current text under the READ admission; refuse any stale base.
                    string beforeText;
                    using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    using (var body = new MemoryStream())
                    {
                        held.OriginalStream.Seek(0, SeekOrigin.Begin);
                        var buffer = new byte[16384]; long bytes = 0;
                        while (true)
                        {
                            await Observe(actual.Physical, () => admissions.ValidateOriginalAsync(actual.Logical, sameReadAdmission, token)).ConfigureAwait(false);
                            var count = await ObserveRead(actual.Physical, sameReadAdmission,
                                () => held.OriginalStream.ReadAsync(buffer.AsMemory(), token).AsTask(), token).ConfigureAwait(false);
                            if (count == 0) break;
                            bytes += count; body.Write(buffer, 0, count);
                            if (bytes > DeveloperProjectSetupIntent.MaximumFileBytes)
                                throw new InvalidOperationException("The captured source file exceeds the bounded read size; it is not truncated.");
                            hash.AppendData(buffer, 0, count);
                        }
                        var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                        if (bytes != entry.SizeBytes || !string.Equals(digest, entry.ContentSha256, StringComparison.Ordinal))
                            throw new IOException("Captured source changed after its original read; no stale edit base was accepted.");
                        if (!string.Equals(digest, expectedSha256, StringComparison.Ordinal))
                            throw new UnauthorizedAccessException("The current captured source revision does not match the reviewed expected source hash; no write occurred.");
                        beforeText = DecodeBoundedText(body.ToArray());
                    }
                    // Revalidate both admissions between the read and the finite kernel write start.
                    await Observe(actual.Physical, () => admissions.ValidateOriginalAsync(actual.Logical, sameReadAdmission, token)).ConfigureAwait(false);
                    await Observe(actual.Physical, () => Edits().ValidateOriginalEditAsync(actual.Logical, sameCapture,
                        projectRelativePath, expectedSha256, sameEditAdmission, token)).ConfigureAwait(false);
                    // Apply the exact reviewed replacement under the SAME EDIT admission, routed
                    // through a genuine original Invocation so the finite effect executes through the
                    // retained native write engine (WriteOriginalLinuxAsync/WriteOriginalWindowsAsync)
                    // under the privately issued home edit fence. No task-attempt admission or
                    // always-allow fence is fabricated.
                    // The genuine manual edit fence is vended ONLY by the configured Home EDIT issuer
                    // after its private recognition; a caller-supplied or AlwaysAllow admission cannot
                    // mint it. The returned fence holds real claimed Home authorization through the
                    // finite native write and binds the exact target + reviewed BEFORE/AFTER digests.
                    var fence = Edits().MintOriginalEditFence(sameEditAdmission,
                        projectRelativePath, expectedSha256, newText);
                    var writer = _service ?? throw new InvalidOperationException("The original workspace writer is unavailable.");
                    IWorkspaceOriginalInvocation? invocation = null;
                    try
                    {
                        invocation = writer.AcquireOriginalInvocation(fence);
                        // Capture the actual non-null service/write task into typed locals; retain and
                        // await the SAME original. The genuine native write still runs beneath the
                        // private Home edit fence and the SAME admitted edit admission.
                        var writeTask = Invoke(() => sameEditAdmission.RunOriginalEdit(() =>
                        {
                            var actualWrite = invocation.Tools.WriteTextAtomicAsync(actual.OriginalExistingProjectRoot,
                                projectRelativePath, newText, token);
                            Retain(actual.Physical, actualWrite);
                            return actualWrite;
                        }, token));
                        write = writeTask;
                        await Observe(actual.Physical, () => writeTask).ConfigureAwait(false);
                        await invocation.CloseAndDrainAsync().ConfigureAwait(false);
                        invocation = null; // closed and drained; nothing further to clean on success.
                    }
                    finally
                    {
                        if (invocation is not null)
                            try { await invocation.CloseAndDrainAsync().ConfigureAwait(false); }
                            catch (Exception cause) { AddOriginalErrors(errors, null, cause); }
                        if (fence is IAsyncDisposable disposable)
                            try { await disposable.DisposeAsync().ConfigureAwait(false); }
                            catch (Exception cause) { AddOriginalErrors(errors, null, cause); }
                    }
                    // Re-read the committed inode under the READ admission; verify exact after hash and text.
                    var committed = await ReadCommittedTextUnderReadAsync(actual.Physical, actual, projectRelativePath, sameReadAdmission, token).ConfigureAwait(false);
                    if (!string.Equals(committed.Sha256, afterHash, StringComparison.Ordinal))
                        throw new IOException("The applied source rewrite did not persist exactly; post-write revision mismatch.");
                    if (!string.Equals(committed.Text, newText, StringComparison.Ordinal))
                        throw new IOException("The persisted source text differs from the reviewed replacement; nothing partial was returned.");
                    return afterHash;
                }
                catch (Exception error) { AddOriginalErrors(errors, write, error); }
                ThrowOriginalErrors(errors);
                throw new InvalidOperationException("No original source edit was applied.");
            });
        }
        /// <summary>Opens the committed file fresh through the retained native root lease and reads its
        /// complete text under the SAME READ admission, returning the text and its SHA-256.</summary>
        private async Task<(string Text, string Sha256)> ReadCommittedTextUnderReadAsync(PhysicalSelection physical, Capture capture,
            string relativePath, IDeveloperProjectOriginalReadAdmission admission, CancellationToken token)
        {
            SafeFileHandle? handle = null; FileStream? stream = null; Task<int>? read = null;
            var errors = new List<Exception>();
            try
            {
                Invoke(() =>
                {
                    DemandCurrent(physical);
                    var full = Path.Combine(physical.OriginalProjectRoot, relativePath);
                    if (physical.WindowsRoot is not null)
                    {
                        handle = physical.WindowsRoot.OpenRead(full);
                        DemandDeveloperWindowsPath(handle, full, false);
                    }
                    else
                    {
                        handle = physical.Root.OpenRead(full);
                        DemandLinuxDescriptorPath(handle, full);
                    }
                    stream = new FileStream(handle, FileAccess.Read, 16 * 1024, isAsync: false);
                    return true;
                });
                var activeStream = stream ?? throw new InvalidOperationException("The committed source handle was not opened.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var body = new MemoryStream();
                var buffer = new byte[16384]; long bytes = 0;
                while (true)
                {
                    await Observe(physical, () => admissions.ValidateOriginalAsync(capture.Logical, admission, token)).ConfigureAwait(false);
                    var count = await ObserveRead(physical, admission,
                        () => activeStream.ReadAsync(buffer.AsMemory(), token).AsTask(), token).ConfigureAwait(false);
                    if (count == 0) break;
                    bytes += count; body.Write(buffer, 0, count);
                    if (bytes > DeveloperProjectSetupIntent.MaximumFileBytes)
                        throw new InvalidOperationException("The committed source file exceeds the bounded read size; it is not truncated.");
                    hash.AppendData(buffer, 0, count);
                }
                var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                return (DecodeBoundedText(body.ToArray()), digest);
            }
            catch (Exception error) { AddOriginalErrors(errors, read, error); }
            finally
            {
                Task? close = null;
                try { if (stream is not null) { close = stream.DisposeAsync().AsTask(); Retain(physical, close); } }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
                try { handle?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors);
            throw new InvalidOperationException("No committed source readback was produced.");
        }
        private IDeveloperProjectOriginalEditAdmissionSource Edits()
            => _edits ?? throw new InvalidOperationException("No genuine SAME Home EDIT admission issuer is composed; edits are refused.");
        private static bool IsHexDigest(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
        private static void DemandSafeProjectRelativePath(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (Path.IsPathRooted(path) || path.Length > 2048 || path.IndexOf('\0') >= 0 ||
                path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(value => value is "." or ".."))
                throw new ArgumentException("A safe project-relative source path is required.", nameof(path));
        }
        private static string DecodeBoundedText(byte[] utf8)
        {
            if (utf8.AsSpan().IndexOf((byte)0) >= 0)
                throw new InvalidOperationException("The source file is not a complete bounded text document.");
            try
            {
                var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(utf8);
                if (text.Length > DeveloperProjectSetupIntent.MaximumFileBytes)
                    throw new InvalidOperationException("The source text exceeds the bounded manual read size; it is not truncated.");
                return text;
            }
            catch (DecoderFallbackException error)
            {
                throw new InvalidOperationException("The source file is not valid UTF-8 text; no partial content was disclosed.", error);
            }
        }
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
            try { actual.WindowsRoot?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
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
