using Haven.Application;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CanonicalCapabilityCatalogueInitializationOwner
{
    public Task<ICapabilityOriginalInitializationAcknowledgment> CommitOriginalInitializationWithinSourceAsync(
        ICapabilityOriginalInitializationIntent value, Action<Action> scope, Action<Task> retain, CancellationToken token)
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
        async Task<ICapabilityOriginalInitializationAcknowledgment> Run(Task start, Commit actual)
        {
            await start.ConfigureAwait(false); var prior = _logical.Value; _logical.Value = prior + 1;
            try
            {
                actual.Inner = _store.RetainOriginalReader<ICapabilityOriginalInitializationAcknowledgment>(actual.Source,
                    () => CommitDriverAsync(actual, token));
                _commitSources.Add(actual.Inner, actual);
                return await actual.Inner.ConfigureAwait(false);
            }
            finally { _logical.Value = prior; }
        }
    }

    private async Task<ICapabilityOriginalInitializationAcknowledgment> CommitDriverAsync(Commit current, CancellationToken token)
    {
        var prior = _executing.Value; _executing.Value = current;
        var source = current.Source; var intent = current.Intent; var errors = new List<Exception>();
        ICapabilityOriginalInitializationAcknowledgment? result = null;
        try
        {
            var writes = OriginalHomeWriteSource ?? throw new InvalidOperationException("Actual individual Home capability setup WRITE is not configured.");
            await source.Read(() => RevalidateOriginalInitializationIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
            Task<ICapabilityOriginalInitializationHomeWriteClaim>? rawAcquisition = null;
            try
            {
                await source.ReadCapture(() => rawAcquisition = writes.AcquireOriginalWriteWithinSourceAsync(intent,
                    source.Run, source.Retain, claim =>
                    {
                        current.Claim = claim; source.CaptureOriginalResource(claim);
                        _commitSources.Add(claim.OriginalAcquisition, current);
                        CaptureOriginalProcessClaim(current);
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
                var refusal = new InvalidOperationException("The individual Home capability setup WRITE was declined before any SQL effect.");
                source.RegisterOriginalPreEffectRefusal(refusal); throw refusal;
            }
            var claim = current.Claim ?? throw new UnauthorizedAccessException("The actual Home setup source returned no claim.");
            if (!writes.IsIssuedOriginalWriteClaim(claim, intent)) throw new UnauthorizedAccessException("The SAME actual Home setup source did not issue this claim.");
            await source.Read(() => RevalidateOriginalInitializationIntentWithinSourceAsync(intent, source.Run, source.Retain, token)).ConfigureAwait(false);
            // Native/SQLite pin follows approval but precedes held Home entry. ReadCapture
            // stores the SAME late successful pin before a caller postguard can reject it.
            await source.ReadCapture(() => _store.AcquireOriginalProtectedWriterPinWithinSourceAsync(intent.Actor,
                source.Run, source.Retain, token), pin => current.Pin = pin).ConfigureAwait(false);
            if (current.Pin!.OriginalIdentity != intent.OriginalStoreIdentity)
                throw new UnauthorizedAccessException("The reviewed canonical SQLite identity changed before setup WRITE.");
            await DemandCurrentAsync(intent.Actor, intent.OriginalStoreIdentity, intent.OriginalStoreOwnership, source, token).ConfigureAwait(false);
            source.Run(() => DemandOriginalInitializationCommit(intent));
            await source.Read(() => writes.AcquireOriginalCommitEntryWithinSourceAsync(claim, source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.Read(() => writes.ValidateOriginalCommitWithinSourceAsync(claim, source.Run, source.Retain, token)).ConfigureAwait(false);
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
            try { await source.Read(() => writes.CompleteOriginalWriteWithinSourceAsync(claim, atomic,
                source.Run, source.Retain, CancellationToken.None)).ConfigureAwait(false); }
            catch (Exception cause) { errors.Add(cause); }
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            // Actual claim joins/releases its held Home entry/completion before native
            // store custody closes. All failures preserve the actual cached close/source.
            if (current.Claim is not null) try { await source.CloseAsync(current.Claim).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            if (current.Pin is not null) try { await current.Pin.CloseAndDrainAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.CloseResourcesAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            try { await source.JoinAllAsync().ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
            _executing.Value = prior;
        }
        CanonicalSqliteOriginalStoreOwner.Throw(errors);
        return result ?? throw new InvalidOperationException("No healthy capability setup SQL acknowledgment was observed.");
    }

    private async Task<ICapabilityOriginalInitializationAcknowledgment> AtomicAsync(Task<bool> start, Commit current,
        ICapabilityOriginalInitializationHomeWriteSource writes, CancellationToken token)
    {
        if (!await start.ConfigureAwait(false)) throw new InvalidOperationException("Actual setup atomic publication was declined before SQL effects.");
        var source = current.Source; var intent = current.Intent; var pin = current.Pin!;
        void Demand() { DemandOriginalInitializationCommit(intent); writes.DemandOriginalCommit(current.Claim!, intent); }
        source.Run(() => { token.ThrowIfCancellationRequested(); Demand(); pin.BeginPinnedOriginalWriter(Demand); });
        var rows = await ReadRowsWithinLeaseAsync(pin, token).ConfigureAwait(false);
        var saved = await ReadReceiptWithinLeaseAsync(pin, intent.OperationId, token).ConfigureAwait(false);
        if (rows.Sha256 != intent.SnapshotSha256 || !SameReceipt(saved, intent.Recovered) || Hash(Haven.Core.CapabilityRegistryCatalog.BuiltIns) != intent.BuiltInsSha256)
            throw new UnauthorizedAccessException("The actual reviewed capability snapshot changed before atomic initialization.");
        Receipt receipt;
        if (saved is not null)
        {
            DemandReceipt(saved, intent.Actor, intent.OriginalStoreIdentity, intent.OperationId, intent.BuiltInsSha256);
            receipt = saved; // Exact durable operation recovery; no second row mutation.
        }
        else
        {
            foreach (var definition in intent.MissingDefinitions)
            {
                Demand(); SqliteCommand? command = null; var errors = new List<Exception>(); int changed = 0;
                try
                {
                    pin.InvokeOriginalSource(() =>
                    {
                        command = pin.Connection.CreateCommand(); command.Transaction = pin.Transaction;
                        CapabilityRepository.BindOriginalMissingBuiltInCommand(command, definition);
                    });
                    changed = await pin.ReadOriginalSourceAsync(() => command!.ExecuteNonQueryAsync(token)).ConfigureAwait(false);
                    if (changed != 1) throw new InvalidDataException("The reviewed missing builtin was not inserted exactly once; preserve the transaction for recovery.");
                }
                catch (Exception cause) { errors.Add(cause); }
                if (command is not null) try { await pin.CloseOriginalResourceAsync(command).ConfigureAwait(false); } catch (Exception cause) { errors.Add(cause); }
                CanonicalSqliteOriginalStoreOwner.Throw(errors);
            }
            var after = await ReadRowsWithinLeaseAsync(pin, token).ConfigureAwait(false);
            if (after.Rows.Count != rows.Rows.Count + intent.MissingDefinitions.Count ||
                rows.RowDigests.Any(existing => !after.RowDigests.TryGetValue(existing.Key, out var digest) || digest != existing.Value) ||
                intent.MissingDefinitions.Any(inserted => !after.Rows.Any(actual => actual == inserted)))
                throw new InvalidDataException("The atomic setup result did not preserve every existing capability row and exact inserted builtin.");
            receipt = new(1, intent.OriginalStoreIdentity.StoreId, intent.Actor.ActorId, intent.Actor.ProfileId,
                intent.OperationId, intent.BuiltInsSha256, intent.SnapshotSha256, after.Sha256,
                Array.AsReadOnly(intent.MissingDefinitions.Select(item => item.Id).ToArray()), DateTimeOffset.UtcNow);
            await InsertReceiptWithinLeaseAsync(pin, receipt, Demand, token).ConfigureAwait(false);
        }
        await pin.CommitPinnedOriginalAsync(Demand, token).ConfigureAwait(false);
        var acknowledgment = new Acknowledgment(intent, receipt);
        source.Run(() => { Demand(); current.Acknowledgment = acknowledgment; _acknowledgments.Add(acknowledgment, acknowledgment); });
        return acknowledgment;
    }
    public void DemandOriginalInitializationCommit(ICapabilityOriginalInitializationIntent value)
    {
        var intent = RequireIntent(value);
        var current = _physical?.TryGetValue(this, out var actual) == true ? actual : _executing.Value;
        if (current is null || !ReferenceEquals(current.Intent, intent) || current.Pin is null ||
            !_store.IsIssuedOriginalLease(current.Pin) || current.Pin.OriginalIdentity != intent.OriginalStoreIdentity)
            throw new UnauthorizedAccessException("No SAME admitted capability setup driver/native store pin is active.");
        current.Pin.DemandPinnedOriginalPhysical();
    }
    public bool IsOriginalAtomicInitializationTask(ICapabilityOriginalInitializationIntent value,
        Task<ICapabilityOriginalInitializationAcknowledgment> actual) =>
        IsIssuedOriginalInitializationIntent(value) && _atomic.TryGetValue(actual, out var current) &&
        ReferenceEquals(current.Intent, value) && ReferenceEquals(current.Atomic, actual);
    public bool IsOwnedOriginalInitializationAcknowledgment(ICapabilityOriginalInitializationIntent value,
        ICapabilityOriginalInitializationAcknowledgment acknowledgment, Task<ICapabilityOriginalInitializationAcknowledgment> actual) =>
        IsOriginalAtomicInitializationTask(value, actual) && actual.IsCompletedSuccessfully && ReferenceEquals(actual.Result, acknowledgment) &&
        acknowledgment is Acknowledgment same && _acknowledgments.TryGetValue(acknowledgment, out var issued) && ReferenceEquals(same, issued) &&
        _atomic.TryGetValue(actual, out var current) && ReferenceEquals(current.Acknowledgment, same) && ReferenceEquals(same.OriginalIntent, value);
    public bool IsAcknowledgedOriginalInitializationSourceRefusal(Task actual)
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
