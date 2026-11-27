using System.Text.Json;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Tests;

public sealed class BuildProjectStartPreferencesTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Task Admit(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }

    [Fact]
    public async Task Setup_choices_and_pins_reopen_under_the_same_profile_and_keep_one_stable_draft_id()
    {
        using var files = new BuildStartTestFiles();
        var store = new VersionedAtomicSettingsStore(files);
        await store.SetAsync("unrelated.project.owner", new { Value = "untouched" }, Token);
        var preferences = new BuildProjectStartPreferences(store, "profile-one", Admit);
        var before = await preferences.ReadAsync(Token);
        var draft = Draft() with { Destination = Path.Combine(files.DataDirectory, "uncreated-source") }; var identity = Manual();
        var first = await preferences.SaveAsync(before, new(0, [draft], [new(identity, true, null)]), Token);
        Assert.True(first.Saved);
        var reopened = new BuildProjectStartPreferences(new VersionedAtomicSettingsStore(files), "profile-one", Admit);
        var current = await reopened.ReadAsync(Token);
        Assert.Equal(draft, Assert.Single(current.List.Drafts));
        var changed = draft with { Name = "Revised setup", Destination = "still/not/a/real/folder", UpdatedAt = draft.UpdatedAt.AddMinutes(1) };
        Assert.True((await reopened.SaveAsync(current, current.List with { Drafts = [changed] }, Token)).Saved);
        Assert.Equal(draft.Id, Assert.Single((await preferences.ReadAsync(Token)).List.Drafts).Id);
        Assert.Equal(changed, Assert.Single((await preferences.ReadAsync(Token)).List.Drafts));
        Assert.True(Assert.Single((await preferences.ReadAsync(Token)).List.Projects).Pinned);
        Assert.Empty((await new BuildProjectStartPreferences(store, "profile-two", Admit).ReadAsync(Token)).List.Drafts);
        Assert.Equal("untouched", (await store.GetAsync<JsonDocument>("unrelated.project.owner", Token))!.RootElement.GetProperty("Value").GetString());
        Assert.False(Directory.Exists(draft.Destination));
    }

    [Fact]
    public async Task Setup_CAS_conflict_preserves_the_other_saved_draft_and_requires_explicit_resave()
    {
        using var files = new BuildStartTestFiles();
        var store = new VersionedAtomicSettingsStore(files);
        var first = new BuildProjectStartPreferences(store, "profile", Admit);
        var second = new BuildProjectStartPreferences(store, "profile", Admit);
        var a = await first.ReadAsync(Token); var b = await second.ReadAsync(Token);
        var retained = Draft();
        Assert.True((await first.SaveAsync(a, new(0, [retained], []), Token)).Saved);
        var conflict = await second.SaveAsync(b, new(0, [Draft()], []), Token);
        Assert.False(conflict.Saved);
        Assert.Equal(retained, Assert.Single(conflict.Current.List.Drafts));
        Assert.Equal(retained, Assert.Single((await first.ReadAsync(Token)).List.Drafts));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("duplicate")]
    [InlineData("extension-target")]
    public async Task Unsupported_or_invalid_setup_metadata_is_preserved_without_repairing_it(string change)
    {
        using var files = new BuildStartTestFiles();
        var store = new VersionedAtomicSettingsStore(files);
        var preferences = new BuildProjectStartPreferences(store, "profile", Admit);
        var draft = Draft();
        Assert.True((await preferences.SaveAsync(await preferences.ReadAsync(Token), new(0, [draft], []), Token)).Saved);
        var key = Assert.Single((await store.ExportAsync(Token)).Settings.Keys, value => value.StartsWith("haven.build.start.v1.", StringComparison.Ordinal));
        var invalid = change switch
        {
            "schema" => new BuildProjectStartState(1, [draft], []) { SchemaVersion = 7 },
            "duplicate" => new BuildProjectStartState(1, [draft, draft], []),
            _ => new BuildProjectStartState(1, [draft with { Flow = "extension" }], [])
        };
        await store.SetAsync(key, invalid, Token);
        var before = (await store.ExportAsync(Token)).Settings[key];
        await Assert.ThrowsAsync<InvalidDataException>(() => preferences.ReadAsync(Token));
        Assert.Equal(before, (await store.ExportAsync(Token)).Settings[key]);
    }

    [Fact]
    public async Task Maximum_escaped_bounded_state_remains_readable_but_oversized_serialized_input_is_refused_before_parsing()
    {
        using var files = new BuildStartTestFiles();
        var actual = new VersionedAtomicSettingsStore(files);
        var controlled = new BuildStartControlledSettings(actual);
        var preferences = new BuildProjectStartPreferences(controlled, "profile", Admit);
        Assert.True((await preferences.SaveAsync(await preferences.ReadAsync(Token), new(0, [Draft()], []), Token)).Saved);
        var key = Assert.Single((await actual.ExportAsync(Token)).Settings.Keys);
        var original = (await actual.ExportAsync(Token)).Settings[key];
        var now = DateTimeOffset.UtcNow;
        var drafts = Enumerable.Range(0, BuildProjectStartState.MaximumDrafts).Select(_ =>
            new BuildSetupDraft(Guid.NewGuid(), new('\u03BB', 120), "new", new('\u03BB', 4096),
                new('\u03BB', 512), new('\u03BB', 512), now, now, true)).ToArray();
        var entries = Enumerable.Range(0, BuildProjectStartState.MaximumProjects).Select(_ =>
            new BuildProjectStartEntry(Manual() with { RepositoryBindingId = new('\u03BB', 1024) }, true, now)).ToArray();
        var serialized = JsonSerializer.Serialize(new BuildProjectStartState(1, drafts, entries));
        Assert.True(serialized.Length < 8 * 1024 * 1024, "The documented maximum escaped metadata fits the headroom calculation.");
        controlled.AlterExport = value => value.Settings[key] = serialized;
        var maximum = await preferences.ReadAsync(Token);
        Assert.Equal(BuildProjectStartState.MaximumDrafts, maximum.List.Drafts.Count);
        Assert.Equal(BuildProjectStartState.MaximumProjects, maximum.List.Projects.Count);
        var oversized = new string(' ', 16 * 1024 * 1024 + 1) + original;
        controlled.AlterExport = value => value.Settings[key] = oversized;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => preferences.ReadAsync(Token));
        Assert.Contains("serialized size", error.Message);
        Assert.Equal(original, (await actual.ExportAsync(Token)).Settings[key]);
    }

    [Fact]
    public async Task Held_setup_read_rechecks_the_original_profile_before_returning_private_choices()
    {
        using var files = new BuildStartTestFiles();
        var actual = new VersionedAtomicSettingsStore(files);
        var initial = new BuildProjectStartPreferences(actual, "profile", Admit);
        Assert.True((await initial.SaveAsync(await initial.ReadAsync(Token), new(0, [Draft()], []), Token)).Saved);
        var held = new BuildStartControlledSettings(actual) { HoldRead = true };
        var allowed = true;
        Task Current(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!allowed) throw new UnauthorizedAccessException("The original profile changed."); return Task.CompletedTask; }
        var preferences = new BuildProjectStartPreferences(held, "profile", Current);
        var read = preferences.ReadAsync(Token);
        try
        {
            await held.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            allowed = false; held.ReadRelease.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => read);
        }
        finally { held.ReadRelease.TrySetResult(); }
        Assert.Single((await initial.ReadAsync(Token)).List.Drafts);
    }

    [Fact]
    public async Task Profile_revocation_during_a_held_save_is_checked_under_the_actual_commit_lease()
    {
        using var files = new BuildStartTestFiles();
        var actual = new VersionedAtomicSettingsStore(files);
        var held = new BuildStartControlledSettings(actual) { HoldCommit = true };
        var allowed = true;
        Task Current(CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!allowed) throw new UnauthorizedAccessException("The original profile changed."); return Task.CompletedTask; }
        var preferences = new BuildProjectStartPreferences(held, "profile", Current);
        var before = await preferences.ReadAsync(Token);
        var save = preferences.SaveAsync(before, new(0, [Draft()], []), Token);
        try
        {
            await held.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            allowed = false; held.CommitRelease.TrySetResult();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => save);
        }
        finally { held.CommitRelease.TrySetResult(); }
        Assert.Empty((await actual.ExportAsync(Token)).Settings);
    }

    [Fact]
    public async Task Recent_retries_only_metadata_CAS_and_keeps_concurrent_pins_drafts_and_the_exact_root_identity()
    {
        using var files = new BuildStartTestFiles();
        var actual = new VersionedAtomicSettingsStore(files);
        var controlled = new BuildStartControlledSettings(actual);
        var preferences = new BuildProjectStartPreferences(controlled, "profile", Admit);
        var identity = Manual(); var draft = Draft();
        controlled.BeforeCommit = async (key, _, token) =>
        {
            if (controlled.CommitCalls == 1)
                await actual.SetAsync(key, new BuildProjectStartState(1, [draft], [new(identity, true, null)]), token);
        };
        var result = await preferences.RecordOpenedAsync(identity, Token);
        Assert.Equal(2, controlled.CommitCalls);
        Assert.Equal(draft, Assert.Single(result.List.Drafts));
        var entry = Assert.Single(result.List.Projects);
        Assert.Equal(identity, entry.Identity); Assert.True(entry.Pinned); Assert.NotNull(entry.LastOpenedAt);
        var otherRoot = identity with { RootId = Guid.NewGuid() };
        var second = await preferences.RecordOpenedAsync(otherRoot, Token);
        Assert.Equal(2, second.List.Projects.Count);
        Assert.Contains(second.List.Projects, item => item.Identity == identity && item.Pinned);
    }

    [Fact]
    public async Task Recent_conflict_retry_has_a_fixed_bound_and_reports_that_the_project_already_opened()
    {
        using var files = new BuildStartTestFiles();
        var actual = new VersionedAtomicSettingsStore(files);
        var controlled = new BuildStartControlledSettings(actual);
        var preferences = new BuildProjectStartPreferences(controlled, "profile", Admit);
        var draft = Draft();
        controlled.BeforeCommit = (key, _, token) => actual.SetAsync(key,
            new BuildProjectStartState(controlled.CommitCalls, [draft], []), token);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => preferences.RecordOpenedAsync(Manual(), Token));
        Assert.Contains("project opened", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, controlled.CommitCalls);
        Assert.Equal(draft, Assert.Single((await preferences.ReadAsync(Token)).List.Drafts));
        Assert.Empty((await preferences.ReadAsync(Token)).List.Projects);
    }

    [Fact]
    public void Project_metadata_identity_keeps_roots_repositories_and_task_owners_distinct_across_revision_updates()
    {
        var reference = new DeveloperProjectReference(Guid.NewGuid(), 1, Guid.NewGuid(), 2, Guid.NewGuid(), "original-repository");
        var identity = BuildProjectStartIdentity.ForManual(reference);
        Assert.Equal(identity, BuildProjectStartIdentity.ForManual(reference with { WorkspaceRevision = 8, ProjectRevision = 9 }));
        Assert.NotEqual(identity, BuildProjectStartIdentity.ForManual(reference with { RootId = Guid.NewGuid() }));
        Assert.NotEqual(identity, BuildProjectStartIdentity.ForManual(reference with { RepositoryBindingId = "other-repository" }));
        var space = Guid.NewGuid(); var context = Guid.NewGuid();
        Assert.NotEqual(identity, BuildProjectStartIdentity.ForTask(space, context, reference));
        Assert.NotEqual(BuildProjectStartIdentity.ForTask(space, context, reference), BuildProjectStartIdentity.ForTask(Guid.NewGuid(), context, reference));
    }

    private static BuildSetupDraft Draft()
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), "Uncreated setup", "new", "unprobed/destination", "installed.provider", "exact-template", now, now);
    }
    private static BuildProjectStartIdentity Manual() => BuildProjectStartIdentity.ForManual(
        new(Guid.NewGuid(), 1, Guid.NewGuid(), 1, Guid.NewGuid(), "canonical-repository"));
}

internal sealed class BuildStartTestFiles : IAppPaths, IDisposable
{
    public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-build-start-" + Guid.NewGuid().ToString("N"));
    internal BuildStartTestFiles() => Directory.CreateDirectory(DataDirectory);
    public string DatabasePath => Path.Combine(DataDirectory, "state.db");
    public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
    public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
    public void Dispose() => Directory.Delete(DataDirectory, true);
}

internal sealed class BuildStartControlledSettings(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
{
    internal bool HoldRead, HoldCommit;
    internal int CommitCalls, ReadCalls;
    internal int? HoldReadNumber;
    internal IOException? CommitFailure;
    internal Action<SettingsExportManifest>? AlterExport;
    internal Func<string, string?, CancellationToken, Task>? BeforeCommit;
    internal TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource ReadRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CommitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CommitRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => actual.GetAsync<T>(key, token);
    public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
    public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
    public async Task<SettingsExportManifest> ExportAsync(CancellationToken token)
    {
        var call = Interlocked.Increment(ref ReadCalls);
        var result = await actual.ExportAsync(token);
        AlterExport?.Invoke(result);
        if (HoldRead && (HoldReadNumber is null || HoldReadNumber == call)) { ReadEntered.TrySetResult(); await ReadRelease.Task; }
        return result;
    }
    public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => actual.ImportAsync(manifest, token);
    public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token) =>
        actual.CompareExchangeAsync(key, expected, replacement, token);
    public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
        IReadOnlyDictionary<string, string?> guards, CancellationToken token) => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
    public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
        IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
    {
        CommitCalls++; CommitEntered.TrySetResult();
        if (HoldCommit) await CommitRelease.Task; // Actual owned task deliberately outlives caller cancellation.
        if (CommitFailure is { } error) throw error;
        if (BeforeCommit is { } before) await before(key, replacement, token);
        return await actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token);
    }
}
