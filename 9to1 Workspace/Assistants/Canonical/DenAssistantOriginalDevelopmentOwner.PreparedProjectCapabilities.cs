using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;

namespace HavenOS.Apps.Assistants.Canonical;

/// <summary>Current observations of ONE SAME retained initial Home input. The
/// capability planner borrows that input; this port creates no input, READ review,
/// policy mode, Task/Run, or authority from a configured path/project preference.</summary>
public sealed partial class DenAssistantOriginalDevelopmentOwner : IAssistantOriginalPreparedProjectCapabilityContextOwner
{
    private readonly object _preparedCapabilityContextIssuer = new();
    private readonly ConditionalWeakTable<AssistantOriginalPreparedProjectCapabilityContext,
        PreparedCapabilityContextOriginal> _preparedCapabilityContexts = new();

    private sealed record PreparedCapabilityContextOriginal(AssistantCanonicalMembershipSource Membership,
        AssistantConversationBinding Binding, AssistantDefinitionSnapshot Definition, string DefinitionJson,
        ITaskRunColdOriginalProjectInput Input, InitialProjectInputOriginal InputCustody,
        AuthenticatedResourceActor Actor, DeveloperProjectReference Project, string Root);

    public Task<AssistantOriginalPreparedProjectCapabilityContext> ReadOriginalPreparedProjectCapabilityContextWithinSourceAsync(
        AssistantConversationBinding binding, AssistantDefinitionSnapshot actualDefinition,
        ITaskRunColdOriginalProjectInput sameActualInput, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token) => _originals.Admit(async () =>
        {
            var sources = new Sources(_originals, originalSynchronousScope, retainOriginalTask);
            var membership = RequireMembership(binding);
            var inputCustody = RequireOriginalProjectInput(sameActualInput);
            string? definitionJson = null;
            sources.Scope(() =>
            {
                inputCustody.DemandPreparedCapabilityAdmission();
                definitionJson = JsonSerializer.Serialize(actualDefinition.Configuration, Json);
            });
            var observed = await ObservePreparedCapabilityContextWithinSourceAsync(membership, binding,
                actualDefinition, definitionJson!, sameActualInput, inputCustody, sources, token).ConfigureAwait(false);
            AssistantOriginalPreparedProjectCapabilityContext? result = null;
            sources.Scope(() =>
            {
                inputCustody.DemandPreparedCapabilityAdmission();
                var original = new PreparedCapabilityContextOriginal(membership, binding, actualDefinition,
                    definitionJson!, sameActualInput, inputCustody, observed.Actor, observed.Project, observed.Root);
                result = new AssistantOriginalPreparedProjectCapabilityContext(_preparedCapabilityContextIssuer,
                    binding, actualDefinition, observed.Actor, sameActualInput, observed.Root, original);
                _preparedCapabilityContexts.Add(result, original);
            });
            return result ?? throw new InvalidOperationException("The actual prepared-input source did not publish its observation.");
        });

    public bool IsIssuedOriginalContext(AssistantOriginalPreparedProjectCapabilityContext sameActual) =>
        sameActual is not null && ReferenceEquals(sameActual.Issuer, _preparedCapabilityContextIssuer) &&
        _preparedCapabilityContexts.TryGetValue(sameActual, out var original) &&
        ReferenceEquals(sameActual.OriginalSourceState, original) &&
        ReferenceEquals(sameActual.Binding, original.Binding) &&
        ReferenceEquals(sameActual.Definition, original.Definition) &&
        ReferenceEquals(sameActual.OriginalProjectInput, original.Input) &&
        sameActual.Actor == original.Actor && sameActual.OriginalWorkspaceRoot == original.Root;

    public Task RevalidateOriginalContextWithinSourceAsync(AssistantOriginalPreparedProjectCapabilityContext sameActual,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        _originals.Admit(async () =>
        {
            if (!IsIssuedOriginalContext(sameActual) || !_preparedCapabilityContexts.TryGetValue(sameActual, out var original))
                throw new AssistantCommandRefusedException("This process source did not issue the SAME prepared project observation.");
            var sources = new Sources(_originals, originalSynchronousScope, retainOriginalTask);
            if (!ReferenceEquals(RequireMembership(original.Binding), original.Membership) ||
                !ReferenceEquals(RequireOriginalProjectInput(original.Input), original.InputCustody))
                throw new UnauthorizedAccessException("The SAME membership issuer and actual process input custody are required.");
            var observed = await ObservePreparedCapabilityContextWithinSourceAsync(original.Membership,
                original.Binding, original.Definition, original.DefinitionJson, original.Input,
                original.InputCustody, sources, token).ConfigureAwait(false);
            sources.Scope(() =>
            {
                original.InputCustody.DemandPreparedCapabilityAdmission();
                if (observed.Actor != original.Actor || observed.Project != original.Project || observed.Root != original.Root)
                    throw new AssistantCommandRefusedException("The actual current prepared project observation changed.");
            });
            return true;
        });

    private sealed record PreparedCapabilityCurrent(AuthenticatedResourceActor Actor,
        DeveloperProjectReference Project, string Root);

    private async Task<PreparedCapabilityCurrent> ObservePreparedCapabilityContextWithinSourceAsync(
        AssistantCanonicalMembershipSource membership, AssistantConversationBinding binding,
        AssistantDefinitionSnapshot definition, string definitionJson, ITaskRunColdOriginalProjectInput input,
        InitialProjectInputOriginal inputCustody, Sources sources, CancellationToken token)
    {
        sources.Scope(() => inputCustody.DemandPreparedCapabilityAdmission());
        var before = await sources.Take(() => membership.ValidateOriginalWithinSourceAsync(binding,
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        DeveloperProjectReference? reference = null;
        sources.Scope(() =>
        {
            DemandPreparedCapabilityMembership(before, definition, definitionJson, input, inputCustody);
            reference = before.OriginalProjectReference;
        });
        var taskActor = await ReadOriginalTaskActorWithinSourceAsync(sources, token).ConfigureAwait(false);
        if (await sources.Take(() => _tasks.GetByContextAsync(before.Conversation.Id, token)).ConfigureAwait(false) is not null)
            throw new AssistantCommandRefusedException("This original conversation already owns a canonical Task; use its existing controls.");
        var metadata = await ReadOriginalProjectContextWithinSourceAsync(before.Conversation.Id,
            before.Actor, sources, token).ConfigureAwait(false);
        sources.Scope(() => DemandPreparedCapabilityStorePair(metadata, input));

        // The genuine input issuer uses its existing fresh short READ/native/actor
        // validation, over the SAME original input references. No future cold input
        // is invented, and no new review or second input is acquired here.
        await sources.Take(() => _projectReads.ValidatePreparedOriginalProjectInputWithinSourceAsync(input,
            input.OriginalConversation, input.OriginalContainer, input.OriginalIdentity.OriginalProjectContextJson,
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);

        var after = await sources.Take(() => membership.ValidateOriginalWithinSourceAsync(binding,
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        var current = await ReadOriginalProjectContextWithinSourceAsync(before.Conversation.Id,
            before.Actor, sources, token).ConfigureAwait(false);
        var currentTaskActor = await ReadOriginalTaskActorWithinSourceAsync(sources, token).ConfigureAwait(false);
        sources.Scope(() =>
        {
            DemandPreparedCapabilityMembership(after, definition, definitionJson, input, inputCustody);
            DemandPreparedCapabilityStorePair(current, input);
            if (after.Actor != before.Actor || currentTaskActor != taskActor ||
                after.Conversation != before.Conversation || after.OriginalProjectReference != reference ||
                current.Context != metadata.Context || current.Container != metadata.Container)
                throw new AssistantCommandRefusedException("The actual project membership/store/actor changed during prepared-input planning.");
        });
        if (await sources.Take(() => _tasks.GetByContextAsync(before.Conversation.Id, token)).ConfigureAwait(false) is not null)
            throw new AssistantCommandRefusedException("A canonical Task entered this original conversation while planning was pending.");
        await sources.Take(() => _projectReads.ValidatePreparedOriginalProjectInputWithinSourceAsync(input,
            input.OriginalConversation, input.OriginalContainer, input.OriginalIdentity.OriginalProjectContextJson,
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        string? root = null;
        sources.Scope(() =>
        {
            inputCustody.DemandPreparedCapabilityAdmission();
            root = input.OriginalIdentity.CanonicalRoot;
            if (string.IsNullOrWhiteSpace(root))
                throw new UnauthorizedAccessException("The actual prepared Home input has no current original project root.");
        });
        return new(before.Actor, reference!, root!);
    }

    private void DemandPreparedCapabilityMembership(AssistantCanonicalMembershipSource.Observation observation,
        AssistantDefinitionSnapshot definition, string definitionJson, ITaskRunColdOriginalProjectInput input,
        InitialProjectInputOriginal inputCustody)
    {
        inputCustody.DemandPreparedCapabilityAdmission();
        if (!_projectReads.IsIssuedOriginalProjectInput(input) || !ReferenceEquals(RequireOriginalProjectInput(input), inputCustody) ||
            input.OriginalPreparation.IsCompletedSuccessfully != true ||
            observation.Definition.Identity != definition.Identity || observation.Definition.Revision != definition.Revision ||
            observation.Definition.Kind != definition.Kind ||
            JsonSerializer.Serialize(definition.Configuration, Json) != definitionJson ||
            JsonSerializer.Serialize(observation.Definition.Configuration, Json) != definitionJson ||
            !observation.Definition.Configuration.Enabled || observation.Definition.Configuration.Archived ||
            observation.Conversation != input.OriginalConversation || observation.Conversation.Mode != HavenMode.Tasks ||
            observation.Conversation.Kind != ConversationKind.Task || observation.Conversation.IsArchived ||
            observation.Conversation.ContainerId != input.OriginalContainer.Id ||
            input.OriginalContainer.Mode != HavenMode.Tasks || input.OriginalContainer.IsArchived ||
            observation.Actor != input.OriginalIdentity.OriginalHomeResourceActor ||
            observation.OriginalProjectReference is not { } reference ||
            input.OriginalIdentity.WorkspaceId != reference.WorkspaceId ||
            input.OriginalIdentity.WorkspaceRevision != reference.WorkspaceRevision ||
            input.OriginalIdentity.ProjectId != reference.ProjectId || input.OriginalIdentity.ProjectRevision != reference.ProjectRevision ||
            input.OriginalIdentity.RootId != reference.RootId || input.OriginalIdentity.RepositoryBindingId != reference.RepositoryBindingId)
            throw new AssistantCommandRefusedException("The SAME live initial Home input and current configured project membership are required.");
    }

    private static void DemandPreparedCapabilityStorePair(ProjectContextStoreObservation actual,
        ITaskRunColdOriginalProjectInput input)
    {
        if (actual.Context != input.OriginalConversation || actual.Container != input.OriginalContainer)
            throw new AssistantCommandRefusedException("The actual protected store READ no longer matches this original prepared project input pair.");
    }
}
