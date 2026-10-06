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
            "dev.run-stage", "dev.create-branch", "dev.switch-branch"];
        Assert.Equal(expected.OrderBy(value => value), commands.OrderBy(value => value));
        Assert.Equal(nodes.Length, nodes.Select(value => value.StableId).Distinct().Count());
    }
    [Fact]
    public void Exact_pass_and_process_output_use_trusted_readonly_objects_while_the_editor_remains_writable()
    {
        var nodes = Descendants(DeveloperWorkbenchCuiDocument.Load().Components).ToArray();
        foreach (var id in new[] { "dev-pass-before", "dev-pass-after", "dev-terminal-output" })
        {
            var node = Assert.Single(nodes.Where(value => value.Name == id));
            Assert.Equal("Object", node.Type);
            Assert.True(node.TryGetLiteralAttribute("type", out var actual));
            Assert.Equal("DeveloperReadonlySource", actual);
            Assert.DoesNotContain(node.Properties.Keys, value => value.Equals("readonly", StringComparison.OrdinalIgnoreCase));
        }
        Assert.Equal("TextBox", Assert.Single(nodes.Where(value => value.Name == "dev-code-editor")).Type);
    }
    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> nodes)
    {
        foreach (var node in nodes)
        { yield return node; foreach (var child in Descendants(node.Children)) yield return child; }
    }
}
