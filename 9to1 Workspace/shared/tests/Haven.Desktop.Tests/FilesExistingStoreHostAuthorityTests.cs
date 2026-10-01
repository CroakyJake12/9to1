using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FilesExistingStoreHostAuthorityTests
{
    [Fact]
    public async Task Actual_Home_configuration_and_existing_UUID_reads_refuse_substitution_without_rewriting_or_adopting()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-existing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home);
            services.AddSingleton(profiles);
            services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            services.AddSingleton<ResourceAuthorizationService>();
            services.AddFilesNativeHost();
            using var graph = services.BuildServiceProvider();
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            var service = graph.GetRequiredService<NativeFilesWorkspaceService>();
            var configured = await service.ConfigureNewAsync(chosen, graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var authority = graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            var storeID = configured.Configuration.StoreId;
            var workspace = Assert.IsType<NativeFilesWorkspace>(await authority.GetCurrentAsync(storeID, token));
            Assert.Equal(configured.Actor, workspace.Actor);
            var path = Path.Combine(chosen, ".9to1-files", "drive.json");
            var originalBytes = await File.ReadAllBytesAsync(path, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.GetCurrentAsync(Guid.NewGuid(), token));
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path, token));
            var replacementID = Guid.NewGuid();
            var replacement = JsonNode.Parse(originalBytes)!.AsObject();
            replacement["state"]!["storeId"] = replacementID;
            replacement["retainedEnvelopeMetadata"] = JsonNode.Parse("{\"opaque\":true}");
            replacement["state"]!["retainedUnknownPayload"] = JsonNode.Parse("{\"value\":null}");
            await File.WriteAllTextAsync(path, replacement.ToJsonString(), token);
            var substitutedBytes = await File.ReadAllBytesAsync(path, token);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.GetCurrentAsync(storeID, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.GetCurrentAsync(token));
            Assert.Null(await service.ReadAsync(storeID.ToString("D"), token));
            Assert.Equal(substitutedBytes, await File.ReadAllBytesAsync(path, token));
            // Even if configuration is replaced with B, a retained selection A fails before materializing B.
            // B remains unbound in real Home: this is not a claim of a valid B migration/import.
            var record = Assert.Single((await home.ReadAsync(token)).State!.Records, item => item.RecordType == "files.native-workspace");
            var stored = record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>()!;
            Assert.True((await home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(stored with { StoreId = replacementID }) }, record.Revision, token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => authority.GetCurrentAsync(storeID, token));
            Assert.Null(await authority.GetCurrentAsync(token));
            Assert.Equal(substitutedBytes, await File.ReadAllBytesAsync(path, token));
            Assert.Equal(configured.Actor, await profiles.GetCurrentAsync(token));
        }
        finally { Directory.Delete(root, true); }
    }
}
