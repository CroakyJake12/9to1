using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

/// <summary>Fresh current personal Files registration/container observation. This owner
/// never reconstructs a setup ACK, reads saved document bytes before READ, creates metadata,
/// grants an action, or owns retirement of borrowed Home/Files/Task repositories.</summary>
public sealed class FilesDeveloperOriginalCurrentProjectSelection(
    NativeFilesWorkspaceAuthority workspaces, FileDeveloperWorkspaceStore originalStore,
    IContainerRepository containers) : IDeveloperOriginalCurrentProjectSelectionSource, IDeveloperOriginalCurrentProjectRestorationSelectionSource,
    IDeveloperOriginalCurrentProjectSelectionRetirementSource, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly AsyncLocal<Original?> _executing = new();
    [ThreadStatic] private static Dictionary<FilesDeveloperOriginalCurrentProjectSelection, int>? _physical;
    private readonly HashSet<Original> _originals = [];
    private readonly HashSet<Selection> _selections = [];
    private bool _retiring; private Task? _close;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed class Original(Original? parent)
    {
        internal Original? Parent => parent;
        internal Task Driver = null!; internal bool Live;
        internal readonly List<Task> Raw = [];
    }
    private sealed class Selection(FilesDeveloperOriginalCurrentProjectSelection owner, Original original, NativeFilesWorkspace workspace,
        FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot registration, Descriptor descriptor)
        : IDeveloperOriginalCurrentProjectSelection
    {
        internal FilesDeveloperOriginalCurrentProjectSelection Owner => owner;
        internal Original Original => original;
        internal NativeFilesWorkspace Workspace => workspace;
        internal FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot Registration => registration;
        internal Descriptor Descriptor => descriptor;
        internal string Configuration { get; } = Serialize(workspace.Configuration);
        internal ResourceScope Scope { get; } = new("dev.project.current", "dev-current-project:" + Guid.NewGuid().ToString("D"),
            Hash(new { workspace.Configuration, workspace.Actor, descriptor.ExactProjectReferenceJson,
                descriptor.OriginalSelectedRegistrationJson, descriptor.OriginalContainer,
                descriptor.ExpectedWorkspaceDocumentSha256, descriptor.ExpectedRegisteredRootFingerprint }), ResourceAccess.Read);
    }
    private sealed class Descriptor(FileDeveloperWorkspaceStore store, DeveloperProjectReference reference,
        string exactReference, Conversation conversation, ContainerDefinition container, NativeFilesWorkspace workspace,
        FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot registration, string? expectedSha, string? expectedRoot)
        : IDeveloperOriginalCurrentProjectDescriptor
    {
        public IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore => store;
        public Guid WorkspaceId => reference.WorkspaceId;
        public Guid ProjectId => reference.ProjectId;
        public Guid RootId => reference.RootId;
        public long WorkspaceRevision => reference.WorkspaceRevision;
        public long ProjectRevision => reference.ProjectRevision;
        public string? RepositoryBindingId => reference.RepositoryBindingId;
        public string ExactProjectReferenceJson => exactReference;
        public Conversation OriginalConversation => conversation;
        public ContainerDefinition OriginalContainer => container;
        public AuthenticatedResourceActor OriginalActor => workspace.Actor;
        public string ConfiguredFilesRoot => workspace.Configuration.RootDirectory;
        public string RegisteredProjectRoot => registration.Binding.DirectoryPath;
        public string OriginalRegistrationStatePath => registration.StatePath;
        public string OriginalRegistrationStateJson => registration.StateJson;
        public string OriginalSelectedRegistrationJson { get; } = Serialize(registration.Binding);
        public string? ExpectedWorkspaceDocumentSha256 => expectedSha;
        public string? ExpectedRegisteredRootFingerprint => expectedRoot;
    }
    public void DemandExternalOriginalCurrentProjectJoin()
    {
        if (_physical?.ContainsKey(this) == true)
            throw new InvalidOperationException("An actual current-project Files callback cannot join its source owner.");
        for (var original = _executing.Value; original is not null; original = original.Parent)
            if (Volatile.Read(ref original.Live)) throw new InvalidOperationException("An actual current-project Files original cannot join its own drain.");
    }
    private void Scope(Action body)
    {
        var active = _physical ??= []; active.TryGetValue(this, out var depth); active[this] = depth + 1;
        try { body(); } finally { if (depth == 0) active.Remove(this); else active[this] = depth; }
    }
    private Task<T> Start<T>(Action<Action> parentScope, Action<Task> parentRetain, Func<Original, FilesOriginalReadSourceScope, Task<T>> body)
    {
        ArgumentNullException.ThrowIfNull(parentScope); ArgumentNullException.ThrowIfNull(parentRetain);
        TaskCompletionSource start; Task<T> driver; FilesOriginalParentOperation pair; Original original;
        lock (_gate)
        {
            if (_retiring || _originals.Count >= 256) throw new InvalidOperationException("Current-project Files custody is sealed or full.");
            original = new(_executing.Value);
            pair = new(Scope, actual => { lock (_gate) original.Raw.Add(actual); }, parentScope, parentRetain);
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            original.Driver = driver = Drive(start.Task, original, pair, body); _originals.Add(original);
        }
        pair.Publish(driver); start.TrySetResult(); return driver;
    }
    private async Task<T> Drive<T>(Task start, Original original, FilesOriginalParentOperation pair,
        Func<Original, FilesOriginalReadSourceScope, Task<T>> body)
    {
        await start.ConfigureAwait(false); var previous = _executing.Value; _executing.Value = original;
        Volatile.Write(ref original.Live, true); Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
        try
        {
            try { pair.DemandPublication(); value = await pair.Sources.Observe(() => actual = body(original, pair.Sources)).ConfigureAwait(false); }
            catch (Exception error) { AddTask(errors, actual, error); }
            Task[] raw; lock (_gate) raw = original.Raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var source in raw) try { await source.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, source, error); }
            if (actual?.IsCanceled == true && raw.All(source => !source.IsFaulted) && errors.All(error => error is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            Throw(errors); return value;
        }
        finally { Volatile.Write(ref original.Live, false); _executing.Value = previous; }
    }
    public Task<IDeveloperOriginalCurrentProjectSelection> SelectOriginalWithinSourceAsync(Conversation conversation,
        ContainerDefinition container, string exactReference, string? expectedSha, string? expectedRoot,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start(scope, retain, (original, sources) => SelectBody(conversation, container, exactReference, expectedSha, expectedRoot, original, sources, token));
    public Task<IDeveloperOriginalCurrentProjectSelection> SelectOriginalRestorationWithinSourceAsync(
        Conversation conversation, Guid actualContainerId, string expectedContainerSha, string exactReference,
        string expectedDocumentSha, string expectedRootFingerprint, Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start<IDeveloperOriginalCurrentProjectSelection>(scope, retain, async (original, sources) =>
    {
        sources.Invoke(() =>
        {
            if (actualContainerId == Guid.Empty || conversation.ContainerId != actualContainerId ||
                !ValidDigest(expectedContainerSha) || expectedContainerSha is null ||
                !ValidDigest(expectedDocumentSha) || expectedDocumentSha is null ||
                !ValidDigest(expectedRootFingerprint) || expectedRootFingerprint is null ||
                conversation.Mode is not (HavenMode.Tasks or HavenMode.Studio))
                throw new UnauthorizedAccessException("The authenticated detached project/container material is incomplete.");
            return true;
        });
        var actual = await sources.Observe(() => containers.GetByModeAsync(conversation.Mode, token)).ConfigureAwait(false);
        ContainerDefinition? container = null;
        sources.Invoke(() =>
        {
            var matches = actual.Where(value => value.Id == actualContainerId && !value.IsArchived).Take(2).ToArray();
            if (matches.Length != 1 || Hash(matches[0]) != expectedContainerSha)
                throw new UnauthorizedAccessException("The actual persisted project container differs from the authenticated detached observation.");
            container = matches[0]; return true;
        });
        return await SelectBody(conversation, container!, exactReference, expectedDocumentSha, expectedRootFingerprint, original, sources, token).ConfigureAwait(false);
    });
    private async Task<IDeveloperOriginalCurrentProjectSelection> SelectBody(Conversation conversation,
        ContainerDefinition container, string exactReference, string? expectedSha, string? expectedRoot,
        Original original, FilesOriginalReadSourceScope sources, CancellationToken token)
    {
        DeveloperProjectReference? reference = null;
        sources.Invoke(() =>
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(exactReference) || exactReference.Length > 16384)
                throw new UnauthorizedAccessException("The selected exact project reference exceeds its bounded original input.");
            reference = JsonSerializer.Deserialize<DeveloperProjectReference>(exactReference, Json);
            if (reference is null || reference.WorkspaceId == Guid.Empty || reference.ProjectId == Guid.Empty || reference.RootId == Guid.Empty ||
                reference.WorkspaceRevision < 1 || reference.ProjectRevision < 1 || exactReference.Length > 16384 ||
                conversation.Id == Guid.Empty || conversation.ContainerId != container.Id || container.Id == Guid.Empty || container.IsArchived ||
                conversation.Mode != container.Mode || container.Mode is not (HavenMode.Tasks or HavenMode.Studio) ||
                !ValidDigest(expectedSha) || !ValidDigest(expectedRoot))
                throw new UnauthorizedAccessException("The actual selected project/reference/Conversation/container tuple is incomplete.");
            return true;
        });
        var current = await sources.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(null, sources, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No genuine current personal Files configuration is available.");
        var row = await ObserveRegistration(current, reference!.ProjectId, sources, token).ConfigureAwait(false);
        await ValidateContainer(container, row.Binding.DirectoryPath, sources, token).ConfigureAwait(false);
        var descriptor = new Descriptor(originalStore, reference, exactReference, conversation, container, current, row, expectedSha, expectedRoot);
        var selection = new Selection(this, original, current, row, descriptor);
        await RevalidateBody(selection, current.Actor, sources, token, beforeIssue: true).ConfigureAwait(false);
        sources.Invoke(() =>
        {
            lock (_gate)
            {
                if (_retiring || _selections.Count >= 128) throw new InvalidOperationException("Current project selections are sealed or full.");
                _selections.Add(selection);
            }
            return true;
        });
        return selection;
    }
    private Task<FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot> ObserveRegistration(
        NativeFilesWorkspace current, Guid projectId, FilesOriginalReadSourceScope sources, CancellationToken token)
        => sources.Observe(() =>
        {
            if (current.Actor.AccountId is not null || current.Actor.OrganisationId is not null ||
                !Guid.TryParse(current.Actor.ProfileId, out var profile) || profile == Guid.Empty)
                throw new UnauthorizedAccessException("The actual Files actor is not a personal Home local profile.");
            return current.Directories.ObserveOriginalCurrentProjectRegistrationAsync(profile, "dev.project." + projectId.ToString("N"),
                current.Provider, current.Configuration.StoreId, current.Configuration.RootDirectory,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token);
        });
    private async Task ValidateContainer(ContainerDefinition expected, string actualRoot,
        FilesOriginalReadSourceScope sources, CancellationToken token)
    {
        var rows = await sources.Observe(() => containers.GetByModeAsync(expected.Mode, token)).ConfigureAwait(false);
        sources.Invoke(() =>
        {
            var found = rows.Where(value => value.Id == expected.Id).Take(2).ToArray();
            if (found.Length != 1 || found[0].IsArchived || Serialize(found[0]) != Serialize(expected) ||
                expected.RootPath != actualRoot)
                throw new UnauthorizedAccessException("The actual persisted Task/Studio container changed or refers to another root.");
            return true;
        });
    }
    private async Task RevalidateBody(Selection selection, AuthenticatedResourceActor actor,
        FilesOriginalReadSourceScope sources, CancellationToken token, bool beforeIssue = false)
    {
        sources.Invoke(() =>
        {
            if (selection.Workspace.Actor != actor || !ReferenceEquals(selection.Descriptor.OriginalStore, originalStore) ||
                selection.Descriptor.OriginalConversation.ContainerId != selection.Descriptor.OriginalContainer.Id)
                throw new UnauthorizedAccessException("The genuine selected current-project owner/store/container changed.");
            if (!beforeIssue) Require(selection); return true;
        });
        var current = await sources.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(
            selection.Workspace.Configuration.StoreId, sources, token)).ConfigureAwait(false);
        sources.Invoke(() =>
        {
            if (current is null || current.Actor != actor || !ReferenceEquals(current.Provider, selection.Workspace.Provider) ||
                !ReferenceEquals(current.Directories, selection.Workspace.Directories) ||
                !ReferenceEquals(current.Materializations, selection.Workspace.Materializations) ||
                Serialize(current.Configuration) != selection.Configuration)
                throw new UnauthorizedAccessException("The current personal Files actor/provider/configuration changed.");
            return true;
        });
        var row = await ObserveRegistration(current!, selection.Descriptor.ProjectId, sources, token).ConfigureAwait(false);
        sources.Invoke(() =>
        {
            if (row.Binding != selection.Registration.Binding ||
                row.StatePath != selection.Registration.StatePath || row.StateJson != selection.Registration.StateJson)
                throw new UnauthorizedAccessException("The actual current project registration changed.");
            return true;
        });
        await ValidateContainer(selection.Descriptor.OriginalContainer, row.Binding.DirectoryPath, sources, token).ConfigureAwait(false);
        var after = await sources.Observe(() => workspaces.GetOriginalCurrentWithinSourceAsync(
            selection.Workspace.Configuration.StoreId, sources, token)).ConfigureAwait(false);
        sources.Invoke(() =>
        {
            if (after is null || after.Actor != actor || !ReferenceEquals(after.Provider, selection.Workspace.Provider) ||
                Serialize(after.Configuration) != selection.Configuration)
                throw new UnauthorizedAccessException("The current Files namespace changed after registered-project observation.");
            token.ThrowIfCancellationRequested(); if (!beforeIssue) Require(selection); return true;
        });
    }
    private Selection Require(IDeveloperProjectOriginalReadSelection value)
    {
        lock (_gate)
            if (!_retiring && value is Selection selection && ReferenceEquals(selection.Owner, this) && _selections.Contains(selection) && selection.Original.Driver.IsCompletedSuccessfully) return selection;
        throw new UnauthorizedAccessException("No live privately issued current project selection exists.");
    }
    public bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection value)
    { lock (_gate) return !_retiring && value is Selection selection && ReferenceEquals(selection.Owner, this) && _selections.Contains(selection) && selection.Original.Driver.IsCompletedSuccessfully; }
    public bool IsIssuedOriginalDescriptor(IDeveloperOriginalCurrentProjectSelection value, IDeveloperOriginalCurrentProjectDescriptor descriptor)
    { lock (_gate) return IsIssuedOriginal(value) && value is Selection selection && ReferenceEquals(selection.Descriptor, descriptor); }
    public IDeveloperOriginalCurrentProjectDescriptor GetOriginalDescriptor(IDeveloperOriginalCurrentProjectSelection value) => Require(value).Descriptor;
    public IReadOnlyList<ResourceScope> GetOriginalReadScopes(IDeveloperProjectOriginalReadSelection value) => [Require(value).Scope];
    public Task RevalidateOriginalWithinSourceAsync(IDeveloperOriginalCurrentProjectSelection value, AuthenticatedResourceActor actor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start(scope, retain, async (_, sources) => { await RevalidateBody(Require(value), actor, sources, token).ConfigureAwait(false); return true; });
    public Task RevalidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, AuthenticatedResourceActor actor, CancellationToken token)
        => Start(Scope, _ => { }, async (_, sources) => { await RevalidateBody(Require(value), actor, sources, token).ConfigureAwait(false); return true; });
    public void RequestOriginalCurrentProjectRetirement() { lock (_gate) _retiring = true; }
    public Task CloseAndDrainOriginalCurrentProjectsAsync()
    {
        DemandExternalOriginalCurrentProjectJoin(); TaskCompletionSource start; Task close; Original[] originals;
        lock (_gate)
        {
            _retiring = true; if (_close is not null) return _close;
            originals = _originals.ToArray(); start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = close = Drain(start.Task, originals);
        }
        start.TrySetResult(); return close;
    }
    private async Task Drain(Task start, Original[] originals)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        foreach (var original in originals)
        {
            try { await original.Driver.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, original.Driver, error); }
            Task[] raw; lock (_gate) raw = original.Raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var source in raw) try { await source.ConfigureAwait(false); } catch (Exception error) { AddTask(errors, source, error); }
        }
        Throw(errors);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainOriginalCurrentProjectsAsync());
    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json))).ToLowerInvariant();
    private static bool ValidDigest(string? value) => value is null || value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void Add(List<Exception> errors, Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group) foreach (var cause in group.InnerExceptions) Add(errors, cause);
        else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
    }
    private static void AddTask(List<Exception> errors, Task? actual, Exception observed)
    { Add(errors, observed); if (actual?.IsFaulted == true) foreach (var cause in actual.Exception!.InnerExceptions) Add(errors, cause); }
    private static void Throw(List<Exception> errors) { if (errors.Count != 0) throw new AggregateException("Current Files project source originals did not settle.", errors); }
}
