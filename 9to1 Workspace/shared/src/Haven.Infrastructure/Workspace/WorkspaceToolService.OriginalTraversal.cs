using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService : IWorkspaceOriginalTraversalSource
{
    public bool SupportsOriginalTraversal(string toolName) => _originalAuthority is not null &&
        toolName is ("list_files" or "search_files") && (OperatingSystem.IsWindows() ||
        OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture is (Architecture.X64 or Architecture.Arm64));

    private const int TraversalEntryLimit = 2000, TraversalValidationLimit = 48;
    private const int TraversalFileBytes = 2 * 1024 * 1024, TraversalTotalBytes = 16 * 1024 * 1024;
    private static readonly HashSet<string> TraversalIgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".svn", ".hg", ".vs", ".idea", "node_modules", "bin", "obj", "dist", "build", "target", ".venv", "venv" };

    private sealed class TraversalState(bool search, int depth, string? query, int matchLimit)
    {
        public readonly List<WorkspaceOriginalDirectoryEntry> Entries = [];
        public readonly List<WorkspaceOriginalTextMatch> Matches = [];
        public bool Search { get; } = search;
        public int Depth { get; } = depth;
        public string? Query { get; } = query;
        public int MatchLimit { get; } = matchLimit;
        public int EnumeratedEntries, Validations, ActiveDirectories, Ignored, Large, Binary;
        public long ReadBytes;
        public bool OriginalCanceled;
        public bool EntryLimit, DepthLimit, MatchLimitReached, ByteLimit, ValidationLimit;
        public bool Stop => MatchLimitReached || ByteLimit || ValidationLimit;
        public WorkspaceOriginalTraversalResult Result() => new(Array.AsReadOnly(Entries.ToArray()), Array.AsReadOnly(Matches.ToArray()),
            EntryLimit, DepthLimit, MatchLimitReached, ByteLimit, Ignored, Large, Binary, ValidationLimit);
    }

    private sealed partial class Invocation : IWorkspaceOriginalTraversalService
    {
        private Task<WorkspaceOriginalTraversalResult>? _originalTraversal;
        public Task<WorkspaceOriginalTraversalResult> ListOriginalFilesAsync(string root, string path, int maxDepth, CancellationToken token)
        {
            DemandTraversalCall(root, path, "list_files");
            if (maxDepth != Integer("max_depth", 5)) throw new UnauthorizedAccessException("The original listing depth changed.");
            return StartOriginalTraversal(new(false, Math.Clamp(maxDepth, 1, 10), null, 0), token);
        }
        public Task<WorkspaceOriginalTraversalResult> SearchOriginalFilesAsync(string root, string path, string query, int maxResults, CancellationToken token)
        {
            DemandTraversalCall(root, path, "search_files");
            if (query != RequiredText("query") || maxResults != Integer("max_results", 100))
                throw new UnauthorizedAccessException("The original search query or match limit changed.");
            if (string.IsNullOrWhiteSpace(query) || query.Length > 4096)
                throw new ArgumentException("The original literal search query requires 1 to 4096 characters.");
            return StartOriginalTraversal(new(true, 10, query, Math.Clamp(maxResults, 1, 200)), token);
        }
        private void DemandTraversalCall(string root, string path, string name)
        {
            DemandRoot(root);
            if (_call.Name != name || path != Text("path", ".") || Fence is not IWorkspaceOriginalReadFence ||
                !Owner.SupportsOriginalTraversal(name))
                throw new UnauthorizedAccessException("SAME issued traversal call, folder and read fence required.");
            _ = ResolveDeclared(path);
        }
        private Task<WorkspaceOriginalTraversalResult> StartOriginalTraversal(TraversalState state, CancellationToken token)
        {
            TaskCompletionSource start;
            Task<WorkspaceOriginalTraversalResult> original;
            lock (_gate)
            {
                if (_originalTraversal is not null) return _originalTraversal;
                if (_sealed) throw new ObjectDisposedException(nameof(Invocation));
                // Own first registers the SAME raw operation; the unresolved barrier prevents
                // native/authority callbacks from executing under this publication lock.
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                original = Own(async ct =>
                {
                    await start.Task.ConfigureAwait(false);
                    return await TraverseOriginalAsync(state, ct).ConfigureAwait(false);
                }, token);
                _originalTraversal = original;
            }
            start.SetResult();
            return original;
        }
        private T ReadGate<T>(string target, Func<T> finiteNativeRead, CancellationToken token)
        {
            DemandIssued();
            return ((IWorkspaceOriginalReadFence)Fence).RunOriginalRead(Root, target, finiteNativeRead, token);
        }
        private async Task ValidateTraversalAsync(TraversalState state, CancellationToken token)
        {
            state.Validations++;
            Task original;
            try { original = ((IWorkspaceOriginalReadFence)Fence).RevalidateOriginalReadAsync(token).AsTask(); }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested)
            { throw new AggregateException("Synchronous original read validation source fault.", error); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { state.OriginalCanceled = true; throw; }
            try { await original.ConfigureAwait(false); }
            catch (Exception) when (original.IsFaulted)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original.Exception!).Throw(); throw; }
            catch (OperationCanceledException) when (original.IsCanceled)
            { state.OriginalCanceled = true; throw; }
            DemandIssued();
        }
        private async Task<bool> TryValidateTraversalAsync(TraversalState state, CancellationToken token)
        {
            // Canonical action custody is finite (64 originals). Reserve one final validation
            // and one for every held directory's exit, leaving the owning execution its reserve.
            if (state.Validations + state.ActiveDirectories + 2 >= TraversalValidationLimit)
            { state.ValidationLimit = true; return false; }
            await ValidateTraversalAsync(state, token).ConfigureAwait(false); return true;
        }
        private async Task<WorkspaceOriginalTraversalResult> TraverseOriginalAsync(TraversalState state, CancellationToken token)
        {
            IAsyncDisposable? pin = null; Task? source = null; WorkspaceOriginalTraversalResult? result = null;
            var failures = new List<Exception>();
            try
            {
                source = ((IWorkspaceOriginalReadFence)Fence).AcquireOriginalCommitPinAsync(token).AsTask();
                pin = await ((Task<IAsyncDisposable?>)source).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The actual original read retirement pin is unavailable.");
                await VisitTraversalDirectoryAsync(ResolveDeclared(Text("path", ".")), 0, state, token).ConfigureAwait(false);
                await ValidateTraversalAsync(state, token).ConfigureAwait(false);
                result = ReadGate(ResolveDeclared(Text("path", ".")), () =>
                {
                    _linuxRoot?.DemandCurrent(); _physicalRoot?.DemandCurrent(); return state.Result();
                }, token);
            }
            catch (Exception error)
            {
                if (error is OperationCanceledException && token.IsCancellationRequested && source?.IsFaulted != true)
                    state.OriginalCanceled = true;
                AddOriginalErrors(failures, source, error);
            }
            finally
            {
                Task? close = null;
                try { if (pin is not null) { close = pin.DisposeAsync().AsTask(); await close.ConfigureAwait(false); } }
                catch (Exception error) { AddOriginalErrors(failures, close, error); }
            }
            // A close/cancel arriving while independent pin cleanup was held must not
            // publish the already private read as a newly successful observation.
            if (failures.Count == 0 && token.IsCancellationRequested)
            { state.OriginalCanceled = true; failures.Add(new OperationCanceledException(token)); }
            ThrowOriginalErrors(failures, source?.IsCanceled == true || state.OriginalCanceled);
            return result ?? throw new InvalidDataException("The original traversal did not complete its read observation.");
        }
        private async Task VisitTraversalDirectoryAsync(string path, int depth, TraversalState state, CancellationToken token)
        {
            if (state.Stop || !await TryValidateTraversalAsync(state, token).ConfigureAwait(false)) return;
            SafeFileHandle? directory = null; IEnumerator<string>? names = null;
            var errors = new List<Exception>();
            var held = false;
            try
            {
                directory = ReadGate(path, () => _linuxRoot is not null ? _linuxRoot.OpenDirectory(path)
                    : _physicalRoot?.OpenTraversalDirectory(path) ?? throw new PlatformNotSupportedException("No original traversal directory owner."), token);
                state.ActiveDirectories++; held = true;
                var linuxBefore = _linuxRoot is not null ? ReadGate(path, () => ReadLinuxIdentity(directory), token) : default;
                var windowsBefore = _physicalRoot is not null ? ReadGate(path, () =>
                {
                    var caseInfo = new byte[4];
                    if (!GetFileInformationByHandleEx(directory, 23, caseInfo, 4) || BitConverter.ToUInt32(caseInfo, 0) != 0)
                        throw new PlatformNotSupportedException("Original Windows traversal requires observed case-insensitive directory semantics.");
                    return ReadTraversalWindowsIdentity(directory);
                }, token) : default;
                var observedNames = new List<string>();
                if (_linuxRoot is not null)
                {
                    // Only names come from /proc/fd. Every child kind/content is opened again
                    // relative to this SAME retained directory with openat2's no-redirection fence.
                    var anchored = $"/proc/{Environment.ProcessId}/fd/{LinuxDescriptor(directory)}";
                    names = ReadGate(path, () => Directory.EnumerateFileSystemEntries(anchored).GetEnumerator(), token);
                    while (ReadGate(path, names.MoveNext, token))
                    {
                        var name = ReadGate(path, () => Path.GetFileName(names.Current), token);
                        DemandTraversalLeaf(name);
                        if (state.EnumeratedEntries == TraversalEntryLimit) { state.EntryLimit = true; break; }
                        state.EnumeratedEntries++; observedNames.Add(name);
                    }
                }
                else
                    ReadWindowsTraversalNames(directory, path, observedNames, state, token);
                if (observedNames.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).Count() != observedNames.Count)
                    throw new IOException("The original directory enumeration repeated a child name.");
                foreach (var name in observedNames.Order(StringComparer.Ordinal))
                {
                    if (state.Stop || !await TryValidateTraversalAsync(state, token).ConfigureAwait(false)) break;
                    var child = Path.Combine(path, name);
                    var relative = Path.GetRelativePath(Root, child).Replace(Path.DirectorySeparatorChar, '/');
                    SafeFileHandle? metadata = null;
                    var childErrors = new List<Exception>();
                    try
                    {
                        metadata = ReadGate(child, () => _linuxRoot is not null ? _linuxRoot.OpenTraversalChild(directory, name, child)
                            : _physicalRoot!.OpenTraversalEntry(child), token);
                        var linux = _linuxRoot is not null ? ReadGate(child, () => ReadLinuxIdentity(metadata), token) : default;
                        var windows = _physicalRoot is not null ? ReadGate(child, () => ReadTraversalWindowsIdentity(metadata), token) : default;
                        var isDirectory = _linuxRoot is not null ? linux.IsDirectory : windows.IsDirectory;
                        var size = _linuxRoot is not null ? checked((long)linux.Size) : windows.Size;
                        if (isDirectory)
                        {
                            if (TraversalIgnoredDirectories.Contains(name)) state.Ignored++;
                            else
                            {
                                if (!state.Search) state.Entries.Add(new(relative, true, 0));
                                if (depth + 1 < state.Depth)
                                    await VisitTraversalDirectoryAsync(child, depth + 1, state, token).ConfigureAwait(false);
                                else state.DepthLimit = true;
                            }
                        }
                        else if (!state.Search) state.Entries.Add(new(relative, false, size));
                        else if (size > TraversalFileBytes) state.Large++;
                        else if (state.ReadBytes + size > TraversalTotalBytes) state.ByteLimit = true;
                        else
                        {
                            var content = await ReadTraversalFileAsync(directory, name, child, linux, windows, checked((int)size), state, token).ConfigureAwait(false);
                            state.ReadBytes += size;
                            if (content is null) state.Binary++;
                            else
                            {
                                using var lines = new StringReader(content);
                                int number = 0; string? line;
                                while ((line = lines.ReadLine()) is not null)
                                {
                                    number++;
                                    if (!line.Contains(state.Query!, StringComparison.OrdinalIgnoreCase)) continue;
                                    var shown = line.Trim();
                                    if (shown.Length > 300) shown = shown[..300] + " [line truncated]";
                                    state.Matches.Add(new(relative, number, shown));
                                    if (state.Matches.Count == state.MatchLimit) { state.MatchLimitReached = true; break; }
                                }
                            }
                        }
                    }
                    catch (Exception error) { AddOriginalErrors(childErrors, null, error); }
                    finally
                    {
                        try { metadata?.Dispose(); } catch (Exception error) { AddOriginalErrors(childErrors, null, error); }
                    }
                    ThrowOriginalErrors(childErrors, state.OriginalCanceled);
                }
                await ValidateTraversalAsync(state, token).ConfigureAwait(false);
                ReadGate(path, () =>
                {
                    if (_linuxRoot is not null)
                    {
                        _linuxRoot.DemandCurrent(); DemandLinuxDescriptorPath(directory, path);
                        if (!linuxBefore.SameReadVersion(ReadLinuxIdentity(directory)))
                            throw new IOException("The original directory changed during traversal.");
                    }
                    else
                    {
                        _physicalRoot!.DemandCurrent(); DemandExactHandlePath(directory, path, true);
                        if (!windowsBefore.SameReadVersion(ReadTraversalWindowsIdentity(directory)))
                            throw new IOException("The original directory changed during traversal.");
                    }
                    return true;
                }, token);
            }
            catch (Exception error)
            {
                if (error is OperationCanceledException && token.IsCancellationRequested) state.OriginalCanceled = true;
                AddOriginalErrors(errors, null, error);
            }
            finally
            {
                if (held) state.ActiveDirectories--;
                // Cleanup is independent of revoked policy/canceled tokens and always joined.
                try { names?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                try { directory?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors, state.OriginalCanceled);
        }
        private async Task<string?> ReadTraversalFileAsync(SafeFileHandle directory, string name, string path,
            LinuxIdentity linuxBefore, TraversalWindowsIdentity windowsBefore, int size, TraversalState state, CancellationToken token)
        {
            SafeFileHandle? handle = null; FileStream? stream = null; Task? read = null;
            var errors = new List<Exception>(); string? text = null;
            try
            {
                handle = ReadGate(path, () => _linuxRoot is not null
                    ? _linuxRoot.OpenTraversalRead(directory, name, path, linuxBefore) : _physicalRoot!.OpenOriginalRead(path), token);
                ReadGate(path, () =>
                {
                    if (_physicalRoot is not null && !windowsBefore.SameReadVersion(ReadTraversalWindowsIdentity(handle)))
                        throw new IOException("The original file changed before its held-handle read.");
                    return true;
                }, token);
                stream = ReadGate(path, () => new FileStream(handle, FileAccess.Read, 4096, isAsync: _physicalRoot is not null), token);
                var bytes = new byte[size];
                read = ReadGate(path, () => stream.ReadExactlyAsync(bytes.AsMemory(), token).AsTask(), token);
                await read.ConfigureAwait(false);
                // Every byte remains private until fresh canonical action/actor/model and the
                // same central permission fence admit the final SAME-handle observation.
                await ValidateTraversalAsync(state, token).ConfigureAwait(false);
                ReadGate(path, () =>
                {
                    if (_linuxRoot is not null)
                    {
                        _linuxRoot.DemandCurrent(); DemandLinuxDescriptorPath(handle, path);
                        var after = ReadLinuxIdentity(handle);
                        if (!after.IsRegular || after.Links != 1 || !linuxBefore.SameReadVersion(after))
                            throw new IOException("The original file changed during its held-handle read.");
                    }
                    else
                    {
                        _physicalRoot!.DemandCurrent(); DemandExactHandlePath(handle, path, false);
                        if (!windowsBefore.SameReadVersion(ReadTraversalWindowsIdentity(handle)))
                            throw new IOException("The original file changed during its held-handle read.");
                    }
                    return true;
                }, token);
                try
                {
                    text = new UTF8Encoding(false, true).GetString(bytes);
                    if (text.StartsWith('\uFEFF')) text = text[1..];
                    if (text.Contains('\0')) text = null;
                }
                catch (DecoderFallbackException) { text = null; } // explicit binary exclusion, never an I/O failure skip
            }
            catch (Exception error)
            {
                if (read?.IsCanceled == true || error is OperationCanceledException && token.IsCancellationRequested && read?.IsFaulted != true)
                    state.OriginalCanceled = true;
                AddOriginalErrors(errors, read, error);
            }
            finally
            {
                Task? close = null;
                try { if (stream is not null) { close = stream.DisposeAsync().AsTask(); await close.ConfigureAwait(false); } }
                catch (Exception error) { AddOriginalErrors(errors, close, error); }
                try { handle?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors, read?.IsCanceled == true || state.OriginalCanceled);
            return text;
        }
        private void ReadWindowsTraversalNames(SafeFileHandle directory, string path, List<string> names,
            TraversalState state, CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            var restart = true;
            while (true)
            {
                var more = ReadGate(path, () =>
                {
                    // FILE_FULL_DIR_INFO, classes 15 (restart) / 14 (continue): fixed
                    // 68-byte prefix followed by an even bounded UTF-16 filename.
                    Array.Clear(buffer);
                    if (GetFileInformationByHandleEx(directory, restart ? 15 : 14, buffer, (uint)buffer.Length)) return true;
                    var error = Marshal.GetLastPInvokeError();
                    if (error == 18) return false; // ERROR_NO_MORE_FILES, the only complete terminator
                    if (error is 1 or 50 or 87) throw new PlatformNotSupportedException("The Windows filesystem has no supported retained directory-record API.");
                    throw new Win32Exception(error, "The original Windows directory record could not be read.");
                }, token);
                restart = false;
                if (!more) return;
                int offset = 0;
                while (true)
                {
                    if (offset < 0 || offset > buffer.Length - 68) throw new InvalidDataException("The original directory record prefix is invalid.");
                    var next = BitConverter.ToUInt32(buffer, offset);
                    var length = BitConverter.ToUInt32(buffer, offset + 60);
                    var available = next == 0 ? buffer.Length - offset : checked((int)next);
                    if (available < 68 || available > buffer.Length - offset || next != 0 && next % 8 != 0 ||
                        length == 0 || length > 510 || length % 2 != 0 || length > available - 68)
                        throw new InvalidDataException("The original directory filename record is unsupported or incomplete.");
                    var name = new UnicodeEncoding(false, false, true).GetString(buffer, offset + 68, checked((int)length));
                    if (name is not ("." or ".."))
                    {
                        DemandTraversalLeaf(name);
                        if (state.EnumeratedEntries == TraversalEntryLimit) { state.EntryLimit = true; return; }
                        state.EnumeratedEntries++; names.Add(name);
                    }
                    if (next == 0) break;
                    offset = checked(offset + (int)next);
                }
            }
        }
    }

    private static void DemandTraversalLeaf(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name is "." or ".." ||
            name.Any(value => char.IsControl(value) || value is '/' or '\\' or ':' or '\uFFFD') ||
            OperatingSystem.IsWindows() && (name.EndsWith('.') || name.EndsWith(' ')))
            throw new InvalidDataException("The original traversal entry is not a direct unambiguous bounded child name.");
    }
    private readonly record struct TraversalWindowsIdentity(ulong Volume, ulong FileLow, ulong FileHigh,
        long Size, uint Links, bool IsDirectory, long Modified, long Changed)
    {
        public bool SameReadVersion(TraversalWindowsIdentity value) => Volume == value.Volume && FileLow == value.FileLow &&
            FileHigh == value.FileHigh && Size == value.Size && Links == value.Links && IsDirectory == value.IsDirectory &&
            Modified == value.Modified && Changed == value.Changed;
    }
    private static TraversalWindowsIdentity ReadTraversalWindowsIdentity(SafeFileHandle handle)
    {
        static byte[] Observe(SafeFileHandle original, int kind, int length)
        {
            var bytes = new byte[length];
            if (!GetFileInformationByHandleEx(original, kind, bytes, (uint)length))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original Windows file identity is unavailable.");
            return bytes;
        }
        var standard = Observe(handle, 1, 24); // FILE_STANDARD_INFO
        var id = Observe(handle, 18, 24); // FILE_ID_INFO: volume + 128-bit ID
        var basic = Observe(handle, 0, 40); // FILE_BASIC_INFO: creation/access/write/change + attributes
        var size = BitConverter.ToInt64(standard, 8); var links = BitConverter.ToUInt32(standard, 16);
        var directory = standard[21] != 0;
        if (size < 0 || standard[20] != 0 || !directory && links != 1 ||
            ((FileAttributes)BitConverter.ToUInt32(basic, 32) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("The original Windows entry is deleted, redirected, aliased or has invalid size.");
        return new(BitConverter.ToUInt64(id, 0), BitConverter.ToUInt64(id, 8), BitConverter.ToUInt64(id, 16),
            size, links, directory, BitConverter.ToInt64(basic, 16), BitConverter.ToInt64(basic, 24));
    }
}
