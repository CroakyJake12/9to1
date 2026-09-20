using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;
using HuiContainer = Haven.UI.Components.Container;

namespace HavenOS.Apps.Wine.Hui;

public enum WineHuiAction
{
    RefreshApps,
    LaunchApp,
    StopApp,
    ViewLogs,
    RegisterApp,
    UnregisterApp,
    ResetApp,
}

public sealed class WineHuiScene
{
    private readonly Queue<WineHuiAction> _actions = new();
    private readonly Dictionary<string, HuiButton> _appActionButtons = new();
    private string? _selectedAppId;

    public WineHuiScene()
    {
        Root = new Page
        {
            Name = "Wine.Hui.Root",
            Layout = HavenLayout.Grid,
            Columns = "320px 1fr",
            Rows = "Auto 1fr Auto",
        };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Background, "Surface");

        // Sidebar - App list
        var sidebar = new HuiContainer { Name = "Wine.Hui.Sidebar", Layout = HavenLayout.Vertical };
        sidebar.SetValue(HavenProperties.Row, 1);
        sidebar.SetValue(HavenProperties.Column, 0);
        sidebar.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        sidebar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        sidebar.SetValue(HavenProperties.Background, "SurfaceVariant");
        sidebar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        sidebar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        sidebar.SetValue(HavenProperties.BorderRight, HavenLength.Px(1));

        var sidebarHeader = new HuiContainer { Name = "Wine.Hui.SidebarHeader", Layout = HavenLayout.Horizontal };
        sidebarHeader.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        sidebarHeader.Add(new HuiText("Windows Apps (Wine)") { Name = "Wine.Hui.Title", Level = TextLevel.H3 });
        RefreshButton = NewActionButton("Wine.Hui.Refresh", "Refresh", WineHuiAction.RefreshApps);
        RegisterButton = NewActionButton("Wine.Hui.Register", "Register", WineHuiAction.RegisterApp);
        sidebarHeader.Add(RefreshButton);
        sidebarHeader.Add(RegisterButton);
        sidebar.Add(sidebarHeader);

        AppList = new HuiContainer { Name = "Wine.Hui.AppList", Layout = HavenLayout.Vertical };
        AppList.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        AppList.SetValue(HavenProperties.Overflow, HavenOverflow.Auto);
        sidebar.Add(AppList);
        Root.Add(sidebar);

        // Main content - App details
        var mainArea = new HuiContainer { Name = "Wine.Hui.MainArea", Layout = HavenLayout.Vertical };
        mainArea.SetValue(HavenProperties.Row, 1);
        mainArea.SetValue(HavenProperties.Column, 1);
        mainArea.SetValue(HavenProperties.Padding, HavenThickness.Parse("24px"));
        mainArea.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        DetailTitle = new HuiText("Select a Windows app") { Name = "Wine.Hui.DetailTitle", Level = TextLevel.H2 };
        mainArea.Add(DetailTitle);

        DetailSubtitle = new HuiText("") { Name = "Wine.Hui.DetailSubtitle", Level = TextLevel.Body2 };
        DetailSubtitle.SetValue(HavenProperties.Foreground, "TextSecondary");
        mainArea.Add(DetailSubtitle);

        DetailRuntime = new HuiText("") { Name = "Wine.Hui.DetailRuntime", Level = TextLevel.Caption };
        DetailRuntime.SetValue(HavenProperties.Foreground, "TextSecondary");
        mainArea.Add(DetailRuntime);

        DetailPermissions = new HuiContainer { Name = "Wine.Hui.DetailPermissions", Layout = HavenLayout.Vertical };
        DetailPermissions.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        mainArea.Add(DetailPermissions);

        DetailMounts = new HuiContainer { Name = "Wine.Hui.DetailMounts", Layout = HavenLayout.Vertical };
        DetailMounts.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        mainArea.Add(DetailMounts);

        // Action buttons
        var actionBar = new HuiContainer { Name = "Wine.Hui.ActionBar", Layout = HavenLayout.Horizontal };
        actionBar.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        actionBar.SetValue(HavenProperties.Alignment, HavenAlignment.Start);

        LaunchButton = NewActionButton("Wine.Hui.Launch", "Launch", WineHuiAction.LaunchApp);
        StopButton = NewActionButton("Wine.Hui.Stop", "Stop", WineHuiAction.StopApp);
        LogsButton = NewActionButton("Wine.Hui.Logs", "View Logs", WineHuiAction.ViewLogs);
        ResetButton = NewActionButton("Wine.Hui.Reset", "Reset Prefix", WineHuiAction.ResetApp);
        UnregisterButton = NewActionButton("Wine.Hui.Unregister", "Unregister", WineHuiAction.UnregisterApp);

        actionBar.Add(LaunchButton);
        actionBar.Add(StopButton);
        actionBar.Add(LogsButton);
        actionBar.Add(ResetButton);
        actionBar.Add(UnregisterButton);
        mainArea.Add(actionBar);

        // Log output
        LogOutput = new HuiText("") { Name = "Wine.Hui.LogOutput", Level = TextLevel.Caption };
        LogOutput.SetValue(HavenProperties.Foreground, "TextSecondary");
        LogOutput.SetValue(HavenProperties.FontFamily, "Monospace");
        LogOutput.SetValue(HavenProperties.MaxHeight, HavenLength.Px(200));
        LogOutput.SetValue(HavenProperties.Overflow, HavenOverflow.Auto);
        mainArea.Add(LogOutput);

        Root.Add(mainArea);

        // Status bar
        var statusBar = new HuiContainer { Name = "Wine.Hui.StatusBar", Layout = HavenLayout.Horizontal };
        statusBar.SetValue(HavenProperties.Row, 2);
        statusBar.SetValue(HavenProperties.ColumnSpan, 2);
        statusBar.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px 24px"));
        statusBar.SetValue(HavenProperties.Background, "SurfaceVariant");
        statusBar.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.BorderColor, "SurfaceBorder");
        statusBar.SetValue(HavenProperties.BorderTop, HavenLength.Px(1));
        statusBar.SetValue(HavenProperties.Gap, HavenLength.Px(16));

        StatusText = new HuiText("Ready") { Name = "Wine.Hui.Status", Level = TextLevel.Body2 };
        StatusText.SetValue(HavenProperties.Foreground, "TextSecondary");
        CapabilitiesText = new HuiText("") { Name = "Wine.Hui.Capabilities", Level = TextLevel.Caption };
        CapabilitiesText.SetValue(HavenProperties.Foreground, "TextSecondary");
        statusBar.Add(StatusText);
        statusBar.Add(new HuiContainer { Name = "Wine.Hui.Spacer" });
        statusBar.Add(CapabilitiesText);
        Root.Add(statusBar);

        Root.ValidateUniqueNames();
        UpdateActionButtons();
    }

    public Page Root { get; }
    public HuiContainer AppList { get; }
    public HuiButton RefreshButton { get; }
    public HuiButton RegisterButton { get; }
    public HuiText DetailTitle { get; }
    public HuiText DetailSubtitle { get; }
    public HuiText DetailRuntime { get; }
    public HuiContainer DetailPermissions { get; }
    public HuiContainer DetailMounts { get; }
    public HuiButton LaunchButton { get; }
    public HuiButton StopButton { get; }
    public HuiButton LogsButton { get; }
    public HuiButton ResetButton { get; }
    public HuiButton UnregisterButton { get; }
    public HuiText LogOutput { get; }
    public HuiText StatusText { get; }
    public HuiText CapabilitiesText { get; }
    public string? SelectedAppId => _selectedAppId;

    public bool TryDequeueAction(out WineHuiAction action) => _actions.TryDequeue(out action);

    public void SetApps(IReadOnlyList<WineAppSummary> apps)
    {
        AppList.Clear();
        _appActionButtons.Clear();

        foreach (var app in apps)
        {
            var button = new HuiButton
            {
                Name = $"Wine.Hui.App.{app.Id}",
                Content = app.DisplayName,
                Variant = ButtonVariant.Ghost,
            };
            button.SetValue(HavenProperties.MinHeight, HavenLength.Px(40));
            button.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
            button.SetValue(HavenProperties.BorderColor, "Transparent");
            button.SetValue(HavenProperties.TextAlignment, HavenTextAlignment.Start);
            button.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));

            var captured = app.Id;
            button.Invoked += (_, _) => SelectApp(captured);
            _appActionButtons[app.Id] = button;
            AppList.Add(button);
        }
    }

    public void SelectApp(string appId)
    {
        _selectedAppId = appId;
        foreach (var kvp in _appActionButtons)
        {
            var selected = kvp.Key == appId;
            kvp.Value.SetState(HavenElementState.Selected, selected);
            kvp.Value.SetValue(HavenProperties.BorderColor, selected ? "Accent" : "Transparent");
            kvp.Value.SetValue(HavenProperties.Background, selected ? "AccentContainer" : "Transparent");
        }
        UpdateActionButtons();
    }

    public void ShowAppDetails(WineAppSummary app)
    {
        DetailTitle.Content = app.DisplayName;
        DetailSubtitle.Content = $"Backend: {app.Backend} · Runtime: {app.Runtime} · Entrypoint: {app.Entrypoint}";
        DetailRuntime.Content = $"Unit: {app.Unit}";

        DetailPermissions.Clear();
        DetailPermissions.Add(new HuiText("Permissions") { Level = TextLevel.H4 });
        DetailPermissions.Add(new HuiText($"Network: {app.Permissions.Network}"));
        DetailPermissions.Add(new HuiText($"Clipboard: {(app.Permissions.Clipboard ? "Enabled" : "Disabled")}"));
        DetailPermissions.Add(new HuiText($"Audio Output: {(app.Permissions.AudioOutput ? "Enabled" : "Disabled")}"));
        DetailPermissions.Add(new HuiText($"Microphone: {(app.Permissions.Microphone ? "Enabled" : "Disabled")}"));
        DetailPermissions.Add(new HuiText($"GPU: {app.Permissions.Gpu}"));

        DetailMounts.Clear();
        if (app.Permissions.Mounts.Count > 0)
        {
            DetailMounts.Add(new HuiText("Mounts") { Level = TextLevel.H4 });
            foreach (var mount in app.Permissions.Mounts)
            {
                DetailMounts.Add(new HuiText($"{mount.Mode.ToUpper()}: {mount.Source} -> {mount.Target}"));
            }
        }

        UpdateActionButtons();
    }

    public void ShowCapabilities(WineCapabilities caps)
    {
        var wine = caps.Providers.Wine;
        var parts = new List<string>();
        if (wine.Enabled)
        {
            parts.Add($"Wine: Slice {wine.Slice}");
            parts.Add($"Display: {string.Join(", ", wine.Display)}");
            parts.Add($"GPU: {string.Join(", ", wine.Gpu)}");
        }
        else
        {
            parts.Add("Wine: Disabled");
        }
        CapabilitiesText.Content = string.Join(" | ", parts);
    }

    public void SetStatus(string status) => StatusText.Content = status;
    public void SetLogOutput(string logs) => LogOutput.Content = logs;
    public void AppendLogOutput(string log) => LogOutput.Content += "\n" + log;

    private void UpdateActionButtons()
    {
        bool hasSelection = !string.IsNullOrEmpty(_selectedAppId);
        LaunchButton.SetState(HavenElementState.Disabled, !hasSelection);
        StopButton.SetState(HavenElementState.Disabled, !hasSelection);
        LogsButton.SetState(HavenElementState.Disabled, !hasSelection);
        ResetButton.SetState(HavenElementState.Disabled, !hasSelection);
        UnregisterButton.SetState(HavenElementState.Disabled, !hasSelection);
    }

    private HuiButton NewActionButton(string name, string content, WineHuiAction action)
    {
        var button = new HuiButton
        {
            Name = name,
            Content = content,
            Variant = ButtonVariant.Secondary,
        };
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        button.SetValue(HavenProperties.MinWidth, HavenLength.Px(100));
        button.Invoked += (_, _) => _actions.Enqueue(action);
        return button;
    }
}

public sealed class WineHuiController(WineCompatBroker broker, WineHuiScene? scene = null)
{
    private readonly WineCompatBroker _broker = broker ?? throw new ArgumentNullException(nameof(broker));

    public WineHuiScene Scene { get; } = scene ?? new WineHuiScene();

    public async Task InitializeAsync()
    {
        try
        {
            var caps = _broker.Capabilities();
            Scene.ShowCapabilities(caps);
            var health = await _broker.HealthAsync();
            Scene.SetStatus($"Wine: {(health.Audit.WineAvailable ? "Available" : "Unavailable")} · Bubblewrap: {(health.Audit.BubblewrapAvailable ? "Available" : "Unavailable")} · Wayland: {(health.Audit.WaylandAvailable ? "Available" : "Unavailable")}");
            await RefreshAppsAsync();
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Initialization error: {ex.Message}");
        }
    }

    public async Task RefreshAppsAsync()
    {
        try
        {
            var apps = await _broker.ListAppsAsync();
            Scene.SetApps(apps);
            Scene.SetStatus($"Loaded {apps.Count} registered apps");
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Failed to load apps: {ex.Message}");
        }
    }

    public async Task ExecuteAsync(WineHuiAction action)
    {
        if (string.IsNullOrEmpty(Scene.SelectedAppId))
        {
            Scene.SetStatus("No app selected");
            return;
        }

        try
        {
            switch (action)
            {
                case WineHuiAction.RefreshApps:
                    await RefreshAppsAsync();
                    break;

                case WineHuiAction.LaunchApp:
                    Scene.SetStatus("Launching...");
                    var launchStatus = await _broker.LaunchRegisteredAsync(Scene.SelectedAppId!);
                    Scene.SetStatus(launchStatus.Running ? "Launched successfully" : $"Launch failed: {launchStatus.State}");
                    break;

                case WineHuiAction.StopApp:
                    Scene.SetStatus("Stopping...");
                    var stopStatus = await _broker.StopRegisteredAsync(Scene.SelectedAppId!);
                    Scene.SetStatus(stopStatus.Running ? "Stop failed" : "Stopped");
                    break;

                case WineHuiAction.ViewLogs:
                    Scene.SetStatus("Fetching logs...");
                    var logs = await _broker.LogsRegisteredAsync(Scene.SelectedAppId!, 200);
                    Scene.SetLogOutput(logs);
                    Scene.SetStatus("Logs loaded");
                    break;

                case WineHuiAction.RegisterApp:
                    Scene.SetStatus("Register app - not implemented in HUI");
                    break;

                case WineHuiAction.UnregisterApp:
                    Scene.SetStatus("Unregistering...");
                    await _broker.UnregisterAppAsync(Scene.SelectedAppId!);
                    Scene.SelectedAppId = null;
                    await RefreshAppsAsync();
                    break;

                case WineHuiAction.ResetApp:
                    Scene.SetStatus("Resetting prefix...");
                    await _broker.ResetRegisteredAsync(Scene.SelectedAppId!);
                    Scene.SetStatus("Prefix reset");
                    break;
            }
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Error: {ex.Message}");
        }
    }

    public async Task SelectAndShowAppAsync(string appId)
    {
        try
        {
            var manifest = await _broker.GetRegisteredAppAsync(appId);
            var summary = new WineAppSummary(
                manifest.AppId, manifest.DisplayName, manifest.Backend, manifest.Runtime, manifest.Entrypoint,
                _broker.LifecycleUnit(manifest),
                new WinePermissions(manifest.Network, manifest.Clipboard, manifest.AudioOutput, manifest.Microphone, manifest.Gpu, manifest.Mounts));
            Scene.SelectApp(appId);
            Scene.ShowAppDetails(summary);
            var status = await _broker.StatusAsync(manifest);
            Scene.SetStatus($"Status: {status.State} (PID: {status.Pid?.ToString() ?? "N/A"})");
        }
        catch (Exception ex)
        {
            Scene.SetStatus($"Failed to load app details: {ex.Message}");
        }
    }
}