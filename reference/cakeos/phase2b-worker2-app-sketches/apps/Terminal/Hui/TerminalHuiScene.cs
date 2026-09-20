using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;
using HuiContainer = Haven.UI.Components.Container;

namespace HavenOS.Apps.Terminal.Hui;

public enum TerminalHuiAction
{
    NewSession,
    CloseSession,
    SplitHorizontal,
    SplitVertical,
    Copy,
    Paste,
    IncreaseFontSize,
    DecreaseFontSize,
    ResetTerminal,
    ClearScrollback,
    ToggleFullscreen,
}

public sealed class TerminalHuiScene
{
    private readonly Queue<TerminalHuiAction> _actions = new();
    private readonly Dictionary<string, HuiButton> _sessionTabs = new();
    private string? _activeSessionId;

    public TerminalHuiScene()
    {
        Root = new Page
        {
            Name = "Terminal.Hui.Root",
            Layout = HavenLayout.Grid,
            Columns = "1fr",
            Rows = "Auto 1fr Auto",
        };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.FontFamily, "Monospace");

        // Tab bar
        var tabBar = new HuiContainer { Name = "Terminal.Hui.TabBar", Layout = HavenLayout.Horizontal };
        tabBar.SetValue(HavenProperties.Row, 0);
        tabBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 8px"));
        tabBar.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        tabBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        tabBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        tabBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        tabBar.SetValue(HavenProperties.BorderBottom, HavenLength.Px(1));

        TabContainer = new HuiContainer { Name = "Terminal.Hui.TabContainer", Layout = HavenLayout.Horizontal };
        TabContainer.SetValue(HavenProperties.Gap, HavenLength.Px(2));
        TabContainer.SetValue(HavenProperties.FlexGrow, 1d);
        tabBar.Add(TabContainer);

        NewTabButton = new HuiButton
        {
            Name = "Terminal.Hui.NewTab",
            Content = "+",
            Variant = ButtonVariant.Ghost,
        };
        NewTabButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(32));
        NewTabButton.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
        NewTabButton.Invoked += (_, _) => _actions.Enqueue(TerminalHuiAction.NewSession);
        tabBar.Add(NewTabButton);

        Root.Add(tabBar);

        // Terminal area
        TerminalArea = new HuiContainer { Name = "Terminal.Hui.TerminalArea", Layout = HavenLayout.Absolute };
        TerminalArea.SetValue(HavenProperties.Row, 1);
        TerminalArea.SetValue(HavenProperties.Background, "Surface");
        TerminalArea.SetValue(HavenProperties.Overflow, HavenOverflow.Hidden);
        
        TerminalContent = new HuiText("") { Name = "Terminal.Hui.Content", Level = TextLevel.Body1 };
        TerminalContent.SetValue(HavenProperties.FontFamily, "Monospace");
        TerminalContent.SetValue(HavenProperties.FontSize, HavenLength.Px(13));
        TerminalContent.SetValue(HavenProperties.LineHeight, HavenLength.Px(18));
        TerminalContent.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        TerminalContent.SetValue(HavenProperties.WhiteSpace, "pre");
        TerminalArea.Add(TerminalContent);
        Root.Add(TerminalArea);

        // Status bar
        var statusBar = new HuiContainer { Name = "Terminal.Hui.StatusBar", Layout = HavenLayout.Horizontal };
        statusBar.SetValue(HavenProperties.Row, 2);
        statusBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 12px"));
        statusBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        statusBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        statusBar.SetValue(HavenProperties.BorderTop, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        SessionInfoText = new HuiText("No session") { Name = "Terminal.Hui.SessionInfo", Level = TextLevel.Caption };
        SessionInfoText.SetValue(HavenProperties.Foreground, "TextSecondary");
        CursorPosText = new HuiText("1,1") { Name = "Terminal.Hui.CursorPos", Level = TextLevel.Caption };
        CursorPosText.SetValue(HavenProperties.Foreground, "TextSecondary");
        EncodingText = new HuiText("UTF-8") { Name = "Terminal.Hui.Encoding", Level = TextLevel.Caption };
        EncodingText.SetValue(HavenProperties.Foreground, "TextSecondary");

        statusBar.Add(SessionInfoText);
        statusBar.Add(new HuiContainer { Name = "Terminal.Hui.Spacer" });
        statusBar.Add(CursorPosText);
        statusBar.Add(EncodingText);
        Root.Add(statusBar);

        Root.ValidateUniqueNames();
    }

    public Page Root { get; }
    public HuiContainer TabContainer { get; }
    public HuiButton NewTabButton { get; }
    public HuiContainer TerminalArea { get; }
    public HuiText TerminalContent { get; }
    public HuiText SessionInfoText { get; }
    public HuiText CursorPosText { get; }
    public HuiText EncodingText { get; }
    public string? ActiveSessionId => _activeSessionId;

    public bool TryDequeueAction(out TerminalHuiAction action) => _actions.TryDequeue(out action);

    public void AddSession(string sessionId, string title)
    {
        var tab = new HuiButton
        {
            Name = $"Terminal.Hui.Tab.{sessionId}",
            Content = title,
            Variant = ButtonVariant.Ghost,
        };
        tab.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
        tab.SetValue(HavenProperties.Padding, HavenThickness.Parse("0 12px"));
        tab.SetValue(HavenProperties.BorderRadius, HavenLength.Px(4));
        
        var captured = sessionId;
        tab.Invoked += (_, _) => SelectSession(captured);
        
        // Close button would be part of tab
        _sessionTabs[sessionId] = tab;
        TabContainer.Add(tab);
        
        if (_activeSessionId == null)
            SelectSession(sessionId);
    }

    public void RemoveSession(string sessionId)
    {
        if (_sessionTabs.TryGetValue(sessionId, out var tab))
        {
            TabContainer.Remove(tab);
            _sessionTabs.Remove(sessionId);
        }
        if (_activeSessionId == sessionId)
        {
            _activeSessionId = _sessionTabs.Keys.FirstOrDefault();
            if (_activeSessionId != null)
                SelectSession(_activeSessionId);
        }
    }

    public void SelectSession(string sessionId)
    {
        _activeSessionId = sessionId;
        foreach (var kvp in _sessionTabs)
        {
            var selected = kvp.Key == sessionId;
            kvp.Value.SetState(HavenElementState.Selected, selected);
            kvp.Value.SetValue(HavenProperties.Background, selected ? "Surface" : "Transparent");
        }
        SessionInfoText.Content = sessionId;
    }

    public void SetTerminalContent(string content)
    {
        TerminalContent.Content = content;
    }

    public void AppendTerminalContent(string content)
    {
        TerminalContent.Content += content;
    }

    public void SetCursorPosition(int row, int col)
    {
        CursorPosText.Content = $"{row + 1},{col + 1}";
    }

    public void SetSessionStatus(string sessionId, bool running, int? exitCode = null)
    {
        if (_sessionTabs.TryGetValue(sessionId, out var tab))
        {
            var title = tab.Content;
            if (!running)
                title += exitCode.HasValue ? $" (exit {exitCode})" : " (exited)";
            tab.Content = title;
        }
    }

    private HuiButton NewActionButton(string name, string content, TerminalHuiAction action)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = ButtonVariant.Secondary,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(28));
        button.Invoked += (_, _) => _actions.Enqueue(action);
        return button;
    }
}

public sealed class TerminalHuiController(TerminalEngine engine, TerminalHuiScene? scene = null)
{
    private readonly TerminalEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));

    public TerminalHuiScene Scene { get; } = scene ?? new TerminalHuiScene();

    public async Task InitializeAsync()
    {
        // Create default session
        await CreateDefaultSessionAsync();
    }

    public async Task CreateDefaultSessionAsync()
    {
        try
        {
            var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
            var session = await _engine.CreateSessionAsync(shell, Array.Empty<string>(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), new Dictionary<string, string>());
            Scene.AddSession(session.Id, $"bash ({session.Id})");
            Scene.SelectSession(session.Id);
            
            // Attach output handler
            var terminalSession = await _engine.GetSessionAsync(session.Id);
            if (terminalSession != null)
            {
                // Would attach to session output
            }
        }
        catch (Exception ex)
        {
            Scene.SetSessionStatus("error", false, -1);
        }
    }

    public async Task ExecuteAsync(TerminalHuiAction action)
    {
        try
        {
            switch (action)
            {
                case TerminalHuiAction.NewSession:
                    await CreateDefaultSessionAsync();
                    break;
                case TerminalHuiAction.CloseSession:
                    if (Scene.ActiveSessionId != null)
                    {
                        var session = await _engine.GetSessionAsync(Scene.ActiveSessionId);
                        if (session != null)
                        {
                            await session.DisposeAsync();
                        }
                        Scene.RemoveSession(Scene.ActiveSessionId);
                    }
                    break;
                case TerminalHuiAction.SplitHorizontal:
                case TerminalHuiAction.SplitVertical:
                case TerminalHuiAction.Copy:
                case TerminalHuiAction.Paste:
                case TerminalHuiAction.IncreaseFontSize:
                case TerminalHuiAction.DecreaseFontSize:
                case TerminalHuiAction.ResetTerminal:
                case TerminalHuiAction.ClearScrollback:
                case TerminalHuiAction.ToggleFullscreen:
                    // Not implemented
                    break;
            }
        }
        catch (Exception ex)
        {
            // Handle error
        }
    }

    public async Task SendInputAsync(string text)
    {
        if (Scene.ActiveSessionId != null)
        {
            var session = await _engine.GetSessionAsync(Scene.ActiveSessionId);
            if (session != null)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                await session.WriteAsync(bytes);
            }
        }
    }
}