using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Services;
using HavenOS.Home.Discover;
using Xunit;
using DiscoverView = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;

namespace Haven.Desktop.Tests;

public sealed partial class HomeDulcheIntegrationTests
{
    [AvaloniaFact]
    public async Task Model_profile_drafts_follow_exact_model_identity_without_name_or_settings_leakage()
    {
        var root = Directory.CreateTempSubdirectory("dulche-model-drafts-");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var handler = new ProfileDraftHandler();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1100, Height = 850, Content = page };
        const string originalSettings = "{ \"temperature\": 0.15, \"context\": 4096, \"gpu\": \"auto\" }";
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token);
            ModelUiControl<TextBox>(page, "dulche-profile-context").Text = "49152";
            ModelUiControl<TextBox>(page, "dulche-profile-device").Text = "cpu";
            ModelUiControl<TextBox>(page, "dulche-profile-name").Text = "Model A edited profile";
            await SelectModelSectionAsync(page, "dulche-manage", timeout.Token);
            var settings = ModelUiControl<TextBox>(page, "dulche-profile-settings");
            Assert.True(settings.IsEffectivelyVisible && settings.IsEffectivelyEnabled);
            settings.Text = originalSettings;
            await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token);

            // Both models deliberately return identical profile arrays. Switching
            // must rebind the editor even when Replace retains the ComboBox rows.
            await SelectDraftModelAsync(page, 1, "Model B", timeout.Token);
            Assert.Equal("8192", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
            Assert.Equal("auto", ModelUiControl<TextBox>(page, "dulche-profile-device").Text);
            Assert.Equal("", ModelUiControl<TextBox>(page, "dulche-profile-name").Text);
            Assert.Equal(handler.Settings("two", "current").ToJsonString(), settings.Text);
            ModelUiControl<TextBox>(page, "dulche-profile-context").Text = "16384";
            ModelUiControl<TextBox>(page, "dulche-profile-name").Text = "Model B edited profile";

            await SelectDraftModelAsync(page, 0, "Model A", timeout.Token);
            Assert.Equal("49152", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
            Assert.Equal("cpu", ModelUiControl<TextBox>(page, "dulche-profile-device").Text);
            Assert.Equal("Model A edited profile", ModelUiControl<TextBox>(page, "dulche-profile-name").Text);
            Assert.Equal(originalSettings, settings.Text);
            Assert.False(handler.Commands.Reader.TryRead(out _));

            await SelectModelSectionAsync(page, "dulche-profile-save", timeout.Token);
            var saved = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/profiles", saved.Path);
            Assert.Equal("current", saved.Body["source_id"]!.ToString());
            Assert.Equal("Model A edited profile", saved.Body["name"]!.ToString());
            Assert.Equal(49152, saved.Body["settings"]!["context"]!.GetValue<int>());
            Assert.Equal("cpu", saved.Body["settings"]!["gpu"]!.ToString());
            Assert.Equal(0.15, saved.Body["settings"]!["temperature"]!.GetValue<double>());

            await SelectDraftModelAsync(page, 1, "Model B", timeout.Token);
            Assert.Equal("16384", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
            Assert.Equal("Model B edited profile", ModelUiControl<TextBox>(page, "dulche-profile-name").Text);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Model_profile_drafts_preserve_each_profile_and_save_only_the_selected_source()
    {
        var root = Directory.CreateTempSubdirectory("dulche-profile-drafts-");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var handler = new ProfileDraftHandler();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1100, Height = 850, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token);
            var profiles = ModelUiControl<ComboBox>(page, "dulche-profiles");
            var context = ModelUiControl<TextBox>(page, "dulche-profile-context");
            var name = ModelUiControl<TextBox>(page, "dulche-profile-name");
            context.Text = "12288"; name.Text = "Current draft";
            profiles.SelectedIndex = 1;
            Assert.Equal("2048", context.Text); Assert.Equal("", name.Text);
            context.Text = "24576"; name.Text = "Fast draft";
            profiles.SelectedIndex = 0;
            Assert.Equal("12288", context.Text); Assert.Equal("Current draft", name.Text);
            profiles.SelectedIndex = 1;
            Assert.Equal("24576", context.Text); Assert.Equal("Fast draft", name.Text);
            Assert.False(handler.Commands.Reader.TryRead(out _));

            await SelectModelSectionAsync(page, "dulche-profile-save", timeout.Token);
            var saved = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/profiles", saved.Path);
            Assert.Equal("fast", saved.Body["source_id"]!.ToString());
            Assert.Equal("Fast draft", saved.Body["name"]!.ToString());
            Assert.Equal(24576, saved.Body["settings"]!["context"]!.GetValue<int>());
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Model_profile_drafts_leave_unedited_server_refreshes_observable()
    {
        var root = Directory.CreateTempSubdirectory("dulche-profile-refresh-");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var handler = new ProfileDraftHandler();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1100, Height = 850, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token);
            Assert.Equal("8192", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
            handler.Settings("one", "current")["context"] = 32768;
            handler.Settings("one", "current")["gpu"] = "cpu";
            handler.Status["models"]![0]!["context"] = 32768;
            var reads = handler.ProfileReads;
            // This authored action performs a fresh accepted profile read after
            // selecting the same server profile. No field was edited locally.
            await SelectModelSectionAsync(page, "dulche-profile-select", timeout.Token);
            var applied = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/profiles/current/select", applied.Path);
            Assert.True(handler.ProfileReads > reads);
            Assert.Equal("32768", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
            Assert.Equal("cpu", ModelUiControl<TextBox>(page, "dulche-profile-device").Text);
            Assert.Equal("", ModelUiControl<TextBox>(page, "dulche-profile-name").Text);
            Assert.Equal(handler.Settings("one", "current").ToJsonString(),
                ModelUiControl<TextBox>(page, "dulche-profile-settings").Text);

            await SelectDraftModelAsync(page, 1, "Model B", timeout.Token);
            handler.Settings("one", "current")["context"] = 65536;
            await SelectDraftModelAsync(page, 0, "Model A", timeout.Token);
            Assert.Equal("65536", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
            Assert.Equal("", ModelUiControl<TextBox>(page, "dulche-profile-name").Text);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    private static async Task SelectDraftModelAsync(DiscoverView page, int index, string name, CancellationToken token)
    {
        var models = ModelUiControl<ComboBox>(page, "dulche-model");
        Assert.True(models.IsEffectivelyVisible && models.IsEffectivelyEnabled);
        models.SelectedIndex = index;
        await SelectModelSectionAsync(page, "dulche-view-performance", token);
        await ModelUiWaitAsync(() => ModelUiControl<TextBox>(page, "dulche-profile-context").IsEffectivelyEnabled, token);
        Assert.Equal(name, ModelUiNamed<TextBlock>(page, "Selected model name").Text);
    }

    private sealed class ProfileDraftHandler : HttpMessageHandler
    {
        public JsonObject Status { get; } = JsonNode.Parse("""
            {"phase":"ready","stop_all":false,"policy":"never","models":[
              {"id":"one","name":"Model A","state":"unloaded","can_start":true,"serving_supported":true,"context":8192,"profile_id":"current","engine":"Fixture engine","gpu":"auto"},
              {"id":"two","name":"Model B","state":"unloaded","can_start":true,"serving_supported":true,"context":8192,"profile_id":"current","engine":"Fixture engine","gpu":"auto"}],"agents":[],"resources":{}}
            """)!.AsObject();
        private readonly Dictionary<string, JsonObject> _profiles = new(StringComparer.Ordinal)
        { ["one"] = NewProfiles(), ["two"] = NewProfiles() };
        public int ProfileReads { get; private set; }
        public Func<CancellationToken, Task>? BeforeBenchmarkRead { get; set; }
        public List<string> ProfileModelsRead { get; } = [];
        public Channel<(string Path, JsonNode Body)> Commands { get; } = Channel.CreateUnbounded<(string, JsonNode)>();
        public JsonObject Settings(string model, string profile) => _profiles[model]["items"]!.AsArray()
            .Single(row => row!["id"]!.ToString() == profile)!["settings"]!.AsObject();

        private static JsonObject NewProfiles() => JsonNode.Parse("""
            {"items":[{"id":"current","name":"Current profile","protected":true,"settings":{"context":8192,"gpu":"auto"}},
              {"id":"fast","name":"Fast profile","settings":{"context":2048,"gpu":"auto"}}]}
            """)!.AsObject();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            JsonNode response = new JsonObject();
            if (request.Method == HttpMethod.Post && path != "/v1/frontend/connect")
            {
                var body = request.Content is null ? new JsonObject() : JsonNode.Parse(await request.Content.ReadAsStringAsync(token))!;
                await Commands.Writer.WriteAsync((path, body), token);
            }
            else if (path == "/v1/status") response = Status.DeepClone();
            else if (path == "/v1/inference/capabilities") response = JsonNode.Parse("""
                {"models":[{"id":"one","optimise":{"supported":false,"reason":"Not used in this fixture."},"adapters":{"supported":false}},
                  {"id":"two","optimise":{"supported":false,"reason":"Not used in this fixture."},"adapters":{"supported":false}}]}
                """)!;
            else if (path is "/v1/models/one/profiles" or "/v1/models/two/profiles")
            {
                ProfileReads++;
                ProfileModelsRead.Add(path.Split('/')[3]);
                response = _profiles[path.Split('/')[3]].DeepClone();
            }
            else if (path == "/v1/benchmarks")
            {
                if (BeforeBenchmarkRead is { } before) await before(token);
                response = new JsonObject { ["items"] = new JsonArray() };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
}
