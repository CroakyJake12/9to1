using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class FormPublicationRegistrationTests
{
    [Fact]
    public async Task Explicit_graph_uses_actual_settings_identity_and_same_Home_actor_without_binding_ownership()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-forms-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var registrations = new ServiceCollection().AddHavenInfrastructure();
            Assert.DoesNotContain(registrations, entry => entry.ServiceType == typeof(FormPublicationService));
            var validator = new ControlledValidator();
            registrations.AddHavenFormsPublication(validator);
            Assert.Throws<InvalidOperationException>(() => registrations.AddHavenFormsPublication(validator));
            registrations.AddSingleton<IAppPaths>(new Paths(root));
            registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            await using var graph = registrations.BuildServiceProvider();
            var settings = Assert.IsType<VersionedAtomicSettingsStore>(graph.GetRequiredService<IVersionedSettingsStore>());
            var identity = await settings.GetStoreIdentityAsync(default);
            var unrelatedSqlIdentity = await graph.GetRequiredService<IResourceStoreIdentitySource>().GetStoreIdentityAsync(default);
            Assert.NotEqual(unrelatedSqlIdentity.StoreId, identity.StoreId);
            var authority = graph.GetRequiredService<FormLocalStoreAuthority>();
            Assert.Same(authority, graph.GetRequiredService<IFormStoreAuthority>());
            Assert.Same(authority, graph.GetRequiredService<IFormStoreCommitAuthority>());
            Assert.Same(validator, graph.GetRequiredService<IFormProjectPublicationValidator>());
            Assert.Same(graph.GetRequiredService<FormPublicationService>(), graph.GetRequiredService<FormPublicationService>());
            Assert.NotNull(graph.GetRequiredService<FormAuthoringService>());
            Assert.NotNull(graph.GetRequiredService<FormResponseSessionService>());
            var evidence = Assert.Single(graph.GetServices<IHomeLocalStoreEvidenceProvider>(), item => item is FormLocalStoreEvidenceProvider);
            Assert.NotNull(await evidence.ReadAsync(identity.StoreId.ToString("D"), default));
            Assert.Null(await evidence.ReadAsync(unrelatedSqlIdentity.StoreId.ToString("D"), default));
            var actor = await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(default);
            Assert.NotNull(actor);
            Assert.Null(await authority.CaptureCommitAdmissionAsync(identity.StoreId, Guid.NewGuid(), 0,
                "forms.create", actor, default));
            Assert.DoesNotContain((await graph.GetRequiredService<IHomeCoreStateStore>().ReadAsync()).State!.Records,
                record => record.RecordType == "home.local-store-ownership");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    // Explicit fictional validator proves DI only, never actual native renderer/publication capability.
    private sealed class ControlledValidator : IFormProjectPublicationValidator
    { public void Validate(Guid formID, JsonElement project) { } }
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
