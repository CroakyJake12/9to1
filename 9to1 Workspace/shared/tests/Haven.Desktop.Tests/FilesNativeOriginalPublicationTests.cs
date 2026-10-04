using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Security.Principal;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Actual Windows pipe/kernel peer identity, canonical File Home/current actor,
/// manual compatibility approval, real Files provider and actual Avalonia controls.
/// The installed/host verifier is explicitly synthetic. These cases establish neither
/// protected installation nor an adopted package/bootstrap/default provider.</summary>
public sealed class FilesNativeOriginalPublicationTests
{
    [AvaloniaFact]
    public Task Actual_final_startup_read_then_Files_binding_retirement_refuses_initial_native_publication() =>
        WithRigAsync(async rig =>
        {
            rig.Readiness.PauseOnCheck(3);
            var original = rig.View.InitializeAsync(rig.Token);
            await rig.Readiness.Entered.Task.WaitAsync(rig.Token);
            Assert.True(rig.Readiness.LastStartup!.CanStartNormally);
            Assert.Null(rig.View.Content);
            var sameActor = await rig.Profiles.GetCurrentAsync(rig.Token);
            var driveBefore = await File.ReadAllBytesAsync(rig.DrivePath, rig.Token);
            await rig.RetireFilesBindingAsync();
            Assert.Equal(sameActor, await rig.Profiles.GetCurrentAsync(rig.Token));
            Assert.True((await rig.Startup.CheckAsync(rig.Token)).CanStartNormally);
            rig.Readiness.Release();
            var refusal = await Record.ExceptionAsync(() => original);
            Assert.NotNull(refusal);
            Assert.Contains(Flatten(refusal!), error => error is UnauthorizedAccessException);
            rig.Observe(refusal!);
            Assert.Null(rig.View.Content);
            Assert.False(rig.View.IsActionAvailable("9to1.Files.Refresh"));
            Assert.Equal(driveBefore, await File.ReadAllBytesAsync(rig.DrivePath, rig.Token));
            var close = rig.View.CloseAndDrainAsync();
            Assert.Same(close, rig.View.CloseAndDrainAsync());
            var closeFailure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeFailure);
            Assert.Contains(Flatten(closeFailure!), error => ReferenceEquals(error, refusal));
            rig.Observe(closeFailure!);
        });

    [AvaloniaFact]
    public Task Actual_final_startup_read_then_Files_binding_retirement_refuses_new_folder_publication() =>
        WithRigAsync(async rig =>
        {
            await rig.View.InitializeAsync(rig.Token);
            var list = rig.List();
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == rig.Picture);
            rig.Readiness.PauseOnCheck(rig.Readiness.Checks + 2);
            var original = rig.View.DispatchAsync("9to1.Files.Open", null, rig.Token).AsTask();
            await rig.Readiness.Entered.Task.WaitAsync(rig.Token);
            Assert.True(rig.Readiness.LastStartup!.CanStartNormally);
            var actor = await rig.Profiles.GetCurrentAsync(rig.Token);
            var driveBefore = await File.ReadAllBytesAsync(rig.DrivePath, rig.Token);
            await rig.RetireFilesBindingAsync();
            Assert.Equal(actor, await rig.Profiles.GetCurrentAsync(rig.Token));
            rig.Readiness.Release();
            var refusal = await Record.ExceptionAsync(() => original);
            Assert.NotNull(refusal);
            Assert.Contains(Flatten(refusal!), error => error is UnauthorizedAccessException);
            rig.Observe(refusal!);
            Assert.Empty(list.Items);
            Assert.DoesNotContain(list.Items.Cast<HostedItemMetadata>(), item => item.Id == rig.Nested);
            Assert.False(rig.View.IsActionAvailable("9to1.Files.Open"));
            Assert.Equal(driveBefore, await File.ReadAllBytesAsync(rig.DrivePath, rig.Token));
        });

    [AvaloniaFact]
    public Task Actual_selection_notification_close_stops_remaining_folder_publication_and_drains_same_operation() =>
        WithRigAsync(async rig =>
        {
            await rig.View.InitializeAsync(rig.Token);
            var list = rig.List();
            list.SelectedItem = list.Items.Cast<HostedItemMetadata>().Single(item => item.Id == rig.Picture);
            Task? close = null;
            var entered = false;
            System.ComponentModel.PropertyChangedEventHandler handler = (_, _) =>
            {
                if (entered || list.SelectedItem is not null) return;
                entered = true;
                close = rig.View.CloseAndDrainAsync();
                Assert.Same(close, rig.View.CloseAndDrainAsync());
                Assert.Null(rig.View.Content);
                Assert.Empty(list.Items);
            };
            rig.View.PropertyChanged += handler;
            try
            {
                var original = rig.View.DispatchAsync("9to1.Files.Open", null, rig.Token).AsTask();
                var refusal = await Record.ExceptionAsync(() => original);
                Assert.True(entered);
                Assert.NotNull(refusal);
                Assert.Contains(Flatten(refusal!), error => error is ObjectDisposedException or OperationCanceledException);
                rig.Observe(refusal!);
                Assert.NotNull(close);
                var closeFailure = await Record.ExceptionAsync(() => close!);
                Assert.NotNull(closeFailure);
                Assert.Contains(Flatten(closeFailure!), error => ReferenceEquals(error, refusal));
                rig.Observe(closeFailure!);
                Assert.Empty(list.Items);
                Assert.Null(rig.View.Content);
                Assert.False(rig.View.IsActionAvailable("9to1.Files.Open"));
                Assert.True(rig.View.TryGetValue("Status", out var status));
                Assert.NotEqual("1 items", status);
            }
            finally { rig.View.PropertyChanged -= handler; }
        });

    [AvaloniaFact]
    public Task Actual_owner_read_IO_failure_is_handled_then_same_native_Refresh_recovers_and_close_retains_original_cause() =>
        WithRigAsync(async rig =>
        {
            await rig.View.InitializeAsync(rig.Token);
            var list = rig.List();
            var failure = new IOException("EXPLICIT_FILES_OWNER_READ_IO_FIXTURE");
            rig.Readiness.FailNextRead(failure);
            await rig.View.DispatchAsync("9to1.Files.Refresh", null, rig.Token);
            Assert.Empty(list.Items);
            Assert.True(rig.View.TryGetValue("Status", out var status));
            Assert.Contains(failure.Message, Assert.IsType<string>(status), StringComparison.Ordinal);
            Assert.True(rig.View.IsActionAvailable("9to1.Files.Refresh"));
            await rig.View.DispatchAsync("9to1.Files.Refresh", null, rig.Token);
            Assert.Equal(rig.Workspace.Configuration.AppFolders.Count, list.Items.Count);
            Assert.True(rig.View.IsActionAvailable("9to1.Files.Open") == false);
            rig.Observe(failure);
            var close = rig.View.CloseAndDrainAsync();
            var closeFailure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(closeFailure);
            Assert.Contains(Flatten(closeFailure!), error => ReferenceEquals(error, failure));
            rig.Observe(closeFailure!);
        });

    [AvaloniaFact]
    public Task Copied_page_cannot_capture_original_owner_and_genuine_callback_refuses_actual_Files_binding_retirement() =>
        WithRigAsync(async rig =>
        {
            var page = await rig.Browser.ListAsync(rig.Actor, token: rig.Token);
            var copy = page with { };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await rig.Browser.CaptureOriginalPageReadCheckAsync(copy, rig.Actor, () => true, rig.Token));
            var check = await rig.Browser.CaptureOriginalPageReadCheckAsync(page, rig.Actor,
                () => !rig.Lifetime.IsCancellationRequested, rig.Token);
            Assert.True(await check(rig.Token));
            var actor = await rig.Profiles.GetCurrentAsync(rig.Token);
            await rig.RetireFilesBindingAsync();
            Assert.Equal(actor, await rig.Profiles.GetCurrentAsync(rig.Token));
            Assert.False(await check(rig.Token));
            Assert.True((await rig.Startup.CheckAsync(rig.Token)).CanStartNormally);
        });

    private static IEnumerable<Exception> Flatten(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var child in aggregate.InnerExceptions)
                foreach (var same in Flatten(child)) yield return same;
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(same => ReferenceEquals(same, error))) errors.Add(error); }
    private static void Throw(List<Exception> errors)
    {
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Actual Files publication fixture and independent cleanup failed.", errors);
    }
    private static async Task WithRigAsync(Func<Rig, Task> body)
    {
        var rig = new Rig(TestContext.Current.CancellationToken);
        List<Exception> failures = [];
        try { await rig.OpenAsync(); await body(rig); }
        catch (Exception error) { Add(failures, error); }
        finally
        {
            try { await rig.CloseAsync(); } catch (Exception error) { Add(failures, error); }
        }
        Throw(failures);
    }

    private sealed class Rig(CancellationToken originalTestToken)
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "files-windows-original-publication-" + Guid.NewGuid().ToString("N"));
        private readonly string _pipeName = "files.windows.original.fixture." + Guid.NewGuid().ToString("N");
        private readonly List<Exception> _observed = [];
        private CancellationTokenSource? _bound;
        private ServiceProvider? _graph;
        private HomeNativeCoreApiSessions? _sessions;
        private HomeCoreRuntime? _runtime;
        private Task<HomeCoreStateSnapshot>? _originalCoreStart;
        private HomeNativeSessionLease? _lease;
        private HomeNativeWindowsCoreHost? _host;
        private NamedPipeClientStream? _pipe;
        private Window? _window;
        internal readonly CancellationTokenSource Lifetime = new();
        internal FileHomeCoreStateStore Home = null!;
        internal HomeLocalProfileIdentity Profiles = null!;
        internal HomePermissionTrustService Permissions = null!;
        internal NativeFilesWorkspace Workspace = null!;
        internal FilesNativeBrowserService Browser = null!;
        internal AuthenticatedResourceActor Actor = null!;
        internal HomeNativeWindowsStartupSession Startup = null!;
        internal ActualStartupReadiness Readiness = null!;
        internal FilesNativeBrowserSurface View = null!;
        internal HostedItemId Picture;
        internal HostedItemId Nested;
        internal CancellationToken Token => _bound!.Token;
        internal string DrivePath => Path.Combine(_root, "chosen", ".9to1-files", "drive.json");

        internal async Task OpenAsync()
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("These actual pipe/native fixtures require Windows; no substituted kernel pass.");
            _bound = CancellationTokenSource.CreateLinkedTokenSource(originalTestToken);
            _bound.CancelAfter(TimeSpan.FromSeconds(90));
            Directory.CreateDirectory(_root);
            Home = new(Path.Combine(_root, "home.json"));
            Profiles = new(Home, new OperatingSystemPrincipalSource());
            var corePolicy = new HomeCoreServiceReadActionPolicies();
            Permissions = new(Home, corePolicy.TryGet);
            var registrations = new ServiceCollection();
            registrations.AddSingleton<IHomeCoreStateStore>(Home);
            registrations.AddSingleton(Profiles);
            registrations.AddSingleton<IAuthenticatedResourceActorSource>(Profiles);
            registrations.AddSingleton(Permissions);
            registrations.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            registrations.AddSingleton<HomeLocalStoreOwnership>();
            registrations.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            registrations.AddSingleton<ResourceAuthorizationService>();
            registrations.AddSingleton<HomeResourceOperationBroker>();
            registrations.AddFilesNativeHost();
            _graph = registrations.BuildServiceProvider();
            var chosen = Path.Combine(_root, "chosen"); Directory.CreateDirectory(chosen);
            await _graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                _graph.GetRequiredService<HomeLocalStoreOwnership>(), Token);
            Workspace = await _graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(Token)
                ?? throw new UnauthorizedAccessException("The real canonical Files workspace is unavailable.");
            Browser = _graph.GetRequiredService<FilesNativeBrowserService>();
            Actor = Workspace.Actor;
            Picture = Workspace.Configuration.AppFolders["picture"];
            Nested = HostedItemId.New();
            var now = DateTimeOffset.UtcNow;
            Assert.True((await Workspace.Provider.MutateAsync(new(new(Guid.NewGuid()), Actor.ActorId,
                Nested, null, Picture, "CreateFolder", null, null, FilesOperationState.Pending,
                now, now, null, null), "Native child", Token)).IsSuccess);
            using var identity = WindowsIdentity.GetCurrent();
            var principal = "windows-sid:" + (identity.User?.Value
                ?? throw new InvalidOperationException("The actual Windows SID is unavailable."));
            var verifier = new SyntheticInstalledVerifier(Profiles, principal);
            IHomeCoreApi? actual = null;
            _sessions = new(Permissions, Profiles, verifier,
                () => actual ?? throw new InvalidOperationException("The same original Core API is not constructed."));
            _runtime = new([new HomeCoreStateService(Home)], authorization: _sessions);
            actual = new HomeCoreApi(_runtime, _sessions, Profiles);
            _lease = await HomeNativeSessionLease.TryAcquireAsync(Profiles, new Paths(_root), Token)
                ?? throw new InvalidOperationException("The genuine original Home lease is unavailable.");
            _originalCoreStart = _runtime.StartAsync(Token);
            await _originalCoreStart;
            _host = HomeNativeWindowsCoreHost.Start(_sessions, _lease, new(_pipeName), Lifetime.Token);
            await _host.OriginalListeningTask;
            _pipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            await _pipe.ConnectAsync(Token);
            Startup = await HomeNativeWindowsStartupSession.AttachAsync(_pipe, verifier, HostRequirement(),
                AppRequirements(), Lifetime.Token, Token)
                ?? throw new UnauthorizedAccessException("The real Windows startup connection was refused.");
            var pending = await Startup.CheckAsync(Token);
            Assert.Equal(HomeNativeStartupState.AwaitingApproval, pending.State);
            Assert.False(pending.CanStartNormally);
            Assert.True((await Permissions.DecideAsync(pending.PermissionRequestId!, HomeApprovalChoice.Accept,
                cancellationToken: Token)).Succeeded);
            Assert.True((await Startup.CheckAsync(Token)).CanStartNormally);
            Assert.Equal(Environment.ProcessId, verifier.LastClient!.ProcessId);
            Assert.Equal(principal, verifier.LastClient.OperatingSystemPrincipalId);
            Assert.Equal(Environment.ProcessId, verifier.LastHost!.ProcessId);
            Readiness = new(Startup, Browser, Actor);
            View = new(Browser, null, Actor, Readiness, Lifetime.Token);
            _window = new() { Content = View, Width = 720, Height = 480 };
            _window.Show();
        }

        internal ListBox List()
        {
            _window!.UpdateLayout();
            return Assert.Single(View.GetVisualDescendants().OfType<ListBox>());
        }
        internal async Task RetireFilesBindingAsync()
        {
            var record = Assert.Single((await Home.ReadAsync(Token)).State!.Records,
                item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await Home.WriteAsync(record with
            {
                Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked-files-binding-only" })
            }, record.Revision, Token)).IsSuccess);
        }
        internal void Observe(Exception error)
        { foreach (var same in Flatten(error)) Add(_observed, same); }
        private bool IsObserved(Exception error) =>
            Flatten(error).All(same => _observed.Any(value => ReferenceEquals(value, same)));
        internal async Task CloseAsync()
        {
            List<Exception> failures = [];
            async Task Settle(Func<Task> original)
            {
                try { await original(); }
                catch (Exception error) { if (!IsObserved(error)) Add(failures, error); }
            }
            Readiness?.Release();
            if (View is not null) await Settle(View.CloseAndDrainAsync);
            try { _window?.Close(); } catch (Exception error) { Add(failures, error); }
            if (Startup is not null) await Settle(Startup.CloseAndDrainAsync);
            if (_pipe is not null) await Settle(() => _pipe.DisposeAsync().AsTask());
            if (_host is not null) await Settle(_host.CloseAndDrainAsync);
            if (_sessions is not null) await Settle(() => _sessions.DisposeAsync().AsTask());
            if (_originalCoreStart is not null) await Settle(() => _originalCoreStart);
            if (_runtime is not null) await Settle(() => _runtime.DisposeAsync().AsTask());
            try { _lease?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { Lifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { _graph?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { _bound?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            catch (Exception error) { Add(failures, error); }
            Throw(failures);
        }
    }

    private static HomeNativeSessionHostRequirement HostRequirement() =>
        new("synthetic-files-home", "EXPLICIT_SYNTHETIC_FILES_HOST_NO_PROTECTED_INSTALLATION");
    private static HomeCompatibilityRequest AppRequirements() =>
        new("synthetic-files-native", "SOURCE_FIXTURE_ONLY", [new("home.core", 1), new("home.state", 1)]);

    private sealed class SyntheticInstalledVerifier(HomeLocalProfileIdentity originalProfiles, string principal) :
        IHomeNativeInstalledPeerOriginalActorVerifier, IHomeNativeSessionHostVerifier
    {
        private readonly Guid _app = Guid.NewGuid();
        private readonly Guid _host = Guid.NewGuid();
        internal HomeNativeObservedPeer? LastClient;
        internal HomeNativeObservedPeer? LastHost;
        private bool Actual(HomeNativeObservedPeer observed) =>
            observed.ProcessId == Environment.ProcessId && observed.OperatingSystemPrincipalId == principal;
        private HomeNativeInstalledPeer App() => new("synthetic-files-native", _app, "fixture-v1",
            "EXPLICIT_SYNTHETIC_RECEIPT_NO_PROTECTED_INSTALLATION", new HashSet<string> { "home.core", "home.state" });
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(observed) ? App() : null); }
        public async ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor expected, CancellationToken token)
        {
            LastClient = observed;
            if (!Actual(observed) || await originalProfiles.GetCurrentAsync(token) != expected) return null;
            return Actual(observed) && await originalProfiles.GetCurrentAsync(token) == expected ? App() : null;
        }
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observed,
            HomeNativeSessionHostRequirement required, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); LastHost = observed;
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(Actual(observed) && required == HostRequirement()
                ? new("synthetic-files-home", _host, "fixture-v1",
                    "EXPLICIT_SYNTHETIC_HOST_NO_PROTECTED_INSTALLATION", new HashSet<string> { "home.core", "home.state" })
                { Roles = new HashSet<string> { HomeNativeSessionHostRequirement.RequiredRole } } : null);
        }
    }

    private sealed class ActualStartupReadiness(IHomeNativeStartupSession startup,
        FilesNativeBrowserService browser, AuthenticatedResourceActor originalActor) : ICuiSceneReadiness
    {
        private int _pauseAt = -1;
        private Exception? _nextFault;
        private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Checks;
        internal HomeNativeStartupObservation? LastStartup;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void PauseOnCheck(int check)
        {
            _pauseAt = check;
            Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        internal void FailNextRead(Exception originalFault) => _nextFault = originalFault;
        internal void Release() => _release.TrySetResult();
        public async ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            Checks++;
            LastStartup = await startup.CheckAsync(token);
            if (!LastStartup.CanStartNormally)
                return new(CuiSceneAvailabilityState.Unavailable, "ActualStartupDenied", "Actual original Home startup unavailable.");
            await browser.ListAsync(originalActor, token: token);
            if (_nextFault is { } original)
            {
                _nextFault = null;
                ExceptionDispatchInfo.Capture(original).Throw();
            }
            if (Checks == _pauseAt)
            {
                Entered.TrySetResult();
                await _release.Task.WaitAsync(token);
            }
            return new(CuiSceneAvailabilityState.Ready, "ActualOriginalStartupAndFilesOwner",
                "Actual Home compatibility and canonical Files owner observed; synthetic installed verifier.");
        }
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}
