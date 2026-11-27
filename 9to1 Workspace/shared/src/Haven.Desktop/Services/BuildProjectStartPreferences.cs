using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Services;

/// <summary>Stable metadata identity only. Every open still resolves its current canonical reference.</summary>
internal sealed record BuildProjectStartIdentity(string Kind, Guid WorkspaceId, Guid ProjectId, Guid RootId,
    string? RepositoryBindingId, Guid SpaceId, Guid ContextReferenceId)
{
    internal static BuildProjectStartIdentity ForManual(DeveloperProjectReference reference) =>
        new("manual", reference.WorkspaceId, reference.ProjectId, reference.RootId, reference.RepositoryBindingId, Guid.Empty, Guid.Empty);

    internal static BuildProjectStartIdentity ForTask(Guid spaceId, Guid contextReferenceId, DeveloperProjectReference reference) =>
        new("task", reference.WorkspaceId, reference.ProjectId, reference.RootId, reference.RepositoryBindingId, spaceId, contextReferenceId);

    internal bool IsValid => WorkspaceId != Guid.Empty && ProjectId != Guid.Empty && RootId != Guid.Empty &&
        (RepositoryBindingId is null || BuildProjectStartState.ValidText(RepositoryBindingId, 1024)) &&
        (Kind == "manual" && SpaceId == Guid.Empty && ContextReferenceId == Guid.Empty ||
         Kind == "task" && SpaceId != Guid.Empty && ContextReferenceId != Guid.Empty);
}

internal sealed record BuildSetupDraft(Guid Id, string Name, string Flow, string Destination,
    string? ProviderId, string? TemplateId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool Pinned = false);

internal sealed record BuildProjectStartEntry(BuildProjectStartIdentity Identity, bool Pinned, DateTimeOffset? LastOpenedAt);

internal sealed record BuildProjectStartState(long Revision, IReadOnlyList<BuildSetupDraft> Drafts,
    IReadOnlyList<BuildProjectStartEntry> Projects)
{
    [JsonRequired] public int SchemaVersion { get; init; } = 1;
    internal const int MaximumDrafts = 128;
    internal const int MaximumProjects = 512;
    internal static BuildProjectStartState Empty { get; } = new(0, [], []);

    internal BuildProjectStartState Validate()
    {
        if (SchemaVersion != 1 || Revision < 0 || Drafts is null || Projects is null ||
            Drafts.Count > MaximumDrafts || Projects.Count > MaximumProjects ||
            Drafts.Any(draft => draft is null || draft.Id == Guid.Empty || !ValidText(draft.Name, 120) ||
                draft.Name != draft.Name.Trim() || draft.Flow is not ("new" or "extension") ||
                draft.Destination is null || draft.Destination.Length > 4096 || draft.Destination.Any(char.IsControl) ||
                (draft.ProviderId is null) != (draft.TemplateId is null) ||
                draft.ProviderId is not null && (!ValidText(draft.ProviderId, 512) || !ValidText(draft.TemplateId, 512)) ||
                draft.Flow == "extension" && draft.ProviderId is not null ||
                draft.CreatedAt == default || draft.UpdatedAt < draft.CreatedAt) ||
            Drafts.Select(draft => draft.Id).Distinct().Count() != Drafts.Count ||
            Projects.Any(project => project is null || project.Identity is null || !project.Identity.IsValid ||
                project.LastOpenedAt == default(DateTimeOffset)) ||
            Projects.Select(project => project.Identity).Distinct().Count() != Projects.Count)
            throw new InvalidDataException("Build setup preferences have an unsupported format or invalid entries. Stored data was preserved.");
        return this;
    }

    internal static bool ValidText(string? value, int maximum) => value is { Length: > 0 } &&
        value.Length <= maximum && !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl);
}

/// <summary>Profile-scoped setup choices, pins and visits in the existing guarded settings store.
/// This owner never probes a destination, creates a project/folder, materializes a template or starts a tool.</summary>
internal sealed class BuildProjectStartPreferences
{
    internal sealed record Snapshot(BuildProjectStartState List, string? Serialized);
    internal sealed record SaveResult(bool Saved, Snapshot Current);
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    // Maximum schema strings at six JSON characters per escaped UTF-16 code unit plus all
    // bounded entries/keys remain below 8 MiB. Keep generous headroom before deserializing.
    private const int MaximumSerializedCharacters = 16 * 1024 * 1024;
    private static readonly IReadOnlyDictionary<string, string?> NoAdditionalGuards = new Dictionary<string, string?>();
    private readonly IVersionedSettingsStore _settings;
    private readonly Func<CancellationToken, Task> _admit;
    private readonly string _key;

    internal BuildProjectStartPreferences(IVersionedSettingsStore settings, string profileId, Func<CancellationToken, Task> admit)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _admit = admit ?? throw new ArgumentNullException(nameof(admit));
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        _key = "haven.build.start.v1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileId)));
    }

    internal bool CanEdit => _settings is IVersionedSettingsGuardedCompareExchange;

    internal async Task<Snapshot> ReadAsync(CancellationToken token)
    {
        await _admit(token).ConfigureAwait(false);
        var exported = await _settings.ExportAsync(token).ConfigureAwait(false);
        await _admit(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return Read(exported.Settings.GetValueOrDefault(_key));
    }

    internal async Task<SaveResult> SaveAsync(Snapshot expected, BuildProjectStartState candidate, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(candidate);
        var store = _settings as IVersionedSettingsGuardedCompareExchange
            ?? throw new NotSupportedException("Saving Build setup choices requires the original guarded settings store.");
        var next = (candidate with { Revision = checked(expected.List.Revision + 1) }).Validate();
        var serialized = JsonSerializer.Serialize(next, Json);
        var result = await store.CompareExchangeGuardedAsync(_key, expected.Serialized, serialized,
            NoAdditionalGuards, new Admission(_admit), token).ConfigureAwait(false);
        if (result.AdmissionRejected)
            throw new UnauthorizedAccessException("The original profile changed before Build setup choices could be saved.");
        return result.Exchanged ? new(true, new(next, serialized)) : new(false, Read(result.CurrentJson));
    }

    /// <summary>Call only AFTER the actual project-open callback has succeeded, under that same
    /// original/profile owner. A metadata failure must never replay the successful project open.</summary>
    internal async Task<Snapshot> RecordOpenedAsync(BuildProjectStartIdentity identity, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid) throw new ArgumentException("Retain the exact canonical project/root identity.", nameof(identity));
        var current = await ReadAsync(token).ConfigureAwait(false);
        var openedAt = DateTimeOffset.UtcNow;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var prior = current.List.Projects.SingleOrDefault(project => project.Identity == identity);
            var recordedAt = prior?.LastOpenedAt is { } later && later > openedAt ? later : openedAt;
            var entries = current.List.Projects.Where(project => project.Identity != identity)
                .Append(new(identity, prior?.Pinned == true, recordedAt)).ToList();
            if (entries.Count > BuildProjectStartState.MaximumProjects)
            {
                var oldest = entries.Where(project => !project.Pinned && project.Identity != identity)
                    .OrderBy(project => project.LastOpenedAt).FirstOrDefault();
                if (oldest is null)
                    throw new InvalidOperationException("The project opened, but Recent could not be saved because all stored project entries are pinned.");
                entries.Remove(oldest);
            }
            var result = await SaveAsync(current, current.List with { Projects = entries.ToArray() }, token).ConfigureAwait(false);
            if (result.Saved) return result.Current;
            current = result.Current; // Retry metadata CAS only; never repeat the project open.
        }
        throw new InvalidOperationException("The project opened, but concurrent preference changes prevented saving its Recent entry. The open was not repeated.");
    }

    private static Snapshot Read(string? raw)
    {
        if (raw is null) return new(BuildProjectStartState.Empty, null);
        if (raw.Length > MaximumSerializedCharacters)
            throw new InvalidDataException("Build setup preferences exceed the bounded serialized size. Stored data was preserved.");
        try
        {
            var value = JsonSerializer.Deserialize<BuildProjectStartState>(raw, Json)
                ?? throw new InvalidDataException("Build setup preferences are empty. Stored data was preserved.");
            return new(value.Validate(), raw);
        }
        catch (JsonException error)
        { throw new InvalidDataException("Build setup preferences could not be read. Stored data was preserved.", error); }
    }

    private sealed class Admission(Func<CancellationToken, Task> admit) : ISettingsCommitAdmission
    {
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
        {
            try { await admit(token).ConfigureAwait(false); return true; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
