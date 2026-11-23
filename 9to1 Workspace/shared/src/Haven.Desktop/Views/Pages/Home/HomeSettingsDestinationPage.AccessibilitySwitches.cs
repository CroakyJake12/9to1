using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeSettingsDestinationPage
{
    private readonly ToggleSwitch _highContrastSwitch = new();
    private readonly ToggleSwitch _reducedMotionSwitch = new();
    private bool _projectingAccessibilitySwitches;
    private Task _accessibilitySwitchWrite = Task.CompletedTask;

    private void RegisterAccessibilitySwitches()
    {
        _loader.BindAuthoredControl("home-settings-high-contrast", _ => _highContrastSwitch);
        _loader.BindAuthoredControl("home-settings-reduced-motion", _ => _reducedMotionSwitch);
        _highContrastSwitch.IsCheckedChanged += OnAccessibilitySwitchChanged;
        _reducedMotionSwitch.IsCheckedChanged += OnAccessibilitySwitchChanged;
    }

    private void RefreshAccessibilitySwitches()
    {
        var saved = _source.SavedAccessibility;
        var available = saved is not null && _active && IsAlive && !_unavailable &&
            _bindings.Get("HomeHighContrastTarget") is not null && _bindings.Get("HomeReduceMotionTarget") is not null;
        _projectingAccessibilitySwitches = true;
        try
        {
            _highContrastSwitch.SetCurrentValue(ToggleButton.IsCheckedProperty, available ? saved?.HighContrast : null);
            _reducedMotionSwitch.SetCurrentValue(ToggleButton.IsCheckedProperty, available ? saved?.ReduceMotion : null);
        }
        finally { _projectingAccessibilitySwitches = false; }
        // Keep keyboard focus while the save settles. The original appearance action gates additional writes.
        _bindings.Set("SettingsAccessibilitySwitchesAvailable", available);
        _bindings.Set("SettingsAccessibilitySwitchesUnavailable", !available);
    }

    private bool IsAccessibilitySwitchIntent(object? target) => target is not null && _category == "accessibility" &&
        (ReferenceEquals(target, _bindings.Get("HomeHighContrastTarget")) ||
         ReferenceEquals(target, _bindings.Get("HomeReduceMotionTarget")));

    private void OnAccessibilitySwitchChanged(object? sender, RoutedEventArgs args)
    {
        if (_projectingAccessibilitySwitches || sender is not ToggleSwitch control) return;
        var highContrast = ReferenceEquals(control, _highContrastSwitch);
        var saved = _source.SavedAccessibility;
        var selected = highContrast ? saved?.HighContrast : saved?.ReduceMotion;
        var target = _bindings.Get(highContrast ? "HomeHighContrastTarget" : "HomeReduceMotionTarget");
        if (selected is null || control.IsChecked is null || control.IsChecked == selected || _category != "accessibility" ||
            !control.IsAttachedToVisualTree() || !control.IsEffectivelyEnabled || !control.IsEffectivelyVisible ||
            _pageCommands is { IsHosted: true, IsSelected: false } || target is null ||
            IsActionAvailable("ApplyHomeAppearance") != true)
        {
            RefreshAccessibilitySwitches();
            return;
        }

        _accessibilitySwitchWrite = ApplyAccessibilitySwitchAsync(target);
        ObserveNotification(_accessibilitySwitchWrite);
    }

    private async Task ApplyAccessibilitySwitchAsync(object target)
    {
        try { await DispatchAsync("ApplyHomeAppearance", target); }
        finally { if (IsAlive) RefreshAccessibilitySwitches(); }
    }

    private void RetireAccessibilitySwitches()
    {
        _highContrastSwitch.IsCheckedChanged -= OnAccessibilitySwitchChanged;
        _reducedMotionSwitch.IsCheckedChanged -= OnAccessibilitySwitchChanged;
        RefreshAccessibilitySwitches();
    }
}
