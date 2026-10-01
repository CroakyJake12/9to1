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
public sealed class GoHomeNativeTests
{
    [Fact]
    public async Task ActualGoHomeButtonsResolveCanonicalPinSwitchToAllAppsAndDispatchOriginalActor()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        await session.Dispatch(async () =>
        {
            using var f = new Fixture(); var original = await f.Configuration.GetAsync();
            var preview = await f.Configuration.PreviewAsync(original.Stored,
                DesktopPageEdits.PinApplication(original.Stored.Current, f.AppId, "Saved old label"), TimeSpan.FromMinutes(1));
            await f.Configuration.KeepAsync(preview.Preview!.Id);
            using var model = new ShellViewModel(); var provider = new Provider(f.AppId);
            await model.StartAsync(f.Configuration, new GoService([provider]), new LinuxApplicationLauncher(new EmptyRegistry(), f.Resources, f.Actors), default);
            using var loader = new CuiControlLoader(TaskbarLayerSurface.CreateRegistry(model)); loader.SetBindingContext(model); loader.SetActionDispatcher(model);
            using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.Shell.cui");
            using var reader = new StreamReader(stream!); var (root, diagnostics) = loader.LoadMarkup(reader.ReadToEnd());
            Assert.NotNull(root); Assert.DoesNotContain(diagnostics, x => x.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(root!); var window = new Window { Content = root }; window.Show();
            try
            {
                await WaitAsync(() => Rows(model).Any(x => x.Label == "Current pinned owner label")); window.UpdateLayout();
                Assert.DoesNotContain(Rows(model), x => x.Label == "Saved old label");
                Assert.False(Button(root!, "Recent").IsEnabled); Assert.False(Button(root!, "Suggested").IsEnabled);
                Button(root!, "Current pinned owner label").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await provider.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(f.Actors.Current, provider.ExpectedActor);
                Button(root!, "All Apps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitAsync(() => Rows(model).Any(x => x.Label == "All Apps owner label")); window.UpdateLayout();
                Assert.Equal(1, provider.Queries); Assert.DoesNotContain(Rows(model), x => x.Label == "Current pinned owner label");
                Assert.NotNull(Button(root!, "All Apps owner label"));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
    [Fact]
    public async Task ActualGoHomeSessionReplacementClearsRowsAndRejectsCopiedOrOldPinActions()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        await session.Dispatch(async () =>
        {
            using var f = new Fixture(); using var model = new ShellViewModel(); var provider = new Provider(f.AppId);
            await model.StartAsync(f.Configuration, new GoService([provider]), new LinuxApplicationLauncher(new EmptyRegistry(), f.Resources, f.Actors), default);
            await model.DispatchAsync("GoHome.All Apps", null); var row = Assert.Single(Rows(model));
            await model.DispatchAsync("Open", row with { }); Assert.Null(provider.ExpectedActor);
            f.Actors.Current = f.Actors.Current with { AuthenticationRevision = "new-session" };
            await model.RefreshAsync(default); Assert.Empty(Rows(model));
            await model.DispatchAsync("Open", row); Assert.Null(provider.ExpectedActor);
        }, CancellationToken.None);
    }
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
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
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
        public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
