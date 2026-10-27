using System.Text.Json;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Apps.Browse;
using Xunit;

namespace Haven.Desktop.Tests;

// Authored during the laptop power hold. UNRUN until the serialized Host/desktop
// verification resumes; these facts are not a claim of runtime acceptance.
public sealed class UnifiedWorkspaceSessionProjectionTests
{
    [Fact]
    public void Active_only_v1_split_migrates_once_with_exact_split_and_pane_ids_then_survives_inactive_save()
    {
        var a = Tab("A"); var b = Tab("B"); var outside = Tab("Outside"); var original = Session([a, b, outside]);
        var splitId = Guid.NewGuid();
        var panes = new[] { new WorkspacePaneSnapshot(Guid.NewGuid(), a.Id, 0), new WorkspacePaneSnapshot(Guid.NewGuid(), b.Id, 1) };
        var legacy = original.Windows[0] with { SelectedTabId = b.Id,
            Layout = new(splitId, WorkspaceLayoutKind.Split, SplitOrientation.Horizontal, .6, panes) };
        original = original with { Windows = [legacy] };
        var tabs = new[] { Project(a), Project(b), Project(outside) };
        var projectedSplit = Assert.Single(UnifiedWorkspaceSessionProjection.ProjectSplitViews(legacy, tabs));
        Assert.Equal(splitId, projectedSplit.Id); Assert.Equal(b.Id, projectedSplit.FocusedTabId);
        var migrated = Merge(original, Projection(tabs, b.Id) with { PaneTabIds = [a.Id, b.Id],
            SplitOrientation = SplitOrientation.Horizontal, PrimaryPaneRatio = .6, SplitViews = [projectedSplit] }, a.Id, b.Id, outside.Id);
        var saved = Assert.Single(migrated.Windows[0].SplitViews);
        Assert.Equal(splitId, saved.Id); Assert.Equal(panes.Select(pane => pane.Id), saved.Panes.Select(pane => pane.Id));
        var inactive = Merge(migrated, Projection(tabs, outside.Id) with { PaneTabIds = [], SplitViews = [projectedSplit] }, a.Id, b.Id, outside.Id);
        var reloaded = Assert.Single(UnifiedWorkspaceSessionProjection.ProjectSplitViews(inactive.Windows[0], tabs));
        Assert.Equal(splitId, reloaded.Id); Assert.Equal(b.Id, reloaded.FocusedTabId);
        Assert.Equal(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(Assert.Single(inactive.Windows[0].SplitViews)));
        Assert.Equal(WorkspaceLayoutKind.Single, inactive.Windows[0].Layout.Kind);
        Assert.Throws<InvalidDataException>(() => Merge(original with { SchemaVersion = WorkspaceSessionSnapshot.CurrentSchemaVersion + 1 },
            Projection(tabs, b.Id) with { PaneTabIds = [a.Id, b.Id], SplitViews = [projectedSplit] }, a.Id, b.Id, outside.Id));
    }

    [Fact]
    public void Split_load_never_projects_a_private_member_or_reinterprets_malformed_saved_objects()
    {
        var a = Tab("A"); var b = Tab("B"); var original = Session([a, b]);
        var window = original.Windows[0] with { SplitViews = [new(Guid.NewGuid(),
            [new(Guid.NewGuid(), a.Id, 0), new(Guid.NewGuid(), b.Id, 1)], a.Id, SplitOrientation.Vertical, .5)] };
        Assert.Empty(UnifiedWorkspaceSessionProjection.ProjectSplitViews(window, [Project(a), Project(b) with { Privacy = BrowserTabPrivacy.Private }]));
        var before = JsonSerializer.Serialize(window);
        Assert.Throws<InvalidDataException>(() => UnifiedWorkspaceSessionProjection.ProjectSplitViews(
            window with { SplitViews = [window.SplitViews[0] with { Panes = [window.SplitViews[0].Panes[0], null!] }] }, [Project(a), Project(b)]));
        Assert.Equal(before, JsonSerializer.Serialize(window));
    }

    [Fact]
    public void Inactive_split_objects_roundtrip_without_losing_navigation_profile_group_or_pane_identity()
    {
        var groupId = Guid.NewGuid();
        var a = Profiled(Tab("A"), "actual-profile") with { GroupId = groupId, NavigationJson = "{\"back\":[\"research\"]}" };
        var b = Profiled(Tab("B"), "other-profile") with { GroupId = groupId };
        var outside = Tab("Outside"); var original = Session([a, b, outside]) with
        { Groups = [new(groupId, "Research", false, [a.Id, b.Id]) { Color = "#7254C7" }] };
        var splitId = Guid.NewGuid();
        var split = new BrowseSplitViewProjection(splitId, [b.Id, a.Id], b.Id, SplitOrientation.Horizontal, .65);
        var projection = Projection([Project(a) with { Group = groupId.ToString("D") },
            Project(b) with { Group = groupId.ToString("D"), ProfileId = "other-profile" }, Project(outside)], outside.Id) with
        { PaneTabIds = [], SplitViews = [split], Groups = [new(groupId, "Research", true, [a.Id, b.Id]) { Color = "#7254C7" }] };
        var merged = Merge(original, projection, a.Id, b.Id, outside.Id);
        var window = merged.Windows[0]; var saved = Assert.Single(window.SplitViews);
        Assert.Equal(WorkspaceLayoutKind.Single, window.Layout.Kind); Assert.Equal(outside.Id, window.SelectedTabId);
        Assert.Equal(splitId, saved.Id); Assert.Equal(b.Id, saved.FocusedTabId);
        Assert.Equal(new[] { b.Id, a.Id }, saved.Panes.OrderBy(pane => pane.Order).Select(pane => pane.TabId));
        Assert.Equal(a.NavigationJson, merged.Tabs.Single(tab => tab.Id == a.Id).NavigationJson);
        Assert.Equal("#7254C7", Assert.Single(merged.Groups).Color); Assert.True(merged.Groups[0].IsCollapsed);
        var cold = JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(JsonSerializer.Serialize(merged))!;
        var loaded = Assert.Single(UnifiedWorkspaceSessionProjection.ProjectSplitViews(cold.Windows[0], projection.Tabs));
        Assert.Equal(splitId, loaded.Id); Assert.Equal(split.PaneTabIds, loaded.PaneTabIds);
        var active = Merge(cold, projection with { SelectedTabId = b.Id, PaneTabIds = split.PaneTabIds,
            SplitOrientation = split.Orientation, PrimaryPaneRatio = split.PrimaryPaneRatio }, a.Id, b.Id, outside.Id);
        Assert.Equal(splitId, active.Windows[0].Layout.Id);
        Assert.Equal(saved.Panes.Select(pane => pane.Id), active.Windows[0].Layout.Panes.Select(pane => pane.Id));
        var unsplit = Merge(active, projection with { SplitViews = [] }, a.Id, b.Id, outside.Id);
        Assert.Empty(unsplit.Windows[0].SplitViews); Assert.Equal(3, unsplit.Tabs.Count);
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("overlap")]
    [InlineData("foreign-member")]
    [InlineData("foreign-object")]
    [InlineData("private-member")]
    [InlineData("invalid-focus")]
    [InlineData("invalid-ratio")]
    [InlineData("invalid-orientation")]
    [InlineData("active-mismatch")]
    public void Invalid_saved_split_projection_refuses_without_mutating_the_canonical_snapshot(string scenario)
    {
        var a = Tab("A"); var b = Tab("B"); var outside = Tab("Outside"); var remote = Tab("Remote"); var remoteOther = Tab("Remote other");
        var original = Session([a, b, outside, remote, remoteOther]);
        var main = original.Windows[0] with { OrderedTabIds = [a.Id, b.Id, outside.Id] };
        var remoteSplitId = Guid.NewGuid();
        var other = Window([remote.Id, remoteOther.Id], WorkspaceWindowKind.Normal) with
        { SplitViews = [new(remoteSplitId, [new(Guid.NewGuid(), remote.Id, 0), new(Guid.NewGuid(), remoteOther.Id, 1)],
            remote.Id, SplitOrientation.Vertical, .5)] };
        original = original with { Windows = [main, other] };
        var split = new BrowseSplitViewProjection(Guid.NewGuid(), [a.Id, b.Id], b.Id, SplitOrientation.Vertical, .5);
        var projection = Projection([Project(a), Project(b), Project(outside)], outside.Id) with { PaneTabIds = [], SplitViews = [split] };
        switch (scenario)
        {
            case "duplicate-id": projection = projection with { SplitViews = [split, split] }; break;
            case "overlap": projection = projection with { SplitViews = [split, split with { Id = Guid.NewGuid(), PaneTabIds = [b.Id, outside.Id] }] }; break;
            case "foreign-member": projection = projection with { SplitViews = [split with { PaneTabIds = [a.Id, remote.Id], FocusedTabId = a.Id }] }; break;
            case "foreign-object": projection = projection with { SplitViews = [split with { Id = remoteSplitId }] }; break;
            case "private-member": projection = projection with { Tabs = [Project(a), Project(b) with { Privacy = BrowserTabPrivacy.Private }, Project(outside)] }; break;
            case "invalid-focus": projection = projection with { SplitViews = [split with { FocusedTabId = outside.Id }] }; break;
            case "invalid-ratio": projection = projection with { SplitViews = [split with { PrimaryPaneRatio = double.NaN }] }; break;
            case "invalid-orientation": projection = projection with { SplitViews = [split with { Orientation = (SplitOrientation)99 }] }; break;
            case "active-mismatch": projection = projection with { SelectedTabId = a.Id }; break;
        }
        var before = JsonSerializer.Serialize(original);
        Assert.Throws<InvalidDataException>(() => Merge(original, projection, a.Id, b.Id, outside.Id));
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Fact]
    public void Unprojectable_saved_split_is_preserved_when_the_port_saves_its_other_tabs()
    {
        var a = Tab("A"); var opaque = Tab("Opaque") with { StateJson = "opaque future state" }; var outside = Tab("Outside");
        var original = Session([a, opaque, outside]); var splitId = Guid.NewGuid();
        var split = new WorkspaceSplitViewSnapshot(splitId, [new(Guid.NewGuid(), a.Id, 0), new(Guid.NewGuid(), opaque.Id, 1)],
            a.Id, SplitOrientation.Vertical, .5);
        original = original with { Windows = [original.Windows[0] with { SelectedTabId = outside.Id, SplitViews = [split] }] };
        var projection = Projection([Project(a), Project(outside)], outside.Id) with { PaneTabIds = [], SplitViews = [] };
        Assert.Empty(UnifiedWorkspaceSessionProjection.ProjectSplitViews(original.Windows[0], projection.Tabs));
        var merged = Merge(original, projection, a.Id, outside.Id);
        Assert.Equal(JsonSerializer.Serialize(split), JsonSerializer.Serialize(Assert.Single(merged.Windows[0].SplitViews)));
        Assert.Equal(opaque, merged.Tabs.Single(tab => tab.Id == opaque.Id));
    }

    [Fact]
    public void Group_colour_roundtrips_and_legacy_omission_does_not_erase_it()
    {
        var id = Guid.NewGuid(); var a = Tab("A") with { GroupId = id };
        var original = Session([a]) with { Groups = [new(id, "Research", false, [a.Id]) { Color = "#7254C7" }] };
        var row = Project(a) with { Group = id.ToString("D") };
        var projection = Projection([row], a.Id) with { Groups = [new(id, "Renamed", true, [a.Id])] };
        var retained = Merge(original, projection, a.Id);
        Assert.Equal("#7254C7", Assert.Single(retained.Groups).Color);
        var changed = Merge(retained, projection with { Groups = [projection.Groups[0] with { Color = "#12abEF" }] }, a.Id);
        Assert.Equal("#12ABEF", Assert.Single(changed.Groups).Color);
        var reset = Merge(changed, projection with { Groups = [projection.Groups[0] with { Color = string.Empty }] }, a.Id);
        Assert.Null(Assert.Single(reset.Groups).Color);
        var before = JsonSerializer.Serialize(original);
        Assert.Throws<InvalidDataException>(() => Merge(original,
            projection with { Groups = [projection.Groups[0] with { Color = "invalid" }] }, a.Id));
        Assert.Throws<InvalidDataException>(() => Merge(original with { Groups = [original.Groups[0] with { Color = "invalid" }] },
            projection, a.Id)); // Omission does not legitimise malformed retained metadata.
        Assert.Equal(before, JsonSerializer.Serialize(original));
    }

    [Fact]
    public void First_projection_retains_unrepresented_rows_and_overlays_current_order_without_mutating_input()
    {
        var a = Tab("A"); var unseen = Tab("Unprojectable") with { StateJson = "opaque future state" };
        var b = Tab("B"); var tail = Tab("Unseen tail"); var added = Tab("New");
        var original = Session([a, unseen, b, tail]);
        var before = JsonSerializer.Serialize(original);
        var current = Projection([Project(b), Project(a), Project(added)], b.Id);

        var merged = Merge(original, current);

        Assert.Equal(new[] { b.Id, unseen.Id, a.Id, tail.Id, added.Id }, merged.Windows[0].OrderedTabIds);
        Assert.Equal(5, merged.Tabs.Count);
        Assert.Equal(unseen, merged.Tabs.Single(tab => tab.Id == unseen.Id));
        Assert.Equal(tail, merged.Tabs.Single(tab => tab.Id == tail.Id));
        Assert.Equal(before, JsonSerializer.Serialize(original));
        Assert.Equal(original.SavedAt, merged.SavedAt);
    }

    [Fact]
    public void Closing_only_previously_managed_tabs_preserves_shared_rows_and_other_windows()
    {
        var shared = Tab("Shared"); var closed = Tab("Closed"); var unseen = Tab("Unseen"); var stay = Tab("Stay");
        var original = Session([shared, closed, unseen, stay]);
        var other = Window([shared.Id], WorkspaceWindowKind.Normal);
        original = original with { Windows = [original.Windows[0], other] };
        var beforeOther = JsonSerializer.Serialize(other);

        var merged = Merge(original, Projection([Project(stay)], stay.Id), shared.Id, closed.Id, stay.Id);

        Assert.Equal(new[] { unseen.Id, stay.Id }, merged.Windows[0].OrderedTabIds);
        Assert.DoesNotContain(merged.Tabs, tab => tab.Id == closed.Id);
        Assert.Contains(merged.Tabs, tab => tab.Id == shared.Id);
        Assert.Contains(merged.Tabs, tab => tab.Id == unseen.Id);
        Assert.Equal(beforeOther, JsonSerializer.Serialize(merged.Windows[1]));
    }

    [Fact]
    public void Recently_closed_profiles_are_bounded_and_round_trip_without_becoming_open_rows()
    {
        var open = Tab("Open"); var closedId = Guid.NewGuid();
        var closed = new BrowseClosedTabProjection(
            new(closedId, "Closed research", "https://research.example/", BrowserTabPrivacy.Standard,
                string.Empty, DateTimeOffset.UnixEpoch.AddDays(2))
            { ProfileId = "alternate-profile", IsPinned = true }, 3, BrowseEngineKind.Chromium);
        var original = Session([open]);
        var merged = Merge(original, Projection([Project(open)], open.Id) with { ClosedTabs = [closed] }, open.Id);
        var window = Assert.Single(merged.Windows);
        var recovery = Assert.Single(window.RecentlyClosedTabs);
        Assert.Equal(closedId, recovery.Id); Assert.Equal("alternate-profile", recovery.ProfileId);
        Assert.Equal("https://research.example/", recovery.Address); Assert.Equal(3, recovery.Index);
        Assert.Equal("Chromium", recovery.EngineOverride); Assert.True(recovery.IsPinned);
        Assert.Single(merged.Tabs); Assert.Equal(new[] { open.Id }, window.OrderedTabIds);

        var serialized = JsonSerializer.Serialize(merged);
        var restored = JsonSerializer.Deserialize<WorkspaceSessionSnapshot>(serialized)!;
        Assert.Equal(serialized, JsonSerializer.Serialize(restored));
        Assert.Equal(recovery, Assert.Single(Assert.Single(restored.Windows).RecentlyClosedTabs));
    }

    [Fact]
    public void Recently_closed_private_rows_and_open_identity_collisions_are_refused()
    {
        var open = Tab("Open");
        var closed = new BrowseClosedTabProjection(Project(open), 0, null);
        var original = Session([open]);
        var privateProjection = Projection([Project(open)], open.Id) with
        { ClosedTabs = [closed with { Tab = closed.Tab with { Privacy = BrowserTabPrivacy.Private } }] };
        Assert.Throws<InvalidDataException>(() => Merge(original, privateProjection, open.Id));
        var collision = Projection([Project(open)], open.Id) with { ClosedTabs = [closed] };
        Assert.Throws<InvalidDataException>(() => Merge(original, collision, open.Id));
        var overCapacity = Enumerable.Range(0, 26).Select(index => closed with
        { Tab = closed.Tab with { Id = Guid.NewGuid(), Title = "Closed " + index } }).ToArray();
        Assert.Throws<InvalidDataException>(() => Merge(original,
            Projection([Project(open)], open.Id) with { ClosedTabs = overCapacity }, open.Id));
    }

    [Fact]
    public void Reopening_a_canonical_recent_tab_preserves_its_profile_but_does_not_authorize_another_profile()
    {
        var id = Guid.NewGuid();
        var closed = new WorkspaceClosedTabSnapshot(id, "Saved research", "https://research.example/",
            "alternate-profile", null, 0, null, false, DateTimeOffset.UnixEpoch);
        var window = Window([], WorkspaceWindowKind.Main) with { RecentlyClosedTabs = [closed] };
        var original = new WorkspaceSessionSnapshot(WorkspaceSessionSnapshot.CurrentSchemaVersion,
            [], [], [window], DateTimeOffset.UnixEpoch);
        var restored = new BrowserTabState(id, closed.Title, closed.Address, BrowserTabPrivacy.Standard,
            string.Empty, closed.ClosedAt) { ProfileId = "alternate-profile" };

        var merged = UnifiedWorkspaceSessionProjection.Merge(original, window.Id, "actual-profile",
            Projection([restored], id), new HashSet<Guid>());

        Assert.True(UnifiedWorkspaceTabState.TryProject(Assert.Single(merged.Tabs), "actual-profile", out var projected));
        Assert.Equal("alternate-profile", projected!.ProfileId);
        Assert.Empty(Assert.Single(merged.Windows).RecentlyClosedTabs);
        Assert.Throws<InvalidDataException>(() => UnifiedWorkspaceSessionProjection.Merge(original, window.Id,
            "actual-profile", Projection([restored with { ProfileId = "forged-profile" }], id), new HashSet<Guid>()));
    }

    [Fact]
    public void Reopening_a_legacy_recent_tab_without_profile_uses_the_current_window_profile()
    {
        var id = Guid.NewGuid();
        var closed = new WorkspaceClosedTabSnapshot(id, "Legacy research", "https://research.example/",
            string.Empty, null, 0, null, false, DateTimeOffset.UnixEpoch);
        var window = Window([], WorkspaceWindowKind.Main) with { RecentlyClosedTabs = [closed] };
        var original = new WorkspaceSessionSnapshot(WorkspaceSessionSnapshot.CurrentSchemaVersion,
            [], [], [window], DateTimeOffset.UnixEpoch);
        var restored = new BrowserTabState(id, closed.Title, closed.Address, BrowserTabPrivacy.Standard,
            string.Empty, closed.ClosedAt) { ProfileId = "actual-profile" };

        var merged = UnifiedWorkspaceSessionProjection.Merge(original, window.Id, "actual-profile",
            Projection([restored], id), new HashSet<Guid>());

        Assert.True(UnifiedWorkspaceTabState.TryProject(Assert.Single(merged.Tabs), "actual-profile", out var projected));
        Assert.Equal("actual-profile", projected!.ProfileId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Another_windows_pane_or_selection_also_keeps_a_shared_global_row(bool paneReference)
    {
        var shared = Tab("Shared"); var stay = Tab("Stay");
        var original = Session([shared, stay]);
        var other = Window([stay.Id], WorkspaceWindowKind.Normal) with
        {
            SelectedTabId = paneReference ? stay.Id : shared.Id
        };
        if (paneReference) other = other with
        {
            Layout = other.Layout with { Panes = [new(Guid.NewGuid(), shared.Id, 0)] }
        };
        original = original with { Windows = [original.Windows[0], other] };

        var merged = Merge(original, Projection([Project(stay)], stay.Id), shared.Id, stay.Id);

        Assert.Contains(merged.Tabs, tab => tab.Id == shared.Id);
        Assert.DoesNotContain(shared.Id, merged.Windows[0].OrderedTabIds);
        Assert.Equal(JsonSerializer.Serialize(other), JsonSerializer.Serialize(merged.Windows[1]));
    }

    [Fact]
    public void Address_update_preserves_document_state_navigation_scroll_flags_and_creation_time()
    {
        var tab = Tab("Original") with
        {
            StateJson = "{\"DocumentId\":\"canonical-resource\",\"extension\":{\"keep\":[1,2]}}",
            NavigationJson = "{\"back\":[\"same-resource\"]}", ScrollOffset = 310,
            IsPinned = true, IsProtected = true
        };
        var original = Session([tab]);
        var projected = Project(tab) with { Title = "Renamed", Address = "9to1://files", UpdatedAt = tab.UpdatedAt.AddDays(2) };

        var merged = Merge(original, Projection([projected], tab.Id), tab.Id);
        var actual = Assert.Single(merged.Tabs);

        using var state = JsonDocument.Parse(actual.StateJson);
        Assert.Equal("canonical-resource", state.RootElement.GetProperty("DocumentId").GetString());
        Assert.Equal(2, state.RootElement.GetProperty("extension").GetProperty("keep").GetArrayLength());
        Assert.Equal(tab.NavigationJson, actual.NavigationJson);
        Assert.Equal(tab.ScrollOffset, actual.ScrollOffset);
        Assert.Equal(tab.CreatedAt, actual.CreatedAt);
        Assert.True(actual.IsPinned); Assert.True(actual.IsProtected);
        Assert.Equal(projected.UpdatedAt, actual.UpdatedAt);
        Assert.Equal("files", actual.AppKey);
    }

    [Fact]
    public void Split_metadata_and_pane_identities_survive_projection_and_collapse_only_after_close()
    {
        var a = Tab("A"); var b = Tab("B"); var original = Session([a, b]);
        var layout = new WorkspaceLayoutSnapshot(Guid.NewGuid(), WorkspaceLayoutKind.Split,
            SplitOrientation.Horizontal, 0.65, [new(Guid.NewGuid(), a.Id, 0), new(Guid.NewGuid(), b.Id, 1)]);
        original = original with { Windows = [original.Windows[0] with { Layout = layout }] };
        var unchanged = Merge(original, Projection([Project(a), Project(b)], a.Id), a.Id, b.Id);
        Assert.Equal(JsonSerializer.Serialize(layout), JsonSerializer.Serialize(unchanged.Windows[0].Layout));

        var merged = Merge(unchanged, Projection([Project(b)], b.Id), a.Id, b.Id);
        var actual = merged.Windows[0];
        var pane = Assert.Single(actual.Layout.Panes);
        Assert.Equal(layout.Id, actual.Layout.Id);
        Assert.Equal(layout.Orientation, actual.Layout.Orientation);
        Assert.Equal(layout.PrimaryRatio, actual.Layout.PrimaryRatio);
        Assert.Equal(WorkspaceLayoutKind.Single, actual.Layout.Kind);
        Assert.Equal(layout.Panes[1].Id, pane.Id);
        Assert.Equal(b.Id, pane.TabId); Assert.Equal(0, pane.Order);
        Assert.Equal(original.Windows[0].BoundsJson, actual.BoundsJson);
    }

    [Fact]
    public void Browse_split_projection_persists_two_different_profile_panes_and_rejects_invalid_references()
    {
        var a = Profiled(Tab("A"), "profile-a"); var b = Profiled(Tab("B"), "profile-b"); var original = Session([a, b]);
        var paneIds = new[] { b.Id, a.Id };
        var split = Projection([Project(a) with { ProfileId = "profile-a" },
            Project(b) with { ProfileId = "profile-b" }], b.Id) with
        { PaneTabIds = paneIds, SplitOrientation = SplitOrientation.Vertical, PrimaryPaneRatio = 0.65 };
        var merged = Merge(original, split, a.Id, b.Id);
        var layout = merged.Windows[0].Layout;
        Assert.Equal(WorkspaceLayoutKind.Split, layout.Kind);
        Assert.Equal(SplitOrientation.Vertical, layout.Orientation);
        Assert.Equal(0.65, layout.PrimaryRatio, precision: 2);
        Assert.Equal(paneIds, layout.Panes.OrderBy(pane => pane.Order).Select(pane => pane.TabId));
        Assert.Equal(b.Id, merged.Windows[0].SelectedTabId);

        var privatePane = split with
        { Tabs = [Project(a) with { ProfileId = "profile-a" },
            Project(b) with { ProfileId = "profile-b", Privacy = BrowserTabPrivacy.Private }] };
        Assert.Throws<InvalidDataException>(() => Merge(original, privatePane, a.Id, b.Id));
        Assert.Throws<InvalidDataException>(() => Merge(original,
            split with { PaneTabIds = [a.Id, Guid.NewGuid()] }, a.Id, b.Id));
        Assert.Throws<InvalidDataException>(() => Merge(original,
            split with { PrimaryPaneRatio = 0.1 }, a.Id, b.Id));
        Assert.Throws<InvalidDataException>(() => Merge(original,
            split with { SplitOrientation = (SplitOrientation)99 }, a.Id, b.Id));
    }

    [Fact]
    public void Single_pane_tracks_selection_without_replacing_its_identity_and_private_selection_is_not_persisted()
    {
        var a = Tab("A"); var b = Tab("B"); var original = Session([a, b]);
        var priorPane = Assert.Single(original.Windows[0].Layout.Panes);
        var merged = Merge(original, Projection([Project(a), Project(b)], b.Id), a.Id, b.Id);
        var selected = Assert.Single(merged.Windows[0].Layout.Panes);
        Assert.Equal(priorPane.Id, selected.Id); Assert.Equal(b.Id, selected.TabId);

        var privateSelected = Merge(merged, Projection([Project(a), Project(b)], null), a.Id, b.Id);
        Assert.Null(privateSelected.Windows[0].SelectedTabId);
        Assert.Empty(privateSelected.Windows[0].Layout.Panes);
        Assert.Equal(2, privateSelected.Tabs.Count);
    }

    [Fact]
    public void Existing_group_names_resolve_to_identity_and_keep_collapse_unseen_members_and_order()
    {
        var groupId = Guid.NewGuid();
        var a = Tab("A") with { GroupId = groupId }; var unseen = Tab("Unseen") with { GroupId = groupId };
        var b = Tab("B") with { GroupId = groupId };
        var original = Session([a, unseen, b]) with { Groups = [new(groupId, "Research", true, [a.Id, unseen.Id, b.Id])] };
        var merged = Merge(original, Projection([
            Project(b) with { Group = "research" }, Project(a) with { Group = groupId.ToString("D") }], b.Id), a.Id, b.Id);

        var group = Assert.Single(merged.Groups);
        Assert.Equal(groupId, group.Id); Assert.Equal("Research", group.Name); Assert.True(group.IsCollapsed);
        Assert.Equal(new[] { b.Id, unseen.Id, a.Id }, group.OrderedTabIds);
        Assert.All(merged.Tabs, tab => Assert.Equal(groupId, tab.GroupId));
    }

    [Fact]
    public void Canonical_group_metadata_updates_existing_guid_and_order_without_replacing_other_windows()
    {
        var groupId = Guid.NewGuid();
        var a = Profiled(Tab("A"), "actual-profile") with { GroupId = groupId };
        var b = Profiled(Tab("B"), "other-profile") with { GroupId = groupId };
        var original = Session([a, b]) with { Groups = [new(groupId, "Research", false, [a.Id, b.Id])] };
        var projection = Projection([Project(b) with { Group = groupId.ToString("D"), ProfileId = "other-profile" },
            Project(a) with { Group = groupId.ToString("D"), ProfileId = "actual-profile" }], b.Id) with
        { Groups = [new(groupId, "Project notes", true, [b.Id, a.Id])] };

        var merged = Merge(original, projection, a.Id, b.Id);

        var group = Assert.Single(merged.Groups);
        Assert.Equal(groupId, group.Id); Assert.Equal("Project notes", group.Name); Assert.True(group.IsCollapsed);
        Assert.Equal(new[] { b.Id, a.Id }, group.OrderedTabIds);
        Assert.All(merged.Tabs, tab => Assert.Equal(groupId, tab.GroupId));
        Assert.True(UnifiedWorkspaceTabState.TryProject(merged.Tabs.Single(tab => tab.Id == b.Id), "fallback", out var restoredB));
        Assert.Equal("other-profile", restoredB!.ProfileId);
    }

    [Fact]
    public void Group_mutation_refuses_cross_window_membership_stale_names_and_private_members()
    {
        var groupId = Guid.NewGuid(); var a = Tab("A") with { GroupId = groupId };
        var b = Profiled(Tab("B"), "other-profile") with { GroupId = groupId }; var original = Session([a, b]) with
        { Groups = [new(groupId, "Research", false, [a.Id, b.Id])] };
        var foreignWindow = Window([b.Id], WorkspaceWindowKind.Normal);
        var shared = original with { Windows = [original.Windows[0], foreignWindow] };
        var mutation = Projection([Project(a) with { Group = groupId.ToString("D") },
            Project(b) with { Group = groupId.ToString("D"), ProfileId = "other-profile" }], a.Id) with
        { Groups = [new(groupId, "Renamed", true, [a.Id, b.Id])] };
        Assert.Throws<InvalidDataException>(() => Merge(shared, mutation, a.Id, b.Id));

        var mismatched = mutation with { Groups = [new(groupId, "Research", true, [a.Id])] };
        Assert.Throws<InvalidDataException>(() => Merge(original, mismatched, a.Id, b.Id));
        var mixedProfile = Merge(original, Projection([
            Project(a) with { Group = groupId.ToString("D"), ProfileId = "actual-profile" },
            Project(b) with { Group = groupId.ToString("D"), ProfileId = "other-profile" }], a.Id) with
        { Groups = [new(groupId, "Research", false, [a.Id, b.Id])] }, a.Id, b.Id);
        Assert.Equal(groupId, mixedProfile.Tabs.Single(tab => tab.Id == b.Id).GroupId);
        Assert.True(UnifiedWorkspaceTabState.TryProject(mixedProfile.Tabs.Single(tab => tab.Id == b.Id), "fallback", out var retainedOtherProfile));
        Assert.Equal("other-profile", retainedOtherProfile!.ProfileId);
        var privateMember = Project(b) with { Group = groupId.ToString("D"), Privacy = BrowserTabPrivacy.Private };
        Assert.Throws<InvalidDataException>(() => Merge(original, Projection([
            Project(a) with { Group = groupId.ToString("D") }, privateMember], a.Id) with
        { Groups = [new(groupId, "Research", false, [a.Id, b.Id])] }, a.Id, b.Id));
    }

    [Fact]
    public void Existing_tab_profile_cannot_be_rewritten_by_a_projection()
    {
        var tab = Profiled(Tab("Retained profile"), "other-profile");
        var original = Session([tab]);
        var forged = Projection([Project(tab) with { ProfileId = "forged-profile" }], tab.Id);

        Assert.Throws<InvalidDataException>(() => Merge(original, forged, tab.Id));
    }

    [Fact]
    public void New_group_name_has_stable_identity_and_membership_can_move_without_erasing_other_members()
    {
        var oldId = Guid.NewGuid(); var a = Tab("A") with { GroupId = oldId }; var unseen = Tab("Unseen") with { GroupId = oldId };
        var b = Tab("B");
        var original = Session([a, unseen, b]) with { Groups = [new(oldId, "Old", true, [a.Id, unseen.Id])] };
        var projection = Projection([Project(a) with { Group = "Trip" }, Project(b) with { Group = "trip" }], a.Id);
        var merged = Merge(original, projection, a.Id, b.Id);
        var trip = merged.Groups.Single(group => group.Name == "Trip");
        var repeated = Merge(original, projection, a.Id, b.Id);
        var next = Merge(merged, projection, a.Id, b.Id);

        Assert.NotEqual(Guid.Empty, trip.Id);
        Assert.Equal(trip.Id, repeated.Groups.Single(group => group.Name == "Trip").Id);
        Assert.Equal(trip.Id, next.Groups.Single(group => group.Name == "Trip").Id);
        Assert.Equal(new[] { a.Id, b.Id }, trip.OrderedTabIds);
        Assert.Equal(new[] { unseen.Id }, merged.Groups.Single(group => group.Id == oldId).OrderedTabIds);
        Assert.True(merged.Groups.Single(group => group.Id == oldId).IsCollapsed);

        var ungrouped = Merge(merged, Projection([Project(a), Project(b) with { Group = trip.Id.ToString("D") }], b.Id), a.Id, b.Id);
        Assert.Null(ungrouped.Tabs.Single(tab => tab.Id == a.Id).GroupId);
        Assert.Equal(new[] { b.Id }, ungrouped.Groups.Single(group => group.Id == trip.Id).OrderedTabIds);
    }

    [Fact]
    public void Later_opaque_state_refusal_does_not_publish_earlier_tab_or_new_group_changes()
    {
        var a = Tab("A"); var b = Tab("Opaque") with { StateJson = "[1,2]" };
        var original = Session([a, b]);
        var before = JsonSerializer.Serialize(original);
        var projection = Projection([Project(a) with { Group = "New group", Title = "Changed" }, Project(b)], a.Id);

        Assert.Throws<InvalidDataException>(() => Merge(original, projection, a.Id, b.Id));

        Assert.Equal(before, JsonSerializer.Serialize(original));
        Assert.Empty(original.Groups);
        Assert.Equal("A", original.Tabs[0].Title);
    }

    [Fact]
    public void Closing_the_last_managed_tab_keeps_layout_identity_and_clears_its_membership()
    {
        var groupId = Guid.NewGuid(); var tab = Tab("Only tab") with { GroupId = groupId };
        var original = Session([tab]) with { Groups = [new(groupId, "Saved group", true, [tab.Id])] };

        var merged = Merge(original, Projection([], null), tab.Id);

        Assert.Empty(merged.Tabs);
        Assert.Empty(merged.Windows[0].OrderedTabIds);
        Assert.Empty(merged.Windows[0].Layout.Panes);
        Assert.Null(merged.Windows[0].SelectedTabId);
        Assert.Equal(original.Windows[0].Layout.Id, merged.Windows[0].Layout.Id);
        Assert.Equal(original.Windows[0].BoundsJson, merged.Windows[0].BoundsJson);
        Assert.Empty(Assert.Single(merged.Groups).OrderedTabIds);
        Assert.True(merged.Groups[0].IsCollapsed);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("empty-id")]
    [InlineData("private")]
    [InlineData("foreign-selection")]
    [InlineData("foreign-managed")]
    [InlineData("other-window-collision")]
    [InlineData("unknown-group-id")]
    [InlineData("ambiguous-group-name")]
    [InlineData("future-schema")]
    [InlineData("duplicate-original")]
    [InlineData("unknown-window")]
    [InlineData("opaque-overwrite")]
    public void Invalid_projection_is_rejected_without_partial_input_changes(string scenario)
    {
        var a = Tab("A"); var b = Tab("Other"); var original = Session([a, b]);
        var owning = original.Windows[0] with { OrderedTabIds = [a.Id] };
        original = original with { Windows = [owning, Window([b.Id], WorkspaceWindowKind.Normal)] };
        var current = Projection([Project(a)], a.Id); var managed = new HashSet<Guid> { a.Id }; var windowId = owning.Id;
        switch (scenario)
        {
            case "duplicate": current = current with { Tabs = [Project(a), Project(a)] }; break;
            case "empty-id": current = current with { Tabs = [Project(a) with { Id = Guid.Empty }] }; break;
            case "private": current = current with { Tabs = [Project(a) with { Privacy = BrowserTabPrivacy.Private }] }; break;
            case "foreign-selection": current = current with { SelectedTabId = b.Id }; break;
            case "foreign-managed": managed.Add(b.Id); break;
            case "other-window-collision": current = Projection([Project(a), Project(b)], a.Id); break;
            case "unknown-group-id": current = current with { Tabs = [Project(a) with { Group = Guid.NewGuid().ToString("D") }] }; break;
            case "ambiguous-group-name":
                original = original with { Groups = [new(Guid.NewGuid(), "Same", false, []), new(Guid.NewGuid(), "same", true, [])] };
                current = current with { Tabs = [Project(a) with { Group = "Same" }] }; break;
            case "future-schema": original = original with { SchemaVersion = WorkspaceSessionSnapshot.CurrentSchemaVersion + 1 }; break;
            case "duplicate-original": original = original with { Tabs = [a, a, b] }; break;
            case "unknown-window": windowId = Guid.NewGuid(); break;
            case "opaque-overwrite": original = original with { Tabs = [a with { StateJson = "[1,2]" }, b] }; break;
        }
        var beforeOriginal = JsonSerializer.Serialize(original); var beforeCurrent = JsonSerializer.Serialize(current);
        var beforeManaged = managed.ToArray();

        Assert.Throws<InvalidDataException>(() => UnifiedWorkspaceSessionProjection.Merge(original, windowId, "actual-profile", current, managed));

        Assert.Equal(beforeOriginal, JsonSerializer.Serialize(original));
        Assert.Equal(beforeCurrent, JsonSerializer.Serialize(current));
        Assert.Equal(beforeManaged, managed.ToArray());
    }

    private static WorkspaceSessionSnapshot Merge(WorkspaceSessionSnapshot original, BrowseTabSessionProjection current,
        params Guid[] managed) => UnifiedWorkspaceSessionProjection.Merge(original, original.Windows[0].Id, "actual-profile", current, managed.ToHashSet());
    private static BrowseTabSessionProjection Projection(IReadOnlyList<BrowserTabState> tabs, Guid? selected) => new(tabs, selected);
    private static BrowserTabState Project(TabSessionSnapshot tab) =>
        new(tab.Id, tab.Title, "https://example.test/" + tab.Id.ToString("D"), BrowserTabPrivacy.Standard, string.Empty, tab.UpdatedAt)
            { ProfileId = "actual-profile" };
    private static TabSessionSnapshot Profiled(TabSessionSnapshot tab, string profileId) =>
        UnifiedWorkspaceTabState.Merge(tab, Project(tab) with { ProfileId = profileId });
    private static TabSessionSnapshot Tab(string title) => new(Guid.NewGuid(), "browse", title, "{}", null, null,
        null, false, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));
    private static WorkspaceSessionSnapshot Session(IReadOnlyList<TabSessionSnapshot> tabs) =>
        new(WorkspaceSessionSnapshot.CurrentSchemaVersion, tabs, [], [Window(tabs.Select(tab => tab.Id).ToArray(), WorkspaceWindowKind.Main)],
            DateTimeOffset.UnixEpoch.AddDays(3));
    private static WorkspaceWindowSnapshot Window(IReadOnlyList<Guid> tabs, WorkspaceWindowKind kind) =>
        new(Guid.NewGuid(), kind, new(Guid.NewGuid(), WorkspaceLayoutKind.Single, SplitOrientation.Vertical, 0.7,
            tabs.Count == 0 ? [] : [new(Guid.NewGuid(), tabs[0], 0)]), tabs, tabs.Count == 0 ? null : tabs[0],
            "{\"X\":18,\"Y\":27,\"Width\":1280,\"Height\":900}", DateTimeOffset.UnixEpoch);
}
