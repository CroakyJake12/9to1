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
    [InlineData(true, "Resume", "resume")]
    [InlineData(false, "Load", "load")]
    [InlineData(null, "Load", "load")]
    public async Task Failed_model_uses_Resume_only_for_an_observed_manual_stop(bool? manual, string label, string action)
    {
        var root = Directory.CreateTempSubdirectory("dulche-failed-manual-stop-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelPresentationHandler();
        var model = handler.Status["models"]![0]!.AsObject();
        model["state"] = "failed";
        model["can_start"] = false;
        model["error"] = "The owned engine exited during startup.";
        if (manual is { } value) model["manual_suspended"] = value;
        else model.Remove("manual_suspended");
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var toggle = ModelUiControl<Button>(page, "dulche-toggle");
            var prompt = ModelUiControl<TextBox>(page, "dulche-prompt");
            prompt.Text = "Retain my draft while I explicitly retry";
            Assert.Equal(label, toggle.Content);
            Assert.True(toggle.IsEffectivelyEnabled);
            Assert.False(ModelUiControl<Button>(page, "dulche-generate").IsEnabled);
            Assert.False(handler.Commands.Reader.TryRead(out _));
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var request = await handler.Commands.Reader.ReadAsync(timeout.Token);
            Assert.Equal("/v1/models/one/" + action, request.Path);
            Assert.Empty(request.Body.AsObject());
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.Equal("Retain my draft while I explicitly retry", prompt.Text);
            Assert.False(handler.Commands.Reader.TryRead(out _));
        }
        finally
        {
            await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task Failed_manual_stop_does_not_bypass_an_unavailable_serving_backend()
    {
        var root = Directory.CreateTempSubdirectory("dulche-failed-unavailable-stop-");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var handler = new ModelPresentationHandler();
        var model = handler.Status["models"]![0]!.AsObject();
        model["state"] = "failed";
        model["manual_suspended"] = true;
        model["can_start"] = false;
        model["serving_supported"] = false;
        var page = new DiscoverView(new HomeDiscoverCatalog([]), null, null,
            new DulcheLocalClient(new UserPreferencesService(new Paths(root.FullName)), handler));
        var window = new Window { Width = 1000, Height = 750, Content = page };
        try
        {
            window.Show(); await page.ActivateAsync(timeout.Token); await OpenFirstModelAsync(page, timeout.Token);
            var prompt = ModelUiControl<TextBox>(page, "dulche-prompt");
            prompt.Text = "Keep this unavailable model draft";
            // Exercise a stale native event as well as the visible state. The manual
            // flag selects the action, but it is never serving authority.
            ModelUiControl<Button>(page, "dulche-toggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await page.TryPrepareToCloseAsync(timeout.Token));
            Assert.False(handler.Commands.Reader.TryRead(out _));
            Assert.False(ModelUiControl<Button>(page, "dulche-generate").IsEnabled);
            Assert.Contains("not available for this operation", ModelUiControl<TextBlock>(page, "dulche-message").Text);
            Assert.Equal("Keep this unavailable model draft", prompt.Text);
        }
        finally
        {
            await page.CloseAndDrainAsync(); window.Content = null; window.Close(); root.Delete(true);
        }
    }
}
