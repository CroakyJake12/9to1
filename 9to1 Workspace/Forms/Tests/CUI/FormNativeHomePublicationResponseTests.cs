using System.Text.Json;
using Avalonia.Headless;
using Haven.Application;
using Haven.Core.Forms;
using Haven.Infrastructure;
using HavenOS.Forms;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Forms.Tests;

[Collection("Forms native renderer")]
public sealed class FormNativeHomePublicationResponseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_Home_owned_native_publication_recovers_failed_mount_and_original_session_denies_revocation_or_root_replacement(bool replaceRoot)
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(FormNativePreviewTests.PreviewApplication));
        await native.Dispatch<bool>(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-forms-home-native-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var registrations = new ServiceCollection().AddHavenInfrastructure();
                registrations.AddSingleton<IAppPaths>(new Paths(root));
                registrations.AddSingleton<IHomeCoreStateStore>(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
                registrations.AddHavenFormsPublication(new FormNativePublicationValidator());
                await using var graph = registrations.BuildServiceProvider();
                var settings = Assert.IsType<VersionedAtomicSettingsStore>(graph.GetRequiredService<IVersionedSettingsStore>());
                var identity = await settings.GetStoreIdentityAsync(default);
                var home = graph.GetRequiredService<IHomeCoreStateStore>();
                var ownership = graph.GetRequiredService<HomeLocalStoreOwnership>();
                var publications = graph.GetRequiredService<FormPublicationService>();
                var origin = (await graph.GetRequiredService<IAuthenticatedResourceActorSource>().GetCurrentAsync(default))
                    ?? throw new InvalidOperationException("Actual native Home actor required.");
                var session = await publications.OpenHostSessionAsync(origin);
                var sessions = session.Responses;
                var project = FormProjectEditor.Create("Actual native owner", FormModeKind.Form, DateTimeOffset.UtcNow);
                project = project with { RuntimeSettings = project.RuntimeSettings with { MaximumAttempts = 1 } };
                Assert.Equal("PermissionDenied", (await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project))).Code);
                await ownership.BindNewEmptyAsync("forms", identity.StoreId.ToString("D"));
                var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
                Assert.True(created.Success);
                var published = await publications.PublishAsync(project.FormID, created.Publication!.Revision);
                Assert.True(published.Success);
                var frozenSchema = Assert.Single(published.Publication!.Versions).Project.GetRawText();
                var mounted = new List<Guid>(); var fail = true;
                var workspace = new FormsCuiWorkspace(session.Publications, session.Authoring,
                    () => project.FormID, _ => true, responseSessions: sessions, showResponse: (surface, _) =>
                    {
                        mounted.Add(surface.Response!.ResponseID);
                        if (fail) { fail = false; throw new IOException("Actual host mount unavailable"); }
                        Assert.Equal(project.FormID, surface.Response.FormID);
                        return Task.CompletedTask;
                    });
                await workspace.DispatchAsync("9to1.Forms.Open", null);
                await Assert.ThrowsAsync<IOException>(async () => await workspace.DispatchAsync("9to1.Forms.Respond", null));
                await workspace.DispatchAsync("9to1.Forms.Respond", null);
                Assert.Equal(mounted[0], mounted[1]);
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await workspace.DispatchAsync("9to1.Forms.NewResponse", null));
                Assert.Equal(2, mounted.Count);
                var reopenedSettings = new VersionedAtomicSettingsStore(new Paths(root));
                var actor = graph.GetRequiredService<IAuthenticatedResourceActorSource>();
                var authority = new FormLocalStoreAuthority(reopenedSettings, reopenedSettings, actor,
                    new HomeResourceStoreOwnershipAuthority(ownership, actor));
                var reopenedPublications = new FormPublicationService(reopenedSettings, reopenedSettings, authority,
                    new FormNativePublicationValidator(), actors: actor);
                var reopenedSessions = new FormResponseSessionService(reopenedPublications, reopenedSettings, reopenedSettings, authority, actor);
                var recovered = await reopenedSessions.ResumeAsync(project.FormID, mounted[0]);
                Assert.True(recovered.Success); Assert.Equal(mounted[0], recovered.Response!.ResponseID);
                var durable = await reopenedPublications.ReadAsync(project.FormID);
                Assert.Equal(frozenSchema, Assert.Single(durable.Publication!.Versions).Project.GetRawText());
                if (replaceRoot)
                {
                    var file = Path.Combine(root, "settings.json");
                    var text = await File.ReadAllTextAsync(file);
                    var replacementID = Guid.NewGuid();
                    Assert.Contains(identity.StoreId.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
                    text = text.Replace(identity.StoreId.ToString("D"), replacementID.ToString("D"), StringComparison.OrdinalIgnoreCase);
                    await File.WriteAllTextAsync(file, text);
                    var beforeReplacement = await File.ReadAllBytesAsync(file);
                    var failure = await Record.ExceptionAsync(() => session.RequireCurrentAsync());
                    Assert.True(failure is UnauthorizedAccessException or InvalidOperationException);
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.RequireCurrentAsync());
                    await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await workspace.DispatchAsync("9to1.Forms.Respond", null));
                    Assert.Equal(2, mounted.Count);
                    Assert.Equal(beforeReplacement, await File.ReadAllBytesAsync(file));
                    return true;
                }
                var bindingRecord = Assert.Single((await home.ReadAsync()).State!.Records, item => item.RecordType == "home.local-store-ownership");
                var binding = bindingRecord.Payload.Deserialize<HomeLocalStoreBinding>()!;
                Assert.True((await home.WriteAsync(bindingRecord with { Revision = bindingRecord.Revision + 1,
                    Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "foreign-profile" }) }, bindingRecord.Revision)).IsSuccess);
                var before = DurableExport(await settings.ExportAsync(default));
                var settingsFile = Path.Combine(root, "settings.json");
                var beforeBytes = await File.ReadAllBytesAsync(settingsFile);
                Assert.Equal("PermissionDenied", (await reopenedSessions.ResumeAsync(project.FormID, mounted[0])).Code);
                Assert.Equal(before, DurableExport(await settings.ExportAsync(default)));
                Assert.Equal(beforeBytes, await File.ReadAllBytesAsync(settingsFile));
            }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
            return true;
        }, default);
    }
    private static string DurableExport(SettingsExportManifest manifest) => JsonSerializer.Serialize(new
    {
        manifest.SchemaVersion, manifest.Version, manifest.StoreIdentity,
        Settings = manifest.Settings.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray()
    });
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
