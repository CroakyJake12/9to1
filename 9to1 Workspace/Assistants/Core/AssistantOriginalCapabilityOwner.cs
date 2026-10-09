using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Core;

/// <summary>Configured discovery over the SAME catalogue and Chat planner. Saved
/// selections narrow observations; canonical Task/Home admission still owns effects.
/// This component borrows one genuine prepared project input and never closes it.</summary>
public sealed partial class AssistantOriginalCapabilityOwner : IAssistantOriginalCapabilityOwner, IAsyncDisposable
{
    private const int MaximumCatalogueEntries = 1024;
    private readonly HomePersonalDenFactory _home;
    private readonly IConversationRepository _conversations;
    private readonly CapabilityRegistryService _registry;
    private readonly ICapabilityOriginalRepositoryReadSource _catalogue;
    private readonly ChatSessionService _chat;
    private readonly IAssistantOriginalPreparedProjectCapabilityContextOwner? _contexts;
    private readonly ICapabilityOriginalInitializationProcessSource? _initialization;
    private readonly AssistantPresentationOriginals _originals = new();
    private readonly ConditionalWeakTable<AssistantOriginalCapabilitySelection, SelectionState> _issued = new();
    private Task? _close;
    private int _retired;

    private sealed class SelectionState
    {
        internal AssistantCanonicalMembershipSource Membership = null!;
        internal AssistantOriginalPreparedProjectCapabilityContext? Project;
        internal ModelDescriptor Model = null!;
        internal ModelRequestToolSelectionConstraints DispatchTools = null!;
        internal string Definition = "", PublicSelection = "", EffectiveSelection = "";
    }
    private sealed record EffectiveSelection(IReadOnlyList<CapabilityDefinition> Catalogue,
        ToolAvailabilityPlan Plan, IReadOnlyList<ActiveCapability> Active,
        IReadOnlyList<ToolCapability> Required, IReadOnlyList<AssistantCapabilityObservation> Observations,
        string Fingerprint, ModelRequestToolSelectionConstraints DispatchTools);

    public AssistantOriginalCapabilityOwner(HomePersonalDenFactory sameHome,
        IConversationRepository sameConversations, CapabilityRegistryService sameRegistry,
        ChatSessionService sameChat, ICapabilityOriginalRepositoryReadSource sameCatalogue,
        IAssistantOriginalPreparedProjectCapabilityContextOwner? sameContexts = null,
        ICapabilityOriginalInitializationProcessSource? sameInitialization = null)
    {
        _home = sameHome ?? throw new ArgumentNullException(nameof(sameHome));
        _conversations = sameConversations ?? throw new ArgumentNullException(nameof(sameConversations));
        _registry = sameRegistry ?? throw new ArgumentNullException(nameof(sameRegistry));
        _catalogue = sameCatalogue ?? throw new ArgumentNullException(nameof(sameCatalogue));
        _chat = sameChat ?? throw new ArgumentNullException(nameof(sameChat)); _contexts = sameContexts;
        _initialization = sameInitialization;
    }
    public HomePersonalDenFactory OriginalHomeDenFactory => _home;
    public IConversationRepository OriginalConversations => _conversations;
    public CapabilityRegistryService OriginalRegistry => _registry;
    public ICapabilityOriginalRepositoryReadSource OriginalCatalogueReadSource => _catalogue;
    public ChatSessionService OriginalChat => _chat;
    public IAssistantOriginalPreparedProjectCapabilityContextOwner? OriginalPreparedProjectContexts => _contexts;
    public ICapabilityOriginalInitializationProcessSource? OriginalInitializationSource => _initialization;
    public bool HasOriginalComposition(HomePersonalDenFactory home, IConversationRepository conversations,
        CapabilityRegistryService registry, ChatSessionService chat,
        ICapabilityOriginalRepositoryReadSource catalogue,
        IAssistantOriginalPreparedProjectCapabilityContextOwner? contexts) =>
        ReferenceEquals(_home, home) && ReferenceEquals(_conversations, conversations) &&
        ReferenceEquals(_registry, registry) && ReferenceEquals(_chat, chat) && ReferenceEquals(_catalogue, catalogue) &&
        ReferenceEquals(_contexts, contexts);
    public Task? OriginalClose => Volatile.Read(ref _close);
    public void RequestOriginalRetirement()
    {
        Interlocked.Exchange(ref _retired, 1);
        RetireOriginalConfigurationInitializationDeliveries();
        _originals.RequestRetirement();
    }
    public void DemandExternalOriginalRetirementJoin() => _originals.DemandExternalJoin();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin(); RequestOriginalRetirement();
        var actual = _originals.CloseAndDrainAsync(); Interlocked.CompareExchange(ref _close, actual, null); return _close!;
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    public bool IsIssuedOriginalSelection(AssistantOriginalCapabilitySelection selected) =>
        Volatile.Read(ref _retired) == 0 && selected is not null && ReferenceEquals(selected.Issuer, this) &&
        _issued.TryGetValue(selected, out var state) && ReferenceEquals(selected.OriginalContext, state);

    public ModelRequestToolSelectionConstraints GetOriginalDispatchToolConstraints(AssistantOriginalCapabilitySelection selected) =>
        IsIssuedOriginalSelection(selected) && _issued.TryGetValue(selected, out var state)
            ? state.DispatchTools : throw new UnauthorizedAccessException("The SAME live capability owner did not issue this selection.");

    public Task<AssistantOriginalCapabilitySelection> ReadOriginalConfiguredCapabilitiesWithinSourceAsync(
        AssistantConversationBinding binding, AssistantDefinitionSnapshot definition, ModelDescriptor selectedModel,
        Action<Action> scope, Action<Task> retain, CancellationToken token,
        ITaskRunColdOriginalProjectInput? projectInput = null) => _originals.Admit(async () =>
    {
        var membership = Invoke(scope, () => RequireMembership(binding, definition));
        var current = await Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        Invoke(scope, () => { DemandDefinition(current.Definition, definition); return true; });
        var model = Invoke(scope, () => selectedModel with { Capabilities = selectedModel.Capabilities.ToFrozenSet() });
        AssistantOriginalPreparedProjectCapabilityContext? context = null;
        if (projectInput is not null)
        {
            var owner = _contexts ?? throw new AssistantCommandRefusedException("The current prepared project capability context source is unavailable.");
            context = await Read(() => owner.ReadOriginalPreparedProjectCapabilityContextWithinSourceAsync(binding,
                current.Definition, projectInput, body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
            Invoke(scope, () => { DemandProjectContext(context, binding, current, projectInput); return true; });
        }
        var effective = await DiscoverAsync(current, context, model, scope, retain, token).ConfigureAwait(false);
        await DemandFreshAsync(membership, binding, current.Definition, current.Actor,
            context, projectInput, scope, retain, token).ConfigureAwait(false);
        var state = new SelectionState { Membership = membership, Project = context, Model = model,
            Definition = DefinitionFingerprint(current.Definition), EffectiveSelection = effective.Fingerprint,
            DispatchTools = effective.DispatchTools };
        var actual = new AssistantOriginalCapabilitySelection(this, binding, current.Definition, current.Actor,
            effective.Catalogue, effective.Plan, effective.Active, effective.Required, effective.Observations,
            PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask, context?.OriginalWorkspaceRoot, projectInput, state);
        state.PublicSelection = Invoke(scope, () => PublicFingerprint(actual));
        Invoke(scope, () => { _issued.Add(actual, state); return true; });
        return actual;
    });

    public Task RevalidateOriginalSelectionWithinSourceAsync(AssistantOriginalCapabilitySelection selection,
        Action<Action> scope, Action<Task> retain, CancellationToken token,
        ITaskRunColdOriginalProjectInput? projectInput = null) => _originals.Admit(async () =>
    {
        var state = Invoke(scope, () => IsIssuedOriginalSelection(selection) && _issued.TryGetValue(selection, out var owned)
            ? owned : throw new AssistantCommandRefusedException("The configured capability source did not issue this live selection."));
        Invoke(scope, () =>
        {
            if (!ReferenceEquals(selection.OriginalProjectInput, projectInput) ||
                state.PublicSelection != PublicFingerprint(selection))
                throw new UnauthorizedAccessException("The actual issued selection or borrowed project input changed.");
            return true;
        });
        await DemandFreshAsync(state.Membership, selection.Binding, selection.Definition,
            selection.Actor, state.Project, projectInput, scope, retain, token).ConfigureAwait(false);
        var current = await Read(() => state.Membership.ValidateOriginalWithinSourceAsync(selection.Binding,
            body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        var fresh = await DiscoverAsync(current, state.Project, state.Model, scope, retain, token).ConfigureAwait(false);
        await DemandFreshAsync(state.Membership, selection.Binding, selection.Definition,
            selection.Actor, state.Project, projectInput, scope, retain, token).ConfigureAwait(false);
        Invoke(scope, () =>
        {
            if (state.Definition != DefinitionFingerprint(current.Definition) ||
                fresh.Fingerprint != state.EffectiveSelection || PublicFingerprint(selection) != state.PublicSelection)
                throw new AssistantCommandRefusedException("The configured catalogue, model support or current project context changed. Refresh before dispatch.");
            return true;
        });
        return true;
    });

    private AssistantCanonicalMembershipSource RequireMembership(AssistantConversationBinding binding,
        AssistantDefinitionSnapshot definition)
    {
        var actual = AssistantCanonicalMembershipSource.ObserveOriginalIssuer(binding);
        if (actual is null || !ReferenceEquals(actual.OriginalHomeDenFactory, _home) ||
            !ReferenceEquals(actual.OriginalConversations, _conversations))
            throw new AssistantCommandRefusedException("Reopen the conversation through this actual Home and canonical membership owner.");
        DemandDefinition(binding.Definition, definition); return actual;
    }
    private static string DefinitionFingerprint(AssistantDefinitionSnapshot definition) => JsonSerializer.Serialize(
        new { definition.Identity, definition.Revision, definition.Kind, definition.Configuration }, DenJson.Options);
    private static void DemandDefinition(AssistantDefinitionSnapshot current, AssistantDefinitionSnapshot expected)
    {
        if (DefinitionFingerprint(current) != DefinitionFingerprint(expected) || !current.Configuration.Enabled || current.Configuration.Archived)
            throw new AssistantCommandRefusedException("The current configured identity changed or is unavailable. Reopen it before planning tools.");
    }
    private void DemandProjectContext(AssistantOriginalPreparedProjectCapabilityContext context,
        AssistantConversationBinding binding, AssistantCanonicalMembershipSource.Observation current,
        ITaskRunColdOriginalProjectInput sameInput)
    {
        if (_contexts is null || !_contexts.IsIssuedOriginalContext(context) ||
            !ReferenceEquals(context.Binding, binding) || !ReferenceEquals(context.OriginalProjectInput, sameInput) ||
            context.Actor != current.Actor || DefinitionFingerprint(context.Definition) != DefinitionFingerprint(current.Definition) ||
            string.IsNullOrWhiteSpace(context.OriginalWorkspaceRoot) ||
            context.OriginalWorkspaceRoot != sameInput.OriginalIdentity.CanonicalRoot ||
            sameInput.OriginalConversation != current.Conversation ||
            sameInput.OriginalContainer.Id != current.Conversation.ContainerId ||
            !sameInput.OriginalPreparation.IsCompletedSuccessfully)
            throw new UnauthorizedAccessException("The SAME current Home/membership/project source did not issue this exact borrowed context.");
    }
    private async Task DemandFreshAsync(AssistantCanonicalMembershipSource membership, AssistantConversationBinding binding,
        AssistantDefinitionSnapshot definition, AuthenticatedResourceActor actor,
        AssistantOriginalPreparedProjectCapabilityContext? context, ITaskRunColdOriginalProjectInput? sameInput,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var current = await Read(() => membership.ValidateOriginalWithinSourceAsync(binding,
            body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        Invoke(scope, () =>
        {
            DemandDefinition(current.Definition, definition);
            if (current.Actor != actor) throw new AssistantCommandRefusedException("The actual current Home actor changed during discovery.");
            if (context is not null) DemandProjectContext(context, binding, current, sameInput!);
            else if (sameInput is not null) throw new UnauthorizedAccessException("No genuine prepared project context was observed.");
            return true;
        });
        if (context is not null)
            await Read(() => _contexts!.RevalidateOriginalContextWithinSourceAsync(context,
                body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
    }

    private async Task<EffectiveSelection> DiscoverAsync(AssistantCanonicalMembershipSource.Observation current,
        AssistantOriginalPreparedProjectCapabilityContext? context, ModelDescriptor model,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var platform = Invoke(scope, () => OperatingSystem.IsWindows() ? CapabilityPlatform.Windows
            : OperatingSystem.IsAndroid() ? CapabilityPlatform.Android
            : OperatingSystem.IsLinux() ? CapabilityPlatform.Linux
            : throw new AssistantCommandRefusedException("This host has no maintained capability catalogue platform."));
        var source = await Read(() => _registry.DiscoverWithinOriginalSourceAsync(_catalogue, current.Actor,
            platform, body => Run(scope, body), raw => Retain(retain, raw), token), scope, retain).ConfigureAwait(false);
        return Invoke<EffectiveSelection>(scope, () =>
        {
            if (!_registry.IsIssuedOriginalCatalogue(source) || source.Actor != current.Actor)
                throw new UnauthorizedAccessException("The SAME protected registry/current actor did not issue this catalogue.");
            if (source.Definitions.Count > MaximumCatalogueEntries)
                throw new AssistantCommandRefusedException("The current capability catalogue exceeds its bounded presentation size.");
            var catalogue = Array.AsReadOnly(source.Definitions.ToArray());
            var configuration = current.Definition.Configuration;
            var requested = new List<CapabilityDefinition>(); var observations = new List<AssistantCapabilityObservation>();
            foreach (var id in configuration.ToolIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var matches = catalogue.Where(value => value.Key.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                    Guid.TryParse(id, out var actualId) && value.Id == actualId).ToArray();
                AddRequested(id, matches, requested, observations);
            }
            foreach (var id in configuration.ConnectedAppIds.Distinct(StringComparer.OrdinalIgnoreCase))
                AddRequested(id, catalogue.Where(value => !value.IsBuiltIn &&
                    (value.Key.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                     Guid.TryParse(id, out var actualId) && value.Id == actualId ||
                     value.ProviderId.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                     value.OwnerAppKey.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray(), requested, observations);
            if (configuration.ComputerUseRequested && !requested.Any(value => value.Key == "computer-device-use"))
                AddRequested("computer-device-use", catalogue.Where(value => value.Key == "computer-device-use").ToArray(), requested, observations);
            var definitions = requested.DistinctBy(value => value.Id).ToArray();
            var activeCandidates = definitions.Where(value => value.IsEnabled && value.IsAgentUsable)
                .Select(ActiveCapability.FromDefinition).ToArray();
            // Ask remains the actual requested policy. No catalogue preference, OS support,
            // imported-store READ or model capability becomes approval for effects.
            var original = _chat.GetToolAvailability(current.Conversation.Mode, context?.OriginalWorkspaceRoot,
                activeCandidates, PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask);
            var restricted = original.RestrictToModel(model);
            var active = new List<ActiveCapability>();
            foreach (var definition in definitions)
            {
                var state = AssistantSupportState.Available; string reason;
                if (!definition.IsAgentUsable || definition.Availability is CapabilityAvailability.Unsupported or CapabilityAvailability.Restricted)
                { state = AssistantSupportState.Unsupported; reason = "The current catalogue does not expose this capability for configured execution."; }
                else if (definition.Availability == CapabilityAvailability.DependencyRequired)
                { state = AssistantSupportState.NotConfigured; reason = "The current owning catalogue requires its actual dependency."; }
                else if (definition.Key == "computer-device-use")
                { state = AssistantSupportState.NotConfigured; reason = "An explicit genuine Computer target/context is not composed by this prepared project source."; }
                else if (!original.HasOriginalAvailableRuntimeCapability(definition.Key))
                { state = AssistantSupportState.NotConfigured; reason = "The SAME current Chat plan exposes no runtime for this selection under the observed Ask context."; }
                else if (!model.Supports(ToolCapability.Tools) || restricted.Definitions.Count == 0)
                { state = AssistantSupportState.Unsupported; reason = "The actual selected model lacks the current concrete tool definitions."; }
                else if (ToolAvailabilityPlanner.Default.GetOriginalConfiguredDefinitions(restricted,
                    [ActiveCapability.FromDefinition(definition)]).Count == 0)
                { state = AssistantSupportState.Unsupported; reason = "The maintained runtime exposes no exact configured definition subset for this selection."; }
                else
                {
                    active.Add(ActiveCapability.FromDefinition(definition));
                    state = definition.Availability == CapabilityAvailability.PermissionRequired
                        ? AssistantSupportState.RequiresAuthorization : AssistantSupportState.Available;
                    reason = state == AssistantSupportState.RequiresAuthorization
                        ? "The current runtime is observed; canonical Task/Home approval remains required for each effect."
                        : "The actual current catalogue, model and prepared source context expose this runtime.";
                }
                observations.Add(new(definition.Key, state, reason));
            }
            var observed = Array.AsReadOnly(observations.ToArray());
            var selected = Array.AsReadOnly(active.ToArray());
            var dispatch = new ModelRequestToolSelectionConstraints(ToolAvailabilityPlanner.Default
                .GetOriginalConfiguredDefinitions(restricted, selected).Select(value => value.Name).ToArray());
            IReadOnlyList<ToolCapability> required = selected.Count == 0 ? [] : Array.AsReadOnly(new[] { ToolCapability.Tools });
            var fingerprint = JsonSerializer.Serialize(new
            {
                Selected = definitions.Select(value => value with { UpdatedAt = DateTimeOffset.UnixEpoch }).OrderBy(value => value.Id).ToArray(),
                Active = selected, Required = required, Observations = observed,
                Definitions = restricted.Definitions, dispatch.AllowedToolNames, WorkspaceRoot = context?.OriginalWorkspaceRoot
            }, DenJson.Options);
            return new(catalogue, restricted, selected, required, observed, fingerprint, dispatch);
        });
    }
    private static void AddRequested(string savedId, CapabilityDefinition[] matches,
        List<CapabilityDefinition> selected, List<AssistantCapabilityObservation> observations)
    {
        if (matches.Length == 0)
            observations.Add(new(savedId, AssistantSupportState.NotConfigured, "The current owning catalogue did not observe this saved selection."));
        else selected.AddRange(matches);
    }
    private static string PublicFingerprint(AssistantOriginalCapabilitySelection selection) => JsonSerializer.Serialize(new
    {
        Definition = DefinitionFingerprint(selection.Definition), selection.Actor, selection.ActiveCapabilities,
        selection.RequiredModelCapabilities, selection.Observations, selection.FilePermission,
        selection.CommandPermission, selection.BrowserPermission, selection.OriginalWorkspaceRoot,
        Definitions = selection.OriginalPlan.Definitions
    }, DenJson.Options);

    private void Retain(Action<Task> caller, Task actual) { _originals.Retain(actual); caller(actual); }
    private void Run(Action<Action> caller, Action body) => Invoke(caller, () => { body(); return true; });
    private T Invoke<T>(Action<Action> caller, Func<T> body) => _originals.Invoke(() =>
    {
        var used = 0; var active = 1; var thread = Environment.CurrentManagedThreadId;
        T result = default!; var failures = new List<Exception>();
        try
        {
            caller(() =>
            {
                if (Volatile.Read(ref active) == 0 || thread != Environment.CurrentManagedThreadId ||
                    Interlocked.CompareExchange(ref used, 1, 0) != 0)
                {
                    var error = new InvalidOperationException("The actual capability source callback expired, repeated or changed threads.");
                    Add(failures, error); throw error;
                }
                try { result = body(); } catch (Exception error) { Add(failures, error); throw; }
            });
            if (Volatile.Read(ref used) != 1) Add(failures, new InvalidOperationException("The actual capability source callback was not invoked."));
        }
        catch (Exception error) { Add(failures, error); }
        finally { Volatile.Write(ref active, 0); }
        Throw(failures); return result;
    });
    private async Task<T> Read<T>(Func<Task<T>> factory, Action<Action> scope, Action<Task> retain)
    {
        Task<T>? actual = null; T value = default!; var errors = new List<Exception>();
        try { Invoke(scope, () => { actual = factory() ?? throw new InvalidOperationException("No actual capability Task returned."); Retain(retain, actual); return true; }); }
        catch (Exception error) { Add(errors, error); }
        if (actual is not null)
        {
            try { value = await actual.ConfigureAwait(false); }
            catch (Exception observed)
            { foreach (var cause in actual.Exception?.InnerExceptions.ToArray() ?? [observed]) Add(errors, cause); }
        }
        else if (errors.Count == 0) Add(errors, new InvalidOperationException("The actual capability source returned no Task."));
        Throw(errors); return value;
    }
    private async Task Read(Func<Task> factory, Action<Action> scope, Action<Task> retain)
    {
        Task? actual = null; var errors = new List<Exception>();
        try { Invoke(scope, () => { actual = factory() ?? throw new InvalidOperationException("No actual capability Task returned."); Retain(retain, actual); return true; }); }
        catch (Exception error) { Add(errors, error); }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); }
            catch (Exception observed)
            { foreach (var cause in actual.Exception?.InnerExceptions.ToArray() ?? [observed]) Add(errors, cause); }
        else if (errors.Count == 0) Add(errors, new InvalidOperationException("The actual capability source returned no Task."));
        Throw(errors);
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error); }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original capability source/callback custody failed.", errors);
    }
}
