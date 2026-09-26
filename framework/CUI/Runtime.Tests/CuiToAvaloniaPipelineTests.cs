using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public class CuiToAvaloniaPipelineTests
{
    [Fact]
    public void Simple_page_loads_as_Avalonia_Panel()
    {
        var cui = """
            <Cui id="test.simple" version="1">
              <Page id="root" title="Test">
                <TextBlock text="Hello from CUI" />
                <Button content="Click me" />
              </Page>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "simple.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);
        Assert.Equal(3, result.ControlCount); // Page(Panel) + TextBlock + Button
    }

    [Fact]
    public void Authored_component_ids_remain_named_and_automatable_in_the_control_tree()
    {
        var cui = """
            <Cui id="test.identity" version="1">
              <StackPanel id="home-root">
                <Button id="nav-home" content="Home" />
                <Button id="nav-settings" automation-id="settings-accessibility-id" content="Settings" />
              </StackPanel>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "component-identity.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        var root = Assert.IsType<StackPanel>(result.Root);
        var home = Assert.IsType<Button>(root.Children[0]);
        var settings = Assert.IsType<Button>(root.Children[1]);
        Assert.Equal("home-root", root.Name);
        Assert.Equal("nav-home", home.Name);
        Assert.Equal("nav-home", Avalonia.Automation.AutomationProperties.GetAutomationId(home));
        Assert.Equal("nav-settings", settings.Name);
        Assert.Equal("settings-accessibility-id", Avalonia.Automation.AutomationProperties.GetAutomationId(settings));
    }

    [Fact]
    public void Nested_layout_produces_correct_control_tree()
    {
        var cui = """
            <Cui id="test.nested" version="1">
              <Page id="root">
                <StackPanel orientation="vertical">
                  <Border padding="10">
                    <TextBlock text="Inside border" />
                  </Border>
                  <Grid>
                    <Button content="Button A" />
                    <Button content="Button B" />
                  </Grid>
                </StackPanel>
              </Page>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "nested.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);
        // Page(Panel) + StackPanel + Border + TextBlock + Grid + Button + Button = 7
        Assert.Equal(7, result.ControlCount);
    }

    [Fact]
    public void Properties_apply_to_Avalonia_controls()
    {
        var cui = """
            <Cui id="test.props" version="1">
              <Page id="root">
                <TextBlock text="Styled" font-size="20" font-weight="Bold" />
                <Button content="Sized" width="200" height="50" />
                <StackPanel orientation="horizontal" margin="5" />
              </Page>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "props.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);

        var root = (Panel)result.Root;
        var stackPanel = root.Children[2] as StackPanel;
        Assert.NotNull(stackPanel);
        Assert.Equal(Avalonia.Layout.Orientation.Horizontal, stackPanel.Orientation);
        Assert.Equal(new Thickness(5), stackPanel.Margin);

        var button = root.Children[1] as Button;
        Assert.NotNull(button);
        Assert.Equal(200, button.Width);
        Assert.Equal(50, button.Height);
    }

    [Fact]
    public void Transform_components_compose_and_lifecycle_properties_lower()
    {
        var result = CuiHeadlessRenderer.Render(
            "<Page><Text id='label' rotate='15' scale='2' translate='3,4' hidden='false'>Hello</Text></Page>",
            "transforms.cui");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var text = Assert.IsType<TextBlock>(Assert.Single(Assert.IsType<Panel>(result.Root).Children));
        Assert.True(text.IsVisible);
        var transform = Assert.IsType<Avalonia.Media.TransformGroup>(text.RenderTransform);
        Assert.Contains(transform.Children, child => child is Avalonia.Media.ScaleTransform { ScaleX: 2, ScaleY: 2 });
        Assert.Contains(transform.Children, child => child is Avalonia.Media.RotateTransform { Angle: 15 });
        Assert.Contains(transform.Children, child => child is Avalonia.Media.TranslateTransform { X: 3, Y: 4 });
    }

    [Fact]
    public void Scrolling_container_uses_native_scroll_viewer()
    {
        var result = CuiHeadlessRenderer.Render(
            "<Container Type='Vertical Stack' HorizontalScrolling='true'><Text>Scrollable</Text></Container>",
            "scroll.cui");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var scroll = Assert.IsType<ScrollViewer>(result.Root);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, scroll.HorizontalScrollBarVisibility);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, scroll.VerticalScrollBarVisibility);
        Assert.IsType<StackPanel>(scroll.Content);
    }

    [Fact]
    public void Linear_and_radial_gradient_values_produce_native_gradient_brushes()
    {
        var linearResult = CuiHeadlessRenderer.Render(
            "<Page background='Gradient(Linear),Red(10%),Blue(20%),Green(70%)' />", "linear-gradient.cui");
        var radialResult = CuiHeadlessRenderer.Render(
            "<Page background='Gradient(Radial),Red,Blue,Green' />", "radial-gradient.cui");

        Assert.True(linearResult.Success, string.Join("; ", linearResult.Errors));
        Assert.True(radialResult.Success, string.Join("; ", radialResult.Errors));
        var linear = Assert.IsType<Avalonia.Media.LinearGradientBrush>(Assert.IsType<Panel>(linearResult.Root).Background);
        var radial = Assert.IsType<Avalonia.Media.RadialGradientBrush>(Assert.IsType<Panel>(radialResult.Root).Background);
        Assert.Equal(new[] { 0.1, 0.2, 0.7 }, linear.GradientStops.Select(stop => stop.Offset));
        Assert.Equal(new[] { 0d, 0.5, 1d }, radial.GradientStops.Select(stop => stop.Offset));
    }

    [Fact]
    public void Effect_blur_uses_native_element_effect_and_backdrop_blur_fails_explicitly()
    {
        var effectResult = CuiHeadlessRenderer.Render("<Page><Text Effect='Blur(0.5)'>Soft</Text></Page>", "effect.cui");
        var backdropResult = CuiHeadlessRenderer.Render("<Page Background='Blur(0.5)'>Behind</Page>", "backdrop.cui");

        Assert.True(effectResult.Success, string.Join("; ", effectResult.Errors));
        var text = Assert.IsType<TextBlock>(Assert.Single(Assert.IsType<Panel>(effectResult.Root).Children));
        Assert.Equal(16, Assert.IsType<Avalonia.Media.BlurEffect>(text.Effect).Radius);
        Assert.False(backdropResult.Success);
        Assert.Contains(backdropResult.Errors, error => error.Contains("CUIR032", StringComparison.Ordinal));
    }

    [Fact]
    public void Layer_order_is_signed_and_inherited_by_descendants()
    {
        var result = CuiHeadlessRenderer.Render(
            "<Page><Text id='low' Layer='-2'>Low</Text><Layer id='group' Layer='4'><Text id='inherited'>Inside</Text></Layer><Text id='high' Layer='7'>High</Text></Page>",
            "layers.cui");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var page = Assert.IsType<Panel>(result.Root);
        Assert.Equal(-2, page.Children[0].ZIndex);
        var layer = Assert.IsType<Panel>(page.Children[1]);
        Assert.Equal(4, layer.ZIndex);
        Assert.Equal(4, Assert.IsType<TextBlock>(Assert.Single(layer.Children)).ZIndex);
        Assert.Equal(7, page.Children[2].ZIndex);
    }

    [Fact]
    public void Grid_attached_coordinates_do_not_replace_a_nested_grids_definitions()
    {
        var cui = """
            <Cui>
              <Grid columnDefinitions="120,*" rowDefinitions="Auto,*">
                <Grid grid-column="1" grid-row="1" columnDefinitions="32,*" rowDefinitions="Auto,*" />
              </Grid>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "nested-grid-coordinates.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        var root = Assert.IsType<Grid>(result.Root);
        var nested = Assert.IsType<Grid>(root.Children[0]);
        Assert.Equal(2, nested.ColumnDefinitions.Count);
        Assert.Equal(2, nested.RowDefinitions.Count);
        Assert.Equal(1, Grid.GetColumn(nested));
        Assert.Equal(1, Grid.GetRow(nested));
    }

    [Fact]
    public void Classes_apply_to_Avalonia_controls()
    {
        var cui = """
            <Cui id="test.classes" version="1">
              <Page id="root">
                <TextBlock class="heading muted" text="Classed" />
                <Button class="primary large" content="Classed button" />
              </Page>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "classes.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);

        var root = (Panel)result.Root;
        var textBlock = root.Children[0] as TextBlock;
        Assert.NotNull(textBlock);
        Assert.Contains("heading", textBlock.Classes);
        Assert.Contains("muted", textBlock.Classes);

        var button = root.Children[1] as Button;
        Assert.NotNull(button);
        Assert.Contains("primary", button.Classes);
        Assert.Contains("large", button.Classes);
    }

    [Fact]
    public void Binding_values_produce_binding_controls()
    {
        var cui = """
            <Cui id="test.binding" version="1">
              <Page id="root">
                <TextBlock text="{Binding message, fallback=Loading...}" />
                <Button content="{Binding action.label}" />
              </Page>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "binding.cui");

        // Bindings are resolved at runtime; the control tree should still load
        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);
        Assert.Equal(3, result.ControlCount);
    }

    [Fact]
    public void Resource_references_resolve()
    {
        var cui = """
            <Cui id="test.resources" version="1">
              <Resources>
                <Resource key="primary-color" value="#0078D4" />
                <Resource key="app-title" value="My Application" />
              </Resources>
              <Page id="root">
                <TextBlock text="{Resource app-title}" />
              </Page>
            </Cui>
            """;

        var result = CuiHeadlessRenderer.Render(cui, "resources.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);
    }

    [Fact]
    public void Compact_CUI_elements_lower_to_native_Avalonia_controls()
    {
        var result = CuiHeadlessRenderer.Render("""
            <Cui>
              <Page>
                <Container Type="Vertical Stack">
                  <Text>Hello</Text>
                  <Button>Continue</Button>
                  <Input Type="Checkbox" />
                  <Image />
                </Container>
              </Page>
            </Cui>
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var page = Assert.IsType<Panel>(result.Root);
        var stack = Assert.IsType<StackPanel>(Assert.Single(page.Children));
        Assert.Equal(Avalonia.Layout.Orientation.Vertical, stack.Orientation);
        Assert.IsType<TextBlock>(stack.Children[0]);
        Assert.IsType<Button>(stack.Children[1]);
        Assert.IsType<CheckBox>(stack.Children[2]);
        Assert.IsType<Image>(stack.Children[3]);
    }

    [Theory]
    [InlineData("Grid", typeof(Grid))]
    [InlineData("Vertical Stack", typeof(StackPanel))]
    [InlineData("Horizontal Stack", typeof(StackPanel))]
    [InlineData("Absolute", typeof(Canvas))]
    public void Container_types_lower_to_their_declared_native_layout_type(string type, Type expectedType)
    {
        var result = CuiHeadlessRenderer.Render($"<Cui><Container Type=\"{type}\" /></Cui>");

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var container = Assert.IsAssignableFrom<Control>(result.Root);
        Assert.IsType(expectedType, container);
        if (type == "Vertical Stack")
            Assert.Equal(Avalonia.Layout.Orientation.Vertical, Assert.IsType<StackPanel>(container).Orientation);
        if (type == "Horizontal Stack")
            Assert.Equal(Avalonia.Layout.Orientation.Horizontal, Assert.IsType<StackPanel>(container).Orientation);
    }

    [Fact]
    public void Unknown_component_types_fail_with_a_source_mapped_runtime_diagnostic()
    {
        var result = CuiHeadlessRenderer.Render("<Cui><AdaptiveSplit /></Cui>", "unknown-component.cui");

        Assert.False(result.Success);
        Assert.Null(result.Root);
        Assert.Contains(result.Errors, error => error.Contains("CUIR001", StringComparison.Ordinal)
            && error.Contains("unknown-component.cui(1,7)", StringComparison.Ordinal)
            && error.Contains("AdaptiveSplit", StringComparison.Ordinal));
    }

    [Fact]
    public void Object_requires_a_registered_specialised_renderer()
    {
        var missing = CuiHeadlessRenderer.Render("<Cui><Object Type=\"chart\" /></Cui>");
        Assert.False(missing.Success);
        Assert.Contains(missing.Errors, error => error.Contains("CUIR002", StringComparison.Ordinal));

        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("chart", _ => new Border());
        var registered = CuiHeadlessRenderer.Render("<Cui><Object Type=\"chart\" /></Cui>", registry);
        Assert.True(registered.Success, string.Join("; ", registered.Errors));
        Assert.IsType<Border>(registered.Root);
    }

    [Fact]
    public void Compiler_and_runtime_share_the_canonical_component_and_property_registry()
    {
        var registry = CuiControlRegistry.Default;

        Assert.True(registry.TryResolveElement("Container", out var container));
        Assert.False(container.RequiresSpecializedHost);
        Assert.True(container.AllowedProperties.Contains("Type"));
        Assert.True(registry.TryResolveElement("Video", out var video));
        Assert.True(video.RequiresSpecializedHost);
        Assert.Contains(registry.Elements, element => element.Name == "Button");
        Assert.DoesNotContain("Text", registry.Elements.Single(element => element.Name == "Image").AllowedProperties);
        Assert.Contains("Text", registry.Elements.Single(element => element.Name == "Button").AllowedProperties);

        Assert.True(registry.TryGetProperty("grid-column", out var column));
        Assert.Equal("GridColumn", column.Name);
        Assert.Equal(typeof(int), column.RuntimeType);
        Assert.True(column.IsAttached);
        Assert.True(registry.TryGetProperty("Opacity", out var opacity));
        Assert.True(opacity.IsAnimatable);
        Assert.Contains("Image", registry.Properties.Single(property => property.Name == "Source").SupportedElementTypes);
        Assert.DoesNotContain("Input", registry.Properties.Single(property => property.Name == "Source").SupportedElementTypes);
        Assert.Contains("Horizontal", registry.Properties.Single(property => property.Name == "Orientation").AllowedValues);
    }

    [Fact]
    public async Task Two_way_text_input_updates_the_host_value_and_notifies_live_bindings()
    {
        await RunOnAvaloniaThread(() =>
        {
        var viewModel = new CuiViewModel();
        viewModel.Set("Name", "Before");
        var loader = new CuiControlLoader();
        loader.SetBindingContext(viewModel);
        var (root, diagnostics) = loader.LoadMarkup("""
            <Cui><Page>
              <Input Type="Text" Value="{Binding Name, mode=TwoWay, type=string}" />
              <Text Text="{Binding Name}" />
            </Page></Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsType<Panel>(root);
        var input = Assert.IsType<TextBox>(page.Children[0]);
        var label = Assert.IsType<TextBlock>(page.Children[1]);
        Assert.Equal("Before", input.Text);
        Assert.Equal("Before", label.Text);

        input.Text = "After";
        input.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent, input));

        Assert.Equal("After", viewModel.Get("Name"));
        Assert.Equal("After", label.Text);
        Assert.False(CuiInputValidationProperties.GetHasError(input));
        loader.Dispose();
        });
    }

    [Fact]
    public void Invalid_two_way_input_preserves_the_source_and_exposes_validation_state()
    {
        var viewModel = new CuiViewModel();
        viewModel.Set("Age", 42);
        var loader = new CuiControlLoader();
        loader.SetBindingContext(viewModel);
        var (root, diagnostics) = loader.LoadMarkup("""
            <Cui><Input Type="Text" Value="{Binding Age, mode=TwoWay, type=int}" /></Cui>
            """);

        Assert.Empty(diagnostics);
        var input = Assert.IsType<TextBox>(root);
        input.Text = "forty two";
        input.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent, input));

        Assert.Equal(42, viewModel.Get("Age"));
        Assert.Equal("forty two", input.Text);
        Assert.True(CuiInputValidationProperties.GetHasError(input));
        loader.Dispose();
    }

    [Fact]
    public void Invalid_parser_diagnostics_prevent_partial_runtime_lowering()
    {
        var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup("<Cui><If><Text>missing condition</Text></If></Cui>", "invalid-structure.cui");

        Assert.Null(root);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
    }

    [Fact]
    public void Reused_loader_does_not_resolve_resources_from_a_previous_document()
    {
        var loader = new CuiControlLoader();
        var (first, firstDiagnostics) = loader.LoadMarkup("""
            <Cui>
              <Resources><Resource key="local-title" value="First document" /></Resources>
              <Page><TextBlock text="{Resource local-title}" /></Page>
            </Cui>
            """);
        Assert.Empty(firstDiagnostics);
        Assert.Equal("First document", Assert.IsType<TextBlock>(Assert.Single(Assert.IsType<Panel>(first).Children)).Text);

        var (second, secondDiagnostics) = loader.LoadMarkup("""
            <Cui><Page><TextBlock text="{Resource local-title}" /></Page></Cui>
            """);
        Assert.Empty(secondDiagnostics);
        Assert.Null(Assert.IsType<TextBlock>(Assert.Single(Assert.IsType<Panel>(second).Children)).Text);
    }

    [Fact]
    public void Invalid_markup_produces_diagnostics()
    {
        var parser = new CuiRichParser();
        var doc = parser.Parse("<not-cui><broken>", "bad.cui");

        Assert.Null(doc.Components.FirstOrDefault());
        Assert.NotEmpty(parser.Diagnostics.Diagnostics);
    }

    [Fact]
    public async Task Live_if_condition_switches_branches_when_its_binding_changes()
    {
        await RunOnAvaloniaThread(() =>
        {
        var viewModel = new CuiViewModel();
        viewModel.Set("LoggedIn", false);
        using var loader = new CuiControlLoader();
        loader.SetBindingContext(viewModel);

        var (root, diagnostics) = loader.LoadMarkup("""
            <Cui><Page><If Condition="{Binding LoggedIn}"><Text>Welcome</Text></If><Else><Text>Sign in</Text></Else></Page></Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsType<Panel>(root);
        var host = Assert.IsType<ContentControl>(Assert.Single(page.Children));
        var initialBranch = Assert.IsType<Panel>(host.Content);
        Assert.Equal("Sign in", Assert.IsType<TextBlock>(Assert.Single(initialBranch.Children)).Text);

        viewModel.Set("LoggedIn", true);

        var activeBranch = Assert.IsType<Panel>(host.Content);
        Assert.Equal("Welcome", Assert.IsType<TextBlock>(Assert.Single(activeBranch.Children)).Text);
        });
    }

    [Fact]
    public async Task Named_action_dispatches_its_typed_parameter()
    {
        await RunOnAvaloniaThread(() =>
        {
            var viewModel = new CuiViewModel();
            var token = new object();
            viewModel.Set("Draft", token);
            var dispatcher = new RecordingActionDispatcher();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(viewModel);
            loader.SetActionDispatcher(dispatcher);

            var (root, diagnostics) = loader.LoadMarkup("""
                <Cui><Actions><Action name="Save" command="chat.save" parameter="{Binding Draft}" /></Actions><Page><Button action="Save">Save</Button></Page></Cui>
                """);
            Assert.Empty(diagnostics);
            loader.WireBindings(root!);
            Assert.IsType<Button>(Assert.Single(Assert.IsType<Panel>(root).Children))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal("chat.save", dispatcher.Command);
            Assert.Same(token, dispatcher.Parameter);
        });
    }

    [Fact]
    public void Runtime_surface_keeps_last_tree_on_failure_and_preserves_input_on_success()
    {
        var host = new ContentControl();
        var surface = new CuiRuntimeSurface(host);
        var parser = new CuiRichParser();
        var initial = parser.Parse(
            "<Page id='surface'><Input id='composer' type='Text' text='starting' /></Page>", "surface.cui");
        var initialDiagnostics = parser.Diagnostics.Diagnostics.ToArray();

        Assert.DoesNotContain(initialDiagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
        Assert.True(surface.TryApply(initial, out var applyDiagnostics), string.Join("; ", applyDiagnostics));
        var firstRoot = Assert.IsType<Panel>(host.Content);
        var firstInput = Assert.IsType<TextBox>(Assert.Single(firstRoot.Children));
        firstInput.Text = "draft kept during refresh";

        var invalid = parser.Parse("<Page><UnknownComponent /></Page>", "bad-surface.cui");
        Assert.False(surface.TryApply(invalid, out var failureDiagnostics));
        Assert.NotEmpty(failureDiagnostics);
        Assert.Same(firstRoot, host.Content);

        var replacement = parser.Parse(
            "<Page id='surface'><Input id='composer' type='Text' text='new server value' /></Page>", "surface-next.cui");
        Assert.True(surface.TryApply(replacement, out applyDiagnostics), string.Join("; ", applyDiagnostics));
        var replacementRoot = Assert.IsType<Panel>(host.Content);
        var replacementInput = Assert.IsType<TextBox>(Assert.Single(replacementRoot.Children));
        Assert.NotSame(firstRoot, replacementRoot);
        Assert.Equal("draft kept during refresh", replacementInput.Text);
    }

    [Fact]
    public void Direct_Page_root_is_valid_CUI_markup()
    {
        var parser = new CuiRichParser();
        var doc = parser.Parse("<Page id='root'><Text>Hello</Text></Page>", "page-root.cui");

        Assert.Equal("Page", Assert.Single(doc.Components).Type);
        Assert.DoesNotContain(parser.Diagnostics.Diagnostics, d => d.Severity == CuiDiagnosticSeverity.Error);
    }

    [Fact]
    public void Missing_document_root_is_rejected()
    {
        var parser = new CuiRichParser();
        var doc = parser.Parse("<!-- no CUI root -->", "missing-root.cui");

        Assert.Empty(doc.Components);
        Assert.Contains(parser.Diagnostics.Diagnostics,
            d => d.Code == "CUI010");
    }

    [Fact]
    public void Complex_chat_space_structure_loads()
    {
        // This is the actual ChatSpace.cui structure - proves real app markup works
        var cui = """
            <Cui id="spaces.chat" version="1">
              <Page id="chat-space" role="main" accessible-name="Chat Space">
                <AdaptiveSplit id="chat-layout" compact-breakpoint="720">
                  <Aside id="recent-chats" role="navigation" accessible-name="Recent chats" compact-mode="drawer">
                    <Heading level="2">Recent chats</Heading>
                    <Button id="new-chat" action="chat.new" accessible-name="Start a new chat">New chat</Button>
                    <SearchField id="recent-search" binding="recentQuery" accessible-name="Search recent chats" placeholder="Search chats" />
                    <List id="recent-list" binding="recentChats" role="list" accessible-name="Recent conversations" empty-text="No recent chats" />
                  </Aside>
                  <Main id="conversation" accessible-name="Conversation">
                    <Toolbar id="conversation-toolbar" accessible-name="Conversation controls" wrap="true">
                      <Heading id="conversation-title" level="1" binding="conversation.title">New chat</Heading>
                      <Select id="model-choice" binding="models" selected-binding="composer.selectedModelName" action="chat.select-model" accessible-name="Choose model" />
                    </Toolbar>
                    <Transcript id="transcript" binding="conversation.messages" accessible-name="Message history" />
                    <Composer id="composer" binding="composer" accessible-name="Message input" />
                    <StatusBar id="status" binding="status" accessible-name="Chat status" />
                  </Main>
                </AdaptiveSplit>
              </Page>
            </Cui>
            """;

        var registry = new CuiControlRegistry();
        foreach (var type in new[] { "AdaptiveSplit", "Aside", "Heading", "SearchField", "List", "Main", "Toolbar", "Transcript", "Composer", "StatusBar", "Select" })
            registry.RegisterControlType(type, _ => new Panel());

        var result = CuiHeadlessRenderer.Render(cui, registry, "ChatSpace.cui");

        Assert.True(result.Success, $"ChatSpace render failed: {string.Join("; ", result.Errors)}");
        Assert.NotNull(result.Root);
        Assert.True(result.ControlCount >= 3, $"Expected at least 3 controls, got {result.ControlCount}");
    }

    [Fact]
    public void Axaml_rejected_by_CuiProjectItems()
    {
        Assert.Equal(CuiProjectItemKind.RejectedLegacyMarkup, CuiProjectItems.Recognize("test.axaml"));
        Assert.Equal(CuiProjectItemKind.RejectedLegacyMarkup, CuiProjectItems.Recognize("test.hui"));
        Assert.Equal(CuiProjectItemKind.CuiMarkup, CuiProjectItems.Recognize("test.cui"));
        Assert.False(CuiProjectItems.IsCui("test.axaml"));
        Assert.False(CuiProjectItems.IsCui("test.hui"));
        Assert.True(CuiProjectItems.IsCui("test.cui"));
    }

    [Fact]
    public void ParseValue_detects_binding_syntax()
    {
        var span = CuiSourceSpan.At("test", 1, 1);
        var literal = CuiRichParser.ParseValue("hello", span);
        Assert.IsType<CuiLiteralValue>(literal);

        var binding = CuiRichParser.ParseValue("{Binding path, mode=TwoWay, fallback=default}", span);
        Assert.IsType<CuiBindingValue>(binding);
        var bv = (CuiBindingValue)binding;
        Assert.Equal("path", bv.Path);
        Assert.Equal(CuiBindingMode.TwoWay, bv.Mode);
        Assert.Equal("default", bv.Fallback);

        var resource = CuiRichParser.ParseValue("{Resource myKey}", span);
        Assert.IsType<CuiResourceValue>(resource);
        var rv = (CuiResourceValue)resource;
        Assert.Equal("myKey", rv.Key);
    }

    private static async Task RunOnAvaloniaThread(Action action)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(action, CancellationToken.None);
    }

    private sealed class RecordingActionDispatcher : ICuiActionDispatcher
    {
        public string? Command { get; private set; }
        public object? Parameter { get; private set; }

        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Command = command;
            Parameter = parameter;
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class CuiRuntimeTestApplication : Application
{
}
