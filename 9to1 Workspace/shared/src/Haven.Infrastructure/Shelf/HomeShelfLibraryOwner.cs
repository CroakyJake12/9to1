using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core;
using Haven.Core.Shelf;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure;

public interface IShelfLibraryDisplay : IDisposable { }
public sealed record ShelfLibraryDisplay(ShelfSnapshot Snapshot, IShelfLibraryDisplay Selection);
public interface IShelfLibraryReview { string RequestID { get; } bool OriginAvailable { get; } }
public sealed record ShelfLibraryCommit(bool Committed, string Code, bool AuditRecorded, bool CompletionUnknown = false);

/// <summary>Actual personal owning library. A display token is private issuer provenance, never a grant.
/// This uses the same canonical library/settings and actual Home binding, not a second store.</summary>
public sealed class HomeShelfLibraryOwner(ShelfLibraryService library, IAuthenticatedResourceActorSource actors,
    IResourceStoreOwnershipAuthority ownership, HomeResourceOperationBroker home, HomePermissionTrustService permissions,
    HomeOwnedLibraryCommitFenceSource? finalFence = null)
{
    public const string ActionID = "shelf.item.add";
    public string ResourceKind => "shelf.library";
    private sealed record Selection(HomeShelfLibraryOwner Issuer, Guid StoreID, AuthenticatedResourceActor Actor,
        long Revision, Func<bool>? OriginalLifetime = null) : IShelfLibraryDisplay
    {
        private int _revoked;
        public bool Revoked
        {
            get
            {
                if (Volatile.Read(ref _revoked) != 0) return true;
                try { return OriginalLifetime is not null && !OriginalLifetime(); }
                catch { return true; }
            }
        }
        public void Dispose() => Interlocked.Exchange(ref _revoked, 1);
    }
    private sealed class Review(HomeShelfLibraryOwner issuer, Selection selection, ShelfLaunchItem proposed,
        long? expectedObjectRevision, JsonElement arguments, ResourceScope[] scopes, string requestID) : IShelfLibraryReview
    {
        public HomeShelfLibraryOwner Issuer { get; } = issuer;
        public Selection Selection { get; } = selection;
        public ShelfLaunchItem Proposed { get; } = proposed;
        public long Revision => Selection.Revision;
        public long? ExpectedObjectRevision { get; } = expectedObjectRevision;
        public JsonElement Arguments { get; } = arguments;
        public ResourceScope[] Scopes { get; } = scopes;
        public string RequestID { get; } = requestID;
        // Observation only; Commit always independently checks the private original origin.
        public bool OriginAvailable { get; set; } = true;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public HomeResourceExecutionCapability? Capability { get; set; }
        public HomeExecutionOutcome? Outcome { get; set; }
        public bool Committed { get; set; }
        public bool AttemptReserved { get; set; }
        public bool ClaimSucceeded { get; set; }
        public bool CompletionUnknown { get; set; }
        public ShelfOwnedMutationReceipt Receipt => new(1, Arguments.GetProperty("operationID").GetGuid(), Selection.StoreID,
            Revision, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Arguments.GetRawText()))));
    }
    public async Task<ShelfLibraryDisplay> LoadForDisplayAsync(AuthenticatedResourceActor expectedActor, CancellationToken token = default,
        Func<bool>? originalLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != expectedActor)
            throw new UnauthorizedAccessException("Original Shelf actor changed before first read.");
        var identity = await library.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty) throw new UnauthorizedAccessException("Actual Shelf UUID required.");
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != expectedActor) throw new UnauthorizedAccessException("Original actor changed during identity read.");
        var provisional = new Selection(this, identity.StoreId, expectedActor, 0, originalLifetime);
        await CaptureAsync(provisional, token).ConfigureAwait(false);
        var snapshot = await library.ReadAsync(token).ConfigureAwait(false);
        var selection = new Selection(this, identity.StoreId, expectedActor, snapshot.Library.Revision, originalLifetime);
        await RequireAsync(selection, token).ConfigureAwait(false);
        await CaptureAsync(selection, token).ConfigureAwait(false);
        await RequireAsync(selection, token).ConfigureAwait(false);
        return new(snapshot, selection);
    }
    public async Task<ShelfLibraryDisplay> ReloadForOriginalDisplayAsync(IShelfLibraryDisplay originalDisplay,
        CancellationToken token = default)
    {
        var original = await RequireAsync(originalDisplay, token).ConfigureAwait(false);
        await CaptureAsync(original, token).ConfigureAwait(false);
        var refreshed = await LoadForDisplayAsync(original.Actor, token, original.OriginalLifetime).ConfigureAwait(false);
        try
        {
            await RequireAsync(original, token).ConfigureAwait(false);
            if (refreshed.Selection is not Selection selection || selection.StoreID != original.StoreID)
                throw new UnauthorizedAccessException("Reload cannot adopt a replacement Shelf root.");
            return refreshed;
        }
        catch { refreshed.Selection.Dispose(); throw; }
    }
    private async Task<Selection> RequireAsync(IShelfLibraryDisplay display, CancellationToken token)
    {
        if (display is not Selection selection || !ReferenceEquals(selection.Issuer, this) || selection.Revoked)
            throw new UnauthorizedAccessException("Actual private Shelf display origin required.");
        if (await actors.GetCurrentAsync(token).ConfigureAwait(false) != selection.Actor)
            throw new UnauthorizedAccessException("Original displayed Shelf actor changed.");
        var identity = await library.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != selection.StoreID
            || await actors.GetCurrentAsync(token).ConfigureAwait(false) != selection.Actor || selection.Revoked)
            throw new UnauthorizedAccessException("Original displayed Shelf root changed.");
        return selection;
    }
    private async Task<ISettingsCommitAdmission> CaptureAsync(Selection selection, CancellationToken token)
    {
        await RequireAsync(selection, token).ConfigureAwait(false);
        if (ownership is not IResourceStoreOwnershipReceiptAuthority receipts || selection.Actor.AccountId is not null
            || selection.Actor.OrganisationId is not null) throw new UnauthorizedAccessException("Personal owner receipts required.");
        var binding = await ownership.GetVerifiedAsync("shelf", selection.StoreID.ToString("D"), token).ConfigureAwait(false);
        if (binding?.Receipt is null || binding.ResourceKind != "shelf" || binding.StoreId != selection.StoreID.ToString("D")
            || binding.ProfileId != selection.Actor.ProfileId || !await receipts.IsCurrentAsync(binding, selection.Actor, token).ConfigureAwait(false))
            throw new UnauthorizedAccessException("Explicit actual Home Shelf ownership required.");
        await RequireAsync(selection, token).ConfigureAwait(false);
        return new Admission(selection, receipts, binding);
    }
    private sealed class Admission(Selection selection,
        IResourceStoreOwnershipReceiptAuthority receipts, VerifiedResourceStoreOwnership binding) : ISettingsCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
        {
            if (selection.Revoked || context.StoreIdentity.SchemaVersion != 1 || context.StoreIdentity.StoreId != selection.StoreID) return false;
            var current = await receipts.IsCurrentAsync(binding, selection.Actor, token).ConfigureAwait(false);
            return current && !selection.Revoked; // No recursive owning settings read while leased.
        }
    }
    public async Task<IShelfLibraryReview> ReviewAsync(IShelfLibraryDisplay display, ShelfLaunchItem proposed,
        long? expectedObjectRevision = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        ArgumentNullException.ThrowIfNull(proposed.Target);
        // Detach before the first actor/identity/Home await. Typed proposal bytes are never authority.
        var captured = proposed with
        {
            Tags = proposed.Tags is null ? null : CaptureDeclaredCollection(proposed.Tags, value => value),
            Target = proposed.Target with { Arguments = proposed.Target.Arguments is null ? null : CaptureDeclaredCollection(proposed.Target.Arguments, value => value) }
        };
        var selection = await RequireAsync(display, token).ConfigureAwait(false);
        var snapshot = await library.ReadAsync(token).ConfigureAwait(false);
        if (snapshot.Library.Revision != selection.Revision) throw new InvalidOperationException("LibraryRevisionConflict");
        _ = ShelfLibraryPolicy.AddOrRefreshTarget(snapshot.Library, captured); // Actual owner preview validation, no storage effect.
        await CaptureAsync(selection, token).ConfigureAwait(false);
        var args = JsonSerializer.SerializeToElement(new { operationID = Guid.NewGuid(), storeID = selection.StoreID,
            revision = selection.Revision, proposed = captured, expectedObjectRevision });
        var scopes = new[] { new ResourceScope(ResourceKind, selection.StoreID.ToString("D"),
            selection.Revision.ToString(CultureInfo.InvariantCulture), ResourceAccess.Write) };
        await RequireAsync(selection, token).ConfigureAwait(false);
        var request = await home.AuthorizeForActorAsync(selection.Actor, "shelf", ActionID, scopes, args,
            "Review actual Shelf library change", null, "shelf-owned-library", token).ConfigureAwait(false);
        if (request.State != HomePermissionRequestState.PendingApproval) throw new UnauthorizedAccessException("Individual Home review required.");
        // Retain the exact issued request BEFORE any post-delivery await. Even a lost
        // origin/observation must leave the caller a controllable original Home request.
        var issued = new Review(this, selection, captured, expectedObjectRevision, args, scopes, request.RequestId);
        try { await RequireAsync(selection, token).ConfigureAwait(false); }
        catch (Exception) { issued.OriginAvailable = false; } // No grant, cancellation or audit inference.
        return issued;
    }
    public async Task<ShelfLibraryCommit> CommitAsync(IShelfLibraryReview issuedReview, CancellationToken token = default)
    {
        if (issuedReview is not Review review || !ReferenceEquals(review.Issuer, this)) throw new UnauthorizedAccessException("Private owning review required.");
        await review.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (review.Outcome is null)
            {
                if (review.CompletionUnknown)
                {
                    if (!await ObserveExactReceiptAsync(review, token).ConfigureAwait(false))
                        return new(false, "CompletionUnknown", false, true); // No Begin, Claim, owner write or guessed terminal audit.
                    review.Committed = true;
                    review.Outcome = new(HomePermissionRequestState.Succeeded, "ShelfLibraryCommitted", "Exact durable Shelf operation receipt observed.", []);
                }
                else if (review.AttemptReserved)
                    return await FinishRejectedAdmissionAsync(review).ConfigureAwait(false);
                else
                {
                    await RequireAsync(review.Selection, token).ConfigureAwait(false);
                    if (finalFence is null || !finalFence.IsFor(home, ownership))
                        return new(false, "FinalClaimFenceUnavailable", false); // No Begin/Claim; retain exact Home request.
                    var approval = await permissions.GetAuthorizationAsync(review.RequestID, token).ConfigureAwait(false);
                    if (!approval.IsAllowed) return new(false, "ApprovalRequired", false);
                    // Reserve the attempt before the first admission await. Repeated Finish cannot
                    // repeat Begin/Claim after a consumed request or unknown admission return.
                    review.AttemptReserved = true;
                    try { review.Capability = await home.BeginExecutionCapabilityAsync(review.RequestID, review.Arguments, token).ConfigureAwait(false); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                    { return await FinishRejectedAdmissionAsync(review).ConfigureAwait(false); }
                    if (review.Capability is null) return await FinishRejectedAdmissionAsync(review).ConfigureAwait(false);
                    AuthenticatedResourceActor? claimed;
                    try { claimed = await home.ClaimExecutionAsync(review.Capability, "shelf", ActionID, review.Scopes, review.Arguments, token).ConfigureAwait(false); }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                    { return await FinishRejectedAdmissionAsync(review).ConfigureAwait(false); }
                    if (claimed is null) return await FinishRejectedAdmissionAsync(review).ConfigureAwait(false);
                    review.ClaimSucceeded = true;
                    try
                    {
                        if (claimed != review.Selection.Actor) throw new UnauthorizedAccessException("Original reviewed actor differs from actual claim.");
                        var saved = await SaveFencedAsync(review, claimed, token).ConfigureAwait(false);
                        if (saved.ErrorCode == "CompletionUnknown")
                        {
                            review.CompletionUnknown = true;
                            if (!await ObserveExactReceiptAsync(review, token).ConfigureAwait(false)) return new(false, "CompletionUnknown", false, true);
                            review.Committed = true;
                        }
                        else review.Committed = saved.Success;
                        review.Outcome = new(review.Committed ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
                            review.Committed ? "ShelfLibraryCommitted" : saved.ErrorCode ?? "ShelfLibraryRejected", "Actual Shelf library mutation outcome.", []);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
                        or ArgumentException or OperationCanceledException or JsonException)
                    {
                        if (review.CompletionUnknown) return new(false, "CompletionUnknown", false, true);
                        review.Outcome = new(error is OperationCanceledException ? HomePermissionRequestState.Cancelled : HomePermissionRequestState.Failed,
                            error is UnauthorizedAccessException ? "PermissionDenied" : "ShelfLibraryRejected", "The actual Shelf mutation did not commit.", []);
                    }
                }
            }
            var recorded = false;
            try { recorded = (await home.CompleteExecutionAsync(review.Capability!, review.Outcome!, CancellationToken.None).ConfigureAwait(false)).Succeeded; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { }
            return new(review.Committed, review.Outcome!.Code, recorded);
        }
        finally { review.Gate.Release(); }
    }
    public async Task<ShelfLibraryCommit> FinishAsync(IShelfLibraryReview issuedReview, CancellationToken token = default)
    {
        if (issuedReview is not Review review || !ReferenceEquals(review.Issuer, this))
            throw new UnauthorizedAccessException("Private owning review required.");
        await review.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (review.Outcome is null && review.CompletionUnknown)
            {
                if (!await ObserveExactReceiptAsync(review, token).ConfigureAwait(false))
                    return new(false, "CompletionUnknown", false, true);
                review.Committed = true;
                review.Outcome = new(HomePermissionRequestState.Succeeded, "ShelfLibraryCommitted",
                    "Exact durable Shelf operation receipt observed.", []);
            }
            if (review.Outcome is null)
                return review.AttemptReserved && !review.ClaimSucceeded
                    ? await FinishRejectedAdmissionAsync(review).ConfigureAwait(false)
                    : new(false, "NoAttemptedOutcome", false);
            var recorded = false;
            try { recorded = (await home.CompleteExecutionAsync(review.Capability!, review.Outcome,
                CancellationToken.None).ConfigureAwait(false)).Succeeded; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException) { }
            return new(review.Committed, review.Outcome.Code, recorded);
        }
        finally { review.Gate.Release(); }
    }
    private async Task<ShelfOperationResult> SaveFencedAsync(Review review, AuthenticatedResourceActor claimed, CancellationToken token)
    {
        // Ordinary resource/ownership checks occur before Home is leased. The final wrapper never
        // calls Capture/Require/receipts again while holding the raw same-Home claim fence.
        await CaptureAsync(review.Selection, token).ConfigureAwait(false);
        if (finalFence is null || !finalFence.IsFor(home, ownership))
            throw new UnauthorizedAccessException("Actual same-composition final Home claim fence is unavailable.");
        await using var fence = await finalFence.CaptureAsync("shelf", review.Selection.StoreID,
            review.Capability!, claimed, () => !review.Selection.Revoked, token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home claim or ownership receipt is no longer current.");
        var admission = new FinalAdmission(review.Selection, fence);
        return await library.AddItemAsync(review.Revision, review.Proposed, admission, review.Receipt, token).ConfigureAwait(false);
        // await using disposes BEFORE caller observes receipts or records Home execution audit.
    }
    private sealed class FinalAdmission(Selection selection, HomeClaimedResourceCommitFence fence) : ISettingsCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token) =>
            !selection.Revoked && context.StoreIdentity.SchemaVersion == 1 && context.StoreIdentity.StoreId == selection.StoreID
            && await fence.ValidateAsync(token).ConfigureAwait(false) && !selection.Revoked;
    }

    private async Task<ShelfLibraryCommit> FinishRejectedAdmissionAsync(Review review)
    {
        try
        {
            var audit = review.Capability is null
                ? await home.RetryRejectedBeginAuditAsync(review.RequestID, CancellationToken.None).ConfigureAwait(false)
                : await home.RetryRejectedClaimAuditAsync(review.Capability, CancellationToken.None).ConfigureAwait(false);
            return new(false, audit.Succeeded ? "OwnerAdmissionRejected" : "OwnerAdmissionAuditPending", audit.Succeeded);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return new(false, "OwnerAdmissionAuditPending", false); }
    }
    private async Task<bool> ObserveExactReceiptAsync(Review review, CancellationToken token)
    {
        try
        {
            await RequireAsync(review.Selection, token).ConfigureAwait(false);
            await CaptureAsync(review.Selection, token).ConfigureAwait(false);
            var snapshot = await library.ReadAsync(token).ConfigureAwait(false);
            await RequireAsync(review.Selection, token).ConfigureAwait(false);
            await CaptureAsync(review.Selection, token).ConfigureAwait(false);
            return snapshot.LastOwnedMutation == review.Receipt && snapshot.Library.Revision >= review.Revision + 1;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        { return false; }
    }

    private static T[] CaptureDeclaredCollection<T>(IReadOnlyList<T> values, Func<T, T> detach)
    {
        ArgumentNullException.ThrowIfNull(values);
        var declared = values.Count;
        if (declared < 0) throw new ArgumentException("Invalid proposed collection count.");
        var captured = new List<T>();
        foreach (var value in values)
        {
            if (captured.Count == declared) throw new ArgumentException("Proposed enumeration exceeds declared count.");
            if (value is null) throw new ArgumentException("Proposed collection contains null.");
            captured.Add(detach(value));
        }
        if (captured.Count != declared) throw new ArgumentException("Proposed enumeration differs from declared count.");
        return captured.ToArray();
    }

}
