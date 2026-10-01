using System.Text.Json;
using Haven.Core.Forms;

namespace Haven.Application;

public sealed record FormResponseSessionResult(bool Success, string? Code, FormResponse? Response);
public sealed record FormSubmittedResponseResult(bool Success, string? Code, FormProject? Project, FormResponse? Response,
    AuthenticatedResourceActor? Actor = null, Guid StoreID = default);

/// <summary>Authenticated response save/resume over the canonical publication and Home-backed settings ports.
/// One compare/exchange contains attempt admission and the response checkpoint; no respondent supplies
/// owner identity, marks, timers, checkpoints or authored rules. Anonymous/live sessions need their own
/// trusted respondent admission and are not inferred from an arbitrary request ID.</summary>
public sealed partial class FormResponseSessionService(FormPublicationService publications, IVersionedSettingsStore settings,
    IResourceStoreIdentitySource identities, IFormStoreAuthority authority, IAuthenticatedResourceActorSource actors,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static string Key(Guid formID) => "forms.response-sessions.v1." + formID.ToString("N");

    public async Task<FormResponseSessionResult> StartAsync(Guid formID, long expectedPublicationRevision, CancellationToken token = default)
    {
        var originalRoot = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (originalRoot.SchemaVersion != 1 || originalRoot.StoreId == Guid.Empty) return new(false, "PermissionDenied", null);
        var loaded = await publications.ReadAsync(formID, token).ConfigureAwait(false);
        if (!loaded.Success) return new(false, loaded.Code, null);
        var publication = loaded.Publication!;
        if (publication.Revision != expectedPublicationRevision) return new(false, "RevisionConflict", null);
        if (publication.State != FormPublicationState.Published || publication.ActiveVersionID is not { } versionID)
            return new(false, "FormClosed", null);
        var version = publication.Versions.Single(version => version.FormVersionID == versionID);
        var project = FormAuthoringService.Decode(version.Project);
        if (!project.PublishingSettings.AcceptResponses) return new(false, "FormClosed", null);
        var actor = await AuthorizeAsync(originalRoot.StoreId, publication, "forms.response.create", token).ConfigureAwait(false);
        if (actor is null) return new(false, "PermissionDenied", null);
        var (state, expectedJson, rootID) = await LoadAsync(formID, token).ConfigureAwait(false);
        if (rootID != originalRoot.StoreId) return new(false, "PermissionDenied", null);
        var owner = Owner.From(actor);
        if (state.Responses.Count(entry => entry.Owner == owner) >= project.RuntimeSettings.MaximumAttempts)
            return new(false, "AttemptLimitReached", null);
        if (state.Responses.Count >= 10000) return new(false, "ResponseCapacityReached", null);
        FormResponseRuntime runtime;
        try { runtime = new(project, versionID, _clock); }
        catch (NotSupportedException) { return new(false, "CapabilityUnavailable", null); }
        var updated = state with { Responses = state.Responses.Append(new Entry(owner, runtime.CaptureCheckpoint())).ToArray() };
        return await CommitAsync(publication, "forms.response.create", actor, rootID, expectedJson, updated, runtime.Read(), token).ConfigureAwait(false);
    }

    /// <summary>Identity of the actual settings root used by this response owner; acquisition may initialize that root UUID.</summary>
    public ValueTask<ResourceStoreIdentity> GetSourceStoreIdentityAsync(CancellationToken token = default) => identities.GetStoreIdentityAsync(token);

    /// <summary>Loads the owner's retained submitted response and its exact immutable published schema.
    /// Callers cannot substitute authored JSON, response marks or a newer draft during Data preparation.</summary>
    public async Task<FormSubmittedResponseResult> ReadSubmittedAsync(Guid formID, Guid responseID, CancellationToken token = default)
    {
        if (formID == Guid.Empty || responseID == Guid.Empty) return new(false, "InvalidArgument", null, null);
        var originalRoot = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (originalRoot.SchemaVersion != 1 || originalRoot.StoreId == Guid.Empty) return new(false, "PermissionDenied", null, null);
        var loaded = await publications.ReadAsync(formID, token).ConfigureAwait(false);
        if (!loaded.Success) return new(false, loaded.Code, null, null);
        var publication = loaded.Publication!;
        var actor = await AuthorizeAsync(originalRoot.StoreId, publication, "forms.response.read", token).ConfigureAwait(false);
        if (actor is null) return new(false, "PermissionDenied", null, null);
        var (state, _, rootID) = await LoadAsync(formID, token).ConfigureAwait(false);
        if (rootID != originalRoot.StoreId) return new(false, "PermissionDenied", null, null);
        var entry = state.Responses.SingleOrDefault(entry => entry.Checkpoint.ResponseID == responseID);
        if (entry is null || entry.Owner != Owner.From(actor)) return new(false, "ResponseUnavailable", null, null);
        if (entry.Checkpoint.SubmittedAt is null) return new(false, "ResponseNotSubmitted", null, null);
        var version = publication.Versions.SingleOrDefault(version => version.FormVersionID == entry.Checkpoint.FormVersionID)
            ?? throw new InvalidDataException("Response refers to a missing immutable form version.");
        var project = FormAuthoringService.Decode(version.Project);
        var response = FormResponseRuntime.Restore(project, entry.Checkpoint, _clock).Read();
        if (response.State != FormResponseState.Submitted) return new(false, "ResponseNotSubmitted", null, null);
        return await AuthorizeAsync(rootID, publication, "forms.response.read", token).ConfigureAwait(false) == actor
            ? new(true, null, project, response, actor, rootID) : new(false, "PermissionDenied", null, null);
    }

    public Task<FormResponseSessionResult> ResumeAsync(Guid formID, Guid responseID, CancellationToken token = default) =>
        OperateAsync(formID, responseID, null, "forms.response.read", null, token);
    public Task<FormResponseSessionResult> AnswerAsync(Guid formID, Guid responseID, long expectedRevision, Guid fieldID,
        JsonElement answer, CancellationToken token = default)
    {
        var captured = answer.Clone();
        return OperateAsync(formID, responseID, expectedRevision, "forms.response.answer", runtime => runtime.Answer(expectedRevision, fieldID, captured), token);
    }
    public Task<FormResponseSessionResult> AdvanceAsync(Guid formID, Guid responseID, long expectedRevision, CancellationToken token = default) =>
        OperateAsync(formID, responseID, expectedRevision, "forms.response.advance", runtime => runtime.Advance(expectedRevision), token);
    public Task<FormResponseSessionResult> SubmitAsync(Guid formID, Guid responseID, long expectedRevision, CancellationToken token = default) =>
        OperateAsync(formID, responseID, expectedRevision, "forms.response.submit", runtime => runtime.Submit(expectedRevision), token);

    private async Task<FormResponseSessionResult> OperateAsync(Guid formID, Guid responseID, long? expectedRevision,
        string action, Func<FormResponseRuntime, FormResponseOperation>? operation, CancellationToken token)
    {
        if (formID == Guid.Empty || responseID == Guid.Empty) return new(false, "InvalidArgument", null);
        var originalRoot = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (originalRoot.SchemaVersion != 1 || originalRoot.StoreId == Guid.Empty) return new(false, "PermissionDenied", null);
        var loaded = await publications.ReadAsync(formID, token).ConfigureAwait(false);
        if (!loaded.Success) return new(false, loaded.Code, null);
        var publication = loaded.Publication!;
        var actor = await AuthorizeAsync(originalRoot.StoreId, publication, action, token).ConfigureAwait(false);
        if (actor is null) return new(false, "PermissionDenied", null);
        var (state, expectedJson, rootID) = await LoadAsync(formID, token).ConfigureAwait(false);
        if (rootID != originalRoot.StoreId) return new(false, "PermissionDenied", null);
        var entry = state.Responses.SingleOrDefault(entry => entry.Checkpoint.ResponseID == responseID);
        if (entry is null || entry.Owner != Owner.From(actor)) return new(false, "ResponseUnavailable", null);
        if (expectedRevision is { } expected && entry.Checkpoint.Revision != expected) return new(false, "RevisionConflict", null);
        var version = publication.Versions.SingleOrDefault(version => version.FormVersionID == entry.Checkpoint.FormVersionID)
            ?? throw new InvalidDataException("Response refers to a missing immutable form version.");
        var project = FormAuthoringService.Decode(version.Project);
        if (operation is null && entry.Checkpoint.SubmittedAt is null && !project.RuntimeSettings.AllowResume)
            return new(false, "ResumeDisabled", null);
        if (operation is not null && (publication.State != FormPublicationState.Published || !project.PublishingSettings.AcceptResponses))
            return new(false, "FormClosed", null);
        var runtime = FormResponseRuntime.Restore(project, entry.Checkpoint, _clock);
        if (operation is null)
            return await AuthorizeAsync(originalRoot.StoreId, publication, action, token).ConfigureAwait(false) == actor
                ? new(true, null, runtime.Read()) : new(false, "PermissionDenied", null);
        var result = operation(runtime);
        if (!result.Success)
            return await AuthorizeAsync(originalRoot.StoreId, publication, action, token).ConfigureAwait(false) == actor
                ? new(false, result.Code, result.Response) : new(false, "PermissionDenied", null);
        var updated = state with { Responses = state.Responses.Select(item => item == entry
            ? entry with { Checkpoint = runtime.CaptureCheckpoint() } : item).ToArray() };
        return await CommitAsync(publication, action, actor, rootID, expectedJson, updated, result.Response, token).ConfigureAwait(false);
    }

    private async Task<FormResponseSessionResult> CommitAsync(FormPublication publication, string action,
        AuthenticatedResourceActor actor, Guid rootID, string? expectedJson, State state, FormResponse response, CancellationToken token)
    {
        if (settings is not IVersionedSettingsGuardedCompareExchange atomic) return new(false, "AtomicStoreUnavailable", null);
        if (authority is not IFormStoreCommitAuthority commitAuthority) return new(false, "PermissionDenied", null);
        var admission = await commitAuthority.CaptureCommitAdmissionAsync(rootID, publication.FormID, publication.Revision, action, actor, token).ConfigureAwait(false);
        if (admission is null) return new(false, "PermissionDenied", null);
        state = state with { SchemaVersion = 3, Responses = state.Responses.Select(entry =>
            entry with { DataWrites = entry.DataWrites ?? [] }).ToArray() };
        var json = JsonSerializer.Serialize(state);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > FormProjectCodec.MaximumBytes) return new(false, "ResponseCapacityReached", null);
        var current = await publications.ReadAsync(publication.FormID, token).ConfigureAwait(false);
        if (!current.Success) return new(false, current.Code, null);
        if (current.Publication!.Revision != publication.Revision) return new(false, "RevisionConflict", null);
        var publicationKey = "forms.publication.v1." + publication.FormID.ToString("N");
        var snapshot = await settings.ExportAsync(token).ConfigureAwait(false);
        if (!snapshot.Settings.TryGetValue(publicationKey, out var publicationJson) || publicationJson is null)
            return new(false, "RevisionConflict", null);
        var guardedPublication = JsonSerializer.Deserialize<FormPublication>(publicationJson);
        if (guardedPublication is null || JsonSerializer.Serialize(guardedPublication) != JsonSerializer.Serialize(current.Publication))
            return new(false, "RevisionConflict", null);
        if (await AuthorizeAsync(rootID, publication, action, token).ConfigureAwait(false) != actor) return new(false, "PermissionDenied", null);
        // Closing or republishing must conflict at the same durable commit as the answer,
        // including a change after the final authority check above.
        var exchanged = await atomic.CompareExchangeGuardedAsync(Key(publication.FormID), expectedJson, json,
            new Dictionary<string, string?> { [publicationKey] = publicationJson }, new FormCommitAdmission(rootID, admission, actors, actor), token).ConfigureAwait(false);
        return exchanged.Exchanged ? new(true, null, response) : new(false, exchanged.AdmissionRejected ? "PermissionDenied" : "RevisionConflict", null);
    }

    private async Task<AuthenticatedResourceActor?> AuthorizeAsync(Guid rootID, FormPublication publication, string action, CancellationToken token)
    {
        var actor = await actors.GetCurrentAsync(token).ConfigureAwait(false);
        if (actor is null || string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.ProfileId)
            || string.IsNullOrWhiteSpace(actor.AuthenticationRevision) || actor.AccountId == Guid.Empty
            || actor.OrganisationId == Guid.Empty || actor.OrganisationId is not null && actor.AccountId is null) return null;
        var root = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (root.SchemaVersion != 1 || root.StoreId != rootID) return null;
        return await authority.AuthorizeAsync(root.StoreId, publication.FormID, publication.Revision, action, token).ConfigureAwait(false)
            && await actors.GetCurrentAsync(token).ConfigureAwait(false) == actor ? actor : null;
    }

    private async Task<(State State, string? Json, Guid RootID)> LoadAsync(Guid formID, CancellationToken token)
    {
        var snapshot = await settings.ExportAsync(token).ConfigureAwait(false);
        snapshot.Settings.TryGetValue(Key(formID), out var json);
        if (json is not null && System.Text.Encoding.UTF8.GetByteCount(json) > FormProjectCodec.MaximumBytes)
            throw new InvalidDataException("Response store exceeds its configured byte bound.");
        var state = json is null ? new State(1, formID, []) : JsonSerializer.Deserialize<State>(json)
            ?? throw new InvalidDataException("Response store is missing.");
        if (state.SchemaVersion is not (1 or 2 or 3) || state.FormID != formID || state.Responses is null || state.Responses.Count > 10000
            || state.Responses.Any(entry => entry is null || entry.Owner is null || entry.Checkpoint is null
                || entry.Checkpoint.FormID != formID || entry.Checkpoint.ResponseID == Guid.Empty)
            || state.Responses.Select(entry => entry.Checkpoint.ResponseID).Distinct().Count() != state.Responses.Count)
            throw new InvalidDataException("Response store identity or schema is invalid.");
        if (snapshot.StoreIdentity is not { SchemaVersion: 1 } identity || identity.StoreId == Guid.Empty)
            throw new InvalidDataException("Response store identity is unavailable.");
        foreach (var entry in state.Responses)
        {
            if (state.SchemaVersion >= 2 && entry.DataWrites is null || state.SchemaVersion == 1 && entry.DataWrites is { Count: > 0 }
                || entry.DataWrites is { Count: > 256 } || entry.Checkpoint.SubmittedAt is null && entry.DataWrites is { Count: > 0 })
                throw new InvalidDataException("Invalid response Data journal schema.");
            var operations = new HashSet<Guid>();
            foreach (var attempt in entry.DataWrites ?? [])
            {
                if (state.SchemaVersion < 3 && (attempt.ObservedTarget is not null || attempt.Status == FormsDataWriteStatus.Conflict))
                    throw new InvalidDataException("Data conflict evidence requires response schema three.");
                var captured = FormDataWriteAttemptValidation.Capture(attempt, formID, entry.Checkpoint.ResponseID,
                    entry.Checkpoint.FormVersionID, entry.Checkpoint.Revision, identity.StoreId);
                if (!operations.Add(captured.Operation.OperationID)) throw new InvalidDataException("Duplicate response Data operation.");
            }
        }
        return (state, json, identity.StoreId);
    }
    private sealed record State(int SchemaVersion, Guid FormID, IReadOnlyList<Entry> Responses);
    private sealed record Entry(Owner Owner, FormResponseCheckpoint Checkpoint, IReadOnlyList<FormDataWriteAttempt>? DataWrites = null);
    private sealed record Owner(string ActorID, string ProfileID, Guid? AccountID, Guid? OrganisationID)
    {
        public static Owner From(AuthenticatedResourceActor actor) => new(actor.ActorId, actor.ProfileId, actor.AccountId, actor.OrganisationId);
    }
}
