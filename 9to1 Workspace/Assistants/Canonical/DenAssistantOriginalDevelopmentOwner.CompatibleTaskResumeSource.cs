using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;
using CheckpointMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.CompatibleCheckpointMetadata;
using MembershipMetadata = HavenOS.Apps.Assistants.Canonical.AssistantCanonicalMembershipSource.MembershipMetadata;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantOriginalDevelopmentOwner
{
    // Weak issuer registrations are observations. Actual admitted commands, late
    // products and unresolved pin closes remain owned by the process ledger.
    private readonly ConditionalWeakTable<AssistantCompatibleConversationCheckpoint, CheckpointOriginal> _compatibleCheckpoints = new();
    private readonly ConditionalWeakTable<ResumeSelection, ResumeSelection> _compatibleResumeSelections = new();
    private readonly ConditionalWeakTable<ResumeRevisionPin, ResumeRevisionPin> _compatibleResumePins = new();
    private sealed record CheckpointOriginal(AssistantCanonicalMembershipSource Membership,
        AuthenticatedResourceActor Actor, SessionRecord Session, MembershipMetadata Metadata,
        CheckpointMetadata Checkpoint);
    private sealed record CheckpointCurrent(HomePersonalDenSession Home, AgentDefinitionRecord Definition,
        SessionRecord Session, MembershipMetadata Metadata, CheckpointMetadata Checkpoint);

    internal Task RegisterOriginalCompatibleCheckpointWithinSourceAsync(
        AssistantCompatibleConversationCheckpoint checkpoint, AssistantCanonicalMembershipSource sameMembership,
        SessionRecord actualAck, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _originals.Admit(async () =>
        {
            var sources = new Sources(_originals, scope, retain);
            sources.Scope(() => DemandRegisteredMembership(sameMembership));
            if (!ReferenceEquals(checkpoint.Issuer, this))
                throw new AssistantCommandRefusedException("The process development source must issue this exact checkpoint.");
            var home = await sources.Take(() => sameMembership.OpenHomeWithinSourceAsync(
                sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            AssistantCanonicalMembershipSource.DemandIdentity(home, checkpoint.Identity);
            var definition = await sources.Take(() => sameMembership.DefinitionWithinSourceAsync(home,
                checkpoint.Identity, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            var session = await sources.Take(() => home.Den.GetAsync<SessionRecord>(checkpoint.Identity.NamespaceId,
                checkpoint.DenSessionId, token)).ConfigureAwait(false)
                ?? throw new AssistantCommandRefusedException("The actual declined checkpoint is unavailable.");
            var metadata = AssistantCanonicalMembershipSource.ReadMetadata<MembershipMetadata>(session,
                AssistantCanonicalMembershipSource.SessionKey);
            var checkpointMetadata = AssistantCanonicalMembershipSource.ReadMetadata<CheckpointMetadata>(session,
                AssistantCanonicalMembershipSource.CompatibleCheckpointKey);
            CheckpointOriginal? captured = null;
            sources.Scope(() =>
            {
                if (!SameDenRecord(session, actualAck) || definition.Revision != checkpoint.DefinitionRevision)
                    throw new AssistantCommandRefusedException("The exact durable checkpoint ACK or definition changed before source registration.");
                DemandCheckpointTuple(checkpoint, session, metadata, checkpointMetadata, "declined-pending");
                captured = new CheckpointOriginal(sameMembership, home.Actor,
                    CloneDenRecord(session), metadata!, checkpointMetadata!);
            });
            // Publish issuer metadata only after the caller's productive scope and
            // postguards have actually returned successfully.
            _originals.Invoke(() =>
            {
                if (_compatibleCheckpoints.TryGetValue(checkpoint, out var prior))
                {
                    if (!ReferenceEquals(prior.Membership, sameMembership) || prior.Actor != home.Actor ||
                        !SameDenRecord(prior.Session, session))
                        throw new AssistantCommandRefusedException("The same checkpoint object cannot change issuer, actor or durable ACK.");
                }
                else _compatibleCheckpoints.Add(checkpoint, captured
                    ?? throw new InvalidOperationException("No actual checkpoint observation was captured."));
                return true;
            });
            return true;
        });

    internal bool IsIssuedOriginalCompatibleCheckpoint(AssistantCompatibleConversationCheckpoint checkpoint) =>
        checkpoint is not null && ReferenceEquals(checkpoint.Issuer, this) &&
        _compatibleCheckpoints.TryGetValue(checkpoint, out _);

    private void DemandRegisteredMembership(AssistantCanonicalMembershipSource membership)
    {
        lock (_gate)
            if (!_memberships.TryGetValue(membership, out _) ||
                !ReferenceEquals(membership.OriginalHomeDenFactory, _home) ||
                !ReferenceEquals(membership.OriginalConversations, _conversations))
                throw new AssistantCommandRefusedException("The actual process composition did not register this membership source.");
    }
    private CheckpointOriginal RequireCompatibleCheckpoint(AssistantCompatibleConversationCheckpoint checkpoint) =>
        checkpoint is not null && ReferenceEquals(checkpoint.Issuer, this) &&
        _compatibleCheckpoints.TryGetValue(checkpoint, out var original) ? original :
            throw new AssistantCommandRefusedException("Only the exact registered durable checkpoint is accepted.");

    private async Task<CheckpointCurrent> ReadCompatibleCheckpointCurrentAsync(
        AssistantCompatibleConversationCheckpoint checkpoint, CheckpointOriginal original,
        SessionRecord expectedSession, string publication, Guid commandOperation,
        Sources sources, CancellationToken token)
    {
        sources.Scope(() => DemandRegisteredMembership(original.Membership));
        var home = await sources.Take(() => original.Membership.OpenHomeWithinSourceAsync(
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        if (home.Actor != original.Actor)
            throw new AssistantCommandRefusedException("The current actor changed; read the durable pending operation again from the current presentation.");
        var definition = await sources.Take(() => original.Membership.DefinitionWithinSourceAsync(home,
            checkpoint.Identity, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        var session = await sources.Take(() => home.Den.GetAsync<SessionRecord>(checkpoint.Identity.NamespaceId,
            checkpoint.DenSessionId, token)).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The exact original pending membership is unavailable.");
        var metadata = AssistantCanonicalMembershipSource.ReadMetadata<MembershipMetadata>(session,
            AssistantCanonicalMembershipSource.SessionKey);
        var checkpointMetadata = AssistantCanonicalMembershipSource.ReadMetadata<CheckpointMetadata>(session,
            AssistantCanonicalMembershipSource.CompatibleCheckpointKey);
        sources.Scope(() =>
        {
            var configured = AssistantCanonicalMembershipSource.ReadMetadata<AssistantCanonicalMembershipSource.DefinitionMetadata>(
                definition, AssistantCanonicalMembershipSource.DefinitionKey);
            if (definition.Revision != checkpoint.DefinitionRevision || !definition.Enabled ||
                configured is null || !configured.Configuration.Enabled || configured.Configuration.Archived ||
                checkpointMetadata?.LastCommandOperation != commandOperation || !SameDenRecord(session, expectedSession))
                throw new AssistantCommandRefusedException("The actual definition or pending membership CAS changed before this resume.");
            DemandCheckpointTuple(checkpoint, session, metadata, checkpointMetadata, publication,
                requireOriginalRevision: publication == "declined-pending");
        });
        var after = await sources.Take(() => original.Membership.OpenHomeWithinSourceAsync(
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        if (after.Actor != original.Actor || after.DenId != home.DenId)
            throw new AssistantCommandRefusedException("The actual Home actor or Den changed during pending operation validation.");
        return new(home, definition, session, metadata!, checkpointMetadata!);
    }

    private static void DemandCheckpointTuple(AssistantCompatibleConversationCheckpoint checkpoint,
        SessionRecord session, MembershipMetadata? metadata, CheckpointMetadata? state,
        string publication, bool requireOriginalRevision = true)
    {
        if (metadata is not { Schema: 1, Kind: AssistantConversationKind.Task } || state is not { Schema: 1 } ||
            metadata.Publication != publication || metadata.DefinitionId != checkpoint.Identity.DefinitionId ||
            metadata.CreationOperation != checkpoint.OriginalCreationOperationId ||
            session.Id != checkpoint.DenSessionId || session.Id != AssistantCanonicalMembershipSource.SessionId(checkpoint.PlannedConversationId) ||
            session.NamespaceId != checkpoint.Identity.NamespaceId || session.ConversationId != checkpoint.PlannedConversationId.ToString("D") ||
            requireOriginalRevision && session.Revision != checkpoint.MembershipRevision ||
            state.DefinitionRevision != checkpoint.DefinitionRevision ||
            metadata.OriginalProjectReference != checkpoint.Project ||
            metadata.OriginalStudioConversationId != checkpoint.OriginalStudioConversationId ||
            metadata.OriginalStudioContainerId != checkpoint.OriginalStudioContainerId ||
            metadata.OriginalStoreIdentity != checkpoint.OriginalStoreIdentity ||
            metadata.OriginalConversation != checkpoint.OriginalTaskConversation ||
            metadata.OriginalTaskContainer != checkpoint.OriginalTaskContainer ||
            metadata.OriginalStudioConversation is not { } studio || studio.Id != checkpoint.OriginalStudioConversationId ||
            metadata.OriginalStudioContainer is not { } studioContainer || studioContainer.Id != checkpoint.OriginalStudioContainerId ||
            studio.ContainerId != studioContainer.Id || studio.Mode != HavenMode.Studio || studio.Kind != ConversationKind.StudioChat ||
            studio.IsArchived || studio.IsTemporary || studio.SpaceId is not null || studio.LessonId is not null ||
            studioContainer.Mode != HavenMode.Studio || studioContainer.IsArchived ||
            checkpoint.OriginalTaskConversation.Id != checkpoint.PlannedConversationId ||
            checkpoint.OriginalTaskConversation.Mode != HavenMode.Tasks || checkpoint.OriginalTaskConversation.Kind != ConversationKind.Task ||
            checkpoint.OriginalTaskConversation.ContainerId != checkpoint.OriginalTaskContainer.Id ||
            checkpoint.OriginalTaskConversation.IsArchived || checkpoint.OriginalTaskConversation.IsTemporary ||
            checkpoint.OriginalTaskConversation.SpaceId is not null || checkpoint.OriginalTaskConversation.LessonId is not null ||
            checkpoint.OriginalTaskContainer.Mode != HavenMode.Tasks || checkpoint.OriginalTaskContainer.IsArchived ||
            checkpoint.OriginalTaskContainer.Id == Guid.Empty || checkpoint.OriginalTaskContainer.Id == studioContainer.Id ||
            checkpoint.PlannedConversationId == Guid.Empty || checkpoint.PlannedConversationId == studio.Id ||
            checkpoint.OriginalCreationOperationId == Guid.Empty || state.LastCommandOperation == Guid.Empty ||
            metadata.OriginalCreationIntentSha256 is not { Length: 64 } digest || !digest.All(Uri.IsHexDigit))
            throw new AssistantCommandRefusedException("The actual Den checkpoint does not retain the exact allocated Tasks pair and original Studio lineage.");
        if (requireOriginalRevision && (state.LastCommandOperation != checkpoint.LastCommandOperationId ||
            state.Reason != checkpoint.Reason || checkpoint.Title != checkpoint.OriginalTaskConversation.Title))
            throw new AssistantCommandRefusedException("The durable decline command or reason changed before checkpoint validation.");
    }

    private sealed class ResumeSelection(DenAssistantOriginalDevelopmentOwner owner,
        AssistantCompatibleConversationCheckpoint checkpoint, CheckpointOriginal original,
        VerifiedResourceStoreOwnership actualDenOwnership)
        : ICanonicalProjectTaskContextResumeSelection
    {
        private readonly object _gate = new();
        private SessionRecord _current = original.Session;
        private Guid _command = checkpoint.LastCommandOperationId;
        private bool _attemptBound;
        internal DenAssistantOriginalDevelopmentOwner Owner => owner;
        internal CheckpointOriginal Original => original;
        internal (SessionRecord Session, Guid Command, bool AttemptBound) Observe()
        { lock (_gate) return (_current, _command, _attemptBound); }
        internal void BindAcknowledgedAttempt(SessionRecord sameAck, Guid command)
        {
            lock (_gate)
            {
                if (_attemptBound) throw new InvalidOperationException("A resume selection may bind only one actual acknowledged attempt.");
                _current = CloneDenRecord(sameAck); _command = command; _attemptBound = true;
            }
        }
        public ICanonicalProjectTaskContextResumeCheckpoint OriginalCheckpoint => checkpoint;
        public AuthenticatedResourceActor Actor => original.Actor;
        public VerifiedResourceStoreOwnership OriginalDenOwnership => actualDenOwnership;
        public ResourceStoreIdentity OriginalStoreIdentity => checkpoint.OriginalStoreIdentity;
        public Conversation OriginalStudioConversation => original.Metadata.OriginalStudioConversation!;
        public ContainerDefinition OriginalStudioContainer => original.Metadata.OriginalStudioContainer!;
        public Conversation TaskConversation => checkpoint.OriginalTaskConversation;
        public ContainerDefinition TaskContainer => checkpoint.OriginalTaskContainer;
        public Guid OriginalCreationOperationId => checkpoint.OriginalCreationOperationId;
        public string DenId => checkpoint.Identity.DenId;
        public string NamespaceId => checkpoint.Identity.NamespaceId;
        public string DefinitionId => checkpoint.Identity.DefinitionId;
        public long DefinitionRevision => checkpoint.DefinitionRevision;
        public string SessionId => checkpoint.DenSessionId;
        // This read-only source observation advances once, after the actual CAS ACK.
        // Callers cannot mutate it, and every use is revalidated by the private source.
        public long MembershipRevision => Observe().Session.Revision;
    }
    private ResumeSelection RequireResumeSelection(ICanonicalProjectTaskContextResumeSelection selection) =>
        selection is ResumeSelection actual && ReferenceEquals(actual.Owner, this) &&
        _compatibleResumeSelections.TryGetValue(actual, out _) ? actual :
            throw new AssistantCommandRefusedException("The process source did not issue this same resume selection.");

    public Task<ICanonicalProjectTaskContextResumeSelection> PrepareOriginalResumeSelectionWithinSourceAsync(
        ICanonicalProjectTaskContextResumeCheckpoint sameCheckpoint, AuthenticatedResourceActor actualActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _originals.Admit<ICanonicalProjectTaskContextResumeSelection>(async () =>
        {
            var checkpoint = sameCheckpoint as AssistantCompatibleConversationCheckpoint
                ?? throw new AssistantCommandRefusedException("An alternate checkpoint implementation cannot issue resume authority.");
            var original = RequireCompatibleCheckpoint(checkpoint);
            if (actualActor != original.Actor) throw new AssistantCommandRefusedException("The exact current actor is required.");
            var sources = new Sources(_originals, scope, retain);
            var current = await ReadCompatibleCheckpointCurrentAsync(checkpoint, original, original.Session,
                "declined-pending", checkpoint.LastCommandOperationId, sources, token).ConfigureAwait(false);
            VerifiedResourceStoreOwnership? denOwnership = null;
            sources.Scope(() =>
            {
                if (!_home.TryObserveOriginalDenOwnership(current.Home, out denOwnership) || denOwnership is null ||
                    denOwnership.Receipt is null || denOwnership.ResourceKind != "den" || denOwnership.StoreId != checkpoint.Identity.DenId ||
                    denOwnership.ProfileId != actualActor.ProfileId)
                    throw new AssistantCommandRefusedException("The SAME actual healthy Home Open did not capture this Den receipt.");
            });
            var selection = new ResumeSelection(this, checkpoint, original, denOwnership!);
            sources.Scope(() => { });
            _originals.Invoke(() => { _compatibleResumeSelections.Add(selection, selection); return true; });
            return selection;
        });

    public bool IsIssuedOriginalResumeSelection(ICanonicalProjectTaskContextResumeSelection sameSelection) =>
        sameSelection is ResumeSelection actual && ReferenceEquals(actual.Owner, this) &&
        _compatibleResumeSelections.TryGetValue(actual, out _);

    public Task RevalidateOriginalResumeSelectionWithinSourceAsync(
        ICanonicalProjectTaskContextResumeSelection sameSelection, AuthenticatedResourceActor actualActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(async () =>
        {
            var selection = RequireResumeSelection(sameSelection);
            if (actualActor != selection.Actor) throw new AssistantCommandRefusedException("The exact current actor is required.");
            var current = selection.Observe();
            var sources = new Sources(_originals, scope, retain);
            var observed = await ReadCompatibleCheckpointCurrentAsync((AssistantCompatibleConversationCheckpoint)selection.OriginalCheckpoint,
                selection.Original, current.Session, current.AttemptBound ? "pending" : "declined-pending",
                current.Command, sources, token).ConfigureAwait(false);
            sources.Scope(() => DemandObservedResumeDenReceipt(selection, observed.Home));
            return true;
        });

    private async Task BindOriginalAcknowledgedResumeAttemptAsync(ResumeSelection selection,
        Guid commandOperation, Sources sources, CancellationToken token)
    {
        var checkpoint = (AssistantCompatibleConversationCheckpoint)selection.OriginalCheckpoint;
        var prior = selection.Observe();
        if (prior.AttemptBound || commandOperation == Guid.Empty || commandOperation == checkpoint.LastCommandOperationId)
            throw new AssistantCommandRefusedException("A distinct explicit resume command and one exact pending CAS ACK are required.");
        var home = await sources.Take(() => selection.Original.Membership.OpenHomeWithinSourceAsync(
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        if (home.Actor != selection.Actor) throw new AssistantCommandRefusedException("The actor changed before pending attempt acknowledgment.");
        var ack = await sources.Take(() => home.Den.GetAsync<SessionRecord>(checkpoint.Identity.NamespaceId,
            checkpoint.DenSessionId, token)).ConfigureAwait(false)
            ?? throw new AssistantCommandRefusedException("The original pending attempt ACK is unavailable.");
        var metadata = AssistantCanonicalMembershipSource.ReadMetadata<MembershipMetadata>(ack, AssistantCanonicalMembershipSource.SessionKey);
        var state = AssistantCanonicalMembershipSource.ReadMetadata<CheckpointMetadata>(ack, AssistantCanonicalMembershipSource.CompatibleCheckpointKey);
        sources.Scope(() =>
        {
            DemandCheckpointTuple(checkpoint, ack, metadata, state, "pending", requireOriginalRevision: false);
            var expectedMetadata = selection.Original.Metadata with { Publication = "pending" };
            var expectedState = selection.Original.Checkpoint with { LastCommandOperation = commandOperation, Reason = "" };
            if (ack.Revision != checked(prior.Session.Revision + 1) || !SameJson(metadata, expectedMetadata) || !SameJson(state, expectedState))
                throw new AssistantCommandRefusedException("The resume callback did not acknowledge exactly the original tuple's next pending CAS revision.");
            var expected = prior.Session with
            {
                Revision = ack.Revision, UpdatedAtUtc = ack.UpdatedAtUtc, OriginDeviceId = ack.OriginDeviceId,
                ExtensionData = ack.ExtensionData
            };
            if (!SameDenRecord(expected, ack) || !SameOtherExtensions(prior.Session, ack))
                throw new AssistantCommandRefusedException("The pending CAS changed original session content or unrelated metadata.");
            selection.BindAcknowledgedAttempt(ack, commandOperation);
        });
        var current = selection.Observe();
        await ReadCompatibleCheckpointCurrentAsync(checkpoint, selection.Original, current.Session,
            "pending", current.Command, sources, token).ConfigureAwait(false);
    }

    public Task<ICanonicalProjectTaskContextResumeRevisionPin> AcquireOriginalResumeRevisionPinWithinSourceAsync(
        ICanonicalProjectTaskContextResumeSelection sameSelection, AuthenticatedResourceActor actualActor,
        Action<Action> scope, Action<Task> retain,
        Action<ICanonicalProjectTaskContextResumeRevisionPin> captureOriginalPin, CancellationToken token) =>
        _originals.Admit<ICanonicalProjectTaskContextResumeRevisionPin>(async () =>
        {
            ArgumentNullException.ThrowIfNull(captureOriginalPin);
            var selection = RequireResumeSelection(sameSelection);
            var observed = selection.Observe();
            if (!observed.AttemptBound || actualActor != selection.Actor)
                throw new AssistantCommandRefusedException("The SAME acknowledged pending resume attempt and actor are required before pinning.");
            var sources = new Sources(_originals, scope, retain);
            var current = await ReadCompatibleCheckpointCurrentAsync((AssistantCompatibleConversationCheckpoint)selection.OriginalCheckpoint,
                selection.Original, observed.Session, "pending", observed.Command, sources, token).ConfigureAwait(false);
            sources.Scope(() => DemandObservedResumeDenReceipt(selection, current.Home));
            ResumeRevisionPin? captured = null;
            var errors = new List<Exception>();
            try
            {
                await sources.Capture(() => current.Home.Den.Store.PinOriginalAssistantRevisionsAsync(
                    current.Definition, current.Session, current.Home.Den.AccessPolicy, current.Home.Den.PrincipalId,
                    sources.Scope, sources.Retain, actualPin =>
                    {
                        captured = new(this, selection, actualPin, current.Session.Revision);
                        _compatibleResumePins.Add(captured, captured);
                        _originals.Observe(captured.CloseOriginalWithinOwnerAsync, () => captured.OriginalClose);
                        captureOriginalPin(captured); // capture actual product before any post-scope refusal
                    }, token), _ => { }).ConfigureAwait(false);
            }
            catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0)
            {
                if (captured is not null)
                    try { await captured.CloseOriginalWithinOwnerAsync().ConfigureAwait(false); }
                    catch (Exception cause) { errors.Add(cause); }
                ThrowResumeCauses(errors);
            }
            return captured ?? throw new InvalidOperationException("No actual Den-owned resume revision pin was captured.");
        });

    private void DemandObservedResumeDenReceipt(ResumeSelection selection, HomePersonalDenSession sameHome)
    {
        if (!_home.TryObserveOriginalDenOwnership(sameHome, out var observed) || observed != selection.OriginalDenOwnership)
            throw new AssistantCommandRefusedException("The genuine current Home Open receipt changed before this resume.");
    }

    private sealed class ResumeRevisionPin(DenAssistantOriginalDevelopmentOwner owner, ResumeSelection selection,
        DenStore.AssistantRevisionPin actualPin, long actualRevision) : ICanonicalProjectTaskContextResumeRevisionPin
    {
        private readonly CompatibleResumePinCloseOriginal _closeOriginal = new(
            () => Physical(owner._originals, actualPin.CloseAndDrainAsync), owner._originals.Retain);
        internal DenAssistantOriginalDevelopmentOwner Owner => owner;
        internal ResumeSelection Selection => selection;
        public ICanonicalProjectTaskContextResumeSelection OriginalSelection => selection;
        public Task? OriginalClose => _closeOriginal.OriginalClose;
        internal bool IsOpen => _closeOriginal.OriginalClose is null;
        internal void DemandCurrent()
        {
            if (!IsOpen || selection.MembershipRevision != actualRevision ||
                actualPin.DefinitionId != selection.DefinitionId || actualPin.DefinitionRevision != selection.DefinitionRevision ||
                actualPin.SessionId != selection.SessionId || actualPin.SessionRevision != actualRevision)
                throw new ObjectDisposedException(nameof(ResumeRevisionPin), "The exact original revision pin is no longer current.");
            actualPin.DemandOriginalPinnedRevisions();
        }
        public Task CloseAndDrainAsync() => _closeOriginal.CloseAndDrainAsync();
        internal Task CloseOriginalWithinOwnerAsync() => _closeOriginal.CloseAndDrainAsync();
        public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    }
    // This child owns only its SAME finite Den release. The caller may legitimately
    // close it inside a process Sources.Scope; it never joins that parent's ledger.
    // Logical and physical source guards apply to this exact child close instead.
    internal sealed class CompatibleResumePinCloseOriginal(Func<Task> acquireActualChild, Action<Task> retainActualChild)
    {
        private readonly object _gate = new();
        private readonly AsyncLocal<Invocation?> _logicalSource = new();
        [ThreadStatic] private static List<CompatibleResumePinCloseOriginal>? _physicalSources;
        private sealed class Invocation { internal bool Active = true; }
        private Task? _close, _rawClose;
        internal Task? OriginalClose { get { lock (_gate) return _close; } }
        internal void DemandExternalJoin()
        {
            if (_logicalSource.Value is { Active: true } ||
                _physicalSources?.Any(source => ReferenceEquals(source, this)) == true)
                throw new InvalidOperationException("An actual resume pin close source cannot join its own child close.");
        }
        internal Task CloseAndDrainAsync()
        {
            DemandExternalJoin();
            TaskCompletionSource? start = null; Task result;
            lock (_gate)
            {
                if (_close is null)
                { start = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = CloseCoreAsync(start.Task); }
                result = _close;
            }
            start?.SetResult(); return result;
        }
        private T Invoke<T>(Func<T> body)
        {
            var stack = _physicalSources ??= []; stack.Add(this);
            try { return body(); }
            finally { stack.RemoveAt(stack.Count - 1); }
        }
        private async Task CloseCoreAsync(Task start)
        {
            await start.ConfigureAwait(false);
            var invocation = new Invocation(); _logicalSource.Value = invocation;
            var errors = new List<Exception>();
            try
            {
                try
                {
                    Invoke(() =>
                    {
                        _rawClose = acquireActualChild() ?? throw new InvalidOperationException("The actual Den pin returned no close original.");
                        retainActualChild(_rawClose); // Capture SAME raw before any retainer callback can refuse.
                        return true;
                    });
                }
                catch (Exception cause) { errors.Add(cause); }
                if (_rawClose is not null)
                    try { await _rawClose.ConfigureAwait(false); }
                    catch (Exception cause)
                    {
                        if (_rawClose.IsFaulted && _rawClose.Exception is { } payload) errors.AddRange(payload.InnerExceptions);
                        else errors.Add(cause);
                    }
                ThrowResumeCauses(errors); // Faulted OCE and every direct raw sibling stay failures.
            }
            finally { invocation.Active = false; _logicalSource.Value = null; }
        }
    }
    public bool IsIssuedOriginalResumeRevisionPin(ICanonicalProjectTaskContextResumeSelection sameSelection,
        ICanonicalProjectTaskContextResumeRevisionPin samePin) =>
        samePin is ResumeRevisionPin actual && ReferenceEquals(actual.Owner, this) &&
        ReferenceEquals(actual.Selection, sameSelection) && _compatibleResumePins.TryGetValue(actual, out _) && actual.IsOpen;
    public void DemandOriginalPinnedResumeSelection(ICanonicalProjectTaskContextResumeSelection sameSelection,
        ICanonicalProjectTaskContextResumeRevisionPin samePin)
    {
        _ = RequireResumeSelection(sameSelection);
        if (!IsIssuedOriginalResumeRevisionPin(sameSelection, samePin))
            throw new UnauthorizedAccessException("Only the SAME issued live revision pin may guard this atomic child.");
        ((ResumeRevisionPin)samePin).DemandCurrent();
    }

    private static SessionRecord CloneDenRecord(SessionRecord record) =>
        JsonSerializer.Deserialize<DenRecord>(JsonSerializer.SerializeToUtf8Bytes<DenRecord>(record, DenJson.Options), DenJson.Options)
            as SessionRecord ?? throw new InvalidDataException("The exact canonical session could not retain its record type.");
    private static bool SameDenRecord(SessionRecord left, SessionRecord right) =>
        JsonSerializer.SerializeToUtf8Bytes<DenRecord>(left, DenJson.Options).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes<DenRecord>(right, DenJson.Options));
    private static bool SameJson<T>(T left, T right) =>
        JsonSerializer.SerializeToUtf8Bytes(left, DenJson.Options).AsSpan()
            .SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(right, DenJson.Options));
    private static bool SameOtherExtensions(SessionRecord prior, SessionRecord current)
    {
        var left = prior.ExtensionData?.Where(value => value.Key != AssistantCanonicalMembershipSource.SessionKey &&
            value.Key != AssistantCanonicalMembershipSource.CompatibleCheckpointKey).ToDictionary(value => value.Key, value => value.Value);
        var right = current.ExtensionData?.Where(value => value.Key != AssistantCanonicalMembershipSource.SessionKey &&
            value.Key != AssistantCanonicalMembershipSource.CompatibleCheckpointKey).ToDictionary(value => value.Key, value => value.Value);
        return SameJson(left, right);
    }
    private static void ThrowResumeCauses(IEnumerable<Exception> errors)
    {
        var causes = errors.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (causes.Length == 1 && causes[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(causes[0]).Throw();
        if (causes.Length != 0) throw new AggregateException("The actual resume original and independent custody failed.", causes);
    }
}
