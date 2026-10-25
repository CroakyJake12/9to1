using System.Security.Cryptography;
using System.Text;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>Merges one window's narrow Browse projection into the canonical DTO.
/// No repository, presentation, clock or app admission is owned here.</summary>
internal static class UnifiedWorkspaceSessionProjection
{
    internal static WorkspaceSessionSnapshot Merge(WorkspaceSessionSnapshot original, Guid windowId,
        BrowseTabSessionProjection current, IReadOnlySet<Guid> previouslyManagedTabIds)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previouslyManagedTabIds);
        if (original.SchemaVersion != WorkspaceSessionSnapshot.CurrentSchemaVersion)
            throw new InvalidDataException("This workspace schema cannot be safely rewritten.");
        if (windowId == Guid.Empty) throw new ArgumentException("An existing window identity is required.", nameof(windowId));

        DemandUnique(original.Tabs.Select(tab => tab.Id), "canonical tabs");
        DemandUnique(original.Groups.Select(group => group.Id), "canonical groups");
        DemandUnique(original.Windows.Select(window => window.Id), "canonical windows");
        foreach (var window in original.Windows)
            DemandUnique(window.OrderedTabIds, "window tab references");
        foreach (var group in original.Groups)
            DemandUnique(group.OrderedTabIds, "group tab references");
        DemandUnique(current.Tabs.Select(tab => tab.Id), "projected tabs");
        if (current.Tabs.Any(tab => tab.Privacy != BrowserTabPrivacy.Standard))
            throw new InvalidDataException("Private tabs cannot enter a canonical workspace projection.");
        DemandUnique(current.ClosedTabs.Select(item => item.Tab.Id), "recently closed tabs");
        if (current.ClosedTabs.Count > 25)
            throw new InvalidDataException("The recently closed tab list exceeds its bounded recovery capacity.");
        var owningWindow = original.Windows.SingleOrDefault(window => window.Id == windowId)
            ?? throw new InvalidDataException("The owning window must be seeded before merging its projection.");
        DemandUnique(owningWindow.Layout.Panes.Select(pane => pane.Id), "owning window panes");
        var ownedIds = owningWindow.OrderedTabIds.ToHashSet();
        var currentIds = current.Tabs.Select(tab => tab.Id).ToHashSet();
        var paneTabIds = current.PaneTabIds ?? RestoreLegacyPanes(owningWindow, current.Tabs, current.SelectedTabId);
        if (paneTabIds.Count is not (0 or 2))
            throw new InvalidDataException("A Browse split must contain exactly two pane identities.");
        DemandUnique(paneTabIds, "split pane tabs");
        var closedTabIds = current.ClosedTabs.Select(item => item.Tab.Id).ToHashSet();
        if (closedTabIds.Overlaps(currentIds))
            throw new InvalidDataException("A tab cannot be both open and recently closed in the same window.");
        if (previouslyManagedTabIds.Any(id => !ownedIds.Contains(id)))
            throw new InvalidDataException("Managed tab identities must belong to the original owning window.");
        if (current.SelectedTabId is { } selected && !currentIds.Contains(selected))
            throw new InvalidDataException("The selected tab must belong to the non-private projection.");
        if (paneTabIds.Count == 2 && (paneTabIds.Any(id => !currentIds.Contains(id)) ||
            current.SelectedTabId is not { } focused || !paneTabIds.Contains(focused) ||
            paneTabIds.Any(id => current.Tabs.Single(tab => tab.Id == id).Privacy != BrowserTabPrivacy.Standard)))
            throw new InvalidDataException("Split panes must be open standard tabs, with focus inside the split.");
        var splitOrientation = current.SplitOrientation ?? (owningWindow.Layout.Kind == WorkspaceLayoutKind.Split
            ? owningWindow.Layout.Orientation : SplitOrientation.Vertical);
        var primaryPaneRatio = current.PrimaryPaneRatio ?? (owningWindow.Layout.Kind == WorkspaceLayoutKind.Split
            ? owningWindow.Layout.PrimaryRatio : 0.5);
        if (paneTabIds.Count == 2 && (!Enum.IsDefined(splitOrientation) || !double.IsFinite(primaryPaneRatio) ||
            primaryPaneRatio < WorkspaceLayoutSnapshot.MinimumPaneRatio ||
            primaryPaneRatio > WorkspaceLayoutSnapshot.MaximumPaneRatio))
            throw new InvalidDataException("Split orientation and primary pane ratio must be valid and bounded.");

        var otherReferences = original.Windows.Where(window => window.Id != windowId)
            .SelectMany(WindowReferences).ToHashSet();
        if (closedTabIds.Overlaps(otherReferences))
            throw new InvalidDataException("A recently closed tab identity is still owned by another window.");
        var otherClosedIds = original.Windows.Where(window => window.Id != windowId)
            .SelectMany(window => window.RecentlyClosedTabs.Select(tab => tab.Id)).ToHashSet();
        if (closedTabIds.Overlaps(otherClosedIds))
            throw new InvalidDataException("A recently closed tab identity cannot be recoverable in more than one window.");
        foreach (var closed in current.ClosedTabs)
        {
            var tab = closed.Tab;
            if (tab.Privacy != BrowserTabPrivacy.Standard || tab.ProfileId is null ||
                tab.ProfileId.Length > 0 && string.IsNullOrWhiteSpace(tab.ProfileId) || closed.Index < 0 ||
                closed.EngineOverride is { } engine && !Enum.IsDefined(engine) ||
                tab.Group.Length > 0 && (!Guid.TryParse(tab.Group, out var group) || group == Guid.Empty) ||
                !AppContentAddress.TryParse(tab.Address, out _))
                throw new InvalidDataException("A recently closed tab contains invalid or private restoration metadata.");
        }
        if (currentIds.Any(id => !ownedIds.Contains(id) && otherReferences.Contains(id)))
            throw new InvalidDataException("A new tab identity cannot replace another window's tab.");
        var closedIds = previouslyManagedTabIds.Where(id => !currentIds.Contains(id)).ToHashSet();
        var deletedIds = closedIds.Where(id => !otherReferences.Contains(id)).ToHashSet();
        var originalTabs = original.Tabs.ToDictionary(tab => tab.Id);
        var groups = original.Groups.ToList();
        if (current.Groups is { } projectedGroups)
            MergeGroupMetadata(groups, projectedGroups, current, owningWindow, original.Windows, windowId,
                ownedIds, otherReferences, deletedIds);
        var mergedTabs = new Dictionary<Guid, TabSessionSnapshot>();
        foreach (var tab in current.Tabs)
        {
            var groupId = ResolveGroup(groups, windowId, tab.Group);
            // The mapper preserves opaque state, navigation, scroll and tab flags.
            // Give it the resolved identity, never an ambiguous display name.
            var resolved = tab with { Group = groupId?.ToString("D") ?? string.Empty };
            mergedTabs.Add(tab.Id, UnifiedWorkspaceTabState.Merge(originalTabs.GetValueOrDefault(tab.Id), resolved));
        }

        var orderedIds = Overlay(owningWindow.OrderedTabIds.Where(id => !closedIds.Contains(id)),
            current.Tabs.Select(tab => tab.Id));
        var tabs = original.Tabs.Where(tab => !deletedIds.Contains(tab.Id))
            .Select(tab => mergedTabs.GetValueOrDefault(tab.Id) ?? tab)
            .Concat(current.Tabs.Where(tab => !originalTabs.ContainsKey(tab.Id)).Select(tab => mergedTabs[tab.Id]))
            .ToArray();

        var mergedGroups = groups.Select(group =>
        {
            var retained = group.OrderedTabIds.Where(id => !deletedIds.Contains(id) &&
                (!mergedTabs.TryGetValue(id, out var updated) || updated.GroupId == group.Id));
            var projected = current.Tabs.Where(tab => mergedTabs[tab.Id].GroupId == group.Id).Select(tab => tab.Id);
            return group with { OrderedTabIds = ReadOnly(Overlay(retained, projected)) };
        }).ToArray();

        var layout = owningWindow.Layout;
        if (paneTabIds.Count == 2)
        {
            var existingPaneIds = owningWindow.Layout.Panes.OrderBy(pane => pane.Order).Select(pane => pane.Id).ToArray();
            var panes = paneTabIds.Select((tabId, order) => new WorkspacePaneSnapshot(
                order < existingPaneIds.Length ? existingPaneIds[order] : StableId(windowId, "pane-" + order), tabId, order)).ToArray();
            layout = layout with { Kind = WorkspaceLayoutKind.Split, Orientation = splitOrientation,
                PrimaryRatio = primaryPaneRatio,
                Panes = ReadOnly(panes) };
        }
        else
        {
            // A single pane must show the persisted selection. Reuse its identity
            // when switching tabs or collapsing a split, instead of fabricating a layout.
            WorkspacePaneSnapshot[] panes;
            if (current.SelectedTabId is { } primary)
            {
                var existing = owningWindow.Layout.Panes.FirstOrDefault(pane => pane.TabId == primary) ??
                    owningWindow.Layout.Panes.OrderBy(pane => pane.Order).FirstOrDefault();
                panes = [existing is { } pane ? pane with { TabId = primary, Order = 0 } :
                    new WorkspacePaneSnapshot(StableId(windowId, "primary-pane"), primary, 0)];
            }
            else panes = [];
            layout = layout with { Kind = WorkspaceLayoutKind.Single, Panes = ReadOnly(panes) };
        }
        var latestUpdate = current.Tabs.Select(tab => tab.UpdatedAt).Append(owningWindow.UpdatedAt).Max();
        var changedWindow = owningWindow with
        {
            OrderedTabIds = ReadOnly(orderedIds), SelectedTabId = current.SelectedTabId,
            Layout = layout,
            RecentlyClosedTabs = ReadOnly(current.ClosedTabs.Select(item => new WorkspaceClosedTabSnapshot(
                item.Tab.Id, item.Tab.Title, item.Tab.Address, item.Tab.ProfileId!,
                Guid.TryParse(item.Tab.Group, out var group) ? group : null, item.Index,
                item.EngineOverride?.ToString(), item.Tab.IsPinned ?? false, item.Tab.UpdatedAt)).ToArray()),
            UpdatedAt = latestUpdate
        };
        return original with
        {
            Tabs = ReadOnly(tabs), Groups = ReadOnly(mergedGroups),
            Windows = ReadOnly(original.Windows.Select(window => Detach(window.Id == windowId ? changedWindow : window)).ToArray())
        };
    }

    private static Guid? ResolveGroup(List<TabGroupSnapshot> groups, Guid windowId, string value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0) return null;
        if (Guid.TryParse(name, out var id))
        {
            if (id == Guid.Empty || !groups.Any(group => group.Id == id))
                throw new InvalidDataException("A projected group identity must already exist.");
            return id;
        }
        var matches = groups.Where(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException("A group name must resolve to one existing identity.");
        if (matches.Length == 1) return matches[0].Id;
        id = StableId(windowId, "group/" + name.ToUpperInvariant());
        if (groups.Any(group => group.Id == id))
            throw new InvalidDataException("The new group identity conflicts with an existing group.");
        groups.Add(new(id, name, false, Array.Empty<Guid>()));
        return id;
    }

    private static void MergeGroupMetadata(List<TabGroupSnapshot> groups,
        IReadOnlyList<BrowseTabGroupProjection> projectedGroups, BrowseTabSessionProjection current,
        WorkspaceWindowSnapshot owningWindow, IReadOnlyList<WorkspaceWindowSnapshot> windows, Guid windowId,
        IReadOnlySet<Guid> ownedIds, IReadOnlySet<Guid> otherReferences, IReadOnlySet<Guid> deletedIds)
    {
        DemandUnique(projectedGroups.Select(group => group.Id), "projected groups");
        var currentTabs = current.Tabs.ToDictionary(tab => tab.Id);
        var closedGroupIds = current.ClosedTabs.Select(item => Guid.TryParse(item.Tab.Group, out var groupId)
            ? groupId : Guid.Empty).Where(id => id != Guid.Empty).ToHashSet();
        var previousClosedGroupIds = owningWindow.RecentlyClosedTabs.Where(tab => tab.GroupId.HasValue)
            .Select(tab => tab.GroupId!.Value).ToHashSet();
        var otherWindowGroupIds = windows.Where(window => window.Id != windowId)
            .SelectMany(window => window.RecentlyClosedTabs.Where(tab => tab.GroupId.HasValue).Select(tab => tab.GroupId!.Value))
            .ToHashSet();
        var touchedGroupIds = groups.Where(group => group.OrderedTabIds.Any(ownedIds.Contains) ||
            previousClosedGroupIds.Contains(group.Id) || closedGroupIds.Contains(group.Id))
            .Select(group => group.Id).ToHashSet();
        var externalGroupIds = groups.Where(group => group.OrderedTabIds.Any(otherReferences.Contains))
            .Select(group => group.Id).Concat(otherWindowGroupIds).ToHashSet();

        foreach (var group in projectedGroups)
        {
            var name = group.Name?.Trim() ?? string.Empty;
            if (group.Id == Guid.Empty || name.Length is < 1 or > 64 || name.Any(char.IsControl) ||
                group.OrderedTabIds is null || group.OrderedTabIds.Distinct().Count() != group.OrderedTabIds.Count ||
                group.OrderedTabIds.Any(id => !currentTabs.TryGetValue(id, out var tab) ||
                    !Guid.TryParse(tab.Group, out var tabGroupId) || tabGroupId != group.Id))
                throw new InvalidDataException("A tab group projection contains invalid identity, name, or membership.");
            var actualMembers = current.Tabs.Where(tab => Guid.TryParse(tab.Group, out var tabGroupId) && tabGroupId == group.Id)
                .Select(tab => tab.Id).ToHashSet();
            if (!actualMembers.SetEquals(group.OrderedTabIds))
                throw new InvalidDataException("A tab group must list every current member in its canonical order.");
            var memberTabs = group.OrderedTabIds.Select(id => currentTabs[id]).ToArray();
            if (memberTabs.Any(tab => tab.Privacy != BrowserTabPrivacy.Standard))
                throw new InvalidDataException("A tab group cannot contain private tabs.");
            if (externalGroupIds.Contains(group.Id) || groups.Any(existing => existing.Id == group.Id && !touchedGroupIds.Contains(group.Id)))
                throw new InvalidDataException("A tab group owned by another window cannot be changed here.");
            var index = groups.FindIndex(existing => existing.Id == group.Id);
            var updated = index >= 0
                ? groups[index] with { Name = name, IsCollapsed = group.IsCollapsed }
                : new TabGroupSnapshot(group.Id, name, group.IsCollapsed, Array.Empty<Guid>());
            if (index >= 0) groups[index] = updated; else groups.Add(updated);
            touchedGroupIds.Add(group.Id);
        }

        var retainedClosedGroups = closedGroupIds;
        groups.RemoveAll(group => touchedGroupIds.Contains(group.Id) &&
            !projectedGroups.Any(projected => projected.Id == group.Id) && !retainedClosedGroups.Contains(group.Id) &&
            !group.OrderedTabIds.Any(id => ownedIds.Contains(id) && !currentTabs.ContainsKey(id) && !deletedIds.Contains(id)));
        // Group identities cannot be borrowed or reassigned from a different window.
        if (projectedGroups.Any(group => groups.All(existing => existing.Id != group.Id)))
            throw new InvalidDataException("A projected group identity was not retained by the owning window.");
    }

    private static IReadOnlyList<Guid> RestoreLegacyPanes(WorkspaceWindowSnapshot window,
        IReadOnlyList<BrowserTabState> tabs, Guid? selectedTabId)
    {
        if (window.Layout.Kind != WorkspaceLayoutKind.Split || window.Layout.Panes.Count != 2) return [];
        var paneIds = window.Layout.Panes.OrderBy(pane => pane.Order).Select(pane => pane.TabId).ToArray();
        var rows = tabs.Where(tab => paneIds.Contains(tab.Id)).ToArray();
        return paneIds[0] != paneIds[1] && rows.Length == 2 && selectedTabId is { } selected && paneIds.Contains(selected) &&
            Enum.IsDefined(window.Layout.Orientation) && double.IsFinite(window.Layout.PrimaryRatio) &&
            window.Layout.PrimaryRatio >= WorkspaceLayoutSnapshot.MinimumPaneRatio &&
            window.Layout.PrimaryRatio <= WorkspaceLayoutSnapshot.MaximumPaneRatio &&
            rows.All(tab => tab.Privacy == BrowserTabPrivacy.Standard &&
                StringComparer.Ordinal.Equals(tab.ProfileId, rows[0].ProfileId)) ? paneIds : [];
    }

    // Keep unseen rows as anchors, fill represented slots in the current order,
    // then append any remaining new rows. Both inputs have unique identities.
    private static Guid[] Overlay(IEnumerable<Guid> original, IEnumerable<Guid> current)
    {
        var projected = current.ToArray();
        var represented = projected.ToHashSet();
        var output = new List<Guid>();
        var next = 0;
        foreach (var id in original)
            output.Add(represented.Contains(id) ? projected[next++] : id);
        output.AddRange(projected.Skip(next));
        return output.ToArray();
    }

    private static IEnumerable<Guid> WindowReferences(WorkspaceWindowSnapshot window) =>
        window.OrderedTabIds.Concat(window.Layout.Panes.Select(pane => pane.TabId))
            .Concat(window.SelectedTabId is { } id ? [id] : Array.Empty<Guid>());

    private static WorkspaceWindowSnapshot Detach(WorkspaceWindowSnapshot window) => window with
    {
        OrderedTabIds = ReadOnly(window.OrderedTabIds.ToArray()),
        Layout = window.Layout with { Panes = ReadOnly(window.Layout.Panes.ToArray()) },
        RecentlyClosedTabs = ReadOnly(window.RecentlyClosedTabs.ToArray())
    };

    private static IReadOnlyList<T> ReadOnly<T>(T[] values) => Array.AsReadOnly(values);

    private static void DemandUnique(IEnumerable<Guid> ids, string description)
    {
        var seen = new HashSet<Guid>();
        foreach (var id in ids)
            if (id == Guid.Empty || !seen.Add(id))
                throw new InvalidDataException($"The {description} contain an empty or duplicate identity.");
    }

    private static Guid StableId(Guid windowId, string purpose)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("9to1.workspace-projection/v1/" + windowId.ToString("D") + "/" + purpose));
        return new Guid(hash.AsSpan(0, 16));
    }
}
