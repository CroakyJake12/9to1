using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Haven.Desktop.Services;
using HavenOS.Home.Discover;
using System.Text.Json.Nodes;
using DiscoverView = Haven.Desktop.Views.Pages.Home.HomeDiscoverPage;

namespace Haven.Desktop.Tests;

public sealed partial class HomeDulcheIntegrationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Held_Home_admission_keeps_the_clicked_message_and_refuses_a_changed_model(bool replaceModel)
    {
        var root = Directory.CreateTempSubdirectory("dulche-admitted-intent-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new InferenceManagementHandler();
        var admission = new HeldModelsAdmission();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        page.BindOriginalAdmission(admission.AdmitAsync);
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var prompt = ModelUiControl<TextBox>(page, "dulche-prompt");
            var send = ModelUiControl<Button>(page, "dulche-generate");
            var selector = ModelUiControl<ComboBox>(page, "dulche-model");
            prompt.Text = "The message actually submitted";
            admission.Hold = true;
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(send.IsEnabled); Assert.False(selector.IsEnabled);
            await admission.Entered.Task.WaitAsync(timeout.Token);
            var preparing = page.TryPrepareToCloseAsync(timeout.Token);
            Assert.False(preparing.IsCompleted);
            prompt.Text = "A newer unsent draft";
            // A queued duplicate event must not acquire a second request, even if a
            // platform callback arrives after the native button was disabled.
            send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (replaceModel) selector.SelectedIndex = -1;
            Assert.Equal(0, handler.CompletionAttempts);
            admission.Release.TrySetResult();
            if (!replaceModel)
            {
                var request = await handler.Completion.Task.WaitAsync(timeout.Token);
                Assert.Equal("one", request["model"]!.ToString());
                Assert.Equal("The message actually submitted", request["messages"]!.AsArray().Last()!["content"]!.ToString());
                handler.ReleaseCompletion.TrySetResult();
            }
            Assert.True(await preparing);
            Assert.Equal(replaceModel ? 0 : 1, handler.CompletionAttempts);
            Assert.Equal("A newer unsent draft", prompt.Text);
        }
        finally
        {
            admission.Release.TrySetResult(); handler.ReleaseCompletion.TrySetResult();
            await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task Profile_save_uses_the_clicked_profile_and_fields_even_when_admission_is_delayed()
    {
        var root = Directory.CreateTempSubdirectory("dulche-profile-intent-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelPresentationHandler();
        var admission = new HeldModelsAdmission();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        page.BindOriginalAdmission(admission.AdmitAsync);
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            await SelectModelSectionAsync(page, "dulche-view-performance", timeout.Token);
            ModelUiControl<TextBox>(page, "dulche-profile-name").Text = "Submitted profile";
            ModelUiControl<TextBox>(page, "dulche-profile-context").Text = "524288";
            admission.Hold = true;
            ModelUiControl<Button>(page, "dulche-profile-save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await admission.Entered.Task.WaitAsync(timeout.Token);
            Assert.False(ModelUiControl<ComboBox>(page, "dulche-profiles").IsEnabled);
            // Exercise a stale binding update, not an enabled user control. Its values
            // remain a new draft and cannot alter the already accepted save intent.
            ModelUiControl<ComboBox>(page, "dulche-profiles").SelectedIndex = 1;
            ModelUiControl<TextBox>(page, "dulche-profile-name").Text = "Later draft";
            ModelUiControl<TextBox>(page, "dulche-profile-context").Text = "16384";
            admission.Release.TrySetResult();
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            var saved = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/profiles", saved.Path);
            Assert.Equal("current", saved.Body["source_id"]!.ToString());
            Assert.Equal("Submitted profile", saved.Body["name"]!.ToString());
            Assert.Equal(524288, saved.Body["settings"]!["context"]!.GetValue<int>());
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally
        {
            admission.Release.TrySetResult(); await page.CloseAndDrainAsync();
            window.Content = null; window.Close(); root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task A_removed_explicit_model_stays_unavailable_until_another_model_is_chosen()
    {
        var root = Directory.CreateTempSubdirectory("dulche-removed-model-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new InferenceManagementHandler();
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var prompt = ModelUiControl<TextBox>(page, "dulche-prompt"); prompt.Text = "Draft for the removed model";
            var selector = ModelUiControl<ComboBox>(page, "dulche-model");
            handler.ReplaceModelOnPoll = true;
            await ModelUiWaitAsync(() => selector.Items.Cast<object>().Any(item => item.ToString() == "Different model"), timeout.Token);
            Assert.Null(selector.SelectedItem);
            Assert.False(ModelUiControl<Button>(page, "dulche-generate").IsEnabled);
            ModelUiControl<Button>(page, "dulche-generate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.Equal(0, handler.CompletionAttempts); Assert.Null(selector.SelectedItem);
            Assert.Equal("Draft for the removed model", prompt.Text);
            selector.SelectedIndex = 0;
            Assert.Equal("Different model", selector.SelectedItem!.ToString());
            Assert.True(ModelUiControl<Button>(page, "dulche-generate").IsEnabled);
        }
        finally { handler.ReleaseCompletion.TrySetResult(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task Unconfirmed_owned_stop_prevents_successful_close_and_retains_the_partial_answer_and_draft()
    {
        var root = Directory.CreateTempSubdirectory("dulche-panel-unconfirmed-stop-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new DirectChatHandler { StopSettled = false, SavedRunModel = "one" };
        handler.Stream.Push("data: {\"choices\":[{\"delta\":{\"content\":\"Retained partial answer\"}}]}\n\n");
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var prompt = ModelUiControl<TextBox>(page, "dulche-prompt"); prompt.Text = "Original request";
            ModelUiControl<Button>(page, "dulche-generate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ModelUiWaitAsync(() => ModelUiControl<TextBlock>(page, "dulche-output").Text == "Retained partial answer", timeout.Token);
            prompt.Text = "New unsent draft";
            // Even a stale callback from Advanced cannot restore a saved run over
            // the current stream. The control and dispatch both refuse it.
            Assert.False(ModelUiControl<Button>(page, "dulche-recover").IsEnabled);
            ModelUiControl<Button>(page, "dulche-recover").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0, handler.SavedRunReads);
            ModelUiControl<Button>(page, "dulche-generation-cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("/v1/agents/direct-current/stop", await handler.Stops.Reader.ReadAsync(timeout.Token));
            var failure = await Record.ExceptionAsync(async () => await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.NotNull(failure); Assert.True(ContainsUnconfirmedStop(failure), failure.ToString());
            Assert.Equal("Retained partial answer", ModelUiControl<TextBlock>(page, "dulche-output").Text);
            Assert.Equal("New unsent draft", prompt.Text);
            Assert.False(ModelUiControl<Button>(page, "dulche-generate").IsEnabled);
            var close = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            Assert.NotNull(close); Assert.True(ContainsUnconfirmedStop(close), close.ToString());
            Assert.False(handler.Stops.Reader.TryRead(out _));
        }
        finally
        {
            // This fixture intentionally owns an unconfirmed stop. Observe its retained
            // close failure without converting it into a successful production receipt.
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            window.Content = null; window.Close(); root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task Saved_run_for_an_unavailable_model_cannot_be_displayed_under_the_current_model()
    {
        var root = Directory.CreateTempSubdirectory("dulche-foreign-saved-run-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new DirectChatHandler { SavedRunModel = "missing-model" };
        handler.Stream.Push("data: {\"choices\":[{\"delta\":{\"content\":\"Current model answer\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            ModelUiControl<TextBox>(page, "dulche-prompt").Text = "Current request";
            ModelUiControl<Button>(page, "dulche-generate").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            var selected = ModelUiControl<ComboBox>(page, "dulche-model").SelectedItem;
            await SelectModelSectionAsync(page, "dulche-manage", timeout.Token);
            await SelectModelSectionAsync(page, "dulche-recover", timeout.Token);
            Assert.Equal(1, handler.SavedRunReads);
            Assert.Same(selected, ModelUiControl<ComboBox>(page, "dulche-model").SelectedItem);
            Assert.Equal("Current model answer", ModelUiControl<TextBlock>(page, "dulche-output").Text);
            Assert.Contains("saved run's model is unavailable", ModelUiControl<TextBlock>(page, "dulche-message").Text);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    [AvaloniaFact]
    public async Task A_delayed_performance_read_cannot_admit_the_previous_models_profiles_under_a_new_selection()
    {
        var root = Directory.CreateTempSubdirectory("dulche-profile-read-target-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ProfileDraftHandler();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.BeforeBenchmarkRead = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); };
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1100, Height = 800, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            ModelUiControl<Button>(page, "dulche-view-performance").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task.WaitAsync(timeout.Token);
            var models = ModelUiControl<ComboBox>(page, "dulche-model");
            Assert.True(models.IsEnabled); models.SelectedIndex = 1;
            release.TrySetResult();
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.DoesNotContain("one", handler.ProfileModelsRead);
            Assert.Contains("two", handler.ProfileModelsRead);
            Assert.Equal("Model B", ModelUiNamed<TextBlock>(page, "Selected model name").Text);
            Assert.True(ModelUiControl<TextBox>(page, "dulche-profile-context").IsEnabled);
            Assert.Equal("8192", ModelUiControl<TextBox>(page, "dulche-profile-context").Text);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally { release.TrySetResult(); await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true); }
    }

    private static bool ContainsUnconfirmedStop(Exception error) => error is DulcheRunStopUnconfirmedException ||
        error is AggregateException aggregate && aggregate.InnerExceptions.Any(ContainsUnconfirmedStop);

    private sealed class HeldModelsAdmission
    {
        public bool Hold;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task AdmitAsync(CancellationToken token)
        {
            if (!Hold) return;
            Entered.TrySetResult(); await Release.Task.WaitAsync(token);
        }
    }
}
