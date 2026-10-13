using System.Runtime.Versioning;
using Avalonia.Threading;
using HavenOS.Home.Core;
using NineToOne.Web.Spaces.Storage;

namespace NineToOne.Web.Spaces;

public sealed partial class BrowserSpacesFeature
{
    [SupportedOSPlatform("browser")]
    internal static void Register(BrowserSurfaceRegistry surfaces, BrowserPrivateTaskStorage sameOwner,
        Action<HomeFeatureNavigationRequest> navigate)
    {
        var chatRuntime = new Chat.BrowserChatRuntime(sameOwner);
        var enrolled = surfaces.RegisterPrivateLifetime(chatRuntime);
        if (!enrolled.Succeeded) { chatRuntime.RevokePrivateContext(); throw new InvalidOperationException(enrolled.Message); }
        var feature = new BrowserSpacesFeature(() => sameOwner.Spaces, sameOwner.DemandPrivateContextCurrent,
            action => Dispatcher.UIThread.InvokeAsync(action).GetTask(), navigate,
            () => chatRuntime.Backend, async token => await sameOwner.Attachments.SelectFileAsync(token));
        var result = surfaces.Register(feature, feature.Render, BrowserSurfaceScope.PrivateContext);
        if (!result.Succeeded) { feature.RevokePrivateContext(); throw new InvalidOperationException(result.Message); }
    }

}
