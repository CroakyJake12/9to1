using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

public sealed class DesktopPagesTests
{
    [Fact]
    public void PagesHaveIndependentIdentityAndScopedSurfacePreservesCanonicalReferences()
    {
        var config = ShellConfiguration.Default(); var app = Guid.NewGuid();
        config = DesktopPageEdits.PinApplication(config, app, "Canonical app");
        var global = DesktopPageEdits.Effective(config); var shortcut = Assert.Single(global.ActivePage.Items);
        var specific = DesktopPageEdits.SetSpaceSpecific(config, true);
        var local = DesktopPageEdits.Effective(specific);
        Assert.NotEqual(global.Id, local.Id); Assert.NotEqual(shortcut.Id, Assert.Single(local.ActivePage.Items).Id);
        Assert.Same(shortcut.Target, local.ActivePage.Items[0].Target);
        specific = DesktopPageEdits.AddPage(specific, "Work");
        Assert.Equal(2, DesktopPageEdits.Effective(specific).Pages.Count); Assert.Single(specific.GlobalDesktopSurface!.Pages);
        specific = DesktopPageEdits.ReorderPage(specific, -1);
        Assert.Equal("Work", DesktopPageEdits.Effective(specific).Pages[0].Name);
        specific = DesktopPageEdits.StepPage(specific, -1);
        Assert.Equal("Work", DesktopPageEdits.Effective(specific).ActivePage.Name);
        var duplicated = ShellEdits.DuplicateSpace(specific, "Copy"); duplicated.Validate();
        var duplicateItems = DesktopPageEdits.Effective(duplicated).Pages.SelectMany(p => p.Items).ToArray();
        Assert.Equal(shortcut.Target, Assert.Single(duplicateItems).Target);
        var restoredGlobal = DesktopPageEdits.SetSpaceSpecific(duplicated, false);
        Assert.Equal(global.Id, DesktopPageEdits.Effective(restoredGlobal).Id);
    }
    [Fact]
    public void GridRejectsOverlapAndBoundsRatherThanSilentlyHidingItems()
    {
        var config = DesktopPageEdits.PinApplication(ShellConfiguration.Default(), Guid.NewGuid(), "A");
        config = DesktopPageEdits.PinApplication(config, Guid.NewGuid(), "B");
        var surface = DesktopPageEdits.Effective(config); var first = surface.ActivePage.Items[0]; var second = surface.ActivePage.Items[1];
        Assert.Equal((0, 0), (first.Column, first.Row)); Assert.Equal((1, 0), (second.Column, second.Row));
        var badPage = surface.ActivePage with { Items = [first, second with { Column = first.Column, Row = first.Row }] };
        Assert.Throws<InvalidDataException>(() => (config with { GlobalDesktopSurface = surface with { Pages = [badPage] } }).Validate());
        badPage = surface.ActivePage with { Items = [first with { ColumnSpan = int.MaxValue }] };
        Assert.Throws<InvalidDataException>(() => (config with { GlobalDesktopSurface = surface with { Pages = [badPage] } }).Validate());
        Assert.Throws<InvalidOperationException>(() => DesktopPageEdits.RemovePage(config));
    }
    [Fact]
    public void MoveResizeAndGridChangesPreserveCanonicalIdentityAndRejectCollisionsOrHiddenItems()
    {
        var config = DesktopPageEdits.PinApplication(ShellConfiguration.Default(), Guid.NewGuid(), "A");
        config = DesktopPageEdits.PinApplication(config, Guid.NewGuid(), "B");
        var original = DesktopPageEdits.Effective(config).ActivePage.Items[0];
        var moved = DesktopPageEdits.ArrangeItem(config, original.Id, 2, 1, 2, 2);
        var item = DesktopPageEdits.Effective(moved).ActivePage.Items.Single(i => i.Id == original.Id);
        Assert.Same(original.Target, item.Target); Assert.Equal(original.Id, item.Id);
        Assert.Equal((2, 1, 2, 2), (item.Column, item.Row, item.ColumnSpan, item.RowSpan));
        Assert.Throws<InvalidDataException>(() => DesktopPageEdits.ArrangeItem(config, original.Id, 1, 0, 1, 1));
        Assert.Throws<InvalidDataException>(() => DesktopPageEdits.ArrangeItem(config, original.Id, 0, 0, 0, 1));
        Assert.Throws<InvalidDataException>(() => DesktopPageEdits.ArrangeItem(config, original.Id, 0, 0, int.MaxValue, 1));
        Assert.Throws<InvalidDataException>(() => DesktopPageEdits.GridSize(moved, 3, 2));
        Assert.Throws<InvalidOperationException>(() => DesktopPageEdits.ArrangeItem(config, Guid.NewGuid(), 0, 0, 1, 1));
        Assert.Equal((0, 0, 1, 1), (original.Column, original.Row, original.ColumnSpan, original.RowSpan));
    }
    [Fact]
    public async Task LegacyMigrationIsDeterministicReadOnlyUntilKeepAndPersistsPagesThroughHome()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-pages-" + Guid.NewGuid());
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(directory, "home.json")); var actors = new Actors();
            var store = new HomeShellConfigurationStore(home, actors, new ResourceAuthorizationService(actors, [new ShellConfigurationResourceResolver(home)]));
            var initial = await store.ReadAsync(default);
            var legacy = initial.Current with { SchemaVersion = 1, GlobalDesktopSurface = null };
            var record = (await home.ReadAsync()).State!.Records.Single();
            Assert.True((await home.WriteAsync(record with { Revision = 2, Payload = JsonSerializer.SerializeToElement(new { ProfileId = "profile", Current = legacy, Previous = (ShellConfiguration?)null }) }, 1)).IsSuccess);
            var firstRead = await store.ReadAsync(default); var secondRead = await store.ReadAsync(default);
            Assert.Equal(2, firstRead.Current.SchemaVersion); Assert.Equal(firstRead.Current.GlobalDesktopSurface!.Id, secondRead.Current.GlobalDesktopSurface!.Id);
            Assert.Equal(1, (await home.ReadAsync()).State!.Records.Single().Payload.GetProperty("Current").GetProperty("SchemaVersion").GetInt32());
            var editor = new ShellConfigurationService(store); var snapshot = await editor.GetAsync();
            var preview = await editor.PreviewAsync(snapshot.Stored, DesktopPageEdits.AddPage(snapshot.Effective, "Second"), TimeSpan.FromSeconds(30));
            Assert.Equal(2, (await home.ReadAsync()).State!.Records.Single().Revision);
            await editor.KeepAsync(preview.Preview!.Id);
            var reopened = await new ShellConfigurationService(store).GetAsync();
            Assert.Equal(3, reopened.Stored.Revision); Assert.Equal(2, DesktopPageEdits.Effective(reopened.Effective).Pages.Count);
            Assert.Single(reopened.Stored.Previous!.GlobalDesktopSurface!.Pages);
            var pinned = DesktopPageEdits.PinApplication(reopened.Effective, Guid.NewGuid(), "Movable");
            var pinnedItem = Assert.Single(DesktopPageEdits.Effective(pinned).ActivePage.Items);
            var arranged = DesktopPageEdits.ArrangeItem(pinned, pinnedItem.Id, 2, 1, 2, 2);
            var placementPreview = await editor.PreviewAsync(reopened.Stored, arranged, TimeSpan.FromSeconds(30));
            Assert.Empty(DesktopPageEdits.Effective((await store.ReadAsync(default)).Current).ActivePage.Items);
            await editor.KeepAsync(placementPreview.Preview!.Id);
            var durableItem = Assert.Single(DesktopPageEdits.Effective((await new ShellConfigurationService(store).GetAsync()).Effective).ActivePage.Items);
            Assert.Equal(pinnedItem.Id, durableItem.Id); Assert.Equal(pinnedItem.Target, durableItem.Target);
            Assert.Equal((2, 1, 2, 2), (durableItem.Column, durableItem.Row, durableItem.ColumnSpan, durableItem.RowSpan));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        private static readonly AuthenticatedResourceActor Current = new("actor", "profile", null, null, "1");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState lockedState, AuthenticatedResourceActor expectedActor, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expectedActor);
    }
}
