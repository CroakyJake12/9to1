using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;

namespace HavenOS.Home;

public enum HomeCuiAction
{
    InstallAllUpdates,
}

/// <summary>
/// Platform-neutral Home CUI surface. It presents state and emits typed user intent; it has no
/// package-manager, model, voice, or platform authority of its own.
/// </summary>
public sealed class HomeCuiScene
{
    private readonly Queue<HomeCuiAction> _actions = new();

    public HomeCuiScene()
    {
        Root = new Page
        {
            Name = "Home.Cui.Root",
            Layout = HavenLayout.Vertical,
        };
        Root.SetValue(HavenProperties.Padding, HavenThickness.Parse("22px"));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);

        var header = new Container { Name = "Home.Cui.Header", Layout = HavenLayout.Vertical };
        header.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        header.Add(new HuiText("Home") { Name = "Home.Cui.Title", Level = TextLevel.H1 });
        header.Add(new HuiText("Local apps, updates, and capability status")
        {
            Name = "Home.Cui.Subtitle",
            Level = TextLevel.Caption,
        });
        Root.Add(header);

        (CatalogPanel, CatalogItems) = AddSection("Catalog", "Catalog");
        (InstalledAppsPanel, InstalledAppsItems) = AddSection("InstalledApps", "Installed apps");
        (UpdatesPanel, UpdatesItems) = AddSection("Updates", "Updates");
        (SettingsPanel, SettingsItems) = AddSection("Settings", "Settings");
        (RuntimePanel, RuntimeItems) = AddSection("Runtime", "Runtime, model, and voice");

        InstallAllButton = new HuiButton
        {
            Name = "Home.Cui.Updates.InstallAll",
            Content = "Install all updates",
            Variant = ButtonVariant.Primary,
        };
        InstallAllButton.Accessibility.AccessibleName = "Install all available updates";
        InstallAllButton.Invoked += (_, _) => _actions.Enqueue(HomeCuiAction.InstallAllUpdates);
        UpdatesPanel.Add(InstallAllButton);

        OperationStatus = new HuiText
        {
            Name = "Home.Cui.OperationStatus",
            Level = TextLevel.Caption,
        };
        Root.Add(OperationStatus);
        Root.ValidateUniqueNames();
    }

    public Page Root { get; }
    public Container CatalogPanel { get; }
    public Container CatalogItems { get; }
    public Container InstalledAppsPanel { get; }
    public Container InstalledAppsItems { get; }
    public Container UpdatesPanel { get; }
    public Container UpdatesItems { get; }
    public Container SettingsPanel { get; }
    public Container SettingsItems { get; }
    public Container RuntimePanel { get; }
    public Container RuntimeItems { get; }
    public HuiButton InstallAllButton { get; }
    public HuiText OperationStatus { get; }
    public HomeDashboardSnapshot? Snapshot { get; private set; }

    public bool TryDequeueAction(out HomeCuiAction action) => _actions.TryDequeue(out action);

    public void ApplySnapshot(HomeDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;

        PopulateCatalog(snapshot.Catalog);
        PopulateInstalledApps(snapshot.InstalledApps);
        PopulateUpdates(snapshot.Updates);
        PopulateSettings(snapshot.Settings);
        PopulateRuntime(snapshot.Runtime);

        SetEnabled(InstallAllButton, snapshot.CanInstallAll);
        InstallAllButton.Accessibility.Description = snapshot.CanInstallAll
            ? "Installs every update reported by the package backend."
            : snapshot.Updates.Status.Message;
        OperationStatus.Content = $"{snapshot.LastOperation.State}: {snapshot.LastOperation.Message}";
        Root.ValidateUniqueNames();
    }

    private (Container Panel, Container Items) AddSection(string key, string title)
    {
        var panel = new Container
        {
            Name = $"Home.Cui.{key}",
            Layout = HavenLayout.Vertical,
        };
        panel.Accessibility.AccessibleName = title;
        panel.SetValue(HavenProperties.Padding, HavenThickness.Parse("14px"));
        panel.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        panel.SetValue(HavenProperties.Background, "SurfaceRaised");
        panel.SetValue(HavenProperties.BorderColor, "Border");
        panel.SetValue(HavenProperties.BorderWidth, HavenLength.Px(1));
        panel.SetValue(HavenProperties.Radius, HavenCornerRadius.Uniform(HavenLength.Px(14)));
        panel.Add(new HuiText(title) { Name = $"Home.Cui.{key}.Title", Level = TextLevel.H3 });

        var items = new Container
        {
            Name = $"Home.Cui.{key}.Items",
            Layout = HavenLayout.Vertical,
        };
        items.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        panel.Add(items);
        Root.Add(panel);
        return (panel, items);
    }

    private void PopulateCatalog(HomeCatalogSection section)
    {
        Clear(CatalogItems);
        AddStatus(CatalogItems, "Catalog", section.Status);
        if (section.Apps.Count == 0 && section.Status.IsAvailable)
            AddLine(CatalogItems, "Catalog.Empty", "No catalog apps were reported.", TextLevel.Caption);
        for (var index = 0; index < section.Apps.Count; index++)
        {
            var app = section.Apps[index];
            AddLine(CatalogItems, $"Catalog.App.{index}", $"{app.Name} {app.Version} - {app.Description}", TextLevel.Paragraph);
        }
    }

    private void PopulateInstalledApps(HomeInstalledAppsSection section)
    {
        Clear(InstalledAppsItems);
        AddStatus(InstalledAppsItems, "InstalledApps", section.Status);
        if (section.Apps.Count == 0 && section.Status.IsAvailable)
            AddLine(InstalledAppsItems, "InstalledApps.Empty", "No installed apps were reported.", TextLevel.Caption);
        for (var index = 0; index < section.Apps.Count; index++)
        {
            var app = section.Apps[index];
            AddLine(InstalledAppsItems, $"InstalledApps.App.{index}", $"{app.Name} {app.Version}", TextLevel.Paragraph);
        }
    }

    private void PopulateUpdates(HomeUpdatesSection section)
    {
        Clear(UpdatesItems);
        AddStatus(UpdatesItems, "Updates", section.Status);
        if (section.Items.Count == 0 && section.Status.IsAvailable)
            AddLine(UpdatesItems, "Updates.Empty", "No updates are available.", TextLevel.Caption);
        for (var index = 0; index < section.Items.Count; index++)
        {
            var update = section.Items[index];
            AddLine(
                UpdatesItems,
                $"Updates.Item.{index}",
                $"{update.Name}: {update.InstalledVersion} -> {update.AvailableVersion}",
                TextLevel.Paragraph);
        }
    }

    private void PopulateSettings(HomeSettingsSection settings)
    {
        Clear(SettingsItems);
        AddStatus(SettingsItems, "Settings", settings.Status);
        AddLine(
            SettingsItems,
            "Settings.AutomaticUpdates",
            $"Automatic updates: {settings.AutomaticUpdates switch { true => "On", false => "Off", null => "Not configured" }}",
            TextLevel.Paragraph);
        AddLine(
            SettingsItems,
            "Settings.Channel",
            $"Update channel: {settings.UpdateChannel ?? "Not configured"}",
            TextLevel.Paragraph);
    }

    private void PopulateRuntime(HomeRuntimeSection runtime)
    {
        Clear(RuntimeItems);
        AddCapability(RuntimeItems, "Runtime", "Runtime", runtime.Runtime);
        AddCapability(RuntimeItems, "Model", "Model", runtime.Model);
        AddCapability(RuntimeItems, "Voice", "Voice", runtime.Voice);
    }

    private static void AddCapability(Container target, string key, string label, HomeCapabilityStatus status) =>
        AddLine(target, $"Runtime.{key}", $"{label}: {status.State} - {status.Message}", TextLevel.Paragraph);

    private static void AddStatus(Container target, string key, HomeSectionStatus status)
    {
        var text = AddLine(target, $"{key}.Status", $"{status.State}: {status.Message}", TextLevel.Caption);
        if (status.State == HomeSectionState.Error)
            text.SetValue(HavenProperties.Foreground, "Danger");
    }

    private static HuiText AddLine(Container target, string key, string content, TextLevel level)
    {
        var text = new HuiText(content)
        {
            Name = $"Home.Cui.{key}",
            Level = level,
        };
        target.Add(text);
        return text;
    }

    private static void Clear(Container target)
    {
        foreach (var child in target.Children.ToArray())
            target.Remove(child);
    }

    private static void SetEnabled(HuiButton button, bool enabled)
    {
        button.SetValue(HavenProperties.Enabled, enabled);
        button.SetState(HavenElementState.Disabled, !enabled);
    }
}

/// <summary>Maps CUI intent to the Home domain service and re-projects each observed snapshot.</summary>
public sealed class HomeCuiController(HomeDashboard dashboard, HomeCuiScene? scene = null)
{
    private readonly HomeDashboard _dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));

    public HomeCuiScene Scene { get; } = scene ?? new HomeCuiScene();

    public HomeDashboardSnapshot ShowCurrent()
    {
        var snapshot = _dashboard.Current;
        Scene.ApplySnapshot(snapshot);
        return snapshot;
    }

    public async Task<HomeDashboardSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _dashboard.RefreshAsync(cancellationToken).ConfigureAwait(false);
        Scene.ApplySnapshot(snapshot);
        return snapshot;
    }

    public async Task<HomeDashboardSnapshot> ExecuteAsync(
        HomeCuiAction action,
        CancellationToken cancellationToken = default)
    {
        var snapshot = action switch
        {
            HomeCuiAction.InstallAllUpdates => await _dashboard.InstallAllAsync(cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        Scene.ApplySnapshot(snapshot);
        return snapshot;
    }
}
