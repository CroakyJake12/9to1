using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Automation;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;
namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class GoHomeLayoutNativeTests
{
    [Fact]
    public async Task DashboardShowsActualSectionsGridThenKeyboardSwitchesCompactAndSearchWithAccessibleNames()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); var original = await f.Configuration.GetAsync();
            var candidate = DesktopPageEdits.PinApplication(original.Stored.Current, f.AppId, "Saved pin label");
            candidate = GoHomeEdits.Configure(candidate, candidate.EffectiveGoHome with { Layout = GoHomeLayout.Dashboard });
            var preview = await f.Configuration.PreviewAsync(original.Stored, candidate, TimeSpan.FromMinutes(1)); await f.Configuration.KeepAsync(preview.Preview!.Id);
            using var model = new ShellViewModel(); var provider = new Provider(f.AppId);
            await model.StartAsync(f.Configuration, new GoService([provider]), new LinuxApplicationLauncher(new EmptyRegistry(), f.Resources, f.Actors), default);
            using var loader = new CuiControlLoader(TaskbarLayerSurface.CreateRegistry(model)); loader.SetBindingContext(model); loader.SetActionDispatcher(model);
            using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui"); using var reader = new StreamReader(stream!);
            var (root, diagnostics) = loader.LoadMarkup(reader.ReadToEnd()); Assert.NotNull(root); Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(root!); var window = new Window { Content = root }; window.Show();
            try
            {
                await WaitAsync(() => Groups(model).Sum(g => g.Results.Count) == 2); window.UpdateLayout();
                Assert.Equal("Current pinned owner label", Assert.Single(Groups(model).Single(g => g.Title == "Pinned").Results).Label);
                Assert.Equal("All Apps owner label", Assert.Single(Groups(model).Single(g => g.Title == "All Apps").Results).Label);
                Assert.Contains(Groups(model), g => g.Availability == "Recent unavailable" && g.Results.Count == 0);
                Assert.Contains(Groups(model), g => g.Availability == "Suggested unavailable" && g.Results.Count == 0);
                var grid = Assert.Single(Traverse(root!).OfType<Grid>(), g => g.Name == "go-home-groups"); Assert.Equal(2, grid.ColumnDefinitions.Count);
                Assert.Contains(grid.Children, c => Grid.GetColumn(c) == 1 && Grid.GetRow(c) == 0);
                Assert.Equal("All Apps", AutomationProperties.GetName(Button(root!, "All Apps")));
                var compact = Button(root!, "Compact search layout"); Assert.True(compact.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
                await WaitAsync(() => model.TryGetValue("GoLayout", out var v) && Equals(v, "CompactSearch"));
                await WaitAsync(() => model.TryGetValue("ShowLinearGoResults", out var v) && Equals(v, true));
                var input = Assert.IsType<GoSearchInput>(Traverse(root!).OfType<TextBox>().Single(t => t.Name == "go-query"));
                Assert.Equal("Go search", AutomationProperties.GetName(input)); input.Text = "canonical query"; input.Focus();
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter"); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                await WaitAsync(() => Rows(model).Any(r => r.Label == "All Apps owner label")); Assert.Empty(Groups(model));
                var open = Button(root!, "All Apps owner label"); open.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await provider.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(f.Actors.Current, provider.ExpectedActor);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None));
    }
    [Fact]
    public async Task NamedGroupUsesActualBothOwnersThenSuspendedOwnerActorChangeClearsEveryGroup()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); var original = await f.Configuration.GetAsync();
            var candidate = DesktopPageEdits.PinApplication(original.Stored.Current, f.AppId, "Saved label");
            foreach (var kind in new[] { GoHomeSectionKind.Pinned, GoHomeSectionKind.AllApps }) candidate = GoHomeEdits.Present(candidate, kind, true, GoHomeSectionSize.Standard, "Work");
            candidate = GoHomeEdits.Present(candidate, GoHomeSectionKind.Suggested, false, GoHomeSectionSize.Standard, null);
            candidate = GoHomeEdits.Configure(candidate, candidate.EffectiveGoHome with { Layout = GoHomeLayout.Dashboard });
            var preview = await f.Configuration.PreviewAsync(original.Stored, candidate, TimeSpan.FromMinutes(1)); await f.Configuration.KeepAsync(preview.Preview!.Id);
            using var model = new ShellViewModel(); var provider = new Provider(f.AppId);
            await model.StartAsync(f.Configuration, new GoService([provider]), new LinuxApplicationLauncher(new EmptyRegistry(), f.Resources, f.Actors), default);
            await WaitAsync(() => provider.Queries == 1 && Rows(model).Count == 2 && Groups(model).Any(g => g.Title == "Work · Pinned, All Apps"));
            var group = Groups(model).Single(g => g.Title == "Work · Pinned, All Apps"); Assert.Single(group.Results);
            Assert.DoesNotContain(Groups(model), g => g.Title.Contains("Suggested"));
            provider.SuspendQuery = true;
            var pending = model.DispatchAsync("Search", null).AsTask(); await provider.QueryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement" }; provider.QueryRelease.TrySetResult(); await pending;
            Assert.Empty(Groups(model)); Assert.Empty(Rows(model)); Assert.Null(provider.ExpectedActor);
            return true;
        }, CancellationToken.None));
    }
    [Fact]
    public async Task QueuedOldLayoutClickCannotReplaceASectionPreviewThatCompletesFirst()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); using var model = new ShellViewModel();
            await model.StartAsync(f.Configuration, new GoService([]), new LinuxApplicationLauncher(new EmptyRegistry(), f.Resources, f.Actors), default);
            await model.DispatchAsync("GoHome.Pinned", null); var pinned = Settings(model).Single(s => s.Kind == GoHomeSectionKind.Pinned);
            f.Actors.SuspendNext = true; var first = model.DispatchAsync("GoSectionVisibility", pinned).AsTask();
            await f.Actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); var queued = model.DispatchAsync("GoLayout.Dashboard", null).AsTask();
            f.Actors.Release.TrySetResult(); await first; await queued;
            var current = await f.Configuration.GetAsync(); Assert.NotNull(current.Preview);
            Assert.False(current.Effective.EffectiveGoHome.Sections.Single(s => s.Kind == GoHomeSectionKind.Pinned).Visible);
            Assert.Equal(GoHomeLayout.StartMenu, current.Effective.EffectiveGoHome.Layout);
            Assert.True(current.Stored.Current.EffectiveGoHome.Sections.Single(s => s.Kind == GoHomeSectionKind.Pinned).Visible);
            return true;
        }, CancellationToken.None));
    }
    private static IReadOnlyList<GoHomeResultGroup> Groups(ShellViewModel m)
    { Assert.True(m.TryGetValue("GoResultGroups", out var v)); return Assert.IsAssignableFrom<IEnumerable<GoHomeResultGroup>>(v).ToArray(); }
    private static IReadOnlyList<GoHomeSection> Settings(ShellViewModel m) { Assert.True(m.TryGetValue("GoHomeSettings", out var v)); return Assert.IsAssignableFrom<IEnumerable<GoHomeSection>>(v).ToArray(); }
    private static IReadOnlyList<GoHomeSection> Visible(ShellViewModel m) { Assert.True(m.TryGetValue("GoVisibleSections", out var v)); return Assert.IsAssignableFrom<IEnumerable<GoHomeSection>>(v).ToArray(); }
    private static IReadOnlyList<GoResult> Rows(ShellViewModel model)
    { Assert.True(model.TryGetValue("Results", out var value)); return Assert.IsAssignableFrom<IEnumerable<GoResult>>(value).ToArray(); }
    private static async Task WaitAsync(Func<bool> ready)
    { var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5); while (!ready()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Go home did not publish its owning UI state."); await Task.Delay(1); } }
    private static Button Button(Control root, string caption) => Assert.Single(Traverse(root).OfType<Button>(), x => Equals(x.Content, caption));
    private static IEnumerable<Control> Traverse(Control root)
    { yield return root; foreach (var child in root.GetLogicalChildren().OfType<Control>()) foreach (var nested in Traverse(child)) yield return nested; }
    // Controlled owner dispatch only, not installed transport/navigation admission proof.
    private sealed class Provider(Guid appId) : IGoCanonicalResolver, IGoOriginalActorInvocation
    {
        public string ProviderId => "os.installed-applications"; public int Queries;
        public bool SuspendQuery; public TaskCompletionSource QueryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource QueryRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AuthenticatedResourceActor? ExpectedActor; public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GoResult Result(string label) => new(ProviderId, new("Home", "os.installed-application", appId.ToString("D"), "7"), label, "Apps", [new("Open", "Open")]);
        public Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct) => Task.FromResult<GoResult?>(locator.Id == appId.ToString("D") ? Result("Current pinned owner label") : null);
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask; ct.ThrowIfCancellationRequested(); Queries++; if (SuspendQuery) { QueryEntered.TrySetResult(); await QueryRelease.Task.WaitAsync(ct); } yield return Result("All Apps owner label"); }
        public Task InvokeAsync(GoCanonicalReference reference, string action, CancellationToken ct) => throw new InvalidOperationException("No current-only fallback.");
        public Task InvokeForActorAsync(GoCanonicalReference reference, string action, AuthenticatedResourceActor actor, CancellationToken ct)
        { ExpectedActor = actor; Invoked.TrySetResult(); return Task.CompletedTask; }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public bool SuspendNext;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct)
        { if (!SuspendNext) return ValueTask.FromResult<AuthenticatedResourceActor?>(Current); SuspendNext = false; return PauseAsync(ct); }
        private async ValueTask<AuthenticatedResourceActor?> PauseAsync(CancellationToken ct)
        { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); return Current; }
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => ValueTask.FromResult(Current == expected);
    }
    private sealed class EmptyRegistry : IInstalledApplicationRegistry
    {
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<InstalledApplicationReference>>([]);
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) => ValueTask.FromResult<InstalledApplicationReference?>(null);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "astra-go-home-native-" + Guid.NewGuid());
        public Guid AppId { get; } = Guid.NewGuid(); public Actors Actors { get; } = new();
        public ShellConfigurationService Configuration { get; } public ResourceAuthorizationService Resources { get; }
        public Fixture()
        { var home = new FileHomeCoreStateStore(Path.Combine(directory, "home.json")); Resources = new(Actors, [new ShellConfigurationResourceResolver(home)]); Configuration = new(new HomeShellConfigurationStore(home, Actors, Resources)); }
        public ShellConfigurationService Reopen()
        { var home = new FileHomeCoreStateStore(Path.Combine(directory, "home.json")); return new(new HomeShellConfigurationStore(home, Actors, new ResourceAuthorizationService(Actors, [new ShellConfigurationResourceResolver(home)]))); }
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
