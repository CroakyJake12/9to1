using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Canonical;

/// <summary>The bridge's privately issued canonical membership, independently read
/// through the caller's actual source custody after presentation retirement. It
/// creates no Home grant, conversation, Task or project and owns no borrowed service.</summary>
internal sealed partial class AssistantCanonicalMembershipSource
{
    internal const string DefinitionKey = "assistants.definition.v1";
    internal const string SessionKey = "assistants.membership.v1";
    internal const string PersonalNamespace = "personal";
    internal const string CompatibleCheckpointKey = "assistants.compatibleCheckpoint.v1";
    internal sealed record CompatibleCheckpointMetadata(int Schema, long DefinitionRevision,
        Guid LastCommandOperation, string Reason);
    private readonly object _issuer = new();
    private static readonly ConditionalWeakTable<object, AssistantCanonicalMembershipSource> OriginalIssuers = new();
    private readonly object _actorGate = new();
    private readonly HomePersonalDenFactory _home;
    private readonly IConversationRepository _conversations;
    private readonly IReadOnlyList<AssistantCapabilityObservation> _capabilities;
    private AuthenticatedResourceActor? _actor;
    internal sealed record DefinitionMetadata(int Schema, ConfiguredIdentityKind Kind,
        AssistantConfiguration Configuration, Guid CreationOperation);
    internal sealed record MembershipMetadata(int Schema, string DefinitionId, Guid CreationOperation,
        AssistantConversationKind Kind, string Publication, Conversation OriginalConversation,
        DeveloperProjectReference? OriginalProjectReference = null,
        Guid? OriginalStudioConversationId = null, Guid? OriginalStudioContainerId = null,
        ContainerDefinition? OriginalTaskContainer = null, Conversation? OriginalStudioConversation = null,
        ContainerDefinition? OriginalStudioContainer = null, string? OriginalCreationIntentSha256 = null,
        ResourceStoreIdentity? OriginalStoreIdentity = null);
    internal sealed record Observation(AuthenticatedResourceActor Actor,
        AssistantDefinitionSnapshot Definition, Conversation Conversation, AssistantConversationBinding Binding,
        DeveloperProjectReference? OriginalProjectReference);

    internal AssistantCanonicalMembershipSource(HomePersonalDenFactory home, IConversationRepository conversations,
        IReadOnlyList<AssistantCapabilityObservation> capabilities)
    { _home = home; _conversations = conversations; _capabilities = capabilities.ToArray(); OriginalIssuers.Add(_issuer, this); }

    internal static AssistantCanonicalMembershipSource? ObserveOriginalIssuer(AssistantConversationBinding binding) =>
        binding is not null && OriginalIssuers.TryGetValue(binding.Issuer, out var actual) ? actual : null;

    internal HomePersonalDenFactory OriginalHomeDenFactory => _home;
    internal IConversationRepository OriginalConversations => _conversations;
    internal AuthenticatedResourceActor? OriginalActor { get { lock (_actorGate) return _actor; } }
    internal bool IsIssuedOriginalBinding(AssistantConversationBinding binding) =>
        binding is not null && ReferenceEquals(binding.Issuer, _issuer);

    internal async Task<HomePersonalDenSession> OpenHomeWithinSourceAsync(Action<Action> scope,
        Action<Task> retain, CancellationToken token)
    {
        var home = await Take(() => _home.OpenWithinOriginalSourceAsync(scope, retain, token), scope, retain).ConfigureAwait(false);
        lock (_actorGate)
        {
            _actor ??= home.Actor;
            if (_actor != home.Actor)
                throw new AssistantCommandRefusedException("The current Home actor changed; reopen the presentation.");
        }
        return home;
    }

    internal async Task<AgentDefinitionRecord> DefinitionWithinSourceAsync(HomePersonalDenSession home,
        AssistantIdentity identity, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        DemandIdentity(home, identity);
        var row = await Take(() => home.Den.GetAsync<AgentDefinitionRecord>(identity.NamespaceId,
            identity.DefinitionId, token), scope, retain).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The configured definition is unavailable.");
        if (!IsConfigured(row))
            throw new AssistantCommandRefusedException("This legacy definition needs explicit semantic migration.");
        return row;
    }

    internal async Task<AssistantConversationBinding> BindWithinSourceAsync(HomePersonalDenSession home,
        AssistantIdentity identity, SessionRecord session, Action<Action> scope, Action<Task> retain,
        CancellationToken token)
    {
        var definition = await DefinitionWithinSourceAsync(home, identity, scope, retain, token).ConfigureAwait(false);
        var metadata = ReadMetadata<MembershipMetadata>(session, SessionKey);
        if (metadata is not { Schema: 1, Publication: "ready" } || metadata.DefinitionId != identity.DefinitionId ||
            !Guid.TryParse(session.ConversationId, out var id) || id != metadata.OriginalConversation.Id ||
            session.Id != SessionId(id) || session.NamespaceId != identity.NamespaceId)
            throw new AssistantCommandRefusedException("The Den session does not acknowledge this configured identity's conversation membership.");
        var actual = await Take(() => _conversations.GetAsync(id, token), scope, retain).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The persisted canonical conversation is unavailable.");
        if (!MatchesMembership(actual, metadata))
            throw new AssistantCommandRefusedException("The actual conversation scope no longer matches its Den membership.");
        await OpenHomeWithinSourceAsync(scope, retain, token).ConfigureAwait(false);
        return new(_issuer, Snapshot(home, definition), session.Id, session.Revision, actual);
    }

    internal async Task<Observation> ValidateOriginalWithinSourceAsync(AssistantConversationBinding binding,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (!IsIssuedOriginalBinding(binding))
            throw new AssistantCommandRefusedException("Only this source's privately issued conversation binding is accepted.");
        var home = await OpenHomeWithinSourceAsync(scope, retain, token).ConfigureAwait(false);
        DemandIdentity(home, binding.Definition.Identity);
        var session = await Take(() => home.Den.GetAsync<SessionRecord>(binding.Definition.Identity.NamespaceId,
            binding.DenSessionId, token), scope, retain).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The original Den membership is unavailable.");
        var current = await BindWithinSourceAsync(home, binding.Definition.Identity, session, scope, retain, token).ConfigureAwait(false);
        if (current.Definition.Revision != binding.Definition.Revision || session.Revision != binding.DenSessionRevision ||
            current.Conversation.Id != binding.Conversation.Id)
            throw new AssistantCommandRefusedException("The configured identity or membership changed; reopen the conversation.");
        var metadata = ReadMetadata<MembershipMetadata>(session, SessionKey)!;
        return new(home.Actor, current.Definition, current.Conversation, current, metadata.OriginalProjectReference);
    }

    internal static void DemandIdentity(HomePersonalDenSession home, AssistantIdentity identity)
    {
        if (identity.DenId != home.DenId || identity.NamespaceId != PersonalNamespace || string.IsNullOrWhiteSpace(identity.DefinitionId))
            throw new AssistantCommandRefusedException("The definition is outside this current Home Den.");
    }
    internal static bool IsConfigured(AgentDefinitionRecord row)
    {
        var metadata = ReadMetadata<DefinitionMetadata>(row, DefinitionKey);
        return metadata is { Schema: 1, Configuration: not null } && Enum.IsDefined(metadata.Kind) &&
            !string.IsNullOrWhiteSpace(metadata.Configuration.Name) && metadata.Configuration.Model is not null &&
            metadata.Configuration.Limits is not null && metadata.Configuration.Proactive is not null;
    }
    internal AssistantDefinitionSnapshot Snapshot(HomePersonalDenSession home, AgentDefinitionRecord row)
    {
        var metadata = ReadMetadata<DefinitionMetadata>(row, DefinitionKey)
            ?? throw new AssistantCommandRefusedException("Configured metadata is unavailable.");
        return new(Identity(home, row), row.Revision, metadata.Kind, metadata.Configuration, _capabilities);
    }
    internal static AssistantIdentity Identity(HomePersonalDenSession home, DenRecord row) => new(home.DenId, row.NamespaceId, row.Id);
    internal static T? ReadMetadata<T>(DenRecord row, string key) where T : class =>
        row.ExtensionData?.TryGetValue(key, out var json) == true ? json.Deserialize<T>(DenJson.Options) : null;
    internal static string SessionId(Guid conversationId) => $"assistant-session-{conversationId:D}";
    internal static bool MatchesMembership(Conversation conversation, MembershipMetadata metadata) =>
        conversation.Id == metadata.OriginalConversation.Id && !conversation.IsTemporary &&
        conversation.SpaceId is null && conversation.LessonId is null &&
        // Creation stores the actual container once; later metadata/configuration
        // or an externally amended row cannot promote a container-free membership.
        conversation.ContainerId == metadata.OriginalConversation.ContainerId &&
        (metadata.Kind == AssistantConversationKind.Chat ? conversation.Mode == HavenMode.Chat && conversation.Kind == ConversationKind.Chat
            : conversation.Mode == HavenMode.Tasks && conversation.Kind == ConversationKind.Task);

    private static async Task<T> Take<T>(Func<Task<T>> factory, Action<Action> scope, Action<Task> retain)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        Task<T>? actual = null; Exception? protocolFailure = null; Exception? bodyFailure = null; Exception? scopeFailure = null;
        var phase = 1; var used = 0; var thread = Environment.CurrentManagedThreadId;
        try
        {
            scope(() =>
            {
                if (Volatile.Read(ref phase) == 0 || thread != Environment.CurrentManagedThreadId ||
                    Interlocked.CompareExchange(ref used, 1, 0) != 0)
                {
                    var refusal = new InvalidOperationException("The actual membership source callback expired, repeated or moved threads.");
                    Interlocked.CompareExchange(ref protocolFailure, refusal, null);
                    throw refusal;
                }
                try
                {
                    actual = factory() ?? throw new InvalidOperationException("No actual canonical source Task was returned.");
                    retain(actual); // SAME raw task before any caller post-publication guard.
                }
                catch (Exception failure) { bodyFailure = failure; throw; }
            });
            if (Volatile.Read(ref used) != 1 && bodyFailure is null && protocolFailure is null)
                throw new InvalidOperationException("The membership source callback was not executed.");
        }
        catch (Exception failure) { scopeFailure = failure; }
        finally { Volatile.Write(ref phase, 0); }

        var failures = new List<Exception>();
        void Add(Exception? cause) { if (cause is not null && !failures.Any(value => ReferenceEquals(value, cause))) failures.Add(cause); }
        Add(bodyFailure); Add(protocolFailure); Add(scopeFailure);
        if (failures.Count == 0)
        {
            if (actual is null) throw new InvalidOperationException("No actual canonical source Task was acquired.");
            try { return await actual.ConfigureAwait(false); }
            catch (Exception observed)
            {
                if (actual.IsCanceled) ExceptionDispatchInfo.Capture(observed).Throw(); // Actual raw cancellation stays separately retained.
                foreach (var cause in actual.Exception?.InnerExceptions.ToArray() ?? [observed]) Add(cause);
            }
        }
        else if (actual is not null)
        {
            // A rejecting callback/retainer cannot abandon work it already acquired.
            // Join the SAME actual task outside the physical callback before reporting
            // every body/protocol/scope cause, even if the caller suppressed an earlier one.
            try { await actual.ConfigureAwait(false); }
            catch (Exception observed)
            { foreach (var cause in actual.Exception?.InnerExceptions.ToArray() ?? [observed]) Add(cause); }
        }
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("Original membership source and callback custody failed.", failures);
    }
}
