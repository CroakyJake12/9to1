using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Home.Core;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Tests;

public sealed class WriteFilesArtifactSnapshotTests
{
    [Fact]
    public async Task Save_publishes_the_validated_snapshot_when_editor_mutates_during_package_IO()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-write-snapshot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var actors = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), new OperatingSystemPrincipalSource());
            var actor = (await actors.GetCurrentAsync(token))!;
            var provider = new DurableDriveProvider(Path.Combine(root, "drive.json"), new(Guid.NewGuid()), actor.ActorId);
            var now = DateTimeOffset.UtcNow;
            var folder = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, HostedItemId.New(), null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(folder, "Documents", token)).IsSuccess);
            var profile = Guid.Parse(actor.ProfileId);
            var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(root, "bindings.json"), _ => null, id => id == profile ? provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder.ItemId, "write", root, token)).IsSuccess);
            var block = NotesBlock.CreateParagraph();
            var run = new NotesTextRun { Text = "Approved original", Bold = true };
            block.Runs = [run];
            var document = new NotesDocument { Title = "Approved title" };
            document.Sections[0].Pages[0].Blocks = [block];
            var originalId = document.Id;
            var fileId = HostedItemId.New();
            Assert.True((await provider.RegisterArtifactAsync(new("write", originalId.ToString("N"), fileId, folder.ItemId,
                nameof(FilesArtifactType.WriteDocument), "Canonical.9to1w"), actor.ActorId, token)).IsSuccess);
            var packages = new DelayedPackages(new WriteNativeDocumentPackageStore());
            var resources = new ResourceAuthorizationService(actors, [new FilesArtifactResourceResolver(_ => provider)]);
            var bridge = new WriteFilesArtifactBridge(actors, _ => provider, directories, packages, resources, () => AppAiAccessMode.Write);
            var save = bridge.SaveAsync(fileId, document, null, token);
            try
            {
                await packages.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                document.Id = Guid.NewGuid(); document.Title = "Concurrent replacement";
                run.Text = "Unapproved replacement"; run.Bold = false;
                document.Sections[0].Pages[0].Blocks.Add(NotesBlock.CreateParagraph("Concurrent extra block"));
            }
            finally { packages.Release.TrySetResult(); }
            var committed = await save;
            var reopened = await bridge.OpenAsync(fileId, token);
            Assert.Equal(originalId, reopened.Id);
            Assert.Equal("Approved title", reopened.Title);
            var savedBlock = Assert.Single(Assert.Single(Assert.Single(reopened.Sections).Pages).Blocks);
            Assert.Equal(block.Id, savedBlock.Id);
            var savedRun = Assert.Single(savedBlock.Runs);
            Assert.Equal(run.Id, savedRun.Id);
            Assert.Equal("Approved original", savedRun.Text); Assert.True(savedRun.Bold);
            Assert.Equal(committed.Id, (await provider.GetAsync(fileId, token)).Value!.CurrentRevisionId);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Save_denies_changed_authenticated_session_while_waiting_for_Files_commit_lease()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-write-commit-actor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? holder = null;
        try
        {
            var profiles = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), new OperatingSystemPrincipalSource());
            var actor = (await profiles.GetCurrentAsync(token))!;
            var actors = new ChangingActor(profiles);
            var statePath = Path.Combine(root, "drive.json");
            var provider = new DurableDriveProvider(statePath, new(Guid.NewGuid()), actor.ActorId);
            var now = DateTimeOffset.UtcNow;
            var folder = HostedItemId.New();
            Assert.True((await provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), actor.ActorId, folder, null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null), "Documents", token)).IsSuccess);
            var profile = Guid.Parse(actor.ProfileId);
            var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(root, "bindings.json"), _ => null, id => id == profile ? provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder, "write", root, token)).IsSuccess);
            var document = new NotesDocument { Title = "Exact current session" };
            var fileId = HostedItemId.New();
            Assert.True((await provider.RegisterArtifactAsync(new("write", document.Id.ToString("N"), fileId, folder,
                nameof(FilesArtifactType.WriteDocument), "Canonical.9to1w"), actor.ActorId, token)).IsSuccess);
            var before = await File.ReadAllBytesAsync(statePath, token);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var beforeCommit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            DurableDriveProvider Supply(AuthenticatedResourceActor current)
            {
                Assert.Equal(actor, current);
                if (Interlocked.Increment(ref calls) == 2)
                {
                    var store = new VersionedJsonStateStore<DurableDriveProvider.State>(statePath, 1, () => throw new InvalidOperationException());
                    holder = Task.Run(() => store.UpdateAsync(state =>
                    {
                        entered.TrySetResult();
                        release.Task.WaitAsync(TimeSpan.FromSeconds(15), token).GetAwaiter().GetResult();
                        return state;
                    }, token), token);
                    entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token).GetAwaiter().GetResult();
                    beforeCommit.TrySetResult();
                }
                return provider;
            }
            var resources = new ResourceAuthorizationService(actors, [new FilesArtifactResourceResolver(_ => provider)]);
            var bridge = new WriteFilesArtifactBridge(actors, Supply, directories, new WriteNativeDocumentPackageStore(), resources, () => AppAiAccessMode.Write);
            var save = bridge.SaveAsync(fileId, document, null, token);
            await beforeCommit.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            actors.ChangeSession = true;
            release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => save);
            await holder!;
            Assert.Null((await provider.GetAsync(fileId, token)).Value!.CurrentRevisionId);
            Assert.Equal(before, await File.ReadAllBytesAsync(statePath, token));
        }
        finally
        {
            release.TrySetResult();
            if (holder is not null) await holder;
            Directory.Delete(root, true);
        }
    }

    private sealed class ChangingActor(IAuthenticatedResourceActorSource inner) : IAuthenticatedResourceActorSource
    {
        public volatile bool ChangeSession;
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken = default)
        {
            var actor = await inner.GetCurrentAsync(cancellationToken);
            return actor is not null && ChangeSession ? actor with { AuthenticationRevision = actor.AuthenticationRevision + ":changed" } : actor;
        }
    }

    private sealed class DelayedPackages(IWriteNativeDocumentPackageStore inner) : IWriteNativeDocumentPackageStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WriteNativeDocumentPackageResult<NotesDocument>> OpenAsync(string sourcePath, CancellationToken token = default)
            => inner.OpenAsync(sourcePath, token);
        public async Task<WriteNativeDocumentPackageResult<string>> SaveAsync(NotesDocument document, string destinationPath, CancellationToken token = default)
        {
            Entered.TrySetResult(); await Release.Task.WaitAsync(token);
            return await inner.SaveAsync(document, destinationPath, token);
        }
    }
}
