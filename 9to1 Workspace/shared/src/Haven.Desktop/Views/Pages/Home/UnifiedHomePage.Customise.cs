using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private const string NewTabCustomisationPrefix = "home.newtab-customise-";
    private const string NewTabBackgroundSource = "New Tab background preference";
    private const string MissingNewTabBackground = "The saved background image is unavailable. Showing its built-in background.";
    private readonly Button _newTabCustomise = new();
    private readonly Button _newTabCustomiseDone = new();
    private readonly Button _newTabCustomiseAddWidget = new();
    private readonly Border _newTabCustomiseMount = new();
    private readonly CuiViewModel _newTabEditor = new();
    private readonly List<IDisposable> _newTabBackgroundObservations = [];
    private NewTabLayoutPreferences? _newTabLayoutPreferences;
    private NewTabLayoutPreferences.Snapshot? _newTabSavedLayout;
    private NewTabLayoutPreference? _newTabLayoutPreview;
    private Func<NewTabLayoutPreference, NewTabLayoutPreference>? _retryNewTabLayout;
    private CuiControlLoader? _newTabEditorLoader;
    private CuiContainerControl? _newTabBackdrop, _newTabHero, _newTabClockBlock, _newTabShortcutsBlock;
    private bool _newTabResetRequested, _newTabLayoutSaving, _newTabChoosingImage, _newTabLayoutEverAttached;
    private bool _focusNewTabEditorOnAttach;
    private bool _newTabVisibilityOpen;
    private string? _newTabEditorStatus, _newTabLoadedImageId;
    private bool _newTabImageUnavailable;
    private Bitmap? _newTabSavedImage;
    private NewTabBackgroundImageCandidate? _newTabImageCandidate;

    private bool IsNewTabCustomising => _newTabCustomiseMount.Child is not null;
    private bool NewTabLayoutBusy => _newTabLayoutSaving || _newTabChoosingImage;
    private bool CanCustomiseNewTab => _newTabLayoutPreferences?.CanEdit == true && _newTabSavedLayout is not null;
    private bool CanChangeNewTabLayout => !NewTabLayoutBusy && _retryNewTabLayout is null &&
        !_newTabResetRequested && _newTabImageCandidate is null;
    private bool CanAcceptNewTabImage => IsAlive && IsNewTabCustomising && _section == "newtab" && !NewTabLayoutBusy &&
        _newTabLayoutPreferences?.Assets is not null && _retryNewTabLayout is null && !_newTabResetRequested;
    private NewTabLayoutPreference EffectiveNewTabLayout => _newTabLayoutPreview ?? _newTabSavedLayout?.Layout ?? NewTabLayoutPreference.Default;

    partial void ApplyNewTabWidgets(NewTabLayoutPreference layout);

    private void RegisterNewTabCustomisation(NewTabLayoutPreferences? preferences)
    {
        if (_request.Address.Authority != "newtab") return;
        _newTabLayoutPreferences = preferences;
        _loader.BindAuthoredControl("home-newtab-customise-open", _ => _newTabCustomise);
        _loader.BindAuthoredControl("home-newtab-customise-done", _ => _newTabCustomiseDone);
        _loader.BindAuthoredControl("home-newtab-customise-add-widget", _ => _newTabCustomiseAddWidget);
        _loader.BindAuthoredControl("home-newtab-customise-mount", _ => _newTabCustomiseMount);
        _loader.BindAuthoredControl("home-main", _ => _newTabBackdrop = new("Grid"));
        _loader.BindAuthoredControl("home-newtab-hero", _ => _newTabHero = new("Vertical Stack"));
        _loader.BindAuthoredControl("home-newtab-clock", _ => _newTabClockBlock = new("Vertical Stack"));
        _loader.BindAuthoredControl("home-newtab-shortcuts-section", _ => _newTabShortcutsBlock = new("Vertical Stack"));
        AttachedToVisualTree += OnNewTabLayoutAttached;
        DetachedFromVisualTree += OnNewTabLayoutDetached;
        AddHandler(KeyDownEvent, OnNewTabCustomisationKey, RoutingStrategies.Tunnel);
    }

    private void OnNewTabLayoutAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (_newTabBackdrop is not { } backdrop) return;
        var observer = new NewTabResourceObserver(ApplyNewTabBackground);
        foreach (var key in new[] { "CuiBackgroundBrush", "CuiAccentPrimaryColor", "CuiHighContrast" })
            _newTabBackgroundObservations.Add(backdrop.GetResourceObservable(key).Subscribe(observer));
        if (_newTabLayoutEverAttached && _newTabSavedLayout is not null && !IsNewTabCustomising && !NewTabLayoutBusy)
            _ = _work.RunAsync(original => RefreshNewTabLayoutAsync(original, original.Token));
        _newTabLayoutEverAttached = true;
        // Deep links may open the editor before a native TopLevel is available.
        if (IsNewTabCustomising) PublishNewTabLayout();
        if (_focusNewTabEditorOnAttach)
        {
            _focusNewTabEditorOnAttach = false;
            FocusNewTabEditorControl("home-newtab-layout-focus");
        }
    }

    private void OnNewTabLayoutDetached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        foreach (var observer in _newTabBackgroundObservations) observer.Dispose();
        _newTabBackgroundObservations.Clear();
    }

    private Task WhenNewTabCustomisationActionsIdleAsync() => _newTabEditorLoader?.WhenActionsIdleAsync() ?? Task.CompletedTask;

    private void StopNewTabCustomisation()
    {
        AttachedToVisualTree -= OnNewTabLayoutAttached;
        DetachedFromVisualTree -= OnNewTabLayoutDetached;
        RemoveHandler(KeyDownEvent, OnNewTabCustomisationKey);
        foreach (var observer in _newTabBackgroundObservations) observer.Dispose();
        _newTabBackgroundObservations.Clear();
        if (_newTabBackdrop is { } backdrop)
            CuiPropertyPresentation.Remove(backdrop, "Background", CuiPropertyLevel.Instance, NewTabBackgroundSource);
        _newTabEditorLoader?.Dispose();
    }

    private void CleanUpNewTabCustomisation()
    {
        _newTabCustomiseMount.Child = null;
        _newTabImageCandidate?.Dispose();
        _newTabSavedImage?.Dispose();
    }

    private async Task RefreshNewTabLayoutAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_section != "newtab" || NewTabLayoutBusy) return;
        if (_newTabLayoutPreferences is not { } owner) { PublishNewTabLayout(); return; }
        try
        {
            var snapshot = await original.AwaitAsync(owner.ReadAsync(token));
            if (!original.IsAcceptedPublicationCurrent || _section != "newtab" || NewTabLayoutBusy) return;
            _newTabSavedLayout = snapshot;
            await RefreshSavedNewTabImageAsync(original, token);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            if (original.IsAcceptedPublicationCurrent)
            {
                _newTabEditorStatus = "The saved layout could not be read. Your settings were preserved.";
                Set(_status, "Text", _newTabEditorStatus);
            }
        }
        if (original.IsAcceptedPublicationCurrent) PublishNewTabLayout();
    }

    private bool NewTabCustomisationActionAvailable(string command)
    {
        if (!IsAlive || !CanCustomiseNewTab || _section != "newtab" || NewTabLayoutBusy) return false;
        if (command == NewTabCustomisationPrefix + "open") return !IsNewTabCustomising && !ShortcutSurfaceOpen;
        if (!IsNewTabCustomising) return false;
        return command[NewTabCustomisationPrefix.Length..] switch
        {
            "retry" or "revert" => _retryNewTabLayout is not null,
            "done" => _retryNewTabLayout is null && !ShortcutSurfaceOpen,
            "visibility" => CanChangeNewTabLayout,
            "widget-visibility" => _newTabVisibilityOpen && _newTabWidgetSources is not null && CanEditWidgetLayout,
            "accent" => _createSettings is not null && CanChangeNewTabLayout,
            "upload" => CanAcceptNewTabImage && TopLevel.GetTopLevel(this)?.StorageProvider.CanOpen == true,
            "use-image" or "cancel-image" => _newTabImageCandidate is not null,
            "confirm-reset" or "cancel-reset" => _newTabResetRequested,
            "custom" or "remove-image" or "fit" or "fill" => EffectiveNewTabLayout.BackgroundImageId is not null && CanChangeNewTabLayout,
            "focus" or "balanced" or "more-widgets" or "neutral" or "horizon" or "paper" or "clock" or "shortcuts" or "reset" =>
                CanChangeNewTabLayout,
            _ => false
        };
    }

    private async Task DispatchNewTabCustomisationAsync(string command, object? parameter,
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (!NewTabCustomisationActionAvailable(command)) return;
        token.ThrowIfCancellationRequested();
        switch (command[NewTabCustomisationPrefix.Length..])
        {
            case "open": OpenNewTabCustomisation(); return;
            case "done": CloseNewTabCustomisation(); return;
            case "visibility": _newTabVisibilityOpen = !_newTabVisibilityOpen; PublishNewTabLayout(); return;
            case "widget-visibility":
                if (parameter is string id) await ToggleNewTabWidgetVisibilityAsync(id, original, token);
                return;
            case "accent":
                if (!Queue(AppContentAddress.Parse("browse://home/settings/appearance-palette"), original, newTab: true))
                {
                    _newTabEditorStatus = "Accent settings could not open. Try again when Browse is ready.";
                    PublishNewTabLayout();
                }
                return;
            case "reset": _newTabResetRequested = true; PublishNewTabLayout(); FocusNewTabEditorControl("home-newtab-reset-cancel"); return;
            case "cancel-reset":
                _newTabResetRequested = false; PublishNewTabLayout(); FocusNewTabEditorControl("home-newtab-reset"); return;
            case "revert":
                _newTabLayoutPreview = null; _retryNewTabLayout = null; _newTabEditorStatus = null;
                await RefreshNewTabLayoutAsync(original, token); return;
            case "retry": await RetryNewTabLayoutAsync(original, token); return;
            case "upload": await PickNewTabBackgroundAsync(null, original, token); return;
            case "cancel-image": CancelNewTabBackgroundPreview(); return;
            case "use-image": await AdoptNewTabBackgroundAsync(original, token); return;
        }

        Func<NewTabLayoutPreference, NewTabLayoutPreference> change = command[NewTabCustomisationPrefix.Length..] switch
        {
            "focus" => layout => layout with { Preset = NewTabLayoutPreset.Focus },
            "balanced" => layout => layout with { Preset = NewTabLayoutPreset.Balanced },
            "more-widgets" => layout => layout with { Preset = NewTabLayoutPreset.MoreWidgets },
            "neutral" => layout => layout with { Background = NewTabBackgroundPreset.Neutral, ShowCustomBackground = false },
            "horizon" => layout => layout with { Background = NewTabBackgroundPreset.Horizon, ShowCustomBackground = false },
            "paper" => layout => layout with { Background = NewTabBackgroundPreset.Paper, ShowCustomBackground = false },
            "custom" => layout => layout with { ShowCustomBackground = true },
            "remove-image" => layout => layout with { ShowCustomBackground = false, BackgroundImageId = null },
            "fit" => layout => layout with { FillBackgroundImage = false },
            "fill" => layout => layout with { FillBackgroundImage = true },
            "clock" => layout => layout with { ShowClock = !layout.ShowClock },
            "shortcuts" => layout => layout with { ShowShortcuts = !layout.ShowShortcuts },
            "confirm-reset" => ResetNewTabLayout,
            _ => throw new InvalidOperationException("Unknown New Tab layout action.")
        };
        _newTabResetRequested = false;
        await ChangeNewTabLayoutAsync(change, original, token);
        if (command == NewTabCustomisationPrefix + "confirm-reset") FocusNewTabEditorControl("home-newtab-reset");
    }

    private static NewTabLayoutPreference ResetNewTabLayout(NewTabLayoutPreference layout)
    {
        var defaultOrder = NewTabLayoutPreference.Default.Widgets
            .Select((widget, index) => (widget.Id, Index: index))
            .ToDictionary(entry => entry.Id, entry => entry.Index, StringComparer.Ordinal);
        var defaultShortcutOrder = NewTabShortcutPlacement.Defaults
            .Select((shortcut, index) => (shortcut.Id, Index: index))
            .ToDictionary(entry => entry.Id, entry => entry.Index, StringComparer.Ordinal);
        return layout with
        {
            Preset = NewTabLayoutPreset.Balanced, ShowClock = true, ShowShortcuts = true,
            Shortcuts = [.. layout.Shortcuts.OrderBy(shortcut => defaultShortcutOrder.GetValueOrDefault(shortcut.Id, int.MaxValue))],
            Widgets = [.. layout.Widgets.OrderBy(widget => defaultOrder.GetValueOrDefault(widget.Id, int.MaxValue))
                .Select(widget => widget with { IsVisible = true, IsWide = false, IsPinned = false, IsLocked = false })]
        };
    }

    private async Task ToggleNewTabWidgetVisibilityAsync(string id, DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (NewTabWidgetSources.Catalogue.FirstOrDefault(widget => widget.Id == id) is not { } definition) return;
        var show = FindWidget(id) is not { IsVisible: true };
        await ChangeNewTabLayoutAsync(layout =>
        {
            var settings = layout.Widgets;
            var placement = settings.FirstOrDefault(widget => widget.Id == id);
            var updated = show
                ? EditWidgetVisibility(settings, definition, placement is null ? "add" : "show")
                : EditWidgetOptions(settings, id, "hide", null);
            return layout with { Widgets = updated };
        }, original, token);
        if (show && _retryNewTabLayout is null && original.IsAcceptedPublicationCurrent)
            await RefreshNewTabWidgetsAsync(original, token, id);
    }

    private async Task RetryNewTabLayoutAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_retryNewTabLayout is not { } change || _newTabLayoutPreferences is not { } owner) return;
        _newTabLayoutSaving = true;
        try
        {
            PublishNewTabLayout();
            _newTabSavedLayout = await original.AwaitAsync(owner.ReadAsync(token));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            _newTabEditorStatus = "The saved layout could not be read. Try again or revert your change.";
            return;
        }
        finally
        {
            _newTabLayoutSaving = false;
            if (original.IsAcceptedPublicationCurrent) PublishNewTabLayout();
        }
        if (original.IsAcceptedPublicationCurrent && _newTabSavedLayout is { } persisted)
        {
            await ChangeNewTabLayoutAsync(change, original, token);
            if (_retryNewTabLayout is null && original.IsAcceptedPublicationCurrent &&
                _newTabSavedLayout is { } refreshed && !persisted.Layout.Widgets.SequenceEqual(refreshed.Layout.Widgets))
                await RefreshNewTabWidgetsAsync(original, token);
        }
    }

    private async Task ChangeNewTabLayoutAsync(Func<NewTabLayoutPreference, NewTabLayoutPreference> change,
        DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_newTabLayoutPreferences is not { CanEdit: true } owner || _newTabSavedLayout is not { } saved ||
            _newTabLayoutSaving || _section != "newtab" ||
            _retryNewTabLayout is { } pending && !ReferenceEquals(change, pending)) return;
        var candidate = change(saved.Layout).Validate();
        if ((candidate with { Widgets = saved.Layout.Widgets, Shortcuts = saved.Layout.Shortcuts }) == saved.Layout &&
            candidate.Widgets.SequenceEqual(saved.Layout.Widgets) && candidate.Shortcuts.SequenceEqual(saved.Layout.Shortcuts))
        {
            _retryNewTabLayout = null; _newTabLayoutPreview = null; _newTabEditorStatus = null;
            PublishNewTabLayout(); return;
        }
        var backgroundChanged = candidate.Background != saved.Layout.Background || candidate.BackgroundImageId != saved.Layout.BackgroundImageId ||
            candidate.ShowCustomBackground != saved.Layout.ShowCustomBackground || candidate.FillBackgroundImage != saved.Layout.FillBackgroundImage;
        _newTabLayoutSaving = true;
        _newTabLayoutPreview = candidate;
        _newTabEditorStatus = "Saving New Tab layout…";
        try
        {
            PublishNewTabLayout();
            var result = await original.AwaitAsync(owner.SaveAsync(saved, candidate, token));
            _newTabSavedLayout = result.Current;
            _retryNewTabLayout = result.Saved ? null : change;
            _newTabLayoutPreview = result.Saved || backgroundChanged ? null : candidate;
            _newTabEditorStatus = result.Saved ? "New Tab layout saved." :
                "New Tab changed in another tab. Retry applies your change to the latest layout, or revert.";
            if (original.IsAcceptedPublicationCurrent) await RefreshSavedNewTabImageAsync(original, token);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            _retryNewTabLayout = change;
            if (backgroundChanged) _newTabLayoutPreview = null;
            _newTabEditorStatus = "New Tab layout could not be saved. Retry or revert your change.";
        }
        finally
        {
            _newTabLayoutSaving = false;
            if (original.IsAcceptedPublicationCurrent) PublishNewTabLayout();
        }
    }

    private void OpenNewTabCustomisation()
    {
        if (_newTabEditorLoader is null)
        {
            using var stream = typeof(UnifiedHomePage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.NewTabCustomisation.cui")
                ?? throw new InvalidDataException("The New Tab editor document is missing.");
            using var reader = new StreamReader(stream);
            var loader = new CuiControlLoader(HomeAppsCuiDocument.CreateControlRegistry());
            loader.SetActionDispatcher(this);
            loader.SetBindingContext(_newTabEditor);
            loader.BindAuthoredControl("home-newtab-editor-fields", _ =>
            {
                var fields = new CuiContainerControl("Grid");
                fields.SizeChanged += (_, args) => SetNewTabEditorColumns(args.NewSize.Width);
                return fields;
            });
            SetNewTabEditorColumns(0);
            if (_newTabLayoutPreferences?.Assets is not null)
                loader.BindAuthoredControl("home-newtab-image-drop", _ => CreateNewTabImageDropTarget());
            var loaded = loader.LoadMarkup(reader.ReadToEnd(), "NewTabCustomisation.cui");
            if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                throw new InvalidDataException("The New Tab editor could not be rendered: " + string.Join("; ", loaded.Diagnostics));
            }
            loader.WireBindings(loaded.Root);
            _newTabEditorLoader = loader;
            _newTabEditorRoot = loaded.Root;
        }
        _newTabCustomiseMount.Child = _newTabEditorRoot;
        Set(_newTabCustomiseMount, "IsVisible", true);
        PublishNewTabLayout();
        if (this.IsAttachedToVisualTree()) FocusNewTabEditorControl("home-newtab-layout-focus");
        else _focusNewTabEditorOnAttach = true;
    }

    private Control? _newTabEditorRoot;

    private void SetNewTabEditorColumns(double width)
    {
        var wide = width >= 700;
        _newTabEditor.Set("EditorColumns", wide ? "1fr 1fr" : "1fr");
        _newTabEditor.Set("EditorRows", wide ? "Auto" : "Auto Auto");
        _newTabEditor.Set("BackgroundColumn", wide ? 1 : 0);
        _newTabEditor.Set("BackgroundRow", wide ? 0 : 1);
        var choiceColumns = width >= 330 ? 3 : width >= 218 ? 2 : 1;
        _newTabEditor.Set("ChoiceColumns", choiceColumns == 3 ? "1fr 1fr 1fr" : choiceColumns == 2 ? "1fr 1fr" : "1fr");
        _newTabEditor.Set("ChoiceRows", choiceColumns == 3 ? "Auto" : choiceColumns == 2 ? "Auto Auto" : "Auto Auto Auto");
        _newTabEditor.Set("SecondChoiceRow", 1 / choiceColumns);
        _newTabEditor.Set("SecondChoiceColumn", 1 % choiceColumns);
        _newTabEditor.Set("ThirdChoiceRow", 2 / choiceColumns);
        _newTabEditor.Set("ThirdChoiceColumn", 2 % choiceColumns);
    }

    private void CloseNewTabCustomisation()
    {
        _newTabVisibilityOpen = false;
        _newTabResetRequested = false;
        _newTabLayoutPreview = null;
        _newTabEditorStatus = null;
        _newTabCustomiseMount.Child = null;
        Set(_newTabCustomiseMount, "IsVisible", false);
        CancelNewTabBackgroundPreview(restoreFocus: false);
        FocusAfterSelection(() => _newTabCustomise.Focus(NavigationMethod.Tab));
    }

    private void FocusNewTabEditorControl(string id) => FocusAfterSelection(() =>
        _newTabEditorRoot?.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(control => control.Name == id)?.Focus(NavigationMethod.Tab));

    private void OnNewTabCustomisationKey(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None || _section != "newtab" || !IsNewTabCustomising || ShortcutSurfaceOpen ||
            _widgetPickerOpen || _widgetOptionsId is not null || _focusTimerResetRequested || _widgetDrag is not null) return;
        args.Handled = true;
        if (NewTabLayoutBusy || _retryNewTabLayout is not null) return;
        if (_newTabImageCandidate is not null) CancelNewTabBackgroundPreview();
        else if (_newTabResetRequested)
        {
            _newTabResetRequested = false;
            PublishNewTabLayout();
            FocusNewTabEditorControl("home-newtab-reset");
        }
        else CloseNewTabCustomisation();
    }

    private void PublishNewTabLayout()
    {
        if (_newTabClockBlock is null || _newTabShortcutsBlock is null || _newTabHero is null) return;
        var layout = EffectiveNewTabLayout;
        var missingBackground = layout.ShowCustomBackground && _newTabImageUnavailable;
        if (_section == "newtab")
        {
            var showCanvasStatus = missingBackground && !IsNewTabCustomising;
            if (showCanvasStatus && string.IsNullOrEmpty(_status.Text)) Set(_status, "Text", MissingNewTabBackground);
            else if (!showCanvasStatus && _status.Text == MissingNewTabBackground) Set(_status, "Text", string.Empty);
        }
        Set(_newTabCustomise, "IsVisible", CanCustomiseNewTab && !IsNewTabCustomising);
        Set(_newTabCustomiseDone, "IsVisible", IsNewTabCustomising);
        Set(_newTabCustomiseAddWidget, "IsVisible", IsNewTabCustomising && _newTabWidgetSources is not null);
        Set(_newTabClockBlock, "IsVisible", layout.ShowClock);
        Set(_newTabShortcutsBlock, "IsVisible", layout.ShowShortcuts);
        Set(_newTabHero, "Margin", layout.Preset == NewTabLayoutPreset.Focus ? new Thickness(0, 26, 0, 10) : default(Thickness));
        Set(_newTabHero, "CellGap", layout.Preset == NewTabLayoutPreset.MoreWidgets ? 8d : 14d);
        Set(_newTabTime, "FontSize", layout.Preset == NewTabLayoutPreset.MoreWidgets ? 42d : _newTabHero.Bounds.Width >= 520 ? 58d : 46d);
        ApplyNewTabBackground();
        ApplyNewTabWidgets(layout);
        PublishNewTabShortcuts();

        _newTabEditor.Set("FocusSelected", layout.Preset == NewTabLayoutPreset.Focus);
        _newTabEditor.Set("BalancedSelected", layout.Preset == NewTabLayoutPreset.Balanced);
        _newTabEditor.Set("MoreWidgetsSelected", layout.Preset == NewTabLayoutPreset.MoreWidgets);
        _newTabEditor.Set("NeutralSelected", !layout.ShowCustomBackground && layout.Background == NewTabBackgroundPreset.Neutral);
        _newTabEditor.Set("HorizonSelected", !layout.ShowCustomBackground && layout.Background == NewTabBackgroundPreset.Horizon);
        _newTabEditor.Set("PaperSelected", !layout.ShowCustomBackground && layout.Background == NewTabBackgroundPreset.Paper);
        _newTabEditor.Set("ClockVisible", layout.ShowClock);
        _newTabEditor.Set("ShortcutsVisible", layout.ShowShortcuts);
        _newTabEditor.Set("ResetRequested", _newTabResetRequested);
        _newTabEditor.Set("HasFailedChange", _retryNewTabLayout is not null);
        _newTabEditor.Set("CanEdit", CanChangeNewTabLayout);
        _newTabEditor.Set("CanOpenAccent", NewTabCustomisationActionAvailable(NewTabCustomisationPrefix + "accent"));
        _newTabEditor.Set("CanConfirm", !NewTabLayoutBusy);
        var status = _newTabEditorStatus ?? (missingBackground ? MissingNewTabBackground : string.Empty);
        _newTabEditor.Set("EditorStatus", status);
        _newTabEditor.Set("HasEditorStatus", status.Length != 0);
        _newTabEditor.Set("VisibilityOpen", _newTabVisibilityOpen);
        _newTabEditor.Set("VisibilityChevron", _newTabVisibilityOpen ? "icon:chevron-up" : "icon:chevron-down");
        _newTabEditor.Set("WidgetVisibility", _newTabWidgetSources is null ? Array.Empty<Dictionary<string, object?>>() : NewTabWidgetSources.Catalogue
            .Select(widget => new Dictionary<string, object?>
            {
                ["Id"] = widget.Id,
                ["Title"] = widget.Title,
                ["IsShown"] = layout.Widgets.Any(placement => placement.Id == widget.Id && placement.IsVisible),
                ["CanEdit"] = CanEditWidgetLayout
            })
            .ToArray());
        _newTabEditor.Set("CanUpload", _newTabLayoutPreferences?.Assets is not null);
        _newTabEditor.Set("CanPickImage", NewTabCustomisationActionAvailable(NewTabCustomisationPrefix + "upload") && _newTabImageCandidate is null);
        _newTabEditor.Set("HasImagePreview", _newTabImageCandidate is not null);
        _newTabEditor.Set("HasSavedImage", layout.BackgroundImageId is not null);
        _newTabEditor.Set("CustomSelected", layout.ShowCustomBackground);
        _newTabEditor.Set("FillSelected", layout.FillBackgroundImage);
        _newTabEditor.Set("FitSelected", !layout.FillBackgroundImage);
    }

    private CuiContainerControl CreateNewTabImageDropTarget()
    {
        var target = new CuiContainerControl("Grid");
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragOverEvent, OnNewTabImageDragOver);
        target.AddHandler(DragDrop.DropEvent, OnNewTabImageDrop);
        return target;
    }

    private void OnNewTabImageDragOver(object? sender, DragEventArgs args)
    {
        args.DragEffects = CanAcceptNewTabImage &&
            args.DataTransfer.TryGetFiles() is [IStorageFile] ? DragDropEffects.Copy : DragDropEffects.None;
        args.Handled = true;
    }

    private async void OnNewTabImageDrop(object? sender, DragEventArgs args)
    {
        args.Handled = true;
        if (!CanAcceptNewTabImage || args.DataTransfer.TryGetFiles() is not [IStorageFile file]) return;
        using (file)
            try { await _work.RunAsync(original => PickNewTabBackgroundAsync(file, original, original.Token)); }
            catch (Exception error) { System.Diagnostics.Trace.TraceError("New Tab background drop failed: {0}", error); }
    }

    private async Task PickNewTabBackgroundAsync(IStorageFile? dropped, DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_newTabLayoutPreferences?.Assets is not { } assets || TopLevel.GetTopLevel(this) is not { } window) return;
        _newTabChoosingImage = true;
        try
        {
            PublishNewTabLayout();
            NewTabBackgroundImageCandidate? candidate;
            if (dropped is null) candidate = await original.AwaitAsync(assets.PickAsync(window, token));
            else
            {
                await using var input = await original.AwaitAsync(dropped.OpenReadAsync());
                candidate = await original.AwaitAsync(assets.ReadCandidateAsync(input, token));
            }
            if (candidate is null) return;
            if (!original.IsAcceptedPublicationCurrent || !IsNewTabCustomising) { candidate.Dispose(); return; }
            _newTabImageCandidate?.Dispose();
            _newTabImageCandidate = candidate;
            _newTabEditorStatus = null;
            FocusNewTabEditorControl("home-newtab-use-image");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        { _newTabEditorStatus = "This image could not be opened. Your saved background was preserved."; }
        finally
        {
            _newTabChoosingImage = false;
            if (original.IsAcceptedPublicationCurrent) PublishNewTabLayout();
        }
    }

    private async Task AdoptNewTabBackgroundAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        if (_newTabLayoutPreferences?.Assets is not { } assets || _newTabImageCandidate is not { } preview) return;
        _newTabChoosingImage = true;
        try
        {
            PublishNewTabLayout();
            var id = await original.AwaitAsync(assets.AdoptAsync(preview, token));
            if (!original.IsAcceptedPublicationCurrent) return;
            CancelNewTabBackgroundPreview(restoreFocus: false);
            await ChangeNewTabLayoutAsync(layout => layout with { BackgroundImageId = id, ShowCustomBackground = true }, original, token);
            FocusNewTabEditorControl("home-newtab-upload");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        { _newTabEditorStatus = "This image could not be saved. Try Use background again or cancel."; }
        finally
        {
            _newTabChoosingImage = false;
            if (original.IsAcceptedPublicationCurrent) PublishNewTabLayout();
        }
    }

    private void CancelNewTabBackgroundPreview(bool restoreFocus = true)
    {
        var candidate = _newTabImageCandidate;
        _newTabImageCandidate = null;
        PublishNewTabLayout();
        candidate?.Dispose();
        if (restoreFocus) FocusNewTabEditorControl("home-newtab-upload");
    }

    private async Task RefreshSavedNewTabImageAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var id = _newTabSavedLayout?.Layout.BackgroundImageId;
        if (id == _newTabLoadedImageId && !_newTabImageUnavailable) return;
        Bitmap? bitmap = null;
        if (id is not null && _newTabLayoutPreferences?.Assets is { } assets)
            try { bitmap = await original.AwaitAsync(assets.LoadAsync(id, token)); }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        if (!original.IsAcceptedPublicationCurrent || id != _newTabSavedLayout?.Layout.BackgroundImageId)
        { bitmap?.Dispose(); return; }
        var replaced = _newTabSavedImage;
        _newTabSavedImage = bitmap;
        _newTabLoadedImageId = id;
        _newTabImageUnavailable = id is not null && bitmap is null;
        ApplyNewTabBackground();
        replaced?.Dispose();
    }

    private void ApplyNewTabBackground()
    {
        if (_newTabBackdrop is not { } host || !IsAlive) return;
        var layout = EffectiveNewTabLayout;
        if (_section != "newtab" || host.TryFindResource("CuiHighContrast", out var highContrast) && highContrast is true ||
            !host.TryFindResource("CuiBackgroundBrush", out var resource) || resource is not ISolidColorBrush background)
        {
            CuiPropertyPresentation.Remove(host, "Background", CuiPropertyLevel.Instance, NewTabBackgroundSource);
            return;
        }
        var accent = host.TryFindResource("CuiAccentPrimaryColor", out var value) && value is Color color ? color : background.Color;
        Color Tint(double weight) => Color.FromRgb(Blend(background.Color.R, accent.R, weight),
            Blend(background.Color.G, accent.G, weight), Blend(background.Color.B, accent.B, weight));
        static byte Blend(byte source, byte target, double weight) => (byte)Math.Round(source + (target - source) * weight);
        var horizon = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = [new(background.Color, .25), new(Tint(.12), 1)]
        };
        var paper = new SolidColorBrush(Tint(.06));
        _newTabEditor.Set("HorizonPreview", horizon);
        _newTabEditor.Set("PaperPreview", paper);
        var image = _newTabImageCandidate?.Bitmap ?? (layout.ShowCustomBackground && layout.BackgroundImageId == _newTabLoadedImageId ? _newTabSavedImage : null);
        IBrush? paint = image is not null ? NewTabImageBrush(image, background.Color, layout.FillBackgroundImage) : layout.Background switch
        {
            NewTabBackgroundPreset.Horizon => horizon,
            NewTabBackgroundPreset.Paper => paper,
            _ => null
        };
        if (paint is null) CuiPropertyPresentation.Remove(host, "Background", CuiPropertyLevel.Instance, NewTabBackgroundSource);
        else CuiPropertyPresentation.Set(host, "Background", paint, CuiPropertyLevel.Instance, NewTabBackgroundSource);
    }

    private static DrawingBrush NewTabImageBrush(Bitmap image, Color background, bool fill)
    {
        var bounds = new Rect(image.Size);
        var drawing = new DrawingGroup();
        drawing.Children.Add(new ImageDrawing { ImageSource = image, Rect = bounds });
        drawing.Children.Add(new GeometryDrawing
        {
            Geometry = new RectangleGeometry(bounds),
            Brush = new SolidColorBrush(Color.FromArgb(0xC2, background.R, background.G, background.B))
        });
        return new DrawingBrush(drawing) { Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform };
    }

    private sealed class NewTabResourceObserver(Action update) : IObserver<object?>
    {
        public void OnNext(object? value) => update();
        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }

}
