using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private sealed record HubConfirmation(string Kind, HomeAppHubPreferences.Snapshot Expected,
        HomeHubActionTarget? Target, string Title, string Detail);

    private string _selectedHub = "develop";
    private string _hubQuery = string.Empty;
    private string _newHubName = string.Empty;
    private readonly Dictionary<string, string> _hubNameDrafts = new(StringComparer.Ordinal);
    private long _hubDraftRevision = -1;
    private HubConfirmation? _hubConfirmation;
    private Button? _hubChooser;
    private Button? _hubAddApp;
    private Task _hubKeyboardAction = Task.CompletedTask;

    private void RegisterHomeHubs()
    {
        _appsLoader.BindAuthoredControl("home-hub-chooser", _ => _hubChooser = new Button());
        _appsLoader.BindAuthoredControl("home-hub-add-app-menu", _ => _hubAddApp = new Button());
        AddHandler(KeyDownEvent, OnHubEditorKey, RoutingStrategies.Tunnel);
    }

    private void StopHomeHubs()
    {
        _hubChooser?.ContextMenu?.Close();
        _hubAddApp?.ContextMenu?.Close();
        RemoveHandler(KeyDownEvent, OnHubEditorKey);
    }

    private Task WhenHubActionsIdleAsync() => _hubKeyboardAction;

    private void ApplyHubPresentation()
    {
        var layout = _appHubSnapshot.Layout;
        if (_hubDraftRevision != layout.Revision)
        {
            _hubNameDrafts.Clear();
            _hubDraftRevision = layout.Revision;
        }
        var editing = _section == "hubs-customise";
        var arrangement = (editing ? layout.CustomLayoutInitialised : layout.Customise)
            ? layout.CustomHubs : HomeAppHubCatalogue.StandardHubs(_appsCatalogue);
        if (!arrangement.Any(hub => hub.Key == _selectedHub))
            _selectedHub = arrangement.FirstOrDefault()?.Key ?? string.Empty;
        _appsState = _appsState with
        {
            HubsView = new(layout.Revision, editing, layout.Customise,
                !_appsMutation && _appHubPreferences?.CanEdit == true, arrangement, _selectedHub, _hubQuery,
                _newHubName, new Dictionary<string, string>(_hubNameDrafts), layout.Undo is not null,
                layout.Undo?.Description ?? string.Empty, _hubConfirmation?.Title, _hubConfirmation?.Detail,
                _hubConfirmation?.Kind == "remove" ? "Remove hub" : "Reset arrangement",
                _hubConfirmation?.Kind == "remove" ? "Keep hub" : "Keep arrangement", _appsMutation)
        };
    }

    private Task ShowHomeHubsAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Queue(AppContentAddress.Parse("browse://home/hubs"), original, newTab: false);
        return Task.CompletedTask;
    }

    private async Task DispatchHomeHubCommandAsync(string command, object? parameter,
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_section is not ("hubs" or "hubs-customise")) return;
        var visit = _selection;
        original.BindPublicationGuard(() => IsAlive && visit == _selection && (_section is "hubs" or "hubs-customise"));
        original.DemandPublication();
        token.ThrowIfCancellationRequested();
        try
        {
            switch (command)
            {
                case "home.hub-all-apps":
                    Queue(AppContentAddress.Parse("browse://home"), original, newTab: false); return;
                case "home.hub-back": await ShowHomeHubsAsync(original, token); return;
                case "home.hub-customise":
                    Queue(AppContentAddress.Parse("browse://home/hubs/customise"), original, newTab: false); return;
                case "home.hub-set-query": _hubQuery = parameter?.ToString() ?? string.Empty; RefreshHubPresentation(); return;
                case "home.hub-clear-query": _hubQuery = string.Empty; RefreshHubPresentation(); return;
                case "home.hub-set-new-name": _newHubName = parameter?.ToString() ?? string.Empty; RefreshHubPresentation(); return;
                case "home.hub-select":
                    if (parameter is HomeHubActionTarget selected &&
                        _appsState.HubsView?.Hubs.Any(hub => hub.Key == selected.HubKey) == true)
                    {
                        _selectedHub = selected.HubKey; _hubQuery = string.Empty;
                        _hubChooser?.ContextMenu?.Close(); _hubAddApp?.ContextMenu?.Close();
                        RefreshHubPresentation();
                    }
                    return;
                case "home.hub-choose": OpenHubMenu(_hubChooser); return;
                case "home.hub-add-app-menu":
                    if (_appHubSnapshot.Layout.Customise && !_appsMutation) OpenHubMenu(_hubAddApp);
                    return;
                case "home.hub-open-app": OpenHubApp(parameter as HomeHubActionTarget, original); return;
                case "home.hub-cancel-confirm": _hubConfirmation = null; RefreshHubPresentation(); return;
                case "home.hub-filter": await SetHubFilterAsync(parameter as string, original, token); return;
            }
            if (_section != "hubs-customise" || _appsMutation || _appHubPreferences?.CanEdit != true) return;
            var layout = _appHubSnapshot.Layout;
            if (command == "home.hub-toggle")
            {
                if (parameter is bool requested && requested == layout.Customise) return;
                _hubConfirmation = null;
                await SaveHubLayoutAsync(HomeHubEdits.Toggle(layout, HomeAppHubCatalogue.StandardHubs(_appsCatalogue)),
                    "Hub customisation " + (layout.Customise ? "off. Your arrangement is saved." : "on."), original, token);
                return;
            }
            if (!layout.Customise) return;
            var target = parameter as HomeHubActionTarget;
            switch (command)
            {
                case "home.hub-set-name" when parameter is HomeHubRenameValue rename:
                    DemandCurrentHub(rename.Target);
                    _hubNameDrafts[rename.Target.HubKey] = rename.Value;
                    RefreshHubPresentation(); return;
                case "home.hub-remove":
                    var removed = DemandCurrentHub(target);
                    _hubConfirmation = new("remove", _appHubSnapshot, target, "Remove " + removed.Name + "?",
                        "This removes its place in your custom arrangement. Apps and project files stay available.");
                    RefreshHubPresentation(); FocusHubConfirmation(); return;
                case "home.hub-reset":
                    _hubConfirmation = new("reset", _appHubSnapshot, null,
                        "Replace the custom arrangement with the standard hubs?",
                        "Only hub names, order and app placements are reset.");
                    RefreshHubPresentation(); FocusHubConfirmation(); return;
                case "home.hub-confirm":
                    if (_hubConfirmation is not { } confirmation) return;
                    _hubConfirmation = null;
                    if (!string.Equals(confirmation.Expected.Serialized, _appHubSnapshot.Serialized, StringComparison.Ordinal) ||
                        confirmation.Expected.Layout.Revision != layout.Revision)
                        throw new InvalidOperationException("The arrangement changed after this confirmation opened. Review it and try again.");
                    await SaveHubLayoutAsync(confirmation.Kind == "remove"
                        ? HomeHubEdits.RemoveHub(layout, confirmation.Target!)
                        : HomeHubEdits.Reset(layout, HomeAppHubCatalogue.StandardHubs(_appsCatalogue)),
                        confirmation.Kind == "remove" ? "Hub removed. Undo is available." : "Standard arrangement restored. Undo is available.", original, token);
                    return;
            }
            HomeAppHubLayout next;
            string status;
            switch (command)
            {
                case "home.hub-add":
                    var key = "custom-" + Guid.NewGuid().ToString("N");
                    next = HomeHubEdits.AddHub(layout, _newHubName, key); status = "Hub added.";
                    if (await SaveHubLayoutAsync(next, status, original, token))
                    { _selectedHub = key; _newHubName = string.Empty; RefreshHubPresentation(); FocusHubName(key); }
                    return;
                case "home.hub-rename":
                    var hub = DemandCurrentHub(target);
                    next = HomeHubEdits.Rename(layout, target!, _hubNameDrafts.GetValueOrDefault(hub.Key, hub.Name));
                    status = "Hub name saved."; break;
                case "home.hub-move-up": case "home.hub-move-down":
                    DemandCurrentHub(target);
                    next = HomeHubEdits.MoveHub(layout, target!, command.EndsWith("up", StringComparison.Ordinal) ? -1 : 1);
                    status = "Hub order saved."; break;
                case "home.hub-undo": next = HomeHubEdits.Undo(layout); status = "Previous arrangement restored."; break;
                case "home.hub-add-app":
                    DemandCurrentHub(target);
                    if (target?.AppIdentity is null || !_appsBindings.HubsPresentation.AddableApps.Any(app => app.Target == target))
                        throw new InvalidOperationException("That app is no longer available to add from this catalogue.");
                    _hubAddApp?.ContextMenu?.Close();
                    next = HomeHubEdits.AddApp(layout, target); status = "App placement added."; break;
                case "home.hub-remove-app":
                    DemandCurrentHub(target);
                    next = HomeHubEdits.RemoveApp(layout, target!); status = "App placement removed. Undo is available."; break;
                case "home.hub-app-up": case "home.hub-app-down":
                    DemandCurrentHub(target);
                    next = HomeHubEdits.MoveApp(layout, target!, command.EndsWith("up", StringComparison.Ordinal) ? -1 : 1);
                    status = "App order saved."; break;
                default: throw new InvalidOperationException("That hub action is not available.");
            }
            _hubConfirmation = null;
            if (await SaveHubLayoutAsync(next, status, original, token) && command is "home.hub-move-up" or "home.hub-move-down")
                FocusHubName(target!.HubKey);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            if (original.IsAcceptedPublicationCurrent) UpdateAppsState(error.Message);
        }
    }

    private HomeAppHub DemandCurrentHub(HomeHubActionTarget? target)
    {
        if (target is null || target.Revision != _appHubSnapshot.Layout.Revision)
            throw new InvalidOperationException("The arrangement changed. Review its latest state and try again.");
        return _appHubSnapshot.Layout.CustomHubs.FirstOrDefault(hub => hub.Key == target.HubKey)
            ?? throw new InvalidOperationException("That hub is no longer in this arrangement.");
    }

    private async Task<bool> SaveHubLayoutAsync(HomeAppHubLayout next, string status,
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_appHubPreferences?.CanEdit != true || _appsMutation) return false;
        if (ReferenceEquals(next, _appHubSnapshot.Layout)) { UpdateAppsState(status); return true; }
        var expected = _appHubSnapshot;
        _appsMutation = true;
        UpdateAppsState("Saving your app arrangement…");
        try
        {
            original.DemandPublication();
            var result = await original.AwaitAsync(_appHubPreferences.SaveAsync(expected, next, token));
            if (!original.IsAcceptedPublicationCurrent) return false;
            _appHubSnapshot = result.Current;
            _hubConfirmation = null;
            UpdateAppsState(result.Saved ? status : "The arrangement changed in another tab. Its latest state is shown; try the action again.");
            return result.Saved;
        }
        finally
        {
            _appsMutation = false;
            if (original.IsAcceptedPublicationCurrent) RefreshHubPresentation();
        }
    }

    private async Task SetHubFilterAsync(string? value, DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_section != "hubs" || _appsMutation) return;
        var filter = value switch
        {
            "installed" => HomeAppsFilter.Installed, "all" => HomeAppsFilter.All, "pinned" => HomeAppsFilter.Pinned,
            _ => throw new ArgumentException("Choose Installed, All apps or Pinned.", nameof(value))
        };
        var next = _appHubSnapshot.Layout with { Filter = filter };
        if (_appHubPreferences?.CanEdit == true) await SaveHubLayoutAsync(next, string.Empty, original, token);
        else { _appHubSnapshot = _appHubSnapshot with { Layout = next }; UpdateAppsState(string.Empty); }
    }

    private void OpenHubApp(HomeHubActionTarget? target, DesktopOriginalWorkLifetime.Original original)
    {
        var arrangement = _appsState.HubsView?.Hubs;
        if (target?.AppIdentity is null || target.Revision != _appHubSnapshot.Layout.Revision ||
            arrangement?.FirstOrDefault(hub => hub.Key == target.HubKey)?.AppIdentities.Contains(target.AppIdentity, StringComparer.Ordinal) != true)
            throw new InvalidOperationException("That app placement changed. Choose it again from the current hub.");
        var app = HomeAppHubCatalogue.ResolvePlacement(_appsCatalogue, target.AppIdentity);
        var actual = _appsCatalogue.FirstOrDefault(item => item.StableIdentity == app.StableIdentity && item.Source == app.Source);
        if (actual is not { IsAvailable: true, IsInstalled: true } ||
            _appsCatalogue.Any(item => item.Key.Equals(actual.Key, StringComparison.OrdinalIgnoreCase) && item.StableIdentity != actual.StableIdentity))
            throw new InvalidOperationException("That exact app cannot be opened in this profile. Its saved placement has been kept.");
        Queue(HomeLaunchRouteResolver.Resolve(actual.Key), original, newTab: false);
    }

    private void RefreshHubPresentation() { ApplyAppPresentation(); _appsBindings.Refresh(); }

    private static void OpenHubMenu(Button? anchor)
    {
        if (anchor is not { IsEffectivelyVisible: true, ContextMenu: { } menu }) return;
        menu.Placement = PlacementMode.Bottom;
        anchor.Focus(NavigationMethod.Tab);
        menu.Open(anchor);
    }

    private void OnHubEditorKey(object? sender, KeyEventArgs args)
    {
        if (_section != "hubs-customise" || _appsMutation || args.Source is not Visual source) return;
        if (args.KeyModifiers == KeyModifiers.None && args.Key == Key.Escape && _hubConfirmation is { } confirmation)
        {
            args.Handled = true; _hubConfirmation = null; RefreshHubPresentation();
            if (confirmation.Target is { } original) FocusHubName(original.HubKey);
            return;
        }
        var control = (source as Control)?.GetSelfAndVisualAncestors().OfType<Control>()
            .FirstOrDefault(item => item.DataContext is ICuiBindingContext context &&
                context.TryGetValue("hub.target", out var value) && value is HomeHubActionTarget);
        if (control?.DataContext is not ICuiBindingContext binding ||
            !binding.TryGetValue("hub.target", out var raw) || raw is not HomeHubActionTarget target) return;
        var name = (source as Control)?.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault();
        if (args.KeyModifiers == KeyModifiers.None && args.Key == Key.Escape && name?.Name == "home-hub-name")
        {
            args.Handled = true; _hubNameDrafts.Remove(target.HubKey); RefreshHubPresentation(); return;
        }
        var command = args.KeyModifiers == KeyModifiers.Alt && args.Key is Key.Up or Key.Down
            ? (args.Key == Key.Up ? "home.hub-move-up" : "home.hub-move-down") : null;
        if (command is null) return;
        args.Handled = true;
        _ = _work.RunAsync(original => DispatchHomeHubCommandAsync(command, target, original, original.Token),
            task => _hubKeyboardAction = task);
    }

    private void FocusHubConfirmation() => FocusAfterSelection(() =>
    {
        var cancel = _appsView?.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.Name == "home-hub-cancel-confirm");
        if (cancel?.Focus(NavigationMethod.Tab) == true) cancel.BringIntoView();
    });

    private void FocusHubName(string key) => FocusAfterSelection(() =>
    {
        var field = _appsView?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box =>
            box.Name == "home-hub-name" && box.DataContext is ICuiBindingContext binding &&
            binding.TryGetValue("hub.target", out var value) && value is HomeHubActionTarget target && target.HubKey == key);
        if (field?.Focus(NavigationMethod.Tab) == true) field.BringIntoView();
    });
}
