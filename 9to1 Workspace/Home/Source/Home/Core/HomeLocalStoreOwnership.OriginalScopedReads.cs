using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
namespace HavenOS.Home.Core;

public sealed partial class HomeLocalStoreOwnership
{
    internal bool HasOriginalProfile(HomeLocalProfileIdentity actual) => ReferenceEquals(profiles, actual);
    public async Task<HomeLocalStoreBinding?> GetVerifiedWithinOriginalSourceAsync(string kind, string storeId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var source = new HomeOwnershipOriginalSourceCallbacks(originalSynchronousScope, retainOriginalTask);
        var actor = await source.ReadAsync(() => profiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (evidence is not IHomeOriginalScopedLocalStoreEvidenceSource scopedEvidence)
            throw new InvalidOperationException("HOME_OWNERSHIP_SCOPE_REQUIRED: the actual store evidence owner must carry original source custody.");
        var observed = await source.ReadAsync(() => scopedEvidence.ReadWithinOriginalSourceAsync(kind, storeId, source.Run, source.Retain, token).AsTask()).ConfigureAwait(false);
        if (actor is null || !Valid(observed, kind, storeId)) return null;
        var read = await source.ReadAsync(() => store.ReadAsync(token)).ConfigureAwait(false);
        if (!read.IsSuccess) throw new InvalidDataException("Store ownership state needs recovery; it was not reset.");
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == Id(kind, storeId));
        if (record is null) return null;
        if (record.SchemaVersion != 1 || record.RecordType != "home.local-store-ownership" ||
            record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical)
            throw new InvalidDataException("Unsupported store ownership schema; preserve it for recovery.");
        HomeLocalStoreBinding binding;
        try { binding = record.Payload.Deserialize<HomeLocalStoreBinding>() ?? throw new JsonException(); }
        catch (JsonException cause) { throw new InvalidDataException("Corrupt store ownership; preserve it for recovery.", cause); }
        return binding.ResourceKind == kind && binding.StoreId == storeId && binding.ProfileId == actor.ProfileId ? binding : null;
    }

    internal async ValueTask<ResourceStoreBindingReceipt?> CaptureReceiptWithinOriginalSourceAsync(HomeLocalStoreBinding expected,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        var source = new HomeOwnershipOriginalSourceCallbacks(originalSynchronousScope, retainOriginalTask);
        // No owning-store evidence read here: the original caller may hold that store's commit lease.
        var read = await source.ReadAsync(() => store.ReadAsync(token)).ConfigureAwait(false);
        if (!read.IsSuccess) return null;
        var record = read.State!.Records.SingleOrDefault(item => item.RecordId == Id(expected.ResourceKind, expected.StoreId));
        if (record is null || record.SchemaVersion != 1 || record.RecordType != "home.local-store-ownership" ||
            record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical) return null;
        try
        {
            if (record.Payload.Deserialize<HomeLocalStoreBinding>() != expected) return null;
            return new(record.Revision, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record.Payload))));
        }
        catch (JsonException) { return null; }
    }
    internal async ValueTask<bool> IsReceiptCurrentWithinOriginalSourceAsync(VerifiedResourceStoreOwnership captured,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        if (captured.Receipt is null) return false;
        var source = new HomeOwnershipOriginalSourceCallbacks(originalSynchronousScope, retainOriginalTask);
        var read = await source.ReadAsync(() => store.ReadAsync(token)).ConfigureAwait(false);
        return read.IsSuccess && IsReceiptCurrentInState(read.State!, captured);
    }
}
