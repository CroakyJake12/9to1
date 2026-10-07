using Avalonia.Threading;
using System.Runtime.Versioning;
using NineToOne.Web.Spaces.Storage;
using NineToOne.Web.Accounts;
using NineToOne.Web.Wave;
using NineToOne.Web.Picture;
using NineToOne.Web.Services;
using NineToOne.Web.Write;
using NineToOne.Web.Write.Storage;
using NineToOne.Web.Productivity.Present;
using NineToOne.Web.Productivity.Present.Storage;
using NineToOne.Web.Productivity.Boards;
using Haven.Application;
using Haven.Infrastructure;

namespace NineToOne.Web;

/// <summary>Browser-owned local features and explicitly unconfigured account presentation.</summary>
internal static class BrowserFeatureComposition
{
    [SupportedOSPlatform("browser")]
    public static AccountSettingsFeature Register(BrowserSurfaceRegistry registry)
    {
        var wave = WaveBrowserFeature.Register(registry);
        if (!wave.Succeeded) throw new InvalidOperationException(wave.Message);
        var picture = PictureBrowserFeature.Register(registry);
        if (!picture.Succeeded) throw new InvalidOperationException(picture.Message);
        var write = WriteBrowserFeature.Register(registry,
            new IndexedDbNotesRepository(new BrowserNotesTransport(), new NotesDocumentValidator()),
            new WriteNativeDocumentPackageStore(), Program.ReduceMotion,
            error => error is NotesCommitOutcomeUnknownException, new BrowserWritePackageBroker());
        if (!write.Succeeded) throw new InvalidOperationException(write.Message);
        var present = PresentBrowserFeature.Register(registry,
            new IndexedDbPresentRepository(new BrowserPresentTransport(), PresentRepository.ValidateForSave),
            Program.ReduceMotion, error => error is PresentCommitOutcomeUnknownException);
        if (!present.Succeeded) throw new InvalidOperationException(present.Message);
        var boards = BoardsBrowserRegistration.Register(registry,
            new IndexedDbNotesRepository(new BrowserNotesTransport(), new NotesDocumentValidator()),
            Program.ReduceMotion, error => error is NotesCommitOutcomeUnknownException);
        if (!boards.Succeeded) throw new InvalidOperationException(boards.Message);
        return RegisterPrivateAccountSettings(registry);
    }

    [SupportedOSPlatform("browser")]
    public static AccountSettingsFeature RegisterPrivateAccountSettings(BrowserSurfaceRegistry registry)
    {
        BrowserPrivateTaskStorageEnrollment.Register(registry);
        var samePrivateOwner = BrowserPrivateTaskStorageEnrollment.GetCurrent(registry);
        BrowserHomeDashboardLayoutStore.Register(registry, samePrivateOwner.DemandPrivateContextCurrent);
        var settings = AccountSettingsFeature.CreateForBrowser(action => Dispatcher.UIThread.InvokeAsync(action).GetTask(),
            BrowserAccountSignIn.IsAvailable ? BrowserAccountSignIn.RequestAsync : null);
        var registered = registry.Register(settings, settings.Render);
        if (!registered.Succeeded) { settings.Dispose(); throw new InvalidOperationException(registered.Message); }
        return settings;
    }
}
