using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Files.NativeHost;

/// <summary>Destination ownership observation only. Home separately reviews the high-risk
/// exact setup intent and issues its held final entry. This source neither creates a folder,
/// adopts a store, registers a project nor treats initial source-read custody as write approval.
/// References are retained with a finite fail-stop limit until this owner is retired.</summary>
public sealed partial class FilesDeveloperOriginalSetupScopeSource(NativeFilesWorkspaceAuthority workspaces,
    IDeveloperProjectOriginalCaptureAuthority captures, Func<HomeDeveloperProjectSetupJournal> journal)
    : IDeveloperProjectOriginalSetupScopeSource, ICanonicalResourceAccessResolver, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly AsyncLocal<Original?> _executing = new();
    [ThreadStatic] private static Dictionary<FilesDeveloperOriginalSetupScopeSource, int>? _physical;
    [ThreadStatic] private static HashSet<FilesDeveloperOriginalSetupScopeSource>? _checkingDependencies;
    private readonly HashSet<Original> _originals = [];
    private readonly HashSet<Destination> _destinations = [];
    private readonly HashSet<HomeDeveloperProjectSetupJournal> _journals = [];
    private readonly Dictionary<DeveloperProjectSetupIntent, Binding> _bindings = new(ReferenceEqualityComparer.Instance);
    private bool _retiring;
    private Task? _close;
    public string ResourceKind => "dev.project.destination";
    private sealed class Original(Original? parent)
    {
        internal Original? Parent => parent;
        internal bool Live;
        internal bool Healthy;
        internal Task Driver = null!;
        internal readonly List<Task> Sources = [];
    }
    public sealed class Destination
    {
        internal Destination(FilesDeveloperOriginalSetupScopeSource owner, NativeFilesWorkspace workspace, HostedItemMetadata folder)
        { Owner = owner; Workspace = workspace; Folder = folder; ConfigurationDigest = Hash(workspace.Configuration); }
        internal FilesDeveloperOriginalSetupScopeSource Owner { get; }
        internal NativeFilesWorkspace Workspace { get; }
        internal HostedItemMetadata Folder { get; }
        internal string ConfigurationDigest { get; }
        internal string ReceiptId { get; } = "dev-destination:" + Guid.NewGuid().ToString("D");
        // Setup metadata for genuine preparation only; values are not issuer/permission evidence.
        public Guid OriginalStoreId => Workspace.Configuration.StoreId;
        public Guid OriginalFolderId => Folder.Id.Value;
        public string OriginalFolderRevision => Folder.CurrentRevisionId!.Value.Value.ToString("D");
        public string OriginalConfigurationDigest => ConfigurationDigest;
        public AuthenticatedResourceActor OriginalActor => Workspace.Actor;
    }
    private sealed record Binding(Destination Destination, HomeDeveloperProjectSetupJournal Journal,
        HomeDeveloperProjectSetupJournal.Prepared Prepared, DeveloperProjectSetupIntent Intent,
        IDeveloperProjectOriginalSourceCapture Capture, ResourceScope Scope);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();

    public void DemandExternalOriginalSetupScopeJoin()
    {
        if (_physical?.ContainsKey(this) == true) throw new InvalidOperationException("An original destination callback cannot join its owner.");
        for (var original = _executing.Value; original is not null; original = original.Parent)
            if (Volatile.Read(ref original.Live)) throw new InvalidOperationException("An original destination producer cannot join itself.");
        // A journal's configured outcome owner may borrow this SAME scope source. Check
        // this owner's live context first, then visit each dependency once on this thread.
        // This is pure join-cycle inspection, never an authority or healthy-close witness.
        var visited = _checkingDependencies ??= [];
        if (!visited.Add(this)) return;
        try
        {
            captures.DemandExternalOriginalCaptureJoin();
            HomeDeveloperProjectSetupJournal[] journals; lock (_gate) journals = _journals.ToArray();
            foreach (var owner in journals) owner.DemandExternalOriginalRetirementJoin();
        }
        finally { visited.Remove(this); }
    }
    private void Scope(Action source)
    {
        var values = _physical ??= []; values.TryGetValue(this, out var before); values[this] = before + 1;
        try { source(); }
        finally { if (before == 0) values.Remove(this); else values[this] = before; }
    }
    private Task<T> Start<T>(Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        TaskCompletionSource start; Task<T> driver;
        lock (_gate)
        {
            if (_retiring) throw new InvalidOperationException("The original destination source is retiring.");
            _originals.RemoveWhere(value => value.Healthy && value.Driver.IsCompletedSuccessfully);
            if (_originals.Count >= 128) throw new InvalidOperationException("Unresolved original destination task custody is full.");
            var original = new Original(_executing.Value); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            driver = Run(start.Task, original, body); original.Driver = driver; _originals.Add(original);
        }
        start.TrySetResult(); return driver;
    }
    private async Task<T> Run<T>(Task start, Original original, Func<FilesOriginalReadSourceScope, Task<T>> body)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = original; Volatile.Write(ref original.Live, true);
        var sources = new FilesOriginalReadSourceScope(Scope, actual => { lock (_gate) original.Sources.Add(actual); });
        var errors = new List<Exception>(); T result = default!; Task<T>? actualBody = null;
        try
        {
            try { result = await sources.Observe(() => actualBody = body(sources)).ConfigureAwait(false); }
            catch (Exception error) { Add(errors, error); }
            Task[] actual; lock (_gate) actual = original.Sources.ToArray();
            foreach (var task in actual)
                try { await task.ConfigureAwait(false); }
                catch (Exception error) { AddTask(errors, task, error); }
            if (errors.Count != 0)
            {
                if (actualBody?.IsCanceled == true && actual.All(task => !task.IsFaulted) && errors.All(error => error is OperationCanceledException))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
                throw new AggregateException("Original destination observation did not settle cleanly.", errors);
            }
            original.Healthy = true; return result;
        }
        finally { Volatile.Write(ref original.Live, false); _executing.Value = previous; }
    }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException compound && compound.InnerExceptions.Count != 0)
        { foreach (var cause in compound.InnerExceptions) Add(errors, cause); }
        else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
    }
    private static void AddTask(List<Exception> errors, Task actual, Exception caught)
    { foreach (var error in actual.Exception?.InnerExceptions ?? new[] { caught }.AsEnumerable()) Add(errors, error); }
    private void RequireDestination(Destination destination)
    {
        lock (_gate) if (_retiring || !ReferenceEquals(destination.Owner, this) || !_destinations.Contains(destination))
            throw new UnauthorizedAccessException("Retain the SAME private current destination selection.");
    }
    private static void RequireFolder(NativeFilesWorkspace workspace, HostedItemMetadata folder)
    {
        if (workspace.Actor.AccountId is not null || workspace.Actor.OrganisationId is not null ||
            folder.Kind != HostedItemKind.Folder || folder.IsShared || folder.OwnerPrincipalId != workspace.Actor.ActorId ||
            folder.LocationId != workspace.Configuration.LocationId || folder.CurrentRevisionId is not { } revision || revision.Value == Guid.Empty)
            throw new UnauthorizedAccessException("Select an actual owned personal folder with an acknowledged revision.");
    }
    private static bool SameWorkspace(NativeFilesWorkspace before, NativeFilesWorkspace after) => before.Actor == after.Actor &&
        ReferenceEquals(before.Provider, after.Provider) && ReferenceEquals(before.Directories, after.Directories) &&
        ReferenceEquals(before.Materializations, after.Materializations) && Hash(before.Configuration) == Hash(after.Configuration);
    private async Task RevalidateDestination(Destination destination, FilesOriginalReadSourceScope sources, CancellationToken token)
    {
        RequireDestination(destination);
        var current = await sources.Observe(() => workspaces.GetOriginalCurrentAsync(destination.OriginalStoreId, sources, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original destination configuration is no longer current.");
        if (!SameWorkspace(destination.Workspace, current) || Hash(current.Configuration) != destination.ConfigurationDigest)
            throw new UnauthorizedAccessException("The original destination actor/configuration changed.");
        var row = await sources.Observe(() => current.Provider.GetForOriginalStoreAtRevisionAsync(destination.OriginalStoreId,
            destination.Folder.Id, destination.Folder.CurrentRevisionId!.Value, token)).ConfigureAwait(false);
        if (!row.IsSuccess || row.Value != destination.Folder) throw new UnauthorizedAccessException("The actual destination folder changed or disappeared.");
        RequireFolder(current, row.Value!);
        // The current profile/configuration is observed again after the owning folder read.
        var after = await sources.Observe(() => workspaces.GetOriginalCurrentAsync(destination.OriginalStoreId, sources, token)).ConfigureAwait(false);
        if (after is null || !SameWorkspace(current, after)) throw new UnauthorizedAccessException("Destination configuration changed during observation.");
        token.ThrowIfCancellationRequested(); RequireDestination(destination);
    }
    public Task<Destination> CaptureOriginalDestinationAsync(Guid actualConfiguredStoreId, HostedItemId actualSelectedFolder,
        CancellationToken token = default) => Start(async sources =>
    {
        if (actualConfiguredStoreId == Guid.Empty || actualSelectedFolder.Value == Guid.Empty) throw new ArgumentException("Select the actual configured store and canonical folder.");
        lock (_gate) if (_destinations.Count >= 128) throw new InvalidOperationException("Original destination reference custody is full; no further selection was read.");
        var workspace = await sources.Observe(() => workspaces.GetOriginalCurrentAsync(actualConfiguredStoreId, sources, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("A genuine current Files configuration/ownership receipt is required.");
        var row = await sources.Observe(() => workspace.Provider.GetForOriginalStoreAsync(actualConfiguredStoreId, actualSelectedFolder, token)).ConfigureAwait(false);
        if (!row.IsSuccess || row.Value is null) throw new UnauthorizedAccessException("The selected canonical destination folder does not exist.");
        RequireFolder(workspace, row.Value);
        var destination = new Destination(this, workspace, row.Value);
        lock (_gate) { if (_destinations.Count >= 128) throw new InvalidOperationException("Original destination reference custody is full."); _destinations.Add(destination); }
        await RevalidateDestination(destination, sources, token).ConfigureAwait(false); return destination;
    });
    public Task RevalidateOriginalDestinationAsync(Destination sameDestination, CancellationToken token = default)
        => Start(async sources => { await RevalidateDestination(sameDestination, sources, token).ConfigureAwait(false); return true; });

    /// <summary>Only the SAME configured journal's successful private preparation can bind
    /// setup scopes. This performs no effect and obtains no Home review or claimed capability.</summary>
    public Task BindOriginalAsync(Destination sameDestination, HomeDeveloperProjectSetupJournal.Prepared samePrepared,
        IDeveloperProjectOriginalSourceCapture sameCapture, CancellationToken token = default) => Start(async sources =>
    {
        RequireDestination(sameDestination);
        var owner = sources.Invoke(journal) ?? throw new InvalidOperationException("The original setup journal is not configured.");
        lock (_gate) _journals.Add(owner);
        await sources.ObserveVoid(() => owner.ValidateOriginalPreparationAsync(samePrepared, sameCapture, token)).ConfigureAwait(false);
        var intent = sources.Invoke(() => samePrepared.Intent);
        if (intent.Validate() is not null || intent.OriginalActor != sameDestination.OriginalActor ||
            intent.OriginalFilesStoreId != sameDestination.OriginalStoreId || intent.OriginalFilesConfigurationDigest != sameDestination.ConfigurationDigest ||
            intent.OriginalDestinationFolderId != sameDestination.OriginalFolderId || intent.OriginalDestinationFolderRevision != sameDestination.OriginalFolderRevision ||
            !sources.Invoke(() => captures.IsIssuedOriginal(sameCapture)) || intent.OriginalSourceCaptureReference != sameCapture.OriginalCaptureReference ||
            intent.OriginalSourceDigest != sameCapture.OriginalCaptureDigest)
            throw new UnauthorizedAccessException("The SAME private intent/capture does not describe this actual destination.");
        await sources.ObserveVoid(() => captures.RevalidateOriginalAsync(sameCapture, intent.OriginalActor, token)).ConfigureAwait(false);
        await RevalidateDestination(sameDestination, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => owner.ValidateOriginalPreparationAsync(samePrepared, sameCapture, token)).ConfigureAwait(false);
        var binding = new Binding(sameDestination, owner, samePrepared, intent, sameCapture,
            new(ResourceKind, sameDestination.ReceiptId, Hash(new { sameDestination.Workspace.Configuration,
                sameDestination.Workspace.Actor, sameDestination.Folder, intent.SetupId, IntentDigest = intent.Digest() }), ResourceAccess.Write));
        lock (_gate)
        {
            if (_bindings.TryGetValue(intent, out var previous))
            { if (!ReferenceEquals(previous.Destination, sameDestination) || !ReferenceEquals(previous.Prepared, samePrepared) || !ReferenceEquals(previous.Capture, sameCapture))
                  throw new UnauthorizedAccessException("An existing private setup binding cannot be replaced."); return true; }
            if (_bindings.Count >= 128) throw new InvalidOperationException("Original destination setup bindings are full.");
            if (_retiring) throw new InvalidOperationException("Original destination setup retired before binding.");
            _bindings.Add(intent, binding);
        }
        return true;
    });
    private Binding RequireBinding(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture)
    {
        lock (_gate)
            if (!_retiring && _bindings.TryGetValue(intent, out var binding) && ReferenceEquals(binding.Capture, capture) &&
                ReferenceEquals(binding.Prepared.Intent, intent) && _destinations.Contains(binding.Destination)) return binding;
        throw new UnauthorizedAccessException("No SAME privately bound current destination/intent/capture exists.");
    }
    public bool IsIssuedOriginalSetupBinding(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture)
    {
        Binding? binding; lock (_gate) binding = !_retiring && _bindings.TryGetValue(intent, out var value) ? value : null;
        return binding is not null && ReferenceEquals(binding.Capture, capture) && ReferenceEquals(binding.Prepared.Intent, intent)
            && captures.IsIssuedOriginal(capture);
    }
    public IReadOnlyList<ResourceScope> GetOriginalSetupScopes(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture)
    { var binding = RequireBinding(intent, capture); return [binding.Scope]; }
    // Only the maintained NativeHost effector consumes the privately bound original provider.
    // This observation is not a Home entry, permission or a substitute for its final fence.
    internal Destination GetOriginalBoundDestination(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture)
        => RequireBinding(intent, capture).Destination;
    internal bool IsCheckingOriginalJournalDependencies => _checkingDependencies?.Contains(this) == true;
    public Task RevalidateOriginalSetupAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture,
        AuthenticatedResourceActor actor, CancellationToken token) => Start(async sources =>
    {
        var binding = RequireBinding(intent, capture);
        if (intent.OriginalActor != actor) throw new UnauthorizedAccessException("The original setup actor differs.");
        await sources.ObserveVoid(() => binding.Journal.ValidateOriginalPreparationAsync(binding.Prepared, capture, token)).ConfigureAwait(false);
        await sources.ObserveVoid(() => captures.RevalidateOriginalAsync(capture, actor, token)).ConfigureAwait(false);
        await RevalidateDestination(binding.Destination, sources, token).ConfigureAwait(false);
        RequireBinding(intent, capture); return true;
    });
    public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope,
        CancellationToken token) => new(Start(async sources =>
    {
        Binding? binding;
        lock (_gate) binding = !_retiring ? _bindings.Values.SingleOrDefault(value => value.Scope == scope) : null;
        if (binding is null || actionId != "dev.project.setup.commit" || scope.Access != ResourceAccess.Write || actor != binding.Intent.OriginalActor)
            return new ResourceAccessDecision(false, "OriginalDestinationRequired", actor.ActorId, scope.Revision, actor.OrganisationId);
        // Every resolver observation repeats genuine owning authority outside any Home lease.
        await sources.ObserveVoid(() => RevalidateOriginalSetupAsync(binding.Intent, binding.Capture, actor, token)).ConfigureAwait(false);
        return new ResourceAccessDecision(true, "OriginalDestinationOwned", actor.ActorId, scope.Revision, actor.OrganisationId);
    }));
    public void RequestOriginalSetupScopeRetirement() { lock (_gate) _retiring = true; }
    public Task CloseAndDrainOriginalSetupScopesAsync()
    {
        DemandExternalOriginalSetupScopeJoin(); TaskCompletionSource start; Task driver; Original[] originals;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true; originals = _originals.ToArray(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            driver = Drain(start.Task, originals); _close = driver;
        }
        start.TrySetResult(); return driver;
    }
    private static async Task Drain(Task start, Original[] originals)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        foreach (var original in originals)
            try { await original.Driver.ConfigureAwait(false); }
            catch (Exception error) { AddTask(errors, original.Driver, error); }
        if (errors.Count != 0) throw new AggregateException("Original destination ownership drain failed.", errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalSetupScopesAsync());
}
