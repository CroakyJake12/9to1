using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HavenOS.AIStudio;

public sealed partial class MainWindow : Window
{
    private readonly AIStudioApi _api;
    private string _page = "Home";
    private StudioProject? _opened;

    public MainWindow(AIStudioApi api)
    {
        _api = api;
        InitializeComponent();
        SearchBox.TextChanged += async (_, _) => { if (_page is "Projects" or "Templates") await RenderAsync(); };
        OpenPage("Home");
    }

    private async void OnNavigate(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page }) OpenPage(page);
        await RenderAsync();
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open an AI Studio project", AllowMultiple = false,
            Filters = { new FileDialogFilter { Name = "AI Studio project", Extensions = { "json" } } } };
        var files = await dialog.ShowAsync(this);
        if (files is not { Length: > 0 }) return;
        try
        {
            var result = await _api.ImportProjectAsync(await File.ReadAllBytesAsync(files[0]));
            if (!result.IsSuccess) { ShowError(result.Error!); return; }
            OpenProject(result.Value!);
        }
        catch (IOException ex) { ShowNotice("Import failed", ex.Message); }
    }

    private void OpenPage(string page)
    {
        _page = page;
        _opened = null;
        Breadcrumb.Text = page;
        _ = RenderAsync();
    }

    private async Task RenderAsync()
    {
        Content.Children.Clear();
        if (_page == "Home") { await RenderHomeAsync(); return; }
        if (_page == "Projects") { await RenderProjectsAsync(); return; }
        if (_page == "Templates") { RenderTemplates(); return; }
        if (_opened is not null) { await RenderProjectAsync(_opened); return; }

        AddHeading(_page, PageDescription(_page));
        switch (_page)
        {
            case "Harnesses": await RenderProjectListAsync(StudioProjectType.Harness); AddPrimary("New Harness", () => CreateFromTemplate("harness.assistant.v1", StudioProjectType.Harness)); break;
            case "Tools": await RenderProjectListAsync(StudioProjectType.Tool); AddPrimary("New Tool", () => OpenPage("Tool type")); break;
            case "Agents": RenderAgentBuilder(); break;
            case "Playground": RenderPlayground(); break;
            case "Evaluations": case "Test Suites": case "Replay": case "Schemas": case "Model Routers": case "Generative UI": case "Run Profiles":
                RenderUnavailableSurface(_page);
                break;
            case "Tool type": RenderToolTypePicker(); break;
            default: await RenderHomeAsync(); break;
        }
    }

    private async Task RenderHomeAsync()
    {
        AddHeading("Build with Dulche", "Author Harnesses and Tools, edit canonical Agents, then validate and run them through shared services.");
        var actions = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10 };
        actions.Children.Add(ActionButton("New Harness", () => CreateFromTemplate("harness.assistant.v1", StudioProjectType.Harness), primary: true));
        actions.Children.Add(ActionButton("New Agent", () => OpenPage("Agents")));
        actions.Children.Add(ActionButton("New Tool", () => OpenPage("Tool type")));
        Content.Children.Add(actions);
        AddSectionLabel("Recent projects");
        await RenderProjectListAsync(null, 6);
        AddSectionLabel("Starter templates");
        foreach (var template in StudioTemplateCatalog.Search(null).Take(4))
        {
            var row = new Border { Background = Brush("#111A24"), BorderBrush = Brush("#263444"), BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(7), Padding = new Avalonia.Thickness(15, 12) };
            var line = new DockPanel();
            var create = new Button { Content = "Use template", Tag = template.TemplateId, Padding = new Avalonia.Thickness(12, 7) };
            create.Click += async (_, _) => await CreateFromTemplate(template.TemplateId, template.ProjectType);
            DockPanel.SetDock(create, Dock.Right); line.Children.Add(create);
            line.Children.Add(new TextBlock { Text = template.Name + "  ·  " + template.Description, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Brush("#DCE4EE") });
            row.Child = line; Content.Children.Add(row);
        }
    }

    private async Task RenderProjectsAsync()
    {
        AddHeading("Projects", "Harnesses and Tool packages persist independently of the Dulche runtime.");
        await RenderProjectListAsync(null, search: SearchBox.Text);
    }

    private async Task RenderProjectListAsync(StudioProjectType? type, int? take = null, string? search = null)
    {
        var result = await _api.ListProjectsAsync(200, search: search, type: type);
        if (!result.IsSuccess) { ShowError(result.Error!); return; }
        var items = result.Value!.Items.Take(take ?? int.MaxValue).ToArray();
        if (items.Length == 0) { AddBody("No projects yet. Start from a versioned template to create a usable baseline."); return; }
        foreach (var project in items)
        {
            var row = new Border { Background = Brush("#111A24"), BorderBrush = Brush("#263444"), BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(7), Padding = new Avalonia.Thickness(15, 11) };
            var line = new DockPanel();
            var open = new Button { Content = "Open", Tag = project.ProjectId, Padding = new Avalonia.Thickness(12, 6) };
            open.Click += async (_, _) => { var opened = await _api.OpenProjectAsync(project.ProjectId); if (opened.IsSuccess) OpenProject(opened.Value!); else ShowError(opened.Error!); };
            DockPanel.SetDock(open, Dock.Right); line.Children.Add(open);
            line.Children.Add(new TextBlock { Text = $"{project.Name}   ·   {project.ProjectType}   ·   v{project.Version}   ·   {project.ModifiedAt.ToLocalTime():g}", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Brush("#DCE4EE") });
            row.Child = line; Content.Children.Add(row);
        }
    }

    private void RenderTemplates()
    {
        AddHeading("Templates", "Versioned starter definitions. Creating one produces an independent project with template provenance.");
        foreach (var item in StudioTemplateCatalog.Search(SearchBox.Text))
        {
            var line = new DockPanel();
            var create = new Button { Content = "Create", Tag = item.TemplateId };
            create.Click += async (_, _) => await CreateFromTemplate(item.TemplateId, item.ProjectType);
            DockPanel.SetDock(create, Dock.Right); line.Children.Add(create);
            line.Children.Add(new TextBlock { Text = $"{item.Name}  ·  v{item.Version}\n{item.Description}", Foreground = Brush("#DCE4EE"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            AddPanel(line);
        }
    }

    private async Task RenderProjectAsync(StudioProject project)
    {
        AddHeading(project.Name, $"{project.ProjectType} project · version {project.Version} · template {project.TemplateId} v{project.TemplateVersion}");
        var name = new TextBox { Text = project.Name, Watermark = "Project name" };
        AddField("Name", name);
        var json = new TextBox { Text = JsonSerializer.Serialize(JsonDocument.Parse(project.DefinitionJson).RootElement, new JsonSerializerOptions(StudioJson.Options) { WriteIndented = true }), AcceptsReturn = true, MinHeight = 330, FontFamily = "Consolas", VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        AddField("Inspectable definition", json);
        AddBody("Edits are version-checked and retained in project history. Saving definitions does not run, install or activate them.");
        var actions = new WrapPanel { ItemWidth = double.NaN, ItemHeight = double.NaN };
        actions.Children.Add(ActionButton("Save draft", async () =>
        {
            var saved = await _api.SaveProjectAsync(project.ProjectId, project.Version, json.Text ?? "", note: "Edited in AI Studio");
            if (saved.IsSuccess) { _opened = saved.Value; await RenderAsync(); } else ShowError(saved.Error!);
        }, primary: true));
        actions.Children.Add(ActionButton("Validate", async () =>
        {
            var saved = await _api.SaveProjectAsync(project.ProjectId, project.Version, json.Text ?? "", note: "Saved before validation");
            if (!saved.IsSuccess) { ShowError(saved.Error!); return; }
            _opened = saved.Value;
            var report = await _api.ValidateProjectAsync(project.ProjectId);
            if (report.IsSuccess) ShowValidation(report.Value!); else ShowError(report.Error!);
        }));
        if (project.ProjectType == StudioProjectType.Harness)
            actions.Children.Add(ActionButton("Preview / Test", async () =>
            {
                var run = await _api.TestProjectAsync(project.ProjectId);
                if (!run.IsSuccess) ShowError(run.Error!); else ShowNotice("Harness run", run.Value!.Output);
            }));
        if (project.ProjectType == StudioProjectType.Tool)
            actions.Children.Add(ActionButton("Install", async () =>
            {
                var installed = await _api.InstallToolAsync(project.ProjectId, project.Version);
                if (!installed.IsSuccess) ShowError(installed.Error!); else ShowNotice("Tool install", installed.Value!.Status);
            }));
        actions.Children.Add(ActionButton("History", async () =>
        {
            var history = await _api.ProjectHistoryAsync(project.ProjectId);
            if (history.IsSuccess) ShowNotice("Project history", history.Value!.Count == 0 ? "No saved revisions yet." : string.Join(Environment.NewLine, history.Value.Select(item => $"v{item.Version}  {item.SavedAt.ToLocalTime():g}  {item.Note}")));
            else ShowError(history.Error!);
        }));
        actions.Children.Add(ActionButton("Duplicate", async () =>
        {
            var copy = await _api.DuplicateProjectAsync(project.ProjectId, project.Name + " copy");
            if (copy.IsSuccess) OpenProject(copy.Value!); else ShowError(copy.Error!);
        }));
        actions.Children.Add(ActionButton("Export JSON", async () =>
        {
            var exported = await _api.ExportProjectAsync(project.ProjectId);
            if (!exported.IsSuccess) { ShowError(exported.Error!); return; }
            var save = new SaveFileDialog { Title = "Export AI Studio project", InitialFileName = project.Name + ".json", DefaultExtension = "json" };
            var path = await save.ShowAsync(this);
            if (path is null) return;
            await File.WriteAllBytesAsync(path, exported.Value!);
            ShowNotice("Project exported", "The lossless project bundle was exported.");
        }));
        actions.Children.Add(ActionButton("Move to recovery", async () =>
        {
            var deleted = await _api.DeleteProjectAsync(project.ProjectId, project.Version);
            if (!deleted.IsSuccess) { ShowError(deleted.Error!); return; }
            _opened = null; OpenPage("Projects"); await RenderAsync();
        }));
        Content.Children.Add(actions);
    }

    private void RenderToolTypePicker()
    {
        AddHeading("New Tool", "Choose a type-specific builder. Skill, Plugin and MCP packages keep separate manifests and permission contracts.");
        foreach (var (type, template, title, detail) in new[]
        {
            (StudioToolType.Skill, "tool.skill.v1", "Skill", "Declarative instructions, examples, resources and declared dependencies."),
            (StudioToolType.Plugin, "tool.plugin.v1", "Plugin", "Versioned actions, schemas, risk declarations and account references."),
            (StudioToolType.Mcp, "tool.mcp-client.v1", "MCP", "Client connection role, transport, authentication references and capability schemas.")
        })
        {
            var button = ActionButton($"{title}  ·  {detail}", () => CreateFromTemplate(template, StudioProjectType.Tool), primary: true);
            button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            Content.Children.Add(button);
        }
    }

    private void RenderAgentBuilder()
    {
        AddHeading("Agent Builder", "Edit the persistent Dulche Agent configuration directly. Agents are canonical Den entities, not AI Studio projects.");
        AddBody("The Den/Dulche Agent API is not registered in this host. Fields below are an unsaved request draft; AI Studio will not create a local copy or claim it was saved.");
        var name = new TextBox { Watermark = "Agent name" };
        var description = new TextBox { Watermark = "Purpose and description" };
        var instructions = new TextBox { Watermark = "Role, responsibilities, boundaries and procedures", AcceptsReturn = true, MinHeight = 210 };
        AddField("Name", name); AddField("Description", description);
        AddSectionLabel("Plugins / Connectors"); AddBody("Canonical capability references appear here when the Dulche catalog adapter is available.");
        AddSectionLabel("Skills"); AddBody("Installed Skill references appear here when the Dulche package adapter is available.");
        AddSectionLabel("Knowledge / Files"); AddBody("Den memory and linked knowledge sources will remain canonical references.");
        var more = new Expander { Header = "More Options", IsExpanded = false };
        more.Content = new StackPanel { Spacing = 10, Children = { new TextBlock { Text = "Availability · Default Model · Quick Actions", Foreground = Brush("#AEBBCC") } } };
        Content.Children.Add(more);
        AddField("Instructions", instructions);
        Content.Children.Add(ActionButton("Create canonical Agent", async () =>
        {
            var result = await _api.CreateAgentAsync(new CanonicalAgentDefinition(Guid.Empty, name.Text ?? "", description.Text ?? "", instructions.Text ?? "", "{}", 0, true));
            if (!result.IsSuccess) ShowError(result.Error!); else ShowNotice("Agent created", $"Agent {result.Value!.AgentId} was created at revision {result.Value.DefinitionRevision}.");
        }, primary: true));
    }

    private void RenderPlayground()
    {
        AddHeading("Playground", "Run test-scoped experiments with canonical models, Agents, Skills, Plugins, context and Run Profiles.");
        AddBody("A request is sent only through the Dulche runtime adapter. Without it, the operation returns RuntimeUnavailable and performs no model or tool call.");
        var instructions = new TextBox { Watermark = "Test instructions", AcceptsReturn = true, MinHeight = 90 };
        var input = new TextBox { Watermark = "Input", AcceptsReturn = true, MinHeight = 110 };
        AddField("Instructions", instructions); AddField("Input", input);
        Content.Children.Add(ActionButton("Run test", async () =>
        {
            var result = await _api.RunPlaygroundAsync(new PlaygroundConfiguration(null, null, [], [], new Dictionary<string, string>(), null, instructions.Text ?? ""), input.Text ?? "");
            if (!result.IsSuccess) ShowError(result.Error!); else ShowNotice("Playground result", result.Value!.Output);
        }, primary: true));
    }

    private void RenderUnavailableSurface(string feature)
    {
        AddBody($"{feature} has a typed navigation destination. Its canonical Dulche/Home storage and execution adapter is not registered in this host, so no demo data or simulated results are shown.");
        AddBody("The AI Studio host reports CapabilityUnavailable until the owning shared service is supplied.");
    }

    private async Task CreateFromTemplate(string templateId, StudioProjectType type)
    {
        var template = StudioTemplateCatalog.Find(templateId);
        var name = template?.Name ?? "New project";
        var result = type == StudioProjectType.Tool && template?.ToolType is { } toolType
            ? await _api.CreateToolAsync(toolType, templateId, "New " + name)
            : await _api.CreateProjectAsync(type, templateId, "New " + name);
        if (!result.IsSuccess) { ShowError(result.Error!); return; }
        OpenProject(result.Value!);
    }

    private void OpenProject(StudioProject project)
    {
        _opened = project; _page = project.ProjectType == StudioProjectType.Harness ? "Harnesses" : "Tools";
        Breadcrumb.Text = $"Projects  /  {project.Name}"; _ = RenderAsync();
    }

    private async void ShowValidation(ValidationReport report)
    {
        var text = string.Join(Environment.NewLine, report.Issues.Select(issue => $"{issue.Severity.ToUpperInvariant()}  {issue.Code}  {issue.Path}: {issue.Message}"));
        await ShowNoticeAsync(report.IsValid ? "Structural validation passed" : "Validation findings",
            string.IsNullOrWhiteSpace(text) ? report.ValidationScope : text + Environment.NewLine + Environment.NewLine + report.ValidationScope);
    }

    private async void ShowError(StudioError error) => await ShowNoticeAsync(error.Code, error.Message);
    private async void ShowNotice(string title, string message) => await ShowNoticeAsync(title, message);
    private async Task ShowNoticeAsync(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 500, Height = 230, MinWidth = 380, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#101720") };
        var stack = new StackPanel { Margin = new Avalonia.Thickness(22), Spacing = 18 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Brush("#F3F6FA") });
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Brush("#BBC6D3") });
        var close = new Button { Content = "Close", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, MinWidth = 88 };
        close.Click += (_, _) => dialog.Close(); stack.Children.Add(close); dialog.Content = stack;
        await dialog.ShowDialog(this);
    }

    private void AddHeading(string title, string subtitle)
    {
        Content.Children.Add(new TextBlock { Text = title, FontSize = 29, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Brush("#F3F6FA") });
        Content.Children.Add(new TextBlock { Text = subtitle, FontSize = 14, Foreground = Brush("#AEBBCC"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 800 });
    }
    private void AddSectionLabel(string text) => Content.Children.Add(new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = Avalonia.Media.FontWeight.SemiBold, Foreground = Brush("#75869A"), Margin = new Avalonia.Thickness(0, 12, 0, 0) });
    private void AddBody(string text) => Content.Children.Add(new TextBlock { Text = text, Foreground = Brush("#AEBBCC"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 820 });
    private void AddField(string label, Control control)
    {
        var stack = new StackPanel { Spacing = 7, MaxWidth = 860 };
        stack.Children.Add(new TextBlock { Text = label, Foreground = Brush("#B9C4D1"), FontSize = 12, FontWeight = Avalonia.Media.FontWeight.Medium });
        stack.Children.Add(control); Content.Children.Add(stack);
    }
    private void AddPanel(Control control)
    {
        var border = new Border { Background = Brush("#111A24"), BorderBrush = Brush("#263444"), BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(7), Padding = new Avalonia.Thickness(16) };
        border.Child = control; Content.Children.Add(border);
    }
    private void AddPrimary(string label, Func<Task> action) => Content.Children.Add(ActionButton(label, action, true));
    private static Button ActionButton(string label, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Avalonia.Thickness(14, 9), MinHeight = 38, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        if (primary) button.Classes.Add("primary");
        button.Click += async (_, _) => await action(); return button;
    }
    private static Avalonia.Media.IBrush Brush(string color) => Avalonia.Media.Brush.Parse(color);
    private static string PageDescription(string page) => page switch
    {
        "Harnesses" => "Inspect, configure, validate and test project-backed Harnesses.",
        "Agents" => "Edit canonical persistent Dulche Agents in Den.",
        "Tools" => "Build reusable Skill, Plugin and MCP packages.",
        _ => $"{page} for reusable Dulche systems."
    };
}
