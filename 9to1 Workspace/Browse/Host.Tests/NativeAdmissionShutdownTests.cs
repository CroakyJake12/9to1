using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Browse.Host.Tests;

// Real Haven shutdown route: the actual App/UnifiedWorkspaceHost, NativeAppContentRegistry,
// BrowseChrome and BrowseNativeWorkspace. Only the registered app owner is a fixture; the
// rejection, retention, retirement join and host close are production code. This is Linux
// headless lifetime evidence, not Windows admission or a full product startup.
public sealed class NativeAdmissionShutdownTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BodyBound = TimeSpan.FromSeconds(60);
    private const string RefusalMessage = "This app does not have a registered native surface in the current host.";

    // Runs the body with an explicit bound and always attempts bounded cleanup afterwards.
    // The body failure and every cleanup failure are retained together, so neither can mask
    // the other and an early assertion failure never excuses unattempted cleanup.
    private static async Task RunBoundedAsync(Rig rig, Func<Rig, Task> body)
    {
        var failures = new List<Exception>();
        try { await body(rig).WaitAsync(BodyBound); }
        catch (Exception failure) { failures.Add(failure); }
        foreach (var cleanup in await rig.CleanupAsync())
            if (!failures.Any(known => ReferenceEquals(known, cleanup))) failures.Add(cleanup);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("The test body and/or its bounded cleanup failed.", failures);
    }

    [AvaloniaFact]
    public async Task Missing_native_binding_in_place_keeps_its_diagnostic_and_closes_the_actual_host_for_a_reopened_workspace()
    {
        var files = new FilesFactory();
        await using var rig = await Rig.CreateAsync((Files, files));
        await RunBoundedAsync(rig, async rig =>
        {
            var chrome = rig.Chrome!; var workspace = rig.Workspace!;

            // Fixture facts established before attributing any rejection to product behaviour:
            // Shelf is a real registered app, this host has no Shelf binding, and the only
            // native creation so far is the admitted Files tab.
            Assert.Equal(1, files.Creations);
            Assert.False(rig.Registry!.HasImplementation(Shelf));
            Assert.NotNull(chrome.State.SelectedTab); Assert.False(chrome.State.SelectedTab.IsHome);
            var originalTabId = chrome.State.SelectedTabId;

            Assert.True(workspace.QueueNavigation(new(chrome.State.SelectedTabId,
                AppContentAddress.Parse("9to1://shelf"), false), Task.CompletedTask));
            var driver = workspace.OriginalCommand!;
            var refusal = await Record.ExceptionAsync(() => driver).AsTask().WaitAsync(Bound);
            Assert.NotNull(refusal);
            Assert.True(driver.IsFaulted);
            Assert.IsAssignableFrom<NotSupportedException>(refusal);
            Assert.Equal(RefusalMessage, refusal!.Message);
            Assert.Equal(1, files.Creations); // the missing binding was refused before any factory ran

            // The rejected admission stays failed and inspectable; nothing rewrites it.
            Assert.Contains(driver, workspace.OriginalCommands);
            var retained = Faulted(chrome.OriginalNativeSources);
            Assert.NotEmpty(retained);
            Assert.All(retained, actual => Assert.True(HasCause(actual, refusal)));
            Assert.All(Faulted(chrome.OriginalContentTransitions), actual => Assert.True(HasCause(actual, refusal)));

            // Typed admission accounting at the poison path: the chrome witnessed this
            // exact refusal at its own app-content creation for this exact request.
            var record = Assert.Single(chrome.AdmissionRefusals);
            Assert.True(ReferenceEquals(record.Refusal, refusal));
            Assert.Equal(chrome.State.SelectedTabId, record.TabId);
            Assert.Equal("9to1://shelf", record.Address.Value);
            Assert.Equal(AppContentNamespace.NativeApp, record.Address.Namespace);

            // Permanence half of the defect: a refused no-effect admission must not
            // consume unresolved admission capacity. The same authority is admitted again
            // and fails again with its own fresh composition proof.
            Assert.True(workspace.QueueNavigation(new(chrome.State.SelectedTabId,
                AppContentAddress.Parse("9to1://shelf"), false), Task.CompletedTask));
            var repeat = workspace.OriginalCommand!;
            var repeatRefusal = await Record.ExceptionAsync(() => repeat).AsTask().WaitAsync(Bound);
            Assert.NotNull(repeatRefusal);
            Assert.NotSame(refusal, repeatRefusal);
            Assert.Equal(RefusalMessage, repeatRefusal!.Message);
            Assert.Equal(2, chrome.AdmissionRefusals.Count);
            Assert.Equal(1, files.Creations);

            // The chrome stays usable: a real admitted native navigation succeeds after the
            // refusals, so the repair is not merely letting a broken session close.
            await chrome.NavigateAsync("9to1://files/after-refusal").WaitAsync(Bound);
            Assert.Equal("9to1://files/after-refusal", chrome.State.SelectedTab!.Address.Value);
            Assert.Equal(2, files.Creations);

            // The defect: this no-effect admission refusal permanently poisons host shutdown,
            // so the actual host close cannot complete and a fresh workspace is not admittable.
            var close = rig.Host.CloseAndDrainAsync();
            await close.WaitAsync(Bound);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.NotNull(chrome.OriginalClose); Assert.True(chrome.OriginalClose!.IsCompletedSuccessfully);
            Assert.NotNull(workspace.OriginalClose); Assert.True(workspace.OriginalClose!.IsCompletedSuccessfully);
            // Only the retirement obligation was withdrawn: no refused source remains in the
            // retirement sets, while both refusals stay failed and inspectable in the ledger.
            var stillRetired = Faulted(chrome.OriginalNativeSources);
            Assert.True(stillRetired.Length == 0,
                "retained: " + string.Join(" ;; ", stillRetired.Select(actual => Graph(actual.Exception))));
            Assert.Empty(Faulted(chrome.OriginalContentTransitions));
            Assert.Equal(2, chrome.AdmissionRefusals.Count);
            Assert.True(driver.IsFaulted);
            await rig.Host.CloseAndDrainAsync().WaitAsync(Bound);
            await rig.ReopenNativeWorkspaceAsync(originalTabId, "9to1://files/after-refusal");
        });
    }

    [AvaloniaFact]
    public async Task Missing_native_binding_in_a_new_tab_keeps_its_diagnostic_and_closes_the_actual_host()
    {
        var files = new FilesFactory();
        await using var rig = await Rig.CreateAsync((Files, files));
        await RunBoundedAsync(rig, async rig =>
        {
            var chrome = rig.Chrome!; var workspace = rig.Workspace!;
            Assert.Equal(1, files.Creations);
            Assert.False(rig.Registry!.HasImplementation(Assistants));

            var selectedTabId = chrome.State.SelectedTabId;
            Assert.True(workspace.QueueNavigation(new(selectedTabId,
                AppContentAddress.Parse("9to1://assistants"), true), Task.CompletedTask));
            var driver = workspace.OriginalCommand!;
            var refusal = await Record.ExceptionAsync(() => driver).AsTask().WaitAsync(Bound);
            Assert.NotNull(refusal);
            Assert.True(driver.IsFaulted);
            Assert.IsAssignableFrom<NotSupportedException>(refusal);
            Assert.Equal(RefusalMessage, refusal!.Message);
            Assert.Equal(1, files.Creations);
            Assert.Contains(driver, workspace.OriginalCommands);
            Assert.All(Faulted(chrome.OriginalNativeSources), actual => Assert.True(HasCause(actual, refusal)));

            var close = rig.Host.CloseAndDrainAsync();
            await close.WaitAsync(Bound);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.NotNull(chrome.OriginalClose); Assert.True(chrome.OriginalClose!.IsCompletedSuccessfully);
            Assert.NotNull(workspace.OriginalClose); Assert.True(workspace.OriginalClose!.IsCompletedSuccessfully);
            // The refusal is accounted in the failed-action ledger, not as a retirement cause.
            var record = Assert.Single(chrome.AdmissionRefusals);
            Assert.True(ReferenceEquals(record.Refusal, refusal));
            // The chrome admits the canonical route for this authority, so the ledger keeps the
            // full address identity the chrome actually requested, not the typed intent text.
            Assert.Equal("9to1://spaces/assistants", record.Address.Value);
            Assert.Equal("spaces", record.Address.Authority);
            Assert.Equal(AppContentNamespace.NativeApp, record.Address.Namespace);
            // A new-tab admission is owned by the speculative new tab, which the chrome
            // removes once the admission is refused, so the ledger keeps that tab's identity
            // rather than the source tab's.
            Assert.NotEqual(Guid.Empty, record.TabId);
            Assert.NotEqual(selectedTabId, record.TabId);
            Assert.Empty(Faulted(chrome.OriginalNativeSources));
            Assert.Empty(Faulted(chrome.OriginalContentTransitions));
            Assert.True(driver.IsFaulted);
        });
    }

    // Guard against a broad fix: a bound authority whose own factory faults is never inferred
    // to have had no effect, so the original failure stays retained and keeps blocking a clean
    // host close on every later attempt.
    [AvaloniaFact]
    public async Task Bound_native_authority_with_a_faulted_factory_still_permanently_blocks_host_close()
    {
        var files = new FilesFactory(); var shelf = new FaultyFactory();
        await using var rig = await Rig.CreateAsync((Files, files), (Shelf, shelf));
        await RunBoundedAsync(rig, async rig =>
        {
            var chrome = rig.Chrome!; var workspace = rig.Workspace!;
            Assert.True(rig.Registry!.HasImplementation(Shelf));

            Assert.True(workspace.QueueNavigation(new(chrome.State.SelectedTabId,
                AppContentAddress.Parse("9to1://shelf"), false), Task.CompletedTask));
            var driver = workspace.OriginalCommand!;
            var driverFailure = await Record.ExceptionAsync(() => driver).AsTask().WaitAsync(Bound);
            Assert.NotNull(driverFailure);
            Assert.True(driver.IsFaulted);
            // Exact retained identity through the production graph, not a wrapper's top message.
            Assert.True(HasCause(driver, shelf.Failure));
            Assert.Equal(1, shelf.Creations); // a real factory invocation, not a missing binding

            var close = rig.Host.CloseAndDrainAsync();
            rig.AssertedFailedClose = close; // the exact host-close task this test asserts as failing
            var first = await Record.ExceptionAsync(() => close).AsTask().WaitAsync(Bound);
            Assert.NotNull(first);
            Assert.True(close.IsFaulted);
            Assert.True(HasCause(close, shelf.Failure));

            var repeat = rig.Host.CloseAndDrainAsync();
            Assert.Same(close, repeat); // the same terminal failed original, never re-attempted clean
            Assert.True(HasCause(repeat, shelf.Failure));
            Assert.NotNull(chrome.OriginalClose);
            Assert.False(chrome.OriginalClose!.IsCompletedSuccessfully);
            Assert.Equal(1, shelf.Creations); // the repeat close did not re-acquire the authority
        });
    }

    public enum RefusalReplay { Marker, DirectRethrow, AggregateForward, WrapperForward }

    // Negative control for the observed replay risk: a bound authority whose factory takes a
    // real effect and then throws a marker-shaped exception, directly rethrows a genuine
    // refusal captured from an earlier admission in the same chrome, aggregates that refusal,
    // or wraps it, never has its retirement obligation waived. Each shape runs in its own
    // rig, because a prior effectful failure intentionally poisons that owner's lifetime and
    // would otherwise stop the next factory from being invoked at all.
    [AvaloniaTheory]
    [InlineData(RefusalReplay.Marker)]
    [InlineData(RefusalReplay.DirectRethrow)]
    [InlineData(RefusalReplay.AggregateForward)]
    [InlineData(RefusalReplay.WrapperForward)]
    public async Task Captured_or_marker_refusal_replayed_by_an_effectful_owner_never_waives_retirement(
        RefusalReplay replay)
    {
        var files = new FilesFactory(); var replayer = new ReplayFactory(replay);
        await using var rig = await Rig.CreateAsync((Files, files), (Shelf, replayer));
        await RunBoundedAsync(rig, async rig =>
        {
            var chrome = rig.Chrome!; var workspace = rig.Workspace!;
            var selectedTabId = chrome.State.SelectedTabId;

            // A genuine missing-binding refusal, witnessed by this chrome for its own request.
            Assert.True(workspace.QueueNavigation(new(selectedTabId,
                AppContentAddress.Parse("9to1://boards"), false), Task.CompletedTask));
            var genuine = await Record.ExceptionAsync(() => workspace.OriginalCommand!).AsTask().WaitAsync(Bound);
            Assert.NotNull(genuine);
            Assert.Equal(RefusalMessage, genuine!.Message);
            Assert.True(ReferenceEquals(Assert.Single(chrome.AdmissionRefusals).Refusal, genuine));
            replayer.Captured = genuine;

            // The bound authority takes a real effect and then reproduces the refusal shape.
            Assert.True(workspace.QueueNavigation(new(selectedTabId,
                AppContentAddress.Parse("9to1://shelf"), false), Task.CompletedTask));
            var effectful = workspace.OriginalCommand!;
            Assert.NotNull(await Record.ExceptionAsync(() => effectful).AsTask().WaitAsync(Bound));

            // Fixture fact before any product attribution: the bound factory really ran and
            // really took its effect, and its exact failure is the one being carried.
            Assert.Equal(1, replayer.Effects);
            Assert.NotNull(replayer.Failure);
            Assert.True(HasCause(effectful, replayer.Failure!), Graph(effectful.Exception));

            // Only the real missing-binding admission was witnessed. A replayed refusal does
            // not become that admission's provenance, so the ledger still holds exactly one.
            Assert.True(ReferenceEquals(Assert.Single(chrome.AdmissionRefusals).Refusal, genuine));
            Assert.Contains(replayer.Failure!, FaultedCauses(chrome.OriginalNativeSources));

            // The effectful owner keeps its obligation: the actual host close stays failed
            // and terminal, carrying that exact cause.
            var close = rig.Host.CloseAndDrainAsync();
            rig.AssertedFailedClose = close;
            var first = await Record.ExceptionAsync(() => close).AsTask().WaitAsync(Bound);
            Assert.NotNull(first);
            Assert.True(close.IsFaulted);
            Assert.True(HasCause(close, replayer.Failure!), Graph(close.Exception));
            Assert.Same(close, rig.Host.CloseAndDrainAsync());
        });
    }

    // Negative control for the observed temporal-overlap risk: an unrelated retained
    // operation that is already pending when the chrome witnesses a missing-binding refusal
    // is not that admission's propagation chain, so forwarding the captured refusal after a
    // real effect must leave it a genuine close cause. Driven through the real workspace
    // retention path: the originating loader's action-settlement source.
    [AvaloniaFact]
    public async Task Unrelated_operation_pending_at_the_witness_stays_a_real_close_cause()
    {
        var files = new FilesFactory();
        await using var rig = await Rig.CreateAsync((Files, files));
        await RunBoundedAsync(rig, async rig =>
        {
            var chrome = rig.Chrome!; var workspace = rig.Workspace!;
            var selectedTabId = chrome.State.SelectedTabId;

            // Real retention path: the loader's actual action-settlement source is retained in
            // the workspace operation history and stays pending across the witness below.
            var loader = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(workspace.QueueNavigation(new(selectedTabId,
                AppContentAddress.Parse("9to1://files/first"), false), loader.Task));
            Assert.Contains(loader.Task, workspace.OriginalOperations);

            // The missing-binding admission happens while that unrelated operation is pending.
            var refusal = await Record.ExceptionAsync(() => chrome.NavigateAsync("9to1://boards"))
                .AsTask().WaitAsync(Bound);
            Assert.NotNull(refusal);
            Assert.Equal(RefusalMessage, refusal!.Message);
            Assert.True(ReferenceEquals(Assert.Single(chrome.AdmissionRefusals).Refusal, refusal));

            // Fixture fact first: the unrelated operation really took an effect, and it is the
            // one now carrying the captured refusal.
            TakeEffect();
            Assert.True(loader.TrySetException(refusal));
            var driver = workspace.OriginalCommand!;
            Assert.NotNull(await Record.ExceptionAsync(() => driver).AsTask().WaitAsync(Bound));
            Assert.True(HasCause(driver, refusal), Graph(driver.Exception));

            // The admission's own chain is retired, so any later close failure can only come
            // from the unrelated sources, which stay retained.
            await chrome.NavigateAsync("9to1://files/second").WaitAsync(Bound);
            Assert.Empty(Faulted(chrome.OriginalNativeSources));
            Assert.Contains(loader.Task, workspace.OriginalOperations);
            Assert.Contains(driver, workspace.OriginalCommands);

            var close = rig.Host.CloseAndDrainAsync();
            rig.AssertedFailedClose = close;
            var first = await Record.ExceptionAsync(() => close).AsTask().WaitAsync(Bound);
            Assert.NotNull(first);
            Assert.True(close.IsFaulted);
            Assert.True(HasCause(close, refusal), Graph(close.Exception));
            Assert.Same(close, rig.Host.CloseAndDrainAsync());
        });
    }

    // Negative control for the observed mixed-graph risk: classification uses the source's
    // complete original fault graph, in either fault order, and a genuine sibling keeps the
    // whole source blocking. The terminal failed close task identity is preserved.
    [AvaloniaFact]
    public async Task Refused_admission_sharing_a_graph_with_a_genuine_failure_still_blocks_close()
    {
        var files = new FilesFactory(); var shelf = new FaultyFactory();
        await using var rig = await Rig.CreateAsync((Files, files), (Shelf, shelf));
        await RunBoundedAsync(rig, async rig =>
        {
            var chrome = rig.Chrome!; var workspace = rig.Workspace!;

            Assert.True(workspace.QueueNavigation(new(chrome.State.SelectedTabId,
                AppContentAddress.Parse("9to1://boards"), false), Task.CompletedTask));
            var refusal = await Record.ExceptionAsync(() => workspace.OriginalCommand!).AsTask().WaitAsync(Bound);
            Assert.NotNull(refusal);
            var genuineRecord = Assert.Single(chrome.AdmissionRefusals);
            Assert.True(ReferenceEquals(genuineRecord.Refusal, refusal));

            // Same source graph, both orderings: the genuine sibling must keep it blocking.
            var saveFailure = new IOException("The original session save failed.");
            var refusalFirst = Task.FromException(new AggregateException((Exception)refusal!, saveFailure));
            var siblingFirst = Task.FromException(new AggregateException(saveFailure, (Exception)refusal!));
            Assert.False(chrome.TryRetireAdmissionSource(refusalFirst));
            Assert.False(chrome.TryRetireAdmissionSource(siblingFirst));

            // End to end: a refused no-effect admission and a genuine acquired-owner fault
            // coexist in one chrome. The genuine cause still blocks close permanently.
            Assert.True(workspace.QueueNavigation(new(chrome.State.SelectedTabId,
                AppContentAddress.Parse("9to1://shelf"), false), Task.CompletedTask));
            var effectful = workspace.OriginalCommand!;
            Assert.NotNull(await Record.ExceptionAsync(() => effectful).AsTask().WaitAsync(Bound));
            Assert.True(HasCause(effectful, shelf.Failure));

            var close = rig.Host.CloseAndDrainAsync();
            rig.AssertedFailedClose = close;
            var first = await Record.ExceptionAsync(() => close).AsTask().WaitAsync(Bound);
            Assert.NotNull(first);
            Assert.True(close.IsFaulted);
            Assert.True(HasCause(close, shelf.Failure));
            Assert.Same(close, rig.Host.CloseAndDrainAsync());
            Assert.Single(chrome.AdmissionRefusals);
        });
    }

    private static Task[] Faulted(IReadOnlyList<Task> sources) => sources.Where(actual => actual.IsFaulted).ToArray();

    // Renders the exact production fault graph, so a classification assertion says which
    // node it rejected instead of only that it rejected one.
    private static string Graph(Exception? error, int depth = 0)
    {
        if (error is null) return string.Empty;
        var shape = error is AggregateException group
            ? string.Join(" | ", group.InnerExceptions.Select(inner => Graph(inner, depth + 1)))
            : Graph(error.InnerException, depth + 1);
        var prefix = new string(' ', depth * 2);
        return error is AggregateException
            ? $"{prefix}AGG[{shape}]"
            : $"{prefix}{error.GetType().Name}: {error.Message}{(shape.Length == 0 ? string.Empty : " -> " + shape)}";
    }

    private static Exception[] FaultedCauses(IReadOnlyList<Task> sources) =>
        sources.Where(actual => actual.IsFaulted).SelectMany(actual => actual.Exception!.InnerExceptions).ToArray();

    // Compares the exact retained diagnostic identity through the whole exception graph:
    // aggregate members and single-inner wrappers, because the host re-wraps on each attempt.
    private static bool HasCause(Task source, Exception cause) => HasCause(source.Exception, cause, []);
    private static bool HasCause(Exception? error, Exception cause, HashSet<Exception> seen)
    {
        if (error is null || !seen.Add(error)) return false;
        if (ReferenceEquals(error, cause)) return true;
        if (HasCause(error.InnerException, cause, seen)) return true;
        return error is AggregateException group && group.InnerExceptions.Any(inner => HasCause(inner, cause, seen));
    }

    private static readonly ModeDefinition Files = BuiltInModeSeed.Modes.Single(mode => mode.Key == "files");
    private static readonly ModeDefinition Shelf = BuiltInModeSeed.Modes.Single(mode => mode.Key == "shelf");
    private static readonly ModeDefinition Assistants = BuiltInModeSeed.Modes.Single(mode => mode.Key == "assistants");

    private sealed class Rig : IAsyncDisposable
    {
        internal readonly App App = new();
        internal readonly Paths Paths = new();
        internal readonly ServiceProvider Services = new ServiceCollection().BuildServiceProvider();
        internal readonly CancellationTokenSource AppLifetime = new(), WindowLifetime = new();
        internal readonly ClassicDesktopStyleApplicationLifetime Desktop = new();
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly App.UnifiedWorkspaceHost Host;
        internal NativeAppContentRegistry? Registry;
        internal BrowseChrome? Chrome; internal BrowseNativeWorkspace? Workspace;
        internal readonly List<CuiSceneHost> _sceneHosts = [];
        // The exact host-close task the test body asserted as failing. Cleanup only tolerates
        // that same task identity; it never waives an unasserted or unknown failure.
        internal Task? AssertedFailedClose;
        private bool _cleanedUp;
        private Rig()
        {
            Directory.CreateDirectory(Paths.DataDirectory);
            Home = new(new FileHomeCoreStateStore(Path.Combine(Paths.DataDirectory, "home.json")),
                new OperatingSystemPrincipalSource(), Paths, new("native.admission." + Guid.NewGuid().ToString("N")));
            Host = new(App, Services, Home, AppLifetime.Token, WindowLifetime.Token);
            Set(App, "_actualDesktop", Desktop); Set(App, "_actualUnifiedWorkspace", Host);
            Set(App, "_actualDesktopBorrowers", new object[] { Host });
        }
        internal static async Task<Rig> CreateAsync(params (ModeDefinition App, IBrowseAppContentFactory Factory)[] bindings)
        {
            var rig = new Rig();
            try
            {
                await rig.Home.StartOriginalAsync().WaitAsync(Bound);
                var profile = await rig.Home.Profiles.GetCurrentAsync(CancellationToken.None).AsTask().WaitAsync(Bound);
                Assert.NotNull(profile);
                rig.Registry = new NativeAppContentRegistry(new Modes(), bindings.Select(binding =>
                    new NativeAppContentRegistration(binding.App.Id, binding.App.Key, HavenAppRoutePolicy.Resolve(binding.App).Kind, binding.Factory)),
                    () => rig.Host.OriginalClose is null);
                var creation = BrowseChrome.CreateAsync(rig.Paths, appContentFactory: rig.Registry, homeProfileId: profile!.ProfileId);
                rig.Chrome = await creation.WaitAsync(Bound);
                await rig.Chrome.NavigateAsync("9to1://files/fixture").WaitAsync(Bound);
                rig.Workspace = new(rig.Chrome, _ => true);
                await rig.PublishNativeSceneAsync(rig.Workspace).WaitAsync(Bound);
                Set(rig.Host, "_initialization", creation); Set(rig.Host, "_chromeCreation", creation);
                Set(rig.Host, "_chrome", rig.Chrome); Set(rig.Host, "_workspace", rig.Workspace);
                return rig;
            }
            catch { await rig.CleanupAsync(); throw; }
        }
        // Production publication sequence (App.UnifiedWorkspaceHost): the actual CUI scene
        // reports readiness through CuiSceneHost and only then may the workspace accept an
        // original action. Readiness is never assigned by reflection here.
        internal async Task<CuiSceneHost> PublishNativeSceneAsync(BrowseNativeWorkspace workspace)
        {
            var scene = BrowseNativeSurface.CreateScene(workspace, new SceneReadiness());
            var host = new CuiSceneHost(scene.ControlRegistry);
            workspace.ObserveReadiness(await host.ShowAsync(scene, CancellationToken.None).WaitAsync(Bound));
            _sceneHosts.Add(host);
            return host;
        }
        // Same disposable profile and same real registry: after an accepted host close a fresh
        // Browse workspace and native view must be admittable, and the saved session must come
        // back with its original tab identity. This is a reopened workspace in the same
        // host/profile, not a new process.
        internal async Task ReopenNativeWorkspaceAsync(Guid expectedTabId, string expectedAddress)
        {
            var profile = await Home.Profiles.GetCurrentAsync(CancellationToken.None).AsTask().WaitAsync(Bound);
            Assert.NotNull(profile);
            var alive = true;
            var registry = new NativeAppContentRegistry(new Modes(), new[] { new NativeAppContentRegistration(Files.Id, Files.Key,
                HavenAppRoutePolicy.Resolve(Files).Kind, new FilesFactory()) }, () => alive);
            var chrome = await BrowseChrome.CreateAsync(Paths, appContentFactory: registry, homeProfileId: profile!.ProfileId).WaitAsync(Bound);
            var restored = chrome.State.Tabs.Single(tab => tab.Id == expectedTabId);
            Assert.Equal(expectedAddress, restored.Address.Value);
            var workspace = new BrowseNativeWorkspace(chrome, _ => true);
            await PublishNativeSceneAsync(workspace).WaitAsync(Bound);
            await workspace.DisposeAsync().AsTask().WaitAsync(Bound);
            alive = false;
            Assert.NotNull(chrome.OriginalClose);
            Assert.True(chrome.OriginalClose!.IsCompletedSuccessfully);
        }
        // Always attempts the appropriate bounded cleanup and returns every failure it observed
        // instead of throwing the last one, so a body failure is never replaced by cleanup noise.
        internal async Task<IReadOnlyList<Exception>> CleanupAsync()
        {
            if (_cleanedUp) return [];
            _cleanedUp = true;
            var failures = new List<Exception>();
            var close = await Record.ExceptionAsync(() => Host.CloseAndDrainAsync().WaitAsync(Bound)).AsTask();
            if (close is not null && !ReferenceEquals(Host.OriginalClose, AssertedFailedClose)) failures.Add(close);
            // Evidence-based: the product keeps a failed actual app close's native view and
            // published scene mounted, so only a genuinely clean retirement withdraws scenes.
            if (Host.OriginalClose is { IsCompletedSuccessfully: true })
                foreach (var scene in _sceneHosts)
                    await AddFailureAsync(failures, () => scene.CloseOriginalAsync().WaitAsync(Bound));
            await AddFailureAsync(failures, () => Home.CloseAndDrainAsync().WaitAsync(Bound));
            await AddFailureAsync(failures, () => Services.DisposeAsync().AsTask().WaitAsync(Bound));
            AppLifetime.Dispose(); WindowLifetime.Dispose();
            try { Directory.Delete(Paths.DataDirectory, true); } catch (Exception failure) { failures.Add(failure); }
            return failures;
        }
        private static async Task AddFailureAsync(List<Exception> failures, Func<Task> action)
        {
            var failure = await Record.ExceptionAsync(action);
            if (failure is not null) failures.Add(failure);
        }
        public async ValueTask DisposeAsync()
        {
            var failures = await CleanupAsync();
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("The bounded cleanup failed.", failures);
        }
    }

    // The scene readiness the actual CUI scene host queries; the product's own
    // App.UnifiedWorkspaceHost supplies the same shape for the real Haven shell.
    private sealed class SceneReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "native-admission-fixture",
                "Explicit local Browse controls fixture"));
    }

    private sealed class FilesFactory : IBrowseAppContentFactory
    {
        internal int Creations;
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        { Creations++; return new FilesOwner(request.Address); }
    }
    private sealed class FaultyFactory : IBrowseAppContentFactory
    {
        // One stable exception instance so the negative guard compares exact identity through
        // the production exception graph instead of a wrapper's top-level message.
        internal readonly Exception Failure = new InvalidOperationException("The native surface factory failed after taking an effect.");
        internal int Creations;
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        { Creations++; throw Failure; }
    }

    // A bound authority whose factory takes a real filesystem effect and then reproduces one
    // of the observed replay shapes. Only the composition-issued proof for the exact
    // chrome-minted request may ever be witnessed, so none of these may waive retirement.
    private sealed class ReplayFactory(RefusalReplay replay) : IBrowseAppContentFactory
    {
        internal Exception? Captured, Failure;
        internal int Effects;
        public IBrowseAppContentSession CreateSession(BrowseAppContentRequest request)
        {
            TakeEffect(); Effects++;
            Failure = replay switch
            {
                // Same exception type and message as a genuine refusal, wrong type identity.
                RefusalReplay.Marker => new NotSupportedException(RefusalMessage),
                // The genuine witnessed refusal, forwarded unchanged after taking an effect.
                RefusalReplay.DirectRethrow => Captured!,
                // The genuine refusal inside a task-shaped aggregate.
                RefusalReplay.AggregateForward => new AggregateException(Captured!),
                // The genuine refusal inside an ordinary wrapper.
                _ => new InvalidOperationException(
                    "The native surface took an effect and reused a captured refusal.", Captured),
            };
            throw Failure;
        }
    }

    private static void TakeEffect()
    {
        var path = Path.Combine(Path.GetTempPath(), "r01-replayed-effect-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "effect");
        File.Delete(path);
    }

    private sealed class Modes : IModeRegistry
    {
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) =>
            Task.FromResult<ModeDefinition?>(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Id == id));
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) =>
            Task.FromResult<ModeDefinition?>(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Key == key));
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<ModeDefinition>>(BuiltInModeSeed.Modes.ToArray());
        public Task UpsertModeAsync(ModeDefinition app, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class FilesOwner(AppContentAddress address) : IBrowseNativeAppContentSession
    {
        public AppContentAddress Address { get; } = address;
        public string Title => "Native admission fixture"; public string IconKey => "files";
        public Control OriginalNativeView { get; } = new Border();
        public event EventHandler? StateChanged { add { } remove { } }
        public bool IsPublicationCurrent => OriginalInitialization is { IsCompletedSuccessfully: true } && OriginalClose is null;
        public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };
        public Task? OriginalInitialization { get; private set; } public Task? OriginalClose { get; private set; }
        public Task InitializeAsync(CancellationToken token = default) => OriginalInitialization ??= Task.CompletedTask;
        public Task<bool> TryPrepareToCloseAsync(CancellationToken token = default) => Task.FromResult(true);
        public Task CloseAndDrainAsync() => OriginalClose ??= Task.CompletedTask;
    }

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "native-admission-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }

    private static void Set(object owner, string name, object? value) => owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(owner, value);
}
