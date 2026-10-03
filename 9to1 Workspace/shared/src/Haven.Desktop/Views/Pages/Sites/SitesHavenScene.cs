using Haven.UI;
using Haven.UI.Components;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using CuiButton = Haven.UI.Components.Button;
using CuiText = Haven.UI.Components.Text;

namespace Haven.Desktop.Views.Pages.Sites;

/// <summary>CUI owns authoring controls, hierarchy, diagnostics and the explicit native preview slot.</summary>
internal sealed class SitesHavenScene
{
    public Page Root { get; } = new() { Name = "Sites.Root", Layout = HavenLayout.Grid, Columns = "260px 1fr", Rows = "Auto 1fr Auto" };
    public NativeHost PreviewHost { get; } = new() { Name = "Sites.Preview.Document" };
    public Container Projects { get; } = new() { Name = "Sites.Projects", Layout = HavenLayout.Vertical };
    public Container Pages { get; } = new() { Name = "Sites.Pages", Layout = HavenLayout.Vertical };
    public Container Components { get; } = new() { Name = "Sites.Components", Layout = HavenLayout.Vertical };
    public Input ProjectName { get; } = Input("Sites.Project.Name", "Website name", "New website");
    public Input ProjectPath { get; } = Input("Sites.Project.Path", "Project folder name", "my-website");
    public Input PageName { get; } = Input("Sites.Page.Name", "Page name", "New page");
    public Input PagePath { get; } = Input("Sites.Page.Path", "Page route", "/");
    public Input ComponentType { get; } = Input("Sites.Component.Type", "Component type", "heading");
    public Input Properties { get; } = Input("Sites.Component.Properties", "Component properties", "{\"text\":\"Welcome\"}");
    public CuiButton Refresh { get; } = Button("Sites.Refresh", "Refresh");
    public CuiButton Create { get; } = Button("Sites.Create", "Create website");
    public CuiButton AddPage { get; } = Button("Sites.Page.Create", "Add page");
    public CuiButton AddComponent { get; } = Button("Sites.Component.Create", "Add component");
    public CuiButton Save { get; } = Button("Sites.Component.Save", "Save properties");
    public CuiButton RetryAudit { get; } = Button("Sites.Audit.Retry", "Retry Home audit");
    public CuiText Status { get; } = new("Open a website to begin.") { Name = "Sites.Status", Level = TextLevel.Caption };
    public CuiText Diagnostics { get; } = new() { Name = "Sites.Diagnostics", Level = TextLevel.Paragraph };
    public event Action<Guid>? SiteSelected;
    public event Action<Guid>? PageSelected;
    public event Action<Guid>? ComponentSelected;

    public SitesHavenScene()
    {
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Root.SetValue(HavenProperties.Background, "Surface");
        var title = new CuiText("Sites") { Name = "Sites.Title", Level = TextLevel.H1 };
        title.SetValue(HavenProperties.Row, 0); Root.Add(title);
        Refresh.SetValue(HavenProperties.Row, 0); Refresh.SetValue(HavenProperties.Column, 1); Root.Add(Refresh);
        var authoring = new Container { Name = "Sites.Authoring", Layout = HavenLayout.Vertical };
        authoring.SetValue(HavenProperties.Row, 1); authoring.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        authoring.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        authoring.Add(new CuiText("Websites") { Level = TextLevel.H3 }); authoring.Add(Projects);
        authoring.Add(ProjectName); authoring.Add(ProjectPath); authoring.Add(Create);
        authoring.Add(new CuiText("Pages") { Level = TextLevel.H3 }); authoring.Add(Pages);
        authoring.Add(PageName); authoring.Add(PagePath); authoring.Add(AddPage);
        authoring.Add(new CuiText("Components") { Level = TextLevel.H3 }); authoring.Add(Components);
        authoring.Add(ComponentType); authoring.Add(Properties); authoring.Add(AddComponent); authoring.Add(Save);
        authoring.Add(RetryAudit); Root.Add(authoring);
        Properties.Multiline = true; Properties.Text = "{\"text\":\"Welcome\"}";
        Properties.SetValue(HavenProperties.MinHeight, HavenLength.Px(160));
        ComponentType.Text = "heading"; ProjectPath.Text = "my-website"; PagePath.Text = "/";
        var preview = new Container { Name = "Sites.Preview", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto 1fr" };
        preview.SetValue(HavenProperties.Row, 1); preview.SetValue(HavenProperties.Column, 1);
        Diagnostics.SetValue(HavenProperties.Row, 0); preview.Add(Diagnostics);
        PreviewHost.SetValue(HavenProperties.Row, 1); PreviewHost.SetValue(HavenProperties.MinHeight, HavenLength.Px(250));
        PreviewHost.Accessibility.AccessibleName = "Current website page preview"; preview.Add(PreviewHost); Root.Add(preview);
        Status.SetValue(HavenProperties.Row, 2); Status.SetValue(HavenProperties.Column, 1); Root.Add(Status);
        SetBusy(false, false);
    }

    public void Apply(SiteAuthoringView view, Guid? componentID, bool hasPendingAudit)
    {
        foreach (var child in Projects.Children.ToArray()) Projects.Remove(child);
        foreach (var child in Pages.Children.ToArray()) Pages.Remove(child);
        foreach (var child in Components.Children.ToArray()) Components.Remove(child);
        foreach (var project in view.Projects)
        {
            var id = project.SiteId; var button = Button("Sites.Project." + id.ToString("N"), project.Name);
            button.Variant = project.SiteId == view.Project?.SiteId ? ButtonVariant.Primary : ButtonVariant.Tertiary;
            button.Invoked += (_, _) => SiteSelected?.Invoke(id); Projects.Add(button);
        }
        if (view.Project is { } current)
        {
            foreach (var page in current.Pages)
            {
                var id = page.PageId; var button = Button("Sites.Page." + id.ToString("N"), page.Name);
                button.Variant = page.PageId == view.PageID ? ButtonVariant.Primary : ButtonVariant.Tertiary;
                button.Invoked += (_, _) => PageSelected?.Invoke(id); Pages.Add(button);
            }
            var selectedPage = current.Pages.SingleOrDefault(page => page.PageId == view.PageID);
            var reachable = selectedPage is null ? new HashSet<Guid>() : SiteAuthoringService.Reachable(current, selectedPage.RootComponentIds);
            foreach (var component in current.Components.Where(component => reachable.Contains(component.ComponentId)))
            {
                var id = component.ComponentId; var button = Button("Sites.Component." + id.ToString("N"), component.ComponentType);
                button.Variant = component.ComponentId == componentID ? ButtonVariant.Primary : ButtonVariant.Tertiary;
                button.Invoked += (_, _) => ComponentSelected?.Invoke(id); Components.Add(button);
            }
        }
        Status.Content = view.Message;
        Diagnostics.Content = view.Preview is { } snapshot ? string.Join("\n", snapshot.Diagnostics.Select(diagnostic => diagnostic.Message)) : "";
        SetBusy(false, hasPendingAudit);
        AddPage.SetValue(HavenProperties.Enabled, view.Project is not null && !hasPendingAudit);
        AddComponent.SetValue(HavenProperties.Enabled, view.PageID is not null && !hasPendingAudit);
        Save.SetValue(HavenProperties.Enabled, componentID is not null && !hasPendingAudit);
    }

    public void SetBusy(bool busy, bool hasPendingAudit)
    {
        foreach (var button in new[] { Refresh, Create, AddPage, AddComponent, Save }) button.SetValue(HavenProperties.Enabled, !busy && !hasPendingAudit);
        RetryAudit.SetValue(HavenProperties.Enabled, !busy && hasPendingAudit);
        RetryAudit.SetValue(HavenProperties.Visibility, hasPendingAudit ? HavenVisibility.Visible : HavenVisibility.Collapsed);
    }

    private static Input Input(string name, string accessibleName, string placeholder)
    {
        var input = new Input { Name = name, Placeholder = placeholder }; input.Accessibility.AccessibleName = accessibleName;
        input.SetValue(HavenProperties.Width, HavenLength.Percent(100)); return input;
    }
    private static CuiButton Button(string name, string content)
    {
        var button = new CuiButton { Name = name, Content = content, Variant = ButtonVariant.Tertiary };
        button.Accessibility.AccessibleName = content; button.SetValue(HavenProperties.MinHeight, HavenLength.Px(40)); return button;
    }
}
