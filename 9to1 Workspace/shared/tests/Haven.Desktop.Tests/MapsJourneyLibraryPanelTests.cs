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

    [AvaloniaTheory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    public async Task Actual_native_typed_step_review_retains_original_intent_and_duration_through_later_edits_and_physical_reopen(int kindIndex, int intentIndex)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        var window = new Window { Content = panel }; window.Show();
        try
        {
            panel.NameInput.Text = "Original typed journey"; panel.InstructionInput.Text = "Original bench activity";
            panel.StepTypeInput.SelectedIndex = kindIndex; panel.StepIntentInput.SelectedIndex = intentIndex;
            panel.DurationInput.Text = kindIndex == 1 ? 7.5m.ToString(System.Globalization.CultureInfo.CurrentCulture) : "";
            var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            var review = Assert.Single(panel.Reviews);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            var originalDetails = panel.ReviewDetails.Text;
            Assert.Contains("Original bench activity", originalDetails);
            panel.NameInput.Text = "Later edit"; panel.InstructionInput.Text = "Not the reviewed instruction";
            panel.StepTypeInput.SelectedIndex = 0; panel.StepIntentInput.SelectedIndex = 2; panel.DurationInput.Text = "99";
            Assert.Equal(originalDetails, panel.ReviewDetails.Text);
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            Assert.Equal("ApprovalRequired", Assert.Single(panel.Reviews).LastObservation!.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.True((await fixture.Permissions.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            Assert.True(Assert.Single(panel.Reviews).LastObservation!.Committed); Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            var committed = await File.ReadAllBytesAsync(fixture.SettingsFile);
            var reopened = await new MapsJourneyService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync();
            var journey = Assert.Single(reopened.Journeys); var step = Assert.Single(journey.Steps);
            Assert.Equal("Original typed journey", journey.Name); Assert.Equal("Original bench activity", step.Instruction);
            Assert.Equal(kindIndex == 1 ? MapJourneyStepKind.Wait : MapJourneyStepKind.Activity, step.Kind);
            Assert.Equal((MapStepIntent)intentIndex, step.Intent);
            if (kindIndex == 1) Assert.Equal(TimeSpan.FromMinutes(7.5), step.Duration); else Assert.Null(step.Duration);
            Assert.Null(step.PlaceId); Assert.Null(step.Coordinate); Assert.Null(step.TravelProfile);
            Assert.NotEqual(Guid.Empty, step.StepId);
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(committed, await File.ReadAllBytesAsync(fixture.SettingsFile)); Assert.Equal(1, fixture.Settings.GuardedWriteCalls);
            panel.Dispose(); Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            Assert.Single(panel.Reviews); Assert.Equal(committed, await File.ReadAllBytesAsync(fixture.SettingsFile));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1441")]
    public async Task Actual_native_invalid_wait_draft_is_refused_before_Home_request_or_owner_write(string duration)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        var window = new Window { Content = panel }; window.Show();
        try
        {
            panel.NameInput.Text = "Invalid wait"; panel.InstructionInput.Text = "Wait at the bench";
            panel.StepTypeInput.SelectedIndex = 1; panel.DurationInput.Text = duration;
            var settings = await File.ReadAllBytesAsync(fixture.SettingsFile); var home = await File.ReadAllBytesAsync(fixture.HomeFile);
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            Assert.Empty(panel.Reviews); Assert.Equal(duration, panel.DurationInput.Text);
            Assert.False(string.IsNullOrWhiteSpace(panel.Status.Text)); Assert.Equal(0, fixture.Settings.GuardedWriteCalls);
            Assert.Equal(settings, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actual_ordered_native_draft_keeps_step_ids_order_and_original_review_through_physical_commit()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        var window = new Window { Content = panel }; window.Show();
        try
        {
            var before = await File.ReadAllBytesAsync(fixture.SettingsFile);
            var home = await File.ReadAllBytesAsync(fixture.HomeFile);
            panel.NameInput.Text = "Ordered original journey";
            panel.InstructionInput.Text = "First instruction";
            Click(panel.AddStepButton); await panel.WhenActionsIdleAsync();
            panel.StepTypeInput.SelectedIndex = 1; panel.DurationInput.Text = "2";
            panel.InstructionInput.Text = "Second wait";
            Click(panel.AddStepButton); await panel.WhenActionsIdleAsync();
            panel.StepTypeInput.SelectedIndex = 2; panel.DurationInput.Text = "";
            panel.InstructionInput.Text = "Temporary activity";
            Click(panel.AddStepButton); await panel.WhenActionsIdleAsync();
            Click(panel.RemoveStepButton); await panel.WhenActionsIdleAsync();
            panel.DraftSteps.SelectedIndex = 1;
            Click(panel.MoveStepUpButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(2, panel.DraftSteps.ItemCount);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
            Assert.Empty(panel.Reviews);
            Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
            var review = Assert.Single(panel.Reviews);
            var originalDetails = panel.ReviewDetails.Text;
            panel.NameInput.Text = "Later name";
            Click(panel.RemoveStepButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(originalDetails, panel.ReviewDetails.Text);
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            Assert.Equal("ApprovalRequired", Assert.Single(panel.Reviews).LastObservation!.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.True((await fixture.Permissions.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
            Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
            Assert.True(Assert.Single(panel.Reviews).LastObservation!.Committed);
            var journey = Assert.Single((await new MapsJourneyService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync()).Journeys);
            Assert.Equal("Ordered original journey", journey.Name);
            Assert.Equal(new[] { "Second wait", "First instruction" }, journey.Steps.Select(step => step.Instruction).ToArray());
            Assert.Equal(MapJourneyStepKind.Wait, journey.Steps[0].Kind);
            Assert.Equal(TimeSpan.FromMinutes(2), journey.Steps[0].Duration);
            Assert.Equal(MapJourneyStepKind.ManualInstruction, journey.Steps[1].Kind);
            Assert.Equal(2, journey.Steps.Select(step => step.StepId).Distinct().Count());
            Assert.All(journey.Steps, step => Assert.NotEqual(Guid.Empty, step.StepId));
            var committed = await File.ReadAllBytesAsync(fixture.SettingsFile);
            Click(panel.FinishButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(committed, await File.ReadAllBytesAsync(fixture.SettingsFile));
            panel.Dispose();
            Click(panel.AddStepButton); await panel.WhenActionsIdleAsync();
            Assert.Equal(committed, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Single(panel.Reviews);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task Actual_displayed_journey_search_preserves_canonical_ids_and_both_physical_stores()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync();
        using var panel = await MapsJourneyLibraryPanel.CreateAsync(fixture.Owner, fixture.Actor, _ => Task.CompletedTask);
        var window = new Window { Content = panel }; window.Show();
        try
        {
            foreach (var pair in new[] { ("Morning route", "Visit orchard"), ("Evening route", "Visit harbor") })
            {
                panel.NameInput.Text = pair.Item1; panel.InstructionInput.Text = pair.Item2;
                Click(panel.ReviewButton); await panel.WhenActionsIdleAsync();
                var review = panel.Reviews.Last();
                Assert.True((await fixture.Permissions.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
                Click(panel.ApplyButton); await panel.WhenActionsIdleAsync();
                Assert.True(panel.Reviews.Last().LastObservation!.Committed);
                Click(panel.ReloadButton); await panel.WhenActionsIdleAsync();
            }
            var reopened = await new MapsJourneyService(new VersionedAtomicSettingsStore(fixture.Paths)).ReadAsync();
            Assert.Equal(2, reopened.Journeys.Count);
            var settings = await File.ReadAllBytesAsync(fixture.SettingsFile);
            var home = await File.ReadAllBytesAsync(fixture.HomeFile);
            panel.SearchInput.Text = "MORNING";
            Assert.Equal(reopened.Journeys.Single(j => j.Name == "Morning route").JourneyId,
                Assert.IsType<TextBlock>(Assert.Single(panel.JourneyItems.Children)).Tag);
            var originalRow = Assert.IsType<MapSavedJourney>(Assert.Single(panel.JourneySelection.Items));
            panel.JourneySelection.SelectedItem = originalRow;
            Assert.Contains("1. ManualInstruction: Visit orchard (Required)", panel.JourneyDetails.Text);
            panel.JourneySelection.SelectedItem = originalRow with { Name = "Foreign display lookalike" };
            Assert.Equal("", panel.JourneyDetails.Text);
            panel.JourneySelection.SelectedItem = originalRow;
            Assert.Contains("Visit orchard", panel.JourneyDetails.Text);
            panel.SearchInput.Text = "harbor";
            Assert.Equal(reopened.Journeys.Single(j => j.Name == "Evening route").JourneyId,
                Assert.IsType<TextBlock>(Assert.Single(panel.JourneyItems.Children)).Tag);
            Assert.Equal("", panel.JourneyDetails.Text);
            panel.JourneySelection.SelectedItem = originalRow;
            Assert.Equal("", panel.JourneyDetails.Text);
            var betaRow = Assert.IsType<MapSavedJourney>(Assert.Single(panel.JourneySelection.Items));
            panel.JourneySelection.SelectedItem = betaRow;
            Assert.Contains("Visit harbor", panel.JourneyDetails.Text);
            panel.SearchInput.Text = "absent"; Assert.Empty(panel.JourneyItems.Children);
            panel.SearchInput.Text = "";
            Assert.Equal(reopened.Journeys.Select(j => j.JourneyId).ToArray(),
                panel.JourneyItems.Children.Cast<TextBlock>().Select(item => (Guid)item.Tag!).ToArray());
            panel.Dispose(); panel.SearchInput.Text = "absent";
            Assert.Equal(2, panel.JourneyItems.Children.Count);
            panel.JourneySelection.SelectedItem = betaRow;
            Assert.Equal("", panel.JourneyDetails.Text);
            Assert.Equal(settings, await File.ReadAllBytesAsync(fixture.SettingsFile));
            Assert.Equal(home, await File.ReadAllBytesAsync(fixture.HomeFile));
        }
        finally { window.Close(); }
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
