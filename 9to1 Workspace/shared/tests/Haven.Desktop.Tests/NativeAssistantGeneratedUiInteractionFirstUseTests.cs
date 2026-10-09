#if !ANDROID
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
using OriginalPermissionRequest = HavenOS.Home.PermissionsTrustNotifications.HomePermissionRequest;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    private static readonly List<object[]> FailedProtectedGeneratedUiOwners = [];
    private const string ProtectedGeneratedPayload = """
        Explicitly fictional user-supplied local calculation. No model produced this declaration.
        ```haven-ui
        {"version":1,"template":"calculator","inputs":{"expression":"2 + 3"}}
        ```
        """;

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Rendered_interaction_requires_individual_Home_save_and_restores_audited_state_after_real_store_reopen() =>
        RunProtectedGeneratedControl(async (rig, graphs) =>
        {
            var binding = await rig.CreateAsync(new() { Name = "Fictional protected calculation", Memory = new(false) });
            var message = await AddProtectedGeneratedMessage(rig, binding);
            var graph = new ProtectedGeneratedGraph(rig); graphs.Add(graph);
            CanonicalGeneratedUiOriginalCommitReceipt? receipt = null;
            await WithProtectedGeneratedView(rig, graph, async (window, surface, controller, host) =>
            {
                await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                var generated = Assert.Single(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
                var completed = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                generated.ActionCompleted += (_, actual) => completed.TrySetResult(actual);
                var calculate = Assert.Single(generated.GetVisualDescendants().OfType<Button>(), item => Equals(item.Content, "Calculate"));
                calculate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var action = await completed.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
                Assert.Equal(GenUiActionStatus.Completed, action.Status);
                Assert.True(graph.Instances.TryObserveOriginalMutation(action, out var mutation));
                Assert.NotNull(mutation); await FlushNativeMemoryUi(window);
                Assert.Contains(generated.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "5");
                var save = ClickProtectedGeneratedControl(window, host, "Save generated interaction 1");
                var request = await DecideProtectedGeneratedSave(rig, save, HomeApprovalChoice.Accept);
                await save.WaitAsync(TimeSpan.FromSeconds(15), Token);
                var review = ClickProtectedGeneratedControl(window, host, "Review generated interaction 1"); await review;
                var observation = Assert.Single(host.OriginalInteractionObservations);
                Assert.True(graph.Writer.IsIssuedOriginalObservation(observation));
                Assert.Equal(CanonicalGeneratedUiOriginalReadState.Restorable, observation.State);
                receipt = Assert.IsType<CanonicalGeneratedUiOriginalCommitReceipt>(observation.OriginalReceipt);
                Assert.Equal(request, receipt.HomeApprovalRequestId);
                Assert.True(await graph.Home.VerifyOriginalStoredAuditWithinSourceAsync(receipt, Scope, graph.Retain, Token));
                var actualRequest = Assert.IsType<OriginalPermissionRequest>(await rig.Permissions.ReadImportRequestWithinOriginalSourceAsync(request, Scope, graph.Retain, Token));
                Assert.Equal(HomePermissionRequestState.Succeeded, actualRequest.State);
                Assert.Equal("HOME_GENUI_INTERACTION_SAVED", actualRequest.ResultCode);
                Assert.Equal(receipt.ObservedActor.AuthenticationRevision, actualRequest.Caller.IdentityVersion);
                var actualConversation = Assert.IsType<AssistantConversationData>(controller.Snapshot.Conversation);
                Assert.Equal(message, Assert.Single(actualConversation.Messages));
                Assert.Null(actualConversation.CanonicalTask); Assert.Empty(controller.Snapshot.Models);
            });
            await graph.CloseAsync(); await rig.ReopenAsync();
            var reopened = new ProtectedGeneratedGraph(rig); graphs.Add(reopened);
            await WithProtectedGeneratedView(rig, reopened, async (window, surface, controller, host) =>
            {
                await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                var initial = Assert.Single(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
                Assert.Contains(initial.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Ready");
                var review = ClickProtectedGeneratedControl(window, host, "Review generated interaction 1"); await review;
                var observation = Assert.Single(host.OriginalInteractionObservations);
                Assert.Equal(CanonicalGeneratedUiOriginalReadState.Restorable, observation.State);
                var stored = Assert.IsType<CanonicalGeneratedUiOriginalCommitReceipt>(observation.OriginalReceipt);
                Assert.Equal(receipt, stored); Assert.NotEqual(initial.Document!.Origin.InstanceId, stored.InstanceId);
                var restore = ClickProtectedGeneratedControl(window, host, "Restore generated interaction 1"); await restore;
                await FlushNativeMemoryUi(window);
                var restored = Assert.Single(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
                Assert.NotSame(initial, restored); Assert.NotEqual(stored.InstanceId, restored.Document!.Origin.InstanceId);
                Assert.Contains(restored.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "5");
                var actualConversation = Assert.IsType<AssistantConversationData>(controller.Snapshot.Conversation);
                Assert.Equal(message, Assert.Single(actualConversation.Messages));
                Assert.False(surface.HasUnsavedChanges); Assert.Null(actualConversation.CanonicalTask);
                Assert.Equal(1L, await CountProtectedGeneratedRows(rig, "genui_apps"));
                Assert.Equal(1L, await CountProtectedGeneratedOperations(rig));
            });
        });

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Actual_Home_decline_keeps_the_runtime_and_saved_message_without_stored_effect_and_all_owners_close() =>
        RunProtectedGeneratedControl(async (rig, graphs) =>
        {
            var binding = await rig.CreateAsync(new() { Name = "Fictional declined calculation", Memory = new(false) });
            var message = await AddProtectedGeneratedMessage(rig, binding);
            var graph = new ProtectedGeneratedGraph(rig); graphs.Add(graph);
            await WithProtectedGeneratedView(rig, graph, async (window, surface, controller, host) =>
            {
                await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                var generated = Assert.Single(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
                var original = Assert.IsType<GenUiDocument>(generated.Document);
                var save = ClickProtectedGeneratedControl(window, host, "Save generated interaction 1");
                var request = await DecideProtectedGeneratedSave(rig, save, HomeApprovalChoice.Decline); await save;
                var actual = Assert.IsType<OriginalPermissionRequest>(await rig.Permissions.ReadImportRequestWithinOriginalSourceAsync(request, Scope, graph.Retain, Token));
                Assert.Equal(HomePermissionRequestState.Denied, actual.State);
                Assert.Same(original, graph.Instances.TryGet(original.Origin.InstanceId));
                Assert.Equal(message, Assert.Single(controller.Snapshot.Conversation!.Messages));
                Assert.Equal(0L, await CountProtectedGeneratedRows(rig, "genui_apps")); Assert.Equal(0L, await CountProtectedGeneratedOperations(rig));
                Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Save declined in Home", StringComparison.Ordinal) == true);
            });
            var close = graph.CloseAsync(); graph.Retain(close); await close;
            Assert.Same(close, graph.CloseAsync()); Assert.True(close.IsCompletedSuccessfully);
        });

    [Haven.Desktop.Tests.FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public async Task Actual_atomic_publication_fault_keeps_the_same_raw_OCE_IO_graph_and_process_dependencies()
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var rig = new Rig(originalAdditionalPolicy: new HomeCanonicalGeneratedUiInteractionActionPolicySource());
            await using var diagnostic = GeneratedOriginalDiagnostic.TryCreate();
            ProtectedGeneratedGraph? graph = null; var roots = new List<object>(); var causes = new List<Exception>();
            try
            {
                await rig.InitializeAsync(true, importMemory: false);
                var binding = await rig.CreateAsync(new() { Name = "Fictional unknown save witness", Memory = new(false) });
                await AddProtectedGeneratedMessage(rig, binding); var actualGraph = graph = new(rig); roots.Add(actualGraph); actualGraph.Diagnostic = diagnostic; diagnostic?.Bind(actualGraph.Writer);
                await WithProtectedGeneratedView(rig, actualGraph, async (window, surface, controller, host) =>
                {
                    await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
                    await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
                    var review = ClickProtectedGeneratedControl(window, host, "Review generated interaction 1"); await review;
                    var observation = Assert.Single(host.OriginalInteractionObservations);
                    diagnostic?.Checkpoint("before-prepare");
                    var intent = await actualGraph.Writer.PrepareOriginalSaveWithinSourceAsync(observation, Guid.NewGuid(), Scope, actualGraph.Retain, Token); diagnostic?.Checkpoint("prepared");
                    var cancellation = new OperationCanceledException("Actual atomic publication"); var io = new IOException("Actual atomic publication sibling");
                    var originalFault = new AggregateException(cancellation, io); causes.Add(originalFault);
                    Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment>? atomic = null;
                    void RetainOriginal(Task raw)
                    {
                        actualGraph.Retain(raw);
                        if (raw is Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment> candidate && actualGraph.Writer.IsOriginalAtomicSaveTask(intent, candidate))
                        { atomic = candidate; roots.Add(candidate); diagnostic?.Checkpoint("atomic-captured", candidate); throw originalFault; }
                    }
                    var driver = actualGraph.Writer.CommitOriginalSaveWithinSourceAsync(intent, Scope, RetainOriginal, Token); actualGraph.Retain(driver); roots.Add(driver); diagnostic?.Checkpoint("driver-captured", driver);
                    var request = await DecideProtectedGeneratedSave(rig, driver, HomeApprovalChoice.Accept);
                    diagnostic?.Checkpoint("before-driver-wait", driver);
                    Assert.NotNull(await Record.ExceptionAsync(() => driver.WaitAsync(TimeSpan.FromSeconds(15), Token)));
                    diagnostic?.Checkpoint("after-driver-wait", driver);
                    var sameAtomic = Assert.IsAssignableFrom<Task<ICanonicalGeneratedUiOriginalSaveAcknowledgment>>(atomic);
                    Assert.True(driver.IsFaulted); Assert.False(driver.IsCanceled); Assert.True(sameAtomic.IsFaulted); Assert.False(sameAtomic.IsCanceled);
                    Assert.True(actualGraph.Writer.IsOriginalAtomicSaveTask(intent, sameAtomic));
                    causes.Add(driver.Exception!); causes.Add(sameAtomic.Exception!);
                    diagnostic?.Checkpoint("before-cause-proof", driver);
                    AssertKnownGeneratedCauseGraph(driver.Exception!, new[] { originalFault, sameAtomic.Exception! });
                    Assert.Contains(ProtectedGeneratedCauses(driver.Exception!), cause => ReferenceEquals(cause, cancellation));
                    Assert.Contains(ProtectedGeneratedCauses(driver.Exception!), cause => ReferenceEquals(cause, io));
                    diagnostic?.Checkpoint("after-cause-proof", driver);
                    Assert.True(actualGraph.Writer.IsOwnedOriginalSaveResourcesRelease(intent, sameAtomic));
                    Assert.False(actualGraph.Writer.IsAcknowledgedOriginalSaveSourceRefusal(driver));
                    var reviewed = Assert.IsType<OriginalPermissionRequest>(await rig.Permissions.ReadImportRequestWithinOriginalSourceAsync(request, Scope, actualGraph.Retain, Token));
                    Assert.NotEqual(HomePermissionRequestState.Succeeded, reviewed.State);
                    Assert.Equal(0L, await CountProtectedGeneratedRows(rig, "genui_apps")); Assert.Equal(0L, await CountProtectedGeneratedOperations(rig));
                    Assert.Null(actualGraph.Origins.OriginalClose); Assert.Null(actualGraph.Home.OriginalClose); Assert.Null(rig.Store.OriginalClose);
                });
                diagnostic?.Checkpoint("before-process-close");
                var close = actualGraph.CloseAsync(); roots.Add(close); diagnostic?.Checkpoint("process-close", close);
                Assert.NotNull(await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(15), Token)));
                diagnostic?.Checkpoint("after-process-close", close);
                Assert.Same(close, actualGraph.CloseAsync()); Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
                AssertKnownGeneratedCauseGraph(close.Exception!, causes);
                Assert.Null(actualGraph.Origins.OriginalClose); Assert.Null(actualGraph.Home.OriginalClose); Assert.Null(rig.Store.OriginalClose);
                return true;
            }
            finally
            {
                // The genuine unknown writer holds process provenance/Home/store/Den.
                // Closing the view neither claims refusal nor tears down these dependencies.
                lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, graph!, roots, causes]);
            }
        }, Token));
    }

    private static IEnumerable<Exception> ProtectedGeneratedCauses(Exception cause)
    {
        yield return cause;
        if (cause is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var item in ProtectedGeneratedCauses(child)) yield return item;
    }
    private static async Task RunProtectedGeneratedControl(Func<Rig, List<ProtectedGeneratedGraph>, Task> body)
    {
        using var session = HeadlessUnitTestSession.StartNew(typeof(Haven.Desktop.Tests.TestAppBuilder));
        Assert.True(await session.Dispatch<bool>(async () =>
        {
            var rig = new Rig(originalAdditionalPolicy: new HomeCanonicalGeneratedUiInteractionActionPolicySource());
            var graphs = new List<ProtectedGeneratedGraph>(); var errors = new List<Exception>();
            try { await rig.InitializeAsync(true, importMemory: false); await body(rig, graphs); }
            catch (Exception cause) { errors.Add(cause); }
            foreach (var graph in graphs)
                try { var close = graph.CloseAsync(); graph.Retain(close); await close; } catch (Exception cause) { errors.Add(graph.CloseAsync().Exception ?? cause); }
            if (graphs.All(graph => graph.OriginalClose?.IsCompletedSuccessfully == true))
            {
                Task? close = null; try { close = rig.CloseAsync(); rig.Retain(close); await close; }
                catch (Exception cause) { errors.Add(close?.Exception ?? cause); }
            }
            if (errors.Count != 0)
            {
                lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, graphs, errors]);
                throw new AggregateException("Actual protected generated owners and original close receipts remain retained.", errors);
            }
            return true;
        }, Token));
    }
    private static async Task<ChatMessage> AddProtectedGeneratedMessage(Rig rig, AssistantConversationBinding binding)
    {
        var message = new ChatMessage(Guid.NewGuid(), binding.Conversation.Id, MessageRole.User, ProtectedGeneratedPayload, null, null, null, DateTimeOffset.UtcNow);
        var raw = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(raw); await raw; return message;
    }
    private static Task ClickProtectedGeneratedControl(Window window, OriginalAssistantGeneratedUiHost host, string name)
    {
        var count = host.OriginalInteractionCommands.Count;
        var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), item => item.IsEffectivelyVisible && AutomationProperties.GetName(item) == name);
        Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var actual = host.OriginalInteractionCommands; Assert.Equal(count + 1, actual.Count); return actual[count];
    }
    private static async Task<string> DecideProtectedGeneratedSave(Rig rig, Task actual, HomeApprovalChoice choice)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(15))
        {
            var pending = (await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests.Where(request =>
                request.Scope.ActionName == HomeCanonicalGeneratedUiInteractionWriteSource.SaveAction && request.State == HomePermissionRequestState.PendingApproval).ToArray();
            if (pending.Length != 0)
            {
                var request = Assert.Single(pending); Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.True(request.Caller.IsVerified); Assert.NotNull(request.Impact.ResourceBinding);
                Assert.True((await rig.Permissions.DecideAsync(request.RequestId, choice, cancellationToken: Token)).Succeeded); return request.RequestId;
            }
            if (actual.IsCompleted) { await actual; throw new InvalidOperationException("The actual save ended without its individual Home review."); }
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The genuine individual generated interaction review was not observed.");
    }
    private static async Task<long> CountProtectedGeneratedRows(Rig rig, string table)
    {
        Assert.Equal("genui_apps", table);
        await using var connection = await rig.Database.OpenAsync(Token); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM genui_apps;"; return Convert.ToInt64(await command.ExecuteScalarAsync(Token));
    }
    private static async Task<long> CountProtectedGeneratedOperations(Rig rig)
    {
        await using var connection = await rig.Database.OpenAsync(Token); await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM settings WHERE key LIKE 'canonical.genui.operation.v1.%';";
        return Convert.ToInt64(await command.ExecuteScalarAsync(Token));
    }
    private sealed class ProtectedGeneratedGraph
    {
        internal readonly Rig Rig; internal readonly GenUiInstanceStore Instances = new();
        internal readonly GenUiLocalActionRegistry Actions = new();
        internal readonly OriginalAssistantGeneratedUiOriginOwner Origins;
        internal readonly CanonicalGeneratedUiInteractionOriginalOwner Writer;
        internal readonly HomeCanonicalGeneratedUiInteractionWriteSource Home;
        internal readonly List<Task> Originals = [];
        internal GeneratedOriginalDiagnostic? Diagnostic;
        private Task? _close; internal Task? OriginalClose => _close;
        internal ProtectedGeneratedGraph(Rig rig)
        {
            Rig = rig;
            Origins = new(rig.Home, rig.OriginalConversations, Assert.IsType<ConversationRepository>(rig.OriginalConversations),
                rig.Store, rig.Database, rig.Authority, Instances);
            Writer = new(rig.Store, rig.Database, new GenUiAppRepository(rig.Database), rig.Authority, Origins);
            HomeCanonicalGeneratedUiInteractionWriteSource? same = null;
            var resources = new ResourceAuthorizationService(rig.Profiles, [new HomeCanonicalGeneratedUiInteractionResourceResolver(() => same!)]);
            var broker = new HomeResourceOperationBroker(resources, rig.Permissions);
            Home = same = new(rig.OriginalStateStore, rig.Profiles, resources, broker, rig.Permissions, Writer);
            Writer.BindOriginalHomeWriteSource(Home); Origins.BindOriginalInteractionStore(Writer);
        }
        internal void Retain(Task actual) { lock (Originals) Originals.Add(actual); Diagnostic?.Track(actual); }
        internal Task CloseAsync()
        {
            Writer.DemandExternalOriginalJoin(); Origins.DemandExternalOriginalJoin(); Home.DemandExternalOriginalJoin();
            if (_close is not null) return _close;
            var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = Drain(begin.Task); Retain(_close); begin.SetResult(); return _close;
        }
        private async Task Drain(Task begin)
        {
            await begin; Writer.RequestOriginalPendingReviewWithdrawals();
            async Task Join(Func<Task> factory)
            {
                var raw = factory(); Retain(raw);
                try { await raw; } catch (Exception cause) { throw new AggregateException("The exact protected dependency remains retained.", raw.Exception ?? cause); }
            }
            await Join(Writer.CloseAndDrainOriginalAsync); await Join(Origins.CloseAndDrainOriginalAsync); await Join(Home.CloseAndDrainOriginalAsync);
        }
    }
    private static async Task WithProtectedGeneratedView(Rig rig, ProtectedGeneratedGraph graph,
        Func<Window, AssistantsNativeCuiSurface, AssistantsWorkspaceController, OriginalAssistantGeneratedUiHost, Task> body)
    {
        var diagnostic = graph.Diagnostic;
        var controller = new AssistantsWorkspaceController(rig.Bridge);
        var host = new OriginalAssistantGeneratedUiHost(controller, new GenerativeUiEventRouter([graph.Actions], new BoundedGenUiEventAuditSink(), graph.Instances),
            graph.Instances, new(graph.Actions, graph.Instances), new(graph.Actions, graph.Instances), new(graph.Actions), new(graph.Actions, graph.Instances));
        host.BindOriginalInteractionOrigins(graph.Origins); host.BindOriginalInteractionStore(graph.Writer);
        var window = new Window { Width = 1320, Height = 960 }; window.Show();
        AssistantsNativeCuiSurface? surface = null; var errors = new List<Exception>(); var closes = new List<Task>();
        try
        {
            surface = new(controller, new MemoryFixtureReadiness(), captureOriginalOwner: actual => surface = actual);
            surface.BindOriginalGeneratedUiHost(host); window.Content = surface;
            await surface.InitializeAsync(Token); await FlushNativeMemoryUi(window); diagnostic?.Checkpoint("before-view-body"); await body(window, surface, controller, host); diagnostic?.Checkpoint("after-view-body");
            Assert.True(await surface.PrepareToCloseAsync(Token));
        }
        catch (Exception cause) { diagnostic?.Checkpoint("view-body-failed"); errors.Add(cause); }
        async Task Join(string label, Func<Task> factory)
        {
            Task? raw = null;
            try { raw = factory(); closes.Add(raw); graph.Retain(raw); diagnostic?.Checkpoint(label, raw); await raw; diagnostic?.Checkpoint(label + "-terminal", raw); }
            catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
        }
        if (surface is not null) { try { surface.RequestRetirement(); } catch (Exception cause) { errors.Add(cause); } await Join("surface-close", surface.CloseAndDrainAsync); }
        await Join("host-close", host.CloseAndDrainAsync);
        if (surface?.OriginalClose?.IsCompletedSuccessfully == true && host.OriginalClose?.IsCompletedSuccessfully == true) await Join("controller-close", controller.CloseAndDrainAsync);
        if (errors.Count == 0) window.Close();
        else
        {
            lock (FailedProtectedGeneratedUiOwners) FailedProtectedGeneratedUiOwners.Add([rig, graph, controller, host, window, surface!, closes, errors]);
            if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Actual protected presentation originals remain retained.", errors);
        }
    }
    // Opt-in fixture observation only. Original factories, assertions and joins stay intact.
    private sealed class GeneratedOriginalDiagnostic : IAsyncDisposable
    {
        private readonly object _gate = new();
        private readonly string _path;
        private readonly Timer _timer;
        private readonly HashSet<Task> _tasks = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Task> _important = new(ReferenceEqualityComparer.Instance);
        private CanonicalGeneratedUiInteractionOriginalOwner? _writer;
        private string _phase = "created";
        private int _snapshots;
        private const System.Reflection.BindingFlags Fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        private GeneratedOriginalDiagnostic(string path)
        { _path = path; _timer = new(_ => Snapshot(), null, 1000, 5000); }
        internal static GeneratedOriginalDiagnostic? TryCreate()
        {
            var path = Environment.GetEnvironmentVariable("ASTRA_GENUI_ORIGINAL_DIAGNOSTIC_DIR");
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = Path.GetFullPath(path);
            if (!path.StartsWith("/tmp/", StringComparison.Ordinal) || !Directory.Exists(path))
                throw new InvalidOperationException("The opt-in diagnostic requires its precreated tmp directory.");
            return new(Path.Combine(path, "genui-original-" + Guid.NewGuid().ToString("N") + ".jsonl"));
        }
        internal void Bind(CanonicalGeneratedUiInteractionOriginalOwner writer) { lock (_gate) _writer = writer; }
        internal void Track(Task task) { lock (_gate) if (_tasks.Count < 128) _tasks.Add(task); }
        internal void Checkpoint(string phase, Task? task = null)
        { lock (_gate) { _phase = phase; if (task is not null && _important.Count < 32) _important.Add(task); } Snapshot(); }
        private static object? Field(object value, string name) => value.GetType().GetField(name, Fields)?.GetValue(value);
        private static bool Enter(object value, string field, Action body)
        {
            var gate = Field(value, field);
            if (gate is null || !Monitor.TryEnter(gate)) return false;
            try { body(); return true; } finally { Monitor.Exit(gate); }
        }
        private void Snapshot(bool final = false)
        {
            if (!Monitor.TryEnter(_gate)) return;
            try
            {
                if (_snapshots >= (final ? 16 : 15)) return;
                var rows = new List<object>(); var seen = new HashSet<Task>(ReferenceEqualityComparer.Instance); var skipped = new List<string>();
                var sourceLists = new List<object>(); var rowBytes = 0; var omittedRows = 0; var unscannedRawEntries = 0;
                void TaskRow(string edge, object? value)
                {
                    if (value is not Task task || !seen.Add(task)) return;
                    var type = task.GetType().FullName ?? string.Empty;
                    if (type.Length > 512) type = type[..512];
                    var row = new { edge, id = task.Id, type, status = task.Status.ToString() };
                    var bytes = System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(row)) + 1;
                    if (rows.Count >= 128 || bytes > 14000 - rowBytes) { omittedRows++; return; }
                    rows.Add(row); rowBytes += bytes;
                }
                void Tasks(object value, string gate, string field, string label)
                {
                    if (!Enter(value, gate, () =>
                    {
                        if (Field(value, field) is List<Task> actual)
                        {
                            unscannedRawEntries += Math.Max(0, actual.Count - 128);
                            for (var i = 0; i < Math.Min(actual.Count, 128); i++) TaskRow(label + "[" + i + "]", actual[i]);
                        }
                    })) skipped.Add(label + ":busy-or-unavailable");
                }
                foreach (var task in _important) TaskRow("checkpoint", task);
                if (_writer is { } writer && !Enter(writer, "_gate", () =>
                {
                    if (Field(writer, "_acceptedCommits") is not System.Collections.IList commits) return;
                    for (var i = 0; i < Math.Min(commits.Count, 8); i++)
                    {
                        var commit = commits[i]; if (commit is null) continue;
                        foreach (var name in new[] { "Driver", "Inner", "Atomic", "Release", "SourceClose", "NativeClose" }) TaskRow("Commit." + name, Field(commit, name));
                        if (Field(commit, "Source") is { } source) sourceLists.Add(source);
                        if (Field(commit, "Claim") is { } claim && !Enter(claim, "_gate", () =>
                        {
                            foreach (var name in new[] { "Acquisition", "_entry", "_sql", "_settlement", "_close" }) TaskRow("Claim." + name, Field(claim, name));
                            if (Field(claim, "_originalSettlementContext") is { } context && Field(context, "Errors") is CloudflareOriginalTaskLedger ledger)
                                sourceLists.Add(ledger);
                        })) skipped.Add("Claim:busy-or-unavailable");
                    }
                })) skipped.Add("Writer:busy");
                foreach (var source in sourceLists)
                    if (source is CloudflareOriginalTaskLedger) Tasks(source, "_sync", "_tasks", "Claim.settlement.raw");
                    else Tasks(source, "_gate", "_raw", "Commit.Source.raw");
                foreach (var task in _tasks) TaskRow("fixture-retained", task);
                var utc = DateTimeOffset.UtcNow; var snapshot = ++_snapshots;
                string Serialize() => JsonSerializer.Serialize(new { utc, phase = _phase, snapshot, final, tasks = rows, skipped, omittedRows, unscannedRawEntries });
                var text = Serialize();
                while (System.Text.Encoding.UTF8.GetByteCount(text) + 1 > 16384 && rows.Count > 0)
                { rows.RemoveAt(rows.Count - 1); omittedRows++; text = Serialize(); }
                File.AppendAllText(_path, text + "\n");
                if (_snapshots >= 15 && !final) _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            catch (Exception) { /* A diagnostic failure cannot replace any original failure or join. */ }
            finally { Monitor.Exit(_gate); }
        }
        public async ValueTask DisposeAsync()
        { await _timer.DisposeAsync(); Snapshot(final: true); }
    }

}
#endif
