using System.Security.Cryptography;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using Xunit;

namespace Haven.Desktop.Tests;

// Authored NOT RUN. Domain/custody refusal controls, not actual native Home/key/UI proof.
public sealed class DeveloperManualDraftRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-manual-recovery-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private static DeveloperDraftRecoveryPayload Draft(string text) => new([new("Main.cui", new string('a', 64), text, true)], "Main.cui");
    private static DeveloperDraftRecoveryScope Manual(Guid profile) => new(profile, Guid.Empty, Guid.Empty, Guid.Empty, IsManual: true);

    [Fact]
    public async Task Protected_key_preparation_runs_only_after_current_CAS_and_detects_existing_profile_recovery()
    {
        var (store, project) = await CreateAsync(); var scope = Manual(Guid.NewGuid()); var calls = 0;
        var first = await store.PrepareAndSaveDraftRecoveryAsync(project, scope, 0, (requiresExistingKey, _) =>
        {
            Assert.False(requiresExistingKey); calls++;
            return Task.FromResult(FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Draft("first draft"), _key));
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(first.Succeeded, first.Error?.Message);
        var stale = await store.PrepareAndSaveDraftRecoveryAsync(project, scope, 0, (_, _) =>
        { calls++; throw new InvalidOperationException("A stale writer must not touch its original key owner."); }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, stale.Error!.Code); Assert.Equal(1, calls);
        var anotherTask = new DeveloperDraftRecoveryScope(scope.ProfileId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var second = await store.PrepareAndSaveDraftRecoveryAsync(project, anotherTask, 0, (requiresExistingKey, _) =>
        {
            Assert.True(requiresExistingKey); // A different task cannot reset the shared profile/workspace key.
            return Task.FromResult(FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, anotherTask, 1, Draft("another task"), _key));
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(second.Succeeded, second.Error?.Message);
        Assert.Equal(first.Value, (await store.GetDraftRecoveryAsync(project, scope, cancellationToken: TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task Manual_restart_stays_unreviewed_and_older_acknowledgement_never_matches_newer_typing()
    {
        var (store, project) = await CreateAsync(); var scope = Manual(Guid.NewGuid());
        var older = Draft("older complete draft"); var newer = Draft("newer unsaved typing");
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, older, _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0, cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
        var read = await new FileDeveloperWorkspaceStore(_root).GetDraftRecoveryAsync(project, scope, cancellationToken: TestContext.Current.CancellationToken);
        var recovered = FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope, read.Value!, _key);
        Assert.True(DeveloperProjectWorkbenchPage.SameManualRecoveryPayload(older, recovered));
        Assert.False(DeveloperProjectWorkbenchPage.SameManualRecoveryPayload(newer, recovered));
        Assert.False(DeveloperProjectWorkbenchPage.SameManualRecoveryPayload(older with { SelectedRelativePath = null }, recovered));
        var taskScope = new DeveloperDraftRecoveryScope(scope.ProfileId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.ThrowsAny<CryptographicException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, taskScope,
            snapshot with { Scope = taskScope }, _key)); // Manual ciphertext cannot borrow a real Task partition.
        Assert.Empty((await store.GetAsync(project.Reference.WorkspaceId, cancellationToken: TestContext.Current.CancellationToken)).Value!.OpenEditors);
    }

    [Fact]
    public async Task Substitute_credential_provider_is_refused_before_any_source_or_secret_access()
    {
        var (store, project) = await CreateAsync(); var substitute = new RefusedSecrets();
        var owner = new DeveloperManualSourceReadOwner(null!, null!, null!, null!);
        var restored = await owner.RestoreManualDraftRecoveryAsync(store, substitute, new Paths(_root), project, TestContext.Current.CancellationToken);
        Assert.Equal(DeveloperOperationErrorCode.PermissionRequired, restored.Error!.Code);
        var saved = await owner.SaveManualDraftRecoveryAsync(store, null!, substitute, new Paths(_root), project,
            Manual(Guid.NewGuid()), 0, Draft("no source grant"), TestContext.Current.CancellationToken);
        Assert.Equal(DeveloperOperationErrorCode.PermissionRequired, saved.Error!.Code);
        Assert.Equal(0, substitute.Calls); // No fake-key or raw-path fallback can create a successful restore.
    }

    private async Task<(FileDeveloperWorkspaceStore Store, DeveloperResolvedProject Project)> CreateAsync()
    {
        var store = new FileDeveloperWorkspaceStore(_root);
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Path.GetTempPath()));
        var project = new DeveloperProject(Guid.NewGuid(), "cui-app", "Example", [root.RootId], "CUI", null, "cui", ["linux"], [], [], [], null);
        var created = await store.CreateAsync(DeveloperWorkspace.Create([root]) with { Projects = [project] }, TestContext.Current.CancellationToken);
        Assert.True(created.Succeeded, created.Error?.Message); var workspace = created.Value!;
        return (store, new(new(workspace.WorkspaceId, workspace.Revision, project.ProjectId, project.Revision, root.RootId), workspace, project, root, null));
    }
    private sealed class RefusedSecrets : IProviderSecretStore
    {
        public int Calls;
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken token) { Calls++; throw new InvalidOperationException("Must not access substitute keys."); }
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken token) { Calls++; throw new InvalidOperationException("Must not access substitute keys."); }
        public Task DeleteAsync(string providerId, string secretName, CancellationToken token) { Calls++; throw new InvalidOperationException("Must not access substitute keys."); }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
