using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Attachments;

public sealed partial class AssistantOriginalAttachmentSource : IAssistantOriginalAttachmentCommandSource,
    ICanonicalAttachmentImportSource, IAsyncDisposable
{
    private readonly HomePersonalDenFactory _home;
    private readonly IConversationRepository _conversations;
    private readonly ConversationProductionRepository _production;
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    private readonly HomeResourceStoreOwnershipAuthority _ownership;
    private readonly ICanonicalAttachmentOriginalSelectionSource _files;
    private readonly CanonicalAttachmentOriginalFileSource _content;
    private readonly OdsAwareMessageAttachmentService _processing;
    private readonly AssistantMemoryOriginals _originals = new();
    private readonly object _writeGate = new();
    private ICanonicalAttachmentOriginalReadSource? _reads;
    private ICanonicalAttachmentHomeImportSource? _writes;
    private readonly ConditionalWeakTable<ICanonicalAttachmentConversationRead, ReadSelection> _readSelections = new();
    private readonly ConditionalWeakTable<WriteIntent, object> _writeIntents = new();
    private readonly ConditionalWeakTable<WriteIntent, WriteAttempt> _writeAttempts = new();
    private readonly ConditionalWeakTable<Task, Exception> _refused = new();
    private readonly ConditionalWeakTable<Task<ICanonicalAttachmentImportAcknowledgment>, WriteInvocation> _atomicWrites = new();
    private readonly List<WriteInvocation> _invocations = [];
    private readonly AsyncLocal<WriteInvocation?> _activeWrite = new();

    public AssistantOriginalAttachmentSource(HomePersonalDenFactory actualHome, IConversationRepository actualConversations,
        ConversationProductionRepository actualProduction, CanonicalSqliteOriginalStoreOwner actualStore,
        HomeLocalProfileIdentity actualProfiles, HomeResourceStoreOwnershipAuthority actualOwnership,
        ICanonicalAttachmentOriginalSelectionSource actualFiles, CanonicalAttachmentOriginalFileSource actualContent,
        OdsAwareMessageAttachmentService actualProcessing)
    {
        if (!actualStore.HasOriginalProfiles(actualProfiles) || !actualProcessing.HasOriginalContentSource(actualContent) ||
            !actualContent.HasOriginalSelectionComposition(actualFiles, actualProfiles))
            throw new ArgumentException("Retain the SAME configured attachment chain, protected SQL owner and Home profiles.");
        _home = actualHome; _conversations = actualConversations; _production = actualProduction; _store = actualStore;
        _ownership = actualOwnership; _files = actualFiles; _content = actualContent; _processing = actualProcessing;
        _production.BindOriginalAttachmentSource(_store, this);
        if (!_production.HasOriginalAttachmentComposition(_store, this, _conversations))
            throw new ArgumentException("The actual attachment repository belongs to another conversation source.");
    }
    public void BindOriginalHomeSources(ICanonicalAttachmentOriginalReadSource reads, ICanonicalAttachmentHomeImportSource writes)
    {
        ArgumentNullException.ThrowIfNull(reads); ArgumentNullException.ThrowIfNull(writes);
        lock (_writeGate)
        {
            if (_reads is not null || _writes is not null) throw new InvalidOperationException("The original attachment Home sources are already bound.");
            _content.BindOriginalReadSource(reads); _reads = reads; _writes = writes;
        }
    }
    public bool HasOriginalHomeSources(ICanonicalAttachmentOriginalReadSource reads, ICanonicalAttachmentHomeImportSource writes) =>
        ReferenceEquals(_reads, reads) && ReferenceEquals(_writes, writes);
    public bool HasOriginalComposition(HomePersonalDenFactory home, IConversationRepository conversations) =>
        ReferenceEquals(_home, home) && ReferenceEquals(_conversations, conversations);
    public Task? OriginalClose => _originals.OriginalClose;
    public void DemandExternalOriginalRetirementJoin() => _originals.DemandExternalOriginalRetirementJoin();
    public void RequestOriginalRetirement() => _originals.RequestRetirement();
    public Task CloseAndDrainAsync() => _originals.CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    public bool IsAcknowledgedOriginalCommandRefusal(Task actual) => actual.IsFaulted &&
        _refused.TryGetValue(actual, out var cause) && actual.Exception is { InnerExceptions.Count: 1 } group &&
        ReferenceEquals(group.InnerExceptions[0], cause);

    private sealed class ReadSelection(AssistantOriginalAttachmentSource owner, AssistantCanonicalMembershipSource membership,
        AssistantConversationBinding binding, AuthenticatedResourceActor actor, ResourceStoreIdentity store,
        VerifiedResourceStoreOwnership permission, VerifiedResourceStoreOwnership den, Guid? branch)
        : ICanonicalAttachmentConversationRead
    {
        internal AssistantOriginalAttachmentSource Owner => owner;
        internal AssistantCanonicalMembershipSource Membership => membership;
        internal AssistantConversationBinding Binding => binding;
        internal VerifiedResourceStoreOwnership Permission => permission;
        internal VerifiedResourceStoreOwnership DenPermission => den;
        public AuthenticatedResourceActor Actor => actor;
        public ResourceStoreIdentity OriginalStoreIdentity => store;
        public Conversation OriginalConversation => binding.Conversation;
        public Guid? BranchId => branch;
    }
    public bool IsIssuedOriginalConversationRead(ICanonicalAttachmentConversationRead read) =>
        read is ReadSelection actual && ReferenceEquals(actual.Owner, this) &&
        _readSelections.TryGetValue(read, out var retained) && ReferenceEquals(actual, retained) &&
        actual.Membership.IsIssuedOriginalBinding(actual.Binding);
    private sealed class WriteIntent(AssistantOriginalAttachmentSource owner, ReadSelection read,
        ConversationProductionRepository.OriginalAttachmentDraft original, MessageAttachment attachment, ConversationDraft draft,
        ICanonicalAttachmentOriginalSelection? selected, OriginalMessageAttachmentProcessingResult? processed,
        ICanonicalAttachmentOriginalContentLease? content, CanonicalAttachmentDraftMutation mutation, Guid operation) : ICanonicalAttachmentImportIntent
    {
        internal AssistantOriginalAttachmentSource Owner => owner;
        internal ReadSelection Read => read;
        internal ICanonicalAttachmentOriginalContentLease? OriginalContent => content;
        internal ConversationProductionRepository.OriginalAttachmentDraft Snapshot => original;
        public AuthenticatedResourceActor Actor => read.Actor;
        public Guid OperationId => operation;
        public CanonicalAttachmentDraftMutation Mutation => mutation;
        public ResourceStoreIdentity OriginalStoreIdentity => read.OriginalStoreIdentity;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => read.Permission;
        public VerifiedResourceStoreOwnership OriginalDenOwnership => read.DenPermission;
        public string DenId => read.Binding.Definition.Identity.DenId;
        public string NamespaceId => read.Binding.Definition.Identity.NamespaceId;
        public string DefinitionId => read.Binding.Definition.Identity.DefinitionId;
        public long DefinitionRevision => read.Binding.Definition.Revision;
        public string SessionId => read.Binding.DenSessionId;
        public long SessionRevision => read.Binding.DenSessionRevision;
        public Conversation OriginalConversation => read.OriginalConversation;
        public ConversationDraft? OriginalDraft => original.Draft;
        public ConversationDraft CandidateDraft => draft;
        public MessageAttachment Attachment => attachment;
        public ICanonicalAttachmentOriginalSelection? OriginalSelection => selected;
        public OriginalMessageAttachmentProcessingResult? OriginalProcessing => processed;
    }
    private sealed class Acknowledgment(WriteIntent intent, bool applied) : ICanonicalAttachmentImportAcknowledgment
    {
        public ICanonicalAttachmentImportIntent OriginalIntent => intent;
        public bool Applied => applied;
        public string Reason => applied ? "The attachment draft was saved." : "The draft changed. Review it before trying again.";
        public MessageAttachment Attachment => intent.Attachment;
        public ConversationDraft Draft => applied ? intent.CandidateDraft : intent.OriginalDraft ?? intent.CandidateDraft;
    }
    private sealed class WriteAttempt { internal Task<ICanonicalAttachmentImportAcknowledgment> Driver = null!; internal WriteInvocation Invocation = null!; }
    private sealed class WriteInvocation(WriteIntent intent)
    {
        internal WriteIntent Intent => intent;
        internal CanonicalSqliteOriginalStoreLease? Sqlite;
        internal DenStore.AssistantRevisionPin? Den;
        internal ICanonicalAttachmentHomeImportClaim? Claim;
        internal Task<ICanonicalAttachmentImportAcknowledgment>? Atomic;
        internal Acknowledgment? Acknowledgment;
        internal int OriginalDispatchSettled;
        internal readonly TaskCompletionSource OriginalDispatchBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? OriginalDispatchWait;
        internal bool OriginalDispatchWaitHealthy;
        internal ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalAttachmentImportIntent, ICanonicalAttachmentImportAcknowledgment>? SettlementPhase;
        internal Task? SettlementRelease, OriginalDenRelease;
        internal bool SettlementReleaseHealthy;
    }
    public bool IsIssuedOriginalImportIntent(ICanonicalAttachmentImportIntent intent) => intent is WriteIntent actual &&
        ReferenceEquals(actual.Owner, this) && IsIssuedOriginalConversationRead(actual.Read) && _writeIntents.TryGetValue(actual, out _);
    private WriteIntent DemandWriteIntent(ICanonicalAttachmentImportIntent intent) => IsIssuedOriginalImportIntent(intent)
        ? (WriteIntent)intent : throw new UnauthorizedAccessException("The actual attachment owner did not issue this intent.");
    public string GetOriginalImportIntentDigest(ICanonicalAttachmentImportIntent intent)
    {
        var actual = DemandWriteIntent(intent);
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { actual.OperationId, actual.Mutation, actual.Actor, actual.OriginalStoreIdentity, actual.OriginalStoreOwnership,
            actual.OriginalDenOwnership, actual.DenId, actual.NamespaceId, actual.DefinitionId, actual.DefinitionRevision,
            actual.SessionId, actual.SessionRevision, actual.OriginalConversation, actual.OriginalDraft,
            actual.CandidateDraft, actual.Attachment, File = actual.OriginalSelection?.OriginalFile })));
    }
    private async Task<ReadSelection> PrepareRead(AssistantConversationBinding binding, Guid? branch,
        AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        var membership = AssistantCanonicalMembershipSource.ObserveOriginalIssuer(binding);
        if (membership is null || !ReferenceEquals(membership.OriginalHomeDenFactory, _home) ||
            !ReferenceEquals(membership.OriginalConversations, _conversations))
            throw new UnauthorizedAccessException("Retain the actual Assistant conversation binding.");
        var current = await source.Read(() => membership.ValidateOriginalDenWithinSourceAsync(binding, source.Run, source.Retain, token)).ConfigureAwait(false);
        var identity = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(current.Actor, source.Run, source.Retain, token)).ConfigureAwait(false);
        var permission = await Permission("canonical.sqlite", identity.StoreId.ToString("D"), current.Actor, source, token).ConfigureAwait(false);
        var den = await Permission("den", current.Definition.Identity.DenId, current.Actor, source, token).ConfigureAwait(false);
        var read = new ReadSelection(this, membership, binding, current.Actor, identity, permission, den, branch);
        _readSelections.Add(read, read); await ValidateRead(read, source, token).ConfigureAwait(false); return read;
    }
    private async Task<VerifiedResourceStoreOwnership> Permission(string kind, string id, AuthenticatedResourceActor actor,
        AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        var permission = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync(kind, id, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (permission?.Receipt is null || permission.ResourceKind != kind || permission.StoreId != id || permission.ProfileId != actor.ProfileId ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(permission, actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Review the original conversation store in Home before importing attachments.");
        return permission;
    }
    private async Task ValidateRead(ReadSelection read, AssistantMemoryOriginals.Scope source, CancellationToken token)
    {
        var current = await source.Read(() => read.Membership.ValidateOriginalDenWithinSourceAsync(read.Binding, source.Run, source.Retain, token)).ConfigureAwait(false);
        if (current.Actor != read.Actor || current.Definition.Revision != read.Binding.Definition.Revision || current.Conversation != read.OriginalConversation ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(read.Permission, read.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false) ||
            !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(read.DenPermission, read.Actor, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
            throw new UnauthorizedAccessException("The current Assistant, conversation or Home store binding changed.");
        await WithInputLease(read, source, async lease =>
        {
            await source.Read(() => _production.ValidateOriginalAttachmentConversationWithinLeaseAsync(lease,
                read, requireExactOriginalRow: true, token)).ConfigureAwait(false); return true;
        }, token).ConfigureAwait(false);
    }
    public Task ValidateOriginalImportIntentWithinSourceAsync(ICanonicalAttachmentImportIntent intent, Action<Action> scope,
        Action<Task> retain, CancellationToken token) => Run(scope, retain, async source =>
        {
            var actual = DemandWriteIntent(intent); await ValidateRead(actual.Read, source, token).ConfigureAwait(false);
            source.Run(() => DemandOriginalProcessing(actual));
            if (actual.OriginalContent is { } content)
                await source.Read(() => content.ValidateOriginalWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            return true;
        });
    private void DemandOriginalProcessing(WriteIntent actual)
    {
        if (actual.Mutation == CanonicalAttachmentDraftMutation.Detach)
        {
            if (actual.OriginalSelection is not null || actual.OriginalProcessing is not null || actual.OriginalContent is not null)
                throw new UnauthorizedAccessException("A detach intent cannot borrow another file's processing proof.");
            return;
        }
        if (actual.OriginalSelection is not { } selected || actual.OriginalProcessing is not { } processed ||
            actual.OriginalContent is not { } content || !_files.IsIssuedOriginalSelection(selected) ||
            !ReferenceEquals(content.OriginalSelection, selected) || !_content.IsIssuedOriginalContent(selected, content.OriginalRead, content) ||
            !_processing.IsIssuedOriginalProcessing(content, processed))
            throw new UnauthorizedAccessException("The same live approved file and original processing receipt are required.");
    }
    private Task<T> Run<T>(Action<Action> caller, Action<Task> retain, Func<AssistantMemoryOriginals.Scope, Task<T>> body) =>
        _originals.Admit(async () =>
        {
            var source = _originals.CreateScope(caller, retain); var errors = new List<Exception>(); T value = default!;
            try { value = await body(source).ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            try { await source.JoinAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            AssistantMemoryOriginals.Throw(errors); return value;
        });
}
