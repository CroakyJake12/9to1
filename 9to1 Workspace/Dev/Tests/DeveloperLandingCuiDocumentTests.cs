using CakeOS.Cui;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperLandingCuiDocumentTests
{
    [Fact]
    public void Compiled_landing_preserves_existing_open_setup_actions_and_adds_real_filter_paging_bindings()
    {
        var nodes = Descendants(DeveloperLandingCuiDocument.Load().Components).ToArray();
        string[] expected = ["dev.references.refresh", "dev.references.open-space", "dev.references.open-task",
            "dev.references.create-manual-project", "dev.references.previous-projects", "dev.references.next-projects"];
        Assert.Equal(expected.OrderBy(value => value), nodes.SelectMany(node => node.Actions.Values).Select(action => action.Name).OrderBy(value => value));
        var filter = Assert.Single(nodes, node => node.Name == "dev-project-query");
        Assert.Contains(filter.Properties, property => property.Key.Equals("Value", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiBindingValue binding && binding.Path == "ProjectQuery");
        var open = Assert.Single(nodes, node => node.Name == "dev-open-task");
        Assert.Contains(open.Properties, property => property.Key.Equals("Content", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiBindingValue binding && binding.Path == "item.OpenLabel");
        foreach (var (id, availability) in new[] { ("dev-projects-previous", "CanPreviousProjectPage"), ("dev-projects-next", "CanNextProjectPage") })
            Assert.Contains(Assert.Single(nodes, node => node.Name == id).Properties, property => property.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
                && property.Value is CuiBindingValue binding && binding.Path == availability);
    }
    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> components)
    {
        foreach (var component in components)
        { yield return component; foreach (var child in Descendants(component.Children)) yield return child; }
    }
}
