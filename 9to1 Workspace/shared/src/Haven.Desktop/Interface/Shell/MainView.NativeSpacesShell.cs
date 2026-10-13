using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell.NativePresentation;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    // This frame belongs to the application view, never to an outgoing Chat page.
    // Its navigation can therefore await that page's real close without a cycle.
    private NativeSpacesShell? _nativeSpacesShell;
    private bool IsNativeSpacesFrameMounted => _nativeSpacesShell is not null
        && ReferenceEquals(PageContent.Content, _nativeSpacesShell);

    private void PresentNativePageContent(object? page)
    {
        if (PageContent is null) return;
        page = NativeLogicalPage(page);
        var useFrame = !IsDisposed && _edition == HavenShellEdition.New && !IsSplitView
            && page is NewChatPage && _nativeChatSidebar is not null;
        if (useFrame && _nativeSpacesShell is null)
        {
            Func<Task>? account = null;
#if !ANDROID
            if (App.Services?.GetService<Haven.Desktop.Accounts.NativeCakeAccountUiOwner>() is { } owner)
                account = () => owner.OpenAsync(TopLevel.GetTopLevel(this) as Window);
#endif
            _nativeSpacesShell = new NativeSpacesShell(_nativeChatSidebar!,
                App.Services?.GetService<CuiThemePreferenceContext>(),
                new(account, ShowUniversalSearchAsync, () =>
                {
                    TopRail.ShowNotifications();
                    return Task.CompletedTask;
                }));
        }
        PresentNativeSpacesContent(PageContent, NativeSidebarHost, _nativeChatSidebar,
            _nativeSpacesShell, page, useFrame, restoreNavigation: !IsDisposed);
        ApplicationContentHost.Margin = useFrame ? new Thickness(0) : new Thickness(24, 0, 24, 20);
        ApplicationContentHost.ColumnSpacing = useFrame ? 0 : 18;
    }

    internal static object? NativeLogicalPage(object? content) =>
        content is NativeSpacesShell frame ? frame.ActiveWorkspace : content;

    /// <summary>The actual application route mount, shared by tab, split and popup
    /// paths. Only the application's existing hosts may release a borrowed child.</summary>
    internal static void PresentNativeSpacesContent(ContentControl pageHost, ContentControl sidebarHost,
        NativeChatSidebar? navigation, NativeSpacesShell? shell, object? logicalPage, bool useFrame,
        bool restoreNavigation = true)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandNativeSpacesDestination(pageHost, shell, logicalPage);
        if (useFrame)
        {
            if (logicalPage is not NewChatPage chat || shell is null || navigation is null)
                throw new InvalidOperationException("The canonical Spaces frame requires its actual Chat and navigation owners.");
            if (navigation.Parent is not null && !ReferenceEquals(sidebarHost.Content, navigation)
                && shell.ActiveWorkspace is null)
                throw new InvalidOperationException("The original navigation is still owned by another platform host.");
            if (!ReferenceEquals(shell.ActiveWorkspace, chat))
            {
                if (ReferenceEquals(pageHost.Content, chat)) SetNativePlatformContent(pageHost, null);
                shell.SetWorkspace(null);
                if (ReferenceEquals(sidebarHost.Content, navigation)) SetNativePlatformContent(sidebarHost, null);
                shell.SetWorkspace(chat);
            }
            SetNativePlatformContent(pageHost, shell);
            return;
        }
        shell?.SetWorkspace(null);
        shell?.ReleaseNavigation();
        SetNativePlatformContent(pageHost, logicalPage);
        if (restoreNavigation && navigation is not null) SetNativePlatformContent(sidebarHost, navigation);
    }

    private void DemandNativePageMountable(object? page)
    {
        if (PageContent is not null) DemandNativeSpacesDestination(PageContent, _nativeSpacesShell, NativeLogicalPage(page));
    }

    private static void DemandNativeSpacesDestination(ContentControl pageHost, NativeSpacesShell? shell, object? page)
    {
        if (page is Control destination && (destination.Parent is not null || destination.GetVisualParent() is not null)
            && !ReferenceEquals(pageHost.Content, destination)
            && !ReferenceEquals(shell?.ActiveWorkspace, destination))
            throw new InvalidOperationException("The destination page is still owned by another platform host.");
    }

    internal static void SetNativePlatformContent(ContentControl host, object? content)
    {
        if (ReferenceEquals(host.Content, content)) return;
        // ContentPresenter detaches visual parentage lazily. Force this existing
        // host to release its original child before a new host can acquire it.
        host.Content = null;
        host.Presenter?.UpdateChild();
        host.Content = content;
        host.Presenter?.UpdateChild();
    }
}
