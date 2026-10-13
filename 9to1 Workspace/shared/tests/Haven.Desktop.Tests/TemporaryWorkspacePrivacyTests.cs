using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Shell;
using Haven.Desktop.Views.Shell.TopRail;

namespace Haven.Desktop.Tests;

// Actual page/tab/capture/coordinator and naming owner; controlled repositories and
// model replies. Mixed DTO projection is not a constructed MainView acceptance claim.
public sealed partial class TemporaryConversationPageTests
{
    [AvaloniaFact]
    public async Task Actual_discard_choice_is_invalidated_by_later_instruction_context()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 0);
        fixture.ShowPage();
        fixture.Page.SetDraft("Unsent private draft");
        var prepare = fixture.Page.PrepareToCloseAsync(null, CancellationToken.None);
        await fixture.ChooseVisiblePromptAsync("Discard");
        Assert.True(await prepare);
        Assert.True(fixture.Page.IsOriginalChatClosePrepared);
        var instruction = new PromptDefinition(Guid.NewGuid(), "New explicit context", "", "prompt",
            "Keep this changed context", false, false, true, DateTimeOffset.UtcNow);
        fixture.Page.ApplyAddSelection(new AddMenuSelection(AddMenu.AddMenuAction.Instruction, instruction));
        Assert.False(fixture.Page.IsOriginalChatClosePrepared);
        var changedPreparation = fixture.Page.PrepareToCloseAsync(null, CancellationToken.None);
        await fixture.ChooseVisiblePromptAsync("Cancel");
        Assert.False(await changedPreparation);
        Assert.False(fixture.Page.IsOriginalChatClosePrepared);
        Assert.Empty(fixture.SaveAttempts);
        Assert.Equal(0, fixture.OrdinaryWrites);
    }

    [AvaloniaFact]
    public async Task Actual_close_save_refuses_unsent_draft_without_its_durable_owner()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 0);
        fixture.ShowPage();
        fixture.Page.SetDraft("Retain this unsent text");
        var prepare = fixture.Page.PrepareToCloseAsync(null, CancellationToken.None);
        await fixture.ChooseVisiblePromptAsync("Save");
        Assert.False(await prepare);
        Assert.False(fixture.Page.IsOriginalChatClosePrepared);
        Assert.True(fixture.Page.IsTemporary);
        Assert.Empty(fixture.SaveAttempts);
        Assert.Contains("unsent draft", fixture.Page.ProjectionState.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task Actual_close_save_waits_for_original_history_acknowledgement()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync();
        fixture.CaptureActualPageFrames();
        var original = fixture.Page.CurrentConversation;
        var acknowledgement = NewSource<Conversation>();
        fixture.Save = (_, _) => acknowledgement.Task;
        var prepare = fixture.Page.PrepareToCloseAsync(null, CancellationToken.None);
        try
        {
            await fixture.ChooseVisiblePromptAsync("Save");
            await fixture.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(prepare.IsCompleted);
            Assert.False(fixture.Page.IsOriginalChatClosePrepared);
            Assert.True(fixture.Page.IsTemporary);
            Assert.Equal(fixture.History, Assert.Single(fixture.SaveAttempts).History);
            acknowledgement.SetResult(original with { IsTemporary = false });
            Assert.True(await prepare);
            Assert.True(fixture.Page.IsOriginalChatClosePrepared);
            Assert.False(fixture.Page.IsTemporary);
        }
        finally
        {
            acknowledgement.TrySetResult(original with { IsTemporary = false });
            await prepare;
        }
    }

    [AvaloniaFact]
    public async Task Actual_temporary_popup_is_absent_from_explicit_and_final_workspace_writes()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 0);
        var tab = new WorkspaceTabViewModel("private-chat", "Private synthetic title 91672", fixture.Page, true, HavenSurface.Chat);
        var repository = new PrivacyWorkspaceRepository();
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var windowId = Guid.NewGuid();
        coordinator.RegisterPopUp(windowId, () => MainView.CreateDetachedTabSnapshot(tab),
            () => PrivacyWindow(windowId, [tab.SessionId], tab.SessionId, [tab.SessionId]));
        try
        {
            Assert.Null(MainView.CreateDetachedTabSnapshot(tab));
            await coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None);
            await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
            Assert.NotEmpty(repository.Writes);
            foreach (var saved in repository.Writes)
            {
                Assert.Empty(saved.Tabs); Assert.Empty(saved.Windows); Assert.Empty(saved.Groups);
                var json = JsonSerializer.Serialize(saved);
                Assert.DoesNotContain(tab.Title, json);
                Assert.DoesNotContain(fixture.Page.ConversationId.ToString(), json);
                Assert.DoesNotContain(tab.SessionId.ToString(), json);
            }
        }
        finally
        {
            await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
            await tab.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Workspace_capture_waits_for_temporary_save_acknowledgement_before_restoring_tab()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 0);
        var tab = new WorkspaceTabViewModel("save-pending", "Keep original after explicit save", fixture.Page, true, HavenSurface.Chat);
        var original = fixture.Page.CurrentConversation;
        var release = NewSource<Conversation>();
        fixture.Save = (_, _) => release.Task;
        var save = fixture.Page.SetTemporaryAsync(false);
        var repository = new PrivacyWorkspaceRepository();
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var windowId = Guid.NewGuid();
        coordinator.RegisterPopUp(windowId, () => MainView.CreateDetachedTabSnapshot(tab),
            () => PrivacyWindow(windowId, [tab.SessionId], tab.SessionId, [tab.SessionId]));
        try
        {
            await fixture.SaveEntered.Task;
            await coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None);
            Assert.Empty(repository.Writes[^1].Tabs);
            Assert.Empty(repository.Writes[^1].Windows);
            release.SetResult(original with { IsTemporary = false });
            await save;
            await coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None);
            var captured = Assert.Single(repository.Writes[^1].Tabs);
            Assert.Equal(tab.SessionId, captured.Id);
            Assert.Equal(tab.Title, captured.Title);
            Assert.Contains(original.Id.ToString(), captured.StateJson);
            Assert.Equal(tab.SessionId, Assert.Single(repository.Writes[^1].Windows).SelectedTabId);
        }
        finally
        {
            release.TrySetResult(original with { IsTemporary = false });
            await save;
            await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
            await tab.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Combined_capture_removes_temporary_primary_and_preserves_visible_durable_secondary()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 0);
        var temporary = new WorkspaceTabViewModel("private", "Private source", fixture.Page, true, HavenSurface.Chat);
        var first = new WorkspaceTabViewModel("first", "Durable background", new PrivacyPage(), true, HavenSurface.Dashboard);
        var visible = new WorkspaceTabViewModel("visible", "Durable visible", new PrivacyPage(), true, HavenSurface.Dashboard);
        var repository = new PrivacyWorkspaceRepository();
        var coordinator = new WorkspaceSessionCoordinator(repository);
        var mixedWindow = Guid.NewGuid(); var temporaryWindow = Guid.NewGuid(); var durableWindow = Guid.NewGuid();
        // The popup registration supplies real captured tabs and a deliberately mixed
        // shell-shaped DTO to exercise the combined projection, not a fake MainView.
        coordinator.RegisterPopUp(mixedWindow, () => MainView.CreateDetachedTabSnapshot(first),
            () => PrivacyWindow(mixedWindow, [first.SessionId, temporary.SessionId, visible.SessionId],
                temporary.SessionId, [temporary.SessionId, visible.SessionId]));
        coordinator.RegisterPopUp(temporaryWindow, () => MainView.CreateDetachedTabSnapshot(temporary),
            () => PrivacyWindow(temporaryWindow, [temporary.SessionId], temporary.SessionId, [temporary.SessionId]));
        coordinator.RegisterPopUp(durableWindow, () => MainView.CreateDetachedTabSnapshot(visible),
            () => PrivacyWindow(durableWindow, [visible.SessionId], visible.SessionId, [visible.SessionId]));
        try
        {
            await coordinator.SaveNowAndCancelPendingAsync(CancellationToken.None);
            var captured = repository.Writes[^1];
            Assert.Equal(new[] { first.SessionId, visible.SessionId }, captured.Tabs.Select(item => item.Id));
            Assert.DoesNotContain(captured.Windows, window => window.Id == temporaryWindow);
            var window = Assert.Single(captured.Windows, item => item.Id == mixedWindow);
            Assert.Equal(new[] { first.SessionId, visible.SessionId }, window.OrderedTabIds);
            Assert.Equal(visible.SessionId, window.SelectedTabId);
            var pane = Assert.Single(window.Layout.Panes);
            Assert.Equal(visible.SessionId, pane.TabId); Assert.Equal(0, pane.Order);
            Assert.Equal(WorkspaceLayoutKind.Single, window.Layout.Kind); Assert.Equal(1d, window.Layout.PrimaryRatio);
            Assert.DoesNotContain(temporary.SessionId.ToString(), JsonSerializer.Serialize(captured));
        }
        finally
        {
            await coordinator.SaveFinalSnapshotAndSealAsync(CancellationToken.None);
            await temporary.CloseAndDrainAsync(); await first.CloseAndDrainAsync(); await visible.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public void Actual_combined_projection_prunes_empty_groups_and_dangling_group_membership()
    {
        var kept = Guid.NewGuid(); var orphan = Guid.NewGuid(); var temporary = Guid.NewGuid();
        var sharedGroup = Guid.NewGuid(); var privateGroup = Guid.NewGuid();
        var original = new WorkspaceSessionSnapshot(WorkspaceSessionSnapshot.CurrentSchemaVersion,
            [PrivacyTab(kept, sharedGroup), PrivacyTab(orphan, Guid.NewGuid())],
            [new(sharedGroup, "Keep shared group", true, [temporary, kept]), new(privateGroup, "Private-only group", false, [temporary])],
            [PrivacyWindow(Guid.NewGuid(), [temporary, kept, orphan], temporary, [temporary])], DateTimeOffset.UtcNow);
        var projection = typeof(WorkspaceSessionCoordinator).GetMethod("NormalizeDurableSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = Assert.IsType<WorkspaceSessionSnapshot>(projection.Invoke(null, [original]));
        var group = Assert.Single(saved.Groups);
        Assert.Equal(sharedGroup, group.Id); Assert.Equal("Keep shared group", group.Name); Assert.True(group.IsCollapsed);
        Assert.Equal(new[] { kept }, group.OrderedTabIds);
        Assert.Null(Assert.Single(saved.Tabs, tab => tab.Id == orphan).GroupId);
        Assert.Equal(sharedGroup, Assert.Single(saved.Tabs, tab => tab.Id == kept).GroupId);
        Assert.DoesNotContain("Private-only group", JsonSerializer.Serialize(saved));
        Assert.DoesNotContain(temporary.ToString(), JsonSerializer.Serialize(saved));
    }

    [AvaloniaFact]
    public async Task Temporary_origin_naming_discards_after_save_and_emits_no_durable_execution_event()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 0);
        var tab = new WorkspaceTabViewModel("private", "Private original title", fixture.Page, true, HavenSurface.Chat);
        var reply = NewSource<string>(); var entered = NewSource();
        var client = new PrivacyNamingClient { Complete = _ => { entered.TrySetResult(); return reply.Task; } };
        var sink = new PrivacyNamingSink();
        var naming = WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink);
        try
        {
            await entered.Task;
            Assert.Equal("Private original title", tab.Title);
            Assert.Empty(sink.Events);
            await fixture.Page.SetTemporaryAsync(false);
            Assert.False(fixture.Page.IsTemporary);
            reply.SetResult("Late private generated name");
            Assert.False(await naming);
            Assert.Equal("Private original title", tab.Title);
            Assert.Empty(sink.Events);
            Assert.Contains("Private original title", Assert.Single(client.Requests).Messages[0].Content);
        }
        finally { reply.TrySetResult("Late private generated name"); await naming; await tab.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Temporary_source_replacement_during_model_discovery_never_dispatches_inference()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 0);
        var tab = new WorkspaceTabViewModel("private", "Original private source", fixture.Page, true, HavenSurface.Chat);
        var models = NewSource<IReadOnlyList<ModelDescriptor>>();
        var client = new PrivacyNamingClient { Models = () => models.Task };
        var sink = new PrivacyNamingSink();
        var naming = WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink);
        try
        {
            var replacement = fixture.Page.CurrentConversation with { Id = Guid.NewGuid(), Title = "Unrelated saved source", IsTemporary = false };
            await fixture.Page.LoadConversationAsync(replacement);
            tab.Title = "Replacement title";
            models.SetResult(PrivacyNamingClient.ModelList);
            Assert.False(await naming);
            Assert.Equal("Replacement title", tab.Title);
            Assert.Empty(client.Requests); Assert.Empty(sink.Events);
        }
        finally { models.TrySetResult(PrivacyNamingClient.ModelList); await naming; await tab.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Naming_failure_after_source_replacement_preserves_new_title_and_actual_provider_cause()
    {
        await using var fixture = new Fixture();
        await fixture.LoadTemporaryAsync(messageCount: 0);
        var tab = new WorkspaceTabViewModel("private", "Private old title", fixture.Page, true, HavenSurface.Chat);
        var reply = NewSource<string>(); var entered = NewSource();
        var failure = new IOException("Actual held naming failure");
        var client = new PrivacyNamingClient { Complete = _ => { entered.TrySetResult(); return reply.Task; } };
        var sink = new PrivacyNamingSink();
        var naming = WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink);
        try
        {
            await entered.Task;
            tab.NavigateTo("replacement", "Replacement title", new PrivacyPage(), true, HavenSurface.Dashboard);
            reply.SetException(failure);
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => naming));
            Assert.Equal("Replacement title", tab.Title); Assert.Empty(sink.Events);
        }
        finally
        {
            reply.TrySetException(failure);
            Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => naming));
            await tab.CloseAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Naming_cannot_publish_after_away_and_back_and_duplicate_admission_is_refused()
    {
        var tab = new WorkspaceTabViewModel("original", "Original title", new PrivacyPage(), true, HavenSurface.Dashboard);
        var reply = NewSource<string>();
        var client = new PrivacyNamingClient { Complete = _ => reply.Task };
        var sink = new PrivacyNamingSink();
        var naming = WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink);
        try
        {
            Assert.Single(client.Requests);
            await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink));
            tab.NavigateTo("other", "Other title", new PrivacyPage(), true, HavenSurface.Dashboard);
            Assert.True(tab.TryGoBack());
            Assert.Equal("Original title", tab.Title);
            reply.SetResult("Stale generated title");
            Assert.False(await naming); Assert.Equal("Original title", tab.Title);
            Assert.DoesNotContain(sink.Events, item => item.Status == ExecutionActionStatus.Completed);
        }
        finally { reply.TrySetResult("Stale generated title"); await naming; await tab.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Naming_keeps_a_manual_title_written_while_the_original_response_is_held()
    {
        var tab = new WorkspaceTabViewModel("original", "Original title", new PrivacyPage(), true, HavenSurface.Dashboard);
        var reply = NewSource<string>();
        var client = new PrivacyNamingClient { Complete = _ => reply.Task };
        var sink = new PrivacyNamingSink();
        var naming = WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink);
        try
        {
            Assert.Single(client.Requests);
            tab.Title = "New manually chosen title";
            reply.SetResult("Obsolete AI suggestion");
            Assert.False(await naming);
            Assert.Equal("New manually chosen title", tab.Title);
            Assert.DoesNotContain(sink.Events, value => value.Status == ExecutionActionStatus.Completed);
        }
        finally { reply.TrySetResult("Obsolete AI suggestion"); await naming; await tab.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Current_naming_publishes_actual_reply_and_reports_real_completion()
    {
        var tab = new WorkspaceTabViewModel("original", "Original title", new PrivacyPage(), true, HavenSurface.Dashboard);
        var sink = new PrivacyNamingSink();
        try
        {
            var client = new PrivacyNamingClient { Complete = _ => Task.FromResult("\"Useful concise name.\"\nExtra line ignored") };
            Assert.True(await WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink));
            Assert.Equal("Useful concise name", tab.Title);
            Assert.Equal(new[] { ExecutionActionStatus.Running, ExecutionActionStatus.Completed }, sink.Events.Select(item => item.Status));
            Assert.Single(client.Requests);
        }
        finally { await tab.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Naming_preserves_compound_provider_faults_when_the_failure_sink_also_throws()
    {
        var tab = new WorkspaceTabViewModel("original", "Original title", new PrivacyPage(), true, HavenSurface.Dashboard);
        var first = new IOException("First actual provider cause"); var second = new IOException("Second actual provider cause");
        var reporting = new InvalidOperationException("Actual reporting failure");
        var reply = NewSource<string>();
        reply.SetException([first, second]);
        var client = new PrivacyNamingClient { Complete = _ => reply.Task };
        var sink = new PrivacyNamingSink { OnEvent = value => { if (value.Status == ExecutionActionStatus.Failed) throw reporting; } };
        try
        {
            var fault = await Assert.ThrowsAsync<AggregateException>(() => WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink));
            var causes = fault.Flatten().InnerExceptions;
            Assert.Equal(3, causes.Count);
            Assert.Contains(causes, cause => ReferenceEquals(cause, first));
            Assert.Contains(causes, cause => ReferenceEquals(cause, second));
            Assert.Contains(causes, cause => ReferenceEquals(cause, reporting));
            Assert.Equal("Original title", tab.Title);
        }
        finally { await tab.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Naming_title_callback_replacement_cannot_be_reported_as_completed()
    {
        var tab = new WorkspaceTabViewModel("original", "Original title", new PrivacyPage(), true, HavenSurface.Dashboard);
        var sink = new PrivacyNamingSink();
        System.ComponentModel.PropertyChangedEventHandler replace = (_, change) =>
        {
            if (change.PropertyName == nameof(tab.Title) && tab.Title == "Generated name")
                tab.NavigateTo("replacement", "Replacement title", new PrivacyPage(), true, HavenSurface.Dashboard);
        };
        tab.PropertyChanged += replace;
        try
        {
            var client = new PrivacyNamingClient { Complete = _ => Task.FromResult("Generated name") };
            Assert.False(await WorkspaceTabNameGeneration.GenerateAsync(tab, client, "naming-probe", sink));
            Assert.Equal("Replacement title", tab.Title);
            Assert.DoesNotContain(sink.Events, item => item.Status == ExecutionActionStatus.Completed);
        }
        finally { tab.PropertyChanged -= replace; await tab.CloseAndDrainAsync(); }
    }

    private static TabSessionSnapshot PrivacyTab(Guid id, Guid? group) =>
        new(id, "dashboard", "Retained tab", "{}", null, null, group, true, false, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static WorkspaceWindowSnapshot PrivacyWindow(Guid id, IReadOnlyList<Guid> ordered, Guid? selected, IReadOnlyList<Guid> panes) =>
        new(id, WorkspaceWindowKind.PopUp,
            new(Guid.NewGuid(), panes.Count > 1 ? WorkspaceLayoutKind.Split : WorkspaceLayoutKind.Single,
                SplitOrientation.Vertical, 0.4, panes.Select((tab, index) => new WorkspacePaneSnapshot(Guid.NewGuid(), tab, index)).ToArray()),
            ordered, selected, "{\"Width\":1000,\"Height\":700}", DateTimeOffset.UnixEpoch);

    // Controlled non-chat page with no business work. Its one original retirement
    // completion is retained and joined by the real WorkspaceTabViewModel owner.
    // A plain ContentControl is intentionally not a valid clean-retirement witness.
    private sealed class PrivacyPage : ContentControl, IDesktopOriginalRetirementParticipant,
        IDesktopOriginalRetirementJoinGuard
    {
        private readonly TaskCompletionSource _retirement = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void RequestRetirement() => _retirement.TrySetResult();
        public Task CloseAndDrainAsync() => _retirement.Task;
        public void DemandExternalOriginalRetirementJoin() { }
    }

    private sealed class PrivacyWorkspaceRepository : IWorkspaceSessionRepository
    {
        internal readonly List<WorkspaceSessionSnapshot> Writes = [];
        public Task<WorkspaceSessionSnapshot?> LoadAsync(CancellationToken token) => Task.FromResult(Writes.LastOrDefault());
        public Task SaveAsync(WorkspaceSessionSnapshot snapshot, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Writes.Add(snapshot); return Task.CompletedTask; }
    }

    private sealed class PrivacyNamingSink : IExecutionEventSink
    {
        internal readonly List<ExecutionEvent> Events = [];
        internal Action<ExecutionEvent>? OnEvent;
        public bool TryPublish(ExecutionEvent value) { OnEvent?.Invoke(value); Events.Add(value); return true; }
    }

    private sealed class PrivacyNamingClient : IOllamaClient
    {
        internal static readonly IReadOnlyList<ModelDescriptor> ModelList = [new("naming-probe", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch)];
        internal Func<Task<IReadOnlyList<ModelDescriptor>>> Models = () => Task.FromResult(ModelList);
        internal Func<OllamaChatRequest, Task<string>> Complete = _ => throw new InvalidOperationException("Unexpected naming inference");
        internal readonly List<OllamaChatRequest> Requests = [];
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Models();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) { Requests.Add(request); return Complete(request); }
        public Task<bool> IsAvailableAsync(CancellationToken token) => throw new InvalidOperationException("Unexpected availability probe");
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new InvalidOperationException("Unexpected streaming inference");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new InvalidOperationException("Unexpected tool inference");
    }
}
