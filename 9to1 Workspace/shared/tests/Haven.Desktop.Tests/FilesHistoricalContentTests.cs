using HavenOS.Files;

namespace Haven.Desktop.Tests;

public sealed class FilesHistoricalContentTests
{
    [Fact]
    public async Task Retained_content_proof_survives_replacement_restart_but_never_crosses_item_or_trash()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-content-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var path = Path.Combine(root, "drive.json");
            var location = new FilesLocationId(Guid.NewGuid());
            const string actor = "trusted-fixture-owner";
            var provider = new DurableDriveProvider(path, location, actor);
            var now = DateTimeOffset.UtcNow;
            var parent = HostedItemId.New();
            var folder = new FilesOperation(new(Guid.NewGuid()), actor, parent, null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(folder, "Images", token)).IsSuccess);
            var file = HostedItemId.New();
            var first = new FilesRevisionId(Guid.NewGuid());
            var second = new FilesRevisionId(Guid.NewGuid());
            var upload = new FilesUploadedContent(file, parent, "source.png", "image/png", first, null, actor, now,
                4, new string('a', 64), "immutable/first.png");
            Assert.True((await provider.CommitUploadedContentAsync(upload, token)).IsSuccess);
            Assert.True((await provider.CommitUploadedContentAsync(upload with { RevisionId = second, ExpectedRevision = first,
                ContentHash = new string('b', 64), ProviderContentReference = "immutable/second.png" }, token)).IsSuccess);
            provider = new(path, location, actor);
            var historic = await provider.GetArtifactRevisionContentAsync(file, first, token);
            Assert.True(historic.IsSuccess);
            Assert.Equal("immutable/first.png", historic.Value!.ProviderContentReference);
            Assert.Equal(first, historic.Value.Revision.Id);
            Assert.False(historic.Value.Revision.IsCurrent);
            Assert.Equal(second, (await provider.GetCurrentArtifactContentAsync(file, token)).Value!.Revision.Id);
            Assert.False((await provider.GetArtifactRevisionContentAsync(HostedItemId.New(), first, token)).IsSuccess);
            Assert.False((await provider.GetArtifactRevisionContentAsync(file, new(Guid.NewGuid()), token)).IsSuccess);
            var folderRevision = (await provider.GetAsync(parent, token)).Value!.CurrentRevisionId;
            var delete = new FilesOperation(new(Guid.NewGuid()), actor, parent, null, null, "Delete", folderRevision, null,
                FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(delete, null, token)).IsSuccess);
            Assert.False((await provider.GetArtifactRevisionContentAsync(file, first, token)).IsSuccess);
        }
        finally { Directory.Delete(root, true); }
    }
}
