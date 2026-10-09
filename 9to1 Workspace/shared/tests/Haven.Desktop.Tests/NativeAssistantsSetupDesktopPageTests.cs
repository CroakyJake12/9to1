#if !ANDROID
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Assistants;

namespace Haven.Desktop.Tests;

/// <summary>Actual read-only native setup CUI controls only. No configured Den,
/// installed-package proof, controller or provider is fabricated by these fixtures.</summary>
public sealed class NativeAssistantsSetupDesktopPageTests
{
    [AvaloniaFact]
    public async Task Missing_owner_preserves_dedicated_authored_Home_and_same_actual_close()
    {
        using var appLifetime = new CancellationTokenSource();
        using var windowLifetime = new CancellationTokenSource();
        var window = new Window(); window.Show();
        NativeAssistantsSetupDesktopPage? page = null;
        Task? initialization = null;
        Task? close = null;
        Exception? bodyFailure = null;
        try
        {
            page = NativeAssistantsSetupDesktopPage.BindOriginal(OriginalAssistantsDependencyStatus.SetupRequired(),
                appLifetime.Token, windowLifetime.Token, window, actual => page = actual);
            window.Content = page;
            initialization = page.InitializeAsync(TestContext.Current.CancellationToken);
            Assert.Same(initialization, page.OriginalInitialization);
            await initialization;
            window.UpdateLayout(); // Materialize the actual window/content templates before visual assertions.
            Assert.NotNull(page.Content);
            Assert.Equal(CuiSceneAvailabilityState.Unavailable, page.Availability.State);
            Assert.Equal("MissingOwner", page.Availability.Code);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Assistants");
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("install or repair Spaces", StringComparison.Ordinal) == true);
            var buttons = page.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.NotEmpty(buttons);
            Assert.All(buttons, button => Assert.False(button.IsEnabled));
            Assert.True(await page.PrepareToCloseAsync(TestContext.Current.CancellationToken));
            Assert.True(page.IsOriginalClosePrepared);
            close = page.CloseAndDrainAsync();
            Assert.Same(close, page.OriginalClose);
            Assert.Same(close, page.CloseAndDrainAsync());
            await close;
            Assert.Null(page.Content);
            Assert.False(page.IsOriginalClosePrepared);
        }
        catch (Exception failure) { bodyFailure = failure; }
        finally { await FinishOriginalFixtureAsync(window, page, initialization, close, bodyFailure); }
    }

    [AvaloniaFact]
    public async Task Actual_partial_owner_exists_before_constructor_callbacks_and_reentrant_join_is_denied()
    {
        using var appLifetime = new CancellationTokenSource();
        using var windowLifetime = new CancellationTokenSource();
        var window = new Window(); window.Show();
        NativeAssistantsSetupDesktopPage? partial = null;
        Task? close = null;
        Exception? bodyFailure = null;
        try
        {
            var page = NativeAssistantsSetupDesktopPage.BindOriginal(OriginalAssistantsDependencyStatus.SetupRequired(),
                appLifetime.Token, windowLifetime.Token, window, actual =>
                {
                    partial = actual;
                    Assert.Null(actual.Content);
                    Assert.Throws<InvalidOperationException>(() => { _ = actual.CloseAndDrainAsync(); });
                });
            Assert.Same(partial, page);
            close = page.CloseAndDrainAsync();
            await close;
            Assert.Null(page.Content);
        }
        catch (Exception failure) { bodyFailure = failure; }
        finally { await FinishOriginalFixtureAsync(window, partial, null, close, bodyFailure); }
    }
    private static async Task FinishOriginalFixtureAsync(Window window, NativeAssistantsSetupDesktopPage? page,
        Task? initialization, Task? originalClose, Exception? bodyFailure)
    {
        var failures = new List<Exception>();
        void Add(Exception cause)
        {
            if (!failures.Any(known => ReferenceEquals(known, cause))) failures.Add(cause);
        }
        async Task Join(Task actual)
        {
            try { await actual; }
            catch (Exception caught)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } payload)
                    foreach (var cause in payload.InnerExceptions) Add(cause);
                else Add(caught); // Actual canceled source, never a blanket type/token waiver.
            }
        }
        if (bodyFailure is not null) Add(bodyFailure);
        if (initialization is not null) await Join(initialization);
        if (page is not null)
        {
            try
            {
                var acquired = page.CloseAndDrainAsync();
                if (originalClose is not null && !ReferenceEquals(originalClose, acquired))
                    Add(new InvalidOperationException("The native fixture close owner replaced its actual original Task."));
                originalClose ??= acquired;
                if (!ReferenceEquals(originalClose, acquired)) await Join(acquired);
            }
            catch (Exception acquisitionFailure) { Add(acquisitionFailure); }
            if (originalClose is not null) await Join(originalClose);
        }
        // Retain the real fixture window if the actual page close remains failed or unknown.
        if (page is null || originalClose?.IsCompletedSuccessfully == true)
            try { window.Close(); } catch (Exception closeFailure) { Add(closeFailure); }
        else if (failures.Count == 0)
            Add(new InvalidOperationException("The actual setup fixture close remains unresolved."));
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual setup fixture body/source/close causes were retained.", failures);
    }
}
#endif
