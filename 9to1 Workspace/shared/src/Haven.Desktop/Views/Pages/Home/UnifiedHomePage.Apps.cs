using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private IPinRepository? _appPins;
    private HomeAppHubPreferences? _appHubPreferences;
    private HomeAppHubPreferences.Snapshot _appHubSnapshot = new(HomeAppHubLayout.Default, null);
    private IReadOnlyList<ModePin> _appPinSnapshot = [];
    private Button? _appFilters;
    private Control? _observedAppsSurface;
    private readonly List<IDisposable> _appsThemeObservations = [];
    private bool _appsMutation;
    private Task _appPinAction = Task.CompletedTask;
    private Func<CancellationToken, Task>? _admitAppChange;

    private void RegisterHomeApps(IPinRepository? pins, HomeAppHubPreferences? preferences, Func<CancellationToken, Task>? admit)
    {
        if (pins is not null && admit is null) throw new ArgumentException("App pin changes require the original profile admission.", nameof(admit));
        _appPins = pins;
        _appHubPreferences = preferences;
        _admitAppChange = admit;
        _appsLoader.BindAuthoredControl("home-apps-filter", _ => _appFilters = new Button());
        AddHandler(KeyDownEvent, OnAppPinKey, RoutingStrategies.Tunnel);
        RegisterAppPinDrag();
        RegisterHomeHubs();
    }

    private void ObserveAppsSurface(Control surface)
    {
        StopObservingAppsSurface();
        _observedAppsSurface = surface;
        surface.SizeChanged += OnAppsSurfaceSize;
        surface.AttachedToVisualTree += OnAppsSurfaceAttached;
        surface.DetachedFromVisualTree += OnAppsSurfaceDetached;
        RefreshAppsViewport(surface.Bounds.Width);
        if (surface.IsAttachedToVisualTree()) ObserveAppsTheme();
        else RefreshAppsTheme();
    }

    private void OnAppsSurfaceAttached(object? sender, VisualTreeAttachmentEventArgs args) => ObserveAppsTheme();

    private void OnAppsSurfaceDetached(object? sender, VisualTreeAttachmentEventArgs args) => StopObservingAppsTheme();

    private void ObserveAppsTheme()
    {
        StopObservingAppsTheme();
        if (_observedAppsSurface is not { } surface || !IsAlive) return;
        var observer = new NewTabResourceObserver(RefreshAppsTheme);
        foreach (var key in new[] { "CuiEffectiveThemeName", "CuiCardRadius", "CuiControlRadius" })
            _appsThemeObservations.Add(surface.GetResourceObservable(key).Subscribe(observer));
        RefreshAppsTheme();
    }

    private void RefreshAppsTheme()
    {
        if (_observedAppsSurface is not { } surface || !IsAlive) return;
        surface.TryFindResource("CuiEffectiveThemeName", out var name);
        surface.TryFindResource("CuiCardRadius", out var card);
        surface.TryFindResource("CuiControlRadius", out var control);
        _appsBindings.SetThemeName(name as string, card is CornerRadius cardRadius ? cardRadius : null,
            control is CornerRadius controlRadius ? controlRadius : null);
    }

    private void StopObservingAppsTheme()
    {
        foreach (var observer in _appsThemeObservations) observer.Dispose();
        _appsThemeObservations.Clear();
    }

    private void StopObservingAppsSurface()
    {
        StopObservingAppsTheme();
        if (_observedAppsSurface is not { } surface) return;
        surface.SizeChanged -= OnAppsSurfaceSize;
        surface.AttachedToVisualTree -= OnAppsSurfaceAttached;
        surface.DetachedFromVisualTree -= OnAppsSurfaceDetached;
        _observedAppsSurface = null;
    }

    private void OnAppsSurfaceSize(object? sender, SizeChangedEventArgs args)
    {
        if (args.WidthChanged) RefreshAppsViewport(args.NewSize.Width);
    }

    private void RefreshAppsViewport(double width)
    {
        var hostWidth = InterfaceWidth(Bounds.Width);
        var padding = hostWidth > 750 ? 60 : 32;
        _appsBindings.SetViewportWidth(Math.Max(0, InterfaceWidth(width) - padding), hostWidth);
    }

    private async Task ReadAppPresentationAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_appPins is not null)
        {
            await original.AwaitAsync(_admitAppChange!(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            var pins = await original.AwaitAsync(_appPins.GetPinsAsync(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            _appPinSnapshot = pins;
        }
        if (_appHubPreferences is not null)
        {
            var hubs = await original.AwaitAsync(_appHubPreferences.ReadAsync(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            _appHubSnapshot = hubs;
        }
    }

    private void ApplyAppPresentation()
    {
        _appsState = _appsState with
        {
            Pins = _appPinSnapshot,
            Filter = _appHubSnapshot.Layout.Filter,
            CanEditPins = !_appsMutation && _appPins is IOrderedPinRepository,
            Hubs = _appHubSnapshot.Layout.Customise ? _appHubSnapshot.Layout.CustomHubs : null
        };
        ApplyHubPresentation();
    }

    private bool OpenAppFilters()
    {
        if (_section != "apps" || _appFilters is not { IsEffectivelyVisible: true, ContextMenu: { } menu } anchor) return false;
        menu.Placement = PlacementMode.Bottom;
        anchor.Focus(NavigationMethod.Tab);
        menu.Open(anchor);
        return menu.IsOpen;
    }

    private async Task SetAppFilterAsync(string? value, DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var filter = value switch
        {
            "installed" => HomeAppsFilter.Installed, "all" => HomeAppsFilter.All, "pinned" => HomeAppsFilter.Pinned,
            _ => throw new ArgumentException("Choose an available app filter.", nameof(value))
        };
        if (_appsMutation || _section != "apps") return;
        _appFilters?.ContextMenu?.Close();
        var next = _appHubSnapshot.Layout with { Filter = filter };
        if (_appHubPreferences?.CanEdit == true)
        {
            var result = await original.AwaitAsync(_appHubPreferences.SaveAsync(_appHubSnapshot, next, token));
            if (!original.IsAcceptedPublicationCurrent) return;
            _appHubSnapshot = result.Current;
            UpdateAppsState(result.Saved ? string.Empty : "The app arrangement changed in another tab. Its latest settings are shown.");
        }
        else
        {
            // A read-only composition can still filter its current view; it does not claim a save.
            _appHubSnapshot = _appHubSnapshot with { Layout = next };
            UpdateAppsState(string.Empty);
        }
    }

    private async Task ChangeAppPinAsync(string? value, int? delta, DesktopOriginalWorkLifetime.Original original,
        CancellationToken token, Guid? dropTarget = null)
    {
        if (_section != "apps" || _appsMutation || _appPins is not IOrderedPinRepository pins ||
            !Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty ||
            _appsCatalogue.FirstOrDefault(app => app.CanonicalId == id && app.IsInstalled) is not { } app) return;
        var visit = _selection;
        original.BindPublicationGuard(() => IsAlive && _section == "apps" && visit == _selection);
        original.DemandPublication();
        var expected = _appPinSnapshot;
        _appsMutation = true;
        UpdateAppsState("Saving app pins…");
        try
        {
            await original.AwaitAsync(_admitAppChange!(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            var ordered = expected.OrderBy(pin => pin.SortOrder).ThenBy(pin => pin.PinnedAt).ThenBy(pin => pin.Id).ToArray();
            var index = Array.FindIndex(ordered, pin => pin.ModeId == id);
            if (delta is null && dropTarget is null)
            {
                var saved = await original.AwaitAsync(pins.TrySetPinnedAsync(expected, id, index < 0, token, _admitAppChange));
                if (!original.IsAcceptedPublicationCurrent) return;
                if (!saved)
                {
                    _appPinSnapshot = await original.AwaitAsync(pins.GetPinsAsync(token));
                    if (original.IsAcceptedPublicationCurrent)
                        UpdateAppsState("Pins changed in another tab. Their latest state is shown; try the action again.");
                    return;
                }
            }
            else
            {
                var destination = dropTarget is { } target ? Array.FindIndex(ordered, pin => pin.ModeId == target) : index + delta!.Value;
                if (index < 0 || destination < 0 || destination >= ordered.Length || destination == index) return;
                var order = ordered.Select(pin => pin.ModeId).ToList();
                order.RemoveAt(index); order.Insert(destination, id);
                var saved = await original.AwaitAsync(pins.TryReorderAsync(expected, order, token, _admitAppChange));
                if (!original.IsAcceptedPublicationCurrent) return;
                if (!saved)
                {
                    _appPinSnapshot = await original.AwaitAsync(pins.GetPinsAsync(token));
                    if (original.IsAcceptedPublicationCurrent)
                        UpdateAppsState("Pins changed in another tab. Their latest order is shown; move the app again.");
                    return;
                }
            }
            var current = await original.AwaitAsync(pins.GetPinsAsync(token));
            await original.AwaitAsync(_admitAppChange!(token));
            if (!original.IsAcceptedPublicationCurrent) return;
            _appPinSnapshot = current;
            UpdateAppsState(delta is not null || dropTarget is not null ? app.Name + " moved." :
                current.Any(pin => pin.ModeId == id) ? app.Name + " pinned." : app.Name + " unpinned.");
            FocusAppPin(id, delta is not null || dropTarget is not null);
        }
        finally
        {
            _appsMutation = false;
            if (original.IsAcceptedPublicationCurrent) { ApplyAppPresentation(); _appsBindings.Refresh(); }
        }
    }

    private void OnAppPinKey(object? sender, KeyEventArgs args)
    {
        if (_section != "apps" || _appsMutation || args.KeyModifiers != KeyModifiers.Alt ||
            args.Key is not (Key.Left or Key.Right) || args.Source is not Visual hit ||
            FindAppPinCard(hit) is not { } card || GetAppPinId(card) is not { } id) return;
        args.Handled = true;
        _ = _work.RunAsync(original => ChangeAppPinAsync(id.ToString("D"), args.Key == Key.Left ? -1 : 1,
            original, original.Token), task => _appPinAction = task);
    }

    private void FocusAppPin(Guid id, bool pinnedCard)
    {
        FocusAfterSelection(() =>
        {
            var buttons = _appsView?.GetVisualDescendants().OfType<Button>() ?? [];
            var target = pinnedCard
                ? buttons.FirstOrDefault(button => button.Name == "home-app-pin-open" &&
                    AutomationProperties.GetAutomationId(button) == "home-app-open-mode:" + id.ToString("D"))
                : buttons.FirstOrDefault(button => button.Name == "home-app-pin" &&
                    button.DataContext is CakeOS.Cui.ICuiBindingContext binding && binding.TryGetValue("app.pinId", out var value) &&
                    Equals(value, id.ToString("D")));
            if (target?.Focus(NavigationMethod.Tab) == true) target.BringIntoView();
        });
    }

    private void StopHomeApps()
    {
        EndAppPinDrag();
        StopAppPinDrag();
        StopHomeHubs();
        _appFilters?.ContextMenu?.Close();
        StopObservingAppsSurface();
        RemoveHandler(KeyDownEvent, OnAppPinKey);
    }
}
