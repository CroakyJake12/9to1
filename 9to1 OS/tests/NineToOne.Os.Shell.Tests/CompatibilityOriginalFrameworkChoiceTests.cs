using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Haven.Application.Go;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Os.Shell;

namespace NineToOne.Os.Shell.Tests;

[Collection("Native CUI")]
public sealed class CompatibilityOriginalFrameworkChoiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultSameHomeRegistrationMakesOriginalGoFrameworkActionReachNativeManager(bool registerControlledOwner)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); var app = await f.SeedAsync(); var actor = (await f.Actors.GetCurrentAsync(default))!;
            var permissions = new HomePermissionTrustService(f.Store, (_, _) => null);
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(f.Store), new HomePermissionsCoreService(permissions, f.Actors)]);
            var resources = new ResourceAuthorizationService(f.Actors, [new InstalledApplicationResourceResolver(f.Registry)]);
            var services = new ServiceCollection();
            services.AddSingleton<IInstalledApplicationRegistry>(f.Registry);
            services.AddSingleton<IAuthenticatedResourceActorSource>(f.Actors); services.AddSingleton(resources); services.AddSingleton(runtime);
            services.AddSingleton<LinuxApplicationLauncher>();
            if (registerControlledOwner) services.AddSingleton<ICompatibilityApplicationOwner>(f.Owner);
            using var lifetime = new CancellationTokenSource();
            var parent = new Window { Width = 900, Height = 700 }; parent.Show();
            services.AddOsCompatibilityManager(lifetime.Token, () => parent);
            services.AddSingleton<IGoProvider, InstalledApplicationsGoProvider>();
            await using var provider = services.BuildServiceProvider();
            try
            {
                var go = new GoService(provider.GetServices<IGoProvider>());
                GoResult? result = null;
                await foreach (var update in go.QueryForActorAsync(new GoQuery("", Limit: 100), actor, default))
                    if (update.Result?.Reference.Id == app.ApplicationId.ToString("D")) result = update.Result;
                Assert.NotNull(result); Assert.Contains(result.Actions, a => a.Id == "Frameworks");
                await go.InvokeForActorAsync(result, "Frameworks", actor, ct: default);
                var window = Assert.Single(parent.OwnedWindows); Assert.True(window.IsVisible);
                var root = Assert.IsAssignableFrom<Control>(window.Content); window.UpdateLayout();
                Assert.False(Find(root, "Install or launch").IsEnabled);
                if (registerControlledOwner) { Assert.Equal(1, f.Owner.Calls); Assert.True(Find(root, "Wine").IsEnabled); }
                else { Assert.Equal(0, f.Owner.Calls); Assert.Contains(Traverse(root).OfType<TextBlock>(), t => t.Text?.Contains("No current compatibility owner", StringComparison.Ordinal) == true); }
                f.Principal.Value = "foreign-principal";
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => go.InvokeForActorAsync(result, "Frameworks", actor, ct: default));
                Assert.Single(parent.OwnedWindows); Assert.Equal(registerControlledOwner ? 1 : 0, f.Owner.Calls);
                window.Close(); Assert.Empty(parent.OwnedWindows);
            }
            finally { parent.Close(); }
            return true;
        }, default));
    }
    [Fact]
    public async Task ActualNativeManagerMountUsesCanonicalReadAuthorityAndRejectsForeignPrincipal()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); var app = await f.SeedAsync(); var actor = (await f.Actors.GetCurrentAsync(default))!;
            var resources = new ResourceAuthorizationService(f.Actors, [new InstalledApplicationResourceResolver(f.Registry)]);
            var launcher = new LinuxApplicationLauncher(f.Registry, resources, f.Actors);
            var parent = new Window { Width = 900, Height = 700 }; parent.Show();
            try
            {
                using var host = await OsCompatibilityFrameworkWindow.OpenAsync(parent, launcher, f.Routing, f.Actors,
                    new Ready(), app.ApplicationId, app.Revision, actor, default);
                Assert.True(host.Window.IsVisible); var root = Assert.IsAssignableFrom<Control>(host.Window.Content);
                Assert.False(Find(root, "Install or launch").IsEnabled);
                Find(root, "Android runtime").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await host.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5)); host.Window.UpdateLayout();
                Assert.Equal(2, f.Owner.Calls);
                f.Principal.Value = "foreign-principal";
                Find(root, "Wine").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await host.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(2, f.Owner.Calls); // Actual resource owner denies before another backend observation.
                Assert.False(host.Window.IsVisible); Assert.Null(host.Window.Content);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => OsCompatibilityFrameworkWindow.OpenAsync(parent,
                    launcher, f.Routing, f.Actors, new Ready(), app.ApplicationId, app.Revision, actor, default));
                host.Window.Close();
            }
            finally { parent.Close(); }
            return true;
        }, default));
    }
    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ControlledHostReady", "Controlled native fixture readiness")); }
    }
    [Fact]
    public async Task ActualCanonicalHomeFrameworkChoiceRechecksPolicyAndUsesPrivateRenderedRows()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(GoAndCuiTests.TestApplication));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            using var f = new Fixture(); var app = await f.SeedAsync(); var original = (await f.Actors.GetCurrentAsync(default))!;
            using var bindings = new CompatibilityFrameworkChoiceBindings(f.Routing, f.Actors, app.ApplicationId, app.Revision, original, default);
            await bindings.RefreshAsync(); Assert.Equal("wine", Read(bindings, "Proposed"));
            Assert.True(bindings.TryGetValue("Frameworks", out var items));
            var staleWine = Assert.Single(Assert.IsAssignableFrom<IEnumerable>(items).Cast<object>(), item => bindings.TryGetItemValue(item, "Label", out var label) && Equals(label, "Wine"));
            using var loader = new CuiControlLoader(); loader.SetBindingContext(bindings); loader.SetActionDispatcher(bindings);
            using var stream = typeof(ShellConfiguration).Assembly.GetManifestResourceStream("NineToOne.Os.Shell.UI.CompatibilityFrameworks.cui");
            using var reader = new StreamReader(stream!); var (root, diagnostics) = loader.LoadMarkup(await reader.ReadToEndAsync());
            Assert.NotNull(root); Assert.DoesNotContain(diagnostics, d => d.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error);
            loader.WireBindings(root!); var window = new Window { Content = root, Width = 780, Height = 620 }; window.Show(); window.UpdateLayout();
            try
            {
                Assert.False(Find(root!, "Windows environment").IsEnabled);
                Find(root!, "Android runtime").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5)); window.UpdateLayout();
                Assert.Equal("android", Read(bindings, "Proposed")); Assert.Equal("ReviewRequired", Read(bindings, "State"));
                Assert.Contains("explicitly chosen", Read(bindings, "Status")); Assert.False(Find(root!, "Install or launch").IsEnabled);
                Assert.Equal(2, f.Owner.Calls);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await bindings.DispatchAsync("ChooseFramework", staleWine));
                Assert.Equal("android", Read(bindings, "Proposed")); Assert.Equal(2, f.Owner.Calls);
                f.Owner.PolicyAllowed = false;
                Find(root!, "Wine").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(5)); window.UpdateLayout();
                Assert.Equal("Denied", Read(bindings, "State")); Assert.Equal("No framework selected", Read(bindings, "Proposed"));
                Assert.False(Find(root!, "Wine").IsEnabled); Assert.False(Find(root!, "Android runtime").IsEnabled);
                Assert.Contains("Controlled policy denial", Read(bindings, "Status"));
            }
            finally { window.Close(); }
            return true;
        }, default));
    }
    [Fact]
    public async Task ExplicitIneligibleChoiceNeverSubstitutesAnEligibleFrameworkOrOverridesTrust()
    {
        using var f = new Fixture(); var app = await f.SeedAsync(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        var preview = await f.Routing.PreviewForActorAsync(app.ApplicationId, app.Revision, actor, "winboat");
        Assert.Equal(CompatibilityRoutingStatus.RequestedBackendUnavailable, preview.Status); Assert.Null(preview.ProposedBackend);
        f.Owner.TrustVerified = false;
        Assert.Equal(CompatibilityRoutingStatus.Denied, (await f.Routing.PreviewForActorAsync(app.ApplicationId, app.Revision, actor, "wine")).Status);
    }
    [Fact]
    public async Task SuspendedOwningBackendObservationCannotAdoptForeignPrincipal()
    {
        using var f = new Fixture(); var app = await f.SeedAsync(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        f.Owner.Suspend = true;
        var pending = f.Routing.PreviewForActorAsync(app.ApplicationId, app.Revision, actor, "wine").AsTask();
        await f.Owner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); f.Principal.Value = "foreign-principal"; f.Owner.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(1, f.Owner.Calls); var state = await f.Store.ReadAsync(default);
        Assert.Single(state.State!.Records, r => r.RecordType == "home.installed-apps"); // Seeded canonical record remains; no foreign owner/app creation.
    }
    [Fact]
    public async Task UnknownOwningRuntimeRemainsUnavailableThenRefreshesWithoutGrantingLaunch()
    {
        using var f = new Fixture(); var app = await f.SeedAsync(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        using var bindings = new CompatibilityFrameworkChoiceBindings(f.Routing, f.Actors, app.ApplicationId, app.Revision, actor, default);
        f.Owner.Available = false; await bindings.RefreshAsync();
        Assert.Equal("Unavailable", Read(bindings, "State")); Assert.Equal("No framework selected", Read(bindings, "Proposed"));
        Assert.True(bindings.TryGetValue("Frameworks", out var rows)); Assert.Empty(Assert.IsAssignableFrom<IEnumerable>(rows).Cast<object>());
        f.Owner.Available = true; await bindings.RefreshAsync(); Assert.Equal("ReviewRequired", Read(bindings, "State"));
        Assert.Equal("wine", Read(bindings, "Proposed")); // Read-only controlled proposal, never launch approval.
    }
    [Fact]
    public async Task MissingOriginalRegistryPortDeniesWithoutLegacyResolveOrBackendObservation()
    {
        using var f = new Fixture(); var app = await f.SeedAsync(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        var legacy = new LegacyRegistry(); var routing = new CompatibilityRoutingService(f.Actors, legacy, f.Owner);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await routing.PreviewForActorAsync(app.ApplicationId, app.Revision, actor));
        Assert.Equal(0, legacy.Calls); Assert.Equal(0, f.Owner.Calls);
    }
    [Fact]
    public async Task ClosedChooserCancelsActualHeldOwnerWithoutPublishingReturnedFrameworks()
    {
        using var f = new Fixture(); var app = await f.SeedAsync(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        using var bindings = new CompatibilityFrameworkChoiceBindings(f.Routing, f.Actors, app.ApplicationId, app.Revision, actor, default);
        f.Owner.Suspend = true; var pending = bindings.RefreshAsync();
        await f.Owner.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); bindings.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(bindings.TryGetValue("Frameworks", out var rows)); Assert.Empty(Assert.IsAssignableFrom<IEnumerable>(rows).Cast<object>());
        Assert.Equal(1, f.Owner.Calls); await bindings.RefreshAsync(); Assert.Equal(1, f.Owner.Calls);
    }
    [Fact]
    public async Task LyingBackendCountIsBoundedByActualEnumerationBeforePublication()
    {
        using var f = new Fixture(); var app = await f.SeedAsync(); var actor = (await f.Actors.GetCurrentAsync(default))!;
        var list = new LyingBackends(); f.Owner.Backends = list;
        await Assert.ThrowsAsync<IOException>(async () => await f.Routing.PreviewForActorAsync(app.ApplicationId, app.Revision, actor));
        Assert.Equal(129, list.Consumed);
    }
    private static string Read(CompatibilityFrameworkChoiceBindings bindings, string path)
    { Assert.True(bindings.TryGetValue(path, out var value)); return Assert.IsType<string>(value); }
    private static Button Find(Control root, string text) => Assert.Single(Traverse(root).OfType<Button>(), b => Equals(b.Content, text));
    private static IEnumerable<Control> Traverse(Control root)
    { yield return root; foreach (var child in root.GetLogicalChildren().OfType<Control>()) foreach (var descendant in Traverse(child)) yield return descendant; }
    private sealed class Principal : ITrustedHostPrincipalSource
    { public string Value = "original-principal"; public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>(Value); }
    private sealed class Inventory : IInstalledApplicationObservationProvider
    {
        public string ProviderId => "linux.xdg-desktop";
        public ValueTask<IReadOnlyList<InstalledApplicationProfileObservation>> ObserveAsync(CancellationToken ct) =>
            ValueTask.FromResult<IReadOnlyList<InstalledApplicationProfileObservation>>([new("original-principal", "Original", false, true, [new("controlled-app", "controlled-entry", "Controlled", null, true)])]);
    }
    // Explicit controlled backend observation, not Wine/Android/VM availability or publisher proof.
    private sealed class Owner : ICompatibilityApplicationOwner
    {
        public bool PolicyAllowed = true, TrustVerified = true, Suspend = false, Available = true; public int Calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<CompatibilityBackendObservation> Backends = [new("wine", CompatibilityBackendKind.Wine, "fixture-wine", "1", 10, true, null),
            new("winboat", CompatibilityBackendKind.WindowsEnvironment, "fixture-guest", "1", 1, false, "No controlled guest runtime"),
            new("android", CompatibilityBackendKind.Android, "fixture-android", "1", 5, true, null)];
        public async ValueTask<CompatibilityApplicationObservation?> ObserveAsync(AuthenticatedResourceActor actor, InstalledApplicationReference app, CancellationToken ct)
        { Calls++; Entered.TrySetResult(); if (Suspend) await Release.Task.WaitAsync(ct); if (!Available) return null; return new(app.ApplicationId, app.Revision, "controlled-observation", PolicyAllowed, "Controlled policy denial", TrustVerified, null, Backends); }
    }
    private sealed class LegacyRegistry : IInstalledApplicationRegistry
    {
        public int Calls;
        public ValueTask<IReadOnlyList<InstalledApplicationReference>> RefreshAsync(CancellationToken ct) { Calls++; throw new InvalidOperationException("Legacy denied"); }
        public ValueTask<InstalledApplicationReference?> ResolveLaunchAsync(Guid id, long revision, CancellationToken ct) { Calls++; throw new InvalidOperationException("Legacy denied"); }
    }
    private sealed class LyingBackends : IReadOnlyList<CompatibilityBackendObservation>
    {
        public int Count => 1; public int Consumed;
        public CompatibilityBackendObservation this[int index] => throw new InvalidOperationException("No indexer");
        public IEnumerator<CompatibilityBackendObservation> GetEnumerator()
        { for (var i = 0; i < 1_000_000; i++) { Consumed++; yield return new("backend" + i, CompatibilityBackendKind.Wine, "fixture", "1", 0, true, null); } }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "astra-framework-choice-" + Guid.NewGuid().ToString("N"));
        public Principal Principal { get; } = new(); public FileHomeCoreStateStore Store { get; }
        public HomeLocalProfileIdentity Actors { get; } public HomeInstalledApplicationRegistry Registry { get; }
        public Owner Owner { get; } = new(); public CompatibilityRoutingService Routing { get; }
        public Fixture()
        { Directory.CreateDirectory(root); Store = new(Path.Combine(root, "home.json")); Actors = new(Store, Principal); Registry = new(Store, Actors, [new Inventory()]); Routing = new(Actors, Registry, Owner); }
        public async Task<InstalledApplicationReference> SeedAsync() => Assert.Single(await Registry.RefreshForActorAsync((await Actors.GetCurrentAsync(default))!, default));
        public void Dispose() => Directory.Delete(root, true);
    }
}
