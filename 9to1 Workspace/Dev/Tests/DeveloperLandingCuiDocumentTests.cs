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
            "dev.references.create-manual-project", "dev.references.previous-projects", "dev.references.next-projects",
            "dev.setup.drafts", "dev.setup.new", "dev.setup.pin-draft", "dev.setup.pinned", "dev.setup.pin-project",
            "dev.setup.recent", "dev.setup.resume", "dev.setup.save",
            "dev.templates.select", "dev.templates.review", "dev.templates.create", "dev.templates.check-and-create",
            "dev.start.projects", "dev.start.projects", "dev.start.projects",
            "dev.start.new", "dev.start.new", "dev.start.new",
            "dev.start.open", "dev.start.open", "dev.start.open", "dev.start.open",
            "dev.start.extension", "dev.start.browse-folder", "dev.start.browse-folder", "dev.start.template-filters",
            "dev.start.diagnostics", "dev.start.details"];
        Assert.Equal(expected.OrderBy(value => value), nodes.SelectMany(node => node.Actions.Values).Select(action => action.Name).OrderBy(value => value));
        var filter = Assert.Single(nodes, node => node.Name == "dev-project-query");
        Assert.Contains(filter.Properties, property => property.Key.Equals("Value", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiBindingValue binding && binding.Path == "ProjectQuery");
        var open = Assert.Single(nodes, node => node.Name == "dev-open-task");
        Assert.Contains(open.Properties, property => property.Key.Equals("Content", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiBindingValue binding && binding.Path == "item.OpenLabel");
        foreach (var (id, availability) in new[] { ("dev-projects-previous", "CanPreviousProjectPage"),
            ("dev-projects-next", "CanNextProjectPage"), ("dev-review-project-template", "CanReviewTemplate"),
            ("dev-create-template-project", "CanCreateTemplateProject"), ("dev-check-and-create", "CanCheckAndCreate") })
            Assert.Contains(Assert.Single(nodes, node => node.Name == id).Properties, property => property.Key.Equals("Interactive", StringComparison.OrdinalIgnoreCase)
                && property.Value is CuiBindingValue binding && binding.Path == availability);
    }

    [Fact]
    public void Compiled_start_surface_separates_actual_projects_folder_setup_and_issued_template_review()
    {
        var nodes = Descendants(DeveloperLandingCuiDocument.Load().Components).ToArray();
        Assert.Equal(nodes.Length, nodes.Select(node => node.StableId).Distinct().Count());
        foreach (var (id, binding) in new[]
        {
            ("dev-saved-projects-root", "StartColumns"), ("dev-start-sidebar", "StartSidebarVisible"),
            ("dev-start-main", "StartPagePadding"), ("dev-start-cards", "StartCardColumns"),
            ("dev-landing-saved-section", "StartShowsProjects"), ("dev-landing-template-section", "StartIsNew"),
            ("dev-landing-identity", "StartIsNew"), ("dev-project-name", "TemplateProjectName"),
            ("dev-destination-parent", "TemplateDestinationParent"), ("dev-creation-hint", "StartCreationHint"),
            ("dev-setup-summary", "StartIsNew"), ("dev-extension-unavailable", "StartIsExtension"),
            ("dev-create-manual", "StartIsOpen"), ("dev-browse-project-folder", "StartCanBrowseFolder"),
            ("dev-browse-destination-parent", "StartCanBrowseFolder")
        })
            Assert.Contains(Assert.Single(nodes, node => node.Name == id).Properties.Values,
                value => value is CuiBindingValue actual && actual.Path == binding);
        Assert.Contains(nodes, node => node.Properties.Values.Any(value => value is CuiBindingValue binding && binding.Path == "item.DisplaySummary"));
        Assert.Contains(nodes, node => node.Properties.Values.Any(value => value is CuiBindingValue binding && binding.Path == "item.DetailsVisible"));
    }
    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> components)
    {
        foreach (var component in components)
        { yield return component; foreach (var child in Descendants(component.Children)) yield return child; }
    }
}
