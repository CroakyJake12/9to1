using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Forms;

namespace HavenOS.Forms.Tests;

[Collection("Forms native renderer")]
public sealed class FormNativeResponseSurfaceTests
{
    [Fact]
    public async Task Published_workspace_retry_opens_same_durable_response_and_real_native_answers_save()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(FormNativePreviewTests.PreviewApplication));
        await native.Dispatch(async () =>
        {
            using var fixture = new Fixture(); var (project, _) = await fixture.Create(maximumAttempts: 2);
            var IDs = new List<Guid>(); var failMount = true;
            var workspace = new FormsCuiWorkspace(fixture.Publications, new FormAuthoringService(fixture.Publications),
                () => project.FormID, _ => true, responseSessions: fixture.Sessions, showResponse: async (surface, token) =>
                {
                    IDs.Add(surface.Response!.ResponseID);
                    if (failMount) { failMount = false; throw new IOException("Actual mount unavailable"); }
                    var registry = new CuiControlRegistry(); surface.Register(registry);
                    var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("joined-response", "Response", "forms",
                        surface.CreateDocument(), surface, surface, new Ready()) { ControlRegistry = registry });
                    using var host = Assert.IsType<CuiSceneHost>(window.Content); window.Show();
                    try
                    {
                        Assert.Single(host.GetVisualDescendants().OfType<TextBox>(), input => Avalonia.Automation.AutomationProperties.GetName(input) == "Name").Text = "Ada";
                        await surface.DispatchAsync("Save", null, token);
                        Assert.Equal("Ada", Assert.Single(surface.Response!.Answers).Value.GetString());
                    }
                    finally { window.Close(); }
                });
            await workspace.DispatchAsync("9to1.Forms.Open", null);
            Assert.True(workspace.IsActionAvailable("9to1.Forms.Respond"));
            await Assert.ThrowsAsync<IOException>(async () => await workspace.DispatchAsync("9to1.Forms.Respond", null));
            await workspace.DispatchAsync("9to1.Forms.Respond", null);
            Assert.Equal(2, IDs.Count); Assert.Equal(IDs[0], IDs[1]);
            var durable = await fixture.Sessions.ResumeAsync(project.FormID, IDs[0]);
            Assert.True(durable.Success); Assert.Equal("Ada", Assert.Single(durable.Response!.Answers).Value.GetString());
            // The actual runtime limit denies another attempt; retaining the existing response
            // must not be mistaken for a fresh successful Start or drop its saved answers.
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await workspace.DispatchAsync("9to1.Forms.NewResponse", null));
            Assert.Equal(2, IDs.Count);
            await workspace.DispatchAsync("9to1.Forms.Respond", null);
            Assert.Equal(IDs[0], IDs[2]);
            await workspace.DispatchAsync("9to1.Forms.Close", null);
            Assert.False(workspace.IsActionAvailable("9to1.Forms.Respond"));
            return true;
        }, default);
    }

    [Fact]
    public async Task Native_answers_save_through_durable_owner_and_invalid_visible_draft_blocks_submission_after_partial_save()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(FormNativePreviewTests.PreviewApplication));
        await native.Dispatch(async () =>
        {
            using var fixture = new Fixture();
            var (project, response) = await fixture.Create();
            var opened = await FormNativeResponseSurface.OpenAsync(fixture.Sessions, project.FormID, response.ResponseID);
            Assert.True(opened.Success);
            using var surface = opened.Surface!;
            var registry = new CuiControlRegistry(); surface.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-response", "Response", "forms",
                surface.CreateDocument(), surface, surface, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content); window.Show();
            T Named<T>(string name) where T : Control => Assert.Single(host.GetVisualDescendants().OfType<T>(),
                control => Avalonia.Automation.AutomationProperties.GetName(control) == name);
            Named<TextBox>("Name").Text = "Ada";
            var number = Named<NumericUpDown>("Amount"); number.Value = 3;
            Assert.Single(number.GetVisualDescendants().OfType<TextBox>()).Text = "invalid";
            Assert.Equal(2, surface.UnsavedAnswerCount);
            await surface.DispatchAsync("Submit", null);
            Assert.Equal(FormResponseState.InProgress, surface.Response!.State);
            Assert.Equal(1, surface.UnsavedAnswerCount);
            var durable = await fixture.Sessions.ResumeAsync(project.FormID, response.ResponseID);
            Assert.Equal("Ada", Assert.Single(durable.Response!.Answers).Value.GetString());
            number = Named<NumericUpDown>("Amount");
            Assert.Equal("invalid", Assert.Single(number.GetVisualDescendants().OfType<TextBox>()).Text);
            Assert.Single(number.GetVisualDescendants().OfType<TextBox>()).Text = "4";
            await surface.DispatchAsync("Submit", null);
            Assert.Equal(FormResponseState.Submitted, surface.Response!.State);
            Assert.Equal(0, surface.UnsavedAnswerCount);
            var reopened = fixture.Reopen();
            var saved = await reopened.ReadSessionAsync(project.FormID, response.ResponseID);
            Assert.Equal(FormResponseState.Submitted, saved.Response!.State);
            Assert.Equal(4, saved.Response.Answers.Single(answer => answer.FieldID == project.Fields[1].FieldID).Value.GetDecimal());
            window.Close();
            Assert.Null(surface.Response);
            number.Value = 99;
            Assert.False(surface.IsActionAvailable("Save"));
            Assert.Equal(saved.Response.Revision, (await reopened.ResumeAsync(project.FormID, response.ResponseID)).Response!.Revision);
            return true;
        }, default);
    }

    [Fact]
    public async Task Native_conflict_retains_draft_until_explicit_reload_and_authentication_change_clears_surface_without_write()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(FormNativePreviewTests.PreviewApplication));
        await native.Dispatch(async () =>
        {
            using var fixture = new Fixture();
            var (project, response) = await fixture.Create();
            using var surface = (await FormNativeResponseSurface.OpenAsync(fixture.Sessions, project.FormID, response.ResponseID)).Surface!;
            var registry = new CuiControlRegistry(); surface.Register(registry);
            var window = await CuiSceneHost.CreateWindowAsync(new CuiNativeScene("forms-response-conflict", "Response", "forms",
                surface.CreateDocument(), surface, surface, new Ready()) { ControlRegistry = registry });
            using var host = Assert.IsType<CuiSceneHost>(window.Content); window.Show();
            TextBox Input() => Assert.Single(host.GetVisualDescendants().OfType<TextBox>(),
                control => Avalonia.Automation.AutomationProperties.GetName(control) == "Name");
            Input().Text = "Local draft";
            var concurrent = await fixture.Reopen().AnswerAsync(project.FormID, response.ResponseID, response.Revision,
                project.Fields[0].FieldID, JsonSerializer.SerializeToElement("Remote saved"));
            Assert.True(concurrent.Success);
            await surface.DispatchAsync("Save", null);
            Assert.Equal("RevisionConflict", surface.StatusCode);
            Assert.Equal("Local draft", Input().Text);
            Assert.False(Input().IsEnabled);
            await surface.DispatchAsync("Reload", null);
            Assert.Equal("Remote saved", Input().Text);
            Assert.Equal(0, surface.UnsavedAnswerCount);
            fixture.Actor.Current = fixture.Actor.Current with { AuthenticationRevision = "other-login" };
            Input().Text = "Must not persist";
            await surface.DispatchAsync("Save", null);
            Assert.Null(surface.Response);
            Assert.Empty(host.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal(concurrent.Response!.Revision,
                (await fixture.Reopen().ResumeAsync(project.FormID, response.ResponseID)).Response!.Revision);
            window.Close();
            return true;
        }, default);
    }

    [Fact]
    public async Task Definition_and_scope_keep_published_schema_and_reject_old_authentication_before_response_operation()
    {
        using var fixture = new Fixture();
        var (project, response) = await fixture.Create(marked: true);
        var loaded = await fixture.Sessions.ReadSessionAsync(project.FormID, response.ResponseID);
        Assert.All(loaded.Presentation!.Fields, field => Assert.Null(field.Assessment));
        Assert.DoesNotContain("hidden expected answer", JsonSerializer.Serialize(loaded.Presentation), StringComparison.Ordinal);
        var publication = (await fixture.Publications.ReadAsync(project.FormID)).Publication!;
        var changed = project with { Revision = project.Revision + 1,
            Fields = project.Fields.Select(field => field with { Label = "Changed draft" }).ToArray() };
        var draft = await fixture.Publications.SaveDraftAsync(project.FormID, publication.Revision, FormProjectEditor.Project(changed));
        Assert.True(draft.Success);
        Assert.True((await fixture.Publications.PublishAsync(project.FormID, draft.Publication!.Revision)).Success);
        var retained = await fixture.Sessions.ReadSessionAsync(project.FormID, response.ResponseID, scope: loaded.Scope);
        Assert.Equal("Name", retained.Presentation!.Fields[0].Label);
        Assert.Equal(response.FormVersionID, retained.Response!.FormVersionID);
        fixture.Actor.Current = fixture.Actor.Current with { AuthenticationRevision = "changed" };
        Assert.Equal("PermissionDenied", (await fixture.Sessions.AnswerAsync(project.FormID, response.ResponseID,
            response.Revision, project.Fields[0].FieldID, JsonSerializer.SerializeToElement("Denied"), scope: loaded.Scope)).Code);
        Assert.Equal("PermissionDenied", (await fixture.Sessions.ReadSessionAsync(project.FormID, response.ResponseID, scope: loaded.Scope)).Code);
        Assert.Empty((await fixture.Sessions.ResumeAsync(project.FormID, response.ResponseID)).Response!.Answers);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Paths _paths = new();
        public ActorSource Actor { get; } = new();
        private readonly Authority _authority = new();
        private readonly VersionedAtomicSettingsStore _store;
        public FormPublicationService Publications { get; }
        public FormResponseSessionService Sessions { get; }
        public Fixture()
        {
            _store = new(_paths); Publications = new(_store, _store, _authority, new FormNativePublicationValidator(), actors: Actor);
            Sessions = new(Publications, _store, _store, _authority, Actor);
        }
        public FormResponseSessionService Reopen()
        {
            var store = new VersionedAtomicSettingsStore(_paths);
            return new(new(store, store, _authority, new FormNativePublicationValidator(), actors: Actor), store, store, _authority, Actor);
        }
        public async Task<(FormProject, FormResponse)> Create(bool marked = false, int maximumAttempts = 1)
        {
            var now = DateTimeOffset.UtcNow;
            var project = FormProjectEditor.Create("Durable response", FormModeKind.Form, now);
            project = project with { RuntimeSettings = project.RuntimeSettings with { MaximumAttempts = maximumAttempts } };
            foreach (var (kind, label) in new[] { (FormFieldKind.ShortText, "Name"), (FormFieldKind.Number, "Amount") })
                project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID,
                    new(Guid.NewGuid(), kind, label, null, JsonSerializer.SerializeToElement(new { }), true, new(),
                        Assessment: marked && kind == FormFieldKind.ShortText ? new(1, 1,
                            [new(Guid.NewGuid(), FormMarkingRuleKind.AcceptedText, 1, AcceptedTexts: ["hidden expected answer"])]) : null), now);
            var created = await Publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
            var publication = (await Publications.PublishAsync(project.FormID, created.Publication!.Revision)).Publication!;
            var started = await Sessions.StartAsync(project.FormID, publication.Revision);
            Assert.True(started.Success);
            return (project, started.Response!);
        }
        public void Dispose() => _paths.Dispose();
    }
    private sealed class ActorSource : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = new("actor", "profile", null, null, "login");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Authority : IFormStoreCommitAuthority
    {
        public ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken token) => ValueTask.FromResult(true);
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken token) => ValueTask.FromResult<ISettingsCommitAdmission?>(new Admission());
    }
    private sealed class Admission : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) => ValueTask.FromResult(true);
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("forms-native-response-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }
}

[CollectionDefinition("Forms native renderer", DisableParallelization = true)]
public sealed class FormsNativeRendererCollection;
