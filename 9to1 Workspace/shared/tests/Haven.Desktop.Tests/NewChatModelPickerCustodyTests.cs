using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Shell.TopRail;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaFact]
    public async Task Main_picker_applies_exact_canonical_Specialist_and_None_to_the_original_Chat_without_send_or_draft_loss()
    {
        await WithPickerPageAsync(async fixture =>
        {
            fixture.Page.SetDraft("Keep the exact unsent question.");
            var initial = Assert.IsType<HomeModelPickerConversationSnapshot>((await fixture.ReadAsync()).Value);
            var selected = await fixture.SelectAsync(initial, fixture.Catalogue.Alpha.Id);
            Assert.True(selected.Succeeded); Assert.Equal(fixture.Catalogue.Alpha.Id, selected.Value!.SelectedSpecialistId);
            Assert.Equal(fixture.Catalogue.Alpha.Name, fixture.Page.ActiveAgentName);
            Assert.Equal("Keep the exact unsent question.", fixture.Page.ComposerInput.Text);
            Assert.Equal(fixture.Model.Name, fixture.Page.SelectedModelName);
            var none = await fixture.SelectAsync(selected.Value!, null);
            Assert.True(none.Succeeded); Assert.Null(none.Value!.SelectedSpecialistId);
            Assert.Equal("No Agent (Default)", fixture.Page.ActiveAgentName);
            Assert.Equal("Keep the exact unsent question.", fixture.Page.ComposerInput.Text);
            Assert.Equal(0, fixture.Rig.Client.Dispatches); Assert.Equal(0, fixture.Rig.Provider.Starts);
            Assert.Equal(0, fixture.Rig.ToolOwner.Preparations);
        });
    }

    [AvaloniaTheory]
    [InlineData("removed")]
    [InlineData("disabled")]
    [InlineData("edited-same-timestamp")]
    public async Task Main_picker_refuses_a_definition_that_changed_after_its_row_was_observed(string change)
    {
        await WithPickerPageAsync(async fixture =>
        {
            var initial = Assert.IsType<HomeModelPickerConversationSnapshot>((await fixture.ReadAsync()).Value);
            fixture.Catalogue.Rows = change switch
            {
                "removed" => [fixture.Catalogue.Beta],
                "disabled" => [fixture.Catalogue.Alpha with { IsEnabled = false }, fixture.Catalogue.Beta],
                _ => [fixture.Catalogue.Alpha with { Instructions = "A different definition with the same timestamp." }, fixture.Catalogue.Beta]
            };
            fixture.Page.SetDraft("A changed definition cannot consume this draft.");
            var result = await fixture.SelectAsync(initial, fixture.Catalogue.Alpha.Id);
            Assert.False(result.Succeeded);
            Assert.Contains("removed, disabled or changed", result.Message, StringComparison.Ordinal);
            Assert.Equal("No Agent (Default)", fixture.Page.ActiveAgentName);
            Assert.Equal("A changed definition cannot consume this draft.", fixture.Page.ComposerInput.Text);
            Assert.Equal(0, fixture.Rig.Client.Dispatches);
        });
    }

    [AvaloniaTheory]
    [InlineData("actor")]
    [InlineData("model")]
    [InlineData("specialist")]
    [InlineData("conversation")]
    public async Task Main_picker_rechecks_actual_actor_model_participant_and_conversation_after_delayed_catalogue(string changed)
    {
        await WithPickerPageAsync(async fixture =>
        {
            var initial = Assert.IsType<HomeModelPickerConversationSnapshot>((await fixture.ReadAsync()).Value);
            fixture.Catalogue.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var selecting = fixture.SelectAsync(initial, fixture.Catalogue.Alpha.Id);
            await fixture.Catalogue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            switch (changed)
            {
                case "actor": fixture.Actors.Current = fixture.Actor with { AuthenticationRevision = "new-session" }; break;
                case "model": fixture.Page.SelectModel(fixture.Model with { Name = fixture.Model.Name + "-new" }); break;
                case "specialist":
                    fixture.Page.ApplyAddSelection(new(AddMenu.AddMenuAction.Agent, fixture.Catalogue.Beta));
                    Assert.Equal(fixture.Catalogue.Beta.Name, fixture.Page.ActiveAgentName);
                    break;
                case "conversation":
                    await fixture.Page.StartFreshConversationAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                    Assert.NotEqual(initial.ConversationId, fixture.Page.ConversationId);
                    break;
            }
            Assert.False(selecting.IsCompleted);
            fixture.Page.SetDraft("A newer draft belongs to the actual current Chat.");
            fixture.Catalogue.Held.SetResult(fixture.Catalogue.Rows);
            var result = await selecting;
            Assert.False(result.Succeeded);
            Assert.Equal(changed == "specialist" ? fixture.Catalogue.Beta.Name : "No Agent (Default)", fixture.Page.ActiveAgentName);
            Assert.Equal("A newer draft belongs to the actual current Chat.", fixture.Page.ComposerInput.Text);
            Assert.Equal(0, fixture.Rig.Client.Dispatches);
        });
    }

    [AvaloniaFact]
    public async Task Main_picker_does_not_acquire_catalogue_when_the_delayed_first_actor_read_changes()
    {
        await WithPickerPageAsync(async fixture =>
        {
            fixture.Actors.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = fixture.ReadAsync();
            await fixture.Actors.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            fixture.Actors.Held.SetResult(fixture.Actor with { AuthenticationRevision = "replaced-authentication" });
            var result = await reading;
            Assert.False(result.Succeeded); Assert.Equal(0, fixture.Catalogue.Reads);
            Assert.Equal(0, fixture.Rig.Client.Dispatches);
        });
    }

    [AvaloniaFact]
    public async Task Main_picker_close_joins_the_exact_delayed_catalogue_without_applying_its_late_selection()
    {
        await WithPickerPageAsync(async fixture =>
        {
            var initial = Assert.IsType<HomeModelPickerConversationSnapshot>((await fixture.ReadAsync()).Value);
            fixture.Catalogue.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var selecting = fixture.SelectAsync(initial, fixture.Catalogue.Alpha.Id);
            await fixture.Catalogue.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var close = fixture.Page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted); Assert.False(selecting.IsCompleted);
            fixture.Catalogue.Held.SetResult(fixture.Catalogue.Rows);
            Assert.False((await selecting).Succeeded);
            await close.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Same(close, fixture.Page.CloseAndDrainAsync());
            Assert.Equal("No Agent (Default)", fixture.Page.ActiveAgentName);
            Assert.Equal(0, fixture.Rig.Client.Dispatches);
        });
    }

    [AvaloniaFact]
    public async Task Main_picker_keeps_the_actual_conversation_safety_lock_at_its_dispatch_boundary()
    {
        await WithPickerPageAsync(async fixture =>
        {
            await fixture.Page.LoadConversationAsync(fixture.Page.CurrentConversation);
            Assert.True(fixture.Page.IsSafetyLocked);
            var observed = Assert.IsType<HomeModelPickerConversationSnapshot>((await fixture.ReadAsync()).Value);
            Assert.True(observed.IsSafetyLocked);
            var reads = fixture.Catalogue.Reads;
            var refused = await fixture.SelectAsync(observed, fixture.Catalogue.Alpha.Id);
            Assert.False(refused.Succeeded); Assert.Contains("safety locked", refused.Message, StringComparison.Ordinal);
            Assert.Equal(reads, fixture.Catalogue.Reads);
            Assert.Equal("No Agent (Default)", fixture.Page.ActiveAgentName);
            Assert.Equal(0, fixture.Rig.Client.Dispatches);
        }, new PickerLockedSafety());
    }

    [AvaloniaFact]
    public async Task Main_picker_retains_a_genuine_catalogue_failure_in_the_same_page_close()
    {
        await WithPickerPageAsync(async fixture =>
        {
            var failure = fixture.ExpectedCloseFailure = new IOException("The real Specialist source read failed.");
            fixture.Catalogue.Failure = failure;
            var caught = await Record.ExceptionAsync(() => fixture.ReadAsync());
            Assert.NotNull(caught); Assert.True(HasPickerCause(caught, failure));
            Assert.Equal("No Agent (Default)", fixture.Page.ActiveAgentName);
            Assert.Equal(0, fixture.Rig.Client.Dispatches);
        });
    }

    [AvaloniaTheory]
    [InlineData("new-chat")]
    [InlineData("closed-tab")]
    public async Task Registered_picker_source_cannot_borrow_a_replaced_Chat_or_authenticated_actor(string transition)
    {
        await WithRegisteredSpacesChatAsync(async fixture =>
        {
            var token = TestContext.Current.CancellationToken;
            var page = await fixture.NewChatAsync();
            var tab = fixture.Chrome.State.SelectedTabId;
            var target = Assert.IsAssignableFrom<NativeAppContentRegistry.IOriginalChatModelTarget>(
                NativeAppContentRegistry.CaptureOriginalChatModelTarget(fixture.OriginalSession, tab, fixture.Actor.ProfileId));
            var source = target.CreateConversationPickerSource(fixture.Actor);
            var first = await source.ReadAsync(token);
            Assert.True(first.Succeeded); Assert.Equal(page.ConversationId, first.Value!.ConversationId);
            var foreign = target.CreateConversationPickerSource(fixture.Actor with { AuthenticationRevision = "another-authentication" });
            Assert.False((await foreign.ReadAsync(token)).Succeeded);
            if (transition == "closed-tab")
                await fixture.Chrome.CloseTabAsync(tab, token).WaitAsync(RegisteredSpacesChatBound, token);
            else
                await fixture.NewChatAsync();
            Assert.False((await source.ReadAsync(token)).Succeeded);
            Assert.Equal(0, fixture.Rig.Client.Dispatches);
        });
    }

    [AvaloniaFact]
    public async Task Registered_picker_observation_and_None_keep_the_owned_running_response_and_newer_draft()
    {
        await WithRegisteredSpacesChatAsync(async fixture =>
        {
            var token = TestContext.Current.CancellationToken;
            var page = await fixture.NewChatAsync();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Rig.Client.OrdinaryProviderBody = async (_, sourceToken) =>
            { entered.TrySetResult(); await release.Task.WaitAsync(sourceToken); return false; };
            try
            {
                var composer = await fixture.ComposerAsync(page);
                fixture.Input(composer).Text = "This original response stays running.";
                var sending = fixture.InvokeSend(composer);
                await Task.WhenAny(entered.Task, sending).WaitAsync(RegisteredSpacesChatBound, token);
                if (sending.IsCompleted) await sending;
                Assert.True(entered.Task.IsCompletedSuccessfully); Assert.True(page.IsSending);
                var tab = fixture.Chrome.State.SelectedTabId;
                var target = Assert.IsAssignableFrom<NativeAppContentRegistry.IOriginalChatModelTarget>(
                    NativeAppContentRegistry.CaptureOriginalChatModelTarget(fixture.OriginalSession, tab, fixture.Actor.ProfileId));
                var source = target.CreateConversationPickerSource(fixture.Actor);
                var snapshot = (await source.ReadAsync(token)).Value!;
                fixture.Input(composer).Text = "This newer draft must remain available.";
                var none = await source.SelectSpecialistAsync(new(snapshot.ConversationId, snapshot.ModelSelectionRevision,
                    snapshot.SelectedSpecialistId, snapshot.SelectedSpecialistVersion, null, null), token);
                Assert.True(none.Succeeded); Assert.True(page.IsSending); Assert.False(sending.IsCompleted);
                Assert.Equal("This newer draft must remain available.", page.ComposerInput.Text);
                release.TrySetResult(); await sending.WaitAsync(RegisteredSpacesChatBound, token);
                Assert.Equal(1, fixture.Rig.Client.Dispatches);
                Assert.Equal("This newer draft must remain available.", page.ComposerInput.Text);
            }
            finally { release.TrySetResult(); }
        });
    }

    private static async Task WithPickerPageAsync(Func<PickerPageFixture, Task> body, IConversationSafetyService? safety = null)
    {
        await using var rig = new Rig(temporary: false);
        var model = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [model];
        var directory = Directory.CreateTempSubdirectory("picker-original-chat-").FullName;
        var page = CreateNativeComposerPage(rig, directory, new NativeComposerDraftStore(), safety: safety);
        var fixture = new PickerPageFixture(page, rig, model);
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await page.RefreshModelsAsync(); page.SelectModel(model);
            await page.JoinAdmittedOriginalWorkAsync();
            await body(fixture);
        }
        finally
        {
            fixture.Catalogue.Held?.TrySetResult(fixture.Catalogue.Rows);
            fixture.Actors.Held?.TrySetResult(fixture.Actor);
            var closeFailure = await Record.ExceptionAsync(() => page.CloseAndDrainAsync()
                .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
            Directory.Delete(directory, true);
            if (fixture.ExpectedCloseFailure is { } expected)
            { Assert.NotNull(closeFailure); Assert.True(HasPickerCause(closeFailure, expected)); }
            else Assert.Null(closeFailure);
        }
    }

    private sealed class PickerPageFixture(NewChatPage page, Rig rig, ModelDescriptor model)
    {
        internal NewChatPage Page { get; } = page;
        internal Rig Rig { get; } = rig;
        internal ModelDescriptor Model { get; } = model;
        internal PickerCatalogue Catalogue { get; } = new();
        internal AuthenticatedResourceActor Actor { get; } = new("picker-actor", "picker-profile", null, null, "auth-1");
        private PickerActors? _actors;
        internal PickerActors Actors => _actors ??= new(Actor);
        internal Exception? ExpectedCloseFailure;
        internal Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> ReadAsync() =>
            Page.ReadModelPickerConversationAsync(Catalogue, Actors, Actor, Page.ConversationId, () => true, null,
                TestContext.Current.CancellationToken);
        internal Task<HomeCoreOperationResult<HomeModelPickerConversationSnapshot>> SelectAsync(
            HomeModelPickerConversationSnapshot snapshot, Guid? id) => Page.ReadModelPickerConversationAsync(Catalogue, Actors, Actor,
                snapshot.ConversationId, () => true, new(snapshot.ConversationId, snapshot.ModelSelectionRevision,
                    snapshot.SelectedSpecialistId, snapshot.SelectedSpecialistVersion, id,
                    snapshot.Specialists.SingleOrDefault(item => item.Id == id)?.Version), TestContext.Current.CancellationToken);
    }

    private sealed class PickerActors(AuthenticatedResourceActor current) : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = current;
        internal TaskCompletionSource<AuthenticatedResourceActor?>? Held;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            if (Held is not null) { Entered.TrySetResult(); return new(Held.Task); }
            return ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
        }
    }
    private sealed class PickerLockedSafety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) =>
            Task.FromResult(new ConversationSafetySnapshot(id, 3, ConversationSafetyState.Locked, DateTimeOffset.UnixEpoch, 1));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) =>
            throw new NotSupportedException();
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken token) =>
            Task.FromException(new InvalidOperationException("This conversation is safety locked."));
    }
    private sealed class PickerCatalogue : ICatalogRepository
    {
        internal AgentDefinition Alpha { get; } = Agent("Alpha");
        internal AgentDefinition Beta { get; } = Agent("Beta");
        internal IReadOnlyList<AgentDefinition> Rows;
        internal TaskCompletionSource<IReadOnlyList<AgentDefinition>>? Held;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Exception? Failure;
        internal int Reads;
        internal PickerCatalogue() => Rows = [Alpha, Beta];
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken cancellationToken)
        {
            Reads++;
            if (Failure is { } error) return Task.FromException<IReadOnlyList<AgentDefinition>>(error);
            if (Held is not null) { Entered.TrySetResult(); return Held.Task; }
            return Task.FromResult(Rows);
        }
        private static AgentDefinition Agent(string name) => new(Guid.NewGuid(), name, name + " definition", name + " instructions",
            "agents", "", null, "", "{}", false, true, DateTimeOffset.UnixEpoch);
        public Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertAgentAsync(AgentDefinition agent, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpsertPromptAsync(PromptDefinition prompt, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteCustomAgentAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteCustomPromptAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private static bool HasPickerCause(Exception cause, Exception expected) => ReferenceEquals(cause, expected) ||
        cause is AggregateException aggregate && aggregate.InnerExceptions.Any(item => HasPickerCause(item, expected)) ||
        cause.InnerException is { } inner && HasPickerCause(inner, expected);
}
