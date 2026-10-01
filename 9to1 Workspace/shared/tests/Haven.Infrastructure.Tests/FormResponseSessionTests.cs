using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;

namespace Haven.Infrastructure.Tests;

public sealed class FormResponseSessionTests
{
    [Theory]
    [InlineData("start", false)]
    [InlineData("start", true)]
    [InlineData("resume", false)]
    [InlineData("presentation", false)]
    [InlineData("answer", false)]
    [InlineData("submit", false)]
    [InlineData("submitted", false)]
    public async Task Originating_actor_change_during_publication_load_does_not_save_or_disclose_a_response(string operation, bool switchProfile)
    {
        using var paths = new Paths(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        var authority = new Authority(); var actor = new Actor(); var originalActor = actor.Current;
        var store = new VersionedAtomicSettingsStore(paths);
        var publications = new FormPublicationService(store, store, authority, new Validator(), actors: actor);
        var project = FormProjectEditor.Create("Private response", FormModeKind.Form, DateTimeOffset.UtcNow);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Answer", null, JsonSerializer.SerializeToElement(new { }), false, new());
        project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, DateTimeOffset.UtcNow);
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project), token);
        var published = (await publications.PublishAsync(project.FormID, created.Publication!.Revision, token)).Publication!;
        var sessions = new FormResponseSessionService(publications, store, store, authority, actor);
        FormResponse? response = null;
        if (operation != "start")
        {
            var started = await sessions.StartAsync(project.FormID, published.Revision, token); Assert.True(started.Success);
            var answered = await sessions.AnswerAsync(project.FormID, started.Response!.ResponseID, started.Response.Revision,
                field.FieldID, JsonSerializer.SerializeToElement("private existing answer"), token);
            Assert.True(answered.Success); response = answered.Response!;
            if (operation == "submitted")
            { var submitted = await sessions.SubmitAsync(project.FormID, response.ResponseID, response.Revision, token); Assert.True(submitted.Success); response = submitted.Response!; }
        }
        var before = JsonSerializer.Serialize((await store.ExportAsync(token)).Settings);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        authority.BeforeReturn = async action =>
        { if (action == "forms.read") { entered.TrySetResult(); await release.Task.WaitAsync(token); } };
        var pending = InvokeAsync(); await entered.Task.WaitAsync(token);
        actor.Current = switchProfile ? originalActor with { ActorId = "other-actor", ProfileId = "other-profile" }
            : originalActor with { AuthenticationRevision = "new-authentication" };
        release.TrySetResult(); var outcome = await pending;
        Assert.False(outcome.Success); Assert.Equal("PermissionDenied", outcome.Code); Assert.False(outcome.Disclosed);
        authority.BeforeReturn = null;
        Assert.Equal(before, JsonSerializer.Serialize((await new VersionedAtomicSettingsStore(paths).ExportAsync(token)).Settings));
        actor.Current = originalActor;
        if (response is not null)
        {
            var retained = await sessions.ReadSessionAsync(project.FormID, response.ResponseID, token);
            Assert.True(retained.Success); Assert.Equal(response.Revision, retained.Response!.Revision);
            Assert.Equal("private existing answer", Assert.Single(retained.Response.Answers).Value.GetString());
        }
        async Task<(bool Success, string? Code, bool Disclosed)> InvokeAsync()
        {
            if (operation == "presentation")
            {
                var result = await sessions.ReadSessionAsync(project.FormID, response!.ResponseID, token);
                return (result.Success, result.Code, result.Presentation is not null || result.Response is not null || result.Scope is not null);
            }
            if (operation == "submitted")
            {
                var result = await sessions.ReadSubmittedAsync(project.FormID, response!.ResponseID, token);
                return (result.Success, result.Code, result.Project is not null || result.Response is not null || result.Actor is not null);
            }
            var session = operation switch
            {
                "start" => await sessions.StartAsync(project.FormID, published.Revision, token),
                "resume" => await sessions.ResumeAsync(project.FormID, response!.ResponseID, token),
                "answer" => await sessions.AnswerAsync(project.FormID, response!.ResponseID, response!.Revision, field.FieldID, JsonSerializer.SerializeToElement("must not save"), token),
                "submit" => await sessions.SubmitAsync(project.FormID, response!.ResponseID, response!.Revision, token),
                _ => throw new InvalidOperationException("Unknown test operation")
            };
            return (session.Success, session.Code, session.Response is not null);
        }
    }

    [Fact]
    public async Task Closing_publication_after_final_response_authority_check_prevents_answer_commit()
    {
        using var paths = new Paths();
        var authority = new Authority(); var actor = new Actor();
        var store = new VersionedAtomicSettingsStore(paths);
        var publications = new FormPublicationService(store, store, authority, new Validator(), actors: actor);
        var project = FormProjectEditor.Create("Closing race", FormModeKind.Form, DateTimeOffset.UtcNow);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Answer", null,
            JsonSerializer.SerializeToElement(new { }), false, new());
        project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, DateTimeOffset.UtcNow);
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        var published = (await publications.PublishAsync(project.FormID, created.Publication!.Revision)).Publication!;
        var sessions = new FormResponseSessionService(publications, store, store, authority, actor);
        var started = await sessions.StartAsync(project.FormID, published.Revision);
        Assert.True(started.Success);
        var concurrentStore = new VersionedAtomicSettingsStore(paths);
        var concurrentPublications = new FormPublicationService(concurrentStore, concurrentStore, authority, new Validator(), actors: actor);
        var responseChecks = 0;
        authority.BeforeReturn = async action =>
        {
            if (action == "forms.response.answer" && ++responseChecks == 2)
                Assert.True((await concurrentPublications.CloseAsync(project.FormID, published.Revision)).Success);
        };
        var answer = await sessions.AnswerAsync(project.FormID, started.Response!.ResponseID, started.Response.Revision,
            field.FieldID, JsonSerializer.SerializeToElement("must not commit"));
        Assert.False(answer.Success);
        Assert.Equal("RevisionConflict", answer.Code);
        var retained = await sessions.ResumeAsync(project.FormID, started.Response.ResponseID);
        Assert.True(retained.Success);
        Assert.Equal(started.Response.Revision, retained.Response!.Revision);
        Assert.Empty(retained.Response.Answers);
        Assert.Equal(FormPublicationState.Closed, (await publications.ReadAsync(project.FormID)).Publication!.State);
    }

    [Fact]
    public async Task Concurrent_attempt_admission_commits_one_response_and_survives_reopen()
    {
        using var paths = new Paths();
        var authority = new Authority(); var actor = new Actor();
        var firstStore = new VersionedAtomicSettingsStore(paths);
        var secondStore = new VersionedAtomicSettingsStore(paths);
        var firstPublication = new FormPublicationService(firstStore, firstStore, authority, new Validator(), actors: actor);
        var secondPublication = new FormPublicationService(secondStore, secondStore, authority, new Validator(), actors: actor);
        var project = FormProjectEditor.Create("One attempt", FormModeKind.Form, DateTimeOffset.UtcNow);
        var created = await firstPublication.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        var published = (await firstPublication.PublishAsync(project.FormID, created.Publication!.Revision)).Publication!;
        var first = new FormResponseSessionService(firstPublication, firstStore, firstStore, authority, actor);
        var second = new FormResponseSessionService(secondPublication, secondStore, secondStore, authority, actor);
        var starts = await Task.WhenAll(first.StartAsync(project.FormID, published.Revision), second.StartAsync(project.FormID, published.Revision));
        var winner = Assert.Single(starts, result => result.Success);
        Assert.Single(starts, result => result.Code is "RevisionConflict" or "AttemptLimitReached");
        var freshStore = new VersionedAtomicSettingsStore(paths);
        var freshPublication = new FormPublicationService(freshStore, freshStore, authority, new Validator(), actors: actor);
        var fresh = new FormResponseSessionService(freshPublication, freshStore, freshStore, authority, actor);
        Assert.Equal("AttemptLimitReached", (await fresh.StartAsync(project.FormID, published.Revision)).Code);
        Assert.Equal(winner.Response!.ResponseID, (await fresh.ResumeAsync(project.FormID, winner.Response.ResponseID)).Response!.ResponseID);
    }

    [Fact]
    public async Task Durable_response_resume_retains_old_published_schema_and_ownership_and_competing_answers_conflict()
    {
        using var paths = new Paths();
        var authority = new Authority(); var actor = new Actor();
        var store = new VersionedAtomicSettingsStore(paths);
        var publications = new FormPublicationService(store, store, authority, new Validator(), actors: actor);
        var project = FormProjectEditor.Create("Assessment", FormModeKind.Test, DateTimeOffset.UtcNow);
        var field = new FormField(Guid.NewGuid(), FormFieldKind.ShortText, "Processor", null,
            JsonSerializer.SerializeToElement(new { }), true, new(), Assessment: new(1, 1,
                [new(Guid.NewGuid(), FormMarkingRuleKind.AcceptedText, 1, AcceptedTexts: ["CPU"])]));
        project = FormProjectEditor.AddField(project, project.Revision, project.Pages[0].PageID, field, DateTimeOffset.UtcNow);
        var created = await publications.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        var published = (await publications.PublishAsync(project.FormID, created.Publication!.Revision)).Publication!;
        var sessions = new FormResponseSessionService(publications, store, store, authority, actor);
        var started = await sessions.StartAsync(project.FormID, published.Revision);
        Assert.True(started.Success);
        var responseID = started.Response!.ResponseID;
        Assert.Equal("ResponseNotSubmitted", (await sessions.ReadSubmittedAsync(project.FormID, responseID)).Code);
        Assert.Equal("AttemptLimitReached", (await sessions.StartAsync(project.FormID, published.Revision)).Code);
        var answered = await sessions.AnswerAsync(project.FormID, responseID, 1, field.FieldID, JsonSerializer.SerializeToElement("CPU"));
        Assert.True(answered.Success); Assert.Empty(answered.Response!.ReleasedResults);
        var updated = await new FormAuthoringService(publications).UpdateFieldAsync(project.FormID, published.Revision,
            field with { Kind = FormFieldKind.Number, Label = "New numeric draft" });
        Assert.True(updated.Success);
        Assert.True((await publications.PublishAsync(project.FormID, updated.Publication!.Revision)).Success);
        var reopenedStore = new VersionedAtomicSettingsStore(paths);
        var reopenedPublications = new FormPublicationService(reopenedStore, reopenedStore, authority, new Validator(), actors: actor);
        var reopened = new FormResponseSessionService(reopenedPublications, reopenedStore, reopenedStore, authority, actor);
        actor.Current = actor.Current with { AuthenticationRevision = "reopened-login" };
        var resumed = await reopened.ResumeAsync(project.FormID, responseID);
        Assert.True(resumed.Success);
        Assert.Equal(started.Response.FormVersionID, resumed.Response!.FormVersionID);
        Assert.Equal("CPU", Assert.Single(resumed.Response.Answers).Value.GetString());
        Assert.Empty(resumed.Response.ReleasedResults);
        var competing = await Task.WhenAll(
            reopened.AnswerAsync(project.FormID, responseID, 2, field.FieldID, JsonSerializer.SerializeToElement("CPU")),
            sessions.AnswerAsync(project.FormID, responseID, 2, field.FieldID, JsonSerializer.SerializeToElement("wrong")));
        Assert.Single(competing, result => result.Success);
        Assert.Single(competing, result => result.Code == "RevisionConflict");
        var beforeDeny = (await reopened.ResumeAsync(project.FormID, responseID)).Response!;
        authority.Allowed = false;
        Assert.Equal("PermissionDenied", (await reopened.SubmitAsync(project.FormID, responseID, beforeDeny.Revision)).Code);
        authority.Allowed = true;
        var owner = actor.Current;
        actor.Current = owner with { ActorId = "other", ProfileId = "other-profile" };
        Assert.Equal("ResponseUnavailable", (await reopened.ResumeAsync(project.FormID, responseID)).Code);
        actor.Current = owner;
        var submitted = await reopened.SubmitAsync(project.FormID, responseID, beforeDeny.Revision);
        Assert.True(submitted.Success);
        Assert.Equal(FormResponseState.Submitted, submitted.Response!.State);
        Assert.Single(submitted.Response.ReleasedResults);
        Assert.Equal(started.Response.FormVersionID, submitted.Response.FormVersionID);
        var retained = await reopened.ReadSubmittedAsync(project.FormID, responseID);
        Assert.True(retained.Success); Assert.Equal(owner, retained.Actor);
        Assert.Equal(FormFieldKind.ShortText, Assert.Single(retained.Project!.Fields).Kind);
        Assert.Equal("Processor", Assert.Single(retained.Project.Fields).Label);
        Assert.Equal(submitted.Response.FormVersionID, retained.Response!.FormVersionID);
        actor.Current = owner with { ActorId = "other", ProfileId = "other-profile" };
        Assert.Equal("ResponseUnavailable", (await reopened.ReadSubmittedAsync(project.FormID, responseID)).Code);
        actor.Current = owner; authority.Allowed = false;
        Assert.Equal("PermissionDenied", (await reopened.ReadSubmittedAsync(project.FormID, responseID)).Code);
        authority.Allowed = true;
        Assert.Equal("ResponseClosed", (await reopened.AnswerAsync(project.FormID, responseID, submitted.Response.Revision,
            field.FieldID, JsonSerializer.SerializeToElement("CPU"))).Code);
    }

    private sealed class Actor : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = new("actor", "profile", null, null, "login");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Authority : IFormStoreCommitAuthority
    {
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ISettingsCommitAdmission?>(new FixtureAdmission(() => Allowed));

        public bool Allowed { get; set; } = true;
        public Func<string, Task>? BeforeReturn { get; set; }
        public async ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken cancellationToken)
        {
            if (BeforeReturn is not null) await BeforeReturn(actionID);
            return Allowed;
        }
    }
    private sealed class Validator : IFormProjectPublicationValidator
    {
        public void Validate(Guid formID, JsonElement canonicalProject)
        {
            var project = FormProjectCodec.Decode(System.Text.Encoding.UTF8.GetBytes(canonicalProject.GetRawText()));
            Assert.Equal(formID, project.FormID);
            _ = new FormResponseRuntime(project, Guid.NewGuid());
        }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("forms-response-").FullName;
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
