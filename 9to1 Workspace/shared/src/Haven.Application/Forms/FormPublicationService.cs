using System.Text.Json;
using Haven.Core.Forms;

namespace Haven.Application;

/// <summary>Trusted host intersects actual Home store ownership, ambient actor and current form ACL.
/// App arguments cannot establish ownership. Missing authority never grants access.</summary>
public interface IFormStoreAuthority
{
    ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID,
        CancellationToken cancellationToken);
}
/// <summary>Captures trusted current authority before acquiring the settings lease. The returned admission
/// must recheck actor/ACL using independent authority state only, without reading this settings store.</summary>
public interface IFormStoreCommitAuthority : IFormStoreAuthority
{
    ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
        string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken);
}

internal sealed class FormCommitAdmission(Guid storeID, ISettingsCommitAdmission inner,
    IAuthenticatedResourceActorSource? actors = null, AuthenticatedResourceActor? expectedActor = null) : ISettingsCommitAdmission
{
    public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken)
    {
        if (context.StoreIdentity.SchemaVersion != 1 || context.StoreIdentity.StoreId != storeID) return false;
        if (actors is not null && await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) != expectedActor) return false;
        return await inner.CheckAsync(context, cancellationToken).ConfigureAwait(false)
            && (actors is null || await actors.GetCurrentAsync(cancellationToken).ConfigureAwait(false) == expectedActor);
    }
}
/// <summary>The canonical Forms builder/runtime validates the complete authored project, including logic and bindings.
/// Publication must not treat a valid identity envelope as a valid form.</summary>
public interface IFormProjectPublicationValidator
{
    void Validate(Guid formID, JsonElement canonicalProject);
    /// <summary>Checks the actual configured runtime/presentation/integration capabilities before activation.
    /// Draft validation remains structural so an unavailable capability does not destroy authored state.</summary>
    void ValidateForPublication(Guid formID, JsonElement canonicalProject) => Validate(formID, canonicalProject);
}
public sealed record FormPublicationResult(bool Success, string? Code, FormPublication? Publication);

/// <summary>One singleton per durable settings root. Failed writes retain the previous published version.
/// Exact serialized compare/exchange rejects concurrent changes across service instances.
/// Builder, preview and runtime retain one project projection.</summary>
public sealed class FormPublicationService(IVersionedSettingsStore settings, IResourceStoreIdentitySource identities,
    IFormStoreAuthority authority, IFormProjectPublicationValidator validator, TimeProvider? clock = null,
    IAuthenticatedResourceActorSource? actors = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    public Task<FormHostSession> OpenHostSessionAsync(AuthenticatedResourceActor expectedActor, CancellationToken token = default) =>
        FormHostSession.CreateAsync(settings, identities, authority, validator, actors, expectedActor, _clock, token);

    private static string Key(Guid id) => "forms.publication.v1." + id.ToString("N");

    public async Task<FormPublicationResult> CreateAsync(Guid formID, JsonElement canonicalProject, CancellationToken token = default)
    {
        FormPublicationValidation.ValidateProject(formID, canonicalProject);
        validator.Validate(formID, canonicalProject);
        var snapshot = canonicalProject.Clone();
        return await ChangeAsync(formID, 0, "forms.create", current => current is not null
            ? throw new InvalidOperationException("FormExists")
            : new(formID, 1, snapshot, [], null, FormPublicationState.Draft), token).ConfigureAwait(false);
    }
    public Task<FormPublicationResult> SaveDraftAsync(Guid id, long revision, JsonElement canonicalProject, CancellationToken token = default)
    {
        FormPublicationValidation.ValidateProject(id, canonicalProject);
        validator.Validate(id, canonicalProject);
        var snapshot = canonicalProject.Clone();
        return ChangeAsync(id, revision, "forms.edit", current => Required(current) with { Draft = snapshot, Revision = checked(revision + 1) }, token);
    }
    public Task<FormPublicationResult> PublishAsync(Guid id, long revision, CancellationToken token = default) =>
        ChangeAsync(id, revision, "forms.publish", current =>
        {
            var form = Required(current);
            validator.ValidateForPublication(id, form.Draft);
            var version = new FormPublishedVersion(id, Guid.NewGuid(), revision, _clock.GetUtcNow(), form.Draft.Clone());
            return form with { Revision = checked(revision + 1), Versions = form.Versions.Append(version).ToArray(),
                ActiveVersionID = version.FormVersionID, State = FormPublicationState.Published };
        }, token);
    public Task<FormPublicationResult> CloseAsync(Guid id, long revision, CancellationToken token = default) =>
        ChangeAsync(id, revision, "forms.close", current => Required(current) with { Revision = checked(revision + 1), State = FormPublicationState.Closed }, token);

    public Task<FormPublicationResult> ReadAsync(Guid id, CancellationToken token = default)
        => ReadCoreAsync(id, null, null, token);

    /// <summary>Owning retained read context. IDs and actor values are constraints, never grants;
    /// actual current authority must still admit the original actor/root before publication.</summary>
    public Task<FormPublicationResult> ReadForOriginalActorAsync(Guid id, AuthenticatedResourceActor expectedActor,
        Guid expectedStoreID, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(expectedActor);
        if (expectedStoreID == Guid.Empty) return Task.FromResult(new FormPublicationResult(false, "PermissionDenied", null));
        return ReadCoreAsync(id, expectedActor, expectedStoreID, token);
    }
    private async Task<FormPublicationResult> ReadCoreAsync(Guid id, AuthenticatedResourceActor? expectedActor,
        Guid? expectedStoreID, CancellationToken token)
    {
        var originatingActor = actors is null ? null : await actors.GetCurrentAsync(token).ConfigureAwait(false);
        if (originatingActor is null || expectedActor is not null && originatingActor != expectedActor)
            return new(false, "PermissionDenied", null);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            ResourceStoreIdentity? originalRoot = null;
            if (expectedStoreID is { } expectedRoot)
            {
                originalRoot = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
                if (originalRoot.SchemaVersion != 1 || originalRoot.StoreId != expectedRoot
                    || await actors!.GetCurrentAsync(token).ConfigureAwait(false) != originatingActor)
                    return new(false, "PermissionDenied", null);
            }
            if (await actors!.GetCurrentAsync(token).ConfigureAwait(false) != originatingActor)
                return new(false, "PermissionDenied", null);
            var form = await LoadAsync(id, token).ConfigureAwait(false);
            if (await actors!.GetCurrentAsync(token).ConfigureAwait(false) != originatingActor)
                return new(false, "PermissionDenied", null);
            if (form is null) return new(false, "NotFound", null);
            var root = originalRoot ?? await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
            if (!await authority.AuthorizeAsync(root.StoreId, id, form.Revision, "forms.read", token).ConfigureAwait(false)
                || await actors!.GetCurrentAsync(token).ConfigureAwait(false) != originatingActor)
                return new(false, "PermissionDenied", null);
            if (expectedStoreID is { } retainedRoot)
            {
                var currentRoot = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
                if (currentRoot.SchemaVersion != 1 || currentRoot.StoreId != retainedRoot
                    || await actors!.GetCurrentAsync(token).ConfigureAwait(false) != originatingActor)
                    return new(false, "PermissionDenied", null);
            }
            return new(true, null, Clone(form));
        }
        finally { _gate.Release(); }
    }

    private async Task<FormPublicationResult> ChangeAsync(Guid id, long expected, string action,
        Func<FormPublication?, FormPublication> change, CancellationToken token)
    {
        if (id == Guid.Empty || expected < 0) return new(false, "InvalidArgument", null);
        var originatingActor = actors is null ? null : await actors.GetCurrentAsync(token).ConfigureAwait(false);
        if (originatingActor is null) return new(false, "PermissionDenied", null);
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (settings is not IVersionedSettingsGuardedCompareExchange atomic) return new(false, "AtomicStoreUnavailable", null);
            var root = await identities.GetStoreIdentityAsync(token).ConfigureAwait(false);
            var snapshot = await settings.ExportAsync(token).ConfigureAwait(false);
            snapshot.Settings.TryGetValue(Key(id), out var expectedJson);
            var current = expectedJson is null ? null : JsonSerializer.Deserialize<FormPublication>(expectedJson)
                ?? throw new InvalidDataException("Stored form publication is null.");
            if (current is not null)
            {
                FormPublicationValidation.Validate(current);
                if (current.FormID != id) throw new InvalidDataException("Stored form identity mismatch.");
            }
            var revision = current?.Revision ?? 0;
            if (!await authority.AuthorizeAsync(root.StoreId, id, revision, action, token).ConfigureAwait(false)
                || await actors!.GetCurrentAsync(token).ConfigureAwait(false) != originatingActor)
                return new(false, "PermissionDenied", null);
            if (expected != revision) return new(false, "RevisionConflict", null);
            if (snapshot.StoreIdentity is not { SchemaVersion: 1 } identity || identity.StoreId != root.StoreId
                || authority is not IFormStoreCommitAuthority commitAuthority) return new(false, "PermissionDenied", null);
            var admission = await commitAuthority.CaptureCommitAdmissionAsync(root.StoreId, id, revision, action, originatingActor, token).ConfigureAwait(false);
            if (admission is null || await actors!.GetCurrentAsync(token).ConfigureAwait(false) != originatingActor) return new(false, "PermissionDenied", null);
            var updated = change(current);
            FormPublicationValidation.Validate(updated);
            // Recheck after preparing the publication; an ownership/ACL revoke prevents commit.
            if (!await authority.AuthorizeAsync(root.StoreId, id, revision, action, token).ConfigureAwait(false)
                || await actors!.GetCurrentAsync(token).ConfigureAwait(false) != originatingActor)
                return new(false, "PermissionDenied", null);
            var exchanged = await atomic.CompareExchangeGuardedAsync(Key(id), expectedJson,
                JsonSerializer.Serialize(updated), new Dictionary<string, string?>(), new FormCommitAdmission(root.StoreId, admission, actors, originatingActor), token).ConfigureAwait(false);
            if (!exchanged.Exchanged) return new(false, exchanged.AdmissionRejected ? "PermissionDenied" : "RevisionConflict", null);
            return new(true, null, Clone(updated));
        }
        catch (NotSupportedException) { return new(false, "CapabilityUnavailable", null); }
        catch (KeyNotFoundException) { return new(false, "NotFound", null); }
        catch (InvalidOperationException exception) when (exception.Message == "FormExists") { return new(false, "AlreadyExists", null); }
        catch (JsonException) { return new(false, "InvalidData", null); }
        catch (IOException) { return new(false, "StorageUnavailable", null); }
        finally { _gate.Release(); }
    }
    private async Task<FormPublication?> LoadAsync(Guid id, CancellationToken token)
    {
        var form = await settings.GetAsync<FormPublication>(Key(id), token).ConfigureAwait(false);
        if (form is not null)
        {
            FormPublicationValidation.Validate(form);
            if (form.FormID != id) throw new InvalidDataException("Stored form identity mismatch.");
        }
        return form;
    }
    private static FormPublication Required(FormPublication? value) => value ?? throw new KeyNotFoundException();
    private static FormPublication Clone(FormPublication form) => form with { Draft = form.Draft.Clone(),
        Versions = form.Versions.Select(version => version with { Project = version.Project.Clone() }).ToArray() };
}
