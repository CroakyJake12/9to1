using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Registry;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Services;

namespace Haven.Desktop.Controls;

/// <summary>
/// Trusted HavenUI renderer for structured documents. It maps component types
/// to known controls, emits semantic events, and applies store patches without
/// replacing the whole surface when document structure is unchanged.
/// </summary>
public sealed class GenerativeUiSurface : UserControl, IDisposable, IAsyncDisposable
{
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly Dictionary<string, Control> _controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _inputValues = new(StringComparer.Ordinal);
    private readonly TextBlock _activity = new()
    {
        Classes = { "muted" },
        FontSize = 11,
        HorizontalAlignment = HorizontalAlignment.Left
    };
    private GenUiDocument? _document;
    private CancellationTokenSource? _revealCancellation;
    private bool _suppressStoreNotification;
    private bool _disposed;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private readonly Func<bool>? _originalPresentationCurrent;
    private readonly List<CancellationTokenSource> _revealScopes = [];
    private readonly List<Task> _revealOriginals = [];
    private readonly List<GeneratedWhiteboardControl> _acquiredWhiteboards = [];
    private readonly List<(Grid Grid, EventHandler<SizeChangedEventArgs> Handler)> _originalGridCallbacks = [];
    private Task? _pendingRebuild;
    private long _generation;
    public Task? OriginalClose => _originalWork.OriginalClose;

    public GenerativeUiSurface(GenerativeUiEventRouter router, GenUiInstanceStore instances)
        : this(router, instances, null) { }
    internal GenerativeUiSurface(GenerativeUiEventRouter router, GenUiInstanceStore instances, Func<bool>? originalPresentationCurrent)
    {
        _originalPresentationCurrent = originalPresentationCurrent;
        _originalWork = new DesktopOriginalWorkLifetime(StopOriginalChildrenAsync, CleanupOriginalAsync);
        _router = router;
        _instances = instances;
        _instances.DocumentChanged += OnDocumentChanged;
    }

    public event EventHandler<GenUiEvent>? SemanticEventEmitted;
    public event EventHandler<GenUiActionResult>? ActionCompleted;

    public GenUiDocument? Document => _document;

    public void Present(GenUiDocument document) => _originalWork.RunSynchronous(original =>
    {
        BindOriginal(original, ++_generation);
        original.DemandPublication();
        GenerativeUiContractValidator.ValidateAndThrow(document);
        _document = document;
        _suppressStoreNotification = true;
        try
        {
            _instances.Register(document);
        }
        finally
        {
            _suppressStoreNotification = false;
        }
        original.DemandPublication();
        Rebuild(document, progressively: true);
        original.DemandPublication();
    });

    public void PresentExisting(GenUiDocument document) => _originalWork.RunSynchronous(original =>
    {
        BindOriginal(original, ++_generation);
        original.DemandPublication();
        GenerativeUiContractValidator.ValidateAndThrow(document);
        var registered = _instances.TryGet(document.Origin.InstanceId)
            ?? throw new InvalidOperationException("The generated UI instance is no longer registered.");
        if (registered.Origin.ThreadId != document.Origin.ThreadId)
            throw new InvalidOperationException("A generated UI instance cannot move between threads.");
        _document = registered;
        Rebuild(registered, progressively: false);
        original.DemandPublication();
    });

    private void OnDocumentChanged(object? sender, GenUiDocument document)
    {
        if (_disposed || _originalWork.IsRetiring || _suppressStoreNotification || _document?.Origin.InstanceId != document.Origin.InstanceId) return;
        var generation = _generation;
        _ = _originalWork.RunAsync(async original =>
        {
            BindOriginal(original, generation);
            await PublishOriginalAsync(original, () =>
            {
                if (_document is null || _document.Origin.InstanceId != document.Origin.InstanceId) return;
                var compatible = _pendingRebuild is null && StructureKey(_document.Root) == StructureKey(document.Root);
                original.DemandPublication();
                _document = document; // SAME captured store observation before a child close can complete synchronously.
                if (compatible) UpdateTree(document.Root);
                else Rebuild(document, progressively: true);
                original.DemandPublication();
            });
        });
    }

    private void Rebuild(GenUiDocument document, bool progressively)
    {
        if (_pendingRebuild is not null || _controls.Count != 0)
        { QueueRebuildOriginal(document, progressively); return; }
        RebuildOriginalCore(document, progressively);
    }
    private void RebuildOriginalCore(GenUiDocument document, bool progressively)
    {
        var callbackFailures = new List<Exception>();
        RetireOriginalGridCallbacks(callbackFailures);
        Throw(callbackFailures); // Actual subscription cleanup before old-tree destruction.
        DemandOriginalPublication();
        _revealCancellation = null;
        _controls.Clear();
        _inputValues.Clear();
        var root = Build(document.Root);
        var content = new StackPanel
        {
            Spacing = 8,
            Children = { root, _activity }
        };

        // Apply per-surface accent scoping if the document specifies one.
        // This changes accent colors only for this surface, not globally.
        var accentSurface = ResolveAccentSurface(document.AccentKey);
        if (accentSurface.HasValue)
        {
            PublishOriginal(() => Content = new HavenAccentScope
            {
                AccentSurface = accentSurface.Value,
                Content = content
            });
        }
        else
        {
            PublishOriginal(() => Content = content);
        }

        if (progressively && !MotionPreferencesService.Current.ReduceAnimations)
            BeginProgressiveReveal(document.Root);
    }

    private void BeginProgressiveReveal(GenUiComponent root)
    {
        var controls = EnumerateComponents(root)
            .Where(component => IsRevealable(component.ComponentType))
            .Select(component => _controls.GetValueOrDefault(component.ComponentId))
            .OfType<Control>().Distinct().ToArray();
        if (controls.Length == 0) return;
        var generation = _generation;
        _revealOriginals.RemoveAll(static actual => actual.IsCompletedSuccessfully);
        _ = _originalWork.RunAsync(async original =>
        {
            BindOriginal(original, generation);
            original.DemandPublication();
            var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token);
            _revealCancellation = scope;
            _revealScopes.Add(scope);
            try
            {
                foreach (var control in controls) PublishOriginal(() => control.Opacity = 0.08);
                await original.AwaitAsync(RevealProgressivelyAsync(original, controls, scope.Token));
            }
            finally
            {
                // All actual fade/delay/dispatcher tasks are terminal before this
                // scope leaves the live stop cohort. Failed originals remain retained.
                _revealScopes.Remove(scope);
                if (ReferenceEquals(_revealCancellation, scope)) _revealCancellation = null;
                try { scope.Dispose(); } catch (Exception cause) { original.Retain(cause); throw; }
            }
        }, actual => _revealOriginals.Add(actual));
    }
    private async Task RevealProgressivelyAsync(DesktopOriginalWorkLifetime.Original original,
        IReadOnlyList<Control> controls, CancellationToken cancellationToken)
    {
        var fades = new List<Task>();
        var staggerMilliseconds = Math.Clamp(680d / Math.Max(1, controls.Count), 18, 58);
        try
        {
            foreach (var control in controls)
            {
                cancellationToken.ThrowIfCancellationRequested(); original.DemandPublication();
                fades.Add(FadeInAsync(original, control, cancellationToken));
                await original.AwaitAsync(Task.Delay(TimeSpan.FromMilliseconds(staggerMilliseconds), cancellationToken));
            }
        }
        catch (Exception cause) { original.Retain(cause); }
        finally
        {
            // Every actual fade settles independently even when stagger/cancellation fails.
            foreach (var fade in fades)
                try { await original.AwaitAsync(fade); } catch (Exception cause) { original.Capture(fade, cause); }
        }
        original.ThrowRetained();
    }
    private async Task FadeInAsync(DesktopOriginalWorkLifetime.Original original, Control control, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var duration = TimeSpan.FromMilliseconds(170);
        while (!cancellationToken.IsCancellationRequested)
        {
            original.DemandPublication();
            var progress = Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            await PublishOriginalAsync(original, () => PublishOriginal(() => control.Opacity = 0.08 + eased * 0.92));
            if (progress >= 1) break;
            await original.AwaitAsync(Task.Delay(16, cancellationToken));
        }
    }
    private Task AnimateFlashcardAsync(HavenCard card, GenUiComponent component)
    {
        var generation = _generation;
        return _originalWork.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => !_disposed && _generation == generation && IsOriginalControlCurrent(component, card) && (_originalPresentationCurrent?.Invoke() ?? true));
            original.DemandPublication();
            if (MotionPreferencesService.Current.ReduceAnimations)
            { await original.AwaitAsync(EmitAsync(component, GenUiEventType.ActionInvoked, null, card)); return; }
            PublishOriginal(() => card.IsHitTestVisible = false);
            var transform = card.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);
            PublishOriginal(() => card.RenderTransform = transform);
            try
            {
                await original.AwaitAsync(AnimateFlipHalfAsync(original, transform, 1, 0.035));
                await original.AwaitAsync(EmitAsync(component, GenUiEventType.ActionInvoked, null, card));
                await Task.Yield(); // Same original scheduling point, no retirement witness.
                original.DemandPublication();
                await original.AwaitAsync(AnimateFlipHalfAsync(original, transform, 0.035, 1));
            }
            finally
            {
                if (original.IsPublicationCurrent)
                    await PublishOriginalAsync(original, () =>
                    {
                        PublishOriginal(() => transform.ScaleX = 1);
                        PublishOriginal(() => transform.ScaleY = 1);
                        PublishOriginal(() => card.IsHitTestVisible = true);
                    });
            }
        });
    }
    private async Task AnimateFlipHalfAsync(DesktopOriginalWorkLifetime.Original original, ScaleTransform transform, double from, double to)
    {
        var started = Stopwatch.GetTimestamp();
        const double durationMilliseconds = 155;
        while (true)
        {
            original.DemandPublication();
            var progress = Math.Clamp(Stopwatch.GetElapsedTime(started).TotalMilliseconds / durationMilliseconds, 0, 1);
            var eased = progress < 0.5 ? 4 * progress * progress * progress : 1 - Math.Pow(-2 * progress + 2, 3) / 2;
            await PublishOriginalAsync(original, () =>
            {
                PublishOriginal(() => transform.ScaleX = from + (to - from) * eased);
                PublishOriginal(() => transform.ScaleY = 1 - Math.Sin(progress * Math.PI) * 0.055);
            });
            if (progress >= 1) break;
            await original.AwaitAsync(Task.Delay(16, original.Token));
        }
    }

    private static IEnumerable<GenUiComponent> EnumerateComponents(GenUiComponent component)
    {
        yield return component;
        foreach (var child in component.Children)
        foreach (var descendant in EnumerateComponents(child))
            yield return descendant;
    }

    private static bool IsRevealable(string componentType) => componentType is not
        ("HavenWorkspace" or "HavenStack" or "HavenGrid" or "HavenSplitView" or "HavenForm" or "HavenWizard");

    private static HavenSurface? ResolveAccentSurface(string? accentKey)
    {
        if (string.IsNullOrWhiteSpace(accentKey)) return null;
        return accentKey.ToLowerInvariant() switch
        {
            "blue" or "studio" => HavenSurface.Studio,
            "green" or "play" => HavenSurface.Play,
            "orange" or "tasks" => HavenSurface.Tasks,
            "purple" or "imagine" or "violet" => HavenSurface.Imagine,
            "teal" or "data" or "cyan" => HavenSurface.Data,
            "pink" or "rose" => HavenSurface.Imagine,
            "yellow" or "plan" or "gold" => HavenSurface.Plan,
            "automations" or "automation" => HavenSurface.Automations,
            "red" or "danger" => HavenSurface.Tasks,
            "indigo" or "study" => HavenSurface.Study,
            "browse" or "sky" => HavenSurface.Browse,
            "home" => HavenSurface.Home,
            "chat" => HavenSurface.Chat,
            "translate" => HavenSurface.Translate,
            "present" => HavenSurface.Present,
            "vision" => HavenSurface.Vision,
            _ => null
        };
    }

    private Control Build(GenUiComponent component)
    {
        Control control = component.ComponentType switch
        {
            "HavenWorkspace" or "HavenStack" or "HavenForm" or "HavenWizard" => BuildStack(component),
            "HavenToolbar" => BuildToolbar(component),
            "HavenGrid" => BuildGrid(component),
            "HavenSplitView" => BuildSplit(component),
            "HavenCard" => BuildCard(component),
            "HavenText" or "HavenMarkdown" => new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontWeight = GetBool(component, "emphasis") ? FontWeight.ExtraBold : FontWeight.Medium
            },
            "HavenButton" => BuildButton(component),
            "HavenTextInput" => BuildTextInput(component),
            "HavenSelect" => BuildSelect(component),
            "HavenToggle" => BuildToggle(component),
            "HavenSlider" => BuildSlider(component),
            "HavenProgress" => new HavenProgressBar { Minimum = 0, Maximum = 100, MinWidth = 180 },
            "HavenStatus" => new HavenStatusChip(),
            "HavenList" or "HavenTable" => new StackPanel { Spacing = 5 },
            "HavenTabs" => BuildTabs(component),
            "HavenChart" or "HavenGraph" or "HavenCanvas" or "HavenImage" => BuildVisualFoundation(component),
            _ => throw new InvalidOperationException($"Trusted renderer has no component mapping for '{component.ComponentType}'.")
        };
        _controls.Add(component.ComponentId, control);
        AutomationProperties.SetAutomationId(control, component.ComponentId);
        AutomationProperties.SetName(control, GetString(component, "automationName") ?? GetString(component, "label") ?? component.ComponentId);
        UpdateControl(component, control);
        return control;
    }

    private Control BuildStack(GenUiComponent component)
    {
        var stack = new StackPanel { Spacing = GetDouble(component, "spacing", 10) };
        foreach (var child in component.Children) stack.Children.Add(Build(child));
        return stack;
    }

    private Control BuildHorizontal(GenUiComponent component)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = GetDouble(component, "spacing", 8) };
        foreach (var child in component.Children) stack.Children.Add(Build(child));
        return stack;
    }

    private Control BuildToolbar(GenUiComponent component) => new HavenToolbar
    {
        Child = BuildHorizontal(component)
    };

    private Control BuildGrid(GenUiComponent component)
    {
        var requestedColumns = Math.Clamp((int)GetDouble(component, "columns", 2), 1, 6);
        var spacing = Math.Max(0, GetDouble(component, "spacing", 12));
        var itemMinWidth = Math.Max(180, GetDouble(component, "itemMinWidth", 280));
        var responsive = GetBool(component, "responsive");
        var controls = component.Children.Select(Build).ToArray();
        var maxColumns = Math.Max(1, Math.Min(requestedColumns, controls.Length));
        var grid = new Grid
        {
            ColumnSpacing = spacing,
            RowSpacing = spacing,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        void ApplyLayout(double availableWidth, bool requireCapturedGrid)
        {
            void PublishLayout(Action actualWrite)
            {
                DemandOriginalPublication();
                if (requireCapturedGrid && !IsCapturedGridCurrent())
                    throw new OperationCanceledException("The original responsive grid was replaced.");
                actualWrite();
                DemandOriginalPublication();
                if (requireCapturedGrid && !IsCapturedGridCurrent())
                    throw new OperationCanceledException("The original responsive grid was replaced.");
            }
            var columns = maxColumns;
            if (responsive && availableWidth > 0)
            {
                var widthBoundColumns = Math.Max(1, (int)Math.Floor((availableWidth + spacing) / (itemMinWidth + spacing)));
                columns = Math.Min(maxColumns, widthBoundColumns);
            }

            PublishLayout(() => grid.ColumnDefinitions = new ColumnDefinitions(string.Join(',', Enumerable.Repeat("*", columns))));
            var rows = (int)Math.Ceiling(controls.Length / (double)columns);
            PublishLayout(() => grid.RowDefinitions = new RowDefinitions(string.Join(',', Enumerable.Repeat("Auto", Math.Max(1, rows)))));
            for (var index = 0; index < controls.Length; index++)
            {
                PublishLayout(() => Grid.SetColumn(controls[index], index % columns));
                PublishLayout(() => Grid.SetRow(controls[index], index / columns));
            }
        }

        bool IsCapturedGridCurrent() => IsOriginalControlCurrent(component, grid) &&
            component.Children.Select((child, index) => _controls.TryGetValue(child.ComponentId, out var current) &&
                ReferenceEquals(current, controls[index])).All(current => current);
        ApplyLayout(Bounds.Width, requireCapturedGrid: false); // SAME admitted construction, before registration.
        foreach (var child in controls) PublishOriginal(() => grid.Children.Add(child));
        EventHandler<SizeChangedEventArgs> actualHandler = (_, args) =>
        {
            if (_originalWork.IsRetiring || !IsCapturedGridCurrent()) return; // Exact old/retired no-effect event.
            RunOriginalCallback(() => ApplyLayout(args.NewSize.Width, requireCapturedGrid: true), IsCapturedGridCurrent);
        };
        _originalGridCallbacks.Add((grid, actualHandler)); // Before installing the actual producer.
        PublishOriginal(() => grid.SizeChanged += actualHandler);
        return grid;
    }

    private Control BuildSplit(GenUiComponent component)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 12 };
        for (var index = 0; index < Math.Min(2, component.Children.Count); index++)
        {
            var child = Build(component.Children[index]);
            Grid.SetColumn(child, index);
            grid.Children.Add(child);
        }
        return grid;
    }

    private Control BuildCard(GenUiComponent component)
    {
        var stack = new StackPanel
        {
            Spacing = GetDouble(component, "spacing", 8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var child in component.Children) stack.Children.Add(Build(child));
        var isFlashcard = string.Equals(GetString(component, "variant"), "flashcard", StringComparison.OrdinalIgnoreCase);
        var card = new HavenCard
        {
            Padding = isFlashcard ? new Thickness(28) : new Thickness(14),
            Child = stack,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        if (isFlashcard)
        {
            card.Background = ResourceBrush("HavenAccentBrush", Color.FromRgb(47, 56, 255));
            card.Cursor = new Cursor(StandardCursorType.Hand);
            card.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        }
        if (component.Actions.Count > 0)
        {
            card.PointerPressed += async (_, args) =>
            {
                if (!args.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
                await RunOriginalInputAsync(component, card, async original =>
                {
                    PublishOriginal(() => args.Handled = true);
                    if (isFlashcard) await original.AwaitAsync(AnimateFlashcardAsync(card, component));
                    else await original.AwaitAsync(EmitAsync(component, GenUiEventType.ActionInvoked, null, card));
                });
            };
        }
        return card;
    }

    private Button BuildButton(GenUiComponent component)
    {
        var button = GetString(component, "kind")?.ToLowerInvariant() switch
        {
            "primary" => (HavenButtonBase)new HavenPrimaryButton(),
            "tertiary" => new HavenTertiaryButton(),
            "negative" or "destructive" => new HavenNegativeButton(),
            "text" or "ghost" => new HavenTextButton(),
            _ => new HavenSecondaryButton()
        };
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.Click += async (_, _) => await EmitAsync(component, GenUiEventType.ActionInvoked, GetValue(component, "value"), button);
        return button;
    }

    private TextBox BuildTextInput(GenUiComponent component)
    {
        HavenTextInput input = GetBool(component, "multiline")
            ? new HavenMultilineInput()
            : new HavenTextInput();
        input.TextWrapping = TextWrapping.Wrap;
        input.TextChanged += (_, _) => RunOriginalCallback(() => _inputValues[component.ComponentId] = input.Text ?? string.Empty, () => IsOriginalControlCurrent(component, input));
        input.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter || args.KeyModifiers.HasFlag(KeyModifiers.Shift) || component.Actions.Count == 0) return;
            await RunOriginalInputAsync(component, input, async original =>
            {
                PublishOriginal(() => args.Handled = true);
                await original.AwaitAsync(EmitAsync(component, GenUiEventType.TextSubmitted,
                    JsonSerializer.SerializeToElement(input.Text ?? string.Empty), input));
            });
        };
        return input;
    }

    private HavenSelect BuildSelect(GenUiComponent component)
    {
        var select = new HavenSelect();
        select.SelectionChanged += async (_, _) =>
        {
            await RunOriginalInputAsync(component, select, async original =>
            {
                PublishOriginal(() => _inputValues[component.ComponentId] = select.SelectedItem);
                if (component.Actions.Count > 0 && select.IsAttachedToVisualTree())
                    await original.AwaitAsync(EmitAsync(component, GenUiEventType.OptionSelected,
                        JsonSerializer.SerializeToElement(select.SelectedItem), select));
            });
        };
        return select;
    }

    private ToggleSwitch BuildToggle(GenUiComponent component)
    {
        var toggle = new HavenSwitch();
        toggle.IsCheckedChanged += async (_, _) =>
        {
            await RunOriginalInputAsync(component, toggle, async original =>
            {
                PublishOriginal(() => _inputValues[component.ComponentId] = toggle.IsChecked);
                if (component.Actions.Count > 0)
                    await original.AwaitAsync(EmitAsync(component, GenUiEventType.ToggleChanged,
                        JsonSerializer.SerializeToElement(toggle.IsChecked), toggle));
            });
        };
        return toggle;
    }

    private Slider BuildSlider(GenUiComponent component)
    {
        var slider = new HavenSlider { MinWidth = 180 };
        slider.ValueChanged += (_, _) => RunOriginalCallback(() => _inputValues[component.ComponentId] = slider.Value, () => IsOriginalControlCurrent(component, slider));
        slider.PointerCaptureLost += async (_, _) =>
        {
            if (component.Actions.Count > 0)
                await EmitAsync(component, GenUiEventType.SliderChanged, JsonSerializer.SerializeToElement(slider.Value), slider);
        };
        return slider;
    }

    private Control BuildTabs(GenUiComponent component)
    {
        var tabs = new HavenTabView();
        tabs.ItemsSource = component.Children.Select(child => new HavenTabItem
        {
            Header = GetString(child, "title") ?? child.ComponentId,
            Content = Build(child)
        }).ToArray();
        return tabs;
    }

    private Control BuildVisualFoundation(GenUiComponent component)
    {
        if (component.ComponentType.Equals("HavenCanvas", StringComparison.Ordinal))
        {
            var stateKey = "canvas." + component.ComponentId;
            JsonElement? persisted = null;
            if (_document?.State.TryGetValue(stateKey, out var existingState) == true)
                persisted = existingState;

            var whiteboard = new GeneratedWhiteboardControl(
                GetString(component, "title") ?? "Whiteboard",
                GetString(component, "prompt") ?? GetString(component, "emptyText") ?? string.Empty,
                GetDouble(component, "minHeight", 420),
                persisted,
                state =>
                {
                    var document = _document;
                    if (document is null) return;
                    _instances.ApplyPatch(new GenUiStatePatch(
                        Guid.NewGuid(),
                        document.Origin.InstanceId,
                        GenUiPatchOperation.Replace,
                        "state",
                        stateKey,
                        state,
                        DateTimeOffset.UtcNow));
                },
                component.Actions.Count == 0
                    ? null
                    : request => EmitAsync(component, GenUiEventType.ActionInvoked, request),
                () => !_disposed && !_originalWork.IsRetiring && _pendingRebuild is null &&
                    (_originalPresentationCurrent?.Invoke() ?? true));
            _acquiredWhiteboards.Add(whiteboard);
            if (_originalWork.IsRetiring) whiteboard.RequestRetirement();
            DemandOriginalPublication();
            return whiteboard;
        }

        return new HavenPanel
        {
            MinHeight = 180,
            CornerRadius = new CornerRadius(16),
            Child = new TextBlock
            {
                Text = GetString(component, "emptyText") ?? $"{component.ComponentType} foundation",
                Classes = { "muted" },
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private void UpdateTree(GenUiComponent component)
    {
        if (_controls.TryGetValue(component.ComponentId, out var control)) UpdateControl(component, control);
        foreach (var child in component.Children) UpdateTree(child);
    }

    private void UpdateControl(GenUiComponent component, Control control)
    {
        switch (control)
        {
            case HavenStatusChip status:
                PublishOriginal(() => status.Content = GetString(component, "text") ?? GetString(component, "label") ?? string.Empty);
                break;
            case ToggleSwitch toggle:
                PublishOriginal(() => toggle.OnContent = GetString(component, "onLabel") ?? "On");
                PublishOriginal(() => toggle.OffContent = GetString(component, "offLabel") ?? "Off");
                PublishOriginal(() => toggle.IsChecked = GetBool(component, "value"));
                break;
            case HavenSelect select:
                var options = GetStringArray(component, "options");
                PublishOriginal(() => select.ItemsSource = options);
                var requested = GetString(component, "value");
                PublishOriginal(() => select.SelectedIndex = requested is null
                    ? (options.Count > 0 ? 0 : -1)
                    : options.ToList().FindIndex(item => item.Equals(requested, StringComparison.Ordinal)));
                _inputValues[component.ComponentId] = select.SelectedItem;
                break;
            case Button button:
                PublishOriginal(() => button.Content = GetString(component, "label") ?? component.ComponentId);
                PublishOriginal(() => button.IsEnabled = !GetBool(component, "disabled"));
                break;
            case TextBox input:
                PublishOriginal(() => input.PlaceholderText = GetString(component, "placeholder") ?? string.Empty);
                var next = GetString(component, "value") ?? string.Empty;
                if (!input.IsFocused && input.Text != next) PublishOriginal(() => input.Text = next);
                _inputValues[component.ComponentId] = input.Text ?? next;
                break;
            case Slider slider:
                PublishOriginal(() => slider.Minimum = GetDouble(component, "minimum", 0));
                PublishOriginal(() => slider.Maximum = GetDouble(component, "maximum", 100));
                PublishOriginal(() => slider.Value = GetDouble(component, "value", slider.Minimum));
                break;
            case ProgressBar progress:
                PublishOriginal(() => progress.Value = GetDouble(component, "value", 0));
                break;
            case TextBlock text:
                PublishOriginal(() => text.Text = GetString(component, "text") ?? GetString(component, "label") ?? string.Empty);
                var fontSize = GetDouble(component, "fontSize", 0);
                if (fontSize > 0) PublishOriginal(() => text.FontSize = fontSize);
                PublishOriginal(() => text.TextAlignment = GetString(component, "textAlignment")?.ToLowerInvariant() switch
                {
                    "center" => TextAlignment.Center,
                    "right" => TextAlignment.Right,
                    _ => TextAlignment.Left
                });
                if (string.Equals(GetString(component, "tone"), "onAccent", StringComparison.OrdinalIgnoreCase))
                    PublishOriginal(() => text.Foreground = Brushes.White);
                PublishOriginal(() => text.Opacity = Math.Clamp(GetDouble(component, "opacity", 1), 0, 1));
                break;
            case StackPanel list when component.ComponentType is "HavenList" or "HavenTable":
                PublishOriginal(list.Children.Clear);
                foreach (var item in GetStringArray(component, "items"))
                    PublishOriginal(() => list.Children.Add(new TextBlock { Text = item, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Medium }));
                break;
        }

        var minWidth = GetDouble(component, "minWidth", 0);
        if (minWidth > 0) PublishOriginal(() => control.MinWidth = minWidth);
        var minHeight = GetDouble(component, "minHeight", 0);
        if (minHeight > 0) PublishOriginal(() => control.MinHeight = minHeight);
        var width = GetDouble(component, "width", 0);
        if (width > 0) PublishOriginal(() => control.Width = width);
        var height = GetDouble(component, "height", 0);
        if (height > 0) PublishOriginal(() => control.Height = height);
        PublishOriginal(() => control.HorizontalAlignment = GetString(component, "horizontalAlignment")?.ToLowerInvariant() switch
        {
            "left" => HorizontalAlignment.Left,
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            "stretch" => HorizontalAlignment.Stretch,
            _ => control.HorizontalAlignment
        });
    }

    private Task EmitAsync(GenUiComponent component, GenUiEventType eventType, JsonElement? value, Control? originalControl = null)
    {
        var generation = _generation;
        var document = _document;
        return _originalWork.RunAsync(async original =>
        {
            BindOriginal(original, generation);
            if (originalControl is not null)
                original.BindPublicationGuard(() => !_disposed && _generation == generation &&
                    IsOriginalControlCurrent(component, originalControl) && (_originalPresentationCurrent?.Invoke() ?? true));
            original.DemandPublication();
            if (document is null || _pendingRebuild is not null) return;
            var binding = GenerativeUiContractValidator.SelectActionBinding(component);
            if (binding is null) return;
            CaptureCurrentInputValues();
            var payload = JsonSerializer.SerializeToElement(new { values = _inputValues, component = component.ComponentId });
            var semanticEvent = new GenUiEvent(Guid.NewGuid(), eventType, DateTimeOffset.UtcNow, document.Origin,
                component.ComponentId, binding.ActionId, null, null, value,
                payload, GenUiEventSource.User, $"User interacted with {component.ComponentId}.");
            original.DemandPublication(); SemanticEventEmitted?.Invoke(this, semanticEvent); original.DemandPublication();
            PublishOriginal(() => _activity.Text = binding.Route == GenUiRouteKind.Local ? "Updating…" : "Haven is working…");
            Task<GenUiActionResult>? actualRoute = null;
            try
            {
                original.DemandPublication();
                actualRoute = _router.RouteAsync(semanticEvent, binding, original.Token);
                var result = await original.AwaitAsync(actualRoute);
                await PublishOriginalAsync(original, () =>
                {
                    PublishOriginal(() => _activity.Text = result.Summary);
                    original.DemandPublication(); ActionCompleted?.Invoke(this, result); original.DemandPublication();
                });
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException)
            {
                original.Capture(actualRoute, exception);
                if (original.IsPublicationCurrent)
                    await PublishOriginalAsync(original, () => PublishOriginal(() => _activity.Text = "The generated action failed: " + exception.Message));
            }
        });
    }

    private bool IsOriginalControlCurrent(GenUiComponent component, Control control) =>
        _controls.TryGetValue(component.ComponentId, out var current) && ReferenceEquals(current, control);
    private Task RunOriginalInputAsync(GenUiComponent component, Control control,
        Func<DesktopOriginalWorkLifetime.Original, Task> callback)
    {
        var generation = _generation;
        return _originalWork.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => !_disposed && _generation == generation &&
                IsOriginalControlCurrent(component, control) && (_originalPresentationCurrent?.Invoke() ?? true));
            original.DemandPublication();
            await original.AwaitAsync(callback(original));
        });
    }
    private void CaptureCurrentInputValues()
    {
        foreach (var (componentId, control) in _controls)
        {
            switch (control)
            {
                case TextBox input:
                    _inputValues[componentId] = input.Text ?? string.Empty;
                    break;
                case ComboBox select:
                    _inputValues[componentId] = select.SelectedItem;
                    break;
                case ToggleSwitch toggle:
                    _inputValues[componentId] = toggle.IsChecked;
                    break;
                case Slider slider:
                    _inputValues[componentId] = slider.Value;
                    break;
            }
        }
    }

    private static string StructureKey(GenUiComponent component) =>
        component.ComponentId + ":" + component.ComponentType + "[" + string.Join(',', component.Children.Select(StructureKey)) + "]";

    private static JsonElement? GetValue(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) ? value : null;

    private static string? GetString(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool GetBool(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True;

    private static double GetDouble(GenUiComponent component, string key, double fallback) =>
        component.Properties.TryGetValue(key, out var value) && value.TryGetDouble(out var result) ? result : fallback;

    private static IReadOnlyList<string> GetStringArray(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.ToString()).ToArray()
            : [];

    private static IBrush ResourceBrush(string key, Color fallback) =>
        Avalonia.Application.Current?.TryFindResource(key, out var value) == true && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    private void BindOriginal(DesktopOriginalWorkLifetime.Original original, long generation) =>
        original.BindPublicationGuard(() => !_disposed && _generation == generation && (_originalPresentationCurrent?.Invoke() ?? true));
    private void DemandOriginalPublication()
    { if (_originalWork.Executing is { } original) original.DemandPublication(); else _originalWork.DemandAdmission(); }
    private void PublishOriginal(Action actualWrite)
    { DemandOriginalPublication(); actualWrite(); DemandOriginalPublication(); }
    private async Task PublishOriginalAsync(DesktopOriginalWorkLifetime.Original original, Action callback)
    {
        var actualDispatcher = Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunSynchronous(callbackOriginal =>
        {
            callbackOriginal.BindPublicationGuard(() => original.IsPublicationCurrent);
            callbackOriginal.DemandPublication(); original.DemandPublication(); callback();
            original.DemandPublication(); callbackOriginal.DemandPublication();
        })).GetTask();
        await original.AwaitAsync(actualDispatcher);
    }
    private void RunOriginalCallback(Action callback, Func<bool>? originalControlCurrent = null)
    {
        if (_originalWork.IsRetiring) return;
        if (_originalWork.Executing is { } original)
        {
            original.DemandPublication();
            if (originalControlCurrent?.Invoke() == false) return;
            original.DemandPublication(); callback(); original.DemandPublication(); return;
        }
        var generation = _generation;
        _originalWork.RunSynchronous(admitted =>
        {
            admitted.BindPublicationGuard(() => !_disposed && _generation == generation &&
                (originalControlCurrent?.Invoke() ?? true) && (_originalPresentationCurrent?.Invoke() ?? true));
            admitted.DemandPublication(); callback(); admitted.DemandPublication();
        });
    }
    private void QueueRebuildOriginal(GenUiDocument document, bool progressively)
    {
        var generation = _generation; var preceding = _pendingRebuild;
        var children = _acquiredWhiteboards.ToArray(); var reveals = _revealOriginals.ToArray();
        _ = _originalWork.RunAsync(async original =>
        {
            BindOriginal(original, generation);
            var childFailures = new List<Exception>();
            foreach (var child in children)
                try { child.RequestRetirement(); } catch (Exception cause) { Add(childFailures, cause); original.Retain(cause); }
            try
            {
                if (preceding is not null)
                    try { await original.AwaitAsync(preceding); } catch (Exception cause) { original.Capture(preceding, cause); }
                // Earlier reveal work cannot mutate a replacement generation. Its
                // real terminal task is joined; failure evidence is retained independently.
                foreach (var reveal in reveals)
                    try { await original.AwaitAsync(reveal); } catch (Exception cause) { original.Capture(reveal, cause); }
                foreach (var child in children)
                {
                    Task? close = null;
                    try
                    { close = child.OriginalClose ?? throw new InvalidOperationException("The native whiteboard close was not published."); await original.AwaitAsync(close); }
                    catch (Exception cause) { Capture(childFailures, close, cause); original.Capture(close, cause); }
                }
                Throw(childFailures);
                await PublishOriginalAsync(original, () =>
                {
                    if (!ReferenceEquals(_document, document)) return;
                    RebuildOriginalCore(document, progressively);
                    foreach (var child in children) _acquiredWhiteboards.Remove(child);
                    foreach (var reveal in reveals) if (reveal.IsCompletedSuccessfully) _revealOriginals.Remove(reveal);
                });
            }
            finally { _ = Interlocked.CompareExchange(ref _pendingRebuild, null, original.Task); }
        }, actual => _pendingRebuild = actual);
    }
    public void RequestRetirement() => _originalWork.RequestRetirement();
    internal void DemandOriginalExternalClose()
    {
        _originalWork.DemandExternalClose();
        foreach (var child in _acquiredWhiteboards.ToArray()) child.DemandOriginalExternalClose();
    }
    public Task CloseAndDrainAsync()
    { DemandOriginalExternalClose(); return _originalWork.CloseAndDrainAsync(); }
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private void RetireOriginalGridCallbacks(List<Exception> failures)
    {
        foreach (var item in _originalGridCallbacks.ToArray())
        {
            try { item.Grid.SizeChanged -= item.Handler; _originalGridCallbacks.Remove(item); }
            catch (Exception cause) { Add(failures, cause); }
        }
    }
    private Task StopOriginalChildrenAsync() => Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunCloseCallback(() =>
    {
        var failures = new List<Exception>();
        try { _instances.DocumentChanged -= OnDocumentChanged; } catch (Exception cause) { Add(failures, cause); }
        foreach (var scope in _revealScopes.ToArray())
            try { scope.Cancel(); } catch (Exception cause) { Add(failures, cause); }
        foreach (var child in _acquiredWhiteboards.ToArray())
            try { child.RequestRetirement(); } catch (Exception cause) { Add(failures, cause); }
        RetireOriginalGridCallbacks(failures); // Independent stop even when another producer fails.
        Throw(failures);
    })).GetTask();
    private async Task CleanupOriginalAsync()
    {
        var failures = new List<Exception>();
        foreach (var child in _acquiredWhiteboards.ToArray())
        {
            try { child.RequestRetirement(); } catch (Exception cause) { Add(failures, cause); }
            Task? close = null;
            try { close = child.OriginalClose ?? throw new InvalidOperationException("Original native whiteboard close is unavailable."); await close; }
            catch (Exception cause) { Capture(failures, close, cause); }
        }
        if (failures.Count != 0) Throw(failures); // Keep physical controls if any actual child close failed.
        var actualCleanup = Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunCloseCallback(() =>
        {
            _disposed = true;
            RetireOriginalGridCallbacks(failures); // Retained failed subscription attempts remain explicit.
            foreach (var scope in _revealScopes.ToArray())
                try { scope.Dispose(); } catch (Exception cause) { Add(failures, cause); }
            try { Content = null; } catch (Exception cause) { Add(failures, cause); }
            _controls.Clear(); _inputValues.Clear(); _acquiredWhiteboards.Clear(); _revealScopes.Clear(); _revealOriginals.Clear();
            _revealCancellation = null;
        })).GetTask();
        try { await actualCleanup; } catch (Exception cause) { Capture(failures, actualCleanup, cause); }
        Throw(failures);
    }
    private static void Add(List<Exception> failures, Exception cause)
    { if (!failures.Any(error => ReferenceEquals(error, cause))) failures.Add(cause); }
    private static void Capture(List<Exception> failures, Task? actual, Exception cause)
    { if (actual?.Exception is { InnerExceptions.Count: > 0 } group) foreach (var direct in group.InnerExceptions) Add(failures, direct); else Add(failures, cause); }
    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == 0) return;
        if (failures.Count > 1 || failures[0] is OperationCanceledException) throw new AggregateException("Original native generated work or cleanup failed.", failures);
        ExceptionDispatchInfo.Capture(failures[0]).Throw();
    }

}
