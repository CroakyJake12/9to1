using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CanonicalAutomationDefinitionOriginalWriteOwner
{
    public Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> CommitOriginalChangeWithinSourceAsync(
        ICanonicalAutomationDefinitionOriginalChangeIntent value, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var intent = RequireIntent(value); Commit current; TaskCompletionSource begin;
        lock (_gate)
        {
            if (_commits.TryGetValue(intent, out var existing)) return existing.Driver;
            Commit? captured = null;
            var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner,
                body => Scope(scope, captured)(body), retain);
            current = new(intent, source); captured = current;
            var original = Reserve(current.Source); begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
            current.Driver = Run(begin.Task, current); _commits.Add(intent, current); _commitSources.Add(current.Driver, current);
            Publish(original, current.Driver);
        }
        begin.SetResult(); return current.Driver;
        async Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> Run(Task start, Commit actual)
        {
            await start.ConfigureAwait(false); var prior = _logical.Value; _logical.Value = prior + 1;
            try
            {
                actual.Inner = _store.RetainOriginalReader<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>(actual.Source,
                    () => CommitDriverAsync(actual, token));
                _commitSources.Add(actual.Inner, actual);
                return await actual.Inner.ConfigureAwait(false);
            }
            finally { _logical.Value = prior; }
        }
    }

    private async Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> CommitDriverAsync(Commit current, CancellationToken token)
    {
        var prior = _executing.Value; _executing.Value = current;
        var source = current.Source; var intent = current.Intent; var errors = new List<Exception>();
        ICanonicalAutomationDefinitionOriginalChangeAcknowledgment? result = null;
        try
        {
            var writes = OriginalHomeWriteSource ?? throw new InvalidOperationException("Actual individual Home Automation change WRITE is not configured.");
            await source.Read(() => RevalidateOriginalChangeIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
            Task<ICanonicalAutomationDefinitionOriginalHomeWriteClaim>? rawAcquisition = null;
            try
            {
                await source.ReadCapture(() => rawAcquisition = writes.AcquireOriginalWriteWithinSourceAsync(intent,
                    source.Run, source.Retain, claim =>
                    {
                        current.Claim = claim; source.CaptureOriginalResource(claim);
                        _commitSources.Add(claim.OriginalAcquisition, current);
                        CaptureOriginalPendingWithdrawal(current);
                    }, token), claim => current.Claim = claim).ConfigureAwait(false);
            }
            catch
            {
                if (rawAcquisition is null || current.Claim is null ||
                    !source.InvokeOwningCleanup(() => writes.IsAcknowledgedOriginalWriteRefusal(rawAcquisition))) throw;
                // Exact issuer-owned review refusal, no accepted native pin/entry/SQL.
                // Close partial claim and independently settle all its originals before
                // acknowledging this exact raw occurrence; mixed callback faults survive.
                await source.CloseAsync(current.Claim).ConfigureAwait(false);
                if (!source.AcknowledgeOriginalExternalPreEffectRefusal(rawAcquisition, writes.IsAcknowledgedOriginalWriteRefusal)) throw;
                await source.JoinAllAsync().ConfigureAwait(false);
                if (!source.IsHealthySettled) throw;
                current.AcknowledgedAcquisition = rawAcquisition;
                var refusal = new InvalidOperationException("The individual Home Automation change WRITE was declined before any SQL effect.");
                source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
            }
            var claim = current.Claim ?? throw new UnauthorizedAccessException("The actual Home setup source returned no claim.");
            if (!writes.IsIssuedOriginalWriteClaim(claim, intent)) throw new UnauthorizedAccessException("The SAME actual Home setup source did not issue this claim.");
            await source.Read(() => RevalidateOriginalChangeIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
            // Native/SQLite pin follows approval but precedes held Home entry. ReadCapture
            // stores the SAME late successful pin before a caller postguard can reject it.
            await source.ReadCapture(() => _store.AcquireOriginalProtectedWriterPinWithinSourceAsync(intent.Actor,
                source.Run, source.Retain, token), pin => current.Pin = pin).ConfigureAwait(false);
            if (current.Pin!.OriginalIdentity != intent.OriginalStoreIdentity)
                throw new UnauthorizedAccessException("The reviewed canonical SQLite identity changed before setup WRITE.");
            await DemandCurrentAsync(intent.Actor, intent.OriginalStoreIdentity, intent.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            source.Run(() => DemandOriginalChangeCommit(intent));
            await source.Read(() => writes.AcquireOriginalCommitEntryWithinSourceAsync(claim, source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.Read(() => writes.ValidateOriginalCommitWithinSourceAsync(claim, source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            var start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var atomic = AtomicAsync(start.Task, current, writes, token);
            current.Atomic = atomic; _atomic.Add(atomic, current); _commitSources.Add(atomic, current);
            Exception? publication = null;
            try { source.Run(() => { writes.RetainOriginalSqlCommit(claim, atomic); source.Retain(atomic); }); }
            catch (Exception cause) { publication = cause; }
            start.TrySetResult(publication is null);
            try { result = await atomic.ConfigureAwait(false); }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, atomic, cause); }
            if (publication is not null) errors.Add(publication);
            current.DispatchSettled = true;
            Task? actualPinClose = null;
            try { actualPinClose = current.Pin!.CloseAndDrainAsync(); current.NativeClose = actualPinClose; source.Retain(actualPinClose); await actualPinClose.ConfigureAwait(false); current.NativeCloseJoined = true; }
            catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, actualPinClose, cause); }
            try { await source.Read(() => writes.CompleteOriginalWriteWithinSourceAsync(claim, atomic,
                source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            // Setup/dispatch has terminally returned: late actual acquisitions were
            // captured by ReadCapture before this exact private transition.
            current.DispatchSettled = true;
            if (current.Pin is not null)
            {
                Task? rawClose = null;
                try
                {
                    rawClose = current.Pin.CloseAndDrainAsync(); current.NativeClose = rawClose;
                    source.Retain(rawClose); await rawClose.ConfigureAwait(false); current.NativeCloseJoined = true;
                }
                catch (Exception cause) { CanonicalSqliteOriginalStoreOwner.Capture(errors, rawClose, cause); }
            }
            // Native pin release precedes held Home entry/completion release and audit.
            if (current.Claim is not null) try { await source.CloseAsync(current.Claim).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            _executing.Value = prior;
        }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
        return result ?? throw new InvalidOperationException("No healthy Automation change SQL acknowledgment was observed.");
    }

    private async Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> AtomicAsync(Task<bool> start, Commit current,
        ICanonicalAutomationDefinitionOriginalHomeWriteSource writes, CancellationToken token)
    {
        if (!await start.ConfigureAwait(false)) throw new InvalidOperationException("Actual setup atomic publication was declined before SQL effects.");
        var source = current.Source; var intent = current.Intent; var pin = current.Pin!;
        void Demand() { DemandOriginalChangeCommit(intent); writes.DemandOriginalCommit(current.Claim!, intent); }
        source.Run(() => { token.ThrowIfCancellationRequested(); Demand(); pin.BeginPinnedOriginalWriter(Demand); });
        var snapshot = await ReadSnapshotWithinLeaseAsync(pin, intent.OriginalDefinition.Id,
            RawText(intent.Selection.Row.RetainedProtectedDescriptors, "id"), intent.OperationId, token).ConfigureAwait(false);
        if (Hash(snapshot) != Hash(intent.Snapshot))
            throw new UnauthorizedAccessException("The full raw row/descriptor/receipt changed before atomic CAS.");
        DemandNoLegacyLease(snapshot.Row); DemandDescriptor(snapshot, intent.Actor, intent.OriginalStoreIdentity, intent.OriginalDefinition.Id);
        DurableOperation operation;
        if (snapshot.Operation is { } durable)
        {
            DemandDurableOperation(durable, snapshot, intent.Actor, intent.OriginalStoreIdentity,
                intent.OriginalDefinition.Id, intent.OperationId, intent.ChangeKind, GetOriginalChangeIntentDigest(intent));
            operation = durable; // Observe current exact durable operation, no second row mutation.
        }
        else
        {
            await DisableRowWithinLeaseAsync(pin, RawText(snapshot.Row, "id"), Demand, token).ConfigureAwait(false);
            var after = await ReadRowWithinLeaseAsync(pin, RawText(snapshot.Row, "id"), token).ConfigureAwait(false);
            if (!SameExceptDisabled(snapshot.Row, after))
                throw new InvalidDataException("The atomic change did not preserve every other observed row value/storage class.");
            var beforeHash = snapshot.RowSha256;
            var afterHash = AutomationDefinitionChange.ComputeRawRowSHA256(after);
            var owner = new AutomationOwnerBinding(intent.OriginalStoreIdentity.StoreId, intent.Actor.ProfileId,
                intent.Actor.ActorId, intent.Actor.AuthenticationRevision, intent.Actor.AccountId, intent.Actor.OrganisationId);
            var now = DateTimeOffset.UtcNow;
            var receipt = new AutomationOwnerCommitReceipt(1, intent.OriginalStoreIdentity.StoreId, intent.OriginalDefinition.Id,
                AutomationDefinitionEntityKind.Automation, intent.OperationId, GetOriginalChangeIntentDigest(intent),
                intent.ExpectedRevision, checked(intent.ExpectedRevision + 1), now, owner);
            var descriptor = new Descriptor(1, receipt.StoreId, receipt.EntityId, receipt.CommittedRevision, owner,
                intent.ChangeKind == CanonicalAutomationOriginalChangeKind.RecoverLegacy ? AutomationOperationalState.NeedsAttention : AutomationOperationalState.Disabled,
                intent.OperationId, beforeHash, afterHash, snapshot.DescriptorJson is null ? null : RawJsonDigest(snapshot.DescriptorJson), receipt);
            operation = new(1, receipt.StoreId, receipt.EntityId, receipt.OperationId, intent.ChangeKind,
                receipt.PayloadSha256, beforeHash, afterHash, descriptor);
            await InsertSettingWithinLeaseAsync(pin, DescriptorKey(descriptor.EntityId, descriptor.Revision), descriptor, now, Demand, token).ConfigureAwait(false);
            await InsertSettingWithinLeaseAsync(pin, ReceiptPrefix + intent.OperationId.ToString("N"), operation, now, Demand, token).ConfigureAwait(false);
        }
        await pin.CommitPinnedOriginalAsync(Demand, token).ConfigureAwait(false);
        var acknowledgment = new Acknowledgment(intent, operation);
        source.Run(() => { Demand(); current.Acknowledgment = acknowledgment; _acknowledgments.Add(acknowledgment, acknowledgment); });
        return acknowledgment;
    }
    public void DemandOriginalChangeCommit(ICanonicalAutomationDefinitionOriginalChangeIntent value)
    {
        var intent = RequireIntent(value);
        var current = _physical?.TryGetValue(this, out var actual) == true ? actual : _executing.Value;
        if (current is null || !ReferenceEquals(current.Intent, intent) || current.Pin is null ||
            !_store.IsIssuedOriginalLease(current.Pin) || current.Pin.OriginalIdentity != intent.OriginalStoreIdentity)
            throw new UnauthorizedAccessException("No SAME admitted Automation change driver/native store pin is active.");
        current.Pin.DemandPinnedOriginalPhysical();
    }
    public bool IsOriginalAtomicChangeTask(ICanonicalAutomationDefinitionOriginalChangeIntent value,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> actual) =>
        IsIssuedOriginalChangeIntent(value) && _atomic.TryGetValue(actual, out var current) &&
        ReferenceEquals(current.Intent, value) && ReferenceEquals(current.Atomic, actual);
    public bool IsOwnedOriginalChangeAcknowledgment(ICanonicalAutomationDefinitionOriginalChangeIntent value,
        ICanonicalAutomationDefinitionOriginalChangeAcknowledgment acknowledgment, Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment> actual) =>
        IsOriginalAtomicChangeTask(value, actual) && actual.IsCompletedSuccessfully && ReferenceEquals(actual.Result, acknowledgment) &&
        acknowledgment is Acknowledgment same && _acknowledgments.TryGetValue(acknowledgment, out var issued) && ReferenceEquals(same, issued) &&
        _atomic.TryGetValue(actual, out var current) && ReferenceEquals(current.Acknowledgment, same) && ReferenceEquals(same.OriginalIntent, value);
    public bool IsOwnedOriginalChangeNativeRelease(ICanonicalAutomationDefinitionOriginalChangeIntent value,
        Task<ICanonicalAutomationDefinitionOriginalChangeAcknowledgment>? sameAtomic)
    {
        if (!IsIssuedOriginalChangeIntent(value) || !_commits.TryGetValue((Intent)value, out var current) ||
            !current.DispatchSettled || !ReferenceEquals(current.Atomic, sameAtomic)) return false;
        if (sameAtomic is not null && (!sameAtomic.IsCompleted || !IsOriginalAtomicChangeTask(value, sameAtomic))) return false;
        return current.Pin is null ? current.NativeClose is null : current.NativeCloseJoined &&
            current.NativeClose is { IsCompletedSuccessfully: true } sameClose && ReferenceEquals(current.Pin.OriginalClose, sameClose);
    }
    public bool IsAcknowledgedOriginalChangeSourceRefusal(Task actual)
    {
        if (_commandSources.TryGetValue(actual, out var ownSource) && ownSource.IsHealthySettled &&
            _store.IsAcknowledgedOriginalCommandRefusal(actual)) return true;
        if (!actual.IsFaulted || !_commitSources.TryGetValue(actual, out var current) || current.Atomic is not null || current.Pin is not null ||
            current.Inner is not { } inner || !_store.IsAcknowledgedOriginalCommandRefusal(inner) ||
            current.AcknowledgedAcquisition is not { } acquisition || !current.Source.IsHealthySettled)
            return false;
        if (ReferenceEquals(actual, acquisition)) return OriginalHomeWriteSource?.IsAcknowledgedOriginalWriteRefusal(actual) == true;
        if (!ReferenceEquals(actual, current.Driver) && !ReferenceEquals(actual, inner)) return false;
        var direct = actual.Exception!.InnerExceptions; var same = inner.Exception!.InnerExceptions;
        return direct.Count == 1 && same.Count == 1 && ReferenceEquals(direct[0], same[0]);
    }
}
