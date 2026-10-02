using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Maps;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class MapsJourneyLibraryPanelTests
{
    [AvaloniaFact]
    public async Task Actual_Journey_panel_requires_individual_Home_approval_and_Finish_never_applies_pending_request()
    {
        using var fixture = new Fixture();
        await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        var window = new Window { Content = panel }; window.Show();
        try
        {
            panel.NameInput.Text = "Walking instructions"; panel.InstructionInput.Text = "Turn left at the library";
            var originalBytes = await File.ReadAllBytesAsync(fixture.SettingsFile);
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            var review = Assert.Single(panel.Reviews);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.Equal("NoAttemptedOutcome", Assert.Single(panel.Reviews).LastObservation!.Code);
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            Assert.Equal("ApprovalRequired", Assert.Single(panel.Reviews).LastObservation!.Code);
            Assert.Empty((await fixture.Library.ReadAsync()).Journeys);
            await fixture.Permissions.DecideAsync(review.RequestID, HomeApprovalChoice.Accept);
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            Assert.True(Assert.Single(panel.Reviews).LastObservation!.Committed);
            var committedBytes = await File.ReadAllBytesAsync(fixture.SettingsFile);
            var journey = Assert.Single((await new MapsJourneyService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync()).Journeys);
            Assert.Equal("Turn left at the library", Assert.Single(journey.Steps).Instruction);
            Click(panel.ReloadButton); await panel.WhenActionsIdleAsync();
            var displayed = Assert.IsType<TextBlock>(Assert.Single(panel.JourneyItems.Children));
            Assert.Equal(journey.Name, displayed.Text); Assert.Equal(journey.JourneyId, Assert.IsType<Guid>(displayed.Tag));
            Assert.Equal(committedBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Equal(review.RequestID, Assert.Single(panel.Reviews).RequestID);
            panel.Dispose();
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.True(Assert.Single(panel.Reviews).LastObservation!.Committed);
            Assert.Equal(committedBytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Actual_panel_close_retains_exact_pending_Home_request_for_explicit_decline_without_write()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        panel.NameInput.Text = "Closed panel"; panel.InstructionInput.Text = "Wait";
        Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
        var request = Assert.Single(panel.Reviews).RequestID;
        var bytes = await File.ReadAllBytesAsync(fixture.SettingsFile);
        panel.Dispose();
        await fixture.Permissions.DecideAsync(request, HomeApprovalChoice.Decline);
        Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
        Assert.Equal(request, Assert.Single(panel.Reviews).RequestID);
        Assert.Equal("NoAttemptedOutcome", Assert.Single(panel.Reviews).LastObservation!.Code);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Empty((await fixture.Library.ReadAsync()).Journeys);
    }
    [AvaloniaFact]
    public async Task Actual_routed_Review_cannot_adopt_replacement_settings_UUID_or_write_foreign_root()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        panel.NameInput.Text = "Stale root"; panel.InstructionInput.Text = "Wait";
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.SettingsFile))!.AsObject();
        envelope[nameof(SettingsExportManifest.StoreIdentity)]![nameof(SettingsStoreIdentity.StoreId)] = Guid.NewGuid();
        var foreign = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await File.WriteAllBytesAsync(fixture.SettingsFile, foreign);
        var home = await File.ReadAllBytesAsync(fixture.HomeFile);
        Click(panel.ReviewButton); await panel.WhenActionsIdleAsync(); // Real first changed-root refresh refusal.
        Click(panel.ReviewButton); await panel.WhenActionsIdleAsync(); // Cached new UUID still denies old token.
        Click(panel.ReloadButton); await panel.WhenActionsIdleAsync();
        Assert.Empty(panel.JourneyItems.Children);
        Assert.Empty(panel.Reviews);
        Assert.Equal(foreign, await File.ReadAllBytesAsync(fixture.SettingsFile));
        Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
    }
    [AvaloniaFact]
    public async Task Actual_native_panel_close_during_real_settings_publication_revokes_original_display_and_preserves_bytes()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        var window = new Window { Content = panel }; window.Show();
        try
        {
            panel.NameInput.Text = "Held publication"; panel.InstructionInput.Text = "Required instruction";
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            var request = Assert.Single(panel.Reviews).RequestID;
            Assert.True((await fixture.Permissions.DecideAsync(request, HomeApprovalChoice.Accept)).Succeeded);
            var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
            fixture.Settings.HoldPublication = true;
            Click(panel.ApplyButton); var accepted = panel.WhenActionsIdleAsync();
            try
            {
                await fixture.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(accepted.IsCompleted);
                // Actual inner settings lease is held BEFORE final original admission check.
                window.Close(); panel.Dispose();
            }
            finally { fixture.Settings.Release.TrySetResult(); }
            await accepted.WaitAsync(TimeSpan.FromSeconds(10));
            var outcome = Assert.Single(panel.Reviews).LastObservation!;
            Assert.False(outcome.Committed); Assert.True(outcome.AuditRecorded);
            Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Empty((await fixture.Library.ReadAsync()).Journeys);
            var home = await File.ReadAllBytesAsync(fixture.HomeFile);
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(request, Assert.Single(panel.Reviews).RequestID);
            Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
        }
        finally { fixture.Settings.Release.TrySetResult(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actual_native_panel_lost_physical_publication_return_stays_unknown_until_original_receipt_and_Finish_never_replays()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        var window = new Window { Content = panel }; window.Show();
        try
        {
            panel.NameInput.Text = "Lost return"; panel.InstructionInput.Text = "Keep original instruction";
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            var request = Assert.Single(panel.Reviews).RequestID;
            Assert.True((await fixture.Permissions.DecideAsync(request, HomeApprovalChoice.Accept)).Succeeded);
            fixture.Settings.LoseWriteReturnOnce = true; fixture.Settings.HideAfterLostReturn = true;
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            var unknown = Assert.Single(panel.Reviews).LastObservation!;
            Assert.False(unknown.Committed); Assert.True(unknown.CompletionUnknown); Assert.False(unknown.AuditRecorded);
            Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            var committed = await File.ReadAllBytesAsync(fixture.SettingsFile);
            var home = await File.ReadAllBytesAsync(fixture.HomeFile);
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.True(Assert.Single(panel.Reviews).LastObservation!.CompletionUnknown);
            Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
            Assert.Equal(committed, await File.ReadAllBytesAsync(fixture.SettingsFile));
            fixture.Settings.HideLibraryReads = false;
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            var recovered = Assert.Single(panel.Reviews).LastObservation!;
            Assert.True(recovered.Committed); Assert.True(recovered.AuditRecorded);
            Assert.False(recovered.CompletionUnknown);
            Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            Assert.Equal(committed, await File.ReadAllBytesAsync(fixture.SettingsFile));
            var journey = Assert.Single((await fixture.Library.ReadAsync()).Journeys);
            Assert.Equal("Keep original instruction", journey.Steps.Single().Instruction);
            window.Close(); panel.Dispose();
            var terminalHome = await File.ReadAllBytesAsync(fixture.HomeFile);
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            Assert.Equal(terminalHome, await File.ReadAllBytesAsync(fixture.HomeFile));
            Assert.Equal(request, Assert.Single(panel.Reviews).RequestID);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Native_original_display_factory_preserves_host_token_and_denies_replacement_UUID_before_UI_construction()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var original = await fixture.Owner.LoadForDisplayAsync(fixture.Actor);
        using (var panel = await MapsJourneyLibraryPanel.CreateForOriginalDisplayAsync(fixture.Owner, original.Selection, _ => Task.CompletedTask))
        {
            panel.NameInput.Text = "Host original"; panel.InstructionInput.Text = "Wait";
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            Assert.Single(panel.Reviews);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ReloadForOriginalDisplayAsync(original.Selection));
        var next = await fixture.Owner.LoadForDisplayAsync(fixture.Actor);
        var envelope = JsonNode.Parse(await File.ReadAllTextAsync(fixture.SettingsFile))!.AsObject();
        envelope[nameof(SettingsExportManifest.StoreIdentity)]![nameof(SettingsStoreIdentity.StoreId)] = Guid.NewGuid();
        var foreign = JsonSerializer.SerializeToUtf8Bytes(envelope);
        await File.WriteAllBytesAsync(fixture.SettingsFile, foreign);
        var home = await File.ReadAllBytesAsync(fixture.HomeFile);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => MapsJourneyLibraryPanel.CreateForOriginalDisplayAsync(
                fixture.Owner, next.Selection, _ => Task.CompletedTask));
            Assert.Equal(foreign, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
        }
        finally { next.Selection.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Original_host_lifetime_guard_survives_factory_reload_and_revokes_real_final_publication()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        var hostCurrent = true;
        var original = await fixture.Owner.LoadForDisplayAsync(fixture.Actor, originalLifetime: () => hostCurrent);
        using var panel = await MapsJourneyLibraryPanel.CreateForOriginalDisplayAsync(fixture.Owner, original.Selection, _ => Task.CompletedTask);
        var window = new Window { Content = panel }; window.Show();
        try
        {
            panel.NameInput.Text = "Original host lifetime"; panel.InstructionInput.Text = "Preserve intent";
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            var request = Assert.Single(panel.Reviews).RequestID;
            Assert.True((await fixture.Permissions.DecideAsync(request, HomeApprovalChoice.Accept)).Succeeded);
            var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
            fixture.Settings.HoldPublication = true;
            Click(panel.ApplyButton); var accepted = panel.WhenActionsIdleAsync();
            try
            {
                await fixture.Settings.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                hostCurrent = false; // Mirrors actual hostDisposed/provider ReferenceEquals pure fence.
            }
            finally { fixture.Settings.Release.TrySetResult(); }
            await accepted.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(Assert.Single(panel.Reviews).LastObservation!.Committed);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ReloadForOriginalDisplayAsync(original.Selection));
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
        }
        finally { fixture.Settings.Release.TrySetResult(); window.Close(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public string SettingsFile => Path.Combine(Paths.DataDirectory, "settings.json");
        public string HomeFile => Path.Combine(Paths.DataDirectory, "home.json");
        public HomePermissionTrustService Permissions { get; }
        public HomeMapsLibraryOwner Owner { get; private set; } = null!;
        public AuthenticatedResourceActor Actor { get; private set; } = null!;
        public MapsJourneyService Library { get; }
        public HeldSettings Settings { get; }
        private readonly HeldSettings _settings;
        private readonly FileHomeCoreStateStore _home;
        private readonly HomeLocalProfileIdentity _profiles;
        public Fixture()
        {
            Settings = new HeldSettings(new VersionedAtomicSettingsStore(Paths)); _settings = Settings; Library = new(_settings);
            _home = new(Path.Combine(Paths.DataDirectory, "home.json"));
            _profiles = new(_home, new OperatingSystemPrincipalSource());
            var policy = new MapsOwnedLibraryActionPolicies();
            Permissions = new(_home, policy.TryGet);
        }
        public async Task InitializeAsync()
        {
            Actor = await _profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual local Home actor required.");
            var identity = await _settings.GetStoreIdentityAsync(default);
            var ownership = new HomeLocalStoreOwnership(_home, _profiles,
                new HomeLocalStoreEvidenceRegistry([new MapsOwnedLibraryEvidence(_settings)]), Permissions);
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, _profiles);
            var resolver = new MapsOwnedLibraryAccessResolver(Library, _profiles, authority);
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(_profiles, [resolver]), Permissions);
            Owner = new(Library, _profiles, authority, broker, Permissions,
                new HomeOwnedLibraryCommitFenceSource(_home, _profiles, authority, broker));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Owner.LoadForDisplayAsync(Actor));
            await ownership.BindNewEmptyAsync("maps", identity.StoreId.ToString("D"));
        }
        public void Dispose() { try { Directory.Delete(Paths.RootDirectory, true); } catch (IOException) { } }
    }
    private sealed class HeldSettings(VersionedAtomicSettingsStore actual) : IVersionedSettingsStore,
        IResourceStoreIdentitySource, IVersionedSettingsGuardedCompareExchange
    {
        public bool LoseWriteReturnOnce { get; set; }
        public bool HideAfterLostReturn { get; set; }
        public bool HideLibraryReads { get; set; }
        public int GuardedWriteCalls { get; private set; }
        public bool HoldIdentity { get; set; }
        public bool HoldPublication { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token)
        {
            var identity = await actual.GetStoreIdentityAsync(token);
            if (HoldIdentity) { HoldIdentity = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return identity;
        }
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class =>
            HideLibraryReads && (key == "shelf.library.v1" || key == "maps.journeys.v1")
                ? Task.FromException<T?>(new IOException("Actual library observation temporarily unavailable after lost return."))
                : actual.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class => actual.SetAsync(key, value, token);
        public Task RemoveAsync(string key, CancellationToken token) => actual.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => actual.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => actual.ImportAsync(value, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expected, string? replacement, CancellationToken token)
            => actual.CompareExchangeAsync(key, expected, replacement, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, CancellationToken token)
            => actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, token);
        public async Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expected,
            string? replacement, IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token)
        {
            GuardedWriteCalls++;
            var result = await actual.CompareExchangeGuardedAsync(key, expected, replacement, guards, new HeldAdmission(this, admission), token);
            if (result.Exchanged && LoseWriteReturnOnce)
            {
                LoseWriteReturnOnce = false; HideLibraryReads = HideAfterLostReturn;
                throw new IOException("Decorator loses return AFTER actual inner atomic physical publication.");
            }
            return result;
        }
        private sealed class HeldAdmission(HeldSettings owner, ISettingsCommitAdmission actual) : ISettingsCommitAdmission
        {
            public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
            {
                if (owner.HoldPublication && context.Phase == SettingsCommitPhase.Publication)
                { owner.HoldPublication = false; owner.Entered.TrySetResult(); await owner.Release.Task.WaitAsync(token); }
                return await actual.CheckAsync(context, token);
            }
        }
    }
    private sealed class Paths : IAppPaths
    {
        public string RootDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-journey-native-" + Guid.NewGuid().ToString("N"));
        public string DataDirectory => RootDirectory;
        public string DatabasePath => Path.Combine(RootDirectory, "actual.sqlite");
        public string BrowserProfileDirectory => Path.Combine(RootDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(RootDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(RootDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(RootDirectory, "logs");
        public string SettingsPath => Path.Combine(RootDirectory, "settings.json");
        public void EnsureCreated() => Directory.CreateDirectory(RootDirectory);
    }
}
