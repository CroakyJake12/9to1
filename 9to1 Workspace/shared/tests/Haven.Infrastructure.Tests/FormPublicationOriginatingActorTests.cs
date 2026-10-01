using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Tests;

public sealed class FormPublicationOriginatingActorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Bound_host_session_rechecks_original_actor_or_disposal_after_actual_final_settings_lease_admission(bool dispose)
    {
        using var paths = new Paths(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var home = new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var actor = new MutableActor((await profiles.GetCurrentAsync(token)) ?? throw new InvalidOperationException("Home actor required."));
        var settings = new VersionedAtomicSettingsStore(paths);
        var evidence = new FormLocalStoreEvidenceProvider(settings, settings);
        var permissions = new HomePermissionTrustService(home, (_, _) => null);
        var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([evidence]), permissions);
        var actual = new FormLocalStoreAuthority(settings, settings, actor, new HomeResourceStoreOwnershipAuthority(ownership, actor));
        var barrier = new FinalLeaseAdmission(actual);
        var canonical = new FormPublicationService(settings, settings, barrier, new Validator(), actors: actor);
        var identity = await settings.GetStoreIdentityAsync(token); await ownership.BindNewEmptyAsync("forms", identity.StoreId.ToString("D"));
        var session = await canonical.OpenHostSessionAsync(actor.Current, token);
        var file = Path.Combine(paths.DataDirectory, "settings.json"); var before = await File.ReadAllBytesAsync(file, token);
        barrier.Armed = true;
        var pending = session.Authoring.CreateAsync("Denied final lease", FormModeKind.Form, token);
        await barrier.Entered.Task.WaitAsync(token);
        if (dispose) session.Dispose();
        else actor.Current = actor.Current with { AuthenticationRevision = "changed-under-actual-settings-lease" };
        barrier.Release.TrySetResult();
        Assert.False((await pending).Success);
        Assert.Equal(before, await File.ReadAllBytesAsync(file, token));
        if (dispose)
        {
            Assert.Equal("PermissionDenied", (await session.Publications.ReadAsync(Guid.NewGuid(), token)).Code);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.RequireCurrentAsync(token));
            // Closing AFTER a successful physical commit preserves its known result; it does not undo/replay it.
            var fresh = await canonical.OpenHostSessionAsync(actor.Current, token);
            var committed = await fresh.Authoring.CreateAsync("Committed before close", FormModeKind.Form, token);
            Assert.True(committed.Success);
            var committedBytes = await File.ReadAllBytesAsync(file, token);
            fresh.Dispose();
            var recovered = await canonical.ReadAsync(committed.Publication!.FormID, token);
            Assert.True(recovered.Success);
            Assert.Equal(JsonSerializer.Serialize(committed.Publication), JsonSerializer.Serialize(recovered.Publication));
            Assert.Equal(committedBytes, await File.ReadAllBytesAsync(file, token));
        }
    }
    private sealed class FinalLeaseAdmission(IFormStoreCommitAuthority actual) : IFormStoreCommitAuthority
    {
        public bool Armed { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string action, CancellationToken token)
            => actual.AuthorizeAsync(storeID, formID, revision, action, token);
        public async ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string action, AuthenticatedResourceActor? expectedActor, CancellationToken token)
        {
            var admission = await actual.CaptureCommitAdmissionAsync(storeID, formID, revision, action, expectedActor, token);
            return admission is null ? null : new HeldAdmission(this, admission);
        }
        private sealed class HeldAdmission(FinalLeaseAdmission owner, ISettingsCommitAdmission actual) : ISettingsCommitAdmission
        {
            public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
            {
                var allowed = await actual.CheckAsync(context, token);
                if (owner.Armed) { owner.Armed = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(token); }
                return allowed;
            }
        }
    }

    [Theory]
    [InlineData("bind")]
    [InlineData("dispose-final-read")]
    [InlineData("create")]
    [InlineData("read")]
    [InlineData("start")]
    public async Task Issued_host_session_denies_actor_switch_during_suspended_bind_or_operation(string operation)
    {
        using var paths = new Paths(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var home = new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var original = (await profiles.GetCurrentAsync(token)) ?? throw new InvalidOperationException("Actual Home actor required.");
        var actors = new SuspendedActor(original);
        var actual = new VersionedAtomicSettingsStore(paths);
        var evidence = new FormLocalStoreEvidenceProvider(actual, actual);
        var permissions = new HomePermissionTrustService(home, (_, _) => null);
        var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([evidence]), permissions);
        var authority = new FormLocalStoreAuthority(actual, actual, actors, new HomeResourceStoreOwnershipAuthority(ownership, actors));
        var canonical = new FormPublicationService(actual, actual, authority, new Validator(), actors: actors);
        var identity = await actual.GetStoreIdentityAsync(token); await ownership.BindNewEmptyAsync("forms", identity.StoreId.ToString("D"));
        var project = FormProjectEditor.Create("Session origin", FormModeKind.Form, DateTimeOffset.UtcNow);
        Assert.True((await canonical.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Success);
        var published = await canonical.PublishAsync(project.FormID, 1, token); Assert.True(published.Success);
        var session = await canonical.OpenHostSessionAsync(original, token);
        var file = Path.Combine(paths.DataDirectory, "settings.json"); var before = await File.ReadAllBytesAsync(file, token);
        actors.Armed = true;
        if (operation == "dispose-final-read")
        {
            actors.ReadsUntilHold = 2;
            var pending = session.RequireCurrentAsync(token);
            await actors.Entered.Task.WaitAsync(token); session.Dispose(); actors.Release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
            Assert.Equal(before, await File.ReadAllBytesAsync(file, token));
            Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
            return;
        }
        if (operation == "bind")
        {
            var pending = canonical.OpenHostSessionAsync(original, token); await actors.Entered.Task.WaitAsync(token);
            actors.Current = original with { AuthenticationRevision = "independently-valid-new-session" }; actors.Release.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        }
        else
        {
            var pending = operation switch
            {
                "create" => session.Authoring.CreateAsync("Rejected", FormModeKind.Form, token),
                "read" => session.Publications.ReadAsync(project.FormID, token),
                _ => ObserveStart(session, project.FormID, published.Publication!.Revision, token)
            };
            await actors.Entered.Task.WaitAsync(token); actors.Current = original with { AuthenticationRevision = "independently-valid-new-session" }; actors.Release.TrySetResult();
            Assert.False((await pending).Success);
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(file, token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.RequireCurrentAsync(token));
        var fresh = await canonical.OpenHostSessionAsync(actors.Current, token);
        Assert.True((await fresh.Publications.ReadAsync(project.FormID, token)).Success);
        Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
    }
    private static async Task<FormPublicationResult> ObserveStart(FormHostSession session, Guid formID, long revision, CancellationToken token)
    {
        var result = await session.Responses.StartAsync(formID, revision, token);
        return new(result.Success, result.Code, null);
    }
    private sealed class SuspendedActor(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = actor;
        public bool Armed { get; set; }
        public int ReadsUntilHold { get; set; } = 1;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            var captured = Current;
            if (Armed && --ReadsUntilHold == 0) { Armed = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return captured;
        }
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("publish")]
    [InlineData("close")]
    [InlineData("read")]
    public async Task Actual_Home_owned_publication_rejects_reauthentication_during_initial_storage_wait(string operation)
    {
        using var paths = new Paths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token;
        var home = new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var actors = new MutableActor((await profiles.GetCurrentAsync(token))!);
        var original = actors.Current;
        var actual = new VersionedAtomicSettingsStore(paths);
        var evidence = new FormLocalStoreEvidenceProvider(actual, actual);
        var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([evidence]),
            new HomePermissionTrustService(home, (_, _) => null));
        var authority = new FormLocalStoreAuthority(actual, actual, actors, new HomeResourceStoreOwnershipAuthority(ownership, actors));
        var identity = await actual.GetStoreIdentityAsync(token);
        await ownership.BindNewEmptyAsync("forms", identity.StoreId.ToString("D"));
        var held = new HeldStore(actual);
        var service = new FormPublicationService(held, actual, authority, new Validator(), actors: actors);
        var project = FormProjectEditor.Create("Original author", FormModeKind.Form, DateTimeOffset.UtcNow);
        if (operation != "create") Assert.True((await service.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Success);
        var file = Path.Combine(paths.DataDirectory, "settings.json");
        var before = await File.ReadAllBytesAsync(file, token);
        held.Armed = true;
        var pending = operation switch
        {
            "create" => service.CreateAsync(project.FormID, FormProjectEditor.Project(project), token),
            "edit" => service.SaveDraftAsync(project.FormID, 1, FormProjectEditor.Project(project), token),
            "publish" => service.PublishAsync(project.FormID, 1, token),
            "close" => service.CloseAsync(project.FormID, 1, token),
            _ => service.ReadAsync(project.FormID, token)
        };
        await held.Entered.Task.WaitAsync(token);
        actors.Current = original with { AuthenticationRevision = "reauthenticated-during-original-operation" };
        held.Release.TrySetResult();
        var denied = await pending;
        Assert.False(denied.Success); Assert.Equal("PermissionDenied", denied.Code); Assert.Null(denied.Publication);
        Assert.Equal(before, await File.ReadAllBytesAsync(file, token));
        // A new operation under the now-valid authentication is independently
        // permitted. The crossed operation cannot silently migrate to it.
        if (operation == "create") Assert.True((await service.CreateAsync(project.FormID, FormProjectEditor.Project(project), token)).Success);
        var retained = await service.ReadAsync(project.FormID, token);
        Assert.True(retained.Success); Assert.Equal(1, retained.Publication!.Revision);
        Assert.Empty(retained.Publication.Versions);
    }

    [Fact]
    public async Task Missing_typed_actor_port_denies_before_authority_or_storage_is_used()
    {
        using var paths = new Paths();
        var actual = new VersionedAtomicSettingsStore(paths);
        var denied = new FormPublicationService(actual, actual, new NeverAuthority(), new Validator());
        var project = FormProjectEditor.Create("Missing actor", FormModeKind.Form, DateTimeOffset.UtcNow);
        Assert.Equal("PermissionDenied", (await denied.CreateAsync(project.FormID, FormProjectEditor.Project(project))).Code);
        Assert.Equal("PermissionDenied", (await denied.ReadAsync(project.FormID)).Code);
        Assert.False(File.Exists(Path.Combine(paths.DataDirectory, "settings.json")));
    }

    private sealed class MutableActor(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = actor;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class NeverAuthority : IFormStoreAuthority
    {
        public ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken token) =>
            throw new InvalidOperationException("Missing actor must be denied first.");
    }
    private sealed class HeldStore(VersionedAtomicSettingsStore inner) : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
    {
        public bool Armed { get; set; }
        public int ReadsUntilHold { get; set; } = 1;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task WaitAsync(CancellationToken token)
        { if (!Armed) return; Armed = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        { var value = await inner.GetAsync<T>(key, token); await WaitAsync(token); return value; }
        public async Task<SettingsExportManifest> ExportAsync(CancellationToken token)
        { var value = await inner.ExportAsync(token); await WaitAsync(token); return value; }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token) =>
            inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token);
    }
    private sealed class Validator : IFormProjectPublicationValidator
    {
        public void Validate(Guid formID, JsonElement value)
        { var project = FormProjectCodec.Decode(System.Text.Encoding.UTF8.GetBytes(value.GetRawText())); Assert.Equal(formID, project.FormID); _ = new FormResponseRuntime(project, Guid.NewGuid()); }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("forms-publication-actor-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
