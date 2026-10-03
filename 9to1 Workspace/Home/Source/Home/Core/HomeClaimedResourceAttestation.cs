using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.PermissionsTrustNotifications;
using PermissionTrustLevel = HavenOS.Home.PermissionsTrustNotifications.HomeTrustLevel;
using PermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Home.Core;

/// <summary>Private same-host attestation of an already claimed original resource intent.
/// It is not a SQL admission, graph publication grant, run permit or serializable credential.</summary>
public sealed class HomeClaimedResourceAttestation
{
    internal HomeClaimedResourceAttestation(HomeResourceOperationBroker issuer,
        HomeResourceExecutionCapability capability, AuthenticatedResourceActor actor,
        HomePermissionRequestSubmission submission, HomePermissionActionPolicy policy)
    { Issuer = issuer; Capability = capability; Actor = actor; Submission = submission; Policy = policy; }
    internal HomeResourceOperationBroker Issuer { get; }
    internal HomeResourceExecutionCapability Capability { get; }
    internal AuthenticatedResourceActor Actor { get; }
    internal HomePermissionRequestSubmission Submission { get; }
    internal HomePermissionActionPolicy Policy { get; }
}

public sealed partial class HomeResourceOperationBroker
{
    private readonly ConditionalWeakTable<HomeResourceExecutionCapability, HomeClaimedResourceAttestation> _claimedAttestations = new();
    private static HomePermissionRequestSubmission NormalizeOriginalSubmission(HomePermissionRequestSubmission original)
    {
        try { return original with { Caller = original.Caller.Validate(), Scope = original.Scope.Validate(), SessionId = (original.SessionId ?? "").Trim() }; }
        catch (ArgumentException) { return original; } // Preserve canonical Home invalid-input denial; never issue an attestation from it.
    }

    private static bool MatchesClaimedOriginal(Binding binding, PermissionRequest? request) =>
        binding.OriginalSubmission is { } original && binding.OriginalPolicy is { } policy && request is not null &&
        request.State == HomePermissionRequestState.Executing && request.Policy == policy &&
        MatchesPrepared(original, request) && original.Impact.ResourceBinding is { SchemaVersion: 1 } resource &&
        resource.OriginalActor == binding.Actor && resource.Scopes.SequenceEqual(binding.Scopes);
    private void RetainClaimedAttestation(HomeResourceExecutionCapability capability, Binding binding, PermissionRequest request)
    {
        if (!MatchesClaimedOriginal(binding, request) || !request.Policy.RequiresPerActionApproval ||
            request.AppliedGrantId is not null || request.AppliedTrustLevel != PermissionTrustLevel.Session) return;
        _claimedAttestations.Add(capability, new(this, capability, binding.Actor,
            binding.OriginalSubmission!, binding.OriginalPolicy!));
    }
    /// <summary>No reads or admission. Returns only this issuer's exact retained already-claimed handle.
    /// Missing original full intent or an owner outcome already retained denies the final mutation fence.</summary>
    public HomeClaimedResourceAttestation? CaptureClaimedAttestation(HomeResourceExecutionCapability originalCapability)
    {
        ArgumentNullException.ThrowIfNull(originalCapability);
        return originalCapability.IsUncompletedClaim(this) && _claimedAttestations.TryGetValue(originalCapability, out var actual)
            ? actual : null;
    }
    /// <summary>Optional local final fence for one/two actual individual claimed resource operations.
    /// It is not owner mutation permission. SQL callers must acquire SQL first, validate their private
    /// owner admissions outside this lease, and release before any Home audit.</summary>
    public async ValueTask<IHomeLocalOperationLease?> AcquireClaimedResourceLeaseAsync(FileHomeCoreStateStore store,
        HomeLocalProfileIdentity profiles, IReadOnlyList<HomeClaimedResourceAttestation> originalAdmissions,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(originalAdmissions);
        if (!profiles.IsBoundToStore(store) || !permissions.IsBoundToStore(store)) return null;
        var detached = new List<HomeClaimedResourceAttestation>();
        foreach (var item in originalAdmissions)
        {
            if (detached.Count == 2 || item is null) return null;
            if (!ReferenceEquals(CaptureClaimedAttestation(item.Capability), item)) return null;
            detached.Add(item);
        }
        if (detached.Count == 0 || detached.Select(item => item.Capability).Distinct().Count() != detached.Count) return null;
        var actor = detached[0].Actor;
        if (detached.Any(item => item.Actor != actor)) return null;
        return await store.AcquireLocalOperationLeaseCoreAsync(profiles, actor,
            new ClaimedGuard(this, detached.ToArray()), ct).ConfigureAwait(false);
    }
    private sealed class ClaimedGuard(HomeResourceOperationBroker issuer, HomeClaimedResourceAttestation[] admissions)
        : IHomeStateCommitActorGuard
    {
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected,
            HomeStateCommitPhase phase, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(admissions.All(item => issuer.IsClaimedAttestationCurrentInState(item, expected, state)));
        }
    }
    // Trusted composition invokes this on genuine state while its same Home lease is held.
    // Never calls resources, permissions, an actor source, or Home ReadAsync.
    internal bool IsClaimedAttestationCurrentInState(HomeClaimedResourceAttestation attestation,
        AuthenticatedResourceActor originalActor, HomeCoreStoredState actualState)
    {
        if (!ReferenceEquals(attestation.Issuer, this) || attestation.Actor != originalActor ||
            !attestation.Capability.IsUncompletedClaim(this) ||
            !_claimedAttestations.TryGetValue(attestation.Capability, out var actual) || !ReferenceEquals(actual, attestation)) return false;
        var records = actualState.Records.Where(item => item.RecordId == "home.permissions-trust").ToArray();
        if (records.Length != 1 || records[0].RecordType != "home.permissions-trust" || records[0].SchemaVersion != 1 ||
            records[0].Scope != HomeDataScope.DeviceLocal || records[0].Authority != HomeRecordAuthority.LocalCanonical) return false;
        try
        {
            // First cut supports actual individual Accept only. Applied grants require a
            // separate trusted time/revocation attestation; they are never guessed valid.
            var blocked = records[0].Payload.GetProperty("BlockedCallerIds");
            foreach (var id in blocked.EnumerateArray())
                if (id.GetString() == attestation.Submission.Caller.CallerId) return false;
            var requests = records[0].Payload.GetProperty("Requests");
            PermissionRequest? original = null;
            foreach (var item in requests.EnumerateArray())
            {
                if (!item.TryGetProperty(nameof(PermissionRequest.RequestId), out var id) || id.GetString() != attestation.Capability.RequestId) continue;
                if (original is not null) return false;
                original = item.Deserialize<PermissionRequest>();
                if (original is null) return false;
            }
            return original is not null && original.State == HomePermissionRequestState.Executing &&
                original.Policy == attestation.Policy && original.Policy.RequiresPerActionApproval &&
                original.AppliedGrantId is null && original.AppliedTrustLevel == PermissionTrustLevel.Session &&
                MatchesPrepared(attestation.Submission, original) &&
                attestation.Submission.Impact.ResourceBinding is { SchemaVersion: 1 } resource && resource.OriginalActor == originalActor &&
                resource.Scopes.SequenceEqual(attestation.Capability.Scopes);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { return false; }
    }
}
