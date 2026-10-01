using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Infrastructure.Tests;

public sealed class HomeDataConversationRegistrationTests
{
    [Fact]
    public async Task Actual_graph_resolves_single_owning_stores_and_guards_without_implicitly_binding_existing_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-data-conversation-di-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var paths = new Paths(root);
            var registrations = new ServiceCollection().AddHavenInfrastructure();
            registrations.AddSingleton<IAppPaths>(paths);
            registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            await using var graph = registrations.BuildServiceProvider();
            Assert.Same(graph.GetRequiredService<ConversationRepository>(), graph.GetRequiredService<IConversationSpaceCommitStore>());
            Assert.IsType<DataWorkbookRepository>(graph.GetRequiredService<IDataWorkbookRepository>());
            Assert.IsType<DataLocalStoreAuthority>(graph.GetRequiredService<IDataWorkbookCommitAuthority>());
            Assert.Single(graph.GetServices<IHomeLocalStoreEvidenceProvider>(), provider => provider is DataLocalStoreEvidenceProvider);
            Assert.Single(graph.GetServices<IHomeLocalStoreEvidenceProvider>(), provider => provider is ConversationLocalStoreEvidenceProvider);
            Assert.Single(graph.GetServices<ICanonicalResourceAccessResolver>(), resolver => resolver is DataWorkbookMutationAccessResolver);
            Assert.NotNull(graph.GetRequiredService<DataHomeRecordUpdateOperation>());
            var schemaDesigner = Assert.IsType<DataHomeTableSchemaDesigner>(graph.GetRequiredService<IDataTableSchemaDesigner>());
            Assert.Same(schemaDesigner, graph.GetRequiredService<IDataTableSchemaDesigner>());
            Assert.Single(graph.GetServices<IDataTableSchemaDesigner>());
            Assert.Same(graph.GetRequiredService<DataHomeTableSchemaUpdateOperation>(), graph.GetRequiredService<DataHomeTableSchemaUpdateOperation>());
            Assert.Same(graph.GetRequiredService<DataSchemaMutationRecovery>(), graph.GetRequiredService<DataSchemaMutationRecovery>());
            Assert.Same(graph.GetRequiredService<DataRecordMutationRecovery>(), graph.GetRequiredService<IDataRecordMutationReceiptSource>());
            Assert.NotNull(graph.GetRequiredService<ConversationLocalStoreAuthority>());
            Assert.Null(await graph.GetRequiredService<HomePersonalModelRoutes>().GetAsync(Dulche.Runtime.ModelCapabilityCategory.Chat));
            var actual = Assert.IsAssignableFrom<IDataWorkbookStoreIdentitySource>(graph.GetRequiredService<IDataWorkbookRepository>());
            var identity = await actual.GetStoreIdentityAsync(default);
            var actor = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(default))!;
            Assert.Null(await graph.GetRequiredService<IDataWorkbookCommitAuthority>().CaptureAsync(identity.StoreId,
                Guid.NewGuid(), 0, Guid.Empty, "data.workbook.create", actor, default));
            Assert.DoesNotContain((await graph.GetRequiredService<IHomeCoreStateStore>().ReadAsync()).State!.Records,
                record => record.RecordType == "home.local-store-ownership");
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
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
