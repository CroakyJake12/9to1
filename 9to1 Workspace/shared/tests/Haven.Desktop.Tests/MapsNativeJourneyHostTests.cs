using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Views.Pages.Maps;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class MapsNativeJourneyHostTests
{
    [AvaloniaFact]
    public async Task Actual_host_mount_preserves_Map_and_routes_exact_Home_native_individual_review_before_actual_Journey_save()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(fixture.Home),
            new HomePermissionsCoreService(fixture.Permissions, fixture.Profiles)]);
        using var home = new HomeApprovalCuiSurface(runtime, fixture.Profiles, fixture.Permissions);
        string? focused = null;
        var legacyMap = new UserControl(); var current = true;
        using var host = new MapsNativeJourneyHost(legacyMap, fixture.Owner, fixture.Profiles, () => current, async (request, originalActor) =>
        {
            focused = request; await home.InitializeAsync(); Assert.True(await home.FocusRequestAsync(request));
        });
        var window = new Window { Content = host }; window.Show();
        var homeWindow = new Window { Content = home };
        try
        {
            Assert.Same(legacyMap, host.LegacyMapsPage);
            Click(host.JourneyButton); await host.WhenActionsIdleAsync();
            var panel = Assert.IsType<MapsJourneyLibraryPanel>(host.JourneyPanel);
            panel.NameInput.Text = "Actual original Journey"; panel.InstructionInput.Text = "Use the footbridge";
            var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            var request = Assert.Single(panel.Reviews).RequestID;
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            Assert.Equal("ApprovalRequired", Assert.Single(panel.Reviews).LastObservation!.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Click(panel.HomeButton); await panel.WhenActionsIdleAsync(); homeWindow.Show();
            Assert.Equal(request, focused);
            Assert.Equal(HomePermissionRequestState.PendingApproval, (await fixture.Permissions.GetAuthorizationAsync(request)).State);
            var accept = Assert.Single(home.GetVisualDescendants().OfType<Button>(), value => Equals(value.Content, "Accept once"));
            Click(accept);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!(await fixture.Permissions.GetAuthorizationAsync(request, timeout.Token)).IsAllowed)
                await Task.Delay(10, timeout.Token);
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            Assert.True(Assert.Single(panel.Reviews).LastObservation!.Committed);
            var saved = Assert.Single((await new MapsJourneyService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync()).Journeys);
            Assert.Equal("Use the footbridge", Assert.Single(saved.Steps).Instruction);
            Assert.Empty((await fixture.Permissions.GetSnapshotAsync()).Grants);
            Click(host.MapButton); Assert.Contains(legacyMap, host.GetVisualDescendants());
            Assert.DoesNotContain(panel, host.GetVisualDescendants());
            Click(host.JourneyButton); await host.WhenActionsIdleAsync(); Assert.Same(panel, host.JourneyPanel);
            current = false; host.RefreshAvailability(); Assert.True(host.IsRetired);
            Assert.False(host.JourneyButton.IsEnabled);
            // Closing or replacing the host never replays its original approved edit.
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(saved.JourneyId, Assert.Single((await fixture.Library.ReadAsync()).Journeys).JourneyId);
        }
        finally { homeWindow.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Provider_retirement_during_actual_owner_identity_read_never_mounts_a_late_panel_or_adopts_new_actor()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var current = true;
        using var host = new MapsNativeJourneyHost(new UserControl(), fixture.Owner, fixture.Profiles, () => current, (_, _) => Task.CompletedTask);
        var window = new Window { Content = host }; window.Show();
        try
        {
            fixture.Settings.HoldIdentity = true;
            Click(host.JourneyButton);
            await fixture.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var bytes = await File.ReadAllBytesAsync(fixture.SettingsFile);
            current = false; fixture.Settings.Release.TrySetResult();
            await host.WhenActionsIdleAsync();
            Assert.True(host.IsRetired); Assert.Null(host.JourneyPanel);
            Assert.False(host.JourneyButton.IsEnabled);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Empty((await fixture.Permissions.GetSnapshotAsync()).PendingRequests);
        }
        finally { fixture.Settings.Release.TrySetResult(); window.Close(); }
    }
    [AvaloniaFact]
    public async Task Actual_host_replacement_during_held_Settings_final_publication_retires_same_panel_and_preserves_physical_bytes()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var current = true;
        using var host = new MapsNativeJourneyHost(new UserControl(), fixture.Owner, fixture.Profiles, () => current, (_, _) => Task.CompletedTask);
        var window = new Window { Content = host }; window.Show();
        try
        {
            Click(host.JourneyButton); await host.WhenActionsIdleAsync();
            var panel = Assert.IsType<MapsJourneyLibraryPanel>(host.JourneyPanel);
            panel.NameInput.Text = "Actual held host publication"; panel.InstructionInput.Text = "Wait for the ferry";
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            var request = Assert.Single(panel.Reviews).RequestID;
            Assert.True((await fixture.Permissions.DecideAsync(request, HomeApprovalChoice.Accept)).Succeeded);
            var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
            fixture.Settings.HoldPublication = true;
            Click(panel.ApplyButton);
            await fixture.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            current = false; host.RefreshAvailability();
            var statusAtRetirement = panel.Status.Text;
            fixture.Settings.Release.TrySetResult();
            await panel.WhenActionsIdleAsync();
            Assert.False(Assert.Single(panel.Reviews).LastObservation!.Committed);
            Assert.Equal(statusAtRetirement, panel.Status.Text); // Closed panel receives no late presentation.
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Empty((await fixture.Library.ReadAsync()).Journeys);
            Assert.True(host.IsRetired); Assert.False(host.JourneyButton.IsEnabled);
        }
        finally { fixture.Settings.Release.TrySetResult(); window.Close(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public string SettingsFile => Path.Combine(Paths.DataDirectory, "settings.json");
        public FileHomeCoreStateStore Home { get; }
        public HomeLocalProfileIdentity Profiles { get; }
        public HomePermissionTrustService Permissions { get; }
        public HomeMapsLibraryOwner Owner { get; private set; } = null!;
        public MapsJourneyService Library { get; }
        public HeldSettings Settings { get; }
        public Fixture()
        {
            Settings = new(new VersionedAtomicSettingsStore(Paths)); Library = new(Settings);
            Home = new(Path.Combine(Paths.DataDirectory, "home.json"));
            Profiles = new(Home, new OperatingSystemPrincipalSource());
            Permissions = new(Home, new MapsOwnedLibraryActionPolicies().TryGet);
        }
        public async Task InitializeAsync()
        {
            var actor = await Profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual local actor required.");
            var identity = await Settings.GetStoreIdentityAsync(default);
            var ownership = new HomeLocalStoreOwnership(Home, Profiles,
                new HomeLocalStoreEvidenceRegistry([new MapsOwnedLibraryEvidence(Settings)]), Permissions);
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, Profiles);
            var resolver = new MapsOwnedLibraryAccessResolver(Library, Profiles, authority);
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(Profiles, [resolver]), Permissions);
            Owner = new(Library, Profiles, authority, broker, Permissions,
                new HomeOwnedLibraryCommitFenceSource(Home, Profiles, authority, broker));
            await ownership.BindNewEmptyAsync("maps", identity.StoreId.ToString("D"));
        }
        public void Dispose() { try { Directory.Delete(Paths.DataDirectory, true); } catch (IOException) { } }
    }
    private sealed class HeldSettings(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore,
        IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
    {
        public bool HoldIdentity { get; set; }
        public bool HoldPublication { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken ct)
        {
            var actualIdentity = await actual.GetStoreIdentityAsync(ct);
            if (HoldIdentity) { HoldIdentity = false; Entered.TrySetResult(); await Release.Task.WaitAsync(ct); }
            return actualIdentity;
        }
        public Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class => actual.GetAsync<T>(key, ct);
        public Task SetAsync<T>(string key, T value, CancellationToken ct) where T : class => actual.SetAsync(key, value, ct);
        public Task RemoveAsync(string key, CancellationToken ct) => actual.RemoveAsync(key, ct);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken ct) => actual.ExportAsync(ct);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken ct) => actual.ImportAsync(value, ct);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken ct)
            => actual.CompareExchangeAsync(key, expected, replacement, ct);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, CancellationToken ct) => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, ct);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected, string? replacement,
            IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken ct)
            => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, new HeldAdmission(this, admission), ct);
        private sealed class HeldAdmission(HeldSettings owner, ISettingsCommitAdmission original) : ISettingsCommitAdmission
        {
            public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken ct)
            {
                if (owner.HoldPublication && context.Phase == SettingsCommitPhase.Publication)
                { owner.HoldPublication = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(ct); }
                return await original.CheckAsync(context, ct);
            }
        }
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-maps-native-host-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
        public void EnsureCreated() => Directory.CreateDirectory(DataDirectory);
    }
}
