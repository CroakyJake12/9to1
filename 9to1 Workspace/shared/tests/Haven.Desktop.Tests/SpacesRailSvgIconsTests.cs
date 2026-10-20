using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Spaces;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Home;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// Source-level and rendered proof for the authored Spaces rail's filled, rounded, packaged-SVG
/// icon family. These tests run against the ACTUAL <c>SpacesHome.cui</c> document, the real
/// <see cref="SpacesHomeCuiBindings"/> projection and a control registry registered through
/// <see cref="SpacesRailPackagedIcons.Register"/> so the packaged assets are wired into the same
/// registry path the desktop host uses. Nothing here replaces acceptance: the tests are authored
/// and the .NET/native runner is dispatched by root.
/// </summary>
public sealed class SpacesRailSvgIconsTests
{
    private static readonly string[] RailAssets =
    [
        "spaces-rail-plus", "spaces-rail-settings", "spaces-rail-chevron-right",
        "spaces-rail-home", "spaces-rail-chat", "spaces-rail-book", "spaces-rail-tasks",
        "spaces-rail-agents", "spaces-rail-cart", "spaces-rail-search", "spaces-rail-translate",
        "spaces-rail-experiences", "spaces-rail-unknown",
    ];

    [AvaloniaFact]
    public void Every_rail_asset_is_a_strict_filled_rounded_svg_resolving_to_an_image()
    {
        foreach (var asset in RailAssets)
        {
            var uri = SpacesRailPackagedIcons.AssetUriPrefix + asset + ".svg";
            using var mounted = NewTarget();
            var image = ResolvePackaged(uri, mounted.Target);
            Assert.NotNull(image);
            var group = GroupOf(image!);
            Assert.True(group.Children.Count > 0, $"{asset} paints no geometry.");
        }
    }

    [AvaloniaFact]
    public void Rail_icon_keys_resolve_to_distinct_packaged_svg()
    {
        using var mounted = NewTarget();
        var target = mounted.Target;
        string[] keys = ["plus", "settings", "chevron-right", "home", "chat", "book", "tasks",
            "agents", "cart", "search", "translate", "experiences", "unknown"];
        foreach (var key in keys)
        {
            var image = SpacesRailPackagedIcons.Resolve("icon:" + key, target);
            Assert.NotNull(image);
            Assert.IsType<DrawingImage>(image);
            Assert.Equal(new Rect(0, 0, 24, 24), ((DrawingImage)image!).Viewbox);
        }

        // Distinct authorship: plus is two rounded bars, home is three painted pieces.
        Assert.Equal(2, GroupOf(SpacesRailPackagedIcons.Resolve("icon:plus", target)!).Children.Count);
        Assert.Equal(3, GroupOf(SpacesRailPackagedIcons.Resolve("icon:home", target)!).Children.Count);
        var chat = GroupOf(SpacesRailPackagedIcons.Resolve("icon:chat", target)!).Bounds;
        var book = GroupOf(SpacesRailPackagedIcons.Resolve("icon:book", target)!).Bounds;
        Assert.NotEqual(chat, book);
    }

    [AvaloniaFact]
    public void Custom_extension_and_non_rail_keys_fall_through_to_the_shared_catalog()
    {
        using var mounted = NewTarget();
        var target = mounted.Target;
        // Genuine custom/extension keys and non-rail glyphs are NOT swallowed by the rail mapper.
        Assert.Null(SpacesRailPackagedIcons.Resolve("icon:folder", target));
        Assert.Null(SpacesRailPackagedIcons.Resolve("icon:more", target));
        Assert.Null(SpacesRailPackagedIcons.Resolve("icon:chevron-left", target));
        Assert.Null(SpacesRailPackagedIcons.Resolve("icon:sparkles", target));
        // The bound malformed-custom fallback still resolves to the filled rounded SVG.
        Assert.NotNull(SpacesRailPackagedIcons.Resolve("icon:unknown", target));
    }

    [AvaloniaFact]
    public void Rail_icon_semantic_foreground_rethemes_with_the_live_theme()
    {
        var bright = ResolveForeground(CuiAppearance.Bright);
        var dark = ResolveForeground(CuiAppearance.Dark);
        Assert.NotEqual(bright, dark);
        Assert.True(bright.A != 0 && dark.A != 0);
    }

    [AvaloniaFact]
    public void Unsupported_svg_semantics_fail_loudly_rather_than_render_partially()
    {
        // The outline asset uses stroke/fill="none", which the strict decoder must reject loudly.
        var outlineUri = "avares://" + typeof(SpacesRailPackagedIcons).Assembly.GetName().Name + "/Assets/home.svg";
        using var mounted = NewTarget();
        var target = mounted.Target;
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = HavenPackagedSvgResolver.Resolve(outlineUri, target);
        });
        Assert.Contains("fill", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Authored_spaces_home_renders_the_rail_with_registered_svg_and_preserved_actions()
    {
        var state = StateWithSelection(NewSelection());
        var actions = new List<(string Command, object? Parameter)>();
        var bindings = new SpacesHomeCuiBindings(
            () => state,
            (command, parameter, _) => { actions.Add((command, parameter)); return ValueTask.CompletedTask; });

        using var loader = new CuiControlLoader(BuildRegistry());
        loader.SetBindingContext(bindings);
        loader.SetActionDispatcher(bindings);
        Window? window = null;
        try
        {
            var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
            Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Control>(tree);
            loader.WireBindings(root);

            window = new Window { Content = root, Width = 1280, Height = 900 };
            window.Show();
            for (var pass = 0; pass < 3; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }

            var rail = Assert.Single(root.GetVisualDescendants().OfType<Control>(), c => c.Name == "spaces-sidebar");
            Assert.Equal(70, rail.Bounds.Width);

            var createIcon = RailImage(rail, "create-space-icon");
            Assert.NotNull(GroupOf(createIcon.Source!));

            // Built-in Space icons resolve to distinct packaged SVG drawings placed inside their buttons.
            var firstParty = rail.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Name == "first-party-space-open").ToArray();
            Assert.Equal(2, firstParty.Length);
            var chatImage = Assert.Single(firstParty[0].GetVisualDescendants().OfType<Image>());
            var bookImage = Assert.Single(firstParty[1].GetVisualDescendants().OfType<Image>());
            Assert.NotEqual(GroupOf(chatImage.Source!).Bounds, GroupOf(bookImage.Source!).Bounds);
            Assert.True(ImageViewportInsideButton(chatImage, firstParty[0]));

            // Genuine custom icon keeps its own catalog identity; malformed custom uses the SVG fallback.
            var custom = rail.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Name == "open-custom-space").ToArray();
            Assert.Equal(2, custom.Length);
            var folderImage = Assert.Single(custom[0].GetVisualDescendants().OfType<Image>());
            var brokenImage = Assert.Single(custom[1].GetVisualDescendants().OfType<Image>());
            Assert.NotNull(GroupOf(folderImage.Source!));
            Assert.NotNull(GroupOf(brokenImage.Source!));
            Assert.True(ImageViewportInsideButton(brokenImage, custom[1]));

            // Bound selected state and preserved navigation dispatch.
            Assert.True(CuiPropertyPresentation.For(firstParty[0]).Resolve("Selected").Value as bool? == true);
            Assert.False(CuiPropertyPresentation.For(firstParty[1]).Resolve("Selected").Value as bool? == true);
            actions.Clear();
            var createButton = Assert.Single(rail.GetVisualDescendants().OfType<Button>(), b => b.Name == "create-space");
            createButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await loader.WhenActionsIdleAsync();
            Assert.Equal(("spaces.create", (object?)null), Assert.Single(actions));

            // Accessible names are unchanged by the icon swap.
            Assert.Equal(new[] { "Open Chat", "Open Study", "Open Recipes", "Open Broken" },
                firstParty.Concat(custom).Select(AutomationProperties.GetName));
        }
        finally { window?.Close(); loader.Dispose(); }
    }

    [AvaloniaFact]
    public void Composite_registry_keeps_catalog_fallback_for_non_rail_glyphs()
    {
        // The overflow "more" and collapse "chevron-left" glyphs are outside the rail family; they
        // must still resolve through the shared catalog in the SAME composite registry.
        var state = StateWithSelection(NewSelection());
        var bindings = new SpacesHomeCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
        using var loader = new CuiControlLoader(BuildRegistry());
        loader.SetBindingContext(bindings);
        loader.SetActionDispatcher(bindings);
        Window? window = null;
        try
        {
            var (tree, diagnostics) = loader.LoadMarkup(ReadAuthoredSource());
            Assert.Empty(diagnostics);
            var root = Assert.IsAssignableFrom<Control>(tree);
            loader.WireBindings(root);
            window = new Window { Content = root, Width = 1280, Height = 900 };
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();

            foreach (var id in new[] { "space-overflow-icon", "space-sidebar-collapse-icon" })
            {
                var image = Assert.Single(root.GetVisualDescendants().OfType<Image>(), i => i.Name == id);
                Assert.NotNull(image.Source);
                Assert.IsType<DrawingImage>(image.Source);
            }
        }
        finally { window?.Close(); loader.Dispose(); }
    }

    // ---- helpers ----

    private static CuiControlRegistry BuildRegistry()
    {
        var registry = new CuiControlRegistry();
        SpacesRailPackagedIcons.Register(registry);
        return registry;
    }

    private static SpacesHomeViewState StateWithSelection(SpacesHomeSelection selection) =>
        SpacesHomeViewState.Empty with
        {
            BuiltIns =
            [
                new SpacesHomeItem(SpaceRegistry.ChatSpaceId, "Chat", "General chat", "spaces.chat", true, null, 0, 1, false) { IconKey = "chat" },
                new SpacesHomeItem(SpaceRegistry.StudySpaceId, "Study", "Study space", "spaces.study", true, null, 1, 1, false) { IconKey = "book" },
            ],
            CustomSpaces =
            [
                new SpacesHomeItem(Guid.NewGuid(), "Recipes", "Dinner plans", "spaces.custom.1", false, null, 0, 1, true) { IconKey = "folder" },
                new SpacesHomeItem(Guid.NewGuid(), "Broken", "Malformed icon", "spaces.custom.2", false, null, 1, 2, true) { IconKey = "not/a/url" },
            ],
            Selection = selection,
        };

    private static SpacesHomeSelection NewSelection() => new(
        SpaceRegistry.ChatSpaceId, "Chat", "General chat", true, false, false, null,
        "Default", "Ask", "Conversation", [], [], false, false, false);

    private static string ReadAuthoredSource()
    {
        var assembly = typeof(SpacesHomeCuiBindings).Assembly;
        using var stream = assembly.GetManifestResourceStream(SpacesHomeCuiDocument.ResourceName)
            ?? throw new InvalidOperationException($"'{SpacesHomeCuiDocument.ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IImage? ResolvePackaged(string uri, Control target) =>
        HavenPackagedSvgResolver.TryResolve(uri, target, out var image) ? image : null;

    private static MountedTarget NewTarget() => new();

    private sealed class MountedTarget : IDisposable
    {
        private readonly Window _window;
        public Control Target { get; }

        public MountedTarget()
        {
            Target = new Border();
            _window = new Window { Content = Target };
            try
            {
                _window.Show();
                _window.UpdateLayout();
            }
            catch
            {
                _window.Close();
                throw;
            }
        }

        public void Dispose() => _window.Close();
    }

    private static DrawingGroup GroupOf(IImage image)
    {
        var drawing = Assert.IsType<DrawingImage>(image);
        Assert.Equal(new Rect(0, 0, 24, 24), drawing.Viewbox);
        var group = Assert.IsType<DrawingGroup>(drawing.Drawing);
        Assert.True(group.Children.Count > 0);
        return group;
    }

    private static Image RailImage(Control rail, string name) =>
        Assert.Single(rail.GetVisualDescendants().OfType<Image>(), i => i.Name == name);

    private static bool ImageViewportInsideButton(Image image, Button button)
    {
        var imageOrigin = image.TranslatePoint(default, button);
        if (imageOrigin is not { } origin) return false;
        if (image.Bounds.Width <= 0 || image.Bounds.Height <= 0) return false;
        return origin.X >= 0 && origin.Y >= 0
            && origin.X + image.Bounds.Width <= button.Bounds.Width + 0.5
            && origin.Y + image.Bounds.Height <= button.Bounds.Height + 0.5;
    }

    private static Color ResolveForeground(CuiAppearance appearance)
    {
        var previousAppearance = CuiThemeScopeApplier.DetectAppearance();
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        try
        {
            CuiThemeScopeApplier.ApplyGlobalTheme(CuiTheme.Professional, appearanceOverride: appearance);
            using var mounted = NewTarget();
            var target = mounted.Target;
            var image = SpacesRailPackagedIcons.Resolve("icon:plus", target);
            var group = GroupOf(image!);
            var drawing = Assert.IsType<GeometryDrawing>(group.Children[0]);
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(drawing.Brush);
            return brush.Color;
        }
        finally
        {
            CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previousAppearance);
        }
    }
}
