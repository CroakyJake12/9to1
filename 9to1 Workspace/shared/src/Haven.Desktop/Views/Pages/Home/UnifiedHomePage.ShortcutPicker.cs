using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private readonly CuiViewModel _shortcutPickerBindings = new();
    private readonly TextBox _shortcutPickerSearch = new();
    private CuiControlLoader? _shortcutPickerLoader;
    private Panel? _shortcutPickerHost;
    private Border? _shortcutPickerBackdrop, _shortcutPickerFrame;
    private NewTabShortcutPlacement? _shortcutPendingPick;
    private string? _shortcutPickerStatus;
    private bool _shortcutPickerOpen, _shortcutAddressFromPicker;

    private double ShortcutPickerWidth => Math.Min(690, Math.Max(1, Bounds.Width - 40));

    private void OpenShortcutPicker(bool resume = false)
    {
        if (Content is not Panel host || _shortcutPickerOpen) return;
        if (!resume)
        {
            _shortcutPickerSearch.Text = string.Empty;
            _shortcutPickerStatus = _shortcutCatalogueStatus;
        }
        _shortcutPickerOpen = true;
        PublishNewTabShortcuts();
        if (_shortcutPickerBackdrop is null)
        {
            using var stream = typeof(UnifiedHomePage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.NewTabShortcutPicker.cui")
                ?? throw new InvalidDataException("The New Tab shortcut picker is missing.");
            using var reader = new StreamReader(stream);
            var loader = new CuiControlLoader(HomeAppsCuiDocument.CreateControlRegistry());
            loader.SetActionDispatcher(this);
            loader.SetBindingContext(_shortcutPickerBindings);
            loader.BindAuthoredControl("home-newtab-shortcut-picker-search", _ => _shortcutPickerSearch);
            var loaded = loader.LoadMarkup(reader.ReadToEnd(), "NewTabShortcutPicker.cui");
            if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                _shortcutPickerOpen = false;
                PublishNewTabShortcuts();
                throw new InvalidDataException("The New Tab shortcut picker could not render. " + string.Join(" ", loaded.Diagnostics));
            }
            loader.WireBindings(loaded.Root);
            _shortcutPickerLoader = loader;
            _shortcutPickerFrame = new Border
            {
                Child = loaded.Root,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            KeyboardNavigation.SetTabNavigation(_shortcutPickerFrame, KeyboardNavigationMode.Cycle);
            _shortcutPickerBackdrop = new Border
            {
                Child = _shortcutPickerFrame,
                Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)),
                ZIndex = 100
            };
            _shortcutPickerBackdrop.PointerPressed += OnShortcutPickerBackdropPressed;
            _shortcutPickerSearch.TextChanged += OnShortcutPickerSearchChanged;
        }
        _shortcutPickerHost = host;
        host.Children.Add(_shortcutPickerBackdrop);
        ApplyShortcutPickerSize();
        if (resume) FocusShortcutPicker("home-newtab-shortcut-picker-custom");
        else FocusShortcutPicker("home-newtab-shortcut-picker-search");
    }

    private void HideShortcutPicker()
    {
        _shortcutPickerOpen = false;
        if (_shortcutPickerHost is { } host && _shortcutPickerBackdrop is { } backdrop)
            host.Children.Remove(backdrop);
        _shortcutPickerHost = null;
    }

    private void CloseShortcutPicker()
    {
        HideShortcutPicker();
        _shortcutPendingPick = null;
        PublishNewTabShortcuts();
        FocusAddShortcut();
    }

    private void FocusShortcutPicker(string id) => FocusAfterSelection(() => _shortcutPickerFrame?.GetVisualDescendants()
        .OfType<Control>().FirstOrDefault(control => control.Name == id && control.IsEffectivelyVisible)?.Focus(NavigationMethod.Tab));

    private void FocusAddShortcut() => FocusAfterSelection(() =>
    {
        var trigger = this.GetVisualDescendants().OfType<Button>()
            .LastOrDefault(button => button.IsEffectivelyVisible && button.Name == "home-newtab-shortcut-add")
            ?? this.GetVisualDescendants().OfType<Button>()
                .LastOrDefault(button => button.IsEffectivelyVisible && button.Name == "home-newtab-shortcut");
        trigger?.Focus(NavigationMethod.Tab);
    });

    private void OnShortcutPickerBackdropPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!ReferenceEquals(args.Source, _shortcutPickerBackdrop) ||
            !args.GetCurrentPoint(_shortcutPickerBackdrop).Properties.IsLeftButtonPressed) return;
        if (CanCloseShortcutPicker) CloseShortcutPicker();
        args.Handled = true;
    }

    private void OnShortcutPickerSearchChanged(object? sender, TextChangedEventArgs args) => PublishShortcutPicker();

    private void OnShortcutPickerSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (!_shortcutPickerOpen) return;
        ApplyShortcutPickerSize();
        if (args.WidthChanged) PublishShortcutPicker();
    }

    private void ApplyShortcutPickerSize()
    {
        if (_shortcutPickerFrame is not { } frame) return;
        frame.Width = ShortcutPickerWidth;
        frame.MaxHeight = Math.Min(748, Math.Max(1, Bounds.Height - 40));
    }

    private bool CanCloseShortcutPicker => _shortcutPickerOpen && !_checkingShortcutApp && !NewTabLayoutBusy && _retryNewTabLayout is null;

    private async Task AddShortcutCandidateAsync(string? address, DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var candidate = EnumerateShortcutCandidates().FirstOrDefault(item => item.Target == address);
        if (candidate.Target is null || SavedShortcuts.Any(item => item.Target == candidate.Target)) return;
        var pin = new NewTabShortcutPlacement(Guid.NewGuid().ToString("N"), candidate.Label, candidate.Target)
        { AppId = _shortcutApps.GetValueOrDefault(candidate.Target)?.Id };
        if (!await IsShortcutAppCurrentAsync(pin, original, token))
        {
            if (original.IsAcceptedPublicationCurrent)
            {
                _shortcutPickerStatus = "This app is no longer available in this profile.";
                PublishShortcutPicker();
                FocusShortcutPicker("home-newtab-shortcut-picker-search");
            }
            return;
        }
        _shortcutPendingPick = pin;
        await SaveNewTabShortcutPlacementAsync(_shortcutPendingPick, editing: null, original, token);
        if (original.IsAcceptedPublicationCurrent) CompleteShortcutPick();
    }

    private IEnumerable<(string Target, string Label, string Icon)> EnumerateShortcutCandidates() =>
        ShortcutDestinations.Where(item => IsShortcutTargetAvailable(item.Target))
            .Concat(_shortcutApps.Select(entry => (entry.Key, entry.Value.Name, entry.Value.IconKey)));

    private void CompleteShortcutPick()
    {
        if (_shortcutPendingPick is not { } pending) return;
        if (_retryNewTabLayout is not null)
        {
            _shortcutPickerStatus = _newTabEditorStatus;
            PublishShortcutPicker();
            FocusShortcutPicker("home-newtab-shortcut-picker-retry");
            return;
        }
        _shortcutPickerStatus = SavedShortcuts.Any(shortcut => shortcut.Target == pending.Target)
            ? pending.Label + " is in Shortcuts."
            : "The shortcut could not be added to the current layout.";
        _shortcutPendingPick = null;
        PublishShortcutPicker();
        FocusShortcutPicker("home-newtab-shortcut-picker-search");
    }

    private void PublishShortcutPicker()
    {
        if (!_shortcutPickerOpen) return;
        var query = _shortcutPickerSearch.Text?.Trim() ?? string.Empty;
        var columns = ShortcutPickerWidth >= 680 && Bounds.Width >= 900 ? 3 : ShortcutPickerWidth >= 420 ? 2 : 1;
        var shortcuts = SavedShortcuts;
        var canAdd = CanEditShortcuts && ShortcutProjectionCanAdd;
        var candidates = EnumerateShortcutCandidates().Where(item =>
            item.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)).Select((item, index) =>
        {
            var pinned = shortcuts.Any(shortcut => shortcut.Target == item.Target);
            return new Dictionary<string, object?>
            {
                ["Target"] = item.Target, ["Label"] = item.Label,
                ["Icon"] = ShortcutIcon(item.Target, item.Icon), ["Mark"] = pinned ? "✓" : "+",
                ["AccessibleName"] = pinned ? item.Label + ", already added" : "Add " + item.Label,
                ["CanAdd"] = canAdd && !pinned, ["Row"] = index / columns, ["Column"] = index % columns
            };
        }).ToArray();
        _shortcutPickerBindings.Set("PickerCandidates", candidates);
        _shortcutPickerBindings.Set("PickerEmpty", candidates.Length == 0);
        _shortcutPickerBindings.Set("PickerRows", string.Join(' ', Enumerable.Repeat("Auto", Math.Max(1, (candidates.Length + columns - 1) / columns))));
        _shortcutPickerBindings.Set("PickerColumns", string.Join(' ', Enumerable.Repeat("1fr", columns)));
        _shortcutPickerBindings.Set("PickerPadding", Bounds.Width >= 900 ? new Thickness(28, 24) : new Thickness(16));
        _shortcutPickerBindings.Set("CanClose", CanCloseShortcutPicker);
        _shortcutPickerBindings.Set("CanAddCustom", canAdd);
        _shortcutPickerBindings.Set("CanRetry", !NewTabLayoutBusy && _retryNewTabLayout is not null && _shortcutPendingPick is not null);
        _shortcutPickerBindings.Set("PickerStatus", _shortcutPickerStatus ?? string.Empty);
        _shortcutPickerBindings.Set("HasStatus", !string.IsNullOrEmpty(_shortcutPickerStatus));
    }

    private Task WhenShortcutPickerActionsIdleAsync() => _shortcutPickerLoader?.WhenActionsIdleAsync() ?? Task.CompletedTask;

    private void StopNewTabShortcutPicker()
    {
        SizeChanged -= OnShortcutPickerSizeChanged;
        _shortcutPickerSearch.TextChanged -= OnShortcutPickerSearchChanged;
        if (_shortcutPickerBackdrop is { } backdrop) backdrop.PointerPressed -= OnShortcutPickerBackdropPressed;
        HideShortcutPicker();
        _shortcutPickerLoader?.Dispose();
    }
}
