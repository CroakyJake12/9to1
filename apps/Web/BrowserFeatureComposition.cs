using Avalonia.Threading;
using System.Runtime.Versioning;
using NineToOne.Web.Accounts;
using NineToOne.Web.Wave;
using NineToOne.Web.Services;
using NineToOne.Web.Write;
using NineToOne.Web.Write.Storage;
using Haven.Application;
using Haven.Infrastructure;

namespace NineToOne.Web;

/// <summary>Browser-owned local features and explicitly unconfigured account presentation.</summary>
internal static class BrowserFeatureComposition
{
    [SupportedOSPlatform("browser")]
    public static void Register(BrowserSurfaceRegistry registry)
    {
        var wave = WaveBrowserFeature.Register(registry);
        if (!wave.Succeeded) throw new InvalidOperationException(wave.Message);
        var write = WriteBrowserFeature.Register(registry,
            new IndexedDbNotesRepository(new BrowserNotesTransport(), new NotesDocumentValidator()),
            new WriteNativeDocumentPackageStore(), Program.ReduceMotion,
            error => error is NotesCommitOutcomeUnknownException, new BrowserWritePackageBroker());
        if (!write.Succeeded) throw new InvalidOperationException(write.Message);
        RegisterPrivateAccountSettings(registry);
    }

    [SupportedOSPlatform("browser")]
    public static void RegisterPrivateAccountSettings(BrowserSurfaceRegistry registry)
    {
        var settings = AccountSettingsFeature.CreateForBrowser(action => Dispatcher.UIThread.InvokeAsync(action).GetTask(),
            BrowserAccountSignIn.IsAvailable ? BrowserAccountSignIn.RequestAsync : null);
        var registered = registry.Register(settings, settings.Render);
        if (!registered.Succeeded) { settings.Dispose(); throw new InvalidOperationException(registered.Message); }
    }
}
