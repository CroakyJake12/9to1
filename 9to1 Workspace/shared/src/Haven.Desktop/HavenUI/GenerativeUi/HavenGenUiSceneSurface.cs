using System.Text.Json;
using Haven.Desktop.Services;
using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.HavenUI.GenerativeUi;

/// <summary>
/// Trusted GenUI adapter for the Haven scene tree. It preserves instance identity, updates compatible
/// component trees in place on store patches, and routes semantic actions through the shared router.
/// </summary>
internal sealed class HavenGenUiSceneSurface : IDisposable, IAsyncDisposable
{
    private readonly GenerativeUiEventRouter _router;
    private readonly GenUiInstanceStore _instances;
    private readonly Dictionary<string, HavenElement> _elements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GenUiComponent> _components = new(StringComparer.Ordinal);
    private readonly Dictionary<Input, GenUiComponent> _inputs = [];
    private readonly Dictionary<string, HavenGenUiWhiteboard> _whiteboards = new(StringComparer.Ordinal);
    private readonly HavenText _activity = new();
    private GenUiDocument? _document;
    private string? _structureSignature;
    private bool _disposed;
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private readonly Func<bool>? _originalPresentationCurrent;
    private readonly List<HavenGenUiWhiteboard> _acquiredWhiteboards = [];
    private GenUiDocument? _registeringOriginalDocument;
    private Task? _pendingRebuild;
    private long _generation;
    public Task? OriginalClose => _originalWork.OriginalClose;

    public HavenGenUiSceneSurface(GenerativeUiEventRouter router, GenUiInstanceStore instances)
        : this(router, instances, null) { }
    internal HavenGenUiSceneSurface(GenerativeUiEventRouter router, GenUiInstanceStore instances, Func<bool>? originalPresentationCurrent)
    {
        _originalPresentationCurrent = originalPresentationCurrent;
        _originalWork = new DesktopOriginalWorkLifetime(StopChildrenAsync, CleanupOriginalAsync);
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _instances = instances ?? throw new ArgumentNullException(nameof(instances));
        Root = new Container { Layout = HavenLayout.Vertical };
        PublishOriginal(() => Root.SetValue(HavenProperties.Width, HavenLength.Percent(100)));
        PublishOriginal(() => Root.SetValue(HavenProperties.Gap, HavenLength.Px(8)));
        Root.Accessibility.AccessibleName = "Generated interface";
        PublishOriginal(() => _activity.SetValue(HavenProperties.FontSize, 11d));
        PublishOriginal(() => _activity.SetValue(HavenProperties.Foreground, "TextSecondary"));
        PublishOriginal(() => _activity.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed));
        _activity.Accessibility.AccessibleName = "Generated interface status";
        _instances.DocumentChanged += OnDocumentChanged;
    }

    public Container Root { get; }
    public GenUiDocument? Document => _document;
    public event EventHandler<GenUiEvent>? SemanticEventEmitted;
    public event EventHandler<GenUiActionResult>? ActionCompleted;

    public void Present(GenUiDocument document) => _originalWork.RunSynchronous(original =>
    {
        ArgumentNullException.ThrowIfNull(document);
        BindOriginal(original, ++_generation);
        original.DemandPublication();
        GenerativeUiContractValidator.ValidateAndThrow(document);
        var previous = _registeringOriginalDocument;
        _registeringOriginalDocument = document;
        try { _instances.Register(document); }
        finally { _registeringOriginalDocument = previous; }
        original.DemandPublication();
        ApplyDocument(document);
        original.DemandPublication();
    });

    public void PresentExisting(GenUiDocument document) => _originalWork.RunSynchronous(original =>
    {
        BindOriginal(original, ++_generation);
        original.DemandPublication();
        ArgumentNullException.ThrowIfNull(document);
        if (_disposed) throw new ObjectDisposedException(nameof(HavenGenUiSceneSurface));
        GenerativeUiContractValidator.ValidateAndThrow(document);
        var registered = _instances.TryGet(document.Origin.InstanceId)
            ?? throw new InvalidOperationException("The generated UI instance is no longer registered.");
        if (registered.Origin.ThreadId != document.Origin.ThreadId)
            throw new InvalidOperationException("A generated UI instance cannot move between threads.");
        ApplyDocument(registered);
        original.DemandPublication();
    });

    public bool OwnsInput(Input input) => !_originalWork.IsRetiring && _pendingRebuild is null &&
        (_inputs.ContainsKey(input) || _whiteboards.Values.Any(whiteboard => whiteboard.OwnsInput(input)));

    public Task SubmitInputAsync(Input input, CancellationToken cancellationToken = default)
    {
        if (_inputs.TryGetValue(input, out var component))
            return EmitAsync(component, GenUiEventType.TextSubmitted, JsonSerializer.SerializeToElement(input.Text), cancellationToken);
        var whiteboard = _whiteboards.Values.FirstOrDefault(candidate => candidate.OwnsInput(input));
        return whiteboard is null ? Task.CompletedTask : whiteboard.SubmitInputAsync(input, cancellationToken);
    }

    private void OnDocumentChanged(object? sender, GenUiDocument document)
    {
        if (_disposed || _originalWork.IsRetiring || ReferenceEquals(document, _registeringOriginalDocument) ||
            _document?.Origin.InstanceId != document.Origin.InstanceId) return;
        var generation = _generation;
        if (Dispatcher.UIThread.CheckAccess())
            RunOriginalCallback(() => ApplyDocument(document));
        else
            _ = _originalWork.RunAsync(async original =>
            {
                BindOriginal(original, generation);
                await PublishOriginalAsync(original, () =>
                {
                    if (_document?.Origin.InstanceId != document.Origin.InstanceId) return;
                    ApplyDocument(document);
                });
            }); // SAME posted-original Task is retained by this owner before dispatch.
    }

    private void ApplyDocument(GenUiDocument document)
    {
        var signature = StructureSignature(document.Root);
        _document = document;
        if (_pendingRebuild is not null || !string.Equals(_structureSignature, signature, StringComparison.Ordinal))
        {
            _structureSignature = signature;
            Rebuild(document);
            return;
        }
        UpdateTree(document.Root);
    }

    private void Rebuild(GenUiDocument document)
    {
        if (_whiteboards.Count != 0 || _pendingRebuild is not null)
        { QueueRebuildOriginal(document); return; }
        RebuildOriginalCore(document);
    }

    private void RebuildOriginalCore(GenUiDocument document)
    {
        DemandOriginalPublication();
        _whiteboards.Clear();
        _elements.Clear();
        _components.Clear();
        _inputs.Clear();
        foreach (var child in Root.Children.ToArray()) PublishOriginal(() => Root.Remove(child));
        var originalRoot = Build(document.Root);
        PublishOriginal(() => Root.Add(originalRoot));
        PublishOriginal(() => Root.Add(_activity));
    }

    private HavenElement Build(GenUiComponent component)
    {
        HavenElement element = component.ComponentType switch
        {
            "HavenWorkspace" or "HavenStack" or "HavenForm" or "HavenWizard" => BuildStack(component, false),
            "HavenToolbar" => BuildStack(component, true),
            "HavenGrid" => BuildGrid(component),
            "HavenSplitView" => BuildSplit(component),
            "HavenCard" => BuildCard(component),
            "HavenText" => new HavenText(),
            "HavenMarkdown" => new Markdown(),
            "HavenButton" => BuildButton(component),
            "HavenTextInput" => BuildInput(component),
            "HavenSelect" => BuildSelect(component),
            "HavenToggle" => BuildToggle(component),
            "HavenSlider" => BuildSlider(component),
            "HavenProgress" => new Progress(),
            "HavenStatus" => BuildStatus(),
            "HavenList" or "HavenTable" => BuildList(component),
            "HavenTabs" => BuildTabs(component),
            "HavenChart" or "HavenGraph" or "HavenCanvas" or "HavenImage" => BuildVisualFoundation(component),
            _ => throw new InvalidOperationException($"Trusted Haven scene renderer has no component mapping for '{component.ComponentType}'.")
        };
        element.Name = "GenUI_" + SanitizeName(component.ComponentId);
        PublishOriginal(() => element.Accessibility.AccessibleName = GetString(component, "automationName") ?? GetString(component, "label") ?? component.ComponentId);
        _elements.Add(component.ComponentId, element);
        _components[component.ComponentId] = component;
        UpdateControl(component, element);
        return element;
    }

    private Container BuildStack(GenUiComponent component, bool horizontal)
    {
        var stack = new Container { Layout = horizontal ? HavenLayout.Horizontal : HavenLayout.Vertical };
        PublishOriginal(() => stack.SetValue(HavenProperties.Gap, HavenLength.Px(GetDouble(component, "spacing", horizontal ? 8 : 10))));
        foreach (var child in component.Children) stack.Add(Build(child));
        return stack;
    }

    private Container BuildGrid(GenUiComponent component)
    {
        var columns = Math.Clamp((int)GetDouble(component, "columns", 2), 1, 6);
        var spacing = Math.Max(0, GetDouble(component, "spacing", 12));
        var responsive = GetBool(component, "responsive");
        var itemMinWidth = Math.Max(120, GetDouble(component, "itemMinWidth", 280));
        var grid = new Container
        {
            Layout = responsive ? HavenLayout.Wrap : HavenLayout.Grid,
            Columns = responsive ? string.Empty : string.Join(' ', Enumerable.Repeat("1fr", columns))
        };
        PublishOriginal(() => grid.SetValue(HavenProperties.Gap, HavenLength.Px(spacing)));
        PublishOriginal(() => grid.SetValue(HavenProperties.Width, HavenLength.Percent(100)));
        PublishOriginal(() => grid.SetValue(HavenProperties.Responsive, responsive));
        for (var index = 0; index < component.Children.Count; index++)
        {
            var child = Build(component.Children[index]);
            if (responsive)
            {
                PublishOriginal(() => child.SetValue(HavenProperties.MinWidth, HavenLength.Px(itemMinWidth)));
                PublishOriginal(() => child.SetValue(HavenProperties.Responsive, true));
            }
            else
            {
                PublishOriginal(() => child.SetValue(HavenProperties.Column, index % columns));
                PublishOriginal(() => child.SetValue(HavenProperties.Row, index / columns));
            }
            grid.Add(child);
        }
        return grid;
    }

    private Container BuildSplit(GenUiComponent component)
    {
        var split = new Container { Layout = HavenLayout.Grid, Columns = "1fr 1fr" };
        PublishOriginal(() => split.SetValue(HavenProperties.Gap, HavenLength.Px(12)));
        for (var index = 0; index < Math.Min(2, component.Children.Count); index++)
        {
            var child = Build(component.Children[index]);
            PublishOriginal(() => child.SetValue(HavenProperties.Column, index));
            split.Add(child);
        }
        return split;
    }

    private Container BuildCard(GenUiComponent component)
    {
        var card = new Container { Layout = HavenLayout.Vertical };
        PublishOriginal(() => card.SetValue(HavenProperties.Background, string.Equals(GetString(component, "variant"), "flashcard", StringComparison.OrdinalIgnoreCase) ? "Accent" : "SurfaceRaised"));
        PublishOriginal(() => card.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(string.Equals(GetString(component, "variant"), "flashcard", StringComparison.OrdinalIgnoreCase) ? 28 : 14))));
        PublishOriginal(() => card.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))));
        PublishOriginal(() => card.SetValue(HavenProperties.Gap, HavenLength.Px(GetDouble(component, "spacing", 8))));
        foreach (var child in component.Children) card.Add(Build(child));
        if (component.Actions.Count > 0)
        {
            card.Accessibility.Focusable = true;
            PublishOriginal(() => card.SetValue(HavenProperties.Hover, true));
            card.Invoked += async (_, _) => await EmitAsync(component, GenUiEventType.ActionInvoked, null, CancellationToken.None);
        }
        return card;
    }

    private HavenButton BuildButton(GenUiComponent component)
    {
        var button = new HavenButton
        {
            Variant = GetString(component, "kind")?.ToLowerInvariant() switch
            {
                "primary" => ButtonVariant.Primary,
                "tertiary" => ButtonVariant.Tertiary,
                "negative" or "destructive" => ButtonVariant.Danger,
                "text" => ButtonVariant.Text,
                "ghost" => ButtonVariant.Ghost,
                _ => ButtonVariant.Secondary
            }
        };
        PublishOriginal(() => button.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Start));
        button.Invoked += async (_, _) => await EmitAsync(component, GenUiEventType.ActionInvoked, GetValue(component, "value"), CancellationToken.None);
        return button;
    }

    private Input BuildInput(GenUiComponent component)
    {
        var input = new Input { Multiline = GetBool(component, "multiline"), SubmitOnEnter = true };
        _inputs[input] = component;
        return input;
    }

    private Select BuildSelect(GenUiComponent component)
    {
        var select = new Select();
        select.SelectionChanged += async (_, _) =>
        {
            if (component.Actions.Count > 0 && select.Parent is not null)
                await EmitAsync(component, GenUiEventType.OptionSelected, JsonSerializer.SerializeToElement(select.SelectedItem), CancellationToken.None);
        };
        return select;
    }

    private Toggle BuildToggle(GenUiComponent component)
    {
        var toggle = new Toggle();
        toggle.Invoked += async (_, _) =>
        {
            if (component.Actions.Count > 0)
                await EmitAsync(component, GenUiEventType.ToggleChanged, JsonSerializer.SerializeToElement(toggle.IsChecked), CancellationToken.None);
        };
        return toggle;
    }

    private Slider BuildSlider(GenUiComponent component)
    {
        var slider = new Slider();
        slider.Invoked += async (_, _) =>
        {
            if (component.Actions.Count > 0)
                await EmitAsync(component, GenUiEventType.SliderChanged, JsonSerializer.SerializeToElement(slider.Value), CancellationToken.None);
        };
        return slider;
    }

    private HavenElement BuildStatus()
    {
        var status = new HavenText();
        PublishOriginal(() => status.SetValue(HavenProperties.Background, "SurfaceRaised"));
        PublishOriginal(() => status.SetValue(HavenProperties.Padding, HavenThickness.Parse("6px 10px")));
        PublishOriginal(() => status.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(12))));
        return status;
    }

    private Container BuildList(GenUiComponent component)
    {
        var list = new Container { Layout = HavenLayout.Vertical };
        PublishOriginal(() => list.SetValue(HavenProperties.Gap, HavenLength.Px(5)));
        return list;
    }

    private Container BuildTabs(GenUiComponent component)
    {
        var root = new Container { Layout = HavenLayout.Vertical };
        PublishOriginal(() => root.SetValue(HavenProperties.Gap, HavenLength.Px(8)));
        var headers = new Container { Layout = HavenLayout.Horizontal };
        PublishOriginal(() => headers.SetValue(HavenProperties.Gap, HavenLength.Px(6)));
        var bodies = new List<HavenElement>();
        for (var index = 0; index < component.Children.Count; index++)
        {
            var tab = component.Children[index];
            var body = Build(tab);
            PublishOriginal(() => body.SetValue(HavenProperties.Visibility, index == 0 ? HavenVisibility.Visible : HavenVisibility.Collapsed));
            bodies.Add(body);
            var tabIndex = index;
            var button = new HavenButton { Variant = ButtonVariant.Ghost, Content = GetString(tab, "title") ?? tab.ComponentId };
            button.Invoked += (_, _) => RunOriginalCallback(() =>
            {
                for (var bodyIndex = 0; bodyIndex < bodies.Count; bodyIndex++)
                    PublishOriginal(() => bodies[bodyIndex].SetValue(HavenProperties.Visibility, bodyIndex == tabIndex ? HavenVisibility.Visible : HavenVisibility.Collapsed));
            });
            headers.Add(button);
        }
        root.Add(headers);
        foreach (var body in bodies) root.Add(body);
        return root;
    }

    private HavenElement BuildVisualFoundation(GenUiComponent component)
    {
        if (component.ComponentType is "HavenGraph" or "HavenChart")
            return HavenGenUiPlot.FromComponent(component);

        if (component.ComponentType.Equals("HavenCanvas", StringComparison.Ordinal))
        {
            var stateKey = "canvas." + component.ComponentId;
            JsonElement? persisted = null;
            if (_document?.State.TryGetValue(stateKey, out var state) == true) persisted = state;
            var originalDocument = _document ?? throw new InvalidOperationException("The original whiteboard has no owning document.");
            HavenGenUiWhiteboard? whiteboard = null;
            whiteboard = new HavenGenUiWhiteboard(
                component,
                persisted,
                value =>
                {
                    DemandOriginalPublication();
                    var document = _document;
                    if (document is null) return;
                    _instances.ApplyPatch(new GenUiStatePatch(
                        Guid.NewGuid(), document.Origin.InstanceId, GenUiPatchOperation.Replace,
                        "state", stateKey, value, DateTimeOffset.UtcNow));
                    DemandOriginalPublication();
                },
                component.Actions.Count == 0
                    ? null
                    : request => EmitAsync(component, GenUiEventType.ActionInvoked, request, CancellationToken.None),
                () => !_disposed && !_originalWork.IsRetiring && _pendingRebuild is null &&
                    (_originalPresentationCurrent?.Invoke() ?? true),
                value => PersistOriginalChildRetirement(
                    whiteboard ?? throw new InvalidOperationException("The original whiteboard acquisition has not returned."),
                    originalDocument.Origin.InstanceId, stateKey, value));
            _acquiredWhiteboards.Add(whiteboard); // Capture actual child before attached publication.
            if (_originalWork.IsRetiring) whiteboard.RequestRetirement();
            DemandOriginalPublication();
            _whiteboards[component.ComponentId] = whiteboard;
            return whiteboard;
        }

        if (component.ComponentType.Equals("HavenImage", StringComparison.Ordinal))
        {
            var image = new Image
            {
                Source = GetString(component, "source") ?? GetString(component, "url") ?? string.Empty,
                Fit = GetString(component, "fit")?.ToLowerInvariant() switch
                {
                    "cover" => HavenImageFit.Cover,
                    "fill" => HavenImageFit.Fill,
                    "none" => HavenImageFit.None,
                    _ => HavenImageFit.Contain
                }
            };
            PublishOriginal(() => image.SetValue(HavenProperties.MinHeight, HavenLength.Px(GetDouble(component, "minHeight", 180))));
            PublishOriginal(() => image.SetValue(HavenProperties.Width, HavenLength.Percent(100)));
            return image;
        }

        var visual = new Container { Layout = HavenLayout.Vertical };
        PublishOriginal(() => visual.SetValue(HavenProperties.MinHeight, HavenLength.Px(GetDouble(component, "minHeight", 180))));
        PublishOriginal(() => visual.SetValue(HavenProperties.Width, HavenLength.Percent(100)));
        PublishOriginal(() => visual.SetValue(HavenProperties.Background, "SurfaceRaised"));
        PublishOriginal(() => visual.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(16))));
        PublishOriginal(() => visual.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(16))));
        var label = new HavenText { Content = GetString(component, "emptyText") ?? $"{component.ComponentType} foundation" };
        PublishOriginal(() => label.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center));
        visual.Add(label);
        return visual;
    }

    private void UpdateTree(GenUiComponent component)
    {
        if (_elements.TryGetValue(component.ComponentId, out var element))
        {
            _components[component.ComponentId] = component;
            UpdateControl(component, element);
        }
        foreach (var child in component.Children) UpdateTree(child);
    }

    private void UpdateControl(GenUiComponent component, HavenElement element)
    {
        switch (element)
        {
            case HavenText text:
                PublishOriginal(() => text.Content = GetString(component, "text") ?? GetString(component, "label") ?? string.Empty);
                var textSize = GetDouble(component, "fontSize", 0);
                if (textSize > 0) PublishOriginal(() => text.SetValue(HavenProperties.FontSize, textSize));
                PublishOriginal(() => text.SetValue(HavenProperties.FontWeight, GetBool(component, "emphasis") ? 800 : 500));
                PublishOriginal(() => text.SetValue(HavenProperties.Opacity, Math.Clamp(GetDouble(component, "opacity", 1), 0, 1)));
                break;
            case Markdown markdown:
                PublishOriginal(() => markdown.Content = GetString(component, "text") ?? GetString(component, "label") ?? string.Empty);
                break;
            case HavenButton button:
                PublishOriginal(() => button.Content = GetString(component, "label") ?? component.ComponentId);
                PublishOriginal(() => button.SetValue(HavenProperties.Enabled, !GetBool(component, "disabled")));
                break;
            case Input input:
                PublishOriginal(() => input.Placeholder = GetString(component, "placeholder") ?? string.Empty);
                var next = GetString(component, "value") ?? string.Empty;
                if (!input.State.HasFlag(HavenElementState.Focused) && input.Text != next) PublishOriginal(() => input.Text = next);
                break;
            case Select select:
                var options = GetStringArray(component, "options");
                PublishOriginal(() => select.Items = options);
                var requested = GetString(component, "value");
                PublishOriginal(() => select.SelectedIndex = requested is null ? (options.Count > 0 ? 0 : -1) : options.ToList().FindIndex(item => item.Equals(requested, StringComparison.Ordinal)));
                break;
            case Toggle toggle:
                PublishOriginal(() => toggle.IsChecked = GetBool(component, "value"));
                break;
            case Slider slider:
                PublishOriginal(() => slider.Minimum = GetDouble(component, "minimum", 0));
                PublishOriginal(() => slider.Maximum = GetDouble(component, "maximum", 100));
                PublishOriginal(() => slider.Value = GetDouble(component, "value", slider.Minimum));
                break;
            case Progress progress:
                PublishOriginal(() => progress.Value = GetDouble(component, "value", 0));
                break;
            case HavenGenUiPlot plot:
                plot.Update(component);
                break;
            case HavenGenUiWhiteboard whiteboard:
                whiteboard.Update(component);
                break;
            case Container list when component.ComponentType is "HavenList" or "HavenTable":
                foreach (var child in list.Children.ToArray()) PublishOriginal(() => list.Remove(child));
                foreach (var item in GetStringArray(component, "items")) PublishOriginal(() => list.Add(new HavenText { Content = item }));
                break;
        }

        var minWidth = GetDouble(component, "minWidth", 0);
        if (minWidth > 0) PublishOriginal(() => element.SetValue(HavenProperties.MinWidth, HavenLength.Px(minWidth)));
        var minHeight = GetDouble(component, "minHeight", 0);
        if (minHeight > 0) PublishOriginal(() => element.SetValue(HavenProperties.MinHeight, HavenLength.Px(minHeight)));
        var width = GetDouble(component, "width", 0);
        if (width > 0) PublishOriginal(() => element.SetValue(HavenProperties.Width, HavenLength.Px(width)));
        var height = GetDouble(component, "height", 0);
        if (height > 0) PublishOriginal(() => element.SetValue(HavenProperties.Height, HavenLength.Px(height)));

        var horizontalAlignment = GetString(component, "horizontalAlignment")?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(horizontalAlignment))
        {
            PublishOriginal(() => element.SetValue(HavenProperties.HorizontalAlignment, horizontalAlignment switch
            {
                "left" or "start" => HavenHorizontalAlignment.Start,
                "center" => HavenHorizontalAlignment.Center,
                "right" or "end" => HavenHorizontalAlignment.End,
                "stretch" => HavenHorizontalAlignment.Stretch,
                _ => element.GetValue(HavenProperties.HorizontalAlignment)
            }));
        }

        var verticalAlignment = GetString(component, "verticalAlignment")?.ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(verticalAlignment))
        {
            PublishOriginal(() => element.SetValue(HavenProperties.VerticalAlignment, verticalAlignment switch
            {
                "top" or "start" => HavenVerticalAlignment.Start,
                "center" => HavenVerticalAlignment.Center,
                "bottom" or "end" => HavenVerticalAlignment.End,
                "stretch" => HavenVerticalAlignment.Stretch,
                _ => element.GetValue(HavenProperties.VerticalAlignment)
            }));
        }

        if (element is HavenText && string.Equals(GetString(component, "tone"), "onAccent", StringComparison.OrdinalIgnoreCase))
            PublishOriginal(() => element.SetValue(HavenProperties.Foreground, "TextOnAccent"));

        PublishOriginal(() => element.Accessibility.AccessibleName = GetString(component, "automationName") ?? GetString(component, "label") ?? component.ComponentId);
    }

    private Task EmitAsync(GenUiComponent component, GenUiEventType eventType, JsonElement? value, CancellationToken cancellationToken)
    {
        var presentedDocument = _document;
        var generation = _generation;
        return _originalWork.RunAsync(async original =>
        {
            BindOriginal(original, generation);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            linked.Token.ThrowIfCancellationRequested();
            original.DemandPublication();
            if (presentedDocument is null || _pendingRebuild is not null) return;
            var document = _instances.TryGet(presentedDocument.Origin.InstanceId);
            if (document is null || document.Origin.ThreadId != presentedDocument.Origin.ThreadId) return;
            var currentComponent = FindComponent(document.Root, component.ComponentId);
            if (currentComponent is null) return;
            var binding = GenerativeUiContractValidator.SelectActionBinding(currentComponent);
            if (binding is null) return;
            component = currentComponent;
            if (value is null && eventType == GenUiEventType.ActionInvoked) value = GetValue(component, "value");
            var semanticEvent = new GenUiEvent(
                Guid.NewGuid(), eventType, DateTimeOffset.UtcNow, document.Origin,
                component.ComponentId, binding.ActionId, null, null, value,
                JsonSerializer.SerializeToElement(new { values = CaptureCurrentInputValues(), component = component.ComponentId, value }),
                GenUiEventSource.User, "Haven Chat generated UI interaction");
            original.DemandPublication();
            SemanticEventEmitted?.Invoke(this, semanticEvent);
            original.DemandPublication();
            SetActivity(binding.Route == GenUiRouteKind.Local ? "Updating…" : "Haven is working…");
            Task<GenUiActionResult>? actualRoute = null;
            try
            {
                original.DemandPublication();
                actualRoute = _router.RouteAsync(semanticEvent, binding, linked.Token);
                var result = await original.AwaitAsync(actualRoute);
                await PublishOriginalAsync(original, () =>
                {
                    original.DemandPublication(); SetActivity(result.Summary);
                    original.DemandPublication(); ActionCompleted?.Invoke(this, result); original.DemandPublication();
                });
            }
            catch (OperationCanceledException cause) when (linked.IsCancellationRequested)
            { original.Capture(actualRoute, cause); } // Retain the exact handled policy cause; never a green close waiver.
            catch (Exception exception)
            {
                original.Capture(actualRoute, exception);
                if (original.IsPublicationCurrent)
                    await PublishOriginalAsync(original, () =>
                    {
                        var message = $"Generated action failed: {exception.Message}";
                        original.DemandPublication(); Root.Accessibility.Description = message;
                        original.DemandPublication(); SetActivity(message);
                    });
            }
        });
    }

    private void SetActivity(string? value)
    {
        PublishOriginal(() => _activity.Content = value ?? string.Empty);
        PublishOriginal(() => _activity.SetValue(HavenProperties.Visibility, string.IsNullOrWhiteSpace(value) ? HavenVisibility.Collapsed : HavenVisibility.Visible));
    }

    private IReadOnlyDictionary<string, object?> CaptureCurrentInputValues()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (componentId, element) in _elements)
        {
            switch (element)
            {
                case Input input:
                    values[componentId] = input.Text;
                    break;
                case Select select:
                    values[componentId] = select.SelectedItem;
                    break;
                case Toggle toggle:
                    values[componentId] = toggle.IsChecked;
                    break;
                case Slider slider:
                    values[componentId] = slider.Value;
                    break;
            }
        }
        return values;
    }

    private static string StructureSignature(GenUiComponent component) =>
        component.ComponentId + ":" + component.ComponentType + "[" + string.Join(',', component.Children.Select(StructureSignature)) + "]";

    private static GenUiComponent? FindComponent(GenUiComponent root, string componentId)
    {
        if (root.ComponentId.Equals(componentId, StringComparison.Ordinal)) return root;
        foreach (var child in root.Children)
        {
            var match = FindComponent(child, componentId);
            if (match is not null) return match;
        }
        return null;
    }

    private static string SanitizeName(string value)
    {
        var chars = value.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray();
        return new string(chars);
    }

    private static string? GetString(GenUiComponent component, string key)
    {
        if (!component.Properties.TryGetValue(key, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static bool GetBool(GenUiComponent component, string key)
    {
        if (!component.Properties.TryGetValue(key, out var value)) return false;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static double GetDouble(GenUiComponent component, string key, double fallback)
    {
        if (!component.Properties.TryGetValue(key, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number) ? number : fallback;
    }

    private static JsonElement? GetValue(GenUiComponent component, string key) =>
        component.Properties.TryGetValue(key, out var value) ? value : null;

    private static IReadOnlyList<string> GetStringArray(GenUiComponent component, string key)
    {
        if (!component.Properties.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString()).ToArray();
    }

    private void BindOriginal(DesktopOriginalWorkLifetime.Original original, long generation) =>
        original.BindPublicationGuard(() => !_disposed && _generation == generation && (_originalPresentationCurrent?.Invoke() ?? true));
    private void DemandOriginalPublication()
    {
        if (_originalWork.Executing is { } original) original.DemandPublication();
        else _originalWork.DemandAdmission(); // Constructor-before-return is a separate acquisition prerequisite.
    }
    private void RunOriginalCallback(Action callback)
    {
        if (_originalWork.Executing is { } original)
        { original.DemandPublication(); callback(); original.DemandPublication(); return; }
        _originalWork.RunSynchronous(admitted =>
        { BindOriginal(admitted, _generation); admitted.DemandPublication(); callback(); admitted.DemandPublication(); });
    }
    private void PublishOriginal(Action actualWrite)
    { DemandOriginalPublication(); actualWrite(); DemandOriginalPublication(); }
    private async Task PublishOriginalAsync(DesktopOriginalWorkLifetime.Original original, Action callback)
    {
        var actualDispatcher = Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunSynchronous(callbackOriginal =>
        {
            callbackOriginal.BindPublicationGuard(() => original.IsPublicationCurrent);
            original.DemandPublication(); callbackOriginal.DemandPublication();
            callback();
            callbackOriginal.DemandPublication(); original.DemandPublication();
        })).GetTask();
        await original.AwaitAsync(actualDispatcher);
    }
    private void PersistOriginalChildRetirement(HavenGenUiWhiteboard actualChild, Guid originalInstanceId,
        string originalStateKey, JsonElement actualState)
    {
        var actualClose = actualChild.OriginalClose
            ?? throw new InvalidOperationException("The actual child release has no published original close.");
        bool HasOriginalChild() => _acquiredWhiteboards.Any(child => ReferenceEquals(child, actualChild)) &&
            ReferenceEquals(actualChild.OriginalClose, actualClose);
        void ApplyActualState()
        {
            if (!HasOriginalChild())
                throw new InvalidOperationException("The released child is not this scene's retained original acquisition.");
            // Source-created callback from the SAME child's stop, exact old instance
            // and state key. This is cleanup, never new input or a replacement frame.
            if (!_instances.ApplyPatch(new GenUiStatePatch(Guid.NewGuid(), originalInstanceId,
                GenUiPatchOperation.Replace, "state", originalStateKey, actualState, DateTimeOffset.UtcNow)))
                throw new InvalidOperationException("Original whiteboard retirement state was not acknowledged by its instance owner.");
        }
        if (_originalWork.OriginalClose is not null)
            _originalWork.RunCloseCallback(ApplyActualState);
        else
            _originalWork.RunSynchronous(original =>
            {
                // A live-parent structural rebuild has no parent close. Admit the
                // actual cleanup callback before its synchronous store notifications.
                original.BindPublicationGuard(() => !_disposed && HasOriginalChild());
                original.DemandPublication();
                ApplyActualState();
                // No normal/native publication follows the acknowledged old-state
                // patch. Any notification/patch failure belongs to this real original.
            });
    }

    private void QueueRebuildOriginal(GenUiDocument document)
    {
        var preceding = _pendingRebuild;
        var generation = _generation;
        var children = _whiteboards.Values.ToArray();
        _ = _originalWork.RunAsync(async original =>
        {
            BindOriginal(original, generation);
            var childFailures = new List<Exception>();
            // This exact renderer original exists before any stop callback is entered.
            foreach (var child in children)
                try { child.RequestRetirement(); } catch (Exception cause) { Capture(childFailures, null, cause); original.Retain(cause); }
            try
            {
                if (preceding is not null)
                    try { await original.AwaitAsync(preceding); }
                    catch (Exception cause) { original.Capture(preceding, cause); }
                // Preceding renderer failure remains retained; independently inspect
                // every actual child close before any physical old-tree removal.
                foreach (var child in children)
                {
                    Task? close = null;
                    try
                    {
                        close = child.OriginalClose ?? throw new InvalidOperationException("The captured whiteboard did not publish its original close.");
                        await original.AwaitAsync(close);
                    }
                    catch (Exception cause) { Capture(childFailures, close, cause); original.Capture(close, cause); }
                }
                Throw(childFailures); // All actual children are independently joined, including failed siblings.
                await PublishOriginalAsync(original, () =>
                {
                    original.DemandPublication();
                    if (!ReferenceEquals(_document, document)) return;
                    RebuildOriginalCore(document);
                    foreach (var child in children) _acquiredWhiteboards.Remove(child);
                });
            }
            finally
            {
                // The exact task remains in the original ledger even after this
                // pending-publication pointer retires. No successful drain is inferred.
                _ = Interlocked.CompareExchange(ref _pendingRebuild, null, original.Task);
            }
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
    private Task StopChildrenAsync() => Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunCloseCallback(() =>
    {
        _instances.DocumentChanged -= OnDocumentChanged;
        var errors = new List<Exception>();
        foreach (var child in _acquiredWhiteboards.ToArray())
            try { child.RequestRetirement(); } catch (Exception cause) { errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Original generated whiteboard stops failed.", errors);
    })).GetTask();
    private async Task CleanupOriginalAsync()
    {
        var errors = new List<Exception>();
        foreach (var child in _acquiredWhiteboards.ToArray())
        {
            try { child.RequestRetirement(); } catch (Exception cause) { Add(errors, cause); }
            Task? close = null;
            try
            {
                close = child.OriginalClose ?? throw new InvalidOperationException("The actual whiteboard close is unavailable.");
                await close;
            }
            catch (Exception cause) { Capture(errors, close, cause); }
        }
        if (errors.Count != 0) Throw(errors);
        var actualCleanup = Dispatcher.UIThread.InvokeAsync(() => _originalWork.RunCloseCallback(() =>
        {
            _disposed = true;
            _whiteboards.Clear(); _acquiredWhiteboards.Clear(); _elements.Clear(); _components.Clear(); _inputs.Clear();
        })).GetTask();
        try { await actualCleanup; } catch (Exception cause) { Capture(errors, actualCleanup, cause); }
        Throw(errors);
    }
    private static void Add(List<Exception> errors, Exception cause)
    { if (!errors.Any(error => ReferenceEquals(error, cause))) errors.Add(cause); }
    private static void Capture(List<Exception> errors, Task? actual, Exception cause)
    {
        if (actual?.Exception is { InnerExceptions.Count: > 0 } group)
            foreach (var direct in group.InnerExceptions) Add(errors, direct);
        else Add(errors, cause);
    }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 0) return;
        if (errors.Count > 1 || errors[0] is OperationCanceledException)
            throw new AggregateException("Original generated scene child or cleanup failed; physical tree retained.", errors);
        ExceptionDispatchInfo.Capture(errors[0]).Throw();
    }

}
