using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Launcher;

namespace NineToOne.Launcher.Tests;

public sealed class LauncherLayoutTests
{
    [Fact]
    public async Task ActorWithoutCommitGuardCannotInitializeLauncherState()
    {
        using var f = new Fixture(); var actor = new UnguardedActors(f.Actors);
        var owner = new HomeLauncherLayoutStore(f.Home, actor, new ResourceAuthorizationService(actor, [new LauncherLayoutResourceResolver(f.Home)]), f.Registry);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.GetAsync());
        Assert.DoesNotContain((await f.Home.ReadAsync()).State!.Records, r => r.RecordType == HomeLauncherLayoutStore.RecordType);
    }
    private sealed class UnguardedActors(IAuthenticatedResourceActorSource source) : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => source.GetCurrentAsync(ct); }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ActorChangeWhileActualHomeWriteLeaseIsHeldCannotPersistLauncherState(bool initialize, bool changeProfile)
    {
        using var f = new Fixture();
        var before = initialize ? null : await f.Store.GetAsync();
        var recordId = HomeLauncherLayoutStore.RecordId(f.Actors.Current.ProfileId);
        var beforeRecord = (await f.Home.ReadAsync()).State!.Records.SingleOrDefault(r => r.RecordId == recordId);
        var blocked = new LeaseBlockingHomeStore(f.Home, f.StatePath);
        var owner = new HomeLauncherLayoutStore(blocked, f.Actors,
            new ResourceAuthorizationService(f.Actors, [new LauncherLayoutResourceResolver(f.Home)]), f.Registry);
        var pending = initialize ? owner.GetAsync() : owner.EditAsync(before!, layout => LauncherLayoutEdits.AddPage(layout, "Must not persist"));
        await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        f.Actors.Current = changeProfile ? f.Actors.Current with { ProfileId = "other-profile" }
            : f.Actors.Current with { AuthenticationRevision = "revoked-session" };
        blocked.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        var afterRecord = (await f.Home.ReadAsync()).State!.Records.SingleOrDefault(r => r.RecordId == recordId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(beforeRecord), System.Text.Json.JsonSerializer.Serialize(afterRecord));
    }

    [Fact]
    public async Task PresentationPersistsWithLayoutAndBackupWithoutRearrangingCanonicalItems()
    {
        using var f = new Fixture(); var original = await f.Store.GetAsync();
        var arranged = await f.Store.EditAsync(original, layout => LauncherLayoutEdits.MovePlacement(layout, layout.ActivePage.Items[0].Id, layout.ActivePageId, 2, 2));
        var expected = arranged.Current.ActivePage.Items.ToArray();
        var changed = await f.Store.EditAsync(arranged, layout => LauncherLayoutEdits.SetPresentation(layout, LauncherPresentation.Large with { ShowPackages = true }));
        var reopened = await f.Store.GetAsync();
        Assert.Equal(changed.Current.Presentation, reopened.Current.Presentation);
        Assert.Equal(expected, reopened.Current.ActivePage.Items);
        var restored = LauncherLayoutExchange.Import(LauncherLayoutExchange.Export(reopened), reopened.AuthorityId);
        Assert.Equal(reopened.Current.Presentation, restored.Presentation);
        Assert.Equal(expected, restored.ActivePage.Items);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.EditAsync(reopened, layout => LauncherLayoutEdits.SetPresentation(layout, new(IconSizeDp: 500))));
        await Assert.ThrowsAsync<IOException>(() => f.Store.EditAsync(arranged, layout => LauncherLayoutEdits.SetPresentation(layout, LauncherPresentation.Compact)));
        Assert.Equal(reopened.Revision, (await f.Store.GetAsync()).Revision);
    }

    [Fact]
    public async Task LegacyCanonicalOrderMigratesOnceAndPackageUpdatePreservesPlacementIdentity()
    {
        using var f = new Fixture(); var apps = await f.Registry.RefreshAsync(default);
        var initial = await f.Store.GetAsync([apps[1].ApplicationId, apps[0].ApplicationId]);
        Assert.Equal(new[] { apps[1].ApplicationId, apps[0].ApplicationId }, initial.Current.ActivePage.Items.Select(i => i.ApplicationId));
        var ids = initial.Current.ActivePage.Items.Select(i => i.Id).ToArray();
        f.Provider.Version = "2"; f.Provider.FirstLabel = "Renamed app"; await f.Registry.RefreshAsync(default);
        var reopened = await f.Store.GetAsync([apps[0].ApplicationId]);
        Assert.Equal(initial.Revision, reopened.Revision);
        Assert.Equal(ids, reopened.Current.ActivePage.Items.Select(i => i.Id));
        Assert.Equal(initial.Current.ActivePageId, reopened.Current.ActivePageId);
    }
    [Fact]
    public async Task MoveBetweenPagesAndReopenPreservesOriginalCanonicalApplicationAndPlacement()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync(); var item = initial.Current.ActivePage.Items[0];
        var added = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddPage(layout, "Work"));
        var moved = await f.Store.EditAsync(added, layout => LauncherLayoutEdits.MovePlacement(layout, item.Id, layout.ActivePageId, 2, 1));
        var reopened = await f.Store.GetAsync(); var actual = Assert.Single(reopened.Current.ActivePage.Items);
        Assert.Equal(item.Id, actual.Id); Assert.Equal(item.ApplicationId, actual.ApplicationId); Assert.Equal(2, actual.Column); Assert.Equal(1, actual.Row);
        Assert.Equal(moved.Revision, reopened.Revision); Assert.NotNull(reopened.Previous);
        Assert.DoesNotContain(reopened.Current.Pages[0].Items, i => i.Id == item.Id);
    }
    [Fact]
    public async Task ConcurrentEditAndForeignProfileCannotOverwriteWinner()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var saved = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.RenamePage(layout, layout.ActivePageId, "Winner"));
        await Assert.ThrowsAsync<IOException>(() => f.Store.EditAsync(initial, layout => LauncherLayoutEdits.RenamePage(layout, layout.ActivePageId, "Stale")));
        Assert.Equal("Winner", (await f.Store.GetAsync()).Current.ActivePage.Name);
        f.Actors.Current = f.Actors.Current with { ProfileId = "second-profile", AuthenticationRevision = "second-session" };
        await Assert.ThrowsAsync<IOException>(() => f.Store.EditAsync(saved, layout => LauncherLayoutEdits.AddPage(layout, "Foreign")));
        Assert.Single((await f.Store.GetAsync()).Current.Pages);
    }
    [Fact]
    public async Task InvalidAndUnknownAppEditsPreserveStoredRevision()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddApplication(layout, layout.ActivePageId, Guid.NewGuid())));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.EditAsync(initial, layout => LauncherLayoutEdits.MovePlacement(layout, layout.ActivePage.Items[0].Id, layout.ActivePageId, 100, 0)));
        Assert.Equal(initial.Revision, (await f.Store.GetAsync()).Revision);
    }
    [Fact]
    public async Task HiddenAppAndUnavailablePackageKeepOwnedLayoutWithoutCompetingPackageState()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync(); var item = initial.Current.ActivePage.Items[0];
        var hidden = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.SetHidden(layout, item.ApplicationId, true));
        f.Provider.IncludeApps = false; Assert.All(await f.Registry.RefreshAsync(default), app => Assert.False(app.Enabled));
        var reopened = await f.Store.GetAsync();
        Assert.Contains(item.ApplicationId, reopened.Current.HiddenApplications);
        Assert.Equal(hidden.Revision, reopened.Revision); Assert.Contains(reopened.Current.ActivePage.Items, p => p.Id == item.Id && p.ApplicationId == item.ApplicationId);
    }
    [Fact]
    public async Task ReadRequiresCurrentResourceResolverAndNewerSchemaIsNotReset()
    {
        using var f = new Fixture(); await f.Store.GetAsync();
        var denied = new HomeLauncherLayoutStore(f.Home, f.Actors, new ResourceAuthorizationService(f.Actors, []), f.Registry);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.GetAsync());
        var state = await f.Home.ReadAsync(); var record = state.State!.Records.Single(r => r.RecordType == HomeLauncherLayoutStore.RecordType);
        Assert.True((await f.Home.WriteAsync(record with { Revision = record.Revision + 1, SchemaVersion = 99 }, record.Revision)).IsSuccess);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.GetAsync());
        Assert.Equal(99, (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordId == record.RecordId).SchemaVersion);
    }
    [Fact]
    public void ReflowSwapAndPageDeletionPreserveAllPlacementIdentities()
    {
        var original = LauncherLayoutEdits.Seed(LauncherLayout.Empty(), Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()));
        var ids = original.Pages.SelectMany(p => p.Items).Select(i => i.Id).ToHashSet(); var first = original.Pages[0].Items[0]; var other = original.Pages[1].Items[0];
        var swapped = LauncherLayoutEdits.MovePlacement(original, first.Id, original.Pages[1].Id, other.Column, other.Row);
        Assert.Contains(swapped.Pages[0].Items, i => i.Id == other.Id);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(swapped),
            System.Text.Json.JsonSerializer.Serialize(LauncherLayoutEdits.Reflow(swapped, original.Rows, original.Columns)));
        var reflow = LauncherLayoutEdits.Reflow(swapped, 3, 3);
        Assert.True(ids.SetEquals(reflow.Pages.SelectMany(p => p.Items).Select(i => i.Id))); reflow.Validate();
        Assert.Throws<InvalidOperationException>(() => LauncherLayoutEdits.RemovePage(reflow, reflow.Pages[0].Id));
        var added = LauncherLayoutEdits.AddPage(reflow, "Empty");
        Assert.Equal(reflow.Pages.Count, LauncherLayoutEdits.RemovePage(added, added.ActivePageId).Pages.Count);
    }
    [Fact]
    public async Task BackupImportKeepsCanonicalIdsAndRejectsForeignUnknownOrStaleChanges()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var backup = LauncherLayoutExchange.Export(initial);
        var moved = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddPage(layout, "Work"));
        var restored = await f.Store.EditAsync(moved, _ => LauncherLayoutExchange.Import(backup, moved.AuthorityId));
        Assert.Equal(initial.Current.ActivePageId, restored.Current.ActivePageId);
        Assert.Equal(initial.Current.Pages[0].Items.Select(i => i.Id), restored.Current.Pages[0].Items.Select(i => i.Id));
        Assert.Equal("Work", restored.Previous!.ActivePage.Name);
        Assert.Throws<UnauthorizedAccessException>(() => LauncherLayoutExchange.Import(backup, "foreign-profile"));
        Assert.Throws<InvalidDataException>(() => LauncherLayoutExchange.Import(backup.Replace("\"Version\": 7", "\"Version\": 99"), restored.AuthorityId));
        await Assert.ThrowsAsync<IOException>(() => f.Store.EditAsync(moved, _ => LauncherLayoutExchange.Import(backup, moved.AuthorityId)));
        var invented = initial with { Current = LauncherLayoutEdits.AddApplication(initial.Current, initial.Current.ActivePageId, Guid.NewGuid()) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Store.EditAsync(restored, _ => LauncherLayoutExchange.Import(LauncherLayoutExchange.Export(invented), restored.AuthorityId)));
        Assert.Equal(restored.Revision, (await f.Store.GetAsync()).Revision);
    }

    [Fact]
    public async Task DockTransfersAndGridChangesKeepIdentityAndOwnedReferences()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync(); var item = initial.Current.ActivePage.Items[0];
        var docked = await f.Store.EditAsync(initial, layout =>
        {
            var configured = LauncherLayoutEdits.ConfigureDock(layout, 1, 4);
            return LauncherLayoutEdits.MoveToContainer(configured, item.Id, configured.Dock!.Id);
        });
        var reopened = await f.Store.GetAsync(); Assert.Equal(item.Id, Assert.Single(reopened.Current.Dock!.Items).Id);
        Assert.Equal(item.ApplicationId, reopened.Current.Dock.Items[0].ApplicationId);
        Assert.DoesNotContain(reopened.Current.ActivePage.Items, p => p.Id == item.Id);
        Assert.Throws<InvalidOperationException>(() => LauncherLayoutEdits.RemoveDock(reopened.Current));
        var resized = LauncherLayoutEdits.ConfigureDock(reopened.Current, 2, 3);
        Assert.Equal(reopened.Current.Dock.Id, resized.Dock!.Id);
        Assert.Equal(item.Id, LauncherLayoutExchange.Import(LauncherLayoutExchange.Export(reopened), reopened.AuthorityId).Dock!.Items[0].Id);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Store.EditAsync(reopened, layout => LauncherLayoutEdits.AddDockApplication(layout, Guid.NewGuid())));
        var returned = await f.Store.EditAsync(reopened, layout => LauncherLayoutEdits.MoveToContainer(layout, item.Id, layout.ActivePageId));
        Assert.Empty(returned.Current.Dock!.Items); Assert.Contains(returned.Current.ActivePage.Items, p => p.Id == item.Id);
        Assert.Null(LauncherLayoutEdits.RemoveDock(returned.Current).Dock);
    }

    [Fact]
    public void VersionOneLayoutUpgradesWithoutChangingExistingIdentities()
    {
        var old = LauncherLayoutEdits.Seed(LauncherLayout.Empty(), [Guid.NewGuid()]) with { SchemaVersion = 1 };
        var current = LauncherLayout.UpgradeKnownSchema(old);
        Assert.Equal(LauncherLayout.CurrentSchema, current.SchemaVersion); Assert.Equal(old.ActivePageId, current.ActivePageId);
        Assert.Equal(old.ActivePage.Items[0].Id, current.ActivePage.Items[0].Id); Assert.Null(current.Dock);
        Assert.Throws<InvalidDataException>(() => LauncherLayout.UpgradeKnownSchema(old with { SchemaVersion = 99 }));
        var oldDock = LauncherLayoutEdits.ConfigureDock(current, 1, 4) with { SchemaVersion = 2 };
        var upgradedDock = LauncherLayout.UpgradeKnownSchema(oldDock);
        Assert.Equal(oldDock.Dock!.Id, upgradedDock.Dock!.Id); Assert.Empty(upgradedDock.Folders);
    }

    [Fact]
    public async Task GoPageNavigationUsesCurrentOwnerAndPersistsOriginalPageIdentity()
    {
        using var f = new Fixture(); var first = await f.Store.GetAsync();
        var second = await f.Store.EditAsync(first, layout => LauncherLayoutEdits.AddPage(layout, "Work"));
        var go = new LauncherNavigationGoProvider(f.Store); var results = new List<Haven.Application.Go.GoResult>();
        await foreach (var result in go.QueryAsync(new("Home", "Launcher Pages"), default)) results.Add(result);
        var home = Assert.Single(results); Assert.Equal(first.Current.ActivePageId.ToString("D"), home.Reference.Id);
        await go.InvokeAsync(home.Reference, "OpenPage", default);
        Assert.Equal(first.Current.ActivePageId, (await f.Store.GetAsync()).Current.ActivePageId);
        await Assert.ThrowsAsync<IOException>(() => go.InvokeAsync(home.Reference, "OpenPage", default));
        results.Clear(); await foreach (var result in go.QueryAsync(new("", "Apps"), default)) results.Add(result);
        Assert.Empty(results);
    }

    [Fact]
    public async Task FolderMovesPreserveAppIdentityAndRejectNestingLossOrUnknownApplications()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync(); var item = initial.Current.ActivePage.Items[0];
        var created = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.CreateFolder(layout, layout.ActivePageId, "Study"));
        var folder = Assert.Single(created.Current.Folders); var icon = created.Current.ActivePage.Items.Single(i => i.FolderId == folder.Id);
        var moved = await f.Store.EditAsync(created, layout => LauncherLayoutEdits.MoveToContainer(layout, item.Id, folder.Id));
        var reopened = await f.Store.GetAsync(); Assert.Equal(item.Id, Assert.Single(reopened.Current.Folders[0].Items).Id);
        Assert.Equal(item.ApplicationId, reopened.Current.Folders[0].Items[0].ApplicationId);
        Assert.Throws<InvalidOperationException>(() => LauncherLayoutEdits.RemovePlacement(reopened.Current, icon.Id));
        Assert.Throws<InvalidDataException>(() => LauncherLayoutEdits.MoveToContainer(reopened.Current, icon.Id, folder.Id));
        var renamed = LauncherLayoutEdits.ConfigureFolder(reopened.Current, folder.Id, "College", 3);
        Assert.Equal(folder.Id, renamed.Folders[0].Id); Assert.Equal(item.Id, renamed.Folders[0].Items[0].Id);
        var exported = LauncherLayoutExchange.Import(LauncherLayoutExchange.Export(reopened), reopened.AuthorityId);
        Assert.Equal(folder.Id, exported.Folders[0].Id); Assert.Equal(item.ApplicationId, exported.Folders[0].Items[0].ApplicationId);
        var bad = renamed with { Folders = [renamed.Folders[0] with { Items = [new(Guid.NewGuid(), Guid.NewGuid(), 0, 0)] }] };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Store.EditAsync(reopened, _ => bad));
        var returned = await f.Store.EditAsync(reopened, layout => LauncherLayoutEdits.MoveToContainer(layout, item.Id, layout.ActivePageId));
        var removed = await f.Store.EditAsync(returned, layout => LauncherLayoutEdits.RemovePlacement(layout, icon.Id));
        Assert.Empty(removed.Current.Folders); Assert.Contains(removed.Current.ActivePage.Items, i => i.Id == item.Id);
    }

    [Fact]
    public async Task LegacySerializedLayoutWithoutDockOrFoldersLoadsWithoutRewritingHome()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var record = (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordType == HomeLauncherLayoutStore.RecordType);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(record.Payload.GetRawText())!;
        var current = payload["Current"]!.AsObject(); current["SchemaVersion"] = 1; current.Remove("Dock"); current.Remove("Folders"); current.Remove("Presentation");
        foreach (var page in current["Pages"]!.AsArray()) foreach (var item in page!["Items"]!.AsArray()) item!.AsObject().Remove("FolderId");
        var old = record with { Revision = record.Revision + 1, Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload) };
        Assert.True((await f.Home.WriteAsync(old, record.Revision)).IsSuccess);
        var migrated = await f.Store.GetAsync();
        Assert.Equal(LauncherLayout.CurrentSchema, migrated.Current.SchemaVersion); Assert.Equal(initial.Current.ActivePageId, migrated.Current.ActivePageId);
        Assert.Equal(initial.Current.ActivePage.Items[0].Id, migrated.Current.ActivePage.Items[0].Id);
        var unchanged = (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordId == record.RecordId);
        Assert.Equal(old.Revision, unchanged.Revision); Assert.Equal(1, unchanged.Payload.GetProperty("Current").GetProperty("SchemaVersion").GetInt32());
        var edited = await f.Store.EditAsync(migrated, layout => LauncherLayoutEdits.CreateFolder(layout, layout.ActivePageId, "New folder"));
        Assert.Equal(old.Revision + 1, edited.Revision); Assert.Equal(LauncherLayout.CurrentSchema, edited.Current.SchemaVersion);
    }

    [Fact]
    public async Task GoDiscoveryDoesNotInitializeLauncherOrObservePackages()
    {
        using var f = new Fixture(); var go = new LauncherNavigationGoProvider(f.Store);
        var results = new List<Haven.Application.Go.GoResult>();
        await foreach (var result in go.QueryAsync(new("", "Launcher Pages"), default)) results.Add(result);
        Assert.Empty(results);
        var state = await f.Home.ReadAsync();
        Assert.DoesNotContain(state.State!.Records, r => r.RecordType is "launcher.layout" or "home.installed-apps");
    }

    [Fact]
    public async Task DrawerCategoriesPersistCanonicalMembershipAndRejectForeignOrStaleEdits()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var appIds = initial.Current.ActivePage.Items.Select(i => i.ApplicationId).ToArray();
        var added = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddDrawerCategory(layout, "Study"));
        var category = Assert.Single(added.Current.Drawer!.Categories);
        var populated = await f.Store.EditAsync(added, layout => LauncherLayoutEdits.SetDrawerCategoryMembership(
            LauncherLayoutEdits.SetDrawerCategoryMembership(layout, category.Id, appIds[0], true), category.Id, appIds[1], true));
        var ordered = await f.Store.EditAsync(populated, layout => LauncherLayoutEdits.SetDrawerSort(
            LauncherLayoutEdits.ReorderDrawerApplication(layout, category.Id, appIds[1], -1), LauncherDrawerSort.CategoryOrder));
        var reopened = await f.Store.GetAsync();
        Assert.Equal(new[] { appIds[1], appIds[0] }, reopened.Current.Drawer!.Categories[0].Applications);
        Assert.Equal(category.Id, reopened.Current.Drawer.Categories[0].Id);
        var backup = LauncherLayoutExchange.Import(LauncherLayoutExchange.Export(reopened), reopened.AuthorityId);
        Assert.Equal(reopened.Current.Drawer.Categories[0].Applications, backup.Drawer!.Categories[0].Applications);
        var clone = LauncherLayoutEdits.Clone(reopened.Current);
        ((Guid[])clone.Drawer!.Categories[0].Applications)[0] = Guid.Empty;
        Assert.Equal(appIds[1], reopened.Current.Drawer.Categories[0].Applications[0]);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Store.EditAsync(reopened,
            layout => LauncherLayoutEdits.SetDrawerCategoryMembership(layout, category.Id, Guid.NewGuid(), true)));
        await Assert.ThrowsAsync<IOException>(() => f.Store.EditAsync(populated,
            layout => LauncherLayoutEdits.RemoveDrawerCategory(layout, category.Id)));
        f.Provider.IncludeApps = false; await f.Registry.RefreshAsync(default);
        var renamed = await f.Store.EditAsync(ordered, layout => LauncherLayoutEdits.RenameDrawerCategory(layout, category.Id, "College"));
        Assert.Equal(appIds[1], renamed.Current.Drawer!.Categories[0].Applications[0]);
        var removed = await f.Store.EditAsync(renamed, layout => LauncherLayoutEdits.RemoveDrawerCategory(layout, category.Id));
        Assert.Empty(removed.Current.Drawer!.Categories);
        Assert.Equal(initial.Current.ActivePage.Items, removed.Current.ActivePage.Items);
    }

    [Fact]
    public async Task SchemaFourDrawerUpgradeIsReadOnlyUntilExplicitEdit()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var record = (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordType == HomeLauncherLayoutStore.RecordType);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(record.Payload.GetRawText())!;
        payload["Current"]!["SchemaVersion"] = 4; payload["Current"]!.AsObject().Remove("Drawer");
        var old = record with { Revision = record.Revision + 1, Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload) };
        Assert.True((await f.Home.WriteAsync(old, record.Revision)).IsSuccess);
        var read = await f.Store.GetAsync(); Assert.Null(read.Current.Drawer);
        Assert.Equal(LauncherLayout.CurrentSchema, read.Current.SchemaVersion);
        Assert.Equal(4, (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordId == record.RecordId).Payload.GetProperty("Current").GetProperty("SchemaVersion").GetInt32());
        var saved = await f.Store.EditAsync(read, layout => LauncherLayoutEdits.AddDrawerCategory(layout, "Work"));
        Assert.Equal(old.Revision + 1, saved.Revision); Assert.Single(saved.Current.Drawer!.Categories);
    }

    [Fact]
    public async Task GestureBindingsPersistAndRejectStaleUnknownOrCrossProfileWrites()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var configured = new LauncherGestures(SwipeDown: LauncherCommand.OpenPageManager, DoubleTap: LauncherCommand.OpenSettings);
        var saved = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.SetGestures(layout, configured));
        var reopened = await f.Store.GetAsync(); Assert.Equal(configured, reopened.Current.Gestures);
        Assert.Equal(initial.Current.ActivePage.Items, reopened.Current.ActivePage.Items);
        var backup = LauncherLayoutExchange.Import(LauncherLayoutExchange.Export(reopened), reopened.AuthorityId);
        Assert.Equal(configured, backup.Gestures);
        await Assert.ThrowsAsync<IOException>(() => f.Store.EditAsync(initial, layout => LauncherLayoutEdits.SetGestures(layout, new())));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.EditAsync(saved,
            layout => LauncherLayoutEdits.SetGestures(layout, configured with { SwipeUp = (LauncherCommand)999 })));
        Assert.Equal(saved.Revision, (await f.Store.GetAsync()).Revision);
        Assert.Throws<UnauthorizedAccessException>(() => LauncherLayoutExchange.Import(LauncherLayoutExchange.Export(saved), "other-profile"));
    }

    [Fact]
    public async Task SchemaFiveGestureUpgradeReadsWithoutWritingAndKeepsPrevious()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var record = (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordType == HomeLauncherLayoutStore.RecordType);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(record.Payload.GetRawText())!;
        payload["Current"]!["SchemaVersion"] = 5; payload["Current"]!.AsObject().Remove("Gestures");
        var old = record with { Revision = record.Revision + 1, Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload) };
        Assert.True((await f.Home.WriteAsync(old, record.Revision)).IsSuccess);
        var read = await f.Store.GetAsync(); Assert.Null(read.Current.Gestures);
        Assert.Equal(old.Payload.GetRawText(), (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordId == record.RecordId).Payload.GetRawText());
        var saved = await f.Store.EditAsync(read, layout => LauncherLayoutEdits.SetGestures(layout, new()));
        Assert.Equal(old.Revision + 1, saved.Revision); Assert.Equal(read.Current.ActivePageId, saved.Previous!.ActivePageId);
    }

    [Fact]
    public void BackgroundGestureInputRejectsCancellationMultitouchAndDraggedTaps()
    {
        var input = new LauncherGestureRecognizer(80, 12);
        input.Down(100, 100, 0, 1); Assert.Equal(LauncherGesture.SwipeUp, input.Up(100, 0, 100, 1));
        input.Down(100, 100, 200, 1); Assert.Equal(LauncherGesture.SwipeDown, input.Up(100, 200, 300, 1));
        input.Down(100, 100, 400, 1); Assert.Equal(LauncherGesture.SwipeLeft, input.Up(0, 120, 500, 1));
        input.Down(100, 100, 600, 1); Assert.Equal(LauncherGesture.SwipeRight, input.Up(200, 100, 700, 1));
        input.Down(100, 100, 800, 1); input.Cancel(); Assert.Null(input.Up(200, 100, 900, 1));
        input.Down(100, 100, 1000, 1); input.Move(100, 100, 2); Assert.Null(input.Up(200, 100, 1100, 1));
        input.Down(100, 100, 1200, 1); Assert.Null(input.Up(100, 0, 2300, 1));
        input.Down(100, 100, 2400, 1); Assert.Null(input.Up(100, 100, 2450, 1));
        input.Down(101, 101, 2500, 1); Assert.Equal(LauncherGesture.DoubleTap, input.Up(101, 101, 2550, 1));
        input.Down(100, 100, 2600, 1); input.Move(130, 100, 1); Assert.Null(input.Up(100, 100, 2650, 1));
        input.Down(100, 100, 2700, 1); Assert.Null(input.Up(100, 100, 2750, 1));
        input.Cancel(); input.Down(100, 100, 2800, 1); Assert.Null(input.Up(100, 100, 2850, 1));
        input.Down(float.NaN, 100, 2900, 1); Assert.Null(input.Up(100, 100, 2950, 1));
    }

    [Fact]
    public async Task SoleLauncherActivityRenamePreservesFolderDockCategoryAndPlacementIdentity()
    {
        using var f = new Fixture(); f.Provider.StableIdentity = "sole-launcher";
        var initial = await f.Store.GetAsync();
        var beforeApp = (await f.Registry.RefreshAsync(default)).Single(a => a.OsApplicationId == "first");
        var placement = initial.Current.ActivePage.Items.Single(i => i.ApplicationId == beforeApp.ApplicationId);
        var arranged = await f.Store.EditAsync(initial, layout =>
        {
            layout = LauncherLayoutEdits.CreateFolder(layout, layout.ActivePageId, "Work");
            layout = LauncherLayoutEdits.MoveToContainer(layout, placement.Id, layout.Folders.Single().Id);
            layout = LauncherLayoutEdits.AddDockApplication(LauncherLayoutEdits.ConfigureDock(layout, 1, 4), beforeApp.ApplicationId);
            layout = LauncherLayoutEdits.AddDrawerCategory(layout, "Study");
            return LauncherLayoutEdits.SetDrawerCategoryMembership(layout, layout.Drawer!.Categories.Single().Id, beforeApp.ApplicationId, true);
        });
        var before = LauncherLayoutExchange.Export(arranged);
        f.Provider.FirstEntrypoint = "first/renamed-main"; f.Provider.Version = "2";
        var afterApp = (await f.Registry.RefreshAsync(default)).Single(a => a.OsApplicationId == "first");
        Assert.Equal(beforeApp.ApplicationId, afterApp.ApplicationId); Assert.True(afterApp.Revision > beforeApp.Revision);
        Assert.Null(await f.Registry.ResolveLaunchAsync(beforeApp.ApplicationId, beforeApp.Revision, default));
        Assert.Equal("first/renamed-main", (await f.Registry.ResolveLaunchAsync(afterApp.ApplicationId, afterApp.Revision, default))!.Entrypoint);
        var reopened = await f.Store.GetAsync();
        Assert.Equal(arranged.Revision, reopened.Revision); Assert.Equal(before, LauncherLayoutExchange.Export(reopened));
        Assert.Equal(placement.Id, reopened.Current.Folders.Single().Items.Single().Id);
    }

    [Fact]
    public async Task WidgetGeometryPersistsReflowsAndRejectsCollisionsWithoutLosingIdentities()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var android = new LauncherAndroidWidgetReference("com.example/.Widget", "42");
        var added = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddWidget(layout, layout.ActivePageId, "Calendar", 2, 2, android: android));
        var widget = Assert.Single(added.Current.Widgets);
        var app = added.Current.ActivePage.Items[0];
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.EditAsync(added,
            layout => LauncherLayoutEdits.MoveWidget(layout, widget.Id, widget.PageId, app.Column, app.Row, 2, 2)));
        Assert.Equal(added.Revision, (await f.Store.GetAsync()).Revision);
        var second = await f.Store.EditAsync(added, layout => LauncherLayoutEdits.AddPage(layout, "Widgets"));
        var moved = await f.Store.EditAsync(second, layout => LauncherLayoutEdits.MoveWidget(layout, widget.Id, layout.ActivePageId, 0, 0, 3, 2));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.EditAsync(moved, layout => LauncherLayoutEdits.RemovePage(layout, layout.ActivePageId)));
        var reflowed = await f.Store.EditAsync(moved, layout => LauncherLayoutEdits.Reflow(layout, 3, 3));
        var reopened = await f.Store.GetAsync(); reopened.Current.Validate();
        Assert.Equal(widget.Id, Assert.Single(reopened.Current.Widgets).Id);
        Assert.Equal(android, reopened.Current.Widgets[0].Android);
        Assert.Equal(initial.Current.ActivePage.Items.Select(item => item.Id).Order(), LauncherLayoutEdits.Placements(reopened.Current).Select(item => item.Id).Order());
        Assert.Equal(reopened.Current.Widgets, LauncherLayoutExchange.Import(LauncherLayoutExchange.Export(reopened), reopened.AuthorityId).Widgets);
        var clone = LauncherLayoutEdits.Clone(reopened.Current);
        ((LauncherWidgetPlacement[])clone.Widgets)[0] = clone.Widgets[0] with { Label = "Changed copy" };
        Assert.Equal("Calendar", reopened.Current.Widgets[0].Label);
        await Assert.ThrowsAsync<IOException>(() => f.Store.EditAsync(moved, layout => LauncherLayoutEdits.RemoveWidget(layout, widget.Id)));
        var removed = await f.Store.EditAsync(reflowed, layout => LauncherLayoutEdits.RemoveWidget(layout, widget.Id));
        Assert.Empty(removed.Current.Widgets); Assert.Single(removed.Previous!.Widgets);
    }

    [Fact]
    public async Task SchemaSixWidgetUpgradeReadsWithoutWritingAndRetainsGestureBindings()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var configured = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.SetGestures(layout, new(DoubleTap: LauncherCommand.OpenSettings)));
        var record = (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordType == HomeLauncherLayoutStore.RecordType);
        var payload = System.Text.Json.Nodes.JsonNode.Parse(record.Payload.GetRawText())!;
        payload["Current"]!["SchemaVersion"] = 6; payload["Current"]!.AsObject().Remove("Widgets");
        var old = record with { Revision = record.Revision + 1, Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload) };
        Assert.True((await f.Home.WriteAsync(old, record.Revision)).IsSuccess);
        var read = await f.Store.GetAsync(); Assert.Empty(read.Current.Widgets);
        Assert.Equal(configured.Current.Gestures, read.Current.Gestures);
        Assert.Equal(old.Payload.GetRawText(), (await f.Home.ReadAsync()).State!.Records.Single(r => r.RecordId == record.RecordId).Payload.GetRawText());
    }

    [Fact]
    public async Task WidgetSessionUsesCurrentStoredPlacementAndNeverTurnsLocatorIntoAuthority()
    {
        using var f = new Fixture();
        var verifier = new WidgetTestPeer();
        var registry = new HomeNativeWidgetRegistry(verifier, f.Actors, new ResourceAuthorizationService(f.Actors, []));
        var session = new HomeLauncherSession(f.Store, f.Actors, registry);
        Assert.Null(await session.ReadAsync());
        var initial = await f.Store.GetAsync();
        var reference = new HomeNativeWidgetReference(verifier.Owner.AppId, verifier.Owner.InstalledApplicationId,
            verifier.Owner.InstallationRevision, "clock", "1");
        var placed = await f.Store.EditAsync(initial, layout => LauncherLayoutEdits.AddWidget(layout, layout.ActivePageId, "Clock", 2, 1, native: reference));
        var widget = Assert.Single(placed.Current.Widgets);
        var snapshot = (await session.ReadAsync())!;
        Assert.Null((await session.ReadWidgetAsync(snapshot, widget.Id))!.NativeDefinition);
        using var registration = await registry.RegisterAsync(new(42, "test-os-principal"),
            [new("clock", "1", "Clock", new(1, 1), new(2, 1), new(4, 2), "config.clock", "surface.clock", HomeNativeWidgetUpdateMode.Event, null, [], [])]);
        Assert.NotNull(registration);
        Assert.NotNull((await session.ReadWidgetAsync(snapshot, widget.Id))!.NativeDefinition);
        ((LauncherWidgetPlacement[])snapshot.Layout.Current.Widgets)[0] = widget with { Native = reference with { WidgetId = "forged" } };
        Assert.Equal(reference, (await session.ReadWidgetAsync(snapshot, widget.Id))!.Placement.Native);
        verifier.Available = false;
        Assert.Null((await session.ReadWidgetAsync(snapshot, widget.Id))!.NativeDefinition);
        Assert.Single((await f.Store.GetAsync()).Current.Widgets);
        f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "new-session" };
        Assert.False(await session.IsCurrentAsync(snapshot)); Assert.Null(await session.ReadWidgetAsync(snapshot, widget.Id));
        var current = (await session.ReadAsync())!;
        await f.Store.EditAsync(current.Layout, layout => LauncherLayoutEdits.RenamePage(layout, layout.ActivePageId, "Updated"));
        Assert.False(await session.IsCurrentAsync(current));
        var next = (await session.ReadAsync())!;
        f.Actors.Current = f.Actors.Current with { ProfileId = "foreign-profile" };
        Assert.Null(await session.ReadWidgetAsync(next, widget.Id));
        Assert.Single((await f.Home.ReadAsync()).State!.Records, record => record.RecordType == HomeLauncherLayoutStore.RecordType);
    }

    [Fact]
    public async Task SessionEditKeepsOriginalActorAcrossDraftAndRejectsReauthenticatedCaller()
    {
        using var f = new Fixture(); var initial = await f.Store.GetAsync();
        var owner = new HomeLauncherSession(f.Store, f.Actors, new HomeNativeWidgetRegistry(new WidgetTestPeer(), f.Actors,
            new ResourceAuthorizationService(f.Actors, [])));
        var snapshot = (await owner.ReadAsync())!;
        var original = await File.ReadAllTextAsync(f.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.EditAsync(snapshot, layout =>
        {
            f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "changed-during-draft" };
            return LauncherLayoutEdits.RenamePage(layout, layout.ActivePageId, "Must not write");
        }));
        Assert.Equal(original, await File.ReadAllTextAsync(f.StatePath));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.EditAsync(snapshot,
            layout => LauncherLayoutEdits.AddPage(layout, "Old session")));
        Assert.Equal(original, await File.ReadAllTextAsync(f.StatePath));
        var fresh = (await owner.ReadAsync())!;
        Assert.Equal(initial.Revision + 1, (await owner.EditAsync(fresh,
            layout => LauncherLayoutEdits.RenamePage(layout, layout.ActivePageId, "Current session"))).Revision);
    }

    private sealed class WidgetTestPeer : IHomeNativeInstalledPeerVerifier
    {
        public bool Available = true;
        public HomeNativeInstalledPeer Owner { get; } = new("test.widget.owner", Guid.NewGuid(), "install-1", "test-executable",
            new HashSet<string>(StringComparer.Ordinal) { HomeNativeWidgetRegistry.ServiceId });
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observedPeer, CancellationToken ct)
            => ValueTask.FromResult<HomeNativeInstalledPeer?>(Available ? Owner : null);
    }

    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expectedActor);
    }
    private sealed class Provider : IInstalledApplicationObservationProvider
    {
        public string FirstEntrypoint = "first/main"; public string? StableIdentity;
        public string ProviderId => "android-test"; public string Version = "1"; public string FirstLabel = "First app"; public bool IncludeApps = true;
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>(
            [new("personal", "Personal", false, true, IncludeApps ? [new("first", FirstEntrypoint, FirstLabel, Version, true) { StableLaunchIdentity = StableIdentity }, new("second", "second/main", "Second app", Version, true)] : [])]);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-launcher-layout-" + Guid.NewGuid().ToString("N"));
        public string StatePath => Path.Combine(_root, "home.json");
        public FileHomeCoreStateStore Home { get; } public Actors Actors { get; } = new(); public Provider Provider { get; } = new();
        public HomeInstalledApplicationRegistry Registry { get; } public HomeLauncherLayoutStore Store { get; }
        public Fixture()
        {
            Home = new(Path.Combine(_root, "home.json")); Registry = new(Home, Actors, [Provider]);
            Store = new(Home, Actors, new ResourceAuthorizationService(Actors, [new LauncherLayoutResourceResolver(Home)]), Registry);
        }
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
}
