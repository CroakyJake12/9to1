using System.Globalization;
using Haven.Core;

namespace Haven.Desktop.Views.Pages.Home;

public enum HomeAppsFilter { Installed, All, Pinned }

/// <summary>A navigation group, never a package or an alternative owner of an app.</summary>
public sealed record HomeAppHub(string Key, string Name, string Description, string IconKey,
    IReadOnlyList<string> AppIdentities);

/// <summary>An editor action names the exact saved arrangement and placement that was shown.</summary>
public sealed record HomeHubActionTarget(long Revision, string HubKey, string? AppIdentity = null);
public sealed record HomeHubRenameValue(HomeHubActionTarget Target, string Value);

public sealed record HomeHubsViewState(long Revision, bool Editing, bool Customise, bool CanEdit,
    IReadOnlyList<HomeAppHub> Hubs, string SelectedHub, string Query, string NewHubName,
    IReadOnlyDictionary<string, string> NameDrafts, bool CanUndo, string UndoDescription,
    string? ConfirmationTitle = null, string? ConfirmationDetail = null,
    string ConfirmationAction = "Confirm change", string ConfirmationCancel = "Keep arrangement", bool IsSaving = false)
{
    public static HomeHubsViewState Empty { get; } = new(0, false, false, false, [], string.Empty,
        string.Empty, string.Empty, new Dictionary<string, string>(), false, string.Empty);
}

public sealed record HomeHubNavigationRow(HomeAppHub Hub, HomeHubActionTarget Target, bool Selected,
    int AppCount, int Row, int Column)
{
    public string CountLabel => AppCount.ToString(CultureInfo.CurrentCulture) + (AppCount == 1 ? " app" : " apps");
}

public sealed record HomeHubEditorRow(HomeAppHub Hub, HomeHubActionTarget Target, string DraftName,
    int Position, bool CanEdit, bool CanMoveEarlier, bool CanMoveLater);

public sealed record HomeHubAppRow(HomeAppItem App, HomeHubActionTarget Target, int Row, int Column,
    bool CanEdit = false, bool CanMoveEarlier = false, bool CanMoveLater = false)
{
    public string Identity => Target.AppIdentity ?? App.StableIdentity;
}

public sealed record HomeHubsPresentation(HomeHubsViewState State, IReadOnlyList<HomeHubNavigationRow> Navigation,
    IReadOnlyList<HomeHubEditorRow> EditorHubs, HomeAppHub? Selected, IReadOnlyList<HomeHubAppRow> Apps,
    IReadOnlyList<HomeHubAppRow> AddableApps, HomeHubAppRow? BuildProjects, int NavigationColumns, int AppColumns)
{
    public string NavigationGridColumns => string.Join(' ', Enumerable.Repeat("1fr", NavigationColumns));
    public string NavigationGridRows => HomeAppHubCatalogue.AutoRows(Navigation.Count, NavigationColumns);
    public string AppGridColumns => string.Join(' ', Enumerable.Repeat("1fr", AppColumns));
    public string AppGridRows => HomeAppHubCatalogue.AutoRows(Apps.Count, AppColumns);
}

public sealed record HomeAppPlacement(HomeAppItem App, int Row, int Column, bool IsPinned,
    bool CanPin, bool CanMoveEarlier, bool CanMoveLater)
{
    public string Identity => App.StableIdentity;
    public string PinLabel => (IsPinned ? "Unpin " : "Pin ") + App.Name;
    public string MoveEarlierLabel => "Move " + App.Name + " earlier";
    public string MoveLaterLabel => "Move " + App.Name + " later";
}

public sealed record HomeAppHubSection(string Key, string Name, string Description, string IconKey,
    IReadOnlyList<HomeAppPlacement> Apps, int Columns)
{
    public string Count => Apps.Count.ToString(CultureInfo.CurrentCulture);
    public string GridRows => HomeAppHubCatalogue.AutoRows(Apps.Count, Columns);
}

public sealed record HomeAppsPresentation(IReadOnlyList<HomeAppHubSection> Hubs,
    IReadOnlyList<HomeAppPlacement> Pinned, int VisibleCount, int CatalogueColumns, int PinnedColumns,
    string Query, HomeAppsFilter Filter)
{
    public bool HasApps => VisibleCount != 0;
    public bool HasPinned => Pinned.Count != 0;
    public string CountLabel => VisibleCount.ToString(CultureInfo.CurrentCulture) + (VisibleCount == 1 ? " app" : " apps");
    public string CatalogueGridColumns => string.Join(' ', Enumerable.Repeat("1fr", CatalogueColumns));
    public string PinnedGridColumns => string.Join(' ', Enumerable.Repeat("1fr", PinnedColumns));
    public string PinnedGridRows => HomeAppHubCatalogue.AutoRows(Pinned.Count, PinnedColumns);
    public string FilterLabel => Filter switch { HomeAppsFilter.All => "All apps", HomeAppsFilter.Pinned => "Pinned", _ => "Installed" };
    public string EmptyTitle => Query.Length > 0 ? "No apps found" : Filter == HomeAppsFilter.Pinned ? "No pinned apps" : "No apps are installed";
    public string EmptyDetail => Query.Length > 0 ? "Try a different search, or clear the search." :
        Filter == HomeAppsFilter.Pinned ? "Pin an app from the catalogue to keep it here." : "Choose All apps to see the catalogue and availability.";
}

/// <summary>
/// PROD-010 / HOME-007: the seven approved hubs over canonical catalogue identities. Known legacy
/// names keep their stored GUID and owning route. A missing app is an unavailable reference, never
/// an invented installation. Custom records cannot impersonate built-ins by choosing the same name.
/// </summary>
public static class HomeAppHubCatalogue
{
    public static bool IsAppRecord(ModeDefinition mode)
    {
        if (mode.Source != ModeSource.BuiltIn) return true;
        // These retained identities are Haven/Spaces features, not duplicate app installations.
        // Launcher is a standalone app again in PROD-010. The former training mode is not Train.
        return !new[] { 1, 2, 5, 7, 13, 15, 16, 19, 21, 29 }.Any(number =>
            mode.Id == Guid.Parse("a0000000-0000-0000-0000-" + number.ToString("D12", CultureInfo.InvariantCulture)));
    }

    private sealed record AppSpec(string Key, string Name, string Description, string IconKey, int? BuiltInNumber = null,
        string? RegisteredKey = null);
    private sealed record HubSpec(string Key, string Name, string Description, string IconKey, AppSpec[] Apps);

    private static readonly HubSpec[] Standard =
    [
        new("coordinate", "Coordinate", "Conversations, models and agents", "messages-square",
        [new("spaces", "Spaces", "Conversations and tasks", "spaces", 22),
         new("voice", "Voice & Vision", "Live voice and vision", "vision", 11, "vision"),
         new("train", "Train", "Fine-tuning, evaluation and checkpoints", "train")]),
        new("create", "Create", "Images, audio, video and 3D", "palette",
        [new("frame", "Frame", "Images and visual creation", "imagine", 8, "imagine"),
         new("wave", "Wave", "Audio and sound", "wave"),
         new("motion", "Motion", "Video and animation", "motion", 25),
         new("sculpt", "Sculpt", "3D models and scenes", "box")]),
        new("develop", "Develop", "Projects, source and execution", "code",
        [new("code", "Code", "Source and software projects", "code"),
         new("sites", "Sites", "Websites and visual development", "globe"),
         new("data", "Data", "Tables and structured data", "data", 10),
         new("stacks", "Stacks", "Block-based development", "layers"),
         new("games", "Games", "Interactive projects", "gamepad"),
         new("device", "Virtual Device", "Virtual devices and environments", "monitor", RegisteredKey: "virtual-device"),
         new("terminal", "Terminal", "Commands and environments", "terminal", 20)]),
        new("organise", "Organise", "Resources, time and connected devices", "folder",
        [new("connect", "Connect", "Email, messages and calls", "mail"),
         new("planner", "Planner", "Plans and calendar", "plan", 6, "plan"),
         new("files", "Files", "Files and resources", "files", 30),
         new("automations", "Automations", "Scheduled and triggered work", "automations", 3),
         new("maps", "Maps", "Places and journeys", "maps", 24),
         new("projector", "Projector", "Present to another display", "projector"),
         new("launcher", "Launcher", "Devices and applications", "launcher", 14),
         new("collection", "Collection", "Saved collections", "shelf", 27, "shelf")]),
        new("work", "Work", "Documents, learning and shared ideas", "file-pen",
        [new("write", "Write", "Documents and review", "write", 17),
         new("present", "Present", "Slides and presentations", "present", 9),
         new("boards", "Boards", "Pages, ideas and ink", "boards", 23),
         new("canvas", "Canvas", "Drawing and spatial work", "canvas", 18),
         new("flow", "Flow", "Connected reports and binders", "link"),
         new("decks", "Decks", "Cards and personal review", "layers", RegisteredKey: "cards"),
         new("forms", "Forms", "Forms, quizzes and responses", "forms", 26)]),
        new("experiment", "Experiment", "Try, evaluate and explore", "flask",
        [new("play", "Play", "Human and agent games", "play", 12),
         new("studio", "AI Studio", "Agents, tools and experiments", "studio", 4),
         new("stories", "Story Sequences", "Branching activities and stories", "route", RegisteredKey: "paths")]),
        new("admin", "Admin", "Organisation and system management", "shield-check",
        [new("admin", "Admin", "People, policy and resources", "shield-check")])
    ];

    public static IReadOnlyList<HomeAppItem> Catalogue(IReadOnlyList<HomeAppItem> actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        var result = new List<HomeAppItem>();
        var matched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in Standard.SelectMany(hub => hub.Apps))
        {
            var record = Find(spec, actual);
            if (record is null)
                result.Add(new(spec.Key, spec.Name, spec.Description, spec.IconKey,
                    Source: ModeSource.BuiltIn, IsAvailable: false, IsInstalled: false, AvailabilityNote: "Not installed in this profile."));
            else
            {
                matched.Add(record.StableIdentity);
                // Presentation aliases do not rewrite keys, package IDs, pins or saved routes.
                result.Add(record with { Name = spec.Name, Description = spec.Description });
            }
        }
        result.AddRange(actual.Where(app => !app.IsGroup && !matched.Contains(app.StableIdentity)));
        return result;
    }

    public static IReadOnlyList<HomeAppHub> StandardHubs(IReadOnlyList<HomeAppItem> actual)
    {
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var hubs = new List<HomeAppHub>();
        foreach (var hub in Standard)
        {
            var identities = hub.Apps.Select(spec => Find(spec, actual)?.StableIdentity ?? "reference:" + spec.Key).ToList();
            foreach (var identity in identities) assigned.Add(identity);
            // Build is still an installed canonical owner. Until separate Code/Sites/Stacks routes
            // are registered, keep its real entry visible instead of silently aliasing all three.
            if (hub.Key == "develop")
                foreach (var app in actual.Where(app => IsBuiltIn(app, 28)))
                    if (assigned.Add(app.StableIdentity)) identities.Add(app.StableIdentity);
            hubs.Add(new(hub.Key, hub.Name, hub.Description, hub.IconKey, identities));
        }
        var additional = actual.Where(app => !app.IsGroup && !assigned.Contains(app.StableIdentity)).Select(app => app.StableIdentity).ToArray();
        if (additional.Length != 0)
        {
            // User and extension applications remain discoverable; they are not guessed into a
            // standard app identity from a display-name resemblance.
            var index = hubs.FindIndex(hub => hub.Key == "experiment");
            hubs[index] = hubs[index] with { AppIdentities = hubs[index].AppIdentities.Concat(additional).ToArray() };
        }
        return hubs;
    }

    public static HomeAppsPresentation Project(IReadOnlyList<HomeAppItem> actual, IReadOnlyList<ModePin> pins,
        string query, HomeAppsFilter filter, bool canEditPins, double contentWidth,
        IReadOnlyList<HomeAppHub>? arrangement = null, string? selectedHub = null)
    {
        query = query.Trim();
        var all = Catalogue(actual).GroupBy(app => app.StableIdentity, StringComparer.Ordinal).Select(group => group.First()).ToArray();
        var byIdentity = all.ToDictionary(app => app.StableIdentity, StringComparer.Ordinal);
        // A saved placement made while an app was unavailable still names that same approved app
        // when its real registration later arrives. Mail and Connect share one reference identity.
        foreach (var spec in Standard.SelectMany(hub => hub.Apps))
            if (Find(spec, actual) is { } installed && byIdentity.TryGetValue(installed.StableIdentity, out var projected))
                byIdentity["reference:" + spec.Key] = projected;
        if (byIdentity.TryGetValue("reference:connect", out var connect)) byIdentity["reference:mail"] = connect;
        var orderedPins = pins.OrderBy(pin => pin.SortOrder).ThenBy(pin => pin.PinnedAt).ThenBy(pin => pin.Id).ToArray();
        var pinnedIds = orderedPins.Select(pin => pin.ModeId).ToHashSet();
        var columns = contentWidth > 900 ? 3 : contentWidth > 500 ? 2 : 1;
        var pinnedColumns = contentWidth > 900 ? 6 : contentWidth > 500 ? 3 : 2;
        bool Matches(HomeAppItem app, string hubName = "") =>
            (filter != HomeAppsFilter.Installed || app.IsInstalled) &&
            (filter != HomeAppsFilter.Pinned || app.CanonicalId is { } id && pinnedIds.Contains(id)) &&
            (query.Length == 0 || app.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             app.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             app.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || hubName.Contains(query, StringComparison.OrdinalIgnoreCase));
        HomeAppPlacement Place(HomeAppItem app, int index, int gridColumns, bool earlier = false, bool later = false) =>
            new(app, index / gridColumns, index % gridColumns, app.CanonicalId is { } id && pinnedIds.Contains(id),
                canEditPins && app.CanonicalId is { } actualId && actualId != Guid.Empty && app.IsInstalled,
                canEditPins && earlier, canEditPins && later);
        var visibleIds = new HashSet<string>(StringComparer.Ordinal);
        var sections = new List<HomeAppHubSection>();
        var hubs = arrangement ?? StandardHubs(actual);
        foreach (var hub in hubs)
        {
            if (selectedHub is not null && hub.Key != selectedHub) continue;
            var rows = hub.AppIdentities.Distinct(StringComparer.Ordinal)
                .Select(identity => byIdentity.GetValueOrDefault(identity))
                .Where(app => app is not null && Matches(app, hub.Name)).Cast<HomeAppItem>().ToArray();
            if (rows.Length == 0) continue;
            foreach (var app in rows) visibleIds.Add(app.StableIdentity);
            sections.Add(new(hub.Key, hub.Name, hub.Description, hub.IconKey,
                rows.Select((app, index) => Place(app, index, columns)).ToArray(), columns));
        }
        if (arrangement is not null && selectedHub is null)
        {
            // Hub removal only changes presentation. Every unplaced catalogue app remains
            // reachable in All apps, including when the custom arrangement is intentionally empty.
            var unplaced = all.Where(app => !visibleIds.Contains(app.StableIdentity) && Matches(app)).ToArray();
            if (unplaced.Length > 0)
            {
                foreach (var app in unplaced) visibleIds.Add(app.StableIdentity);
                sections.Add(new("unplaced:apps", "Other apps", "Apps outside your hub arrangement", "grid",
                    unplaced.Select((app, index) => Place(app, index, columns)).ToArray(), columns));
            }
        }
        var pinned = new List<HomeAppPlacement>();
        for (var index = 0; index < orderedPins.Length; index++)
        {
            var app = all.FirstOrDefault(item => item.CanonicalId == orderedPins[index].ModeId);
            if (app is null || !Matches(app) || selectedHub is not null && !visibleIds.Contains(app.StableIdentity)) continue;
            pinned.Add(Place(app, pinned.Count, pinnedColumns, index > 0, index < orderedPins.Length - 1));
        }
        return new(sections, pinned, visibleIds.Count, columns, pinnedColumns, query, filter);
    }

    public static HomeHubsPresentation ProjectHubs(IReadOnlyList<HomeAppItem> actual, HomeHubsViewState state,
        HomeAppsFilter filter, IReadOnlyList<ModePin> pins, double contentWidth)
    {
        var pinnedIds = pins.Select(pin => pin.ModeId).ToHashSet();
        var query = state.Query.Trim();
        var navigationColumns = contentWidth > 1000 ? 7 : contentWidth > 650 ? 4 : contentWidth > 380 ? 3 : 2;
        var appColumns = state.Editing || contentWidth <= 650 ? 1 : 2;
        var selected = state.Hubs.FirstOrDefault(hub => hub.Key == state.SelectedHub) ?? state.Hubs.FirstOrDefault();
        bool MatchesFilter(HomeAppItem app) => state.Editing ||
            (filter != HomeAppsFilter.Installed || app.IsInstalled) &&
            (filter != HomeAppsFilter.Pinned || app.CanonicalId is { } id && pinnedIds.Contains(id));
        bool MatchesQuery(HomeAppItem app) => query.Length == 0 ||
            app.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            app.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            app.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
        var navigation = state.Hubs.Select((hub, index) => new HomeHubNavigationRow(hub,
            new(state.Revision, hub.Key), hub.Key == selected?.Key,
            hub.AppIdentities.Select(identity => ResolvePlacement(actual, identity))
                .Where(MatchesFilter).DistinctBy(app => app.StableIdentity).Count(),
            index / navigationColumns, index % navigationColumns)).ToArray();
        var editors = state.Hubs.Select((hub, index) => new HomeHubEditorRow(hub,
            new(state.Revision, hub.Key), state.NameDrafts.GetValueOrDefault(hub.Key, hub.Name), index + 1,
            state.CanEdit && state.Customise, state.CanEdit && state.Customise && index > 0,
            state.CanEdit && state.Customise && index < state.Hubs.Count - 1)).ToArray();
        var apps = new List<HomeHubAppRow>();
        HomeHubAppRow? build = null;
        if (selected is not null)
        {
            for (var index = 0; index < selected.AppIdentities.Count; index++)
            {
                var identity = selected.AppIdentities[index];
                var app = ResolvePlacement(actual, identity);
                var target = new HomeHubActionTarget(state.Revision, selected.Key, identity);
                if (!state.Editing && selected.Key == "develop" && IsBuiltIn(app, 28))
                {
                    // Shared projects remain in their actual Build owner. No example project or
                    // unrelated project counter is manufactured for the Hubs overview.
                    build = new(app, target, 0, 0);
                    continue;
                }
                if (!MatchesFilter(app) || !state.Editing && !MatchesQuery(app)) continue;
                apps.Add(new(app, target, apps.Count / appColumns, apps.Count % appColumns,
                    state.CanEdit && state.Customise, state.CanEdit && state.Customise && index > 0,
                    state.CanEdit && state.Customise && index < selected.AppIdentities.Count - 1));
            }
        }
        var occupied = selected?.AppIdentities.Select(identity => ResolvePlacement(actual, identity).StableIdentity)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        HomeHubAppRow[] addable = selected is null ? [] : Catalogue(actual).DistinctBy(app => app.StableIdentity)
            .Where(app => !occupied.Contains(app.StableIdentity))
            .Select(app => new HomeHubAppRow(app, new(state.Revision, selected.Key, app.StableIdentity), 0, 0,
                state.CanEdit && state.Customise)).ToArray();
        return new(state, navigation, editors, selected, apps, addable, build, navigationColumns, appColumns);
    }

    /// <summary>Resolve a saved reference only to its canonical registration. Unknown or removed
    /// identities stay visible as unavailable, so a user's saved placement is never silently lost.</summary>
    public static HomeAppItem ResolvePlacement(IReadOnlyList<HomeAppItem> actual, string identity)
    {
        var projected = Catalogue(actual);
        if (projected.FirstOrDefault(app => app.StableIdentity == identity) is { } exact) return exact;
        if (identity.StartsWith("reference:", StringComparison.Ordinal))
        {
            var key = identity[10..];
            if (key == "mail") key = "connect";
            var spec = Standard.SelectMany(hub => hub.Apps).FirstOrDefault(item => item.Key == key);
            if (spec is not null && Find(spec, actual) is { } installed)
                return installed with { Name = spec.Name, Description = spec.Description };
            if (spec is not null)
                return new(spec.Key, spec.Name, spec.Description, spec.IconKey, Source: ModeSource.BuiltIn, IsAvailable: false,
                    IsInstalled: false, AvailabilityNote: "Not installed in this profile.");
        }
        return new(identity, "Unavailable app", "Saved app placement", "grid", IsAvailable: false,
            IsInstalled: false, AvailabilityNote: "This saved app is no longer available. Its placement is kept: " + identity);
    }

    internal static string AutoRows(int count, int columns) => string.Join(' ', Enumerable.Repeat("Auto", Math.Max(1, (count + columns - 1) / columns)));

    private static HomeAppItem? Find(AppSpec spec, IReadOnlyList<HomeAppItem> actual) => actual.FirstOrDefault(app =>
        app.Source == ModeSource.BuiltIn && (spec.BuiltInNumber is { } number ? IsBuiltIn(app, number) :
            app.Key.Equals(spec.RegisteredKey ?? spec.Key, StringComparison.OrdinalIgnoreCase) ||
            spec.Key == "connect" && app.Key.Equals("mail", StringComparison.OrdinalIgnoreCase)));

    private static bool IsBuiltIn(HomeAppItem app, int number) => app.Source == ModeSource.BuiltIn &&
        app.CanonicalId == Guid.Parse("a0000000-0000-0000-0000-" + number.ToString("D12", CultureInfo.InvariantCulture));
}
