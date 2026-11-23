using Haven.Core;

namespace Haven.Desktop.Views.Pages.Home;

internal enum HomeSettingsOwner { ModelPicker, Profile, Notifications }

internal static class HomeSettingsCatalog
{
    internal sealed record Group(string Key, string Label, string Subtitle, string Scope, bool InitiallyExpanded);
    internal sealed record Destination(string Key, string Parent, string Label, string Icon, string Description,
        string Keywords = "", string? Availability = null, string? App = null, Link[]? Actions = null);
    internal sealed record Link(string Label, string Description, string Action, string? Category = null,
        AppContentAddress? Address = null, HomeSettingsOwner? Owner = null, string? App = null);

    internal static readonly Group[] Groups =
    [
        new("global", "Global", "Follows across all 9to1", "Global", true),
        new("local", "Local", "This device only", "This device", true),
        new("haven", "9to1 Haven", "Tabs and browsing", "Haven", false),
        new("apps", "Apps", "Shared and individual app settings", "App", false)
    ];

    // Navigation follows the dated Atlas directory. Links delegate to existing owners and
    // deliberately distinguish account storage, device storage, model discovery and market routes.
    internal static readonly Destination[] Destinations =
    [
        new("global-general", "global", "General", "settings", "Your preferences across 9to1.",
            "language formatting", "Language and regional formatting controls are unavailable in this workspace.",
            Actions: [Related("appearance", "Theme, layout and accessibility."), Related("ai", "Model routes and shared AI preferences."),
                Related("security", "Account access and data boundaries."), Related("account", "Profile, resources and Mesh.")]),
        new("appearance", "global", "Appearance", "palette", "Theme, layout and accessibility across your workspace."),
        new("theme", "appearance", "Theme", "palette", "Theme and accent preferences.", "brightness colour contrast override app accents high contrast"),
        new("theme-options", "appearance", "Theme Options", "palette", "Adjust the display mode and shared accent palette.", "light dark follow system custom theme"),
        new("layout", "appearance", "Layout", "layout", "Arrange your workspace.", "dashboard recover saved appearance"),
        new("accessibility", "appearance", "Accessibility", "settings", "Accessibility across your workspace.", "high contrast reduce motion"),
        new("ai", "global", "AI", "sparkles", "Model routes and shared AI preferences.",
            Actions: [Panel("Model settings", "Review model roles and routes in the existing model picker.", HomeSettingsOwner.ModelPicker),
                Open("Discover models", "Browse the model catalogue.", "browse://home/models"), Related("local-models")]),
        new("ai-personalisation", "ai", "Personalisation", "agents", "Memory and learning preferences for your account.",
            "personality memory frequency background learning", "Account-wide memory and learning controls are unavailable in this workspace.",
            Actions: [Related("security-privacy")]),
        new("ai-plugins", "ai", "Plugins/MCPs", "plugin", "Connections and access for installed capabilities.",
            "plugins mcp connections", "Plugin installation and MCP connection management are unavailable in this workspace.",
            Actions: [Related("security-permissions"), Related("ai-providers")]),
        new("ai-skills", "ai", "Skills", "skills", "Task instructions and configured expertise.",
            Availability: "Installed skill management is unavailable in this workspace.",
            Actions: [Launch("Spaces", "Open the app that owns conversation context.", "spaces")]),
        new("ai-providers", "ai", "Providers", "browse", "Model connections and routes.",
            "personal endpoint provider connection", "Creating or editing provider connections is unavailable here.",
            Actions: [Panel("Model routes", "Review available choices and provider routing in Model settings.", HomeSettingsOwner.ModelPicker)]),
        new("ai-voice", "ai", "Voice", "wave", "Voice routes and session controls.",
            Availability: "Voice route and source preferences are unavailable in this workspace.",
            Actions: [Launch("Vision & Voice", "Open the voice and capture workspace when available.", "voice-vision")]),
        new("security", "global", "Security & Privacy", "shield", "Account access, permissions and privacy."),
        new("security-general", "security", "General", "shield", "Account access and signed-in devices.",
            "sessions sign in", "Signed-in device and session management is unavailable here.",
            Actions: [Related("account-profile"), Related("security-permissions")]),
        new("security-permissions", "security", "Permissions", "shield", "Access granted to apps and callers.",
            "trust grants grant revoke", "Editing account-wide grants is unavailable here.",
            Actions: [Open("Permission activity", "Review recorded permission requests and decisions in Events.", "browse://home/events")]),
        new("security-sandbox", "security", "Sandbox", "test", "Execution, file and network boundaries.",
            Availability: "Host sandbox policy controls are unavailable in this workspace.",
            Actions: [Related("security-permissions")]),
        new("security-privacy", "security", "Privacy", "shield", "Review how information is stored and processed.",
            "processing memory storage", "Account-wide processing and memory controls are unavailable here.",
            Actions: [Related("haven-history", "Manage this profile’s browsing history.")]),
        new("account", "global", "Account", "agents", "Profile, plan, storage and connected devices."),
        new("account-profile", "account", "Profile", "agents", "Your 9to1 account identity.", "name username pronouns job profile picture",
            Actions: [Panel("Profile", "Open the account owner to review your profile.", HomeSettingsOwner.Profile)]),
        new("account-subscriptions", "account", "Subscriptions", "file", "Your plan and available resources.",
            "billing entitlement payment", "Plan selection and billing management are unavailable here.",
            Actions: [Related("account-storage")]),
        new("account-storage", "account", "Storage", "data", "Storage allocated to your 9to1 account.",
            "cloud quota", "Account usage and quotas are unavailable here.",
            Actions: [Related("account-subscriptions")]),
        new("account-mesh", "account", "Mesh", "agents", "Review connected devices and shared resources.",
            Actions: [Launch("Mesh", "Open the Mesh workspace for its current devices and connection status.", "mesh")]),
        new("local-general", "local", "General", "window", "Services, storage and preferences for this device.",
            "settings storage settings advanced disk recovery device services updates",
            "Device storage, background service and recovery controls are unavailable here.",
            Actions: [Related("local-apps"), Related("local-models"), Related("local-notifications")]),
        new("local-apps", "local", "Installed Apps", "apps", "Apps and packages available on this device.",
            "installed apps updates packages", "Package installation, updates and rollback are unavailable here.",
            Actions: [Open("Home app library", "Browse apps available to the current profile.", "browse://home")]),
        new("local-models", "local", "Installed Models", "cpu", "Models and runtime resources on this device.",
            "model files memory residency ollama", "Local model files, residency and memory limits cannot be managed here.",
            Actions: [Open("Discover models", "Browse the model catalogue.", "browse://home/models"),
                Panel("Model settings", "Review currently configured model choices.", HomeSettingsOwner.ModelPicker)]),
        new("local-notifications", "local", "Notifications", "bell", "Notifications on this device.",
            "quiet hours delivery sound", "Notification delivery, sound and quiet-hours preferences are unavailable here.",
            Actions: [Panel("Notifications", "Open your current notifications.", HomeSettingsOwner.Notifications)]),
        new("haven-general", "haven", "General", "layout", "New Tab and the shared tab workspace.", "browse engine history startup restore tabs",
            Actions: [Related("haven-bookmarks"), Related("haven-history"), Related("haven-shortcuts"), Related("haven-performance")]),
        new("haven-newtab", "haven-general", "New Tab", "window", "The starting page for the next tab you open.",
            "new tab background widgets shortcuts layout",
            Actions: [Open("New Tab layout", "Choose the layout, background and available widgets in the New Tab editor.", "browse://newtab/customise", "Customise"),
                Open("New Tab", "Open the current New Tab page.", "browse://newtab")]),
        new("haven-tabs-address", "haven-general", "Tabs & address", "layout", "Controls for the active tab and address bar.",
            "tab search address mode switcher", Actions: [Related("haven-key-navigation", "View navigation keys."),
                Open("Browse settings", "Change the default engine or tab restoration for this profile.", "browse://settings")]),
        new("haven-bookmarks", "haven", "Bookmarks", "bookmark", "Saved Browse bookmarks and New Tab destinations.",
            "bookmarks saved destinations"),
        new("haven-pinned", "haven-bookmarks", "Pinned destinations", "bookmark", "Shortcuts available on the New Tab page.",
            Availability: "Changing pinned destinations is unavailable here.",
            Actions: [Open("New Tab", "See the current New Tab shortcuts.", "browse://newtab")]),
        new("haven-shortcut-order", "haven-bookmarks", "Shortcut order", "bookmark", "The order of shortcuts on New Tab.",
            Availability: "Reordering New Tab shortcuts is unavailable here.", Actions: [Related("haven-pinned")]),
        new("haven-history", "haven", "History", "history", "Browsing history and navigation in the active tab.",
            "previous web visits"),
        new("haven-tab-history", "haven-history", "Back & forward", "history", "Move through the active tab’s history.",
            "back forward previous next", "Use the Back and Forward buttons in the tab bar when the current tab has history.",
            Actions: [Open("Browse history", "Review past web visits in this profile.", "browse://history")]),
        new("haven-recent-files", "haven-history", "Recent files", "folder", "Return to recent work through the Home Library.",
            Actions: [Open("Home Library", "Open the current profile’s file and activity list.", "browse://home/library")]),
        new("haven-shortcuts", "haven", "Keyboard Shortcuts", "settings", "Key commands for the workspace.",
            "keyboard shortcut focus key remap", Availability: "Editing keyboard shortcuts is unavailable here."),
        new("haven-key-navigation", "haven-shortcuts", "Navigation", "layout", "Use Tab and Shift+Tab to move between controls.",
            "keyboard next previous control",
            Actions: [Related("haven-key-menus", "See keys for activating controls and dismissing menus."), Related("accessibility")]),
        new("haven-key-menus", "haven-shortcuts", "Menus & focus", "settings",
            "Use Enter to activate a focused settings category. Use Escape to dismiss an open Browse menu or clear Settings search.",
            "keyboard menu escape activate focus", Actions: [Related("accessibility")]),
        new("haven-performance", "haven", "Performance", "chart", "Device services and resources used by Haven."),
        new("haven-services", "haven-performance", "Services", "settings", "Background services, recovery and diagnostics for this device.",
            Actions: [Related("local-general")]),
        new("haven-model-memory", "haven-performance", "Models & memory", "cpu", "Local model files and runtime memory.",
            Actions: [Related("local-models")]),
        new("haven-storage-cache", "haven-performance", "Storage & cache", "data", "Disk usage and rebuildable caches on this device.",
            Actions: [Related("local-general"), Related("local-apps")]),
        new("apps-shared", "apps", "Shared settings", "apps", "Common preferences used by your apps.",
            "editor ai access contextual chat", Actions: [Related("appearance"), Related("layout"), Related("security-permissions")]),
        .. AppDestinations()
    ];

    private static IEnumerable<Destination> AppDestinations()
    {
        (string Key, string Label, string App)[] apps =
        [
            ("spaces", "Spaces", "spaces"), ("boards", "Boards", "boards"), ("write", "Write", "write"),
            ("present", "Present", "present"), ("canvas", "Canvas", "canvas"), ("build", "Build", "dev"),
            ("data", "Data", "data"), ("files", "Files", "files"), ("forms", "Forms", "forms"),
            ("planner", "Planner", "plan"), ("automations", "Automations", "automations"),
            ("picture", "Picture", "picture"), ("wave", "Wave", "wave"), ("motion", "Motion", "motion"),
            ("connect", "Connect", "connect"), ("maps", "Maps", "maps"), ("shelf", "Shelf", "shelf"),
            ("ai-studio", "AI Studio", "ai-studio"), ("terminal", "Terminal", "terminal"),
            ("vision-and-voice", "Vision & Voice", "voice-vision"), ("cards", "Cards", "cards"),
            ("play", "Play", "play"), ("games", "Games", "games"), ("link", "Link", "link"),
            ("prototype", "Prototype", "prototype"), ("paths", "Paths", "paths"),
            ("replay", "Replay", "replay"), ("mini-computer", "Mini Computer", "mini-computer"),
            ("admin", "Admin", "admin"), ("launcher", "Launcher", "launcher")
        ];

        return apps.Select(app => new Destination("app-" + app.Key, "apps", app.Label, "apps",
            "App-specific and shared preferences for " + app.Label + ".", App: app.App,
            Actions: [Launch(app.Label, "Open the app to use its contextual controls.", app.App),
                Related("apps-shared"), Related("security-permissions")]));
    }

    private static readonly Dictionary<string, Destination> ByRoute =
        Destinations.ToDictionary(CanonicalRoute, StringComparer.Ordinal);

    internal static bool IsSettingsAddress(AppContentAddress address) =>
        address.Namespace == AppContentNamespace.Browse && address.Authority == "home" &&
        (address.ResourceSegments is ["settings"] ||
            address.ResourceSegments is ["settings", var route] && ResolveRoute(route) is not null);

    internal static bool IsNewTabCustomisationAddress(AppContentAddress address) =>
        address.Namespace == AppContentNamespace.Browse && address.Authority == "newtab" &&
        address.ResourceSegments is ["customise"];

    internal static string? ResolveRoute(string route) => route switch
    {
        "home-appearance" or "appearance-palette" => "theme",
        "custom-theme" => "theme-options",
        "account" or "home-account" => "account",
        "settings-models" => "ai",
        "settings-provider" => "ai-providers",
        "settings-storage" or "settings-advanced" => "local-general",
        _ => ByRoute.TryGetValue(route, out var destination) ? destination.Key : null
    };

    private static string CanonicalRoute(Destination destination) => destination.Key switch
    {
        "global-general" => "settings-general",
        "theme" => "appearance",
        "ai-personalisation" => "settings-personalisation",
        "ai-plugins" => "settings-plugins",
        "ai-skills" => "settings-skills",
        "ai-providers" => "settings-providers",
        "ai-voice" => "settings-voice",
        "security-permissions" => "settings-permissions",
        "security-sandbox" => "settings-sandbox",
        "security-privacy" => "settings-privacy",
        "account-profile" => "settings-profile",
        "account-subscriptions" => "settings-subscriptions",
        "account-mesh" => "settings-mesh",
        "local-apps" => "settings-apps",
        "local-models" => "settings-installed-models",
        "local-notifications" => "settings-notifications",
        _ => "settings-" + destination.Key
    };

    private static readonly Dictionary<string, Destination> ByKey = Destinations.ToDictionary(item => item.Key);
    internal static Destination Get(string key) => ByKey[key];
    internal static bool TryGet(string key, out Destination destination) => ByKey.TryGetValue(key, out destination!);
    internal static IEnumerable<Destination> Children(string key) => Destinations.Where(item => item.Parent == key);
    internal static Group GroupFor(Destination item) =>
        Groups.Single(group => group.Key == (TryGet(item.Parent, out var parent) ? parent.Parent : item.Parent));

    internal static Link Related(string category, string description = "") =>
        new(string.Empty, description, "Open", Category: category);

    private static Link Open(string label, string description, string address, string action = "Open") =>
        new(label, description, action, Address: AppContentAddress.Parse(address));

    private static Link Panel(string label, string description, HomeSettingsOwner owner) =>
        new(label, description, "Open", Owner: owner);

    private static Link Launch(string label, string description, string app) =>
        new(label, description, "Open app", App: app);
}
