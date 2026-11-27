#if !ANDROID
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Browse;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    internal sealed partial class UnifiedWorkspaceHost
    {
        private IFilesNativeBoardOpenHost CreateFilesBoardOpenHost(BrowseAppContentRequest request,
            FilesNativeBrowserService browser, AuthenticatedResourceActor actor)
        {
            DemandCurrent();
            if (_actor != actor || request.ProfileId != actor.ProfileId ||
                request.Privacy != BrowserTabPrivacy.Standard || request.TabId == Guid.Empty ||
                !ReferenceEquals(browser, _services.GetRequiredService<FilesNativeBrowserService>()))
                throw new UnauthorizedAccessException("Files must open Boards through the original tab, profile and service graph.");
            var reads = _boardReadSource ?? throw new UnauthorizedAccessException("The original Files board reader is unavailable.");
            var approvals = _services.GetService<HomeNativeApprovalWindowOwner>();
            if (approvals is not null && !approvals.IsBoundToOriginalComposition(_home))
                throw new UnauthorizedAccessException("Retain the original Home review-window owner.");

            // Prepared leases move to the same window's existing Boards registry. They
            // cannot borrow the originating Files view's shorter presentation lifetime.
            bool Alive() => IsCurrent;
            void HostScope(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                () => { DemandCurrent(); body(); return true; });
            void Retain(Task actual) { _ = Track(actual); }
            Task? Queue(AppContentAddress address, Task originalSettlement)
            {
                DemandCurrent();
                if (OriginalClose is not null || _workspace is not { } workspace ||
                    !workspace.TryQueueNavigationOriginal(new(request.TabId, address, OpenInNewTab: true),
                        originalSettlement, out var navigation)) return null;
                return navigation;
            }
            return new NativeFilesBoardOpenHost(browser, actor, reads,
                approvals is null ? null : _boardWriteSource, Alive, ObserveAppearanceAuthorityAsync,
                approvals is null ? null : (authorization, token) => PresentHomeLibraryReviewAsync(approvals, authorization, token),
                HostScope, Retain, Queue);
        }
    }
}
#endif
