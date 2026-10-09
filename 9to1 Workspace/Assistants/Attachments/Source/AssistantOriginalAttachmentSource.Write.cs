using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using NineToOne.Dulche.Den;

namespace HavenOS.Apps.Assistants.Attachments;

public sealed partial class AssistantOriginalAttachmentSource
{
    public Task<ICanonicalAttachmentImportAcknowledgment> CommitOriginalImportWithinSourceAsync(
        ICanonicalAttachmentImportIntent intent, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var actual = DemandWriteIntent(intent); WriteAttempt attempt; TaskCompletionSource begin;
        lock (_writeGate)
        {
            if (_writeAttempts.TryGetValue(actual, out var prior)) return prior.Driver;
            begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var invocation = new WriteInvocation(actual); attempt = new() { Invocation = invocation };
            attempt.Driver = _originals.Admit(async () =>
            {
                await begin.Task.ConfigureAwait(false);
                return await WriteDriver(invocation, attempt, _originals.CreateScope(scope, retain)).ConfigureAwait(false);
            });
            _writeAttempts.Add(actual, attempt); _invocations.Add(invocation);
            _originals.RegisterOriginalRefusalProof(attempt.Driver, IsAcknowledgedOriginalCommandRefusal);
        }
        begin.SetResult(); return attempt.Driver;
    }
    public bool IsOriginalAtomicImportTask(ICanonicalAttachmentImportIntent intent, Task<ICanonicalAttachmentImportAcknowledgment> actual) =>
        IsIssuedOriginalImportIntent(intent) && _atomicWrites.TryGetValue(actual, out var invocation) &&
        ReferenceEquals(invocation.Intent, intent) && ReferenceEquals(invocation.Atomic, actual);
    public bool IsOwnedOriginalImportAcknowledgment(ICanonicalAttachmentImportIntent intent,
        ICanonicalAttachmentImportAcknowledgment acknowledgment, Task<ICanonicalAttachmentImportAcknowledgment> actual) =>
        IsOriginalAtomicImportTask(intent, actual) && _atomicWrites.TryGetValue(actual, out var invocation) &&
        ReferenceEquals(invocation.Acknowledgment, acknowledgment) && ReferenceEquals(acknowledgment.OriginalIntent, intent);
    public void DemandOriginalImportCommit(ICanonicalAttachmentImportIntent intent)
    {
        var actual = DemandWriteIntent(intent); DemandOriginalProcessing(actual); var invocation = _activeWrite.Value;
        if (invocation is null || !ReferenceEquals(invocation.Intent, actual) || invocation.Sqlite is null || invocation.Den is null ||
            invocation.Claim is null || _writes?.IsIssuedOriginalImportClaim(invocation.Claim, actual) != true)
            throw new UnauthorizedAccessException("The same approved attachment operation and actual revision pins are required.");
        invocation.Den.DemandOriginalPinnedRevisions(); _production.DemandOriginalAttachmentWriter(invocation.Sqlite);
        if (invocation.Sqlite.OriginalIdentity != actual.OriginalStoreIdentity || invocation.Sqlite.Actor != actual.Actor ||
            invocation.Den.DefinitionId != actual.DefinitionId || invocation.Den.DefinitionRevision != actual.DefinitionRevision ||
            invocation.Den.SessionId != actual.SessionId || invocation.Den.SessionRevision != actual.SessionRevision)
            throw new UnauthorizedAccessException("The pinned attachment destination changed.");
    }
    private async Task<ICanonicalAttachmentImportAcknowledgment> WriteDriver(WriteInvocation invocation,
        WriteAttempt attempt, AssistantMemoryOriginals.Scope source)
    {
        var prior = _activeWrite.Value; _activeWrite.Value = invocation;
        var errors = new List<Exception>(); ICanonicalAttachmentImportAcknowledgment? result = null;
        var token = CancellationToken.None; var intent = invocation.Intent; bool declined = false;
        try
        {
            var writes = _writes ?? throw new InvalidOperationException("The actual Home attachment WRITE owner is unavailable.");
            await source.Read(() => ValidateOriginalImportIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
            Task<ICanonicalAttachmentHomeImportClaim>? approval = null;
            try
            {
                await source.Read(() => approval = writes.AcquireOriginalImportWithinSourceAsync(intent, source.Run, source.Retain,
                    claim => invocation.Claim = claim, token), claim => invocation.Claim = claim).ConfigureAwait(false);
            }
            catch
            {
                var acknowledged = false;
                if (approval is not null) source.Run(() => acknowledged = writes.IsAcknowledgedOriginalImportRefusal(approval));
                if (!acknowledged) throw;
                source.Run(() => source.AcknowledgeOriginalRefusalOccurrences(writes.IsAcknowledgedOriginalImportRefusal));
                declined = true;
            }
            if (!declined)
            {
                var issuedClaim = false;
                source.Run(() => issuedClaim = invocation.Claim is not null && writes.IsIssuedOriginalImportClaim(invocation.Claim, intent));
                if (invocation.Claim is null || !issuedClaim)
                    throw new UnauthorizedAccessException("The exact attachment review did not issue a current WRITE claim.");
                await source.Read(() => ValidateOriginalImportIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
                var home = await source.Read(() => intent.Read.Membership.OpenHomeWithinSourceAsync(source.Run, source.Retain, token)).ConfigureAwait(false);
                if (home.Actor != intent.Actor) throw new UnauthorizedAccessException("The original Home profile changed.");
                var definition = await source.Read(() => intent.Read.Membership.DefinitionWithinSourceAsync(home,
                    intent.Read.Binding.Definition.Identity, source.Run, source.Retain, token)).ConfigureAwait(false);
                var session = await source.Read(() => home.Den.GetAsync<SessionRecord>(intent.NamespaceId, intent.SessionId, token)).ConfigureAwait(false);
                if (definition.Revision != intent.DefinitionRevision || session?.Revision != intent.SessionRevision)
                    throw new UnauthorizedAccessException("The Assistant definition or conversation membership changed.");
                await source.Read(() => _production.AcquireOriginalAttachmentWriterAsync(intent, source.Run, source.Retain, token),
                    pin => invocation.Sqlite = pin).ConfigureAwait(false);
                await source.Read(() => home.Den.Store.PinOriginalAssistantRevisionsAsync(definition, session!,
                    home.Den.AccessPolicy, home.Den.PrincipalId, source.Run, source.Retain, pin => invocation.Den = pin, token),
                    pin => invocation.Den = pin).ConfigureAwait(false);
                source.Run(() => DemandOriginalImportCommit(intent));
                await source.Read(() => writes.AcquireOriginalCommitEntryWithinSourceAsync(invocation.Claim, source.Run, source.Retain, token)).ConfigureAwait(false);
                await source.Read(() => writes.ValidateOriginalCommitWithinSourceAsync(invocation.Claim, source.Run, source.Retain, token)).ConfigureAwait(false);
                var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var raw = AtomicImport(start.Task, invocation, source, writes);
                invocation.Atomic = raw; _atomicWrites.Add(raw, invocation);
                Exception? publication = null;
                try { source.Run(() => { source.Retain(raw); writes.RetainOriginalSqlCommit(invocation.Claim, raw); }); }
                catch (Exception error) { publication = error; }
                start.SetResult(publication is null);
                try { result = await raw.ConfigureAwait(false); } catch (Exception error) { errors.Add(raw.Exception ?? error); }
                if (publication is not null) errors.Add(publication);
                source.BeginOriginalCleanup();
                MarkOriginalDispatchSettled(invocation);
                try { await source.Read(() => writes.CompleteOriginalImportWithinSourceAsync(invocation.Claim, raw,
                    source.Run, source.Retain, token)).ConfigureAwait(false); }
                catch (Exception error) { errors.Add(error); }
            }
        }
        catch (Exception error) { errors.Add(error); }
        finally
        {
            // All productive acquisition/raw SQL is terminal before Home can issue
            // its exact phase. Claim close also handles the genuine no-SQL case.
            source.BeginOriginalCleanup();
            MarkOriginalDispatchSettled(invocation);
            if (invocation.Claim is not null)
                try { await source.Read(() => invocation.Claim.CloseAndDrainOriginalAsync()).ConfigureAwait(false); }
                catch (Exception error) { errors.Add(error); }
            // Home must prove its held entry/completion released before Den leaves.
            // Unknown release keeps the exact Den pin and SQL lease strongly owned.
            if (invocation.Den is null || invocation.SettlementReleaseHealthy)
            {
                if (invocation.Sqlite is not null)
                    try { await source.Read(() => invocation.Sqlite.CloseAndDrainAsync()).ConfigureAwait(false); }
                    catch (Exception error) { errors.Add(error); }
            }
            else errors.Add(new InvalidOperationException("The attachment Home settlement is unconfirmed; its original pins remain retained."));
            try { await source.JoinAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
            _activeWrite.Value = prior;
        }
        AssistantMemoryOriginals.Throw(errors);
        if (declined)
        {
            var refusal = new AssistantCommandRefusedException("Attachment import was declined before any write.");
            _refused.Add(attempt.Driver, refusal); throw refusal;
        }
        return result ?? throw new InvalidOperationException("No exact attachment import acknowledgment was returned.");
    }
    private async Task<ICanonicalAttachmentImportAcknowledgment> AtomicImport(Task<bool> start, WriteInvocation invocation,
        AssistantMemoryOriginals.Scope source, ICanonicalAttachmentHomeImportSource writes)
    {
        if (!await start.ConfigureAwait(false)) throw new InvalidOperationException("Attachment dispatch publication failed before SQL effects.");
        source.Run(() => { DemandOriginalImportCommit(invocation.Intent); writes.DemandOriginalCommit(invocation.Claim!, invocation.Intent); });
        var applied = await source.Read(() => _production.CommitOriginalAttachmentDraftAsync(invocation.Sqlite!,
            invocation.Intent, invocation.Intent.Read, invocation.Intent.Snapshot, CancellationToken.None)).ConfigureAwait(false);
        var acknowledgment = new Acknowledgment(invocation.Intent, applied);
        source.Run(() => { DemandOriginalImportCommit(invocation.Intent); writes.DemandOriginalCommit(invocation.Claim!, invocation.Intent);
            invocation.Acknowledgment = acknowledgment; });
        return acknowledgment;
    }
}
