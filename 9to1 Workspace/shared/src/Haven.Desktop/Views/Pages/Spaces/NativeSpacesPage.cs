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
public sealed class NativeSpacesPage : UserControl, IActivatablePage, IDisposable
{
    private readonly SpaceRegistry _registry;
    private readonly bool _allowDelete;
    private readonly Func<CancellationToken, Task>? _requireCurrentAccess;
    private readonly Func<SpaceDefinition, SpaceContextReference, Task>? _openCanonicalSource;
    private readonly IConversationRepository? _conversations;
    private readonly Func<Conversation, Task>? _openConversation;
    private readonly Func<SpaceDefinition, Task>? _launchSpace;
    private readonly Func<Guid, Task>? _deleteSpace;
    private readonly Func<SpaceDefinition, Task>? _manageLayout;
    private readonly SpaceGeneratedSurfaceRenderer? _generatedSurfaceRenderer;
    private readonly SpaceEditPlanner? _editPlanner;
    private readonly SpacesHavenScene _scene;
    private SpaceGeneratedSurfaceMount? _generatedSurfaceMount;
    private CancellationTokenSource? _refreshCancellation;
    private IReadOnlyList<SpaceDefinition> _spaces = [];
    private Guid? _selectedId;
    private bool _disposed;

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
        Func<Conversation, Task>? openConversation = null,
        Func<SpaceDefinition, SpaceContextReference, Task>? openCanonicalSource = null,
        Func<CancellationToken, Task>? requireCurrentAccess = null, bool allowDelete = true)
    {
        _allowDelete = allowDelete;
        _requireCurrentAccess = requireCurrentAccess;
        _openCanonicalSource = openCanonicalSource;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _conversations = conversations;
        _openConversation = openConversation;
        _generatedSurfaceRenderer = generatedSurfaceRenderer;
        _editPlanner = editPlanner;
        _launchSpace = launchSpace;
        _deleteSpace = deleteSpace;
        _manageLayout = manageLayout;
        _scene = new SpacesHavenScene { CanonicalSourceNavigationAvailable = _openCanonicalSource is not null };
        _scene.CanonicalSourceRequested += OnCanonicalSourceRequested;
        _scene.SetDeleteAvailable(allowDelete);
        _scene.SetLaunchAvailable(_launchSpace is not null);
        _scene.SetLayoutEditorAvailable(_manageLayout is not null);
        _scene.SetEditWithHavenAvailable(_editPlanner is not null);
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

    public Task ActivateAsync(CancellationToken cancellationToken) => RefreshAsync(cancellationToken);

    public void Deactivate() => Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();

    internal Task RefreshNowAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) =>
        _scene.SetCompactLayout(e.NewSize.Width > 0 && e.NewSize.Width < 760d);

    private async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        var refresh = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = Interlocked.Exchange(ref _refreshCancellation, refresh);
        previous?.Cancel();
        var token = refresh.Token;
        try
        {
            await RequireCurrentAccessAsync(token).ConfigureAwait(false);
            var spaces = await _registry.GetAllAsync(_scene.IncludeArchived, token).ConfigureAwait(false);
            await RequireCurrentAccessAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && !token.IsCancellationRequested && ReferenceEquals(_refreshCancellation, refresh)) ApplySpaces(spaces);
            });
            await RefreshConversationsAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_refreshCancellation, refresh)) return;
                if (exception is UnauthorizedAccessException) ClearDeniedProjection();
                _scene.SetStatus($"Spaces could not refresh: {exception.Message}");
            });
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
            await Dispatcher.UIThread.InvokeAsync(() => _scene.SetConversations([]));
            return;
        }

        await RequireCurrentAccessAsync(cancellationToken).ConfigureAwait(false);
        var conversations = await _conversations.GetBySpaceAsync(spaceId, 500, cancellationToken).ConfigureAwait(false);
        await RequireCurrentAccessAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_disposed && !cancellationToken.IsCancellationRequested && _selectedId == spaceId) _scene.SetConversations(conversations);
        });
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
            var all = await _registry.GetAllAsync(includeArchived: true, CancellationToken.None);
            var names = all.Select(space => space.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = "New Space";
            for (var suffix = 2; names.Contains(name); suffix++) name = $"New Space {suffix}";
            var created = await _registry.CreateAsync(name, "Describe what you want this Space to help with.", CancellationToken.None);
            _selectedId = created.Id;
            await RefreshAsync();
        }, "create Space");
    }

    private async void OnArchivedVisibilityChanged(object? sender, bool includeArchived) => await RefreshAsync();

    private async void OnSpaceSelected(object? sender, Guid id)
    {
        try
        {
            await RequireCurrentAccessAsync(CancellationToken.None);
            if (_disposed) return;
            _selectedId = id;
            _scene.SetSpaces(_spaces, id);
            var current = CurrentSpace();
            _scene.SetSpace(current);
            RefreshGeneratedPreview(current);
            _scene.SetStatus(null);
            await RefreshConversationsAsync();
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (exception is UnauthorizedAccessException) ClearDeniedProjection();
            _scene.SetStatus($"Chats could not refresh: {exception.Message}");
        }
    }

    private async void OnConversationSelected(object? sender, Guid id)
    {
        if (_conversations is null || _openConversation is null) return;
        await RunMutationAsync(async () =>
        {
            var conversation = await _conversations.GetAsync(id, CancellationToken.None);
            if (conversation is null) { await RefreshConversationsAsync(); return; }
            await RequireCurrentAccessAsync(CancellationToken.None);
            await _openConversation(conversation);
        }, "open Space chat");
    }

    private async void OnNewConversationRequested(object? sender, Guid id)
    {
        var space = _spaces.FirstOrDefault(candidate => candidate.Id == id);
        if (space is null || _launchSpace is null || space.IsArchived) return;
        await RunMutationAsync(async () =>
        {
            await _launchSpace(space);
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
            await _registry.UpdateAsync(updated, CancellationToken.None);
            await RefreshAsync();
            _scene.SetStatus("Space saved.");
        }, "save Space");
    }

    private async void OnLaunchRequested(object? sender, Guid id)
    {
        if (_launchSpace is null) return;
        var space = _spaces.FirstOrDefault(item => item.Id == id);
        if (space is null) return;
        await RunActionAsync(() => _launchSpace(space), "open Space");
    }

    private async void OnForkRequested(object? sender, Guid id)
    {
        await RunMutationAsync(async () =>
        {
            var fork = await _registry.ForkAsync(id, cancellationToken: CancellationToken.None);
            _selectedId = fork.Id;
            await RefreshAsync();
            _scene.SetStatus($"Forked as {fork.Name}.");
        }, "fork Space");
    }

    private async void OnArchiveRequested(object? sender, Guid id)
    {
        var current = _spaces.FirstOrDefault(space => space.Id == id);
        if (current is null) return;
        await RunMutationAsync(async () =>
        {
            await _registry.SetArchivedAsync(id, !current.IsArchived, CancellationToken.None);
            if (!current.IsArchived && !_scene.IncludeArchived) _selectedId = null;
            await RefreshAsync();
        }, current.IsArchived ? "restore Space" : "archive Space");
    }

    private async void OnDeleteRequested(object? sender, Guid id) => await DeleteSpaceAsync(id);

    internal Task DeleteSpaceAsync(Guid id) => RunMutationAsync(async () =>
        {
            if (!_allowDelete) throw new NotSupportedException("Space deletion is not available in this view yet.");
            if (_deleteSpace is not null)
                await _deleteSpace(id);
            else
            {
                if (_conversations is not null) await _conversations.DetachSpaceAsync(id, CancellationToken.None);
                await _registry.DeleteAsync(id, CancellationToken.None);
            }
            if (_selectedId == id) _selectedId = null;
            await RefreshAsync();
        }, "delete Space");

    private async void OnAddFileRequested(object? sender, SpaceFilePermission permission)
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
            files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Add files to {current.Name}",
                AllowMultiple = true
            });
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _scene.SetStatus($"Could not open the file picker: {exception.Message}");
            return;
        }
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
        if (paths.Length == 0) return;

        await RunMutationAsync(async () =>
        {
            foreach (var path in paths) await _registry.AddFileAsync(current.Id, path, permission, CancellationToken.None);
            await RefreshAsync();
            _scene.SetStatus($"Added {paths.Length} file{(paths.Length == 1 ? string.Empty : "s")} as {(permission == SpaceFilePermission.ReadWrite ? "read & write" : "read-only")}.");
        }, "add files");
    }

    private async void OnRemoveFileRequested(object? sender, string path)
    {
        var current = CurrentSpace();
        if (current is null) return;
        await RunMutationAsync(async () =>
        {
            await _registry.RemoveFileAsync(current.Id, path, CancellationToken.None);
            await RefreshAsync();
        }, "remove file");
    }

    private async void OnEditWithHavenRequested(object? sender, string instruction)
    {
        var current = CurrentSpace();
        if (_editPlanner is null || current is null) return;
        _scene.SetBusy(true);
        _scene.SetStatus("Planning safe Space changes…");
        try
        {
            await RequireCurrentAccessAsync(CancellationToken.None);
            var result = await _editPlanner.PlanAsync(instruction, current, CancellationToken.None);
            await RequireCurrentAccessAsync(CancellationToken.None);
            if (!result.Succeeded || result.Patch is null)
            {
                _scene.SetStatus(result.Message);
                return;
            }
            _scene.ApplyEditPatch(result.Patch);
        }
        catch (OperationCanceledException)
        {
            _scene.SetStatus("Space edit cancelled.");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (exception is UnauthorizedAccessException) ClearDeniedProjection();
            _scene.SetStatus($"Space edit unavailable: {exception.Message}");
        }
        finally
        {
            _scene.SetBusy(false);
        }
    }

    private async void OnCanonicalSourceRequested(object? sender, Guid contextId)
    {
        if (_disposed || _openCanonicalSource is null || CurrentSpace() is not { } space) return;
        var source = space.ContextReferences?.SingleOrDefault(item => item.ContextId == contextId);
        if (source is null) return;
        await RunActionAsync(() => _openCanonicalSource(space, source), "open canonical source");
    }

    private async void OnManageLayoutRequested(object? sender, Guid id)
    {
        if (_manageLayout is null) return;
        var space = _spaces.FirstOrDefault(item => item.Id == id);
        if (space is null) return;
        await RunActionAsync(() => _manageLayout(space), "open layout editor");
    }

    private void RefreshGeneratedPreview(SpaceDefinition? space)
    {
        _scene.SetGeneratedPreview(null, null);
        _generatedSurfaceMount?.Dispose();
        _generatedSurfaceMount = null;

        if (space?.GeneratedSurface is null) return;
        if (_generatedSurfaceRenderer is null)
        {
            _scene.SetGeneratedPreview(null, "Live preview will appear when Spaces is connected to Haven's trusted GenUI runtime.");
            return;
        }

        try
        {
            _generatedSurfaceMount = _generatedSurfaceRenderer.Render(space);
            _scene.SetGeneratedPreview(_generatedSurfaceMount.Root, $"Live {space.GeneratedSurface.TemplateKey} surface");
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _scene.SetGeneratedPreview(null, $"Generated surface could not render: {exception.Message}");
        }
    }

    private SpaceDefinition? CurrentSpace() => _selectedId is { } id ? _spaces.FirstOrDefault(space => space.Id == id) : null;

    private async Task RunMutationAsync(Func<Task> operation, string action)
    {
        _scene.SetBusy(true);
        try { await RequireCurrentAccessAsync(CancellationToken.None); if (!_disposed) await operation(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (exception is UnauthorizedAccessException) ClearDeniedProjection();
            _scene.SetStatus($"Could not {action}: {exception.Message}");
        }
        finally { _scene.SetBusy(false); }
    }

    private async Task RunActionAsync(Func<Task> operation, string action)
    {
        try { await RequireCurrentAccessAsync(CancellationToken.None); if (!_disposed) await operation(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (exception is UnauthorizedAccessException) ClearDeniedProjection();
            _scene.SetStatus($"Could not {action}: {exception.Message}");
        }
    }

    private Task RequireCurrentAccessAsync(CancellationToken token) => _requireCurrentAccess?.Invoke(token) ?? Task.CompletedTask;

    private void ClearDeniedProjection()
    {
        _spaces = []; _selectedId = null;
        _scene.SetSpaces([], null); _scene.SetSpace(null); _scene.SetConversations([]);
        RefreshGeneratedPreview(null);
    }

    private static bool IsExpected(Exception exception) =>
        exception is NotSupportedException or IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or JsonException;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SizeChanged -= OnSizeChanged;
        Interlocked.Exchange(ref _refreshCancellation, null)?.Cancel();
        _scene.SetGeneratedPreview(null, null);
        _generatedSurfaceMount?.Dispose();
        _generatedSurfaceMount = null;
        _scene.CanonicalSourceRequested -= OnCanonicalSourceRequested;
        _scene.CreateRequested -= OnCreateRequested;
        _scene.ArchivedVisibilityChanged -= OnArchivedVisibilityChanged;
        _scene.SpaceSelected -= OnSpaceSelected;
        _scene.SaveRequested -= OnSaveRequested;
        _scene.LaunchRequested -= OnLaunchRequested;
        _scene.ForkRequested -= OnForkRequested;
        _scene.ArchiveRequested -= OnArchiveRequested;
        _scene.DeleteRequested -= OnDeleteRequested;
        _scene.AddFileRequested -= OnAddFileRequested;
        _scene.RemoveFileRequested -= OnRemoveFileRequested;
        _scene.ManageLayoutRequested -= OnManageLayoutRequested;
        _scene.EditWithHavenRequested -= OnEditWithHavenRequested;
        _scene.Dispose();
    }
}
