using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Shell;
using Haven.Desktop.Views.Shell.NativePresentation;

namespace Haven.Desktop.Tests;

public sealed partial class TemporaryConversationPageTests
{
    [AvaloniaFact]
    public async Task Actual_application_frame_navigation_joins_outgoing_page_without_retiring_its_initiating_sidebar()
    {
        await using var first = new Fixture();
        await using var second = new Fixture();
        await first.Page.RefreshModelsAsync();
        await second.Page.RefreshModelsAsync();
        await first.DrainObservedPageWorkAsync();
        await second.DrainObservedPageWorkAsync();
        var entered = NewSource();
        var release = NewSource();
        var pageHost = new ContentControl();
        var sidebarHost = new ContentControl();
        NativeSpacesShell? frame = null;
        NativeChatSidebar? sidebar = null;
        var stateDirectory = Path.Combine(Path.GetTempPath(), "astra-independent-spaces-frame-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateDirectory);
        try
        {
            sidebar = CreateFrameSidebar(stateDirectory, async () =>
            {
                entered.TrySetResult();
                await release.Task;
                await first.Page.CloseAndDrainAsync();
                MainView.PresentNativeSpacesContent(pageHost, sidebarHost, sidebar, frame, second.Page, true);
            });
            frame = new NativeSpacesShell(sidebar);
            await sidebar.RefreshAsync();
            MainView.SetNativePlatformContent(sidebarHost, sidebar);
            MainView.SetNativePlatformContent(pageHost, first.Page);
            var window = CreateFrameWindow(pageHost, sidebarHost);
            try
            {
                window.Show(); window.UpdateLayout();
                MainView.PresentNativeSpacesContent(pageHost, sidebarHost, sidebar, frame, first.Page, true);
                window.UpdateLayout();
                Assert.Same(frame, pageHost.Content);
                Assert.Same(first.Page, MainView.NativeLogicalPage(pageHost.Content));
                AssertStandaloneHeader(first, false);
                var newChat = sidebar.Navigation.Root.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Name == "new-sidebar-chat");
                Assert.True(newChat.IsEffectivelyVisible);
                Assert.True(newChat.IsEffectivelyEnabled);
                newChat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                var originalNavigation = sidebar.Navigation.WhenActionsIdleAsync();
                Assert.False(originalNavigation.IsCompleted);
                Assert.Null(sidebar.OriginalClose);
                Assert.Same(first.Page, frame.ActiveWorkspace);
                release.TrySetResult();
                await originalNavigation.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Same(frame, pageHost.Content);
                Assert.Same(second.Page, frame.ActiveWorkspace);
                Assert.Null(first.Page.Parent);
                Assert.Null(sidebar.OriginalClose);
                AssertStandaloneHeader(second, false);
                // The same navigation remains operational after the outgoing page's
                // complete close; no replacement sidebar is substituted in the test.
                await sidebar.RefreshAsync();
                window.UpdateLayout();
                CaptureFrameEvidence(window, "actual-mainview-mount-controlled-frame-1530.png");
                await second.Page.CloseAndDrainAsync();
                await sidebar.CloseAndDrainAsync();
                await frame.CloseAndDrainAsync();
                MainView.PresentNativeSpacesContent(pageHost, sidebarHost, sidebar, frame, null, false, restoreNavigation: false);
                Assert.Null(second.Page.Parent);
                Assert.Null(sidebar.Parent);
            }
            finally
            {
                release.TrySetResult();
                // All original owners are joined before the platform window disappears.
                await first.Page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await second.Page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await sidebar.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await frame.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
                window.Content = null; window.Close();
            }
        }
        finally { Directory.Delete(stateDirectory, recursive: true); }
    }

    [AvaloniaFact]
    public async Task Actual_application_mount_restores_standalone_header_and_preserves_original_page_through_popup_transfer()
    {
        await using var fixture = new Fixture();
        await fixture.Page.RefreshModelsAsync();
        await fixture.DrainObservedPageWorkAsync();
        var identity = fixture.Page.CurrentConversation;
        var pageHost = new ContentControl(); var sidebarHost = new ContentControl(); var popupHost = new ContentControl();
        var stateDirectory = Path.Combine(Path.GetTempPath(), "astra-independent-spaces-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stateDirectory);
        var sidebar = CreateFrameSidebar(stateDirectory, () => Task.CompletedTask);
        var frame = new NativeSpacesShell(sidebar);
        var window = CreateFrameWindow(pageHost, sidebarHost);
        var popup = new Window { Width = 800, Height = 650, Content = popupHost };
        try
        {
            await sidebar.RefreshAsync();
            MainView.SetNativePlatformContent(sidebarHost, sidebar);
            MainView.PresentNativeSpacesContent(pageHost, sidebarHost, sidebar, frame, fixture.Page, true);
            window.Show(); window.UpdateLayout();
            AssertStandaloneHeader(fixture, false);
            // This is the production seam used for split/standalone presentation.
            MainView.PresentNativeSpacesContent(pageHost, sidebarHost, sidebar, frame, fixture.Page, false);
            window.UpdateLayout();
            Assert.Same(fixture.Page, pageHost.Content);
            Assert.Same(sidebar, sidebarHost.Content);
            Assert.Null(frame.ActiveWorkspace);
            AssertStandaloneHeader(fixture, true);
            MainView.PresentNativeSpacesContent(pageHost, sidebarHost, sidebar, frame, null, false);
            MainView.SetNativePlatformContent(popupHost, fixture.Page);
            popup.Show(); popup.UpdateLayout();
            Assert.Throws<InvalidOperationException>(() => MainView.PresentNativeSpacesContent(
                pageHost, sidebarHost, sidebar, frame, fixture.Page, true));
            Assert.Same(fixture.Page, popupHost.Content);
            Assert.Null(pageHost.Content);
            MainView.SetNativePlatformContent(popupHost, null);
            MainView.PresentNativeSpacesContent(pageHost, sidebarHost, sidebar, frame, fixture.Page, true);
            window.UpdateLayout();
            Assert.Same(frame, pageHost.Content);
            Assert.Same(fixture.Page, frame.ActiveWorkspace);
            Assert.Same(identity, fixture.Page.CurrentConversation);
            AssertStandaloneHeader(fixture, false);
            Assert.Null(sidebar.OriginalClose);
        }
        finally
        {
            await fixture.Page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await sidebar.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await frame.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(5));
            MainView.SetNativePlatformContent(popupHost, null);
            MainView.PresentNativeSpacesContent(pageHost, sidebarHost, sidebar, frame, null, false, restoreNavigation: false);
            window.Content = null; window.Close(); popup.Content = null; popup.Close();
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static NativeChatSidebar CreateFrameSidebar(string directory, Func<Task> start) => new(
        Probe<IConversationRepository>((method, _) => method.Name == nameof(IConversationRepository.GetRecentAsync)
            ? Task.FromResult<IReadOnlyList<Conversation>>([])
            : throw new InvalidOperationException("Unexpected original sidebar repository source: " + method.Name)),
        Probe<IContainerRepository>((method, _) => method.Name == nameof(IContainerRepository.GetByModeAsync)
            ? Task.FromResult<IReadOnlyList<ContainerDefinition>>([])
            : throw new InvalidOperationException("Unexpected original sidebar container source: " + method.Name)),
        _ => throw new InvalidOperationException("This test opens only a fresh actual page."),
        (_, _) => start(), _ => throw new InvalidOperationException("This test does not open a group."),
        stateStore: new NativeChatUiStateStore(Path.Combine(directory, "sidebar.json")));

    private static Window CreateFrameWindow(ContentControl pageHost, ContentControl sidebarHost)
    {
        var host = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(pageHost, 1); host.Children.Add(sidebarHost); host.Children.Add(pageHost);
        return new Window { Width = 1530, Height = 946, Content = host };
    }

    private static void AssertStandaloneHeader(Fixture fixture, bool expected)
    {
        Assert.True(fixture.Page.CuiPresentation.TryGetValue("chat.standaloneHeader", out var value));
        Assert.Equal(expected, value);
    }

    private static void CaptureFrameEvidence(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        using var image = window.CaptureRenderedFrame(); Assert.NotNull(image);
        image.Save(Path.Combine(directory, name));
    }
}
