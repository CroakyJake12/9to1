using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>Independent current-project READ owner. It restores no setup Task/ACK,
    /// creates no source/metadata, and issues no process/model/Task authority.</summary>
    public IDeveloperOriginalCurrentProjectNativeSource CreateOriginalCurrentProjectNativeSource(
        Func<IDeveloperOriginalCurrentProjectSelectionSource> originalSelections,
        IDeveloperOriginalCurrentProjectReadAdmissionSource originalReads,
        IDeveloperProjectOriginalWorkspaceMetadataStore originalStore)
        => new CurrentProjectNativeSource(originalSelections, originalReads, originalStore);

    private sealed partial class CurrentProjectNativeSource(
        Func<IDeveloperOriginalCurrentProjectSelectionSource> selections,
        IDeveloperOriginalCurrentProjectReadAdmissionSource reads,
        IDeveloperProjectOriginalWorkspaceMetadataStore store) : IDeveloperOriginalCurrentProjectNativeSource
    {
        private readonly object _gate = new();
        private readonly AsyncLocal<Work?> _executing = new();
        [ThreadStatic] private static Dictionary<CurrentProjectNativeSource, int>? _physical;
        private readonly HashSet<Work> _work = [];
        private readonly HashSet<Read> _reads = [];
        private bool _retiring; private Task? _close;
        private sealed class Work(Work? parent, Action<Action> parentScope, Action<Task> parentRetain)
        {
            internal Work? Parent => parent;
            internal Action<Action> ParentScope => parentScope;
            internal Action<Task> ParentRetain => parentRetain;
            internal Task Driver = null!;
            internal bool Live, CallbackFailed;
            internal readonly List<Task> Sources = [];
            internal readonly List<Exception> Publication = [];
        }
        private sealed class Read(CurrentProjectNativeSource owner, Work work,
            IDeveloperOriginalCurrentProjectSelection selection, IDeveloperOriginalCurrentProjectDescriptor descriptor,
            IDeveloperOriginalCurrentProjectSelectionSource issuer) : IDeveloperOriginalCurrentProjectNativeRead
        {
            internal CurrentProjectNativeSource Owner => owner;
            internal Work Work => work;
            internal IDeveloperOriginalCurrentProjectSelection Selection => selection;
            internal IDeveloperOriginalCurrentProjectDescriptor Descriptor => descriptor;
            internal IDeveloperOriginalCurrentProjectSelectionSource Issuer => issuer;
            internal Task? Close, NativeClose;
            internal readonly object NativeGate = new();
            internal bool Sealed, Published;
            internal OriginalLinuxPathLease? MetadataRoot, FilesRoot;
            internal DeveloperWindowsRootLease? WindowsMetadataRoot, WindowsFilesRoot;
            internal DeveloperWindowsIdentity? WindowsMetadataIdentity, WindowsRegistrationIdentity, WindowsWorkingIdentity;
            internal string? WindowsSid;
            internal SafeFileHandle? MetadataHandle, RegistrationHandle, WorkingRoot;
            internal LinuxIdentity MetadataIdentity, RegistrationIdentity, WorkingIdentity;
            internal uint OriginalUid;
            internal string MetadataPath = "", Document = "", DocumentSha = "", Fingerprint = "";
            internal string RegistrationSha = "", RegistrationPath = "", WorkingPath = "";
            public string OriginalWorkspaceDocument => Document;
            public string OriginalWorkspaceDocumentSha256 => DocumentSha;
            public string OriginalRegisteredRootFingerprint => Fingerprint;
            public void DemandOriginalExecutionBinding() => owner.Demand(this);
            public ValueTask DisposeAsync() => new(owner.CloseRead(this));
        }
        private void Physical(Action callback)
        {
            var active = _physical ??= []; active.TryGetValue(this, out var before); active[this] = before + 1;
            try { callback(); } finally { if (before == 0) active.Remove(this); else active[this] = before; }
        }
        private T Invoke<T>(Work work, Func<T> callback)
        {
            T value = default!; var thread = Environment.CurrentManagedThreadId; int phase = 1, used = 0;
            var errors = new List<Exception>();
            void Record(Exception error) { lock (errors) Add(errors, error); }
            Physical(() =>
            {
                try
                {
                    work.ParentScope(() =>
                    {
                        if (Volatile.Read(ref phase) == 0 || Environment.CurrentManagedThreadId != thread ||
                            Interlocked.CompareExchange(ref used, 1, 0) != 0)
                        {
                            var refusal = new InvalidOperationException("Current project callback is expired, repeated or foreign-thread.");
                            Record(refusal); throw refusal;
                        }
                        try { value = callback(); } catch (Exception error) { Record(error); throw; }
                    });
                }
                catch (Exception error) { Record(error); }
                finally { Volatile.Write(ref phase, 0); }
            });
            lock (errors)
                if (Volatile.Read(ref used) == 0 && errors.Count == 0)
                    Add(errors, new InvalidOperationException("Current project parent omitted its finite callback."));
            Exception[] causes; lock (errors) causes = errors.ToArray();
            if (causes.Length != 0) { work.CallbackFailed = true; throw new AggregateException("Current project original callback failed.", causes); }
            return value;
        }
        private void Retain(Work work, Task actual)
        { lock (_gate) work.Sources.Add(actual); Invoke(work, () => { work.ParentRetain(actual); return true; }); }
        private async Task<T> Observe<T>(Work work, Func<Task<T>> callback)
        {
            Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
            try { Invoke(work, () => { actual = callback(); Retain(work, actual); return true; }); }
            catch (Exception error) { Add(errors, error); }
            if (actual is not null) try { value = await actual.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, actual, error); }
            if (actual?.IsCanceled == true && !work.CallbackFailed && errors.All(Canceled))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors); return value;
        }
        private async Task ObserveVoid(Work work, Func<Task> callback)
        {
            Task? actual = null; var errors = new List<Exception>();
            try { Invoke(work, () => { actual = callback(); Retain(work, actual); return true; }); }
            catch (Exception error) { Add(errors, error); }
            if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, actual, error); }
            if (actual?.IsCanceled == true && !work.CallbackFailed && errors.All(Canceled))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors);
        }
        private Task<T> Start<T>(Action<Action> parentScope, Action<Task> parentRetain, Func<Work, Task<T>> body)
        {
            ArgumentNullException.ThrowIfNull(parentScope); ArgumentNullException.ThrowIfNull(parentRetain);
            Work work; TaskCompletionSource start; Task<T> driver;
            lock (_gate)
            {
                if (_retiring || _work.Count >= 128) throw new InvalidOperationException("Current project native custody is sealed or full.");
                work = new(_executing.Value, parentScope, parentRetain); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                work.Driver = driver = Drive(start.Task, work, body); _work.Add(work);
            }
            try { Invoke(work, () => { parentRetain(driver); return true; }); } catch (Exception error) { Add(work.Publication, error); }
            start.TrySetResult(); return driver;
        }
        private async Task<T> Drive<T>(Task start, Work work, Func<Work, Task<T>> body)
        {
            await start.ConfigureAwait(false); var before = _executing.Value; _executing.Value = work; work.Live = true;
            Task<T>? actual = null; T result = default!; var errors = new List<Exception>();
            try
            {
                foreach (var error in work.Publication) Add(errors, error);
                if (errors.Count == 0)
                {
                    try { result = await Observe(work, () => actual = body(work)).ConfigureAwait(false); }
                    catch (Exception error) { if (actual is not null) AddTask(errors, actual, error); else Add(errors, error); }
                }
                Task[] tasks; lock (_gate) tasks = work.Sources.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
                foreach (var task in tasks) try { await task.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, task, error); }
                if (errors.Count != 0)
                {
                    Read[] owned; lock (_gate) owned = _reads.Where(value => ReferenceEquals(value.Work, work)).ToArray();
                    foreach (var read in owned)
                        try { var close = CloseNative(read); lock (_gate) work.Sources.Add(close); await close.ConfigureAwait(false); }
                        catch (Exception error) { Add(errors, error); }
                    if (actual?.IsCanceled == true && !work.CallbackFailed && tasks.All(task => !task.IsFaulted) && errors.All(Canceled))
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
                    Throw(errors);
                }
                return result;
            }
            finally { work.Live = false; _executing.Value = before; }
        }

        public Task<IDeveloperOriginalCurrentProjectNativeRead> CaptureOriginalWithinSourceAsync(
            IDeveloperOriginalCurrentProjectSelection selection, IDeveloperProjectOriginalReadAdmission admission,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
            => Start<IDeveloperOriginalCurrentProjectNativeRead>(scope, retain, async work =>
        {
            var source = Invoke(work, selections) ?? throw new InvalidOperationException("The actual current Files project source is unavailable.");
            var descriptor = Invoke(work, () => source.GetOriginalDescriptor(selection));
            Invoke(work, () =>
            {
                if (!source.IsIssuedOriginal(selection) || !source.IsIssuedOriginalDescriptor(selection, descriptor) ||
                    !ReferenceEquals(descriptor.OriginalStore, store))
                    throw new UnauthorizedAccessException("The actual current project selection/descriptor/store was substituted.");
                return true;
            });
            await ObserveVoid(work, () => source.RevalidateOriginalWithinSourceAsync(selection, descriptor.OriginalActor,
                callback => Invoke(work, () => { callback(); return true; }), task => Retain(work, task), token)).ConfigureAwait(false);
            Invoke(work, () =>
            {
                if (!reads.IsIssuedOriginalCurrentProjectRead(selection, admission))
                    throw new UnauthorizedAccessException("The configured Home owner did not privately issue this current project READ.");
                return true;
            });
            await ObserveVoid(work, () => reads.ValidateOriginalCurrentProjectReadWithinSourceAsync(selection, admission,
                callback => Invoke(work, () => { callback(); return true; }), task => Retain(work, task), token)).ConfigureAwait(false);
            var read = new Read(this, work, selection, descriptor, source);
            lock (_gate) { if (_retiring || _reads.Count >= 128) throw new InvalidOperationException("Current project read custody is sealed or full."); _reads.Add(read); }
            Task<IDeveloperOriginalCurrentProjectNativeRead>? raw = null; var errors = new List<Exception>();
            try
            {
                Invoke(work, () =>
                {
                    var returned = admission.RunOriginalRead(() =>
                    {
                        raw = CaptureDescriptors(read, token); Retain(work, raw); return raw;
                    }, token);
                    if (!ReferenceEquals(returned, raw)) throw new UnauthorizedAccessException("The READ admission substituted the original native Task.");
                    return true;
                });
            }
            catch (Exception error) { Add(errors, error); }
            IDeveloperOriginalCurrentProjectNativeRead? result = null;
            if (raw is not null) try { result = await raw.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, raw, error); }
            if (raw?.IsCanceled == true && !work.CallbackFailed && errors.All(Canceled))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors); return result ?? throw new UnauthorizedAccessException("No original current native project read was observed.");
        });

        private async Task<IDeveloperOriginalCurrentProjectNativeRead> CaptureDescriptors(Read read, CancellationToken token)
        {
            var work = read.Work; var descriptor = read.Descriptor;
            Invoke(work, () =>
            {
                token.ThrowIfCancellationRequested();
                if (OperatingSystem.IsWindows()) { CaptureWindowsDescriptors(read); return true; }
                if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
                    throw new PlatformNotSupportedException("Current project reconciliation requires supported Linux64 descriptor primitives.");
                RequireLinuxExports();
                var ancestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.OriginalWorkspaceMetadataAncestor));
                var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.OriginalWorkspaceMetadataDirectory));
                if (ancestor == "/" || !IsWithinRoot(ancestor, directory, StringComparison.Ordinal) || directory == ancestor)
                    throw new UnauthorizedAccessException("The actual configured metadata ancestor is unavailable.");
                var components = Path.GetRelativePath(ancestor, directory).Split(Path.DirectorySeparatorChar);
                if (components.Length is < 1 or > 4 || components.Any(value => value is "" or "." or ".."))
                    throw new UnauthorizedAccessException("The actual metadata directory exceeds its bounded configured ancestry.");
                read.OriginalUid = CurrentProjectGetEuid();
                read.RegistrationPath = descriptor.OriginalRegistrationStatePath; read.WorkingPath = descriptor.RegisteredProjectRoot;
                read.MetadataRoot = new OriginalLinuxPathLease(ancestor);
                using (var parent = read.MetadataRoot.OpenDirectory(directory)) DemandUid(parent, read.OriginalUid);
                read.MetadataPath = Path.Combine(directory, descriptor.WorkspaceId.ToString("N") + ".json");
                read.MetadataHandle = read.MetadataRoot.OpenRead(read.MetadataPath); DemandUid(read.MetadataHandle, read.OriginalUid);
                read.MetadataIdentity = ReadLinuxIdentity(read.MetadataHandle);
                read.FilesRoot = new OriginalLinuxPathLease(descriptor.ConfiguredFilesRoot);
                using (var parent = read.FilesRoot.OpenDirectory(descriptor.ConfiguredFilesRoot)) DemandUid(parent, read.OriginalUid);
                read.RegistrationHandle = read.FilesRoot.OpenRead(descriptor.OriginalRegistrationStatePath); DemandUid(read.RegistrationHandle, read.OriginalUid);
                read.RegistrationIdentity = ReadLinuxIdentity(read.RegistrationHandle);
                read.WorkingRoot = read.FilesRoot.OpenDirectory(descriptor.RegisteredProjectRoot); DemandUid(read.WorkingRoot, read.OriginalUid);
                read.WorkingIdentity = ReadLinuxIdentity(read.WorkingRoot); return true;
            });
            var metadata = await ReadBytes(read, read.MetadataHandle!, token).ConfigureAwait(false);
            var registration = await ReadBytes(read, read.RegistrationHandle!, token).ConfigureAwait(false);
            Invoke(work, () =>
            {
                using var document = JsonDocument.Parse(metadata);
                using var registrationDocument = JsonDocument.Parse(registration);
                using var expectedState = JsonDocument.Parse(descriptor.OriginalRegistrationStateJson);
                using var selected = JsonDocument.Parse(descriptor.OriginalSelectedRegistrationJson);
                if (!registrationDocument.RootElement.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1 ||
                    !registrationDocument.RootElement.TryGetProperty("state", out var state) || !JsonElement.DeepEquals(state, expectedState.RootElement) ||
                    !state.GetProperty("bindings").EnumerateArray().Any(value => JsonElement.DeepEquals(value, selected.RootElement)))
                    throw new UnauthorizedAccessException("The actual descriptor-backed registration state/selected project differ from current Files observations.");
                DemandDocument(document.RootElement, descriptor);
                read.Document = Encoding.UTF8.GetString(metadata); read.DocumentSha = Digest(metadata); read.RegistrationSha = Digest(registration);
                if (read.WindowsSid is not null) read.Fingerprint = WindowsFingerprint(read, selected.RootElement);
                else read.Fingerprint = Digest(JsonSerializer.SerializeToUtf8Bytes(new
                {
                    Registration = selected.RootElement, Root = new { read.WorkingIdentity.Inode, read.WorkingIdentity.DeviceMajor,
                        read.WorkingIdentity.DeviceMinor, read.WorkingIdentity.MountId }, descriptor.ConfiguredFilesRoot,
                    descriptor.RegisteredProjectRoot, read.OriginalUid
                }));
                if (descriptor.ExpectedWorkspaceDocumentSha256 is { } expectedSha && read.DocumentSha != expectedSha ||
                    descriptor.ExpectedRegisteredRootFingerprint is { } expectedRoot && read.Fingerprint != expectedRoot)
                    throw new UnauthorizedAccessException("Current project document/root no longer matches the authenticated detached identity.");
                return true;
            });
            await ObserveVoid(work, () => read.Issuer.RevalidateOriginalWithinSourceAsync(read.Selection, descriptor.OriginalActor,
                callback => Invoke(work, () => { callback(); return true; }), task => Retain(work, task), token)).ConfigureAwait(false);
            Invoke(work, () =>
            {
                token.ThrowIfCancellationRequested(); DemandNative(read);
                if (!ReferenceEquals(selections(), read.Issuer) || !read.Issuer.IsIssuedOriginalDescriptor(read.Selection, descriptor))
                    throw new UnauthorizedAccessException("The configured current project source changed before native publication.");
                lock (_gate) { if (_retiring || read.Sealed) throw new ObjectDisposedException("current project reconciliation"); read.Published = true; }
                return true;
            });
            return read;
        }

        private async Task<byte[]> ReadBytes(Read read, SafeFileHandle handle, CancellationToken token)
        {
            var work = read.Work; FileStream? stream = null; Task? actual = null;
            var errors = new List<Exception>(); byte[]? result = null; LinuxIdentity before = default; DeveloperWindowsIdentity? windowsBefore = null;
            try
            {
                Invoke(work, () =>
                {
                    if (read.WindowsSid is not null)
                    {
                        windowsBefore = ReadDeveloperWindowsIdentity(handle); DemandDeveloperWindowsOwner(handle, read.WindowsSid);
                        if (!windowsBefore.Value.IsRegular || windowsBefore.Value.Links != 1 || windowsBefore.Value.Size > 1024UL * 1024)
                            throw new UnauthorizedAccessException("The current Windows project document is not a bounded single-link regular file.");
                    }
                    else
                    {
                    before = ReadLinuxIdentity(handle); DemandUid(handle, read.OriginalUid);
                    if (!before.IsRegular || before.Links != 1 || before.Size > 1024UL * 1024)
                        throw new UnauthorizedAccessException("The current project document is not a bounded single-link regular file.");
                    }
                    // The private native owner retains the real handle. The stream owns only
                    // this nonowning wrapper, so stream retirement cannot retire that pin.
                    var wrapper = new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false);
                    try { stream = new FileStream(wrapper, FileAccess.Read, 4096, isAsync: false); }
                    catch { wrapper.Dispose(); throw; }
                    return true;
                });
                using var bytes = new MemoryStream(); var buffer = new byte[4096];
                while (true)
                {
                    Task<int>? raw = null; int count = 0; var stageErrors = new List<Exception>();
                    try { Invoke(work, () => { raw = stream!.ReadAsync(buffer.AsMemory(), token).AsTask(); actual = raw; Retain(work, raw); return true; }); }
                    catch (Exception error) { Add(stageErrors, error); }
                    if (raw is not null) try { count = await raw.ConfigureAwait(false); } catch (Exception error) { AddTask(stageErrors, raw, error); }
                    if (raw?.IsCanceled == true && !work.CallbackFailed && stageErrors.All(Canceled))
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stageErrors[0]).Throw();
                    Throw(stageErrors);
                    if (raw is null) throw new InvalidOperationException("No actual descriptor read Task was acquired.");
                    if (count == 0) break;
                    if (bytes.Length + count > 1024 * 1024) throw new InvalidDataException("The current project document exceeded the finite read limit.");
                    bytes.Write(buffer, 0, count);
                }
                Invoke(work, () =>
                {
                    if (windowsBefore is { } windows)
                    {
                        var observed = ReadDeveloperWindowsIdentity(handle); DemandDeveloperWindowsOwner(handle, read.WindowsSid!);
                        if (!observed.IsRegular || observed.Links != 1 || !windows.SameReadVersion(observed) || (ulong)bytes.Length != observed.Size)
                            throw new IOException("The actual current Windows project document changed during its native read.");
                    }
                    else
                    {
                    var after = ReadLinuxIdentity(handle); DemandUid(handle, read.OriginalUid);
                    if (!after.IsRegular || after.Links != 1 || !before.SameReadVersion(after) || (ulong)bytes.Length != after.Size)
                        throw new IOException("The actual current project document changed during its descriptor read.");
                    }
                    return true;
                });
                result = bytes.ToArray();
            }
            catch (Exception error) { AddTask(errors, actual, error); }
            finally
            {
                Task? close = null;
                if (stream is not null)
                {
                    try { OwnedCleanup(work, () => { close = stream.DisposeAsync().AsTask(); Retain(work, close); }); }
                    catch (Exception error) { Add(errors, error); }
                    if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, close, error); }
                }
            }
            if (actual?.IsCanceled == true && !work.CallbackFailed && errors.All(Canceled))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors); return result ?? throw new InvalidDataException("No actual current project bytes were read.");
        }
        private static void DemandDocument(JsonElement envelope, IDeveloperOriginalCurrentProjectDescriptor descriptor)
        {
            if (envelope.GetProperty("schemaVersion").GetInt32() != 1)
                throw new InvalidDataException("The saved workspace has an unsupported original schema.");
            var workspace = envelope.GetProperty("workspace");
            if (workspace.GetProperty("workspaceId").GetGuid() != descriptor.WorkspaceId ||
                workspace.GetProperty("revision").GetInt64() != descriptor.WorkspaceRevision)
                throw new UnauthorizedAccessException("The actual saved workspace identity/revision changed.");
            var projects = workspace.GetProperty("projects").EnumerateArray()
                .Where(value => value.GetProperty("projectId").GetGuid() == descriptor.ProjectId).Take(2).ToArray();
            var roots = workspace.GetProperty("roots").EnumerateArray()
                .Where(value => value.GetProperty("rootId").GetGuid() == descriptor.RootId).Take(2).ToArray();
            if (projects.Length != 1 || roots.Length != 1 ||
                projects[0].GetProperty("revision").GetInt64() != descriptor.ProjectRevision ||
                !projects[0].GetProperty("rootIds").EnumerateArray().Any(value => value.GetGuid() == descriptor.RootId) ||
                roots[0].GetProperty("environmentId").GetString() != "local" ||
                roots[0].GetProperty("location").GetString() != descriptor.RegisteredProjectRoot)
                throw new UnauthorizedAccessException("The actual saved project/root differs from the genuine registered project.");
            if (descriptor.RepositoryBindingId is { } binding)
            {
                var repositories = workspace.GetProperty("sourceControlBindings").EnumerateArray()
                    .Where(value => value.GetProperty("bindingId").GetString() == binding).Take(2).ToArray();
                if (repositories.Length != 1 || repositories[0].GetProperty("rootId").GetGuid() != descriptor.RootId)
                    throw new UnauthorizedAccessException("The saved repository binding differs from the selected actual root.");
            }
        }
        private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        private static void DemandUid(SafeFileHandle handle, uint uid)
        {
            var bytes = new byte[256];
            if (LinuxStatx(LinuxDescriptor(handle), "", 0x1000, 0x8, bytes) != 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError(), "Current project descriptor owner is unavailable.");
            if ((BitConverter.ToUInt32(bytes, 0) & 0x8) == 0)
                throw new PlatformNotSupportedException("The kernel did not return the consumed descriptor UID field.");
            if (BitConverter.ToUInt32(bytes, 20) != uid)
                throw new UnauthorizedAccessException("The actual current project descriptor belongs to another OS principal.");
        }
        private static void DemandNative(Read read)
        {
            lock (read.NativeGate)
            {
                if (read.WindowsSid is not null) { DemandWindowsNative(read); return; }
                if (read.NativeClose is not null || CurrentProjectGetEuid() != read.OriginalUid)
                    throw new ObjectDisposedException("current project native descriptors");
                read.MetadataRoot!.DemandCurrent(); read.FilesRoot!.DemandCurrent();
                DemandLinuxDescriptorPath(read.MetadataHandle!, read.MetadataPath);
                DemandLinuxDescriptorPath(read.RegistrationHandle!, read.RegistrationPath);
                DemandLinuxDescriptorPath(read.WorkingRoot!, read.WorkingPath);
                DemandUid(read.MetadataHandle!, read.OriginalUid); DemandUid(read.RegistrationHandle!, read.OriginalUid);
                DemandUid(read.WorkingRoot!, read.OriginalUid);
                var metadata = ReadLinuxIdentity(read.MetadataHandle!); var registration = ReadLinuxIdentity(read.RegistrationHandle!);
                var root = ReadLinuxIdentity(read.WorkingRoot!);
                if (!metadata.IsRegular || metadata.Links != 1 || !read.MetadataIdentity.SameReadVersion(metadata) ||
                    !registration.IsRegular || registration.Links != 1 || !read.RegistrationIdentity.SameReadVersion(registration) ||
                    !root.IsDirectory || !read.WorkingIdentity.SameFile(root))
                    throw new IOException("The actual saved document/registration/root descriptor pin changed.");
            }
        }
        private void Demand(Read read)
        {
            lock (_gate)
                if (_retiring || read.Sealed || !read.Published || !read.Work.Driver.IsCompletedSuccessfully || !_reads.Contains(read))
                    throw new ObjectDisposedException("current project original pin");
            DemandNative(read);
            lock (_gate)
                if (_retiring || read.Sealed) throw new ObjectDisposedException("current project original pin");
        }
        private bool Owned(IDeveloperOriginalCurrentProjectSelection selection, Task actual, IDeveloperOriginalCurrentProjectNativeRead value)
            => value is Read read && ReferenceEquals(read.Owner, this) && _reads.Contains(read) &&
                ReferenceEquals(read.Selection, selection) && ReferenceEquals(read.Work.Driver, actual);
        public bool IsOwnedOriginalRead(IDeveloperOriginalCurrentProjectSelection selection, Task actual, IDeveloperOriginalCurrentProjectNativeRead value)
        { lock (_gate) return Owned(selection, actual, value); }
        public bool IsIssuedOriginalRead(IDeveloperOriginalCurrentProjectSelection selection, Task actual, IDeveloperOriginalCurrentProjectNativeRead value)
        { lock (_gate) return Owned(selection, actual, value) && !_retiring && value is Read { Sealed: false, Published: true } read && read.Work.Driver.IsCompletedSuccessfully; }
        public bool IsClosedOriginalRead(IDeveloperOriginalCurrentProjectSelection selection, Task actual,
            IDeveloperOriginalCurrentProjectNativeRead value, Task close)
        { lock (_gate) return Owned(selection, actual, value) && value is Read read && read.Published && read.Work.Driver.IsCompletedSuccessfully &&
            ReferenceEquals(read.Close, close) && close.IsCompletedSuccessfully && read.NativeClose?.IsCompletedSuccessfully == true; }
        public Task ValidateOriginalReadWithinSourceAsync(IDeveloperOriginalCurrentProjectSelection selection, Task actual,
            IDeveloperOriginalCurrentProjectNativeRead value, Action<Action> scope, Action<Task> retain, CancellationToken token)
            => Start(scope, retain, async work =>
        {
            Read read;
            lock (_gate) { if (!Owned(selection, actual, value) || value is not Read issued) throw new UnauthorizedAccessException("Unknown original current project read."); read = issued; }
            Invoke(work, () => { if (!IsIssuedOriginalRead(selection, actual, value) || !ReferenceEquals(selections(), read.Issuer))
                throw new UnauthorizedAccessException("The actual current project native source/selection changed."); return true; });
            await ObserveVoid(work, () => read.Issuer.RevalidateOriginalWithinSourceAsync(selection, read.Descriptor.OriginalActor,
                callback => Invoke(work, () => { callback(); return true; }), task => Retain(work, task), token)).ConfigureAwait(false);
            Invoke(work, () => { token.ThrowIfCancellationRequested(); Demand(read); return true; }); return true;
        });
        private void OwnedCleanup(Work work, Action fixedPrivateCleanup)
        {
            var errors = new List<Exception>(); var attempted = false;
            try { Invoke(work, () => { attempted = true; fixedPrivateCleanup(); return true; }); }
            catch (Exception error) { Add(errors, error); }
            if (!attempted)
                try { Physical(fixedPrivateCleanup); } catch (Exception error) { Add(errors, error); }
            Throw(errors);
        }
        private Task CloseNative(Read read)
        {
            TaskCompletionSource start; Task close;
            lock (read.NativeGate)
            {
                if (read.NativeClose is not null) return read.NativeClose;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                read.NativeClose = close = DrainNative(start.Task, read);
            }
            start.TrySetResult(); return close;
        }
        private async Task DrainNative(Task start, Read read)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>();
            // These are fixed privately acquired descriptors only. Parent refusal cannot
            // suppress owed disposal, and refusal remains a direct close failure.
            foreach (var handle in new IDisposable?[] { read.WorkingRoot, read.RegistrationHandle,
                read.MetadataHandle, read.FilesRoot, read.MetadataRoot, read.WindowsFilesRoot, read.WindowsMetadataRoot })
                if (handle is not null)
                    try { OwnedCleanup(read.Work, handle.Dispose); } catch (Exception error) { Add(errors, error); }
            Throw(errors);
        }
        private Task CloseRead(Read read)
        {
            DemandExternalOriginalJoin(); TaskCompletionSource start; Task close;
            lock (_gate)
            {
                if (!ReferenceEquals(read.Owner, this) || !_reads.Contains(read)) throw new UnauthorizedAccessException("Unknown project descriptor close.");
                read.Sealed = true;
                if (read.Close is not null) return read.Close;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously); read.Close = close = DrainRead(start.Task, read);
            }
            start.TrySetResult(); return close;
        }
        private async Task DrainRead(Task start, Read read)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>();
            try { await read.Work.Driver.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, read.Work.Driver, error); }
            var native = CloseNative(read); lock (_gate) read.Work.Sources.Add(native);
            try { await native.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, native, error); }
            Task[] tasks; lock (_gate) tasks = read.Work.Sources.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var task in tasks) try { await task.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, task, error); }
            Throw(errors);
        }
        public void RequestOriginalRetirement() { lock (_gate) _retiring = true; }
        public void DemandExternalOriginalJoin()
        {
            if (_physical?.ContainsKey(this) == true) throw new InvalidOperationException("A current-project native callback cannot join its original owner.");
            for (var work = _executing.Value; work is not null; work = work.Parent)
                if (work.Live) throw new InvalidOperationException("A current-project native original cannot join its encompassing close.");
        }
        public Task CloseAndDrainOriginalAsync()
        {
            DemandExternalOriginalJoin(); TaskCompletionSource start; Task close;
            lock (_gate)
            {
                _retiring = true;
                if (_close is not null) return _close;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = close = Drain(start.Task);
            }
            start.TrySetResult(); return close;
        }
        private async Task Drain(Task start)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>(); Read[] reads;
            lock (_gate) reads = _reads.ToArray(); var closes = new List<Task>();
            foreach (var read in reads) try { closes.Add(CloseRead(read)); } catch (Exception error) { Add(errors, error); }
            Work[] work; lock (_gate) work = _work.ToArray();
            foreach (var item in work) try { await item.Driver.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, item.Driver, error); }
            foreach (var close in closes) try { await close.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, close, error); }
            foreach (var item in work)
            {
                Task[] raw; lock (_gate) raw = item.Sources.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
                foreach (var task in raw) try { await task.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, task, error); }
            }
            Throw(errors);
        }
        private static void Add(List<Exception> errors, Exception error)
        {
            if (error is AggregateException { InnerExceptions.Count: > 0 } group)
                foreach (var cause in group.InnerExceptions) Add(errors, cause);
            else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
        }
        private static void AddTask(List<Exception> errors, Task? actual, Exception caught)
        { Add(errors, caught); if (actual?.IsFaulted == true) foreach (var cause in actual.Exception!.InnerExceptions) Add(errors, cause); }
        private static bool Canceled(Exception error) => error is OperationCanceledException;
        private static void Throw(List<Exception> errors)
        { if (errors.Count != 0) throw new AggregateException("Current project descriptor original/cleanup did not settle.", errors); }
    }
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint CurrentProjectGetEuid();
}
