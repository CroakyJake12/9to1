using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;

namespace Haven.Infrastructure.Tests;

public sealed class FormAuthoringWorkflowTests
{
    [Fact]
    public async Task Actual_durable_authoring_restart_published_and_preview_share_canonical_fields_and_marking()
    {
        using var paths = new Paths();
        var token = CancellationToken.None;
        var store = new VersionedAtomicSettingsStore(paths);
        var authority = new Authority();
        var publications = new FormPublicationService(store, store, authority, new Validator());
        var editor = new FormAuthoringService(publications);
        var created = (await editor.CreateAsync("Hardware", FormModeKind.Test, token)).Publication!;
        var project = Decode(created.Draft);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Processor", null,
            JsonSerializer.SerializeToElement(new { }), true, new(), Assessment: new(1, 1,
                [new(Guid.NewGuid(), FormMarkingRuleKind.AcceptedText, 1, AcceptedTexts: ["CPU", "Central Processing Unit"]) ]));
        var added = await editor.AddFieldAsync(project.FormID, created.Revision, project.Pages[0].PageID, field, token);
        Assert.True(added.Success);
        var styled = await editor.SetThemeAsync(project.FormID, added.Publication!.Revision, new("accessible-dark"), token);
        Assert.True(styled.Success);
        var preview = await editor.PreviewAsync(project.FormID, styled.Publication!.Revision, token);
        var previewAnswer = preview.Answer(1, field.FieldID, JsonSerializer.SerializeToElement("CPU"));
        var previewResult = preview.Submit(previewAnswer.Response.Revision);
        Assert.Equal(1, previewResult.Response.AwardedPoints);
        var published = (await publications.PublishAsync(project.FormID, styled.Publication.Revision, token)).Publication!;
        var version = Assert.Single(published.Versions);
        var updated = await editor.UpdateFieldAsync(project.FormID, published.Revision, field with { Label = "Edited draft label" }, token);
        Assert.True(updated.Success);
        var restartedStore = new VersionedAtomicSettingsStore(paths);
        var restarted = new FormPublicationService(restartedStore, restartedStore, authority, new Validator());
        var reopened = (await restarted.ReadAsync(project.FormID, token)).Publication!;
        Assert.Equal("Edited draft label", Assert.Single(Decode(reopened.Draft).Fields).Label);
        var pinned = Decode(Assert.Single(reopened.Versions).Project);
        Assert.Equal("Processor", Assert.Single(pinned.Fields).Label);
        Assert.Equal(project.Pages[0].PageID, Assert.Single(pinned.Pages).PageID);
        Assert.Equal("accessible-dark", pinned.Theme.ThemeID);
        var runtime = new FormResponseRuntime(pinned, version.FormVersionID);
        var answer = runtime.Answer(1, field.FieldID, JsonSerializer.SerializeToElement("Central Processing Unit"));
        var completed = runtime.Submit(answer.Response.Revision).Response;
        Assert.Equal(previewResult.Response.AwardedPoints, completed.AwardedPoints);
        var persistedResponse = FormResponseSubmissionProjection.Create(pinned, completed);
        var responses = new DataWorkbookFormsSubmissionStore(new DataWorkbookRepository(paths));
        await responses.SaveAsync(persistedResponse, token);
        await responses.SaveAsync(persistedResponse, token); // Same response retry is idempotent.
        var reopenedResponses = await new DataWorkbookFormsSubmissionStore(new DataWorkbookRepository(paths)).GetLatestAsync(token);
        var retained = Assert.Single(reopenedResponses);
        Assert.Equal(completed.ResponseID.ToString("D"), retained.Id);
        Assert.Equal(version.FormVersionID.ToString("D"), retained.FormVersionId);
        Assert.Equal("Central Processing Unit", retained.Answers[field.FieldID.ToString("D")].Value.GetString());
        Assert.Equal(FormsDataWriteStatus.NotBound, retained.DataWriteStatus);
        Assert.Throws<InvalidDataException>(() => FormResponseSubmissionProjection.Create(Decode(reopened.Draft), completed));
        Assert.Equal("RevisionConflict", (await editor.SetThemeAsync(project.FormID, published.Revision, new("stale"), token)).Code);
        authority.Allowed = false;
        Assert.Equal("PermissionDenied", (await editor.SetThemeAsync(project.FormID, reopened.Revision, new("denied"), token)).Code);
        authority.Allowed = true;
        Assert.Equal("accessible-dark", Decode((await restarted.ReadAsync(project.FormID, token)).Publication!.Draft).Theme.ThemeID);
    }

    private static FormProject Decode(JsonElement element) => FormProjectCodec.Decode(Encoding.UTF8.GetBytes(element.GetRawText()));
    private sealed class Validator : IFormProjectPublicationValidator
    {
        public void Validate(Guid id, JsonElement element)
        {
            var project = Decode(element);
            Assert.Equal(id, project.FormID);
            Assert.Empty(project.DataBindings); // This fixture never claims a live Data binding or graph provider.
            _ = new FormResponseRuntime(project, Guid.NewGuid());
        }
    }
    private sealed class Authority : IFormStoreCommitAuthority
    {
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ISettingsCommitAdmission?>(new FixtureAdmission(() => Allowed));

        public bool Allowed { get; set; } = true;
        public ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Allowed && storeID != Guid.Empty && formID != Guid.Empty);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("forms-authoring-").FullName;
        public string DataDirectory => _root;
        public string DatabasePath => Path.Combine(_root, "data.db");
        public string BrowserProfileDirectory => Path.Combine(_root, "browser");
        public string AttachmentsDirectory => Path.Combine(_root, "attachments");
        public string LogsDirectory => Path.Combine(_root, "logs");
        public string LegacyStatePath => Path.Combine(_root, "legacy.json");
        public void Dispose() => Directory.Delete(_root, true);
    }
    private sealed class FixtureAdmission(Func<bool> allowed) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) => ValueTask.FromResult(allowed());
    }

}
