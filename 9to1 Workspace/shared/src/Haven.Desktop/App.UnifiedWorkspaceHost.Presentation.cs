#if !ANDROID
using System.ComponentModel;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Browse;

namespace Haven.Desktop;

public sealed partial class App
{
    internal sealed partial class UnifiedWorkspaceHost
    {
        internal static CuiScenePresentationScope PresentationForAddress(AppContentAddress address)
        {
            if (address.Namespace == AppContentNamespace.Browse && address.Authority is "home" or "newtab")
                return new("Home", "app.home");
            if (address.Namespace == AppContentNamespace.NativeApp)
            {
                // Resolve the real registered app identity, never an address draft,
                // tab title or inferred model choice. Build keeps its canonical dev route.
                var mode = BuiltInModeSeed.Modes.FirstOrDefault(item => item.Key == address.Authority);
                var app = mode is null ? null : CuiGlowAppAccentCatalog.Find(mode.Key)
                    ?? CuiGlowAppAccentCatalog.Find(mode.Name);
                if (app is not null) return new(app.Surface, app.AppId);
            }
            return new("Browse", "app.browse");
        }

        private void OnSelectedAppPresentationChanged(object? sender, PropertyChangedEventArgs args) =>
            RefreshSelectedAppPresentation();

        private void RefreshSelectedAppPresentation()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsCurrent || _scene is null || _workspace is not { IsOriginalPublicationCurrent: true } workspace) return;
            _scene.UpdatePresentationScope(PresentationForAddress(workspace.State.SelectedTab.Address));
        }
    }
}
#endif
