using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            Assert.Contains(("home.open-app", (object?)"automations"), actions);
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
}
