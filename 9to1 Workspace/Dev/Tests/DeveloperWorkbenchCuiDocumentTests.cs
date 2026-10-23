using System.Collections.Generic;
using CakeOS.Cui;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperWorkbenchCuiDocumentTests
{
    [Fact]
    public void Maintained_document_parses_and_exposes_all_real_project_editor_and_command_actions()
    {
        var document = DeveloperWorkbenchCuiDocument.Load();
        var nodes = Descendants(document.Components).ToArray();
        var commands = nodes.SelectMany(value => value.Actions.Values).Select(value => value.Name).ToArray();
        string[] expected = ["dev.refresh", "dev.tree", "dev.git-status", "dev.git-diff", "dev.git-branches", "dev.open-source",
            "dev.search", "dev.preview-edit", "dev.apply-edit", "dev.revert-pass", "dev.run-command", "dev.run-tests",
            "dev.run-stage", "dev.create-branch", "dev.switch-branch",
            "dev.select-source-tab", "dev.close-source-tab", "dev.toggle-source-tab-pin"];
        Assert.Equal(expected.OrderBy(value => value), commands.OrderBy(value => value));
        Assert.Equal(nodes.Length, nodes.Select(value => value.StableId).Distinct().Count());
    }
    [Fact]
    public void Exact_pass_and_process_output_use_trusted_readonly_objects_while_the_editor_remains_writable()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        var expectedBindings = new Dictionary<string, string>
        {
            ["dev-pass-before"] = "BeforeText",
            ["dev-pass-after"] = "AfterText",
            ["dev-terminal-output"] = "ProcessOutput",
        };
        foreach (var (id, binding) in expectedBindings)
        {
            var node = Assert.Single(nodes, value => value.Name == id);
            Assert.Equal("Object", node.Type);
            // The neutral shared readonly source contract, not a Host/Dev-specific renderer name.
            Assert.True(node.TryGetLiteralAttribute("type", out var actual));
            Assert.Equal("ReadOnlyText", actual);
            Assert.Contains(node.Properties, pair => pair.Key.Equals("font-family", StringComparison.OrdinalIgnoreCase)
                && pair.Value is CuiLiteralValue font && font.Value == "monospace");
            // Read-only is structural to the shared renderer contract, never authored per-node, so a
            // writable/driver edge can never turn the source review into an editable surface.
            Assert.DoesNotContain(node.Properties.Keys, value => value.Equals("readonly", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(node.Properties.Keys, value => value.Equals("value", StringComparison.OrdinalIgnoreCase));
            // The exact initial/live owner binding is preserved so Before/After/ProcessOutput always
            // reflect the genuine canonical owner state (never a duplicate or sanitised copy).
            Assert.Contains(node.Properties, pair => pair.Value is CuiBindingValue bound && bound.Path == binding);
        }
        // The full source editor remains a genuine writable text Input.
        var editor = Assert.Single(nodes, value => value.Name == "dev-code-editor");
        Assert.Equal("Input", editor.Type);
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiLiteralValue type && type.Value == "Text");
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("Multiline", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiLiteralValue multiline && bool.TryParse(multiline.Value, out var enabled) && enabled);
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("font-family", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiLiteralValue font && font.Value == "monospace");
        Assert.Contains(editor.Properties, pair => pair.Key.Equals("Value", StringComparison.OrdinalIgnoreCase)
            && pair.Value is CuiBindingValue draft && draft.Path == "DraftText");
    }
    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> nodes)
    {
        foreach (var node in nodes)
        { yield return node; foreach (var child in Descendants(node.Children)) yield return child; }
    }
}
