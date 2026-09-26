using Haven.Browser;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;

namespace HavenOS.Apps.Browse;

public enum BrowseCuiActionKind
{
    Navigate, Back, Forward, Reload, Stop, NewTab, NewPrivateTab, CloseTab, SelectTab,
    ToggleBookmark, ClearHistory, RefreshDownloads, FindNext, FindPrevious,
    ZoomIn, ZoomOut, ZoomReset, AllowPopups, DenyPopups, Recover,
    EngineGeckoTab, EngineChromiumTab, EngineGeckoSite, EngineChromiumSite,
    EngineDefaultGecko, EngineDefaultChromium
}

public sealed record BrowseCuiAction(BrowseCuiActionKind Kind, string? Value = null, Guid? TabId = null);

/// <summary>Haven.UI-owned browser chrome. The Web element is only a native-engine slot.</summary>
public sealed class BrowseCuiScene
{
    private readonly Queue<BrowseCuiAction> _actions = new();
    private bool _syncing;

    public BrowseCuiScene()
    {
        Root = new Page { Name = "Browse.Cui.Root", Layout = HavenLayout.Grid, Columns = "1fr", Rows = "Auto Auto Auto 1fr Auto" };
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.SetValue(HavenProperties.Overflow, HavenOverflow.Clip);

        var tabRow = new Container { Name = "Browse.Cui.TabRow", Layout = HavenLayout.Grid, Columns = "1fr Auto Auto Auto", Rows = "54px" };
        Tabs = new TabStrip { Name = "Browse.Cui.Tabs" };
        Tabs.SetValue(HavenProperties.Column, 0);
        NewTabButton = Button("Browse.Cui.NewTab", "+ tab", BrowseCuiActionKind.NewTab, 1);
        PrivateTabButton = Button("Browse.Cui.NewPrivateTab", "Private", BrowseCuiActionKind.NewPrivateTab, 2);
        CloseTabButton = Button("Browse.Cui.CloseTab", "Close", BrowseCuiActionKind.CloseTab, 3);
        tabRow.Add(Tabs); tabRow.Add(NewTabButton); tabRow.Add(PrivateTabButton); tabRow.Add(CloseTabButton);
        Root.Add(tabRow);

        var navigation = new Container { Name = "Browse.Cui.Navigation", Layout = HavenLayout.Grid, Columns = "Auto Auto Auto Auto 1fr Auto", Rows = "48px" };
        navigation.SetValue(HavenProperties.Row, 1);
        navigation.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        navigation.SetValue(HavenProperties.Padding, HavenThickness.Parse("2px 8px"));
        BackButton = Nav("Browse.Cui.Back", "Back", BrowseCuiActionKind.Back, 0);
        ForwardButton = Nav("Browse.Cui.Forward", "Forward", BrowseCuiActionKind.Forward, 1);
        ReloadButton = Nav("Browse.Cui.Reload", "Reload", BrowseCuiActionKind.Reload, 2);
        StopButton = Nav("Browse.Cui.Stop", "Stop", BrowseCuiActionKind.Stop, 3);
        AddressInput = new Input { Name = "Browse.Cui.Address", Placeholder = "Search or enter address", SubmitOnEnter = true };
        AddressInput.Accessibility.AccessibleName = "Address and search";
        AddressInput.SetValue(HavenProperties.Column, 4);
        BookmarkButton = Nav("Browse.Cui.Bookmark", "Bookmark", BrowseCuiActionKind.ToggleBookmark, 5);
        navigation.Add(BackButton); navigation.Add(ForwardButton); navigation.Add(ReloadButton); navigation.Add(StopButton); navigation.Add(AddressInput); navigation.Add(BookmarkButton);
        Root.Add(navigation);

        var tools = new Container { Name = "Browse.Cui.Tools", Layout = HavenLayout.Horizontal };
        tools.SetValue(HavenProperties.Row, 2);
        tools.SetValue(HavenProperties.Gap, HavenLength.Px(4));
        tools.SetValue(HavenProperties.Padding, HavenThickness.Parse("4px 8px"));
        tools.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        FindInput = new Input { Name = "Browse.Cui.Find", Placeholder = "Find on page", SubmitOnEnter = true };
        FindInput.SetValue(HavenProperties.Width, HavenLength.Px(220));
        tools.Add(FindInput);
        tools.Add(Tool("Browse.Cui.FindPrevious", "Previous", BrowseCuiActionKind.FindPrevious));
        tools.Add(Tool("Browse.Cui.FindNext", "Next", BrowseCuiActionKind.FindNext));
        tools.Add(Tool("Browse.Cui.ZoomOut", "Zoom -", BrowseCuiActionKind.ZoomOut));
        ZoomText = new HuiText("100%") { Name = "Browse.Cui.Zoom", Level = TextLevel.Caption };
        tools.Add(ZoomText);
        tools.Add(Tool("Browse.Cui.ZoomIn", "Zoom +", BrowseCuiActionKind.ZoomIn));
        tools.Add(Tool("Browse.Cui.ZoomReset", "Reset", BrowseCuiActionKind.ZoomReset));
        tools.Add(Tool("Browse.Cui.Downloads", "Downloads", BrowseCuiActionKind.RefreshDownloads));
        tools.Add(Tool("Browse.Cui.ClearHistory", "Clear history", BrowseCuiActionKind.ClearHistory));
        tools.Add(Tool("Browse.Cui.AllowPopups", "Allow popups", BrowseCuiActionKind.AllowPopups));
        tools.Add(Tool("Browse.Cui.DenyPopups", "Block popups", BrowseCuiActionKind.DenyPopups));
        tools.Add(Tool("Browse.Cui.Engine.GeckoTab", "Use Gecko in tab", BrowseCuiActionKind.EngineGeckoTab));
        tools.Add(Tool("Browse.Cui.Engine.ChromiumTab", "Use Chromium in tab", BrowseCuiActionKind.EngineChromiumTab));
        tools.Add(Tool("Browse.Cui.Engine.GeckoSite", "Prefer Gecko for site", BrowseCuiActionKind.EngineGeckoSite));
        tools.Add(Tool("Browse.Cui.Engine.ChromiumSite", "Prefer Chromium for site", BrowseCuiActionKind.EngineChromiumSite));
        tools.Add(Tool("Browse.Cui.Engine.DefaultGecko", "Set Gecko default", BrowseCuiActionKind.EngineDefaultGecko));
        tools.Add(Tool("Browse.Cui.Engine.DefaultChromium", "Set Chromium default", BrowseCuiActionKind.EngineDefaultChromium));
        Root.Add(tools);

        var content = new Container { Name = "Browse.Cui.Content", Layout = HavenLayout.Grid, Columns = "1fr Auto", Rows = "1fr" };
        content.SetValue(HavenProperties.Row, 3);
        WebSurface = new Web { Name = "Browse.Cui.Web" };
        WebSurface.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        WebSurface.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        content.Add(WebSurface);
        SidePanel = new Container { Name = "Browse.Cui.SidePanel", Layout = HavenLayout.Vertical };
        SidePanel.SetValue(HavenProperties.Column, 1);
        SidePanel.SetValue(HavenProperties.Width, HavenLength.Px(340));
        SidePanel.SetValue(HavenProperties.MaxWidth, HavenLength.Percent(42));
        SidePanel.SetValue(HavenProperties.Padding, HavenThickness.Parse("12px"));
        SidePanel.SetValue(HavenProperties.Gap, HavenLength.Px(7));
        SidePanel.SetValue(HavenProperties.Background, "SurfaceRaised");
        SidePanel.SetValue(HavenProperties.Overflow, HavenOverflow.Scroll);
        SecurityText = Line("Browse.Cui.Security", "Security");
        PrivacyText = Line("Browse.Cui.Privacy", "Privacy");
        PopupText = Line("Browse.Cui.PopupStatus", "Popup policy");
        DataText = Line("Browse.Cui.DataSummary", "Data");
        EngineText = Line("Browse.Cui.EngineStatus", "Engine");
        RecoverButton = Tool("Browse.Cui.Recover", "Recover tab", BrowseCuiActionKind.Recover);
        SidePanel.Add(SecurityText); SidePanel.Add(PrivacyText); SidePanel.Add(PopupText); SidePanel.Add(DataText); SidePanel.Add(EngineText); SidePanel.Add(RecoverButton);
        content.Add(SidePanel);
        Root.Add(content);

        StatusText = new HuiText("Browse is starting.") { Name = "Browse.Cui.Status", Level = TextLevel.Caption };
        StatusText.SetValue(HavenProperties.Row, 4);
        StatusText.SetValue(HavenProperties.Padding, HavenThickness.Parse("5px 10px"));
        Root.Add(StatusText);

        Tabs.ItemInvoked += (_, key) => { if (Guid.TryParse(key, out var id)) _actions.Enqueue(new(BrowseCuiActionKind.SelectTab, TabId: id)); };
        Tabs.ItemSecondaryInvoked += (_, key) => { if (Guid.TryParse(key, out var id)) _actions.Enqueue(new(BrowseCuiActionKind.CloseTab, TabId: id)); };
        Root.ValidateUniqueNames();
    }

    public Page Root { get; }
    public TabStrip Tabs { get; }
    public Input AddressInput { get; }
    public Input FindInput { get; }
    public Web WebSurface { get; }
    public Container SidePanel { get; }
    public HuiText SecurityText { get; }
    public HuiText PrivacyText { get; }
    public HuiText PopupText { get; }
    public HuiText DataText { get; }
    public HuiText EngineText { get; }
    public HuiText ZoomText { get; }
    public HuiText StatusText { get; }
    public HuiButton BackButton { get; }
    public HuiButton ForwardButton { get; }
    public HuiButton ReloadButton { get; }
    public HuiButton StopButton { get; }
    public HuiButton BookmarkButton { get; }
    public HuiButton NewTabButton { get; }
    public HuiButton PrivateTabButton { get; }
    public HuiButton CloseTabButton { get; }
    public HuiButton RecoverButton { get; }
    public BrowseChromeSnapshot? Snapshot { get; private set; }

    public bool TryDequeueAction(out BrowseCuiAction action) => _actions.TryDequeue(out action!);

    public bool HandleInputSubmitted(Input input)
    {
        if (ReferenceEquals(input, AddressInput))
        {
            _actions.Enqueue(new(BrowseCuiActionKind.Navigate, AddressInput.Text));
            return true;
        }
        if (ReferenceEquals(input, FindInput))
        {
            _actions.Enqueue(new(BrowseCuiActionKind.FindNext, FindInput.Text));
            return true;
        }
        return false;
    }

    public void ApplySnapshot(BrowseChromeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
        var selected = snapshot.SelectedTab;
        _syncing = true;
        try
        {
            Tabs.SetItems(snapshot.Tabs.Select(tab => new TabStripItem(
                tab.Id.ToString(),
                tab.Privacy == BrowserTabPrivacy.Private ? $"Private · {tab.Title}" : tab.Title,
                tab.Id == snapshot.SelectedTabId)).ToArray());
            AddressInput.Text = selected.Address.Scheme == "about" ? string.Empty : selected.Address.ToString();
            FindInput.Text = snapshot.FindQuery;
            WebSurface.Url = selected.EngineState == BrowseEngineState.Ready ? selected.Address.ToString() : string.Empty;
            ZoomText.Content = $"{snapshot.ZoomPercent}%";
            SecurityText.Content = $"{snapshot.Security.Label}: {snapshot.Security.Explanation}";
            PrivacyText.Content = selected.Privacy == BrowserTabPrivacy.Private
                ? "Private tab: history, permissions, download ledger, and tab state remain ephemeral."
                : "Standard tab: local history and restart state follow Browse settings.";
            PopupText.Content = snapshot.PopupStatus;
            DataText.Content = $"{snapshot.Bookmarks.Count} bookmarks · {snapshot.History.Count} history · {snapshot.Downloads.Items.Count} downloads · {snapshot.Permissions.Count} saved permissions";
            EngineText.Content = selected.EngineState switch
            {
                BrowseEngineState.Ready => $"{selected.Engine} renderer ready.",
                BrowseEngineState.Crashed => "Native web renderer crashed.",
                _ => selected.Status
            };
            StatusText.Content = snapshot.Status;
            SetEnabled(BackButton, selected.CanGoBack && selected.EngineState == BrowseEngineState.Ready);
            SetEnabled(ForwardButton, selected.CanGoForward && selected.EngineState == BrowseEngineState.Ready);
            SetEnabled(ReloadButton, selected.EngineState == BrowseEngineState.Ready);
            SetVisible(ReloadButton, !selected.IsLoading);
            SetVisible(StopButton, selected.IsLoading);
            SetVisible(RecoverButton, selected.EngineState == BrowseEngineState.Crashed);
        }
        finally { _syncing = false; }
        Root.ValidateUniqueNames();
    }

    private HuiButton Button(string name, string content, BrowseCuiActionKind action, int column)
    {
        var button = Nav(name, content, action, column);
        button.SetValue(HavenProperties.Column, column);
        return button;
    }
    private HuiButton Nav(string name, string content, BrowseCuiActionKind action, int column)
    {
        var button = new HuiButton { Name = name, Content = content, Variant = ButtonVariant.Secondary };
        button.SetValue(HavenProperties.Column, column);
        button.Accessibility.AccessibleName = content;
        button.Invoked += (_, _) => _actions.Enqueue(action is BrowseCuiActionKind.CloseTab
            ? new(action, TabId: Snapshot?.SelectedTabId)
            : new(action));
        return button;
    }
    private HuiButton Tool(string name, string content, BrowseCuiActionKind action)
    {
        var button = new HuiButton { Name = name, Content = content, Variant = ButtonVariant.Text };
        button.Accessibility.AccessibleName = content;
        button.Invoked += (_, _) =>
        {
            if (_syncing) return;
            var value = action is BrowseCuiActionKind.FindNext or BrowseCuiActionKind.FindPrevious ? FindInput.Text : null;
            _actions.Enqueue(new(action, value));
        };
        return button;
    }
    private static HuiText Line(string name, string content) => new(content) { Name = name, Level = TextLevel.Paragraph };
    private static void SetEnabled(HuiButton button, bool enabled)
    {
        button.SetValue(HavenProperties.Enabled, enabled);
        button.SetState(HavenElementState.Disabled, !enabled);
    }
    private static void SetVisible(HavenElement element, bool visible) =>
        element.SetValue(HavenProperties.Visibility, visible ? HavenVisibility.Visible : HavenVisibility.Collapsed);
}

public sealed class BrowseCuiController : IAsyncDisposable
{
    private readonly BrowseChrome _chrome;
    public BrowseCuiController(BrowseChrome chrome, BrowseCuiScene? scene = null)
    {
        _chrome = chrome ?? throw new ArgumentNullException(nameof(chrome));
        Scene = scene ?? new BrowseCuiScene();
        Scene.ApplySnapshot(chrome.State);
        _chrome.StateChanged += OnStateChanged;
    }

    public BrowseCuiScene Scene { get; }
    public BrowseChromeSnapshot State => _chrome.State;

    public async Task<BrowseChromeSnapshot> ExecuteAsync(BrowseCuiAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var current = _chrome.State;
        var snapshot = action.Kind switch
        {
            BrowseCuiActionKind.Navigate => await _chrome.NavigateAsync(action.Value ?? string.Empty, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.Back => await _chrome.BackAsync(cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.Forward => await _chrome.ForwardAsync(cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.Reload => await _chrome.ReloadAsync(cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.Stop => await _chrome.StopAsync(cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.NewTab => await _chrome.NewTabAsync(false, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.NewPrivateTab => await _chrome.NewTabAsync(true, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.CloseTab => await _chrome.CloseTabAsync(action.TabId ?? current.SelectedTabId, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.SelectTab => await _chrome.SelectTabAsync(action.TabId ?? throw new ArgumentException("A tab ID is required."), cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.ToggleBookmark => await _chrome.ToggleBookmarkAsync(cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.ClearHistory => await _chrome.ClearHistoryAsync(cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.RefreshDownloads => await _chrome.RefreshDownloadsAsync(cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.FindNext => await _chrome.FindAsync(action.Value ?? string.Empty, false, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.FindPrevious => await _chrome.FindAsync(action.Value ?? string.Empty, true, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.ZoomIn => await _chrome.SetZoomAsync(current.ZoomPercent + 10, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.ZoomOut => await _chrome.SetZoomAsync(current.ZoomPercent - 10, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.ZoomReset => await _chrome.SetZoomAsync(100, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.AllowPopups => await _chrome.SetPermissionAsync(BrowserSitePermissionKind.WindowManagement, BrowserSitePermissionDecision.Allow, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.DenyPopups => await _chrome.SetPermissionAsync(BrowserSitePermissionKind.WindowManagement, BrowserSitePermissionDecision.Deny, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.Recover => await _chrome.RecoverSelectedTabAsync(cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.EngineGeckoTab => await _chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Gecko, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.EngineChromiumTab => await _chrome.SetSelectedTabEngineAsync(BrowseEngineKind.Chromium, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.EngineGeckoSite => await _chrome.SetSiteEngineAsync(BrowseEngineKind.Gecko, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.EngineChromiumSite => await _chrome.SetSiteEngineAsync(BrowseEngineKind.Chromium, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.EngineDefaultGecko => await _chrome.SetDefaultEngineAsync(BrowseEngineKind.Gecko, cancellationToken).ConfigureAwait(false),
            BrowseCuiActionKind.EngineDefaultChromium => await _chrome.SetDefaultEngineAsync(BrowseEngineKind.Chromium, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        Scene.ApplySnapshot(snapshot);
        return snapshot;
    }

    private void OnStateChanged(object? sender, BrowseChromeSnapshot snapshot) => Scene.ApplySnapshot(snapshot);
    public async ValueTask DisposeAsync()
    {
        _chrome.StateChanged -= OnStateChanged;
        await _chrome.DisposeAsync().ConfigureAwait(false);
    }
}
