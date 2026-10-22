using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

/// <summary>The configured Files owner issues an opaque existing-project selection, retaining
/// SAME profile/store/provider/configuration and actual kernel root. No manifest, FileId, copy,
/// import effect or permission is produced by selection. Home approves its distinct READ first.</summary>
public sealed partial class FilesDeveloperOriginalSourceSelection
    : IDeveloperProjectOriginalPhysicalReadSelectionSource, IDeveloperProjectOriginalCaptureAuthority,
      IDeveloperProjectOriginalEditScopeSource, IDeveloperProjectOriginalEditCurrentnessPinSource,
      IDeveloperOriginalRegisteredProjectSelectionSource, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly NativeFilesWorkspaceAuthority workspaces;
    private readonly IDeveloperProjectOriginalReadAdmissionSource reads;
    private readonly IDeveloperProjectOriginalPhysicalCaptureSource _physical;
    private readonly FileDeveloperWorkspaceStore? _registeredStore;
    private readonly Lazy<IDeveloperOriginalRegisteredProjectNativeSource> _registeredNative;
    public FilesDeveloperOriginalSourceSelection(NativeFilesWorkspaceAuthority workspaces,
        IDeveloperProjectOriginalPhysicalCaptureSource physical, IDeveloperProjectOriginalReadAdmissionSource reads)
        : this(workspaces, physical, reads, null) { }
    public FilesDeveloperOriginalSourceSelection(NativeFilesWorkspaceAuthority workspaces,
        IDeveloperProjectOriginalPhysicalCaptureSource physical, IDeveloperProjectOriginalReadAdmissionSource reads,
        FileDeveloperWorkspaceStore? originalSavedStore)
    {
        this.workspaces = workspaces; this.reads = reads; _physical = physical; _registeredStore = originalSavedStore;
        _registeredNative = new(() => _physical.CreateOriginalRegisteredProjectNativeSource(() => this,
            _registeredStore ?? throw new InvalidOperationException("The SAME configured saved Build store is unavailable.")));
    }
    private readonly AsyncLocal<Work?> _executing = new();
    [ThreadStatic] private static Dictionary<FilesDeveloperOriginalSourceSelection, int>? _physicalSources;
    private readonly HashSet<Work> _work = [];
    private readonly HashSet<Selection> _selections = [];
    private readonly Dictionary<IDeveloperProjectOriginalSourceCapture, Selection> _captures = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<EditCurrentnessPin> _pins = [];
    private readonly HashSet<CreateCurrentnessPin> _createPins = [];
    private bool _retiring;
    private Task? _close;
    private sealed class Work(Work? parent)
    {
        public Work? Parent => parent;
        public bool Live;
        public bool Healthy;
        public Task Driver = null!;
        public readonly List<Task> Sources = [];
    }
    private sealed class Selection(FilesDeveloperOriginalSourceSelection issuer, NativeFilesWorkspace workspace,
        IDeveloperProjectOriginalPhysicalSelection root, RegisteredDescriptor? registered = null) : IDeveloperProjectOriginalReadSelection
    {
        public FilesDeveloperOriginalSourceSelection Issuer => issuer;
        public NativeFilesWorkspace Workspace => workspace;
        public IDeveloperProjectOriginalPhysicalSelection Root => root;
        public RegisteredDescriptor? Registered => registered;
        public Func<CancellationToken, ValueTask<bool>> Check = null!;
        public string ConfigurationDigest { get; } = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(workspace.Configuration))).ToLowerInvariant();
        public ResourceScope Scope { get; } = new("dev.project.source", registered is null
                ? "dev-source-selection:" + Guid.NewGuid().ToString("D")
                : $"dev-project-source:{registered.WorkspaceId:D}:{registered.ProjectId:D}:{registered.RootId:D}:{Guid.NewGuid():D}",
            registered is null
                ? Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { workspace.Configuration, workspace.Actor, root.OriginalProjectRoot }))).ToLowerInvariant()
                : RegisteredScopeRevision(workspace, registered), ResourceAccess.Read);
    }
    public void DemandExternalOriginalReadSelectionJoin()
    {
        if (_physicalSources?.ContainsKey(this) == true) throw new InvalidOperationException("An original Files callback cannot join its source selection owner.");
        for (var current = _executing.Value; current is not null; current = current.Parent)
            if (Volatile.Read(ref current.Live)) throw new InvalidOperationException("An original Files source cannot join its own retirement.");
    }
    public void DemandExternalOriginalCaptureJoin()
    { DemandExternalOriginalReadSelectionJoin(); _physical.DemandExternalOriginalJoin(); }
    private void Scope(Action action)
    {
        var values = _physicalSources ??= []; values.TryGetValue(this, out var before); values[this] = before + 1;
        try { action(); }
        finally { if (before == 0) values.Remove(this); else values[this] = before; }
    }
    private void Retain(Task task)
    {
        var work = _executing.Value ?? throw new InvalidOperationException("No original source work owns the returned Task.");
        Retain(work, task);
    }
    private void Retain(Work work, Task task)
    {
        // A retained native product can owe cleanup after its original factory's async context
        // has exited. Keep its SAME cohort, rather than assigning cleanup to a later/absent caller.
        lock (_gate) work.Sources.Add(task);
    }
    private FilesOriginalReadSourceScope Sources
    {
        get
        {
            var work = _executing.Value ?? throw new InvalidOperationException("No original source cohort owns this scope.");
            return new(Scope, task => Retain(work, task));
        }
    }
    private Task<T> Start<T>(Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        TaskCompletionSource start; Task<T> driver;
        lock (_gate)
        {
            if (_retiring) throw new InvalidOperationException("Original Files project selection is retiring.");
            _work.RemoveWhere(value => value.Healthy && value.Driver.IsCompletedSuccessfully);
            if (_work.Count >= 128) throw new InvalidOperationException("Unresolved original project source custody is full.");
            var work = new Work(_executing.Value); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            driver = Run(start.Task, work, body); work.Driver = driver; _work.Add(work);
        }
        start.TrySetResult(); return driver;
    }
    private async Task<T> Run<T>(Task start, Work work, Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = work; Volatile.Write(ref work.Live, true);
        T value = default!; var errors = new List<Exception>(); var sources = Sources;
        try
        {
            try { value = await sources.Observe(() => body(sources)).ConfigureAwait(false); }
            catch (Exception error) { errors.Add(error); }
            Task[] actual; lock (_gate) actual = work.Sources.ToArray();
            foreach (var task in actual)
                try { await task.ConfigureAwait(false); }
                catch (Exception error) { foreach (var cause in task.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
            if (errors.Count != 0 && errors.All(error => error is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            if (errors.Count != 0) throw new AggregateException("Original Files selection/capture did not settle cleanly.", errors);
            work.Healthy = true; return value;
        }
        finally { Volatile.Write(ref work.Live, false); _executing.Value = previous; }
    }
    private Selection Require(IDeveloperProjectOriginalReadSelection value)
    {
        if (value is not Selection selection || !ReferenceEquals(selection.Issuer, this)) throw new UnauthorizedAccessException("Foreign project source selection.");
        lock (_gate) if (_retiring || !_selections.Contains(selection)) throw new UnauthorizedAccessException("Original project selection retired.");
        return selection;
    }
    public Task<IDeveloperProjectOriginalReadSelection> SelectOriginalExistingProjectAsync(Guid originalStoreId,
        string explicitlySelectedExistingProjectRoot, CancellationToken token) => Start<IDeveloperProjectOriginalReadSelection>(async sources =>
    {
        if (originalStoreId == Guid.Empty) throw new ArgumentException("Retain the actual configured Files store UUID.", nameof(originalStoreId));
        var workspace = await sources.Observe(() => workspaces.GetOriginalCurrentAsync(originalStoreId, sources, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original profile has no verified Files configuration.");
        lock (_gate) if (_selections.Count >= 128) throw new InvalidOperationException("Original project selections are full; no physical root was opened.");
        var root = await sources.Observe(() => _physical.OpenOriginalSelectionAsync(workspace.Configuration.RootDirectory,
            explicitlySelectedExistingProjectRoot, token)).ConfigureAwait(false);
        // Capture the actual returned product before any further awaited authority callbacks.
        var selection = new Selection(this, workspace, root); lock (_gate) _selections.Add(selection);
        selection.Check = await sources.Observe(() => workspaces.CaptureOriginalReadCheckAsync(workspace,
            () => !_retiring && _physical.IsIssuedOriginalSelection(root), sources, token).AsTask()).ConfigureAwait(false);
        await RevalidateBody(selection, workspace.Actor, sources, token).ConfigureAwait(false);
        return selection;
    });
    public bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection value)
    {
        lock (_gate) return !_retiring && value is Selection selection && ReferenceEquals(selection.Issuer, this)
            && _selections.Contains(selection) && _physical.IsIssuedOriginalSelection(selection.Root);
    }
    public bool IsIssuedOriginalPhysicalBinding(IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalPhysicalSelection root)
        => value is Selection selection && ReferenceEquals(selection.Root, root) && IsIssuedOriginal(value);
    public IReadOnlyList<ResourceScope> GetOriginalReadScopes(IDeveloperProjectOriginalReadSelection value)
    { var selection = Require(value); return [selection.Scope]; }
    public Task RevalidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, AuthenticatedResourceActor actor, CancellationToken token)
        => Start(async sources => { await RevalidateBody(Require(value), actor, sources, token).ConfigureAwait(false); return true; });
    private async Task RevalidateBody(Selection selection, AuthenticatedResourceActor actor, FilesOriginalReadSourceScope sources, CancellationToken token)
    {
        if (selection.Workspace.Actor != actor) throw new UnauthorizedAccessException("Original selected Files actor differs.");
        var current = await sources.Observe(() => workspaces.GetOriginalCurrentAsync(selection.Workspace.Configuration.StoreId, sources, token)).ConfigureAwait(false);
        if (current is null || current.Actor != actor || !ReferenceEquals(current.Provider, selection.Workspace.Provider)
            || !ReferenceEquals(current.Directories, selection.Workspace.Directories) || !ReferenceEquals(current.Materializations, selection.Workspace.Materializations)
            || Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(current.Configuration))).ToLowerInvariant() != selection.ConfigurationDigest)
            throw new UnauthorizedAccessException("The original configured Files source/profile changed.");
        if (selection.Check is null || !await sources.Observe(() => selection.Check(token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The original Home profile/configuration/owning-store receipt changed.");
        await RevalidateRegisteredSelectionAsync(selection, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => _physical.RevalidateOriginalSelectionAsync(selection.Root, token)).ConfigureAwait(false);
        if (!IsIssuedOriginal(selection)) throw new UnauthorizedAccessException("Original Files project source retired during revalidation.");
    }
    public Task<IDeveloperProjectOriginalExistingSourceCapture> CaptureOriginalAsync(IDeveloperProjectOriginalReadSelection value,
        IDeveloperProjectOriginalReadAdmission actualReadAdmission, CancellationToken token) => Start<IDeveloperProjectOriginalExistingSourceCapture>(async sources =>
    {
        var selection = Require(value);
        await RevalidateBody(selection, selection.Workspace.Actor, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => reads.ValidateOriginalAsync(selection, actualReadAdmission, token)).ConfigureAwait(false);
        var registered = await CaptureRegisteredWitnessAsync(selection, actualReadAdmission, sources, token).ConfigureAwait(false);
        var capture = await sources.Observe(() => _physical.CaptureOriginalAsync(selection.Root, selection, actualReadAdmission, token)).ConfigureAwait(false);
        // Private issuer custody is recorded before publication, and exact original root is kept.
        lock (_gate)
        {
            _captures.Add(capture, selection);
            if (registered is not null) _registeredCaptures.Add(capture, registered);
        }
        if (!_physical.IsIssuedOriginalCapture(capture, selection.Root, selection) || capture.OriginalExistingProjectRoot != selection.Root.OriginalProjectRoot)
            throw new UnauthorizedAccessException("Kernel source returned no paired actual original capture.");
        await RevalidateBody(selection, selection.Workspace.Actor, sources, token).ConfigureAwait(false);
        DemandRegisteredCaptureNative(selection, capture);
        return capture;
    });
    public bool IsIssuedOriginal(IDeveloperProjectOriginalSourceCapture capture)
    { lock (_gate) return !_retiring && _captures.TryGetValue(capture, out var selection) && IsIssuedOriginal(selection) &&
        IsIssuedRegisteredCapture(selection, capture) && _physical.IsIssuedOriginalCapture(capture, selection.Root, selection); }
    public Task RevalidateOriginalAsync(IDeveloperProjectOriginalSourceCapture capture, AuthenticatedResourceActor actor, CancellationToken token)
        => Start(async sources =>
        {
            Selection selection; lock (_gate) if (!_captures.TryGetValue(capture, out selection!)) throw new UnauthorizedAccessException("Foreign original captured source.");
            await RevalidateBody(selection, actor, sources, token).ConfigureAwait(false);
            await RevalidateRegisteredCaptureAsync(selection, capture, sources, token).ConfigureAwait(false);
            await sources.ObserveVoid(() => _physical.RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
            if (!IsIssuedOriginal(capture)) throw new UnauthorizedAccessException("Original captured source retired.");
            return true;
        });
    /// <summary>Issues one distinct WRITE scope for an exact captured source file/revision. It
    /// requires the SAME issued selection and capture, and the exact project-relative file present
    /// in that capture. The scope revision is the expected current content hash being replaced;
    /// scope metadata alone never grants a write — the Home owner still requires broker approval.</summary>
    public IReadOnlyList<ResourceScope> GetOriginalEditScopes(IDeveloperProjectOriginalReadSelection value,
        IDeveloperProjectOriginalExistingSourceCapture capture, string projectRelativePath, string expectedSha256)
    {
        var selection = Require(value);
        if (capture is null || !IsIssuedOriginal(capture) || capture.OriginalExistingProjectRoot != selection.Root.OriginalProjectRoot)
            throw new UnauthorizedAccessException("Original captured source required for an edit scope.");
        if (!IsHash(expectedSha256)) throw new ArgumentException("The expected current source SHA-256 is required.", nameof(expectedSha256));
        DemandCapturedFile(selection, capture, projectRelativePath);
        return [new ResourceScope("dev.project.file", "dev-file-edit:" + selection.Scope.Id + ":" + projectRelativePath,
            expectedSha256, ResourceAccess.Write)];
    }

    public IReadOnlyList<ResourceScope> GetOriginalCreateScopes(IDeveloperProjectOriginalReadSelection value,
        IDeveloperProjectOriginalExistingSourceCapture capture, string projectRelativePath)
    {
        var selection = Require(value);
        DemandCreateTarget(selection, capture, projectRelativePath);
        return [new ResourceScope("dev.project.file", "dev-file-create:" + selection.Scope.Id + ":" + projectRelativePath,
            DeveloperProjectSourceWriteRevision.Absent, ResourceAccess.Write)];
    }

    public Task RevalidateOriginalCreateScopeAsync(IDeveloperProjectOriginalReadSelection value,
        IDeveloperProjectOriginalExistingSourceCapture capture, string projectRelativePath,
        AuthenticatedResourceActor actor, CancellationToken token)
        => Start(async sources =>
        {
            var selection = Require(value);
            DemandCreateTarget(selection, capture, projectRelativePath);
            await RevalidateBody(selection, actor, sources, token).ConfigureAwait(false);
            await RevalidateRegisteredCaptureAsync(selection, capture, sources, token).ConfigureAwait(false);
            await sources.ObserveVoid(() => _physical.RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
            // A fresh native descriptor probe distinguishes an absent target from an empty file.
            // This short observation grants no WRITE and is never reused as the final commit pin.
            await sources.ObserveVoid(() => _physical.ValidateOriginalCreateTargetAsync(capture, projectRelativePath, token)).ConfigureAwait(false);
            return true;
        });

    private void DemandCreateTarget(Selection selection, IDeveloperProjectOriginalExistingSourceCapture capture, string relativePath)
    {
        lock (_gate)
            if (!_captures.TryGetValue(capture, out var actual) || !ReferenceEquals(actual, selection))
                throw new UnauthorizedAccessException("The SAME original selection/capture is required for CREATE.");
        if (!IsIssuedOriginal(capture)) throw new UnauthorizedAccessException("The original CREATE capture retired.");
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 2048 || Path.IsPathRooted(relativePath) ||
            relativePath.Contains('\\') || relativePath.Split('/').Any(part => string.IsNullOrWhiteSpace(part) || part is "." or ".." ||
                part.Any(value => value == ':' || char.IsControl(value))))
            throw new ArgumentException("CREATE requires one canonical safe project-relative path.", nameof(relativePath));
        if (relativePath.Contains('/'))
            throw new NotSupportedException("This CREATE dependency supports direct project-root files only. Nested publication requires retained captured parent identity.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (capture.OriginalFiles.Any(file => string.Equals(file.RelativePath, relativePath, comparison)) ||
            capture.OriginalFolderPaths.Any(folder => string.Equals(folder, relativePath, comparison)))
            throw new UnauthorizedAccessException("CREATE never replaces an existing captured entry, including an empty file.");
        var slash = relativePath.LastIndexOf('/');
        var parent = slash < 0 ? "" : relativePath[..slash];
        if (parent.Length != 0 && !capture.OriginalFolderPaths.Any(folder => string.Equals(folder, parent, comparison)))
            throw new UnauthorizedAccessException("CREATE requires an existing captured parent; it cannot manufacture directories.");
    }

    public Task<IDeveloperProjectOriginalEditCurrentnessPin> AcquireOriginalCreateCurrentnessPinAsync(
        IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalExistingSourceCapture capture,
        string projectRelativePath, string reviewedAfterSha256, CancellationToken token)
        => Start<IDeveloperProjectOriginalEditCurrentnessPin>(async sources =>
        {
            var selection = Require(value);
            DemandCreateTarget(selection, capture, projectRelativePath);
            if (!IsHash(reviewedAfterSha256)) throw new ArgumentException("The exact reviewed AFTER SHA-256 is required.", nameof(reviewedAfterSha256));
            await RevalidateBody(selection, selection.Workspace.Actor, sources, token).ConfigureAwait(false);
            await RevalidateRegisteredCaptureAsync(selection, capture, sources, token).ConfigureAwait(false);
            await sources.ObserveVoid(() => _physical.RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
            IDeveloperProjectOriginalEditCurrentnessPin? native = null;
            try
            {
                native = await sources.ObserveProduct(() => _physical.AcquireOriginalCreateCurrentnessPinAsync(
                    capture, projectRelativePath, reviewedAfterSha256, token), actual => native = actual).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_retiring || _createPins.Count >= 128)
                        throw new InvalidOperationException("Original CREATE currentness custody is sealed or full.");
                    var pin = new CreateCurrentnessPin(this, selection, capture, projectRelativePath, native);
                    _createPins.Add(pin);
                    return pin;
                }
            }
            catch (Exception primary)
            {
                if (native is not null)
                    try { await sources.ObserveVoid(() => native.DisposeAsync().AsTask()).ConfigureAwait(false); }
                    catch (Exception cleanup) { throw new AggregateException("Original CREATE pin acquisition and independent cleanup failed.", primary, cleanup); }
                throw;
            }
        });

    private sealed class CreateCurrentnessPin(FilesDeveloperOriginalSourceSelection owner, Selection selection,
        IDeveloperProjectOriginalExistingSourceCapture capture, string relativePath,
        IDeveloperProjectOriginalEditCurrentnessPin native) : IDeveloperProjectOriginalEditCurrentnessPin
    {
        private readonly object _sync = new();
        private Task? _close;
        public bool RequiresAbsentTarget => true;
        public string ReviewedAfterSha256 => native.ReviewedAfterSha256;
        public void DemandOriginalEditCurrentness()
        {
            lock (owner._gate) lock (_sync)
            {
                if (owner._retiring || _close is not null || !owner._createPins.Contains(this))
                    throw new UnauthorizedAccessException("Original CREATE currentness custody retired.");
                owner.Require(selection);
                owner.DemandCreateTarget(selection, capture, relativePath);
                owner.DemandRegisteredCaptureNative(selection, capture);
                if (!native.RequiresAbsentTarget) throw new UnauthorizedAccessException("The original native pin is not an absent-target pin.");
                native.DemandOriginalEditCurrentness();
            }
        }
        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_close is not null) return new(_close);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _close = Close(start.Task); start.SetResult(); return new(_close);
            }
        }
        private async Task Close(Task start)
        {
            await start.ConfigureAwait(false);
            await native.DisposeAsync().ConfigureAwait(false);
            lock (owner._gate) owner._createPins.Remove(this);
        }
    }
    /// <summary>Revalidates the SAME selection/capture and that the exact project-relative file is
    /// still part of the capture, before an edit scope is relied on. The exact current byte hash is
    /// rechecked by the kernel just before the write, under the SAME READ admission.</summary>
    public Task RevalidateOriginalEditScopeAsync(IDeveloperProjectOriginalReadSelection value,
        IDeveloperProjectOriginalExistingSourceCapture capture, string projectRelativePath, string expectedSha256,
        AuthenticatedResourceActor actor, CancellationToken token)
        => Start(async sources =>
        {
            var selection = Require(value);
            if (capture is null || !_captures.TryGetValue(capture, out var owned) || !ReferenceEquals(owned, selection))
                throw new UnauthorizedAccessException("Foreign or retired original captured source.");
            await RevalidateBody(selection, actor, sources, token).ConfigureAwait(false);
            await RevalidateRegisteredCaptureAsync(selection, capture, sources, token).ConfigureAwait(false);
            await sources.ObserveVoid(() => _physical.RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
            DemandCapturedFile(selection, capture, projectRelativePath);
            if (!IsIssuedOriginal(capture)) throw new UnauthorizedAccessException("Original captured source retired during edit revalidation.");
            if (!IsHash(expectedSha256)) throw new ArgumentException("The expected current source SHA-256 is required.", nameof(expectedSha256));
            return true;
        });
    /// <summary>Acquires genuine finite-currentness custody for one exact reviewed manual source-EDIT
    /// commit. Full async SAME selection/capture/config/actor/file revalidation runs here, outside any
    /// held Home lease; the returned pin's probe is pure and synchronous, so it is safe to run beneath a
    /// held Home entry. A completed prevalidation alone never freezes later source/capture changes — the
    /// probe re-checks live private issuance immediately before the finite swap.</summary>
    public Task<IDeveloperProjectOriginalEditCurrentnessPin> AcquireOriginalEditCurrentnessPinAsync(
        IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalExistingSourceCapture capture,
        string projectRelativePath, string expectedSha256, string reviewedAfterSha256, CancellationToken token)
        => Start<IDeveloperProjectOriginalEditCurrentnessPin>(async sources =>
    {
        var selection = Require(value);
        if (capture is null || !_captures.TryGetValue(capture, out var owned) || !ReferenceEquals(owned, selection))
            throw new UnauthorizedAccessException("Foreign or retired original captured source.");
        await RevalidateBody(selection, selection.Workspace.Actor, sources, token).ConfigureAwait(false);
        await RevalidateRegisteredCaptureAsync(selection, capture, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => _physical.RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
        DemandCapturedFile(selection, capture, projectRelativePath);
        if (!IsHash(expectedSha256)) throw new ArgumentException("The reviewed BEFORE source SHA-256 is required.", nameof(expectedSha256));
        if (!IsHash(reviewedAfterSha256)) throw new ArgumentException("The reviewed AFTER source SHA-256 is required.", nameof(reviewedAfterSha256));
        // Bind the reviewed BEFORE revision to the genuinely captured source file content, not to a
        // remembered boolean or private registry membership alone.
        var bound = capture.OriginalFiles.First(file => StringComparer.Ordinal.Equals(file.RelativePath, projectRelativePath));
        if (!string.Equals(bound.ContentSha256, expectedSha256, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The reviewed BEFORE revision does not match the genuinely captured source file content.");
        EditCurrentnessPin pin; lock (_gate)
        {
            if (_retiring) throw new UnauthorizedAccessException("Original Files project selection is retiring.");
            if (_pins.Count >= 128) throw new InvalidOperationException("Original edit currentness custody is full.");
            pin = new(this, selection, capture, projectRelativePath, expectedSha256, reviewedAfterSha256); _pins.Add(pin);
        }
        return pin;
    });
    private static void DemandCapturedFile(Selection selection, IDeveloperProjectOriginalExistingSourceCapture capture, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 2048 || relativePath.IndexOf('\0') >= 0 ||
            Path.IsPathRooted(relativePath) || relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(value => value is "." or ".."))
            throw new ArgumentException("A safe project-relative source path is required.", nameof(relativePath));
        var match = capture.OriginalFiles.Where(file => StringComparer.Ordinal.Equals(file.RelativePath, relativePath)).Take(2).ToArray();
        if (match.Length != 1) throw new UnauthorizedAccessException("The exact captured source file is not part of this selected project.");
    }
    private static bool IsHash(string value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    /// <summary>A genuine finite-currentness pin owned by this configured Files producer for one exact
    /// reviewed manual source-EDIT commit. Its probe is pure identity/issuance/native metadata only —
    /// it never reenters async or public Home/Files/store methods, so it is safe beneath a held Home
    /// entry. It binds the SAME selection/capture/target and the reviewed BEFORE/AFTER digests.</summary>
    private sealed class EditCurrentnessPin(FilesDeveloperOriginalSourceSelection owner, Selection selection,
        IDeveloperProjectOriginalExistingSourceCapture capture, string relativePath,
        string expectedSha256, string reviewedAfterSha256) : IDeveloperProjectOriginalEditCurrentnessPin
    {
        internal FilesDeveloperOriginalSourceSelection Owner => owner;
        internal bool Sealed;
        internal Task? Close;
        public string ReviewedAfterSha256 => reviewedAfterSha256;
        public void DemandOriginalEditCurrentness()
        {
            lock (owner._gate)
                if (owner._retiring || Sealed || !owner._pins.Contains(this))
                    throw new UnauthorizedAccessException("The SAME original manual edit currentness pin retired.");
            // Genuine live private issuance + retained native physical-state identity/currentness.
            // A completed prevalidation alone never freezes source/capture changes; these live probes
            // run immediately before the finite swap. No remembered boolean or private registry
            // membership alone can satisfy this.
            if (!owner.IsIssuedOriginal(selection) || !owner.IsIssuedOriginal(capture))
                throw new UnauthorizedAccessException("The SAME configured Files source/capture retired before the finite swap.");
            owner.DemandRegisteredCaptureNative(selection, capture);
            DemandCapturedFile(selection, capture, relativePath);
            // Re-bind the reviewed BEFORE revision to the retained captured source content at probe
            // time; the genuine finite currentness is the live native physical state below, not a
            // remembered boolean or private registry membership alone.
            var bound = capture.OriginalFiles.First(file => StringComparer.Ordinal.Equals(file.RelativePath, relativePath));
            if (!string.Equals(bound.ContentSha256, expectedSha256, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The reviewed BEFORE revision no longer matches the retained captured source content.");
            if (!owner._physical.IsOriginalCapturedFileCurrentNative(capture, relativePath))
                throw new UnauthorizedAccessException("The genuine captured source changed before the finite swap.");
            lock (owner._gate)
                if (owner._retiring || Sealed || !owner._pins.Contains(this))
                    throw new UnauthorizedAccessException("The SAME original manual edit currentness pin retired during its probe.");
        }
        public ValueTask DisposeAsync() => new(owner.ClosePin(this));
    }
    private Task ClosePin(EditCurrentnessPin pin)
    {
        TaskCompletionSource start; Task close;
        lock (_gate)
        {
            if (pin.Close is not null) return pin.Close;
            if (!_pins.Contains(pin)) throw new UnauthorizedAccessException("Unknown original edit currentness pin cleanup was refused.");
            pin.Sealed = true; start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            pin.Close = close = DrainPin(pin);
        }
        start.TrySetResult(); return close;
        async Task DrainPin(EditCurrentnessPin p)
        {
            await start.Task.ConfigureAwait(false);
            lock (_gate) _pins.Remove(p);
        }
    }
    public void RequestOriginalSelectionRetirement() { lock (_gate) _retiring = true; }
    public Task CloseAndDrainOriginalSelectionsAsync()
    {
        DemandExternalOriginalCaptureJoin(); TaskCompletionSource start; Task driver; Work[] originals;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; originals = _work.ToArray(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            driver = Drain(start.Task, originals); _close = driver;
        }
        start.TrySetResult(); return driver;
    }
    private async Task Drain(Task start, Work[] originals)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        foreach (var original in originals)
            try { await original.Driver.ConfigureAwait(false); }
            catch (Exception error) { errors.AddRange(original.Driver.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()); }
        // Close every genuine edit currentness pin acquired before retirement.
        EditCurrentnessPin[] pins; lock (_gate) pins = _pins.ToArray();
        foreach (var pin in pins)
            try { await pin.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { errors.AddRange(error is AggregateException aggregate ? aggregate.InnerExceptions : new[] { error }.AsEnumerable()); }
        CreateCurrentnessPin[] creates; lock (_gate) creates = _createPins.ToArray();
        foreach (var pin in creates)
            try { await pin.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { errors.AddRange(error is AggregateException aggregate ? aggregate.InnerExceptions : new[] { error }.AsEnumerable()); }
        if (_registeredNative.IsValueCreated)
        {
            var native = _registeredNative.Value; native.RequestOriginalRetirement();
            var actualNativeClose = native.CloseAndDrainOriginalAsync();
            try { await actualNativeClose.ConfigureAwait(false); }
            catch (Exception error) { errors.AddRange(actualNativeClose.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()); }
        }
        // Kernel owner retains failed roots too; stopping after parent drivers settle captures
        // every late product without relying on returned selection DTOs or task-status flags.
        _physical.RequestOriginalCaptureRetirement(); var actual = _physical.CloseAndDrainOriginalCapturesAsync();
        try { await actual.ConfigureAwait(false); } catch (Exception error) { errors.AddRange(actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()); }
        if (errors.Count != 0) throw new AggregateException("Original Files project source drain failed.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalSelectionsAsync());
}
