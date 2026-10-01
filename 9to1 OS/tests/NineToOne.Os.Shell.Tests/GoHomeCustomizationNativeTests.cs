using System.Runtime.CompilerServices;
using Avalonia.Controls;
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
public sealed class GoHomeCustomizationNativeTests
{
    [Fact]
    public async Task ActualButtonsPreviewHideResizeGroupLayoutThenKeepPersistsOnReopen()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); using var model = new ShellViewModel();
            await model.StartAsync(f.Configuration, new GoService([new Provider(f.AppId)]), new LinuxApplicationLauncher(new EmptyRegistry(), f.Resources, f.Actors), default);
            using var loader = new CuiControlLoader(TaskbarLayerSurface.CreateRegistry(model)); loader.SetBindingContext(model); loader.SetActionDispatcher(model);
            using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui");
            using var reader = new StreamReader(stream!); var (root, diagnostics) = loader.LoadMarkup(reader.ReadToEnd());
            Assert.NotNull(root); Assert.DoesNotContain(diagnostics, x => x.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(root!); var window = new Window { Content = root }; window.Show();
            try
            {
                Button(root!, "Hide Pinned").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await WaitAsync(() => Visible(model).All(s => s.Kind != GoHomeSectionKind.Pinned)); window.UpdateLayout();
                Assert.DoesNotContain(Traverse(root!).OfType<Button>(), b => Equals(b.Content, "Pinned"));
                Assert.True((await f.Configuration.GetAsync()).Stored.Current.EffectiveGoHome.Sections.Single(s => s.Kind == GoHomeSectionKind.Pinned).Visible);
                Button(root!, "Resize All Apps (Standard)").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await WaitAsync(() => Visible(model).Single(s => s.Kind == GoHomeSectionKind.AllApps).Size == GoHomeSectionSize.Large);
                Assert.True(model.TrySetValue("GoGroup", "Work"));
                Button(root!, "Group All Apps").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await WaitAsync(() => Visible(model).Single(s => s.Kind == GoHomeSectionKind.AllApps).Group == "Work");
                Button(root!, "Move All Apps earlier").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await WaitAsync(() => Settings(model).ToList().FindIndex(s => s.Kind == GoHomeSectionKind.AllApps) == 2);
                Button(root!, "Dashboard layout").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await WaitAsync(() => model.TryGetValue("GoLayout", out var v) && Equals(v, "Dashboard")); window.UpdateLayout();
                Assert.Equal(22, Button(root!, "All Apps").FontSize);
                Button(root!, "Keep").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await WaitAsync(() => model.TryGetValue("HasPreview", out var v) && Equals(v, false));
                var reopened = (await f.Reopen().GetAsync()).Stored.Current.EffectiveGoHome;
                Assert.Equal(GoHomeLayout.Dashboard, reopened.Layout);
                Assert.False(reopened.Sections.Single(s => s.Kind == GoHomeSectionKind.Pinned).Visible);
                Assert.Equal("Work", reopened.Sections.Single(s => s.Kind == GoHomeSectionKind.AllApps).Group);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None));
    }
    [Fact]
    public async Task CopiedSectionAndOriginalSessionReplacementCannotPublishCustomization()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); using var model = new ShellViewModel();
            await model.StartAsync(f.Configuration, new GoService([]), new LinuxApplicationLauncher(new EmptyRegistry(), f.Resources, f.Actors), default);
            var original = Settings(model).First(); var baseline = (await f.Configuration.GetAsync()).Stored.Revision;
            await model.DispatchAsync("GoSectionVisibility", original with { });
            Assert.Null((await f.Configuration.GetAsync()).Preview);
            f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "replacement" };
            await model.DispatchAsync("GoSectionVisibility", original);
            var current = await f.Configuration.GetAsync(); Assert.Null(current.Preview); Assert.Equal(baseline, current.Stored.Revision);
            Assert.True(current.Stored.Current.EffectiveGoHome.Sections.Single(s => s.Kind == original.Kind).Visible);
            return true;
        }, CancellationToken.None));
    }
    [Fact]
    public async Task RefreshDuringSectionAdmissionCannotAdoptTheReplacementDisplayedHome()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); using var model = new ShellViewModel(); var provider = new Provider(f.AppId);
            await model.StartAsync(f.Configuration, new GoService([provider]), new LinuxApplicationLauncher(new EmptyRegistry(), f.Resources, f.Actors), default);
            await model.DispatchAsync("GoHome.Pinned", null);
            var original = Settings(model).Single(s => s.Kind == GoHomeSectionKind.AllApps);
            f.Actors.SuspendNext = true;
            var pending = model.DispatchAsync("OpenGoHomeSection", original).AsTask();
            await f.Actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await model.RefreshAsync(default); f.Actors.Release.TrySetResult(); await pending;
            Assert.Equal(0, provider.Queries);
            return true;
        }, CancellationToken.None));
    }
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
        public AuthenticatedResourceActor? ExpectedActor; public TaskCompletionSource Invoked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GoResult Result(string label) => new(ProviderId, new("Home", "os.installed-application", appId.ToString("D"), "7"), label, "Apps", [new("Open", "Open")]);
        public Task<GoResult?> ResolveAsync(GoCanonicalLocator locator, CancellationToken ct) => Task.FromResult<GoResult?>(locator.Id == appId.ToString("D") ? Result("Current pinned owner label") : null);
        public async IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, [EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask; ct.ThrowIfCancellationRequested(); Queries++; yield return Result("All Apps owner label"); }
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
