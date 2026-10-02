using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Go;
using HavenOS.Home.Core;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class GoQueryStateNativeTests
{
    [Fact]
    public async Task ActualLocalCanonicalRowStaysVisibleWhileControlledSlowOwnerWaitsAndFails()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var slow = new SlowOwner(); using var f = new Fixture(slow); slow.Actors = f.Actors;
            using var model = new ShellViewModel(); await model.StartAsync(f.Configuration, f.Go, f.Launcher, default);
            Assert.True(model.TrySetValue("Query", "Controlled"));
            var pending = model.DispatchAsync("Search", null).AsTask();
            await slow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitAsync(() => Rows(model).Count == 1);
            var original = Assert.Single(Rows(model)); Assert.Equal("Controlled owner app", original.Label);
            var actual = Assert.Single(await f.Registry.RefreshForActorAsync((await f.Actors.GetCurrentAsync(default))!, default));
            Assert.Equal(actual.ApplicationId.ToString("D"), original.Reference.Id);
            Assert.Equal("Streaming", State(model)); Assert.False(pending.IsCompleted);
            var (root, diagnostics, loader) = Load(model); using (loader)
            {
                Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var window = new Window { Width = 1100, Height = 900, Content = root }; window.Show(); window.UpdateLayout();
                try
                {
                    Assert.True(Assert.Single(Traverse(root!).OfType<Button>(), b => Equals(b.Content, original.Label)).IsEnabled);
                    Assert.Contains("arriving", Assert.IsType<string>(Assert.Single(Traverse(root!).OfType<TextBlock>(), t => t.Name == "go-query-status").Text));
                    slow.Release.TrySetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(5)); window.UpdateLayout();
                    Assert.Same(original, Assert.Single(Rows(model))); Assert.Equal("Partial", State(model));
                    Assert.Contains("unavailable", Assert.IsType<string>(Assert.Single(Traverse(root!).OfType<TextBlock>(), t => t.Name == "go-query-status").Text));
                    Assert.True(Assert.Single(Traverse(root!).OfType<Button>(), b => Equals(b.Content, original.Label)).IsEnabled);
                    Assert.Equal(0, f.Route.Invocations); // No real installed-process launch is asserted.
                }
                finally { slow.Release.TrySetResult(); window.Close(); }
            }
            return true;
        }, default));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualEmptyOwnerQueryIsDistinctFromMissingOriginalOwnerCapability(bool missingOwner)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var legacy = new LegacyOwner(); using var f = new Fixture(missingOwner ? legacy : null);
            using var model = new ShellViewModel(); await model.StartAsync(f.Configuration, f.Go, f.Launcher, default);
            Assert.True(model.TrySetValue("Query", "There is no matching canonical item")); await model.DispatchAsync("Search", null);
            Assert.Empty(Rows(model)); Assert.Equal(missingOwner ? "Unavailable" : "Empty", State(model)); Assert.Equal(0, legacy.Calls);
            var (root, diagnostics, loader) = Load(model); using (loader)
            {
                Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var window = new Window { Width = 1100, Height = 900, Content = root }; window.Show(); window.UpdateLayout();
                try
                {
                    var text = Assert.IsType<string>(Assert.Single(Traverse(root!).OfType<TextBlock>(), t => t.Name == "go-query-status").Text);
                    Assert.Contains(missingOwner ? "cannot confirm" : "No matching items", text);
                }
                finally { window.Close(); }
            }
            return true;
        }, default));
    }
    [Fact]
    public async Task ActualDashboardWithOnlyEmptyPinnedSectionReportsEmptyAfterOwnerRead()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture();
            var original = await f.Configuration.GetAsync();
            var presentation = original.Stored.Current.EffectiveGoHome;
            var candidate = GoHomeEdits.Configure(original.Stored.Current, presentation with
            {
                Layout = GoHomeLayout.Dashboard,
                Sections = presentation.Sections.Select(section => section with { Visible = section.Kind == GoHomeSectionKind.Pinned }).ToArray()
            });
            var preview = await f.Configuration.PreviewAsync(original.Stored, candidate, TimeSpan.FromSeconds(30));
            await f.Configuration.KeepAsync(preview.Preview!.Id);
            using var model = new ShellViewModel(); await model.StartAsync(f.Configuration, f.Go, f.Launcher, default);
            await model.DispatchAsync("Search", null);
            Assert.Empty(Rows(model)); Assert.Equal("Empty", State(model));
            Assert.True(model.TryGetValue("GoHomeView", out var view)); Assert.Equal("Dashboard", Assert.IsType<string>(view));
            var (root, diagnostics, loader) = Load(model); using (loader)
            {
                Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var window = new Window { Width = 1100, Height = 900, Content = root }; window.Show(); window.UpdateLayout();
                try
                {
                    var text = Assert.IsType<string>(Assert.Single(Traverse(root!).OfType<TextBlock>(), t => t.Name == "go-query-status").Text);
                    Assert.Contains("No matching items", text);
                }
                finally { window.Close(); }
            }
            return true;
        }, default));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualQueryKeyboardSubmissionDistinguishesSearchingAndPartialAndFencesInvocation(bool localRow)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var slow = new SlowOwner(); using var f = new Fixture(slow); slow.Actors = f.Actors;
            using var model = new ShellViewModel(); await model.StartAsync(f.Configuration, f.Go, f.Launcher, default);
            var (root, diagnostics, loader) = Load(model); using (loader)
            {
                Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
                var window = new Window { Width = 1100, Height = 900, Content = root }; window.Show(); window.UpdateLayout();
                try
                {
                    var query = Assert.Single(Traverse(root!).OfType<GoSearchInput>());
                    query.Text = localRow ? "Controlled" : "No matching canonical item";
                    Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.True(query.Focus());
                    window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                    window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                    await slow.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await WaitAsync(() => State(model) == (localRow ? "Streaming" : "Searching"));
                    if (localRow) Assert.Single(Rows(model)); else Assert.Empty(Rows(model));
                    slow.Release.TrySetResult();
                    await WaitAsync(() => State(model) == (localRow ? "Partial" : "Unavailable")); window.UpdateLayout();
                    Assert.Contains("unavailable", Assert.IsType<string>(Assert.Single(Traverse(root!).OfType<TextBlock>(), t => t.Name == "go-query-status").Text));
                    if (localRow)
                    {
                        var row = Assert.Single(Rows(model));
                        var button = Assert.Single(Traverse(root!).OfType<Button>(), b => Equals(b.Content, row.Label));
                        Assert.True(button.IsEnabled); Assert.True(button.Focus());
                        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                        await WaitAsync(() => f.Route.Invocations == 1);
                        await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        f.Principal.Value = "replacement-principal";
                        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "Enter");
                        // Await the complete rendered action/readiness/catch/finally pipeline, not just the model queue.
                        await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
                        Assert.Equal(1, f.Route.Invocations);
                        await model.DispatchAsync("Open", row);
                        Assert.Equal(1, f.Route.Invocations);
                    }
                    else Assert.Equal(0, f.Route.Invocations);
                }
                finally { slow.Release.TrySetResult(); window.Close(); }
            }
            return true;
        }, default));
    }
    private static string State(ShellViewModel model)
    { Assert.True(model.TryGetValue("GoQueryState", out var value)); return Assert.IsType<string>(value); }
    private static async Task WaitAsync(Func<bool> ready)
    { var until = DateTime.UtcNow + TimeSpan.FromSeconds(5); while (!ready()) { if (DateTime.UtcNow > until) throw new TimeoutException("Local owning Go row did not arrive."); await Task.Delay(1); } }
    // Controlled blocked/failing owner, not real remote transport or a fabricated canonical result.
    private sealed class SlowOwner : IGoOriginalActorQuery
    {
        public string ProviderId => "controlled.slow-owner"; public Actors? Actors;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<GoResult> QueryForActorAsync(GoQuery query, AuthenticatedResourceActor expected, [EnumeratorCancellation] CancellationToken ct)
        {
            if (Actors is null || await Actors.GetCurrentAsync(ct) != expected) throw new UnauthorizedAccessException();
            Entered.TrySetResult(); await Release.Task.WaitAsync(ct);
            if (await Actors.GetCurrentAsync(ct) != expected) throw new UnauthorizedAccessException();
            await Task.FromException(new IOException("Controlled owner failure"));
            yield break;
        }
        public IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, CancellationToken ct) => throw new InvalidOperationException("No legacy query fallback.");
        public Task InvokeAsync(GoCanonicalReference reference, string action, CancellationToken ct) => throw new InvalidOperationException("Read-only controlled owner.");
    }
    private sealed class LegacyOwner : IGoProvider
    {
        public string ProviderId => "controlled.missing-original-owner"; public int Calls;
        public IAsyncEnumerable<GoResult> QueryAsync(GoQuery query, CancellationToken ct) { Calls++; throw new InvalidOperationException("Forbidden legacy query."); }
        public Task InvokeAsync(GoCanonicalReference reference, string action, CancellationToken ct) { Calls++; throw new InvalidOperationException("Forbidden legacy action."); }
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
        public string ProviderId => "linux.xdg-desktop"; public bool Suspend = false; // Canonical local fixture starts unpaused.
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
        public Fixture(IGoProvider? extra = null)
        {
            Directory.CreateDirectory(root); Store = new(Path.Combine(root, "home.json")); Actors = new(new HomeLocalProfileIdentity(Store, Principal));
            Registry = new(Store, Actors, [Observations]); var resources = new ResourceAuthorizationService(Actors,
                [new InstalledApplicationResourceResolver(Registry), new ShellConfigurationResourceResolver(Store)]);
            Configuration = new(new HomeShellConfigurationStore(Store, Actors, resources)); Launcher = new(Registry, resources, Actors);
            Route = new(new InstalledApplicationsGoProvider(Registry, Launcher));
            Go = new GoService(new IGoProvider[] { Route, new ShellNavigationGoProvider(Configuration) }.Concat(extra is null ? Array.Empty<IGoProvider>() : new IGoProvider[] { extra! }));
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
