using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;
using Haven.Infrastructure;
using HavenOS.Forms;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

/// <summary>Actual local OS/Home/settings/Forms owners. Paths are explicit test backing only.
/// No account actor, StoreID, grant, public audience or browser storage authority is fabricated.</summary>
sealed class NativeFormsFixture
{
    public required VersionedAtomicSettingsStore Durable { get; init; }
    public required SettingsReplyFault Settings { get; init; }
    public required FileHomeCoreStateStore Home { get; init; }
    public required HomeLocalProfileIdentity Actors { get; init; }
    public required HomeLocalStoreOwnership Ownership { get; init; }
    public required FormPublicationService Publications { get; init; }
    public required FormAuthoringService Authoring { get; init; }
    public required string Root { get; init; }
    public static async Task<NativeFormsFixture> CreateAsync(string root, bool bindNew = true)
    {
        Directory.CreateDirectory(root);
        var durable = new VersionedAtomicSettingsStore(new Paths(root)); var settings = new SettingsReplyFault(durable);
        var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
        var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        _ = await actors.GetCurrentAsync(default) ?? throw new UnauthorizedAccessException("Actual OS principal unavailable.");
        var evidence = new FormLocalStoreEvidenceProvider(settings, settings);
        var ownership = new HomeLocalStoreOwnership(home, actors, new HomeLocalStoreEvidenceRegistry([evidence]), new HomePermissionTrustService(home, (_, _) => null));
        var identity = await settings.GetStoreIdentityAsync(default);
        if (bindNew) await ownership.BindNewEmptyAsync("forms", identity.StoreId.ToString("D"));
        var authority = new FormLocalStoreAuthority(settings, settings, actors, new HomeResourceStoreOwnershipAuthority(ownership, actors));
        var publications = new FormPublicationService(settings, settings, authority, new FormNativePublicationValidator(), actors: actors);
        return new() { Root = root, Durable = durable, Settings = settings, Home = home, Actors = actors, Ownership = ownership, Publications = publications, Authoring = new(publications) };
    }
    public async Task<FormHostSession> OpenOwnerAsync(CancellationToken token)
    {
        var actor = await Actors.GetCurrentAsync(token) ?? throw new UnauthorizedAccessException();
        return await Publications.OpenHostSessionAsync(actor, token);
    }
    public async Task<FormPublication> PublicationAsync(Guid id)
    {
        var result = await Publications.ReadAsync(id); return result.Success ? result.Publication! : throw new InvalidOperationException(result.Code);
    }
    public static FormProject Project(FormPublication publication) => FormProjectCodec.Decode(System.Text.Encoding.UTF8.GetBytes(publication.Draft.GetRawText()));
    public async Task<bool> VerifyPrivateActorsRejectedAsync()
    {
        var actor = await Actors.GetCurrentAsync(default) ?? throw new UnauthorizedAccessException();
        var store = await Settings.GetStoreIdentityAsync(default); var id = Guid.NewGuid();
        var ownership = new HomeResourceStoreOwnershipAuthority(Ownership, Actors);
        var local = new FormLocalStoreAuthority(Settings, Settings, Actors, ownership);
        if (!await local.AuthorizeAsync(store.StoreId, id, 0, "forms.create", default)) return false;
        // Explicit negative actor inputs only: never grant a browser/private actor a local authority.
        foreach (var denied in new[] { actor with { AccountId = Guid.NewGuid() }, actor with { OrganisationId = Guid.NewGuid() } })
        {
            var authority = new FormLocalStoreAuthority(Settings, Settings, new NegativeActor(denied), ownership);
            if (await authority.AuthorizeAsync(store.StoreId, id, 0, "forms.create", default)
                || await authority.CaptureCommitAdmissionAsync(store.StoreId, id, 0, "forms.create", denied, default) is not null) return false;
        }
        return true;
    }
    private sealed class NegativeActor(AuthenticatedResourceActor input) : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<AuthenticatedResourceActor?>(input); }
    public async Task RevokeActualHomeBindingAsync()
    {
        var record = (await Home.ReadAsync()).State!.Records.Single(item => item.RecordType == "home.local-store-ownership");
        var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
        var denied = binding with { ProfileId = "explicit-negative-foreign-profile" };
        if (!(await Home.WriteAsync(record with { Revision = record.Revision + 1, Payload = JsonSerializer.SerializeToElement(denied) }, record.Revision)).IsSuccess)
            throw new Exception("Actual Home ownership revocation failed.");
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "app.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}

/// <summary>Fault boundary discards a receipt AFTER actual durable guarded compare/exchange.
/// All identity, admissions, storage and results come from the real owner.</summary>
sealed class SettingsReplyFault(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore,
    IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
{
    public bool LoseNextCommittedReply { get; set; }
    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => actual.GetStoreIdentityAsync(token);
    public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => actual.GetAsync<T>(key, token);
    public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
    public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
    public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
    public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => actual.ImportAsync(manifest, token);
    public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token)
        => actual.CompareExchangeAsync(key, expected, replacement, token);
    public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
        IReadOnlyDictionary<string, string?> guards, CancellationToken token) => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
    public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
        IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
    {
        var committed = await actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token);
        if (LoseNextCommittedReply && committed.Exchanged) { LoseNextCommittedReply = false; throw new IOException("Induced lost response after actual guarded durable Forms commit."); }
        return committed;
    }
}
