using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

// Actual legacy Page and maintained ViewModel owners. Boundaries refuse every
// unexpected provider/persistence operation; this is state verification, not live-provider acceptance.
public sealed class LegacyTemporaryConversationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Temporary_branch_keeps_history_metadata_context_and_space_without_storing_it(bool page)
    {
        using var fixture = new Fixture(page);
        await fixture.LoadAsync(true);
        var original = fixture.Current;
        await fixture.InvokePrivateAsync("RegisterContextAsync", "A registered source");
        var previous = fixture.Messages.Select(message => message.ToMessage(original.Id)).ToArray();

        await fixture.BranchAsync();

        var branch = fixture.Current;
        Assert.True(branch.IsTemporary);
        Assert.NotEqual(original.Id, branch.Id);
        Assert.Equal(original.Id, branch.ParentConversationId);
        Assert.Equal(original.SpaceId, branch.SpaceId);
        Assert.Equal(previous.Select(message => message.Content), fixture.Messages.Select(message => message.Content));
        Assert.DoesNotContain(fixture.Messages, message => previous.Any(old => old.Id == message.Id));
        Assert.Contains("source-proof", fixture.Messages[1].ToMessage(branch.Id).MetadataJson);
        Assert.Equal(0, fixture.OrdinaryWrites);

        await fixture.SetTemporaryAsync(false);
        Assert.Single(fixture.SaveAttempts);
        Assert.Equal(branch.Id, fixture.SaveAttempts[0].Conversation.Id);
        var context = Assert.Single(fixture.SaveAttempts[0].Context);
        Assert.Equal(branch.Id, context.ConversationId);
        Assert.Equal("A registered source", context.Content);
        Assert.False(fixture.Current.IsTemporary);
        Assert.Equal(branch.Id, fixture.Current.Id);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_reset_or_load_cannot_discard_the_live_temporary_transcript_without_consent(bool page)
    {
        using var fixture = new Fixture(page);
        await fixture.LoadAsync(true);
        var original = fixture.Current;
        var ids = fixture.Messages.Select(item => item.Id).ToArray();
        Assert.Throws<InvalidOperationException>(fixture.NewChat);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.LoadAsync(false));
        Assert.Same(original, fixture.Current);
        Assert.Equal(ids, fixture.Messages.Select(item => item.Id));
        Assert.Equal(0, fixture.OrdinaryWrites);
        Assert.Empty(fixture.SaveAttempts);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lost_save_ack_keeps_exact_snapshot_and_rejects_navigation_until_actual_retry_ack(bool page)
    {
        using var fixture = new Fixture(page);
        await fixture.LoadAsync(true);
        var original = fixture.Current;
        var historyIds = fixture.Messages.Select(message => message.Id).ToArray();
        var fault = new IOException("Controlled lost local save acknowledgement");
        fixture.Save = _ => Task.FromException<Conversation>(fault);

        Assert.Same(fault, await Assert.ThrowsAsync<IOException>(() => fixture.SetTemporaryAsync(false)));
        Assert.True(fixture.Current.IsTemporary);
        Assert.Same(original, fixture.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.BranchAsync);
        Assert.Throws<InvalidOperationException>(fixture.NewChat);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.LoadAsync(false));
        Assert.Equal(0, fixture.OrdinaryWrites);
        fixture.Save = conversation => Task.FromResult(conversation with { IsTemporary = false });

        await fixture.SetTemporaryAsync(false);

        Assert.Equal(2, fixture.SaveAttempts.Count);
        Assert.Same(fixture.SaveAttempts[0].Conversation, fixture.SaveAttempts[1].Conversation);
        Assert.Same(fixture.SaveAttempts[0].History, fixture.SaveAttempts[1].History);
        Assert.Same(fixture.SaveAttempts[0].Context, fixture.SaveAttempts[1].Context);
        Assert.Equal(original.Id, fixture.Current.Id);
        Assert.False(fixture.Current.IsTemporary);
        Assert.Equal(historyIds, fixture.Messages.Select(message => message.Id));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normal_history_cannot_be_relabelled_temporary_and_a_new_identity_invalidates_old_presentation(bool page)
    {
        using var fixture = new Fixture(page);
        await fixture.LoadAsync(false);
        var original = fixture.Current;
        var guard = fixture.PresentationGuard();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.SetTemporaryAsync(true));
        Assert.Equal(original.Id, fixture.Current.Id);
        Assert.False(fixture.Current.IsTemporary);
        Assert.Equal(0, fixture.OrdinaryWrites);
        fixture.NewChat();
        Assert.False(guard());
        Assert.Empty(fixture.Messages);
    }

    [AvaloniaFact]
    public async Task Held_attachment_import_prevents_empty_normal_chat_becoming_temporary()
    {
        using var fixture = new Fixture(page: true);
        await fixture.LoadAsync(false);
        fixture.ClearHistory();
        var original = fixture.Current;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var branch = new TaskCompletionSource<ConversationBranch?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var import = fixture.BeginHeldImport(entered, branch);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.SetTemporaryAsync(true));
            Assert.Throws<InvalidOperationException>(fixture.NewChat);
            Assert.Equal(original.Id, fixture.Current.Id);
            Assert.False(fixture.Current.IsTemporary);
            Assert.Empty(fixture.Messages);
        }
        finally { branch.TrySetException(new IOException("Controlled stopped attachment import")); }
        await Assert.ThrowsAsync<IOException>(() => import);
        Assert.Equal(0, fixture.OrdinaryWrites);
    }

    [AvaloniaFact]
    public async Task Actual_legacy_page_asks_every_request_and_cancel_preserves_draft_and_prior_turns()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var fixture = new Fixture(page: true);
        await fixture.LoadAsync(true);
        var page = Assert.IsType<ChatPage>(fixture.Owner);
        page.SelectedModel = fixture.Model;
        var window = new Window { Width = 900, Height = 700, Content = page };
        window.Show();
        try
        {
            page.Composer = "Third question";
            var send = page.SendCommand.ExecuteAsync();
            await WaitForChoiceAsync(window, "choice-secondary", cancellation);
            Assert.Empty(fixture.Requests);
            Assert.Equal("Third question", page.Composer);
            FindChoice(window, "choice-secondary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await send;
            Assert.Single(fixture.Requests);
            Assert.Contains(fixture.Requests[0].Messages, message => message.Role == "user" && message.Content == "Question");
            Assert.Contains(fixture.Requests[0].Messages, message => message.Role == "assistant" && message.Content == "Answer");
            Assert.Equal(4, page.Messages.Count);
            Assert.True(page.IsTemporary);
            Assert.Equal(0, fixture.OrdinaryWrites);

            page.Composer = "Keep this draft";
            var cancel = page.SendCommand.ExecuteAsync();
            await WaitForChoiceAsync(window, "choice-cancel", cancellation);
            Assert.Single(fixture.Requests);
            FindChoice(window, "choice-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await cancel;
            Assert.Single(fixture.Requests);
            Assert.Equal("Keep this draft", page.Composer);
            Assert.Equal(4, page.Messages.Count);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ollama_wake_does_not_claim_ready_or_send_when_model_discovery_fails(bool page)
    {
        using var fixture = new Fixture(page, wakeLocalModel: true);
        fixture.SelectModel(fixture.Model); fixture.Composer = "An original unsent draft";
        fixture.DiscoverModels = () => Task.FromException<IReadOnlyList<ModelDescriptor>>(new IOException("Controlled catalogue unavailable"));

        await fixture.SendAsync();

        Assert.Equal(2, fixture.AvailabilityReads); Assert.Equal(1, fixture.CatalogueReads);
        Assert.Contains("Controlled catalogue unavailable", fixture.Status);
        Assert.DoesNotContain("ready", fixture.Status, StringComparison.OrdinalIgnoreCase);
        Assert.True(fixture.OfflineWarning);
        Assert.Empty(fixture.Catalogue);
        Assert.Equal(fixture.Model.Name, fixture.SelectedModel?.Name);
        Assert.Equal(fixture.Model.Name, fixture.Preferences.DefaultModel);
        Assert.Equal(fixture.Model.Name, fixture.ReopenSavedModel());
        Assert.Equal("An original unsent draft", fixture.Composer);
        Assert.Empty(fixture.Requests); Assert.Equal(0, fixture.OrdinaryWrites);
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Ollama_wake_requires_the_requested_model_without_switching_saved_preferences(bool page, bool empty)
    {
        using var fixture = new Fixture(page, wakeLocalModel: true);
        fixture.SelectModel(fixture.Model); fixture.Composer = "Keep my exact selected model";
        var other = fixture.Model with { Name = "another-controlled-local-model" };
        fixture.DiscoverModels = () => Task.FromResult<IReadOnlyList<ModelDescriptor>>(empty ? [] : [other]);

        await fixture.SendAsync();

        Assert.Equal(2, fixture.AvailabilityReads); Assert.Equal(1, fixture.CatalogueReads);
        Assert.DoesNotContain("ready", fixture.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(empty ? "no models" : "selected model", fixture.Status, StringComparison.OrdinalIgnoreCase);
        Assert.False(fixture.OfflineWarning); // The actual endpoint is up; availability of this model is the missing fact.
        Assert.Equal(empty ? 0 : 1, fixture.Catalogue.Count);
        Assert.Same(fixture.Model, fixture.SelectedModel);
        Assert.Equal(fixture.Model.Name, fixture.Preferences.DefaultModel);
        Assert.Equal(fixture.Model.Name, fixture.ReopenSavedModel());
        Assert.Equal("Keep my exact selected model", fixture.Composer);
        Assert.Empty(fixture.Requests); Assert.Equal(0, fixture.OrdinaryWrites);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ollama_wake_ready_uses_the_actual_matching_catalogue_descriptor(bool page)
    {
        using var fixture = new Fixture(page, wakeLocalModel: true);
        fixture.SelectModel(fixture.Model);
        var refreshed = fixture.Model with { SizeBytes = 42, Family = "Observed refreshed metadata" };
        var other = fixture.Model with { Name = "another-controlled-local-model" };
        fixture.DiscoverModels = () => Task.FromResult<IReadOnlyList<ModelDescriptor>>([other, refreshed]);

        Assert.True(await fixture.EnsureModelAsync());

        Assert.Equal(2, fixture.AvailabilityReads); Assert.Equal(1, fixture.CatalogueReads);
        Assert.Same(refreshed, fixture.SelectedModel);
        Assert.Equal("Ollama is ready.", fixture.Status); Assert.False(fixture.OfflineWarning);
        Assert.Equal(fixture.Model.Name, fixture.Preferences.DefaultModel);
        Assert.Empty(fixture.Requests); Assert.Equal(0, fixture.OrdinaryWrites);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ollama_wake_does_not_probe_a_model_owned_by_an_actual_registered_remote_provider(bool page)
    {
        using var fixture = new Fixture(page, wakeLocalModel: true);
        var remote = fixture.Model with { Name = "controlled-remote:model-v1" };
        fixture.SelectModel(remote);

        Assert.True(await fixture.EnsureModelAsync()); // This check owns only the local engine, not remote readiness.

        Assert.Equal(0, fixture.AvailabilityReads); Assert.Equal(0, fixture.CatalogueReads);
        Assert.Same(remote, fixture.SelectedModel);
        Assert.DoesNotContain("Ollama is ready", fixture.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Requests); Assert.Equal(0, fixture.OrdinaryWrites);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ollama_wake_revalidates_untagged_local_names_too(bool page)
    {
        using var fixture = new Fixture(page, wakeLocalModel: true);
        var local = fixture.Model with { Name = "controlled-local-model" };
        fixture.SelectModel(local);
        fixture.DiscoverModels = () => Task.FromResult<IReadOnlyList<ModelDescriptor>>([local]);

        Assert.True(await fixture.EnsureModelAsync());

        Assert.Equal(2, fixture.AvailabilityReads); Assert.Equal(1, fixture.CatalogueReads);
        Assert.Same(local, fixture.SelectedModel); Assert.Equal("Ollama is ready.", fixture.Status);
    }

    [AvaloniaTheory]
    [InlineData(false, 0, false)]
    [InlineData(false, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 2, true)]
    public async Task Ollama_wake_preserves_a_newer_user_choice_and_refuses_the_original_send(bool page, int boundary, bool returnToOriginal)
    {
        using var fixture = new Fixture(page, wakeLocalModel: true);
        fixture.SelectModel(fixture.Model); fixture.Composer = "Retain this original request";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newer = fixture.Model with { Name = "another-real-selection:latest" };
        fixture.ProbeAvailability = async count =>
        {
            if ((boundary == 0 && count == 1) || (boundary == 1 && count == 2))
            { entered.TrySetResult(); await release.Task; }
            return count > 1;
        };
        fixture.DiscoverModels = async () =>
        {
            if (boundary == 2) { entered.TrySetResult(); await release.Task; }
            return new[] { fixture.Model, newer };
        };

        var send = fixture.SendAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        fixture.SelectModel(newer);
        if (returnToOriginal) fixture.SelectModel(fixture.Model);
        release.TrySetResult(true);
        await send.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var expected = returnToOriginal ? fixture.Model : newer;
        Assert.Same(expected, fixture.SelectedModel);
        Assert.Equal(expected.Name, fixture.Preferences.DefaultModel);
        Assert.Equal(expected.Name, fixture.ReopenSavedModel());
        Assert.Contains("selection changed", fixture.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ready", fixture.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Retain this original request", fixture.Composer);
        Assert.Empty(fixture.Requests); Assert.Equal(0, fixture.OrdinaryWrites);
    }

    private static Button FindChoice(Window window, string name) => window.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
    private static async Task WaitForChoiceAsync(Window window, string name, CancellationToken cancellation)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            window.UpdateLayout();
            if (window.GetVisualDescendants().OfType<Button>().Any(button => button.Name == name)) return;
            await Task.Delay(10, cancellation);
        }
        Assert.Fail("The actual legacy page did not show its Memory choice.");
    }

    public interface ILegacyConversationProbe : IConversationRepository, ITemporaryConversationSaveRepository { }
    public class BoundaryProbe : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = (method, _) => throw new InvalidOperationException("Unexpected boundary: " + method.Name);
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
    private static T Probe<T>(Func<MethodInfo, object?[], object?>? handler = null) where T : class
    {
        var result = DispatchProxy.Create<T, BoundaryProbe>();
        if (handler is not null) ((BoundaryProbe)(object)result).Handler = handler;
        return result;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "9to1-legacy-temp", Guid.NewGuid().ToString("N"));
        private readonly object _owner;
        private readonly Type _type;
        private Conversation? _stored;
        public int OrdinaryWrites;
        public object Owner => _owner;
        public ModelDescriptor Model { get; } = new("legacy:fixture", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);
        public List<OllamaChatRequest> Requests { get; } = [];
        public UserPreferencesService Preferences { get; private set; } = null!;
        public Func<Task<IReadOnlyList<ModelDescriptor>>> DiscoverModels { get; set; } = null!;
        public Func<int, Task<bool>> ProbeAvailability { get; set; } = null!;
        public IAppPaths Paths { get; private set; } = null!;
        public string? ReopenSavedModel() => new UserPreferencesService(Paths).DefaultModel;
        public int AvailabilityReads { get; private set; }
        public int CatalogueReads { get; private set; }
        public IReadOnlyList<ModelDescriptor> Catalogue => ((System.Collections.ObjectModel.ObservableCollection<ModelDescriptor>)_type.GetProperty("Models")!.GetValue(_owner)!).ToArray();
        public ModelDescriptor? SelectedModel => (ModelDescriptor?)_type.GetProperty("SelectedModel")!.GetValue(_owner);
        public string Status => (string)_type.GetProperty("Status")!.GetValue(_owner)!;
        public bool OfflineWarning => (bool)_type.GetProperty("IsOllamaOfflineWarningVisible")!.GetValue(_owner)!;
        public string Composer { get => (string)_type.GetProperty("Composer")!.GetValue(_owner)!; set => _type.GetProperty("Composer")!.SetValue(_owner, value); }
        public void SelectModel(ModelDescriptor value) => _type.GetProperty("SelectedModel")!.SetValue(_owner, value);
        public Task SendAsync() => _owner is ChatPage page ? page.SendCommand.ExecuteAsync() : ((ChatPageViewModel)_owner).SendCommand.ExecuteAsync();
        public Task<bool> EnsureModelAsync() => (Task<bool>)_type.GetMethod("EnsureSelectedModelAvailableAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_owner, null)!;

        public ChatMessage[] SeedHistory { get; private set; } = [];
        public List<(Conversation Conversation, IReadOnlyList<ChatMessage> History, IReadOnlyList<ConversationContextEntry> Context)> SaveAttempts { get; } = [];
        public Func<Conversation, Task<Conversation>> Save = conversation => Task.FromResult(conversation with { IsTemporary = false });
        public Conversation Current => (Conversation)_type.GetField("_conversation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_owner)!;
        public System.Collections.ObjectModel.ObservableCollection<MessageBubbleViewModel> Messages =>
            (System.Collections.ObjectModel.ObservableCollection<MessageBubbleViewModel>)_type.GetProperty("Messages")!.GetValue(_owner)!;

        public Fixture(bool page, bool wakeLocalModel = false)
        {
            if (wakeLocalModel) Model = Model with { Name = "qwen3:1.7b", Family = "Controlled local model" };
            DiscoverModels = () => Task.FromResult<IReadOnlyList<ModelDescriptor>>([Model]);
            ProbeAvailability = count => Task.FromResult(count > 1 || !wakeLocalModel);
            Directory.CreateDirectory(_directory);
            var repository = Probe<ILegacyConversationProbe>((method, args) => method.Name switch
            {
                nameof(IConversationRepository.GetAsync) => Task.FromResult(_stored?.Id == (Guid)args[0]! ? _stored : null),
                nameof(IConversationRepository.GetMessagesAsync) => Task.FromResult<IReadOnlyList<ChatMessage>>(SeedHistory),
                nameof(IConversationRepository.GetContextEntriesAsync) => Task.FromResult<IReadOnlyList<ConversationContextEntry>>([]),
                nameof(IConversationRepository.GetContextMessagesAsync) => Task.FromResult<IReadOnlyList<ChatMessage>>(SeedHistory),
                nameof(ITemporaryConversationSaveRepository.SaveTemporaryLocallyAsync) => SaveOriginal(args),
                _ => UnexpectedWrite(method.Name)
            });
            var paths = Probe<IAppPaths>((method, _) => method.Name == "get_DataDirectory" ? _directory : Path.Combine(_directory, method.Name));
            Paths = paths;
            var preferences = new UserPreferencesService(paths); Preferences = preferences;
            var remote = Probe<IModelProvider>((method, _) => method.Name switch
            {
                "get_Id" => "controlled-remote",
                "get_DisplayName" => "Controlled remote provider",
                "get_IsLocal" => false,
                _ => throw new InvalidOperationException("Unexpected remote provider operation: " + method.Name)
            });
            var providers = new ModelProviderRegistry([remote]);
            var client = Probe<IOllamaClient>((method, args) => method.Name switch
            {
                "get_TransportEndpoint" => new Uri("http://127.0.0.1:11434"),
                "get_IsDeviceLocalTransportVerified" => true,
                nameof(IOllamaClient.IsAvailableAsync) => ProbeAvailability(++AvailabilityReads),
                nameof(IOllamaClient.GetModelsAsync) => DiscoverOriginalModels(),
                nameof(IOllamaClient.StreamChatAsync) => CaptureResponse((OllamaChatRequest)args[0]!),
                _ => throw new InvalidOperationException("Unexpected model boundary: " + method.Name)
            });
            var preflight = new CapabilityPreflightService();
            var sessions = new ChatSessionService(repository, client, preflight, Probe<IConversationSafetyService>((method, _) =>
                    method.Name == nameof(IConversationSafetyService.EnsureMayActAsync) ? Task.CompletedTask : throw new InvalidOperationException("Unexpected safety boundary: " + method.Name)),
                new WorkspaceToolRuntime(Probe<IWorkspaceToolService>()), new ComputerToolRuntime(Probe<IComputerToolService>((method, _) =>
                    method.Name == "get_IsSupported" ? false : throw new InvalidOperationException("Unexpected computer boundary: " + method.Name))));
            _owner = page
                ? new ChatPage(new HavenEventBus(), HavenMode.Chat, repository, Probe<IContainerRepository>(), Probe<ICatalogRepository>(), client,
                    sessions, preferences, preflight, Probe<IWorkspaceStateRepository>(), Probe<IProjectIntelligenceService>(),
                    ollamaWake: new OllamaWakeService(client, providers))
                : new ChatPageViewModel(HavenMode.Chat, repository, Probe<IContainerRepository>(), Probe<ICatalogRepository>(), client,
                    sessions, preferences, preflight, Probe<IWorkspaceStateRepository>(), Probe<IProjectIntelligenceService>(),
                    ollamaWake: new OllamaWakeService(client, providers));
            _type = _owner.GetType();
            if (page)
            {
                // App.Services is shared by the headless application. Replace this
                // instance's already borrowed persistence port before any operation;
                // do not mutate the global container or touch another profile's database.
                var production = Probe<IConversationProductionRepository>((method, _) => method.Name switch
                {
                    nameof(IConversationProductionRepository.GetCurrentBranchAsync) => Task.FromResult<ConversationBranch?>(null),
                    nameof(IConversationProductionRepository.GetDraftAsync) => Task.FromResult<ConversationDraft?>(null),
                    _ => throw new InvalidOperationException("Unexpected production boundary: " + method.Name)
                });
                _type.GetField("_production", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_owner, production);
                _type.GetField("_attachmentService", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_owner, null);
                _type.GetField("_paths", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_owner, paths);
            }

        }

        private Task<IReadOnlyList<ModelDescriptor>> DiscoverOriginalModels() { CatalogueReads++; return DiscoverModels(); }

        private async IAsyncEnumerable<string> CaptureResponse(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Requests.Add(request);
            await Task.CompletedTask;
            yield return "Actual controlled response";
        }
        private object UnexpectedWrite(string name)
        { OrdinaryWrites++; throw new InvalidOperationException("Unexpected repository mutation: " + name); }
        private Task<Conversation> SaveOriginal(object?[] args)
        {
            var conversation = (Conversation)args[0]!;
            SaveAttempts.Add((conversation, (IReadOnlyList<ChatMessage>)args[1]!, (IReadOnlyList<ConversationContextEntry>)args[2]!));
            return Save(conversation);
        }
        public async Task LoadAsync(bool temporary)
        {
            var now = DateTimeOffset.UtcNow;
            _stored = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Original", null, null, false, false, now, now, SpaceId: Guid.NewGuid());
            // A stored descriptor never supplies Temporary custody. Start through the
            // real empty-scope conversion and seed only its visible in-memory history.
            SeedHistory = temporary ? [] : NewHistory(_stored.Id, now);
            if (_owner is ChatPage page) await page.LoadConversationAsync(_stored.Id, TestContext.Current.CancellationToken);
            else await ((ChatPageViewModel)_owner).LoadConversationAsync(_stored.Id, TestContext.Current.CancellationToken);
            if (temporary)
            {
                await SetTemporaryAsync(true);
                SeedHistory = NewHistory(Current.Id, now);
                foreach (var message in SeedHistory) Messages.Add(new MessageBubbleViewModel(message));
            }
        }
        private static ChatMessage[] NewHistory(Guid id, DateTimeOffset now) =>
            [new(Guid.NewGuid(), id, MessageRole.User, "Question", null, null, null, now),
             new(Guid.NewGuid(), id, MessageRole.Assistant, "Answer", "Haven", "fixture", "{\"proof\":\"source-proof\"}", now)];
        public void ClearHistory() { SeedHistory = []; Messages.Clear(); }
        public Task BeginHeldImport(TaskCompletionSource entered, TaskCompletionSource<ConversationBranch?> branch)
        {
            var production = Probe<IConversationProductionRepository>((method, _) =>
            {
                if (method.Name != nameof(IConversationProductionRepository.GetCurrentBranchAsync))
                    throw new InvalidOperationException("Unexpected import production boundary: " + method.Name);
                entered.TrySetResult(); return branch.Task;
            });
            _type.GetField("_production", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_owner, production);
            _type.GetField("_attachmentService", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_owner, Probe<IMessageAttachmentService>());
            var path = Path.Combine(_directory, "held-source.txt"); File.WriteAllText(path, "Actual attachment fixture");
            return InvokePrivateAsync("ImportAttachmentsAsync", (object)new[] { path });
        }
        public Task SetTemporaryAsync(bool value) => _owner is ChatPage page ? page.SetTemporaryAsync(value) : ((ChatPageViewModel)_owner).SetTemporaryAsync(value);
        public Task BranchAsync() => _owner is ChatPage page ? page.BranchCurrentAsync() : ((ChatPageViewModel)_owner).BranchCurrentAsync();
        public void NewChat() { if (_owner is ChatPage page) page.NewChat(); else ((ChatPageViewModel)_owner).NewChat(); }
        public Func<bool> PresentationGuard() => _owner is ChatPage page ? page.CapturePresentationGuard() : ((ChatPageViewModel)_owner).CapturePresentationGuard();
        public Task InvokePrivateAsync(string method, params object?[] arguments) => (Task)_type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_owner, arguments)!;
        public void Dispose()
        {
            if (_owner is ChatPage) _type.GetMethod("Detach", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_owner, null);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
