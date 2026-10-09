using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;

namespace Haven.Desktop.Views.Pages.Spaces;

/// <summary>Platform host for the Haven-native Spaces picker and editor.</summary>
public sealed class NativeSpacesPage : UserControl, IActivatablePage, IDisposable, IAsyncDisposable,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly SpaceRegistry _registry;
    private readonly IConversationRepository? _conversations;
    private readonly Func<Conversation, Task>? _openConversation;
    private readonly Func<SpaceDefinition, Task>? _launchSpace;
    private readonly Func<Guid, Task>? _deleteSpace;
    private readonly Func<SpaceDefinition, Task>? _manageLayout;
    private readonly SpaceGeneratedSurfaceRenderer? _generatedSurfaceRenderer;
    private readonly SpaceEditPlanner? _editPlanner;
    private readonly SpacesHavenScene _scene;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly List<SpaceGeneratedSurfaceMount> _previewMounts = [];
    private readonly object _previewGate = new();
    [ThreadStatic] private static List<NativeSpacesPage>? _physicalSources;
    private long _generation;
    private long _refreshEpoch;
    private volatile bool _active = true;
    private Task? _latestAction;
    private SpaceGeneratedSurfaceMount? _generatedSurfaceMount;
    private CancellationTokenSource? _refreshCancellation;
    private IReadOnlyList<SpaceDefinition> _spaces = [];
    private Guid? _selectedId;

    public NativeSpacesPage(
        SpaceRegistry registry,
        Func<SpaceDefinition, Task>? launchSpace = null,
        Func<SpaceDefinition, Task>? manageLayout = null,
        IConversationRepository? conversations = null,
        Func<Conversation, Task>? openConversation = null)
        : this(registry, null, null, launchSpace, null, manageLayout, conversations, openConversation)
    {
    }

    internal NativeSpacesPage(
        SpaceRegistry registry,
        SpaceGeneratedSurfaceRenderer? generatedSurfaceRenderer,
        SpaceEditPlanner? editPlanner,
        Func<SpaceDefinition, Task>? launchSpace = null,
        Func<Guid, Task>? deleteSpace = null,
        Func<SpaceDefinition, Task>? manageLayout = null,
        IConversationRepository? conversations = null,
        Func<Conversation, Task>? openConversation = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _conversations = conversations;
        _openConversation = openConversation;
        _generatedSurfaceRenderer = generatedSurfaceRenderer;
        _editPlanner = editPlanner;
        _launchSpace = launchSpace;
        _deleteSpace = deleteSpace;
        _manageLayout = manageLayout;
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        _scene = new SpacesHavenScene(OwnOriginalSceneCallback, DemandOriginalSceneWrite);
        OwnOriginalSceneCallback(() =>
        {
            _scene.SetLaunchAvailable(_launchSpace is not null);
            _scene.SetLayoutEditorAvailable(_manageLayout is not null);
            _scene.SetEditWithHavenAvailable(_editPlanner is not null);
        });
        Scene = new HavenSceneControl { Root = _scene.Root };
        AutomationProperties.SetAutomationId(this, "HavenNativeSpacesPage");
        AutomationProperties.SetName(this, "Haven Spaces");
        AutomationProperties.SetAutomationId(Scene, "HavenNativeSpacesScene");
        AutomationProperties.SetName(Scene, "Spaces picker and editor");
        Content = Scene;
        SizeChanged += OnSizeChanged;

        _scene.CreateRequested += OnCreateRequested;
        _scene.ArchivedVisibilityChanged += OnArchivedVisibilityChanged;
        _scene.SpaceSelected += OnSpaceSelected;
        _scene.ConversationSelected += OnConversationSelected;
        _scene.NewConversationRequested += OnNewConversationRequested;
        _scene.SaveRequested += OnSaveRequested;
        _scene.LaunchRequested += OnLaunchRequested;
        _scene.ForkRequested += OnForkRequested;
        _scene.ArchiveRequested += OnArchiveRequested;
        _scene.DeleteRequested += OnDeleteRequested;
        _scene.AddFileRequested += OnAddFileRequested;
        _scene.RemoveFileRequested += OnRemoveFileRequested;
        _scene.ManageLayoutRequested += OnManageLayoutRequested;
        _scene.EditWithHavenRequested += OnEditWithHavenRequested;
    }

    public HavenSceneControl Scene { get; }

    internal SpacesHavenScene OriginalScene => _scene;
    internal Task? LatestOriginalAction => _latestAction;

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        _work.DemandAdmission();
        _active = true;
        Interlocked.Increment(ref _generation);
        return RunActionAsync(() => RefreshAsync(cancellationToken), "refresh Spaces");
    }

    public void Deactivate()
    {
        _active = false;
        Interlocked.Increment(ref _generation);
        Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();
    }

    internal Task RefreshNowAsync(CancellationToken cancellationToken = default) =>
        RunActionAsync(() => RefreshAsync(cancellationToken), "refresh Spaces");

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_work.IsRetiring || !_active) return;
        OwnOriginalSceneCallback(() => _scene.SetCompactLayout(e.NewSize.Width > 0 && e.NewSize.Width < 760d));
    }

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return;
        var original = _work.Executing ?? throw new InvalidOperationException("No original owns this Spaces refresh.");
        var refresh = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
        var previous = Interlocked.Exchange(ref _refreshCancellation, refresh);
        previous?.Cancel();
        var token = refresh.Token;
        var epoch = Interlocked.Increment(ref _refreshEpoch);
        bool IsCurrentRefresh() => !token.IsCancellationRequested && Volatile.Read(ref _refreshEpoch) == epoch;
        try
        {
            var spaces = await ObserveAsync(() => _registry.GetAllAsync(_scene.IncludeArchived, token)).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await PublishAsync(() => { if (IsCurrentRefresh()) ApplySpaces(spaces); });
            await RefreshConversationsAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The original owner retains an actual canceled read; cancellation is not drain proof.
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            original.Retain(exception);
            await PublishAsync(() => { if (IsCurrentRefresh()) _scene.SetStatus($"Spaces could not refresh: {exception.Message}"); });
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _refreshCancellation, null, refresh), refresh)) refresh.Dispose();
            else refresh.Dispose();
        }
    }

    private async Task RefreshConversationsAsync(CancellationToken cancellationToken = default)
    {
        if (_conversations is null || _selectedId is not { } spaceId)
        {
            await PublishAsync(() => { if (!cancellationToken.IsCancellationRequested) _scene.SetConversations([]); });
            return;
        }

        var conversations = await ObserveAsync(() => _conversations.GetBySpaceAsync(spaceId, 500, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await PublishAsync(() => { if (!cancellationToken.IsCancellationRequested && _selectedId == spaceId) _scene.SetConversations(conversations); });
    }
    private void ApplySpaces(IReadOnlyList<SpaceDefinition> spaces)
    {
        _spaces = spaces;
        if (_selectedId is not { } selected || spaces.All(space => space.Id != selected))
            _selectedId = spaces.FirstOrDefault(space => !space.IsArchived)?.Id ?? spaces.FirstOrDefault()?.Id;
        _scene.SetSpaces(spaces, _selectedId);
        var current = CurrentSpace();
        _scene.SetSpace(current);
        RefreshGeneratedPreview(current);
        _scene.SetStatus(null);
    }

    private async void OnCreateRequested(object? sender, EventArgs e)
    {
        await RunMutationAsync(async () =>
        {
            var all = await ObserveAsync(() => _registry.GetAllAsync(includeArchived: true, CancellationToken.None));
            var names = all.Select(space => space.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = "New Space";
            for (var suffix = 2; names.Contains(name); suffix++) name = $"New Space {suffix}";
            var created = await ObserveAsync(() => _registry.CreateAsync(name, "Describe what you want this Space to help with.", CancellationToken.None));
            _selectedId = created.Id;
            await RefreshAsync();
        }, "create Space");
    }

    private async void OnArchivedVisibilityChanged(object? sender, bool includeArchived) =>
        await RunActionAsync(() => RefreshAsync(), "refresh Spaces");

    private async void OnSpaceSelected(object? sender, Guid id)
    {
        await RunActionAsync(async () =>
        {
            _selectedId = id;
            _scene.SetSpaces(_spaces, id);
            var current = CurrentSpace();
            _scene.SetSpace(current);
            RefreshGeneratedPreview(current);
            _scene.SetStatus(null);
            await RefreshConversationsAsync();
        }, "refresh Space chats");
    }

    private async void OnConversationSelected(object? sender, Guid id)
    {
        if (_conversations is null || _openConversation is null) return;
        await RunMutationAsync(async () =>
        {
            var conversation = await ObserveAsync(() => _conversations.GetAsync(id, CancellationToken.None));
            if (conversation is null) { await RefreshConversationsAsync(); return; }
            await ObserveAsync(() => _openConversation(conversation));
        }, "open Space chat");
    }

    private async void OnNewConversationRequested(object? sender, Guid id)
    {
        var space = _spaces.FirstOrDefault(candidate => candidate.Id == id);
        if (space is null || _launchSpace is null || space.IsArchived) return;
        await RunMutationAsync(async () =>
        {
            await ObserveAsync(() => _launchSpace(space));
            await RefreshConversationsAsync();
        }, "start Space chat");
    }

    private async void OnSaveRequested(object? sender, SpaceEditorDraft draft)
    {
        var current = CurrentSpace();
        if (current is null) return;
        if (draft.GeneratedSurface is { } generated)
        {
            try { _ = SpaceGeneratedSurfaceRenderer.ParseInputs(generated.InputsJson); }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                _scene.SetStatus($"Generated surface inputs are invalid: {exception.Message}");
                return;
            }
        }

        await RunMutationAsync(async () =>
        {
            var updated = current with
            {
                Name = draft.Name,
                Description = draft.Description,
                ModelName = draft.ModelName,
                Instructions = draft.Instructions,
                ThinkingMode = draft.ThinkingMode,
                ExamplePairs = draft.ExamplePairs,
                GeneratedSurface = draft.GeneratedSurface
            };
            await ObserveAsync(() => _registry.UpdateAsync(updated, CancellationToken.None));
            await RefreshAsync();
            await PublishAsync(() => _scene.SetStatus("Space saved."));
        }, "save Space");
    }

    private async void OnLaunchRequested(object? sender, Guid id)
    {
        if (_launchSpace is null) return;
        var space = _spaces.FirstOrDefault(item => item.Id == id);
        if (space is null) return;
        await RunActionAsync(() => ObserveAsync(() => _launchSpace(space)), "open Space");
    }

    private async void OnForkRequested(object? sender, Guid id)
    {
        await RunMutationAsync(async () =>
        {
            var fork = await ObserveAsync(() => _registry.ForkAsync(id, cancellationToken: CancellationToken.None));
            _selectedId = fork.Id;
            await RefreshAsync();
            await PublishAsync(() => _scene.SetStatus($"Forked as {fork.Name}."));
        }, "fork Space");
    }

    private async void OnArchiveRequested(object? sender, Guid id)
    {
        var current = _spaces.FirstOrDefault(space => space.Id == id);
        if (current is null) return;
        await RunMutationAsync(async () =>
        {
            await ObserveAsync(() => _registry.SetArchivedAsync(id, !current.IsArchived, CancellationToken.None));
            if (!current.IsArchived && !_scene.IncludeArchived) _selectedId = null;
            await RefreshAsync();
        }, current.IsArchived ? "restore Space" : "archive Space");
    }

    private async void OnDeleteRequested(object? sender, Guid id)
    {
        await RunMutationAsync(async () =>
        {
            if (_deleteSpace is not null)
                await ObserveAsync(() => _deleteSpace(id));
            else
            {
                if (_conversations is not null) await ObserveAsync(() => _conversations.DetachSpaceAsync(id, CancellationToken.None));
                await ObserveAsync(() => _registry.DeleteAsync(id, CancellationToken.None));
            }
            if (_selectedId == id) _selectedId = null;
            await RefreshAsync();
        }, "delete Space");
    }

    private async void OnAddFileRequested(object? sender, SpaceFilePermission permission)
        => await RunActionAsync(() => AddFilesAsync(permission), "choose Space files");

    private async Task AddFilesAsync(SpaceFilePermission permission)
    {
        var current = CurrentSpace();
        if (current is null) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null)
        {
            _scene.SetStatus("The platform file picker is unavailable.");
            return;
        }

        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await ObserveAsync(() => storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Add files to {current.Name}",
                AllowMultiple = true
            }));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _work.Executing!.Retain(exception);
            await PublishAsync(() => _scene.SetStatus($"Could not open the file picker: {exception.Message}"));
            return;
        }
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (paths.Length == 0) return;

        await RunMutationAsync(async () =>
        {
            foreach (var path in paths) await ObserveAsync(() => _registry.AddFileAsync(current.Id, path, permission, CancellationToken.None));
            await RefreshAsync();
            await PublishAsync(() => _scene.SetStatus($"Added {paths.Length} file{(paths.Length == 1 ? string.Empty : "s")} as {(permission == SpaceFilePermission.ReadWrite ? "read & write" : "read-only")}.") );
        }, "add files");
    }

    private async void OnRemoveFileRequested(object? sender, string path)
    {
        var current = CurrentSpace();
        if (current is null) return;
        await RunMutationAsync(async () =>
        {
            await ObserveAsync(() => _registry.RemoveFileAsync(current.Id, path, CancellationToken.None));
            await RefreshAsync();
        }, "remove file");
    }

    private async void OnEditWithHavenRequested(object? sender, string instruction)
        => await RunMutationAsync(() => PlanEditAsync(instruction), "plan Space changes");

    private async Task PlanEditAsync(string instruction)
    {
        var current = CurrentSpace();
        if (_editPlanner is null || current is null) return;
        var draftRevision = _scene.DraftRevision;
        var selectionEpoch = _scene.SelectionEpoch;
        var pageGeneration = Volatile.Read(ref _generation);
        var original = _work.Executing!;
        bool IsCurrentDraft() => _selectedId == current.Id && _scene.DraftRevision == draftRevision;
        await PublishAsync(() => _scene.SetStatus("Planning safe Space changes…"));
        try
        {
            var result = await ObserveAsync(() => _editPlanner.PlanAsync(instruction, current, CancellationToken.None));
            if (!result.Succeeded || result.Patch is null)
            {
                await PublishAsync(() => { if (IsCurrentDraft()) _scene.SetStatus(result.Message); });
                return;
            }
            await PublishAsync(() =>
            {
                if (!IsCurrentDraft()) return;
                // Applying a patch raises synchronous invalidation callbacks. A callback
                // may replace the selected draft between setters; every following Write
                // must still belong to this same target, even for an A→B→A replacement.
                original.BindPublicationGuard(() => _active && Volatile.Read(ref _generation) == pageGeneration
                    && _selectedId == current.Id && _scene.SelectionEpoch == selectionEpoch);
                _scene.ApplyEditPatch(result.Patch);
            });
        }
        catch (OperationCanceledException error)
        {
            _work.Executing!.Retain(error);
            await PublishAsync(() => { if (IsCurrentDraft()) _scene.SetStatus("Space edit cancelled."); });
        }
    }

    private async void OnManageLayoutRequested(object? sender, Guid id)
    {
        if (_manageLayout is null) return;
        var space = _spaces.FirstOrDefault(item => item.Id == id);
        if (space is null) return;
        await RunActionAsync(() => ObserveAsync(() => _manageLayout(space)), "open layout editor");
    }

    private void RefreshGeneratedPreview(SpaceDefinition? space)
    {
        _scene.SetGeneratedPreview(null, null);
        _generatedSurfaceMount?.RequestRetirement();
        _generatedSurfaceMount = null;

        if (space?.GeneratedSurface is null) return;
        if (_generatedSurfaceRenderer is null)
        {
            _scene.SetGeneratedPreview(null, "Live preview will appear when Spaces is connected to Haven's trusted GenUI runtime.");
            return;
        }

        try
        {
            lock (_previewGate)
            {
                _previewMounts.RemoveAll(mount => mount.OriginalClose?.IsCompletedSuccessfully == true);
                if (_previewMounts.Count >= 128)
                    throw new InvalidOperationException("Unresolved Space previews require page retirement before another preview is acquired.");
            }
            _generatedSurfaceMount = InvokePhysicalSource(() => _generatedSurfaceRenderer.Render(space));
            lock (_previewGate) _previewMounts.Add(_generatedSurfaceMount); // Retain before publishing/replacing the preview.
            if (_work.IsRetiring) _generatedSurfaceMount.RequestRetirement();
            else _scene.SetGeneratedPreview(_generatedSurfaceMount.Root, $"Live {space.GeneratedSurface.TemplateKey} surface");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (exception is SpaceGeneratedSurfaceRenderException failed)
                lock (_previewGate) _previewMounts.Add(failed.ActualMount);
            _work.Executing?.Retain(exception);
            if (CanPublish()) _scene.SetGeneratedPreview(null, $"Generated surface could not render: {exception.Message}");
        }
    }

    private SpaceDefinition? CurrentSpace() => _selectedId is { } id ? _spaces.FirstOrDefault(space => space.Id == id) : null;

    private Task RunMutationAsync(Func<Task> operation, string action) => RunOwnedAsync(operation, action, true);

    private Task RunActionAsync(Func<Task> operation, string action) => RunOwnedAsync(operation, action, false);

    private Task RunOwnedAsync(Func<Task> operation, string action, bool mutation)
    {
        var generation = Volatile.Read(ref _generation);
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => _active && Volatile.Read(ref _generation) == generation);
            try
            {
                original.DemandPublication();
                if (mutation) await PublishAsync(() => _scene.SetBusy(true));
                await ObserveAsync(operation);
            }
            catch (Exception error)
            {
                original.Retain(error); // Includes handled errors and faulted OCE payloads.
                if (original.IsPublicationCurrent)
                    await PublishAsync(() => _scene.SetStatus($"Could not {action}: {error.Message}"));
            }
            finally
            {
                if (mutation && original.IsPublicationCurrent)
                    await PublishAsync(() => _scene.SetBusy(false));
            }
        }, actual => _latestAction = actual);
    }

    private bool CanPublish() => _active && !_work.IsRetiring && (_work.Executing?.IsPublicationCurrent ?? true);

    private void DemandOriginalSceneWrite()
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No original owns this Spaces scene write.");
        original.DemandPublication();
    }

    private void OwnOriginalSceneCallback(Action callback)
    {
        if (!CanPublish()) return;
        var generation = Volatile.Read(ref _generation);
        InvokePhysicalSource(() =>
        {
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(() => _active && Volatile.Read(ref _generation) == generation);
                original.DemandPublication();
                callback();
            });
            return true;
        });
    }

    private Task PublishAsync(Action callback)
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No original owns this Spaces publication.");
        var actual = InvokePhysicalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            InvokePhysicalSource(() =>
            {
                if (original.IsPublicationCurrent) callback();
                return true;
            })).GetTask());
        return original.AwaitAsync(actual);
    }

    private Task ObserveAsync(Func<Task> source)
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No original owns this Spaces action.");
        original.DemandPublication();
        return original.AwaitAsync(InvokePhysicalSource(source));
    }

    private Task<T> ObserveAsync<T>(Func<Task<T>> source)
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No original owns this Spaces source.");
        original.DemandPublication();
        return original.AwaitAsync(InvokePhysicalSource(source));
    }

    private T InvokePhysicalSource<T>(Func<T> source)
    {
        var stack = _physicalSources ??= [];
        stack.Add(this);
        try { return source(); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or JsonException;

    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An original Spaces source callback cannot join its own page retirement.");
        _work.DemandExternalClose();
        foreach (var mount in CapturePreviewMounts()) mount.DemandExternalOriginalRetirementJoin();
    }

    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }

    private Task StopOriginalPresentationAsync()
    {
        _active = false;
        Interlocked.Increment(ref _generation);
        SizeChanged -= OnSizeChanged;
        Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();
        _scene.CreateRequested -= OnCreateRequested;
        _scene.ArchivedVisibilityChanged -= OnArchivedVisibilityChanged;
        _scene.SpaceSelected -= OnSpaceSelected;
        _scene.ConversationSelected -= OnConversationSelected;
        _scene.NewConversationRequested -= OnNewConversationRequested;
        _scene.SaveRequested -= OnSaveRequested;
        _scene.LaunchRequested -= OnLaunchRequested;
        _scene.ForkRequested -= OnForkRequested;
        _scene.ArchiveRequested -= OnArchiveRequested;
        _scene.DeleteRequested -= OnDeleteRequested;
        _scene.AddFileRequested -= OnAddFileRequested;
        _scene.RemoveFileRequested -= OnRemoveFileRequested;
        _scene.ManageLayoutRequested -= OnManageLayoutRequested;
        _scene.EditWithHavenRequested -= OnEditWithHavenRequested;
        var failures = new List<Exception>();
        foreach (var mount in CapturePreviewMounts())
            try { mount.RequestRetirement(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Original Space preview retirement failed.", failures);
        return Task.CompletedTask;
    }

    private async Task DetachOriginalPresentationAsync()
    {
        var failures = new List<Exception>();
        foreach (var mount in CapturePreviewMounts())
            try
            {
                mount.RequestRetirement();
                var actualClose = mount.OriginalClose ?? throw new InvalidOperationException("No actual Space preview close was acquired.");
                await actualClose.ConfigureAwait(false);
            }
            catch (Exception error) { failures.Add(error); }
        // Failed child retirement retains its physical tree and the exact mount custody.
        // Detaching the parent here would discard the unresolved child's presentation.
        if (failures.Count != 0) throw new AggregateException("Original Space preview cleanup failed; the scene remains retained.", failures);
        var actual = InvokePhysicalSource(() => Dispatcher.UIThread.InvokeAsync(() => _work.RunCloseCallback(() =>
        {
            if (ReferenceEquals(Scene.Root, _scene.Root)) Scene.Root = null;
            _scene.Dispose();
            if (ReferenceEquals(Content, Scene)) Content = null;
            _generatedSurfaceMount = null;
            lock (_previewGate) _previewMounts.Clear();
        })).GetTask());
        try { await actual.ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0) throw new AggregateException("Original Space preview/scene cleanup failed.", failures);
    }

    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private SpaceGeneratedSurfaceMount[] CapturePreviewMounts()
    { lock (_previewGate) return _previewMounts.ToArray(); }
}
