using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

/// <summary>The configured Files owner issues an opaque existing-project selection, retaining
/// SAME profile/store/provider/configuration and actual kernel root. No manifest, FileId, copy,
/// import effect or permission is produced by selection. Home approves its distinct READ first.</summary>
public sealed class FilesDeveloperOriginalSourceSelection(NativeFilesWorkspaceAuthority workspaces,
    IDeveloperProjectOriginalPhysicalCaptureSource physical,
    IDeveloperProjectOriginalReadAdmissionSource reads)
    : IDeveloperProjectOriginalPhysicalReadSelectionSource, IDeveloperProjectOriginalCaptureAuthority, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly AsyncLocal<Work?> _executing = new();
    [ThreadStatic] private static Dictionary<FilesDeveloperOriginalSourceSelection, int>? _physicalSources;
    private readonly HashSet<Work> _work = [];
    private readonly HashSet<Selection> _selections = [];
    private readonly Dictionary<IDeveloperProjectOriginalSourceCapture, Selection> _captures = new(ReferenceEqualityComparer.Instance);
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
        IDeveloperProjectOriginalPhysicalSelection root) : IDeveloperProjectOriginalReadSelection
    {
        public FilesDeveloperOriginalSourceSelection Issuer => issuer;
        public NativeFilesWorkspace Workspace => workspace;
        public IDeveloperProjectOriginalPhysicalSelection Root => root;
        public Func<CancellationToken, ValueTask<bool>> Check = null!;
        public string ConfigurationDigest { get; } = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(workspace.Configuration))).ToLowerInvariant();
        public ResourceScope Scope { get; } = new("dev.project.source", "dev-source-selection:" + Guid.NewGuid().ToString("D"),
            Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { workspace.Configuration, workspace.Actor, root.OriginalProjectRoot }))).ToLowerInvariant(), ResourceAccess.Read);
    }
    public void DemandExternalOriginalReadSelectionJoin()
    {
        if (_physicalSources?.ContainsKey(this) == true) throw new InvalidOperationException("An original Files callback cannot join its source selection owner.");
        for (var current = _executing.Value; current is not null; current = current.Parent)
            if (Volatile.Read(ref current.Live)) throw new InvalidOperationException("An original Files source cannot join its own retirement.");
    }
    public void DemandExternalOriginalCaptureJoin()
    { DemandExternalOriginalReadSelectionJoin(); physical.DemandExternalOriginalJoin(); }
    private void Scope(Action action)
    {
        var values = _physicalSources ??= []; values.TryGetValue(this, out var before); values[this] = before + 1;
        try { action(); }
        finally { if (before == 0) values.Remove(this); else values[this] = before; }
    }
    private void Retain(Task task)
    {
        var work = _executing.Value ?? throw new InvalidOperationException("No original source work owns the returned Task.");
        lock (_gate) work.Sources.Add(task);
    }
    private FilesOriginalReadSourceScope Sources => new(Scope, Retain);
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
        var root = await sources.Observe(() => physical.OpenOriginalSelectionAsync(workspace.Configuration.RootDirectory,
            explicitlySelectedExistingProjectRoot, token)).ConfigureAwait(false);
        // Capture the actual returned product before any further awaited authority callbacks.
        var selection = new Selection(this, workspace, root); lock (_gate) _selections.Add(selection);
        selection.Check = await sources.Observe(() => workspaces.CaptureOriginalReadCheckAsync(workspace,
            () => !_retiring && physical.IsIssuedOriginalSelection(root), sources, token).AsTask()).ConfigureAwait(false);
        await RevalidateBody(selection, workspace.Actor, sources, token).ConfigureAwait(false);
        return selection;
    });
    public bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection value)
    {
        lock (_gate) return !_retiring && value is Selection selection && ReferenceEquals(selection.Issuer, this)
            && _selections.Contains(selection) && physical.IsIssuedOriginalSelection(selection.Root);
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
        await sources.ObserveVoid(() => physical.RevalidateOriginalSelectionAsync(selection.Root, token)).ConfigureAwait(false);
        if (!IsIssuedOriginal(selection)) throw new UnauthorizedAccessException("Original Files project source retired during revalidation.");
    }
    public Task<IDeveloperProjectOriginalExistingSourceCapture> CaptureOriginalAsync(IDeveloperProjectOriginalReadSelection value,
        IDeveloperProjectOriginalReadAdmission actualReadAdmission, CancellationToken token) => Start<IDeveloperProjectOriginalExistingSourceCapture>(async sources =>
    {
        var selection = Require(value);
        await RevalidateBody(selection, selection.Workspace.Actor, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => reads.ValidateOriginalAsync(selection, actualReadAdmission, token)).ConfigureAwait(false);
        var capture = await sources.Observe(() => physical.CaptureOriginalAsync(selection.Root, selection, actualReadAdmission, token)).ConfigureAwait(false);
        // Private issuer custody is recorded before publication, and exact original root is kept.
        lock (_gate) _captures.Add(capture, selection);
        if (!physical.IsIssuedOriginalCapture(capture, selection.Root, selection) || capture.OriginalExistingProjectRoot != selection.Root.OriginalProjectRoot)
            throw new UnauthorizedAccessException("Kernel source returned no paired actual original capture.");
        await RevalidateBody(selection, selection.Workspace.Actor, sources, token).ConfigureAwait(false);
        return capture;
    });
    public bool IsIssuedOriginal(IDeveloperProjectOriginalSourceCapture capture)
    { lock (_gate) return !_retiring && _captures.TryGetValue(capture, out var selection) && IsIssuedOriginal(selection) && physical.IsIssuedOriginalCapture(capture, selection.Root, selection); }
    public Task RevalidateOriginalAsync(IDeveloperProjectOriginalSourceCapture capture, AuthenticatedResourceActor actor, CancellationToken token)
        => Start(async sources =>
        {
            Selection selection; lock (_gate) if (!_captures.TryGetValue(capture, out selection!)) throw new UnauthorizedAccessException("Foreign original captured source.");
            await RevalidateBody(selection, actor, sources, token).ConfigureAwait(false);
            await sources.ObserveVoid(() => physical.RevalidateOriginalCaptureAsync(capture, token)).ConfigureAwait(false);
            if (!IsIssuedOriginal(capture)) throw new UnauthorizedAccessException("Original captured source retired.");
            return true;
        });
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
        // Kernel owner retains failed roots too; stopping after parent drivers settle captures
        // every late product without relying on returned selection DTOs or task-status flags.
        physical.RequestOriginalCaptureRetirement(); var actual = physical.CloseAndDrainOriginalCapturesAsync();
        try { await actual.ConfigureAwait(false); } catch (Exception error) { errors.AddRange(actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()); }
        if (errors.Count != 0) throw new AggregateException("Original Files project source drain failed.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalSelectionsAsync());
}
