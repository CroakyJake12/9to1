using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class GoPrivateOriginalReadNativeTests
{
    [Fact]
    public async Task ActualHomeOwnerSearchAllAppsAndPinnedPublishOriginalCanonicalRowsToRealCui()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); using var model = new ShellViewModel();
            await model.StartAsync(f.Configuration, f.Go, f.Launcher, default);
            Assert.True(model.TrySetValue("Query", "Controlled"));
            await model.DispatchAsync("Search", null);
            var search = Assert.Single(Rows(model)); Assert.Equal("Controlled owner app", search.Label);
            Assert.Equal((await f.Actors.GetCurrentAsync(default))!.ProfileId,
                Assert.Single(await f.Registry.RefreshForActorAsync((await f.Actors.GetCurrentAsync(default))!, default)).HomeProfileId);
            var (root, diagnostics, loader) = Load(model); using (loader)
            {
                Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var window = new Window { Width = 1100, Height = 900, Content = root }; window.Show(); window.UpdateLayout();
                try
                {
                    Assert.Single(Traverse(root!).OfType<Button>(), b => Equals(b.Content, search.Label));
                    await model.DispatchAsync("GoHome.All Apps", null); window.UpdateLayout();
                    var allApps = Assert.Single(Rows(model)); Assert.Equal(search.Reference, allApps.Reference);
                    Assert.Single(Traverse(root!).OfType<Button>(), b => Equals(b.Content, allApps.Label));
                    var original = await f.Configuration.GetAsync();
                    var preview = await f.Configuration.PreviewAsync(original.Stored,
                        DesktopPageEdits.PinApplication(original.Stored.Current, Guid.Parse(allApps.Reference.Id), "Stored label is not authority"), TimeSpan.FromMinutes(1));
                    await f.Configuration.KeepAsync(preview.Preview!.Id);
                    await model.RefreshAsync(default); await model.DispatchAsync("GoHome.Pinned", null); window.UpdateLayout();
                    var pinned = Assert.Single(Rows(model)); Assert.Equal(allApps.Reference, pinned.Reference);
                    Assert.Equal("Controlled owner app", pinned.Label);
                    Assert.Single(Traverse(root!).OfType<Button>(), b => Equals(b.Content, pinned.Label));
                    await model.DispatchAsync("Open", pinned with { }); // Copied result must not enter actual native launch.
                    Assert.Equal(0, f.Route.Invocations);
                }
                finally { window.Close(); }
            }
            return true;
        }, default));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HeldActualOwnerReadRejectsOriginalPrincipalOrSessionReplacementWithoutNativeRows(bool replaceSession)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); using var model = new ShellViewModel();
            await model.StartAsync(f.Configuration, f.Go, f.Launcher, default);
            f.Observations.Suspend = true; Assert.True(model.TrySetValue("Query", "Controlled"));
            var pending = model.DispatchAsync("Search", null).AsTask();
            await f.Observations.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (replaceSession) f.Actors.Current = new HomeLocalProfileIdentity(f.Store, f.Principal);
            else f.Principal.Value = "replacement-principal";
            f.Observations.Release.TrySetResult(); await pending;
            Assert.Empty(Rows(model)); Assert.DoesNotContain((await f.Store.ReadAsync()).State!.Records, r => r.RecordType == "home.installed-apps");
            var (root, diagnostics, loader) = Load(model); using (loader)
            {
                Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var window = new Window { Width = 1100, Height = 900, Content = root }; window.Show(); window.UpdateLayout();
                try { Assert.DoesNotContain(Traverse(root!).OfType<Button>(), b => Equals(b.Content, "Controlled owner app")); }
                finally { window.Close(); }
            }
            return true;
        }, default));
    }
    [Fact]
    public async Task PrivateDisplayedScopeStopsAtActualSentinelBeforeAnyOwnerQuery()
    {
        using var f = new Fixture(); var original = await f.Configuration.GetAsync(); var values = new LyingScope();
        var owner = new ShellGoSearchOwner(f.Configuration);
        Assert.Throws<ArgumentException>(() => owner.Begin(original.Stored, new GoQuery("", Scope: new GoScope(ProviderIds: values))));
        Assert.Equal(257, values.Consumed);
        Assert.DoesNotContain((await f.Store.ReadAsync()).State!.Records, r => r.RecordType == "home.installed-apps");
    }
    private sealed class LyingScope : IReadOnlySet<string>
    {
        public int Count => 1; public int Consumed;
        public IEnumerator<string> GetEnumerator()
        { for (var i = 0; i < 1000000; i++) { Consumed++; yield return "owner-" + i; } }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        public bool Contains(string value) => throw new NotSupportedException();
        public bool IsProperSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsProperSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsSubsetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool IsSupersetOf(IEnumerable<string> other) => throw new NotSupportedException();
        public bool Overlaps(IEnumerable<string> other) => throw new NotSupportedException();
        public bool SetEquals(IEnumerable<string> other) => throw new NotSupportedException();
    }
    private static IReadOnlyList<GoResult> Rows(ShellViewModel model)
    { Assert.True(model.TryGetValue("Results", out var value)); return Assert.IsAssignableFrom<IEnumerable<GoResult>>(value).ToArray(); }
    private static (Control?, IReadOnlyList<CakeOS.Cui.Language.CuiDiagnostic>, CuiControlLoader) Load(ShellViewModel model)
    {
        var loader = new CuiControlLoader(TaskbarLayerSurface.CreateRegistry(model)); loader.SetBindingContext(model); loader.SetActionDispatcher(model);
        using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui"); using var reader = new StreamReader(stream!);
        var (root, diagnostics) = loader.LoadMarkup(reader.ReadToEnd()); Assert.NotNull(root); loader.WireBindings(root!); return (root, diagnostics, loader);
    }
    private static IEnumerable<Control> Traverse(Control root)
    { yield return root; foreach (var child in root.GetLogicalChildren().OfType<Control>()) foreach (var nested in Traverse(child)) yield return nested; }
    private sealed class Principal : ITrustedHostPrincipalSource
    { public string Value = "original-principal"; public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>(Value); }
    private sealed class Actors(HomeLocalProfileIdentity initial) : IAuthenticatedResourceActorSource, IHomeStateCommitActorGuard
    {
        public HomeLocalProfileIdentity Current = initial;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => Current.GetCurrentAsync(ct);
        public ValueTask<bool> CheckAsync(HomeCoreStoredState state, AuthenticatedResourceActor expected, HomeStateCommitPhase phase, CancellationToken ct) => Current.CheckAsync(state, expected, phase, ct);
    }
    private sealed class Observations : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "linux.xdg-desktop"; public bool Suspend;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct)
        { if (Suspend) { Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
          return [new("original-principal", "Original", false, true, [new("controlled-app", "controlled-entry", "Controlled owner app", null, true)])]; }
    }
    // Query/resolve use the actual owning adapter; only invocation is a controlled denial recorder.
    private sealed class InvocationFence(InstalledApplicationsGoProvider actual) : IGoOriginalActorQuery, IGoOriginalActorCanonicalResolver, IGoOriginalActorInvocation
    {
        public string ProviderId => actual.ProviderId; public int Invocations;
        public IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor actor, CancellationToken ct) => actual.QueryForActorAsync(query, actor, ct);
        public Task<GoResult?> ResolveForActorAsync(GoCanonicalLocator locator, AuthenticatedResourceActor actor, CancellationToken ct) => actual.ResolveForActorAsync(locator, actor, ct);
        public IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, CancellationToken ct) => throw new InvalidOperationException("No legacy query fallback.");
        public Task InvokeAsync(GoCanonicalReference reference, string action, CancellationToken ct) => throw new InvalidOperationException("No legacy invocation fallback.");
        public Task InvokeForActorAsync(GoCanonicalReference reference, string action, AuthenticatedResourceActor actor, CancellationToken ct)
        { Invocations++; throw new InvalidOperationException("Controlled invocation rejection; no native process."); }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-private-go-native-" + Guid.NewGuid().ToString("N"));
        public Principal Principal { get; } = new(); public Observations Observations { get; } = new();
        public FileHomeCoreStateStore Store { get; } public Actors Actors { get; } public HomeInstalledApplicationRegistry Registry { get; }
        public ShellConfigurationService Configuration { get; } public LinuxApplicationLauncher Launcher { get; } public GoService Go { get; } public InvocationFence Route { get; }
        public Fixture()
        {
            Directory.CreateDirectory(root); Store = new(Path.Combine(root, "home.json")); Actors = new(new HomeLocalProfileIdentity(Store, Principal));
            Registry = new(Store, Actors, [Observations]); var resources = new ResourceAuthorizationService(Actors,
                [new InstalledApplicationResourceResolver(Registry), new ShellConfigurationResourceResolver(Store)]);
            Configuration = new(new HomeShellConfigurationStore(Store, Actors, resources)); Launcher = new(Registry, resources, Actors);
            Route = new(new InstalledApplicationsGoProvider(Registry, Launcher));
            Go = new([Route, new ShellNavigationGoProvider(Configuration)]);
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
