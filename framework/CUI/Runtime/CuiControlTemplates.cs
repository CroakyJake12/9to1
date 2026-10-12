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
    public static void Populate(IResourceDictionary resources)
    { foreach(var entry in Catalogue.Value) resources[entry.Key]=entry.Value; }
    public static bool TryGet(Type backendType,out IControlTemplate? template)
    {
        if(Catalogue.Value.TryGetValue(backendType,out var value) && value is ControlTheme theme && theme.Setters.SingleOrDefault() is Setter setter && setter.Value is IControlTemplate found)
        { template=found;return true; }
        template=null;return false;
    }
    private static ResourceDictionary CreateCatalogue()
    {
        var resources=new ResourceDictionary();
        Add<Button>(resources, Button); Add<RepeatButton>(resources, Button); Add<ToggleButton>(resources, Button);
        Add<CheckBox>(resources, Choice); Add<RadioButton>(resources, Choice); Add<ToggleSwitch>(resources, Choice);
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
        Add<TreeViewItem>(resources, TreeItem); Add<Menu>(resources, Items); Add<ContextMenu>(resources, Items);
        Add<MenuItem>(resources, MenuItem); Add<TabControl>(resources, Tabs); Add<TabItem>(resources, Header);
        Add<CuiMenuItem>(resources, (owner, scope) => MenuItem(owner, scope));
        Add<ContentControl>(resources, Content); Add<UserControl>(resources, Content); Add<ToolTip>(resources, Content);
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
        var frame = new Border { Child = content };
        Bind(frame, Border.BackgroundProperty, owner, "Background"); Bind(frame, Border.BorderBrushProperty, owner, "BorderBrush");
        Bind(frame, Border.BorderThicknessProperty, owner, "BorderThickness"); Bind(frame, Border.CornerRadiusProperty, owner, "CornerRadius");
        return frame;
    }
    private static ContentPresenter Presenter(TemplatedControl owner, INameScope scope, string name = "PART_ContentPresenter", string property = "Content")
    {
        var presenter = Name(new ContentPresenter(), name, scope);
        Bind(presenter, ContentPresenter.ContentProperty, owner, property);
        Bind(presenter, ContentPresenter.ContentTemplateProperty, owner, property == "Header" ? "HeaderTemplate" : "ContentTemplate");
        Bind(presenter, ContentPresenter.PaddingProperty, owner, "Padding");
        Bind(presenter, ContentPresenter.HorizontalContentAlignmentProperty, owner, "HorizontalContentAlignment");
        Bind(presenter, ContentPresenter.VerticalContentAlignmentProperty, owner, "VerticalContentAlignment");
        return presenter;
    }

    private static Control Button(Button owner, INameScope scope) => Frame(owner, Presenter(owner, scope));
    private static Control Content(ContentControl owner, INameScope scope) => Frame(owner, Presenter(owner, scope));
    private static Control Header(HeaderedContentControl owner, INameScope scope) => Frame(owner, Presenter(owner, scope, property: "Header"));

    private static Control TopLevelContent(TopLevel owner, INameScope scope)
    {
        // Popups, menu light-dismiss and text selection require the native root
        // layers even when all visible chrome comes from CUI-owned templates.
        var layers = Name(new VisualLayerManager { Child = Presenter(owner, scope) }, "PART_VisualLayerManager", scope);
        var fallback = Name(new Border { IsHitTestVisible = false }, "PART_TransparencyFallback", scope);
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
        var presenter = Presenter(owner, scope); Grid.SetColumn(presenter, 1);
        row.Children.Add(box); row.Children.Add(presenter);
        return row;
    }

    private static Control TextInput(TextBox owner, INameScope scope)
    {
        if(owner.ContextMenu is null)
        {
            var menu=new ContextMenu();
            foreach(var (label,path,action) in new (string,string?,Action)[] { ("Cut","CanCut",owner.Cut),("Copy","CanCopy",owner.Copy),("Paste","CanPaste",owner.Paste),("Select all",null,owner.SelectAll) })
            {
                var item=new CuiMenuItem { Header=label }; if(path is not null) Bind(item,Control.IsEnabledProperty,owner,path);
                item.Click+=(_,_)=>action();menu.Items.Add(item);
            }
            owner.ContextMenu=menu;owner.DetachedFromVisualTree+=(_,_)=>menu.Close();
        }
        var text = Name(new TextPresenter(), "PART_TextPresenter", scope);
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
        var surface = new Panel { Children = { placeholder, text }, Cursor = new Cursor(StandardCursorType.Ibeam) };
        var scroll = Name(new ScrollViewer { Content = surface, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, "PART_ScrollViewer", scope);
        scroll.Bind(ScrollViewer.HorizontalScrollBarVisibilityProperty, owner.GetObservable(ScrollViewer.HorizontalScrollBarVisibilityProperty));
        scroll.Bind(ScrollViewer.VerticalScrollBarVisibilityProperty, owner.GetObservable(ScrollViewer.VerticalScrollBarVisibilityProperty));
        var inner = new Grid { ColumnDefinitions = new("Auto,*,Auto"), Children = { scroll } }; Grid.SetColumn(scroll, 1);
        var left = new ContentPresenter(); Bind(left, ContentPresenter.ContentProperty, owner, "InnerLeftContent"); inner.Children.Add(left);
        var right = new ContentPresenter(); Bind(right, ContentPresenter.ContentProperty, owner, "InnerRightContent"); Grid.SetColumn(right,2); inner.Children.Add(right);
        Bind(inner, Control.MarginProperty, owner, "Padding");
        return Frame(owner, inner);
    }

    private static Control Scroll(ScrollViewer owner, INameScope scope)
    {
        var root = new Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("*,Auto") };
        var content = Name(new ScrollContentPresenter(), "PART_ContentPresenter", scope);
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
        Bind(track, Track.OrientationProperty, owner, "Orientation"); Bind(track, Track.IsDirectionReversedProperty, owner, "IsDirectionReversed");
        track.DecreaseButton = Name(new RepeatButton { Focusable = false }, "PART_DecreaseButton", scope);
        track.IncreaseButton = Name(new RepeatButton { Focusable = false }, "PART_IncreaseButton", scope);
        CuiPropertyPresentation.ConfigurePart(track.DecreaseButton,CuiPropertyPresentation.TemplatePart.RailButton);
        CuiPropertyPresentation.ConfigurePart(track.IncreaseButton,CuiPropertyPresentation.TemplatePart.RailButton);
        var line = new Border { Height = 4, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new(2) };
        Resource(line, Border.BackgroundProperty, "CuiLineStrongBrush");
        return new Panel { Children = { line, track } };
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
        var frame = new Border { Child = content, Padding = new(6), BorderThickness = new(1) };
        // Explicit popup scope borrows the owner's canonical resources, not a donor popup theme.
        frame.Bind(Border.BackgroundProperty, owner.GetResourceObservable("CuiPanelBrush"));
        frame.Bind(Border.BorderBrushProperty, owner.GetResourceObservable("CuiLineBrush"));
        frame.Bind(Border.CornerRadiusProperty, owner.GetResourceObservable("CuiPopupRadius"));
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
        AutomationProperties.SetName(arrow, "Open choices");
        Grid.SetColumn(arrow,1); row.Children.Add(arrow);
        Bind(arrow, ToggleButton.IsCheckedProperty, owner, "IsDropDownOpen", true);
        var popup = Name(new Popup { PlacementTarget = owner, IsLightDismissEnabled = true }, "PART_Popup", scope);
        Bind(popup, Popup.IsOpenProperty, owner, "IsDropDownOpen", true); Bind(popup, Control.MinWidthProperty, owner, "Bounds.Width");
        Bind(popup, Control.MaxHeightProperty, owner, "MaxDropDownHeight");
        popup.Child = PopupFrame(owner, new ScrollViewer { Content = ItemPresenter(owner, scope) }); row.Children.Add(popup);
        return Frame(owner, row);
    }

    private static Control MenuItem(MenuItem owner, INameScope scope)
    {
        var row = new Grid { ColumnDefinitions = new(owner.IsTopLevel ? "*" : "Auto,*,Auto,Auto"), ColumnSpacing = owner.IsTopLevel ? 0 : 8 };
        var header = Presenter(owner, scope, "PART_HeaderPresenter", "Header");
        Grid.SetColumn(header, owner.IsTopLevel ? 0 : 1); row.Children.Add(header);
        if (!owner.IsTopLevel)
        {
            var icon = new ContentPresenter(); Bind(icon, ContentPresenter.ContentProperty, owner, "Icon"); row.Children.Add(icon);
            var shortcut = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            Bind(shortcut, TextBlock.TextProperty, owner, "InputGesture");
            Resource(shortcut, TextBlock.ForegroundProperty, "CuiTextSoftBrush");
            Grid.SetColumn(shortcut, 2); row.Children.Add(shortcut);
            var arrow = new TextBlock { Text = "›", IsVisible = owner.Items.Count > 0, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(arrow, 3); row.Children.Add(arrow);
        }
        var popup = Name(new Popup { PlacementTarget = owner, Placement = owner.IsTopLevel ? PlacementMode.Bottom : PlacementMode.RightEdgeAlignedTop, IsLightDismissEnabled = false }, "PART_Popup", scope);
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
        input.Bind(AutomationProperties.NameProperty, owner.GetObservable(AutomationProperties.NameProperty));
        input.Bind(AutomationProperties.HelpTextProperty, owner.GetObservable(AutomationProperties.HelpTextProperty));
        Bind(input,TextBox.IsReadOnlyProperty,owner,"IsReadOnly"); Bind(input,TextBox.PlaceholderTextProperty,owner,"PlaceholderText");
        var spinner=Name(new ButtonSpinner { Content=input },"PART_Spinner",scope); Bind(spinner,ButtonSpinner.AllowSpinProperty,owner,"AllowSpin");
        return spinner;
    }
    private static Control Spinner(ButtonSpinner owner,INameScope scope)
    {
        var root=new Grid { ColumnDefinitions=new("*,Auto") }; root.Children.Add(Presenter(owner,scope));
        var buttons=new StackPanel(); Grid.SetColumn(buttons,1);
        var increase=Name(new RepeatButton { Content="+",Focusable=false },"PART_IncreaseButton",scope);
        var decrease=Name(new RepeatButton { Content="−",Focusable=false },"PART_DecreaseButton",scope);
        CuiPropertyPresentation.ConfigurePart(increase,CuiPropertyPresentation.TemplatePart.SpinnerButton);
        CuiPropertyPresentation.ConfigurePart(decrease,CuiPropertyPresentation.TemplatePart.SpinnerButton);
        buttons.Children.Add(increase);buttons.Children.Add(decrease);root.Children.Add(buttons);return Frame(owner,root);
    }
    private static Control Progress(ProgressBar owner,INameScope scope)
    {
        var progress=Name(new Border { HorizontalAlignment=HorizontalAlignment.Left },"PART_Indicator",scope);
        Resource(progress,Border.BackgroundProperty,"CuiAccentBrush");
        var track=Frame(owner,progress);
        void Update() => progress.Width=track.Bounds.Width*Math.Clamp((owner.Value-owner.Minimum)/Math.Max(1,owner.Maximum-owner.Minimum),0,1);
        owner.PropertyChanged+=(_,_)=>Update(); track.SizeChanged+=(_,_)=>Update(); return track;
    }
    private static Control Separator(Separator owner,INameScope scope)
    { var line=new Border { Height=1,Margin=new(4) }; Resource(line,Border.BackgroundProperty,"CuiLineBrush"); return line; }
    private static Control Expander(Expander owner,INameScope scope)
    {
        var stack=new StackPanel(); var header=new ToggleButton(); Bind(header,ContentControl.ContentProperty,owner,"Header"); Bind(header,ToggleButton.IsCheckedProperty,owner,"IsExpanded",true);
        stack.Children.Add(header); var content=Presenter(owner,scope); Bind(content,Control.IsVisibleProperty,owner,"IsExpanded"); stack.Children.Add(content); return Frame(owner,stack);
    }
}
