using System.Text.Json;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeCoreStateStoreTests
{
    [Fact]
    public async Task Store_creates_versioned_records_and_rejects_stale_revision()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        try
        {
            var store = new FileHomeCoreStateStore(path);
            var payload = JsonDocument.Parse("{\"mode\":\"dark\"}").RootElement.Clone();
            var record = new HomeCoreStateRecord("settings", "settings", 1, HomeDataScope.DeviceLocal,
                HomeRecordAuthority.LocalCanonical, 0, payload);
            var first = await store.WriteAsync(record, 0);
            Assert.True(first.IsSuccess);
            Assert.Equal(1, first.State!.Records.Single().Revision);
            var conflict = await new FileHomeCoreStateStore(path).WriteAsync(record, 0);
            Assert.Equal(HomeCoreErrorCode.HomeStateConflict, conflict.Failure!.Code);
            Assert.Equal("dark", (await store.ReadAsync()).State!.Records.Single().Payload.GetProperty("mode").GetString());
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Store_reports_corruption_without_replacing_the_original_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"home-state-{Guid.NewGuid():N}.json");
        const string original = "{ not valid state";
        try
        {
            await File.WriteAllTextAsync(path, original);
            var result = await new FileHomeCoreStateStore(path).ReadAsync();
            Assert.Equal(HomeCoreErrorCode.HomeStateCorrupt, result.Failure!.Code);
            Assert.Equal(original, await File.ReadAllTextAsync(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
