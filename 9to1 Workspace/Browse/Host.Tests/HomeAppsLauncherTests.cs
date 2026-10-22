using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Home;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Rendered proof for the authored Home app launcher in <c>UnifiedHomeApps.cui</c>. It loads the
/// actual embedded document through the production CUI loader and the real
/// <see cref="HomeAppsCuiBindings"/> projection, so an invalid element, an unregistered property, an
/// unresolvable Image source or a broken binding fails here instead of appearing as a blank region,
/// a stale page under an "Apps" heading, or an "empty catalogue" in the running product.
/// </summary>
public sealed class HomeAppsLauncherTests
{
    private static HomeAppsViewState State(string query = "", string status = "Select an app to open it in this tab.")
    {
        // Registered catalogue shape: real identity, real description and the real IconKey stored on
        // the mode record, including one key the vector catalog does not carry.
        var all = new[]
        {
            new HomeAppItem("files", "Files", "Private browser workspace", "folder"),
            new HomeAppItem("automations", "Automations", "Reusable, scheduled, recurring and triggered workflows", "not-a-catalog-key"),
            new HomeAppItem("boards", "Boards", "Context workspace", "boards")
        };
        return new HomeAppsViewState(all, HomeAppsViewState.Filter(all, query), query, status);
    }

    private static (Control Root, CuiControlLoader Loader) Render(HomeAppsCuiBindings bindings, out IReadOnlyList<CuiDiagnostic> diagnostics)
    {
        var registry = HomeAppsCuiDocument.CreateControlRegistry();
        var loader = new CuiControlLoader(registry);
        loader.SetBindingContext(bindings);
        loader.SetActionDispatcher(bindings);
        var document = HomeAppsCuiDocument.Parse();
        var loaded = loader.TryLoad(document);
        diagnostics = loaded.Diagnostics;
        var root = loaded.Root ?? throw new InvalidDataException("The Home apps document produced no surface: " +
            string.Join("; ", loaded.Diagnostics.Select(item => item.Code + " " + item.Message)));
        loader.WireBindings(root);
        // A real window with a real viewport: the results row is a scrolling 1fr row, and a
        // zero-height test surface would leave its virtualized item containers unrealized.
        var window = new Window { Content = root, Width = 1280, Height = 900 };
        window.Show();
        window.UpdateLayout();
        root.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (root, loader);
    }

    /// <summary>Pins the recorded cause: the authored launcher uses canonical Image sources, and a
    /// loader without the bounded icon resolver rejects them. This is what made the Apps route fail
    /// publication before the catalogue was ever read; it is not an empty catalogue.</summary>
    [AvaloniaFact]
    public void Authored_launcher_requires_a_registered_icon_resolver_on_the_loading_host()
    {
        var bindings = new HomeAppsCuiBindings(() => State(), (_, _, _) => ValueTask.CompletedTask);
        using var loader = new CuiControlLoader();
        loader.SetBindingContext(bindings);
        loader.SetActionDispatcher(bindings);
        var document = HomeAppsCuiDocument.Parse();
        var (_, diagnostics) = loader.TryLoad(document);
        var errors = diagnostics.Where(item => item.Severity == CuiDiagnosticSeverity.Error).ToArray();
        Assert.Contains(errors, item => item.Code == "CUIR053" && item.Message.Contains("icon:close", StringComparison.Ordinal));
    }

    /// <summary>The production registry renders the exact resource with zero loader errors while the
    /// catalogue is still empty, so publication cannot fail before the mode fetch runs.</summary>
    [AvaloniaFact]
    public void Production_registry_renders_the_empty_launcher_without_any_loader_error()
    {
        var bindings = new HomeAppsCuiBindings(() => HomeAppsViewState.Empty, (_, _, _) => ValueTask.CompletedTask);
        var (root, loader) = Render(bindings, out var diagnostics);
        using (loader)
        {
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            Assert.Contains(root.GetVisualDescendants().OfType<TextBlock>(), text => text.Name == "home-apps-empty-title");
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "home-app-tile");
            // The honest empty state, not a blank region and not a silently removed launcher.
            Assert.Contains(root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text),
                text => text != null && text.Contains("No apps are available", StringComparison.Ordinal));
        }
    }

    [AvaloniaFact]
    public async Task Authored_launcher_renders_icon_tiles_that_open_their_own_app_in_one_action()
    {
        var state = HomeAppsViewState.Empty;
        var actions = new List<(string Command, object? Parameter)>();
        HomeAppsCuiBindings bindings = null!;
        bindings = new HomeAppsCuiBindings(() => state, (command, parameter, _) =>
        {
            actions.Add((command, parameter));
            if (command == "home.set-app-query") state = State((parameter as string ?? string.Empty).Trim());
            bindings.Refresh();
            return ValueTask.CompletedTask;
        });
        var (root, loader) = Render(bindings, out var diagnostics);
        using (loader)
        {
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            // Production ordering: the authored surface is published before the mode catalogue is
            // read, then the fetched catalogue is projected through the launcher's own action path.
            await bindings.DispatchAsync("home.set-app-query", string.Empty, CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();

            // The search field is an authored canonical Input; no legacy list control is substituted.
            Assert.Contains(root.GetVisualDescendants().OfType<TextBox>(), input => input.Name == "home-app-search");
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<ListBox>(), _ => true);

            var tiles = root.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-app-tile").ToArray();
            Assert.Equal(3, tiles.Length);
            Assert.Equal(new[] { "Open Files", "Open Automations", "Open Boards" }, tiles.Select(AutomationProperties.GetName));
            // Every tile carries a real resolved vector icon, including the unrecognised catalogue key.
            Assert.All(tiles, tile => Assert.IsType<DrawingImage>(Assert.Single(tile.GetVisualDescendants().OfType<Image>()).Source));
            Assert.All(tiles, tile => Assert.False(string.IsNullOrWhiteSpace(
                Assert.Single(tile.GetVisualDescendants().OfType<TextBlock>()).Text)));
            Assert.Equal("Private browser workspace", ToolTip.GetTip(tiles[0]));

            // One action per tile opens that app directly; the old separate commit control is gone.
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "home-open-app");
            tiles[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            // Exactly one activation for the clicked tile, with the exact registered identity. Other
            // commands (query/clear) are legitimate and are not counted here.
            Assert.Equal([("home.open-app", (object?)"automations")],
                actions.Where(action => action.Command == "home.open-app").ToArray());
        }
    }

    [AvaloniaFact]
    public async Task Launcher_search_narrows_the_tiles_states_no_match_and_clear_restores_them()
    {
        var state = State();
        var actions = new List<(string Command, object? Parameter)>();
        HomeAppsCuiBindings bindings = null!;
        bindings = new HomeAppsCuiBindings(() => state, (command, parameter, _) =>
        {
            actions.Add((command, parameter));
            if (command == "home.set-app-query") state = State((parameter as string ?? string.Empty).Trim());
            if (command == "home.clear-app-query") state = State(string.Empty);
            bindings.Refresh();
            return ValueTask.CompletedTask;
        });
        var (root, loader) = Render(bindings, out var diagnostics);
        using (loader)
        {
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);

            await bindings.DispatchAsync("home.set-app-query", "boards", CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            var tiles = root.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-app-tile").ToArray();
            Assert.Equal(["Open Boards"], tiles.Select(AutomationProperties.GetName));

            await bindings.DispatchAsync("home.set-app-query", "nothing-installed-like-this", CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "home-app-tile");
            var texts = string.Join(" | ", root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            Assert.Contains("No app matches", texts);
            Assert.Contains("Clear the search", texts);

            await bindings.DispatchAsync("home.clear-app-query", null, CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, root.GetVisualDescendants().OfType<Button>().Count(button => button.Name == "home-app-tile"));
            Assert.Contains(actions, action => action.Command == "home.clear-app-query");
        }
    }

    /// <summary>Real canonical catalogue shape used by the Create-group tests. Picture (imagine,
    /// GUID …0008) and Motion (motion, GUID …0025) carry their stored canonical machine and built-in
    /// source; Files is an ordinary tile.</summary>
    private static IReadOnlyList<HomeAppItem> RealCatalogue()
    {
        return new[]
        {
            new HomeAppItem("files", "Files", "Private browser workspace", "folder"),
            new HomeAppItem("imagine", "Picture", "Create and refine image concepts with local models", "palette",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000008"), Source: ModeSource.BuiltIn),
            new HomeAppItem("motion", "Motion", "Open the Motion workspace shell", "play",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000025"), Source: ModeSource.BuiltIn),
            new HomeAppItem("boards", "Boards", "Context workspace", "boards")
        };
    }

    private static HomeAppsViewState GroupState(string query = "")
    {
        var all = RealCatalogue();
        var launcher = HomeCreateGroup.BuildLauncher(all);
        var group = HomeCreateGroup.Build(all);
        var groupVisible = HomeCreateGroup.ResolveGroupVisible(group, query);
        return new HomeAppsViewState(
            all,
            HomeCreateGroup.FilterLauncher(launcher, group, query),
            query,
            "Select an app to open it in this tab.",
            launcher,
            group,
            groupVisible);
    }

    /// <summary>Projection contract for the Create group: membership is resolved by REAL canonical
    /// built-in identity (GUID + source), the group contributes no app count and never duplicates
    /// child records, and a custom app whose key/name merely resembles a member is never captured.
    /// Wave is a referenced identity with no native ModeDefinition/GUID and is surfaced unavailable.</summary>
    [Fact]
    public void Create_group_projection_uses_real_identity_and_never_captures_lookalike_custom_apps()
    {
        var realPicture = Guid.Parse("a0000000-0000-0000-0000-000000000008");
        var realMotion = Guid.Parse("a0000000-0000-0000-0000-000000000025");
        var catalogue = new[]
        {
            new HomeAppItem("imagine", "Picture", "Create and refine image concepts", "palette",
                CanonicalId: realPicture, Source: ModeSource.BuiltIn),
            new HomeAppItem("motion", "Motion", "Open the Motion workspace shell", "play",
                CanonicalId: realMotion, Source: ModeSource.BuiltIn),
            // Custom lookalikes: same visible name/key but a distinct user GUID and a non-built-in
            // source. These are independent custom apps and MUST stay at top level.
            new HomeAppItem("imagine", "Picture (custom)", "Custom lookalike that must not be captured", "palette",
                CanonicalId: Guid.NewGuid(), Source: ModeSource.Created),
            new HomeAppItem("motion", "Motion (custom)", "Another custom lookalike", "play",
                CanonicalId: Guid.NewGuid(), Source: ModeSource.Created),
            new HomeAppItem("files", "Files", "Private browser workspace", "folder")
        };

        var launcher = HomeCreateGroup.BuildLauncher(catalogue);
        // Only the REAL canonical built-in members move under Create; a custom Created lookalike that
        // merely shares the key/name is never treated as a canonical member and stays at top level.
        Assert.DoesNotContain(launcher, item => !item.IsGroup && HomeCreateGroup.IsCanonicalMember(item));
        Assert.Contains(launcher, item => !item.IsGroup && item.Key == "imagine" && item.Source == ModeSource.Created);
        Assert.Contains(launcher, item => !item.IsGroup && item.Key == "motion" && item.Source == ModeSource.Created);
        // Exactly one group-navigation tile appears, keyed "create" and flagged as a group.
        var create = Assert.Single(launcher, item => item.IsGroup);
        Assert.Equal("create", create.Key);
        // Projected app count excludes the navigation group and does NOT duplicate child records:
        // the launcher keeps exactly the three non-member records (both customs stay top-level, plus
        // Files) and the single group tile. No canonical child record is duplicated as a tile.
        Assert.Equal(catalogue.Length - 2, launcher.Count(item => !item.IsGroup));
        Assert.Equal(1, launcher.Count(item => item.IsGroup));

        var group = HomeCreateGroup.Build(catalogue);
        Assert.True(group.HasMembers);

        var picture = Assert.Single(group.Members, member => member.CanonicalId == realPicture);
        Assert.True(picture.IsInstalled);
        Assert.Equal("Picture", picture.Name);
        Assert.Equal(ModeSource.BuiltIn, picture.Source);

        var motion = Assert.Single(group.Members, member => member.CanonicalId == realMotion);
        Assert.True(motion.IsInstalled);
        Assert.Equal("Motion", motion.Name);
        Assert.Equal(ModeSource.BuiltIn, motion.Source);

        // The custom lookalike identities (distinct user GUID, non-built-in source) are never captured.
        Assert.DoesNotContain(group.Members, member => member.Name.Contains("custom", StringComparison.OrdinalIgnoreCase));

        // Wave is an honest, non-launchable reference: audio creation with no native ModeDefinition/
        // GUID in this composition, so its canonical identity is null rather than a fabricated GUID.
        var wave = Assert.Single(group.Members, member => member.Key == "wave");
        Assert.False(wave.IsInstalled);
        Assert.Null(wave.CanonicalId);
        Assert.Contains("audio", wave.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not installed", wave.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Hypothetical FUTURE fixture: no native Wave ModeDefinition/GUID or factory exists in this
    /// composition, so there is no production Wave identity to claim. This test fabricates a TEST-ONLY
    /// generated ID to exercise the identity contract, proving that if such a record were to appear,
    /// projection would propagate its provided stable ID and built-in source/key once inside Create and
    /// never duplicate a custom Wave. It does not mint, claim, or assert any production Wave identity or
    /// engine readiness.</summary>
    [Fact]
    public void Create_group_projection_propagates_a_hypothetical_future_builtin_wave_ID()
    {
        var realPicture = Guid.Parse("a0000000-0000-0000-0000-000000000008");
        // Test-only IDs for the hypothetical future fixture; NOT production Wave identities.
        var futureWaveId = Guid.NewGuid();
        var catalogue = new[]
        {
            new HomeAppItem("imagine", "Picture", "Create and refine image concepts", "palette",
                CanonicalId: realPicture, Source: ModeSource.BuiltIn),
            // Hypothetical future authoritative built-in Wave record carrying the test-only stable ID.
            new HomeAppItem("wave", "Wave", "Create and edit audio locally", "wave",
                CanonicalId: futureWaveId, Source: ModeSource.BuiltIn),
            // A custom Created app that merely uses the key/name "wave": must stay independent.
            new HomeAppItem("wave", "Wave (custom)", "User-created lookalike", "wave",
                CanonicalId: Guid.NewGuid(), Source: ModeSource.Created),
            new HomeAppItem("files", "Files", "Private browser workspace", "folder")
        };

        var launcher = HomeCreateGroup.BuildLauncher(catalogue);
        // The future built-in Wave resolves as a canonical member and moves under Create; the custom
        // "wave" lookalike remains a top-level app.
        Assert.DoesNotContain(launcher, item => !item.IsGroup && item.Source == ModeSource.BuiltIn && item.Key == "wave");
        Assert.Contains(launcher, item => !item.IsGroup && item.Key == "wave" && item.Source == ModeSource.Created);

        var group = HomeCreateGroup.Build(catalogue);
        // Exactly one Wave member, propagating the provided stable test-only ID and built-in source/key.
        var wave = Assert.Single(group.Members, member => member.Key == "wave");
        Assert.Equal(futureWaveId, wave.CanonicalId);
        Assert.Equal(ModeSource.BuiltIn, wave.Source);
        Assert.Equal("wave", wave.Key);
        // The custom lookalike is never captured as a second Wave member.
        Assert.DoesNotContain(group.Members, member => member.Name.Contains("custom", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Group-aware launcher search surfaces Create when the query matches the group's OWN
    /// key/name or one of its child members, and honestly yields an empty (no-match) projection when
    /// nothing matches — the group is never surfaced as an installed app.</summary>
    [Fact]
    public void Launcher_search_surfaces_Create_via_own_name_or_child_and_shows_no_match_honestly()
    {
        var all = RealCatalogue();
        var launcher = HomeCreateGroup.BuildLauncher(all);
        var group = HomeCreateGroup.Build(all);

        // Searching "create" matches the group's OWN key/name -> Create stays visible.
        var byOwn = HomeCreateGroup.FilterLauncher(launcher, group, "create");
        Assert.Contains(byOwn, item => item.IsGroup && item.Key == "create");

        // Searching a child identity ("imagine") also keeps Create visible even while closed.
        var byChild = HomeCreateGroup.FilterLauncher(launcher, group, "imagine");
        Assert.Contains(byChild, item => item.IsGroup && item.Key == "create");

        // An unrelated query hides every tile, including Create, producing an honest no-match state.
        var noMatch = HomeCreateGroup.FilterLauncher(launcher, group, "zzz-no-such-app");
        Assert.Empty(noMatch);
    }

    /// <summary>When the Create group is OPEN, a query matching the group's OWN key/name/description
    /// (e.g. "create") surfaces every member, so opening Create from its own tile never hides its
    /// children; a query matching only a child keeps child-specific filtering; an unrelated query
    /// yields an honest empty surface (never a blank area).</summary>
    [Fact]
    public void ResolveGroupVisible_shows_all_members_on_own_match_and_filters_by_child_otherwise()
    {
        var group = HomeCreateGroup.Build(RealCatalogue());

        // Query matches the group's own key/name -> all three members (Picture, Motion, Wave) show.
        var byOwn = HomeCreateGroup.ResolveGroupVisible(group, "create");
        Assert.Equal(3, byOwn.Count);
        Assert.Equal(new[] { "Picture", "Motion", "Wave" }, byOwn.Select(member => member.Name));

        // An empty query also shows every member (unfiltered open group).
        Assert.Equal(3, HomeCreateGroup.ResolveGroupVisible(group, string.Empty).Count);

        // A child-specific query keeps child filtering: only Wave matches "wave".
        var waveOnly = HomeCreateGroup.ResolveGroupVisible(group, "wave");
        Assert.Equal("Wave", Assert.Single(waveOnly).Name);

        // An unrelated no-match query yields an honest empty surface.
        Assert.Empty(HomeCreateGroup.ResolveGroupVisible(group, "zzz-no-such-member"));

        // A description-only match with no child key/name match ("Open" is in the group description,
        // but in no child key/name) still shows all members under the documented precedence.
        Assert.Equal(3, HomeCreateGroup.ResolveGroupVisible(group, "Open").Count);
    }

    /// <summary>Rendered proof of the Create group through the real authored CUI document and typed
    /// bindings: a single Create tile opens via an actual rendered click (CUI document Variable
    /// state), Picture/Motion appear exactly once and enabled, Wave is a truthful disabled/unavailable
    /// row, member clicks route the canonical key, Back restores the launcher, reopen works, and
    /// searching a child key still surfaces Create.</summary>
    [AvaloniaFact]
    public async Task Create_group_tile_is_navigation_and_opens_three_rows_picture_motion_enabled_wave_disabled()
    {
        var state = GroupState();
        var actions = new List<(string Command, object? Parameter)>();
        HomeAppsCuiBindings bindings = null!;
        bindings = new HomeAppsCuiBindings(() => state, (command, parameter, _) =>
        {
            actions.Add((command, parameter));
            if (command == "home.set-app-query") state = GroupState((parameter as string ?? string.Empty).Trim());
            if (command == "home.clear-app-query") state = GroupState(string.Empty);
            bindings.Refresh();
            return ValueTask.CompletedTask;
        });
        var (root, loader) = Render(bindings, out var diagnostics);
        using (loader)
        {
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);

            // Closed launcher: exactly one Create tile; Picture/Motion are not duplicated at top level.
            var createTile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-tile");
            Assert.Equal("Open Create", AutomationProperties.GetName(createTile));
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-member");
            var topTiles = root.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-app-tile").Select(button => Assert.IsType<string>(AutomationProperties.GetName(button))).ToArray();
            Assert.DoesNotContain(topTiles, name => name.Contains("Picture", StringComparison.Ordinal) || name.Contains("Motion", StringComparison.Ordinal));

            // Open via an ACTUAL rendered click on the Create tile (document Variable state).
            createTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();

            // Exactly three member rows render: Picture and Motion enabled, Wave disabled/unavailable.
            var members = root.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-create-member").ToArray();
            Assert.Equal(3, members.Length);
            var enabled = members.Where(button => button.IsEnabled).ToArray();
            Assert.Equal(2, enabled.Length);
            Assert.Equal(new[] { "Open Motion", "Open Picture" },
                enabled.Select(AutomationProperties.GetName).OrderBy(name => name));
            var disabledWave = Assert.Single(members, button => !button.IsEnabled);
            Assert.Equal("Unavailable: Wave", AutomationProperties.GetName(disabledWave));

            // Wave is honestly unavailable via the member note (and mentions audio).
            var waveNote = Assert.Single(root.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Name == "home-create-member-note");
            Assert.Contains("Wave", waveNote.Text, StringComparison.Ordinal);
            Assert.Contains("not installed", waveNote.Text, StringComparison.OrdinalIgnoreCase);

            // An ACTUAL member click routes the canonical key (positive routing identity).
            var picture = Assert.Single(members, button => button.IsEnabled
                && AutomationProperties.GetName(button) == "Open Picture");
            picture.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(actions, action => action.Command == "home.open-app" && (string?)action.Parameter == "imagine");
            // Wave is disabled and cannot dispatch as ready.
            Assert.DoesNotContain(actions, action => action.Command == "home.open-app" && (string?)action.Parameter == "wave");

            // Back (actual rendered click on the Back button) restores the launcher.
            var back = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-back");
            back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-tile");
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-member");

            // Reopen still works (document Variable toggles again).
            root.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "home-create-tile")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, root.GetVisualDescendants().OfType<Button>()
                .Count(button => button.Name == "home-create-member"));

            // Back again, then searching a CHILD key/name still surfaces the Create group.
            root.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "home-create-back")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            await bindings.DispatchAsync("home.set-app-query", "imagine", CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-tile");

            // Clear restores the full launcher (Files, Boards, Create).
            await bindings.DispatchAsync("home.clear-app-query", null, CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, root.GetVisualDescendants().OfType<Button>()
                .Count(button => button.Name == "home-app-tile"));
            Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-tile");
        }
    }

    /// <summary>Rendered proof of the fixed search-then-open flow: searching "create" keeps the Create
    /// tile visible, and actually clicking it opens the group showing all THREE rows (Picture, Motion,
    /// Wave) rather than filtering its children away by the retained "create" query.</summary>
    [AvaloniaFact]
    public async Task Search_create_then_click_Create_opens_all_three_member_rows()
    {
        var state = GroupState();
        var actions = new List<(string Command, object? Parameter)>();
        HomeAppsCuiBindings bindings = null!;
        bindings = new HomeAppsCuiBindings(() => state, (command, parameter, _) =>
        {
            actions.Add((command, parameter));
            if (command == "home.set-app-query") state = GroupState((parameter as string ?? string.Empty).Trim());
            if (command == "home.clear-app-query") state = GroupState(string.Empty);
            bindings.Refresh();
            return ValueTask.CompletedTask;
        });
        var (root, loader) = Render(bindings, out var diagnostics);
        using (loader)
        {
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);

            // Searching "create" keeps the Create navigation tile visible.
            await bindings.DispatchAsync("home.set-app-query", "create", CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            var createTile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-tile");
            Assert.Equal("Open Create", AutomationProperties.GetName(createTile));

            // Clicking Create opens the group with all three member rows (Picture, Motion, Wave), not
            // filtered away by the retained "create" query.
            createTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            var members = root.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-create-member").ToArray();
            Assert.Equal(3, members.Length);
            Assert.Equal(new[] { "Open Motion", "Open Picture", "Unavailable: Wave" },
                members.Select(AutomationProperties.GetName).OrderBy(name => name));
        }
    }

    /// <summary>Rendered proof of the honest open-group no-match surface: with the group open and a
    /// query that matches nothing, the CUI shows a clear "No Create members match your search." text
    /// and a "Clear search" button (not a blank region); clicking Clear restores all three rows.</summary>
    [AvaloniaFact]
    public async Task Open_group_no_match_shows_clear_prompt_and_clear_search_restores_rows()
    {
        var state = GroupState();
        var actions = new List<(string Command, object? Parameter)>();
        HomeAppsCuiBindings bindings = null!;
        bindings = new HomeAppsCuiBindings(() => state, (command, parameter, _) =>
        {
            actions.Add((command, parameter));
            if (command == "home.set-app-query") state = GroupState((parameter as string ?? string.Empty).Trim());
            if (command == "home.clear-app-query") state = GroupState(string.Empty);
            bindings.Refresh();
            return ValueTask.CompletedTask;
        });
        var (root, loader) = Render(bindings, out var diagnostics);
        using (loader)
        {
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);

            // Open the group first (empty query -> all three rows).
            root.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "home-create-tile")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, root.GetVisualDescendants().OfType<Button>()
                .Count(button => button.Name == "home-create-member"));

            // A no-match query while open shows the honest group-empty text and the Clear search button.
            await bindings.DispatchAsync("home.set-app-query", "zzz-no-such-member", CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-member");
            var texts = string.Join(" | ", root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text));
            Assert.Contains("No Create members match your search.", texts);
            var clear = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-group-clear");
            Assert.Equal("Clear search", AutomationProperties.GetName(clear));

            // Clicking Clear restores all three rows.
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, root.GetVisualDescendants().OfType<Button>()
                .Count(button => button.Name == "home-create-member"));
        }
    }

    /// <summary>Pins the exact 14 canonical built-in GUID → PNG stem brand map and confirms held/
    /// excluded/missing identities (Build/dev, Terminal, Plan, Wave) never map.</summary>
    [Fact]
    public void Owner_brand_map_is_exact_builtin_GUID_and_stem()
    {
        var expected = new Dictionary<string, string>
        {
            ["a0000000-0000-0000-0000-000000000003"] = "automations",
            ["a0000000-0000-0000-0000-000000000023"] = "boards",
            ["a0000000-0000-0000-0000-000000000018"] = "canvas",
            ["a0000000-0000-0000-0000-000000000010"] = "data",
            ["a0000000-0000-0000-0000-000000000030"] = "files",
            ["a0000000-0000-0000-0000-000000000026"] = "forms",
            ["a0000000-0000-0000-0000-000000000008"] = "picture",
            ["a0000000-0000-0000-0000-000000000027"] = "shelf",
            ["a0000000-0000-0000-0000-000000000024"] = "maps",
            ["a0000000-0000-0000-0000-000000000025"] = "motion",
            ["a0000000-0000-0000-0000-000000000012"] = "play",
            ["a0000000-0000-0000-0000-000000000009"] = "present",
            ["a0000000-0000-0000-0000-000000000022"] = "spaces",
            ["a0000000-0000-0000-0000-000000000004"] = "ai-studio"
        };
        foreach (var pair in expected)
        {
            Assert.True(HomeAppBrandingIcons.TryGetBrandStem(Guid.Parse(pair.Key), ModeSource.BuiltIn, out var stem));
            Assert.Equal(pair.Value, stem);
        }
        // Held/excluded/missing identities never map: Build (dev) has no reached image, Terminal failed
        // verification, Plan variants are held, and Wave has no native GUID in this composition.
        Assert.False(HomeAppBrandingIcons.TryGetBrandStem(Guid.Parse("a0000000-0000-0000-0000-000000000028"), ModeSource.BuiltIn, out _));
        Assert.False(HomeAppBrandingIcons.TryGetBrandStem(Guid.Parse("a0000000-0000-0000-0000-000000000020"), ModeSource.BuiltIn, out _));
        Assert.False(HomeAppBrandingIcons.TryGetBrandStem(Guid.Parse("a0000000-0000-0000-0000-000000000006"), ModeSource.BuiltIn, out _));
        Assert.False(HomeAppBrandingIcons.TryGetBrandStem(null, ModeSource.BuiltIn, out _));
    }

    /// <summary>Brand source projection is by REAL built-in identity: custom same-name/created apps and
    /// shared-IconKey lookalikes stay on their existing catalog icon; only exact built-in GUIDs map.</summary>
    [Fact]
    public void Owner_brand_source_projects_only_real_builtin_identity()
    {
        var picture = Guid.Parse("a0000000-0000-0000-0000-000000000008");
        var files = Guid.Parse("a0000000-0000-0000-0000-000000000030");
        var shelf = Guid.Parse("a0000000-0000-0000-0000-000000000027");
        var build = Guid.Parse("a0000000-0000-0000-0000-000000000028");

        // Real built-in Picture → brand token with PNG stem 'picture' and fallback icon 'palette'.
        Assert.Equal("ownerbrand:picture|palette", HomeAppBrandingIcons.SourceFor("palette", picture, ModeSource.BuiltIn));
        // Shared-icon-key apps that ARE mapped keep their distinct stems (never by key alone).
        Assert.Equal("ownerbrand:files|folder", HomeAppBrandingIcons.SourceFor("folder", files, ModeSource.BuiltIn));
        Assert.Equal("ownerbrand:shelf|folder", HomeAppBrandingIcons.SourceFor("folder", shelf, ModeSource.BuiltIn));
        // Build shares 'code' with AI Studio but is NOT on the reached brand map.
        Assert.Equal("icon:code", HomeAppBrandingIcons.SourceFor("code", build, ModeSource.BuiltIn));
        // Custom/created apps (even same name, key or icon) are never branded.
        Assert.Equal("icon:palette", HomeAppBrandingIcons.SourceFor("palette", Guid.NewGuid(), ModeSource.Created));
        Assert.Equal("icon:file", HomeAppBrandingIcons.SourceFor("file", picture, ModeSource.Created));
        // Null identity (Wave reference) stays a plain catalog icon.
        Assert.Equal("icon:wave", HomeAppBrandingIcons.SourceFor("wave", null, ModeSource.BuiltIn));
    }

    /// <summary>Both Apps and installed Create member projection carry the brand source; the Wave
    /// reference (no native GUID) remains unmapped and keeps its catalog icon.</summary>
    [Fact]
    public void Installed_Create_members_project_brand_sources_wave_stays_unmapped()
    {
        var group = HomeCreateGroup.Build(RealCatalogue());

        var picture = Assert.Single(group.Members, member =>
            member.CanonicalId == Guid.Parse("a0000000-0000-0000-0000-000000000008"));
        Assert.True(picture.IsInstalled);
        Assert.Equal("ownerbrand:picture|palette",
            HomeAppBrandingIcons.SourceFor(picture.IconKey, picture.CanonicalId, picture.Source));

        var motion = Assert.Single(group.Members, member =>
            member.CanonicalId == Guid.Parse("a0000000-0000-0000-0000-000000000025"));
        Assert.True(motion.IsInstalled);
        Assert.Equal("ownerbrand:motion|play",
            HomeAppBrandingIcons.SourceFor(motion.IconKey, motion.CanonicalId, motion.Source));

        var wave = Assert.Single(group.Members, member => member.Key == "wave");
        Assert.Null(wave.CanonicalId);
        Assert.Equal("icon:wave", HomeAppBrandingIcons.SourceFor(wave.IconKey, wave.CanonicalId, wave.Source));
    }

    /// <summary>The real effective CUI appearance selects the themed PNG folder: SuperBright/Bright →
    /// light; Dark/SuperDark → dark. Never inferred from theme name or accent.</summary>
    [Theory]
    [InlineData(CuiAppearance.SuperBright, "light")]
    [InlineData(CuiAppearance.Bright, "light")]
    [InlineData(CuiAppearance.Dark, "dark")]
    [InlineData(CuiAppearance.SuperDark, "dark")]
    public void Appearance_selects_theme_folder(CuiAppearance appearance, string folder)
    {
        Assert.Equal(folder, HomeAppBrandingIcons.AppearanceFolder(appearance));
    }

    /// <summary>The brand resource URI is a bounded packaged `avares://Haven/Assets/OwnerAppIcons/{light,
    /// dark}/<stem>.png` identity — never a file/network/data URI.</summary>
    [Fact]
    public void Brand_resource_uri_is_bounded_packaged_and_themed()
    {
        Assert.Equal("avares://Haven/Assets/OwnerAppIcons/light/files.png",
            HomeAppBrandingIcons.ResourceUri("files", "light"));
        Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png",
            HomeAppBrandingIcons.ResourceUri("files", "dark"));
        Assert.Equal("avares://Haven/Assets/OwnerAppIcons/light/ai-studio.png",
            HomeAppBrandingIcons.ResourceUri("ai-studio", "light"));
    }

    /// <summary>Rendered honesty independent of whether the real owner PNGs exist yet: a brand app
    /// renders SOME resolved image (the packaged PNG once integrated, or the existing vector catalog
    /// icon before), never a blank/loader error; unknown brand tokens resolve to nothing so the
    /// composed chain can continue honestly. The deterministic missing/corrupt packaged fallback is
    /// proven separately by the injected recorder provider (see the scoped-appearance tests).</summary>
    [AvaloniaFact]
    public void Brand_tiles_render_a_resolved_image_before_and_after_png_integration()
    {
        var all = new[]
        {
            new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn),
            new HomeAppItem("imagine", "Picture (custom)", "Custom lookalike must stay unmapped", "palette",
                CanonicalId: Guid.NewGuid(), Source: ModeSource.Created),
            new HomeAppItem("boards", "Boards", "Context workspace", "boards",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000023"), Source: ModeSource.BuiltIn)
        };
        var state = new HomeAppsViewState(all, HomeAppsViewState.Filter(all, string.Empty), string.Empty,
            "Select an app to open it in this tab.");
        var bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
        var (root, loader) = Render(bindings, out var diagnostics);
        using (loader)
        {
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            var tiles = root.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-app-tile").ToArray();
            Assert.Equal(3, tiles.Length);
            // Every tile resolves SOME IImage (real PNG once integrated, or the honest vector fallback
            // before), never a blank or loader error.
            Assert.All(tiles, tile => Assert.IsAssignableFrom<IImage>(
                Assert.Single(tile.GetVisualDescendants().OfType<Image>()).Source));
            // Direct resolver proof: a known brand source resolves to a real IImage.
            Assert.IsAssignableFrom<IImage>(HomeAppBrandingIcons.Resolve("ownerbrand:files|folder", root));
            // Unknown brand tokens resolve to nothing (bounded) so the chain can continue honestly.
            Assert.Null(HomeAppBrandingIcons.Resolve("ownerbrand:not-a-stem|folder", root));
        }
    }

    /// <summary>Unlike the missing-resource fallback probes, this integration check requires the
    /// actual owner PNGs in the built assembly and decodes them with the production backend. A vector
    /// fallback cannot turn a missing/incorrectly wired package resource into a passing result.</summary>
    [AvaloniaFact]
    public void Packaged_owner_brand_assets_decode_with_the_production_image_resolver()
    {
        var resolver = new HavenAvaloniaImageResolver();
        string[] stems =
        [
            "automations", "boards", "canvas", "data", "files", "forms", "picture", "shelf",
            "maps", "motion", "play", "present", "spaces", "ai-studio"
        ];
        foreach (var folder in new[] { "light", "dark" })
        foreach (var stem in stems)
        {
            var uri = HomeAppBrandingIcons.ResourceUri(stem, folder);
            Assert.True(resolver.TryResolve(uri, out var image), "Production asset decoding failed: " + uri);
            var bitmap = Assert.IsType<Avalonia.Media.Imaging.Bitmap>(image);
            Assert.True(bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0, uri);
            bitmap.Dispose();
        }
    }

    // ------------------------------------------------------------------------------------------
    // Fix 1 + Fix 3: real Home-owned scoped appearance observation/invalidation, exercised through
    // a deterministic injected packaged-provider recorder. The recorder replaces the private default
    // decoder only within the test; production keeps the real backend decoder.
    // ------------------------------------------------------------------------------------------

    private sealed class RecordingImageResolver : IHavenAvaloniaImageResolver
    {
        internal List<string> Attempts { get; } = new();

        public bool TryResolve(string source, out IImage? image)
        {
            Attempts.Add(source);
            image = null;
            return false;
        }
    }

    private static (Control Root, CuiControlLoader Loader) RenderUnderHost(
        HomeAppsCuiBindings bindings, ContentControl host, out IReadOnlyList<CuiDiagnostic> diagnostics,
        out Window window)
    {
        window = null!;
        CuiControlLoader? loader = null;
        try
        {
            var registry = HomeAppsCuiDocument.CreateControlRegistry(bindings);
            loader = new CuiControlLoader(registry);
            loader.SetBindingContext(bindings);
            loader.SetActionDispatcher(bindings);
            var document = HomeAppsCuiDocument.Parse();
            var loaded = loader.TryLoad(document);
            diagnostics = loaded.Diagnostics;
            var root = loaded.Root ?? throw new InvalidDataException("The Home apps document produced no surface: " +
                string.Join("; ", loaded.Diagnostics.Select(item => item.Code + " " + item.Message)));
            loader.WireBindings(root);
            host.Content = root;
            window = new Window { Content = host, Width = 1280, Height = 900 };
            window.Show();
            window.UpdateLayout();
            root.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            return (root, loader);
        }
        catch
        {
            // If load/layout fails before the caller receives the loader, clean up locally so the
            // caller's finally never sees an un-owned loader/window.
            loader?.Dispose();
            window?.Close();
            throw;
        }
    }

    /// <summary>Captures the active Image of the Files brand target, removes that row via the real
    /// repeated list (page/bindings/loader stay alive), then returns only a weak reference.
    /// <see cref="MethodImplOptions.NoInlining"/> gives the caller a genuine stack-frame lifetime
    /// boundary before it collects and asserts that the old image is collectable.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CaptureRemovedImageWeakReference(
        HomeAppsCuiBindings bindings, Control root, HomeAppsViewState[] state,
        HomeAppItem files, HomeAppItem boards, List<Image>? retainedByOwner = null)
    {
        var filesTile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
            button => button.Name == "home-app-tile" && AutomationProperties.GetName(button) == "Open Files");
        var filesImage = Assert.Single(filesTile.GetVisualDescendants().OfType<Image>());
        var weakImage = new WeakReference(filesImage);
        retainedByOwner?.Add(filesImage);

        // Actual Repeat removal: drop the Files row from the repeated list. The page, bindings and
        // loader stay alive in the caller, so collectability proves the owner does not retain the
        // removed target.
        state[0] = new HomeAppsViewState(new[] { files, boards },
            HomeAppsViewState.Filter(new[] { files, boards }, "boards"), "boards", "...");
        bindings.Refresh();
        Dispatcher.UIThread.RunJobs();

        // Collection belongs in the caller, after this NoInlining frame has returned. Clearing
        // locals is not a GC lifetime boundary: the JIT may retain this frame's last-use temporaries.
        return weakImage;
    }

    /// <summary>Test seam that swaps the packaged provider to a deterministic missing/corrupt recorder
    /// EXACTLY ONCE per test, remembering the genuine original decoder so it is always restored even on
    /// assertion/load failure. The observer coordinator is never global: each test passes its own
    /// bindings into <see cref="HomeAppsCuiDocument.CreateControlRegistry(IHomeBrandObservationCoordinator?)"/>
    /// instead, so there is no process-wide lifecycle owner and nothing is left installed.</summary>
    private sealed class ArmedImaging : IDisposable
    {
        internal RecordingImageResolver Recording { get; } = new();
        private readonly IHavenAvaloniaImageResolver _priorProvider;

        internal ArmedImaging()
        {
            _priorProvider = HomeAppBrandingIcons.PackagedBitmap;
            HomeAppBrandingIcons.PackagedBitmap = Recording;
        }

        internal string LastAttempt => Recording.Attempts.Count == 0 ? string.Empty : Recording.Attempts[^1];

        public void Dispose() => HomeAppBrandingIcons.PackagedBitmap = _priorProvider;
    }

    /// <summary>Pins the live scoped-appearance invalidation contract: when the effective
    /// "CuiAppearance" changes, the actual brand Image target's resource observation fires and CUI
    /// re-resolves the existing rendered image to the new folder. The injected missing provider also
    /// proves the deterministic vector fallback (no fabricated art) and that the live resolution really
    /// follows the scoped resource across appearances.</summary>
    [AvaloniaFact]
    public void Scoped_appearance_change_refreshes_rendered_brand_images_via_recorded_provider()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var all = new[]
            {
                new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                    CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn)
            };
            var state = new HomeAppsViewState(all, HomeAppsViewState.Filter(all, string.Empty), string.Empty,
                "Select an app to open it in this tab.");
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright";
            bindings = new HomeAppsCuiBindings(() => state,
                (_, _, _) => ValueTask.CompletedTask);
            var (root, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            var tile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-app-tile");
            var image = Assert.Single(tile.GetVisualDescendants().OfType<Image>());
            // Missing provider -> the honest vector fallback, never a blank/loader error.
            Assert.IsType<DrawingImage>(image.Source);
            // Light folder was requested for the mapped Files brand.
            Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/light/files.png", StringComparison.Ordinal));

            // Change the effective scoped appearance: the actual brand target's observation fires and
            // CUI re-resolves the existing rendered image to the dark folder.
            var before = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "Dark";
            Dispatcher.UIThread.RunJobs();
            Assert.True(armed.Recording.Attempts.Count > before, "appearance change must trigger a fresh request");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>Lifecycle of the actual-target appearance observation: subscribed on attach (synchronous
    /// baseline consumed), disposed on detach so detached resource changes run no callback, resubscribed
    /// on re-attach and then refreshed so the current scope reapplies, and permanently retired (no
    /// callbacks even after teardown). The real authored document is rendered so the recorded provider
    /// proves each refresh genuinely re-resolves the existing rendered image.</summary>
    [AvaloniaFact]
    public void Appearance_observation_lifecycle_attach_detach_reattach_and_retirement()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var all = new[]
            {
                new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                    CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn)
            };
            var state = new HomeAppsViewState(all, HomeAppsViewState.Filter(all, string.Empty), string.Empty,
                "Select an app to open it in this tab.");
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright";
            bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            var (root, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            var tile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-app-tile");
            Assert.IsType<DrawingImage>(Assert.Single(tile.GetVisualDescendants().OfType<Image>()).Source);
            // Attached: initial Bright baseline produced a light request.
            Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/light/files.png", StringComparison.Ordinal));

            // Attached appearance change re-resolves to dark.
            var afterAttached = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "Dark";
            Dispatcher.UIThread.RunJobs();
            Assert.True(armed.Recording.Attempts.Count > afterAttached);
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);

            // Detach the whole tree: no callbacks run while detached, even if the resource changes.
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            var afterDetach = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "SuperDark";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(afterDetach, armed.Recording.Attempts.Count);

            // Re-attach: resubscribed with the current appearance (SuperDark) and refreshed.
            window.Content = host;
            Dispatcher.UIThread.RunJobs();
            Assert.True(armed.Recording.Attempts.Count > afterDetach, "reattach must re-resolve with the current appearance");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);

            // Still attached: change to Bright refreshes to light.
            var beforeRetire = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "Bright";
            Dispatcher.UIThread.RunJobs();
            Assert.True(armed.Recording.Attempts.Count > beforeRetire);
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/light/files.png", armed.LastAttempt);

            // Retirement: no further callbacks, even when the resource changes afterwards.
            bindings.RetireAppearanceObservation();
            var retired = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "Dark";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(retired, armed.Recording.Attempts.Count);
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>Scoped resolution: a child scope overrides the parent's "CuiAppearance"; the
    /// selected folder follows the effective scoped resource per target.</summary>
    [Fact]
    public void DetectAppearance_respects_scoped_resources_across_parent_child_scopes()
    {
        var parent = new ContentControl();
        parent.Resources["CuiAppearance"] = "Bright";
        var child = new ContentControl();
        parent.Content = child;
        Assert.Equal(CuiAppearance.Bright, HomeAppBrandingIcons.DetectAppearance(child));
        child.Resources["CuiAppearance"] = "SuperDark";
        Assert.Equal(CuiAppearance.SuperDark, HomeAppBrandingIcons.DetectAppearance(child));
        Assert.Equal(CuiAppearance.Bright, HomeAppBrandingIcons.DetectAppearance(parent));
    }

    // ------------------------------------------------------------------------------------------
    // Fix 2: installation/identity honesty. Unavailable Picture/Motion placeholders carry real GUIDs;
    // the actual binding must require IsInstalled before emitting owner branding.
    // ------------------------------------------------------------------------------------------

    private static string ItemIconSource(HomeAppsCuiBindings bindings, object item)
    {
        Assert.True(bindings.TryGetItemValue(item, "iconSource", out var value), "iconSource binding resolved");
        Assert.IsAssignableFrom<string>(value);
        return (string)value!;
    }

    /// <summary>The actual binding projection: a member with a real canonical GUID but not installed
    /// stays on its catalog icon (never branded); only installed members emit owner branding.</summary>
    [Fact]
    public void Unavailable_member_reference_never_emits_owner_branding()
    {
        var catalogue = new[] { new HomeAppItem("files", "Files", "Private browser workspace", "folder") };
        var group = HomeCreateGroup.Build(catalogue);
        var picture = Assert.Single(group.Members, member => member.Key == "imagine");
        Assert.False(picture.IsInstalled);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000008"), picture.CanonicalId);
        var motion = Assert.Single(group.Members, member => member.Key == "motion");
        Assert.False(motion.IsInstalled);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000025"), motion.CanonicalId);
        var wave = Assert.Single(group.Members, member => member.Key == "wave");
        Assert.False(wave.IsInstalled);

        var bindings = new HomeAppsCuiBindings(() => new HomeAppsViewState([], [], string.Empty, ""),
            (_, _, _) => ValueTask.CompletedTask);
        // Real GUIDs are carried, yet absent/unavailable members stay on their generic catalog icon.
        Assert.Equal("icon:palette", ItemIconSource(bindings, picture));
        Assert.Equal("icon:play", ItemIconSource(bindings, motion));
        Assert.Equal("icon:wave", ItemIconSource(bindings, wave));
    }

    /// <summary>Installed members carry the owner-brand source (no baked-in folder; light/dark is chosen
    /// at resolution time from the actual target scope); the group tile itself stays generic navigation
    /// (never branded).</summary>
    [Fact]
    public void Installed_member_binding_projects_owner_brand_with_current_appearance()
    {
        var group = HomeCreateGroup.Build(RealCatalogue());
        var picture = Assert.Single(group.Members, m =>
            m.CanonicalId == Guid.Parse("a0000000-0000-0000-0000-000000000008"));
        var motion = Assert.Single(group.Members, m =>
            m.CanonicalId == Guid.Parse("a0000000-0000-0000-0000-000000000025"));
        Assert.True(picture.IsInstalled && motion.IsInstalled);

        var bindings = new HomeAppsCuiBindings(() => new HomeAppsViewState([], [], string.Empty, ""),
            (_, _, _) => ValueTask.CompletedTask);
        Assert.Equal("ownerbrand:picture|palette", ItemIconSource(bindings, picture));
        Assert.Equal("ownerbrand:motion|play", ItemIconSource(bindings, motion));
        // The group navigation tile stays generic, never branded.
        Assert.Equal("icon:folder", ItemIconSource(bindings,
            new HomeAppItem("create", "Create", "Open Picture, Motion and Wave", "folder", IsGroup: true)));
    }

    /// <summary>Real canonical fixture: Picture (built-in GUID …0008) is installed at top level; Motion
    /// (built-in GUID …0025) is absent, so it is a real unavailable member. The Create navigation tile
    /// exists because a genuine canonical member (Picture) is present; it is never manufactured empty.</summary>
    private static IReadOnlyList<HomeAppItem> PictureInstalledMotionUnavailableCatalogue() => new[]
    {
        new HomeAppItem("files", "Files", "Private browser workspace", "folder"),
        new HomeAppItem("imagine", "Picture", "Create and refine image concepts with local models", "palette",
            CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000008"), Source: ModeSource.BuiltIn),
        new HomeAppItem("boards", "Boards", "Context workspace", "boards")
    };

    /// <summary>Converse real fixture: Motion is installed; Picture is absent (real unavailable member).</summary>
    private static IReadOnlyList<HomeAppItem> MotionInstalledPictureUnavailableCatalogue() => new[]
    {
        new HomeAppItem("files", "Files", "Private browser workspace", "folder"),
        new HomeAppItem("motion", "Motion", "Open the Motion workspace shell", "play",
            CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000025"), Source: ModeSource.BuiltIn),
        new HomeAppItem("boards", "Boards", "Context workspace", "boards")
    };

    private static HomeAppsViewState GroupStateFor(IReadOnlyList<HomeAppItem> catalogue, string query = "")
    {
        var launcher = HomeCreateGroup.BuildLauncher(catalogue);
        var group = HomeCreateGroup.Build(catalogue);
        var groupVisible = HomeCreateGroup.ResolveGroupVisible(group, query);
        return new HomeAppsViewState(catalogue, HomeCreateGroup.FilterLauncher(launcher, group, query),
            query, "Select an app to open it in this tab.", launcher, group, groupVisible);
    }

    private static readonly (string Text, string Folder)[] Modes =
    [
        ("SuperBright", "light"), ("Bright", "light"),
        ("Dark", "dark"), ("SuperDark", "dark")
    ];

    /// <summary>Real branded live fixture: Files carries a real brand mapping at top level, Picture is an
    /// installed canonical built-in member, Motion is absent (real unavailable member). Exercises both
    /// the Apps launcher brand image and an already-open Create installed-member image.</summary>
    private static IReadOnlyList<HomeAppItem> BrandedLiveCatalogue() => new[]
    {
        new HomeAppItem("files", "Files", "Private browser workspace", "folder",
            CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn),
        new HomeAppItem("imagine", "Picture", "Create and refine image concepts with local models", "palette",
            CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000008"), Source: ModeSource.BuiltIn),
        new HomeAppItem("boards", "Boards", "Context workspace", "boards")
    };

    /// <summary>Render helper that returns the shown Window so callers can close it (and the loader) in
    /// a finally block, so the test doesn't leak a Window.</summary>
    private static (Control Root, CuiControlLoader Loader) RenderClosing(
        HomeAppsCuiBindings bindings, out IReadOnlyList<CuiDiagnostic> diagnostics, out Window window)
    {
        var registry = HomeAppsCuiDocument.CreateControlRegistry(bindings);
        var loader = new CuiControlLoader(registry);
        loader.SetBindingContext(bindings);
        loader.SetActionDispatcher(bindings);
        var document = HomeAppsCuiDocument.Parse();
        var loaded = loader.TryLoad(document);
        diagnostics = loaded.Diagnostics;
        var root = loaded.Root ?? throw new InvalidDataException("The Home apps document produced no surface: " +
            string.Join("; ", loaded.Diagnostics.Select(item => item.Code + " " + item.Message)));
        loader.WireBindings(root);
        window = new Window { Content = root, Width = 1280, Height = 900 };
        window.Show();
        window.UpdateLayout();
        root.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return (root, loader);
    }

    /// <summary>Rendered proof with a REAL Create fixture: Picture installed, Motion/Wave unavailable.
    /// The real canonical Picture drives the genuine Create tile. Only the installed Picture row may
    /// request its owner PNG; unavailable Motion/Wave rows stay generic (never branded) despite carrying
    /// real canonical GUIDs, and the group-navigation tile itself stays generic.</summary>
    [AvaloniaFact]
    public void Rendered_Create_with_Picture_installed_Motion_unavailable_requests_png_only_for_installed()
    {
        using var armed = new ArmedImaging();
        Window? window = null;
        try
        {
            var state = GroupStateFor(PictureInstalledMotionUnavailableCatalogue());
            var bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            var (root, loader) = RenderClosing(bindings, out var diagnostics, out window);
            using (loader)
            {
                Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
                // A real canonical member exists (Picture), so a genuine Create tile is present.
                var createTile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "home-create-tile");
                Assert.IsType<DrawingImage>(Assert.Single(createTile.GetVisualDescendants().OfType<Image>()).Source);
                createTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                loader.WhenActionsIdleAsync().GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();

                var members = root.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Name == "home-create-member").ToArray();
                Assert.Equal(3, members.Length);
                var pictureRow = Assert.Single(members, b => b.IsEnabled && AutomationProperties.GetName(b) == "Open Picture");
                var motionRow = Assert.Single(members, b => !b.IsEnabled && AutomationProperties.GetName(b) == "Unavailable: Motion");
                var waveRow = Assert.Single(members, b => !b.IsEnabled && AutomationProperties.GetName(b) == "Unavailable: Wave");
                Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/picture.png", StringComparison.Ordinal));
                Assert.DoesNotContain(armed.Recording.Attempts, uri =>
                    uri.Contains("/motion.png", StringComparison.Ordinal)
                    || uri.Contains("/wave.png", StringComparison.Ordinal));
                Assert.IsType<DrawingImage>(Assert.Single(motionRow.GetVisualDescendants().OfType<Image>()).Source);
                Assert.IsType<DrawingImage>(Assert.Single(waveRow.GetVisualDescendants().OfType<Image>()).Source);
            }
        }
        finally { window?.Close(); }
    }

    /// <summary>Converse real fixture: Motion installed, Picture/Wave unavailable. Only Motion requests
    /// its owner PNG; unavailable Picture/Wave stay generic.</summary>
    [AvaloniaFact]
    public void Rendered_Create_with_Motion_installed_Picture_unavailable_requests_png_only_for_installed()
    {
        using var armed = new ArmedImaging();
        Window? window = null;
        try
        {
            var state = GroupStateFor(MotionInstalledPictureUnavailableCatalogue());
            var bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            var (root, loader) = RenderClosing(bindings, out var diagnostics, out window);
            using (loader)
            {
                Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
                var createTile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "home-create-tile");
                Assert.IsType<DrawingImage>(Assert.Single(createTile.GetVisualDescendants().OfType<Image>()).Source);
                createTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                loader.WhenActionsIdleAsync().GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();

                var members = root.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Name == "home-create-member").ToArray();
                Assert.Equal(3, members.Length);
                Assert.Single(members, b => b.IsEnabled && AutomationProperties.GetName(b) == "Open Motion");
                Assert.Single(members, b => !b.IsEnabled && AutomationProperties.GetName(b) == "Unavailable: Picture");
                Assert.Single(members, b => !b.IsEnabled && AutomationProperties.GetName(b) == "Unavailable: Wave");
                Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/motion.png", StringComparison.Ordinal));
                Assert.DoesNotContain(armed.Recording.Attempts, uri =>
                    uri.Contains("/picture.png", StringComparison.Ordinal)
                    || uri.Contains("/wave.png", StringComparison.Ordinal));
            }
        }
        finally { window?.Close(); }
    }

    /// <summary>Live retained-UI proof: the same rendered Apps brand image and an ALREADY-OPEN Create
    /// installed-member image remain (same control instances) across all four appearances, each source
    /// is genuinely re-resolved through the typed CUI binding, unavailable members stay generic, and a
    /// NONEMPTY authored query plus a focused real search input / selection / open-group / current theme
    /// survive every appearance change.</summary>
    [AvaloniaFact]
    public async Task Live_home_brand_imaging_retains_same_controls_across_all_four_appearances()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var host = new ContentControl();
            var state = GroupStateFor(BrandedLiveCatalogue());
            bindings = new HomeAppsCuiBindings(() => state, (command, parameter, _) =>
            {
                if (command == "home.set-app-query")
                    state = GroupStateFor(BrandedLiveCatalogue(), (parameter as string ?? string.Empty).Trim());
                if (command == "home.clear-app-query")
                    state = GroupStateFor(BrandedLiveCatalogue());
                bindings.Refresh();
                return ValueTask.CompletedTask;
            });
            var (root, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);

            // Phase 1: Apps launcher brand image (Files tile), empty query. Capture BEFORE first stimulus.
            var filesTile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-app-tile" && AutomationProperties.GetName(button) == "Open Files");
            var retainedFileTile = filesTile;
            var retainedFileImage = Assert.Single(filesTile.GetVisualDescendants().OfType<Image>());
            foreach (var (text, folder) in Modes)
            {
                var before = armed.Recording.Attempts.Count;
                host.Resources["CuiAppearance"] = text;
                Dispatcher.UIThread.RunJobs();
                var tileNow = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "home-app-tile" && AutomationProperties.GetName(button) == "Open Files");
                Assert.Same(retainedFileTile, tileNow);
                Assert.Same(retainedFileImage, Assert.Single(tileNow.GetVisualDescendants().OfType<Image>()));
                Assert.True(armed.Recording.Attempts.Count > before, $"stimulus {text} must yield a fresh request");
                Assert.Equal($"avares://Haven/Assets/OwnerAppIcons/{folder}/files.png", armed.LastAttempt);
            }

            // Phase 2: NONEMPTY authored query ('create' matches the group's own identity and filters
            // Files out) + focus on the real authored search input.
            await bindings.DispatchAsync("home.set-app-query", "create", CancellationToken.None);
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("create", state.Query);
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-app-tile" && AutomationProperties.GetName(button) == "Open Files");

            var search = Assert.Single(root.GetVisualDescendants().OfType<TextBox>(), tb => tb.Name == "home-app-search");
            search.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(search.IsKeyboardFocusWithin);
            search.SelectionStart = 1;
            var selectionBefore = search.SelectionStart;

            // Open the Create group and keep it open; capture the Picture row/image BEFORE first stimulus.
            var createTile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-create-tile");
            createTile.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Dispatcher.UIThread.RunJobs();

            // Phase 3: ALREADY-OPEN Create installed-member image (Picture) across all four appearances.
            Button? retainedPictureRow = null;
            Image? retainedPictureImage = null;
            foreach (var (text, folder) in Modes)
            {
                var members = root.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Name == "home-create-member").ToArray();
                var pictureRow = Assert.Single(members, b => b.IsEnabled && AutomationProperties.GetName(b) == "Open Picture");
                if (retainedPictureRow is null) retainedPictureRow = pictureRow;
                else Assert.Same(retainedPictureRow, pictureRow);
                var pictureImage = Assert.Single(pictureRow.GetVisualDescendants().OfType<Image>());
                if (retainedPictureImage is null) retainedPictureImage = pictureImage;
                else Assert.Same(retainedPictureImage, pictureImage);

                var before = armed.Recording.Attempts.Count;
                host.Resources["CuiAppearance"] = text;
                Dispatcher.UIThread.RunJobs();
                Assert.True(armed.Recording.Attempts.Count > before, $"stimulus {text} must yield a fresh request");
                Assert.Equal($"avares://Haven/Assets/OwnerAppIcons/{folder}/picture.png", armed.LastAttempt);
                Assert.DoesNotContain(armed.Recording.Attempts, uri =>
                    uri.Contains("/motion.png", StringComparison.Ordinal)
                    || uri.Contains("/wave.png", StringComparison.Ordinal));

                var motionRow = Assert.Single(members, b => !b.IsEnabled && AutomationProperties.GetName(b) == "Unavailable: Motion");
                var waveRow = Assert.Single(members, b => !b.IsEnabled && AutomationProperties.GetName(b) == "Unavailable: Wave");
                Assert.IsType<DrawingImage>(Assert.Single(motionRow.GetVisualDescendants().OfType<Image>()).Source);
                Assert.IsType<DrawingImage>(Assert.Single(waveRow.GetVisualDescendants().OfType<Image>()).Source);

                // Identity must survive THIS update too (the update above may have replaced controls).
                var afterMembers = root.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Name == "home-create-member").ToArray();
                var afterRow = Assert.Single(afterMembers, b => b.IsEnabled && AutomationProperties.GetName(b) == "Open Picture");
                Assert.Same(retainedPictureRow, afterRow);
                Assert.Same(retainedPictureImage, Assert.Single(afterRow.GetVisualDescendants().OfType<Image>()));
            }

            // NONEMPTY query / open-group / focus / selection / current theme survived.
            Assert.Equal("create", state.Query);
            var finalMembers = root.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Name == "home-create-member").ToArray();
            Assert.Equal(3, finalMembers.Length);
            var finalPictureRow = Assert.Single(finalMembers, b => b.IsEnabled && AutomationProperties.GetName(b) == "Open Picture");
            Assert.Same(retainedPictureRow, finalPictureRow);
            Assert.Same(retainedPictureImage, Assert.Single(finalPictureRow.GetVisualDescendants().OfType<Image>()));
            Assert.Same(search, Assert.Single(root.GetVisualDescendants().OfType<TextBox>(), tb => tb.Name == "home-app-search"));
            Assert.True(search.IsKeyboardFocusWithin);
            Assert.Equal(selectionBefore, search.SelectionStart);
            Assert.Equal("SuperDark", host.Resources["CuiAppearance"]);
            await loader.WhenActionsIdleAsync();
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>Scoped resolution: the ACTUAL nearer image/child scope selects the folder even when the
    /// parent page scope differs, both initially and when only that child scope changes (Dark → Bright →
    /// Dark) with the parent untouched. Provider attempts are snapshot before each stimulus; the same
    /// captured target re-resolves and the current source follows the nearest scope.</summary>
    [AvaloniaFact]
    public void Nearer_child_scope_overrides_parent_for_brand_folder_selection()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var all = new[]
            {
                new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                    CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn)
            };
            var state = new HomeAppsViewState(all, HomeAppsViewState.Filter(all, string.Empty), string.Empty,
                "Select an app to open it in this tab.");
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright"; // parent page scope stays unchanged
            bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            var (root, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);

            var tile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-app-tile");
            var image = Assert.Single(tile.GetVisualDescendants().OfType<Image>());

            // Initial: parent Bright -> light request.
            Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/light/files.png", StringComparison.Ordinal));

            // Change ONLY the actual nearer image/child scope to Dark (parent untouched).
            var beforeDark = armed.Recording.Attempts.Count;
            image.Resources["CuiAppearance"] = "Dark";
            Dispatcher.UIThread.RunJobs();
            Assert.True(armed.Recording.Attempts.Count > beforeDark, "child-scope change must trigger a fresh request");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);

            // Back to Bright on the SAME captured target.
            var beforeBright = armed.Recording.Attempts.Count;
            image.Resources["CuiAppearance"] = "Bright";
            Dispatcher.UIThread.RunJobs();
            Assert.True(armed.Recording.Attempts.Count > beforeBright, "child-scope change back must trigger a fresh request");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/light/files.png", armed.LastAttempt);
            Assert.Same(image, Assert.Single(tile.GetVisualDescendants().OfType<Image>()));
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>Two simultaneous independent Home registry/binding scopes (each with its own captured
    /// coordinator). Retiring A never stops or cross-observes B: after A retires a genuinely NEW B
    /// target is created via real catalogue/filter/reconciliation and B still resolves its current scope.
    /// Owners are distinguished by distinct app stems (files vs picture), so count-only or same-URI
    /// fingerprints cannot mask cross-page invalidation.</summary>
    [AvaloniaFact]
    public void Two_independent_home_registry_scopes_do_not_cross_observe_or_retire()
    {
        Window? windowA = null;
        Window? windowB = null;
        CuiControlLoader? loaderA = null;
        CuiControlLoader? loaderB = null;
        HomeAppsCuiBindings bindingsA = null!;
        HomeAppsCuiBindings bindingsB = null!;
        using var armed = new ArmedImaging();
        try
        {
            var files = new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn);
            var picture = new HomeAppItem("imagine", "Picture", "Create and refine image concepts", "palette",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000008"), Source: ModeSource.BuiltIn);

            // Scope A: branded Files under Bright.
            var hostA = new ContentControl();
            hostA.Resources["CuiAppearance"] = "Bright";
            HomeAppsViewState stateA = new(new[] { files },
                HomeAppsViewState.Filter(new[] { files }, string.Empty), string.Empty, "...");
            bindingsA = new HomeAppsCuiBindings(() => stateA, (_, _, _) => ValueTask.CompletedTask);
            var (_, la) = RenderUnderHost(bindingsA, hostA, out var diagnosticsA, out windowA);
            loaderA = la;
            Assert.DoesNotContain(diagnosticsA, item => item.Severity == CuiDiagnosticSeverity.Error);

            // Scope B: empty launcher under Dark (no target yet).
            var hostB = new ContentControl();
            hostB.Resources["CuiAppearance"] = "Dark";
            HomeAppsViewState stateB = new([], [], string.Empty, "...");
            bindingsB = new HomeAppsCuiBindings(() => stateB, (_, _, _) => ValueTask.CompletedTask);
            var (_, lb) = RenderUnderHost(bindingsB, hostB, out var diagnosticsB, out windowB);
            loaderB = lb;
            Assert.DoesNotContain(diagnosticsB, item => item.Severity == CuiDiagnosticSeverity.Error);

            Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/light/files.png", StringComparison.Ordinal));

            // Retire A.
            bindingsA.RetireAppearanceObservation();
            var retiredA = armed.Recording.Attempts.Count;
            hostA.Resources["CuiAppearance"] = "SuperDark";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(retiredA, armed.Recording.Attempts.Count); // A retired: no callbacks

            // CREATE a genuinely NEW B target after A retired (real catalogue/reconciliation).
            stateB = new HomeAppsViewState(new[] { picture },
                HomeAppsViewState.Filter(new[] { picture }, string.Empty), string.Empty, "...");
            bindingsB.Refresh();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/dark/picture.png", StringComparison.Ordinal));

            // Stimulate B ONLY: change its nearest scope; prove B stays observed.
            var beforeB = armed.Recording.Attempts.Count;
            hostB.Resources["CuiAppearance"] = "Bright";
            Dispatcher.UIThread.RunJobs();
            Assert.True(armed.Recording.Attempts.Count > beforeB, "B must stay observed after A retires");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/light/picture.png", armed.LastAttempt);
            // No cross-page callbacks: A produced no files.png since its retirement.
            Assert.DoesNotContain(
                armed.Recording.Attempts.Skip(retiredA),
                uri => uri.EndsWith("/files.png", StringComparison.Ordinal));

            // Converse: retire B; both scopes quiet.
            bindingsB.RetireAppearanceObservation();
            var retiredB = armed.Recording.Attempts.Count;
            hostB.Resources["CuiAppearance"] = "SuperDark";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(retiredB, armed.Recording.Attempts.Count);
        }
        finally
        {
            bindingsA?.RetireAppearanceObservation(); bindingsB?.RetireAppearanceObservation();
            windowA?.Close(); loaderA?.Dispose();
            windowB?.Close(); loaderB?.Dispose();
        }
    }

    /// <summary>The SAME actual rendered target moves brand → supported rail generic → canonical generic →
    /// brand (a malformed token is also reconciled). Every branch reconciles, so stale brand ownership is
    /// dropped while generic — proven by counting ACTUAL typed binding invalidations (PropertyChanged) on
    /// a theme change, not just by the absence of a PNG request — and resumes when branded. The same
    /// tile/Image control is retained across the whole chain.</summary>
    [AvaloniaFact]
    public void Same_target_brand_to_rail_to_canonical_and_back_reconciles_ownership()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright";
            var brandFiles = new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn);
            HomeAppsViewState state = new(new[] { brandFiles },
                HomeAppsViewState.Filter(new[] { brandFiles }, string.Empty), string.Empty,
                "Select an app to open it in this tab.");
            bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            var invalidations = 0;
            bindings.PropertyChanged += (_, _) => invalidations++;
            var (root, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);

            var tile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-app-tile");
            var retainedImage = Assert.Single(tile.GetVisualDescendants().OfType<Image>());
            invalidations = 0; // baseline after initial render/attach

            // Phase 1: brand @ Bright -> light request; a theme change drives a real invalidation.
            Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/light/files.png", StringComparison.Ordinal));
            var brandBeforeTheme = invalidations;
            host.Resources["CuiAppearance"] = "Dark";
            Dispatcher.UIThread.RunJobs();
            Assert.True(invalidations > brandBeforeTheme, "brand theme change must drive typed binding invalidation");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);
            var brandAttempts = armed.Recording.Attempts.Count;

            // Phase 2: generic rail source (icon:folder) on the SAME target -> observation dropped.
            var railFiles = new HomeAppItem("files", "Files", "Private browser workspace", "folder");
            state = new HomeAppsViewState(new[] { railFiles },
                HomeAppsViewState.Filter(new[] { railFiles }, string.Empty), string.Empty, "...");
            invalidations = 0;
            bindings.Refresh();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(retainedImage, Assert.Single(tile.GetVisualDescendants().OfType<Image>()));
            Assert.DoesNotContain(armed.Recording.Attempts.Skip(brandAttempts),
                uri => uri.EndsWith("/files.png", StringComparison.Ordinal));
            var railRefreshInvalidations = invalidations;

            // While generic, a theme change must NOT resurrect a stale observer: no invalidation fires.
            host.Resources["CuiAppearance"] = "SuperDark"; // same dark folder, but any stale observer would fire
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(railRefreshInvalidations, invalidations);
            Assert.DoesNotContain(armed.Recording.Attempts.Skip(brandAttempts),
                uri => uri.EndsWith("/files.png", StringComparison.Ordinal));

            // Phase 3: canonical generic (icon:boards) on the SAME target.
            var canonicalFiles = new HomeAppItem("files", "Files", "Private browser workspace", "boards");
            state = new HomeAppsViewState(new[] { canonicalFiles },
                HomeAppsViewState.Filter(new[] { canonicalFiles }, string.Empty), string.Empty, "...");
            bindings.Refresh();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(retainedImage, Assert.Single(tile.GetVisualDescendants().OfType<Image>()));
            Assert.DoesNotContain(armed.Recording.Attempts.Skip(brandAttempts),
                uri => uri.EndsWith("/files.png", StringComparison.Ordinal));

            // Phase 4: back to brand on the SAME target -> ownership resumes, current (dark) scope.
            state = new HomeAppsViewState(new[] { brandFiles },
                HomeAppsViewState.Filter(new[] { brandFiles }, string.Empty), string.Empty, "...");
            bindings.Refresh();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(retainedImage, Assert.Single(tile.GetVisualDescendants().OfType<Image>()));
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);
            var liveBaseline = invalidations;
            host.Resources["CuiAppearance"] = "Bright";
            Dispatcher.UIThread.RunJobs();
            Assert.True(invalidations > liveBaseline, "revived brand must drive invalidation again");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/light/files.png", armed.LastAttempt);

            // Phase 5: a malformed brand token reconciles ownership away. It cannot be produced by the
            // strict SourceFor projection, so it is exercised through the same coordinator entry the
            // resolved resolver calls; after it, a theme change must be quiet.
            ((IHomeBrandObservationCoordinator)bindings).ReconcileTarget(retainedImage, "ownerbrand:files|folder|extra");
            var malformedBaseline = invalidations;
            var malformedAttempts = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "Dark";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(malformedBaseline, invalidations);
            Assert.Equal(malformedAttempts, armed.Recording.Attempts.Count);
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>An actual rendered brand Repeat target removed by filter (while the page stays alive)
    /// stops its observation: appearance changes emit no further brand request. Re-adding the target
    /// re-observes and resolves the CURRENT scope, and the lifecycle stays correct across repeated
    /// remove/re-attach cycles (bounded, no stale observers). Permanent retirement silences everything.</summary>
    [AvaloniaFact]
    public void Removed_brand_target_stops_observation_and_reattach_resumes_current_scope()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright";
            var files = new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn);
            var boards = new HomeAppItem("boards", "Boards", "Context workspace", "boards");
            HomeAppsViewState state = new(new[] { files, boards },
                HomeAppsViewState.Filter(new[] { files, boards }, string.Empty), string.Empty,
                "Select an app to open it in this tab.");
            bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            var (root, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/light/files.png", StringComparison.Ordinal));

            var all = new[] { files, boards };
            for (var cycle = 0; cycle < 3; cycle++)
            {
                // Remove the brand target by filtering it out; the page/registry/loader stays alive.
                state = new HomeAppsViewState(all, HomeAppsViewState.Filter(all, "boards"), "boards", "");
                bindings.Refresh();
                Dispatcher.UIThread.RunJobs();

                // Its subscription stopped: an appearance change no longer emits a files.png request.
                var afterRemoval = armed.Recording.Attempts.Count;
                host.Resources["CuiAppearance"] = "Dark";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(afterRemoval, armed.Recording.Attempts.Count);

                // Reattach/current-scope refresh: bring the brand back; it resolves the CURRENT scope.
                host.Resources["CuiAppearance"] = "Bright";
                state = new HomeAppsViewState(all, HomeAppsViewState.Filter(all, string.Empty), string.Empty, "...");
                bindings.Refresh();
                Dispatcher.UIThread.RunJobs();
                Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "home-app-tile" && AutomationProperties.GetName(button) == "Open Files");
                Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/light/files.png", StringComparison.Ordinal));

                // The revived target is live: a later appearance change refreshes it.
                var beforeLive = armed.Recording.Attempts.Count;
                host.Resources["CuiAppearance"] = "SuperDark";
                Dispatcher.UIThread.RunJobs();
                Assert.True(armed.Recording.Attempts.Count > beforeLive, $"cycle {cycle}: revived target must stay observed");
                Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);
            }

            // Permanent retirement: nothing afterwards, even on appearance change.
            bindings.RetireAppearanceObservation();
            var retired = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "Bright";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(retired, armed.Recording.Attempts.Count);
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>A removed actual Repeat target must be weakly releasable while the bindings/page stay
    /// alive: the strong fixture locals are dropped, GC is forced, and the removed target control is no
    /// longer alive. This is the owner-retention (not quiet-while-detached) half of the removal contract.</summary>
    [AvaloniaFact]
    public void Removed_brand_target_is_weakly_releasable_by_the_owner()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright";
            var files = new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn);
            var boards = new HomeAppItem("boards", "Boards", "Context workspace", "boards");
            // Mutable holder so the same closure the bindings subscribed to can be changed from inside
            // the removal helper without re-capturing a local.
            var state = new HomeAppsViewState[1];
            state[0] = new HomeAppsViewState(new[] { files, boards },
                HomeAppsViewState.Filter(new[] { files, boards }, string.Empty), string.Empty, "...");
            bindings = new HomeAppsCuiBindings(() => state[0], (_, _, _) => ValueTask.CompletedTask);
            var (root, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            Assert.Contains(armed.Recording.Attempts, uri => uri.EndsWith("/light/files.png", StringComparison.Ordinal));

            // Collect after the helper's frame has returned; collection inside the capture frame
            // can mistake a live JIT temporary for an owner leak. Keep the real page/bindings/loader
            // alive so the assertion still detects retained removed controls.
            var weakImage = CaptureRemovedImageWeakReference(bindings, root, state, files, boards);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(weakImage.IsAlive, "owner must not strongly retain a removed brand target");
            GC.KeepAlive(bindings);
            GC.KeepAlive(root);
            GC.KeepAlive(loader);
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>Negative control for the removal probe: an intentionally retaining owner must be
    /// detected after the same real Repeat removal and collection boundary. Releasing that owner
    /// must then allow collection while the page/bindings/loader remain alive.</summary>
    [AvaloniaFact]
    public void Removed_brand_retention_probe_detects_a_strong_owner()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright";
            var files = new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn);
            var boards = new HomeAppItem("boards", "Boards", "Context workspace", "boards");
            var state = new HomeAppsViewState[]
            {
                new(new[] { files, boards }, HomeAppsViewState.Filter(new[] { files, boards }, string.Empty),
                    string.Empty, "...")
            };
            bindings = new HomeAppsCuiBindings(() => state[0], (_, _, _) => ValueTask.CompletedTask);
            var (root, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            var retainingOwner = new List<Image>();
            var weakImage = CaptureRemovedImageWeakReference(bindings, root, state, files, boards, retainingOwner);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.True(weakImage.IsAlive, "the retention probe must detect a genuine strong owner");
            GC.KeepAlive(retainingOwner);

            retainingOwner.Clear();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.False(weakImage.IsAlive, "removing the strong owner must make the removed target collectable");
            GC.KeepAlive(bindings);
            GC.KeepAlive(root);
            GC.KeepAlive(loader);
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>First-attach appearance gap: an image whose effective nearest scope changes to B BEFORE
    /// its first visual attachment must be re-resolved for B at attach time. Load/wire the real authored
    /// document off-tree under A, change the scope to B, then attach; the actual target must emit a FRESH
    /// B request (dark) rather than keeping the earlier A-resolved image.</summary>
    [AvaloniaFact]
    public void First_attach_applies_current_scope_even_when_resolved_under_earlier_appearance()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright"; // pre-attach scope A
            var all = new[] { new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn) };
            HomeAppsViewState state = new(all, HomeAppsViewState.Filter(all, string.Empty), string.Empty, "...");
            bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

            // Load + wire WITHOUT attaching to a shown window yet.
            var registry = HomeAppsCuiDocument.CreateControlRegistry(bindings);
            var load = new CuiControlLoader(registry);
            loader = load;
            load.SetBindingContext(bindings);
            load.SetActionDispatcher(bindings);
            var document = HomeAppsCuiDocument.Parse();
            var loaded = load.TryLoad(document);
            Assert.DoesNotContain(loaded.Diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);
            var root = loaded.Root ?? throw new InvalidDataException("The Home apps document produced no surface.");
            load.WireBindings(root);
            host.Content = root;

            // Change the effective nearest scope to Dark BEFORE first visual attachment.
            host.Resources["CuiAppearance"] = "Dark";

            // Attach by showing the window; the actual target must re-resolve for the current scope.
            var beforeAttach = armed.Recording.Attempts.Count;
            window = new Window { Content = host, Width = 1280, Height = 900 };
            window.Show();
            window.UpdateLayout();
            root.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.True(armed.Recording.Attempts.Count > beforeAttach, "first attach must apply the current scope");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);
            var tile = Assert.Single(root.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-app-tile");
            Assert.IsType<DrawingImage>(Assert.Single(tile.GetVisualDescendants().OfType<Image>()).Source);
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>Reentrant refresh safety: source replacement and owner retirement that happen through the
    /// genuine typed binding invalidation path must never resurrect a subscription or corrupt registration.
    /// Theme changes (which only fire via a live actual-target observation) prove liveness and quietness.</summary>
    [AvaloniaFact]
    public void Reentrant_refresh_source_replacement_and_retirement_do_not_resurrect()
    {
        Window? window = null;
        CuiControlLoader? loader = null;
        HomeAppsCuiBindings bindings = null!;
        using var armed = new ArmedImaging();
        try
        {
            var host = new ContentControl();
            host.Resources["CuiAppearance"] = "Bright";
            var brandFiles = new HomeAppItem("files", "Files", "Private browser workspace", "folder",
                CanonicalId: Guid.Parse("a0000000-0000-0000-0000-000000000030"), Source: ModeSource.BuiltIn);
            var railFiles = new HomeAppItem("files", "Files", "Private browser workspace", "folder");
            var brandState = new HomeAppsViewState(new[] { brandFiles },
                HomeAppsViewState.Filter(new[] { brandFiles }, string.Empty), string.Empty, "...");
            var railState = new HomeAppsViewState(new[] { railFiles },
                HomeAppsViewState.Filter(new[] { railFiles }, string.Empty), string.Empty, "...");
            HomeAppsViewState state = brandState;
            bindings = new HomeAppsCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

            var invalidations = 0;
            var sourceSwapped = false;
            var retiredInCallback = false;
            var callbackStage = -1; // -1 = no transition armed
            bindings.PropertyChanged += (_, _) =>
            {
                invalidations++;
                if (callbackStage == 0 && !sourceSwapped)
                {
                    sourceSwapped = true;
                    state = railState; // mutates source INSIDE this genuine invalidation
                }
                else if (callbackStage == 1 && !retiredInCallback)
                {
                    retiredInCallback = true;
                    bindings.RetireAppearanceObservation(); // retires owner INSIDE this invalidation
                }
            };

            var (_, loadedLoader) = RenderUnderHost(bindings, host, out var diagnostics, out window);
            loader = loadedLoader;
            Assert.DoesNotContain(diagnostics, item => item.Severity == CuiDiagnosticSeverity.Error);

            // Live brand observation drives a genuine invalidation on a theme change.
            invalidations = 0;
            host.Resources["CuiAppearance"] = "Dark";
            Dispatcher.UIThread.RunJobs();
            Assert.True(invalidations > 0, "live brand must drive invalidation");
            Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/files.png", armed.LastAttempt);

            // STAGE 0: the NEXT real invalidation swaps source to rail FROM INSIDE the callback.
            callbackStage = 0;
            sourceSwapped = false;
            invalidations = 0;
            host.Resources["CuiAppearance"] = "SuperDark"; // still dark folder; the swap runs in the callback
            Dispatcher.UIThread.RunJobs();
            Assert.True(sourceSwapped, "source replacement must run inside the actual invalidation callback");
            Assert.True(invalidations > 0, "the swap must have been driven by a real invalidation");
            callbackStage = -1;

            // Materialize the already-mutated rail source; ownership must now be generic, so a later
            // theme change produces no invalidation and no brand request.
            bindings.Refresh();
            Dispatcher.UIThread.RunJobs();
            invalidations = 0;
            var afterSwapAttempts = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "Bright";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, invalidations);
            Assert.Equal(afterSwapAttempts, armed.Recording.Attempts.Count);

            // Revive the brand; the live observation resumes.
            state = brandState;
            bindings.Refresh();
            Dispatcher.UIThread.RunJobs();
            invalidations = 0;
            host.Resources["CuiAppearance"] = "Dark";
            Dispatcher.UIThread.RunJobs();
            Assert.True(invalidations > 0, "revived brand must drive invalidation");

            // STAGE 1: the NEXT real invalidation retires the owner FROM INSIDE the callback.
            callbackStage = 1;
            retiredInCallback = false;
            invalidations = 0;
            host.Resources["CuiAppearance"] = "SuperDark";
            Dispatcher.UIThread.RunJobs();
            Assert.True(retiredInCallback, "retirement must run inside the actual invalidation callback");
            Assert.True(invalidations > 0, "retirement must have been driven by a real invalidation");
            callbackStage = -1;

            // After retirement, later resource changes are quiet: no invalidation, no new attempt.
            invalidations = 0;
            var afterRetireAttempts = armed.Recording.Attempts.Count;
            host.Resources["CuiAppearance"] = "Bright";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, invalidations);
            Assert.Equal(afterRetireAttempts, armed.Recording.Attempts.Count);
        }
        finally { bindings?.RetireAppearanceObservation(); window?.Close(); loader?.Dispose(); }
    }

    /// <summary>Malformed brand tokens are rejected (not approximated): a baked-in folder segment, a
    /// missing key, an empty stem, extra segments and unknown stems all resolve to nothing so the
    /// composed chain continues honestly.</summary>
    [Fact]
    public void Resolve_rejects_malformed_brand_tokens()
    {
        var host = new ContentControl();
        host.Resources["CuiAppearance"] = "Bright";
        Assert.Null(HomeAppBrandingIcons.Resolve("ownerbrand:files|folder|light", host));
        Assert.Null(HomeAppBrandingIcons.Resolve("ownerbrand:files", host));
        Assert.Null(HomeAppBrandingIcons.Resolve("ownerbrand:|folder", host));
        Assert.Null(HomeAppBrandingIcons.Resolve("ownerbrand:files|folder|extra|segment", host));
        Assert.Null(HomeAppBrandingIcons.Resolve("ownerbrand:not-a-stem|folder", host));
        Assert.Null(HomeAppBrandingIcons.Resolve("icon:folder", host));
    }

    // ------------------------------------------------------------------------------------------
    // Bounded helper contracts: strict URI/stem/folder validation and undefined-appearance rejection.
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void ResourceUri_rejects_unknown_stems_traversal_and_arbitrary_shapes()
    {
        Assert.Throws<ArgumentException>(() => HomeAppBrandingIcons.ResourceUri("..", "light"));
        Assert.Throws<ArgumentException>(() => HomeAppBrandingIcons.ResourceUri("a/b", "light"));
        Assert.Throws<ArgumentException>(() => HomeAppBrandingIcons.ResourceUri("files", "blue"));
        Assert.Throws<ArgumentException>(() => HomeAppBrandingIcons.ResourceUri("not-a-stem", "dark"));
        Assert.Throws<ArgumentException>(() => HomeAppBrandingIcons.ResourceUri("files", "light/../../etc"));
        Assert.Throws<ArgumentException>(() => HomeAppBrandingIcons.ResourceUri("", "dark"));
        Assert.Equal("avares://Haven/Assets/OwnerAppIcons/light/files.png",
            HomeAppBrandingIcons.ResourceUri("files", "light"));
        Assert.Equal("avares://Haven/Assets/OwnerAppIcons/dark/ai-studio.png",
            HomeAppBrandingIcons.ResourceUri("ai-studio", "dark"));
    }

    [Fact]
    public void AppearanceFolder_rejects_undefined_enum_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HomeAppBrandingIcons.AppearanceFolder((CuiAppearance)999));
    }

    [Fact]
    public void DetectAppearance_rejects_undefined_or_numeric_resource_text()
    {
        var host = new ContentControl();
        host.Resources["CuiAppearance"] = "12";
        Assert.Throws<ArgumentException>(() => HomeAppBrandingIcons.DetectAppearance(host));
        host.Resources["CuiAppearance"] = "Crimson";
        Assert.Throws<ArgumentException>(() => HomeAppBrandingIcons.DetectAppearance(host));
        // All four modes resolve; undefined text is never silently mapped to light.
        foreach (var (text, expected) in new[]
                 {
                     ("SuperBright", CuiAppearance.SuperBright), ("Bright", CuiAppearance.Bright),
                     ("Dark", CuiAppearance.Dark), ("SuperDark", CuiAppearance.SuperDark)
                 })
        {
            host.Resources["CuiAppearance"] = text;
            Assert.Equal(expected, HomeAppBrandingIcons.DetectAppearance(host));
        }
    }
}
