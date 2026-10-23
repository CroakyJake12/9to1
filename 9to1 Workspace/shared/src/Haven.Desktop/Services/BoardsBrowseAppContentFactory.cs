using CakeOS.Cui.Themes;
using Haven.Browser;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Borrow the actual Home/Notes owners. Allocation creates no ownership, setup,
/// import, permission, model or Ready observation. Embedded and standalone entry points use
/// this same factory and shared Boards core/CUI; global services outlive each tab.</summary>
internal sealed class BoardsBrowseAppContentFactory : IBrowseAppContentFactory
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CancellationToken _appLifetime, _hostLifetime;
    private readonly Func<bool> _reduceMotion;
    private readonly CuiThemePreferenceContext? _theme;
    private BoardsBrowseAppContentFactory(IServiceProvider provider, HomeNativeWindowsComposition home,
        CancellationToken appLifetime, CancellationToken hostLifetime, Func<bool> reduceMotion,
        CuiThemePreferenceContext? theme)
    { _provider = provider; _home = home; _appLifetime = appLifetime; _hostLifetime = hostLifetime;
        _reduceMotion = reduceMotion; _theme = theme; }

    internal static BoardsBrowseAppContentFactory BindOriginalSameProcess(IServiceProvider originalProvider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime,
        CancellationToken originalHostLifetime, Func<bool> actualReduceMotion,
        CuiThemePreferenceContext? actualThemePreference = null)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalProvider, sameHome);
        ArgumentNullException.ThrowIfNull(actualReduceMotion);
        if (!originalAppLifetime.CanBeCanceled || !originalHostLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual application and host lifetimes.");
        originalAppLifetime.ThrowIfCancellationRequested(); originalHostLifetime.ThrowIfCancellationRequested();
        return new(originalProvider, sameHome, originalAppLifetime, originalHostLifetime,
            actualReduceMotion, actualThemePreference);
    }

    public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _appLifetime.ThrowIfCancellationRequested(); _hostLifetime.ThrowIfCancellationRequested();
        if (request.TabId == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId))
            throw new UnauthorizedAccessException("Retain the original tab and actual Home profile.");
        if (request.Privacy != BrowserTabPrivacy.Standard)
            throw new NotSupportedException("An owned device Boards library is not a private browser session.");
        var address = BoardsBrowseAppContentAddress.Parse(request.Address);
        // All borrowed resolution and identity checks precede the per-tab lifetime acquisition.
        var notes = _provider.GetRequiredService<NotesRepository>();
        var verified = _provider.GetRequiredService<VerifiedNotesRepository>();
        var admission = _provider.GetRequiredService<HomeNotesResourceAdmissionSource>();
        var approvals = _provider.GetRequiredService<HomeNativeApprovalWindowOwner>();
        if (!admission.IsBoundToOriginalNotes(notes) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NotesRepository)), notes) ||
            !ReferenceEquals(_home.Services.GetService(typeof(HomeNotesResourceAdmissionSource)), admission) ||
            !approvals.IsBoundToOriginalComposition(_home))
            throw new UnauthorizedAccessException("Boards must borrow the SAME original Notes, verified store and Home admission/review owners.");
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _hostLifetime);
        try
        {
            var route = new NativeBoardsDesktopRoute(_provider, _home, notes, verified, admission,
                approvals, request.ProfileId, "boards-tab:" + request.TabId.ToString("D"),
                _appLifetime, lifetime.Token);
            return new BoardsBrowseAppContentSession(request, address, route, lifetime, _reduceMotion, _theme);
        }
        catch { lifetime.Dispose(); throw; }
    }
}
