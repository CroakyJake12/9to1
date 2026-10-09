#if !ANDROID
using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

// Actual protected SQLite/Home/OS profile/readonly authored CUI over the maintained
// compatible-context fixture. No scheduler, installation, App bootstrap or writer
// admission is fabricated; these observations grant no execution or edit authority.
public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    public static bool LinuxLibraryFixtureSupported => OperatingSystem.IsLinux();

    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Saved_automation_library_renders_source_rows_and_selected_settings_without_changing_store_or_Home() =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary();
            var rows = await SeedSavedAutomationLibrary(rig, repository, 2);
            var homeBefore = await File.ReadAllBytesAsync(Path.Combine(rig.Root, "Home", "state.json"), rig.Token);
            var observedBefore = await rig.ReadOriginalAutomationDescriptors(source);
            await WithActualSavedLibrary(rig, source, async (window, page) =>
            {
                var list = SavedLibraryRows(page);
                Assert.Equal(2, list.Length);
                Assert.Contains(list, row => row.Status.Contains("Disabled in saved settings", StringComparison.Ordinal));
                Assert.Contains(list, row => row.Status.Contains("Enabled in saved settings", StringComparison.Ordinal));
                Assert.All(list, row => Assert.Contains("review needed", row.Status));
                Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Saved automation library");
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button =>
                    Label(button) is "Run" or "Edit" or "Delete");
                var target = list.Single(row => row.Name == rows[0].Name);
                Assert.False(page.Bindings.TryGetItemValue(target with { Name = "Copied row" }, "Name", out _));
                Assert.False(page.Bindings.TryGetItemValue(target with { Name = "Copied row" }, "CanOpen", out _));
                Assert.True(page.Bindings.TryGetItemValue(target, "Name", out var sourceName));
                Assert.Equal(target.Name, sourceName);
                var button = page.GetVisualDescendants().OfType<Button>().Single(value =>
                    Label(value) == "View saved settings" && IsRowButton(value, target.Name));
                var previousCommand = page.Bindings.OriginalCommand;
                Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var actual = page.Bindings.OriginalCommand ?? throw new InvalidOperationException("The actual loader did not dispatch its library command.");
                Assert.NotSame(previousCommand, actual);
                await rig.Keep(actual); await PumpSavedLibrary(window);
                Assert.Equal(rows[0].Name, BindingValue<string>(page, "SelectedName"));
                Assert.Equal(rows[0].Instruction, BindingValue<string>(page, "Instruction"));
                Assert.Contains(rows[0].ScheduleJson, BindingValue<string>(page, "SelectedSchedule"));
                Assert.Contains("Review is needed", BindingValue<string>(page, "Review"));
            });
            Assert.Equal(homeBefore, await File.ReadAllBytesAsync(Path.Combine(rig.Root, "Home", "state.json"), rig.Token));
            Assert.Equal(observedBefore, await rig.ReadOriginalAutomationDescriptors(source));
        });

    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Saved_library_older_page_and_literal_search_keep_actual_rows_and_refuse_a_retired_selection() =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary();
            var rows = await SeedSavedAutomationLibrary(rig, repository, 35);
            var before = await rig.ReadOriginalAutomationDescriptors(source);
            await WithActualSavedLibrary(rig, source, async (window, page) =>
            {
                Assert.Equal(32, SavedLibraryRows(page).Length);
                var oldTarget = SavedLibraryRows(page)[0].Target;
                Assert.True(page.Bindings.IsActionAvailable("older"));
                await rig.Keep(page.Bindings.DispatchAsync("older", null, rig.Token).AsTask()); await PumpSavedLibrary(window);
                Assert.Equal(3, SavedLibraryRows(page).Length);
                Assert.False(page.Bindings.IsActionAvailable("older"));
                Assert.Contains(rows[0].Name, SavedLibraryRows(page).Select(row => row.Name));
                Assert.Throws<InvalidOperationException>(() => { _ = page.Bindings.DispatchAsync("select", oldTarget, rig.Token); });
                Assert.True(page.Bindings.TrySetValue("Search", "100%_literal"));
                await rig.Keep(page.Bindings.DispatchAsync("refresh", null, rig.Token).AsTask()); await PumpSavedLibrary(window);
                Assert.Equal(rows.Count(row => row.Name.Contains("100%_literal", StringComparison.Ordinal)), SavedLibraryRows(page).Length);
                Assert.DoesNotContain(SavedLibraryRows(page), row => row.Name.Contains("100XXliteral", StringComparison.Ordinal));
                var actual = Assert.Single(SavedLibraryRows(page));
                await rig.Keep(page.Bindings.DispatchAsync("select", actual.Target, rig.Token).AsTask());
                Assert.Equal(actual.Name, BindingValue<string>(page, "SelectedName"));
            });
            Assert.Equal(before, await rig.ReadOriginalAutomationDescriptors(source));
        });

    [AvaloniaFact]
    public async Task Saved_library_missing_configured_source_keeps_authored_setup_and_cannot_issue_actions()
    {
        using var app = new CancellationTokenSource(); using var native = new CancellationTokenSource();
        var window = new Window { Width = 1000, Height = 800 }; window.Show();
        OriginalAutomationLibraryDesktopPage? page = null; Exception? failure = null; var closeHealthy = false;
        try
        {
            page = OriginalAutomationLibraryDesktopPage.BindOriginal(null, null, window, app.Token, native.Token, actual => page = actual);
            window.Content = page; await page.InitializeAsync(TestContext.Current.CancellationToken); await PumpSavedLibrary(window);
            Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Saved automation library");
            Assert.Contains("Open Automations from Home", BindingValue<string>(page, "Status"));
            Assert.Empty(SavedLibraryRows(page));
            Assert.All(page.GetVisualDescendants().OfType<Button>(), button => Assert.False(button.IsEnabled));
            Assert.Throws<InvalidOperationException>(() => { _ = page.Bindings.DispatchAsync("refresh", null); });
        }
        catch (Exception cause) { failure = cause; }
        finally
        {
            try { if (page is not null) await page.CloseAndDrainAsync(); closeHealthy = true; }
            catch (Exception cause) { failure = failure is null ? cause : new AggregateException(failure, cause); }
            if (closeHealthy) window.Close();
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Saved_library_failed_binding_close_independently_joins_actual_loader_and_retains_its_tree() =>
        Run(async rig =>
        {
            var (source, _) = rig.CreateOriginalAutomationLibrary();
            var failure = new IOException("The actual protected read completed before the presentation source failed.");
            var instrumented = new ActualSavedLibraryReadFaultSource(source, failure);
            using var app = new CancellationTokenSource(); using var native = new CancellationTokenSource();
            var window = new Window { Width = 1000, Height = 800 }; window.Show();
            OriginalAutomationLibraryDesktopPage? page = null; Task? initialization = null, close = null;
            // This wrapper instruments presentation custody only. Its underlying
            // actor/store/receipt/SQL read is the SAME genuine configured source;
            // it is never an App producer or a replacement Home grant.
            try
            {
                page = OriginalAutomationLibraryDesktopPage.BindOriginal(instrumented, rig.Home.Profiles, window,
                    app.Token, native.Token, actual => page = actual);
                window.Content = page;
                initialization = rig.Keep(page.InitializeAsync(rig.Token));
                var observed = await Record.ExceptionAsync(() => initialization);
                Assert.NotNull(observed); AssertOnlySavedLibraryFailure(initialization.Exception ?? observed, failure);
                rig.KeepExpected(initialization, observed);
                var actualRead = instrumented.ActualProtectedRead ?? throw new InvalidOperationException("The genuine protected read was not accepted.");
                await actualRead; Assert.True(actualRead.IsCompletedSuccessfully);
                Assert.NotNull(page.Content);
                close = rig.Keep(page.CloseAndDrainAsync()); Assert.Same(close, page.OriginalClose);
                Assert.Same(close, page.CloseAndDrainAsync());
                var closed = await Record.ExceptionAsync(() => close);
                Assert.NotNull(closed); AssertOnlySavedLibraryFailure(close.Exception ?? closed, failure);
                rig.KeepExpected(close, closed);
                Assert.True(page.OriginalBindingsClose?.IsFaulted);
                foreach (var actual in new[] { page.OriginalLoaderIdle, page.OriginalLoaderDispose, page.OriginalLoaderTerminal })
                {
                    Assert.NotNull(actual); await actual; Assert.True(actual.IsCompletedSuccessfully);
                }
                Assert.NotNull(page.Content); Assert.True(window.IsVisible);
            }
            finally
            {
                if (page is not null)
                {
                    close ??= rig.Keep(page.CloseAndDrainAsync());
                    var observed = await Record.ExceptionAsync(() => close);
                    Assert.NotNull(observed); AssertOnlySavedLibraryFailure(close.Exception ?? observed, failure);
                    rig.KeepExpected(close, observed);
                }
                // Failed originals and their actual native tree remain retained.
            }
        });

    private static void AssertOnlySavedLibraryFailure(Exception actual, Exception expected)
    {
        if (ReferenceEquals(actual, expected)) return;
        if (actual is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var child in group.InnerExceptions) AssertOnlySavedLibraryFailure(child, expected); return; }
        Assert.Fail("An independently unknown library/body/cleanup occurrence was returned: " + actual.GetType().FullName);
    }
    private sealed class ActualSavedLibraryReadFaultSource(
        ICanonicalAutomationLibraryOriginalReadSource actual, Exception failure) : ICanonicalAutomationLibraryOriginalReadSource
    {
        internal Task<ICanonicalAutomationLibraryOriginalObservation>? ActualProtectedRead { get; private set; }
        public bool IsIssuedOriginalObservation(ICanonicalAutomationLibraryOriginalObservation value) => actual.IsIssuedOriginalObservation(value);
        public bool IsIssuedOriginalContinuation(ICanonicalAutomationLibraryOriginalContinuation value) => actual.IsIssuedOriginalContinuation(value);
        public async Task<ICanonicalAutomationLibraryOriginalObservation> ReadOriginalLibraryWithinSourceAsync(
            AuthenticatedResourceActor actor, AutomationLibraryQuery query, Action<Action> scope, Action<Task> retain,
            CancellationToken token, ICanonicalAutomationLibraryOriginalContinuation? continuation = null)
        {
            var raw = actual.ReadOriginalLibraryWithinSourceAsync(actor, query, scope, retain, token, continuation);
            ActualProtectedRead = raw;
            await raw;
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            throw new InvalidOperationException("The original injected failure did not throw.");
        }
        public Task RevalidateOriginalObservationWithinSourceAsync(ICanonicalAutomationLibraryOriginalObservation observation,
            AuthenticatedResourceActor actor, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
            actual.RevalidateOriginalObservationWithinSourceAsync(observation, actor, scope, retain, token);
    }
    private static async Task WithActualSavedLibrary(Rig rig, ICanonicalAutomationLibraryOriginalReadSource source,
        Func<Window, OriginalAutomationLibraryDesktopPage, Task> body)
    {
        using var app = new CancellationTokenSource(); using var native = new CancellationTokenSource();
        var window = new Window { Width = 1100, Height = 900 }; window.Show();
        OriginalAutomationLibraryDesktopPage? page = null; var failures = new List<Exception>(); var closedHealthy = false;
        try
        {
            page = OriginalAutomationLibraryDesktopPage.BindOriginal(source, rig.Home.Profiles, window,
                app.Token, native.Token, actual => page = actual);
            window.Content = page; var init = rig.Keep(page.InitializeAsync(rig.Token));
            Assert.Same(init, page.OriginalInitialization); await init; await PumpSavedLibrary(window);
            await body(window, page);
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            if (page is not null)
            {
                Task? close = null;
                try
                {
                    close = rig.Keep(page.CloseAndDrainAsync()); Assert.Same(close, page.OriginalClose);
                    Assert.Same(close, page.CloseAndDrainAsync()); await close; Assert.Null(page.Content); closedHealthy = true;
                }
                catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
            }
            else closedHealthy = true;
            if (closedHealthy) window.Close(); // Failed native originals and their tree remain retained.
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual saved-library body and original close failed.", failures);
    }
    private static async Task<AutomationDefinition[]> SeedSavedAutomationLibrary(Rig rig, AutomationRepository repository, int count)
    {
        var now = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var rows = Enumerable.Range(0, count).Select(index => new AutomationDefinition(Guid.NewGuid(),
            index == 0 ? "100%_literal saved automation" : index == 1 ? "100XXliteral decoy" : "Saved automation " + index,
            HavenMode.Chat, "Original saved instruction " + index, AutomationScheduleKind.Once,
            "{\"at\":\"2030-01-02T03:04:05Z\",\"original\":true}", null, null, index % 2 == 1,
            now.AddMinutes(index), now.AddMinutes(index))).ToArray();
        // Explicit fixture owner initialization/write, before the library reads. The
        // production READ route never invokes this ordinary repository lifecycle.
        foreach (var row in rows) await rig.Keep(repository.UpsertAsync(row, rig.Token));
        return rows;
    }
    private static string Label(Button button) => button.Content is TextBlock text ? text.Text ?? "" : button.Content?.ToString() ?? "";
    private static bool IsRowButton(Button button, string name) => button.GetVisualAncestors().OfType<StackPanel>().FirstOrDefault() is { } panel &&
        panel.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == name);
    private static T BindingValue<T>(OriginalAutomationLibraryDesktopPage page, string key)
    { Assert.True(page.Bindings.TryGetValue(key, out var value)); return Assert.IsType<T>(value); }
    private static OriginalAutomationLibraryBindings.LibraryRow[] SavedLibraryRows(OriginalAutomationLibraryDesktopPage page)
    {
        Assert.True(page.Bindings.TryGetValue("Rows", out var value));
        return Assert.IsAssignableFrom<IEnumerable>(value).Cast<OriginalAutomationLibraryBindings.LibraryRow>().ToArray();
    }
    private static async Task PumpSavedLibrary(Window window)
    { await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background); await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background); }
    private sealed partial class Rig
    {
        internal (ICanonicalAutomationLibraryOriginalReadSource Source, AutomationRepository Repository) CreateOriginalAutomationLibrary()
        {
            var repository = Assert.IsType<AutomationRepository>(Provider.GetRequiredService<IAutomationRepository>());
            Assert.True(repository.HasOriginalSqliteFactory(Database));
            var actual = new CanonicalAutomationLibraryOriginalReadOwner(Store, Database, _paths, repository, Home.Ownership);
            return (actual, repository);
        }
        internal async Task<string[]> ReadOriginalAutomationDescriptors(ICanonicalAutomationLibraryOriginalReadSource source)
        {
            var values = new List<string>(); ICanonicalAutomationLibraryOriginalContinuation? cursor = null;
            do
            {
                var page = await Keep(source.ReadOriginalLibraryWithinSourceAsync(Actor, new(Limit: 32), Scope, Retain, Token, cursor));
                Assert.True(source.IsIssuedOriginalObservation(page));
                foreach (var row in page.Definitions)
                    values.Add(System.Text.Json.JsonSerializer.Serialize(new { row.Value, Fields = row.RetainedProtectedDescriptors.OrderBy(value => value.Key) }));
                cursor = page.NextContinuation;
            } while (cursor is not null);
            return values.ToArray();
        }
    }
}
#endif
