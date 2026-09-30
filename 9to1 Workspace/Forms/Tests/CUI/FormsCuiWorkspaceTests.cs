using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Forms;

namespace HavenOS.Forms.Tests;

public sealed class FormsCuiWorkspaceTests
{
    [Fact]
    public async Task Actual_native_publication_capabilities_preserve_advanced_draft_and_prior_active_version()
    {
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var authority = new Authority();
        var publications = new FormPublicationService(settings, settings, authority, new FormNativePublicationValidator());
        var project = FormProjectEditor.Create("Publication capabilities", FormModeKind.Form, DateTimeOffset.UtcNow);
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        Assert.True(created.Success);
        var published = await publications.PublishAsync(project.FormID, created.Publication!.Revision);
        Assert.True(published.Success);
        var advanced = project with { Revision = project.Revision + 1, ModifiedAt = project.ModifiedAt.AddSeconds(1),
            AccessPolicy = new(FormRespondentAccess.Public, []) };
        var edited = await publications.SaveDraftAsync(project.FormID, published.Publication!.Revision, FormProjectEditor.Project(advanced));
        Assert.True(edited.Success); // Authoring an advanced policy never silently discards it.
        var denied = await publications.PublishAsync(project.FormID, edited.Publication!.Revision);
        Assert.False(denied.Success);
        Assert.Equal("CapabilityUnavailable", denied.Code);
        var retained = (await publications.ReadAsync(project.FormID)).Publication!;
        Assert.Equal(edited.Publication.Revision, retained.Revision);
        Assert.Equal(published.Publication.ActiveVersionID, retained.ActiveVersionID);
        Assert.Equal(FormRespondentAccess.Public, Decode(retained.Draft).AccessPolicy.Respondents);
        Assert.Equal(FormRespondentAccess.OwnerOnly, Decode(Assert.Single(retained.Versions).Project).AccessPolicy.Respondents);
    }

    [Fact]
    public async Task Owning_builder_saves_and_moves_canonical_field_then_reopens_immutable_publication_and_preserves_dirty_edit_on_conflict()
    {
        Assert.NotNull(FormsCuiWorkspace.LoadDocument());
        using var paths = new Paths();
        var store = new VersionedAtomicSettingsStore(paths);
        var authority = new Authority();
        var publications = new FormPublicationService(store, store, authority, new FormNativePublicationValidator());
        var authoring = new FormAuthoringService(publications);
        Guid? selected = null;
        var allowed = true;
        FormNativePreview? seenPreview = null;
        var surface = new FormsCuiWorkspace(publications, authoring, () => selected, _ => allowed,
            (preview, token) => { seenPreview = preview; Assert.Equal(selected, preview.Response.FormID); return Task.CompletedTask; });
        Assert.True(surface.TrySetValue("Title", "Canonical survey"));
        await surface.DispatchAsync("9to1.Forms.Create", null);
        selected = surface.FormID;
        await surface.DispatchAsync("9to1.Forms.AddText", null);
        Assert.True(surface.TrySetValue("Label", "Your name"));
        await surface.DispatchAsync("9to1.Forms.SaveField", null);
        await surface.DispatchAsync("9to1.Forms.ToggleRequired", null);
        var initial = (await publications.ReadAsync(selected!.Value)).Publication!;
        var field = Assert.Single(Decode(initial.Draft).Fields);
        Assert.True(field.Required);
        await surface.DispatchAsync("9to1.Forms.AddPage", null);
        Assert.True(surface.TrySetValue("SelectedPageIndex", 0));
        Assert.True(surface.TrySetValue("SelectedPageIndex", 1));
        Assert.False(surface.TrySetValue("SelectedPageIndex", -1));
        Assert.False(surface.TrySetValue("SelectedFieldIndex", 999));
        await surface.DispatchAsync("9to1.Forms.MoveToPage", null);
        await surface.DispatchAsync("9to1.Forms.Preview", null);
        Assert.NotNull(seenPreview);
        Assert.Throws<ObjectDisposedException>(() => seenPreview.Submit());
        await surface.DispatchAsync("9to1.Forms.Publish", null);
        var published = (await publications.ReadAsync(selected.Value)).Publication!;
        var project = Decode(published.Draft);
        Assert.Empty(project.Pages[0].Children);
        Assert.Equal(field.FieldID, Assert.Single(project.Pages[1].Children).ID);
        Assert.Equal(field.FieldID, Assert.Single(project.Fields).FieldID);
        var version = Assert.Single(published.Versions);
        var reopenedStore = new VersionedAtomicSettingsStore(paths);
        var reopenedPublications = new FormPublicationService(reopenedStore, reopenedStore, authority, new FormNativePublicationValidator());
        var reopened = new FormsCuiWorkspace(reopenedPublications, new(reopenedPublications), () => selected, _ => true);
        await reopened.DispatchAsync("9to1.Forms.Open", null);
        Assert.True(reopened.TrySetValue("Label", "New draft label"));
        await reopened.DispatchAsync("9to1.Forms.SaveField", null);
        Assert.True(surface.TrySetValue("Label", "Unsaved local edit"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.SaveField", null));
        Assert.True(surface.TryGetValue("Label", out var dirty));
        Assert.Equal("Unsaved local edit", dirty);
        var retained = (await reopenedPublications.ReadAsync(selected.Value)).Publication!;
        Assert.Equal("New draft label", Assert.Single(Decode(retained.Draft).Fields).Label);
        Assert.Equal("Your name", Assert.Single(Decode(Assert.Single(retained.Versions).Project).Fields).Label);
        Assert.Equal(version.FormVersionID, retained.ActiveVersionID);
        allowed = false;
        Assert.False(surface.TrySetValue("SelectedFieldIndex", 0));
        Assert.False(surface.TrySetValue("Label", "Denied local mutation"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Forms.Publish", null));
        authority.Allowed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reopened.DispatchAsync("9to1.Forms.Close", null));
        authority.Allowed = true;
        Assert.Equal(retained.Revision, (await reopenedPublications.ReadAsync(selected.Value)).Publication!.Revision);
    }

    private static FormProject Decode(JsonElement value) => FormProjectCodec.Decode(Encoding.UTF8.GetBytes(value.GetRawText()));
    private sealed class Authority : IFormStoreCommitAuthority
    {
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ISettingsCommitAdmission?>(new FixtureAdmission(() => Allowed));

        public bool Allowed { get; set; } = true;
        public ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken cancellationToken) => ValueTask.FromResult(Allowed);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("forms-cui-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
    private sealed class FixtureAdmission(Func<bool> allowed) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) => ValueTask.FromResult(allowed());
    }

}
