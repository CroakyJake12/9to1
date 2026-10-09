using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Memory;

public sealed partial class AssistantOriginalMemorySource : ICanonicalAssistantMemoryWriteSource, IAssistantOriginalMemoryCapabilitySource
{
    private readonly object _writeGate = new();
    private ICanonicalAssistantMemoryHomeWriteSource? _writes;
    private readonly Dictionary<Guid, WriteIntent> _writeOperations = [];
    private readonly ConditionalWeakTable<WriteIntent, WriteAttempt> _writeAttempts = new();
    private readonly ConditionalWeakTable<Task, WriteAttempt> _writePublicTasks = new();
    private readonly ConditionalWeakTable<Task, WriteAttempt> _writeInnerTasks = new();
    private readonly ConditionalWeakTable<Task, WriteInvocation> _homeWriteTasks = new();
    private readonly ConditionalWeakTable<Task<ICanonicalAssistantMemoryWriteAcknowledgment>, WriteInvocation> _atomicWrites = new();
    private readonly AsyncLocal<WriteInvocation?> _activeWrite = new();
    [ThreadStatic] private static Dictionary<AssistantOriginalMemorySource, WriteInvocation>? _physicalWrites;
    private const string MemoryWriteReceiptPrefix = "canonical.sqlite.assistant-memory.create.v1.";
    public void BindOriginalHomeWriteSource(ICanonicalAssistantMemoryHomeWriteSource actualWrites)
    {
        ArgumentNullException.ThrowIfNull(actualWrites);
        lock (_writeGate)
        {
            if (_writes is not null) throw new InvalidOperationException("The original memory WRITE owner is already composed.");
            _writes = actualWrites;
        }
    }
    public bool HasOriginalHomeWriteSource(ICanonicalAssistantMemoryHomeWriteSource actual) => ReferenceEquals(_writes, actual);
    public AssistantCapabilityObservation ObserveOriginalMemoryCapability() => new("Own Assistant memory",
        _knowledge.HasOriginalLocalAssistantMemoryWriter(_store) ? AssistantSupportState.RequiresAuthorization : AssistantSupportState.Unsupported,
        _knowledge.HasOriginalLocalAssistantMemoryWriter(_store)
            ? _writes is null ? "Scoped local reads require actual Home import; adding records awaits the separate Home WRITE owner."
                : "Scoped local reads require Home import; each new private record requires its separate Home approval."
            : "The SAME maintained Knowledge store and configured local embedding owner are required.");
    private sealed record MemoryWriteReceipt(int Schema, Guid StoreId, AuthenticatedResourceActor Actor,
        string DenId, string NamespaceId, string DefinitionId, long DefinitionRevision, string SessionId,
        long SessionRevision, Guid ConversationId, Guid OperationId, KnowledgeRecord Record, RetrievalDocument Document,
        CanonicalAssistantMemoryMutationKind MutationKind = CanonicalAssistantMemoryMutationKind.Create,
        KnowledgeRecord? OriginalRecord = null, string? OriginalRecordSha256 = null, string? OriginalRawJson = null);
    private sealed class WriteIntent(AssistantOriginalMemorySource owner, Input input, Conversation conversation,
        VerifiedResourceStoreOwnership denPermission,
        KnowledgeLibraryService.AssistantMemoryCreatePreparation? prepared, MemoryWriteReceipt receipt,
        KnowledgeLibraryService.AssistantMemoryOriginalRecord? original = null)
        : ICanonicalAssistantMemoryRevisionWriteIntent
    {
        internal AssistantOriginalMemorySource Owner => owner;
        internal Input Input => input;
        internal KnowledgeLibraryService.AssistantMemoryCreatePreparation? Prepared => prepared;
        internal KnowledgeLibraryService.AssistantMemoryOriginalRecord? Original => original;
        internal MemoryWriteReceipt Durable => receipt;
        public AuthenticatedResourceActor Actor => input.Actor;
        public ResourceStoreIdentity OriginalStoreIdentity => input.Store;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => input.Permission;
        public VerifiedResourceStoreOwnership OriginalDenOwnership => denPermission;
        public string DenId => receipt.DenId;
        public string NamespaceId => receipt.NamespaceId;
        public string DefinitionId => receipt.DefinitionId;
        public long DefinitionRevision => receipt.DefinitionRevision;
        public string OriginalStorageScope => input.ReadScope.StorageScope;
        public Guid OperationId => receipt.OperationId;
        public KnowledgeRecord Candidate => receipt.Record;
        public CanonicalAssistantMemoryMutationKind MutationKind => receipt.MutationKind;
        public KnowledgeRecord? OriginalRecord => receipt.OriginalRecord;
        public string? ExpectedOriginalRecordSha256 => receipt.OriginalRecordSha256;
        public IChatOriginalPersistentMemoryInput OriginalProductInput => input;
        public IChatOriginalPersistentMemorySource OriginalProductSource => owner;
        public Conversation OriginalConversation => conversation;
        public ProviderExecutionContext? OriginalCanonicalContext => null;
    }
    private sealed class WriteAcknowledgment(WriteIntent intent, bool applied = true, string reason = "The approved memory operation was committed.") : ICanonicalAssistantMemoryWriteDecisionAcknowledgment
    {
        public ICanonicalAssistantMemoryWriteIntent OriginalIntent => intent;
        public KnowledgeRecord Record => intent.Candidate;
        public RetrievalDocument OriginalRetrievalDocument => intent.Durable.Document;
        public Guid OperationId => intent.OperationId;
        public bool Applied => applied;
        public string Reason => reason;
    }
    private sealed class WriteAttempt
    {
        internal Task<ICanonicalAssistantMemoryWriteAcknowledgment> Driver = null!;
        internal Task<ICanonicalAssistantMemoryWriteAcknowledgment>? Inner;
        internal WriteInvocation Invocation = null!;
    }
    private sealed class WriteInvocation(WriteIntent intent)
    {
        internal WriteIntent Intent => intent;
        internal CanonicalSqliteOriginalStoreLease? Sqlite;
        internal DenStore.AssistantRevisionPin? Den;
        internal ICanonicalAssistantMemoryHomeWriteClaim? Claim;
        internal Task<ICanonicalAssistantMemoryWriteAcknowledgment>? Atomic;
        internal WriteAcknowledgment? Acknowledgment;
        internal int OriginalDispatchSettled;
        internal readonly TaskCompletionSource OriginalDispatchBarrier = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task? OriginalDispatchWait;
        internal bool OriginalDispatchWaitHealthy;
        internal ICanonicalOriginalWriteSettlementReleasePhase<ICanonicalAssistantMemoryWriteIntent, ICanonicalAssistantMemoryWriteAcknowledgment>? SettlementPhase;
        internal Task? SettlementRelease;
        internal Task? OriginalDenRelease;
        internal bool SettlementReleaseHealthy;
    }
    public bool IsIssuedOriginalWriteIntent(ICanonicalAssistantMemoryWriteIntent intent) =>
        intent is WriteIntent actual && ReferenceEquals(actual.Owner, this) && IsIssuedOriginalInput(actual.Input);
    private WriteIntent DemandWriteIntent(ICanonicalAssistantMemoryWriteIntent intent) =>
        IsIssuedOriginalWriteIntent(intent) ? (WriteIntent)intent : throw new UnauthorizedAccessException("The SAME Assistant memory producer did not issue this write intent.");
    public string GetOriginalWriteIntentDigest(ICanonicalAssistantMemoryWriteIntent intent) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(DemandWriteIntent(intent).Durable)));
    public bool IsOriginalAtomicWriteTask(ICanonicalAssistantMemoryWriteIntent intent, Task<ICanonicalAssistantMemoryWriteAcknowledgment> actual) =>
        IsIssuedOriginalWriteIntent(intent) && _atomicWrites.TryGetValue(actual, out var invocation) &&
        ReferenceEquals(invocation.Intent, intent) && ReferenceEquals(invocation.Atomic, actual);
    public bool IsOwnedOriginalWriteAcknowledgment(ICanonicalAssistantMemoryWriteIntent intent,
        ICanonicalAssistantMemoryWriteAcknowledgment acknowledgment, Task<ICanonicalAssistantMemoryWriteAcknowledgment> actual) =>
        IsOriginalAtomicWriteTask(intent, actual) && _atomicWrites.TryGetValue(actual, out var invocation) &&
        ReferenceEquals(invocation.Acknowledgment, acknowledgment) && ReferenceEquals(acknowledgment.OriginalIntent, intent);
    public bool IsAcknowledgedOriginalWriteRefusal(Task actual)
    {
        if (!actual.IsFaulted) return false;
        if (_homeWriteTasks.TryGetValue(actual, out _) && _writes?.IsAcknowledgedOriginalWriteRefusal(actual) == true) return true;
        if (_writeInnerTasks.TryGetValue(actual, out var same) && ReferenceEquals(same.Inner, actual))
            return _store.IsAcknowledgedOriginalCommandRefusal(actual);
        return _writePublicTasks.TryGetValue(actual, out var attempt) && ReferenceEquals(attempt.Driver, actual) &&
            attempt.Inner is { } inner && _store.IsAcknowledgedOriginalCommandRefusal(inner) &&
            actual.Exception!.InnerExceptions.Count == 1 && inner.Exception!.InnerExceptions.Count == 1 &&
            ReferenceEquals(actual.Exception.InnerExceptions[0], inner.Exception.InnerExceptions[0]);
    }

    public Task<ICanonicalAssistantMemoryWriteIntent> PrepareOriginalWriteWithinSourceAsync(
        IChatOriginalPersistentMemoryInput sameInput, Conversation sameConversation, string title, string summary,
        Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        WriteSourceAsync<ICanonicalAssistantMemoryWriteIntent>(scope, retain, async source =>
        {
            var input = DemandInput(sameInput);
            if (sameConversation.Mode != HavenMode.Chat || sameConversation.Kind != ConversationKind.Chat)
                throw new NotSupportedException("Explicit memory editing currently requires the Assistant's actual ordinary conversation.");
            if (operationId == Guid.Empty || string.IsNullOrWhiteSpace(title) || title.Length > 160 ||
                string.IsNullOrWhiteSpace(summary) || summary.Length > 4000)
                throw new ArgumentException("A nonempty operation and bounded memory title/value are required.");
            await source.Read(() => ValidateOriginalWithinSourceAsync(input, sameConversation, null, source.Run, source.Retain, token)).ConfigureAwait(false);
            lock (_writeGate)
            {
                if (_writeOperations.TryGetValue(operationId, out var prior))
                {
                    if (!ReferenceEquals(prior.Input, input) || prior.OriginalConversation != sameConversation ||
                        prior.MutationKind != CanonicalAssistantMemoryMutationKind.Create || prior.Candidate.Title != title.Trim() || prior.Candidate.Summary != summary.Trim())
                        throw new InvalidOperationException("Preserve the original operation; its draft and scoped input cannot be replaced.");
                    return prior;
                }
                if (_writeOperations.Count >= 128) throw new InvalidOperationException("This source's prepared memory operations require retirement.");
            }
            var denPermission = await source.Read(() => _ownership.GetVerifiedWithinOriginalSourceAsync(
                "den", input.Binding.Definition.Identity.DenId, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
            if (denPermission?.Receipt is null || denPermission.ResourceKind != "den" ||
                denPermission.StoreId != input.Binding.Definition.Identity.DenId || denPermission.ProfileId != input.Actor.ProfileId ||
                !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(denPermission, input.Actor,
                    source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The actual current Den ownership receipt is required for this exact memory write.");
            var now = DateTimeOffset.UtcNow;
            var record = new KnowledgeRecord(Guid.NewGuid(), KnowledgeCategory.LearnMe, "Assistant preference", title.Trim(), summary.Trim(),
                KnowledgePrivacyClass.Private, 1, false, now, now, null, "Explicitly added by the current user to this Assistant",
                Array.AsReadOnly(new[] { new KnowledgeSource(sameConversation.Id.ToString("D"), "Assistant conversation", "assistant-conversation",
                    null, null, now, now, null, "ExplicitUser") }), Scope: input.ReadScope.StorageScope, Origin: KnowledgeOrigin.Explicit,
                LastConfirmedAt: now, IsUserLocked: true, AppId: KnowledgeLibraryService.AssistantMemoryApplicationId,
                AgentId: input.Binding.Definition.Identity.DefinitionId);
            var prepared = await source.Read(() => _knowledge.PrepareOriginalAssistantMemoryCreateAsync(input.ReadScope, record, source, token)).ConfigureAwait(false);
            await source.Read(() => ValidateOriginalWithinSourceAsync(input, sameConversation, null, source.Run, source.Retain, token)).ConfigureAwait(false);
            var identity = input.Binding.Definition.Identity;
            var receipt = new MemoryWriteReceipt(1, input.Store.StoreId, input.Actor, identity.DenId, identity.NamespaceId,
                identity.DefinitionId, input.Binding.Definition.Revision, input.Binding.DenSessionId, input.Binding.DenSessionRevision,
                sameConversation.Id, operationId, prepared.Record, prepared.IndexPreparation.Document);
            var intent = new WriteIntent(this, input, sameConversation, denPermission, prepared, receipt);
            lock (_writeGate)
            {
                if (_writeOperations.ContainsKey(operationId)) throw new InvalidOperationException("The original operation was concurrently prepared; refresh its original preview.");
                _writeOperations.Add(operationId, intent);
            }
            return intent;
        });
    public Task ValidateOriginalWriteIntentWithinSourceAsync(ICanonicalAssistantMemoryWriteIntent intent,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = DemandWriteIntent(intent);
        return RunAsync(scope, retain, async source =>
        {
            await source.Read(() => ValidateOriginalWithinSourceAsync(actual.Input, actual.OriginalConversation, null,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            var den = actual.OriginalDenOwnership;
            if (den.Receipt is null || den.ResourceKind != "den" || den.StoreId != actual.DenId || den.ProfileId != actual.Actor.ProfileId ||
                !await source.Read(() => _ownership.IsCurrentWithinOriginalSourceAsync(den, actual.Actor,
                    source.Run, source.Retain, token).AsTask()).ConfigureAwait(false))
                throw new UnauthorizedAccessException("The exact captured Den ownership receipt changed before this write.");
            return true;
        });
    }
    private Task<T> WriteSourceAsync<T>(Action<Action> scope, Action<Task> retain,
        Func<CanonicalSqliteOriginalSourceScope, Task<T>> body) => _originals.Admit(async () =>
    {
        var product = _originals.CreateScope(scope, retain);
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, product.Run, product.Retain);
        return await _store.RetainOriginalReader(source, async () =>
        {
            T result = default!; var errors = new List<Exception>();
            try { result = await body(source).ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            product.BeginOriginalCleanup();
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            CanonicalSqliteOriginalStoreOwner.Throw(errors); return result;
        }).ConfigureAwait(false);
    });

    public Task<ICanonicalAssistantMemoryWriteAcknowledgment> CommitOriginalWriteWithinSourceAsync(
        ICanonicalAssistantMemoryWriteIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = DemandWriteIntent(intent); WriteAttempt attempt; TaskCompletionSource begin;
        lock (_writeGate)
        {
            if (_writeAttempts.TryGetValue(actual, out var existing)) return existing.Driver;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously); attempt = new();
            var invocation = new WriteInvocation(actual); attempt.Invocation = invocation;
            attempt.Driver = _originals.Admit(async () =>
            {
                await begin.Task.ConfigureAwait(false);
                var product = _originals.CreateScope(scope, retain);
                var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner,
                    body => WithinOriginalWrite(invocation, () => product.Run(body)), product.Retain);
                attempt.Inner = _store.RetainOriginalReader<ICanonicalAssistantMemoryWriteAcknowledgment>(source,
                    () => WriteDriverAsync(invocation, source, product, token));
                _writeInnerTasks.Add(attempt.Inner, attempt);
                return await attempt.Inner.ConfigureAwait(false);
            });
            _writeAttempts.Add(actual, attempt); _writePublicTasks.Add(attempt.Driver, attempt);
            _originals.RegisterOriginalRefusalProof(attempt.Driver, IsAcknowledgedOriginalWriteRefusal);
        }
        begin.SetResult(); return attempt.Driver;
    }
    private async Task<ICanonicalAssistantMemoryWriteAcknowledgment> WriteDriverAsync(WriteInvocation invocation,
        CanonicalSqliteOriginalSourceScope source, AssistantMemoryOriginals.Scope product, CancellationToken token)
    {
        var previous = _activeWrite.Value; _activeWrite.Value = invocation;
        var errors = new List<Exception>(); ICanonicalAssistantMemoryWriteAcknowledgment? result = null;
        var intent = invocation.Intent;
        try
        {
            var writes = _writes ?? throw new InvalidOperationException("The separate actual Home memory WRITE source is not composed.");
            await source.Read(() => ValidateOriginalWriteIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
            Task<ICanonicalAssistantMemoryHomeWriteClaim>? rawApproval = null;
            try
            {
                await source.ReadCapture(() =>
                {
                    rawApproval = writes.AcquireOriginalWriteWithinSourceAsync(intent, source.Run, source.Retain,
                        claim => { invocation.Claim = claim; source.CaptureOriginalResource(claim); }, token);
                    _homeWriteTasks.Add(rawApproval, invocation); return rawApproval;
                },
                    claim => invocation.Claim = claim).ConfigureAwait(false);
            }
            catch (Exception)
            {
                if (rawApproval is null || invocation.Claim is null || !source.InvokeOwningCleanup(() => writes.IsAcknowledgedOriginalWriteRefusal(rawApproval))) throw;
                product.BeginOriginalCleanup();
                MarkOriginalDispatchSettled(invocation);
                await source.CloseAsync(invocation.Claim).ConfigureAwait(false);
                if (!source.AcknowledgeOriginalExternalPreEffectRefusal(rawApproval, writes.IsAcknowledgedOriginalWriteRefusal)) throw;
                await source.JoinAllAsync().ConfigureAwait(false);
                if (!source.IsHealthySettled) throw;
                var refused = new InvalidOperationException("The separate Home memory WRITE was declined before any SQL effect.");
                source.RegisterOriginalPreEffectRefusal(refused); throw refused;
            }
            if (!writes.IsIssuedOriginalWriteClaim(invocation.Claim!, intent)) throw new UnauthorizedAccessException("The SAME individual Home memory WRITE claim is required.");
            await source.Read(() => ValidateOriginalWriteIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
            // Capture canonical Den observations before its writer pin. Never reopen Home,
            // reobserve ownership or validate product membership while that pin is held.
            var home = await source.Read(() => intent.Input.Membership.OpenHomeWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
            if (home.Actor != intent.Actor) throw new UnauthorizedAccessException("The actual Home actor changed before memory commit.");
            var definition = await source.Read(() => intent.Input.Membership.DefinitionWithinSourceAsync(home,
                intent.Input.Binding.Definition.Identity, source.Run, source.Retain, token)).ConfigureAwait(false);
            var session = await source.Read(() => home.Den.GetAsync<SessionRecord>(intent.NamespaceId,
                intent.Input.Binding.DenSessionId, token)).ConfigureAwait(false);
            if (definition.Revision != intent.DefinitionRevision || session is null || session.Revision != intent.Input.Binding.DenSessionRevision)
                throw new UnauthorizedAccessException("The actual Assistant definition or membership revision changed before the pin.");
            await source.ReadCapture(() => _store.AcquireOriginalProtectedWriterPinWithinSourceAsync(intent.Actor,
                source.Run, source.Retain, token), pin => invocation.Sqlite = pin).ConfigureAwait(false);
            if (invocation.Sqlite!.OriginalIdentity != intent.OriginalStoreIdentity) throw new UnauthorizedAccessException("The actual memory SQLite identity changed.");
            await source.ReadCapture(() => home.Den.Store.PinOriginalAssistantRevisionsAsync(definition, session,
                home.Den.AccessPolicy, home.Den.PrincipalId, source.Run, source.Retain,
                pin => { invocation.Den = pin; source.CaptureOriginalResource(pin); }, token), pin => invocation.Den = pin).ConfigureAwait(false);
            DemandOriginalWriteCommit(intent);
            await source.Read(() => writes.AcquireOriginalCommitEntryWithinSourceAsync(invocation.Claim!, source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.Read(() => writes.ValidateOriginalCommitWithinSourceAsync(invocation.Claim!, source.Run, source.Retain, token)).ConfigureAwait(false);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var raw = AtomicMemoryWriteAsync(start.Task, invocation, source, writes, token);
            invocation.Atomic = raw; _atomicWrites.Add(raw, invocation);
            Exception? publication = null;
            try { source.Run(() => { writes.RetainOriginalSqlCommit(invocation.Claim!, raw); source.Retain(raw); }); }
            catch (Exception failure) { publication = failure; }
            start.SetResult(publication is null);
            try { result = await raw.ConfigureAwait(false); } catch (Exception failure) { CanonicalSqliteOriginalStoreOwner.Capture(errors, raw, failure); }
            if (publication is not null) errors.Add(publication);
            product.BeginOriginalCleanup();
            MarkOriginalDispatchSettled(invocation);
            try { await source.Read(() => writes.CompleteOriginalWriteWithinSourceAsync(invocation.Claim!, raw,
                source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false); }
            catch (Exception failure) { errors.Add(failure); }
        }
        catch (Exception failure) { errors.Add(failure); }
        finally
        {
            // The driver has left productive admission. Every created atomic child was
            // independently awaited above before Home may request the SAME pin release.
            product.BeginOriginalCleanup();
            MarkOriginalDispatchSettled(invocation);
            if (invocation.Claim is not null) try { await source.CloseAsync(invocation.Claim).ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            if (invocation.Den is not null) try { await source.CloseAsync(invocation.Den).ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            if (invocation.Sqlite is not null) try { await invocation.Sqlite.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception failure) { errors.Add(failure); }
            _activeWrite.Value = previous;
        }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
        return result ?? throw new InvalidOperationException("No acknowledged original memory write was returned.");
    }
    private async Task<ICanonicalAssistantMemoryWriteAcknowledgment> AtomicMemoryWriteAsync(Task<bool> start,
        WriteInvocation invocation, CanonicalSqliteOriginalSourceScope source,
        ICanonicalAssistantMemoryHomeWriteSource writes, CancellationToken token)
    {
        if (!await start.ConfigureAwait(false)) throw new InvalidOperationException("Atomic memory dispatch was declined before SQL effects.");
        var intent = invocation.Intent; var pin = invocation.Sqlite!;
        void Demand() { DemandOriginalWriteCommit(intent); writes.DemandOriginalCommit(invocation.Claim!, intent); }
        // Migration can hold SQLite while waiting for Den. Canonical Tasks hold Home
        // before SQLite, so this memory leaf must not introduce an unbounded reverse wait.
        await ConfigureOriginalBoundedWriterWaitAsync(pin, Demand, token).ConfigureAwait(false);
        source.Run(() => { token.ThrowIfCancellationRequested(); Demand(); pin.BeginPinnedOriginalWriter(Demand); });
        if (intent.Original is { } original && !await _knowledge.IsCurrentOriginalAssistantMemoryRecordAsync(pin, original, token).ConfigureAwait(false))
        {
            // Exact acknowledged no-effect result: raw rollback settles before ACK.
            // Any rollback or source callback fault remains an original unknown failure.
            await pin.ReadOriginalSourceAsync(() => pin.Transaction.RollbackAsync(CancellationToken.None)).ConfigureAwait(false);
            var declined = new WriteAcknowledgment(intent, false, "This memory changed after preview. Refresh and review the current record before trying again.");
            source.Run(() => { Demand(); invocation.Acknowledgment = declined; }); return declined;
        }
        await KnowledgeLibraryService.EnsureOriginalAssistantMemorySchemaWithinTransactionAsync(pin, Demand, token).ConfigureAwait(false);
        // Exact canonical conversation is rechecked inside SAME serialized SQLite writer.
        var rawConversation = await KnowledgeLibraryService.ExecuteAssistantMemoryScalarAsync(pin, command =>
        {
            Demand(); command.CommandText = "SELECT COUNT(*) FROM conversations WHERE id=$id AND mode=$mode AND kind=$kind AND is_temporary=0 AND is_archived=0 AND container_id IS $container AND space_id IS $space AND lesson_id IS $lesson;";
            var conversation = intent.OriginalConversation;
            command.Parameters.AddWithValue("$id", conversation.Id.ToString()); command.Parameters.AddWithValue("$mode", (int)conversation.Mode);
            command.Parameters.AddWithValue("$kind", (int)conversation.Kind); command.Parameters.AddWithValue("$container", conversation.ContainerId?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$space", conversation.SpaceId?.ToString() ?? (object)DBNull.Value); command.Parameters.AddWithValue("$lesson", conversation.LessonId?.ToString() ?? (object)DBNull.Value);
        }, token).ConfigureAwait(false);
        if (Convert.ToInt64(rawConversation) != 1) throw new UnauthorizedAccessException("The actual Assistant conversation changed before atomic memory creation.");
        if (intent.Prepared is { } prepared)
            await _knowledge.InsertOriginalAssistantMemoryWithinTransactionAsync(pin, prepared, Demand, token).ConfigureAwait(false);
        if (intent.Original is { } retired)
            await _knowledge.RetireOriginalAssistantMemoryWithinTransactionAsync(pin, retired, intent.Candidate, intent.OperationId,
                intent.MutationKind == CanonicalAssistantMemoryMutationKind.Correct ? KnowledgeRecordStatus.Superseded : KnowledgeRecordStatus.Rejected,
                Demand, token).ConfigureAwait(false);
        await KnowledgeLibraryService.ExecuteAssistantMemoryInsertAsync(pin, command =>
        {
            Demand(); command.CommandText = "INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$at);";
            command.Parameters.AddWithValue("$key", (intent.MutationKind == CanonicalAssistantMemoryMutationKind.Create ? MemoryWriteReceiptPrefix : "canonical.sqlite.assistant-memory.revision.v1.") + intent.OperationId.ToString("N"));
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(intent.Durable));
            command.Parameters.AddWithValue("$at", intent.Candidate.CreatedAt.ToString("O"));
        }, token).ConfigureAwait(false);
        await pin.CommitPinnedOriginalAsync(Demand, token).ConfigureAwait(false);
        var acknowledgment = new WriteAcknowledgment(intent);
        source.Run(() => { Demand(); invocation.Acknowledgment = acknowledgment; }); return acknowledgment;
    }
    public void DemandOriginalWriteCommit(ICanonicalAssistantMemoryWriteIntent intent)
    {
        var actual = DemandWriteIntent(intent);
        var invocation = _physicalWrites?.TryGetValue(this, out var physical) == true ? physical : _activeWrite.Value;
        if (invocation is null || !ReferenceEquals(invocation.Intent, actual) || invocation.Sqlite is null || invocation.Den is null ||
            !_store.IsIssuedOriginalLease(invocation.Sqlite) || invocation.Sqlite.OriginalIdentity != actual.OriginalStoreIdentity ||
            invocation.Den.DefinitionId != actual.DefinitionId || invocation.Den.DefinitionRevision != actual.DefinitionRevision ||
            invocation.Den.SessionId != actual.Input.Binding.DenSessionId || invocation.Den.SessionRevision != actual.Input.Binding.DenSessionRevision)
            throw new UnauthorizedAccessException("The SAME live memory write driver and exact definition/session pins are required.");
        invocation.Sqlite.DemandPinnedOriginalPhysical(); invocation.Den.DemandOriginalPinnedRevisions();
    }
    private void WithinOriginalWrite(WriteInvocation invocation, Action body)
    {
        var map = _physicalWrites ??= new(ReferenceEqualityComparer.Instance);
        map.TryGetValue(this, out var previous); map[this] = invocation;
        try { body(); } finally { if (previous is null) map.Remove(this); else map[this] = previous; }
    }
}
