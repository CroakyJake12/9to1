using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Embedding;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// CUI-owned backend templates. The pinned backend supplies behavior, input and
/// accessibility; no donor theme, donor selectors or AXAML loader participates.
/// Named parts below are the mature backend's private behavior adapter contract.
/// </summary>
public static class CuiControlTemplates
{
    private static readonly Lazy<ResourceDictionary> Catalogue = new(CreateCatalogue);

    // NOTE: there is intentionally NO process-wide ToolTip.DefaultConfigurePopup registration here.
    // Per-open transparency routing for CUI tooltips is established through the narrow per-control
    // attached ConfigurePopup path (CuiControlLoader sets ToolTip.SetConfigurePopup on authored
    // controls), so unrelated non-CUI tooltip producers are never configured by this runtime.
    public static void Populate(IResourceDictionary resources)
    { foreach(var entry in Catalogue.Value) resources[entry.Key]=entry.Value; }
    public static bool TryGet(Type backendType,out IControlTemplate? template)
    {
        // Ordinary adapters may subclass a supported primitive for behavior.
        // Resolve its nearest CUI template explicitly; backend style lookup must
        // never choose an inherited donor template for that subclass.
        for (var type = backendType; type is not null; type = type.BaseType)
            if(Catalogue.Value.TryGetValue(type,out var value) && value is ControlTheme theme && theme.Setters.SingleOrDefault() is Setter setter && setter.Value is IControlTemplate found)
            { template=found;return true; }
        template=null;return false;
    }
    private static ResourceDictionary CreateCatalogue()
    {
        var resources=new ResourceDictionary();
        Add<Button>(resources, Button); Add<RepeatButton>(resources, Button); Add<ToggleButton>(resources, Button);
        Add<CheckBox>(resources, Choice); Add<RadioButton>(resources, Choice); Add<ToggleSwitch>(resources, Switch);
        Add<TextBox>(resources, TextInput); Add<ScrollViewer>(resources, Scroll); Add<ScrollBar>(resources, ScrollBar);
        Add<Thumb>(resources, (owner, _) => Frame(owner)); Add<Slider>(resources, Slider);
        Add<CuiResizeInput>(resources, (owner, _) =>
        {
            var handle = new Border { Width = 2, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Resource(handle, Border.BackgroundProperty, "CuiLineBrush");
            return Frame(owner, handle);
        });
        Add<ComboBox>(resources, Select); Add<ComboBoxItem>(resources, Content); Add<ListBoxItem>(resources, Content);
        Add<ListBox>(resources, Items); Add<ItemsControl>(resources, Items); Add<TreeView>(resources, Items);
        Add<TreeViewItem>(resources, TreeItem); Add<Menu>(resources, Items);
        Add<ContextMenu>(resources, (owner, scope) => PopupFrame(owner, new ScrollViewer { Content = ItemPresenter(owner, scope) }));
        Add<MenuItem>(resources, MenuItem); Add<TabControl>(resources, Tabs); Add<TabItem>(resources, Header);
        Add<CuiMenuItem>(resources, (owner, scope) => MenuItem(owner, scope));
        Add<ContentControl>(resources, Content); Add<UserControl>(resources, Content);
        Add<ToolTip>(resources, (owner, scope) => PopupFrame(owner, Presenter(owner, scope)));
        Add<NumericUpDown>(resources, Numeric); Add<ButtonSpinner>(resources, Spinner);
        Add<ProgressBar>(resources, Progress); Add<Separator>(resources, Separator);
        Add<DataValidationErrors>(resources, Content); Add<Expander>(resources, Expander);
        Add<Window>(resources, TopLevelContent);
        Add<EmbeddableControlRoot>(resources, TopLevelContent);
        Add<PopupRoot>(resources, TopLevelContent);
        return resources;
    }

    private static void Add<T>(IResourceDictionary resources, Func<T, INameScope, Control> build) where T : TemplatedControl
    {
        var theme = new ControlTheme(typeof(T));
        theme.Setters.Add(new Setter(TemplatedControl.TemplateProperty, new FuncControlTemplate<T>((owner, names) =>
        {
            CuiPropertyPresentation.Attach(owner);
            return build(owner, names);
        })));
        resources[typeof(T)] = theme;
    }

    private static T Name<T>(T control, string name, INameScope scope) where T : Control
    { control.Name = name; scope.Register(name, control); return control; }
    private static void Bind(AvaloniaObject target, AvaloniaProperty property, object source, string path, bool twoWay = false) =>
        target.Bind(property, new Binding(path) { Source = source, Mode = twoWay ? BindingMode.TwoWay : BindingMode.OneWay });
    private static void Resource(Control target, AvaloniaProperty property, string key) =>
        target.Bind(property, target.GetResourceObservable(key));
    private static Border Frame(TemplatedControl owner, Control? content = null)
    {
        var frame = new CuiMaterialBorder { Child = content };
        Bind(frame, Border.BackgroundProperty, owner, "Background"); Bind(frame, Border.BorderBrushProperty, owner, "BorderBrush");
        Bind(frame, Border.BorderThicknessProperty, owner, "BorderThickness"); Bind(frame, Border.CornerRadiusProperty, owner, "CornerRadius");
        return frame;
    }
    private static ContentPresenter Presenter(TemplatedControl owner, INameScope scope, string name = "PART_ContentPresenter", string property = "Content")
    {
        var presenter = Name(new ContentPresenter(), name, scope);
        // A retiring template must release its visual content when its templated
        // parent is removed. A Binding with Source=owner survives that removal
        // and leaves the same Control parented by the old presenter on retemplate.
        var content = AvaloniaPropertyRegistry.Instance.FindRegistered(owner, property)
            ?? throw new InvalidOperationException($"No canonical template content property '{property}'.");
        var contentTemplate = AvaloniaPropertyRegistry.Instance.FindRegistered(owner, property == "Header" ? "HeaderTemplate" : "ContentTemplate")
            ?? throw new InvalidOperationException("No canonical content-template property.");
        presenter.Bind(ContentPresenter.ContentProperty, new TemplateBinding(content));
        presenter.Bind(ContentPresenter.ContentTemplateProperty, new TemplateBinding(contentTemplate));
        Bind(presenter, ContentPresenter.PaddingProperty, owner, "Padding");
        Bind(presenter, ContentPresenter.HorizontalContentAlignmentProperty, owner, "HorizontalContentAlignment");
        Bind(presenter, ContentPresenter.VerticalContentAlignmentProperty, owner, "VerticalContentAlignment");
        return presenter;
    }

    private static Control Button(Button owner, INameScope scope) => Frame(owner, Presenter(owner, scope));
    private static Control Content(ContentControl owner, INameScope scope)
    {
        if (owner is ComboBoxItem) CuiPropertyPresentation.ConfigurePart(owner, CuiPropertyPresentation.TemplatePart.FloatingMenuItem);
        return Frame(owner, Presenter(owner, scope));
    }
    private static Control Header(HeaderedContentControl owner, INameScope scope) => Frame(owner, Presenter(owner, scope, property: "Header"));

    private static Control TopLevelContent(TopLevel owner, INameScope scope)
    {
        // Popups, menu light-dismiss and text selection require the native root
        // layers even when all visible chrome comes from CUI-owned templates.
        var layers = Name(new VisualLayerManager { Child = Presenter(owner, scope) }, "PART_VisualLayerManager", scope);
        var fallback = Name(new Border { IsHitTestVisible = false }, "PART_TransparencyFallback", scope);
        // Popup roots host floating popup chrome whose rounded surface and shadow margin must remain
        // transparent. The donor PopupRoot default Background is white, so an unmodified root would
        // paint one opaque white rectangle behind every popup (the observed defect). CUI owns the
        // popup backdrop: leave the root backing transparent and give TopLevel's transparency
        // fallback a distinct guaranteed-Opaque semantic brush. The popup chrome (CuiMaterialBorder)
        // keeps using CuiPopupSurfaceBrush, so material/transparency of the visible surface is
        // preserved independently. The fallback border is driven by an alpha-aware, lifetime-owned
        // projection (CuiTransparencyFallback): when real per-pixel alpha was negotiated it stays
        // unpainted across theme changes; only a positively opaque destination paints the latest
        // opaque semantic color. Normal full-window roots keep the shared Frame (owner-bound
        // background).
        if (owner is PopupRoot popupRoot)
        {
            // The donor PopupRoot default Background is white; leave the root backing transparent so only
            // the themed chrome (rounded surface + shadow margin) paints. The alpha-aware fallback border
            // paints the guaranteed-Opaque semantic brush only on a positively opaque destination.
            owner.Background = Brushes.Transparent;
            owner.Bind(TopLevel.TransparencyBackgroundFallbackProperty, owner.GetResourceObservable("CuiPopupOpaqueFallbackBrush"));
            CuiTransparencyFallback.Attach(popupRoot, fallback);
            return new Panel { Children = { fallback, new CuiMaterialBorder { Child = layers } } };
        }
        return new Panel { Children = { fallback, Frame(owner, layers) } };
    }

    private static Control Choice(ToggleButton owner, INameScope scope)
    {
        var row = new Grid { ColumnDefinitions = new("Auto,*"), ColumnSpacing = 8 };
        var mark = new TextBlock { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        Bind(mark, TextBlock.ForegroundProperty, owner, "Foreground");
        void Update() => mark.Text = owner.IsChecked == true ? owner is RadioButton ? "●" : "✓" : owner.IsChecked is null ? "−" : string.Empty;
        owner.PropertyChanged += (_, change) => { if (change.Property == ToggleButton.IsCheckedProperty) Update(); };
        Update();
        var box = Frame(owner, mark); Resource(box, Control.WidthProperty, "CuiChoiceSize"); Resource(box, Control.HeightProperty, "CuiChoiceSize"); box.VerticalAlignment = VerticalAlignment.Center;
        Resource(box, Border.CornerRadiusProperty, owner is RadioButton ? "CuiThumbRadius" : "CuiChoiceRadius");
        var presenter = Presenter(owner, scope); Grid.SetColumn(presenter, 1);
        row.Children.Add(box); row.Children.Add(presenter);
        return row;
    }

    /// <summary>
    /// CUI-owned real toggle switch (track + sliding thumb). This is a distinct primitive from the
    /// checkbox Choice template: it paints a themed rail whose fill flips to the accent when the
    /// switch is on, and positions a round thumb on the checked side.
    ///
    /// The pinned ToggleSwitch backend owns the toggle/pointer/keyboard behavior, so this template
    /// must provide the backend's named geometry contract rather than a parallel hand-rolled mover:
    ///   * PART_SwitchKnob (Panel) — the coordinate space/range the moving knob travels inside. The
    ///     backend reads its Bounds.Width to decide the on-position and drag midpoint.
    ///   * PART_MovingKnobs (Panel) — the panel the backend moves via Canvas.Left (0 off, switchKnob
    ///     width on); it carries the actual round thumb visual, so the visible thumb really moves.
    /// Pointer press/release/move handlers, drag, keyboard Space/Enter activation and the
    /// IToggleProvider automation peer are all native backend behavior. The typed projection still
    /// supplies disabled/hover/focus/validation state paint via the shared CUI state model.
    /// </summary>
    private static Control Switch(ToggleSwitch owner, INameScope scope)
    {
        var offTrack = new Border();
        Resource(offTrack, Border.BackgroundProperty, "CuiSwitchTrackBrush");
        Resource(offTrack, Border.CornerRadiusProperty, "CuiSwitchTrackRadius");
        var hoverTrack = new Border();
        Resource(hoverTrack, Border.BackgroundProperty, "CuiSwitchHoverBrush");
        Resource(hoverTrack, Border.CornerRadiusProperty, "CuiSwitchTrackRadius");
        var onTrack = new Border();
        Resource(onTrack, Border.BackgroundProperty, "CuiAccentBrush");
        Resource(onTrack, Border.CornerRadiusProperty, "CuiSwitchTrackRadius");

        var surface = new Panel { Children = { offTrack, hoverTrack, onTrack } };
        var track = Name(new Border
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            ClipToBounds = true,
            Child = surface
        }, "PART_Track", scope);
        Resource(track, Control.WidthProperty, "CuiSwitchWidth");
        Resource(track, Control.HeightProperty, "CuiSwitchHeight");

        double Resolve(string key, double fallback) =>
            owner.TryFindResource(key, out var value) && value is double resolved ? resolved : fallback;

        var width = Resolve("CuiSwitchWidth", 40);
        var thumbSize = Resolve("CuiSwitchThumbSize", 18);
        var hPad = Resolve("CuiSwitchPad", 3);
        var vPad = Resolve("CuiSwitchVPad", 2);
        // Travel range of the moving knob = rail width minus the two horizontal pads and the thumb,
        // so the thumb never overflows the rail (off at hPad, on at width - hPad - thumbSize).
        var travel = Math.Max(0, width - (2 * hPad) - thumbSize);

        var switchKnob = Name(new Canvas
        {
            Width = travel,
            Height = thumbSize,
            Margin = new Thickness(hPad, vPad, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, "PART_SwitchKnob", scope);

        var thumb = Name(new Border
        {
            Width = thumbSize,
            Height = thumbSize,
            IsHitTestVisible = false
        }, "PART_Thumb", scope);
        Resource(thumb, Border.BackgroundProperty, "CuiSwitchThumbBrush");
        Resource(thumb, Border.CornerRadiusProperty, "CuiSwitchThumbRadius");

        var movingKnobs = Name(new Panel
        {
            Width = thumbSize,
            Height = thumbSize,
            Children = { thumb }
        }, "PART_MovingKnobs", scope);
        switchKnob.Children.Add(movingKnobs);
        surface.Children.Add(switchKnob);

        void Update()
        {
            var on = owner.IsChecked == true;
            onTrack.IsVisible = on;
            hoverTrack.IsVisible = !on && owner.IsPointerOver && owner.IsEffectivelyEnabled;
        }
        owner.PropertyChanged += (_, change) =>
        {
            if (change.Property == ToggleButton.IsCheckedProperty
                || change.Property.Name is "IsPointerOver" or "IsEnabled" or "IsEffectivelyEnabled")
                Update();
        };
        track.SizeChanged += (_, _) => Update();
        if (string.IsNullOrEmpty(AutomationProperties.GetName(owner)))
            AutomationProperties.SetName(owner, "Toggle switch");
        Update();
        return track;
    }

    private static Control TextInput(TextBox owner, INameScope scope)
    {
        if(owner.ContextMenu is null)
        {
            var menu=new ContextMenu();
            // Route the text-input context menu popup through the shared per-open transparency request seam.
            menu.ConfigurePopup = CuiPopupPresentation.RequestTransparency;
            foreach(var (label,path,action) in new (string,string?,Action)[] { ("Cut","CanCut",owner.Cut),("Copy","CanCopy",owner.Copy),("Paste","CanPaste",owner.Paste),("Select all",null,owner.SelectAll) })
            {
                var item=new CuiMenuItem { Header=label }; if(path is not null) Bind(item,Control.IsEnabledProperty,owner,path);
                item.Click+=(_,_)=>action();menu.Items.Add(item);
            }
            owner.ContextMenu=menu;owner.DetachedFromVisualTree+=(_,_)=>menu.Close();
        }
        var text = Name(new CuiFormattedTextPresenter(), "PART_TextPresenter", scope);
        text.Bind(CuiFormattedTextPresenter.PresentationProperty, owner.GetObservable(CuiFormattedTextInput.PresentationProperty));
        foreach (var (property, path) in new (AvaloniaProperty, string)[]
        {
            (TextPresenter.CaretIndexProperty,"CaretIndex"),(TextPresenter.SelectionStartProperty,"SelectionStart"),
            (TextPresenter.SelectionEndProperty,"SelectionEnd"),(TextPresenter.SelectionBrushProperty,"SelectionBrush"),
            (TextPresenter.SelectionForegroundBrushProperty,"SelectionForegroundBrush"),(TextPresenter.CaretBrushProperty,"CaretBrush"),
            (TextPresenter.TextWrappingProperty,"TextWrapping"),(TextPresenter.TextAlignmentProperty,"TextAlignment"),
            (TextPresenter.PasswordCharProperty,"PasswordChar"),(TextPresenter.RevealPasswordProperty,"RevealPassword"),
            (TextElement.FontFamilyProperty,"FontFamily"),(TextElement.FontSizeProperty,"FontSize"),
            (TextElement.FontWeightProperty,"FontWeight"),(TextElement.ForegroundProperty,"Foreground")
        }) Bind(text, property, owner, path);
        Bind(text, TextPresenter.TextProperty, owner, "Text", true);
        var placeholder = new TextBlock { IsHitTestVisible = false };
        Bind(placeholder, TextBlock.TextProperty, owner, "PlaceholderText");
        Bind(placeholder, TextBlock.ForegroundProperty, owner, "PlaceholderForeground");
        Bind(placeholder, TextBlock.TextWrappingProperty, owner, "TextWrapping");
        void Placeholder() => placeholder.IsVisible = string.IsNullOrEmpty(owner.Text) && string.IsNullOrEmpty(text.PreeditText);
        owner.PropertyChanged += (_, change) => { if (change.Property == TextBox.TextProperty) Placeholder(); };
        text.PropertyChanged += (_, change) => { if (change.Property.Name == "PreeditText") Placeholder(); };
        Placeholder();
        // The presenter and placeholder share one overlay cell. A Grid (not a bare Panel) honours
        // the native TextBox.VerticalContentAlignment through the presenter/placeholder vertical
        // alignment, so a centred single-line field centres its actual text (not just the whole
        // input box). Default Stretch preserves the existing fill/top behaviour for ordinary and
        // multiline inputs; only an authored VerticalContentAlignment changes the placement.
        var surface = new Grid { Children = { placeholder, text }, Cursor = new Cursor(StandardCursorType.Ibeam) };
        Bind(text, Layoutable.VerticalAlignmentProperty, owner, "VerticalContentAlignment");
        Bind(placeholder, Layoutable.VerticalAlignmentProperty, owner, "VerticalContentAlignment");
        var scroll = Name(new ScrollViewer { Content = surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, "PART_ScrollViewer", scope);
        scroll.Bind(ScrollViewer.HorizontalScrollBarVisibilityProperty, owner.GetObservable(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        scroll.Bind(ScrollViewer.VerticalScrollBarVisibilityProperty, owner.GetObservable(ScrollViewer.VerticalScrollBarVisibilityProperty));
        var inner = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto"), Children = { scroll } }; Grid.SetColumn(scroll, 1);
        var left = new ContentPresenter(); Bind(left, ContentPresenter.ContentProperty, owner, "InnerLeftContent"); inner.Children.Add(left);
        var right = new ContentPresenter(); Bind(right, ContentPresenter.ContentProperty, owner, "InnerRightContent"); Grid.SetColumn(right,2); inner.Children.Add(right);
        var invalid = ValidationMarker(owner, scope); Grid.SetColumn(invalid, 3); inner.Children.Add(invalid);
        Bind(inner, Control.MarginProperty, owner, "Padding");
        return Frame(owner, inner);
    }

    private static Control ValidationMarker(Control owner, INameScope scope)
    {
        // A shape cue remains visible when high contrast intentionally shares
        // the same foreground for ordinary borders and invalid outlines.
        var marker = Name(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 8,1 L 15,15 L 1,15 Z M 8,5 L 8,9 M 8,11 L 8,12"),
            Width = 16, Height = 16, Margin = new Thickness(8, 0, 0, 0),
            StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        }, "PART_ValidationIndicator", scope);
        Resource(marker, Avalonia.Controls.Shapes.Shape.StrokeProperty, "CuiValidationBrush");
        marker.Bind(Control.IsVisibleProperty, owner.GetObservable(CuiInputValidationProperties.HasErrorProperty));
        AutomationProperties.SetName(marker, "Invalid value");
        return marker;
    }

    private static Control Scroll(ScrollViewer owner, INameScope scope)
    {
        var root = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("*,Auto") };
        // Keep wheel input inside the presenter even when the pointer is over a gap between children.
        var content = Name(new ScrollContentPresenter { Background = Brushes.Transparent }, "PART_ContentPresenter", scope);
        Bind(content, ContentPresenter.PaddingProperty, owner, "Padding");
        var vertical = Name(new ScrollBar { Orientation = Orientation.Vertical }, "PART_VerticalScrollBar", scope);
        var horizontal = Name(new ScrollBar { Orientation = Orientation.Horizontal }, "PART_HorizontalScrollBar", scope);
        Grid.SetColumn(vertical, 1); Grid.SetRow(horizontal, 1);
        root.Children.Add(content); root.Children.Add(vertical); root.Children.Add(horizontal);
        var gesture = new ScrollGestureRecognizer();
        Bind(gesture, ScrollGestureRecognizer.CanHorizontallyScrollProperty, content, "CanHorizontallyScroll");
        Bind(gesture, ScrollGestureRecognizer.CanVerticallyScrollProperty, content, "CanVerticallyScroll");
        content.GestureRecognizers.Add(gesture);
        return root;
    }

    private static Control ScrollBar(ScrollBar owner, INameScope scope)
    {
        CuiPropertyPresentation.ConfigurePart(owner,CuiPropertyPresentation.TemplatePart.ScrollBar);
        var thumb = new Thumb();CuiPropertyPresentation.ConfigurePart(thumb,CuiPropertyPresentation.TemplatePart.ScrollThumb);
        var track = Name(new Track { Thumb = thumb },"PART_Track",scope);
        Bind(track, Track.MinimumProperty, owner, "Minimum"); Bind(track, Track.MaximumProperty, owner, "Maximum");
        Bind(track, Track.ViewportSizeProperty, owner, "ViewportSize"); Bind(track, Track.ValueProperty, owner, "Value", true);
        Bind(track, Track.OrientationProperty, owner, "Orientation");
        track.Bind(Track.IsDirectionReversedProperty,new Binding("Orientation")
        { Source=owner,Converter=new Avalonia.Data.Converters.FuncValueConverter<Orientation,bool>(orientation=>orientation==Orientation.Vertical) });
        track.DecreaseButton = Name(new RepeatButton { Focusable = false }, "PART_PageUpButton", scope);
        track.IncreaseButton = Name(new RepeatButton { Focusable = false }, "PART_PageDownButton", scope);
        CuiPropertyPresentation.ConfigurePart(track.DecreaseButton,CuiPropertyPresentation.TemplatePart.RailButton);
        CuiPropertyPresentation.ConfigurePart(track.IncreaseButton,CuiPropertyPresentation.TemplatePart.RailButton);
        return track;
    }

    private static Control Slider(Slider owner, INameScope scope)
    {
        var thumb = new Thumb();CuiPropertyPresentation.ConfigurePart(thumb,CuiPropertyPresentation.TemplatePart.SliderThumb);
        var track = Name(new Track { Thumb = thumb }, "PART_Track", scope);
        Bind(track, Track.MinimumProperty, owner, "Minimum"); Bind(track, Track.MaximumProperty, owner, "Maximum");
        Bind(track, Track.ValueProperty, owner, "Value", true);
        Bind(track, Track.OrientationProperty, owner, "Orientation"); Bind(track, Track.IsDirectionReversedProperty, owner, "IsDirectionReversed");
        track.DecreaseButton = Name(new RepeatButton { Focusable = false }, "PART_DecreaseButton", scope);
        track.IncreaseButton = Name(new RepeatButton { Focusable = false }, "PART_IncreaseButton", scope);
        CuiPropertyPresentation.ConfigurePart(track.DecreaseButton,CuiPropertyPresentation.TemplatePart.RailButton);
        CuiPropertyPresentation.ConfigurePart(track.IncreaseButton,CuiPropertyPresentation.TemplatePart.RailButton);
        // The rail is a theme-owned rounded control rail: a fixed-height track (CuiRailHeight,
        // CuiRailRadius) using the same muted track brush as determinate progress. It reads as part
        // of the same control family while rounding at its own fixed height. The Track above still
        // owns all position/value/thumb logic; the rail is purely visual.
        var rail = new Border { VerticalAlignment = VerticalAlignment.Center };
        Resource(rail, Border.HeightProperty, "CuiRailHeight");
        Resource(rail, Border.CornerRadiusProperty, "CuiRailRadius");
        Resource(rail, Border.BackgroundProperty, "CuiProgressTrackBrush");
        return new Panel { Children = { rail, track } };
    }

    private static ItemsPresenter ItemPresenter(ItemsControl owner, INameScope scope)
    {
        var presenter = Name(new ItemsPresenter(), "PART_ItemsPresenter", scope);
        Bind(presenter, ItemsPresenter.ItemsPanelProperty, owner, "ItemsPanel");
        return presenter;
    }
    private static Control Items(ItemsControl owner, INameScope scope) => Frame(owner,
        owner is Menu ? ItemPresenter(owner, scope) : new ScrollViewer { Content = ItemPresenter(owner, scope) });

    private static Border PopupFrame(Control owner, Control content)
    {
        var frame = new CuiMaterialBorder { Child = content };
        // Explicit popup scope borrows the owner's canonical resources, not a donor popup theme.
        frame.Bind(Border.BackgroundProperty, owner.GetResourceObservable("CuiPopupSurfaceBrush"));
        frame.Bind(Border.BorderBrushProperty, owner.GetResourceObservable("CuiPopupBorderBrush"));
        frame.Bind(Border.CornerRadiusProperty, owner.GetResourceObservable("CuiPopupRadius"));
        frame.Bind(Border.PaddingProperty, owner.GetResourceObservable("CuiPopupPadding"));
        frame.Bind(Border.BorderThicknessProperty, owner.GetResourceObservable("CuiControlBorderWidth"));
        frame.Bind(Border.BoxShadowProperty, owner.GetResourceObservable("CuiPopupShadow"));
        // Reserve the rendered shadow's actual extents inside the popup host;
        // otherwise native and browser popup roots clip it to a hard rectangle.
        frame.Bind(Control.MarginProperty, owner.GetResourceObservable("CuiPopupShadowMargin"));
        return frame;
    }

    private static Control Select(ComboBox owner, INameScope scope)
    {
        var row = new Grid { ColumnDefinitions = new("*,Auto") };
        var selected = new ContentPresenter(); Bind(selected, ContentPresenter.ContentProperty, owner, "SelectionBoxItem");
        Bind(selected, ContentPresenter.ContentTemplateProperty, owner, "SelectionBoxItemTemplate");
        Bind(selected, Control.MarginProperty, owner, "Padding"); row.Children.Add(selected);
        var placeholder=new TextBlock();Bind(placeholder,TextBlock.TextProperty,owner,"PlaceholderText");
        Resource(placeholder,TextBlock.ForegroundProperty,"CuiTextSoftBrush");Bind(placeholder,Control.MarginProperty,owner,"Padding");row.Children.Add(placeholder);
        var editable=Name(new TextBox(),"PART_EditableTextBox",scope);Bind(editable,TextBox.TextProperty,owner,"Text",true);
        Bind(editable,TextBox.PlaceholderTextProperty,owner,"PlaceholderText");row.Children.Add(editable);
        void SelectMode() {selected.IsVisible=!owner.IsEditable;editable.IsVisible=owner.IsEditable;placeholder.IsVisible=!owner.IsEditable&&owner.SelectedIndex<0;}
        owner.PropertyChanged+=(_,change)=> { if(change.Property.Name is "IsEditable" or "SelectedIndex") SelectMode(); };SelectMode();
        var chevron = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 1,3 L 6,8 L 11,3"), Width = 12, Height = 12,
            StrokeThickness = 1.5, VerticalAlignment = VerticalAlignment.Center
        };
        Resource(chevron, Avalonia.Controls.Shapes.Shape.StrokeProperty, "CuiTextBrush");
        var arrow = new ToggleButton { Content = chevron, Focusable = false, Width = 28 };
        CuiPropertyPresentation.ConfigurePart(arrow,CuiPropertyPresentation.TemplatePart.DisclosureButton);
        AutomationProperties.SetName(arrow, "Open choices");
        Grid.SetColumn(arrow,1); row.Children.Add(arrow);
        Bind(arrow, ToggleButton.IsCheckedProperty, owner, "IsDropDownOpen", true);
        var popup = Name(new Popup { PlacementTarget = owner, IsLightDismissEnabled = true }, "PART_Popup", scope);
        CuiPopupPresentation.RequestTransparency(popup);
        popup.Bind(Popup.HorizontalOffsetProperty, owner.GetResourceObservable("CuiPopupHorizontalOffset"));
        popup.Bind(Popup.VerticalOffsetProperty, owner.GetResourceObservable("CuiPopupVerticalOffset"));
        Bind(popup, Popup.IsOpenProperty, owner, "IsDropDownOpen", true); Bind(popup, Control.MinWidthProperty, owner, "Bounds.Width");
        Bind(popup, Control.MaxHeightProperty, owner, "MaxDropDownHeight");
        popup.Child = PopupFrame(owner, new ScrollViewer { Content = ItemPresenter(owner, scope) }); row.Children.Add(popup);
        return Frame(owner, row);
    }

    private static Control MenuItem(MenuItem owner, INameScope scope)
    {
        if (!owner.IsTopLevel) CuiPropertyPresentation.ConfigurePart(owner, CuiPropertyPresentation.TemplatePart.FloatingMenuItem);
        var row = new Grid { ColumnDefinitions = new(owner.IsTopLevel ? "*" : "24,*,Auto,12"), ColumnSpacing = owner.IsTopLevel ? 0 : 12 };
        // Padding belongs to the entire row, so icons, labels and shortcuts share
        // a single alignment and target. Nested buttons do not draw extra boxes.
        Bind(row, Control.MarginProperty, owner, "Padding");
        var header = Presenter(owner, scope, "PART_HeaderPresenter", "Header");
        header.Padding = new Thickness(0);
        Grid.SetColumn(header, owner.IsTopLevel ? 0 : 1); row.Children.Add(header);
        var iconImage = new Image { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        Bind(iconImage, Image.SourceProperty, owner, "Icon"); row.Children.Add(iconImage);
        var iconContent = new ContentPresenter(); Bind(iconContent, ContentPresenter.ContentProperty, owner, "Icon"); row.Children.Add(iconContent);
        TextBlock? shortcut = null, arrow = null;
        if (!owner.IsTopLevel)
        {
            shortcut = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            Bind(shortcut, TextBlock.TextProperty, owner, "InputGesture");
            Resource(shortcut, TextBlock.ForegroundProperty, "CuiTextSoftBrush");
            Grid.SetColumn(shortcut, 2); row.Children.Add(shortcut);
            arrow = new TextBlock { Text = "›", IsVisible = owner.Items.Count > 0, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(arrow, 3); row.Children.Add(arrow);
        }
        void UpdateIcon()
        {
            var iconOnly = owner is CuiMenuItem { IconOnly: true };
            var hasImage = owner.Icon is Avalonia.Media.IImage;
            row.ColumnDefinitions = new(owner.IsTopLevel || iconOnly ? "*" : "24,*,Auto,12");
            Grid.SetColumn(header, owner.IsTopLevel || iconOnly ? 0 : 1);
            iconImage.Width = iconImage.Height = iconOnly ? 36 : 20;
            iconImage.IsVisible = hasImage;
            iconContent.IsVisible = !hasImage && owner.Icon is not null;
            header.IsVisible = !(hasImage && (owner.IsTopLevel || iconOnly));
            if (shortcut is not null) shortcut.IsVisible = !iconOnly;
            if (arrow is not null) arrow.IsVisible = !iconOnly && owner.Items.Count > 0;
        }
        owner.PropertyChanged += (_, change) =>
        {
            if (change.Property == Avalonia.Controls.MenuItem.IconProperty || change.Property == CuiMenuItem.IconOnlyProperty) UpdateIcon();
        };
        UpdateIcon();
        var popup = Name(new Popup { PlacementTarget = owner, Placement = owner.IsTopLevel ? PlacementMode.Bottom : PlacementMode.RightEdgeAlignedTop, IsLightDismissEnabled = false }, "PART_Popup", scope);
        CuiPopupPresentation.RequestTransparency(popup);
        popup.Bind(Popup.HorizontalOffsetProperty, owner.GetResourceObservable(owner.IsTopLevel ? "CuiPopupHorizontalOffset" : "CuiSubmenuHorizontalOffset"));
        popup.Bind(Popup.VerticalOffsetProperty, owner.GetResourceObservable(owner.IsTopLevel ? "CuiPopupVerticalOffset" : "CuiSubmenuVerticalOffset"));
        Bind(popup, Popup.IsOpenProperty, owner, "IsSubMenuOpen", true);
        popup.Child = PopupFrame(owner, new ScrollViewer { Content = ItemPresenter(owner, scope) }); row.Children.Add(popup);
        return Frame(owner, row);
    }

    private static Control Tabs(TabControl owner, INameScope scope)
    {
        var root = new Grid { RowDefinitions = new("Auto,*") };
        root.Children.Add(ItemPresenter(owner, scope));
        var content = Name(new ContentPresenter(), "PART_SelectedContentHost", scope); Grid.SetRow(content,1);
        Bind(content, ContentPresenter.ContentProperty, owner, "SelectedContent");
        Bind(content, ContentPresenter.ContentTemplateProperty, owner, "SelectedContentTemplate"); root.Children.Add(content);
        return Frame(owner, root);
    }

    private static Control TreeItem(TreeViewItem owner, INameScope scope)
    {
        var root = new StackPanel();
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var toggle = new ToggleButton { Content = "›", Focusable = false }; Bind(toggle, ToggleButton.IsCheckedProperty, owner, "IsExpanded",true);
        header.Children.Add(toggle); header.Children.Add(Presenter(owner,scope,"PART_HeaderPresenter","Header")); root.Children.Add(header);
        var children = ItemPresenter(owner,scope); children.Margin = new(18,0,0,0); Bind(children, Control.IsVisibleProperty,owner,"IsExpanded"); root.Children.Add(children);
        return Frame(owner,root);
    }

    private static Control Numeric(NumericUpDown owner,INameScope scope)
    {
        var input=Name(new TextBox(),"PART_TextBox",scope); Bind(input,TextBox.TextProperty,owner,"Text",true);
        CuiPropertyPresentation.ConfigurePart(input,CuiPropertyPresentation.TemplatePart.EmbeddedInput);
        input.Bind(AutomationProperties.NameProperty, owner.GetObservable(AutomationProperties.NameProperty));
        input.Bind(AutomationProperties.HelpTextProperty, owner.GetObservable(AutomationProperties.HelpTextProperty));
        Bind(input,TextBox.IsReadOnlyProperty,owner,"IsReadOnly"); Bind(input,TextBox.PlaceholderTextProperty,owner,"PlaceholderText");
        var spinner=Name(new ButtonSpinner { Content=input },"PART_Spinner",scope); Bind(spinner,ButtonSpinner.AllowSpinProperty,owner,"AllowSpin");
        var row = new Grid { ColumnDefinitions = new("*,Auto"), Children = { spinner } };
        var invalid = ValidationMarker(owner, scope); invalid.Margin = new Thickness(8, 0, 8, 0);
        Grid.SetColumn(invalid, 1); row.Children.Add(invalid);
        return Frame(owner, row);
    }
    private static Control Spinner(ButtonSpinner owner,INameScope scope)
    {
        var root=new Grid { ColumnDefinitions=new("*,Auto") }; root.Children.Add(Presenter(owner,scope));
        var buttons=new StackPanel(); Grid.SetColumn(buttons,1);
        Control SpinArrow(bool up)
        {
            var arrow = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(up ? "M 1,7 L 5,3 L 9,7" : "M 1,3 L 5,7 L 9,3"),
                Width = 10, Height = 10, StrokeThickness = 1.5,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            Resource(arrow, Avalonia.Controls.Shapes.Shape.StrokeProperty, "CuiTextBrush");
            return arrow;
        }
        var increase=Name(new RepeatButton { Content=SpinArrow(true),Focusable=false },"PART_IncreaseButton",scope);
        var decrease=Name(new RepeatButton { Content=SpinArrow(false),Focusable=false },"PART_DecreaseButton",scope);
        AutomationProperties.SetName(increase,"Increase value"); AutomationProperties.SetName(decrease,"Decrease value");
        CuiPropertyPresentation.ConfigurePart(increase,CuiPropertyPresentation.TemplatePart.SpinnerButton);
        CuiPropertyPresentation.ConfigurePart(decrease,CuiPropertyPresentation.TemplatePart.SpinnerButton);
        buttons.Children.Add(increase);buttons.Children.Add(decrease);root.Children.Add(buttons);return Frame(owner,root);
    }
    /// <summary>
    /// CUI-owned read-only determinate progress bar. The rail fill is theme-owned and the native
    /// ProgressBar automation peer exposes the determinate RangeValue (minimum/maximum/value,
    /// read-only). The backend RangeBase coercion is the source of truth for invalid numbers: a
    /// non-finite value is refused and the prior/default value is retained, and Maximum is forced
    /// >= Minimum. The template therefore paints a fill only for a finite, non-degenerate span and
    /// paints no fill (unknown) otherwise — it never fabricates a bar from a coerced unknown.
    /// </summary>
    private static Control Progress(ProgressBar owner,INameScope scope)
    {
        var progress=Name(new Border { HorizontalAlignment=HorizontalAlignment.Left, IsHitTestVisible=false },"PART_Indicator",scope);
        Resource(progress,Border.BackgroundProperty,"CuiAccentBrush");
        var track=new Border { ClipToBounds=true };
        Resource(track,Border.BackgroundProperty,"CuiProgressTrackBrush");
        track.Child=progress;
        // Round the ends fully and proportionately to the actual bar height, so a tall bar and a
        // short bar both get true full-round ends (a fixed radius tied to another control's size
        // would mis-round taller bars). Before layout the theme token is used as a fallback; once a
        // real size is known the live half-height radius wins, so the author-visible default and the
        // rendered geometry stay coherent.
        void RoundEnds()
        {
            var height = track.Bounds.Height;
            track.CornerRadius = height > 0
                ? new CornerRadius(height / 2)
                : owner.TryFindResource("CuiProgressTrackRadius", out var token) && token is CornerRadius fallback
                    ? fallback : new CornerRadius(0);
        }
        // The native ProgressBar peer reports the determinate range; keep that semantics truthful and
        // read-only (no write-back is offered for Progress). Only add a generic fallback description
        // when the author did not supply one, so an authored AccessibleDescription is never overwritten.
        if (string.IsNullOrEmpty(AutomationProperties.GetHelpText(owner)))
            AutomationProperties.SetHelpText(owner, "Usage progress (read-only)");
        void Update()
        {
            var span=owner.Maximum-owner.Minimum;
            var valid=double.IsFinite(owner.Value)&&double.IsFinite(owner.Minimum)&&double.IsFinite(owner.Maximum)&&span>0;
            var ratio=valid?Math.Clamp((owner.Value-owner.Minimum)/span,0d,1d):0d;
            var desired=track.Bounds.Width*ratio;
            if(!double.IsFinite(desired)) desired=0;
            progress.Width=desired;
            progress.IsVisible=valid&&desired>0;
        }
        owner.PropertyChanged+=(_,change)=>
        {
            if(change.Property==ProgressBar.ValueProperty||change.Property==ProgressBar.MinimumProperty||change.Property==ProgressBar.MaximumProperty) Update();
        };
        track.SizeChanged+=(_,_)=>{ RoundEnds(); Update(); };
        Update();
        return track;
    }
    private static Control Separator(Separator owner,INameScope scope)
    { var line=new Border { Height=1,Margin=new(4) }; Resource(line,Border.BackgroundProperty,"CuiLineBrush"); return line; }
    private static Control Expander(Expander owner,INameScope scope)
    {
        var stack=new StackPanel(); var header=new ToggleButton(); Bind(header,ContentControl.ContentProperty,owner,"Header"); Bind(header,ToggleButton.IsCheckedProperty,owner,"IsExpanded",true);
        stack.Children.Add(header); var content=Presenter(owner,scope); Bind(content,Control.IsVisibleProperty,owner,"IsExpanded"); stack.Children.Add(content); return Frame(owner,stack);
    }
}
