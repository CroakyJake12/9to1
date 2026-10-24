using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Runtime.Tests;
using Haven.CUI.DevTools;
using Xunit;

namespace Haven.CUI.DevTools.Tests;

public sealed class ElementTreeSemanticSelectorTests
{
    [Fact]
    public void Accessibility_inspector_recognises_authored_automation_id_as_stable()
    {
        var source = "<Cui>\n    <Page>\n        <Button automation-id=\"save\" action=\"save\" />\n    </Page>\n</Cui>\n";
        var document = new CuiRichParser().Parse(source, "selectors.cui");

        var inspection = new CuiAccessibilityInspector().Inspect(document);
        var node = Assert.Single(inspection.Nodes, item => item.Type == "Button");

        Assert.Equal("save", node.AutomationId);
        Assert.True(node.HasStableSemanticSelector);
        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void Binding_dependency_inspector_resolves_document_root_variables()
    {
        const string source = "<Cui><Variable Type=\"bool\" ID=\"Ready\">False</Variable><Page><Text ID=\"status\" Text=\"{Binding Ready}\" /></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "dependencies.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var binding = Assert.Single(inspection.Bindings, item => item.Path == "Ready");

        Assert.True(binding.IsDeclaredSymbol);
        Assert.Equal("variable", binding.DeclaredSymbolKind);
        Assert.Empty(inspection.UnresolvedPaths);
        Assert.Contains(binding.ElementId, inspection.DependentsOf("Ready"));
    }

    [Fact]
    public void Binding_dependency_inspector_resolves_dotted_paths_through_root_variables()
    {
        const string source = "<Cui><Variable Type=\"commandstate\" ID=\"Result\"/><Page><If ID=\"success\" Condition=\"{Binding Result.Succeeded}\"><Text>Done</Text></If></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "dotted-dependency.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var binding = Assert.Single(inspection.Bindings, item => item.Path == "Result.Succeeded");

        Assert.True(binding.IsDeclaredSymbol);
        Assert.Equal("variable", binding.DeclaredSymbolKind);
        Assert.Empty(inspection.UnresolvedPaths);
        Assert.Contains(binding.ElementId, inspection.DependentsOf("Result"));
        Assert.DoesNotContain(inspection.UnusedSymbols, symbol => symbol == "Result");
    }

    [Fact]
    public void Binding_dependency_inspector_includes_text_condition_and_action_parameters()
    {
        const string source = "<Cui><Variable Type=\"bool\" ID=\"Ready\">False</Variable><Variable Type=\"string\" ID=\"Title\">Hello</Variable><Actions><Action Name=\"Show\" Command=\"show\" Parameter=\"{Binding Title}\" /></Actions><Page><If ID=\"visible\" Condition=\"{Binding Ready}\"><Text ID=\"caption\">Hello {Binding Title}</Text></If></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "binding-sites.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var captionId = new ElementId(Assert.Single(compilation.Output.SourceMap,
            entry => entry.AuthoredId == "caption").StableId);
        var conditionId = new ElementId(Assert.Single(compilation.Output.SourceMap,
            entry => entry.AuthoredId == "visible").StableId);

        var textBinding = Assert.Single(inspection.BindingsOn(captionId), item => item.Property == "TextParts");
        Assert.Equal("Title", textBinding.Path);
        Assert.True(textBinding.IsDeclaredSymbol);
        var conditionBinding = Assert.Single(inspection.BindingsOn(conditionId), item => item.Property == "Condition");
        Assert.Equal("Ready", conditionBinding.Path);
        Assert.True(conditionBinding.IsDeclaredSymbol);
        var actionBinding = Assert.Single(inspection.BindingsOn(new ElementId("action:Show")));
        Assert.Equal("Parameter", actionBinding.Property);
        Assert.Equal("Title", actionBinding.Path);
        Assert.Empty(inspection.UnresolvedPaths);
    }

    [Fact]
    public void Binding_dependency_inspector_keeps_set_condition_binding_and_ignores_literal_set_value()
    {
        const string source = "<Cui><Variable Type=\"bool\" ID=\"Ready\">True</Variable><Variable Type=\"string\" ID=\"Message\">Old</Variable><Actions><Action Name=\"Save\" Command=\"save\"><Set Target=\"Message\" Value=\"Saved\" When=\"{Binding Ready}\"/></Action></Actions><Page/></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "set-dependency.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var actionId = new ElementId("action:Save");
        var binding = Assert.Single(inspection.BindingsOn(actionId));

        Assert.Equal("Set[0].When", binding.Property);
        Assert.Equal("Ready", binding.Path);
        Assert.Equal(CuiBindingMode.OneWay, binding.Mode);
        Assert.Null(binding.Fallback);
        Assert.True(binding.IsDeclaredSymbol);
        Assert.Equal("variable", binding.DeclaredSymbolKind);
        Assert.Contains(actionId, inspection.DependentsOf("Ready"));
        Assert.Empty(inspection.UnresolvedPaths);
        Assert.DoesNotContain(inspection.Bindings, item => item.Property == "Set[0].Value");
    }

    [Fact]
    public void Binding_dependency_inspector_uses_compiler_identity_for_template_content()
    {
        const string source = "<Cui><Variable Type=\"string\" ID=\"Title\">Hello</Variable><Templates><Template Name=\"Card\"><Text ID=\"label\" Text=\"{Binding Title}\" /></Template></Templates><Page><Card /></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "template-dependencies.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var binding = Assert.Single(inspection.Bindings, item => item.Path == "Title");

        Assert.StartsWith("template:Card/", binding.ElementId.Value);
        Assert.Equal("Text", binding.Property);
        Assert.True(binding.IsDeclaredSymbol);
        Assert.Contains(binding.ElementId, inspection.DependentsOf("Title"));
    }

    [Fact]
    public void Binding_dependency_inspector_resolves_template_parameters_in_template_scope()
    {
        const string source = "<Cui><Templates><Template Name=\"Card\" Parameters=\"Title\"><Text ID=\"label\" Text=\"{Binding Title}\" /></Template></Templates><Page><Card Title=\"Hello\" /></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "template-parameter.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var binding = Assert.Single(inspection.Bindings, item => item.Path == "Title");

        Assert.True(binding.IsDeclaredSymbol);
        Assert.Equal("template parameter", binding.DeclaredSymbolKind);
        Assert.Equal("template:Card.Title", binding.DeclaredSymbol);
        Assert.Empty(inspection.UnresolvedPaths);
        Assert.Contains(binding.ElementId, inspection.DependentsOf("template:Card.Title"));
    }

    [Fact]
    public void Binding_dependency_inspector_resolves_template_local_variable_definitions()
    {
        const string source = "<Cui><Templates><Template Name=\"Card\"><Variable (Definition) Type=\"string\" ID=\"Title\"/><Text ID=\"label\" Text=\"{Binding Title}\" /></Template></Templates><Page><Card /></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "template-variable.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var binding = Assert.Single(inspection.Bindings, item => item.Path == "Title");

        Assert.True(binding.IsDeclaredSymbol);
        Assert.Equal("template variable", binding.DeclaredSymbolKind);
        Assert.Equal("template:Card.variable:Title", binding.DeclaredSymbol);
        Assert.Empty(inspection.UnresolvedPaths);
        Assert.Contains(binding.ElementId, inspection.DependentsOf("template:Card.variable:Title"));
    }

    [Fact]
    public void Binding_dependency_inspector_resolves_repeat_item_bindings_in_local_scope()
    {
        const string source = "<Page><Repeat ID=\"rows\" Source=\"{Binding Rows}\" As=\"row\" Key=\"{Binding row.Id}\"><Text Text=\"{Binding row.Name}\" /></Repeat></Page>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "repeat-dependencies.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var keyBinding = Assert.Single(inspection.Bindings, item => item.Property == "Repeat.Key");
        var itemBinding = Assert.Single(inspection.Bindings, item => item.Path == "row.Name");

        Assert.True(keyBinding.IsDeclaredSymbol);
        Assert.Equal("repeat item", keyBinding.DeclaredSymbolKind);
        Assert.True(itemBinding.IsDeclaredSymbol);
        Assert.Equal(keyBinding.DeclaredSymbol, itemBinding.DeclaredSymbol);
        Assert.Contains(keyBinding.ElementId, inspection.DependentsOf(keyBinding.DeclaredSymbol!));
        Assert.Contains(itemBinding.ElementId, inspection.DependentsOf(keyBinding.DeclaredSymbol!));
        Assert.Contains("Rows", inspection.UnresolvedPaths);
        Assert.DoesNotContain("row.Id", inspection.UnresolvedPaths);
        Assert.DoesNotContain("row.Name", inspection.UnresolvedPaths);
    }

    [Fact]
    public void Binding_dependency_inspector_composes_nested_template_and_repeat_scopes()
    {
        const string source = "<Cui><Templates><Template Name=\"Card\" Parameters=\"Title\"><Repeat Source=\"{Binding Rows}\" As=\"row\" Key=\"{Binding row.Id}\"><Text ID=\"label\">{Binding Title} {Binding row.Name}</Text></Repeat></Template></Templates><Page><Card Title=\"Header\" /></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "nested-scopes.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var titleBinding = Assert.Single(inspection.Bindings, item => item.Path == "Title");
        var itemBinding = Assert.Single(inspection.Bindings, item => item.Path == "row.Name");
        var keyBinding = Assert.Single(inspection.Bindings, item => item.Path == "row.Id");

        Assert.Equal("template:Card.Title", titleBinding.DeclaredSymbol);
        Assert.Equal("template parameter", titleBinding.DeclaredSymbolKind);
        Assert.Equal("repeat item", itemBinding.DeclaredSymbolKind);
        Assert.Equal(keyBinding.DeclaredSymbol, itemBinding.DeclaredSymbol);
        Assert.Equal(["Rows"], inspection.UnresolvedPaths);
    }

    [Fact]
    public void Binding_dependency_inspector_does_not_treat_resources_as_binding_variables()
    {
        const string source = "<Cui><Resources><Resource Key=\"accent\" Value=\"Blue\" /></Resources><Page><Text ID=\"title\" Text=\"{Binding accent}\" /></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "binding-resource-name.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var binding = Assert.Single(inspection.Bindings);

        Assert.Equal("accent", binding.Path);
        Assert.False(binding.IsDeclaredSymbol);
        Assert.Null(binding.DeclaredSymbolKind);
        Assert.Contains("accent", inspection.UnresolvedPaths);
    }

    [Fact]
    public void Binding_dependency_inspector_scopes_definition_inputs_to_their_component()
    {
        const string source = "<Cui><Prefab (Definition) Type=\"dynamic\" ID=\"AIBar\"><Variable (Definition) Type=\"string\" ID=\"Title\"/><Text ID=\"inside\" Text=\"{Binding Title}\"/></Prefab><Page><AIBar Title=\"caller value\"/><Text ID=\"outside\" Text=\"{Binding Title}\"/></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "definition-input.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var insideId = new ElementId(Assert.Single(compilation.Output.SourceMap,
            entry => entry.AuthoredId == "inside").StableId);
        var outsideId = new ElementId(Assert.Single(compilation.Output.SourceMap,
            entry => entry.AuthoredId == "outside").StableId);
        var inside = Assert.Single(inspection.Bindings, binding => binding.ElementId == insideId);
        var outside = Assert.Single(inspection.Bindings, binding => binding.ElementId == outsideId);

        Assert.True(inside.IsDeclaredSymbol);
        Assert.Equal("definition input", inside.DeclaredSymbolKind);
        Assert.False(outside.IsDeclaredSymbol);
        Assert.Contains("Title", inspection.UnresolvedPaths);
        Assert.Contains(inside.ElementId, inspection.DependentsOf(inside.DeclaredSymbol!));
    }

    [Fact]
    public void Binding_dependency_inspector_composes_template_and_repeat_scopes()
    {
        const string source = "<Cui><Templates><Template Name=\"Card\" Parameters=\"Title\"><Repeat Source=\"{Binding Rows}\" As=\"row\" Key=\"{Binding row.Id}\"><Text ID=\"label\">{Binding Title} {Binding row.Name}</Text></Repeat></Template></Templates><Page><Card Title=\"Header\" /></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "nested-binding-scopes.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var textBindings = inspection.Bindings.Where(item => item.Property == "TextParts").ToArray();

        Assert.Contains(textBindings, item => item.Path == "Title"
            && item.DeclaredSymbol == "template:Card.Title");
        var itemBinding = Assert.Single(textBindings, item => item.Path == "row.Name");
        var keyBinding = Assert.Single(inspection.Bindings, item => item.Property == "Repeat.Key");
        Assert.Equal("repeat item", itemBinding.DeclaredSymbolKind);
        Assert.Equal(keyBinding.DeclaredSymbol, itemBinding.DeclaredSymbol);
        Assert.Contains("Rows", inspection.UnresolvedPaths);
        Assert.DoesNotContain("Title", inspection.UnresolvedPaths);
        Assert.DoesNotContain("row.Id", inspection.UnresolvedPaths);
        Assert.DoesNotContain("row.Name", inspection.UnresolvedPaths);
    }

    [Fact]
    public void Binding_dependency_inspector_preserves_document_variable_source_semantics()
    {
        const string source = "<Cui><Variable Type=\"string\" ID=\"EmojiIcon\" Source=\"{Binding Candidates}\" Selection=\"RandomOnce\"/><Page/></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "variable-source.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var dependency = Assert.Single(inspection.VariableSources);

        Assert.Equal(new ElementId("variable:EmojiIcon"), dependency.ElementId);
        Assert.Equal("EmojiIcon", dependency.Variable);
        Assert.Equal("Candidates", dependency.Path);
        Assert.Equal("RandomOnce", dependency.Selection);
        Assert.False(dependency.IsDeclaredSymbol);
        Assert.Equal("external binding context", dependency.DeclaredSymbolKind);
        Assert.True(dependency.RequiresHostBindingContext);
        Assert.Empty(inspection.UnresolvedPaths);
    }

    [Fact]
    public void Binding_dependency_inspector_resolves_page_properties_in_the_page_local_scope()
    {
        const string source = "<Cui><Variable Type=\"string\" ID=\"Title\">outer</Variable><Page Title=\"{Binding Title}\"><Variable (Definition) Type=\"string\" ID=\"Title\"/><Text ID=\"inside\" Text=\"{Binding Title}\"/></Page><Page><Text ID=\"outside\" Text=\"{Binding Title}\"/></Page></Cui>";
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var compilation = new CuiCompiler().Compile(source, "page-local-scope.cui", options);

        Assert.True(compilation.Succeeded);
        var inspection = new CuiBindingDependencyInspector().Inspect(compilation.Output!);
        var page = Assert.Single(inspection.Bindings, binding => binding.Property == "Title");
        var inside = Assert.Single(inspection.Bindings, binding => binding.Property == "Text" && binding.ElementId.Value == "inside");
        var outside = Assert.Single(inspection.Bindings, binding => binding.Property == "Text" && binding.ElementId.Value == "outside");

        Assert.Equal("Title", page.Path);
        Assert.Equal("Title", inside.Path);
        Assert.Equal("Title", outside.Path);
        Assert.Equal(inside.DeclaredSymbol, page.DeclaredSymbol);
        Assert.Equal("page variable", page.DeclaredSymbolKind);
        Assert.Equal("page variable", inside.DeclaredSymbolKind);
        Assert.Equal("variable", outside.DeclaredSymbolKind);
        Assert.Contains(page.ElementId, inspection.DependentsOf(page.DeclaredSymbol!));
        Assert.Contains(inside.ElementId, inspection.DependentsOf(page.DeclaredSymbol!));
        Assert.DoesNotContain(outside.ElementId, inspection.DependentsOf(page.DeclaredSymbol!));
    }

    [Fact]
    public void Accessibility_tree_keeps_owned_action_event_on_the_visual_button()
    {
        var source = "<Cui>\n    <Actions><Action Name=\"Save\" Command=\"save\" /></Actions>\n    <Page>\n        <Button ID=\"save\"><ActionEvent Type=\"Clicked\" Action=\"Save\" /></Button>\n    </Page>\n</Cui>";
        var document = new CuiRichParser().Parse(source, "action-event.cui");

        var inspection = new CuiAccessibilityInspector().Inspect(document);
        var button = Assert.Single(inspection.Nodes, node => node.Type == "Button");

        Assert.Equal(["ActionEvent:Clicked"], button.Actions);
        Assert.DoesNotContain(inspection.Nodes, node => node.Type == "ActionEvent");
        Assert.Empty(inspection.Findings);
    }

    [Fact]
    public void Multi_action_control_does_not_attribute_single_command_status_to_each_route()
    {
        var status = CuiLiveTreeInspector.ResolveActionRouteStatus(2, registered: true, available: false);

        Assert.Null(status.Registered);
        Assert.Null(status.Available);
    }

    [Fact]
    public void Single_action_control_retains_its_command_status()
    {
        var status = CuiLiveTreeInspector.ResolveActionRouteStatus(1, registered: true, available: false);

        Assert.True(status.Registered);
        Assert.False(status.Available);
    }

    [Fact]
    public void Find_matches_id_role_and_accessible_name_on_the_same_node()
    {
        var button = new CuiElementNode(new ElementId("button"), "Button", automationId: "save",
            accessibleName: "Save changes", role: "Button", isFocusable: true);
        var root = new CuiElementNode(new ElementId("root"), "Page", children: [button]);
        var tree = new CuiElementTree(root);

        var match = Assert.Single(tree.Find(new CuiElementSelector(Id: "save", Role: "button", AccessibleName: "Save changes")));
        Assert.Equal(button.Id, match.Id);
        Assert.Empty(tree.Find(new CuiElementSelector(Id: "other", Role: "button")));
    }

    [Fact]
    public void Hot_reload_keeps_last_valid_tree_after_a_compile_error()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update("<Page id=\"home\"><Text id=\"title\" Text=\"Hello\" /></Page>", "home.cui");
        var current = session.Current;

        var failed = session.Update("<Page><Missing /></Page>", "home.cui");

        Assert.True(initial.Applied);
        Assert.False(failed.Applied);
        Assert.Same(current, failed.Current);
        Assert.Equal(1, failed.Revision);
    }

    [Fact]
    public void Hot_reload_marks_definition_transition_as_replaced()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update("<Page id=\"home\"><Text id=\"title\">Hello</Text></Page>", "home.cui");
        var updated = session.Update("<Page id=\"home\"><Text (Definition) ID=\"title\">Hello</Text></Page>", "home.cui");

        Assert.True(initial.Applied);
        Assert.True(updated.Applied);
        var previousTitle = Assert.Single(Flatten(initial.Current!.Components), node => node.Type == "Text");
        var titleChange = Assert.Single(updated.Changes, change => change.StableId == previousTitle.StableId);
        Assert.Equal(CuiHotReloadChangeKind.Replaced, titleChange.Kind);

        static IEnumerable<CuiCompiledNode> Flatten(IEnumerable<CuiCompiledNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var descendant in Flatten(node.Children.Concat(node.ElseChildren)))
                    yield return descendant;
            }
        }
    }

    [Fact]
    public void Hot_reload_reports_changes_inside_template_previews()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update(
            "<Cui><Templates><Template name=\"Card\" parameters=\"Title\"><Text id=\"label\" Text=\"{Binding Title}\" /></Template></Templates><Page><Card Title=\"First\" /></Page></Cui>",
            "home.cui");
        var updated = session.Update(
            "<Cui><Templates><Template name=\"Card\" parameters=\"Caption\"><Text id=\"label\" Text=\"{Binding Caption}\" /></Template></Templates><Page><Card Caption=\"Second\" /></Page></Cui>",
            "home.cui");

        Assert.True(initial.Applied);
        Assert.True(updated.Applied);
        var templateNode = Assert.Single(initial.Current!.Templates["Card"].Content, node => node.Type == "Text");
        var change = Assert.Single(updated.Changes, item => item.StableId == templateNode.StableId);
        Assert.Equal(CuiHotReloadChangeKind.Replaced, change.Kind);
        Assert.Contains(updated.Changes, item =>
            item.StableId == "template:Card" && item.Kind == CuiHotReloadChangeKind.Replaced);
    }

    [Fact]
    public void Hot_reload_reports_template_addition_and_removal()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update("<Cui><Page/></Cui>", "home.cui");
        var added = session.Update(
            "<Cui><Templates><Template name=\"Card\"><Text Text=\"Card\" /></Template></Templates><Page/></Cui>",
            "home.cui");
        var removed = session.Update("<Cui><Page/></Cui>", "home.cui");

        Assert.True(initial.Applied);
        Assert.True(added.Applied);
        Assert.True(removed.Applied);
        Assert.Contains(added.Changes, item =>
            item.StableId == "template:Card" && item.Kind == CuiHotReloadChangeKind.Added);
        Assert.Contains(removed.Changes, item =>
            item.StableId == "template:Card" && item.Kind == CuiHotReloadChangeKind.Removed);
    }

    [Fact]
    public void Hot_reload_reports_stable_nodes_moved_to_new_sibling_positions()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update(
            "<Page id=\"home\"><Text id=\"first\" Text=\"First\"/><Text id=\"second\" Text=\"Second\"/></Page>",
            "home.cui");
        var updated = session.Update(
            "<Page id=\"home\"><Text id=\"second\" Text=\"Second\"/><Text id=\"first\" Text=\"First\"/></Page>",
            "home.cui");

        Assert.True(initial.Applied);
        Assert.True(updated.Applied);
        var previousNodes = Flatten(initial.Current!.Components).Where(node => node.Type == "Text").ToArray();
        Assert.Equal(2, previousNodes.Length);
        Assert.All(previousNodes, node =>
            Assert.Contains(updated.Changes, change =>
                change.StableId == node.StableId && change.Kind == CuiHotReloadChangeKind.Moved));

        static IEnumerable<CuiCompiledNode> Flatten(IEnumerable<CuiCompiledNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var descendant in Flatten(node.Children.Concat(node.ElseChildren)))
                    yield return descendant;
            }
        }
    }

    [Fact]
    public void Hot_reload_keeps_nodes_unchanged_when_their_positions_are_stable()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        const string source = "<Page id=\"home\"><Text id=\"title\" Text=\"Hello\" /></Page>";

        Assert.True(session.Update(source, "home.cui").Applied);
        var updated = session.Update(source, "home.cui");

        Assert.True(updated.Applied);
        Assert.All(updated.Changes, change => Assert.Equal(CuiHotReloadChangeKind.Unchanged, change.Kind));
    }

    [Fact]
    public void Hot_reload_ignores_value_source_spans_but_detects_semantic_value_changes()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update(Markup("Hello", "Opacity", "accent"), "values.cui");
        var offsetOnly = session.Update("\n" + Markup("Hello", "Opacity", "accent"), "values.cui");

        Assert.True(initial.Applied);
        Assert.True(offsetOnly.Applied);
        var textId = TextNodeId(initial.Current!);
        Assert.Equal(CuiHotReloadChangeKind.Unchanged,
            Assert.Single(offsetOnly.Changes, change => change.StableId == textId).Kind);

        var literalChanged = session.Update(Markup("Changed", "Opacity", "accent"), "values.cui");
        Assert.Equal(CuiHotReloadChangeKind.Replaced,
            Assert.Single(literalChanged.Changes, change => change.StableId == textId).Kind);

        var bindingChanged = session.Update(Markup("Changed", "OtherOpacity", "accent"), "values.cui");
        Assert.Equal(CuiHotReloadChangeKind.Replaced,
            Assert.Single(bindingChanged.Changes, change => change.StableId == textId).Kind);

        var bindingModeChanged = session.Update(
            Markup("Changed", "OtherOpacity", "accent", "OneTime"), "values.cui");
        Assert.Equal(CuiHotReloadChangeKind.Replaced,
            Assert.Single(bindingModeChanged.Changes, change => change.StableId == textId).Kind);

        var resourceChanged = session.Update(Markup("Changed", "OtherOpacity", "alternate"), "values.cui");
        Assert.Equal(CuiHotReloadChangeKind.Replaced,
            Assert.Single(resourceChanged.Changes, change => change.StableId == textId).Kind);

        var resourceValueChanged = session.Update(
            Markup("Changed", "OtherOpacity", "alternate", resourceValue: "Teal"), "values.cui");
        Assert.Contains(resourceValueChanged.Changes, change =>
            change.StableId == "resource:alternate" && change.Kind == CuiHotReloadChangeKind.Replaced);

        static string Markup(string text, string bindingPath, string resourceKey,
            string bindingMode = "OneWay", string resourceValue = "Green") =>
            $"<Cui><Resources><Resource Key=\"accent\" Value=\"Blue\"/><Resource Key=\"alternate\" Value=\"{resourceValue}\"/></Resources>" +
            $"<Page><Text ID=\"sample\" Text=\"{text}\" Opacity=\"{{Binding {bindingPath}, mode={bindingMode}}}\" Foreground=\"{{Resource {resourceKey}}}\"/></Page></Cui>";

        static string TextNodeId(CuiCompiledDocument document) =>
            Flatten(document.Components).Single(node => node.Type == "Text").StableId;

        static IEnumerable<CuiCompiledNode> Flatten(IEnumerable<CuiCompiledNode> nodes)
        {
            foreach (var node in nodes)
            {
                yield return node;
                foreach (var descendant in Flatten(node.Children.Concat(node.ElseChildren)))
                    yield return descendant;
            }
        }
    }

    [Fact]
    public void Hot_reload_detects_condition_and_direct_text_changes_retained_only_in_semantics()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update(
            "<Page><If ID=\"choice\" Condition=\"true\"><Text ID=\"inside\" Text=\"Inside\" /></If><Text ID=\"caption\">Old</Text></Page>",
            "semantic-fields.cui");
        var conditionChanged = session.Update(
            "<Page><If ID=\"choice\" Condition=\"false\"><Text ID=\"inside\" Text=\"Inside\" /></If><Text ID=\"caption\">Old</Text></Page>",
            "semantic-fields.cui");

        Assert.True(initial.Applied);
        Assert.True(conditionChanged.Applied);
        var choiceId = Assert.Single(initial.Current!.SourceMap, entry => entry.AuthoredId == "choice").StableId;
        Assert.Equal(CuiHotReloadChangeKind.Replaced,
            Assert.Single(conditionChanged.Changes, change => change.StableId == choiceId).Kind);

        var textChanged = session.Update(
            "<Page><If ID=\"choice\" Condition=\"false\"><Text ID=\"inside\" Text=\"Inside\" /></If><Text ID=\"caption\">New</Text></Page>",
            "semantic-fields.cui");
        var captionId = Assert.Single(conditionChanged.Current!.SourceMap, entry => entry.AuthoredId == "caption").StableId;
        Assert.Equal(CuiHotReloadChangeKind.Replaced,
            Assert.Single(textChanged.Changes, change => change.StableId == captionId).Kind);
    }

    [Fact]
    public void Hot_reload_reports_changed_root_action_definitions()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update(
            "<Cui><Actions><Action Name=\"Save\" Command=\"save.v1\" /></Actions><Page><Button ID=\"save\" Action=\"Save\" /></Page></Cui>",
            "actions.cui");
        var updated = session.Update(
            "<Cui><Actions><Action Name=\"Save\" Command=\"save.v2\" /></Actions><Page><Button ID=\"save\" Action=\"Save\" /></Page></Cui>",
            "actions.cui");

        Assert.True(initial.Applied);
        Assert.True(updated.Applied);
        Assert.Contains(updated.Changes, change =>
            change.StableId == "action:Save" && change.Kind == CuiHotReloadChangeKind.Replaced);
    }

    [Fact]
    public void Hot_reload_reports_changed_document_style_setters()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update(
            "<Cui><Styles><Style selector=\"{::title}\"><Setter property=\"Foreground\" value=\"Blue\" /></Style></Styles><Page><Text ID=\"title\">Title</Text></Page></Cui>",
            "styles.cui");
        var updated = session.Update(
            "<Cui><Styles><Style selector=\"{::title}\"><Setter property=\"Foreground\" value=\"Green\" /></Style></Styles><Page><Text ID=\"title\">Title</Text></Page></Cui>",
            "styles.cui");

        Assert.True(initial.Applied);
        Assert.True(updated.Applied);
        Assert.Contains(updated.Changes, change =>
            change.StableId == "style:0" && change.Kind == CuiHotReloadChangeKind.Replaced);
    }

    [Fact]
    public void Hot_reload_reports_document_variable_and_effect_changes()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update(
            "<Cui><Variable Type=\"bool\" ID=\"Ready\">False</Variable><Actions><Action Name=\"Complete\" Command=\"complete\" /></Actions><Effects><Effect Name=\"completion\" Condition=\"True\" Action=\"Complete\" /></Effects><Page /></Cui>",
            "state.cui");
        var updated = session.Update(
            "<Cui><Variable Type=\"bool\" ID=\"Ready\">True</Variable><Actions><Action Name=\"Complete\" Command=\"complete\" /></Actions><Effects><Effect Name=\"completion\" Condition=\"False\" Action=\"Complete\" /></Effects><Page /></Cui>",
            "state.cui");

        Assert.True(initial.Applied);
        Assert.True(updated.Applied);
        Assert.Contains(updated.Changes, change =>
            change.StableId == "variable:Ready" && change.Kind == CuiHotReloadChangeKind.Replaced);
        Assert.Contains(updated.Changes, change =>
            change.StableId == "effect:completion" && change.Kind == CuiHotReloadChangeKind.Replaced);
    }

    [Fact]
    public void Hot_reload_reconciles_effect_rename_reorder_add_remove_and_whitespace()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        string Source(string effects) =>
            $"<Cui><Actions><Action Name=\"FirstAction\" Command=\"first\"/><Action Name=\"SecondAction\" Command=\"second\"/></Actions><Effects>{effects}</Effects><Page/></Cui>";
        const string first = "<Effect Name=\"first-effect\" Condition=\"True\" Action=\"FirstAction\"/>";
        const string second = "<Effect Name=\"second-effect\" Condition=\"False\" Action=\"SecondAction\"/>";

        var initial = session.Update(Source(first), "effects.cui");
        var renamed = session.Update(Source(first.Replace("first-effect", "renamed-effect", StringComparison.Ordinal)), "effects.cui");
        Assert.True(initial.Applied);
        Assert.True(renamed.Applied);
        Assert.Contains(renamed.Changes, change =>
            change.StableId == "effect:first-effect" && change.Kind == CuiHotReloadChangeKind.Removed);
        Assert.Contains(renamed.Changes, change =>
            change.StableId == "effect:renamed-effect" && change.Kind == CuiHotReloadChangeKind.Added);

        var reordered = session.Update(Source(second + first.Replace("first-effect", "renamed-effect", StringComparison.Ordinal)), "effects.cui");
        Assert.Contains(reordered.Changes, change =>
            change.StableId == "effect:renamed-effect" && change.Kind == CuiHotReloadChangeKind.Moved);
        Assert.Contains(reordered.Changes, change =>
            change.StableId == "effect:second-effect" && change.Kind == CuiHotReloadChangeKind.Moved);

        var added = session.Update(Source(second + first.Replace("first-effect", "renamed-effect", StringComparison.Ordinal)
            + "<Effect Name=\"third-effect\" Condition=\"True\" Action=\"FirstAction\"/>"), "effects.cui");
        Assert.Contains(added.Changes, change =>
            change.StableId == "effect:third-effect" && change.Kind == CuiHotReloadChangeKind.Added);

        var removed = session.Update(Source(second + first.Replace("first-effect", "renamed-effect", StringComparison.Ordinal)), "effects.cui");
        Assert.Contains(removed.Changes, change =>
            change.StableId == "effect:third-effect" && change.Kind == CuiHotReloadChangeKind.Removed);

        var whitespaceOnly = session.Update(Source("\n  " + second + "\n  " + first.Replace("first-effect", "renamed-effect", StringComparison.Ordinal) + "\n"), "effects.cui");
        Assert.Empty(whitespaceOnly.Changes);

        var revision = session.Revision;
        var current = session.Current;
        var invalid = session.Update(Source("<Effect Name=\"broken\" Action=\"MissingAction\"/>"), "effects.cui");
        Assert.False(invalid.Applied);
        Assert.Same(current, invalid.Current);
        Assert.Equal(revision, invalid.Revision);
        Assert.Empty(invalid.Changes);
    }

    [Fact]
    public void Hot_reload_reports_document_root_property_changes()
    {
        var options = new CuiCompilerOptions(new CuiCompilationMetadata("1", "1", "2.0"));
        var session = new CuiHotReloadSession(new CuiCompiler(), options);
        var initial = session.Update("<Cui Version=\"1\"><Page /></Cui>", "root-properties.cui");
        var updated = session.Update("<Cui Version=\"2\"><Page /></Cui>", "root-properties.cui");

        Assert.True(initial.Applied);
        Assert.True(updated.Applied);
        Assert.Contains(updated.Changes, change =>
            change.StableId == "root-property:Version" && change.Kind == CuiHotReloadChangeKind.Replaced);
    }

    [Fact]
    public async Task Live_action_trace_preserves_a_single_authored_route_and_reports_absent_routes_as_empty()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            const string source = "<Cui>\n    <Actions><Action Name=\"Save\" Command=\"Save\" /></Actions>\n    <Page>\n        <Button ID=\"save\" Action=\"Save\" />\n        <Button ID=\"plain\" />\n    </Page>\n</Cui>";
            var model = new CuiViewModel();
            model.On("Save", _ => { });
            model.SetActionAvailability("Save", available: false);
            using var loader = new CuiControlLoader();
            loader.SetActionDispatcher(model);

            var loaded = loader.LoadMarkup(source, "action-traces.cui");
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            loader.WireBindings(root);
            var inspector = CuiLiveTreeInspector.Capture(root, loader);
            var saveId = Assert.Single(inspector.Tree.Find(new CuiElementSelector(Id: "save"))).Id;
            var trace = Assert.Single(inspector.GetActions(saveId));

            Assert.Equal("action", trace.Attribute);
            Assert.Equal("Save", trace.Command);
            Assert.Equal("action-traces.cui", trace.Source.FilePath);
            Assert.Equal(4, trace.Source.Span.Start.Line);
            Assert.True(trace.Source.Span.Start.Column > 0);
            Assert.True(trace.DispatcherConnected);
            Assert.True(trace.DispatchWired);
            Assert.True(trace.HandlerRegistered);
            Assert.False(trace.HostAvailable);

            var plainId = Assert.Single(inspector.Tree.Find(new CuiElementSelector(Id: "plain"))).Id;
            Assert.Empty(inspector.GetActions(plainId));

            var unowned = CuiLiveTreeInspector.Capture(new Button(), loader);
            Assert.Empty(unowned.GetActions(new ElementId("root")));
        }, CancellationToken.None);
    }
}
