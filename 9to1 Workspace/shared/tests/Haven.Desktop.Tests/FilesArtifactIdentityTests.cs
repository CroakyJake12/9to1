using HavenOS.Files;

namespace Haven.Desktop.Tests;

public sealed class FilesArtifactIdentityTests
{
    [Fact]
    public async Task Owner_identity_survives_restart_and_rename_but_hidden_artifacts_are_unavailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-artifact-owner-query-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var path = Path.Combine(root, "drive.json");
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(path, location, "trusted-owner");
            var fileId = HostedItemId.New();
            var reference = new FilesArtifactReference("games", Guid.NewGuid().ToString("D"), fileId, null, "GameProject", "Project");
            Assert.True((await provider.RegisterArtifactAsync(reference, "trusted-owner", token)).IsSuccess);
            provider = new(path, location, "trusted-owner");
            Assert.Equal(fileId, (await provider.GetArtifactByOwnerIdentityAsync("games", reference.ArtifactId, token)).Value!.FileId);
            Assert.False((await provider.GetArtifactByOwnerIdentityAsync("canvas", reference.ArtifactId, token)).IsSuccess);
            var item = (await provider.GetAsync(fileId, token)).Value!;
            var now = DateTimeOffset.UtcNow;
            var rename = new FilesOperation(new(Guid.NewGuid()), "trusted-owner", fileId, null, null,
                "Rename", item.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(rename, "Renamed", token)).IsSuccess);
            Assert.Equal("Renamed", (await provider.GetArtifactByOwnerIdentityAsync("games", reference.ArtifactId, token)).Value!.DisplayName);
            item = (await provider.GetAsync(fileId, token)).Value!;
            Assert.True((await provider.MutateAsync(rename with { Id = new(Guid.NewGuid()), Operation = "Delete", BaseRevisionId = item.CurrentRevisionId }, null, token)).IsSuccess);
            Assert.False((await provider.GetArtifactByOwnerIdentityAsync("games", reference.ArtifactId, token)).IsSuccess);
        }
        finally { Directory.Delete(root, true); }
    }
}
