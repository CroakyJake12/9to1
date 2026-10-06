using System.Text;
using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    public IWorkspaceOriginalInvocation AcquireOriginalInvocation(IWorkspaceToolFinalFence originalFence)
    {
        ArgumentNullException.ThrowIfNull(originalFence);
        if (_originalAuthority is null || !_originalAuthority.IsIssuedOriginal(originalFence))
            throw new UnauthorizedAccessException("The original workspace fence was not issued by this configured authority.");
        return new Invocation(this, originalFence);
    }

    public bool IsIssuedOriginal(IWorkspaceOriginalInvocation originalInvocation) =>
        originalInvocation is Invocation actual && ReferenceEquals(actual.Owner, this);

    public bool ValidateOriginalOutcome(IWorkspaceOriginalInvocation originalInvocation, WorkspaceToolPhysicalOutcome originalOutcome) =>
        originalInvocation is Invocation actual && ReferenceEquals(actual.Owner, this) && actual.OwnsOutcome(originalOutcome);

    private sealed class OriginalEffect(WorkspaceToolEffectKind kind, string target, string digest)
    {
        public bool Eligible { get; set; }
        public WorkspaceToolPhysicalEffect Observation { get; set; } = new(kind, target, digest, false, false, false);
    }

    /// <summary>One privately issued original call over the existing physical service. Its retained
    /// operations are joined before outcome publication. Persisted IDs, hashes and public interfaces
    /// cannot manufacture this source's original invocation or an accepted physical receipt.</summary>
    private sealed partial class Invocation : IWorkspaceOriginalInvocation, IWorkspaceOriginalRollbackService
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _lifetime = new();
        private sealed record Operation(TaskCompletionSource<Task> Original, bool ExpectedReadAbsence);
        private readonly List<Operation> _operations = [];
        private readonly List<OriginalEffect> _effects = [];
        private readonly List<Exception> _errors = [];
        private readonly Dictionary<string, string> _readPreimages;
        private readonly HashSet<string> _reservedWrites;
        private readonly Dictionary<string, string> _declaredWrites;
        private readonly Dictionary<string, string> _expectedPreimageHashes;
        private readonly OllamaToolCall _call;
        private readonly string _callDigest;
        private Task? _close;
        private bool _sealed;
        private bool _processReserved;
        private readonly OriginalWindowsPathLease? _physicalRoot;
        private readonly OriginalLinuxPathLease? _linuxRoot;
        private WorkspaceToolPhysicalOutcome? _outcome;
        private bool? _completedBody;
        public WorkspaceToolService Owner { get; }
        public IWorkspaceToolFinalFence Fence { get; }
        public string Root { get; }
        public IWorkspaceToolService Tools => this;

        public Invocation(WorkspaceToolService owner, IWorkspaceToolFinalFence fence)
        {
            Owner = owner;
            Fence = fence;
            Root = owner.ResolveWorkspacePath(fence.CanonicalWorkspaceRoot, ".");
            if (!Path.IsPathFullyQualified(Root)) throw new PlatformNotSupportedException("This original adapter requires a full physical workspace directory, not a drive-relative or volume alias.");
            RequireDirectOriginalPath(Root);
            if (!Directory.Exists(Root)) throw new DirectoryNotFoundException("The original workspace root is unavailable.");
            _call = fence.OriginalCall with
            {
                Arguments = new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonElement>(
                    fence.OriginalCall.Arguments.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal))
            };
            _callDigest = WorkspaceToolOriginalDigest.Call(_call);
            var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            _readPreimages = new(comparer);
            _reservedWrites = new(comparer);
            _declaredWrites = new(comparer);
            _expectedPreimageHashes = new(comparer);
            if (_call.Name == "write_file") _declaredWrites.Add(ResolveDeclared(RequiredText("path")), Text("content"));
            else if (_call.Name == "replace_in_file") _declaredWrites.Add(ResolveDeclared(RequiredText("path")), string.Empty);
            else if (_call.Name is "apply_change_set" or "preview_change_set")
            {
                var entries = JsonSerializer.Deserialize<DeclaredChange[]>(RequiredText("changes_json"),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new ArgumentException("A changes array is required.");
                if (entries.Length is < 1 or > 50) throw new ArgumentException("The original change set requires 1 to 50 entries.");
                foreach (var entry in entries)
                {
                    if (entry.Content is null || entry.Content.Length > 2_000_000) throw new ArgumentException("Invalid original change content.");
                    var target = ResolveDeclared(entry.Path);
                    _declaredWrites.Add(target, entry.Content);
                    if (!string.IsNullOrWhiteSpace(entry.ExpectedSha256))
                    {
                        var hash = entry.ExpectedSha256.Trim();
                        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid original expected preimage hash.");
                        _expectedPreimageHashes.Add(target, hash);
                    }
                }
            }
            else if (_call.Name is "list_files" or "search_files")
                throw new PlatformNotSupportedException("Original directory traversal requires an owning safe per-child traversal port; ordinary local tools remain available.");
            else if (_call.Name is not ("read_file" or "run_command" or "run_tests"))
                throw new UnauthorizedAccessException("This original call is not an implemented workspace tool.");
            if (OperatingSystem.IsWindows()) _physicalRoot = new OriginalWindowsPathLease(Root);
            else if (OperatingSystem.IsLinux()) _linuxRoot = new OriginalLinuxPathLease(Root);
        }

        public void DemandIssued()
        {
            if (Owner._originalAuthority is null || !Owner._originalAuthority.IsIssuedOriginal(Fence) ||
                !string.Equals(Owner.ResolveWorkspacePath(Fence.CanonicalWorkspaceRoot, "."), Root, PathComparison) ||
                !string.Equals(WorkspaceToolOriginalDigest.Call(Fence.OriginalCall), _callDigest, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The original workspace issuer, root or captured call is no longer current.");
        }

        private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private string ResolveDeclared(string path)
        {
            var actual = Owner.ResolveWorkspacePath(Root, path);
            RequireDirectOriginalPath(actual);
            return actual;
        }

        // Matches the current NativeFiles owner preflight: direct ancestors only. This check
        // rejects present redirections; retained Windows parent handles supply exclusion below.
        private static void RequireDirectOriginalPath(string path)
        {
            for (string? current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); current is not null; current = Path.GetDirectoryName(current))
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new UnauthorizedAccessException("The original workspace path redirects through a reparse point or symbolic link.");
                }
                catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException) { }
        }
        private void DemandRoot(string root)
        {
            DemandIssued();
            if (!string.Equals(Owner.ResolveWorkspacePath(root, "."), Root, PathComparison))
                throw new UnauthorizedAccessException("The physical operation is for a different original workspace.");
        }

        public string ResolveWorkspacePath(string workspaceRoot, string relativePath)
        {
            DemandRoot(workspaceRoot);
            lock (_gate) if (_sealed) throw new ObjectDisposedException(nameof(Invocation));
            var path = ResolveDeclared(relativePath);
            var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
            if (_physicalRoot is not null) _physicalRoot.EnsureDirectory(directory, null);
            else if (_linuxRoot is not null) _linuxRoot.EnsureDirectory(directory);
            else throw new PlatformNotSupportedException("The original physical path requires a supported retained directory owner.");
            return path;
        }

        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken)
        {
            DemandRoot(workspaceRoot);
            var path = ResolveDeclared(relativePath);
            var expectedAbsence = _declaredWrites.ContainsKey(path) && _call.Name != "preview_change_set";
            if (!_declaredWrites.ContainsKey(path) && !(_call.Name == "read_file" && string.Equals(path, ResolveDeclared(RequiredText("path")), PathComparison)))
                throw new UnauthorizedAccessException("The read target is not declared by the original call.");
            return Own(async token =>
            {
                var content = await ReadOriginalTextAsync(path, token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_readPreimages.TryGetValue(path, out var previous) && !string.Equals(previous, content, StringComparison.Ordinal))
                        throw new IOException("The original workspace preimage changed during this invocation.");
                    _readPreimages[path] = content;
                }
                return content;
            }, cancellationToken, expectedAbsence);
        }

        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) =>
            throw new PlatformNotSupportedException("The original invocation has no safe per-child traversal owner.");

        public Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken) =>
            Own(async token =>
            {
                await Owner.WriteTextAtomicCoreAsync(workspaceRoot, relativePath, content, this, token).ConfigureAwait(false);
                return true;
            }, cancellationToken);

        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            Own(token => Owner.RunProcessCoreAsync(request, this, token), cancellationToken);

        public Task DeleteOriginalCreatedFileAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken) =>
            Own<bool>(token =>
            {
                token.ThrowIfCancellationRequested();
                DemandRoot(workspaceRoot);
                _ = Owner.ResolveWorkspacePath(Root, relativePath);
                // File.Move(overwrite:true), File.Exists and a content hash do not prove the SAME
                // current filesystem identity. Never use legacy direct-delete as a fallback.
                throw new InvalidOperationException("Rollback deletion requires retained native file identity and exclusion; this source cannot certify them.");
            }, cancellationToken);

        public async Task<OriginalEffect> PrepareWriteAsync(string root, string path, string content, CancellationToken token)
        {
            DemandRoot(root);
            RequireDirectOriginalPath(path);
            if (_call.Name is not ("write_file" or "replace_in_file" or "apply_change_set"))
                throw new UnauthorizedAccessException("The original call is read-only.");
            string expected;
            string? preimage = null;
            lock (_gate)
            {
                if (!_declaredWrites.TryGetValue(path, out expected!))
                    throw new UnauthorizedAccessException("The write target is not declared by this original call.");
                if (_reservedWrites.Contains(path))
                    throw new InvalidOperationException("An original write cannot be replayed or repurposed as rollback.");
                if (_call.Name == "replace_in_file")
                {
                    if (!_readPreimages.TryGetValue(path, out preimage))
                        throw new InvalidOperationException("The actual original replace read was not retained.");
                    var oldText = RequiredText("old_text");
                    var index = preimage.IndexOf(oldText, StringComparison.Ordinal);
                    if (index < 0) throw new InvalidOperationException("The original old_text was not present.");
                    var newText = Text("new_text");
                    expected = Boolean("replace_all") ? preimage.Replace(oldText, newText, StringComparison.Ordinal)
                        : string.Concat(preimage.AsSpan(0, index), newText, preimage.AsSpan(index + oldText.Length));
                }
                if (!string.Equals(expected, content, StringComparison.Ordinal))
                    throw new UnauthorizedAccessException("The write content does not match the original call and retained preimage.");
                _reservedWrites.Add(path);
            }
            // This verifies the owning original preimage before the final native fence; it does
            // not claim an interprocess filesystem CAS or a rollback identity witness.
            if (preimage is not null && !string.Equals(await ReadOriginalTextAsync(path, token).ConfigureAwait(false), preimage, StringComparison.Ordinal))
                throw new IOException("The original replace preimage is no longer current.");
            if (_expectedPreimageHashes.TryGetValue(path, out var expectedHash))
            {
                string actualBefore;
                try { actualBefore = await ReadOriginalTextAsync(path, token).ConfigureAwait(false); }
                catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException) { actualBefore = string.Empty; }
                if (!string.Equals(expectedHash, WorkspaceToolOriginalDigest.Text(actualBefore), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The original change-set expected preimage is no longer current.");
            }
            var effect = new OriginalEffect(WorkspaceToolEffectKind.AtomicWrite, path, WorkspaceToolOriginalDigest.Text(content));
            lock (_gate) _effects.Add(effect);
            return effect;
        }

        public async Task RevalidateWriteAsync(string path, CancellationToken token)
        {
            DemandIssued();
            RequireDirectOriginalPath(path);
            string? preimage;
            lock (_gate) _readPreimages.TryGetValue(path, out preimage);
            if (_call.Name == "replace_in_file" && (preimage is null ||
                !string.Equals(await ReadOriginalTextAsync(path, token).ConfigureAwait(false), preimage, StringComparison.Ordinal)))
                throw new IOException("The original replace preimage changed while staging.");
            if (_expectedPreimageHashes.TryGetValue(path, out var expectedHash))
            {
                string current;
                try { current = await ReadOriginalTextAsync(path, token).ConfigureAwait(false); }
                catch (Exception missing) when (missing is FileNotFoundException or DirectoryNotFoundException) { current = string.Empty; }
                if (!string.Equals(expectedHash, WorkspaceToolOriginalDigest.Text(current), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The original change-set preimage changed while staging.");
            }
        }

        public void EnsureOriginalWriteParent(string path, Action<string> createOriginal)
        {
            if (_physicalRoot is null) throw new PlatformNotSupportedException("The original mutation has no retained Windows parent owner.");
            _physicalRoot.EnsureDirectory(Path.GetDirectoryName(path)!, createOriginal);
        }

        private async Task<string> ReadOriginalTextAsync(string path, CancellationToken token)
        {
            if (_linuxRoot is not null) { DemandIssued(); var actual = await _linuxRoot.ReadTextAsync(path, token).ConfigureAwait(false); DemandIssued(); return actual; }
            if (_physicalRoot is null) throw new PlatformNotSupportedException("The original read has no retained physical Windows path owner.");
            token.ThrowIfCancellationRequested();
            var errors = new List<Exception>();
            Microsoft.Win32.SafeHandles.SafeFileHandle? originalHandle = null;
            FileStream? originalStream = null;
            StreamReader? reader = null;
            Task<string>? originalRead = null;
            string? text = null;
            try
            {
                originalHandle = _physicalRoot.OpenOriginalRead(path);
                originalStream = new FileStream(originalHandle, FileAccess.Read, 4096, isAsync: true);
                if (originalStream.Length > 4L * 1024 * 1024)
                    throw new InvalidOperationException("The original file exceeds the existing workspace 4 MB text-read limit.");
                reader = new StreamReader(originalStream, Encoding.UTF8, true, 4096, leaveOpen: true);
                originalRead = reader.ReadToEndAsync(token);
                text = await originalRead.ConfigureAwait(false);
            }
            catch (Exception error) { AddOriginalErrors(errors, originalRead, error); }
            finally
            {
                try { reader?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                Task? originalClose = null;
                try
                {
                    if (originalStream is not null)
                    {
                        originalClose = originalStream.DisposeAsync().AsTask();
                        await originalClose.ConfigureAwait(false);
                    }
                }
                catch (Exception error) { AddOriginalErrors(errors, originalClose, error); }
                try { originalHandle?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors, originalRead?.IsCanceled == true);
            return text ?? throw new InvalidDataException("The original read produced no text.");
        }

        public OriginalEffect PrepareProcess(ProcessRequest actual)
        {
            DemandRoot(actual.WorkingDirectory);
            if (_call.Name is not ("run_command" or "run_tests"))
                throw new UnauthorizedAccessException("A process is not declared by this original call.");
            var command = Text("command");
            if (_call.Name == "run_tests" && string.IsNullOrWhiteSpace(command))
                command = Directory.EnumerateFiles(Root, "*.sln", SearchOption.TopDirectoryOnly).Any() ? "dotnet test"
                    : File.Exists(Path.Combine(Root, "package.json")) ? "npm test"
                    : File.Exists(Path.Combine(Root, "go.mod")) ? "go test ./..."
                    : throw new InvalidOperationException("No supported original test project was detected.");
            if (string.IsNullOrWhiteSpace(command)) throw new ArgumentException("The original command is required.");
            var timeout = Integer("timeout_seconds", _call.Name == "run_tests" ? 600 : 120);
            if (_call.Name == "run_tests") timeout = Math.Clamp(timeout, 1, 1800);
            timeout = Math.Clamp(timeout, 1, 900);
            var expected = WorkspaceToolProcessRequestFactory.CreateOriginal(Root, command, timeout);
            var digest = WorkspaceToolOriginalDigest.Process(actual);
            if (!string.Equals(digest, WorkspaceToolOriginalDigest.Process(expected), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The process fields differ from the original declared command.");
            if (_physicalRoot is not null) _physicalRoot.EnsureDirectory(Root, null);
            else if (_linuxRoot is not null) _linuxRoot.DemandCurrent();
            else throw new PlatformNotSupportedException("The original process requires a supported retained root descriptor.");
            var effect = new OriginalEffect(WorkspaceToolEffectKind.ProcessStart, Root, digest);
            lock (_gate)
            {
                if (_processReserved) throw new InvalidOperationException("The original process cannot be replayed.");
                _processReserved = true;
                _effects.Add(effect);
            }
            return effect;
        }

        public string OriginalProcessWorkingDirectory()
        {
            DemandIssued();
            return _linuxRoot is not null ? _linuxRoot.ProcessWorkingDirectory : Root;
        }
        public Microsoft.Win32.SafeHandles.SafeFileHandle OpenOriginalLinuxParent(string path)
        {
            DemandIssued();
            if (_linuxRoot is null) throw new PlatformNotSupportedException("No original Linux directory owner is available.");
            try { return _linuxRoot.OpenDirectory(Path.GetDirectoryName(path)!, flushable: true); }
            catch (FileNotFoundException missing)
            { throw new PlatformNotSupportedException("The bounded Linux edit adapter requires an existing parent; it never creates unchecked directory components.", missing); }
        }
        public Microsoft.Win32.SafeHandles.SafeFileHandle OpenOriginalLinuxTarget(string path)
        {
            DemandIssued();
            if (_linuxRoot is null) throw new PlatformNotSupportedException("No original Linux file owner is available.");
            return _linuxRoot.OpenRead(path);
        }
        public void DemandOriginalLinuxRoot() { DemandIssued(); _linuxRoot?.DemandCurrent(); }

        public void FinishEffect(OriginalEffect effect, bool admitted, bool known, bool terminal, int? processId = null, int? exitCode = null, bool? eligible = null)
        {
            lock (_gate)
            {
                effect.Observation = effect.Observation with
                { Admitted = admitted, EffectKnown = known, TerminalOutcomeKnown = terminal, ProcessId = processId, ExitCode = exitCode };
                effect.Eligible = eligible ?? (admitted && known && terminal);
            }
        }

        private Task<T> Own<T>(Func<CancellationToken, Task<T>> body, CancellationToken caller, bool expectedReadAbsence = false)
        {
            var slot = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                if (_sealed) throw new ObjectDisposedException(nameof(Invocation));
                _operations.Add(new Operation(slot, expectedReadAbsence));
            }
            var original = RunOriginalAsync(body, caller, expectedReadAbsence);
            slot.SetResult(original);
            return original;
        }

        private async Task<T> RunOriginalAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken caller, bool expectedReadAbsence)
        {
            var failures = new List<Exception>();
            CancellationTokenSource? linked = null;
            Task<T>? original = null;
            T? result = default;
            try
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
                original = body(linked.Token);
                result = await original.ConfigureAwait(false);
            }
            catch (Exception error) { AddOriginalErrors(failures, original, error); }
            finally
            {
                try { linked?.Dispose(); } catch (Exception error) { AddOriginalErrors(failures, null, error); }
                if (!expectedReadAbsence || !failures.All(IsReadAbsence))
                    lock (_gate) _errors.AddRange(failures);
            }
            ThrowOriginalErrors(failures, original?.IsCanceled == true);
            return result!;
        }

        public Task CloseAndDrainAsync()
        {
            // Pure own/source ancestry guard precedes even an existing coalesced close.
            DemandExternalOriginalProcessStartReleaseJoin();
            TaskCompletionSource<Task> completion;
            Operation[] operations;
            lock (_gate)
            {
                if (_close is not null) return _close;
                _sealed = true;
                operations = _operations.ToArray();
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = CompleteCloseAsync(completion.Task);
            }
            // Cancellation callbacks and original cleanup never execute under the registry lock.
            var actualDrain = DrainOriginalAsync(operations);
            completion.SetResult(actualDrain);
            return _close;
        }

        private static async Task CompleteCloseAsync(Task<Task> actualClose) => await (await actualClose.ConfigureAwait(false)).ConfigureAwait(false);

        private async Task DrainOriginalAsync(Operation[] operations)
        {
            var errors = new List<Exception>();
            try { _lifetime.Cancel(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            foreach (var operation in operations)
            {
                var original = await operation.Original.Task.ConfigureAwait(false);
                var observed = new List<Exception>();
                await JoinOriginalAsync(original, observed).ConfigureAwait(false);
                if (!operation.ExpectedReadAbsence || !observed.All(IsReadAbsence)) errors.AddRange(observed);
            }
            // Actual process originals above publish/retain late start-entry cleanup before
            // they can terminate. Snapshot only after those genuine originals have joined.
            Task[] releases;
            lock (_gate) releases = _originalProcessStartEntryReleases.ToArray();
            foreach (var actualRelease in releases)
                await JoinOriginalAsync(actualRelease, errors).ConfigureAwait(false);
            try { _physicalRoot?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { _linuxRoot?.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { _lifetime.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            lock (_gate) _errors.AddRange(errors);
            // The SAME close task retains terminal failures. Physical outcome observation below
            // still conserves known effects and exact causes even when this drain faults.
            ThrowOriginalErrors(errors);
        }

        private static bool IsReadAbsence(Exception error) => error is FileNotFoundException or DirectoryNotFoundException ||
            error is AggregateException compound && compound.InnerExceptions.Count != 0 && compound.InnerExceptions.All(IsReadAbsence);

        public async Task<WorkspaceToolPhysicalOutcome> CompleteOriginalAsync(bool effectBodySucceeded, CancellationToken cancellationToken)
        {
            var originalClose = CloseAndDrainAsync();
            try { await originalClose.ConfigureAwait(false); }
            catch (Exception error)
            {
                var observed = new List<Exception>();
                AddOriginalErrors(observed, originalClose, error);
                lock (_gate) _errors.AddRange(observed);
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_outcome is not null)
                {
                    if (_completedBody != effectBodySucceeded) throw new InvalidOperationException("An original outcome cannot be reclassified.");
                    return _outcome;
                }
                var effects = _effects.Select(effect => effect.Observation).ToArray();
                var errors = _errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
                var expected = (_call.Name == "preview_change_set" ? 0 : _declaredWrites.Count) + (_call.Name is "run_command" or "run_tests" ? 1 : 0);
                var complete = effectBodySucceeded && errors.Length == 0 && effects.Length == expected && _effects.All(effect => effect.Eligible) && effects.All(effect =>
                    effect.Admitted && effect.EffectKnown && effect.TerminalOutcomeKnown &&
                    (effect.Kind != WorkspaceToolEffectKind.ProcessStart || effect.ExitCode is not null));
                var noEffect = effects.All(effect => !effect.Admitted);
                _completedBody = effectBodySucceeded;
                _outcome = new WorkspaceToolPhysicalOutcome(complete && expected > 0 ? Guid.NewGuid().ToString("N") : null,
                    Array.AsReadOnly(effects), Array.AsReadOnly(errors), noEffect, !noEffect && !complete);
                return _outcome;
            }
        }

        public bool OwnsOutcome(WorkspaceToolPhysicalOutcome outcome)
        {
            lock (_gate) return _outcome is not null && ReferenceEquals(outcome, _outcome);
        }

        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
        private string Text(string key, string fallback = "") => _call.Arguments.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString() : fallback;
        private string RequiredText(string key) => string.IsNullOrWhiteSpace(Text(key)) ? throw new ArgumentException(key + " is required.") : Text(key);
        private int Integer(string key, int fallback) => _call.Arguments.TryGetValue(key, out var value) && value.TryGetInt32(out var number) ? number : fallback;
        private bool Boolean(string key) => _call.Arguments.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True;
        private sealed record DeclaredChange(string Path, string? Content, string? ExpectedSha256 = null);
    }
}
