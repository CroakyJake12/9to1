using System.Security.Cryptography;
using System.Text.Json;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

// These controls cover retained-byte storage and issuer refusal. They supply no
// actual signed installer, enrolled Root, installation or Windows runtime proof.
public sealed class NativeWindowsHomeRootCatalogueTransferTests
{
    [Fact]
    public void Exact_catalogue_bytes_survive_same_enrollment_record_roundtrip_and_detached_digest_tampering_refuses()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\n  \"schemaVersion\": 1, \"opaqueOriginal\": \"unaltered\"\n}");
        var original = Record(bytes);
        var raw = JsonSerializer.SerializeToUtf8Bytes(original);
        var reopened = JsonSerializer.Deserialize<HomePackageOriginalPublisherEnrollmentRecord>(raw)!;
        Assert.Equal(bytes, NativeWindowsHomeInstalledRootAdmission.DecodeOriginalRetainedCatalogue(reopened));
        Assert.Equal(original, reopened with { SelectedPackageIds = original.SelectedPackageIds });
        var changed = reopened with { OriginalSignedCatalogueBase64 = Convert.ToBase64String([1, 2, 3]) };
        Assert.Throws<UnauthorizedAccessException>(() => NativeWindowsHomeInstalledRootAdmission.DecodeOriginalRetainedCatalogue(changed));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Historical_or_unknown_enrollment_schema_never_becomes_a_retained_catalogue_source(int schema)
    {
        var historical = Record([1, 2, 3]) with { SchemaVersion = schema };
        Assert.Throws<InvalidDataException>(() => NativeWindowsHomeInstalledRootAdmission.DecodeOriginalRetainedCatalogue(historical));
    }

    [Fact]
    public async Task Detached_metadata_even_with_matching_bytes_and_known_owner_cannot_issue_protected_transfer_or_touch_storage()
    {
        var file = Path.Combine(Path.GetTempPath(), "root-transfer-uncreated-" + Guid.NewGuid().ToString("N") + ".json");
        var store = new FileHomeCoreStateStore(file);
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var database = new HomePackageDatabase(store);
        var publisher = new NativeWindowsHomeInstallerBootstrapAdmission(profiles);
        var actual = new NativeWindowsHomeInstalledRootAdmission(publisher, database, store, profiles, file);
        Task? actualClose = null; Task? publisherClose = null; var failures = new List<Exception>();
        try
        {
            var bytes = new byte[] { 1, 2, 3 };
            var detached = new NativeWindowsHomeInstalledRootAdmission.OriginalProtectedEnrollmentCatalogue(
                actual, null!, HomePackageDatabaseSnapshot.Empty, Record(bytes), bytes, "detached");
            Assert.Throws<UnauthorizedAccessException>(() => actual.DemandOriginalProtectedCatalogueCurrent(detached, publisher));
            Assert.Throws<UnauthorizedAccessException>(() => actual.ReadOriginalProtectedCatalogueBytes(detached, publisher, "unissued"));
            Assert.False(File.Exists(file));
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            try { actualClose = actual.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            try { publisherClose = publisher.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            foreach (var same in new[] { actualClose, publisherClose })
                if (same is not null) try { await same; } catch (Exception cause) { failures.Add(same.Exception ?? cause); }
        }
        if (failures.Count != 0) throw new AggregateException("Actual transfer issuer refusal/independent close failed.", failures);
        Assert.Same(actualClose, actual.OriginalClose); Assert.Same(publisherClose, publisher.OriginalClose);
        Assert.False(File.Exists(file));
    }

    private static HomePackageOriginalPublisherEnrollmentRecord Record(byte[] bytes) => new(
        HomePackageOriginalPublisherEnrollmentRecord.RetainedCatalogueSchemaVersion,
        "certificate-observation", "issuer-observation", "release-observation", Convert.ToHexString(SHA256.HashData(bytes)),
        "installer-observation", Guid.NewGuid(), "actor-observation", "profile-observation", "principal-observation",
        "home", "root", Array.AsReadOnly(new[] { "home", "root" }), DateTimeOffset.UtcNow, Convert.ToBase64String(bytes));
}
