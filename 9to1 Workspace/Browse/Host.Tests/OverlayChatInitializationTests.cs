using System.Reflection;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Events;
using Haven.Desktop.Overlay;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
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

    private static IEnumerable<Exception> AllCauses(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                foreach (var cause in AllCauses(inner)) yield return cause;
        else if (error.InnerException is { } inner)
            foreach (var cause in AllCauses(inner)) yield return cause;
    }

    private static Task SettleAdmittedPageOriginalsAsync(NewChatPage page)
    {
        // Test-only observation of the real owner's exact tasks. Dispatcher idle alone
        // does not join their asynchronous continuations. With no further input admitted,
        // this establishes the settled-page precondition without a delay or a fake owner.
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var owner = (DesktopOriginalWorkLifetime)typeof(NewChatPage).GetField("_originalWork", flags)!.GetValue(page)!;
        var gate = typeof(DesktopOriginalWorkLifetime).GetField("_gate", flags)!.GetValue(owner)!;
        Task[] actual;
        lock (gate)
            actual = ((IEnumerable<DesktopOriginalWorkLifetime.Original>)typeof(DesktopOriginalWorkLifetime)
                .GetField("_originals", flags)!.GetValue(owner)!).Select(original => original.Task).ToArray();
        return Task.WhenAll(actual);
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
                ((CancellationToken)args![0]!).Register(() => ModelsCancelled.TrySetResult());
                ModelsEntered.TrySetResult();
                return Models.Task;
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
