// UNAPPLIED Core-assembly proposal. Requires Framework's shared membership-source
// extraction described in MEMBERSHIP-SOURCE-CONTRACT.md; no currently composed port
// or successful compilation/runtime is asserted by this file.
using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Canonical;

/// <summary>Source-issued Dev observations over the actual canonical owners and
/// fresh Home project READ. It creates no project, container, conversation or Task,
/// and never retires borrowed business services.</summary>
public sealed partial class DenAssistantOriginalDevelopmentOwner : IAssistantOriginalDevelopmentOwner,
    ICanonicalProjectTaskContextResumeSelectionSource, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly object _issuer = new();
    private readonly HomePersonalDenFactory _home;
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly TaskExecutionCoordinator _tasks;
    private readonly DeveloperTaskWorkspaceService _developer;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly HomeColdProjectReadReconciliation _projectReads;
    private readonly ITaskRunColdRecoveryJournal _journal;
    private readonly ITaskRunColdContextAuthority _coldContext;
    private readonly ICanonicalProjectContextStoreReadSource? _projectContexts;
    private readonly ICanonicalProjectTaskContextCreateSource? _compatibleTaskContexts;
    private readonly AssistantPresentationOriginals _originals = new();
    private readonly ConditionalWeakTable<AssistantCanonicalMembershipSource, object> _memberships = new();
    private readonly ConditionalWeakTable<AssistantDevelopmentBinding, BindingOriginal> _bindings = new();
    private readonly HashSet<Custody> _custodies = [];
    [ThreadStatic] private static List<AssistantPresentationOriginals>? _physicalSources;
    private const int MaximumCustodies = 32;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public DenAssistantOriginalDevelopmentOwner(HomePersonalDenFactory sameHome,
        IConversationRepository sameConversations, IContainerRepository sameContainers,
        TaskExecutionCoordinator sameTasks, DeveloperTaskWorkspaceService sameDeveloper,
        IAuthenticatedResourceActorSource sameTaskActors, HomeColdProjectReadReconciliation sameProjectReads,
        ITaskRunColdRecoveryJournal sameJournal, ITaskRunColdContextAuthority sameColdContext,
        ICanonicalProjectContextStoreReadSource? sameProjectContexts = null,
        ICanonicalProjectTaskContextCreateSource? sameCompatibleTaskContexts = null)
    {
        if (!sameDeveloper.IsBoundToOriginalCanonicalOwner(sameTasks) ||
            !sameProjectReads.HasOriginalColdProjectComposition(sameJournal, sameTaskActors) ||
            !sameTasks.HasOriginalColdRecoveryComposition(sameJournal, sameColdContext))
            throw new InvalidOperationException("The SAME canonical Dev/Task and actual Home project/actor sources are required.");
        _home = sameHome; _conversations = sameConversations; _containers = sameContainers;
        _tasks = sameTasks; _developer = sameDeveloper; _actors = sameTaskActors; _projectReads = sameProjectReads;
        _journal = sameJournal; _coldContext = sameColdContext; _projectContexts = sameProjectContexts; _compatibleTaskContexts = sameCompatibleTaskContexts;
    }

    // Trusted Core construction only. The bridge creates this sealed source over its
    // private issuer and registers it before publishing a binding. No public ID or
    // interface implementation can register a different membership issuer here.
    internal void BindOriginalMembershipSource(AssistantCanonicalMembershipSource source)
    {
        if (!ReferenceEquals(source.OriginalHomeDenFactory, _home) ||
            !ReferenceEquals(source.OriginalConversations, _conversations))
            throw new InvalidOperationException("The actual membership source belongs to another Home/conversation composition.");
        lock (_gate)
        {
            _ = _memberships.GetValue(source, static _ => new object());
            // Metadata-only issuer association adds no strong lifetime. Active
            // bindings/custodies retain the SAME source; its bridge and real
            // borrowers own their actual pending/failed originals independently.
        }
    }

    public HomePersonalDenFactory OriginalHomeDenFactory => _home;
    public IConversationRepository OriginalConversationOwner => _conversations;
    public IContainerRepository OriginalContainerOwner => _containers;
    public TaskExecutionCoordinator OriginalTaskOwner => _tasks;
    public DeveloperTaskWorkspaceService OriginalDeveloperOwner => _developer;
    public IAuthenticatedResourceActorSource OriginalTaskActorOwner => _actors;
    public HomeColdProjectReadReconciliation OriginalProjectReadOwner => _projectReads;
    public ITaskRunColdRecoveryJournal OriginalColdJournal => _journal;
    public ITaskRunColdContextAuthority OriginalColdContext => _coldContext;
    public ICanonicalProjectContextStoreReadSource? OriginalProjectContextStoreReadOwner => _projectContexts;
    public ICanonicalProjectTaskContextCreateSource? OriginalCompatibleTaskContextCreateOwner => _compatibleTaskContexts;

    private sealed record BindingOriginal(AssistantCanonicalMembershipSource Membership,
        AssistantConversationBinding Conversation, DeveloperProjectReference Reference,
        ProviderExecutionContext InitialTask, AuthenticatedResourceActor HomeActor, AuthenticatedResourceActor TaskActor);

    private sealed class Custody(DenAssistantOriginalDevelopmentOwner owner, AssistantDevelopmentBinding binding)
        : IAssistantOriginalDevelopmentCustody
    {
        private readonly object _gate = new();
        private bool _retiring;
        private Task? _close;
        internal DenAssistantOriginalDevelopmentOwner Owner => owner;
        internal AssistantDevelopmentBinding Binding => binding;
        internal AssistantPresentationOriginals Work { get; } = new();
        internal SemaphoreSlim ValidationGate { get; } = new(1, 1);
        internal CustodyRead? ProjectRead;
        internal Task? OriginalClose { get { lock (_gate) return _close; } }
        internal bool AllowsAdmission { get { lock (_gate) return !_retiring; } }
        public void RequestRetirement() { lock (_gate) _retiring = true; Work.RequestRetirement(); }
        public void DemandExternalOriginalRetirementJoin()
        { DemandPhysicalExternalJoin(Work); Work.DemandExternalJoin(); owner._projectReads.DemandExternalOriginalJoin(); }
        public Task CloseAndDrainAsync()
        { DemandExternalOriginalRetirementJoin(); lock (_gate) { _retiring = true; return _close ??= Work.CloseAndDrainAsync(); } }
    }

    private AssistantCanonicalMembershipSource RequireMembership(AssistantConversationBinding binding)
    {
        var source = AssistantCanonicalMembershipSource.ObserveOriginalIssuer(binding);
        lock (_gate)
            return source is not null && _memberships.TryGetValue(source, out _) && source.IsIssuedOriginalBinding(binding)
                ? source : throw new AssistantCommandRefusedException("No actual registered canonical membership source issued this binding.");
    }

    private BindingOriginal RequireBinding(AssistantDevelopmentBinding binding)
    {
        lock (_gate)
            return binding is not null && ReferenceEquals(binding.Issuer, _issuer)
                && _bindings.TryGetValue(binding, out var original) && ReferenceEquals(binding.Original, original)
                ? original : throw new AssistantCommandRefusedException("The actual Core Dev source did not issue this binding.");
    }

    public bool IsIssuedOriginalBinding(AssistantDevelopmentBinding binding)
    { lock (_gate) return binding is not null && ReferenceEquals(binding.Issuer, _issuer)
        && _bindings.TryGetValue(binding, out var original) && ReferenceEquals(binding.Original, original); }

    public bool IsIssuedOriginalCustody(AssistantDevelopmentBinding binding, IAssistantOriginalDevelopmentCustody custody)
    { lock (_gate) return IsIssuedOriginalBinding(binding) && custody is Custody actual
        && ReferenceEquals(actual.Owner, this) && ReferenceEquals(actual.Binding, binding)
        && _custodies.Contains(actual) && actual.AllowsAdmission; }

    public Task<AssistantDevelopmentBinding> OpenOriginalAsync(AssistantConversationBinding conversation,
        DeveloperProjectReference reference, ProviderExecutionContext expectedTask, CancellationToken token) =>
        _originals.Admit(async () =>
        {
            var membership = RequireMembership(conversation);
            var sources = new Sources(_originals, static body => body(), static _ => { });
            var home = await sources.Take(() => membership.ValidateOriginalWithinSourceAsync(conversation,
                sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            var taskActor = await ReadOriginalTaskActorWithinSourceAsync(sources, token).ConfigureAwait(false);
            var provisional = new BindingOriginal(membership, conversation, reference, expectedTask, home.Actor, taskActor);
            var observed = await ObserveCurrentAsync(provisional, sources, expectedTask, token).ConfigureAwait(false);
            var binding = new AssistantDevelopmentBinding(_issuer, conversation, observed.Project, expectedTask, provisional);
            lock (_gate)
            {
                _bindings.Add(binding, provisional); // Metadata-only weak key; actual custody below retains live/failed borrowers.
            }
            return binding;
        });

    public Task<IAssistantOriginalDevelopmentCustody> TransferOriginalWithinSourceAsync(AssistantDevelopmentBinding binding,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        _originals.Admit<IAssistantOriginalDevelopmentCustody>(async () =>
        {
            var original = RequireBinding(binding);
            _originals.DemandObservationCapacity();
            var custody = new Custody(this, binding);
            lock (_gate)
            {
                _custodies.RemoveWhere(actual => actual.OriginalClose?.IsCompletedSuccessfully == true);
                if (_custodies.Count >= MaximumCustodies)
                    throw new AssistantCommandRefusedException("Close existing transferred Dev views before another acquisition.");
                _custodies.Add(custody);
            }
            _originals.Observe(custody.CloseAndDrainAsync, () => custody.OriginalClose);
            // This actual owner is retained before caller scope/retainer callbacks.
            var transfer = custody.Work.Admit(async () =>
            {
                var sources = new Sources(custody.Work, originalSynchronousScope, retainOriginalTask);
                await sources.Take(() => custody.ValidationGate.WaitAsync(token)).ConfigureAwait(false);
                try { _ = await ObserveCurrentAsync(original, sources, original.InitialTask, token, custody).ConfigureAwait(false); }
                finally { custody.ValidationGate.Release(); }
                return (IAssistantOriginalDevelopmentCustody)custody;
            });
            _originals.Retain(transfer);
            Physical(_originals, () => { retainOriginalTask(transfer); return true; });
            return await transfer.ConfigureAwait(false);
        });

    public Task<AssistantDevelopmentCurrentObservation> ValidateOriginalBindingWithinSourceAsync(AssistantDevelopmentBinding binding,
        IAssistantOriginalDevelopmentCustody custody, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        var original = RequireBinding(binding);
        if (!IsIssuedOriginalCustody(binding, custody) || custody is not Custody actual)
            throw new AssistantCommandRefusedException("The SAME privately transferred Dev borrower is required.");
        return actual.Work.Admit(async () =>
        {
            var sources = new Sources(actual.Work, originalSynchronousScope, retainOriginalTask);
            await sources.Take(() => actual.ValidationGate.WaitAsync(token)).ConfigureAwait(false);
            try { return await ObserveCurrentAsync(original, sources, null, token, actual).ConfigureAwait(false); }
            finally { actual.ValidationGate.Release(); }
        });
    }

    public Task<TaskExecutionSnapshot> ValidateOriginalBindingWithinSourceAsync(AssistantDevelopmentBinding binding,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        _originals.Admit(async () => (await ObserveCurrentAsync(RequireBinding(binding),
            new Sources(_originals, originalSynchronousScope, retainOriginalTask), null, token).ConfigureAwait(false)).CanonicalTask);

    public Task<DeveloperOperationResult<DeveloperActionObservation>> ReadOriginalAsync(AssistantDevelopmentBinding binding,
        DeveloperCanonicalActionContext action, DeveloperCodeDocument document, CancellationToken token) =>
        DispatchAsync(binding, action, () => _developer.ReadFileAsync(binding.Project.Reference, action, document, token), token);
    public Task<DeveloperOperationResult<DeveloperActionObservation>> ApplyOriginalAsync(AssistantDevelopmentBinding binding,
        DeveloperCanonicalActionContext action, DeveloperReviewedTextEdit edit, CancellationToken token) =>
        DispatchAsync(binding, action, () => _developer.ApplyEditAsync(binding.Project.Reference, action, edit, token), token);
    public Task<DeveloperOperationResult<DeveloperActionObservation>> RunTestsOriginalAsync(AssistantDevelopmentBinding binding,
        DeveloperCanonicalActionContext action, string command, int timeoutSeconds, CancellationToken token) =>
        DispatchAsync(binding, action, () => _developer.RunTestsAsync(binding.Project.Reference, action, command, timeoutSeconds, token), token);

    private Task<DeveloperOperationResult<DeveloperActionObservation>> DispatchAsync(AssistantDevelopmentBinding binding,
        DeveloperCanonicalActionContext action, Func<Task<DeveloperOperationResult<DeveloperActionObservation>>> actualOperation,
        CancellationToken token) => _originals.Admit(async () =>
        {
            var sources = new Sources(_originals, static body => body(), static _ => { });
            var current = await ObserveCurrentAsync(RequireBinding(binding), sources, null, token).ConfigureAwait(false);
            if (action.TaskId != current.CanonicalTask.TaskId || action.ExecutionId != current.CanonicalTask.ExecutionId ||
                action.ContextId != current.CanonicalTask.ContextId || action.PersistenceRevision != current.CanonicalTask.PersistenceRevision ||
                action.AttemptId != current.CanonicalTask.Attempts.LastOrDefault()?.Id)
                throw new AssistantCommandRefusedException("Refresh the SAME actual Task/run/issued attempt before this existing Dev action.");
            // The maintained Dev owner still issues its action admission, READ/effect
            // permission, native fence, checkpoint and owner outcome. This adapter
            // never invokes the runtime directly or turns readiness into consent.
            return await sources.Take(actualOperation).ConfigureAwait(false);
        });

    private async Task<AssistantDevelopmentCurrentObservation> ObserveCurrentAsync(BindingOriginal original,
        Sources sources, ProviderExecutionContext? exactInitial, CancellationToken token, Custody? transferred = null)
    {
        var before = await sources.Take(() => original.Membership.ValidateOriginalWithinSourceAsync(original.Conversation,
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        if (!before.Definition.Configuration.Enabled || before.Definition.Configuration.Archived)
            throw new AssistantCommandRefusedException("The actual configured identity is inactive.");
        var actor = await ReadOriginalTaskActorWithinSourceAsync(sources, token).ConfigureAwait(false);
        DemandActor(original, actor, before.Actor);
        var task = await ReadSameTaskAsync(original, actor, sources, token).ConfigureAwait(false);
        if (exactInitial is not null && (task.PersistenceRevision != exactInitial.PersistenceRevision ||
            task.Attempts.LastOrDefault()?.Id != exactInitial.AttemptId))
            throw new AssistantCommandRefusedException("The initial Task observation changed before Dev acquisition.");
        var conversation = before.Conversation;
        if (conversation.IsArchived || conversation.Mode is not (HavenMode.Tasks or HavenMode.Studio) ||
            conversation.ContainerId is not { } containerId)
            throw new AssistantCommandRefusedException("This canonical conversation has no actual authorised project container.");
        var metadata = await ReadOriginalProjectContextWithinSourceAsync(conversation.Id, before.Actor, sources, token).ConfigureAwait(false);
        var container = metadata.Container;
        if (metadata.Context != conversation || container.Id != containerId || container.Mode != conversation.Mode)
            throw new AssistantCommandRefusedException("The SAME current store READ project context/container differs from actual membership.");
        async Task Revalidate()
        {
            var after = await sources.Take(() => original.Membership.ValidateOriginalWithinSourceAsync(original.Conversation,
                sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            DemandActor(original, actor, after.Actor);
            var current = await ReadOriginalProjectContextWithinSourceAsync(conversation.Id, before.Actor, sources, token).ConfigureAwait(false);
            if (current.Context != conversation || current.Container != container || after.Conversation != conversation ||
                await ReadOriginalTaskActorWithinSourceAsync(sources, token).ConfigureAwait(false) != actor)
                throw new AssistantCommandRefusedException("The canonical conversation or current actor changed during project READ.");
            var finalTask = await ReadSameTaskAsync(original, actor, sources, token).ConfigureAwait(false);
            if (finalTask.PersistenceRevision != task.PersistenceRevision || finalTask.OwnerBinding != task.OwnerBinding)
                throw new AssistantCommandRefusedException("The SAME canonical Task changed during project READ; refresh it.");
        }
        DeveloperResolvedProject project;
        if (transferred is null)
            project = await ReadProjectWithinSourceAsync(conversation, container, original.Reference, before.Actor, sources, token, Revalidate).ConfigureAwait(false);
        else
        {
            if (transferred.ProjectRead is null)
            {
                transferred.Work.DemandObservationCapacity();
                transferred.ProjectRead = new CustodyRead(_projectReads, transferred.Work);
                var retained = transferred.ProjectRead;
                transferred.Work.Observe(() => Physical(transferred.Work, retained.CloseAndDrainAsync));
            }
            project = await transferred.ProjectRead.ValidateAsync(conversation, container, original.Reference, before.Actor,
                sources, token, Revalidate).ConfigureAwait(false);
        }
        return new(project, task, actor) { HomeActor = before.Actor };
    }

    private async Task<DeveloperResolvedProject> ReadProjectWithinSourceAsync(Conversation conversation,
        ContainerDefinition container, DeveloperProjectReference reference, AuthenticatedResourceActor actor,
        Sources sources, CancellationToken token, Func<Task> revalidate)
    {
        var borrowers = new ReadBorrowers(_projectReads, sources.Work);
        Exception? primary = null;
        List<Exception> cleanup = [];
        DeveloperResolvedProject? project = null;
        try
        {
            Task<IDeveloperOriginalProjectCommandRead>? preparation = null;
            var read = await sources.Capture(() => preparation = _projectReads.AcquireOriginalCommandReadWithinSourceAsync(
                conversation, container, JsonSerializer.Serialize(reference, Json), sources.Scope, sources.Retain, token),
                borrowers.RetainRead).ConfigureAwait(false);
            // Retain actual cleanup custody before caller currentness/permission guards.
            if (!_projectReads.IsOwnedOriginalCommandRead(read) || !_projectReads.IsIssuedOriginalCommandRead(read) ||
                !ReferenceEquals(read.OriginalPreparation, preparation))
                throw new UnauthorizedAccessException("The actual Home source did not issue this project READ.");
            var native = await sources.Capture(() => _projectReads.CaptureOriginalCommandNativeReadWithinSourceAsync(
                read, sources.Scope, sources.Retain, token), borrowers.RetainNative).ConfigureAwait(false);
            if (!_projectReads.IsOwnedOriginalCommandNativeRead(read, native) ||
                !_projectReads.IsIssuedOriginalCommandNativeRead(read, native))
                throw new UnauthorizedAccessException("The same READ source did not issue its actual native document capture.");
            await sources.Take(() => _projectReads.ValidateOriginalCommandNativeReadWithinSourceAsync(
                read, native, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            project = ObserveAuthorizedMetadata(reference, read, native, actor);
            await revalidate().ConfigureAwait(false);
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            try { await borrowers.JoinAsync().ConfigureAwait(false); }
            catch (Exception failure) { cleanup.Add(failure); }
        }
        if (primary is not null) cleanup.Insert(0, primary);
        var causes = cleanup.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (causes.Length == 1) ExceptionDispatchInfo.Capture(causes[0]).Throw();
        if (causes.Length > 1) throw new AggregateException("Actual project observation and independent borrower joins failed.", causes);
        return project ?? throw new InvalidOperationException("No original project was observed.");
    }

    private async Task<TaskExecutionSnapshot> ReadSameTaskAsync(BindingOriginal original, AuthenticatedResourceActor actor,
        Sources sources, CancellationToken token)
    {
        var task = await sources.Take(() => _tasks.GetAsync(original.InitialTask.TaskId, token)).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The SAME existing canonical Task is unavailable.");
        if (task.TaskId != original.InitialTask.TaskId || task.ExecutionId != original.InitialTask.ExecutionId ||
            task.ContextId != original.Conversation.Conversation.Id || task.ContextId != original.InitialTask.ContextId ||
            task.OwnerBinding is not { } owner || owner.TaskId != task.TaskId || owner.ContextId != task.ContextId ||
            owner.ExecutionId != task.ExecutionId || owner.ActorId != actor.ActorId || owner.ProfileId != actor.ProfileId ||
            owner.AccountId != actor.AccountId || owner.OrganisationId != actor.OrganisationId ||
            owner.AuthenticationRevision != actor.AuthenticationRevision)
            throw new AssistantCommandRefusedException("The actual Task/run/context belongs to another current authenticated owner.");
        return task;
    }

    private static void DemandActor(BindingOriginal original, AuthenticatedResourceActor actual,
        AuthenticatedResourceActor actualHome)
    {
        if (actual != original.TaskActor || actualHome != original.HomeActor)
            throw new AssistantCommandRefusedException("The original Home access or Task owner changed; reopen its actual binding.");
    }

    private static DeveloperResolvedProject ObserveAuthorizedMetadata(DeveloperProjectReference reference,
        IDeveloperOriginalProjectCommandRead read, IDeveloperOriginalProjectCommandNativeRead native,
        AuthenticatedResourceActor actor)
    {
        var identity = read.OriginalIdentity;
        var descriptor = read.OriginalDescriptor;
        var document = native.OriginalRead.OriginalWorkspaceDocument;
        if (identity.WorkspaceId != reference.WorkspaceId || identity.ProjectId != reference.ProjectId || identity.RootId != reference.RootId ||
            identity.WorkspaceRevision != reference.WorkspaceRevision || identity.ProjectRevision != reference.ProjectRevision ||
            identity.RepositoryBindingId != reference.RepositoryBindingId || identity.OriginalHomeResourceActor != actor ||
            descriptor.OriginalActor != actor || descriptor.OriginalContainer.Id != identity.ContainerId ||
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(document))) != identity.SavedWorkspaceDocumentSha256 ||
            native.OriginalRead.OriginalWorkspaceDocumentSha256 != identity.SavedWorkspaceDocumentSha256)
            throw new UnauthorizedAccessException("The actual authorized native document differs from the saved project/container/actor observation.");
        using var parsed = JsonDocument.Parse(document);
        var decoded = FileDeveloperWorkspaceStore.DecodeOriginalDocument(parsed.RootElement, reference.WorkspaceId);
        if (decoded.Value is not { } workspace || workspace.Revision != reference.WorkspaceRevision)
            throw new AssistantCommandRefusedException("The actual READ document does not contain the selected current workspace: " + decoded.Error?.Message);
        var project = workspace.Projects.SingleOrDefault(value => value.ProjectId == reference.ProjectId && value.Revision == reference.ProjectRevision);
        var root = workspace.Roots.SingleOrDefault(value => value.RootId == reference.RootId);
        if (project is null || root is null || !project.RootIds.Contains(root.RootId) ||
            !string.Equals(Path.GetFullPath(root.Location).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(identity.CanonicalRoot).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new AssistantCommandRefusedException("The observed registered root does not belong to this exact saved project.");
        var repository = reference.RepositoryBindingId is null ? null : workspace.SourceControlBindings.SingleOrDefault(
            value => value.BindingId == reference.RepositoryBindingId && value.RootId == root.RootId && !string.IsNullOrWhiteSpace(value.CanonicalRepositoryId));
        if (reference.RepositoryBindingId is not null && repository is null)
            throw new AssistantCommandRefusedException("The SAME saved canonical repository binding is unavailable.");
        return new(reference, workspace, project, root, repository); // Metadata; no effect/readiness grant.
    }

    private sealed class Sources(AssistantPresentationOriginals work, Action<Action> parentScope, Action<Task> parentRetain)
    {
        private readonly object _retainedGate = new();
        private readonly List<Task> _retainedOriginals = [];
        internal AssistantPresentationOriginals Work => work;
        internal void Retain(Task actual)
        {
            lock (_retainedGate)
                if (!_retainedOriginals.Any(value => ReferenceEquals(value, actual))) _retainedOriginals.Add(actual);
            work.Retain(actual); Physical(work, () => { parentRetain(actual); return true; });
        }
        internal async Task JoinAndAcknowledgeRetainedSourceCohortAsync(Func<Task, bool> sameConfiguredIssuerProof)
        {
            var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            var failures = new List<Exception>();
            while (true)
            {
                Task[] sources;
                lock (_retainedGate) sources = _retainedOriginals.Where(value => !joined.Contains(value)).ToArray();
                if (sources.Length == 0) break;
                foreach (var actual in sources)
                {
                    joined.Add(actual);
                    try { await actual.ConfigureAwait(false); }
                    catch (Exception cause)
                    {
                        // Only the configured owner may certify this exact retained raw
                        // occurrence. Sharing a cause object with another task grants nothing.
                        if (AssistantOriginalExternalRefusalReceipts.IsAcknowledgedOriginal(actual) ||
                            work.AcknowledgeOriginalExternalPreEffectRefusal(actual, sameConfiguredIssuerProof)) continue;
                        if (actual.IsFaulted && actual.Exception is { } payload) failures.AddRange(payload.InnerExceptions);
                        else failures.Add(cause);
                    }
                }
            }
            var causes = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (causes.Length == 1 && causes[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(causes[0]).Throw();
            if (causes.Length != 0) throw new AggregateException("An actual retained source refused or failed without issuer-owned settlement.", causes);
        }
        internal void Scope(Action body)
        {
            var phase = 1; var used = 0; Exception? protocolFailure = null; Exception? bodyFailure = null; var thread = Environment.CurrentManagedThreadId;
            try
            {
                work.Invoke(() =>
                {
                    Physical(work, () =>
                    {
                        parentScope(() =>
                        {
                            if (Volatile.Read(ref phase) == 0 || thread != Environment.CurrentManagedThreadId ||
                                Interlocked.CompareExchange(ref used, 1, 0) != 0)
                                {
                                var refusal = new InvalidOperationException("The actual Dev source callback expired, repeated or moved threads.");
                                Interlocked.CompareExchange(ref protocolFailure, refusal, null);
                                throw refusal;
                            }
                            try { body(); }
                            catch (Exception failure) { bodyFailure = failure; throw; }
                        });
                        return true;
                    });
                    if (bodyFailure is not null) ExceptionDispatchInfo.Capture(bodyFailure).Throw();
                    if (protocolFailure is not null) ExceptionDispatchInfo.Capture(protocolFailure).Throw();
                    if (Volatile.Read(ref used) != 1) throw new InvalidOperationException("The actual source callback was not executed.");
                    return true;
                });
            }
            catch (Exception scopeFailure)
            {
                // Parent scopes may catch the productive callback and throw a new
                // post-scope cause. Preserve the actual callback/protocol cause too;
                // foreign aggregates remain opaque and are never flattened here.
                var failures = new[] { bodyFailure, protocolFailure, scopeFailure }.OfType<Exception>()
                    .Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
                if (failures.Length == 1 && failures[0] is not OperationCanceledException)
                    ExceptionDispatchInfo.Capture(failures[0]).Throw();
                throw new AggregateException("The actual synchronous Dev source callback and scope failed.", failures);
            }
            finally { Volatile.Write(ref phase, 0); }
        }
        internal async Task<T> TakeOwnerAcknowledgedRefusal<T>(Func<Task<T>> factory,
            Func<Task, bool> sameConfiguredIssuerProof, bool normalizePreEffect)
        {
            var actual = Take(factory); // SAME raw Task retained before postguards and before awaiting.
            try { return await actual.ConfigureAwait(false); }
            catch (Exception cause)
            {
                if (work.AcknowledgeOriginalExternalPreEffectRefusal(actual, sameConfiguredIssuerProof) && normalizePreEffect)
                    throw new AssistantCommandRefusedException("The actual canonical owner declined before this operation entered effects: " + cause.Message);
                // A commit's raw no-effect receipt settles only that child. Its parent
                // may already hold durable Den Pending state and cannot claim no effects.
                throw;
            }
        }

        internal Task<T> Take<T>(Func<Task<T>> factory)
        {
            Task<T>? actual = null; var retained = false;
            return work.Source(() =>
            {
                Exception? primary = null;
                try { Scope(() => { actual = factory() ?? throw new InvalidOperationException("No original source Task was returned."); retained = true; Retain(actual); }); }
                catch (Exception failure) { primary = failure; }
                if (actual is not null && !retained)
                    try { Retain(actual); }
                    catch (Exception failure) { primary = primary is null ? failure : new AggregateException("Source callback and raw retainer both failed.", primary, failure); }
                if (primary is OperationCanceledException canceled)
                    throw new AggregateException("The actual synchronous source callback returned no canceled Task.", canceled);
                if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
                return actual ?? throw new InvalidOperationException("The actual source factory was not executed.");
            });
        }
        internal Task Take(Func<Task> factory)
        {
            Task? actual = null; var retained = false;
            return work.Source(() =>
            {
                Exception? primary = null;
                try { Scope(() => { actual = factory() ?? throw new InvalidOperationException("No original source Task was returned."); retained = true; Retain(actual); }); }
                catch (Exception failure) { primary = failure; }
                if (actual is not null && !retained)
                    try { Retain(actual); }
                    catch (Exception failure) { primary = primary is null ? failure : new AggregateException("Source callback and raw retainer both failed.", primary, failure); }
                if (primary is OperationCanceledException canceled)
                    throw new AggregateException("The actual synchronous source callback returned no canceled Task.", canceled);
                if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
                return actual ?? throw new InvalidOperationException("The actual source factory was not executed.");
            });
        }
        internal async Task<T> Capture<T>(Func<Task<T>> factory, Action<T> retainActualProduct)
        {
            Task<T>? actual = null, capture = null;
            Exception? callbackFailure = null;
            try
            {
                _ = Take(() =>
                {
                    actual = factory();
                    capture = CaptureProductAsync(actual, retainActualProduct);
                    work.Retain(capture); // Retain late products even if parent Scope/Retain then refuses.
                    return actual;
                });
            }
            catch (Exception failure) { callbackFailure = failure; }
            T product = default!;
            Exception? productFailure = null;
            if (capture is not null)
                try { product = await capture.ConfigureAwait(false); }
                catch (Exception failure) { productFailure = failure; }
            if (callbackFailure is not null && productFailure is not null)
                throw new AggregateException("Actual source capture and callback both failed.", callbackFailure, productFailure);
            if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
            if (productFailure is not null) ExceptionDispatchInfo.Capture(productFailure).Throw();
            return capture is null ? throw new InvalidOperationException("The original product was not acquired.") : product;
        }
        private static async Task<T> CaptureProductAsync<T>(Task<T> actual, Action<T> retain)
        {
            var product = await actual.ConfigureAwait(false);
            retain(product); // Cleanup custody precedes all productive currentness checks.
            return product;
        }
    }

    // One real manual Home READ belongs to the transferred workbench. Its genuine
    // current permission/Files/native descriptor is validated for each observation;
    // origin presentation retirement neither closes nor replaces it.
    private sealed class CustodyRead(HomeColdProjectReadReconciliation source, AssistantPresentationOriginals work)
    {
        private IDeveloperOriginalProjectCommandRead? _read;
        private Task<IDeveloperOriginalProjectCommandRead>? _preparation;
        private Task? _readClose;
        private readonly List<NativeOriginal> _natives = [];
        private sealed class NativeOriginal(IDeveloperOriginalProjectCommandNativeRead product)
        {
            private Task? _close;
            internal IDeveloperOriginalProjectCommandNativeRead Product => product;
            internal Task? OriginalClose => _close;
            internal Task Close() => _close ??= product.OriginalRead.DisposeAsync().AsTask();
        }
        internal async Task<DeveloperResolvedProject> ValidateAsync(Conversation conversation, ContainerDefinition container,
            DeveloperProjectReference reference, AuthenticatedResourceActor actor, Sources sources, CancellationToken token,
            Func<Task> revalidate)
        {
            if (_read is null)
                _ = await sources.Capture(() => _preparation = source.AcquireOriginalCommandReadWithinSourceAsync(conversation,
                    container, JsonSerializer.Serialize(reference, Json), sources.Scope, sources.Retain, token), actual =>
                { _read = actual; if (!source.IsOwnedOriginalCommandRead(actual)) throw new UnauthorizedAccessException("The transferred READ lacks actual historical issuer custody."); }).ConfigureAwait(false);
            var read = _read ?? throw new InvalidOperationException("No genuine transferred project READ was retained.");
            if (!source.IsOwnedOriginalCommandRead(read) || !source.IsIssuedOriginalCommandRead(read) ||
                !ReferenceEquals(read.OriginalPreparation, _preparation) || read.OriginalDescriptor.OriginalContainer != container)
                throw new UnauthorizedAccessException("The SAME transferred project READ/container is no longer current.");
            await sources.Take(() => source.ValidateOriginalCommandReadWithinSourceAsync(read, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            NativeOriginal? retained = null;
            Exception? primary = null; DeveloperResolvedProject? project = null; List<Exception> failures = [];
            try
            {
                work.DemandObservationCapacity();
                var native = await sources.Capture(() => source.CaptureOriginalCommandNativeReadWithinSourceAsync(read,
                    sources.Scope, sources.Retain, token), actual =>
                {
                    retained = new(actual); _natives.Add(retained);
                    var same = retained;
                    work.Observe(() => Physical(work, same.Close), () => same.OriginalClose);
                    if (!source.IsOwnedOriginalCommandNativeRead(read, actual))
                        throw new UnauthorizedAccessException("The actual transferred native capture lacks historical issuer custody.");
                }).ConfigureAwait(false);
                if (!source.IsIssuedOriginalCommandNativeRead(read, native))
                    throw new UnauthorizedAccessException("The transferred native capture is no longer current.");
                await sources.Take(() => source.ValidateOriginalCommandNativeReadWithinSourceAsync(read, native,
                    sources.Scope, sources.Retain, token)).ConfigureAwait(false);
                project = ObserveAuthorizedMetadata(reference, read, native, actor);
                await revalidate().ConfigureAwait(false);
            }
            catch (Exception failure) { primary = failure; }
            finally
            {
                if (retained is not null)
                    try { await JoinNativeAsync(read, retained).ConfigureAwait(false); }
                    catch (Exception failure) { failures.Add(failure); }
            }
            if (primary is not null) failures.Insert(0, primary);
            var distinct = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (distinct.Length == 1) ExceptionDispatchInfo.Capture(distinct[0]).Throw();
            if (distinct.Length > 1) throw new AggregateException("Transferred native observation and exact cleanup failed.", distinct);
            return project ?? throw new InvalidOperationException("No transferred original project was observed.");
        }
        private async Task JoinNativeAsync(IDeveloperOriginalProjectCommandRead read, NativeOriginal native)
        {
            if (!source.IsOwnedOriginalCommandNativeRead(read, native.Product))
                throw new UnauthorizedAccessException("Cannot invent teardown custody for a foreign native capture.");
            var close = Physical(work, native.Close);
            work.Retain(close);
            await close.ConfigureAwait(false);
            if (!Physical(work, () => source.IsClosedOriginalCommandNativeRead(read, native.Product, close)))
                throw new InvalidOperationException("The transferred native capture has no exact healthy close proof.");
        }
        internal async Task CloseAndDrainAsync()
        {
            List<Exception> failures = [];
            var read = _read;
            if (read is null) return;
            foreach (var native in _natives.ToArray())
                try { await JoinNativeAsync(read, native).ConfigureAwait(false); }
                catch (Exception failure) { CaptureCleanup(failures, native.OriginalClose, failure); }
            if (!source.IsOwnedOriginalCommandRead(read))
                failures.Add(new UnauthorizedAccessException("Cannot invent teardown custody for a foreign transferred READ."));
            else
            {
                try { Physical(work, () => { read.RequestOriginalRetirement(); return true; }); }
                catch (Exception failure) { failures.Add(failure); }
                try { _readClose ??= Physical(work, read.CloseAndDrainOriginalAsync); await _readClose.ConfigureAwait(false); }
                catch (Exception failure) { CaptureCleanup(failures, _readClose, failure); }
            }
            var distinct = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (distinct.Length == 1) ExceptionDispatchInfo.Capture(distinct[0]).Throw();
            if (distinct.Length > 1) throw new AggregateException("The actual transferred READ did not drain independently.", distinct);
        }
    }

    private sealed class ReadBorrowers(HomeColdProjectReadReconciliation source, AssistantPresentationOriginals work)
    {
        private IDeveloperOriginalProjectCommandRead? _read;
        private IDeveloperOriginalProjectCommandNativeRead? _native;
        private Task? _readClose, _nativeClose;
        internal void RetainRead(IDeveloperOriginalProjectCommandRead actual)
        {
            _read = actual;
            if (!source.IsOwnedOriginalCommandRead(actual))
                throw new UnauthorizedAccessException("The undisclosed READ product has no genuine historical source custody.");
            work.Observe(CloseRead);
        }
        internal void RetainNative(IDeveloperOriginalProjectCommandNativeRead actual)
        {
            _native = actual;
            if (_read is null || !source.IsOwnedOriginalCommandNativeRead(_read, actual))
                throw new UnauthorizedAccessException("The undisclosed native product has no genuine historical source custody.");
            work.Observe(CloseNative);
        }
        private Task CloseRead() => _readClose ??= Physical(work, () => _read!.CloseAndDrainOriginalAsync());
        private Task CloseNative() => _nativeClose ??= Physical(work, () => _native!.OriginalRead.DisposeAsync().AsTask());
        internal async Task JoinAsync()
        {
            List<Exception> failures = [];
            if (_native is not null)
            {
                if (_read is null || !source.IsOwnedOriginalCommandNativeRead(_read, _native))
                    failures.Add(new UnauthorizedAccessException("Cannot invent cleanup custody for the foreign native READ product."));
                else
                {
                    try { await CloseNative().ConfigureAwait(false); }
                    catch (Exception failure) { CaptureCleanup(failures, _nativeClose, failure); }
                    try
                    {
                        if (_nativeClose is null || !source.IsClosedOriginalCommandNativeRead(_read, _native, _nativeClose))
                            throw new InvalidOperationException("The actual native project READ has no healthy exact close proof.");
                    }
                    catch (Exception failure) { failures.Add(failure); }
                }
            }
            if (_read is not null)
            {
                if (!source.IsOwnedOriginalCommandRead(_read))
                    failures.Add(new UnauthorizedAccessException("Cannot invent cleanup custody for the foreign Home READ product."));
                else
                {
                    try { Physical(work, () => { _read.RequestOriginalRetirement(); return true; }); }
                    catch (Exception failure) { failures.Add(failure); }
                    try { await CloseRead().ConfigureAwait(false); }
                    catch (Exception failure) { CaptureCleanup(failures, _readClose, failure); }
                    try
                    {
                        if (_readClose is null || !Physical(work, () => source.IsClosedOwnedOriginalCommandRead(_read, _readClose)))
                            throw new InvalidOperationException("The actual command project READ has no exact healthy original cohort close proof.");
                    }
                    catch (Exception failure) { failures.Add(failure); }
                }
            }
            var distinct = failures.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            if (distinct.Length == 1) ExceptionDispatchInfo.Capture(distinct[0]).Throw();
            if (distinct.Length > 1) throw new AggregateException("Actual Home/native READ borrowers did not close independently.", distinct);
        }
    }

    private static void CaptureCleanup(List<Exception> destination, Task? actual, Exception caught)
    {
        if (actual?.Exception is { InnerExceptions.Count: > 0 } group) destination.AddRange(group.InnerExceptions);
        else destination.Add(caught);
    }

    private static T Physical<T>(AssistantPresentationOriginals work, Func<T> source)
    {
        var stack = _physicalSources ??= []; stack.Add(work);
        try { return source(); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }
    private static void DemandPhysicalExternalJoin(AssistantPresentationOriginals work)
    {
        if (_physicalSources?.Any(actual => ReferenceEquals(actual, work)) == true)
            throw new InvalidOperationException("An actual Dev source callback cannot join its own borrower drain.");
    }

    public void RequestRetirement() => _originals.RequestRetirement();
    public void DemandExternalOriginalRetirementJoin()
    {
        DemandPhysicalExternalJoin(_originals);
        Custody[] custodies; lock (_gate) custodies = _custodies.ToArray();
        foreach (var custody in custodies) custody.DemandExternalOriginalRetirementJoin();
        _originals.DemandExternalJoin(); _developer.DemandExternalOriginalRetirementJoin(); _projectReads.DemandExternalOriginalJoin();
    }
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _originals.CloseAndDrainAsync(); }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
