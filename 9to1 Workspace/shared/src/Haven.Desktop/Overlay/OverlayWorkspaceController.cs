#if !ANDROID
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Overlay;

/// <summary>Owns Desktop Overlay lifecycle while reusing production Chat and normal capability permission/preflight policy.</summary>
internal sealed partial class OverlayWorkspaceController : IAsyncDisposable, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private sealed record OverlayCaptureDraft(OverlayContextEnvelope SourceContext, string SourcePath);

    private readonly OverlayWorkspaceRegistry _registry;
    private readonly OverlayContextActionCandidateService _actionCandidates;
    private readonly OverlayForegroundContextCaptureService _contextCapture;
    private readonly OverlayVisualContextCaptureService _visualCapture;
    private readonly OverlayRegionCaptureService _regionCapture;
    private readonly OverlayChatSessionFactory _chats;
    private readonly OverlayGoSessionFactory _go;
    private readonly OverlayGlobalHotkey _hotkey;
    private readonly NotificationService _notifications;
    private readonly Dictionary<Guid, OverlayWorkspaceWindow> _windows = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _geometryUpdates = [];
    private readonly Dictionary<Guid, OverlayCaptureDraft> _captureDrafts = [];
    private bool _initialized;
    private bool _disposed;

    public OverlayWorkspaceController(
        OverlayWorkspaceRegistry registry,
        OverlayContextActionCandidateService actionCandidates,
        OverlayForegroundContextCaptureService contextCapture,
        OverlayVisualContextCaptureService visualCapture,
        OverlayRegionCaptureService regionCapture,
        OverlayChatSessionFactory chats,
        OverlayGoSessionFactory go,
        OverlayGlobalHotkey hotkey,
        NotificationService notifications)
    {
        _registry = registry;
        _actionCandidates = actionCandidates;
        _contextCapture = contextCapture;
        _visualCapture = visualCapture;
        _regionCapture = regionCapture;
        _chats = chats;
        _go = go;
        _hotkey = hotkey;
        _notifications = notifications;
        // Wire the typed recovery seam so every Overlay chat can route the genuine local-model
        // management request. The floating Overlay cannot open the shell's Home/Discover tab, so
        // it surfaces honest guidance instead of fabricating navigation or a model.
        chats.WireHost = page => page.ManageLocalModelsRequested += (_, _) =>
            _notifications.Show("Local models",
                "Open Home to manage local models, or configure a model provider.",
                ToastKind.Warning, TimeSpan.FromSeconds(6));
    }

    public Task InitializeAsync(CancellationToken cancellationToken) => RunControllerAsync(() => InitializeCoreAsync(cancellationToken));

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;
        _initialized = true;

        await AwaitControllerAsync(() => _registry.InitializeAsync(cancellationToken));
        if (_disposed) return;
        _registry.Changed += OnRegistryChanged;

        foreach (var session in _registry.Snapshot.Sessions.Where(session => session.IsPinned && session.IsVisible))
            await AwaitControllerAsync(() => EnsureMountedAsync(session.Id, show: true, cancellationToken));

        if (_disposed) return;
        _hotkey.Pressed += OnHotkeyPressed;
        if (!_hotkey.Start())
        {
            _notifications.Show(
                "Overlay shortcut unavailable",
                _hotkey.UnavailableReason ?? "Windows could not register " + _hotkey.ShortcutLabel + ".",
                ToastKind.Warning,
                TimeSpan.FromSeconds(12));
        }

        await AwaitControllerAsync(() => RefreshAllAsync(_registry.Snapshot, cancellationToken));
    }

    internal Task<OverlaySessionState> OpenNewGoAsync(OverlayContextEnvelope? context, string? sourceAssociation, CancellationToken cancellationToken) =>
        RunControllerAsync(() => OpenNewGoCoreAsync(context, sourceAssociation, cancellationToken));

    private async Task<OverlaySessionState> OpenNewGoCoreAsync(
        OverlayContextEnvelope? context,
        string? sourceAssociation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var page = RetainControllerPage(await AwaitControllerAsync(() => _go.CreateAsync(cancellationToken)));
        var session = await AwaitControllerAsync(() => _registry.OpenSessionAsync(
            "go",
            "Go",
            null,
            sourceAssociation,
            cancellationToken));
        await AwaitControllerAsync(() => _registry.UpdateGeometryAsync(
            session.Id,
            new OverlaySurfaceGeometry(460, 400, 80, 110),
            cancellationToken));
        session = _registry.Snapshot.Sessions.First(item => item.Id == session.Id);

        if (context is not null)
            await AwaitControllerAsync(() => _registry.SetContextAsync(session.Id, context, cancellationToken));

        Mount(session, page);
        if (!_disposed && _windows.TryGetValue(session.Id, out var mounted)) mounted.ShowAndActivate();
        await AwaitControllerAsync(() => RefreshAllAsync(_registry.Snapshot, cancellationToken));
        QueueGoSuggestionRefresh(session.Id, page, "The user opened the floating 9to1 Overlay workspace.");
        return _registry.Snapshot.Sessions.First(item => item.Id == session.Id);
    }

    internal Task<OverlaySessionState> OpenNewChatAsync(OverlayContextEnvelope? context, string? sourceAssociation, CancellationToken cancellationToken) =>
        RunControllerAsync(() => OpenNewChatCoreAsync(context, sourceAssociation, cancellationToken));

    private async Task<OverlaySessionState> OpenNewChatCoreAsync(
        OverlayContextEnvelope? context,
        string? sourceAssociation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var page = RetainControllerPage(await AwaitControllerAsync(() => _chats.CreateNewAsync(cancellationToken)));
        var session = await AwaitControllerAsync(() => _registry.OpenSessionAsync(
            "chat",
            "Chat",
            page.ConversationId,
            sourceAssociation,
            cancellationToken));

        if (context is not null)
            await AwaitControllerAsync(() => _registry.SetContextAsync(session.Id, context, cancellationToken));

        Mount(session, page);
        if (!_disposed && _windows.TryGetValue(session.Id, out var mounted)) mounted.ShowAndActivate();
        await AwaitControllerAsync(() => RefreshAllAsync(_registry.Snapshot, cancellationToken));
        return _registry.Snapshot.Sessions.First(item => item.Id == session.Id);
    }

    internal Task ToggleWorkspaceAsync(CancellationToken cancellationToken) => RunControllerAsync(() => ToggleWorkspaceCoreAsync(cancellationToken));

    private async Task ToggleWorkspaceCoreAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var visibleUnpinned = _windows
            .Where(pair => !IsPinned(pair.Key) && pair.Value.WorkspaceVisible)
            .Select(pair => pair.Value)
            .ToArray();

        if (visibleUnpinned.Length > 0)
        {
            foreach (var window in visibleUnpinned) window.HideWorkspace();
            return;
        }

        var goSession = _registry.Snapshot.Sessions
            .Where(session => !session.IsPinned && session.AppKey.Equals("go", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(session => session.UpdatedAt)
            .FirstOrDefault();

        if (goSession is null)
        {
            await AwaitControllerAsync(() => OpenNewGoAsync(null, null, cancellationToken));
            return;
        }

        await AwaitControllerAsync(() => EnsureMountedAsync(goSession.Id, show: true, cancellationToken));
        await AwaitControllerAsync(() => _registry.ActivateAsync(goSession.Id, cancellationToken));
    }

    private async Task EnsureMountedAsync(Guid sessionId, bool show, CancellationToken cancellationToken)
    {
        if (_disposed || _closingSessions.Contains(sessionId)) return;
        if (_windows.TryGetValue(sessionId, out var existing))
        {
            if (show) existing.ShowAndActivate();
            return;
        }

        var session = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId);
        if (session is null) return;

        if (session.AppKey.Equals("go", StringComparison.OrdinalIgnoreCase))
        {
            var goPage = RetainControllerPage(await AwaitControllerAsync(() => _go.CreateAsync(cancellationToken)));
            Mount(session, goPage);
            if (!_disposed && show && _windows.TryGetValue(session.Id, out var restoredGo)) restoredGo.ShowAndActivate();
            QueueGoSuggestionRefresh(session.Id, goPage, "The user restored a floating 9to1 Overlay Go workspace.");
            return;
        }

        if (!session.AppKey.Equals("chat", StringComparison.OrdinalIgnoreCase) || session.ThreadId is not Guid conversationId)
        {
            _notifications.Show("Overlay session unavailable", "This saved Overlay surface cannot be restored by the current product host.", ToastKind.Warning);
            return;
        }

        var page = await AwaitControllerAsync(() => _chats.RestoreAsync(conversationId, cancellationToken));
        if (page is not null) RetainControllerPage(page);
        if (page is null)
        {
            await AwaitControllerAsync(() => _registry.RemoveSessionAsync(session.Id, cancellationToken));
            _notifications.Show("Overlay chat unavailable", "The saved conversation no longer exists, so its pinned surface was removed.", ToastKind.Warning);
            return;
        }

        Mount(session, page);
        if (!_disposed && show && _windows.TryGetValue(session.Id, out var restored)) restored.ShowAndActivate();
    }

    private void Mount(OverlaySessionState session, Haven.Desktop.Views.Pages.Chat.NewChatPage page)
    {
        if (_disposed || _windows.ContainsKey(session.Id)) return;
        _closingSessions.Remove(session.Id);
        RetainControllerPage(page);
        var window = new OverlayWorkspaceWindow(session, page);
        _windows[session.Id] = window;
        RecordWindowBorrower(window);
        WireWindow(window, session.Id, openGoOnNewWorkspace: true);
        window.CaptureRequested += (_, _) => RunOnUi(session.Id, () => CaptureVisualContextAsync(session.Id));
        window.ApplySelectionRequested += (_, _) => RunOnUi(session.Id, () => ApplyRegionSelectionAsync(session.Id));
        window.ReplaceCaptureRequested += (_, _) => RunOnUi(session.Id, () => CaptureVisualContextAsync(session.Id));
        window.ClearSelectionRequested += (_, _) => RunOnUi(session.Id, () => ClearRegionSelectionAsync(session.Id));
        window.OverlayBackRequested += (_, _) => RunOnUi(session.Id, () => NavigateBackAsync(session.Id));
        window.ActionRequested += (_, action) => RunOnUi(session.Id, () => HandleActionAsync(session.Id, action));
    }

    private void Mount(OverlaySessionState session, Haven.Desktop.Views.Pages.Go.GoPage page)
    {
        if (_disposed || _windows.ContainsKey(session.Id)) return;
        _closingSessions.Remove(session.Id);
        RetainControllerPage(page);
        var window = new OverlayWorkspaceWindow(session, page);
        _windows[session.Id] = window;
        RecordWindowBorrower(window);
        WireWindow(window, session.Id, openGoOnNewWorkspace: true);

        page.SubmitRequested += (_, instruction) =>
        {
            RunOnUi(session.Id, () => SubmitFromGoAsync(session.Id, instruction, page.TakeAttachments()));
        };
        page.RefreshSuggestionsRequested += (_, _) =>
            QueueGoSuggestionRefresh(session.Id, page, "The user asked for another set of useful Overlay actions.");
        page.AddRequested += (_, action) =>
        {
            if (action == AddMenu.AddMenuAction.File)
                RunOnUi(session.Id, () => AttachFilesToGoAsync(session.Id, page));
        };
        window.CaptureRequested += (_, _) => RunOnUi(session.Id, () => CaptureVisualContextAsync(session.Id));
        window.ApplySelectionRequested += (_, _) => RunOnUi(session.Id, () => ApplyRegionSelectionAsync(session.Id));
        window.ReplaceCaptureRequested += (_, _) => RunOnUi(session.Id, () => CaptureVisualContextAsync(session.Id));
        window.ClearSelectionRequested += (_, _) => RunOnUi(session.Id, () => ClearRegionSelectionAsync(session.Id));
        window.OverlayBackRequested += (_, _) => RunOnUi(session.Id, () => NavigateBackAsync(session.Id));
        window.SetSuggestions(page.Suggestions.Select(suggestion => suggestion.Label).ToArray());
        window.ActionRequested += (_, action) => RunOnUi(session.Id, () => HandleGoActionAsync(session.Id, action));
        window.SubmitRequested += (_, text) => RunOnUi(session.Id, () => page.SubmitOverlayInstructionAsync(text));
        window.AddRequested += (_, _) => RunOnUi(session.Id, () => AttachFilesToGoAsync(session.Id, page));
    }

    private void WireWindow(OverlayWorkspaceWindow window, Guid sessionId, bool openGoOnNewWorkspace)
    {
        window.NewChatRequested += (_, _) =>
            RunOnUi(sessionId, () => openGoOnNewWorkspace
                ? OpenNewGoAsync(null, null, CancellationToken.None)
                : OpenNewChatAsync(null, null, CancellationToken.None));
        window.PinToggleRequested += (_, _) => RunOnUi(sessionId, () => TogglePinAsync(sessionId));
        window.CollapseToggleRequested += (_, _) => RunOnUi(sessionId, () => ToggleCollapsedAsync(sessionId));
        window.CloseRequested += (_, _) => RunOnUi(() => CloseSessionAsync(sessionId));
        window.NativeCloseRequested += (_, _) => RunOnUi(() => HandleNativeCloseAsync(sessionId));
        window.SessionActivated += (_, id) => RunOnUi(sessionId, () => ActivateAsync(id));
        window.GeometryChanged += (_, geometry) =>
        {
            var current = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId);
            QueueGeometryUpdate(sessionId, GeometryForPersistence(current, geometry));
        };
    }

    private async Task ToggleCollapsedAsync(Guid sessionId)
    {
        var current = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId);
        if (current is null) return;

        if (_windows.TryGetValue(sessionId, out var window))
        {
            CancelGeometryUpdate(sessionId);
            await AwaitControllerAsync(() => _registry.UpdateGeometryAsync(
                sessionId,
                GeometryForPersistence(current, window.CaptureGeometry()),
                CancellationToken.None));
            current = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId) ?? current;
        }

        await AwaitControllerAsync(() => _registry.SetCollapsedAsync(sessionId, !current.IsCollapsed, CancellationToken.None));
    }

    internal static OverlaySurfaceGeometry GeometryForPersistence(OverlaySessionState? current, OverlaySurfaceGeometry liveGeometry) =>
        current?.IsCollapsed == true
            ? current.Geometry with { X = liveGeometry.X, Y = liveGeometry.Y }
            : liveGeometry;

    private async Task SubmitFromGoAsync(
        Guid sourceSessionId,
        string instruction,
        TaskAttachmentSnapshot attachments)
    {
        if (string.IsNullOrWhiteSpace(instruction)) return;
        var source = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sourceSessionId);
        if (source is null) return;

        var chat = await AwaitControllerAsync(() => OpenNewChatAsync(source.Context, source.SourceAssociation, CancellationToken.None));
        if (_disposed || !_windows.TryGetValue(chat.Id, out var target)) return;

        target.ChatPage.AttachSnapshot(attachments);
        if (source.Context is not null)
            await AwaitControllerAsync(() => AttachConcreteFilesAsync(target, source.Context));
        if (!CanPublish(chat.Id, target)) return;
        target.ChatPage.Submit(instruction);

        if (!source.IsPinned && _windows.TryGetValue(sourceSessionId, out var sourceWindow))
            sourceWindow.HideWorkspace();
    }

    private async Task CaptureVisualContextAsync(Guid sessionId)
    {
        if (!_windows.TryGetValue(sessionId, out var window)) return;
        var restoreVisible = window.WorkspaceVisible;
        if (restoreVisible) window.HideWorkspace();
        OverlayContextEnvelope? context = null;
        string? captureError = null;
        try
        {
            context = await AwaitControllerAsync(() => _visualCapture.CaptureWithStatusAsync(CancellationToken.None));
        }
        catch (OperationCanceledException)
        {
            captureError = "Screen capture cancelled; any existing context remains active.";
        }
        catch (Exception exception)
        {
            if (!_disposed) _notifications.Show("Overlay capture unavailable", exception.Message, ToastKind.Warning, TimeSpan.FromSeconds(6));
            captureError = "Screen capture failed: " + exception.Message;
        }
        finally
        {
            if (!_disposed && !_closingSessions.Contains(sessionId) && restoreVisible && _windows.TryGetValue(sessionId, out var restored)) restored.ShowAndActivate();
        }

        if (_disposed || _closingSessions.Contains(sessionId) || !_windows.TryGetValue(sessionId, out var currentWindow)
            || !_registry.Snapshot.Sessions.Any(session => session.Id == sessionId))
        {
            if (context is not null) _visualCapture.CleanupCapture(context);
            return;
        }
        window = currentWindow;
        if (context is null)
        {
            window.SetRegionStatus(captureError ?? "Screen capture did not produce a selection.");
            return;
        }
        var sourcePath = SourceCapturePath(context);
        if (!context.HasPayload || sourcePath is null)
        {
            _visualCapture.CleanupCapture(context);
            window.SetRegionStatus(context.Provenance.PermissionDescription ?? "Screen capture did not produce an image.");
            return;
        }

        var previous = _captureDrafts.GetValueOrDefault(sessionId);
        _captureDrafts[sessionId] = new OverlayCaptureDraft(context, sourcePath);
        if (previous is not null) _visualCapture.CleanupCapture(previous.SourceContext);
        window.ShowRegionDraft(sourcePath, "Captured real screen content. Drag over the frame to choose a region.");
        if (window.GoPage is { } page)
            QueueGoSuggestionRefresh(sessionId, page, "The user captured new visual Overlay context.");
    }

    private async Task ApplyRegionSelectionAsync(Guid sessionId)
    {
        if (!_windows.TryGetValue(sessionId, out var window)) return;
        if (!_captureDrafts.TryGetValue(sessionId, out var draft))
        {
            window.SetRegionStatus("Capture a real screen or window before applying a region.");
            return;
        }
        if (draft.SourceContext.IsExpired(DateTimeOffset.UtcNow))
        {
            window.SetRegionStatus("The captured frame expired. Replace capture before selecting a region.");
            return;
        }
        if (window.SelectedRegion is not Haven.UI.HavenRect selection)
        {
            window.SetRegionStatus("Drag over the captured frame to choose a region first.");
            return;
        }

        OverlayContextEnvelope region;
        try
        {
            var viewport = window.RegionPreviewViewport;
            region = await AwaitControllerAsync(() => _regionCapture.CreateRegionAsync(
                draft.SourceContext,
                selection,
                viewport.Width,
                viewport.Height,
                CancellationToken.None));
        }
        catch (OperationCanceledException)
        {
            if (CanPublish(sessionId, window)) window.SetRegionStatus("Region selection cancelled; the existing context remains active.");
            return;
        }
        catch (Exception exception)
        {
            if (CanPublish(sessionId, window)) window.SetRegionStatus("Region selection failed: " + exception.Message);
            return;
        }

        if (_disposed || _closingSessions.Contains(sessionId)) { _regionCapture.CleanupRegion(region); return; }
        var previous = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId)?.Context;
        try
        {
            if (!await AwaitControllerAsync(() => _registry.SetContextAsync(sessionId, region, CancellationToken.None)))
            {
                _regionCapture.CleanupRegion(region);
                if (CanPublish(sessionId, window)) window.SetRegionStatus("The Overlay session closed before the region could be applied.");
                return;
            }
        }
        catch (Exception exception)
        {
            _regionCapture.CleanupRegion(region);
            if (CanPublish(sessionId, window)) window.SetRegionStatus("The region could not be applied: " + exception.Message);
            return;
        }
        if (previous is not null) _regionCapture.CleanupRegion(previous);
        if (CanPublish(sessionId, window)) window.SetRegionStatus("Region applied. The selected highlight remains visible.");
    }

    private async Task ClearRegionSelectionAsync(Guid sessionId)
    {
        if (!_windows.TryGetValue(sessionId, out var window)) return;
        var previous = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId)?.Context;
        if (!await AwaitControllerAsync(() => _registry.SetContextAsync(sessionId, null, CancellationToken.None))) return;
        if (previous is not null) _regionCapture.CleanupRegion(previous);
        if (_captureDrafts.Remove(sessionId, out var draft)) _visualCapture.CleanupCapture(draft.SourceContext);
        if (CanPublish(sessionId, window)) window.ClearRegionDraft();
    }

    private static string? SourceCapturePath(OverlayContextEnvelope context) =>
        context.Attachments.FirstOrDefault(attachment =>
            string.Equals(attachment.Kind, "image", StringComparison.OrdinalIgnoreCase)
            && File.Exists(attachment.Id))?.Id;

    private async Task HandleGoActionAsync(Guid sourceSessionId, OverlayContextActionDescriptor action)
    {
        var source = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sourceSessionId);
        if (source is null) return;

        var chat = await AwaitControllerAsync(() => OpenNewChatAsync(source.Context, source.SourceAssociation, CancellationToken.None));
        if (_disposed) return;
        await AwaitControllerAsync(() => HandleActionAsync(chat.Id, action));
        if (!source.IsPinned && _windows.TryGetValue(sourceSessionId, out var sourceWindow))
            sourceWindow.HideWorkspace();
    }

    private async Task AttachFilesToGoAsync(Guid sessionId, Haven.Desktop.Views.Pages.Go.GoPage page)
    {
        if (!_windows.TryGetValue(sessionId, out var window)) return;
        var storage = TopLevel.GetTopLevel(window)?.StorageProvider;
        if (storage is null) return;
        await AttachFilesToGoFromPickerAsync(sessionId, page, () => storage.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Attach files to 9to1 Overlay",
            AllowMultiple = true
        }));
    }

    // Same owned noncancelable OS-picker boundary, exposed internally so a headless
    // contract check can hold the real returned task without opening a system picker.
    internal async Task AttachFilesToGoFromPickerAsync(Guid sessionId, Haven.Desktop.Views.Pages.Go.GoPage page,
        Func<Task<IReadOnlyList<Avalonia.Platform.Storage.IStorageFile>>> actualPicker)
    {
        if (!_windows.TryGetValue(sessionId, out var window)) return;
        var files = await AwaitControllerAsync(actualPicker);
        if (!CanPublish(sessionId, window)) return;
        page.AttachFiles(files.Select(file => file.Path.LocalPath).Where(path => !string.IsNullOrWhiteSpace(path)));
    }

    private void QueueGoSuggestionRefresh(Guid sessionId, Haven.Desktop.Views.Pages.Go.GoPage page, string activity)
    {
        RunOnUi(sessionId, async () =>
        {
            if (!_windows.ContainsKey(sessionId)) return;
            await AwaitControllerAsync(() => _go.RefreshSuggestionsAsync(page, activity, CancellationToken.None));
            if (!_disposed && !_closingSessions.Contains(sessionId) && _windows.TryGetValue(sessionId, out var window))
                window.SetSuggestions(page.Suggestions.Select(suggestion => suggestion.Label).ToArray());
        });
    }

    private async Task TogglePinAsync(Guid sessionId)
    {
        var current = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId);
        if (current is null) return;
        await AwaitControllerAsync(() => _registry.SetPinnedAsync(sessionId, !current.IsPinned, CancellationToken.None));
    }

    private Task NavigateBackAsync(Guid sessionId)
    {
        if (_windows.TryGetValue(sessionId, out var window) && window.TryNavigateBack())
            window.ShowAndActivate();
        return Task.CompletedTask;
    }

    private async Task ActivateAsync(Guid sessionId)
    {
        if (!await AwaitControllerAsync(() => _registry.ActivateAsync(sessionId, CancellationToken.None))) return;
        await AwaitControllerAsync(() => EnsureMountedAsync(sessionId, show: true, CancellationToken.None));
    }

    private async Task CloseSessionAsync(Guid sessionId)
    {
        if (!_closingSessions.Add(sessionId)) return;
        await AwaitControllerAsync(() => DrainOverlayWindowAsync(sessionId));
        await AwaitControllerAsync(() => CleanupSessionArtifactsAsync(sessionId, clearRegistryContext: true));
        await AwaitControllerAsync(() => _registry.CloseSessionAsync(sessionId, CancellationToken.None));
        CloseWindow(sessionId);
    }

    private Task HandleNativeCloseAsync(Guid sessionId) => CloseSessionAsync(sessionId);

    private async Task CleanupSessionArtifactsAsync(Guid sessionId, bool clearRegistryContext)
    {
        var current = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId)?.Context;
        if (clearRegistryContext && current is not null
            && !await AwaitControllerAsync(() => _registry.SetContextAsync(sessionId, null, CancellationToken.None))) return;
        if (current is not null) _regionCapture.CleanupRegion(current);
        if (_captureDrafts.Remove(sessionId, out var draft)) _visualCapture.CleanupCapture(draft.SourceContext);
    }

    private async Task HandleActionAsync(Guid sessionId, OverlayContextActionDescriptor action)
    {
        var session = _registry.Snapshot.Sessions.FirstOrDefault(item => item.Id == sessionId);
        if (session is null || !_windows.TryGetValue(sessionId, out var window)) return;
        var context = session.Context;

        if (action.RequiresContext && context is null)
        {
            _notifications.Show("No Overlay context", action.Label + " needs an active bounded selection first.", ToastKind.Info);
            return;
        }

        if (action.Id.Equals("copy", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(context?.SelectedText))
        {
            if (TopLevel.GetTopLevel(window)?.Clipboard is { } clipboard)
            {
                await AwaitControllerAsync(() => clipboard.SetTextAsync(context.SelectedText));
                _notifications.Show("Copied", "The selected Overlay text was copied.", ToastKind.Success);
            }
            return;
        }

        var capability = action.IsGenerated
            ? await AwaitControllerAsync(() => _chats.FindCapabilityAsync(action, CancellationToken.None))
            : null;
        if (!CanPublish(sessionId, window)) return;
        if (capability is not null)
            window.ChatPage.ApplyAddSelection(new AddMenuSelection(AddMenu.AddMenuAction.Capability, capability));

        if (context is not null)
            await AwaitControllerAsync(() => AttachConcreteFilesAsync(window, context));

        if (!CanPublish(sessionId, window)) return;
        window.ChatPage.SetDraft(BuildReviewDraft(action, context, capability));
        window.ShowAndActivate();
    }

    private async Task AttachConcreteFilesAsync(OverlayWorkspaceWindow window, OverlayContextEnvelope context)
    {
        var files = ConcreteContextFiles(context);
        if (files.Length > 0) await AwaitControllerAsync(() => window.ChatPage.AddFilesAsync(files));
    }

    internal static string[] ConcreteContextFiles(OverlayContextEnvelope context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Attachments
            .Select(attachment => attachment.Id)
            .Append(context.MediaReference)
            .Concat(context.SelectedItems.SelectMany(item => new[] { item.MediaReference, item.Attachment?.Id }))
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string BuildReviewDraft(
        OverlayContextActionDescriptor action,
        OverlayContextEnvelope? context,
        CapabilityDefinition? capability)
    {
        var instruction = action.Id.ToLowerInvariant() switch
        {
            "ask-haven" => "Use this selected context to help me.",
            "explain" => "Explain the selected context clearly.",
            "summarise" => "Summarise the selected context.",
            "rewrite" => "Rewrite the selected context while preserving its meaning.",
            "add-task" => "Turn the selected context into a task for my review.",
            "add-plan" => "Use the selected context to draft a Plan item for my review.",
            "send-study" => "Use the selected context as Study material.",
            "search" or "visual-search" => "Search using the selected context.",
            "save-write" => "Prepare the selected context for Write.",
            "analyse" => "Analyse the selected visual context.",
            "ocr-copy" => "Extract readable text from the selected visual context for my review.",
            "send-vision" => "Analyse the selected context with Vision.",
            "edit-imagine" => "Use the selected visual context as an Imagine editing request.",
            "cut" => "Help me review a cut action for the selected context; do not modify another app without explicit permission.",
            "paste" => "Help me review a paste action for the selected context; do not modify another app without explicit permission.",
            "share" => "Prepare the selected context for sharing, but do not send it until I explicitly approve the destination.",
            "inspect-control" => "Inspect the selected UI control's role, state, accessibility details, and available interactions without activating it.",
            "run-automation" => "Identify the automation associated with the selected UI context and prepare the requested run for my review; do not execute it without the normal 9to1 permission flow.",
            "open-in-app" => "Identify the app or destination associated with the selected UI context and prepare an open or navigation action for my review; do not execute it without the normal 9to1 permission flow.",
            "analyse-frame" => "Analyse the selected video frame at the captured media position using only the provided visible context.",
            "summarise-media" => "Summarise the selected visible media context without assuming content outside the captured selection.",
            _ when action.IsGenerated => "Use the selected context with the suggested capability action: " + action.Label + ".",
            _ => action.Label + " the selected context."
        };

        if (context is null && capability is null && !action.IsGenerated) return instruction;

        var builder = new StringBuilder(instruction);
        if (capability is not null)
        {
            builder.AppendLine();
            builder.Append("Attached capability candidate: " )
                .Append(capability.Name)
                .Append(" (")
                .Append(capability.RiskClass)
                .Append(", ")
                .Append(capability.Availability)
                .Append("). Use the normal 9to1 permission flow before any capability/tool execution; this draft does not mean the action has run.");
        }
        else if (action.IsGenerated)
        {
            builder.AppendLine();
            builder.Append("Suggested capability metadata: risk ")
                .Append(action.RiskClass?.ToString() ?? "unknown")
                .Append(", availability ")
                .Append(action.Availability?.ToString() ?? "unknown")
                .Append(", provider ")
                .Append(action.ProviderId ?? "unknown")
                .Append(". Use the normal 9to1 permission flow before execution.");
        }

        if (context is null) return builder.ToString();

        if (!string.IsNullOrWhiteSpace(context.SelectedText))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("Selected text:");
            builder.Append(context.SelectedText);
        }

        var selectionDetails = OverlaySelectionPresentation.ReviewDetails(context);
        if (!string.IsNullOrWhiteSpace(selectionDetails))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.Append(selectionDetails);
        }

        var provenance = context.Provenance;
        var source = string.Join(" · ", new[] { provenance.SourceApplication, provenance.SourceWindow }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        builder.AppendLine();
        builder.AppendLine();
        builder.Append("Context provenance: ")
            .Append(string.IsNullOrWhiteSpace(source) ? "unspecified source" : source)
            .Append("; permission ")
            .Append(provenance.PermissionState)
            .Append("; captured ")
            .Append(provenance.CapturedAt.ToString("O"))
            .Append("; expires ")
            .Append(provenance.ExpiresAt.ToString("O"));
        if (provenance.Bounds is { } bounds)
            builder.Append($"; bounds {bounds.X:0.#},{bounds.Y:0.#} {bounds.Width:0.#}×{bounds.Height:0.#}");
        if (context.WasTruncated) builder.Append("; bounded/truncated");
        builder.Append('.');
        return builder.ToString();
    }

    private async Task RefreshAllAsync(OverlayWorkspaceSnapshot snapshot, CancellationToken cancellationToken)
    {
        foreach (var obsoleteId in _windows.Keys.Where(id => snapshot.Sessions.All(session => session.Id != id)).ToArray())
        {
            if (!_closingSessions.Add(obsoleteId)) continue;
            await AwaitControllerAsync(() => DrainOverlayWindowAsync(obsoleteId));
            CloseWindow(obsoleteId);
        }

        foreach (var pair in _windows.ToArray())
        {
            var session = snapshot.Sessions.FirstOrDefault(item => item.Id == pair.Key);
            if (session is null) continue;
            if (_disposed || _closingSessions.Contains(pair.Key)) continue;
            RecordWindowBorrower(pair.Value);
            pair.Value.ApplySnapshot(snapshot);
            var generated = await AwaitControllerAsync(() => _actionCandidates.DiscoverAsync(session.Context, cancellationToken));
            if (_disposed || _closingSessions.Contains(pair.Key) || !_windows.TryGetValue(pair.Key, out var current) || !ReferenceEquals(current, pair.Value)) continue;
            var actions = OverlayContextActionCatalog.BuildFixed(session.Context)
                .Concat(generated)
                .GroupBy(action => action.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            pair.Value.SetActions(actions);
        }
    }

    private void OnRegistryChanged(object? sender, OverlayWorkspaceSnapshot snapshot)
    {
        if (_disposed) return;
        RunOnUi(() => RefreshAllAsync(snapshot, CancellationToken.None));
    }

    private void OnHotkeyPressed(object? sender, EventArgs e)
    {
        if (_disposed) return;
        RunOnUi(() => HandleHotkeyAsync(CancellationToken.None));
    }
    private async Task HandleHotkeyAsync(CancellationToken cancellationToken)
    {
        OverlayContextEnvelope? context = null;
        try { context = await AwaitControllerAsync(() => _contextCapture.CaptureAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            if (!_disposed) _notifications.Show("Overlay context unavailable", exception.Message, ToastKind.Warning, TimeSpan.FromSeconds(5));
        }
        if (context is null)
        {
            await AwaitControllerAsync(() => ToggleWorkspaceAsync(cancellationToken));
            return;
        }
        await AwaitControllerAsync(() => OpenNewGoAsync(context,
            context.Provenance.SourceApplication ?? context.Provenance.SourceWindow, cancellationToken));
    }

    private void QueueGeometryUpdate(Guid sessionId, OverlaySurfaceGeometry geometry)
    {
        if (_disposed || ControllerWork.IsRetiring || _closingSessions.Contains(sessionId)) return;
        CancelGeometryUpdate(sessionId);
        var cancellation = new CancellationTokenSource();
        _geometryUpdates[sessionId] = cancellation;
        _ = RunControllerAsync(() => PersistGeometryAsync(sessionId, geometry, cancellation));
    }

    private async Task PersistGeometryAsync(Guid sessionId, OverlaySurfaceGeometry geometry, CancellationTokenSource owner)
    {
        try
        {
            await AwaitControllerAsync(() => Task.Delay(250));
            if (_disposed || owner.IsCancellationRequested || !_geometryUpdates.TryGetValue(sessionId, out var current) || !ReferenceEquals(current, owner)) return;
            await AwaitControllerAsync(() => _registry.UpdateGeometryAsync(sessionId, geometry, CancellationToken.None));
        }
        catch (OperationCanceledException) when (owner.IsCancellationRequested)
        {
        }
        finally
        {
            if (_geometryUpdates.TryGetValue(sessionId, out var current) && ReferenceEquals(current, owner))
                _geometryUpdates.Remove(sessionId);
            owner.Dispose();
        }
    }

    private void CancelGeometryUpdate(Guid sessionId)
    {
        if (!_geometryUpdates.Remove(sessionId, out var cancellation)) return;
        cancellation.Cancel();
    }

    private bool IsPinned(Guid sessionId) =>
        _registry.Snapshot.Sessions.FirstOrDefault(session => session.Id == sessionId)?.IsPinned == true;

    private void CloseWindow(Guid sessionId)
    {
        CancelGeometryUpdate(sessionId);
        if (_windows.TryGetValue(sessionId, out var window))
        {
            ControllerSource(() => { window.CloseFromController(); return true; });
            PruneHealthyClosedOverlay(window);
        }
    }

    private async Task RunGuardedAsync(Func<Task> operation)
    {
        try
        {
            await AwaitControllerAsync(operation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (!_disposed) _notifications.Show("Overlay action failed", exception.Message, ToastKind.Error, TimeSpan.FromSeconds(10));
        }
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
#endif
