using System.Net;
using System.Net.Sockets;
using System.Reflection;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.Overlay;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Browse.Host.Tests;

public sealed class OverlayChatInitializationTests
{
    // This is the production NewChatPage, factory and original-work lifetime, with
    // only external I/O controlled. The held model read exposes the construction race.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conversation_selection_waits_for_actual_page_initialization_then_settled_page_closes(bool restore)
    {
        using var rig = new Rig();
        Task<NewChatPage?> creating = restore
            ? rig.Factory.RestoreAsync(rig.Saved.Id, CancellationToken.None)
            : AsNullable(rig.Factory.CreateNewAsync(CancellationToken.None));
        NewChatPage? page = null;
        try
        {
            await rig.ModelsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // Fresh persists its new target; Restore reads the selected target's history.
            // Neither may happen while construction still owns its earlier target.
            Assert.Equal(0, rig.ConversationSelections);
            Assert.False(creating.IsCompleted);
            rig.Models.TrySetResult([]);
            page = await creating.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(page);
            Assert.True(page.OriginalInitialization.IsCompletedSuccessfully);
            Assert.Equal(1, rig.ConversationSelections);
            if (restore) Assert.Equal(rig.Saved.Id, page.ConversationId);
            // This case models a settled UI, not closing while queued background scroll
            // publication is still pending (that separate failure remains unaccepted).
            await SettleAdmittedPageOriginalsAsync(page).WaitAsync(TimeSpan.FromSeconds(10));
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            rig.Models.TrySetResult([]);
            page ??= await creating.WaitAsync(TimeSpan.FromSeconds(10));
            if (page is not null) await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Cancelled_creation_joins_held_original_model_read_before_returning_failure()
    {
        using var rig = new Rig();
        using var cancellation = new CancellationTokenSource();
        var creating = rig.Factory.CreateNewAsync(cancellation.Token);
        try
        {
            await rig.ModelsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            await rig.ModelsCancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(creating.IsCompleted); // The actual I/O deliberately has not acknowledged stop.
            Assert.Equal(0, rig.ConversationSelections);
            rig.Models.TrySetResult([]);
            var failure = await Record.ExceptionAsync(() => creating.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(failure);
            Assert.Contains(AllCauses(failure), error => error is OperationCanceledException cancelled
                && cancelled.CancellationToken == cancellation.Token);
            Assert.Equal(0, rig.ConversationSelections);
        }
        finally
        {
            rig.Models.TrySetResult([]);
            await Record.ExceptionAsync(() => creating.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }

    // The held second read exposes the discovery-admission visual gate and the stale
    // selector without constructing a second rig or a fake page.
    [AvaloniaFact]
    public async Task Held_retry_publishes_the_visual_gate_and_a_stale_selector_cannot_send()
    {
        using var rig = new Rig();
        NewChatPage? page = null;
        HeldModelRead? held = null;
        var creating = rig.Factory.CreateNewWithConstructionJoinAsync(
            DrainBackgroundPageOriginalsAsync, CancellationToken.None);
        try
        {
            await rig.ModelsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var offered = Model("llama3");
            rig.Models.TrySetResult([offered]);
            page = await creating.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ChatModelAvailability.Available, page.ModelAvailability);
            Assert.Equal("llama3", page.SelectedModelName);
            // The current, still-offered catalogue entry is fully actionable.
            page.SelectModel(offered);
            Assert.True(SendEnabled(page));

            // A held retry: the provider read never settles until this test releases it.
            held = new HeldModelRead();
            rig.NextModelRead = held;
            var refresh = page.RefreshModelsAsync();
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => page.ModelAvailability == ChatModelAvailability.Discovering);

            // Discovery admission publishes the truthful held state BEFORE the provider await,
            // so the previously enabled Send control is visibly gated during the retry.
            Assert.False(SendEnabled(page));

            // An older picker's descriptor cannot promote a held retry.
            page.SelectModel(offered);
            Assert.Equal(ChatModelAvailability.Discovering, page.ModelAvailability);
            Assert.False(SendEnabled(page));

            // A queued instruction stays queued: the backend gate holds and the draft survives.
            await page.SubmitOriginalInstructionAsync("preserved draft").WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("preserved draft", ComposerText(page));

            // The held read settles as a real expected provider-unavailability observation.
            var refused = new HttpRequestException("Connection refused (127.0.0.1:11434)",
                new SocketException((int)SocketError.ConnectionRefused));
            held.Result.SetException(refused);
            await refresh.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            Assert.Same(refused, page.ModelAvailabilityCause);
            Assert.False(SendEnabled(page));

            // The stale selector neither promotes availability nor clears the real cause.
            page.SelectModel(offered);
            Assert.Equal(ChatModelAvailability.Unavailable, page.ModelAvailability);
            Assert.Same(refused, page.ModelAvailabilityCause);
            Assert.False(SendEnabled(page));

            // No pending instruction is dispatched on a stale selector's strength.
            await Task.Delay(200);
            Assert.False(page.IsSending);
            Assert.False(page.HasStarted);
            Assert.Equal("preserved draft", ComposerText(page));

            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            rig.Models.TrySetResult([]);
            // A failed assertion must not leave the deliberately held provider read blocking the
            // cleanup join: release the SAME held read so the actual assertion stays the reported
            // failure instead of being masked by a close timeout.
            held?.Result.TrySetResult([]);
            page ??= await creating.WaitAsync(TimeSpan.FromSeconds(10));
            if (page is not null) await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [AvaloniaFact]
    public async Task Successful_catalogue_replacement_refuses_an_older_pickers_removed_descriptor()
    {
        using var rig = new Rig();
        NewChatPage? page = null;
        var creating = rig.Factory.CreateNewWithConstructionJoinAsync(
            DrainBackgroundPageOriginalsAsync, CancellationToken.None);
        try
        {
            await rig.ModelsEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var removed = Model("llama3");
            rig.Models.TrySetResult([removed]);
            page = await creating.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("llama3", page.SelectedModelName);

            // A newer successful catalogue no longer offers the older picker's model.
            var replacement = Model("qwen2");
            var held = new HeldModelRead();
            rig.NextModelRead = held;
            var refresh = page.RefreshModelsAsync();
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            held.Result.SetResult([replacement]);
            await refresh.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(ChatModelAvailability.Available, page.ModelAvailability);
            Assert.Equal("qwen2", page.SelectedModelName);
            page.SelectModel(replacement);
            Assert.True(SendEnabled(page)); // The current catalogue entry stays actionable.

            // The still-open old picker hands back its now-removed descriptor. The user's
            // configuration is preserved, but the stale descriptor never becomes actionable.
            page.SelectModel(removed);
            Assert.Equal("llama3", page.SelectedModelName);
            Assert.Equal(ChatModelAvailability.Available, page.ModelAvailability);
            Assert.False(SendEnabled(page));

            await page.SubmitOriginalInstructionAsync("stale picker gate").WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(200);
            Assert.False(page.IsSending);
            Assert.False(page.HasStarted);
            Assert.Equal("stale picker gate", ComposerText(page)); // Not dispatched; no send occurred.

            await DrainBackgroundPageOriginalsAsync(page);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            rig.Models.TrySetResult([]);
            page ??= await creating.WaitAsync(TimeSpan.FromSeconds(10));
            if (page is not null) await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static ModelDescriptor Model(string name) =>
        new(name, 1024, "llama", "1B", "Q4_0", new HashSet<ToolCapability>(), DateTimeOffset.UnixEpoch);

    /// <summary>One later GetModelsAsync read the test explicitly arms and releases.</summary>
    private sealed class HeldModelRead
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<IReadOnlyList<ModelDescriptor>> Result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static ChatHavenScene Scene(NewChatPage page) =>
        (ChatHavenScene)typeof(NewChatPage)
            .GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static bool SendEnabled(NewChatPage page) =>
        Scene(page).SendButton.GetValue(HavenProperties.Enabled);

    private static string ComposerText(NewChatPage page) => Scene(page).Instruction.Text ?? string.Empty;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private static IEnumerable<Exception> AllCauses(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in AllCauses(inner)) yield return cause;
        else if (error.InnerException is { } inner)
            foreach (var cause in AllCauses(inner)) yield return cause;
    }

    private static Task SettleAdmittedPageOriginalsAsync(NewChatPage page) =>
        Task.WhenAll(AdmittedPageOriginalTasks(page));

    private static Task[] AdmittedPageOriginalTasks(NewChatPage page)
    {
        // Test-only observation of the real owner's exact tasks. Dispatcher idle alone
        // does not join their asynchronous continuations. With no further input admitted,
        // this establishes the settled-page precondition without a delay or a fake owner.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var owner = (DesktopOriginalWorkLifetime)typeof(NewChatPage).GetField("_originalWork", flags)!.GetValue(page)!;
        var gate = typeof(DesktopOriginalWorkLifetime).GetField("_gate", flags)!.GetValue(owner)!;
        lock (gate)
            return ((IEnumerable<DesktopOriginalWorkLifetime.Original>)typeof(DesktopOriginalWorkLifetime)
                .GetField("_originals", flags)!.GetValue(owner)!).Select(original => original.Task).ToArray();
    }

    /// <summary>The real page also queues a background-priority transcript scroll whose admitted
    /// original is created lazily on the dispatcher. Flush that background work and join the SAME
    /// admitted originals until no page original is still pending, so a close never races a
    /// scroll that was still waiting for its dispatcher callback. This reuses the existing settle
    /// observation, adds no second owner and no fake page, and still surfaces every actual
    /// failure a joined original produces.</summary>
    private static async Task DrainBackgroundPageOriginalsAsync(NewChatPage page)
    {
        for (var pass = 0; pass != 8; pass++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var pending = AdmittedPageOriginalTasks(page).Where(task => !task.IsCompleted).ToArray();
            if (pending.Length == 0) return;
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.DoesNotContain(AdmittedPageOriginalTasks(page), task => !task.IsCompleted);
    }

    private static async Task<NewChatPage?> AsNullable(Task<NewChatPage> task) => await task;

    private sealed class Rig : IDisposable
    {
        public readonly TaskCompletionSource<IReadOnlyList<ModelDescriptor>> Models = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ModelsEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ModelsCancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly Conversation Saved = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Saved", null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public int ConversationSelections;
        public OverlayChatSessionFactory Factory { get; }
        // The ARMED next GetModelsAsync read, if any, so a test can hold a later retry open.
        public HeldModelRead? NextModelRead;
        private int _modelReadCount;
        private readonly ServiceProvider _services;
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "9to1-chat-initialization-" + Guid.NewGuid().ToString("N"));

        public Rig()
        {
            Directory.CreateDirectory(_directory);
            var conversations = Proxy<IConversationRepository>((name, args) => name switch
            {
                "GetAsync" => Task.FromResult<Conversation?>(Saved),
                "GetMessagesAsync" => SelectHistory(),
                "UpsertConversationAsync" => SelectFresh(),
                "GetContextEntriesAsync" => Task.FromResult<IReadOnlyList<ConversationContextEntry>>([]),
                _ => throw Unexpected(name)
            });
            var ollama = Proxy<IOllamaClient>((name, args) =>
            {
                if (name != "GetModelsAsync") throw Unexpected(name);
                var token = (CancellationToken)args![0]!;
                if (Interlocked.Increment(ref _modelReadCount) == 1)
                {
                    token.Register(() => ModelsCancelled.TrySetResult());
                    ModelsEntered.TrySetResult();
                    return Models.Task;
                }
                // A later retry is explicitly armed by the test that wants to hold it open;
                // an unarmed extra read is unexpected provider I/O.
                var held = NextModelRead ?? throw Unexpected("GetModelsAsync (unarmed later read)");
                NextModelRead = null;
                token.Register(() => held.Cancelled.TrySetResult());
                held.Entered.TrySetResult();
                return held.Result.Task;
            });
            var safety = Proxy<IConversationSafetyService>((name, args) => name switch
            {
                "GetSnapshotAsync" => Task.FromResult(new ConversationSafetySnapshot((Guid)args![0]!, 0, ConversationSafetyState.Active, null, 0)),
                _ => throw Unexpected(name)
            });
            var paths = Proxy<IAppPaths>((name, _) => name == "get_DataDirectory" ? _directory : throw Unexpected(name));
            var locals = new GenUiLocalActionRegistry();
            var instances = new GenUiInstanceStore();
            var services = new ServiceCollection()
                .AddSingleton(new HavenEventBus())
                .AddSingleton(conversations).AddSingleton(ollama).AddSingleton(safety)
                .AddSingleton(Proxy<IConversationVersioningService>())
                .AddSingleton(new UserPreferencesService(paths))
                .AddSingleton(new ChatSessionService(conversations, ollama, new CapabilityPreflightService(), safety,
                    new WorkspaceToolRuntime(Proxy<IWorkspaceToolService>()), new ComputerToolRuntime(Proxy<IComputerToolService>())))
                .AddSingleton(locals).AddSingleton(instances)
                .AddSingleton(new GenerativeUiEventRouter([], Proxy<IGenUiEventAuditSink>(), instances));
            foreach (var type in new[] {
                typeof(CalculatorTemplateRuntime), typeof(StructuredFormTemplateRuntime), typeof(ChoicePromptTemplateRuntime),
                typeof(ChecklistTemplateRuntime), typeof(DataGridTemplateRuntime), typeof(CardDeckTemplateRuntime),
                typeof(GraphTemplateRuntime), typeof(TaskListTemplateRuntime), typeof(DashboardTemplateRuntime),
                typeof(AssessmentTemplateRuntime), typeof(WorkflowTemplateRuntime), typeof(CustomTemplateRuntime) })
                services.AddSingleton(type);
            _services = services.BuildServiceProvider();
            var capabilities = new CapabilityRegistryService(Proxy<ICapabilityRepository>((name, _) =>
                name == "GetCapabilitiesAsync" ? Task.FromResult<IReadOnlyList<CapabilityDefinition>>([]) : throw Unexpected(name)));
            var catalog = Proxy<ICatalogRepository>((name, _) => name switch {
                "GetAgentsAsync" => Task.FromResult<IReadOnlyList<AgentDefinition>>([]),
                "GetPromptsAsync" => Task.FromResult<IReadOnlyList<PromptDefinition>>([]),
                _ => throw Unexpected(name) });
            var modes = Proxy<IModeRegistry>((name, _) => name == "GetModesAsync"
                ? Task.FromResult<IReadOnlyList<ModeDefinition>>([]) : throw Unexpected(name));
            Factory = new OverlayChatSessionFactory(_services, conversations, capabilities, catalog, modes);
        }
        private Task SelectFresh() { ConversationSelections++; return Task.CompletedTask; }
        private Task<IReadOnlyList<ChatMessage>> SelectHistory() { ConversationSelections++; return Task.FromResult<IReadOnlyList<ChatMessage>>([]); }
        public void Dispose() { _services.Dispose(); Directory.Delete(_directory, recursive: true); }
    }

    private static InvalidOperationException Unexpected(string name) => new("Unexpected external I/O: " + name);
    private static T Proxy<T>(Func<string, object?[]?, object?>? invoke = null) where T : class
    {
        var value = DispatchProxy.Create<T, ControlledIo>();
        ((ControlledIo)(object)value).Call = invoke ?? ((name, _) => throw Unexpected(name));
        return value;
    }
    public class ControlledIo : DispatchProxy
    {
        public Func<string, object?[]?, object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args);
    }
}
