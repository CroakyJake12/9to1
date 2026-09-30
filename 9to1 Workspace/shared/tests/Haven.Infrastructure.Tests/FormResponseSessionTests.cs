using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;

namespace Haven.Infrastructure.Tests;

public sealed class FormResponseSessionTests
{
    [Fact]
    public async Task Closing_publication_after_final_response_authority_check_prevents_answer_commit()
    {
        using var paths = new Paths();
        var authority = new Authority(); var actor = new Actor();
        var store = new VersionedAtomicSettingsStore(paths);
        var publications = new FormPublicationService(store, store, authority, new Validator());
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
        var concurrentPublications = new FormPublicationService(concurrentStore, concurrentStore, authority, new Validator());
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
        var firstPublication = new FormPublicationService(firstStore, firstStore, authority, new Validator());
        var secondPublication = new FormPublicationService(secondStore, secondStore, authority, new Validator());
        var project = FormProjectEditor.Create("One attempt", FormModeKind.Form, DateTimeOffset.UtcNow);
        var created = await firstPublication.CreateAsync(project.FormID, FormProjectEditor.Project(project));
        var published = (await firstPublication.PublishAsync(project.FormID, created.Publication!.Revision)).Publication!;
        var first = new FormResponseSessionService(firstPublication, firstStore, firstStore, authority, actor);
        var second = new FormResponseSessionService(secondPublication, secondStore, secondStore, authority, actor);
        var starts = await Task.WhenAll(first.StartAsync(project.FormID, published.Revision), second.StartAsync(project.FormID, published.Revision));
        var winner = Assert.Single(starts, result => result.Success);
        Assert.Single(starts, result => result.Code is "RevisionConflict" or "AttemptLimitReached");
        var freshStore = new VersionedAtomicSettingsStore(paths);
        var freshPublication = new FormPublicationService(freshStore, freshStore, authority, new Validator());
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
        var publications = new FormPublicationService(store, store, authority, new Validator());
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
        Assert.Equal("AttemptLimitReached", (await sessions.StartAsync(project.FormID, published.Revision)).Code);
        var answered = await sessions.AnswerAsync(project.FormID, responseID, 1, field.FieldID, JsonSerializer.SerializeToElement("CPU"));
        Assert.True(answered.Success); Assert.Empty(answered.Response!.ReleasedResults);
        var updated = await new FormAuthoringService(publications).UpdateFieldAsync(project.FormID, published.Revision,
            field with { Kind = FormFieldKind.Number, Label = "New numeric draft" });
        Assert.True(updated.Success);
        Assert.True((await publications.PublishAsync(project.FormID, updated.Publication!.Revision)).Success);
        var reopenedStore = new VersionedAtomicSettingsStore(paths);
        var reopenedPublications = new FormPublicationService(reopenedStore, reopenedStore, authority, new Validator());
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
