using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Home.Core;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Tests;

public sealed class WriteFilesArtifactBridgeTests
{
    [Fact]
    public async Task Replaced_Files_root_directory_is_denied_before_package_writes()
    {
        if (OperatingSystem.IsWindows()) return; // Symlink creation needs platform privileges on Windows.
        var root = Path.Combine(Path.GetTempPath(), "astra-write-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var actors = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), new OperatingSystemPrincipalSource());
            var actor = await actors.GetCurrentAsync(token);
            Assert.NotNull(actor);
            var provider = new DurableDriveProvider(Path.Combine(root, "drive.json"), new(Guid.NewGuid()), actor.ActorId);
            var now = DateTimeOffset.UtcNow;
            var folder = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, HostedItemId.New(), null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(folder, "Documents", token)).IsSuccess);
            var physical = Path.Combine(root, "physical");
            var elsewhere = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(physical);
            Directory.CreateDirectory(elsewhere);
            var profile = Guid.Parse(actor.ProfileId);
            var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(root, "bindings.json"), _ => null, id => id == profile ? provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder.ItemId, "write", physical, token)).IsSuccess);
            var document = new NotesDocument { Title = "Canonical document" };
            var fileId = HostedItemId.New();
            Assert.True((await provider.RegisterArtifactAsync(new("write", document.Id.ToString("N"), fileId, folder.ItemId,
                nameof(FilesArtifactType.WriteDocument), "Canonical.9to1w"), actor.ActorId, token)).IsSuccess);
            var packages = new ObservedPackages(new WriteNativeDocumentPackageStore());
            var authorization = new ResourceAuthorizationService(actors, [new FilesArtifactResourceResolver(_ => provider)]);
            var bridge = new WriteFilesArtifactBridge(actors, _ => provider, directories, packages, authorization, () => AppAiAccessMode.Write);
            Directory.Move(physical, Path.Combine(root, "preserved-original"));
            Directory.CreateSymbolicLink(physical, elsewhere);
            Assert.False((await directories.ResolveProfileAsync(profile, "write", token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.SaveAsync(fileId, document, null, token));
            Assert.Equal(0, packages.Saves);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Foreign_app_and_artifact_type_are_rejected_before_native_package_IO_even_with_bad_host_policy()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-write-foreign-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var actors = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), new OperatingSystemPrincipalSource());
            var actor = await actors.GetCurrentAsync(token);
            Assert.NotNull(actor);
            var provider = new DurableDriveProvider(Path.Combine(root, "drive.json"), new(Guid.NewGuid()), actor.ActorId);
            var now = DateTimeOffset.UtcNow;
            var folder = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, HostedItemId.New(), null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(folder, "Documents", token)).IsSuccess);
            var profile = Guid.Parse(actor.ProfileId);
            var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(root, "bindings.json"), _ => null, id => id == profile ? provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder.ItemId, "write", root, token)).IsSuccess);
            var actualPackages = new WriteNativeDocumentPackageStore();
            var observedPackages = new ObservedPackages(actualPackages);
            // A wrongly broad host adapter cannot substitute for owning-app type authority.
            var authorization = new ResourceAuthorizationService(actors, [new BadHostPolicy()]);
            var bridge = new WriteFilesArtifactBridge(actors, _ => provider, directories, observedPackages, authorization, () => AppAiAccessMode.Write);
            foreach (var (owner, type) in new[] { ("canvas", nameof(FilesArtifactType.Canvas)), ("write", nameof(FilesArtifactType.Presentation)) })
            {
                var document = new NotesDocument { Title = "Foreign owning application" };
                var fileId = HostedItemId.New();
                Assert.True((await provider.RegisterArtifactAsync(new(owner, document.Id.ToString("N"), fileId, folder.ItemId, type, fileId + ".9to1w"), actor.ActorId, token)).IsSuccess);
                var relative = fileId + ".9to1w";
                Assert.True((await actualPackages.SaveAsync(document, Path.Combine(root, relative), token)).IsSuccess);
                var bytes = await File.ReadAllBytesAsync(Path.Combine(root, relative), token);
                var committed = await provider.CommitDurableRevisionAsync(new(fileId, owner, Guid.NewGuid().ToString("N"), actor.ActorId,
                    now, bytes.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), relative, null), token);
                Assert.True(committed.IsSuccess);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.OpenAsync(fileId, token));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.SaveAsync(fileId, document, committed.Value!.Id, token));
            }
            Assert.Equal(0, observedPackages.Opens);
            Assert.Equal(0, observedPackages.Saves);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Real_local_profile_package_survives_restart_and_denies_readonly_and_changed_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-write-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var actors = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), new OperatingSystemPrincipalSource());
            var actor = await actors.GetCurrentAsync(TestContext.Current.CancellationToken);
            Assert.NotNull(actor);
            Assert.Null(actor.AccountId);
            Assert.Null(actor.OrganisationId);
            var profile = Guid.Parse(actor.ProfileId);
            var statePath = Path.Combine(root, "drive.json");
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(statePath, location, actor.ActorId);
            var now = DateTimeOffset.UtcNow;
            var folder = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, HostedItemId.New(), null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(folder, "Documents", TestContext.Current.CancellationToken)).IsSuccess);
            var bindingsPath = Path.Combine(root, "bindings.json");
            var directories = new FilesWorkspaceDirectoryResolver(bindingsPath, _ => null, id => id == profile ? provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder.ItemId, "write", root, TestContext.Current.CancellationToken)).IsSuccess);
            var document = new NotesDocument { Title = "Canonical original" };
            var fileId = HostedItemId.New();
            var reference = new FilesArtifactReference("write", document.Id.ToString("N"), fileId, folder.ItemId,
                "WriteDocument", "Canonical.9to1w");
            Assert.True((await provider.RegisterArtifactAsync(reference, actor.ActorId, TestContext.Current.CancellationToken)).IsSuccess);
            var mode = AppAiAccessMode.Write;
            DurableDriveProvider? Resolve(AuthenticatedResourceActor current) => current.ProfileId == actor.ProfileId ? provider : null;
            var authorization = new ResourceAuthorizationService(actors, [new FilesArtifactResourceResolver(Resolve)]);
            var bridge = new WriteFilesArtifactBridge(actors, Resolve, directories, new WriteNativeDocumentPackageStore(), authorization, () => mode);
            var initial = (await provider.GetAsync(fileId, TestContext.Current.CancellationToken)).Value!.CurrentRevisionId;
            var saved = await bridge.SaveAsync(fileId, document, initial, TestContext.Current.CancellationToken);
            document.Title = "Persisted edit";
            var edited = await bridge.SaveAsync(fileId, document, saved.Id, TestContext.Current.CancellationToken);
            provider = new DurableDriveProvider(statePath, location, actor.ActorId);
            directories = new FilesWorkspaceDirectoryResolver(bindingsPath, _ => null, id => id == profile ? provider : null);
            bridge = new WriteFilesArtifactBridge(actors, Resolve, directories, new WriteNativeDocumentPackageStore(), authorization, () => mode);
            var reopened = await bridge.OpenAsync(fileId, TestContext.Current.CancellationToken);
            Assert.Equal(document.Id, reopened.Id);
            Assert.Equal("Persisted edit", reopened.Title);
            Assert.Equal(fileId, (await provider.GetArtifactAsync(fileId, TestContext.Current.CancellationToken)).Value!.FileId);
            mode = AppAiAccessMode.ReadOnly;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.SaveAsync(fileId, document, edited.Id, TestContext.Current.CancellationToken));
            Assert.Equal(edited.Id, (await provider.GetAsync(fileId, TestContext.Current.CancellationToken)).Value!.CurrentRevisionId);
            var content = (await provider.GetCurrentArtifactContentAsync(fileId, TestContext.Current.CancellationToken)).Value!;
            await File.AppendAllTextAsync(Path.Combine(root, content.ProviderContentReference!), "changed bytes", TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.OpenAsync(fileId, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class BadHostPolicy : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "files.item";
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new ResourceAccessDecision(true, "MisconfiguredTestOnly", actor.ActorId, scope.Revision, actor.OrganisationId));
    }

    private sealed class ObservedPackages(IWriteNativeDocumentPackageStore actual) : IWriteNativeDocumentPackageStore
    {
        public int Opens { get; private set; }
        public int Saves { get; private set; }
        public Task<WriteNativeDocumentPackageResult<NotesDocument>> OpenAsync(string sourcePath, CancellationToken cancellationToken = default)
        { Opens++; return actual.OpenAsync(sourcePath, cancellationToken); }
        public Task<WriteNativeDocumentPackageResult<string>> SaveAsync(NotesDocument document, string destinationPath, CancellationToken cancellationToken = default)
        { Saves++; return actual.SaveAsync(document, destinationPath, cancellationToken); }
    }
}
