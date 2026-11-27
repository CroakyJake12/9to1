using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Haven.Desktop.Services;
using HavenOS.Home.Discover;
using DiscoverView = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;

namespace Haven.Desktop.Tests;

public sealed partial class HomeDulcheIntegrationTests
{
    [AvaloniaTheory]
    [InlineData("loading")]
    [InlineData("resuming")]
    [InlineData("recovering")]
    public async Task Manual_suspend_during_an_owned_start_uses_the_controller_and_waits_for_observed_settlement(string state)
    {
        var root = Directory.CreateTempSubdirectory("dulche-transition-suspend-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelTransitionHandler(state);
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var toggle = ModelUiControl<Button>(page, "dulche-toggle");
            var prompt = ModelUiControl<TextBox>(page, "dulche-prompt");
            prompt.Text = "Retain this unsent message";
            Assert.Equal("Suspend", toggle.Content);
            Assert.True(toggle.IsEffectivelyEnabled);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("/v1/models/one/suspend", await handler.Commands.Reader.ReadAsync(timeout.Token));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            await ModelUiWaitAsync(() => Equals(toggle.Content, "suspending…"), timeout.Token);
            Assert.False(toggle.IsEnabled);
            Assert.Equal("Retain this unsent message", prompt.Text);
            Assert.False(ModelUiControl<Button>(page, "dulche-generate").IsEnabled);

            // An accepted pending suspend is not a completed unload or permission to
            // resume. Even a stale native event must not dispatch another operation.
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.False(handler.Commands.Reader.TryRead(out _));

            handler.ObserveSuspended();
            ModelUiControl<Button>(page, "dulche-view-chat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            await ModelUiWaitAsync(() => Equals(toggle.Content, "Resume") && toggle.IsEnabled, timeout.Token);
            Assert.False(ModelUiControl<Button>(page, "dulche-generate").IsEnabled);
            Assert.Equal("Retain this unsent message", prompt.Text);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally
        {
            await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_suspend_keeps_the_clicked_model_while_Home_admission_is_held(bool changeSelection)
    {
        var root = Directory.CreateTempSubdirectory("dulche-transition-identity-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelTransitionHandler("recovering");
        var admission = new HeldModelsAdmission();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        page.BindOriginalAdmission(admission.AdmitAsync);
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var toggle = ModelUiControl<Button>(page, "dulche-toggle");
            var selector = ModelUiControl<ComboBox>(page, "dulche-model");
            var prompt = ModelUiControl<TextBox>(page, "dulche-prompt");
            prompt.Text = "Keep the current draft";
            admission.Hold = true;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await admission.Entered.Task.WaitAsync(timeout.Token);
            Assert.False(toggle.IsEnabled);
            Assert.False(selector.IsEnabled);
            Assert.False(handler.Commands.Reader.TryRead(out _));
            // A queued duplicate and a stale selection callback cannot retarget the
            // already issued manual intent to the other configured model.
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (changeSelection) selector.SelectedIndex = 1;
            prompt.Text = "Keep a newer draft too";
            admission.Release.TrySetResult();
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            if (changeSelection)
                Assert.Contains("selected model changed", ModelUiControl<TextBlock>(page, "dulche-message").Text);
            else
                Assert.Equal("/v1/models/one/suspend", await handler.Commands.Reader.ReadAsync(timeout.Token));
            Assert.False(handler.Commands.Reader.TryRead(out _));
            Assert.Equal("Keep a newer draft too", prompt.Text);
        }
        finally
        {
            admission.Release.TrySetResult();
            await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true);
        }
    }

    [AvaloniaTheory]
    [InlineData("suspending")]
    [InlineData("unloading")]
    public async Task Pending_lifecycle_transition_cannot_issue_another_start_or_stop(string state)
    {
        var root = Directory.CreateTempSubdirectory("dulche-transition-pending-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelTransitionHandler(state);
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var toggle = ModelUiControl<Button>(page, "dulche-toggle");
            Assert.Equal(state + "…", toggle.Content);
            Assert.False(toggle.IsEnabled);
            Assert.False(ModelUiControl<Button>(page, "dulche-unload").IsEnabled);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally
        {
            await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task Manual_suspend_refusal_retains_the_error_and_draft_without_retrying_or_claiming_release()
    {
        var root = Directory.CreateTempSubdirectory("dulche-transition-refused-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelTransitionHandler("recovering") { RefuseSuspend = true };
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var toggle = ModelUiControl<Button>(page, "dulche-toggle");
            var prompt = ModelUiControl<TextBox>(page, "dulche-prompt");
            prompt.Text = "Preserve this draft after refusal";
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("/v1/models/one/suspend", await handler.Commands.Reader.ReadAsync(timeout.Token));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.Contains(ModelTransitionHandler.Refusal, ModelUiControl<TextBlock>(page, "dulche-message").Text);
            Assert.Equal("Suspend", toggle.Content);
            Assert.Equal("Preserve this draft after refusal", prompt.Text);
            Assert.Equal("recovering", handler.State);
            ModelUiControl<Button>(page, "dulche-view-chat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally
        {
            await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true);
        }
    }

    private sealed class ModelTransitionHandler(string state) : HttpMessageHandler
    {
        internal const string Refusal = "The controller could not verify ownership of this model transition.";
        internal string State { get; private set; } = state;
        internal bool RefuseSuspend { get; init; }
        private bool ManualSuspended { get; set; } = state == "suspending";
        internal Channel<string> Commands { get; } = Channel.CreateUnbounded<string>();
        internal void ObserveSuspended() { State = "suspended"; ManualSuspended = true; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            object body = new { };
            if (request.Method == HttpMethod.Post && path.StartsWith("/v1/models/", StringComparison.Ordinal))
            {
                Assert.Empty(JsonNode.Parse(await request.Content!.ReadAsStringAsync(token))!.AsObject());
                await Commands.Writer.WriteAsync(path, token);
                if (path == "/v1/models/one/suspend")
                {
                    if (RefuseSuspend)
                        return Reply(HttpStatusCode.Conflict, new { error = Refusal });
                    State = "suspending";
                    ManualSuspended = true;
                }
            }
            else if (path == "/v1/status")
                body = new
                {
                    phase = "ready", stop_all = false,
                    models = new[]
                    {
                        new { id = "one", name = "Fixture starting model", state = State, context = 524288,
                            managed = !RefuseSuspend, serving_supported = true, manual_suspended = ManualSuspended,
                            can_start = State is "loading" or "resuming" or "recovering" },
                        new { id = "two", name = "Other configured model", state = "unloaded", context = 8192,
                            managed = true, serving_supported = true, manual_suspended = false, can_start = true }
                    },
                    agents = Array.Empty<object>(), resources = new { }
                };
            else if (path == "/v1/inference/capabilities")
                body = new { optimise = new { supported = false }, adapters = new { supported = false } };
            return Reply(HttpStatusCode.OK, body);
        }

        private static HttpResponseMessage Reply(HttpStatusCode status, object body) => new(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
    }
}
