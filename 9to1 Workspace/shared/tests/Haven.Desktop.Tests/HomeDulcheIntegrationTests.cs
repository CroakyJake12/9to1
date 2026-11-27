using System.Net;
using System.Threading.Channels;
using System.Text.Json.Nodes;
using Haven.Desktop.Views.Pages.Home;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HomeDiscoverPage = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;
using HavenOS.Home.Discover;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class HomeDulcheIntegrationTests
{
    [AvaloniaFact]
    public async Task UnloadedModelStartsOnSendAndPreservesANewerComposerDraft()
    {
        var root = Directory.CreateTempSubdirectory("dulche-cold-send-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var handler = new InferenceManagementHandler();
        var client = new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler);
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null, client);
        var window = new Window { Width = 1100, Height = 850, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            T Named<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
            Button ByText(string label) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label);
            while (!ByText("Send").IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.Equal(0, handler.CompletionAttempts);
            var prompt = Named<TextBox>("Dulche prompt");
            prompt.Text = "First request";
            ByText("Send").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var submitted = await handler.Completion.Task.WaitAsync(timeout.Token);
            Assert.Equal("one", submitted["model"]!.ToString());
            Assert.Equal("First request", submitted["messages"]![0]!["content"]!.ToString());
            Assert.Equal("First request", prompt.Text);
            prompt.Text = "Keep this next request";
            handler.ReleaseCompletion.TrySetResult();
            while (Named<TextBlock>("Dulche response").Text != "Completed answer") await Task.Delay(10, timeout.Token);
            while (!ByText("Send").IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.Equal("Keep this next request", prompt.Text);
            Assert.Equal(1, handler.CompletionAttempts);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally
        {
            handler.ReleaseCompletion.TrySetResult();
            await page.CloseAndDrainAsync();
            window.Close();
            root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task FailedColdSendKeepsTheOriginalPromptAndUnavailableControlsDisabled()
    {
        var root = Directory.CreateTempSubdirectory("dulche-cold-failure-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var handler = new InferenceManagementHandler { FailCompletion = true, ManagementSupported = false };
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1100, Height = 850, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            T Named<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
            Button ByText(string label) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label);
            while (!ByText("Send").IsEnabled) await Task.Delay(10, timeout.Token);
            Named<TextBox>("Dulche prompt").Text = "Retain the failed request";
            ByText("Send").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await handler.Completion.Task.WaitAsync(timeout.Token);
            handler.ReleaseCompletion.TrySetResult();
            while (!ByText("Send").IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.Equal("Retain the failed request", Named<TextBox>("Dulche prompt").Text);
            ByText("Performance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False((await WaitForButtonAsync(page, "Optimise inference", timeout.Token)).IsEnabled);
            Assert.Contains("unavailable", (await WaitForNamedAsync<TextBlock>(page, "Inference management availability", timeout.Token)).Text);
            ByText("Adapters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False((await WaitForButtonAsync(page, "Attach adapter", timeout.Token)).IsEnabled);
            Assert.Equal(1, handler.CompletionAttempts);
        }
        finally
        {
            handler.ReleaseCompletion.TrySetResult();
            await page.CloseAndDrainAsync();
            window.Close();
            root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task InferenceControlsPreserveCurrentContextAndObserveAdapterAndRollbackState()
    {
        var root = Directory.CreateTempSubdirectory("dulche-inference-controls-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var handler = new InferenceManagementHandler();
        var specialists = new SpecialistCatalog();
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler), specialists);
        var window = new Window { Width = 1200, Height = 900, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            T Named<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
            Button ByText(string label) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label);
            void Click(string label) => ByText(label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (!ByText("Send").IsEnabled) await Task.Delay(10, timeout.Token);
            Click("Performance");
            var profiles = await WaitForNamedAsync<ComboBox>(page, "Inference profiles", timeout.Token);
            while (profiles.ItemCount != 1) await Task.Delay(10, timeout.Token);
            (await WaitForNamedAsync<TextBox>(page, "Profile context length", timeout.Token)).Text = "16384";
            Click("Performance");
            var optimiseButton = await WaitForButtonAsync(page, "Optimise inference", timeout.Token);
            while (!optimiseButton.IsEnabled) await Task.Delay(10, timeout.Token);
            Click("Optimise inference");
            var optimise = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/optimise", optimise.Path);
            Assert.Equal("start", optimise.Body["action"]!.ToString());
            Assert.Equal(524288, optimise.Body["context_tokens"]!.GetValue<int>());
            while (!ByText("Restore previous settings").IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.Contains("completed", Named<TextBlock>("Inference optimisation status").Text);
            Click("Restore previous settings");
            var restore = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("restore", restore.Body["action"]!.ToString());
            Assert.Equal("op-one", restore.Body["operation_id"]!.ToString());
            Click("Adapters");
            var attachButton = await WaitForButtonAsync(page, "Attach adapter", timeout.Token);
            while (!attachButton.IsEnabled) await Task.Delay(10, timeout.Token);
            Named<TextBox>("LoRA adapter path").Text = "/fixtures/adapter.gguf";
            Named<TextBox>("LoRA adapter scale").Text = "0.75";
            Named<ComboBox>("LoRA adapter binding scope").SelectedItem = "Specialist";
            var specialist = await WaitForNamedAsync<ComboBox>(page, "Choose a Specialist", timeout.Token);
            while (specialist.ItemCount != 2) await Task.Delay(10, timeout.Token);
            specialist.SelectedIndex = 1;
            Assert.Equal("Review specialist", specialist.SelectedItem!.ToString());
            Click("Attach adapter");
            var attached = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/adapters", attached.Path);
            Assert.Equal("/fixtures/adapter.gguf", attached.Body["path"]!.ToString());
            Assert.Equal(0.75, attached.Body["scale"]!.GetValue<double>());
            Assert.Equal(SpecialistCatalog.ReviewerId.ToString("D"), attached.Body["specialist_id"]!.ToString());
            while (!ByText("Detach selected adapter").IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.Contains("selected", Named<TextBlock>("LoRA adapter runtime status").Text);
            Assert.DoesNotContain("Active in inference", Named<TextBlock>("LoRA adapter runtime status").Text);
            Click("Detach selected adapter");
            var detached = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/adapters/detach", detached.Path);
            Assert.Equal("binding-one", detached.Body["binding_id"]!.ToString());
        }
        finally
        {
            await page.CloseAndDrainAsync();
            window.Close();
            root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task RemovedSpecialistRequiresANewExplicitSelectionEvenWhenAnotherRemains()
    {
        var root = Directory.CreateTempSubdirectory("dulche-specialist-selection-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var handler = new InferenceManagementHandler();
        var specialists = new SpecialistCatalog();
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler), specialists);
        var window = new Window { Width = 1000, Height = 650, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            Button FindButton(string label) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label);
            void Click(string label) => FindButton(label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (!FindButton("Send").IsEnabled) await Task.Delay(10, timeout.Token);
            Click("Adapters");
            (await WaitForNamedAsync<ComboBox>(page, "LoRA adapter binding scope", timeout.Token)).SelectedItem = "Specialist";
            var picker = await WaitForNamedAsync<ComboBox>(page, "Choose a Specialist", timeout.Token);
            while (picker.ItemCount != 2) await Task.Delay(10, timeout.Token);
            Assert.Null(picker.SelectedItem);
            Assert.False(FindButton("Attach adapter").IsEnabled);
            picker.SelectedIndex = 1;
            Assert.Equal("Review specialist", picker.SelectedItem!.ToString());
            Assert.True(FindButton("Attach adapter").IsEnabled);
            specialists.RemovedIds.Add(SpecialistCatalog.ReviewerId);
            Click("Chat");
            await WaitForNamedAsync<TextBox>(page, "Dulche prompt", timeout.Token);
            Click("Adapters");
            picker = await WaitForNamedAsync<ComboBox>(page, "Choose a Specialist", timeout.Token);
            while (picker.ItemCount != 1) await Task.Delay(10, timeout.Token);
            Assert.Null(picker.SelectedItem);
            Assert.False(FindButton("Attach adapter").IsEnabled);
            var previousReads = specialists.Reads;
            Click("Chat");
            await WaitForNamedAsync<TextBox>(page, "Dulche prompt", timeout.Token);
            Click("Adapters");
            picker = await WaitForNamedAsync<ComboBox>(page, "Choose a Specialist", timeout.Token);
            while (specialists.Reads == previousReads) await Task.Delay(10, timeout.Token);
            Assert.Null(picker.SelectedItem);
            Assert.False(FindButton("Attach adapter").IsEnabled);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task FreshPanelRediscoversApplicableRollbackWithoutStartingAnotherOptimisation()
    {
        var root = Directory.CreateTempSubdirectory("dulche-rollback-discovery-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var handler = new InferenceManagementHandler { DiscoverSavedOperation = true };
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 650, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            Button FindButton(string label) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label);
            FindButton("Performance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var restoreButton = await WaitForButtonAsync(page, "Restore previous settings", timeout.Token);
            while (!restoreButton.IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.False(handler.Commands.Reader.TryRead(out _));
            FindButton("Restore previous settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var restored = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/optimise", restored.Path);
            Assert.Equal("restore", restored.Body["action"]!.ToString());
            Assert.Equal("saved-operation", restored.Body["operation_id"]!.ToString());
            // The command channel reports request admission before the original
            // response and its final capability refresh have settled.
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.True(FindButton("Optimise inference").IsEnabled);
            Assert.False(FindButton("Restore previous settings").IsEnabled);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task HistoricalOperationCannotRestoreWhenItsOverlayIsNoLongerApplicable()
    {
        var root = Directory.CreateTempSubdirectory("dulche-stale-rollback-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var handler = new InferenceManagementHandler { DiscoverSavedOperation = true, SavedRollbackApplicable = false };
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 650, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            Button FindButton(string label) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label);
            FindButton("Performance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var optimiseButton = await WaitForButtonAsync(page, "Optimise inference", timeout.Token);
            while (!optimiseButton.IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.False(FindButton("Restore previous settings").IsEnabled);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task InstalledModelChatFitsTheHomeViewportAndSectionChangesPreserveDraft()
    {
        var root = Directory.CreateTempSubdirectory("dulche-chat-layout-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var handler = new InferenceManagementHandler { ModelName = "Qwen3.8 Flash Next ALORA multimodal · 524,288 context · dual GPU" };
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 650, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            T Named<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
            Button FindButton(string label) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == label);
            while (!FindButton("Send").IsEnabled) await Task.Delay(10, timeout.Token);
            var prompt = Named<TextBox>("Dulche prompt");
            prompt.Text = "Keep this draft while I inspect performance and adapters.";
            foreach (var size in new[] { new Size(1000, 650), new Size(720, 480) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                window.UpdateLayout();
                foreach (var control in new Control[] { Named<ComboBox>("Installed inference model"), prompt, FindButton("Send") })
                {
                    Assert.True(control.IsEffectivelyVisible);
                    var origin = control.TranslatePoint(default(Point), window);
                    Assert.NotNull(origin);
                    Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
                    Assert.InRange(origin!.Value.X, -1, window.ClientSize.Width);
                    Assert.InRange(origin.Value.Y, -1, window.ClientSize.Height);
                    Assert.True(origin.Value.X + control.Bounds.Width <= window.ClientSize.Width + 1,
                        $"{AutomationProperties.GetName(control) ?? control.ToString()} extends beyond the horizontal viewport at {size}.");
                    Assert.True(origin.Value.Y + control.Bounds.Height <= window.ClientSize.Height + 1,
                        $"{AutomationProperties.GetName(control) ?? control.ToString()} is below the viewport at {size}.");
                }
                foreach (var scroll in page.GetVisualDescendants().OfType<ScrollViewer>().Where(s => s.IsEffectivelyVisible))
                    Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1, $"Horizontal overflow in Home at {size}.");
            }
            AssertNoVisibleNamed<TextBox>(page, "GPU, context and inference profile settings");
            AssertNoVisibleNamed<TextBox>(page, "Local model path");
            FindButton("Performance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForButtonAsync(page, "Optimise inference", timeout.Token);
            AssertNoVisibleNamed<TextBox>(page, "Dulche prompt");
            FindButton("Adapters").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForNamedAsync<TextBox>(page, "LoRA adapter path", timeout.Token);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), b => b.Content?.ToString() == "Optimise inference" && b.IsEffectivelyVisible);
            FindButton("Chat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            prompt = await WaitForNamedAsync<TextBox>(page, "Dulche prompt", timeout.Token);
            Assert.Equal("Keep this draft while I inspect performance and adapters.", prompt.Text);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task CancelResponseStopsOnlyItsOwnedRunAndRetainsPartialOutputAndNewDraft()
    {
        var root = Directory.CreateTempSubdirectory("dulche-cancel-response-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var handler = new DirectChatHandler { HoldStopSettlement = true };
        handler.Stream.Push("data: {\"choices\":[{\"delta\":{\"content\":\"Partial answer\"}}]}\n\n");
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 650, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            T Named<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
            var send = page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Send");
            while (!send.IsEnabled) await Task.Delay(10, timeout.Token);
            var prompt = Named<TextBox>("Dulche prompt");
            prompt.Text = "Initial request";
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while (Named<TextBlock>("Dulche response").Text != "Partial answer") await Task.Delay(10, timeout.Token);
            prompt.Text = "A newer request";
            Named<Button>("Cancel this Dulche response").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("/v1/agents/direct-current/stop", await handler.Stops.Reader.ReadAsync(timeout.Token));
            Assert.False(send.IsEnabled);
            Assert.Contains("Waiting", Named<TextBlock>("Dulche run status").Text);
            handler.ReleaseStop.TrySetResult();
            while (!send.IsEnabled) await Task.Delay(10, timeout.Token);
            Assert.Contains("Cancelled", Named<TextBlock>("Dulche run status").Text);
            Assert.Equal("Partial answer", Named<TextBlock>("Dulche response").Text);
            Assert.Equal("A newer request", prompt.Text);
            Assert.False(handler.Stops.Reader.TryRead(out _));
        }
        finally { handler.ReleaseStop.TrySetResult(); await page.CloseAndDrainAsync(); window.Close(); root.Delete(true); }
    }

    private sealed class InferenceManagementHandler : HttpMessageHandler
    {
        public bool FailCompletion { get; init; }
        public bool ManagementSupported { get; init; } = true;
        public bool DiscoverSavedOperation { get; init; }
        public bool SavedRollbackApplicable { get; init; } = true;
        public bool ReplaceModelOnPoll { get; set; }
        public string ModelName { get; init; } = "Model One";
        public int StatusReads { get; private set; }
        public int CompletionAttempts { get; private set; }
        public TaskCompletionSource<JsonNode> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Channel<(string Path, JsonNode Body)> Commands { get; } = Channel.CreateUnbounded<(string, JsonNode)>();
        private bool _attached;
        private bool _restored;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/chat/completions")
            {
                CompletionAttempts++;
                Completion.TrySetResult(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!);
                await ReleaseCompletion.Task.WaitAsync(token);
                return FailCompletion
                    ? new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"error\":\"Model startup failed\"}", Encoding.UTF8, "application/json") }
                    : new(HttpStatusCode.OK) { Content = new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"Completed answer\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream") };
            }
            string data;
            if (path == "/v1/status")
            {
                StatusReads++;
                data = JsonSerializer.Serialize(new { phase = "ready", stop_all = false,
                    models = new[] { new { id = ReplaceModelOnPoll ? "two" : "one", name = ReplaceModelOnPoll ? "Different model" : ModelName, state = "unloaded", can_start = true, context = 524288 } },
                    agents = Array.Empty<object>(), resources = new { } });
            }
            else if (path == "/v1/inference/capabilities")
            {
                var caps = JsonSerializer.SerializeToNode(new { optimise = new { supported = ManagementSupported }, adapters = new { supported = ManagementSupported } })!;
                if (DiscoverSavedOperation)
                {
                    var saved = JsonNode.Parse("""{"id":"saved-operation","model":"one","state":"completed","rollback_available":true,"context_tokens":524288}""")!;
                    caps["models"] = new JsonArray(new JsonObject { ["id"] = "one", ["optimise"] = caps["optimise"]!.DeepClone(), ["adapters"] = caps["adapters"]!.DeepClone(), ["latest_operation"] = saved,
                        ["rollback_operation"] = SavedRollbackApplicable && !_restored ? saved.DeepClone() : null });
                    caps["active_operation"] = null;
                }
                data = caps.ToJsonString();
            }
            else if (path == "/v1/models/one/optimise")
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
                await Commands.Writer.WriteAsync((path, body), token);
                _restored = body["action"]!.ToString() == "restore";
                data = body["action"]!.ToString() == "start"
                    ? """{"id":"op-one","state":"queued","context_tokens":524288}"""
                    : """{"id":"restore-one","state":"completed","message":"Previous settings restored"}""";
            }
            else if (path == "/v1/inference/operations/op-one")
                data = """{"id":"op-one","state":"completed","rollback_available":true,"context_tokens":524288}""";
            else if (path.StartsWith("/v1/models/one/adapters", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Post)
                {
                    var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
                    _attached = !path.EndsWith("/detach", StringComparison.Ordinal);
                    await Commands.Writer.WriteAsync((path, body), token);
                }
                data = _attached
                    ? """{"items":[{"binding_id":"binding-one","path":"/fixtures/adapter.gguf","scale":0.75,"specialist_id":"specialist:fixture","state":"selected","active":false}]}"""
                    : """{"items":[]}""";
            }
            else if (path.EndsWith("/profiles", StringComparison.Ordinal))
                data = """{"items":[{"id":"current","name":"Current","settings":{"context":524288}}]}""";
            else data = "{}";
            return new(HttpStatusCode.OK) { Content = new StringContent(data, Encoding.UTF8, "application/json") };
        }
    }

    [AvaloniaFact]
    public async Task HomeModelsSelectsInstalledModelAndStreamsFromItsIndependentUnixSocket()
    {
        if (!OperatingSystem.IsLinux())
            return;
        var root = Directory.CreateTempSubdirectory("dulche-ui-");
        var socketPath = Path.Combine(root.FullName, "service.sock");
        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(socketPath));
        server.Listen(8);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var appended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = Task.Run(async () =>
        {
            while (!timeout.IsCancellationRequested)
            {
                using var peer = await server.AcceptAsync(timeout.Token);
                using var io = new NetworkStream(peer);
                using var reader = new StreamReader(io, Encoding.ASCII, false, leaveOpen: true);
                var request = (await reader.ReadLineAsync(timeout.Token))!;
                var size = 0;
                var chunked = false;
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } header)
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        size = int.Parse(header[15..]);
                    if (header.Contains("chunked", StringComparison.OrdinalIgnoreCase))
                        chunked = true;
                }
                var raw = new char[size];
                if (size > 0)
                    await reader.ReadBlockAsync(raw, timeout.Token);
                var text = new string(raw);
                if (chunked)
                {
                    var body = new StringBuilder();
                    while (true)
                    {
                        var length = Convert.ToInt32(await reader.ReadLineAsync(timeout.Token), 16);
                        if (length == 0)
                        {
                            await reader.ReadLineAsync(timeout.Token);
                            break;
                        }
                        var chunk = new char[length];
                        await reader.ReadBlockAsync(chunk, timeout.Token);
                        body.Append(chunk);
                        await reader.ReadLineAsync(timeout.Token);
                    }
                    text = body.ToString();
                }
                if (request.Contains("/chat/completions"))
                {
                    posted.SetResult(text);
                    await io.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\nX-Dulche-Run-Id: ui-fixture\r\n\r\ndata: {\"choices\":[{\"delta\":{\"content\":\"alpha\"}}]}\n\n"), timeout.Token);
                    appended.TrySetResult();
                    await release.Task.WaitAsync(timeout.Token);
                    await io.WriteAsync(Encoding.ASCII.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\" beta\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"), timeout.Token);
                    continue;
                }
                var answer = request.Contains("/status")
                    ? """{"models":[{"id":"one","name":"Model One","state":"loaded"},{"id":"two","name":"Model Two","state":"loaded"}],"agents":[],"resources":{}}""" : "{}";
                if (request.Contains("frontend/connect"))
                    connected.TrySetResult();
                await io.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {answer.Length}\r\nConnection: close\r\n\r\n{answer}"), timeout.Token);
            }
        });
        Window? window = null;
        HomeDiscoverPage? page = null;
        try
        {
            var preferences = new UserPreferencesService(new Paths(root.FullName));
            var client = new DulcheLocalClient(preferences, socketPath);
            page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null, client);
            window = new Window { Width = 1000, Height = 850, Content = page };
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            await connected.Task.WaitAsync(timeout.Token);
            T Named<T>(string accessible) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == accessible);
            var pick = Named<ComboBox>("Installed inference model");
            while (pick.ItemCount < 2)
                await Task.Delay(20, timeout.Token);
            pick.SelectedIndex = 1;
            Named<TextBox>("Dulche prompt").Text = "Summarize a test.";
            var button = page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == "Send");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var submitted = JsonDocument.Parse(await posted.Task.WaitAsync(timeout.Token));
            Assert.Equal("two", submitted.RootElement.GetProperty("model").GetString());
            Assert.Equal("Summarize a test.", submitted.RootElement.GetProperty("messages")[0].GetProperty("content").GetString());
            Assert.True(submitted.RootElement.GetProperty("stream").GetBoolean());
            await appended.Task.WaitAsync(timeout.Token);
            var output = Named<TextBlock>("Dulche response");
            while (output.Text != "alpha")
                await Task.Delay(20, timeout.Token);
            release.TrySetResult();
            while (output.Text != "alpha beta")
                await Task.Delay(20, timeout.Token);
        }
        finally
        {
            release.TrySetResult();
            if (page is not null)
                await page.CloseAndDrainAsync();
            window?.Close();
            timeout.Cancel();
            try
            {
                await serving;
            }
            catch (OperationCanceledException) { }
            root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task SwarmUsesChosenWorkerContextAndCommonWindowThroughProductionControls()
    {
        var root = Directory.CreateTempSubdirectory("dulche-swarm-cui-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var handler = new SwarmHandler();
        var client = new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler);
        var page = new HomeDiscoverPage(new HomeDiscoverCatalog([]), null, null, client);
        var window = new Window { Width = 1200, Height = 820, Content = page };
        try
        {
            window.Show();
            await page.ActivateAsync(timeout.Token);
            await OpenFirstModelAsync(page, timeout.Token);
            Button ButtonByLabel(string name) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == name);
            T InputByLabel<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
            void Click(string name) => ButtonByLabel(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Click("Advanced");
            await WaitForButtonAsync(page, "Swarm Mode", timeout.Token);
            Click("Swarm Mode");
            while (!page.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() == "Configure"))
                await Task.Delay(15, timeout.Token);
            Click("Configure");
            while (!page.GetVisualDescendants().OfType<ComboBox>().Any(c => AutomationProperties.GetName(c) == "Worker model and device"))
                await Task.Delay(15, timeout.Token);
            var worker = InputByLabel<ComboBox>("Worker model and device");
            while (worker.ItemCount != 2)
                await Task.Delay(15, timeout.Token);
            worker.SelectedIndex = 1;
            var context = InputByLabel<TextBox>("Worker context (tokens)");
            while (!context.IsEnabled || InputByLabel<TextBlock>("Worker budget").Text?.Contains("V100") != true)
                await Task.Delay(15, timeout.Token);
            Assert.Equal("32768", context.Text);
            context.Text = "8192";
            Click("Add worker role");
            while (InputByLabel<TextBlock>("Status").Text?.Contains("16,384") != true)
                await Task.Delay(15, timeout.Token);
            Assert.Equal(0, InputByLabel<ComboBox>("Configured worker roles").ItemCount);
            context.Text = "24576";
            InputByLabel<TextBox>("Output per request (tokens)").Text = "1536";
            while (InputByLabel<TextBlock>("Worker budget").Text?.Contains((24576 - 1536).ToString("N0")) != true)
                await Task.Delay(15, timeout.Token);
            var reasoning = InputByLabel<ComboBox>("Worker reasoning");
            while (reasoning.ItemCount < 2)
                await Task.Delay(15, timeout.Token);
            reasoning.SelectedIndex = 1;
            Click("Add worker role");
            InputByLabel<TextBox>("Objective").Text = "Review independent modules.";
            InputByLabel<TextBox>("Working directory").Text = root.FullName;
            InputByLabel<TextBox>("Completion criteria").Text = "Provide two verified findings.";
            Click("Start swarm");
            var payload = await handler.Created.Task.WaitAsync(timeout.Token);
            Assert.NotNull(handler.Idempotency);
            Assert.Equal("mentor", payload["mentor"]!["model"]!.ToString());
            Assert.Equal("worker", payload["workers"]![0]!["model"]!.ToString());
            Assert.Equal("V100", payload["workers"]![0]!["gpu"]!.ToString());
            Assert.Equal(24576, payload["workers"]![0]!["context"]!.GetValue<int>());
            Assert.Equal(1536, payload["limits"]!["max_output_tokens"]!.GetValue<int>());
            Assert.Empty(payload["workers"]![0]!["recipes"]!.AsArray());
            Assert.Equal("disabled", payload["workers"]![0]!["reasoning"]!.ToString());
            Assert.Null(payload["mentor"]!["reasoning"]);
            var rates = InputByLabel<TextBlock>("Rates");
            while (rates.Text?.Contains("25.0") != true)
                await Task.Delay(15, timeout.Token);
            Assert.Contains("10.0 tok/s", rates.Text);
            Assert.Contains("15.0 tok/s", rates.Text);
            Assert.Contains("30.0 s", rates.Text);
            InputByLabel<TextBox>("Optional escalation for mentor").Text = "Two source descriptions conflict.";
            Click("Request mentor check-in");
            var review = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/swarms/run/check-in", review.Path);
            Assert.Equal("Two source descriptions conflict.", review.Body["note"]!.ToString());
            Click("Stop selected worker");
            Assert.Equal("/v1/swarms/run/tasks/t1/stop", (await handler.Commands.Reader.ReadAsync(timeout.Token)).Path);
            Click("Pause");
            Assert.Equal("/v1/swarms/run/pause", (await handler.Commands.Reader.ReadAsync(timeout.Token)).Path);
            while (!ButtonByLabel("Resume").IsEnabled)
                await Task.Delay(15, timeout.Token);
            Click("Resume");
            Assert.Equal("/v1/swarms/run/resume", (await handler.Commands.Reader.ReadAsync(timeout.Token)).Path);
            Click("Stop swarm");
            Assert.Equal("/v1/swarms/run/stop", (await handler.Commands.Reader.ReadAsync(timeout.Token)).Path);
        }
        finally
        {
            await page.CloseAndDrainAsync();
            window.Close();
            root.Delete(true);
        }
    }

    private sealed class SwarmHandler : HttpMessageHandler
    {
        public TaskCompletionSource<JsonNode> Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Idempotency
        {
            get; private set;
        }
        public Channel<(string Path, JsonNode Body)> Commands { get; } = Channel.CreateUnbounded<(string, JsonNode)>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var route = request.RequestUri!.AbsolutePath;
            string data;
            if (route == "/v1/status")
                data = """{"models":[{"id":"mentor","name":"Mentor","state":"loaded","engine":"Strata","gpu":"RTX","context":65536,"slots":1},{"id":"worker","name":"Worker","state":"loaded","engine":"Lightning","gpu":"V100","context":32768,"slots":2}],"resources":{},"agents":[]}""";
            else if (route.EndsWith("/profiles", StringComparison.Ordinal))
                data = """{"items":[{"id":"current","name":"Verified","settings":{}}]}""";
            else if (route == "/v1/swarms" && request.Method == HttpMethod.Post)
            {
                Idempotency = request.Headers.GetValues("Idempotency-Key").Single();
                Created.TrySetResult(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!);
                data = Report;
            }
            else if (route == "/v1/swarms")
                data = Created.Task.IsCompleted ? """{"items":[{"id":"run","objective":"Objective","state":"running"}],"presets":{}}""" : """{"items":[],"presets":{},"reasoning_modes":{"worker":{"modes":["default","disabled"]}}}""";
            else if (route.StartsWith("/v1/swarms/run/", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!;
                await Commands.Writer.WriteAsync((route, body), token);
                var observed = JsonNode.Parse(Report)!;
                if (route.EndsWith("/pause", StringComparison.Ordinal))
                    observed["state"] = "paused";
                if (route == "/v1/swarms/run/stop")
                    observed["state"] = "stopped";
                data = observed.ToJsonString();
            }
            else if (route == "/v1/swarms/run")
                data = Report;
            else
                data = "{}";
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(data, Encoding.UTF8, "application/json")
            };
        }

        private const string Report = """
            {"id":"run","state":"running","stage":"workers","tasks":[{"id":"t1","role":"reviewer","state":"generating"}],"decisions":[],"resources":{},"config":{},
             "metrics":{"rolling":{"elapsed_s":30,"aggregate_tps":25,"workers":[{"worker":"a","role":"reviewer","tps":10},{"worker":"b","role":"builder","tps":15}]},
             "finished":{"elapsed_s":60,"aggregate_tps":20,"workers":[]}}}
            """;
    }

    [AvaloniaFact]
    public async Task AtlasSettingsSearchRoutesToLiveMaskedProviderControls()
    {
        var root=Directory.CreateTempSubdirectory("dulche-settings-");
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(9));
        using var theme=new CakeOS.Cui.Themes.CuiThemePreferenceContext(new ThemeSource());
        var handler=new ProviderHandler();
        using var shell=new HomeAtlasSettingsView(new Border(),new Border(),
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)),handler),
            ()=>throw new InvalidOperationException("Unexpected legacy destination."),
            (op,token)=>op(token),theme);
        var window=new Window {Width=1280,Height=900,Content=shell};window.Show();
        try
        {
            shell.Activate();
            T Named<T>(string label) where T:Control => shell.GetVisualDescendants().OfType<T>().Single(c=>AutomationProperties.GetName(c)==label);
            Button ByText(string name)=>shell.GetVisualDescendants().OfType<Button>().Single(b=>b.Content?.ToString()==name);
            var search=Named<TextBox>("Search settings");search.Text="Providers";
            while(!shell.GetVisualDescendants().OfType<Button>().Any(b=>AutomationProperties.GetName(b)=="Global › AI › Providers"))
                await Task.Delay(10,deadline.Token);
            Named<Button>("Global › AI › Providers").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while(!shell.GetVisualDescendants().OfType<ComboBox>().Any(c=>AutomationProperties.GetName(c)=="External inference model IDs"))
                await Task.Delay(10,deadline.Token);
            var ids=Named<ComboBox>("External inference model IDs");
            while(ids.ItemCount==0)await Task.Delay(10,deadline.Token);
            Assert.Equal("verified-id",ids.SelectedItem);
            ByText("Create credential").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var token=Named<TextBox>("One-time client credential");
            while(token.Text!="fixture-secret")await Task.Delay(10,deadline.Token);
            Assert.False(token.RevealPassword);Assert.NotEqual('\0',token.PasswordChar);
            ByText("Show").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            while(!token.RevealPassword)await Task.Delay(10,deadline.Token);
            shell.Deactivate();Assert.Equal("",token.Text);Assert.False(token.RevealPassword);
            Assert.Equal(1,handler.Issuances);
        }
        finally { window.Close();root.Delete(true); }
    }

    private sealed class ThemeSource : CakeOS.Cui.Themes.ICuiThemePreferenceSource
    {
        public CakeOS.Cui.Themes.CuiThemePreferenceSnapshot Current {get;} = new("Professional",1,"fixture",false,CakeOS.Cui.Themes.CuiAccessibilitySettings.Default);
        public event EventHandler? Changed {add{} remove{}}
    }
    private sealed class ProviderHandler : HttpMessageHandler
    {
        public int Issuances {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var route=request.RequestUri!.AbsolutePath;
            string data;
            if(route=="/v1/external/clients"&&request.Method==HttpMethod.Post)
            {Issuances++;data="""{"id":"test","name":"Test fixture","token":"fixture-secret"}""";}
            else data="""{"enabled":true,"listening":true,"base_url":"http://127.0.0.1:18740/v1","models":[{"id":"verified-id"}],"clients":[],"opencode_config":{"providers":{"dulche":{"settings":{"baseURL":"http://127.0.0.1:18740/v1"}}}}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(data,Encoding.UTF8,"application/json")});
        }
    }

    [Fact]
    public async Task DirectChatMissingAdmissionIsBoundedWithoutStoppingAnUnidentifiedRun()
    {
        Assert.Equal(TimeSpan.FromMinutes(8), DulcheLocalClient.DirectChatAdmissionTimeout);
        Assert.Equal(TimeSpan.FromMinutes(8), DulcheLocalClient.DirectChatFirstProgressTimeout);
        Assert.Equal(TimeSpan.FromMinutes(2), DulcheLocalClient.DirectChatIdleTimeout);
        var root = Directory.CreateTempSubdirectory("dulche-missing-admission-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new DirectChatHandler { WithholdHeaders = true };
        var client = new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler)
            { DirectAdmissionLimit = TimeSpan.FromMilliseconds(100) };
        try
        {
            var error = await Assert.ThrowsAsync<DulcheRunStopUnconfirmedException>(async () =>
            {
                await foreach (var _ in client.StreamAsync("one", [], _ => { }, timeout.Token)) { }
            });
            Assert.Null(error.RunId);
            Assert.Contains("timed out", error.Message);
            Assert.False(handler.Stops.Reader.TryRead(out _));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task RoleOnlyChunkDoesNotCountAsFirstModelProgress()
    {
        var root = Directory.CreateTempSubdirectory("dulche-role-only-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new DirectChatHandler();
        handler.Stream.Push("data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n");
        var client = new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler)
            { DirectFirstProgressLimit = TimeSpan.FromMilliseconds(100), DirectIdleLimit = TimeSpan.FromSeconds(3) };
        try
        {
            var error = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await foreach (var _ in client.StreamAsync("one", [], _ => { }, timeout.Token)) { }
            });
            Assert.Contains("startup limit", error.Message);
            Assert.Equal("/v1/agents/direct-current/stop", await handler.Stops.Reader.ReadAsync(timeout.Token));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task ReasoningProgressExtendsTheWaitBeforeFirstVisibleContent()
    {
        var root = Directory.CreateTempSubdirectory("dulche-reasoning-progress-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new DirectChatHandler();
        handler.Stream.Push("data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"Considering the request\"}}]}\n\n");
        var client = new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler)
            { DirectFirstProgressLimit = TimeSpan.FromMilliseconds(150), DirectIdleLimit = TimeSpan.FromSeconds(2) };
        try
        {
            var answer = new StringBuilder();
            async Task Read()
            {
                await foreach (var chunk in client.StreamAsync("one", [], _ => { }, timeout.Token)) answer.Append(chunk);
            }
            var reading = Read();
            await handler.Stream.FirstRead.Task.WaitAsync(timeout.Token);
            await Task.Delay(250, timeout.Token);
            Assert.False(reading.IsCompleted);
            handler.Stream.Push("data: {\"choices\":[{\"delta\":{\"content\":\"Final answer\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
            await reading;
            Assert.Equal("Final answer", answer.ToString());
            Assert.False(handler.Stops.Reader.TryRead(out _));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task DirectChatIdleTimeoutRetainsPartialOutputAndAwaitsExactStop()
    {
        var root = Directory.CreateTempSubdirectory("dulche-idle-response-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new DirectChatHandler { HoldStopSettlement = true };
        handler.Stream.Push("data: {\"choices\":[{\"delta\":{\"content\":\"Partial answer\"}}]}\n\n");
        var client = new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler)
            { DirectIdleLimit = TimeSpan.FromMilliseconds(100) };
        try
        {
            var answer = new StringBuilder();
            async Task Read()
            {
                await foreach (var chunk in client.StreamAsync("one", [], _ => { }, timeout.Token)) answer.Append(chunk);
            }
            var reading = Read();
            Assert.Equal("/v1/agents/direct-current/stop", await handler.Stops.Reader.ReadAsync(timeout.Token));
            Assert.False(reading.IsCompleted);
            handler.ReleaseStop.TrySetResult();
            var error = await Assert.ThrowsAsync<TimeoutException>(() => reading);
            Assert.Contains("stalled", error.Message);
            Assert.Equal("Partial answer", answer.ToString());
            Assert.False(handler.Stops.Reader.TryRead(out _));
        }
        finally { handler.ReleaseStop.TrySetResult(); root.Delete(true); }
    }

    [Fact]
    public async Task UnsettledOwnedStopRemainsAnErrorInsteadOfConfirmedCancellation()
    {
        var root = Directory.CreateTempSubdirectory("dulche-unsettled-stop-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var handler = new DirectChatHandler { StopSettled = false };
        handler.Stream.Push("data: {\"choices\":[{\"delta\":{\"content\":\"Partial\"}}]}\n\n");
        var client = new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler)
            { DirectIdleLimit = TimeSpan.FromMilliseconds(100) };
        try
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(async () =>
            {
                await foreach (var _ in client.StreamAsync("one", [], _ => { }, timeout.Token)) { }
            });
            Assert.Contains(failure.Flatten().InnerExceptions, e => e is DulcheRunStopUnconfirmedException { RunId: "direct-current" });
            Assert.Equal("/v1/agents/direct-current/stop", await handler.Stops.Reader.ReadAsync(timeout.Token));
        }
        finally { root.Delete(true); }
    }

    private sealed class DirectChatHandler : HttpMessageHandler
    {
        public bool WithholdHeaders { get; init; }
        public string? SavedRunModel { get; init; }
        public int SavedRunReads { get; private set; }
        public bool HoldStopSettlement { get; init; }
        public bool StopSettled { get; init; } = true;
        public ChannelBytesStream Stream { get; } = new();
        public Channel<string> Stops { get; } = Channel.CreateUnbounded<string>();
        public TaskCompletionSource ReleaseStop { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/chat/completions")
            {
                if (WithholdHeaders) await Task.Delay(Timeout.Infinite, token);
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(Stream) };
                response.Headers.Add("X-Dulche-Run-Id", "direct-current");
                return response;
            }
            string json = "{}";
            if (path.StartsWith("/v1/agents/", StringComparison.Ordinal) && path.EndsWith("/stop", StringComparison.Ordinal))
            {
                await Stops.Writer.WriteAsync(path, token);
                if (HoldStopSettlement) await ReleaseStop.Task.WaitAsync(token);
                json = JsonSerializer.Serialize(new { id = "direct-current", state = "stopped", settled = StopSettled });
            }
            else if (path == "/v1/status")
                json = """{"phase":"ready","models":[{"id":"one","name":"Model One","state":"loaded","context":524288}],"agents":[{"id":"unrelated-agent","state":"running"}],"resources":{}}""";
            else if (request.Method == HttpMethod.Get && path == "/v1/agents/unrelated-agent")
            {
                SavedRunReads++;
                json = JsonSerializer.Serialize(new { id = "unrelated-agent", model = SavedRunModel ?? "one", state = "completed",
                    completed_steps = new[] { new { choices = new[] { new { message = new { content = "Saved model answer" } } } } },
                    request = new { steps = Array.Empty<object>() } });
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ChannelBytesStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        private byte[]? _chunk;
        private int _position;
        public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Push(string text) => _chunks.Writer.TryWrite(Encoding.UTF8.GetBytes(text));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            if (_chunk is null || _position == _chunk.Length)
            {
                if (!await _chunks.Reader.WaitToReadAsync(token)) return 0;
                _chunk = await _chunks.Reader.ReadAsync(token);
                _position = 0;
            }
            var count = Math.Min(buffer.Length, _chunk.Length - _position);
            _chunk.AsMemory(_position, count).CopyTo(buffer);
            _position += count;
            FirstRead.TrySetResult();
            return count;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => ReadAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { _chunks.Writer.TryComplete(); base.Dispose(disposing); }
    }

    private static async Task<T> WaitForNamedAsync<T>(Control page, string name, CancellationToken token) where T : Control
    {
        while (true)
        {
            var control = page.GetVisualDescendants().OfType<T>().SingleOrDefault(c =>
                c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);
            if (control is not null) return control;
            await Task.Delay(10, token);
        }
    }

    private static async Task<Button> WaitForButtonAsync(Control page, string label, CancellationToken token)
    {
        while (true)
        {
            var button = page.GetVisualDescendants().OfType<Button>().SingleOrDefault(b =>
                b.IsEffectivelyVisible && b.Content?.ToString() == label);
            if (button is not null) return button;
            await Task.Delay(10, token);
        }
    }

    private static void AssertNoVisibleNamed<T>(Control page, string name) where T : Control =>
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<T>(), c =>
            c.IsEffectivelyVisible && AutomationProperties.GetName(c) == name);

    private sealed class SpecialistCatalog : ICatalogRepository
    {
        internal static readonly Guid ReviewerId = Guid.Parse("df5fb502-9d48-4bd4-9e11-55c6f7b99d60");
        public HashSet<Guid> RemovedIds { get; } = [];
        public int Reads { get; private set; }
        private readonly AgentDefinition[] _definitions = [
            new(Guid.Parse("46765915-fd77-4455-bd6b-3e219cf6dc78"), "Coding specialist", "", "", "agent", "one", null, "", "{}", false, true, DateTimeOffset.UtcNow),
            new(ReviewerId, "Review specialist", "", "", "agent", "one", null, "", "{}", false, true, DateTimeOffset.UtcNow),
            new(Guid.Parse("1b9968cc-44d4-48a8-86c6-e93b1c048bac"), "Disabled specialist", "", "", "agent", "one", null, "", "{}", false, false, DateTimeOffset.UtcNow)
        ];
        public Task<IReadOnlyList<AgentDefinition>> GetAgentsAsync(CancellationToken token)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<AgentDefinition>>(_definitions.Where(item => !RemovedIds.Contains(item.Id)).ToArray());
        }
        public Task<IReadOnlyList<PromptDefinition>> GetPromptsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<PromptDefinition>>([]);
        public Task UpsertAgentAsync(AgentDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertPromptAsync(PromptDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task SetAgentEnabledAsync(Guid id, bool enabled, CancellationToken token) => throw new NotSupportedException();
        public Task SetPromptEnabledAsync(Guid id, bool enabled, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteCustomAgentAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteCustomPromptAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }

    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "store.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
