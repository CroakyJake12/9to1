using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class UnifiedHomePage
{
    private const string widgetActionPrefix = "home.newtab-widget-";
    private readonly CuiViewModel _widgetBindings = new();
    private readonly Dictionary<(string Id, string Source), (NewTabWidgetContent Content, DateTimeOffset Refreshed)> _widgetContent = [];
    private readonly Dictionary<string, (NewTabWidgetPlacement Placement, NewTabWidgetItem Item)> _widgetItems = new(StringComparer.Ordinal);
    private NewTabWidgetSources? _newTabWidgetSources;
    private CuiControlLoader? _widgetLoader;
    private Border? _newTabWidgetMount;
    private CuiDocument? _widgetPickerDocument;
    private CuiControlLoader? _widgetPickerLoader;
    private Border? _widgetPickerBackdrop;
    private Border? _widgetPickerFrame;
    private Panel? _widgetPickerHost;
    private string _widgetPickerTrigger = "newtab-widget-picker-open";
    private NewTabLayoutPreference _widgetLayout = NewTabLayoutPreference.Default;
    private IReadOnlyList<NewTabWidgetSourceChoice> _widgetChoices = [];
    private bool _widgetLayoutWritable, _widgetPickerOpen, _widgetBusy;
    private string? _widgetOptionsId;
    private string _widgetNotice = string.Empty;
    private string _widgetSourceNotice = string.Empty;
    private DispatcherTimer? _focusTimerTicker;
    private Stopwatch? _focusTimerRun;
    private TimeSpan _focusTimerRemaining;
    private TimeSpan _focusTimerDuration = TimeSpan.FromMinutes(25);
    private bool _focusTimerResetRequested;
    private WidgetDrag? _widgetDrag;
    private Task? _widgetDragCommit;

    private sealed record WidgetDrag(IPointer Pointer, string SourceId, Control Source, Button Handle, Point Start,
        bool IsDragging = false, Control? Target = null);
    private sealed record WidgetReorder(string SourceId, string TargetId);

    private const string WidgetDragPresentation = "New Tab widget drag";

    private void RegisterNewTabWidgets(NewTabWidgetSources? sources, bool layoutWritable)
    {
        if (_request.Address.Authority != "newtab" || sources is null) return;
        _newTabWidgetSources = sources;
        _widgetLayoutWritable = layoutWritable;
        _loader.BindAuthoredControl("home-newtab-widgets-mount", _ => _newTabWidgetMount = new Border());
    }

    private void MountNewTabWidgets()
    {
        if (_newTabWidgetMount is null || _newTabWidgetSources is null) return;
        using var stream = typeof(UnifiedHomePage).Assembly.GetManifestResourceStream("Haven.Desktop.Home.NewTabWidgets.cui")
            ?? throw new InvalidDataException("The New Tab widgets document is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "NewTabWidgets.cui");
        var parseErrors = parser.Diagnostics.Diagnostics.Where(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error).ToArray();
        if (parseErrors.Length != 0)
            throw new InvalidDataException("The New Tab widgets document is invalid. " + string.Join(" ", parseErrors.AsEnumerable()));

        var theme = document.Components.Single(component => component.IsThemeScope);
        _widgetPickerDocument = WidgetDocumentFor(document, theme, "newtab-widgets-picker-page");
        _widgetLoader = CreateWidgetLoader();
        _widgetBindings.Set("WidgetQuery", string.Empty);
        ApplyNewTabWidgets(_widgetLayout);
        _newTabWidgetMount.Child = LoadWidgetDocument(_widgetLoader, WidgetDocumentFor(document, theme, "newtab-widgets-content-page"));
        _newTabWidgetMount.SizeChanged += OnWidgetViewportChanged;
        _widgetBindings.PropertyChanged += OnWidgetQueryChanged;
        SizeChanged += OnWidgetPickerPageSizeChanged;
        DetachedFromVisualTree += OnWidgetPickerDetached;
        AddHandler(KeyDownEvent, OnWidgetKeyDown, RoutingStrategies.Tunnel);
        _newTabWidgetMount.AddHandler(PointerPressedEvent, OnWidgetPointerPressed, RoutingStrategies.Tunnel);
        _newTabWidgetMount.AddHandler(PointerMovedEvent, OnWidgetPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        _newTabWidgetMount.AddHandler(PointerReleasedEvent, OnWidgetPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        _newTabWidgetMount.PointerCaptureLost += OnWidgetPointerCaptureLost;
    }

    private static CuiDocument WidgetDocumentFor(CuiDocument document, CuiComponent theme, string page)
    {
        var fragment = theme.Children.Single(component => component.AuthoredId == page);
        var scope = new CuiComponent(theme.Type, theme.Name, theme.Classes, theme.Properties, theme.Actions,
            [fragment], theme.Condition, theme.List, theme.Span, theme.DefaultTheme, theme.Repeat, theme.ElseChildren,
            theme.Groups, theme.IsDefinition, theme.PropertyRegion);
        return document with { Components = [scope] };
    }

    private CuiControlLoader CreateWidgetLoader()
    {
        var loader = new CuiControlLoader(HomeAppsCuiDocument.CreateControlRegistry());
        loader.SetBindingContext(_widgetBindings);
        loader.SetActionDispatcher(this);
        return loader;
    }

    private static Control LoadWidgetDocument(CuiControlLoader loader, CuiDocument document)
    {
        var loaded = loader.TryLoad(document);
        var errors = loaded.Diagnostics.Where(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error).ToArray();
        if (loaded.Root is null || errors.Length != 0)
            throw new InvalidDataException("New Tab widgets could not be rendered: " +
                string.Join("; ", errors.Select(diagnostic => diagnostic.Code + " " + diagnostic.Message)));
        loader.WireBindings(loaded.Root);
        return loaded.Root;
    }

    private void OpenWidgetPicker()
    {
        if (Content is not Panel home || _widgetPickerDocument is null) return;
        if (_widgetPickerBackdrop is null)
        {
            var loader = CreateWidgetLoader();
            Control picker;
            try { picker = LoadWidgetDocument(loader, _widgetPickerDocument); }
            catch { loader.Dispose(); throw; }
            _widgetPickerLoader = loader;
            _widgetPickerFrame = new Border
            {
                Child = picker,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            KeyboardNavigation.SetTabNavigation(_widgetPickerFrame, KeyboardNavigationMode.Cycle);
            _widgetPickerBackdrop = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(85, 0, 0, 0)),
                Child = _widgetPickerFrame,
                ZIndex = 100
            };
            _widgetPickerBackdrop.PointerPressed += OnWidgetPickerBackdropPressed;
        }
        _widgetPickerOpen = true;
        _widgetPickerHost = home;
        home.Children.Add(_widgetPickerBackdrop);
        ApplyPickerSize();
        FocusWidgetControl("newtab-widget-search");
    }

    private void OnWidgetPickerBackdropPressed(object? sender, PointerPressedEventArgs args)
    {
        if (!_widgetPickerOpen || !ReferenceEquals(args.Source, _widgetPickerBackdrop) ||
            !args.GetCurrentPoint(_widgetPickerBackdrop).Properties.IsLeftButtonPressed) return;
        CloseWidgetEditor();
        args.Handled = true;
    }

    private void OnWidgetPickerPageSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (_widgetPickerOpen)
        {
            ApplyPickerSize();
            if (args.WidthChanged && IsAlive) PublishNewTabWidgets();
        }
    }

    private void ApplyPickerSize()
    {
        if (_widgetPickerFrame is not { } frame) return;
        frame.Width = Math.Min(690, Math.Max(1, Bounds.Width - 40));
        frame.Height = Math.Min(570, Math.Max(1, Bounds.Height - 40));
    }

    private void OnWidgetPickerDetached(object? sender, VisualTreeAttachmentEventArgs args) => HideWidgetPicker();

    private void HideWidgetPicker()
    {
        _widgetPickerOpen = false;
        if (_widgetPickerHost is { } home && _widgetPickerBackdrop is { } overlay)
            home.Children.Remove(overlay);
        _widgetPickerHost = null;
    }

    private void OnWidgetViewportChanged(object? sender, SizeChangedEventArgs args)
    {
        if (args.WidthChanged && IsAlive) PublishNewTabWidgets();
    }

    private void OnWidgetQueryChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == "WidgetQuery" && IsAlive) PublishNewTabWidgets();
    }

    private void OnWidgetKeyDown(object? sender, KeyEventArgs args)
    {
        if (_section != "newtab" || args.Key != Key.Escape || args.KeyModifiers != KeyModifiers.None) return;
        if (_widgetDrag is not null)
        {
            EndWidgetDrag();
            args.Handled = true;
            return;
        }
        if (!_widgetPickerOpen && _widgetOptionsId is null && !_focusTimerResetRequested) return;
        if (_focusTimerResetRequested)
        {
            _focusTimerResetRequested = false;
            PublishTimer();
        }
        else CloseWidgetEditor();
        args.Handled = true;
    }

    private void OnWidgetPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (_section != "newtab" || _widgetBusy || !CanEditWidgetLayout || _widgetDrag is not null ||
            !args.GetCurrentPoint(this).Properties.IsLeftButtonPressed || args.Source is not Visual hit)
            return;
        var lineage = hit.GetVisualAncestors().Prepend(hit).OfType<Control>().ToArray();
        if (lineage.OfType<Button>().FirstOrDefault(control => control.Name == "newtab-widget-drag" && control.IsEffectivelyEnabled) is not { } handle ||
            lineage.FirstOrDefault(control => control.Name == "newtab-widget-card") is not { } source ||
            GetWidgetId(source) is not { } sourceId || FindWidget(sourceId) is not { IsLocked: false, IsVisible: true })
            return;
        _widgetDrag = new(args.Pointer, sourceId, source, handle, args.GetPosition(this));
        handle.PointerCaptureLost += OnWidgetDragHandleCaptureLost;
    }

    private void OnWidgetPointerMoved(object? sender, PointerEventArgs args)
    {
        if (_widgetDrag is not { } drag || drag.Pointer != args.Pointer) return;
        if (_section != "newtab" || _widgetBusy || !CanEditWidgetLayout) { EndWidgetDrag(); return; }
        if (!drag.IsDragging)
        {
            var offset = args.GetPosition(this) - drag.Start;
            if (Math.Abs(offset.X) < 6 && Math.Abs(offset.Y) < 6) return;
            _widgetDrag = drag = drag with { IsDragging = true };
            args.Pointer.Capture(_newTabWidgetMount);
            CuiPropertyPresentation.Set(drag.Source, "Opacity", .66, CuiPropertyLevel.Instance, WidgetDragPresentation, order: 3);
        }
        var target = FindWidgetDropTarget(drag, args.GetPosition(this));
        if (drag.Target != target)
        {
            ClearWidgetDropOutline(drag.Target);
            if (target is not null)
                CuiPropertyPresentation.Set(target, "Opacity", .78, CuiPropertyLevel.Instance, WidgetDragPresentation, order: 3);
            _widgetDrag = drag with { Target = target };
        }
        args.Handled = true;
    }

    private void OnWidgetPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_widgetDrag is not { } drag || drag.Pointer != args.Pointer) return;
        var target = drag.IsDragging ? FindWidgetDropTarget(drag, args.GetPosition(this)) : null;
        EndWidgetDrag();
        if (!drag.IsDragging) return;
        args.Handled = true;
        if (IsAlive && _section == "newtab" && CanEditWidgetLayout && target is not null && GetWidgetId(target) is { } targetId)
            _widgetDragCommit = DispatchAsync(widgetActionPrefix + "drag-reorder", new WidgetReorder(drag.SourceId, targetId)).AsTask();
    }

    private void OnWidgetPointerCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (_widgetDrag is { } drag && drag.Pointer == args.Pointer && ReferenceEquals(args.Source, _newTabWidgetMount))
            EndWidgetDrag();
    }

    private void OnWidgetDragHandleCaptureLost(object? sender, PointerCaptureLostEventArgs args)
    {
        if (_widgetDrag is { IsDragging: false } drag && drag.Pointer == args.Pointer)
            EndWidgetDrag();
    }

    private Control? FindWidgetDropTarget(WidgetDrag drag, Point point) =>
        !new Rect(Bounds.Size).Contains(point) ? null :
        _newTabWidgetMount?.GetVisualDescendants().OfType<Control>().FirstOrDefault(card =>
            card.Name == "newtab-widget-card" && card.IsEffectivelyVisible && card != drag.Source &&
            card.TranslatePoint(default, this) is { } origin && new Rect(origin, card.Bounds.Size).Contains(point) &&
            GetWidgetId(card) is { } targetId && CanReorderWidget(_widgetLayout.Widgets, drag.SourceId, targetId));

    private static string? GetWidgetId(Control card) =>
        card.DataContext is ICuiBindingContext binding && binding.TryGetValue("card.Id", out var id) ? id as string : null;

    private void EndWidgetDrag()
    {
        if (_widgetDrag is not { } drag) return;
        _widgetDrag = null;
        drag.Handle.PointerCaptureLost -= OnWidgetDragHandleCaptureLost;
        ClearWidgetDropOutline(drag.Source);
        ClearWidgetDropOutline(drag.Target);
        if (ReferenceEquals(drag.Pointer.Captured, _newTabWidgetMount)) drag.Pointer.Capture(null);
    }

    private static void ClearWidgetDropOutline(Control? card)
    {
        if (card is not null)
            CuiPropertyPresentation.Remove(card, "Opacity", CuiPropertyLevel.Instance, WidgetDragPresentation);
    }

    private static bool CanReorderWidget(ImmutableArray<NewTabWidgetPlacement> placements, string sourceId, string targetId)
    {
        var origin = FindWidgetIndex(placements, sourceId);
        var target = FindWidgetIndex(placements, targetId);
        if (origin < 0 || target < 0 || origin == target || placements[origin] is not { IsVisible: true, IsLocked: false } source ||
            placements[target] is not { IsVisible: true, IsLocked: false } destination || destination.IsPinned != source.IsPinned)
            return false;
        var first = Math.Min(origin, target);
        var count = Math.Abs(target - origin);
        return !placements.Skip(first).Take(count).Any(widget => widget.IsVisible && widget.IsLocked && widget.IsPinned == source.IsPinned);
    }

    private static ImmutableArray<NewTabWidgetPlacement> ReorderWidget(ImmutableArray<NewTabWidgetPlacement> placements, WidgetReorder drag)
    {
        if (!CanReorderWidget(placements, drag.SourceId, drag.TargetId)) return placements;
        var origin = FindWidgetIndex(placements, drag.SourceId);
        var target = FindWidgetIndex(placements, drag.TargetId);
        return placements.RemoveAt(origin).Insert(target, placements[origin]);
    }

    partial void ApplyNewTabWidgets(NewTabLayoutPreference layout)
    {
        if (_newTabWidgetSources is null) return;
        if (_widgetDrag is not null && !_widgetLayout.Widgets.SequenceEqual(layout.Widgets)) EndWidgetDrag();
        _widgetLayout = layout;
        foreach (var key in _widgetContent.Keys.Where(key => !layout.Widgets.Any(widget => widget.Id == key.Id && widget.SourceId == key.Source)).ToArray())
            _widgetContent.Remove(key);
        var timer = layout.Widgets.FirstOrDefault(widget => widget.Id == "focus-timer");
        var duration = TimerDuration(timer?.SourceId);
        if (timer is null || duration != _focusTimerDuration)
        {
            _focusTimerDuration = duration;
            ResetTimer();
        }
        PublishNewTabWidgets();
    }

    private bool IsNewTabWidgetActionAvailable(string command) =>
        _section == "newtab" && _newTabWidgetSources is not null && command.StartsWith(widgetActionPrefix, StringComparison.Ordinal);

    private bool CanEditWidgetLayout => _widgetLayoutWritable && CanChangeNewTabLayout;

    private async Task RefreshNewTabWidgetsAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token,
        string? widgetId = null)
    {
        if (_newTabWidgetSources is null || _section != "newtab") return;
        foreach (var placement in _widgetLayout.Widgets.Where(placement => placement.IsVisible && placement.Id != "focus-timer" &&
            (widgetId is null || placement.Id == widgetId)).ToArray())
        {
            if (widgetId is null && _widgetContent.TryGetValue((placement.Id, placement.SourceId), out var cached) &&
                DateTimeOffset.UtcNow - cached.Refreshed < TimeSpan.FromMinutes(2)) continue;
            var content = await original.AwaitAsync(_newTabWidgetSources.ReadAsync(placement, token));
            if (!original.IsAcceptedPublicationCurrent || _section != "newtab") return;
            if (!_widgetLayout.Widgets.Any(current => current.Id == placement.Id && current.SourceId == placement.SourceId)) continue;
            _widgetContent[(placement.Id, placement.SourceId)] = (content, DateTimeOffset.UtcNow);
        }
        PublishNewTabWidgets();
    }

    private async Task DispatchNewTabWidgetAsync(string command, object? parameter, DesktopOriginalWorkLifetime.Original original,
        CancellationToken token)
    {
        if (command == widgetActionPrefix + "close")
        {
            CloseWidgetEditor();
            return;
        }
        if (_widgetBusy) return;

        var action = command[widgetActionPrefix.Length..];
        var key = parameter as string;
        // Do not let a stale rendered control issue writes in a different view generation.
        var generation = _selection;
        original.BindPublicationGuard(() => IsAlive && _section == "newtab" && _selection == generation);
        // Adding makes the focused card temporarily unavailable, then permanently checked.
        // Keep the modal's keyboard focus on its search field before disabling the action rows.
        if (action == "pick" && _widgetPickerOpen) FocusWidgetControlNow("newtab-widget-search");
        _widgetBusy = true;
        PublishNewTabWidgets();
        try
        {
            switch (action)
            {
                case "picker" when CanEditWidgetLayout && !_widgetPickerOpen:
                    _widgetOptionsId = null;
                    _widgetPickerTrigger = key == "header" && IsNewTabCustomising
                        ? "home-newtab-customise-add-widget" : "newtab-widget-picker-open";
                    _widgetBindings.Set("WidgetQuery", string.Empty);
                    OpenWidgetPicker();
                    return;
                case "options" when CanEditWidgetLayout && FindWidget(key) is { IsVisible: true } selected:
                    HideWidgetPicker();
                    _widgetOptionsId = selected.Id;
                    _widgetChoices = [];
                    _widgetSourceNotice = string.Empty;
                    try
                    {
                        var choices = await original.AwaitAsync(_newTabWidgetSources!.ChoicesAsync(selected.Id, token));
                        if (original.IsAcceptedPublicationCurrent && _widgetOptionsId == selected.Id)
                        {
                            _widgetChoices = choices;
                            if (choices.Count == 0) _widgetSourceNotice = "No source is available for this widget.";
                            FocusWidgetControl("newtab-widget-options-close");
                        }
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception error)
                    {
                        Trace.TraceError("New Tab widget source choices failed: {0}", error);
                        if (_widgetOptionsId == selected.Id) _widgetSourceNotice = "Sources could not be read. Open Options to retry.";
                    }
                    return;
                case "open" when key is not null && _widgetItems.TryGetValue(key, out var link) && link.Item.Address is { } target:
                    var content = await original.AwaitAsync(_newTabWidgetSources!.ReadAsync(link.Placement, token));
                    if (original.IsAcceptedPublicationCurrent && FindWidget(link.Placement.Id) == link.Placement &&
                        content.Items.Any(item => item.Address == target))
                        Queue(target, original, newTab: true);
                    else _widgetNotice = "That item is no longer listed by its source. Refresh the widget to open a current item.";
                    return;
                case "complete" when key is not null && _widgetItems.TryGetValue(key, out var task) && task.Item.TaskId is { } taskId &&
                    FindWidget(task.Placement.Id) == task.Placement:
                    _widgetNotice = await original.AwaitAsync(_newTabWidgetSources!.CompleteTaskAsync(task.Placement.SourceId, taskId, token))
                        ?? "Planner completed the task.";
                    if (original.IsAcceptedPublicationCurrent) await RefreshNewTabWidgetsAsync(original, token, task.Placement.Id);
                    return;
                case "drag-reorder" when CanEditWidgetLayout && parameter is WidgetReorder drag:
                    await ChangeNewTabLayoutAsync(layout => layout with { Widgets = ReorderWidget(layout.Widgets, drag) }, original, token);
                    if (original.IsAcceptedPublicationCurrent)
                    {
                        var title = NewTabWidgetSources.Catalogue.FirstOrDefault(item => item.Id == drag.SourceId)?.Title ?? "Unavailable widget";
                        FocusWidgetControl("newtab-widget-drag", "Reorder " + title);
                    }
                    return;
                case "retry" when FindWidget(key) is { IsVisible: true } retry:
                    await RefreshNewTabWidgetsAsync(original, token, retry.Id);
                    return;
                case "timer-toggle" or "timer-reset" or "timer-reset-confirm" or "timer-reset-cancel"
                    when FindWidget("focus-timer") is { IsVisible: true }:
                    ChangeTimer(action);
                    return;
            }

            if (!CanEditWidgetLayout) return;
            if (action == "pick")
            {
                if (!_widgetPickerOpen || NewTabWidgetSources.Catalogue.FirstOrDefault(entry => entry.Id == key) is not { } definition)
                    return;
                var placement = FindWidget(definition.Id);
                if (placement is { IsVisible: true }) return;
                await ChangeNewTabLayoutAsync(layout => layout with
                {
                    Widgets = EditWidgetVisibility(layout.Widgets, definition, placement is null ? "add" : "show")
                }, original, token);
                if (original.IsAcceptedPublicationCurrent)
                    await RefreshNewTabWidgetsAsync(original, token, definition.Id);
                return;
            }

            if (FindWidget(_widgetOptionsId) is not { } edited) return;
            var source = action == "source" ? _widgetChoices.FirstOrDefault(choice => choice.Id == key) : null;
            if (action == "source" && source is null) return;
            await ChangeNewTabLayoutAsync(layout => layout with
            {
                Widgets = EditWidgetOptions(layout.Widgets, edited.Id, action, source?.Id)
            }, original, token);
            if (!original.IsAcceptedPublicationCurrent) return;
            if (action == "source" && FindWidget(edited.Id)?.SourceId == source?.Id)
                await RefreshNewTabWidgetsAsync(original, token, edited.Id);
            if ((action is "hide" or "remove") && FindWidget(edited.Id) is not { IsVisible: true })
                CloseWidgetEditor();
        }
        finally
        {
            _widgetBusy = false;
            if (IsAlive) PublishNewTabWidgets();
        }
    }

    private static ImmutableArray<NewTabWidgetPlacement> EditWidgetVisibility(ImmutableArray<NewTabWidgetPlacement> placements,
        NewTabWidgetDefinition definition, string action)
    {
        var index = FindWidgetIndex(placements, definition.Id);
        if (action == "add" && index < 0) return placements.Add(new(definition.Id, definition.InitialSource));
        return action == "show" && index >= 0 ? placements.SetItem(index, placements[index] with { IsVisible = true }) : placements;
    }

    private static ImmutableArray<NewTabWidgetPlacement> EditWidgetOptions(ImmutableArray<NewTabWidgetPlacement> placements,
        string id, string action, string? source)
    {
        var index = FindWidgetIndex(placements, id);
        if (index < 0) return placements;
        var placement = placements[index];
        var updated = action switch
        {
            "width" when !placement.IsLocked => placement with { IsWide = !placement.IsWide },
            "pin" when !placement.IsLocked => placement with { IsPinned = !placement.IsPinned },
            "lock" => placement with { IsLocked = !placement.IsLocked },
            "hide" => placement with { IsVisible = false },
            "source" when source is not null => placement with { SourceId = source },
            _ => placement
        };
        if (action == "remove") return placements.RemoveAt(index);
        if ((action is "move-up" or "move-down") && !placement.IsLocked)
        {
            var neighbor = FindNeighbor(placements, index, action == "move-up" ? -1 : 1);
            if (neighbor >= 0) return placements.SetItem(index, placements[neighbor]).SetItem(neighbor, placement);
        }
        return placements.SetItem(index, updated);
    }

    private static int FindWidgetIndex(ImmutableArray<NewTabWidgetPlacement> placements, string id)
    {
        for (var index = 0; index < placements.Length; index++)
            if (placements[index].Id == id) return index;
        return -1;
    }

    private static int FindNeighbor(ImmutableArray<NewTabWidgetPlacement> placements, int index, int direction)
    {
        for (var candidate = index + direction; candidate >= 0 && candidate < placements.Length; candidate += direction)
            if (placements[candidate].IsVisible && placements[candidate].IsPinned == placements[index].IsPinned)
                return placements[candidate].IsLocked ? -1 : candidate;
        return -1;
    }

    private NewTabWidgetPlacement? FindWidget(string? id) => _widgetLayout.Widgets.FirstOrDefault(placement => placement.Id == id);

    private void CloseWidgetEditor()
    {
        var selected = FindWidget(_widgetOptionsId);
        var returnTrigger = _widgetPickerOpen ? _widgetPickerTrigger : "newtab-widget-picker-open";
        _widgetOptionsId = null;
        HideWidgetPicker();
        _widgetChoices = [];
        _widgetSourceNotice = string.Empty;
        PublishNewTabWidgets();
        if (selected is { IsVisible: true })
        {
            var title = NewTabWidgetSources.Catalogue.FirstOrDefault(item => item.Id == selected.Id)?.Title ?? "Unavailable widget";
            FocusWidgetControl("newtab-widget-options", "Options for " + title);
        }
        else FocusWidgetControl(returnTrigger);
    }

    private void FocusWidgetControl(string id, string? name = null) => Dispatcher.UIThread.Post(
        () => FocusWidgetControlNow(id, name), DispatcherPriority.Loaded);

    private void FocusWidgetControlNow(string id, string? name = null)
    {
        if (!IsAlive || _section != "newtab") return;
        Control? scope = _widgetPickerOpen ? _widgetPickerBackdrop : this;
        var target = scope?.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
            control.IsEffectivelyVisible && control.Name == id && (name is null || AutomationProperties.GetName(control) == name));
        target?.Focus(NavigationMethod.Tab);
    }

    private void PublishNewTabWidgets()
    {
        if (_newTabWidgetMount is { } mount)
            Set(mount, "IsVisible", _widgetLayout.Preset != NewTabLayoutPreset.Focus || IsNewTabCustomising);
        var query = _widgetBindings.TryGetValue("WidgetQuery", out var currentQuery) ? currentQuery as string : null;
        var filter = query?.Trim() ?? string.Empty;
        var canEdit = CanEditWidgetLayout && !_widgetBusy;
        var pickerColumns = Bounds.Width >= 760 || Bounds.Width <= 0 ? 3 : Bounds.Width >= 475 ? 2 : 1;
        var catalogue = NewTabWidgetSources.Catalogue.Select(entry => (Entry: entry, Preview: WidgetPreviewFor(entry.Id))).Where(item =>
            item.Entry.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
            item.Entry.Source.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
            item.Preview.Text.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .Select((item, index) => new WidgetCatalogueRow(item.Entry.Id, item.Entry.Title, item.Entry.Source, item.Preview.Text,
                item.Preview.Icon, item.Preview.Large ? 23d : 14d, index / pickerColumns, index % pickerColumns,
                FindWidget(item.Entry.Id), canEdit)).ToArray();
        _widgetBindings.Set("WidgetCatalogue", catalogue);
        _widgetBindings.Set("WidgetCatalogueEmpty", catalogue.Length == 0);
        _widgetBindings.Set("WidgetPickerColumns", string.Join(" ", Enumerable.Repeat("1fr", pickerColumns)));
        _widgetBindings.Set("WidgetPickerRows", string.Join(" ", Enumerable.Repeat("Auto", Math.Max(1, (catalogue.Length + pickerColumns - 1) / pickerColumns))));
        _widgetBindings.Set("WidgetPickerPadding", Bounds.Width >= 760 ? new Thickness(28, 25) : new Thickness(16));
        _widgetBindings.Set("WidgetCanEdit", canEdit);
        _widgetBindings.Set("WidgetCanRead", !_widgetBusy);
        _widgetBindings.Set("WidgetNotice", _widgetNotice);
        _widgetBindings.Set("WidgetHasNotice", _widgetNotice.Length > 0);

        var width = _newTabWidgetMount?.Bounds.Width ?? 0;
        var columns = width >= 820 && _widgetLayout.Preset == NewTabLayoutPreset.MoreWidgets ? 3 : width >= 560 ? 2 : 1;
        var cards = new List<WidgetCard>();
        _widgetItems.Clear();
        var row = 0;
        var column = 0;
        foreach (var placement in _widgetLayout.Widgets.Where(placement => placement.IsVisible).OrderByDescending(placement => placement.IsPinned))
        {
            var span = placement.IsWide ? columns : 1;
            if (column + span > columns) { row++; column = 0; }
            var definition = NewTabWidgetSources.Catalogue.FirstOrDefault(item => item.Id == placement.Id);
            var isTimer = placement.Id == "focus-timer";
            var status = isTimer ? string.Empty : definition is null ? "The saved widget type is unavailable." : "Reading the source…";
            var items = new List<WidgetItemRow>();
            if (_widgetContent.TryGetValue((placement.Id, placement.SourceId), out var cached))
            {
                status = cached.Content.Status;
                if (cached.Content.Items.Count > 0 && DateTimeOffset.UtcNow - cached.Refreshed >= TimeSpan.FromMinutes(10))
                    status = "Last refreshed " + cached.Refreshed.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) + ". " + status;
                foreach (var item in cached.Content.Items)
                {
                    var key = placement.Id + ":" + (item.Address?.Value ?? item.TaskId?.ToString("D") ?? items.Count.ToString(CultureInfo.InvariantCulture));
                    if (_widgetItems.TryAdd(key, (placement, item))) items.Add(new(key, item));
                }
            }
            var source = placement is { Id: "continue-working", SourceId: NewTabWidgetSources.SpacesRecentSource }
                ? "Spaces" : definition?.Source ?? "Saved layout";
            cards.Add(new(placement.Id, definition?.Title ?? "Unavailable widget", source,
                row, column, span, status, isTimer, definition is not null && !isTimer, canEdit && !placement.IsLocked, items));
            column += span;
            if (column >= columns) { row++; column = 0; }
        }
        _widgetBindings.Set("WidgetGridColumns", string.Join(" ", Enumerable.Repeat("1fr", columns)));
        _widgetBindings.Set("WidgetGridRows", string.Join(" ", Enumerable.Repeat("Auto", Math.Max(1, row + (column > 0 ? 1 : 0)))));
        _widgetBindings.Set("WidgetCards", cards.ToArray());

        var options = FindWidget(_widgetOptionsId);
        var optionsIndex = options is null ? -1 : FindWidgetIndex(_widgetLayout.Widgets, options.Id);
        var canReposition = canEdit && options is { IsLocked: false };
        _widgetBindings.Set("WidgetOptionsOpen", options is not null);
        _widgetBindings.Set("WidgetOptionsTitle", options is null ? string.Empty :
            (NewTabWidgetSources.Catalogue.FirstOrDefault(item => item.Id == options.Id)?.Title ?? "Unavailable widget") + " options");
        _widgetBindings.Set("WidgetCanMoveUp", canReposition && FindNeighbor(_widgetLayout.Widgets, optionsIndex, -1) >= 0);
        _widgetBindings.Set("WidgetCanMoveDown", canReposition && FindNeighbor(_widgetLayout.Widgets, optionsIndex, 1) >= 0);
        _widgetBindings.Set("WidgetCanReposition", canReposition);
        _widgetBindings.Set("WidgetWidthLabel", options?.IsWide == true ? "Make compact" : "Make wide");
        _widgetBindings.Set("WidgetPinLabel", options?.IsPinned == true ? "Unpin layout position" : "Pin layout position");
        _widgetBindings.Set("WidgetLockLabel", options?.IsLocked == true ? "Unlock layout" : "Lock layout");
        _widgetBindings.Set("WidgetSourceNotice", _widgetSourceNotice);
        _widgetBindings.Set("WidgetSourceChoices", _widgetChoices.Select(choice =>
            new WidgetSourceRow(choice.Id, choice.Label, choice.Id == options?.SourceId)).ToArray());
        PublishTimer();
    }

    private static (string Text, string Icon, bool Large) WidgetPreviewFor(string id) => id switch
    {
        "up-next" => ("Upcoming events", "icon:calendar", false),
        "pinned-note" => ("Notebook", "icon:notes", false),
        "focus-timer" => ("25:00", "icon:history", true),
        "continue-working" => ("Recent work", "icon:folder", false),
        "my-tasks" => ("Task list", "icon:tasks", false),
        "study-progress" => ("Assignment progress", "icon:study", false),
        _ => ("Widget", "icon:apps", false)
    };

    private sealed record WidgetCatalogueRow(string Id, string Title, string Source, string Preview, string Icon, double PreviewFontSize,
        int Row, int Column, NewTabWidgetPlacement? Placement, bool Editable) : ICuiBindingContext
    {
        public bool CanActivate => Editable && Placement is not { IsVisible: true };
        public string ActionName => Placement switch
        {
            null => "Add " + Title,
            { IsVisible: false } => "Show " + Title,
            _ => Title + ", already shown"
        };

        public string StateIcon => Placement is { IsVisible: true } ? "icon:check" : "icon:plus";

        public bool TryGetValue(string path, out object? value) => (value = path switch
        {
            nameof(Id) => Id,
            nameof(Title) => Title,
            nameof(Source) => Source,
            nameof(Preview) => Preview,
            nameof(Icon) => Icon,
            nameof(PreviewFontSize) => PreviewFontSize,
            nameof(Row) => Row,
            nameof(Column) => Column,
            nameof(CanActivate) => CanActivate,
            nameof(ActionName) => ActionName,
            nameof(StateIcon) => StateIcon,
            _ => null
        }) is not null;
    }

    private sealed record WidgetCard(string Id, string Title, string Source, int Row, int Column, int Span, string Status,
        bool IsTimer, bool CanRefresh, bool CanDrag, IReadOnlyList<WidgetItemRow> Items) : ICuiBindingContext
    {
        public string OptionsName => "Options for " + Title;
        public string DragName => "Reorder " + Title;
        public bool HasStatus => Status.Length > 0;

        public bool TryGetValue(string path, out object? value) => (value = path switch
        {
            nameof(Id) => Id,
            nameof(Title) => Title,
            nameof(Source) => Source,
            nameof(Row) => Row,
            nameof(Column) => Column,
            nameof(Span) => Span,
            nameof(Status) => Status,
            nameof(IsTimer) => IsTimer,
            nameof(CanRefresh) => CanRefresh,
            nameof(Items) => Items,
            nameof(OptionsName) => OptionsName,
            nameof(DragName) => DragName,
            nameof(CanDrag) => CanDrag,
            nameof(HasStatus) => HasStatus,
            _ => null
        }) is not null;
    }

    private sealed record WidgetItemRow(string Key, NewTabWidgetItem Item) : ICuiBindingContext
    {
        public string Title => Item.Title;
        public string Detail => Item.Detail;
        public bool CanOpen => Item.Address is not null;
        public bool CanComplete => Item.TaskId is not null;
        public bool IsPlain => !CanOpen;
        public string OpenName => "Open " + Item.Title;
        public string CompleteName => "Complete " + Item.Title;

        public bool TryGetValue(string path, out object? value) => (value = path switch
        {
            nameof(Key) => Key,
            nameof(Title) => Title,
            nameof(Detail) => Detail,
            nameof(CanOpen) => CanOpen,
            nameof(CanComplete) => CanComplete,
            nameof(IsPlain) => IsPlain,
            nameof(OpenName) => OpenName,
            nameof(CompleteName) => CompleteName,
            _ => null
        }) is not null;
    }

    private sealed record WidgetSourceRow(string Id, string Label, bool Selected) : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value) => (value = path switch
        {
            nameof(Id) => Id,
            nameof(Label) => Label,
            nameof(Selected) => Selected,
            _ => null
        }) is not null;
    }

    private static TimeSpan TimerDuration(string? source) =>
        source is not null && source.StartsWith("timer:", StringComparison.Ordinal) &&
        int.TryParse(source.AsSpan("timer:".Length), out var minutes) && minutes is >= 1 and <= 120
            ? TimeSpan.FromMinutes(minutes) : TimeSpan.FromMinutes(25);

    private void ChangeTimer(string action)
    {
        if (action == "timer-toggle")
        {
            if (_focusTimerRun is not null) PauseTimer();
            else
            {
                if (_focusTimerRemaining <= TimeSpan.Zero) _focusTimerRemaining = _focusTimerDuration;
                _focusTimerRun = Stopwatch.StartNew();
                _focusTimerTicker ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _focusTimerTicker.Tick -= OnFocusTimerTick;
                _focusTimerTicker.Tick += OnFocusTimerTick;
                _focusTimerTicker.Start();
            }
            _focusTimerResetRequested = false;
        }
        else if (action == "timer-reset" && _focusTimerRun is not null) _focusTimerResetRequested = true;
        else if (action is "timer-reset" or "timer-reset-confirm") ResetTimer();
        else _focusTimerResetRequested = false;
        PublishTimer();
    }

    private void OnFocusTimerTick(object? sender, EventArgs args)
    {
        if (_focusTimerRun is not null && _focusTimerRun.Elapsed >= _focusTimerRemaining) PauseTimer();
        PublishTimer();
    }

    private void PauseTimer()
    {
        if (_focusTimerRun is not null)
            _focusTimerRemaining = TimeSpan.FromTicks(Math.Max(0, (_focusTimerRemaining - _focusTimerRun.Elapsed).Ticks));
        _focusTimerRun = null;
        _focusTimerTicker?.Stop();
    }

    private void ResetTimer()
    {
        PauseTimer();
        _focusTimerRemaining = _focusTimerDuration;
        _focusTimerResetRequested = false;
    }

    private void PublishTimer()
    {
        var remaining = _focusTimerRemaining - (_focusTimerRun?.Elapsed ?? TimeSpan.Zero);
        var seconds = (int)Math.Max(0, Math.Ceiling(remaining.TotalSeconds));
        _widgetBindings.Set("WidgetTimerReadout", $"{seconds / 60:D2}:{seconds % 60:D2}");
        _widgetBindings.Set("WidgetTimerAction", _focusTimerRun is not null ? "Pause" :
            seconds > 0 && _focusTimerRemaining < _focusTimerDuration ? "Resume" : "Start");
        _widgetBindings.Set("WidgetTimerConfirmReset", _focusTimerResetRequested);
    }

    private void StopNewTabWidgets()
    {
        _focusTimerTicker?.Stop();
        if (_focusTimerTicker is not null) _focusTimerTicker.Tick -= OnFocusTimerTick;
        EndWidgetDrag();
        if (_newTabWidgetMount is { } mount)
        {
            mount.SizeChanged -= OnWidgetViewportChanged;
            mount.RemoveHandler(PointerPressedEvent, OnWidgetPointerPressed);
            mount.RemoveHandler(PointerMovedEvent, OnWidgetPointerMoved);
            mount.RemoveHandler(PointerReleasedEvent, OnWidgetPointerReleased);
            mount.PointerCaptureLost -= OnWidgetPointerCaptureLost;
        }
        _widgetBindings.PropertyChanged -= OnWidgetQueryChanged;
        SizeChanged -= OnWidgetPickerPageSizeChanged;
        DetachedFromVisualTree -= OnWidgetPickerDetached;
        RemoveHandler(KeyDownEvent, OnWidgetKeyDown);
        HideWidgetPicker();
        if (_widgetPickerBackdrop is { } backdrop) backdrop.PointerPressed -= OnWidgetPickerBackdropPressed;
        _widgetPickerLoader?.Dispose();
        _widgetLoader?.Dispose();
    }

    private Task WhenWidgetActionsIdleAsync() => Task.WhenAll(
        _widgetLoader?.WhenActionsIdleAsync() ?? Task.CompletedTask,
        _widgetPickerLoader?.WhenActionsIdleAsync() ?? Task.CompletedTask,
        _widgetDragCommit ?? Task.CompletedTask);
}
