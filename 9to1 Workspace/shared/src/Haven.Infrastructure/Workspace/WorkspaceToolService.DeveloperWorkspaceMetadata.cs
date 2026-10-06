using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>The SAME selected-source owner, with an optional lazy alias to the actual
    /// configured Dev store. Missing/foreign configuration refuses strict metadata creation.</summary>
    public IDeveloperProjectOriginalPhysicalCaptureSource CreateOriginalDeveloperCaptureSource(
        IDeveloperProjectOriginalReadAdmissionSource originalReads,
        Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> originalSelections,
        Func<IDeveloperProjectOriginalSetupPermissionSource> originalPermissions,
        Func<IDeveloperProjectOriginalWorkspaceMetadataStore> originalWorkspaceStore)
        => new DeveloperCaptureSource(originalReads, originalSelections, originalPermissions, originalWorkspaceStore);

    private sealed partial class DeveloperCaptureSource : IDeveloperProjectOriginalWorkspaceMetadataSource
    {
        private readonly Func<IDeveloperProjectOriginalWorkspaceMetadataStore>? _originalWorkspaceStore;
        private readonly HashSet<WorkspaceMetadataPreparation> _workspaceMetadataPreparations = [];
        private DeveloperCaptureSource(IDeveloperProjectOriginalReadAdmissionSource reads,
            Func<IDeveloperProjectOriginalPhysicalReadSelectionSource> selections,
            Func<IDeveloperProjectOriginalSetupPermissionSource> permissions,
            Func<IDeveloperProjectOriginalWorkspaceMetadataStore> store) : this(reads, selections, permissions)
            => _originalWorkspaceStore = store ?? throw new ArgumentNullException(nameof(store));

        private sealed class WorkspaceMetadataPreparation(DeveloperCaptureSource owner,
            IDeveloperProjectOriginalWorkspaceMetadataStore store, Capture capture, DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSetupPermission permission, IDeveloperProjectOriginalSetupPermissionSource issuer,
            DeveloperProjectSetupStep step, string ancestor, string directory, string[] components)
            : IDeveloperProjectOriginalWorkspaceMetadataPreparation
        {
            internal DeveloperCaptureSource Owner => owner;
            internal IDeveloperProjectOriginalWorkspaceMetadataStore Store => store;
            internal Capture Capture => capture;
            internal DeveloperProjectSetupIntent Intent => intent;
            internal IDeveloperProjectOriginalSetupPermission Permission => permission;
            internal IDeveloperProjectOriginalSetupPermissionSource Issuer => issuer;
            internal DeveloperProjectSetupStep Step => step;
            internal string Ancestor => ancestor;
            internal string DirectoryPath => directory;
            internal string[] Components => components;
            internal OriginalLinuxPathLease? Root;
            internal SafeFileHandle? AncestorHandle;
            internal LinuxIdentity AncestorIdentity;
            internal Original PreparingOriginal = null!;
            internal WorkspaceMetadataOperation? Operation;
            internal Task? OriginalClose;
            internal bool Sealed, Attempted;
            public bool IsBoundToOriginalStore(IDeveloperProjectOriginalWorkspaceMetadataStore sameStore, Guid workspaceId)
                => owner.IsIssuedOriginalWorkspaceMetadataPreparation(this, sameStore, workspaceId);
            public Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> CreateOriginalMetadataAsync(
                ReadOnlyMemory<byte> document, IDeveloperProjectOriginalSetupStepEntry entry,
                Action<Action> scope, Action<Task> retain, CancellationToken token)
                => owner.CreateWorkspaceMetadata(this, document, entry, scope, retain, token);
            public bool IsIssuedOriginalMetadata(Task actual, IDeveloperProjectOriginalWorkspaceMetadataObservation observation)
                => owner.IsIssuedOriginalWorkspaceMetadataOutcome(this, actual, observation);
            public Task CloseAndDrainAsync() { owner.DemandExternalMetadataPreparationJoin(this); return owner.CloseWorkspaceMetadata(this); }
            public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
        }
        private sealed class WorkspaceMetadataOperation(IDeveloperProjectOriginalSetupStepEntry entry,
            byte[] document, Action<Action> scope, Action<Task> retain)
        {
            internal IDeveloperProjectOriginalSetupStepEntry Entry => entry;
            internal byte[] Document => document;
            internal Action<Action> CallerScope => scope;
            internal Action<Task> CallerRetain => retain;
            internal Original Original = null!;
            internal Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> Driver = null!;
            internal WorkspaceMetadataObservation? Observation;
            internal bool Cleaned, Confirmed;
            internal Task? OriginalCanceledTask;
            internal Exception? OriginalCancellationCause;
            internal LinuxIdentity CommittedIdentity;
            internal SafeFileHandle? LockHandle, StageHandle, InputHandle, BorrowedStageHandle;
            internal readonly List<SafeFileHandle> Parents = [];
            internal FileStream? Output, Input;
        }
        private sealed class WorkspaceMetadataObservation(WorkspaceMetadataPreparation preparation,
            WorkspaceMetadataOperation operation, string document, string hash, LinuxIdentity identity)
            : IDeveloperProjectOriginalWorkspaceMetadataObservation
        {
            internal WorkspaceMetadataPreparation Preparation => preparation;
            internal WorkspaceMetadataOperation Operation => operation;
            internal LinuxIdentity Identity => identity;
            public string OriginalCommittedDocument => document;
            public string OriginalDocumentSha256 => hash;
        }

        public void DemandExternalOriginalWorkspaceMetadataJoin() => DemandExternalOriginalJoin();
        private void DemandExternalMetadataPreparationJoin(WorkspaceMetadataPreparation preparation)
        {
            // This child joins only its own preparation/write drivers. It never calls the
            // encompassing kernel guard, whose unrelated parent originals it does not join.
            if (_physicalSources?.ContainsKey(this) == true)
                throw new InvalidOperationException("An actual metadata source callback cannot join the same preparation.");
            for (var current = _executing.Value; current is not null; current = current.Parent)
                if (Volatile.Read(ref current.Live) && (ReferenceEquals(current, preparation.PreparingOriginal) ||
                    ReferenceEquals(current, preparation.Operation?.Original)))
                    throw new InvalidOperationException("A live actual metadata original cannot join its own preparation.");
        }
        private void DemandOriginalWorkspaceMetadataDependencies()
        {
            var visited = _directoryDependencyVisits ??= [];
            if (!visited.Add(this)) return;
            try
            {
                IDeveloperProjectOriginalSetupPermissionSource[] issuers;
                lock (_gate) issuers = _workspaceMetadataPreparations.Select(value => value.Issuer).Distinct().ToArray();
                foreach (var issuer in issuers) Invoke(() => { issuer.DemandExternalOriginalSetupJoin(); return true; });
            }
            finally { visited.Remove(this); }
        }
        public bool IsIssuedOriginalWorkspaceMetadataPreparation(IDeveloperProjectOriginalWorkspaceMetadataPreparation value,
            IDeveloperProjectOriginalWorkspaceMetadataStore store, Guid workspaceId)
        {
            lock (_gate) return !_retiring && value is WorkspaceMetadataPreparation preparation &&
                ReferenceEquals(preparation.Owner, this) && ReferenceEquals(preparation.Store, store) &&
                _workspaceMetadataPreparations.Contains(preparation) && preparation.Root is not null &&
                !preparation.Sealed && preparation.Intent.WorkspaceId == workspaceId && !preparation.Attempted;
        }
        public Task<IDeveloperProjectOriginalWorkspaceMetadataPreparation> PrepareOriginalWorkspaceMetadataAsync(
            IDeveloperProjectOriginalWorkspaceMetadataStore store, DeveloperProjectSetupIntent intent,
            IDeveloperProjectOriginalSourceCapture value, IDeveloperProjectOriginalSetupPermission permission,
            DeveloperProjectSetupStep step, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(store);
            if (value is not Capture capture || !IsIssuedOriginalCapture(capture, capture.Physical, capture.Logical) ||
                intent.Validate() is not null || intent.Mode != DeveloperProjectSetupMode.RegisterExisting || intent.ExpectedWorkspaceRevision != 0 ||
                intent.OriginalExistingProjectRoot != capture.OriginalExistingProjectRoot ||
                intent.OriginalSourceCaptureReference != capture.OriginalCaptureReference || intent.OriginalSourceDigest != capture.OriginalCaptureDigest ||
                !intent.Steps.Any(actual => ReferenceEquals(actual, step)) || step.Kind != DeveloperProjectSetupStepKind.SaveDevWorkspace)
                throw new UnauthorizedAccessException("Retain the exact original capture, once-created workspace and reviewed SaveDevWorkspace step.");
            return Start<IDeveloperProjectOriginalWorkspaceMetadataPreparation>(capture.Physical, async () =>
            {
                var configured = Invoke(() => _originalWorkspaceStore?.Invoke());
                if (!ReferenceEquals(configured, store)) throw new InvalidOperationException("No SAME configured actual Dev store/native issuer exists.");
                var selection = Invoke(selections);
                await Observe(capture.Physical, () => selection.RevalidateOriginalAsync(capture.Logical, intent.OriginalActor, token)).ConfigureAwait(false);
                var issuer = Invoke(() => setupPermissions?.Invoke()) ?? throw new InvalidOperationException("No actual setup permission source exists.");
                await Observe(capture.Physical, () => issuer.ValidateOriginalAsync(intent, permission, token)).ConfigureAwait(false);
                await Observe(capture.Physical, () => RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
                WorkspaceMetadataPreparation? preparation = null;
                Invoke(() =>
                {
                    token.ThrowIfCancellationRequested(); RequireWorkspaceMetadataExports();
                    var ancestor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.OriginalWorkspaceMetadataAncestor));
                    var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.OriginalWorkspaceMetadataDirectory));
                    if (!IsWithinRoot(ancestor, directory, StringComparison.Ordinal) || ancestor == directory)
                        throw new UnauthorizedAccessException("The configured metadata directory escapes its bounded actual ancestor.");
                    var components = Path.GetRelativePath(ancestor, directory).Split(Path.DirectorySeparatorChar);
                    if (components.Length is < 1 or > 4 || components.Any(value => !SafeLeaf(value)))
                        throw new NotSupportedException("The configured metadata ancestry exceeds the bounded native preparation.");
                    preparation = new(this, store, capture, intent, permission, issuer, step, ancestor, directory, components);
                    lock (_gate)
                    {
                        if (_retiring || capture.Physical.Sealed || _workspaceMetadataPreparations.Count >= 512)
                            throw new InvalidOperationException("Original metadata preparation custody is sealed or full.");
                        preparation.PreparingOriginal = _executing.Value ?? throw new InvalidOperationException("No actual preparation original exists.");
                        _workspaceMetadataPreparations.Add(preparation);
                    }
                    // Retain actual ancestor before returning the preparation. No metadata
                    // directory/file is created until the separately held original Home entry.
                    preparation.Root = new OriginalLinuxPathLease(ancestor);
                    preparation.AncestorHandle = preparation.Root.OpenDirectory(ancestor);
                    preparation.AncestorIdentity = ReadLinuxIdentity(preparation.AncestorHandle); return true;
                });
                return preparation ?? throw new InvalidOperationException("No native metadata preparation was issued.");
            });
        }
        private Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> CreateWorkspaceMetadata(
            WorkspaceMetadataPreparation preparation, ReadOnlyMemory<byte> document,
            IDeveloperProjectOriginalSetupStepEntry entry, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(entry); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
            lock (_gate)
            {
                if (!IsIssuedOriginalWorkspaceMetadataPreparation(preparation, preparation.Store, preparation.Intent.WorkspaceId))
                    throw new UnauthorizedAccessException("The original metadata preparation retired or was already attempted; no replay is admitted.");
                if (document.Length is < 1 or > 1024 * 1024) throw new NotSupportedException("The complete metadata document exceeds the native one MiB bound.");
                var operation = new WorkspaceMetadataOperation(entry, document.ToArray(), scope, retain);
                ValidateOriginalWorkspaceDocument(preparation, operation.Document);
                preparation.Attempted = true;
                return Start<IDeveloperProjectOriginalWorkspaceMetadataObservation>(preparation.Capture.Physical,
                    () => WriteWorkspaceMetadata(preparation, operation, token),
                    (original, actual) => { operation.Original = original; operation.Driver = actual; preparation.Operation = operation; });
            }
        }
        private void MetadataScope(WorkspaceMetadataPreparation preparation, WorkspaceMetadataOperation operation, Action callback)
        {
            var thread = Environment.CurrentManagedThreadId; int phase = 1, used = 0;
            try
            {
                Invoke(() =>
                {
                    operation.CallerScope(() =>
                    {
                        if (Volatile.Read(ref phase) == 0 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                            throw new InvalidOperationException("Native metadata callback is finite, same-thread and single-use.");
                        if (!ReferenceEquals(preparation.Operation, operation) || !Volatile.Read(ref operation.Original.Live))
                            throw new UnauthorizedAccessException("No SAME live original metadata driver owns this callback.");
                        callback();
                    });
                    if (Volatile.Read(ref used) == 0) throw new InvalidOperationException("The native original callback was not invoked synchronously.");
                    return true;
                });
            }
            finally { Volatile.Write(ref phase, 0); }
        }
        private void RetainMetadata(WorkspaceMetadataOperation operation, Task actual)
        {
            lock (_gate) operation.Original.Sources.Add(actual);
            operation.CallerRetain(actual);
        }
        private async Task<T> ObserveMetadata<T>(WorkspaceMetadataPreparation preparation, WorkspaceMetadataOperation operation,
            Func<Task<T>> factory)
        {
            Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
            try { MetadataScope(preparation, operation, () => { actual = factory() ?? throw new InvalidOperationException("Native metadata source returned no Task."); RetainMetadata(operation, actual); }); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (actual is not null) try { value = await actual.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            if (actual?.IsCanceled == true && errors.Count == 1)
            { operation.OriginalCanceledTask = actual; operation.OriginalCancellationCause = errors[0]; }
            ThrowOriginalErrors(errors, actual?.IsCanceled == true && errors.Count == 1); return value;
        }
        private async Task ObserveMetadata(WorkspaceMetadataPreparation preparation, WorkspaceMetadataOperation operation,
            Func<Task> factory)
        {
            Task? actual = null; var errors = new List<Exception>();
            try { MetadataScope(preparation, operation, () => { actual = factory() ?? throw new InvalidOperationException("Native metadata source returned no Task."); RetainMetadata(operation, actual); }); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            if (actual?.IsCanceled == true && errors.Count == 1)
            { operation.OriginalCanceledTask = actual; operation.OriginalCancellationCause = errors[0]; }
            ThrowOriginalErrors(errors, actual?.IsCanceled == true && errors.Count == 1);
        }
        private async Task ObserveMetadataCleanup(WorkspaceMetadataOperation operation, Func<Task> factory)
        {
            Task? actual = null; var errors = new List<Exception>();
            try { Invoke(() => { actual = factory() ?? throw new InvalidOperationException("Owned native stream cleanup returned no Task."); RetainMetadata(operation, actual); return true; }); }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            if (actual is not null) try { await actual.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            ThrowOriginalErrors(errors);
        }
        private async Task CheckWorkspaceMetadata(WorkspaceMetadataPreparation preparation, WorkspaceMetadataOperation operation, CancellationToken token)
        {
            if (operation.Entry is not IDeveloperProjectOriginalSetupScopedStepEntry scoped)
                throw new NotSupportedException("The genuine post-await scoped Home entry is required for this native original.");
            var current = await ObserveMetadata(preparation, operation, () => scoped.CheckOriginalStepCommitAsync(preparation.Step,
                callback => MetadataScope(preparation, operation, callback), actual => RetainMetadata(operation, actual), token).AsTask()).ConfigureAwait(false);
            if (!current) throw new UnauthorizedAccessException("The held original Home setup entry is no longer current.");
            MetadataScope(preparation, operation, () => DemandWorkspaceMetadata(preparation, operation, token));
        }
        private void DemandWorkspaceMetadata(WorkspaceMetadataPreparation preparation, WorkspaceMetadataOperation operation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(preparation.Operation, operation) || !preparation.Permission.IsIssuedOriginalStepEntry(preparation.Step, operation.Entry))
                throw new UnauthorizedAccessException("No SAME privately issued held entry owns this native metadata step.");
            operation.Entry.DemandOriginalStepEntry(preparation.Step); DemandCurrent(preparation.Capture.Physical);
            preparation.Root!.DemandCurrent();
            var path = preparation.Ancestor;
            for (var i = 0; i < operation.Parents.Count; i++)
            {
                if (i != 0) path = Path.Combine(path, preparation.Components[i - 1]);
                DemandLinuxDescriptorPath(operation.Parents[i], path);
                if (!ReadLinuxIdentity(operation.Parents[i]).IsDirectory) throw new UnauthorizedAccessException("The held metadata parent is unavailable.");
            }
            if (operation.LockHandle is not null)
            {
                DemandLinuxDescriptorPath(operation.LockHandle, Path.Combine(preparation.DirectoryPath, preparation.Intent.WorkspaceId.ToString("N") + ".lock"));
                var identity = ReadLinuxIdentity(operation.LockHandle);
                if (!identity.IsRegular || identity.Links != 1) throw new UnauthorizedAccessException("The participating native workspace lock changed.");
            }
        }
        private async Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> WriteWorkspaceMetadata(
            WorkspaceMetadataPreparation preparation, WorkspaceMetadataOperation operation, CancellationToken token)
        {
            var errors = new List<Exception>(); WorkspaceMetadataObservation? observation = null;
            try
            {
                await CheckWorkspaceMetadata(preparation, operation, token).ConfigureAwait(false);
                MetadataScope(preparation, operation, () =>
                {
                    DemandWorkspaceMetadata(preparation, operation, token);
                    var parent = preparation.Root!.OpenDirectory(preparation.Ancestor, flushable: true); operation.Parents.Add(parent);
                    var path = preparation.Ancestor;
                    foreach (var leaf in preparation.Components)
                    {
                        DemandWorkspaceMetadata(preparation, operation, token);
                        var expected = Path.Combine(path, leaf); SafeFileHandle? child = null;
                        try { child = OpenLinuxAt(parent, leaf, LinuxDirectory | LinuxCloseOnExec); }
                        catch (FileNotFoundException)
                        {
                            if (LinuxMetadataMkdirAt(LinuxDescriptor(parent), leaf, 0x1c0) != 0)
                                throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original metadata directory create failed or is unknown.");
                            if (LinuxFsync(LinuxDescriptor(parent)) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Original metadata ancestor sync failed after directory creation.");
                            child = OpenLinuxAt(parent, leaf, LinuxDirectory | LinuxCloseOnExec);
                        }
                        operation.Parents.Add(child); DemandLinuxDescriptorPath(child, expected);
                        if (!ReadLinuxIdentity(child).IsDirectory) throw new UnauthorizedAccessException("The original metadata component is not a directory.");
                        parent = child; path = expected;
                    }
                    DemandWorkspaceMetadata(preparation, operation, token);
                    operation.LockHandle = OpenLinuxAt(parent, preparation.Intent.WorkspaceId.ToString("N") + ".lock",
                        2UL | 0x40UL | LinuxCloseOnExec | LinuxNonBlocking, 0x180);
                    DemandWorkspaceMetadata(preparation, operation, token);
                    if (LinuxMetadataFlock(LinuxDescriptor(operation.LockHandle), 2 | 4) != 0)
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), "The SAME participating workspace lock is busy or unavailable; no strict mutation occurred.");
                    // Anonymous staging has no name for an attacker to replace before commit.
                    operation.StageHandle = OpenLinuxAt(parent, ".", LinuxAnonymousWrite, 0x180);
                    var identity = ReadLinuxIdentity(operation.StageHandle);
                    if (!identity.IsRegular || identity.Links != 0) throw new UnauthorizedAccessException("No genuine unnamed regular staging inode exists.");
                    operation.BorrowedStageHandle = new SafeFileHandle(operation.StageHandle.DangerousGetHandle(), ownsHandle: false);
                    operation.Output = new FileStream(operation.BorrowedStageHandle, FileAccess.Write, 16 * 1024, isAsync: false);
                });
                await ObserveMetadata(preparation, operation, () => operation.Output!.WriteAsync(operation.Document.AsMemory(), token).AsTask()).ConfigureAwait(false);
                await ObserveMetadata(preparation, operation, () => operation.Output!.FlushAsync(token)).ConfigureAwait(false);
                MetadataScope(preparation, operation, () => operation.Output!.Flush(flushToDisk: true));
                await ObserveMetadataCleanup(operation, () => operation.Output!.DisposeAsync().AsTask()).ConfigureAwait(false);
                operation.Output = null;
                await CheckWorkspaceMetadata(preparation, operation, token).ConfigureAwait(false);
                MetadataScope(preparation, operation, () =>
                {
                    DemandWorkspaceMetadata(preparation, operation, token);
                    var stage = operation.StageHandle!; var parent = operation.Parents[^1];
                    var staged = ReadLinuxIdentity(stage);
                    if (!staged.IsRegular || staged.Links != 0 || staged.Size != (ulong)operation.Document.Length)
                        throw new InvalidDataException("The SAME anonymous metadata inode no longer contains the complete staged document.");
                    var leaf = preparation.Intent.WorkspaceId.ToString("N") + ".json";
                    // Atomic create-only commit of THIS exact held inode, with no stage name,
                    // rename/delete-by-path, overwrite, automatic retry or external CAS claim.
                    if (LinuxLinkAt(LinuxDescriptor(stage), "", LinuxDescriptor(parent), leaf, 0x1000) != 0)
                        throw new Win32Exception(Marshal.GetLastPInvokeError(), "Exact original workspace link failed or is unknown; an existing identity is never overwritten.");
                    if (LinuxFsync(LinuxDescriptor(parent)) != 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Original workspace parent sync failed after link; commit is unknown.");
                    operation.InputHandle = OpenLinuxAt(parent, leaf, LinuxCloseOnExec | LinuxNonBlocking);
                    var committed = ReadLinuxIdentity(operation.InputHandle);
                    DemandLinuxDescriptorPath(operation.InputHandle, Path.Combine(preparation.DirectoryPath, leaf));
                    if (!committed.SameFile(staged) || !committed.IsRegular || committed.Links != 1 || committed.Size != (ulong)operation.Document.Length)
                        throw new IOException("The actually committed metadata name resolves to another inode or document.");
                    operation.CommittedIdentity = committed;
                    operation.Input = new FileStream(operation.InputHandle, FileAccess.Read, 16 * 1024, isAsync: false);
                });
                var observed = new byte[operation.Document.Length]; var offset = 0;
                while (offset < observed.Length)
                {
                    var count = await ObserveMetadata(preparation, operation,
                        () => operation.Input!.ReadAsync(observed.AsMemory(offset), token).AsTask()).ConfigureAwait(false);
                    if (count == 0) throw new EndOfStreamException("Original workspace readback is incomplete."); offset += count;
                }
                await CheckWorkspaceMetadata(preparation, operation, token).ConfigureAwait(false);
                MetadataScope(preparation, operation, () =>
                {
                    DemandLinuxDescriptorPath(operation.InputHandle!, Path.Combine(preparation.DirectoryPath, preparation.Intent.WorkspaceId.ToString("N") + ".json"));
                    var actual = ReadLinuxIdentity(operation.InputHandle!);
                    if (!actual.IsRegular || actual.Links != 1 || !actual.SameReadVersion(operation.CommittedIdentity) ||
                        !actual.SameFile(ReadLinuxIdentity(operation.StageHandle!)) || actual.Size != (ulong)observed.Length ||
                        !observed.AsSpan().SequenceEqual(operation.Document))
                        throw new IOException("The complete native workspace readback differs from the original exact bytes.");
                    observation = new(preparation, operation, Encoding.UTF8.GetString(observed),
                        Convert.ToHexString(SHA256.HashData(observed)).ToLowerInvariant(), actual);
                });
            }
            catch (Exception error) { AddOriginalErrors(errors, null, error); }
            finally
            {
                if (operation.Input is not null) try { await ObserveMetadataCleanup(operation, () => operation.Input.DisposeAsync().AsTask()).ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                if (operation.Output is not null) try { await ObserveMetadataCleanup(operation, () => operation.Output.DisposeAsync().AsTask()).ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                // Independent exact handle cleanup, including partial acquisition. No named
                // stage/lock/directory deletion: uncertain participating residue stays visible.
                foreach (var handle in new[] { operation.InputHandle, operation.BorrowedStageHandle, operation.StageHandle, operation.LockHandle }.Concat(operation.Parents.AsEnumerable().Reverse()))
                    if (handle is not null) try { Invoke(() => { handle.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
                operation.Cleaned = errors.Count == 0;
            }
            ThrowOriginalErrors(errors, operation.OriginalCanceledTask?.IsCanceled == true &&
                errors.Count != 0 && errors.All(error => ReferenceEquals(error, operation.OriginalCancellationCause)));
            if (observation is null) throw new InvalidOperationException("No private actual metadata readback was issued.");
            lock (_gate) { operation.Observation = observation; operation.Confirmed = true; }
            return observation;
        }
        private static void ValidateOriginalWorkspaceDocument(WorkspaceMetadataPreparation preparation, byte[] bytes)
        {
            using var document = JsonDocument.Parse(bytes); var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1) throw new InvalidDataException("Unsupported original Dev workspace schema.");
            var workspace = root.GetProperty("workspace"); var intent = preparation.Intent;
            var roots = workspace.GetProperty("roots").EnumerateArray().ToArray();
            var projects = workspace.GetProperty("projects").EnumerateArray().ToArray();
            var editors = workspace.GetProperty("openEditors").EnumerateArray().ToArray();
            if (workspace.GetProperty("workspaceId").GetGuid() != intent.WorkspaceId || workspace.GetProperty("revision").GetInt64() != 1 ||
                roots.Length != 1 || roots[0].GetProperty("rootId").GetGuid() != intent.RootId || roots[0].GetProperty("location").GetString() != preparation.Capture.OriginalExistingProjectRoot ||
                projects.Length != 1 || projects[0].GetProperty("projectId").GetGuid() != intent.ProjectId || projects[0].GetProperty("name").GetString() != intent.ProjectName ||
                projects[0].GetProperty("rootIds").GetArrayLength() != 1 || projects[0].GetProperty("rootIds")[0].GetGuid() != intent.RootId ||
                editors.Length != intent.Files.Length || editors.Select(value => value.GetProperty("fileId").GetGuid()).Distinct().Count() != editors.Length ||
                editors.Any(value => value.GetProperty("projectId").GetGuid() != intent.ProjectId ||
                    !intent.Files.Any(file => file.FileId == value.GetProperty("fileId").GetGuid()) ||
                    value.GetProperty("canonicalResourceId").GetString() != value.GetProperty("fileId").GetGuid().ToString("D")))
                throw new UnauthorizedAccessException("The complete original workspace metadata changes the reviewed canonical project/root/file identities.");
        }
        private Task CloseWorkspaceMetadata(WorkspaceMetadataPreparation preparation)
        {
            TaskCompletionSource begin; Task actual;
            lock (_gate)
            {
                if (!_workspaceMetadataPreparations.Contains(preparation) || !ReferenceEquals(preparation.Owner, this))
                    throw new UnauthorizedAccessException("Foreign original workspace metadata preparation.");
                if (preparation.OriginalClose is not null) return preparation.OriginalClose;
                preparation.Sealed = true; begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                actual = DrainWorkspaceMetadata(begin.Task, preparation); preparation.OriginalClose = actual;
            }
            begin.TrySetResult(); return actual;
        }
        private async Task DrainWorkspaceMetadata(Task begin, WorkspaceMetadataPreparation preparation)
        {
            await begin.ConfigureAwait(false); var errors = new List<Exception>();
            foreach (var actual in new[] { preparation.PreparingOriginal.Driver, preparation.Operation?.Driver }.OfType<Task>())
                try { await actual.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            try { Invoke(() => { preparation.AncestorHandle?.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            try { Invoke(() => { preparation.Root?.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            ThrowOriginalErrors(errors);
        }
        private WorkspaceMetadataPreparation RequireWorkspaceMetadataOutcome(IDeveloperProjectOriginalWorkspaceMetadataPreparation value,
            Task actual, IDeveloperProjectOriginalWorkspaceMetadataObservation observation)
        {
            lock (_gate)
                if (value is WorkspaceMetadataPreparation preparation && ReferenceEquals(preparation.Owner, this) &&
                    _workspaceMetadataPreparations.Contains(preparation) && preparation.Operation is { Confirmed: true, Cleaned: true } operation &&
                    ReferenceEquals(operation.Driver, actual) && ReferenceEquals(operation.Observation, observation) && actual.IsCompletedSuccessfully)
                    return preparation;
            throw new UnauthorizedAccessException("No SAME private native workspace Task/result/cleanup acknowledgement exists.");
        }
        public bool IsIssuedOriginalWorkspaceMetadataOutcome(IDeveloperProjectOriginalWorkspaceMetadataPreparation value,
            Task actual, IDeveloperProjectOriginalWorkspaceMetadataObservation observation)
        { try { RequireWorkspaceMetadataOutcome(value, actual, observation); return true; } catch (UnauthorizedAccessException) { return false; } }
        public Task ValidateOriginalWorkspaceMetadataOutcomeAsync(IDeveloperProjectOriginalWorkspaceMetadataPreparation value,
            Task actual, IDeveloperProjectOriginalWorkspaceMetadataObservation observation, CancellationToken token)
        {
            var preparation = RequireWorkspaceMetadataOutcome(value, actual, observation);
            if (preparation.OriginalClose?.IsCompletedSuccessfully != true)
                throw new UnauthorizedAccessException("The SAME complete native preparation close has not succeeded.");
            return Start(preparation.Capture.Physical, async () =>
            {
                var selection = Invoke(selections);
                await Observe(preparation.Capture.Physical, () => selection.RevalidateOriginalAsync(preparation.Capture.Logical, preparation.Intent.OriginalActor, token)).ConfigureAwait(false);
                await Observe(preparation.Capture.Physical, () => RevalidateOriginalCaptureAsync(preparation.Capture, token)).ConfigureAwait(false);
                await ValidateCommittedWorkspaceDocument(preparation, (WorkspaceMetadataObservation)observation, token).ConfigureAwait(false);
                Invoke(() => { token.ThrowIfCancellationRequested(); RequireWorkspaceMetadataOutcome(value, actual, observation); return true; }); return true;
            });
        }
        private async Task ValidateCommittedWorkspaceDocument(WorkspaceMetadataPreparation preparation,
            WorkspaceMetadataObservation observation, CancellationToken token)
        {
            OriginalLinuxPathLease? root = null; SafeFileHandle? ancestor = null, handle = null; FileStream? input = null;
            var errors = new List<Exception>(); var original = _executing.Value ?? throw new InvalidOperationException("No actual validation original exists.");
            Task? actual = null;
            try
            {
                Invoke(() =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(_originalWorkspaceStore?.Invoke(), preparation.Store))
                        throw new UnauthorizedAccessException("The actual configured workspace store changed before metadata acknowledgement.");
                    root = new OriginalLinuxPathLease(preparation.Ancestor);
                    ancestor = root.OpenDirectory(preparation.Ancestor);
                    if (!ReadLinuxIdentity(ancestor).SameFile(preparation.AncestorIdentity))
                        throw new IOException("The actual metadata ancestor changed after the original commit.");
                    handle = root.OpenRead(Path.Combine(preparation.DirectoryPath, preparation.Intent.WorkspaceId.ToString("N") + ".json"));
                    if (!ReadLinuxIdentity(handle).SameReadVersion(observation.Identity))
                        throw new IOException("The current actual workspace record changed after the original commit.");
                    input = new FileStream(handle, FileAccess.Read, 16 * 1024, isAsync: false); return true;
                });
                var bytes = new byte[observation.Operation.Document.Length]; var offset = 0;
                while (offset < bytes.Length)
                {
                    Task<int>? read = null;
                    Invoke(() => { read = input!.ReadAsync(bytes.AsMemory(offset), token).AsTask(); lock (_gate) original.Sources.Add(read); return true; });
                    actual = read; var count = await read!.ConfigureAwait(false);
                    if (count == 0) throw new EndOfStreamException("The current actual workspace acknowledgement read is incomplete."); offset += count;
                }
                Invoke(() =>
                {
                    token.ThrowIfCancellationRequested(); root!.DemandCurrent();
                    DemandLinuxDescriptorPath(handle!, Path.Combine(preparation.DirectoryPath, preparation.Intent.WorkspaceId.ToString("N") + ".json"));
                    var current = ReadLinuxIdentity(handle!);
                    if (!current.IsRegular || current.Links != 1 || !current.SameReadVersion(observation.Identity) || !bytes.AsSpan().SequenceEqual(observation.Operation.Document))
                        throw new IOException("The complete current workspace metadata does not match the exact original inode/document.");
                    return true;
                });
            }
            catch (Exception error) { AddOriginalErrors(errors, actual, error); }
            finally
            {
                Task? close = null;
                try { Invoke(() => { if (input is not null) { close = input.DisposeAsync().AsTask(); lock (_gate) original.Sources.Add(close); } return true; }); }
                catch (Exception error) { AddOriginalErrors(errors, null, error); }
                if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, close, error); }
                foreach (var owned in new IDisposable?[] { handle, ancestor, root })
                    if (owned is not null) try { Invoke(() => { owned.Dispose(); return true; }); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            }
            ThrowOriginalErrors(errors);
        }
        private Original[] OriginalWorkspaceMetadataOwners(PhysicalSelection selection)
            => _workspaceMetadataPreparations.Where(value => ReferenceEquals(value.Capture.Physical, selection)).SelectMany(value =>
                value.Operation is { } operation ? new[] { value.PreparingOriginal, operation.Original } : [value.PreparingOriginal]).Distinct().ToArray();
        private async Task DrainOriginalWorkspaceMetadata(PhysicalSelection selection, List<Exception> errors)
        {
            WorkspaceMetadataPreparation[] preparations; lock (_gate) preparations = _workspaceMetadataPreparations.Where(value => ReferenceEquals(value.Capture.Physical, selection)).ToArray();
            var closes = new List<Task>();
            foreach (var preparation in preparations) try { closes.Add(CloseWorkspaceMetadata(preparation)); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            foreach (var actual in closes) try { await actual.ConfigureAwait(false); } catch (Exception error) { AddOriginalErrors(errors, actual, error); }
        }
    }
    private static void RequireWorkspaceMetadataExports()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Strict original Dev metadata currently requires Linux descriptor-relative create-only link; Windows selection remains unconfigured.");
        RequireLinuxExports();
        if (!NativeLibrary.TryLoad("libc.so.6", out var library)) throw new PlatformNotSupportedException("No supported native metadata library exists.");
        try { foreach (var name in new[] { "mkdirat", "flock" }) if (!NativeLibrary.TryGetExport(library, name, out _)) throw new PlatformNotSupportedException("The required original native metadata primitive is unavailable: " + name); }
        finally { NativeLibrary.Free(library); }
    }
    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)] private static extern int LinuxMetadataMkdirAt(int parent, [MarshalAs(UnmanagedType.LPUTF8Str)] string leaf, uint mode);
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)] private static extern int LinuxMetadataFlock(int file, int operation);
}
