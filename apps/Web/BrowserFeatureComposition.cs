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
using HavenOS.Home.Core;
using NineToOne.Web.Spaces;

namespace NineToOne.Web;

/// <summary>Browser-owned local features and explicitly unconfigured account presentation.</summary>
internal static class BrowserFeatureComposition
{
    [SupportedOSPlatform("browser")]
    public static async Task<IBrowserHomeThemePreferences> CreateThemePreferencesAsync() =>
        await BrowserHomeThemePreferences.CreateAsync();

    [SupportedOSPlatform("browser")]
    public static AccountSettingsFeature Register(BrowserSurfaceRegistry registry, Action<HomeFeatureNavigationRequest> navigate)
    {
        var wave = WaveBrowserFeature.Register(registry);
        if (!wave.Succeeded) throw new InvalidOperationException(wave.Message);
        var picture = PictureBrowserFeature.Register(registry);
        if (!picture.Succeeded) throw new InvalidOperationException(picture.Message);
        var localNotes = new IndexedDbNotesRepository(new BrowserNotesTransport(), new NotesDocumentValidator());
        var write = WriteBrowserFeature.Register(registry, localNotes,
            new WriteNativeDocumentPackageStore(), Program.ReduceMotion,
            error => error is NotesCommitOutcomeUnknownException, new BrowserWritePackageBroker());
        if (!write.Succeeded) throw new InvalidOperationException(write.Message);
        BrowserHomeBriefingSources.Register(registry, localNotes);
        var present = PresentBrowserFeature.Register(registry,
            new IndexedDbPresentRepository(new BrowserPresentTransport(), PresentRepository.ValidateForSave),
            Program.ReduceMotion, error => error is PresentCommitOutcomeUnknownException);
        if (!present.Succeeded) throw new InvalidOperationException(present.Message);
        BrowserApplication.StartupTrace?.Invoke("present:registered");
        var boards = BoardsBrowserRegistration.Register(registry,
            new IndexedDbNotesRepository(new BrowserNotesTransport(), new NotesDocumentValidator()),
            Program.ReduceMotion, error => error is NotesCommitOutcomeUnknownException);
        if (!boards.Succeeded) throw new InvalidOperationException(boards.Message);
        return RegisterPrivateAccountSettings(registry, navigate);
    }

    [SupportedOSPlatform("browser")]
    public static AccountSettingsFeature RegisterPrivateAccountSettings(BrowserSurfaceRegistry registry, Action<HomeFeatureNavigationRequest> navigate)
    {
        BrowserPrivateTaskStorageEnrollment.Register(registry);
        var samePrivateOwner = BrowserPrivateTaskStorageEnrollment.GetCurrent(registry);
        BrowserHomeDashboardLayoutStore.Register(registry, samePrivateOwner.DemandPrivateContextCurrent);
        BrowserSpacesFeature.Register(registry, samePrivateOwner, navigate);
        var settings = AccountSettingsFeature.CreateForBrowser(action => Dispatcher.UIThread.InvokeAsync(action).GetTask(),
            BrowserAccountSignIn.IsAvailable ? BrowserAccountSignIn.RequestAsync : null);
        var registered = registry.Register(settings, settings.Render);
        if (!registered.Succeeded) { settings.Dispose(); throw new InvalidOperationException(registered.Message); }
        return settings;
    }
}
