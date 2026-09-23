using Avalonia;
using Avalonia.Controls;
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
    public void Root_must_be_Cui_element()
    {
        var parser = new CuiRichParser();
        var doc = parser.Parse("<Page id='root'><TextBlock /></Page>", "wrong-root.cui");

        Assert.Null(doc.Components.FirstOrDefault());
        Assert.Contains(parser.Diagnostics.Diagnostics,
            d => d.Code == "CUI003");
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

        var result = CuiHeadlessRenderer.Render(cui, "ChatSpace.cui");

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
}
