using Haven.Application;
using System.Security.Cryptography;
using System.Text.Json;
namespace HavenOS.Home.Core;

// Requires only the additive partial class marker on the exact selected fence body.
// Capture is not Settings authority or canonical edit validation.
public sealed partial class HomeClaimedResourceCommitFence
{
    public static ValueTask<HomeClaimedResourceCommitFence?> CaptureShelfItemEditAsync(HomeResourceOperationBroker broker,
        FileHomeCoreStateStore store, HomeLocalProfileIdentity profiles, HomeResourceStoreOwnershipAuthority ownership,
        string originalStoreId, long originalRevision, string originalAction, JsonElement originalArguments,
        HomeResourceExecutionCapability originalCapability, AuthenticatedResourceActor originalActor,
        Func<bool> isOriginalLifetimeCurrent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(broker); ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(profiles); ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(originalCapability); ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(isOriginalLifetimeCurrent);
        if (originalAction != "shelf.item.edit" || originalRevision < 0 ||
            !Guid.TryParseExact(originalStoreId, "D", out var uuid) || uuid == Guid.Empty || originalStoreId != uuid.ToString("D"))
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        var admission = broker.CaptureClaimedAttestation(originalCapability);
        var revision = originalRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (admission is null || admission.Actor != originalActor || admission.Submission.Scope.TargetAppId != "shelf" ||
            admission.Submission.Scope.ActionName != originalAction ||
            admission.Submission.Impact.ResourceBinding is not { SchemaVersion: 1 } binding ||
            !binding.Scopes.Any(scope => scope.Kind == "shelf.library" && scope.Id == originalStoreId &&
                scope.Revision == revision && scope.Access == ResourceAccess.Write))
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        try
        {
            if (originalArguments.ValueKind != JsonValueKind.Object ||
                admission.Submission.Impact.ArgumentsDigest != Convert.ToHexString(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(originalArguments.GetRawText()))))
                return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
            var names = originalArguments.EnumerateObject().Select(value => value.Name).ToArray();
            if (names.Length != 5 || names.Distinct(StringComparer.Ordinal).Count() != 5 ||
                originalArguments.GetProperty("operationID").GetGuid() == Guid.Empty ||
                originalArguments.GetProperty("storeID").GetGuid() != uuid ||
                originalArguments.GetProperty("revision").GetInt64() != originalRevision ||
                originalArguments.GetProperty("operation").GetString() != originalAction)
                return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
            var edit = originalArguments.GetProperty("itemEdit");
            if (edit.ValueKind != JsonValueKind.Object) return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
            var editNames = edit.EnumerateObject().Select(value => value.Name).ToArray();
            if (editNames.Length != 6 || editNames.Distinct(StringComparer.Ordinal).Count() != 6 ||
                edit.GetProperty("ItemID").GetGuid() == Guid.Empty ||
                edit.GetProperty("Name").GetString() is not { Length: > 0 and <= 4096 } name || string.IsNullOrWhiteSpace(name) ||
                edit.GetProperty("Order").GetInt32() < 0 ||
                edit.GetProperty("Behaviour").GetInt32() is < 0 or > 3)
                return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
            _ = edit.GetProperty("IsFavourite").GetBoolean();
            var tags = edit.GetProperty("Tags");
            if (tags.ValueKind != JsonValueKind.Array || tags.GetArrayLength() > 256)
                return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
            foreach (var tag in tags.EnumerateArray())
                if (tag.GetString() is not { Length: > 0 and <= 4096 } value || string.IsNullOrWhiteSpace(value))
                    return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null); }
        return CaptureCoreAsync(broker, store, profiles, ownership, "shelf", originalStoreId,
            originalCapability, originalActor, [], isOriginalLifetimeCurrent, ct);
    }

}
