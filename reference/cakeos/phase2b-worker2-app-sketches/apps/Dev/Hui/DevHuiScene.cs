using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;
using HuiContainer = Haven.UI.Components.Container;

namespace HavenOS.Apps.Dev.Hui;

public enum DevHuiAction
{
    OpenFolder,
    OpenWorkspace,
    NewFile,
    NewFolder,
    SaveFile,
    SaveAll,
    ToggleSidebar,
    TogglePanel,
    ToggleTerminal,
    RunCommand,
    InstallExtension,
    StartDebug,
    StopDebug,
    RunTask,
    Settings,
}

public sealed class DevHuiScene
{
    private readonly Queue<DevHuiAction> _actions = new();
    private readonly Dictionary<string, HuiButton> _fileTreeItems = new();
    private readonly Dictionary<string, HuiButton> _editorTabs = new();
    private string? _activeFile;

    public DevHuiScene()
    {
        Root = new Page
        {
            Name = "Dev.Hui.Root",
            Layout = HavenLayout.Grid,
            Columns = "280px 1fr 300px",
            Rows = "Auto 1fr Auto",
        };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.FontFamily, "Monospace");

        // Title bar
        var titleBar = new HuiContainer { Name = "Dev.Hui.TitleBar", Layout = HavenLayout.Horizontal };
        titleBar.SetValue(HavenProperties.Row, 0);
        titleBar.SetValue(HavenProperties.ColumnSpan, 3);
        titleBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px 12px"));
        titleBar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        titleBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        titleBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        titleBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        titleBar.SetValue(HavenProperties.BorderBottom, HavenLength.Px(1));

        var fileMenu = NewMenuButton("File", [DevHuiAction.OpenFolder, DevHuiAction.OpenWorkspace, DevHuiAction.NewFile, DevHuiAction.NewFolder, DevHuiAction.SaveFile, DevHuiAction.SaveAll]);
        var editMenu = NewMenuButton("Edit", [DevHuiAction.RunCommand]);
        var viewMenu = NewMenuButton("View", [DevHuiAction.ToggleSidebar, DevHuiAction.TogglePanel, DevHuiAction.ToggleTerminal]);
        var debugMenu = NewMenuButton("Debug", [DevHuiAction.StartDebug, DevHuiAction.StopDebug]);
        var terminalMenu = NewMenuButton("Terminal", [DevHuiAction.RunTask]);
        var helpMenu = NewMenuButton("Help", [DevHuiAction.Settings]);

        titleBar.Add(fileMenu);
        titleBar.Add(editMenu);
        titleBar.Add(viewMenu);
        titleBar.Add(debugMenu);
        titleBar.Add(terminalMenu);
        titleBar.Add(helpMenu);
        Root.Add(titleBar);

        // Sidebar - File explorer
        var sidebar = new HuiContainer { Name = "Dev.Hui.Sidebar", Layout = HavenLayout.Vertical };
        sidebar.SetValue(HavenProperties.Row, 1);
        sidebar.SetValue(HavenProperties.Column, 0);
        sidebar.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
        sidebar.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        sidebar.SetValue(HavenProperties.Background, "SurfaceVariant");
        sidebar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        sidebar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        sidebar.SetValue(HavenProperties.BorderRight, HavenLength.Px(1));

        var sidebarHeader = new HuiContainer { Name = "Dev.Hui.SidebarHeader", Layout = HavenLayout.Horizontal };
        sidebarHeader.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        sidebarHeader.Add(new HuiText("EXPLORER") { Name = "Dev.Hui.ExplorerTitle", Level = TextLevel.Caption });
        sidebarHeader.Add(new HuiContainer { Name = "Dev.Hui.SidebarSpacer" });
        NewFileButton = new HuiButton { Name = "Dev.Hui.NewFileBtn", Content = "📄", Variant = ButtonVariant.Ghost };
        NewFileButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(28));
        NewFileButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
        NewFileButton.Invoked += (_, _) => _actions.Enqueue(DevHuiAction.NewFile);
        sidebarHeader.Add(NewFileButton);
        NewFolderButton = new HuiButton { Name = "Dev.Hui.NewFolderBtn", Content = "📁", Variant = ButtonVariant.Ghost };
        NewFolderButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(28));
        NewFolderButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
        NewFolderButton.Invoked += (_, _) => _actions.Enqueue(DevHuiAction.NewFolder);
        sidebarHeader.Add(NewFolderButton);
        sidebar.Add(sidebarHeader);

        FileTree = new HuiContainer { Name = "Dev.Hui.FileTree", Layout = HavenLayout.Vertical };
        FileTree.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        FileTree.SetValue(HavenProperties.Overflow, HavenOverflow.Auto);
        sidebar.Add(FileTree);
        Root.Add(sidebar);

        // Editor area
        var editorArea = new HuiContainer { Name = "Dev.Hui.EditorArea", Layout = HavenLayout.Vertical };
        editorArea.SetValue(HavenProperties.Row, 1);
        editorArea.SetValue(HavenProperties.Column, 1);
        editorArea.SetValue(HavenProperties.Background, "Surface");

        // Editor tabs
        var tabBar = new HuiContainer { Name = "Dev.Hui.TabBar", Layout = HavenLayout.Horizontal };
        tabBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 8px"));
        tabBar.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        tabBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        tabBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        tabBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        tabBar.SetValue(HavenProperties.BorderBottom, HavenLength.Px(1));

        EditorTabContainer = new HuiContainer { Name = "Dev.Hui.EditorTabContainer", Layout = HavenLayout.Horizontal };
        EditorTabContainer.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        EditorTabContainer.SetValue(HavenProperties.FlexGrow, 1d);
        tabBar.Add(EditorTabContainer);

        NewEditorTabButton = new HuiButton { Name = "Dev.Hui.NewEditorTab", Content = "+", Variant = ButtonVariant.Ghost };
        NewEditorTabButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(28));
        NewEditorTabButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(24));
        tabBar.Add(NewEditorTabButton);
        editorArea.Add(tabBar);

        // Editor content
        EditorContent = new HuiContainer { Name = "Dev.Hui.EditorContent", Layout = HavenLayout.Absolute };
        EditorContent.SetValue(HavenProperties.FlexGrow, 1d);
        EditorContent.SetValue(HavenProperties.Background, "Surface");
        EditorContent.SetValue(HavenProperties.Overflow, HavenOverflow.Auto);
        EditorContent.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));

        EditorText = new HuiText("") { Name = "Dev.Hui.EditorText", Level = TextLevel.Body1 };
        EditorText.SetValue(HavenProperties.FontFamily, "Monospace");
        EditorText.SetValue(HavenProperties.FontSize, HavenLength.Px(13));
        EditorText.SetValue(HavenProperties.LineHeight, HavenLength.Px(20));
        EditorText.SetValue(HavenProperties.WhiteSpace, "pre");
        EditorContent.Add(EditorText);
        editorArea.Add(EditorContent);
        Root.Add(editorArea);

        // Panel - Terminal, Output, Problems
        var panel = new HuiContainer { Name = "Dev.Hui.Panel", Layout = HavenLayout.Vertical };
        panel.SetValue(HavenProperties.Row, 1);
        panel.SetValue(HavenProperties.Column, 2);
        panel.SetValue(HavenProperties.Background, "SurfaceVariant");
        panel.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        panel.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        panel.SetValue(HavenProperties.BorderLeft, HavenLength.Px(1));

        var panelTabs = new HuiContainer { Name = "Dev.Hui.PanelTabs", Layout = HavenLayout.Horizontal };
        panelTabs.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
        panelTabs.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        panelTabs.SetValue(HavenProperties.Background, "Surface");

        ProblemsTab = NewPanelTab("Dev.Hui.ProblemsTab", "Problems", 0);
        OutputTab = NewPanelTab("Dev.Hui.OutputTab", "Output", 1);
        TerminalTab = NewPanelTab("Dev.Hui.TerminalTab", "Terminal", 2);
        DebugTab = NewPanelTab("Dev.Hui.DebugTab", "Debug", 3);

        panelTabs.Add(ProblemsTab);
        panelTabs.Add(OutputTab);
        panelTabs.Add(TerminalTab);
        panelTabs.Add(DebugTab);
        panel.Add(panelTabs);

        PanelContent = new HuiContainer { Name = "Dev.Hui.PanelContent", Layout = HavenLayout.Vertical };
        PanelContent.SetValue(HavenProperties.FlexGrow, 1d);
        PanelContent.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
        PanelContent.SetValue(HavenProperties.Overflow, HavenOverflow.Auto);
        panel.Add(PanelContent);
        Root.Add(panel);

        // Status bar
        var statusBar = new HuiContainer { Name = "Dev.Hui.StatusBar", Layout = HavenLayout.Horizontal };
        statusBar.SetValue(HavenProperties.Row, 2);
        statusBar.SetValue(HavenProperties.ColumnSpan, 3);
        statusBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 12px"));
        statusBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        statusBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        statusBar.SetValue(HavenProperties.BorderTop, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        WorkspaceText = new HuiText("No workspace") { Name = "Dev.Hui.Workspace", Level = TextLevel.Caption };
        WorkspaceText.SetValue(HavenProperties.Foreground, "TextSecondary");
        FileInfoText = new HuiText("") { Name = "Dev.Hui.FileInfo", Level = TextLevel.Caption };
        FileInfoText.SetValue(HavenProperties.Foreground, "TextSecondary");
        CursorPosText = new HuiText("Ln 1, Col 1") { Name = "Dev.Hui.CursorPos", Level = TextLevel.Caption };
        CursorPosText.SetValue(HavenProperties.Foreground, "TextSecondary");
        EncodingText = new HuiText("UTF-8") { Name = "Dev.Hui.Encoding", Level = TextLevel.Caption };
        EncodingText.SetValue(HavenProperties.Foreground, "TextSecondary");
        LanguageText = new HuiText("Plain Text") { Name = "Dev.Hui.Language", Level = TextLevel.Caption };
        LanguageText.SetValue(HavenProperties.Foreground, "TextSecondary");

        statusBar.Add(WorkspaceText);
        statusBar.Add(new HuiContainer { Name = "Dev.Hui.StatusSpacer" });
        statusBar.Add(FileInfoText);
        statusBar.Add(CursorPosText);
        statusBar.Add(EncodingText);
        statusBar.Add(LanguageText);
        Root.Add(statusBar);

        Root.ValidateUniqueNames();
    }

    public Page Root { get; }
    public HuiContainer FileTree { get; }
    public HuiButton NewFileButton { get; }
    public HuiButton NewFolderButton { get; }
    public HuiContainer EditorTabContainer { get; }
    public HuiButton NewEditorTabButton { get; }
    public HuiContainer EditorContent { get; }
    public HuiText EditorText { get; }
    public HuiButton ProblemsTab { get; }
    public HuiButton OutputTab { get; }
    public HuiButton TerminalTab { get; }
    public HuiButton DebugTab { get; }
    public HuiContainer PanelContent { get; }
    public HuiText WorkspaceText { get; }
    public HuiText FileInfoText { get; }
    public HuiText CursorPosText { get; }
    public HuiText EncodingText { get; }
    public HuiText LanguageText { get; }
    public string? ActiveFile => _activeFile;

    public bool TryDequeueAction(out DevHuiAction action) => _actions.TryDequeue(out action);

    public void SetWorkspace(DevWorkspace workspace)
    {
        WorkspaceText.Content = workspace.Name;
        FileTree.Clear();
        _fileTreeItems.Clear();
        AddFolderToTree(workspace.Path, workspace.Name, 0);
    }

    private void AddFolderToTree(string path, string name, int depth)
    {
        var item = new HuiContainer { Name = $"Dev.Hui.TreeItem.{path}", Layout = HavenLayout.Horizontal };
        item.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        item.SetValue(HavenProperties.Padding, HavenThickness.Parse($"{depth * 16}px 8px 4px 8px"));

        var expandBtn = new HuiButton { Name = $"Dev.Hui.Expand.{path}", Content = "▶", Variant = ButtonVariant.Ghost };
        expandBtn.SetValue(HavenProperties.MinWidth, HavenLength.Px(16));
        expandBtn.SetValue(HavenProperties.MinHeight, HavenLength.Px(16));

        var label = new HuiButton { Name = $"Dev.Hui.Label.{path}", Content = name, Variant = ButtonVariant.Ghost };
        label.SetValue(HavenProperties.TextAlignment, HavenTextAlignment.Start);
        label.SetValue(HavenProperties.FlexGrow, 1d);
        label.SetValue(HavenProperties.Padding, HavenThickness.Parse("0"));

        var captured = path;
        label.Invoked += (_, _) => SelectFile(captured);
        expandBtn.Invoked += (_, _) => ToggleFolder(captured);

        item.Add(expandBtn);
        item.Add(label);
        _fileTreeItems[path] = label;
        FileTree.Add(item);
    }

    public void SetFiles(IReadOnlyList<DevFile> files, string folderPath)
    {
        // Would add files to tree under folder
    }

    public void OpenFile(DevFile file)
    {
        if (_editorTabs.ContainsKey(file.Path)) return;

        var tab = new HuiButton
        {
            Name = $"Dev.Hui.EditorTab.{file.Path}",
            Content = file.Name,
            Variant = ButtonVariant.Ghost,
        };
        tab.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
        tab.SetValue(HavenProperties.Padding, HavenThickness.Parse("0 12px"));
        tab.SetValue(HavenProperties.BorderRadius, HavenLength.Px(4));

        var captured = file.Path;
        tab.Invoked += (_, _) => SelectFile(captured);
        
        _editorTabs[file.Path] = tab;
        EditorTabContainer.Add(tab);
        SelectFile(file.Path);
    }

    public void CloseFile(string path)
    {
        if (_editorTabs.TryGetValue(path, out var tab))
        {
            EditorTabContainer.Remove(tab);
            _editorTabs.Remove(path);
        }
        if (_activeFile == path)
        {
            _activeFile = _editorTabs.Keys.FirstOrDefault();
            if (_activeFile != null)
                SelectFile(_activeFile);
        }
    }

    public void SelectFile(string path)
    {
        _activeFile = path;
        foreach (var kvp in _editorTabs)
        {
            var selected = kvp.Key == path;
            kvp.Value.SetState(HavenElementState.Selected, selected);
            kvp.Value.SetValue(HavenProperties.Background, selected ? "Surface" : "Transparent");
        }
        FileInfoText.Content = path;
    }

    public void SetEditorContent(string content)
    {
        EditorText.Content = content;
    }

    public void SetCursorPosition(int line, int col)
    {
        CursorPosText.Content = $"Ln {line}, Col {col}";
    }

    public void SetLanguage(string language)
    {
        LanguageText.Content = language;
    }

    public void SetPanelContent(string panel, string content)
    {
        PanelContent.Clear();
        var text = new HuiText(content) { Name = $"Dev.Hui.Panel.{panel}", Level = TextLevel.Body2 };
        text.SetValue(HavenProperties.FontFamily, "Monospace");
        text.SetValue(HavenProperties.FontSize, HavenLength.Px(11));
        text.SetValue(HavenProperties.LineHeight, HavenLength.Px(16));
        text.SetValue(HavenProperties.Foreground, "TextSecondary");
        PanelContent.Add(text);
    }

    public void SetTerminalOutput(string output)
    {
        SetPanelContent("terminal", output);
    }

    private void ToggleFolder(string path)
    {
        // Would expand/collapse folder
    }

    private HuiButton NewPanelTab(string name, string content, int index)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = index == 0 ? ButtonVariant.Primary : ButtonVariant.Ghost,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(24));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("0 8px"));
        return button;
    }

    private HuiContainer NewMenuButton(string label, DevHuiAction[] actions)
    {
        var container = new HuiContainer { Name = $"Dev.Hui.Menu.{label}", Layout = HavenLayout.Horizontal };
        container.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        
        var button = new HuiButton
        {
            Name = $"Dev.Hui.MenuBtn.{label}",
            Content = label,
            Variant = ButtonVariant.Ghost,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 8px"));
        
        if (actions.Length > 0)
        {
            var action = actions[0];
            button.Invoked += (_, _) => _actions.Enqueue(action);
        }
        
        container.Add(button);
        return container;
    }
}

public sealed class DevHuiController(DevEngine engine, DevHuiScene? scene = null)
{
    private readonly DevEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public DevHuiScene Scene { get; } = scene ?? new DevHuiScene();

    public async Task InitializeAsync()
    {
        try
        {
            // Try to open current directory as workspace
            var cwd = Environment.CurrentDirectory;
            var workspace = await _engine.OpenWorkspaceAsync(cwd);
            if (workspace != null)
            {
                Scene.SetWorkspace(workspace);
            }
        }
        catch (Exception ex)
        {
            // Ignore
        }
    }

    public async Task ExecuteAsync(DevHuiAction action)
    {
        try
        {
            switch (action)
            {
                case DevHuiAction.OpenFolder:
                    // Would show folder picker
                    break;
                case DevHuiAction.OpenWorkspace:
                    // Would show workspace picker
                    break;
                case DevHuiAction.NewFile:
                    // Create new untitled file
                    break;
                case DevHuiAction.NewFolder:
                    // Create new folder
                    break;
                case DevHuiAction.SaveFile:
                    if (Scene.ActiveFile != null)
                    {
                        await _engine.WriteFileAsync(Scene.ActiveFile, Scene.EditorText.Content);
                    }
                    break;
                case DevHuiAction.SaveAll:
                    // Save all dirty files
                    break;
                case DevHuiAction.ToggleSidebar:
                    // Toggle sidebar visibility
                    break;
                case DevHuiAction.TogglePanel:
                    // Toggle panel visibility
                    break;
                case DevHuiAction.ToggleTerminal:
                    // Toggle terminal panel
                    break;
                case DevHuiAction.RunCommand:
                    // Show command palette
                    break;
                case DevHuiAction.InstallExtension:
                    // Show extension marketplace
                    break;
                case DevHuiAction.StartDebug:
                    // Start debugging
                    break;
                case DevHuiAction.StopDebug:
                    // Stop debugging
                    break;
                case DevHuiAction.RunTask:
                    // Run task
                    break;
                case DevHuiAction.Settings:
                    // Open settings
                    break;
            }
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }
}