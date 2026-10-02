using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class PictureFilesCopyResourceAdmissionTests
{
    [Fact]
    public async Task Actual_configured_Picture_copy_sources_and_destination_require_exact_personal_owner_type_revision_and_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-picture-copy-admission-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var files = new NativeFilesWorkspaceService(home, profiles);
            var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([files]),
                new HomePermissionTrustService(home, (_, _) => null));
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            var workspace = await files.ConfigureNewAsync(chosen, ownership, token);
            var actor = workspace.Actor;
            var authority = new NativeFilesWorkspaceAuthority(files, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
            var provider = workspace.Provider;
            var folder = workspace.Configuration.AppFolders["picture"];
            var resolver = new FilesArtifactResourceResolver(async (current, ct) =>
            {
                var original = await authority.GetCurrentAsync(workspace.Configuration.StoreId, ct);
                return original?.Actor == current ? original.Provider : null;
            }, async (current, app, ct) =>
            {
                var original = await authority.GetCurrentAsync(workspace.Configuration.StoreId, ct);
                return original?.Actor == current && original.Configuration.AppFolders.TryGetValue(app, out var configured) ? configured : null;
            });
            var picture = HostedItemId.New();
            Assert.True((await provider.RegisterArtifactAsync(new("picture", Guid.NewGuid().ToString("N"), picture, folder,
                nameof(FilesArtifactType.Picture), "Source.9to1p"), actor.ActorId, token)).IsSuccess);
            var foreign = HostedItemId.New();
            Assert.True((await provider.RegisterArtifactAsync(new("write", Guid.NewGuid().ToString("N"), foreign, folder,
                nameof(FilesArtifactType.WriteDocument), "Foreign.9to1w"), actor.ActorId, token)).IsSuccess);
            var raw = HostedItemId.New(); var rawRevision = new FilesRevisionId(Guid.NewGuid());
            Assert.True((await provider.CommitUploadedContentAsync(new(raw, folder, "source.png", "image/png", rawRevision,
                null, actor.ActorId, DateTimeOffset.UtcNow, 4, "sha256:" + new string('a', 64), "source.png"), token)).IsSuccess);
            async Task<ResourceScope> Scope(HostedItemId id, ResourceAccess access)
            {
                var item = (await provider.GetAsync(id, token)).Value!;
                return new("files.item", id.ToString(), item.CurrentRevisionId?.ToString() ?? "uncommitted", access);
            }
            var source = await Scope(picture, ResourceAccess.Read);
            var destination = await Scope(folder, ResourceAccess.Write);
            var dependency = await Scope(raw, ResourceAccess.Read);
            var before = await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token);
            var driveFile = Path.Combine(chosen, ".9to1-files", "drive.json");
            var driveBefore = await File.ReadAllBytesAsync(driveFile, token);
            Assert.True((await resolver.EvaluateAsync(actor, "picture.file.copy", source, token)).Allowed);
            Assert.True((await resolver.EvaluateAsync(actor, "picture.file.copy", dependency, token)).Allowed);
            Assert.True((await resolver.EvaluateAsync(actor, "picture.file.copy", destination, token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor, "picture.file.copy", source with { Access = ResourceAccess.Write }, token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor, "picture.file.copy", dependency with { Access = ResourceAccess.Write }, token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor, "picture.file.copy", destination with { Access = ResourceAccess.Read }, token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor, "picture.file.copy", source with { Revision = Guid.NewGuid().ToString("D") }, token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor, "picture.file.copy", dependency with { Revision = Guid.NewGuid().ToString("D") }, token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor, "picture.file.copy", await Scope(foreign, ResourceAccess.Read), token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor, "picture.file.copy", await Scope(workspace.Configuration.AppFolders["write"], ResourceAccess.Write), token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor with { ActorId = "foreign" }, "picture.file.copy", source, token)).Allowed);
            Assert.False((await resolver.EvaluateAsync(actor with { OrganisationId = Guid.NewGuid() }, "picture.file.copy", source, token)).Allowed);
            Assert.False((await new FilesArtifactResourceResolver(_ => provider).EvaluateAsync(actor, "picture.file.copy", destination, token)).Allowed);
            Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(root, "home.json"), token));
            Assert.Equal(driveBefore, await File.ReadAllBytesAsync(driveFile, token));
        }
        finally { Directory.Delete(root, true); }
    }
}
