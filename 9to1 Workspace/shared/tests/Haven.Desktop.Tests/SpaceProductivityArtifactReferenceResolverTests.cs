using System.Security.Cryptography;
using Haven.Core;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class SpaceProductivityArtifactReferenceResolverTests
{
    [Fact]
    public async Task Shared_reference_reads_actual_owners_after_Move_and_rejects_stale_or_changed_authority()
    {
        var token = TestContext.Current.CancellationToken;
    var root = Path.Combine(Path.GetTempPath(), "astra-shared-reference-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var actor = (await profiles.GetCurrentAsync(token))!;
        var settings = new VersionedAtomicSettingsStore(new Paths(Path.Combine(root, "settings")));
        var files = new NativeFilesWorkspaceService(home, profiles);
        var permissions = new HomePermissionTrustService(home, (_, _) => null);
        var ownership = new HomeLocalStoreOwnership(home, profiles,
            new HomeLocalStoreEvidenceRegistry([files, new SpacesLocalStoreEvidenceProvider(settings, settings)]), permissions);
        var storeOwnership = new HomeResourceStoreOwnershipAuthority(ownership, profiles);
        var settingsId = await settings.GetStoreIdentityAsync(token);
        Assert.NotNull(await ownership.BindNewEmptyAsync("spaces", settingsId.StoreId.ToString("D"), token));
        var chosen = Path.Combine(root, "files"); Directory.CreateDirectory(chosen);
        var workspace = await files.ConfigureNewAsync(chosen, ownership, token);
        var filesAuthority = new NativeFilesWorkspaceAuthority(files, profiles, storeOwnership);
        var spaces = new SpaceRegistry(settings);
        var resources = new ResourceAuthorizationService(profiles,
            [new FilesArtifactResourceResolver(async (current, ct) =>
                (await filesAuthority.GetCurrentAsync(ct))?.Actor == current ? workspace.Provider : null,
                async (current, app, ct) => (await filesAuthority.GetCurrentAsync(ct))?.Actor == current &&
                    workspace.Configuration.AppFolders.TryGetValue(app, out var folder) ? folder : null),
            new SpaceSourceResourceResolver(profiles, settings, storeOwnership, spaces, () => true)]);
        var block = NotesBlock.CreateParagraph();
        block.Runs = [new NotesTextRun { Text = "Canonical rich paragraph", Bold = true, Italic = true, FontFamily = "fonts:Inter#Inter" }];
        var document = new NotesDocument { Title = "Canonical Write" };
        document.Sections[0].Pages[0].Blocks = [block];
        var packages = new WriteNativeDocumentPackageStore();
        var packagePath = Path.Combine(chosen, "Write", "document.9to1w");
        Assert.True((await packages.SaveAsync(document, packagePath, token)).IsSuccess);
        var bytes = await File.ReadAllBytesAsync(packagePath, token);
        var fileId = HostedItemId.New();
        var reference = new FilesArtifactReference("write", document.Id.ToString("N"), fileId,
            workspace.Configuration.AppFolders["write"], nameof(FilesArtifactType.WriteDocument), "Document.9to1w");
        Assert.True((await workspace.Provider.RegisterArtifactAsync(reference, actor.ActorId, token)).IsSuccess);
        var committed = await workspace.Provider.CommitDurableRevisionAsync(new(fileId, "write", "owning-revision", actor.ActorId,
            DateTimeOffset.UtcNow, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), "document.9to1w", null), token);
        Assert.True(committed.IsSuccess);
        var now = DateTimeOffset.UtcNow;
        var moved = await workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, fileId,
            reference.ParentFolderId, workspace.Configuration.AppFolders["boards"], "Move", committed.Value!.Id,
            null, FilesOperationState.Pending, now, now, null, null), null, token);
        Assert.True(moved.IsSuccess);
        var source = new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.WriteArtifact, "write",
            document.Id.ToString("N"), "owning-revision", SpaceContextPermission.Read, SpaceContextIndexState.NotRequired, false, now, fileId.Value);
        var space = await spaces.CreateAsync("Canonical Write", cancellationToken: token);
        space = await spaces.UpdateAsync(space with { ContextReferences = [source] }, space.Revision, token);
        var router = new SpaceFilesArtifactActionRouter(spaces, filesAuthority, profiles, resources);
        var resolver = new SpaceProductivityArtifactReferenceResolver(router);
        var pointer = new ProductivityArtifactReferenceSource(space.Id, space.Revision, source, moved.Value!.ResultRevisionId!.Value.Value);
        var resolved = await resolver.ResolveAsync(pointer, token);
        Assert.Equal(pointer, resolved.Source);
        Assert.Equal(reference.DisplayName, resolved.DisplayName);
        Assert.Equal(nameof(FilesArtifactType.WriteDocument), resolved.ArtifactType);
        using var prepared = await HomePreparedArtifactReference.PrepareAsync(resolver, pointer, token);
        var handler = new HomeArtifactReferenceObjectHandler(prepared);
        var shared = handler.Create(source.ContextId, HomeArtifactReferenceObjectHandler.ReferenceContent(pointer));
        Assert.Equal(source.ContextId, shared.ObjectId);
        Assert.Contains(reference.DisplayName, handler.Render(shared).CuiSource);
        Assert.Empty(handler.Actions);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(pointer with
        { Context = source with { Permission = SpaceContextPermission.ReadWrite } }, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(pointer with
        { FilesRevisionId = committed.Value!.Id.Value }, token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(pointer with
        { Context = source with { RevisionToken = "different-owner-revision" } }, token));
        space = await spaces.UpdateAsync(space with { ContextReferences = [source with { Permission = SpaceContextPermission.Denied }] }, space.Revision, token);
        await Assert.ThrowsAsync<SpaceRevisionConflictException>(() => resolver.ResolveAsync(pointer, token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(pointer with
        { SpaceRevision = space.Revision, Context = source with { Permission = SpaceContextPermission.Denied } }, token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(packagePath, token));
    }
    finally { Directory.Delete(root, true); }
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
