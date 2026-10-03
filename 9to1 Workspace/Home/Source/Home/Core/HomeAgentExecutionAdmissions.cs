using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Dulche.Runtime.Agents;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using BrokerRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;
using NineToOne.Dulche.Den;

namespace HavenOS.Home.Core;

/// <summary>Owning tool policy observations, never a grant. The registered owner must resolve the
/// SAME original call and immutable dispatch snapshot against current canonical object scopes; no matching name/DTO grants authority.</summary>
public sealed record HomeAgentToolDemand(string ActionId, IReadOnlyList<HomeObjectReference> Objects,
    IReadOnlyList<ResourceScope> ResourceScopes, IReadOnlySet<string> RequiredCapabilities,
    HomePermissionActionPolicy Policy, string TargetAppId = HomeAgentExecutionActionPolicies.AppId);
public interface IHomeAgentOriginalToolPolicySource
{
    ValueTask<HomeAgentToolDemand?> ResolveAsync(AuthenticatedResourceActor originalActor,
        DenAgentReference reference, AgentExecutionStep originalStep, OllamaToolCall originalCall, OllamaToolCall originalDispatchCall,
        CancellationToken cancellationToken) => ValueTask.FromResult<HomeAgentToolDemand?>(null);

    /// <summary>The registered owning adapter verifies its actual canonical receipt for the SAME
    /// original call/immutable dispatch/result/failure and approved request. DTO result flags/text never supply proof.</summary>
    ValueTask<HomeExecutionOutcome?> VerifyOriginalOutcomeAsync(AuthenticatedResourceActor originalActor,
        DenAgentReference reference, AgentExecutionStep originalStep, OllamaToolCall originalCall, OllamaToolCall originalDispatchCall,
        string originalRequestId, WorkspaceToolResult? originalResult, Exception? originalFailure,
        CancellationToken cancellationToken) => ValueTask.FromResult<HomeExecutionOutcome?>(null);
}

/// <summary>Only trusted Home composition registers this policy. Agent execution is an explicit
/// per-action decision; personal Den ownership/read/write never silently supplies Execute/Admin.</summary>
public sealed class HomeAgentExecutionActionPolicies : IHomeActionPolicySource
{
    public const string AppId = "home.agent";
    public const string RunAction = "agent.run";
    public HomePermissionActionPolicy? TryGet(string appId, string actionId) =>
        appId == AppId && actionId == RunAction ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null;
}

public sealed class HomeAgentPermissionRequiredException(string requestId) : UnauthorizedAccessException(
    "The exact original Home Agent request requires a durable Home decision.")
{
    public string RequestId { get; } = requestId;
}

/// <summary>Home-only issuer over the actual personal Den, held Home lease and canonical run store.
/// Only its private reference registries confer admission. This is not a run/Agent registry or a
/// public wire authority service. Missing owning tool/host composition remains denied.</summary>
public sealed class HomeAgentExecutionAdmissions : IAgentPermissionBroker, IChatExecutionAdmission,
    IDenAgentCurrentRuntimeContextSource, IAsyncDisposable
{
    private readonly HomePersonalDenFactory _dens;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly HomeNativeSessionLease _lease;
    private readonly HomePermissionTrustService _permissions;
    private readonly HomeApprovalPromptFlow _prompts;
    private readonly IAgentExecutionStateStore _runs;
    private readonly IModelProviderRegistry _models;
    private readonly HomePersonalModelRoutes? _routes;
    private readonly ResourceAuthorizationService _resources;
    private readonly IHomeAgentOriginalToolPolicySource? _tools;
    private readonly CancellationTokenSource _hostLifetime;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _admissionGate = new();
    private readonly Dictionary<Preparation, Prepared> _prepared = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AgentInvocationContext, Invocation> _invocations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AgentExecutionStep, StepAuthority> _steps = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<object, StepAuthority> _tokens = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<object, StepAuthority> _commitRoutes = new(ReferenceEqualityComparer.Instance);
    private readonly HomeNativeCoreApiSessions.AgentConnection? _nativeConnection;
    private readonly Func<DulcheDen, string, IAgentExecutionStateStore>? _auditReaders;
    private readonly Dictionary<Exception, PendingStepApproval> _approvalPauses = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> _unconfirmedRequests = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Operation> _operations = [];
    private Task? _closeTask;
    private bool _closing;

    public HomeAgentExecutionAdmissions(HomePersonalDenFactory dens, IAuthenticatedResourceActorSource actors,
        HomeNativeSessionLease actualHeldHomeLease, HomePermissionTrustService permissions,
        IAgentExecutionStateStore canonicalRuns, IModelProviderRegistry models, ResourceAuthorizationService resources,
        CancellationToken actualHostLifetime, HomePersonalModelRoutes? routes = null,
        IHomeApprovalPromptPresenter? presenter = null, IHomeAgentOriginalToolPolicySource? tools = null)
    {
        if (!actualHeldHomeLease.IsHeld || !actualHostLifetime.CanBeCanceled || actualHostLifetime.IsCancellationRequested)
            throw new UnauthorizedAccessException("A genuinely held Home lease and live original host lifetime are required.");
        _dens = dens; _actors = actors; _lease = actualHeldHomeLease; _permissions = permissions;
        _runs = canonicalRuns; _models = models; _resources = resources; _routes = routes; _tools = tools;
        _prompts = new(permissions, presenter); _hostLifetime = CancellationTokenSource.CreateLinkedTokenSource(actualHostLifetime);
    }

    // Trusted same-Store audit composition; it only reads an already bound original run.
    // The public held-lease fixture path remains distinct from native accepted-socket admission.
    internal HomeAgentExecutionAdmissions(HomePersonalDenFactory dens, IAuthenticatedResourceActorSource actors,
        HomeNativeSessionLease actualHeldHomeLease, HomePermissionTrustService permissions,
        IAgentExecutionStateStore canonicalRuns, IModelProviderRegistry models, ResourceAuthorizationService resources,
        CancellationToken actualHostLifetime, Func<DulcheDen, string, IAgentExecutionStateStore> originalAuditReaders,
        HomePersonalModelRoutes? routes = null, IHomeApprovalPromptPresenter? presenter = null,
        IHomeAgentOriginalToolPolicySource? tools = null)
        : this(dens, actors, actualHeldHomeLease, permissions, canonicalRuns, models, resources,
            actualHostLifetime, routes, presenter, tools)
    { _auditReaders = originalAuditReaders; }

    internal HomeAgentExecutionAdmissions(HomePersonalDenFactory dens, IAuthenticatedResourceActorSource actors,
        HomeNativeCoreApiSessions.AgentConnection sameOriginalConnection, HomePermissionTrustService permissions,
        IAgentExecutionStateStore canonicalRuns, IModelProviderRegistry models, ResourceAuthorizationService resources,
        CancellationToken actualHostLifetime, Func<DulcheDen, string, IAgentExecutionStateStore> originalAuditReaders,
        HomePersonalModelRoutes? routes, IHomeApprovalPromptPresenter? presenter, IHomeAgentOriginalToolPolicySource? tools)
        : this(dens, actors, sameOriginalConnection.Lease, permissions, canonicalRuns, models, resources,
            actualHostLifetime, routes, presenter, tools)
    { _nativeConnection = sameOriginalConnection; _auditReaders = originalAuditReaders; }

    // This lookup only routes a token to its issuer. Each forwarded boundary still validates
    // the exact private reference and original connection/run/current permission.
    internal bool OwnsOriginalAuthority(object token) => _tokens.ContainsKey(token);
    internal bool OwnsOriginalCommit(object admission) => _commitRoutes.ContainsKey(admission);

    /// <summary>Observation only. The private constructor is not serializable wire authority.
    /// Only the SAME issuer-registered preparation can later enter execution.</summary>
    public sealed class Preparation
    {
        internal Preparation(string requestId, DenAgentReference reference) { RequestId = requestId; Reference = reference; }
        public string RequestId { get; }
        public DenAgentReference Reference { get; }
    }
    public sealed record OriginalInvocation(AgentInvocationContext Context, DulcheDen Den, CancellationToken OriginalLifetime);

    public Task<Preparation> PrepareAsync(DenAgentReference reference, string objective,
        CancellationToken cancellationToken = default) => RunAsync(async token =>
    {
        if (string.IsNullOrWhiteSpace(objective) || objective.Length > 16384)
            throw new ArgumentException("An exact bounded Agent objective is required.", nameof(objective));
        var (session, definition) = await ReadCurrentAsync(reference, null, token).ConfigureAwait(false);
        var saved = ReadDeclaration<AgentCapabilityPolicy>(definition.CapabilityPolicyJson);
        if (saved is not null && AgentSavedPolicyValidation.Validate(saved, reference.AgentId) is not null) throw Refused();
        var capabilities = definition.AllowedPermissions
            .Where(value => saved is null || saved.AllowedCapabilities.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Where(value => saved is null || !saved.DeniedCapabilities.Contains(value, StringComparer.OrdinalIgnoreCase))
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        if (definition.AvailabilityBindings is not null && !definition.AvailabilityBindings.Any(binding =>
                binding.Scope == AgentAvailabilityScope.Global ||
                binding.Scope == AgentAvailabilityScope.Surface && binding.TargetId == "home.agent") ||
            definition.KnowledgeReferences.Count != 0 || definition.GraphReference is not null ||
            definition.MemoryPolicy is { } memory &&
                (memory.ReadFrequency != MemoryFrequency.Never || memory.WriteFrequency != MemoryFrequency.Never))
            throw new NotSupportedException("The saved Agent is unavailable here or requires an unsupported owning knowledge, graph or memory route.");
        if (ReadDeclaration<AgentDelegationPolicy>(definition.DelegationPolicyJson) is { } delegation &&
            AgentSavedPolicyValidation.Validate(delegation, reference.AgentId) is not null) throw Refused();
        var digest = Digest(new { reference, objective, capabilities = capabilities.Order(StringComparer.Ordinal).ToArray(),
            definition.ModelPolicyJson, definition.BudgetJson, definition.ToolIds, definition.SkillIds, definition.PluginIds, definition.McpCapabilityIds,
            definition.CapabilityPolicyJson, definition.DelegationPolicyJson, definition.AvailabilityBindings });
        var caller = _nativeConnection?.Caller ?? new HomePermissionCallerIdentity(session.Actor.ActorId, "Current Home Agent caller", "home.local-agent",
            session.Actor.AuthenticationRevision, true);
        var objects = new[] { new HomeObjectReference("den.agent", reference.DenId + "/" + reference.NamespaceId + "/" + reference.AgentId) };
        var submission = new HomePermissionRequestSubmission(Guid.NewGuid().ToString("N"), caller,
            "home-agent:" + Guid.NewGuid().ToString("N"), new(HomeAgentExecutionActionPolicies.AppId,
                HomeAgentExecutionActionPolicies.RunAction, objects), new(["den.agent"], 1, objects, false,
                    "Run this exact saved Agent revision for the supplied objective.", null, digest));
        var policy = _permissions.ResolveTrustedActionPolicy(submission.Scope.TargetAppId, submission.Scope.ActionName)
            ?? throw new UnauthorizedAccessException("The registered owning Home Agent action policy is unavailable.");
        var approved = await _permissions.AuthorizeAsync(submission, token).ConfigureAwait(false);
        await ReadCurrentAsync(reference, session, token).ConfigureAwait(false);
        var preparation = new Preparation(approved.RequestId, reference);
        _prepared.Add(preparation, new(preparation, session, definition, objective, capabilities, submission, policy));
        return preparation;
    }, cancellationToken);

    public Task<OriginalInvocation> AdmitAsync(Preparation samePreparation,
        CancellationToken cancellationToken = default) => RunAsync(async token =>
    {
        if (!_prepared.TryGetValue(samePreparation, out var prepared)) throw Refused();
        await ReadCurrentAsync(prepared.Preparation.Reference, prepared.Session, token).ConfigureAwait(false);
        var observed = await _permissions.ReadRequestObservationAsync(samePreparation.RequestId, token).ConfigureAwait(false);
        if (!Matches(prepared, observed)) throw Refused();
        var approval = await _prompts.ReviewPendingAsync(samePreparation.RequestId, token).ConfigureAwait(false);
        await ReadCurrentAsync(prepared.Preparation.Reference, prepared.Session, token).ConfigureAwait(false);
        if (!approval.IsAllowed) throw new HomeAgentPermissionRequiredException(samePreparation.RequestId);
        _unconfirmedRequests.Add(samePreparation.RequestId);
        // Begin only once for this exact private preparation; an executing/replayed request cannot mint a new context.
        var begun = await _permissions.BeginExecutionAsync(samePreparation.RequestId, token).ConfigureAwait(false);
        if (!begun.IsAllowed) throw Refused();
        observed = await _permissions.ReadRequestObservationAsync(samePreparation.RequestId, token).ConfigureAwait(false);
        if (!Matches(prepared, observed) || observed!.State != HomePermissionRequestState.Executing) throw Refused();
        await ReadCurrentAsync(prepared.Preparation.Reference, prepared.Session, token).ConfigureAwait(false);
        var context = new AgentInvocationContext(prepared.Session.Actor.ActorId, "home.agent", Context: [],
            SurfaceCapabilities: prepared.Capabilities, SpaceCapabilities: prepared.Capabilities,
            ProjectCapabilities: prepared.Capabilities, CallerCapabilities: prepared.Capabilities,
            HomeGrantedCapabilities: prepared.Capabilities);
        var invocation = new Invocation(prepared, context, CancellationTokenSource.CreateLinkedTokenSource(_hostLifetime.Token));
        _invocations.Add(context, invocation); _prepared.Remove(samePreparation);
        var executionDen = new DulcheDen(prepared.Session.Den.Store, new ExecutionPolicy(this, invocation), prepared.Session.Actor.ActorId);
        return new OriginalInvocation(context, executionDen, invocation.Lifetime.Token);
    }, cancellationToken);

    public ValueTask<AgentResult<IReadOnlySet<string>>> ResolveCapabilitiesAsync(AgentCapabilityPolicy agentPolicy,
        AgentInvocationContext context, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_invocations.TryGetValue(context, out var invocation)) return Failure<IReadOnlySet<string>>();
        await RequireInvocationAsync(invocation, token).ConfigureAwait(false);
        var narrowed = invocation.Prepared.Capabilities.Intersect(agentPolicy.AllowedCapabilities, StringComparer.OrdinalIgnoreCase)
            .Except(agentPolicy.DeniedCapabilities, StringComparer.OrdinalIgnoreCase).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        return AgentResult<IReadOnlySet<string>>.Success(narrowed);
    }, cancellationToken));

    public ValueTask<AgentResult<bool>> BindOriginalRunAsync(AgentInvocationContext originalContext,
        AgentRunSnapshot committedCanonicalRun, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_invocations.TryGetValue(originalContext, out var invocation)) return Failure<bool>();
        await RequireInvocationAsync(invocation, token).ConfigureAwait(false);
        var actual = await _runs.ReadAsync(committedCanonicalRun.AgentRunId, token).ConfigureAwait(false);
        await RequireInvocationAsync(invocation, token).ConfigureAwait(false);
        var reference = invocation.Prepared.Preparation.Reference;
        if (actual is null || Digest(actual.Run) != Digest(committedCanonicalRun) || actual.Run.AgentId != reference.AgentId ||
            actual.Run.DefinitionRevision != reference.DefinitionRevision || actual.Run.CallerId != originalContext.CallerId ||
            actual.Run.SurfaceId != originalContext.SurfaceId || actual.Run.Objective != invocation.Prepared.Objective ||
            actual.Run.SpaceId is not null || actual.Run.ProjectOrEntityId is not null || actual.Run.ParentAgentRunId is not null ||
            actual.Run.ParentSubagentId is not null || actual.Run.ContextSnapshot.Count != 0 ||
            !actual.Run.EffectiveCapabilities.IsSubsetOf(invocation.Prepared.Capabilities) ||
            !Guid.TryParse(actual.Run.SessionId, out var conversationId) || conversationId == Guid.Empty ||
            actual.Run.State != AgentRunState.Queued || string.IsNullOrWhiteSpace(actual.Run.CurrentAttemptId)) return Failure<bool>();
        var savedBudget = string.IsNullOrWhiteSpace(invocation.Prepared.Definition.BudgetJson) ? new AgentBudgetLimits() :
            JsonSerializer.Deserialize<AgentBudgetLimits>(invocation.Prepared.Definition.BudgetJson, DenJson.Options) ?? throw Refused();
        if (actual.Run.BudgetLimits.Validate("home.agent.run") is not null || savedBudget.Validate("home.agent.definition") is not null ||
            AgentBudgetLimits.Narrow(savedBudget, actual.Run.BudgetLimits) != actual.Run.BudgetLimits) return Failure<bool>();
        if (invocation.RunId is not null) return invocation.RunId == actual.Run.AgentRunId &&
            invocation.AttemptId == actual.Run.CurrentAttemptId ? AgentResult<bool>.Success(true) : Failure<bool>();
        var model = await FindCurrentModelAsync(invocation, actual.Run.ProviderId, actual.Run.ModelId, token).ConfigureAwait(false);
        if (model is null) return Failure<bool>();
        invocation.RunId = actual.Run.AgentRunId; invocation.AttemptId = actual.Run.CurrentAttemptId;
        invocation.RunInvariant = RunInvariant(actual.Run); invocation.ModelIdentity = CompatibilityName(model);
        invocation.ConversationId = conversationId; invocation.ModelKey = model.Key;
        if (_auditReaders is not null)
            invocation.AuditRuns = _auditReaders(new DulcheDen(invocation.Prepared.Session.Den.Store,
                new OriginalRunReadPolicy(invocation.Prepared.Session.Actor.ActorId, reference.NamespaceId, actual.Run.AgentRunId),
                invocation.Prepared.Session.Actor.ActorId), reference.NamespaceId);
        return AgentResult<bool>.Success(true);
    }, cancellationToken));

    public ValueTask<AgentResult<bool>> AuthorizeOriginalStepAsync(AgentInvocationContext originalContext,
        AgentExecutionStep originalStep, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_invocations.TryGetValue(originalContext, out var invocation)) return Failure<bool>();
        var actual = await RequireRunAsync(invocation, token).ConfigureAwait(false);
        if (actual.Run.State != AgentRunState.Running || originalStep.AgentRunId != actual.Run.AgentRunId ||
            originalStep.AttemptId != actual.Run.CurrentAttemptId || originalStep.CallerId != actual.Run.CallerId ||
            originalStep.Prompt != actual.Run.Objective || originalStep.SessionId != actual.Run.SessionId ||
            originalStep.Target.SessionId != actual.Run.SessionId || originalStep.Target.ModelId != actual.Run.ModelId ||
            originalStep.Target.ProviderId != actual.Run.ProviderId || originalStep.Target.EndpointId != (actual.Run.EndpointId ?? "") ||
            !originalStep.EffectiveCapabilities.SetEquals(actual.Run.EffectiveCapabilities) ||
            !originalStep.Target.Capabilities.SetEquals(actual.Run.EffectiveCapabilities) ||
            Digest(originalStep.ContextSnapshot) != Digest(actual.Run.ContextSnapshot) ||
            !originalStep.CompletedConsequentialActionIds.SetEquals(actual.CompletedConsequentialActionIds) ||
            originalStep.EventCursor != actual.LastEventSequence || originalStep.ResumeCheckpointId != actual.Run.CheckpointId ||
            Digest(originalStep.RemainingBudget) != Digest(actual.Run.BudgetLimits)) return Failure<bool>();
        var definition = invocation.Prepared.Definition;
        var dependencies = definition.ToolIds.Concat(definition.SkillIds).Concat(definition.PluginIds).Concat(definition.McpCapabilityIds).ToHashSet(StringComparer.Ordinal);
        if (!originalStep.EffectiveTools.SetEquals(dependencies)) return Failure<bool>();
        if (_steps.TryGetValue(originalStep, out var existing)) return AgentResult<bool>.Success(ReferenceEquals(existing.Invocation, invocation));
        var authoritativeTime = ReadAuthoritativeElapsed(actual);
        if (originalStep.RemainingBudget.MaxTime is not null && authoritativeTime is null) return Failure<bool>();
        if (invocation.ActiveStep is not null || originalStep.RemainingBudget.Validate("agent.step") is not null ||
            originalStep.RemainingBudget.MaxTokens is not null || originalStep.RemainingBudget.MaxCost is not null ||
            originalStep.RemainingBudget.MaxSteps is 0 || originalStep.RemainingBudget.MaxTime == TimeSpan.Zero) return Failure<bool>();
        // No authoritative token/cost reports exist in the current shared loop: finite limits refuse before model work.
        if (originalStep.RemainingBudget.MaxSteps is { } maxSteps &&
            (actual.Run.BudgetUsage.Steps.Availability != UsageAvailability.Measured || actual.Run.BudgetUsage.Steps.Value is not { } steps || steps >= maxSteps)) return Failure<bool>();
        var authority = new StepAuthority(invocation, originalStep, Stopwatch.GetTimestamp(),
            actual.Run.BudgetUsage.ToolCalls.Value ?? 0, authoritativeTime ?? TimeSpan.Zero);
        invocation.ActiveStep = authority; _steps.Add(originalStep, authority); _tokens.TryAdd(authority.Token, authority);
        return AgentResult<bool>.Success(true);
    }, cancellationToken));

    public ValueTask<AgentResult<AgentStepAdmission>> GetOriginalStepAdmissionAsync(AgentExecutionStep originalStep,
        CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_steps.TryGetValue(originalStep, out var authority)) return Failure<AgentStepAdmission>();
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        return AgentResult<AgentStepAdmission>.Success(new(originalStep.EffectiveCapabilities.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            authority.Token, authority.Invocation.Lifetime.Token));
    }, cancellationToken));

    public ValueTask DemandCurrentAsync(object originalStepAuthority, Guid conversationId, string modelIdentity,
        OllamaToolCall? originalCall, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_tokens.TryGetValue(originalStepAuthority, out var authority) || authority.Invocation.ConversationId != conversationId ||
            authority.Invocation.ModelIdentity != modelIdentity) throw Refused();
        var actual = await RequireStepAsync(authority, token).ConfigureAwait(false);
        if (originalCall is null)
        {
            if (authority.PendingCall is { } pending) RequireCallBody(pending.Call, pending.CallBody, pending.DispatchCall);
            return true;
        }
        if (authority.PendingCall is not null) throw Refused();
        // Every actual callback/retry is counted once. Publication/currentness null-call demands never spend a call.
        var maximum = authority.Step.RemainingBudget.MaxToolCalls;
        var measured = actual.Run.BudgetUsage.ToolCalls;
        if (maximum is not null && (measured.Availability != UsageAvailability.Measured || measured.Value is null ||
            Math.Max(measured.Value.Value, checked(authority.BaselineCalls + authority.Calls)) >= maximum.Value)) throw Refused();
        if (_tools is null) throw Refused();
        // Capture before any owning/provider/review await; runtime and owner receive only this immutable body.
        var dispatchCall = originalCall with { Arguments = originalCall.Arguments.ToFrozenDictionary(
            item => item.Key, item => item.Value.Clone(), StringComparer.Ordinal) };
        var callBody = CallFingerprint(dispatchCall);
        RequireCallBody(originalCall, callBody, dispatchCall);
        var demand = await _tools.ResolveAsync(authority.Invocation.Prepared.Session.Actor,
            authority.Invocation.Prepared.Preparation.Reference, authority.Step, originalCall, dispatchCall, token).ConfigureAwait(false);
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(originalCall, callBody, dispatchCall);
        if (demand is null || string.IsNullOrWhiteSpace(demand.TargetAppId) || demand.TargetAppId != demand.TargetAppId.Trim() || string.IsNullOrWhiteSpace(demand.ActionId) || demand.Objects.Count == 0 ||
            demand.ResourceScopes.Count == 0 || !demand.RequiredCapabilities.IsSubsetOf(authority.Step.EffectiveCapabilities)) throw Refused();
        // Retain a detached owning scope observation across permission/provider awaits.
        demand = demand with { Objects = demand.Objects.ToArray(), ResourceScopes = demand.ResourceScopes.ToArray(),
            RequiredCapabilities = demand.RequiredCapabilities.ToFrozenSet(StringComparer.OrdinalIgnoreCase) };
        var actor = await _resources.AuthorizeForActorAsync(authority.Invocation.Prepared.Session.Actor, demand.ActionId, demand.ResourceScopes, token).ConfigureAwait(false);
        if (actor != authority.Invocation.Prepared.Session.Actor) throw Refused();
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(originalCall, callBody, dispatchCall);
        var digest = Digest(new { callBody, reference = authority.Invocation.Prepared.Preparation.Reference,
            authority.Step.AgentRunId, authority.Step.AttemptId, authority.Step.SessionId });
        var policy = _permissions.ResolveTrustedActionPolicy(demand.TargetAppId, demand.ActionId);
        if (policy is null || policy != demand.Policy || !policy.RequiresPerActionApproval) throw Refused();
        var submission = new HomePermissionRequestSubmission(Guid.NewGuid().ToString("N"), authority.Invocation.Prepared.Submission.Caller,
            authority.Invocation.Prepared.Submission.SessionId, new(demand.TargetAppId, demand.ActionId, demand.Objects),
            new(demand.Objects.Select(item => item.ObjectType).Distinct(StringComparer.Ordinal).ToArray(), demand.Objects.Count,
                demand.Objects, false, "Run this exact original tool call against its current owning objects.", null, digest));
        var approval = await _permissions.AuthorizeAsync(submission, token).ConfigureAwait(false);
        if (approval.State == HomePermissionRequestState.PendingApproval)
            approval = await _prompts.ReviewPendingAsync(approval.RequestId, token).ConfigureAwait(false);
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(originalCall, callBody, dispatchCall);
        if (!approval.IsAllowed)
        {
            var current = await _permissions.ReadRequestObservationAsync(approval.RequestId, token).ConfigureAwait(false);
            await RequireStepAsync(authority, token).ConfigureAwait(false);
            RequireCallBody(originalCall, callBody, dispatchCall);
            if (current is null || current.State != HomePermissionRequestState.PendingApproval || current.Caller != submission.Caller ||
                current.SessionId != submission.SessionId || Digest(current.Scope) != Digest(submission.Scope) ||
                current.Impact.ArgumentsDigest != digest || current.Policy != policy) throw Refused();
            var refusal = new HomeAgentPermissionRequiredException(current.RequestId);
            _approvalPauses.Add(refusal, new(authority, current.RequestId, Digest(current.Scope), digest));
            throw refusal;
        }
        var approvedRequest = await _permissions.ReadRequestObservationAsync(approval.RequestId, token).ConfigureAwait(false);
        if (approvedRequest is null || approvedRequest.Caller != submission.Caller || approvedRequest.SessionId != submission.SessionId ||
            Digest(approvedRequest.Scope) != Digest(submission.Scope) || approvedRequest.Impact.ArgumentsDigest != digest || approvedRequest.Policy != policy) throw Refused();
        RequireCallBody(originalCall, callBody, dispatchCall);
        _unconfirmedRequests.Add(approval.RequestId);
        var begun = await _permissions.BeginExecutionAsync(approval.RequestId, token).ConfigureAwait(false);
        if (!begun.IsAllowed) throw Refused();
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(originalCall, callBody, dispatchCall);
        if (await _resources.AuthorizeForActorAsync(authority.Invocation.Prepared.Session.Actor, demand.ActionId, demand.ResourceScopes, token).ConfigureAwait(false) != actor) throw Refused();
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(originalCall, callBody, dispatchCall);
        authority.Calls = checked(authority.Calls + 1); authority.ToolRequests.Add(begun.RequestId);
        authority.PendingCall = new(originalCall, callBody, dispatchCall, demand, begun.RequestId, Digest(submission.Scope), digest);
        _commitRoutes.TryAdd(authority.PendingCall.CommitAdmission, authority);
        // Admission remains unconfirmed until the registered owning receipt settles this exact call.
        return true;
    }, cancellationToken));

    public ValueTask<OllamaToolCall> GetOriginalDispatchCallAsync(object originalStepAuthority, Guid conversationId,
        string modelIdentity, OllamaToolCall originalCall, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_tokens.TryGetValue(originalStepAuthority, out var authority) || authority.Invocation.ConversationId != conversationId ||
            authority.Invocation.ModelIdentity != modelIdentity || authority.PendingCall is not { } pending ||
            !ReferenceEquals(pending.Call, originalCall)) throw Refused();
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(originalCall, pending.CallBody, pending.DispatchCall);
        var observed = await _permissions.ReadRequestObservationAsync(pending.RequestId, token).ConfigureAwait(false);
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(originalCall, pending.CallBody, pending.DispatchCall);
        if (!MatchesPendingCall(authority, pending, observed)) throw Refused();
        return pending.DispatchCall;
    }, cancellationToken));

    public ValueTask<object> GetOriginalCommitAdmissionAsync(object originalStepAuthority, Guid conversationId,
        string modelIdentity, OllamaToolCall originalDispatchCall, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_tokens.TryGetValue(originalStepAuthority, out var authority) || authority.Invocation.ConversationId != conversationId ||
            authority.Invocation.ModelIdentity != modelIdentity || authority.PendingCall is not { } pending ||
            !ReferenceEquals(pending.DispatchCall, originalDispatchCall)) throw Refused();
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(pending.Call, pending.CallBody, pending.DispatchCall);
        var observed = await _permissions.ReadRequestObservationAsync(pending.RequestId, token).ConfigureAwait(false);
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        if (!MatchesPendingCall(authority, pending, observed)) throw Refused();
        return pending.CommitAdmission;
    }, cancellationToken));

    public ValueTask<AuthenticatedResourceActor> DemandOriginalCommitCurrentAsync(object originalCommitAdmission,
        CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_commitRoutes.TryGetValue(originalCommitAdmission, out var authority) ||
            authority.PendingCall is not { } held || !ReferenceEquals(held.CommitAdmission, originalCommitAdmission)) throw Refused();
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(held.Call, held.CallBody, held.DispatchCall);
        var observed = await _permissions.ReadRequestObservationAsync(held.RequestId, token).ConfigureAwait(false);
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        RequireCallBody(held.Call, held.CallBody, held.DispatchCall);
        if (!MatchesPendingCall(authority, held, observed)) throw Refused();
        // The Files metadata lease is already held: never call the Files resource resolver here.
        return authority.Invocation.Prepared.Session.Actor;
    }, cancellationToken));

    public ValueTask CompleteOriginalCallAsync(object originalStepAuthority, Guid conversationId, string modelIdentity,
        OllamaToolCall originalCall, OllamaToolCall originalDispatchCall, WorkspaceToolResult? originalResult, Exception? originalFailure,
        CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_tokens.TryGetValue(originalStepAuthority, out var authority) || authority.Invocation.ConversationId != conversationId ||
            authority.Invocation.ModelIdentity != modelIdentity || authority.PendingCall is not { } pending ||
            !ReferenceEquals(pending.Call, originalCall) || !ReferenceEquals(pending.DispatchCall, originalDispatchCall) ||
            (originalResult is null) == (originalFailure is null) || _tools is null) throw Refused();
        var observed = await _permissions.ReadRequestObservationAsync(pending.RequestId, token).ConfigureAwait(false);
        if (!MatchesPendingSettlement(authority, pending, observed)) throw Refused();
        if (CallFingerprint(originalDispatchCall) != pending.CallBody) throw Refused();
        var outcome = await _tools.VerifyOriginalOutcomeAsync(authority.Invocation.Prepared.Session.Actor,
            authority.Invocation.Prepared.Preparation.Reference, authority.Step, originalCall, originalDispatchCall, pending.RequestId,
            originalResult, originalFailure, token).ConfigureAwait(false);
        if (outcome is null) throw Refused();
        outcome.Validate(); outcome = outcome with { AffectedObjects = outcome.AffectedObjects.ToArray() };
        if (outcome.AffectedObjects.Any(item => !pending.Demand.Objects.Contains(item))) throw Refused();
        // The registered owner proves its actual durable receipt, including a known
        // committed effect followed by cancellation/history/cleanup failure. Text or
        // Activity flags cannot fabricate success; future work still checks authority.
        observed = await _permissions.ReadRequestObservationAsync(pending.RequestId, token).ConfigureAwait(false);
        if (!MatchesPendingSettlement(authority, pending, observed)) throw Refused();
        var recorded = await _permissions.RecordExecutionAsync(pending.RequestId, outcome, token).ConfigureAwait(false);
        if (!recorded.Succeeded) throw new InvalidOperationException("The exact owning Agent tool outcome could not be recorded.");
        var actual = await _permissions.ReadRequestObservationAsync(pending.RequestId, token).ConfigureAwait(false);
        if (actual is null || actual.State != outcome.State || actual.Caller != authority.Invocation.Prepared.Submission.Caller ||
            actual.SessionId != authority.Invocation.Prepared.Submission.SessionId || Digest(actual.Scope) != pending.ScopeBody ||
            actual.Impact.ArgumentsDigest != pending.ArgumentsDigest || actual.ResultCode != outcome.Code ||
            actual.ResultMessage != outcome.Message) throw Refused();
        authority.RecordedOutcomes.Add((pending.RequestId, outcome));
        if (outcome.State == HomePermissionRequestState.PartiallyCompleted) authority.Invocation.HasPartialOwnedOutcome = true;
        _unconfirmedRequests.Remove(pending.RequestId); _commitRoutes.TryRemove(pending.CommitAdmission, out _); authority.PendingCall = null;
        // This path only settles that SAME already-admitted original effect. It does not
        // refresh execution authority, admit another call, or allow publication after retirement.
        return true;
    }, CancellationToken.None, originalSettlement: true));

    internal Task AuditOriginalTerminalAsync(AgentInvocationContext sameOriginalContext, string? expectedRunId = null) =>
        RunAsync(async token =>
        {
            if (!_invocations.TryGetValue(sameOriginalContext, out var invocation)) throw Refused();
            if (invocation.RunId is not { } sameRunId) return false;
            if (expectedRunId is not null && expectedRunId != sameRunId || invocation.AuditRuns is null) throw Refused();
            var snapshot = await invocation.AuditRuns.ReadAsync(sameRunId, token).ConfigureAwait(false);
            if (snapshot is null || snapshot.Run.AgentRunId != sameRunId || snapshot.Run.CurrentAttemptId != invocation.AttemptId ||
                RunInvariant(snapshot.Run) != invocation.RunInvariant) throw Refused();
            var terminal = snapshot.Run.State switch
            {
                AgentRunState.Completed => HomePermissionRequestState.Succeeded,
                AgentRunState.Failed or AgentRunState.CannotRecover => HomePermissionRequestState.Failed,
                AgentRunState.Stopped or AgentRunState.Cancelled => HomePermissionRequestState.Cancelled,
                _ => (HomePermissionRequestState?)null
            };
            if (terminal is null) return false; // AwaitingApproval/Paused are live canonical runs, not terminal success.
            if (invocation.HasPartialOwnedOutcome || invocation.ActiveStep?.PendingCall is not null || snapshot.UncertainConsequentialActionIds.Count != 0)
                terminal = HomePermissionRequestState.PartiallyCompleted;
            var root = invocation.Prepared.Preparation.RequestId;
            var observed = await _permissions.ReadRequestObservationAsync(root, token).ConfigureAwait(false);
            if (!MatchesOriginalRootSettlement(invocation.Prepared, observed)) throw Refused();
            var outcome = new HomeExecutionOutcome(terminal.Value, "HOME_AGENT_CANONICAL_" + snapshot.Run.State.ToString().ToUpperInvariant(),
                "The same canonical Agent run reached its recorded terminal state.", invocation.Prepared.Submission.Scope.Objects);
            if (observed!.State == terminal && observed.ResultCode == outcome.Code && observed.ResultMessage == outcome.Message &&
                !_unconfirmedRequests.Contains(root)) return true;
            if (observed.State != HomePermissionRequestState.Executing) throw Refused();
            var recorded = await _permissions.RecordExecutionAsync(root, outcome, token).ConfigureAwait(false);
            if (!recorded.Succeeded) throw new InvalidOperationException("The original canonical Agent terminal outcome could not be audited.");
            var actual = await _permissions.ReadRequestObservationAsync(root, token).ConfigureAwait(false);
            if (actual is null || actual.State != terminal || actual.ResultCode != outcome.Code ||
                actual.Caller != invocation.Prepared.Submission.Caller || actual.SessionId != invocation.Prepared.Submission.SessionId ||
                Digest(actual.Scope) != Digest(invocation.Prepared.Submission.Scope) ||
                actual.Impact.ArgumentsDigest != invocation.Prepared.Submission.Impact.ArgumentsDigest) throw Refused();
            _unconfirmedRequests.Remove(root);
            return true;
        }, CancellationToken.None, originalSettlement: true);

    private static bool MatchesOriginalRootSettlement(Prepared prepared, BrokerRequest? observed) => observed is not null &&
        observed.RequestId == prepared.Submission.RequestId && observed.Caller == prepared.Submission.Caller &&
        observed.SessionId == prepared.Submission.SessionId && observed.Policy == prepared.Policy &&
        Digest(observed.Scope) == Digest(prepared.Submission.Scope) &&
        observed.Impact.ArgumentsDigest == prepared.Submission.Impact.ArgumentsDigest &&
        observed.Impact.ResourceBinding is null && !observed.Impact.IsUnknown;

    private bool MatchesPendingCall(StepAuthority authority, PendingCallAuthority pending, BrokerRequest? observed) =>
        ReferenceEquals(authority.PendingCall, pending) && observed is not null && observed.State == HomePermissionRequestState.Executing &&
        observed.RequestId == pending.RequestId && observed.Caller == authority.Invocation.Prepared.Submission.Caller &&
        observed.SessionId == authority.Invocation.Prepared.Submission.SessionId && Digest(observed.Scope) == pending.ScopeBody &&
        observed.Impact.ArgumentsDigest == pending.ArgumentsDigest && observed.Policy == pending.Demand.Policy &&
        _permissions.ResolveTrustedActionPolicy(observed.Scope.TargetAppId, observed.Scope.ActionName) == pending.Demand.Policy;

    private bool MatchesPendingSettlement(StepAuthority authority, PendingCallAuthority pending, BrokerRequest? observed) =>
        _steps.TryGetValue(authority.Step, out var held) && ReferenceEquals(held, authority) &&
        _tokens.TryGetValue(authority.Token, out var tokenAuthority) && ReferenceEquals(tokenAuthority, authority) &&
        ReferenceEquals(authority.Invocation.ActiveStep, authority) && Digest(authority.Step) == authority.OriginalBody &&
        ReferenceEquals(authority.PendingCall, pending) && observed is not null &&
        observed.State == HomePermissionRequestState.Executing && observed.RequestId == pending.RequestId &&
        observed.Caller == authority.Invocation.Prepared.Submission.Caller &&
        observed.SessionId == authority.Invocation.Prepared.Submission.SessionId &&
        Digest(observed.Scope) == pending.ScopeBody && observed.Impact.ArgumentsDigest == pending.ArgumentsDigest &&
        observed.Policy == pending.Demand.Policy;

    public ValueTask<CancellationToken> GetOriginalLifetimeAsync(object originalStepAuthority,
        CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_tokens.TryGetValue(originalStepAuthority, out var authority)) throw Refused();
        await RequireStepAsync(authority, token).ConfigureAwait(false); return authority.Invocation.Lifetime.Token;
    }, cancellationToken));

    public ValueTask<AgentResult<AgentApprovalRequirement>> GetOriginalApprovalAsync(AgentExecutionStep originalStep,
        Exception originalFailure, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_steps.TryGetValue(originalStep, out var authority) || !_approvalPauses.TryGetValue(originalFailure, out var pause) ||
            !ReferenceEquals(pause.Authority, authority) || Digest(originalStep) != authority.OriginalBody) return Failure<AgentApprovalRequirement>();
        var run = await RequireRunAsync(authority.Invocation, token).ConfigureAwait(false);
        if (run.Run.State is not (AgentRunState.Running or AgentRunState.AwaitingApproval)) return Failure<AgentApprovalRequirement>();
        var current = await _permissions.ReadRequestObservationAsync(pause.RequestId, token).ConfigureAwait(false);
        await RequireInvocationAsync(authority.Invocation, token).ConfigureAwait(false);
        if (current is null || current.State != HomePermissionRequestState.PendingApproval || current.Caller != authority.Invocation.Prepared.Submission.Caller ||
            current.SessionId != authority.Invocation.Prepared.Submission.SessionId || Digest(current.Scope) != pause.ScopeBody ||
            current.Impact.ArgumentsDigest != pause.ArgumentsDigest || current.Policy != _permissions.ResolveTrustedActionPolicy(current.Scope.TargetAppId, current.Scope.ActionName))
            return Failure<AgentApprovalRequirement>();
        return AgentResult<AgentApprovalRequirement>.Success(new(current.RequestId, run.Run.AgentRunId, run.Run.CallerId,
            current.Scope.ActionName, Digest(current.Scope), current.Policy.Risk.ToString(),
            current.Scope.Objects.Select(item => item.ObjectId).ToArray(), current.Impact.ChangePreview ?? "Original owning action approval.",
            "Pending", current.RequestedAt, null, null));
    }, cancellationToken));

    public ValueTask<AgentResult<AgentApprovalRequirement>> RequestApprovalAsync(AgentApprovalRequirement request,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(Failure<AgentApprovalRequirement>());

    public ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> GetForObservationAsync(DenAgentReference reference,
        CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        var (session, _) = await ReadCurrentAsync(reference, null, token, requireEnabled: false).ConfigureAwait(false);
        var inherited = await ReadInheritedModelKeyAsync(token).ConfigureAwait(false);
        await ReadCurrentAsync(reference, session, token, requireEnabled: false).ConfigureAwait(false);
        return AgentResult<DenAgentCurrentRuntimeContext>.Success(new(reference, CapabilityHostPlatform.Current, "user", inherited));
    }, cancellationToken));
    public ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> GetForInvocationAsync(DenAgentReference reference,
        AgentInvocationContext originalInvocation, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_invocations.TryGetValue(originalInvocation, out var invocation) || invocation.Prepared.Preparation.Reference != reference) return Failure<DenAgentCurrentRuntimeContext>();
        await RequireInvocationAsync(invocation, token).ConfigureAwait(false);
        var inherited = await ReadInheritedModelKeyAsync(token).ConfigureAwait(false);
        await RequireInvocationAsync(invocation, token).ConfigureAwait(false);
        return AgentResult<DenAgentCurrentRuntimeContext>.Success(new(reference, CapabilityHostPlatform.Current, AgentScope(reference), inherited));
    }, cancellationToken));
    public ValueTask<AgentResult<DenAgentCurrentRuntimeContext>> GetForStepAsync(DenAgentReference reference,
        AgentExecutionStep originalStep, CancellationToken cancellationToken = default) => new(RunAsync(async token =>
    {
        if (!_steps.TryGetValue(originalStep, out var authority) || authority.Invocation.Prepared.Preparation.Reference != reference) return Failure<DenAgentCurrentRuntimeContext>();
        await RequireStepAsync(authority, token).ConfigureAwait(false);
        return AgentResult<DenAgentCurrentRuntimeContext>.Success(new(reference, CapabilityHostPlatform.Current, AgentScope(reference), authority.Invocation.ModelKey));
    }, cancellationToken));

    private async Task<(HomePersonalDenSession Session, AgentDefinitionRecord Definition)> ReadCurrentAsync(
        DenAgentReference reference, HomePersonalDenSession? expected, CancellationToken token, bool requireEnabled = true)
    {
        token.ThrowIfCancellationRequested();
        if (_nativeConnection is not null) await _nativeConnection.DemandCurrentAsync(token).ConfigureAwait(false);
        if (!_lease.IsHeld || string.IsNullOrWhiteSpace(reference.DenId) || string.IsNullOrWhiteSpace(reference.NamespaceId) ||
            !Guid.TryParseExact(reference.AgentId, "D", out var id) || id == Guid.Empty || reference.AgentId != id.ToString("D") || reference.DefinitionRevision < 1) throw Refused();
        var session = await _dens.OpenAsync(token).ConfigureAwait(false);
        if (session.Actor.ProfileId != _lease.ProfileId || session.Actor.AccountId is not null || session.Actor.OrganisationId is not null ||
            session.DenId != reference.DenId || expected is not null &&
            (session.Actor != expected.Actor || session.DenId != expected.DenId || !ReferenceEquals(session.Den.Store, expected.Den.Store))) throw Refused();
        var definition = await session.Den.GetAsync<AgentDefinitionRecord>(reference.NamespaceId, reference.AgentId, token).ConfigureAwait(false);
        if (definition is null || definition.Revision != reference.DefinitionRevision || requireEnabled && !definition.Enabled ||
            definition.AllowedPermissions.Any(string.IsNullOrWhiteSpace)) throw Refused();
        var current = await _dens.OpenAsync(token).ConfigureAwait(false);
        if (!_lease.IsHeld || current.Actor != session.Actor || current.DenId != session.DenId ||
            !ReferenceEquals(current.Den.Store, session.Den.Store) || await _actors.GetCurrentAsync(token).ConfigureAwait(false) != session.Actor) throw Refused();
        if (_nativeConnection is not null) await _nativeConnection.DemandCurrentAsync(token).ConfigureAwait(false);
        definition = DenAgentAuthoringFields.Capture(definition, cancellationToken: token);
        token.ThrowIfCancellationRequested(); return (current, definition);
    }
    private async Task RequireInvocationAsync(Invocation invocation, CancellationToken token)
    {
        invocation.Lifetime.Token.ThrowIfCancellationRequested();
        if (!_invocations.TryGetValue(invocation.Context, out var held) || !ReferenceEquals(held, invocation)) throw Refused();
        await ReadCurrentAsync(invocation.Prepared.Preparation.Reference, invocation.Prepared.Session, token).ConfigureAwait(false);
        var request = await _permissions.ReadRequestObservationAsync(invocation.Prepared.Preparation.RequestId, token).ConfigureAwait(false);
        if (!Matches(invocation.Prepared, request) || !await _permissions.IsExecutionCurrentAsync(invocation.Prepared.Preparation.RequestId, token).ConfigureAwait(false)) throw Refused();
        await ReadCurrentAsync(invocation.Prepared.Preparation.Reference, invocation.Prepared.Session, token).ConfigureAwait(false);
    }
    private async Task<AgentExecutionSnapshot> RequireRunAsync(Invocation invocation, CancellationToken token)
    {
        await RequireInvocationAsync(invocation, token).ConfigureAwait(false);
        var run = invocation.RunId is null ? null : await _runs.ReadAsync(invocation.RunId, token).ConfigureAwait(false);
        await RequireInvocationAsync(invocation, token).ConfigureAwait(false);
        if (run is null || run.Run.CurrentAttemptId != invocation.AttemptId || RunInvariant(run.Run) != invocation.RunInvariant ||
            run.Run.State is not (AgentRunState.Queued or AgentRunState.Running or AgentRunState.Waiting or AgentRunState.AwaitingApproval)) throw Refused();
        await RequireInvocationAsync(invocation, token).ConfigureAwait(false);
        if (await FindCurrentModelAsync(invocation, run.Run.ProviderId, run.Run.ModelId, token).ConfigureAwait(false) is null) throw Refused();
        return run;
    }
    private async Task<AgentExecutionSnapshot> RequireStepAsync(StepAuthority authority, CancellationToken token)
    {
        if (!_steps.TryGetValue(authority.Step, out var held) || !ReferenceEquals(held, authority) ||
            !ReferenceEquals(authority.Invocation.ActiveStep, authority) || Digest(authority.Step) != authority.OriginalBody) throw Refused();
        var run = await RequireRunAsync(authority.Invocation, token).ConfigureAwait(false);
        var authoritativeTime = ReadAuthoritativeElapsed(run);
        if (run.Run.State != AgentRunState.Running || authority.Step.EventCursor > run.LastEventSequence ||
            authority.Step.RemainingBudget.MaxTime is { } maximum && (authoritativeTime is null ||
                Max(authoritativeTime.Value, authority.BaselineTime + Stopwatch.GetElapsedTime(authority.StartedAt)) >= maximum)) throw Refused();
        return run;
    }
    // Empty is not an estimate for prior work. Only the exact initial canonical usage,
    // original first attempt and absence of durable work can supply the initial zero.
    // Once admitted, this SAME step's real elapsed time is always measured above.
    private static TimeSpan? ReadAuthoritativeElapsed(AgentExecutionSnapshot snapshot)
    {
        var run = snapshot.Run;
        if (run.BudgetUsage.Time.Availability == UsageAvailability.Measured && run.BudgetUsage.Time.Value is { } measured)
            return measured >= TimeSpan.Zero ? measured : null;
        if (run.BudgetUsage != AgentBudgetUsage.Empty || run.Attempts.Count != 1 ||
            run.Attempts[0].AttemptId != run.CurrentAttemptId || run.Attempts[0].Number != 1 ||
            run.Attempts[0].Usage != AgentBudgetUsage.Empty || run.Attempts[0].CompletedActionIds.Count != 0 ||
            run.Attempts[0].FinishedAtUtc is not null || run.CheckpointId is not null || run.RecoveryState is not null ||
            run.OutputReferenceIds.Count != 0 || run.BlockerIds.Count != 0 || run.ApprovalIds.Count != 0 ||
            run.SubagentRunIds.Count != 0 || run.ChildAgentRunIds.Count != 0 || run.FinishedAtUtc is not null ||
            snapshot.Checkpoints.Count != 0 || snapshot.Outputs.Count != 0 || snapshot.Subagents.Count != 0 ||
            snapshot.Reservations.Count != 0 || snapshot.Blockers.Count != 0 || snapshot.Approvals.Count != 0 ||
            snapshot.CompletedConsequentialActionIds.Count != 0 || snapshot.UncertainConsequentialActionIds.Count != 0) return null;
        return TimeSpan.Zero;
    }
    private async Task<ProviderModelDescriptor?> FindCurrentModelAsync(Invocation invocation, string? provider, string? model,
        CancellationToken token)
    {
        var policy = string.IsNullOrWhiteSpace(invocation.Prepared.Definition.ModelPolicyJson) ? new AgentModelPolicy(true) :
            JsonSerializer.Deserialize<AgentModelPolicy>(invocation.Prepared.Definition.ModelPolicyJson, DenJson.Options) ?? throw Refused();
        var required = new HashSet<ToolCapability> { ToolCapability.Text };
        foreach (var value in policy.RequiredCapabilities ?? FrozenSet<string>.Empty)
            if (!Enum.TryParse<ToolCapability>(value, false, out var capability) || !Enum.IsDefined(capability) || capability.ToString() != value) return null;
            else required.Add(capability);
        var observations = await _models.GetModelsAsync(new ModelCataloguePolicy(AllowLocal: true, AllowRemote: false), token).ConfigureAwait(false);
        var models = observations.Select(item => item with
        { Model = item.Model with { Capabilities = item.Capabilities.ToFrozenSet() } }).ToArray();
        var selected = models.Where(item => item.IsLocal && item.ProviderId == provider && item.Name == model).ToArray();
        if (selected.Length != 1 || !required.All(selected[0].Supports)) return null;
        var inherited = policy.Inherit ? await ReadInheritedModelKeyAsync(token).ConfigureAwait(false) : null;
        if (policy.Inherit ? inherited != selected[0].Key : policy.ProviderId != provider || policy.ModelId != model) return null;
        // Route/profile/provider preview may await. The earlier detached inventory is
        // retained for comparison; publication must use a fresh exact current model.
        var finalObservations = await _models.GetModelsAsync(new ModelCataloguePolicy(AllowLocal: true, AllowRemote: false), token).ConfigureAwait(false);
        var current = finalObservations.Where(item => item.IsLocal && item.ProviderId == provider && item.Name == model)
            .Select(item => item with { Model = item.Model with { Capabilities = item.Capabilities.ToFrozenSet() } }).ToArray();
        if (current.Length != 1 || !required.All(current[0].Supports) || ModelSnapshot(selected[0]) != ModelSnapshot(current[0])) return null;
        return current[0];
    }
    private async Task<string?> ReadInheritedModelKeyAsync(CancellationToken token)
    {
        if (_routes is null) return null;
        var route = await _routes.GetAsync(Dulche.Runtime.ModelCapabilityCategory.Active, token).ConfigureAwait(false) ??
            await _routes.GetAsync(Dulche.Runtime.ModelCapabilityCategory.Chat, token).ConfigureAwait(false);
        if (route is null) return null;
        var registry = new Dulche.Runtime.ModelRouteRegistry(_models, new Dulche.Runtime.InMemoryModelRouteRepository(), new Dulche.Runtime.ModelRouteResolver(_models));
        var preview = await registry.PreviewAsync(route with { Policy = route.Policy with { AllowRemote = false, AllowCloud = false } },
            containsPrivateContext: true, cancellationToken: token).ConfigureAwait(false);
        var selected = preview.Selection?.Model;
        return selected is null || selected.ArtifactRevision is not null ? null : selected.ProviderId + ":" + selected.ModelId;
    }
    private static string ModelSnapshot(ProviderModelDescriptor model) => Digest(new { model.ProviderId, model.IsLocal,
        model.Model.Name, model.Model.SizeBytes, model.Model.Family, model.Model.ParameterSize, model.Model.Quantization,
        model.Model.ModifiedAt, model.ContextWindow, model.DisplayName, Features = model.Capabilities.Order().ToArray() });
    private static string CompatibilityName(ProviderModelDescriptor model) => model.ProviderId == "ollama" ? model.Name : model.Key;
    private static string AgentScope(DenAgentReference reference) => "agent:" + Guid.ParseExact(reference.AgentId, "D").ToString("N");
    private static void RequireCallBody(OllamaToolCall originalCall, string body, OllamaToolCall dispatchCall)
    {
        if (ReferenceEquals(originalCall, dispatchCall) || dispatchCall.Arguments is not FrozenDictionary<string, JsonElement> ||
            CallFingerprint(originalCall) != body || CallFingerprint(dispatchCall) != body) throw Refused();
    }
    // Object property and argument ordering are canonical; arrays and exact scalar values retain order/identity.
    // This same fingerprint binds the immutable owner observation, approved digest and actual dispatch snapshot.
    private static string CallFingerprint(OllamaToolCall call)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes))
        {
            writer.WriteStartObject(); writer.WriteString("name", call.Name); writer.WriteString("id", call.Id);
            writer.WritePropertyName("arguments"); writer.WriteStartObject();
            foreach (var item in call.Arguments.OrderBy(item => item.Key, StringComparer.Ordinal))
            { writer.WritePropertyName(item.Key); WriteCanonical(writer, item.Value); }
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var item in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            { writer.WritePropertyName(item.Name); WriteCanonical(writer, item.Value); }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        { writer.WriteStartArray(); foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item); writer.WriteEndArray(); }
        else value.WriteTo(writer);
    }
    private static T? ReadDeclaration<T>(string? json) where T : class => json is null ? null :
        JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(DenJson.Options)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new JsonException("A saved execution declaration cannot be null.");
    private static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, DenJson.Options)));
    private static string RunInvariant(AgentRunSnapshot run) => Digest(new { run.AgentRunId, run.AgentId, run.DefinitionRevision, run.Objective,
        run.CallerId, run.SurfaceId, run.SpaceId, run.ProjectOrEntityId, run.SessionId, run.EndpointId, run.ModelId, run.ProviderId,
        capabilities = run.EffectiveCapabilities.Order(StringComparer.Ordinal).ToArray(), run.ContextSnapshot, run.BudgetLimits, run.ParentAgentRunId, run.ParentSubagentId });
    private bool Matches(Prepared prepared, BrokerRequest? request) => request is not null &&
        request.RequestId == prepared.Submission.RequestId && request.Caller == prepared.Submission.Caller && request.SessionId == prepared.Submission.SessionId &&
        request.Policy == prepared.Policy && _permissions.ResolveTrustedActionPolicy(request.Scope.TargetAppId, request.Scope.ActionName) == prepared.Policy &&
        Digest(request.Scope) == Digest(prepared.Submission.Scope) && request.Impact.ArgumentsDigest == prepared.Submission.Impact.ArgumentsDigest &&
        request.Impact.ResourceBinding is null && !request.Impact.IsUnknown;
    private static UnauthorizedAccessException Refused() => new("The original Home Agent authority is unavailable or changed.");
    private static AgentResult<T> Failure<T>() => AgentResult<T>.Failure(new(AgentFailureCode.PermissionDenied,
        "The exact original Home Agent context, run or step is not admitted.", "home.agent"));

    private sealed record PendingStepApproval(StepAuthority Authority, string RequestId, string ScopeBody, string ArgumentsDigest);
    private sealed record PendingCallAuthority(OllamaToolCall Call, string CallBody, OllamaToolCall DispatchCall, HomeAgentToolDemand Demand,
        string RequestId, string ScopeBody, string ArgumentsDigest)
    { internal object CommitAdmission { get; } = new(); }
    private sealed record Prepared(Preparation Preparation, HomePersonalDenSession Session, AgentDefinitionRecord Definition,
        string Objective, IReadOnlySet<string> Capabilities, HomePermissionRequestSubmission Submission, HomePermissionActionPolicy Policy);
    private sealed class Invocation(Prepared prepared, AgentInvocationContext context, CancellationTokenSource lifetime)
    {
        internal Prepared Prepared { get; } = prepared; internal AgentInvocationContext Context { get; } = context;
        internal CancellationTokenSource Lifetime { get; } = lifetime;
        internal string? RunId; internal string? AttemptId; internal string? RunInvariant; internal string? ModelIdentity; internal string? ModelKey;
        internal Guid ConversationId; internal StepAuthority? ActiveStep; internal IAgentExecutionStateStore? AuditRuns;
        internal bool HasPartialOwnedOutcome;
    }
    private sealed class StepAuthority(Invocation invocation, AgentExecutionStep step, long startedAt, long baselineCalls, TimeSpan baselineTime)
    {
        internal Invocation Invocation { get; } = invocation; internal AgentExecutionStep Step { get; } = step;
        internal object Token { get; } = new(); internal long StartedAt { get; } = startedAt;
        internal long BaselineCalls { get; } = baselineCalls; internal TimeSpan BaselineTime { get; } = baselineTime;
        internal string OriginalBody { get; } = Digest(step);
        internal long Calls; internal List<string> ToolRequests { get; } = [];
        internal List<(string OriginalRequestId, HomeExecutionOutcome ActualOutcome)> RecordedOutcomes { get; } = [];
        internal PendingCallAuthority? PendingCall;
    }
    // Audit-only access to one already-admitted canonical run on the retained SAME Store.
    // It cannot list definitions, write state, execute, or expose a reconstructed admission.
    private sealed class OriginalRunReadPolicy(string principal, string originalNamespace, string originalRun) : IDenAccessPolicy
    {
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(permission == DenPermission.Read && principalId == principal &&
                namespaceId == originalNamespace && objectId == originalRun);
        }
    }

    private sealed class ExecutionPolicy(HomeAgentExecutionAdmissions issuer, Invocation invocation) : IDenAccessPolicy
    {
        public async ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken cancellationToken = default)
        {
            if (permission is DenPermission.Read or DenPermission.Write)
                return await invocation.Prepared.Session.Den.AccessPolicy.IsAllowedAsync(principalId, namespaceId, objectId, permission, cancellationToken).ConfigureAwait(false);
            if (permission != DenPermission.Execute || principalId != invocation.Prepared.Session.Actor.ActorId ||
                namespaceId != invocation.Prepared.Preparation.Reference.NamespaceId || objectId != invocation.Prepared.Preparation.Reference.AgentId) return false;
            return await issuer.RunAsync(async token => { await issuer.RequireInvocationAsync(invocation, token).ConfigureAwait(false); return true; }, cancellationToken).ConfigureAwait(false);
        }
    }

    // Publish the SAME original asynchronous operation before releasing its start gate. Closing
    // cannot miss work between admission and the first awaited I/O. Only already-settled tasks
    // are pruned; shutdown snapshots every still-active original Task and awaits its cleanup.
    private sealed class Operation
    {
        internal Task Task { get; set; } = Task.CompletedTask;
        internal CancellationToken OwnedToken;
        internal CancellationToken Caller { get; init; }
    }
    private Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller, bool originalSettlement = false)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> original;
        lock (_admissionGate)
        {
            if (_closing) throw new ObjectDisposedException(nameof(HomeAgentExecutionAdmissions));
            foreach (var completed in _operations.Where(item => item.Value.Task.IsCompleted).Select(item => item.Key).ToArray())
                _operations.Remove(completed);
            var operation = new Operation { Caller = caller };
            original = RunOriginalOperationAsync(start.Task, operation, action, caller, originalSettlement);
            operation.Task = original;
            _operations.Add(Guid.NewGuid(), operation);
        }
        start.SetResult();
        return original;
    }
    private async Task<T> RunOriginalOperationAsync<T>(Task start, Operation operation,
        Func<CancellationToken, Task<T>> action, CancellationToken caller, bool originalSettlement)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        CancellationTokenSource? linked = null;
        bool entered = false;
        T? result = default;
        try
        {
            var ownedToken = CancellationToken.None;
            if (!originalSettlement)
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _hostLifetime.Token);
                ownedToken = linked.Token;
            }
            operation.OwnedToken = ownedToken;
            await _gate.WaitAsync(ownedToken).ConfigureAwait(false);
            entered = true;
            ownedToken.ThrowIfCancellationRequested();
            result = await action(ownedToken).ConfigureAwait(false);
        }
        catch (Exception error) { AddFailure(errors, error); }
        finally
        {
            if (entered) try { _gate.Release(); } catch (Exception error) { AddFailure(errors, error); }
            if (linked is not null) try { linked.Dispose(); } catch (Exception error) { AddFailure(errors, error); }
        }
        ThrowFailures(errors);
        return result!;
    }
    public ValueTask DisposeAsync()
    {
        lock (_admissionGate)
        {
            if (_closeTask is not null) return new(_closeTask);
            _closing = true;
            var originals = _operations.Values.Where(item => !item.Task.IsCompleted).ToArray();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _closeTask = CloseOriginalAsync(start.Task, originals);
            start.SetResult();
            return new(_closeTask);
        }
    }
    private async Task CloseOriginalAsync(Task start, IReadOnlyList<Operation> originals)
    {
        await start.ConfigureAwait(false);
        var errors = new List<Exception>();
        try { _hostLifetime.Cancel(); } catch (Exception error) { AddFailure(errors, error); }
        foreach (var original in originals)
        {
            try { await original.Task.ConfigureAwait(false); }
            catch (OperationCanceledException error) when (_hostLifetime.IsCancellationRequested &&
                !original.Caller.IsCancellationRequested && original.OwnedToken.IsCancellationRequested && error.CancellationToken == original.OwnedToken) { }
            catch (Exception error) { AddFailure(errors, error); }
        }
        // Work admission is closed and every original body/cleanup has settled before private
        // capabilities retire. No marker or replacement task stands in for the original drain.
        foreach (var invocation in _invocations.Values)
        {
            try { invocation.Lifetime.Cancel(); } catch (Exception error) { AddFailure(errors, error); }
            try { invocation.Lifetime.Dispose(); } catch (Exception error) { AddFailure(errors, error); }
        }
        foreach (var requestId in _unconfirmedRequests)
        {
            try { await AuditUnconfirmedAsync(requestId).ConfigureAwait(false); }
            catch (Exception error) { AddFailure(errors, error); }
        }
        _tokens.Clear(); _commitRoutes.Clear(); _steps.Clear(); _invocations.Clear(); _prepared.Clear(); _unconfirmedRequests.Clear(); _approvalPauses.Clear();
        try { _gate.Dispose(); } catch (Exception error) { AddFailure(errors, error); }
        try { _hostLifetime.Dispose(); } catch (Exception error) { AddFailure(errors, error); }
        lock (_admissionGate) _operations.Clear();
        ThrowFailures(errors);
    }
    private async Task AuditUnconfirmedAsync(string requestId)
    {
        var request = await _permissions.ReadRequestObservationAsync(requestId, CancellationToken.None).ConfigureAwait(false);
        if (request?.State != HomePermissionRequestState.Executing) return;
        var outcome = new HomeExecutionOutcome(HomePermissionRequestState.PartiallyCompleted,
            "HOME_AGENT_OUTCOME_UNCONFIRMED", "The original Agent admission retired without a confirmed owning outcome.", []);
        var recorded = await _permissions.RecordExecutionAsync(requestId, outcome, CancellationToken.None).ConfigureAwait(false);
        if (!recorded.Succeeded) throw new InvalidOperationException("The unconfirmed original Agent outcome could not be audited.");
    }
    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
    private static void AddFailure(List<Exception> failures, Exception error)
    {
        if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error);
    }
    private static void ThrowFailures(IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original Home Agent work and cleanup refused.", failures);
    }
}
