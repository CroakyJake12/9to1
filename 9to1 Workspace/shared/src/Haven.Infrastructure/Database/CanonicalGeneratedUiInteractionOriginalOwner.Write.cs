using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

public sealed partial class CanonicalGeneratedUiInteractionOriginalOwner
{
    private readonly ConditionalWeakTable<ICanonicalGeneratedUiOriginalSaveIntent, Intent> _intents = new();
    private readonly ConditionalWeakTable<ICanonicalGeneratedUiOriginalSaveAcknowledgment, Acknowledgment> _acknowledgments = new();
    private readonly Dictionary<Guid, Intent> _operations = [];
    private sealed class Intent(CanonicalGeneratedUiInteractionOriginalOwner owner, Observation observation, Guid operation,
        string payload) : ICanonicalGeneratedUiOriginalSaveIntent
    {
        internal readonly CanonicalGeneratedUiInteractionOriginalOwner Owner = owner;
        internal readonly Observation Observation = observation;
        public ICanonicalGeneratedUiOriginalObservation OriginalObservation => Observation;
        public AuthenticatedResourceActor Actor => Observation.Origin.Actor;
        public ResourceStoreIdentity OriginalStoreIdentity => Observation.OriginalStoreIdentity;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => Observation.Ownership;
        public Guid OperationId { get; } = operation;
        public string OriginalPayloadSha256 { get; } = payload;
        public string OriginalRowSha256 => Observation.Stored.TargetRowSha256;
    }
    private sealed class Acknowledgment(Intent intent, DurableOperation operation) : ICanonicalGeneratedUiOriginalSaveAcknowledgment
    {
        internal readonly Intent Intent = intent;
        internal readonly DurableOperation Operation = operation;
        public ICanonicalGeneratedUiOriginalSaveIntent OriginalIntent => Intent;
        public CanonicalGeneratedUiOriginalCommitReceipt OriginalReceipt => Operation.Descriptor.Receipt;
    }
    private Intent RequireIntent(ICanonicalGeneratedUiOriginalSaveIntent same) => same is Intent intent &&
        ReferenceEquals(intent.Owner, this) && _intents.TryGetValue(same, out var issued) && ReferenceEquals(intent, issued)
        ? intent : throw new UnauthorizedAccessException("Use the SAME privately issued original interaction save intent.");
    public bool IsIssuedOriginalSaveIntent(ICanonicalGeneratedUiOriginalSaveIntent same) => same is Intent intent &&
        ReferenceEquals(intent.Owner, this) && _intents.TryGetValue(same, out var issued) && ReferenceEquals(intent, issued);
    public string GetOriginalSaveDigest(ICanonicalGeneratedUiOriginalSaveIntent same) => RequireIntent(same).OriginalPayloadSha256;

    public Task<ICanonicalGeneratedUiOriginalSaveIntent> PrepareOriginalSaveWithinSourceAsync(
        ICanonicalGeneratedUiOriginalObservation sameObservation, Guid operationId, Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = CreateOriginalInteractionSource(scope, retain);
        return Admit<ICanonicalGeneratedUiOriginalSaveIntent>(source, async () =>
        {
            var observed = source.InvokeProductive(() => RequireObservation(sameObservation));
            if (operationId == Guid.Empty) throw new ArgumentException("An explicit stable save operation is required.", nameof(operationId));
            await source.Read(() => RevalidateOriginalObservationWithinSourceAsync(observed, source.Run, source.Retain, token)).ConfigureAwait(false);
            source.RunProductive(() => DemandSaveable(observed));
            var origin = observed.Origin;
            // Historical authentication is evidence, while the fresh READ and separate
            // individual Home WRITE validate the current authentication revision.
            var payload = Hash(JsonSerializer.Serialize(new { observed.OriginalStoreIdentity.StoreId,
                origin.Actor.ProfileId, origin.Actor.ActorId, origin.Actor.AccountId, origin.Actor.OrganisationId,
                origin.ConversationId, origin.MessageId, origin.Ordinal, origin.ContentDigest, origin.DeclarationDigest,
                origin.DefinitionJson, operationId, Before = observed.Stored.Descriptor?.Receipt.OperationId == operationId
                    ? observed.Stored.Descriptor.Receipt.BeforeRowSha256 : observed.Stored.TargetRowSha256,
                observed.Stored.MessageSourceSha256 }));
            await source.JoinAllAsync().ConfigureAwait(false);
            return source.InvokeProductive(() =>
            {
                lock (_gate)
                {
                    if (_operations.TryGetValue(operationId, out var prior))
                    {
                        if (prior.OriginalPayloadSha256 != payload || !ReferenceEquals(prior.Observation, observed))
                            throw new InvalidOperationException("This explicit operation belongs to another original reviewed occurrence.");
                        return prior;
                    }
                    if (_operations.Count >= 128) throw new InvalidOperationException("Original saved operations require process retirement before another submission.");
                    var intent = new Intent(this, observed, operationId, payload);
                    _intents.Add(intent, intent); _operations.Add(operationId, intent); return intent;
                }
            });
        });
    }
    private static void DemandSaveable(Observation actual)
    {
        if (actual.State is CanonicalGeneratedUiOriginalReadState.Unprovenance or CanonicalGeneratedUiOriginalReadState.AuditUnavailable)
            throw new UnauthorizedAccessException("Preserve unprovenance or unaudited historical rows; this save cannot adopt them.");
        if (actual.Stored.TargetRow is not null && actual.Stored.Descriptor?.InstanceId != actual.Origin.RuntimeDocument.Origin.InstanceId)
            throw new UnauthorizedAccessException("The current runtime ID collides with a separately retained original row.");
    }
    public Task RevalidateOriginalSaveWithinSourceAsync(ICanonicalGeneratedUiOriginalSaveIntent same,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = CreateOriginalInteractionSource(scope, retain);
        var accepted = RequireIntent(same);
        return Admit(source, async () =>
        {
            var intent = source.InvokeProductive(() => RequireIntent(same));
            await RevalidateObserved(intent.Observation, source, token).ConfigureAwait(false);
            source.RunProductive(() => DemandSaveable(intent.Observation)); await source.JoinAllAsync().ConfigureAwait(false); return 0;
        }, accepted);
    }

    private async Task<DurableOperation> WriteAtomicRows(Commit current, Action demand, CancellationToken token)
    {
        var intent = current.Intent; var origin = intent.Observation.Origin; var pin = current.NativePin!;
        var snapshot = await ReadStoredUnderLease(pin, origin, intent.OriginalStoreIdentity, token).ConfigureAwait(false);
        if (JsonSerializer.Serialize(snapshot) != JsonSerializer.Serialize(intent.Observation.Stored))
            throw new UnauthorizedAccessException("The complete raw source message, saved row or immutable descriptor changed before atomic CAS.");
        var triggers = await Query(pin, c => c.CommandText =
            "SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name IN ('genui_apps','settings') LIMIT 1;", r => r.GetString(0), 1, token).ConfigureAwait(false);
        if (triggers.Count != 0) throw new InvalidDataException("Unknown generated-app/settings trigger effects require their own protected review.");
        if (snapshot.Descriptor?.Receipt.OperationId == intent.OperationId)
        {
            DemandStoredProof(snapshot, origin, intent.OriginalStoreIdentity);
            var old = snapshot.Operation!;
            if (old.IntentSha256 != intent.OriginalPayloadSha256 || old.Descriptor.DefinitionSha256 != Hash(origin.DefinitionJson) ||
                old.Descriptor.InstanceId != origin.RuntimeDocument.Origin.InstanceId)
                throw new InvalidDataException("The exact durable operation does not acknowledge this original payload/runtime occurrence.");
            return old;
        }
        var collision = await Query(pin, c => { c.CommandText = "SELECT value FROM settings WHERE key=$key;";
            c.Parameters.AddWithValue("$key", OperationPrefix + intent.OperationId.ToString("N")); }, r => r.GetString(0), 1, token).ConfigureAwait(false);
        if (collision.Count != 0) throw new InvalidDataException("Preserve an existing operation belonging to another saved slot or predecessor.");
        var now = DateTimeOffset.UtcNow;
        var updated = now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var after = await Query(pin, c =>
        {
            demand();
            c.CommandText = snapshot.TargetRow is null
                ? "INSERT INTO genui_apps(instance_id,app_id,thread_id,definition_json,updated_at) VALUES($id,$app,$thread,$json,$updated) RETURNING *;"
                : "UPDATE genui_apps SET definition_json=$json,updated_at=$updated WHERE instance_id=$id RETURNING *;";
            c.Parameters.AddWithValue("$id", origin.RuntimeDocument.Origin.InstanceId.ToString("D"));
            if (snapshot.TargetRow is null) { c.Parameters.AddWithValue("$app", JsonSerializer.Deserialize<GenUiAppDefinition>(origin.DefinitionJson)!.AppId);
                c.Parameters.AddWithValue("$thread", origin.ConversationId.ToString("D")); }
            c.Parameters.AddWithValue("$json", origin.DefinitionJson); c.Parameters.AddWithValue("$updated", updated);
        }, CaptureRawRow, 1, token).ConfigureAwait(false);
        var row = after.SingleOrDefault() ?? throw new InvalidDataException("The exact atomic save returned no single actual row.");
        if (RawText(row, "instance_id") != origin.RuntimeDocument.Origin.InstanceId.ToString("D") ||
            RawText(row, "thread_id") != origin.ConversationId.ToString("D") || RawText(row, "definition_json") != origin.DefinitionJson ||
            RawText(row, "updated_at") != updated || (snapshot.TargetRow is not null && !SameExceptSavedState(snapshot.TargetRow, row)))
            throw new InvalidDataException("The actual saved row did not preserve every other original value/storage class.");
        var reviewed = current.Source.Invoke(() =>
        {
            var claim = current.Claim ?? throw new UnauthorizedAccessException("No exact retained Home claim exists.");
            var request = claim.OriginalApprovalRequestId; var digest = claim.OriginalReviewArgumentsDigest;
            if (string.IsNullOrWhiteSpace(request) || !IsDigest(digest)) throw new UnauthorizedAccessException("The actual reviewed Home request/digest is unavailable.");
            return (Request: request, Digest: digest);
        });
        var receipt = new CanonicalGeneratedUiOriginalCommitReceipt(1, intent.OriginalStoreIdentity.StoreId, intent.OperationId,
            origin.RuntimeDocument.Origin.InstanceId, origin.ConversationId, origin.MessageId, origin.Ordinal,
            origin.ContentDigest, origin.DeclarationDigest, snapshot.TargetRowSha256, RowDigest(row), intent.OriginalPayloadSha256,
            intent.Actor, reviewed.Request, reviewed.Digest, now);
        var descriptor = new StoredInteraction(1, receipt.StoreId, receipt.ConversationId, receipt.MessageId, receipt.TemplateOrdinal,
            receipt.MessageContentSha256, receipt.DeclarationSha256, receipt.InstanceId, checked((snapshot.Descriptor?.Revision ?? 0) + 1),
            Hash(origin.DefinitionJson), receipt.AfterRowSha256, snapshot.DescriptorJson is null ? null : Hash(snapshot.DescriptorJson), receipt);
        var operation = new DurableOperation(1, receipt.StoreId, receipt.OperationId, receipt.PayloadSha256,
            receipt.BeforeRowSha256, receipt.AfterRowSha256, descriptor);
        foreach (var (key, value) in new[] { (DescriptorPrefix + OriginKey(origin, intent.OriginalStoreIdentity) + "." + descriptor.Revision.ToString("D20"), JsonSerializer.Serialize(descriptor)),
            (OperationPrefix + intent.OperationId.ToString("N"), JsonSerializer.Serialize(operation)) })
        {
            var inserted = await Query(pin, c => { demand(); c.CommandText = "INSERT INTO settings(key,value,updated_at) VALUES($key,$value,$now) RETURNING key;";
                c.Parameters.AddWithValue("$key", key); c.Parameters.AddWithValue("$value", value); c.Parameters.AddWithValue("$now", updated); }, r => r.GetString(0), 1, token).ConfigureAwait(false);
            if (inserted.Count != 1 || inserted[0] != key) throw new InvalidDataException("No exact create-only original operation/descriptor was stored.");
        }
        return operation;
    }
    private static bool SameExceptSavedState(SortedDictionary<string, RawValue> before, SortedDictionary<string, RawValue> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out var value) &&
            (pair.Key is "definition_json" or "updated_at" || pair.Value == value));
}
