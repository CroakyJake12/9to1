using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private readonly Func<ModeDefinition, bool>? _shortcutAppAvailable;
    private bool _checkingShortcutApp;
    private string? _shortcutCatalogueStatus;
    private long _shortcutCatalogueVersion;
    private readonly Dictionary<string, ModeDefinition> _shortcutApps = new(StringComparer.Ordinal);

    private async Task RefreshShortcutAppsAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_shortcutAppAvailable is null) return;
        var visit = _selection;
        var version = ++_shortcutCatalogueVersion;
        IReadOnlyList<ModeDefinition> modes;
        string? status = null;
        try { modes = await original.AwaitAsync(_modes.GetModesAsync(token)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            modes = [];
            status = "Installed apps are unavailable. Reopen Add shortcuts to retry.";
        }
        if (!original.IsAcceptedPublicationCurrent || version != _shortcutCatalogueVersion || visit != _selection) return;
        _shortcutCatalogueStatus = status;
        _shortcutApps.Clear();
        foreach (var mode in modes.Where(mode => mode.IsEnabled && _shortcutAppAvailable(mode))
                     .OrderBy(mode => mode.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var target = HomeLaunchRouteResolver.Resolve(mode.Key);
            // Pins to application homes carry their registered mode identity. Resource-specific
            // links (including conversations and documents) have separate owning admission paths.
            if (target.ResourceSegments.Count == 0) _shortcutApps.TryAdd(target.Value, mode);
        }
        PublishNewTabShortcuts();
    }

    private bool IsShortcutAvailable(NewTabShortcutPlacement pin) => pin.AppId is { } id
        ? _shortcutApps.TryGetValue(pin.Target, out var mode) && mode.Id == id
        : IsShortcutTargetAvailable(pin.Target);

    private string ShortcutIcon(string target, string fallback) => _shortcutApps.TryGetValue(target, out var app)
        ? HomeAppBrandingIcons.SourceFor(app.IconKey, app.Id, app.Source) : "icon:" + fallback;

    private async Task<bool> IsShortcutAppCurrentAsync(NewTabShortcutPlacement pin,
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (pin.AppId is not { } id) return true;
        if (_shortcutAppAvailable is null) return false;
        var visit = _selection;
        ModeDefinition? mode;
        _checkingShortcutApp = true;
        try { mode = await original.AwaitAsync(_modes.GetModeByIdAsync(id, token)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { mode = null; }
        finally { _checkingShortcutApp = false; }
        if (!original.IsAcceptedPublicationCurrent || _section != "newtab" || _selection != visit) return false;
        if (mode is not null && mode.Id == id && mode.IsEnabled && _shortcutAppAvailable(mode) &&
            HomeLaunchRouteResolver.Resolve(mode.Key).Value == pin.Target) return true;
        if (_shortcutApps.TryGetValue(pin.Target, out var previous) && previous.Id == id)
            _shortcutApps.Remove(pin.Target);
        PublishNewTabShortcuts();
        return false;
    }
}
