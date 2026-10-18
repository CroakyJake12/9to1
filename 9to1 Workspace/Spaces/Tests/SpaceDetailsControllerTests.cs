using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Spaces.Builder;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class SpaceDetailsControllerTests
{
    [Fact]
    public async Task Save_patches_only_four_details_and_reopens_the_same_canonical_record()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var store = files.OpenStore();
        await store.SetAsync("unrelated.fixture", new Marker("keep"), token);
        var registry = new SpaceRegistry(store);
        var parent = await registry.CreateAsync("Parent", cancellationToken: token);
        var original = await registry.CreateAsync("Original", "Before", parent.Id, token);
        original = await registry.UpdateAsync(original with
        {
            Instructions = "Old instructions", ModelName = "configured-model",
            ModelPolicy = new("configured-model", ["text"], "configured-provider"),
            ContextPolicy = new(SpaceWebAccessPolicy.Disabled, true, false, 4),
            MemoryPolicy = new([SpaceMemoryScope.Conversation], SpaceMemoryScope.Conversation),
            CapabilityBindings = [new("plugin-id", SpaceCapabilityKind.Plugin, SpaceCapabilityOverride.Disabled, "v1")],
            ContextReferences = [new(Guid.NewGuid(), SpaceContextReferenceKind.File, "files", "canonical-file", "revision-3",
                SpaceContextPermission.Read, SpaceContextIndexState.Ready, false, DateTimeOffset.UtcNow)],
            Shares = [new("fixture-principal", SpaceShareRole.Viewer, DateTimeOffset.UtcNow)],
            LayoutDocument = new([new(Guid.NewGuid(), "Harness", "Preserved authored node")], []),
            GeneratedSurface = new("custom", "{}"),
            Files = [new(Path.Combine(files.DataDirectory, "source.txt"), "source.txt", SpaceFilePermission.ReadOnly, DateTimeOffset.UtcNow)],
            ExamplePairs = [new("Example question", "Example response")]
        }, original.Revision, token);
        var ids = (await registry.GetAllAsync(cancellationToken: token)).Select(space => space.Id).Order().ToArray();
        var editor = new SpaceDetailsController(registry, original.Id);
        Assert.True(await editor.LoadAsync(token));
        Assert.True(editor.SetField(SpaceDetailsField.Name, "  Updated  "));
        Assert.True(editor.SetField(SpaceDetailsField.Description, "New purpose"));
        Assert.True(editor.SetField(SpaceDetailsField.IconKey, "book"));
        Assert.True(editor.SetField(SpaceDetailsField.Instructions, "Use my sources.\nKeep original evidence."));
        Assert.True(await editor.SaveAsync(token));
        Assert.False(editor.State.HasChanges);
        Assert.Equal("Updated", editor.State.Draft.Name);

        var reopened = files.OpenRegistry();
        var saved = (await reopened.GetAsync(original.Id, token))!;
        Assert.Equal(original.Revision + 1, saved.Revision);
        Assert.Equal(editor.State.LoadedRevision, saved.Revision);
        Assert.Equal("New purpose", saved.Description);
        Assert.Equal("book", saved.IconKey);
        Assert.Equal("Use my sources.\nKeep original evidence.", saved.Instructions);
        var unexposed = saved with { Name = original.Name, Description = original.Description, IconKey = original.IconKey,
            Instructions = original.Instructions, Revision = original.Revision, UpdatedAt = original.UpdatedAt };
        Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(unexposed));
        Assert.Equal(ids, (await reopened.GetAllAsync(cancellationToken: token)).Select(space => space.Id).Order());
        Assert.Equal("keep", (await files.OpenStore().GetAsync<Marker>("unrelated.fixture", token))!.Value);
        var second = new SpaceDetailsController(reopened, original.Id);
        Assert.True(await second.LoadAsync(token));
        Assert.Equal(editor.State.Draft, second.State.Draft);
        Assert.False(File.Exists(files.DatabasePath)); // No conversation/SQLite owner is involved.
    }

    [Fact]
    public async Task Stale_editor_retains_draft_and_first_save_until_explicit_discard_and_reload()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        var first = new SpaceDetailsController(files.OpenRegistry(), space.Id);
        var second = new SpaceDetailsController(files.OpenRegistry(), space.Id);
        Assert.True(await first.LoadAsync(token));
        Assert.True(await second.LoadAsync(token));
        first.SetField(SpaceDetailsField.Name, "First saved");
        Assert.True(await first.SaveAsync(token));
        second.SetField(SpaceDetailsField.Name, "Second draft");
        second.SetField(SpaceDetailsField.Instructions, "Keep this unsaved instruction");
        var draft = second.State.Draft;
        Assert.False(await second.SaveAsync(token));
        Assert.True(second.State.HasConflict);
        Assert.True(second.State.HasChanges);
        Assert.Equal(draft, second.State.Draft);
        Assert.Equal("First saved", (await files.OpenRegistry().GetAsync(space.Id, token))!.Name);
        Assert.False(await second.LoadAsync(token));
        Assert.Equal(draft, second.State.Draft);
        Assert.True(await second.DiscardChangesAsync(token));
        Assert.Equal(first.State.Draft, second.State.Draft);
        Assert.False(second.State.HasChanges);
        Assert.False(second.State.HasConflict);
    }

    [Fact]
    public async Task Protected_unavailable_and_invalid_records_never_report_a_save()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var registry = files.OpenRegistry();
        var builtin = new SpaceDetailsController(registry, SpaceRegistry.ChatSpaceId);
        Assert.True(await builtin.LoadAsync(token));
        Assert.True(builtin.State.IsReadOnly);
        Assert.False(builtin.SetField(SpaceDetailsField.Name, "Replace built-in"));
        Assert.False(await builtin.SaveAsync(token));
        var missing = new SpaceDetailsController(registry, Guid.NewGuid());
        Assert.False(await missing.LoadAsync(token));
        Assert.False(missing.State.CanEdit);
        var archived = await registry.CreateAsync("Archived", cancellationToken: token);
        await registry.SetArchivedAsync(archived.Id, true, token);
        var unavailable = new SpaceDetailsController(registry, archived.Id);
        Assert.False(await unavailable.LoadAsync(token));
        Assert.False(await unavailable.SaveAsync(token));
        var custom = await registry.CreateAsync("Valid", cancellationToken: token);
        var editor = new SpaceDetailsController(registry, custom.Id);
        Assert.True(await editor.LoadAsync(token));
        editor.SetField(SpaceDetailsField.Name, " ");
        Assert.False(await editor.SaveAsync(token));
        Assert.True(editor.State.HasChanges);
        Assert.False(editor.State.IsBusy);
        Assert.Contains("Could not save", editor.State.StatusMessage);
        Assert.Equal("Valid", (await files.OpenRegistry().GetAsync(custom.Id, token))!.Name);
    }

    [Fact]
    public async Task Actual_source_failures_keep_draft_and_fault_the_operation()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        var source = new ControlledDetailsSettings(files.OpenStore());
        var editor = new SpaceDetailsController(new SpaceRegistry(source), space.Id);
        Assert.True(await editor.LoadAsync(token));
        editor.SetField(SpaceDetailsField.Description, "Unsaved description");
        var draft = editor.State.Draft;
        var failure = new IOException("Fixture read unavailable");
        source.FailNextExport = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => editor.DiscardChangesAsync(token)));
        Assert.Equal(draft, editor.State.Draft);
        Assert.True(editor.State.HasChanges);
        Assert.False(editor.State.IsBusy);
        source.FailNextExport = failure;
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => editor.SaveAsync(token)));
        Assert.Equal(draft, editor.State.Draft);
        Assert.False(editor.State.IsBusy);
        Assert.Equal("", (await files.OpenRegistry().GetAsync(space.Id, token))!.Description);
    }

    [Fact]
    public async Task New_edits_during_actual_held_commit_remain_unsaved_and_duplicate_commands_are_refused()
    {
        using var files = new SpaceDetailsTestFiles();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        var space = await files.OpenRegistry().CreateAsync("Original", cancellationToken: token);
        var source = new ControlledDetailsSettings(files.OpenStore());
        var editor = new SpaceDetailsController(new SpaceRegistry(source), space.Id);
        Assert.True(await editor.LoadAsync(token));
        editor.SetField(SpaceDetailsField.Instructions, "Captured for this save");
        source.HoldNextCommit = true;
        var save = editor.SaveAsync(token);
        try
        {
            await source.CommitStarted.Task.WaitAsync(token);
            Assert.True(editor.State.IsSaving);
            Assert.True(editor.SetField(SpaceDetailsField.Instructions, "Newer unsaved instructions"));
            Assert.False(await editor.SaveAsync(token));
            Assert.False(await editor.DiscardChangesAsync(token));
            source.ReleaseCommit.TrySetResult();
            Assert.True(await save);
            Assert.Equal("Captured for this save", (await files.OpenRegistry().GetAsync(space.Id, token))!.Instructions);
            Assert.Equal("Newer unsaved instructions", editor.State.Draft.Instructions);
            Assert.True(editor.State.HasChanges);
            Assert.True(await editor.SaveAsync(token));
            Assert.False(editor.State.HasChanges);
            Assert.Equal("Newer unsaved instructions", (await files.OpenRegistry().GetAsync(space.Id, token))!.Instructions);
        }
        finally { source.ReleaseCommit.TrySetResult(); await save; }
    }

    private sealed record Marker(string Value);
}

internal sealed class SpaceDetailsTestFiles : IAppPaths, IDisposable
{
    public SpaceDetailsTestFiles()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "9to1-space-details-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(DataDirectory);
        else Directory.CreateDirectory(DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    public string DataDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
    public string BrowserProfileDirectory => Path.Combine(DataDirectory, "unused-browser");
    public string AttachmentsDirectory => Path.Combine(DataDirectory, "unused-attachments");
    public string LogsDirectory => Path.Combine(DataDirectory, "unused-logs");
    public string LegacyStatePath => Path.Combine(DataDirectory, "unused-legacy");
    public VersionedAtomicSettingsStore OpenStore() => new(this);
    public SpaceRegistry OpenRegistry() => new(OpenStore());
    public void Dispose() => Directory.Delete(DataDirectory, true);
}

// A transparent production-file-store wrapper, controlling only a concrete source
// failure or one real compare/exchange entry. No registry/configuration replica.
internal sealed class ControlledDetailsSettings(VersionedAtomicSettingsStore inner) : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
{
    public IOException? FailNextExport { get; set; }
    public bool HoldNextCommit { get; set; }
    public TaskCompletionSource CommitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource ReleaseCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
    public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => inner.SetAsync(key, value, token);
    public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
    public Task<SettingsExportManifest> ExportAsync(CancellationToken token)
    {
        if (FailNextExport is { } failure) { FailNextExport = null; return Task.FromException<SettingsExportManifest>(failure); }
        return inner.ExportAsync(token);
    }
    public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => inner.ImportAsync(value, token);
    public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token) =>
        inner.CompareExchangeAsync(key, expected, replacement, token);
    public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
        IReadOnlyDictionary<string, string?> guards, CancellationToken token)
    {
        if (HoldNextCommit && key == "spaces.registry")
        {
            HoldNextCommit = false; CommitStarted.TrySetResult();
            await ReleaseCommit.Task.WaitAsync(token);
        }
        return await inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
    }
    public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
        IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token) =>
        inner.CompareExchangeGuardedAsync(key, expected, replacement, guards, admission, token);
}
