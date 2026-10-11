using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// FW-012 regression: a definition declaration is library metadata. It must never create a
/// visible native control or replace the actual root. Instances preserve authored
/// identity, explicit inputs, content precedence and independent or shared state scopes.
/// </summary>
public sealed class CuiDefinitionDeclarationsTests
{
    [Fact]
    public void Preceding_declaration_leaves_the_real_page_as_the_rendered_root()
    {
        var result = CuiHeadlessRenderer.Render(
            """
            <Cui id="definitions" version="1">
              <Text (Definition) id="Paragraph" text="library" />
              <Page id="actual"><Text text="visible" /></Page>
            </Cui>
            """,
            "definition-root.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        var page = Assert.IsType<Panel>(result.Root);
        Assert.Equal("actual", page.Name);
        var visible = Assert.IsType<TextBlock>(Assert.Single(page.Children));
        Assert.Equal("visible", visible.Text);
        Assert.Equal(2, result.ControlCount); // Page and Text; declaration adds no control
    }

    [Fact]
    public void Nested_declaration_produces_no_control_inside_the_real_page()
    {
        var result = CuiHeadlessRenderer.Render(
            """
            <Cui id="nested" version="1">
              <Page id="actual">
                <Text (Definition) id="Paragraph" text="library" />
                <Text text="visible" />
              </Page>
            </Cui>
            """,
            "definition-nested.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        var page = Assert.IsType<Panel>(result.Root);
        Assert.Equal("actual", page.Name);
        var visible = Assert.IsType<TextBlock>(Assert.Single(page.Children));
        Assert.Equal("visible", visible.Text);
        Assert.Equal(2, result.ControlCount);
    }

    [Fact]
    public void Definition_only_document_returns_no_root_control()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            """
            <Cui id="library-only" version="1">
              <Text (Definition) id="Paragraph" text="library" />
            </Cui>
            """,
            "definition-only.cui");

        Assert.Empty(diagnostics);
        Assert.Null(root);
    }

    [Fact]
    public void Theme_scope_omits_declaration_children()
    {
        var result = CuiHeadlessRenderer.Render(
            """
            <Cui id="theme" version="1">
              <Page id="actual">
                <Theme Name="Professional">
                  <Text (Definition) id="Paragraph" text="library" />
                  <Text text="visible" />
                </Theme>
              </Page>
            </Cui>
            """,
            "definition-theme.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        var page = Assert.IsType<Panel>(result.Root);
        var visible = Assert.IsType<TextBlock>(Assert.Single(page.Children));
        Assert.Equal("visible", visible.Text);
        Assert.Equal(2, result.ControlCount);
    }

    [Fact]
    public void Conditional_branch_omits_declaration_children()
    {
        var result = CuiHeadlessRenderer.Render(
            """
            <Cui id="conditional" version="1">
              <Page id="actual"><If Condition="true"><Text (Definition) id="Paragraph" text="library" /><Text text="visible" /></If></Page>
            </Cui>
            """,
            "definition-conditional.cui");

        Assert.True(result.Success, $"Render failed: {string.Join("; ", result.Errors)}");
        var page = Assert.IsType<Panel>(result.Root);
        var host = Assert.IsType<ContentControl>(Assert.Single(page.Children));
        var branch = Assert.IsType<Panel>(host.Content);
        var visible = Assert.IsType<TextBlock>(Assert.Single(branch.Children));
        Assert.Equal("visible", visible.Text);
        Assert.Equal(4, result.ControlCount); // Page, If host, branch and Text
    }

    [Fact]
    public async Task Repeat_item_root_omits_declaration_children()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        var observed = await session.Dispatch<string?>(() =>
        {
            var model = new CuiViewModel();
            model.Set("Rows", new[] { new Dictionary<string, object?> { ["Id"] = "row-1" } });
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);

            var (root, diagnostics) = loader.LoadMarkup(
                """
                <Cui id="repeat" version="1">
                  <Page id="actual"><Repeat Source="{Binding Rows}" As="row" Key="{Binding row.Id}"><Text (Definition) id="Paragraph" text="library" /><Text text="visible" /></Repeat></Page>
                </Cui>
                """);

            Assert.Empty(diagnostics);
            var page = Assert.IsType<Panel>(root);
            var host = Assert.IsType<StackPanel>(Assert.Single(page.Children));
            var itemRoot = Assert.IsType<Panel>(Assert.Single(host.Children));
            var visible = Assert.IsType<TextBlock>(Assert.Single(itemRoot.Children));
            Assert.NotNull(visible.Text);
            return visible.Text;
        }, CancellationToken.None);

        Assert.Equal("visible", observed);
    }

    [Fact]
    public void Definition_instance_renders_the_declaration_with_instance_precedence()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            """
            <Cui id="instance" version="1">
              <Page id="actual">
                <Text (Definition) id="Paragraph" text="library" />
                <Paragraph text="rendered" />
              </Page>
            </Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsType<Panel>(root);
        var instance = Assert.IsType<TextBlock>(Assert.Single(page.Children));
        Assert.NotNull(instance.Text);
        Assert.Equal("rendered", instance.Text);
    }

    [Fact]
    public void Definition_instance_content_replaces_the_declaration_default_content()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            """
            <Cui id="instance-content" version="1">
              <Page id="actual">
                <Container Type="Vertical Stack" (Definition) id="Card"><Text text="default" /></Container>
                <Card id="card"><Text text="supplied" /></Card>
              </Page>
            </Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsType<Panel>(root);
        var card = Assert.IsType<CuiContainerControl>(Assert.Single(page.Children));
        Assert.Equal("card", card.Name);
        var supplied = Assert.IsType<TextBlock>(Assert.Single(card.AuthoredChildren));
        Assert.NotNull(supplied.Text);
        Assert.Equal("supplied", supplied.Text);
    }

    [Fact]
    public void Dynamic_definition_instances_create_child_scopes()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            """
            <Cui id="instance-scopes" version="1">
              <Page id="actual">
                <Container Type="Overlay" (Definition) id="Card"><Text id="inner" /></Container>
                <Card id="first" />
                <Card id="second" />
              </Page>
            </Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsType<Panel>(root);
        var first = Assert.IsType<CuiContainerControl>(page.Children[0]);
        var second = Assert.IsType<CuiContainerControl>(page.Children[1]);
        Assert.Equal("id:first", CuiRuntimeIdentity.GetStableId(first));
        Assert.Equal("id:second", CuiRuntimeIdentity.GetStableId(second));
        Assert.Equal("id:first/id:inner", CuiRuntimeIdentity.GetStableId(Assert.IsType<TextBlock>(Assert.Single(first.AuthoredChildren))));
        Assert.Equal("id:second/id:inner", CuiRuntimeIdentity.GetStableId(Assert.IsType<TextBlock>(Assert.Single(second.AuthoredChildren))));
        Assert.NotEqual(CuiRuntimeIdentity.GetStableId(first), CuiRuntimeIdentity.GetStableId(second));
    }

    [Fact]
    public void Static_prefab_instances_share_one_internal_scope()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            """
            <Cui id="static-prefab" version="1">
              <Page id="actual">
                <Prefab (Definition) id="AIBar" Type="static"><Text id="inner" text="bar" /></Prefab>
                <AIBar id="first" />
                <AIBar id="second" />
              </Page>
            </Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsType<Panel>(root);
        var first = Assert.IsType<CuiContainerControl>(page.Children[0]);
        var second = Assert.IsType<CuiContainerControl>(page.Children[1]);
        var firstInner = Assert.IsType<TextBlock>(Assert.Single(first.AuthoredChildren));
        var secondInner = Assert.IsType<TextBlock>(Assert.Single(second.AuthoredChildren));
        Assert.Equal("definition:AIBar/id:inner", CuiRuntimeIdentity.GetStableId(firstInner));
        Assert.Equal(CuiRuntimeIdentity.GetStableId(firstInner), CuiRuntimeIdentity.GetStableId(secondInner));
        Assert.NotEqual(CuiRuntimeIdentity.GetStableId(first), CuiRuntimeIdentity.GetStableId(second));
    }

    [Fact]
    public void Cyclic_definition_dependency_is_rejected_with_a_truthful_diagnostic()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            """
            <Cui id="cycle" version="1">
              <Page id="actual">
                <Text (Definition) id="A"><B /></Text>
                <Text (Definition) id="B"><A /></Text>
                <A />
              </Page>
            </Cui>
            """);

        Assert.Null(root);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("CUIR060", diagnostic.Code);
        Assert.Contains("A -> B -> A", diagnostic.Message);
    }

    [Fact]
    public void Template_instance_receives_only_declared_inputs()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            """
            <Cui id="template" version="1">
              <Templates><Template name="Card" parameters="Title"><Text text="{Binding Title}" /></Template></Templates>
              <Page id="actual"><Card Title="supplied" /></Page>
            </Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsType<Panel>(root);
        var instance = Assert.IsType<Panel>(Assert.Single(page.Children));
        var rendered = Assert.IsType<TextBlock>(Assert.Single(instance.Children));
        Assert.NotNull(rendered.Text);
        Assert.Equal("supplied", rendered.Text);
    }

    [Fact]
    public void Definition_instance_preserves_its_conditional_branch()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            """
            <Cui id="def-conditional" version="1">
              <Page id="actual">
                <Container Type="Overlay" (Definition) id="Card"><If id="branch" Condition="true"><Text id="yes" text="yes" /></If></Container>
                <Card id="first" />
              </Page>
            </Cui>
            """);

        Assert.Empty(diagnostics);
        var page = Assert.IsType<Panel>(root);
        var instance = Assert.IsType<CuiContainerControl>(Assert.Single(page.Children));
        var host = Assert.IsType<ContentControl>(Assert.Single(instance.AuthoredChildren));
        var branch = Assert.IsType<Panel>(host.Content);
        var visible = Assert.IsType<TextBlock>(Assert.Single(branch.Children));
        Assert.NotNull(visible.Text);
        Assert.Equal("yes", visible.Text);
        Assert.Equal("id:first", CuiRuntimeIdentity.GetStableId(instance));
        Assert.Equal("id:first/id:branch", CuiRuntimeIdentity.GetStableId(host));
        Assert.Equal("id:first/id:yes", CuiRuntimeIdentity.GetStableId(visible));
    }

    [Fact]
    public async Task Definition_instances_keep_separate_repeat_scopes()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        var observed = await session.Dispatch<(string?, string?)>(() =>
        {
            var model = new CuiViewModel();
            model.Set("Rows", new[] { new Dictionary<string, object?> { ["Name"] = "row" } });
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(model);

            var (root, diagnostics) = loader.LoadMarkup(
                """
                <Cui id="def-repeat" version="1">
                  <Page id="actual">
                    <Container Type="Overlay" (Definition) id="Card"><Repeat id="rows" Source="{Binding Rows}" As="row" Key="{Binding row.Name}"><Text id="cell" text="{Binding row.Name}" /></Repeat></Container>
                    <Card id="first" />
                    <Card id="second" />
                  </Page>
                </Cui>
                """);

            Assert.Empty(diagnostics);
            var page = Assert.IsType<Panel>(root);
            var first = Assert.IsType<CuiContainerControl>(page.Children[0]);
            var second = Assert.IsType<CuiContainerControl>(page.Children[1]);
            var firstHost = Assert.IsType<StackPanel>(Assert.Single(first.AuthoredChildren));
            var secondHost = Assert.IsType<StackPanel>(Assert.Single(second.AuthoredChildren));
            var firstItem = Assert.IsType<Panel>(Assert.Single(firstHost.Children));
            var secondItem = Assert.IsType<Panel>(Assert.Single(secondHost.Children));
            var firstCell = Assert.IsType<TextBlock>(Assert.Single(firstItem.Children));
            var secondCell = Assert.IsType<TextBlock>(Assert.Single(secondItem.Children));
            Assert.NotNull(firstCell.Text);
            Assert.NotNull(secondCell.Text);
            Assert.Equal("row", firstCell.Text);
            Assert.Equal("row", secondCell.Text);
            return (CuiRuntimeIdentity.GetStableId(firstItem), CuiRuntimeIdentity.GetStableId(secondItem));
        }, CancellationToken.None);

        Assert.Equal("id:first/id:rows|key:3:row", observed.Item1);
        Assert.Equal("id:second/id:rows|key:3:row", observed.Item2);
        Assert.NotEqual(observed.Item1, observed.Item2);
    }}
