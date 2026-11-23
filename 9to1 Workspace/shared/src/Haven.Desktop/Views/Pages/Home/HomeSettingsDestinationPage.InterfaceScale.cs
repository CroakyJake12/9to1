using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeSettingsDestinationPage
{
    private sealed record InterfaceScaleOption(string Label, double Scale, object? Target);
    private sealed record InterfaceScaleDraft(InterfaceScaleOption Option, string Scope, long Revision, double SavedScale)
    {
        internal object Target => Option.Target!;
    }

    private readonly ComboBox _interfaceScale = new();
    private InterfaceScaleOption[] _interfaceScaleOptions = [];
    private InterfaceScaleDraft? _interfaceScaleDraft;
    private InterfaceScaleDraft? _interfaceScaleRetry;
    private string? _interfaceScaleStatus;
    private Task _interfaceScalePreview = Task.CompletedTask;
    private long _interfaceScaleRequest;
    private bool _projectingInterfaceScale, _interfaceScalePreviewPending, _interfaceScaleWasHosted, _restoreInterfaceScaleFocus;

    private bool IsInterfaceScalePage => _active && IsAlive && _category == "accessibility" &&
        (!_interfaceScaleWasHosted || _pageCommands is { IsHosted: true, IsSelected: true });

    private void RefreshInterfaceScaleHost()
    {
        _interfaceScaleWasHosted |= _pageCommands?.IsHosted == true;
        RefreshInterfaceScale();
    }
    private bool CanSelectInterfaceScale => IsInterfaceScalePage && !_unavailable && !IsBusy &&
        !_interfaceScalePreviewPending && _interfaceScaleOptions.Any(option => option.Target is not null);
    private bool CanApplyInterfaceScale => CanSelectInterfaceScale && InterfaceScaleWrite is not null;
    private bool CanRevertInterfaceScale => CanSelectInterfaceScale &&
        (_interfaceScaleDraft is not null || _interfaceScaleRetry is not null);

    private InterfaceScaleDraft? InterfaceScaleWrite
    {
        get
        {
            var saved = _source.Current;
            if (_interfaceScaleDraft is { } draft && IsCurrent(draft, saved.ScopeId, saved.Revision)) return draft;
            if (_interfaceScaleRetry is not { } retry || retry.Scope != saved.ScopeId ||
                retry.SavedScale != saved.Accessibility.DisplayScale) return null;
            var option = _interfaceScaleOptions.FirstOrDefault(item => item.Scale == retry.Option.Scale);
            return option?.Target is not null ? retry with { Option = option, Revision = saved.Revision } : null;
        }
    }

    private static bool IsCurrent(InterfaceScaleDraft draft, string scope, long revision) =>
        draft.Scope == scope && draft.Revision == revision;

    private void RegisterInterfaceScale()
    {
        _loader.BindAuthoredControl("home-settings-interface-scale", _ => _interfaceScale);
        _interfaceScale.SelectionChanged += OnInterfaceScaleSelected;
        _interfaceScale.PropertyChanged += OnInterfaceScalePropertyChanged;
        _source.Context.Changed += OnEffectiveInterfaceScaleChanged;
        AddHandler(KeyDownEvent, OnInterfaceScaleKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnEffectiveInterfaceScaleChanged(object? sender, EventArgs args) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsAlive || !_active) return;
        ApplyResponsiveLayout(Bounds.Width);
        TryRevealInterfaceScale();
    }, DispatcherPriority.Loaded);

    private void OnInterfaceScalePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (_restoreInterfaceScaleFocus && args.Property == InputElement.IsEffectivelyEnabledProperty && _interfaceScale.IsEffectivelyEnabled)
            RevealInterfaceScale(focus: false);
    }

    private void RefreshInterfaceScale()
    {
        var saved = _source.Current;
        var savedScale = saved.Accessibility.DisplayScale;
        if (!IsInterfaceScalePage || _unavailable)
        {
            ClearInterfaceScalePreview();
            _restoreInterfaceScaleFocus = false;
            _interfaceScaleRetry = null;
            _interfaceScaleStatus = _unavailable ? "Reopen Settings for the current profile." : null;
        }
        else if (!IsBusy && _interfaceScaleDraft is { } draft && !IsCurrent(draft, saved.ScopeId, saved.Revision))
        {
            ClearInterfaceScalePreview();
            _interfaceScaleStatus = "Appearance changed. Choose an interface scale to preview again.";
        }
        if (_interfaceScaleRetry is { } retry && (retry.Scope != saved.ScopeId || retry.SavedScale != savedScale))
        {
            _interfaceScaleRetry = null;
            _interfaceScaleStatus = "The saved interface scale changed. Choose a size to preview again.";
        }

        _interfaceScaleOptions = _active && !_unavailable &&
            _bindings.Get("HomeInterfaceScaleChoices") is IReadOnlyList<Dictionary<string, object?>> choices
                ? choices.Select(option => new InterfaceScaleOption((string)option["Name"]!, (double)option["Scale"]!, option["Target"]))
                    .ToArray()
                : [];
        if (_interfaceScaleOptions.Length != 0 && !_interfaceScaleOptions.Any(option => option.Scale == savedScale))
            _interfaceScaleOptions = [new((string)_bindings.Get("HomeInterfaceScaleText")!, savedScale, null), .. _interfaceScaleOptions];
        var selected = _interfaceScaleDraft?.Option.Scale ?? savedScale;
        var labels = _interfaceScaleOptions.Select(option => option.Label).ToArray();
        _projectingInterfaceScale = true;
        try
        {
            if (_interfaceScale.ItemsSource is not string[] existing || !existing.SequenceEqual(labels))
                _interfaceScale.ItemsSource = labels;
            _interfaceScale.SelectedIndex = Array.FindIndex(_interfaceScaleOptions, option => option.Scale == selected);
        }
        finally { _projectingInterfaceScale = false; }

        var available = CanSelectInterfaceScale;
        var status = _interfaceScaleStatus;
        if (IsInterfaceScalePage && !available)
            status = IsBusy || _interfaceScalePreviewPending ? "Updating interface scale…" : status ?? "Loading the saved interface scale…";
        _bindings.Set("SettingsInterfaceScaleAvailable", available);
        _bindings.Set("SettingsInterfaceScaleCanApply", CanApplyInterfaceScale);
        _bindings.Set("SettingsInterfaceScaleCanRevert", CanRevertInterfaceScale);
        _bindings.Set("SettingsInterfaceScaleStatus", status ?? string.Empty);
        _bindings.Set("SettingsInterfaceScaleHasStatus", !string.IsNullOrEmpty(status));
    }

    private void OnInterfaceScaleSelected(object? sender, Avalonia.Controls.SelectionChangedEventArgs args)
    {
        if (_projectingInterfaceScale || !CanSelectInterfaceScale || _interfaceScale.SelectedIndex < 0) return;
        var option = _interfaceScaleOptions[_interfaceScale.SelectedIndex];
        var saved = _source.Current;
        if (option.Scale == saved.Accessibility.DisplayScale)
        {
            RevertInterfaceScale();
            return;
        }
        if (option.Target is null) return;
        var draft = new InterfaceScaleDraft(option, saved.ScopeId, saved.Revision, saved.Accessibility.DisplayScale);
        var request = ++_interfaceScaleRequest;
        _interfaceScalePreviewPending = true;
        _interfaceScaleRetry = null;
        var actual = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsInterfaceScalePage && request == _interfaceScaleRequest);
            try
            {
                if (!await original.AwaitAsync(IsAdmittedAsync(original.Token)))
                {
                    Deny(original, false);
                    return;
                }
                original.RunAcceptedPublicationCallback(() =>
                {
                    if (_source.TryPreviewInterfaceScale(this, draft.Target))
                    {
                        _interfaceScaleDraft = draft;
                        _interfaceScaleStatus = $"Previewing {option.Label}. Apply to keep it, or Revert.";
                    }
                    else
                    {
                        _interfaceScaleStatus = "Interface scale could not be previewed. Choose a size to try again.";
                    }
                });
            }
            finally
            {
                if (request == _interfaceScaleRequest)
                {
                    _interfaceScalePreviewPending = false;
                    RefreshInterfaceScale();
                    if (original.IsAcceptedPublicationCurrent) RevealInterfaceScale(focus: false);
                }
            }
        });
        _interfaceScalePreview = actual;
        ObserveNotification(actual);
    }

    private object? CaptureInterfaceScaleWrite(InterfaceScaleDraft draft)
    {
        // A retry has already returned the presentation to its saved value. Claim a fresh
        // preview only for this explicit attempt, then pass its exact owner receipt to storage.
        if (!ReferenceEquals(draft, _interfaceScaleDraft) && !_source.TryPreviewInterfaceScale(this, draft.Target))
            return null;
        return _source.CaptureInterfaceScaleDraft(this, draft.Target);
    }

    private void RejectInterfaceScaleWrite(InterfaceScaleDraft draft) =>
        CompleteInterfaceScaleWrite(draft, new(false, "SettingsIntentRetired",
            Diagnostic: "Appearance changed. Choose an interface scale to preview again."));

    private void CompleteInterfaceScaleWrite(InterfaceScaleDraft draft, HomeThemeOperationResult receipt)
    {
        ClearInterfaceScalePreview();
        var saved = _source.Current;
        _interfaceScaleRetry = !receipt.Succeeded && receipt.Code != "PermissionDenied" &&
            draft.Scope == saved.ScopeId && draft.SavedScale == saved.Accessibility.DisplayScale ? draft : null;
        _interfaceScaleStatus = receipt.Succeeded ? $"Interface scale saved at {draft.Option.Label}." :
            _interfaceScaleRetry is not null ? "Interface scale was not saved. Apply to try again, or Revert." :
            receipt.Diagnostic ?? "Interface scale was not saved. Choose a size to try again.";
        RevealInterfaceScale(focus: true);
    }

    private void RevertInterfaceScale()
    {
        ClearInterfaceScalePreview();
        _interfaceScaleRetry = null;
        _interfaceScaleStatus = "Using the saved interface scale.";
        RefreshInterfaceScale();
        RevealInterfaceScale(focus: true);
    }

    private void ClearInterfaceScalePreview()
    {
        ++_interfaceScaleRequest;
        _interfaceScalePreviewPending = false;
        _interfaceScaleDraft = null;
        _source.EndInterfaceScalePreview(this);
    }

    private void OnInterfaceScaleKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None ||
            _interfaceScale.IsDropDownOpen || !IsInterfaceScalePage || IsBusy ||
            _interfaceScaleDraft is null && _interfaceScaleRetry is null && !_interfaceScalePreviewPending) return;
        args.Handled = true;
        RevertInterfaceScale();
    }

    private void RevealInterfaceScale(bool focus)
    {
        _restoreInterfaceScaleFocus |= focus;
        Dispatcher.UIThread.Post(TryRevealInterfaceScale, DispatcherPriority.Background);
    }

    private void TryRevealInterfaceScale()
    {
        if (!IsInterfaceScalePage || !_interfaceScale.IsAttachedToVisualTree()) return;
        if (_restoreInterfaceScaleFocus && _interfaceScale.IsEffectivelyEnabled && _interfaceScale.Focus(NavigationMethod.Tab))
            _restoreInterfaceScaleFocus = false;
        if (_interfaceScale.IsFocused) _interfaceScale.BringIntoView();
    }

    private void RetireInterfaceScale()
    {
        _interfaceScale.SelectionChanged -= OnInterfaceScaleSelected;
        _interfaceScale.PropertyChanged -= OnInterfaceScalePropertyChanged;
        _source.Context.Changed -= OnEffectiveInterfaceScaleChanged;
        RemoveHandler(KeyDownEvent, OnInterfaceScaleKeyDown);
        ClearInterfaceScalePreview();
    }
}
