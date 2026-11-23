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
