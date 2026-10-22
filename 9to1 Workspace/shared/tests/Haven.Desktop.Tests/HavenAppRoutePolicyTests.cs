using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Tests;

public sealed class HavenAppRoutePolicyTests
{
    private static readonly string[] ExpectedBuiltInKeys =
    [
        "chat", "study", "automations", "terminal", "tasks", "studio", "dev", "browse", "plan", "training", "imagine", "canvas",
        "present", "data", "vision", "play", "translate", "launcher", "go", "dashboard", "write", "mesh", "spaces", "boards", "maps", "motion", "forms", "shelf", "assistants", "files"
    ];

    public static TheoryData<string, HavenAppRouteKind, HavenSurface> BuiltInRoutes => new()
    {
        { "chat", HavenAppRouteKind.BaseMode, HavenSurface.Chat },
        { "study", HavenAppRouteKind.BaseMode, HavenSurface.Study },
        { "automations", HavenAppRouteKind.Automations, HavenSurface.Automations },
        { "terminal", HavenAppRouteKind.Terminal, HavenSurface.Terminal },
        { "tasks", HavenAppRouteKind.BaseMode, HavenSurface.Tasks },
        { "studio", HavenAppRouteKind.BaseMode, HavenSurface.Studio },
        { "dev", HavenAppRouteKind.Dev, HavenSurface.Studio },
        { "browse", HavenAppRouteKind.Browse, HavenSurface.Browse },
        { "plan", HavenAppRouteKind.Plan, HavenSurface.Plan },
        { "training", HavenAppRouteKind.Training, HavenSurface.Training },
        { "imagine", HavenAppRouteKind.Imagine, HavenSurface.Imagine },
        { "write", HavenAppRouteKind.Write, HavenSurface.Write },
        { "canvas", HavenAppRouteKind.ModeWorkspace, HavenSurface.Canvas },
        { "present", HavenAppRouteKind.ModeWorkspace, HavenSurface.Present },
        { "data", HavenAppRouteKind.ModeWorkspace, HavenSurface.Data },
        { "vision", HavenAppRouteKind.Vision, HavenSurface.Vision },
        { "play", HavenAppRouteKind.Play, HavenSurface.Play },
        { "translate", HavenAppRouteKind.Translate, HavenSurface.Translate },
        { "launcher", HavenAppRouteKind.ModeWorkspace, HavenSurface.Launcher },
        { "go", HavenAppRouteKind.Go, HavenSurface.Go },
        { "dashboard", HavenAppRouteKind.Dashboard, HavenSurface.Dashboard },
        { "mesh", HavenAppRouteKind.Mesh, HavenSurface.Mesh },
        { "spaces", HavenAppRouteKind.Spaces, HavenSurface.Spaces },
        { "boards", HavenAppRouteKind.ModeWorkspace, HavenSurface.Boards },
        { "maps", HavenAppRouteKind.Maps, HavenSurface.Maps },
        { "motion", HavenAppRouteKind.ModeWorkspace, HavenSurface.Motion },
        { "forms", HavenAppRouteKind.Forms, HavenSurface.Forms },
        { "shelf", HavenAppRouteKind.BaseMode, HavenSurface.Chat },
        { "assistants", HavenAppRouteKind.Spaces, HavenSurface.Spaces },
        { "files", HavenAppRouteKind.Files, HavenSurface.Files }
    };

    [Theory]
    [MemberData(nameof(BuiltInRoutes))]
    public void EveryBuiltInAppHasTheExpectedConcreteRoute(
        string key,
        HavenAppRouteKind expectedKind,
        HavenSurface expectedSurface)
    {
        var app = Assert.Single(BuiltInModeSeed.Modes, item => item.Key == key);

        var route = HavenAppRoutePolicy.Resolve(app);

        Assert.Equal(expectedKind, route.Kind);
        Assert.Equal(expectedSurface, route.Surface);
    }

    [Fact]
    public void RouteMatrixCoversTheCompleteBuiltInInventory()
    {
        var registeredKeys = BuiltInModeSeed.Modes.Select(item => item.Key).Order().ToArray();

        Assert.Equal(ExpectedBuiltInKeys.Order(), registeredKeys);
    }

    [Fact]
    public void LegacyAssistantsIdentityResolvesToItsDestinationWithinSpaces()
    {
        var app = Assert.Single(BuiltInModeSeed.Modes, item => item.Key == "assistants");
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000029"), app.Id);
        Assert.Equal("Assistants", app.Name);
        Assert.Equal("user", app.IconKey);
        Assert.Equal(string.Empty, app.SystemPromptSuffix);
        Assert.Equal(20, (int)HavenAppRouteKind.Assistants);
        Assert.Equal(31, (int)HavenSurface.Assistants);
        Assert.Equal(new HavenAppRoute(HavenAppRouteKind.Spaces, HavenSurface.Spaces, "assistants"), HavenAppRoutePolicy.Resolve(app));
        Assert.True(HavenAppRoutePolicy.IsDeprecatedStandalone(app));
        // Canonical ownership (owner correction 2026-10-18 + roster amendment
        // 2026-10-21): folded identities stay out of the standalone catalogue.
        foreach (var absorbed in new[] { "dashboard", "study", "tasks", "translate",
            "browse", "chat", "go", "launcher", "mesh", "training" })
            Assert.True(HavenAppRoutePolicy.IsDeprecatedStandalone(BuiltInModeSeed.Modes.Single(item => item.Key == absorbed)),
                absorbed + " should be deprecated standalone");
        // Dev is the canonical Build owner now: exposed once as Build, not hidden.
        Assert.False(HavenAppRoutePolicy.IsDeprecatedStandalone(BuiltInModeSeed.Modes.Single(item => item.Key == "dev")),
            "dev should resolve to the canonical Build owner, not be hidden");
        // Canonical owning surfaces remain standalone.
        foreach (var owner in new[] { "spaces", "files" })
            Assert.False(HavenAppRoutePolicy.IsDeprecatedStandalone(BuiltInModeSeed.Modes.Single(item => item.Key == owner)),
                owner + " should remain standalone");
        Assert.Equal(BuiltInModeSeed.Modes.Count, BuiltInModeSeed.Modes.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public void UserAppsFallBackToTheirCompatibleBaseMode()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new ModeDefinition(
            Guid.NewGuid(), "my-study-app", "My Study App", "", "study",
            HavenMode.Study, "[]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);

        var route = HavenAppRoutePolicy.Resolve(app);

        Assert.Equal(HavenAppRouteKind.BaseMode, route.Kind);
        Assert.Equal(HavenSurface.Study, route.Surface);
    }

    [Fact]
    public void WebAliasUsesTheExistingBrowseRoute()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new ModeDefinition(
            Guid.NewGuid(), "web", "Web", "", "browse",
            HavenMode.Studio, "[]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);

        var route = HavenAppRoutePolicy.Resolve(app);

        Assert.Equal(HavenAppRouteKind.Browse, route.Kind);
        Assert.Equal(HavenSurface.Browse, route.Surface);
    }

    [Fact]
    public void SpreadsheetAliasUsesTheExistingDataWorkspaceRoute()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new ModeDefinition(
            Guid.NewGuid(), "data-spreadsheet", "Data Spreadsheet", "", "data-spreadsheet",
            HavenMode.Studio, "[]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);

        var route = HavenAppRoutePolicy.Resolve(app);

        Assert.Equal(HavenAppRouteKind.ModeWorkspace, route.Kind);
        Assert.Equal(HavenSurface.Data, route.Surface);
    }
    [Fact]
    public void DatabaseAliasUsesTheExistingDataWorkspaceRoute()
    {
        var now = DateTimeOffset.UtcNow;
        var app = new ModeDefinition(
            Guid.NewGuid(), "data-database", "Data Database", "", "data-database",
            HavenMode.Studio, "[]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);

        var route = HavenAppRoutePolicy.Resolve(app);

        Assert.Equal(HavenAppRouteKind.ModeWorkspace, route.Kind);
        Assert.Equal(HavenSurface.Data, route.Surface);
    }

    // The exact production projection consumed by UnifiedHomePage (see
    // UnifiedHomePage.SelectAsync): enabled modes minus the deprecated-standalone set,
    // ordered by stored name.
    private static (string Key, string Name, Guid Id)[] ProjectVisibleCatalogue() =>
        BuiltInModeSeed.Modes
            .Where(mode => mode.IsEnabled && !HavenAppRoutePolicy.IsDeprecatedStandalone(mode))
            .OrderBy(mode => mode.Name, StringComparer.OrdinalIgnoreCase)
            .Select(mode => (mode.Key, mode.Name, mode.Id))
            .ToArray();

    [Fact]
    public void BuildAppearsExactlyOnceProjectedFromTheRealSeed()
    {
        var visible = ProjectVisibleCatalogue();

        // Dev's canonical Build identity is exposed once, named "Build", with the real GUID.
        var buildTiles = visible.Where(entry => entry.Name == "Build").ToArray();
        var build = Assert.Single(buildTiles);
        Assert.Equal("dev", build.Key);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000028"), build.Id);
        Assert.Single(BuiltInModeSeed.Modes, mode => mode.Key == "dev");

        // Studio004 is a distinct identity, shown separately as AI Studio, never a Build alias.
        var studioTile = Assert.Single(visible, entry => entry.Name == "AI Studio");
        Assert.Equal("studio", studioTile.Key);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000004"), studioTile.Id);
        Assert.NotEqual(studioTile.Id, build.Id);
    }

    [Theory]
    [InlineData("browse")]
    [InlineData("chat")]
    [InlineData("go")]
    [InlineData("launcher")]
    [InlineData("mesh")]
    [InlineData("training")]
    public void OwnerRemovalKeysStayOutOfTheVisibleCatalogueButKeepTheirRealIdentity(string key)
    {
        // The real record is preserved with its original stable id.
        var mode = Assert.Single(BuiltInModeSeed.Modes, item => item.Key == key);
        Assert.NotEqual(Guid.Empty, mode.Id);

        // The exact six owner-removal identities no longer project as standalone tiles.
        Assert.DoesNotContain(ProjectVisibleCatalogue(), entry => entry.Key == key);

        // Compatibility routing survives so pins/usage/saved links keep resolving.
        var route = HavenAppRoutePolicy.Resolve(mode);
        Assert.True(Enum.IsDefined(route.Kind));
        Assert.True(Enum.IsDefined(route.Surface));
    }

    [Theory]
    [InlineData("imagine", "Picture", "a0000000-0000-0000-0000-000000000008")]
    [InlineData("studio", "AI Studio", "a0000000-0000-0000-0000-000000000004")]
    [InlineData("vision", "Voice & Vision", "a0000000-0000-0000-0000-000000000011")]
    [InlineData("dev", "Build", "a0000000-0000-0000-0000-000000000028")]
    public void RenamedProductLabelsPreserveOriginalKeysAndGuids(string key, string label, string guid)
    {
        var mode = Assert.Single(BuiltInModeSeed.Modes, item => item.Key == key);
        Assert.Equal(Guid.Parse(guid), mode.Id);
        Assert.Equal(label, mode.Name);
        Assert.Equal(key, mode.Key);
    }

    [Fact]
    public void BuildRegisteredThroughTheProductionRegistrySeamUsesDevGuidAndDevOwner()
    {
        var buildFactory = new RecordingBrowseFactory("build");
        var registrations = NativeAppContentRegistry.NativeAppContentRegistrations.Build(
                new RecordingBrowseFactory("files"),
                new RecordingBrowseFactory("spaces"),
                buildFactory)
            .ToArray();

        // Exactly one Build/Dev registration bound to the real dev GUID and Dev route kind.
        var devBindings = registrations.Where(binding => binding.RouteKind == HavenAppRouteKind.Dev).ToArray();
        var dev = Assert.Single(devBindings);
        Assert.Equal(Guid.Parse("a0000000-0000-0000-0000-000000000028"), dev.AppId);
        Assert.Equal("dev", dev.AppKey);
        Assert.Same(buildFactory, dev.Factory);
        Assert.Single(registrations, binding => binding.AppKey == "dev");

        // Studio004 is never bound as a Build alias; no other registration claims Dev.
        Assert.DoesNotContain(registrations, binding => binding.AppKey == "studio");
        Assert.DoesNotContain(registrations,
            binding => binding.AppId == Guid.Parse("a0000000-0000-0000-0000-000000000004")
                && binding.RouteKind == HavenAppRouteKind.Dev);
    }

    [Fact]
    public void HomeLaunchResolverRoutesDevToTheRegisteredBuildAddress()
    {
        var address = HomeLaunchRouteResolver.Resolve("dev");
        Assert.Equal(AppContentNamespace.NativeApp, address.Namespace);
        Assert.Equal("dev", address.Authority);

        // Chat keeps its canonical Spaces special-case; a plain non-Chat identity keeps 9to1://key.
        var chat = HomeLaunchRouteResolver.Resolve("chat");
        Assert.Equal("spaces", chat.Authority);
    }

    [Fact]
    public void CustomUserEnabledAppsProjectNormallyAndDisabledRecordsDoNot()
    {
        var now = DateTimeOffset.UtcNow;
        var userApp = new ModeDefinition(
            Guid.NewGuid(), "my-app", "My App", "", "general",
            HavenMode.Chat, "[]", "[]", "[]", "[]", "",
            ModeSource.Created, ModeInstallState.InstalledByUser, "User", "1.0.0", "[]", now, now);

        var projected = BuiltInModeSeed.Modes.Concat(new[] { userApp })
            .Where(mode => mode.IsEnabled && !HavenAppRoutePolicy.IsDeprecatedStandalone(mode))
            .Select(mode => mode.Id)
            .ToArray();
        Assert.Contains(userApp.Id, projected);

        var disabled = userApp with { IsEnabled = false };
        var projectedAfterDisable = BuiltInModeSeed.Modes.Concat(new[] { disabled })
            .Where(mode => mode.IsEnabled && !HavenAppRoutePolicy.IsDeprecatedStandalone(mode))
            .Select(mode => mode.Id)
            .ToArray();
        Assert.DoesNotContain(disabled.Id, projectedAfterDisable);
    }

    /// <summary>Deterministic factory used only behind the REAL production registry binding
    /// (<see cref="NativeAppContentRegistry.NativeAppContentRegistrations.Build"/>); it never creates a session.</summary>
    private sealed class RecordingBrowseFactory : IBrowseAppContentFactory
    {
        private readonly string _label;
        public RecordingBrowseFactory(string label) => _label = label;
        public string Label => _label;
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request) =>
            throw new NotSupportedException("Registry-seam tests never create sessions.");
    }
}
