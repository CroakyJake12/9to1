using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Application.Call;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell.VoiceVision;
using Haven.Desktop.Views.Shell.NativePresentation;
using Haven.Desktop.Views.Shell.Overlays;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private bool _betaOverlayLifecycleWired;
    private bool _betaOverlaysAttached;
    private bool _redirectingLegacyPresentation;
    private object? _lastNonCallContent;
    private object? _legacyProjectsContent;
    private TranslateTransform? _globalCallTranslation;
    private GlobalCallWidget? _globalCallWidget;
    private ChatExecutionStatusControl? _globalExecutionStatus;
    private InChatCallWidgetViewModel? _globalCallViewModel;
    private NativeProjectsPage? _nativeProjectsPage;
    private readonly NativeProjectUiStateStore _nativeProjectUiStateStore = new();

    // The shared Voice &amp; Vision CUI host (pill/full). One adapter owns one canonical session
    // and borrows real composition state (conversation/space/model) from the active Chat surface;
    // it never fabricates a default model or a permissive local-only claim.
    private VoiceVisionHostAdapter? _voiceAdapter;
    private Control? _voicePillControl;
    private Control? _voiceFullSurface;
    private bool _voiceSurfaceShowingFull;

    public void OpenVoiceSession()
    {
        AttachBetaOverlays();

        if (_globalCallViewModel is null || _globalCallWidget is null)
        {
            return;
        }

        _globalCallViewModel.Open();
        _globalCallWidget.IsVisible = true;
    }

    private async Task OpenVoiceSessionFromActionAsync()
    {
        AttachBetaOverlays();
        if (_globalCallViewModel is null)
        {
            return;
        }

        Guid? conversationId = null;
        ModelDescriptor? selectedModel = null;
        if (CurrentPage is NewChatPage chat)
        {
            conversationId = chat.ConversationId;
            selectedModel = chat.SelectedModel;
        }

        selectedModel ??= CurrentChat.SelectedModel;
        if (selectedModel is null)
        {
            try
            {
                var models = await _ollama.GetModelsAsync(CancellationToken.None);
                selectedModel = models.FirstOrDefault(model => model.Supports(ToolCapability.Text))
                    ?? models.FirstOrDefault();
            }
            catch
            {
                // The model card and disabled Start button expose the unavailable
                // state without navigating away or fabricating a default model.
            }
        }

        // Voice is an application action, not a navigation destination. It can
        // float over Home, Projects, Study, Research, or any other current page.
        _globalCallViewModel.AttachConversation(conversationId, selectedModel);

        if (_globalCallViewModel.IsOpen && !_globalCallViewModel.IsActive)
        {
            _globalCallViewModel.Close();
            return;
        }

        OpenVoiceSession();
    }

    /// <summary>Resolves the shared Voice &amp; Vision runtime from the DI container and builds the
    /// single canonical host adapter once, borrowing real composition state from the active Chat
    /// surface. Returns null when no runtime is registered so the voice toggle truthfully stays
    /// absent.</summary>
    private VoiceVisionHostAdapter? EnsureVoiceAdapter()
    {
        if (_voiceAdapter is not null) return _voiceAdapter;
        var runtime = Haven.Desktop.App.Services?.GetService<IVoiceVisionRuntime>();
        if (runtime is null) return null;

        var adapter = new VoiceVisionHostAdapter(
            runtime,
            conversationProvider: () => VoiceConversationId(),
            startOptionsProvider: BuildVoiceStartOptions,
            spaceIdProvider: () => (CurrentPage as NewChatPage)?.CurrentConversation.SpaceId);
        adapter.RequestFullSurfaceAsync = ShowFullVoiceSurfaceAsync;
        adapter.PresentationClosedAsync = async () =>
        {
            HideVoiceOverlay();
            await Task.CompletedTask;
        };
        _voiceAdapter = adapter;
        return adapter;
    }

    private Guid? VoiceConversationId() =>
        CurrentPage switch
        {
            NewChatPage chat => chat.ConversationId,
            ChatSpacePage space => space.InitialConversationId,
            _ => null
        };

    private CallStartOptions BuildVoiceStartOptions()
    {
        // Real production start options only: a selected model from the active NewChatPage. There
        // is no fabricated default model; without one a Voice session cannot start truthfully.
        var model = (CurrentPage as NewChatPage)?.SelectedModel;
        if (model is null)
            throw new InvalidOperationException("No Voice-ready model is selected.");
        return new CallStartOptions(model);
    }

    /// <summary>Presents the shared Voice &amp; Vision pill for the current conversation in the shell
    /// floating layer. It reuses the one canonical adapter/runtime and never starts a second one.</summary>
    private void ShowVoicePill()
    {
        AttachBetaOverlays();
        var adapter = EnsureVoiceAdapter();
        if (adapter is null) return;

        if (_voicePillControl is null)
        {
            _voicePillControl = adapter.LoadPill();
            _voicePillControl.HorizontalAlignment = HorizontalAlignment.Right;
            _voicePillControl.VerticalAlignment = VerticalAlignment.Bottom;
            _voicePillControl.Margin = new Thickness(20);
            _voicePillControl.SetValue(Panel.ZIndexProperty, 40);
            NativeOverlayLayer.Children.Add(_voicePillControl);
        }

        if (_voiceFullSurface is not null) _voiceFullSurface.IsVisible = false;
        _voiceSurfaceShowingFull = false;
        _voicePillControl.IsVisible = true;
    }

    /// <summary>Expands the shared Voice &amp; Vision pill into the full session surface using the same
    /// canonical adapter/runtime (never a second runtime).</summary>
    private async Task ShowFullVoiceSurfaceAsync()
    {
        var adapter = EnsureVoiceAdapter();
        if (adapter is null) return;

        if (_voiceFullSurface is null)
        {
            _voiceFullSurface = adapter.LoadSession();
            _voiceFullSurface.HorizontalAlignment = HorizontalAlignment.Stretch;
            _voiceFullSurface.VerticalAlignment = VerticalAlignment.Stretch;
            _voiceFullSurface.SetValue(Panel.ZIndexProperty, 45);
            NativeOverlayLayer.Children.Add(_voiceFullSurface);
        }

        if (_voicePillControl is not null) _voicePillControl.IsVisible = false;
        _voiceFullSurface.IsVisible = true;
        _voiceSurfaceShowingFull = true;
        await Task.CompletedTask;
    }

    private void HideVoiceOverlay()
    {
        if (_voicePillControl is not null) _voicePillControl.IsVisible = false;
        if (_voiceFullSurface is not null) _voiceFullSurface.IsVisible = false;
        _voiceSurfaceShowingFull = false;
    }

    /// <summary>Closes the Voice presentation WITHOUT ending the session: the shared projection
    /// leaves the surface and the adapter acknowledges close after loader settlement. End is only
    /// the explicit End control inside the surface.</summary>
    private async Task CloseVoicePresentationAsync()
    {
        AttachBetaOverlays();
        if (_voiceAdapter is not null) await _voiceAdapter.ClosePresentationAsync();
        else HideVoiceOverlay();
    }

    private void AttachBetaOverlays()
    {
        if (!_betaOverlayLifecycleWired)
        {
            _betaOverlayLifecycleWired = true;
            AttachedToVisualTree += OnBetaOverlayAttached;
            DetachedFromVisualTree += OnBetaOverlayDetached;
        }

        DisableLegacyOverlayHost();
        HideLegacyShellContextBar();

        if (_betaOverlaysAttached)
        {
            RedirectLegacyPresentation(PageContent.Content);
            return;
        }

        _betaOverlaysAttached = true;

        _globalCallViewModel = new InChatCallWidgetViewModel(
            _callCoordinator,
            _conversations);

        _globalCallWidget = new GlobalCallWidget(_globalCallViewModel);
        _globalCallWidget.DragDelta += OnGlobalCallDragDelta;
        _globalCallTranslation = new TranslateTransform();
        _globalCallWidget.HorizontalAlignment = HorizontalAlignment.Right;
        _globalCallWidget.VerticalAlignment = VerticalAlignment.Bottom;
        _globalCallWidget.Margin = new Thickness(20);
        _globalCallWidget.RenderTransform = _globalCallTranslation;
        _globalCallWidget.IsVisible = false;
        _globalCallWidget.SetValue(Panel.ZIndexProperty, 30);
        NativeOverlayLayer.Children.Add(_globalCallWidget);

        _globalExecutionStatus = new ChatExecutionStatusControl
        {
            MaxWidth = 720,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(20)
        };
        _globalExecutionStatus.Snapshot = _sessions.CurrentExecution;
        _globalExecutionStatus.SetValue(Panel.ZIndexProperty, 20);
        NativeOverlayLayer.Children.Add(_globalExecutionStatus);

        _sessions.ExecutionChanged += OnExecutionChanged;
        _globalCallViewModel.PropertyChanged += OnGlobalCallPropertyChanged;
        PageContent.PropertyChanged += OnPageContentPropertyChanged;

        RedirectLegacyPresentation(PageContent.Content);
    }

    private void OnGlobalCallDragDelta(object? sender, Vector delta)
    {
        if (_globalCallTranslation is null || _globalCallWidget is null)
        {
            return;
        }

        var availableWidth = Math.Max(0, NativeOverlayLayer.Bounds.Width - _globalCallWidget.Bounds.Width - 40);
        var availableHeight = Math.Max(0, NativeOverlayLayer.Bounds.Height - _globalCallWidget.Bounds.Height - 40);
        _globalCallTranslation.X = Math.Clamp(_globalCallTranslation.X + delta.X, -availableWidth, 0);
        _globalCallTranslation.Y = Math.Clamp(_globalCallTranslation.Y + delta.Y, -availableHeight, 0);
    }

    private void DisableLegacyOverlayHost()
    {
        OverlayHost.IsVisible = false;
        OverlayHost.IsHitTestVisible = false;
        OverlayHost.Child = null;
        OverlayHost.Background = null;
    }

    private void HideLegacyShellContextBar()
    {
        ShellContextBar.IsVisible = false;
        ShellContextBar.IsHitTestVisible = false;
        ShellContextBar.Opacity = 0;
        ShellContextBar.Width = 0;
        ShellContextBar.Height = 0;
        ShellContextBar.Margin = new Thickness(0);
    }

    private void OnGlobalCallPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(InChatCallWidgetViewModel.IsVisible) &&
            e.PropertyName is not nameof(InChatCallWidgetViewModel.IsOpen) &&
            e.PropertyName is not nameof(InChatCallWidgetViewModel.IsActive))
        {
            return;
        }

        if (_globalCallWidget is not null && _globalCallViewModel is not null)
        {
            _globalCallWidget.IsVisible = _globalCallViewModel.IsVisible;
        }
    }

    private void OnPageContentPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != ContentControl.ContentProperty || _redirectingLegacyPresentation)
        {
            return;
        }

        RedirectLegacyPresentation(PageContent.Content);
    }

    private void RedirectLegacyPresentation(object? content)
    {
        if (content is null || _redirectingLegacyPresentation)
        {
            return;
        }

        var destination = ClassifyLegacyPresentation(content);
        switch (destination)
        {
            case NativePresentationDestination.ChatCallWidget:
                RedirectLegacyCallPage();
                return;

            case NativePresentationDestination.Projects:
                ReplaceLegacyProjectsPage(content);
                return;

            case NativePresentationDestination.Chat:
                // OpenNewChatAsync replaces the visible content before its first await.
                // NewChatPage is exempt from legacy classification, so this cannot recurse.
                _ = OpenNewChatAsync();
                return;

            default:
                if (!ReferenceEquals(content, _nativeProjectsPage))
                {
                    DisposeNativeProjectsPage();
                }

                _lastNonCallContent = content;
                return;
        }
    }

    private void RedirectLegacyCallPage()
    {
        _redirectingLegacyPresentation = true;
        try
        {
            PageContent.Content = _lastNonCallContent;
            // A retired/restored Call tab must never activate Voice. Voice is opened
            // exclusively from Actions; the legacy route is discarded silently.
            if (_lastNonCallContent is null)
            {
                _ = OpenGoAsync();
            }
        }
        finally
        {
            _redirectingLegacyPresentation = false;
        }
    }

    private void ReplaceLegacyProjectsPage(object legacyContent)
    {
        if (legacyContent is NativeProjectsPage)
        {
            _lastNonCallContent = legacyContent;
            return;
        }

        var nativePage = CreateNativeProjectsPage(legacyContent);

        _redirectingLegacyPresentation = true;
        try
        {
            PageContent.Content = nativePage;
            _lastNonCallContent = nativePage;
        }
        finally
        {
            _redirectingLegacyPresentation = false;
        }
    }

    private NativeProjectsPage CreateNativeProjectsPage(object source)
    {
        _legacyProjectsContent = source;
        DisposeNativeProjectsPage();

        var nativePage = new NativeProjectsPage(
            source,
            ReadFallbackProjects,
            OpenProjectCreatorFallbackAsync,
            OpenProjectFallbackAsync,
            ArchiveProjectFallbackAsync,
            _nativeProjectUiStateStore);

        _nativeProjectsPage = nativePage;
        return nativePage;
    }

    private static NativePresentationDestination ClassifyLegacyPresentation(object content)
    {
        var surfaceName = content.GetType().Name;
        var dataContextName = content is Control control
            ? control.DataContext?.GetType().Name
            : surfaceName;

        return NativePresentationRoutePolicy.Classify(surfaceName, dataContextName);
    }

    private IEnumerable<object> ReadFallbackProjects()
    {
        return NativePresentationReflection.ReadCollection(
            this,
            "Projects",
            "ProjectItems",
            "ProjectCards",
            "Workspaces",
            "Containers");
    }

    private async Task OpenProjectCreatorFallbackAsync()
    {
        var handled = await NativePresentationReflection.ExecuteCommandAsync(
            this,
            null,
            "NewProjectCommand",
            "CreateProjectCommand",
            "OpenProjectCreatorCommand",
            "NewContainerCommand");

        if (handled)
        {
            return;
        }

        var invocation = await NativePresentationReflection.InvokeAsync(
            this,
            ["OpenProjectCreatorAsync", "OpenProjectCreator", "OpenNewContainer", "CreateProjectAsync"],
            Array.Empty<object?>());

        if (!invocation.Invoked)
        {
            throw new InvalidOperationException("The project creator route is unavailable.");
        }
    }

    private async Task OpenProjectFallbackAsync(object project)
    {
        var handled = await NativePresentationReflection.ExecuteCommandAsync(
            this,
            project,
            "OpenProjectCommand",
            "SelectProjectCommand",
            "OpenContainerCommand",
            "SelectContainerCommand");

        if (handled)
        {
            return;
        }

        var invocation = await NativePresentationReflection.InvokeAsync(
            this,
            ["OpenProjectAsync", "OpenProject", "OpenContainerAsync", "OpenContainer"],
            project);

        if (!invocation.Invoked)
        {
            throw new InvalidOperationException("The selected project could not be opened.");
        }
    }

    private async Task ArchiveProjectFallbackAsync(object project)
    {
        var handled = await NativePresentationReflection.ExecuteCommandAsync(
            this,
            project,
            "ArchiveProjectCommand",
            "ArchiveContainerCommand");

        if (handled)
        {
            return;
        }

        var invocation = await NativePresentationReflection.InvokeAsync(
            this,
            ["ArchiveProjectAsync", "ArchiveProject", "ArchiveContainerAsync", "ArchiveContainer"],
            project);

        if (!invocation.Invoked)
        {
            throw new InvalidOperationException("The selected project could not be archived.");
        }
    }

    private void DisposeNativeProjectsPage()
    {
        _nativeProjectsPage?.Dispose();
        _nativeProjectsPage = null;
    }

    private void DetachBetaOverlays()
    {
        if (!_betaOverlaysAttached)
        {
            DisableLegacyOverlayHost();
            return;
        }

        _betaOverlaysAttached = false;
        _sessions.ExecutionChanged -= OnExecutionChanged;
        PageContent.PropertyChanged -= OnPageContentPropertyChanged;

        if (_globalCallViewModel is not null)
        {
            _globalCallViewModel.PropertyChanged -= OnGlobalCallPropertyChanged;
        }

        if (_globalCallWidget is not null) NativeOverlayLayer.Children.Remove(_globalCallWidget);
        if (_globalExecutionStatus is not null) NativeOverlayLayer.Children.Remove(_globalExecutionStatus);

        DisposeVoiceOverlay();

        DisposeNativeProjectsPage();
        _legacyProjectsContent = null;

        if (_globalCallWidget is not null)
        {
            _globalCallWidget.DragDelta -= OnGlobalCallDragDelta;
        }

        _globalCallWidget?.Dispose();
        _globalCallViewModel?.Dispose();
        _globalExecutionStatus?.Dispose();

        _globalCallTranslation = null;
        _globalCallWidget = null;
        _globalCallViewModel = null;
        _globalExecutionStatus = null;

        DisableLegacyOverlayHost();
    }

    /// <summary>Detaches and disposes the Voice &amp; Vision overlay (pill/full) and its canonical
    /// adapter together with the overlay layer. Closing the presentation never ends the session;
    /// this disposal tears down the host owned by the overlay lifetime.</summary>
    private void DisposeVoiceOverlay()
    {
        if (_voicePillControl is not null)
        {
            NativeOverlayLayer.Children.Remove(_voicePillControl);
            _voicePillControl = null;
        }
        if (_voiceFullSurface is not null)
        {
            NativeOverlayLayer.Children.Remove(_voiceFullSurface);
            _voiceFullSurface = null;
        }
        if (_voiceAdapter is not null)
        {
            _voiceAdapter.Dispose();
            _voiceAdapter = null;
        }
        _voiceSurfaceShowingFull = false;
    }

    private void OnExecutionChanged(ChatExecutionSnapshot snapshot)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_globalExecutionStatus is not null)
            {
                _globalExecutionStatus.Snapshot = snapshot;
            }
        });
    }

    private void OnBetaOverlayAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        AttachBetaOverlays();
    }

    private void OnBetaOverlayDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        DetachBetaOverlays();
    }
}
