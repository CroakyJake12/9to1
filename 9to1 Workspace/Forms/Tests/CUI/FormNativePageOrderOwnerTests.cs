using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Forms;

namespace HavenOS.Forms.Tests;

public sealed class FormNativePageOrderOwnerTests
{
    [Fact]
    public async Task Native_page_order_actions_preserve_ids_published_version_and_reject_stale_or_revoked_owner()
    {
        using var paths = new Paths(); var settings = new VersionedAtomicSettingsStore(paths); var authority = new Authority();
        var actor = new Actor(); var publications = new FormPublicationService(settings, settings, authority, new FormNativePublicationValidator(), actors: actor);
        var authoring = new FormAuthoringService(publications);
        var project = FormProjectEditor.Create("Ordering", FormModeKind.Form, DateTimeOffset.UtcNow);
        foreach (var label in new[] { "First", "Second", "Third" })
            project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID,
                new(Guid.NewGuid(), FormFieldKind.ShortText, label, null, JsonSerializer.SerializeToElement(new { }), false, new()), DateTimeOffset.UtcNow);
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        var published = await publications.PublishAsync(project.FormID, created.Publication!.Revision);
        var originalVersion = published.Publication!.Versions.Single().Project.GetRawText();
        var surface = new FormsCuiWorkspace(publications, authoring, () => project.FormID, _ => true);
        await surface.DispatchAsync("9to1.Forms.Open", null);
        Assert.False(surface.IsActionAvailable("9to1.Forms.MoveEarlier"));
        await surface.DispatchAsync("9to1.Forms.MoveLater", null);
        var current = (await publications.ReadAsync(project.FormID)).Publication!;
        var ordered = Decode(current.Draft); Assert.Equal(project.Pages[0].PageID, ordered.Pages[0].PageID);
        Assert.Equal(new[] { project.Fields[1].FieldID, project.Fields[0].FieldID, project.Fields[2].FieldID }, ordered.Pages[0].Children.Select(child => child.ID));
        Assert.Equal(project.Fields.Select(field => field.FieldID), ordered.Fields.Select(field => field.FieldID));
        Assert.Equal(originalVersion, current.Versions.Single().Project.GetRawText());
        await surface.DispatchAsync("9to1.Forms.MoveEarlier", null);
        current = (await publications.ReadAsync(project.FormID)).Publication!;
        Assert.Equal(project.Pages[0].Children.Select(child => child.ID), Decode(current.Draft).Pages[0].Children.Select(child => child.ID));
        // A concurrent actual edit makes the retained surface revision stale.
        await authoring.UpdateFieldAsync(project.FormID, current.Revision, Decode(current.Draft).Fields[0] with { Label = "Concurrent" });
        var before = DurableExport(await settings.ExportAsync(default));
        var settingsFile = Path.Combine(paths.DataDirectory, "settings.json");
        var beforeBytes = await File.ReadAllBytesAsync(settingsFile);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.MoveLater", null));
        Assert.Equal(before, DurableExport(await settings.ExportAsync(default)));
        Assert.Equal(beforeBytes, await File.ReadAllBytesAsync(settingsFile));
        await surface.DispatchAsync("9to1.Forms.Open", null); authority.Allowed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.MoveLater", null));
        Assert.Equal(before, DurableExport(await settings.ExportAsync(default)));
        Assert.Equal(beforeBytes, await File.ReadAllBytesAsync(settingsFile));
    }
    // ExportedAt describes each observation, not a persisted mutation. Compare every durable
    // envelope field plus exact physical bytes (including the stored envelope timestamp).
    private static string DurableExport(SettingsExportManifest export) => JsonSerializer.Serialize(new
    {
        export.SchemaVersion, export.Version, export.StoreIdentity,
        Settings = export.Settings.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray()
    });
    private static FormProject Decode(JsonElement value) => FormProjectCodec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()));
    private sealed class Actor : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<AuthenticatedResourceActor?>(new("actor", "profile", null, null, "login"));
    }
    private sealed class Authority : IFormStoreCommitAuthority
    {
        public bool Allowed { get; set; } = true;
        public ValueTask<bool> AuthorizeAsync(Guid root, Guid form, long revision, string action, CancellationToken token) => ValueTask.FromResult(Allowed);
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid root, Guid form, long revision, string action, AuthenticatedResourceActor? expected, CancellationToken token)
            => ValueTask.FromResult<ISettingsCommitAdmission?>(new Admission(() => Allowed));
    }
    private sealed class Admission(Func<bool> allowed) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token) => ValueTask.FromResult(allowed());
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("forms-order-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json"); public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
