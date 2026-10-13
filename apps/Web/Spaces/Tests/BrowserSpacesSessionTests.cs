using System.Collections;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Web.Spaces;
using NineToOne.Web.Spaces.Storage;
using Xunit;

namespace NineToOne.Web.Tests;

public sealed class BrowserSpacesSessionTests
{
    [Fact]
    public async Task Create_edit_archive_restore_reopens_the_same_canonical_space_from_real_settings()
    {
        using var files = new RegistryFiles();
        var registry = files.Open();
        await using var view = Session(registry);
        await view.PrepareAsync(default); view.Admit();
        await view.DispatchAsync("spaces.create", null);
        Assert.True(view.TrySetValue("editor.name", "My project"));
        Assert.True(view.TrySetValue("editor.description", "Retained instructions and identity"));
        await view.DispatchAsync("spaces.confirm-edit", null);
        var saved = Assert.Single(await files.Open().GetAllAsync(), space => !space.IsBuiltIn);
        Assert.Equal("My project", saved.Name);
        Assert.False(Value<bool>(view, "editor.visible"));

        await view.DispatchAsync("spaces.rename", saved.Id.ToString("D"));
        Assert.True(view.TrySetValue("editor.name", "Updated project"));
        await view.DispatchAsync("spaces.confirm-edit", null);
        var edited = (await files.Open().GetAsync(saved.Id))!;
        Assert.Equal("Updated project", edited.Name);
        Assert.Equal(saved.CreatedAt, edited.CreatedAt);
        Assert.Equal(saved.Revision + 1, edited.Revision);
        await view.DispatchAsync("spaces.archive", saved.Id.ToString("D"));
        Assert.True((await files.Open().GetAsync(saved.Id))!.IsArchived);
        Assert.Empty(Rows(view, "spaces.customSpaces"));
        Assert.True(view.TrySetValue("spaces.showArchived", true));
        Assert.Single(Rows(view, "spaces.customSpaces"));
        await view.DispatchAsync("spaces.restore", saved.Id.ToString("D"));
        Assert.False((await files.Open().GetAsync(saved.Id))!.IsArchived);
        Assert.Equal(saved.Id.ToString("D"), Value<string>(view, "selection.id"));
    }

    [Fact]
    public async Task Draft_survives_presentation_deactivation_and_conflicting_revision_keeps_saved_record()
    {
        using var files = new RegistryFiles();
        var registry = files.Open(); var saved = await registry.CreateAsync("Original");
        await using var view = Session(registry);
        await view.PrepareAsync(default); view.Admit();
        await view.DispatchAsync("spaces.rename", saved.Id.ToString("D"));
        view.TrySetValue("editor.name", "My unsaved draft");
        view.Deactivate(); await view.PrepareAsync(default); view.Admit();
        Assert.Equal("My unsaved draft", Value<string>(view, "editor.name"));
        await files.Open().UpdateAsync(saved with { Name = "Another tab" }, saved.Revision);
        await view.DispatchAsync("spaces.confirm-edit", null);
        Assert.Equal("Another tab", (await files.Open().GetAsync(saved.Id))!.Name);
        Assert.Equal("My unsaved draft", Value<string>(view, "editor.name"));
        Assert.True(Value<bool>(view, "editor.visible"));
        Assert.Contains("changed from revision", Value<string>(view, "editor.error"));
    }

    [Fact]
    public async Task Private_revocation_clears_rows_and_draft_and_rejects_late_actions()
    {
        using var files = new RegistryFiles(); var registry = files.Open();
        await using var view = Session(registry);
        await view.PrepareAsync(default); view.Admit();
        await view.DispatchAsync("spaces.create", null); view.TrySetValue("editor.name", "Private draft");
        view.RevokePrivateContext();
        Assert.Empty(Rows(view, "spaces.builtIns"));
        Assert.Equal("", Value<string>(view, "editor.name"));
        Assert.False(view.IsActionAvailable("spaces.confirm-edit"));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await view.DispatchAsync("spaces.confirm-edit", null));
        Assert.DoesNotContain(await files.Open().GetAllAsync(), space => !space.IsBuiltIn);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lost_save_acknowledgement_requires_inspection_before_create_can_be_repeated(bool wrongKindAcknowledgement)
    {
        using var files = new RegistryFiles();
        var transport = new LostReplyStore(new VersionedAtomicSettingsStore(files));
        if (wrongKindAcknowledgement) transport.ReplyFailure = new InvalidOperationException("Controlled wrong-kind JSON acknowledgement after real commit.");
        var registry = new SpaceRegistry(transport);
        var view = Session(registry);
        await view.PrepareAsync(default); view.Admit();
        await view.DispatchAsync("spaces.create", null); view.TrySetValue("editor.name", "Committed once");
        transport.LoseNextReply = true;
        Assert.Same(transport.ReplyFailure, await Assert.ThrowsAnyAsync<Exception>(() => view.DispatchAsync("spaces.confirm-edit", null).AsTask()));
        Assert.False(view.IsActionAvailable("spaces.confirm-edit"));
        view.Deactivate(); await view.PrepareAsync(default); view.Admit();
        Assert.Equal("Committed once", Value<string>(view, "editor.name"));
        Assert.False(view.IsActionAvailable("spaces.confirm-edit"));
        await view.DispatchAsync("spaces.confirm-edit", null);
        Assert.Single(await files.Open().GetAllAsync(), space => !space.IsBuiltIn);
        await view.DispatchAsync("spaces.cancel-edit", null);
        await view.DispatchAsync("spaces.refresh", null);
        Assert.Single(Rows(view, "spaces.customSpaces"));
        Assert.True(view.IsActionAvailable("spaces.create"));
        Assert.Same(transport.ReplyFailure, await Assert.ThrowsAnyAsync<Exception>(() => view.DisposeAsync().AsTask()));
    }

    [Fact]
    public async Task Lost_save_reply_survives_failing_status_and_cleanup_presentation_and_still_fences_replay()
    {
        using var files = new RegistryFiles();
        var store = new LostReplyStore(new VersionedAtomicSettingsStore(files));
        var registry = new SpaceRegistry(store);
        var presentationFailure = new InvalidOperationException("Controlled actual presentation failure");
        var refusePresentation = false;
        var view = new BrowserSpacesSession(() => registry, () => { }, action =>
        {
            if (refusePresentation) return Task.FromException(presentationFailure);
            action(); return Task.CompletedTask;
        });
        await view.PrepareAsync(default); view.Admit();
        await view.DispatchAsync("spaces.create", null); view.TrySetValue("editor.name", "Saved once despite unavailable UI");
        store.AfterLostReply = () => refusePresentation = true;
        store.LoseNextReply = true;
        var failure = await Assert.ThrowsAsync<AggregateException>(() => view.DispatchAsync("spaces.confirm-edit", null).AsTask());
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Contains(failure.InnerExceptions, error => ReferenceEquals(error, store.ReplyFailure));
        Assert.Contains(failure.InnerExceptions, error => ReferenceEquals(error, presentationFailure));
        refusePresentation = false;
        Assert.False(view.IsActionAvailable("spaces.confirm-edit"));
        await view.DispatchAsync("spaces.confirm-edit", null);
        Assert.Single(await files.Open().GetAllAsync(), space => !space.IsBuiltIn);
        var close = await Assert.ThrowsAsync<AggregateException>(() => view.DisposeAsync().AsTask());
        Assert.Equal(2, close.InnerExceptions.Count);
        Assert.Contains(close.InnerExceptions, error => ReferenceEquals(error, store.ReplyFailure));
        Assert.Contains(close.InnerExceptions, error => ReferenceEquals(error, presentationFailure));
    }

    [Fact]
    public async Task Actual_viewport_width_keeps_compact_list_and_details_separately_accessible()
    {
        using var files = new RegistryFiles();
        await using var view = Session(files.Open()); await view.PrepareAsync(default); view.Admit();
        view.SetViewportWidth(390);
        Assert.Equal("0 1fr", Value<string>(view, "layout.columns"));
        Assert.True(Value<bool>(view, "layout.detailsVisible"));
        await view.DispatchAsync("spaces.toggle-sidebar", null);
        Assert.Equal("1fr 0", Value<string>(view, "layout.columns"));
        Assert.False(Value<bool>(view, "layout.detailsVisible"));
        await view.DispatchAsync("spaces.select", SpaceRegistry.ChatSpaceId.ToString("D"));
        Assert.Equal("0 1fr", Value<string>(view, "layout.columns"));
        view.SetViewportWidth(1200);
        Assert.Equal("280 1fr", Value<string>(view, "layout.columns"));
    }

    [Fact]
    public async Task No_signed_profile_gives_setup_state_without_a_substitute_registry_or_working_workspace_actions()
    {
        await using var view = new BrowserSpacesSession(
            () => throw new BrowserTaskContextUnavailableException("Controlled absence of the actual signed owner."),
            () => { }, action => { action(); return Task.CompletedTask; });
        await view.PrepareAsync(default); view.Admit();
        Assert.Empty(Rows(view, "spaces.builtIns"));
        Assert.Contains("Sign in", Value<string>(view, "status.message"));
        Assert.False(view.IsActionAvailable("spaces.create"));
        Assert.False(view.IsActionAvailable("spaces.open"));
        Assert.True(view.IsActionAvailable("spaces.refresh"));
    }

    [Fact]
    public async Task Normal_feature_admission_keeps_cached_draft_and_rejects_unsupported_or_superseded_targets()
    {
        using var files = new RegistryFiles(); var registry = files.Open();
        await using var feature = new BrowserSpacesFeature(() => registry, () => { }, action => { action(); return Task.CompletedTask; }, _ => { });
        var request = new HomeFeatureNavigationRequest(HomeFeatureRouteIds.Spaces, Action: "manage");
        var original = await feature.OpenAsync(request); Assert.True(original.Succeeded);
        var first = feature.Render(original.ViewState!); first.Admission!.Accept();
        var view = Assert.IsType<BrowserSpacesSession>(first.Bindings);
        await view.DispatchAsync("spaces.create", null); view.TrySetValue("editor.name", "Retained through Home");
        first.Lifetime!.Dispose();
        var reopened = await feature.OpenAsync(request);
        var second = feature.Render(reopened.ViewState!); second.Admission!.Accept();
        Assert.Same(first.Bindings, second.Bindings);
        Assert.Equal("Retained through Home", Value<string>(view, "editor.name"));
        var unsupported = await feature.OpenAsync(request with { EntityId = Guid.NewGuid().ToString("D") });
        Assert.False(unsupported.Succeeded);
        Assert.Equal("HomeFeatureTargetUnsupported", unsupported.Code);
        var superseded = await feature.OpenAsync(request);
        await feature.OpenAsync(request);
        Assert.Throws<InvalidOperationException>(() => feature.Render(superseded.ViewState!));
        feature.RevokePrivateContext();
        Assert.Empty(Rows(view, "spaces.builtIns"));
    }

    [Fact]
    public async Task Feature_close_retains_each_original_session_and_factory_fault_exactly_once()
    {
        var first = new IOException("Controlled manager read failure");
        var second = new InvalidDataException("Controlled builder read failure");
        var attempts = 0;
        var feature = new BrowserSpacesFeature(() => throw (++attempts == 1 ? first : second),
            () => { }, action => { action(); return Task.CompletedTask; }, _ => { });
        Assert.Same(first, await Assert.ThrowsAsync<IOException>(() => feature.OpenAsync(new(HomeFeatureRouteIds.Spaces, Action: "manage"))));
        Assert.Same(second, await Assert.ThrowsAsync<InvalidDataException>(() => feature.OpenAsync(
            new(HomeFeatureRouteIds.Spaces, "Space", Guid.NewGuid().ToString("D"), "edit"))));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => feature.DisposeAsync().AsTask());
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Contains(failure.InnerExceptions, error => ReferenceEquals(error, first));
        Assert.Contains(failure.InnerExceptions, error => ReferenceEquals(error, second));
        Assert.Same(failure, await Assert.ThrowsAsync<AggregateException>(() => feature.DisposeAsync().AsTask()));
    }

    [Fact]
    public async Task Session_close_joins_pending_registry_read_after_cancel_callback_throws_and_retains_both_causes()
    {
        var store = new HeldReadStore();
        var view = Session(new SpaceRegistry(store));
        var prepare = view.PrepareAsync(default);
        await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closing = view.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        store.Release.TrySetResult();
        Assert.Same(store.ReadFailure, await Assert.ThrowsAsync<IOException>(() => prepare));
        var failure = await Assert.ThrowsAsync<AggregateException>(() => closing);
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.Contains(failure.InnerExceptions, error => ReferenceEquals(error, store.CancelFailure));
        Assert.Contains(failure.InnerExceptions, error => ReferenceEquals(error, store.ReadFailure));
    }

    [Fact]
    public async Task Drain_preserves_faulted_cancellation_and_joins_later_sources_after_synchronous_failure()
    {
        var original = new OperationCanceledException("A faulted original is not a cancelled task");
        var later = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoked = false;
        var drain = BrowserSpacesDrain.CompleteAsync([
            () => throw original,
            () => Task.FromException(original),
            () => { invoked = true; return later.Task; },
            () => Task.FromCanceled(new CancellationToken(true))
        ]);
        Assert.True(invoked); Assert.False(drain.IsCompleted);
        later.SetResult();
        Assert.Same(original, await Assert.ThrowsAsync<OperationCanceledException>(() => drain));
        Assert.True(drain.IsFaulted); Assert.False(drain.IsCanceled);
        Assert.Same(original, Assert.Single(drain.Exception!.InnerExceptions));
    }

    private static BrowserSpacesSession Session(SpaceRegistry registry) => new(() => registry, () => { }, action => { action(); return Task.CompletedTask; });
    private static T Value<T>(BrowserSpacesSession view, string path)
    { Assert.True(view.TryGetValue(path, out var value)); return Assert.IsType<T>(value); }
    private static object[] Rows(BrowserSpacesSession view, string path)
    { Assert.True(view.TryGetValue(path, out var value)); return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object>().ToArray(); }

    private sealed class RegistryFiles : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-browser-spaces-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public RegistryFiles() => Directory.CreateDirectory(DataDirectory);
        public SpaceRegistry Open() => new(new VersionedAtomicSettingsStore(this));
        public void Dispose() => Directory.Delete(DataDirectory, recursive: true);
    }
    // Real versioned file commit, with an explicitly controlled lost receipt afterward.
    // This proves UI replay prevention; browser transport authority is tested separately.
    private sealed class LostReplyStore(IVersionedSettingsStore actual) : IVersionedSettingsStore
    {
        public bool LoseNextReply { get; set; }
        public Exception ReplyFailure { get; set; } = new IOException("Controlled lost acknowledgement after real commit.");
        public Action? AfterLostReply { get; set; }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => actual.GetAsync<T>(key, token);
        public async Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        {
            await actual.SetAsync(key, value, token);
            if (LoseNextReply) { LoseNextReply = false; AfterLostReply?.Invoke(); throw ReplyFailure; }
        }
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => actual.ImportAsync(manifest, token);
    }

    private sealed class HeldReadStore : IVersionedSettingsStore
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception CancelFailure { get; } = new InvalidOperationException("Controlled original cancellation callback failure");
        public IOException ReadFailure { get; } = new("Controlled original read failure after cancellation");
        public async Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        {
            using var registration = token.Register(() => throw CancelFailure);
            Started.TrySetResult();
            await Release.Task;
            throw ReadFailure;
        }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }
}
