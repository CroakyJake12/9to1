using System.Net;
using System.Threading.Channels;
using System.Text.Json.Nodes;
using Haven.Desktop.Views.Pages.Home;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Desktop.Services;
using HomeDiscoverPage = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;
using HavenOS.Home.Discover;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class HomeDulcheIntegrationTests
{
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
                    await io.WriteAsync(Encoding.ASCII.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\" beta\"}}]}\n\ndata: [DONE]\n\n"), timeout.Token);
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
            Button ButtonByLabel(string name) => page.GetVisualDescendants().OfType<Button>().Single(b => b.Content?.ToString() == name);
            T InputByLabel<T>(string name) where T : Control => page.GetVisualDescendants().OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
            void Click(string name) => ButtonByLabel(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
