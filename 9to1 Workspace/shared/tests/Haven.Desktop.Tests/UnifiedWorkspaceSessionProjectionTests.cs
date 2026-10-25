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

        Assert.Throws<InvalidDataException>(() => UnifiedWorkspaceSessionProjection.Merge(original, windowId, current, managed));

        Assert.Equal(beforeOriginal, JsonSerializer.Serialize(original));
        Assert.Equal(beforeCurrent, JsonSerializer.Serialize(current));
        Assert.Equal(beforeManaged, managed.ToArray());
    }

    private static WorkspaceSessionSnapshot Merge(WorkspaceSessionSnapshot original, BrowseTabSessionProjection current,
        params Guid[] managed) => UnifiedWorkspaceSessionProjection.Merge(original, original.Windows[0].Id, current, managed.ToHashSet());
    private static BrowseTabSessionProjection Projection(IReadOnlyList<BrowserTabState> tabs, Guid? selected) => new(tabs, selected);
    private static BrowserTabState Project(TabSessionSnapshot tab) =>
        new(tab.Id, tab.Title, "https://example.test/" + tab.Id.ToString("D"), BrowserTabPrivacy.Standard, string.Empty, tab.UpdatedAt)
            { ProfileId = "actual-profile" };
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
