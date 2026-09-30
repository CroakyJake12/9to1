using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Tests;

public sealed class FormLocalStoreAuthorityTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Actual_settings_lease_admission_rejects_changed_Home_binding_or_actor(bool authoring, int change)
    {
        using var paths = new Paths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token;
        var home = new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var actor = new MutableActor((await profiles.GetCurrentAsync(token))!);
        var actual = new VersionedAtomicSettingsStore(paths);
        using var settings = new LeaseBarrierStore(actual, Path.Combine(paths.DataDirectory, "settings.json.lock"));
        var evidence = new FormLocalStoreEvidenceProvider(settings, actual);
        var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([evidence]),
            new HomePermissionTrustService(home, (_, _) => null));
        var authority = new FormLocalStoreAuthority(settings, actual, actor, new HomeResourceStoreOwnershipAuthority(ownership, actor));
        var publications = new FormPublicationService(settings, actual, authority, new Validator());
        var identity = await actual.GetStoreIdentityAsync(token);
        await ownership.BindNewEmptyAsync("forms", identity.StoreId.ToString("D"));
        var project = FormProjectEditor.Create("Lease race", FormModeKind.Form, DateTimeOffset.UtcNow);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Answer", null,
            JsonSerializer.SerializeToElement(new { }), false, new());
        project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, DateTimeOffset.UtcNow);
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token);
        Assert.True(created.Success);
        var published = await publications.PublishAsync(project.FormID, created.Publication!.Revision, token);
        Assert.True(published.Success);
        var sessions = new FormResponseSessionService(publications, settings, actual, authority, actor);
        var started = await sessions.StartAsync(project.FormID, published.Publication!.Revision, token);
        Assert.True(started.Success);
        var before = await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "settings.json"), token);
        settings.Armed = true;
        Task<string?> Pending() => authoring ? Edit() : Answer();
        async Task<string?> Edit() => (await new FormAuthoringService(publications).SetThemeAsync(project.FormID,
            published.Publication.Revision, new("rejected"), token)).Code;
        async Task<string?> Answer() => (await sessions.AnswerAsync(project.FormID, started.Response!.ResponseID,
            started.Response.Revision, field.FieldID, JsonSerializer.SerializeToElement("rejected"), token)).Code;
        var pending = Pending();
        await settings.Waiting.Task.WaitAsync(token);
        Assert.False(pending.IsCompleted);
        if (change == 2)
        {
            var record = Assert.Single((await home.ReadAsync()).State!.Records, item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked-profile" }) }, record.Revision)).IsSuccess);
        }
        else actor.Current = change == 0 ? actor.Current with { AuthenticationRevision = "changed-while-waiting" }
            : actor.Current with { ActorId = "other-principal" };
        settings.Release();
        Assert.Equal("PermissionDenied", await pending);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(paths.DataDirectory, "settings.json"), token));
        var retained = await actual.GetAsync<FormPublication>("forms.publication.v1." + project.FormID.ToString("N"), token);
        Assert.Equal(published.Publication.Revision, retained!.Revision);
    }

    private sealed class MutableActor(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = actor;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class LeaseBarrierStore(VersionedAtomicSettingsStore inner, string leasePath) : IVersionedSettingsStore,
        IVersionedSettingsGuardedCompareExchange, IDisposable
    {
        private FileStream? _lease;
        public bool Armed { get; set; }
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson, string? replacementJson,
            IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            if (!Armed) return inner.CompareExchangeGuardedAsync(key, expectedJson, replacementJson, guards, admission, token);
            Armed = false;
            _lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var pending = inner.CompareExchangeGuardedAsync(key, expectedJson, replacementJson, guards, admission, token);
            Assert.False(pending.IsCompleted); // The real store is waiting for its durable lease, after final caller validation.
            Waiting.TrySetResult();
            return pending;
        }
        public void Release() { _lease?.Dispose(); _lease = null; }
        public void Dispose() => Release();
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson, string? replacementJson,
            IReadOnlyDictionary<string, string?> guards, CancellationToken token) => inner.CompareExchangeGuardedAsync(key, expectedJson, replacementJson, guards, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token) => inner.CompareExchangeAsync(key, expectedJson, replacementJson, token);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => inner.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
    }

    [Fact]
    public async Task Actual_Home_owned_Forms_store_reopens_and_foreign_profile_revocation_denies_authoring_and_response_resume()
    {
        using var paths = new Paths();
        var home = new FileHomeCoreStateStore(Path.Combine(paths.DataDirectory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var settings = new VersionedAtomicSettingsStore(paths);
        var evidence = new FormLocalStoreEvidenceProvider(settings, settings);
        var ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([evidence]),
            new HomePermissionTrustService(home, (_, _) => null));
        var authority = new FormLocalStoreAuthority(settings, settings, profiles, new HomeResourceStoreOwnershipAuthority(ownership, profiles));
        var publications = new FormPublicationService(settings, settings, authority, new Validator());
        var project = FormProjectEditor.Create("Owned Forms", FormModeKind.Form, DateTimeOffset.UtcNow);
        Assert.Equal("PermissionDenied", (await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project))).Code);
        var identity = await settings.GetStoreIdentityAsync(CancellationToken.None);
        Assert.True((await evidence.ReadAsync(identity.StoreId.ToString("D"), CancellationToken.None))!.IsEmpty);
        await ownership.BindNewEmptyAsync("forms", identity.StoreId.ToString("D"));
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        Assert.True(created.Success);
        var published = await publications.PublishAsync(project.FormID, created.Publication!.Revision);
        Assert.True(published.Success);
        var actualActor = (await profiles.GetCurrentAsync(CancellationToken.None))!;
        foreach (var invalidActor in new[] { actualActor with { ActorId = "" }, actualActor with { AuthenticationRevision = "" },
            actualActor with { AccountId = Guid.NewGuid() } })
            Assert.False(await new FormLocalStoreAuthority(settings, settings, new FixedActor(invalidActor),
                new HomeResourceStoreOwnershipAuthority(ownership, profiles)).AuthorizeAsync(identity.StoreId,
                project.FormID, published.Publication!.Revision, "forms.read", CancellationToken.None));
        var sessions = new FormResponseSessionService(publications, settings, settings, authority, profiles);
        var started = await sessions.StartAsync(project.FormID, published.Publication!.Revision);
        Assert.True(started.Success);

        var reopenedSettings = new VersionedAtomicSettingsStore(paths);
        var reopenedProfiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var reopenedEvidence = new FormLocalStoreEvidenceProvider(reopenedSettings, reopenedSettings);
        var reopenedOwnership = new HomeLocalStoreOwnership(home, reopenedProfiles, new HomeLocalStoreEvidenceRegistry([reopenedEvidence]),
            new HomePermissionTrustService(home, (_, _) => null));
        var reopenedAuthority = new FormLocalStoreAuthority(reopenedSettings, reopenedSettings, reopenedProfiles,
            new HomeResourceStoreOwnershipAuthority(reopenedOwnership, reopenedProfiles));
        var reopenedPublications = new FormPublicationService(reopenedSettings, reopenedSettings, reopenedAuthority, new Validator());
        var resumed = new FormResponseSessionService(reopenedPublications, reopenedSettings, reopenedSettings, reopenedAuthority, reopenedProfiles);
        Assert.Equal(started.Response!.ResponseID, (await resumed.ResumeAsync(project.FormID, started.Response.ResponseID)).Response!.ResponseID);
        Assert.False(await reopenedAuthority.AuthorizeAsync(Guid.NewGuid(), project.FormID, published.Publication.Revision, "forms.read", CancellationToken.None));
        Assert.False(await reopenedAuthority.AuthorizeAsync(identity.StoreId, project.FormID, 1, "forms.read", CancellationToken.None));
        Assert.False(await reopenedAuthority.AuthorizeAsync(identity.StoreId, project.FormID, published.Publication.Revision, "forms.unknown", CancellationToken.None));
        var snapshot = (await home.ReadAsync()).State!;
        var bindingRecord = Assert.Single(snapshot.Records, record => record.RecordType == "home.local-store-ownership");
        var binding = bindingRecord.Payload.Deserialize<HomeLocalStoreBinding>()!;
        Assert.True((await home.WriteAsync(bindingRecord with { Revision = bindingRecord.Revision + 1,
            Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "foreign-profile" }) }, bindingRecord.Revision)).IsSuccess);
        Assert.Equal("PermissionDenied", (await resumed.ResumeAsync(project.FormID, started.Response.ResponseID)).Code);
        Assert.Equal("PermissionDenied", (await new FormAuthoringService(reopenedPublications).SetThemeAsync(
            project.FormID, published.Publication.Revision, new("changed"))).Code);
        var retained = await reopenedSettings.GetAsync<FormPublication>("forms.publication.v1." + project.FormID.ToString("N"), CancellationToken.None);
        Assert.Equal(published.Publication.Revision, retained!.Revision);
        Assert.Equal(project.Title, retained.Draft.GetProperty("Title").GetString());
    }
    private sealed class Validator : IFormProjectPublicationValidator
    {
        public void Validate(Guid formID, JsonElement canonicalProject)
        {
            var project = FormProjectCodec.Decode(System.Text.Encoding.UTF8.GetBytes(canonicalProject.GetRawText()));
            Assert.Equal(formID, project.FormID); _ = new FormResponseRuntime(project, Guid.NewGuid());
        }
    }
    private sealed class FixedActor(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(actor);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("forms-home-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
