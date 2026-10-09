using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Configured route/credential presence and actual per-request personal-app context admission.
/// This does not reserve quota, calculate prices, attest upstream credential validity, grant Home
/// rights, or prove exclusion against concurrent policy writers. Every raw remote frame must use
/// the context port; an attempt's general configured-cloud lease alone is insufficient.
/// Capture belongs only to the trusted Chat producer after its existing source authorization.
/// Foreign/shared context requires its real domain owner; this adapter cannot grant it from text.
/// </summary>
public sealed partial class TaskRunConfiguredCloudAdmissionSource : ITaskRunCloudAdmissionSource,
    ITaskRunProviderContextCapture, ITaskRunProviderContextAuthority
{
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly IProviderConfigurationStore _configurations;
    private readonly IProviderSecretStore _secrets;
    private readonly IPrivacyPreferenceStore _privacy;
    private readonly IConversationRepository _conversations;
    private readonly IKnowledgeLibrary? _knowledge;
    private readonly ITaskRunCloudUsePermissionSource? _cloudUse;
    private readonly Func<Guid, Guid, Guid, CancellationToken, Task<TaskRunAttemptAdmission?>> _issued;
    private readonly ConditionalWeakTable<object, Capture> _captures = new();
    private readonly object _sync = new();

    private sealed record KnowledgeSelection(Guid Id, string Fingerprint, bool Background);
    private sealed record Capture(TaskExecutionOwnerBinding Owner, ProviderExecutionContext Context,
        string PayloadFingerprint, string ConversationFingerprint, string DomainContextFingerprint, bool TemporaryConversation,
        FrozenSet<string> UnsupportedRemoteContext, FrozenDictionary<Guid, string> History,
        KnowledgeSelection[] Knowledge, string? BackgroundAppId, string? BackgroundProjectId,
        FrozenSet<string> BackgroundScopes, ChatOriginalAttachmentInvocation? AttachmentInvocation);

    /// <param name="originalIssuedAttemptLookup">Lazy SAME canonical coordinator lookup. Do not
    /// construct a second coordinator or resolve it while constructing this source/authority.</param>
    public TaskRunConfiguredCloudAdmissionSource(IAuthenticatedResourceActorSource actors,
        IProviderConfigurationStore configurations, IProviderSecretStore secrets,
        IPrivacyPreferenceStore privacy, IConversationRepository conversations,
        Func<Guid, Guid, Guid, CancellationToken, Task<TaskRunAttemptAdmission?>> originalIssuedAttemptLookup,
        IKnowledgeLibrary? knowledge = null, ITaskRunCloudUsePermissionSource? cloudUsePermission = null)
    {
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _issued = originalIssuedAttemptLookup ?? throw new ArgumentNullException(nameof(originalIssuedAttemptLookup));
        _knowledge = knowledge;
        _cloudUse = cloudUsePermission;
    }

    public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot snapshot, OllamaChatRequest request,
        TaskRunContextInventory inventory, CancellationToken token) =>
        CaptureAsync(snapshot, request, request.ExecutionContext, Payload(request), inventory, token);

    public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot snapshot, OllamaToolRequest request,
        TaskRunContextInventory inventory, CancellationToken token) =>
        CaptureAsync(snapshot, request, request.ExecutionContext, Payload(request), inventory, token);

    private async ValueTask CaptureAsync(TaskExecutionSnapshot snapshot, object originalRequest,
        ProviderExecutionContext? originalContext, string payload, TaskRunContextInventory inventory,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot); ArgumentNullException.ThrowIfNull(inventory);
        var owner = snapshot.OwnerBinding ?? throw new UnauthorizedAccessException("Actual task owner is unavailable.");
        DemandSnapshot(snapshot, owner);
        DemandContext(originalContext, snapshot);
        if (inventory.OriginalConversation.Id != owner.ContextId || inventory.OriginalHistory.Count > 4096 ||
            inventory.SelectedPersistentMemory.Count > MemoryInjection.MaximumRecords ||
            inventory.SelectedBackgroundLearning.Count > 128)
            throw new UnauthorizedAccessException("The actual context selection does not bind this task or exceeds its finite capture bound.");
        if (inventory.OriginalHistory.Any(message => message.ConversationId != owner.ContextId) ||
            inventory.OriginalHistory.Select(message => message.Id).Distinct().Count() != inventory.OriginalHistory.Count)
            throw new UnauthorizedAccessException("The selected history belongs to another conversation or repeats an identity.");
        if (inventory.SelectedPersistentMemory.Any(record => record.Category != KnowledgeCategory.LearnMe) ||
            inventory.SelectedBackgroundLearning.Any(record => record.Category is KnowledgeCategory.LearnMe or KnowledgeCategory.ApiBank))
            throw new UnauthorizedAccessException("Persistent Memory and Background Learning selections cannot be relabelled.");
        var knowledge = inventory.SelectedPersistentMemory.Select(record => new KnowledgeSelection(record.Id, Digest(record), false))
            .Concat(inventory.SelectedBackgroundLearning.Select(record => new KnowledgeSelection(record.Id, Digest(record), true))).ToArray();
        if (knowledge.Any(record => record.Id == Guid.Empty) || knowledge.Select(record => record.Id).Distinct().Count() != knowledge.Length)
            throw new UnauthorizedAccessException("Knowledge selection identities are incomplete or duplicated.");
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        if (inventory.OriginalAttachmentLineage is not null && !HasCapturedOriginalAttachmentDomain(inventory))
            unsupported.Add("attachment-context");
        if (!string.IsNullOrWhiteSpace(inventory.ProjectContext)) unsupported.Add("project-context");
        if (!string.IsNullOrWhiteSpace(inventory.ProjectInstructions)) unsupported.Add("project-instructions");
        if (!string.IsNullOrWhiteSpace(inventory.RegisteredContext) && !IsExactlyPublicGenUiInstruction(inventory.RegisteredContext))
            unsupported.Add("registered-context");
        if (!string.IsNullOrWhiteSpace(inventory.OriginalWorkspaceRoot)) unsupported.Add("workspace-context");
        var hasWireImages = originalRequest switch
        {
            OllamaChatRequest chat => chat.Messages.Any(message => message.Images is { Count: > 0 }),
            OllamaToolRequest tools => tools.Messages.Any(message => message.Images is { Count: > 0 }),
            _ => throw new UnauthorizedAccessException("Actual request kind unavailable.")
        };
        if (hasWireImages || inventory.OriginalImages is { Count: > 0 }) unsupported.Add("image-context");
        var hasToolOutput = originalRequest switch
        {
            OllamaChatRequest chat => chat.Messages.Any(message => string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase)),
            OllamaToolRequest tools => tools.Messages.Any(message => string.Equals(message.Role, "tool", StringComparison.OrdinalIgnoreCase) || message.ToolName is not null),
            _ => throw new UnauthorizedAccessException("Actual request kind unavailable.")
        };
        if (hasToolOutput) unsupported.Add("tool-output-context");
        var domainFingerprint = Digest(new { inventory.ProjectContext, inventory.ProjectInstructions,
            inventory.RegisteredContext, inventory.OriginalImages, inventory.OriginalWorkspaceRoot });
        if (inventory.OriginalAttachmentLineage is not null)
            domainFingerprint = Digest(new { OriginalDomainFingerprint = domainFingerprint, inventory.OriginalAttachmentLineage });
        var capture = new Capture(owner, originalContext!, payload, Digest(inventory.OriginalConversation), domainFingerprint,
            inventory.OriginalConversation.IsTemporary, unsupported.ToFrozenSet(StringComparer.Ordinal),
            inventory.OriginalHistory.ToFrozenDictionary(message => message.Id, Digest), knowledge,
            inventory.BackgroundAppId, inventory.BackgroundProjectId,
            (inventory.OriginalBackgroundScopes ?? new HashSet<string>()).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            inventory.OriginalAttachmentInvocation);
        // Capture is allowed for local first attempts without authorizing later cloud egress.
        await RequireActorAsync(owner, token).ConfigureAwait(false);
        await DemandCurrentSelectionsAsync(capture, remote: false, token).ConfigureAwait(false);
        await RequireActorAsync(owner, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_captures.TryGetValue(originalRequest, out var earlier))
            {
                if (!ReferenceEquals(earlier.Context, originalContext) || earlier.Owner != owner ||
                    earlier.PayloadFingerprint != payload || earlier.ConversationFingerprint != capture.ConversationFingerprint ||
                    earlier.DomainContextFingerprint != capture.DomainContextFingerprint ||
                    !earlier.History.OrderBy(pair => pair.Key).SequenceEqual(capture.History.OrderBy(pair => pair.Key)) ||
                    !earlier.Knowledge.SequenceEqual(capture.Knowledge) ||
                    earlier.TemporaryConversation != capture.TemporaryConversation ||
                    !ReferenceEquals(earlier.AttachmentInvocation, capture.AttachmentInvocation) ||
                    !earlier.UnsupportedRemoteContext.SetEquals(capture.UnsupportedRemoteContext) ||
                    earlier.BackgroundAppId != capture.BackgroundAppId || earlier.BackgroundProjectId != capture.BackgroundProjectId ||
                    !earlier.BackgroundScopes.SetEquals(capture.BackgroundScopes))
                    throw new UnauthorizedAccessException("An actual original request capture cannot be replaced.");
                return;
            }
            _captures.Add(originalRequest, capture);
        }
    }

    public async ValueTask<ITaskRunCloudAdmissionLease?> AcquireOriginalAsync(TaskExecutionOwnerBinding owner,
        ProviderModelDescriptor actualModel, ProviderConfiguration actualConfiguration,
        TaskRunRouteCandidate candidate, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(actualModel);
        ArgumentNullException.ThrowIfNull(actualConfiguration); ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.UsesCloud || actualModel.IsLocal || actualConfiguration.IsLocal ||
            candidate.ProviderId != actualModel.ProviderId || candidate.ModelId != actualModel.Name ||
            actualConfiguration.Id != actualModel.ProviderId)
            throw new UnauthorizedAccessException("The configured cloud observation does not bind the actual selected route.");
        await DemandConfiguredAsync(owner, actualConfiguration, token).ConfigureAwait(false);
        var permissionSource = _cloudUse ?? throw new InvalidOperationException("The actual central remote-use permission producer is unavailable.");
        var permission = await ObserveOriginalPermissionAsync(permissionSource.AcquireOriginalAsync(owner, candidate, token)).ConfigureAwait(false);
        return new ConfiguredLease(this, owner, Detach(actualConfiguration), permission);
    }

    public ValueTask<ITaskRunProviderContextFrame> AcquireOriginalFrameAsync(TaskRunAttemptAdmission admission,
        TaskExecutionSnapshot snapshot, OllamaChatRequest original, OllamaChatRequest routed,
        CancellationToken token)
    {
        DemandRoutedModel(admission, routed.Model);
        return AcquireFrameAsync(admission, snapshot, original, routed,
            original.ExecutionContext, routed.ExecutionContext, Payload(original), Payload(routed), token);
    }

    public ValueTask<ITaskRunProviderContextFrame> AcquireOriginalFrameAsync(TaskRunAttemptAdmission admission,
        TaskExecutionSnapshot snapshot, OllamaToolRequest original, OllamaToolRequest routed,
        CancellationToken token)
    {
        DemandRoutedModel(admission, routed.Model);
        return AcquireFrameAsync(admission, snapshot, original, routed,
            original.ExecutionContext, routed.ExecutionContext, Payload(original), Payload(routed), token);
    }

    private async ValueTask<ITaskRunProviderContextFrame> AcquireFrameAsync(TaskRunAttemptAdmission admission,
        TaskExecutionSnapshot snapshot, object originalRequest, object routedRequest, ProviderExecutionContext? originalContext,
        ProviderExecutionContext? routedContext, string originalPayload, string routedPayload, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(admission); ArgumentNullException.ThrowIfNull(snapshot);
        Capture capture;
        lock (_sync)
            capture = _captures.TryGetValue(originalRequest, out var found) ? found
                : throw new UnauthorizedAccessException("The actual original request has no trusted context capture.");
        if (!ReferenceEquals(capture.Context, originalContext) || capture.PayloadFingerprint != originalPayload ||
            originalPayload != routedPayload || capture.Owner != admission.Lease.Owner)
            throw new UnauthorizedAccessException("The request, selected payload or actual owner changed before dispatch.");
        DemandSnapshot(snapshot, capture.Owner);
        DemandContext(routedContext, snapshot);
        if (routedContext!.AttemptId != admission.AttemptId || routedContext.ActionId != originalContext!.ActionId ||
            admission.AttemptId != admission.Lease.AttemptId ||
            snapshot.Attempts.LastOrDefault()?.Id != admission.AttemptId)
            throw new UnauthorizedAccessException("The actual request does not bind the same current issued attempt/action.");
        var permissionSource = _cloudUse ?? throw new InvalidOperationException("The actual central remote-use permission producer is unavailable.");
        var permission = await ObserveOriginalPermissionAsync(permissionSource.AcquireOriginalAsync(capture.Owner, admission.Lease.Candidate, token)).ConfigureAwait(false);
        var frame = new ContextFrame(this, admission, snapshot, capture, routedContext, originalRequest, routedRequest, permission);
        Task? validation = null;
        try
        {
            validation = frame.RevalidateAsync(token).AsTask();
            await validation.ConfigureAwait(false);
            return frame;
        }
        catch (Exception error)
        {
            var errors = new List<Exception>();
            AddTask(errors, validation, error);
            Task? close = null;
            try { close = frame.DisposeAsync().AsTask(); await close.ConfigureAwait(false); }
            catch (Exception cleanup) { AddTask(errors, close, cleanup); }
            Throw(errors);
            throw;
        }
    }

    private async ValueTask DemandFrameAsync(TaskRunAttemptAdmission admission, TaskExecutionSnapshot snapshot,
        Capture capture, ProviderExecutionContext routedContext, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var issued = await _issued(snapshot.TaskId, snapshot.ExecutionId, admission.AttemptId, token).ConfigureAwait(false);
        if (!ReferenceEquals(issued, admission))
            throw new UnauthorizedAccessException("The actual original attempt issuance is no longer current.");
        DemandSnapshot(snapshot, capture.Owner); DemandContext(routedContext, snapshot);
        await admission.Lease.RevalidateAsync(token).ConfigureAwait(false);
        await RequireActorAsync(capture.Owner, token).ConfigureAwait(false);
        await DemandCurrentSelectionsAsync(capture, admission.Lease.Candidate.UsesCloud, token).ConfigureAwait(false);
        // Own/context observations are sequential. No borrowed Home/SQLite/permission lock is
        // held over the external provider body and no upstream policy/quota guarantee is implied.
        await admission.Lease.RevalidateAsync(token).ConfigureAwait(false);
        await RequireActorAsync(capture.Owner, token).ConfigureAwait(false);
    }

    private async ValueTask DemandCurrentSelectionsAsync(Capture capture, bool remote, CancellationToken token)
    {
        if (remote && capture.UnsupportedRemoteContext.Count != 0)
            throw new NotSupportedException("The actual selected project, registered, workspace, image or tool-output context lacks its original domain egress owner.");
        if (!capture.TemporaryConversation)
        {
            var conversation = await _conversations.GetAsync(capture.Owner.ContextId, token).ConfigureAwait(false);
            if (conversation is null || Digest(conversation) != capture.ConversationFingerprint)
                throw new UnauthorizedAccessException("The actual captured conversation changed or retired.");
            DemandCurrentPrivacy(capture, remote);
        }
        // A temporary Conversation is a trusted producer-owned observation in this SAME private
        // request/live task issuance. It creates no repository row and is not restorable from IDs.
        // Any selected stored history still has to exist at its real source. Empty transient
        // history incurs no artificial persistence operation.
        if (capture.History.Count != 0)
        {
            var history = await _conversations.GetContextMessagesAsync(capture.Owner.ContextId, token).ConfigureAwait(false);
            foreach (var pair in capture.History)
                if (history.FirstOrDefault(message => message.Id == pair.Key) is not { } current || Digest(current) != pair.Value)
                    throw new UnauthorizedAccessException("An actual selected history record changed or was removed.");
            DemandCurrentPrivacy(capture, remote);
        }
        DemandCurrentPrivacy(capture, remote);
        foreach (var selection in capture.Knowledge)
        {
            var knowledge = _knowledge ?? throw new InvalidOperationException("The actual knowledge owner is unavailable.");
            var current = await knowledge.GetAsync(selection.Id, token).ConfigureAwait(false);
            if (current is null || Digest(current) != selection.Fingerprint ||
                current.Status is not (KnowledgeRecordStatus.Active or KnowledgeRecordStatus.Corrected) ||
                current.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow && current.Freshness != KnowledgeFreshnessClass.Durable)
                throw new UnauthorizedAccessException("An actual selected knowledge record changed or was forgotten.");
            if (remote && current.PrivacyClass is KnowledgePrivacyClass.Sensitive or KnowledgePrivacyClass.NeverLearn)
                throw new UnauthorizedAccessException("Sensitive knowledge requires its actual scoped disclosure approval; this adapter cannot create it.");
            DemandCurrentPrivacy(capture, remote);
            if (remote && selection.Background && !capture.BackgroundScopes.Contains(current.Scope))
                throw new UnauthorizedAccessException("Current Background Learning selection is outside its actual original scopes.");
        }
        DemandCurrentPrivacy(capture, remote);
        token.ThrowIfCancellationRequested();
    }

    private void DemandCurrentPrivacy(Capture capture, bool remote)
    {
        if (!remote) return;
        var current = _privacy.Current;
        if (current.LocalOnlyMode) throw new UnauthorizedAccessException("Current privacy policy requires local processing.");
        if (capture.Knowledge.Any(selection => selection.Background) &&
            (!current.BackgroundLearningEnabled || !current.BackgroundLearningCloudDisclosureEnabled ||
             !current.BackgroundLearningPolicy.Allows(capture.BackgroundAppId, capture.BackgroundProjectId)))
            throw new UnauthorizedAccessException("Current Background Learning disclosure/contributor policy refuses this selection.");
    }

    private async ValueTask DemandConfiguredAsync(TaskExecutionOwnerBinding owner,
        ProviderConfiguration original, CancellationToken token)
    {
        await RequireActorAsync(owner, token).ConfigureAwait(false);
        if (_privacy.Current.LocalOnlyMode)
            throw new UnauthorizedAccessException("Current privacy policy requires local processing.");
        var current = await _configurations.GetAsync(original.Id, token).ConfigureAwait(false);
        if (current is null || !current.IsEnabled || current.IsLocal || ConfigurationDigest(current) != ConfigurationDigest(original))
            throw new UnauthorizedAccessException("The actual cloud provider configuration changed or is disabled.");
        if (current.Kind is not (ModelProviderKind.OpenAI or ModelProviderKind.OpenAICompatible or
            ModelProviderKind.Anthropic or ModelProviderKind.Gemini or ModelProviderKind.OpenRouter))
            throw new NotSupportedException("This provider has no maintained configured credential-presence adapter.");
        if (!string.IsNullOrWhiteSpace(current.Endpoint) &&
            (!Uri.TryCreate(current.Endpoint, UriKind.Absolute, out var endpoint) ||
             endpoint.Scheme is not ("https" or "http") || endpoint.Scheme == "http" && !endpoint.IsLoopback))
            throw new UnauthorizedAccessException("The actual configured provider transport is invalid.");
        // Only the existing native vault/provider secret interface is used. Never retain, print,
        // hash, persist, expose or copy its actual key into request/context metadata.
        if (string.IsNullOrWhiteSpace(await _secrets.GetAsync(current.Id, "api-key", token).ConfigureAwait(false)))
            throw new InvalidOperationException("The actual provider credential is unavailable.");
        var finalConfiguration = await _configurations.GetAsync(original.Id, token).ConfigureAwait(false);
        if (finalConfiguration is null || ConfigurationDigest(finalConfiguration) != ConfigurationDigest(original))
            throw new UnauthorizedAccessException("Provider configuration changed during credential observation.");
        await RequireActorAsync(owner, token).ConfigureAwait(false);
        if (_privacy.Current.LocalOnlyMode)
            throw new UnauthorizedAccessException("Privacy policy changed during configured cloud admission.");
    }

    private async ValueTask RequireActorAsync(TaskExecutionOwnerBinding owner, CancellationToken token)
    {
        var actual = await _actors.GetCurrentAsync(token).ConfigureAwait(false);
        var expected = new AuthenticatedResourceActor(owner.ActorId, owner.ProfileId, owner.AccountId,
            owner.OrganisationId, owner.AuthenticationRevision);
        if (actual != expected) throw new UnauthorizedAccessException("The original authenticated task owner changed.");
        token.ThrowIfCancellationRequested();
    }

    private static void DemandSnapshot(TaskExecutionSnapshot snapshot, TaskExecutionOwnerBinding owner)
    {
        if (snapshot.OwnerBinding != owner || snapshot.TaskId != owner.TaskId ||
            snapshot.ContextId != owner.ContextId || snapshot.ExecutionId != owner.ExecutionId)
            throw new UnauthorizedAccessException("The current task snapshot belongs to another original owner/context/run.");
    }
    private static void DemandRoutedModel(TaskRunAttemptAdmission admission, string actualModel)
    {
        ArgumentNullException.ThrowIfNull(admission);
        var candidate = admission.Lease.Candidate;
        if (actualModel != candidate.ModelId && actualModel != candidate.ProviderId + ":" + candidate.ModelId)
            throw new UnauthorizedAccessException("The actual raw request model differs from the same issued attempt.");
    }
    private static void DemandContext(ProviderExecutionContext? context, TaskExecutionSnapshot snapshot)
    {
        if (context is null || context.TaskId != snapshot.TaskId || context.ContextId != snapshot.ContextId ||
            context.ExecutionId != snapshot.ExecutionId || context.PersistenceRevision != snapshot.PersistenceRevision)
            throw new UnauthorizedAccessException("The actual request context does not match the current task snapshot.");
    }
    private static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static bool IsExactlyPublicGenUiInstruction(string actual) =>
        Enum.GetValues<GenerativeUiResponseMode>().Any(mode =>
            string.Equals(actual, GenUiChatDirectiveParser.ModelInstructionFor(mode), StringComparison.Ordinal));
    private static string Payload(OllamaChatRequest request) => Digest(new
    { request.Messages, request.Effort, request.SystemPrompt, request.EnableTools, request.Options });
    private static string Payload(OllamaToolRequest request) => Digest(new
    { request.Messages, request.Tools, request.Effort, request.SystemPrompt, request.Options });
    private static string Payload(object actual) => actual switch
    {
        OllamaChatRequest chat => Payload(chat),
        OllamaToolRequest tools => Payload(tools),
        _ => throw new UnauthorizedAccessException("The actual original provider request has another kind.")
    };
    private static string ConfigurationDigest(ProviderConfiguration value) => Digest(new
    { value.Id, value.Kind, value.DisplayName, value.Endpoint, value.IsEnabled, value.IsLocal,
      value.AllowCloudFallback, Metadata = value.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(), value.UpdatedAt });
    private static ProviderConfiguration Detach(ProviderConfiguration value) => value with
    { Metadata = value.Metadata.ToFrozenDictionary(StringComparer.Ordinal) };

    private static async Task<ITaskRunCloudUsePermissionLease> ObserveOriginalPermissionAsync(
        ValueTask<ITaskRunCloudUsePermissionLease> supplied)
    {
        Task<ITaskRunCloudUsePermissionLease>? original = null;
        try
        {
            original = supplied.AsTask(); // Consume the actual supplied ValueTask exactly once.
            return await original.ConfigureAwait(false);
        }
        catch (Exception)
        {
            var direct = original?.Exception; // Capture the actual terminal direct fault snapshot once.
            if (direct is not null)
            {
                // Keep nested members exact, never Flatten. A FAULTED OCE is not actual task
                // cancellation: preserve its group so an async builder cannot relabel it canceled.
                var cause = direct.InnerExceptions.Count == 1 && direct.InnerExceptions[0] is not OperationCanceledException
                    ? direct.InnerExceptions[0] : direct;
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cause).Throw();
            }
            throw; // The SAME genuinely canceled task remains canceled; no success/refusal waiver.
        }
    }

    private static void AddTask(List<Exception> errors, Task? task, Exception observed)
    {
        IEnumerable<Exception> causes = task?.Exception is { } compound ? compound.InnerExceptions : [observed];
        foreach (var cause in causes)
            if (!errors.Any(existing => ReferenceEquals(existing, cause))) errors.Add(cause);
    }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Actual context admission and original permission cleanup failed.", errors);
    }

    private sealed partial class ConfiguredLease : ITaskRunCloudAdmissionLease
    {
        private readonly TaskRunConfiguredCloudAdmissionSource _source;
        private readonly TaskExecutionOwnerBinding _owner;
        private readonly ProviderConfiguration _configuration;
        private readonly ITaskRunCloudUsePermissionLease _permission;
        private readonly object _gate = new();
        private bool _closed;
        private Task? _close;
        public ConfiguredLease(TaskRunConfiguredCloudAdmissionSource source, TaskExecutionOwnerBinding owner,
            ProviderConfiguration configuration, ITaskRunCloudUsePermissionLease permission)
        { _source = source; _owner = owner; _configuration = configuration; _permission = permission; }
        public async ValueTask RevalidateAsync(CancellationToken token)
        {
            lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
            await _source.DemandConfiguredAsync(_owner, _configuration, token).ConfigureAwait(false);
            await _permission.RevalidateAsync(token).ConfigureAwait(false);
            lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
        }
        public ValueTask DisposeAsync()
        {
            DemandExternalOriginalScopedCloudJoin();
            TaskCompletionSource start; Task close;
            lock (_gate)
            {
                if (_close is not null) return new ValueTask(_close);
                _closed = true;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                close = _close = _originalScopedValidations.Count == 0
                    ? ClosePermissionAsync(_permission, start.Task) : CloseScopedPermissionAsync(start.Task);
            }
            start.SetResult();
            return new ValueTask(close);
        }
    }

    private static async Task ClosePermissionAsync(ITaskRunCloudUsePermissionLease permission, Task start)
    {
        await start.ConfigureAwait(false);
        Task? original = null;
        try { original = permission.DisposeAsync().AsTask(); await original.ConfigureAwait(false); }
        catch (Exception error) { var errors = new List<Exception>(); AddTask(errors, original, error); Throw(errors); throw; }
    }

    private sealed class ContextFrame : ITaskRunProviderContextFrame, ITaskRunProviderInvocationFence
    {
        private readonly TaskRunConfiguredCloudAdmissionSource _source;
        private readonly TaskRunAttemptAdmission _admission;
        private readonly TaskExecutionSnapshot _snapshot;
        private readonly Capture _capture;
        private readonly ProviderExecutionContext _context;
        private readonly object _originalRequest;
        private readonly object _routedRequest;
        private readonly ITaskRunCloudUsePermissionLease _permission;
        private readonly object _gate = new();
        private bool _closed;
        private bool _invoked;
        private AttachmentFrame? _attachments;
        private Task? _close;
        public ContextFrame(TaskRunConfiguredCloudAdmissionSource source, TaskRunAttemptAdmission admission,
            TaskExecutionSnapshot snapshot, Capture capture, ProviderExecutionContext context,
            object originalRequest, object routedRequest, ITaskRunCloudUsePermissionLease permission)
        { _source = source; _admission = admission; _snapshot = snapshot; _capture = capture; _context = context;
          _originalRequest = originalRequest; _routedRequest = routedRequest; _permission = permission; }
        public async ValueTask RevalidateAsync(CancellationToken token)
        {
            lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
            DemandPayload();
            await _source.DemandFrameAsync(_admission, _snapshot, _capture, _context, token).ConfigureAwait(false);
            await _permission.RevalidateAsync(token).ConfigureAwait(false);
            if (_capture.AttachmentInvocation is not null && _admission.Lease.Candidate.UsesCloud)
            {
                Task actualValidation;
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_closed, this);
                    _attachments ??= _source.CreateOriginalAttachmentFrame(_admission, _snapshot, _context, _capture);
                    actualValidation = _attachments.RevalidateAsync(token);
                }
                await actualValidation.ConfigureAwait(false);
            }
            _source.DemandCurrentPrivacy(_capture, _admission.Lease.Candidate.UsesCloud);
            DemandPayload();
            lock (_gate) ObjectDisposedException.ThrowIf(_closed, this);
        }
        private void DemandPayload()
        {
            if (Payload(_originalRequest) != _capture.PayloadFingerprint || Payload(_routedRequest) != _capture.PayloadFingerprint)
                throw new UnauthorizedAccessException("The actual admitted request payload changed before raw dispatch.");
        }
        // This releases ONLY metadata capture custody. The caller must first join its actual raw
        // body/enumerator/finally, and the attempt owner alone disposes the encompassing lease.
        public T RunOriginalInvocation<T>(Func<T> originalRawStart)
        {
            ArgumentNullException.ThrowIfNull(originalRawStart);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                if (_invoked) throw new InvalidOperationException("This finite request already admitted its original raw start; replay is refused.");
                DemandPayload();
                _invoked = true; // Seal even if Start throws: unknown admission may not be replayed.
                return _attachments is null ? _permission.RunOriginalInvocation(originalRawStart)
                    : _attachments.RunOriginalInvocation(() => _permission.RunOriginalInvocation(originalRawStart));
            }
        }
        public ValueTask DisposeAsync()
        {
            TaskCompletionSource start; Task close;
            lock (_gate)
            {
                if (_close is not null) return new ValueTask(_close);
                _closed = true;
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                close = _close = CloseContextAsync(start.Task);
            }
            start.SetResult();
            return new ValueTask(close);
        }
        private async Task CloseContextAsync(Task start)
        {
            await start.ConfigureAwait(false); var errors = new List<Exception>(); Task? actual = null;
            if (_attachments is not null)
                try { actual = _attachments.CloseAndDrainAsync(); await actual.ConfigureAwait(false); }
                catch (Exception cause) { AddTask(errors, actual, cause); }
            actual = null;
            try { actual = ClosePermissionAsync(_permission, Task.CompletedTask); await actual.ConfigureAwait(false); }
            catch (Exception cause) { AddTask(errors, actual, cause); }
            Throw(errors);
        }
    }
}
