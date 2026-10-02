using Haven.Application;
using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Home.Core;

/// <summary>Same-host final fence of one actual claimed resource operation. Capture grants no
/// owner mutation. The owner must acquire its own canonical transaction FIRST, perform ordinary
/// resource/receipt/configuration checks BEFORE ValidateAsync, then hold this fence through its
/// actual durable commit and dispose it BEFORE recording Home's terminal audit.
/// While held, neither caller nor guard may recursively read Home, resources or the owner store.</summary>
public sealed class HomeClaimedResourceCommitFence : IAsyncDisposable
{
    private readonly HomeResourceOperationBroker _broker;
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomeClaimedResourceAttestation _admission;
    private readonly Func<bool> _isOriginalLifetimeCurrent;
    private readonly VerifiedResourceStoreOwnership _ownership;
    private readonly (string Id, byte[] Fingerprint)[] _records;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IHomeLocalOperationLease? _lease;
    private IAsyncDisposable? _completionLease;
    private bool _attempted;
    private bool _disposed;
    private HomeClaimedResourceCommitFence(HomeResourceOperationBroker broker, FileHomeCoreStateStore store,
        HomeLocalProfileIdentity profiles, HomeClaimedResourceAttestation admission, VerifiedResourceStoreOwnership ownership, (string Id, byte[] Fingerprint)[] records, Func<bool> lifetime)
    { _broker = broker; _store = store; _profiles = profiles; _admission = admission; _ownership = ownership; _records = records; _isOriginalLifetimeCurrent = lifetime; }

    /// <summary>Actual same-composition ownership capture before the owner transaction, never approval or claim. A copied capability
    /// descriptor, foreign issuer, incomplete original tuple or already retained outcome denies.</summary>
    public static async ValueTask<HomeClaimedResourceCommitFence?> CaptureAsync(HomeResourceOperationBroker broker,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        string resourceKind, string originalStoreId, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, IReadOnlyList<HomeCoreStateRecord> originalConfigurationRecords,
        Func<bool> isOriginalLifetimeCurrent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profiles); ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(originalCapability); ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(isOriginalLifetimeCurrent);
        ArgumentNullException.ThrowIfNull(originalConfigurationRecords);
        // Additional expected-record comparisons only narrow an actual private claim. The owning
        // composition must supply its ORIGINAL provider-bound configuration, never a fresh substitute.
        var records = new List<(string Id, byte[] Fingerprint)>();
        foreach (var record in originalConfigurationRecords)
        {
            if (records.Count == 8 || record is null || string.IsNullOrWhiteSpace(record.RecordId) ||
                records.Any(item => item.Id == record.RecordId)) return null;
            records.Add((record.RecordId, SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(record))));
        }
        if (records.Count == 0) return null;
        return await CaptureCoreAsync(broker, store, profiles, ownership, resourceKind, originalStoreId,
            originalCapability, originalActor, records.ToArray(), isOriginalLifetimeCurrent, ct).ConfigureAwait(false);
    }

    /// <summary>Explicit local Maps/Shelf settings owner variant; these owners have no separate Home
    /// native-provider configuration. Requires an actual claimed WRITE scope for this exact library UUID
    /// and canonical same-Home ownership. The owning settings transaction must independently enforce its
    /// original private component/factory identity, UUID, revision and lifetime. Files cannot use this path.</summary>
    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureSettingsAsync(HomeResourceOperationBroker broker,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        string resourceKind, string originalStoreId, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, Func<bool> isOriginalLifetimeCurrent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profiles); ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(originalCapability); ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(isOriginalLifetimeCurrent);
        if (resourceKind is not ("maps" or "shelf") || !Guid.TryParseExact(originalStoreId, "D", out var uuid) || uuid == Guid.Empty)
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        var admission = broker.CaptureClaimedAttestation(originalCapability);
        var action = resourceKind == "maps" ? "maps.journey.save" : "shelf.item.add";
        if (admission is null || admission.Actor != originalActor || admission.Submission.Scope.TargetAppId != resourceKind ||
            admission.Submission.Scope.ActionName != action || admission.Submission.Impact.ResourceBinding is not { SchemaVersion: 1 } binding ||
            !binding.Scopes.Any(scope => scope.Kind == resourceKind + ".library" && scope.Id == originalStoreId && scope.Access == ResourceAccess.Write))
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        return CaptureCoreAsync(broker, store, profiles, ownership, resourceKind, originalStoreId,
            originalCapability, originalActor, [], isOriginalLifetimeCurrent, ct);
    }

    /// <summary>SQL definition-update variant for the exact original reusable definition. Capture grants
    /// no candidate publication: the owning SQL writer must enforce its private Prepared-only proposal,
    /// original factory, UUID and revision before acquiring this fence inside its genuine transaction.</summary>
    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureAutomationDefinitionAsync(HomeResourceOperationBroker broker,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        string originalStoreId, Guid originalDefinitionId, long originalExpectedRevision,
        HomeResourceExecutionCapability originalCapability, AuthenticatedResourceActor originalActor,
        Func<bool> isOriginalLifetimeCurrent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profiles); ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(originalCapability); ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(isOriginalLifetimeCurrent);
        if (!Guid.TryParseExact(originalStoreId, "D", out var uuid) || uuid == Guid.Empty ||
            originalStoreId != uuid.ToString("D") || originalDefinitionId == Guid.Empty || originalExpectedRevision < 1)
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        var admission = broker.CaptureClaimedAttestation(originalCapability);
        var definition = originalStoreId + "/" + originalDefinitionId.ToString("D");
        var revision = originalExpectedRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (admission is null || admission.Actor != originalActor ||
            admission.Submission.Scope.TargetAppId != "automations" || admission.Submission.Scope.ActionName != "automations.update" ||
            admission.Submission.Impact.ResourceBinding is not { SchemaVersion: 1 } binding ||
            !binding.Scopes.Any(scope => scope.Kind == "automation.reusable-task" && scope.Id == definition &&
                scope.Revision == revision && scope.Access == ResourceAccess.Write))
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        return CaptureCoreAsync(broker, store, profiles, ownership, "automations", originalStoreId,
            originalCapability, originalActor, [], isOriginalLifetimeCurrent, ct);
    }

    private static async ValueTask<HomeClaimedResourceCommitFence?> CaptureCoreAsync(HomeResourceOperationBroker broker,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        string resourceKind, string originalStoreId, HomeResourceExecutionCapability originalCapability,
        AuthenticatedResourceActor originalActor, (string Id, byte[] Fingerprint)[] records,
        Func<bool> isOriginalLifetimeCurrent, CancellationToken ct)
    {
        // Canonical receipt is obtained from the SAME trusted composition before the owner transaction.
        // Public receipt fields and a different ownership provider cannot substitute for this issuer.
        var admission = broker.CaptureClaimedAttestation(originalCapability);
        if (admission is null || admission.Actor != originalActor || !profiles.IsBoundToStore(store)
            || !broker.IsBoundToLocalCommitStore(store) || !ownership.IsBoundTo(store, profiles)
            || !LifetimeCurrent(isOriginalLifetimeCurrent) || await profiles.GetCurrentAsync(ct).ConfigureAwait(false) != originalActor) return null;
        var receipt = await ownership.GetVerifiedAsync(resourceKind, originalStoreId, ct).ConfigureAwait(false);
        return receipt?.Receipt is null || receipt.ProfileId != originalActor.ProfileId ||
            receipt.ResourceKind != resourceKind || receipt.StoreId != originalStoreId ||
            !LifetimeCurrent(isOriginalLifetimeCurrent) || await profiles.GetCurrentAsync(ct).ConfigureAwait(false) != originalActor
            ? null : new(broker, store, profiles, admission, receipt, records, isOriginalLifetimeCurrent);
    }

    private static bool LifetimeCurrent(Func<bool> lifetime)
    {
        try { return lifetime(); }
        catch { return false; } // A failed pure owner lifetime predicate can only deny admission.
    }

    /// <summary>Call only inside the already-held actual owner transaction. The first validation
    /// acquires the same Home state lease; later validations inspect that retained raw lease.
    /// Refusal/cancellation never retries acquisition or reconstructs an owner admission.</summary>
    public async ValueTask<bool> ValidateAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_disposed || !LifetimeCurrent(_isOriginalLifetimeCurrent)) return false;
            if (!_attempted)
            {
                _attempted = true;
                // CompleteAsync reserves its in-memory outcome before writing Home. Serialize that
                // reservation too: owner transaction -> exact capability completion gate -> raw Home.
                _completionLease = await _admission.Capability.AcquireCommitCompletionLeaseAsync(_broker, ct).ConfigureAwait(false);
                if (_completionLease is not null)
                    _lease = await _store.AcquireLocalOperationLeaseCoreAsync(_profiles, _admission.Actor,
                        new Guard(this), ct).ConfigureAwait(false);
            }
            return _lease is not null && LifetimeCurrent(_isOriginalLifetimeCurrent) &&
                await _lease.IsCurrentAsync(ct).ConfigureAwait(false) && LifetimeCurrent(_isOriginalLifetimeCurrent);
        }
        finally { _gate.Release(); }
    }

    private sealed class Guard(HomeClaimedResourceCommitFence issuer) : IHomeStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState actualState, AuthenticatedResourceActor originalActor,
            HomeStateCommitPhase phase, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(LifetimeCurrent(issuer._isOriginalLifetimeCurrent) &&
                issuer._admission.Actor == originalActor &&
                HomeLocalStoreOwnership.IsReceiptCurrentInState(actualState, issuer._ownership) &&
                issuer._records.All(expected =>
                {
                    var matches = actualState.Records.Where(item => item.RecordId == expected.Id).Take(2).ToArray();
                    return matches.Length == 1 && CryptographicOperations.FixedTimeEquals(expected.Fingerprint,
                        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(matches[0])));
                }) &&
                issuer._broker.IsClaimedAttestationCurrentInState(issuer._admission, originalActor, actualState));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            var lease = _lease; _lease = null;
            var completionLease = _completionLease; _completionLease = null;
            try { if (lease is not null) await lease.DisposeAsync().ConfigureAwait(false); }
            finally { if (completionLease is not null) await completionLease.DisposeAsync().ConfigureAwait(false); }
        }
        finally { _gate.Release(); }
    }
}

public sealed partial class HomeResourceOperationBroker
{
    internal bool IsBoundToLocalCommitStore(FileHomeCoreStateStore store) => permissions.IsBoundToStore(store);
}
