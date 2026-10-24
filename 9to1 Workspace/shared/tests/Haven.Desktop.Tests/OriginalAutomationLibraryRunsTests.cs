#if !ANDROID
using System.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Original_automation_CUI_reads_and_pages_saved_run_lifecycle_without_disclosing_payloads_or_changing_history() =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary();
            var definitions = await SeedSavedAutomationLibrary(rig, repository, 2);
            var graphId = Guid.Parse("e0000000-0000-0000-0000-000000000001");
            const string secret = "private-original-run-payload-must-not-render";
            var now = new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero);
            var runs = Enumerable.Range(0, 18).Select(index => new AutomationRun(
                Guid.Parse("a0000000-0000-0000-0000-" + (index + 1).ToString("D12")), definitions[0].Id,
                index == 0 ? AutomationRunStatus.Failed : index == 1 ? AutomationRunStatus.Waiting : AutomationRunStatus.Succeeded,
                now, now.AddMinutes(1), index == 1 ? null : now.AddMinutes(2), secret, index == 0 ? secret : null, secret)
            {
                Revision = 7, DefinitionRevision = 3, PinnedGraph = new(graphId, 3),
                ContinuationDescriptorJson = index == 1 ? "{\"private\":\"" + secret + "\"}" : null,
                Details = new(Guid.NewGuid(), "actual-trigger-event", secret, secret, secret, secret, secret, secret, Guid.NewGuid())
            }).ToArray();
            foreach (var run in runs) await rig.Keep(repository.SuspendRunAsync(run, rig.Token));
            var homeBefore = await File.ReadAllBytesAsync(Path.Combine(rig.Root, "Home", "state.json"), rig.Token);
            var definitionsBefore = await rig.ReadOriginalAutomationDescriptors(source);
            var historyBefore = System.Text.Json.JsonSerializer.Serialize(await rig.Keep(repository.GetRunsAsync(definitions[0].Id, 32, rig.Token)));
            await WithActualSavedLibrary(rig, source, async (window, page) =>
            {
                Assert.False(BindingValue<bool>(page, "CanRefreshRuns"));
                var selected = SavedLibraryRows(page).Single(row => row.Name == definitions[0].Name);
                await rig.Keep(page.Bindings.DispatchAsync("select", selected.Target, rig.Token).AsTask());
                await PumpSavedLibrary(window);
                var button = page.GetVisualDescendants().OfType<Button>().Single(value => Label(value) == "Refresh run history");
                Assert.True(button.IsEnabled); var previous = page.Bindings.OriginalCommand;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var actual = page.Bindings.OriginalCommand ?? throw new InvalidOperationException("The original CUI did not dispatch its history read.");
                Assert.NotSame(previous, actual); await rig.Keep(actual); await PumpSavedLibrary(window);
                var first = SavedRunRows(page); Assert.Equal(16, first.Length);
                var firstKeys = first.Select(row => SavedRunField(page, row, "Key")).ToArray();
                Assert.Equal("Failed", SavedRunField(page, first[0], "Status"));
                Assert.Equal("Waiting", SavedRunField(page, first[1], "Status"));
                Assert.Contains(runs[0].Id.ToString("D"), SavedRunField(page, first[0], "Identity"));
                Assert.Contains("Automation revision: 3", SavedRunField(page, first[0], "Revision"));
                Assert.Contains(graphId.ToString("D"), SavedRunField(page, first[0], "Revision"));
                Assert.Contains("revision 3", SavedRunField(page, first[0], "Revision"));
                Assert.Contains("Error: saved", SavedRunField(page, first[0], "Content"));
                Assert.Contains("Continuation: saved", SavedRunField(page, first[1], "Content"));
                Assert.True(BindingValue<bool>(page, "CanOlderRuns"));
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains(secret, StringComparison.Ordinal) == true);
                foreach (var row in first)
                    foreach (var field in new[] { "Identity", "Status", "Timing", "Revision", "Content" })
                        Assert.DoesNotContain(secret, SavedRunField(page, row, field));
                var older = page.GetVisualDescendants().OfType<Button>().Single(value => Label(value) == "Older saved runs");
                Assert.True(older.IsEnabled); previous = page.Bindings.OriginalCommand;
                older.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                actual = page.Bindings.OriginalCommand ?? throw new InvalidOperationException("The original CUI did not dispatch its history continuation.");
                Assert.NotSame(previous, actual); await rig.Keep(actual); await PumpSavedLibrary(window);
                var last = SavedRunRows(page); Assert.Equal(2, last.Length);
                Assert.Equal(runs.Select(run => run.Id.ToString("D")), firstKeys.Concat(last.Select(row => SavedRunField(page, row, "Key"))));
                Assert.False(BindingValue<bool>(page, "CanOlderRuns"));
                Assert.Equal("Saved runs · page 2", BindingValue<string>(page, "RunHistoryPageLabel"));
            });
            Assert.Equal(homeBefore, await File.ReadAllBytesAsync(Path.Combine(rig.Root, "Home", "state.json"), rig.Token));
            Assert.Equal(definitionsBefore, await rig.ReadOriginalAutomationDescriptors(source));
            Assert.Equal(historyBefore, System.Text.Json.JsonSerializer.Serialize(await rig.Keep(repository.GetRunsAsync(definitions[0].Id, 32, rig.Token))));
        });

    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Original_automation_run_history_clears_on_selection_and_search_and_refuses_retired_presentation_rows() =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary();
            var definitions = await SeedSavedAutomationLibrary(rig, repository, 2);
            var now = DateTimeOffset.UtcNow;
            await rig.Keep(repository.SuspendRunAsync(new(Guid.NewGuid(), definitions[0].Id, AutomationRunStatus.Waiting,
                now, now, null, null, null, null) { DefinitionRevision = 2, PinnedGraph = new(Guid.NewGuid(), 2) }, rig.Token));
            await WithActualSavedLibrary(rig, source, async (window, page) =>
            {
                var firstSelection = SavedLibraryRows(page).Single(row => row.Name == definitions[0].Name);
                var secondSelection = SavedLibraryRows(page).Single(row => row.Name == definitions[1].Name);
                await rig.Keep(page.Bindings.DispatchAsync("select", firstSelection.Target, rig.Token).AsTask());
                await rig.Keep(page.Bindings.DispatchAsync("refresh-runs", null, rig.Token).AsTask());
                var retired = Assert.Single(SavedRunRows(page));
                Assert.Contains("Automation revision: 2", SavedRunField(page, retired, "Revision"));
                await rig.Keep(page.Bindings.DispatchAsync("select", secondSelection.Target, rig.Token).AsTask());
                Assert.Empty(SavedRunRows(page));
                Assert.False(page.Bindings.TryGetItemValue(retired, "Identity", out _));
                Assert.False(BindingValue<bool>(page, "CanOlderRuns"));
                await rig.Keep(page.Bindings.DispatchAsync("refresh-runs", null, rig.Token).AsTask());
                Assert.Empty(SavedRunRows(page));
                Assert.Contains("No saved runs", BindingValue<string>(page, "RunHistoryStatus"));
                Assert.True(page.Bindings.TrySetValue("Search", "different saved Automation"));
                Assert.Empty(SavedRunRows(page)); Assert.False(BindingValue<bool>(page, "CanRefreshRuns"));
                Assert.Throws<InvalidOperationException>(() => { _ = page.Bindings.DispatchAsync("refresh-runs", null, rig.Token); });
                await PumpSavedLibrary(window);
            });
            var preserved = Assert.Single(await rig.Keep(repository.GetRunsAsync(definitions[0].Id, 8, rig.Token)));
            Assert.Equal(AutomationRunStatus.Waiting, preserved.Status); Assert.Equal(2L, preserved.DefinitionRevision);
        });

    private static object[] SavedRunRows(OriginalAutomationLibraryDesktopPage page)
    {
        Assert.True(page.Bindings.TryGetValue("RunHistoryRows", out var value));
        return Assert.IsAssignableFrom<IEnumerable>(value).Cast<object>().ToArray();
    }
    private static string SavedRunField(OriginalAutomationLibraryDesktopPage page, object row, string field)
    {
        Assert.True(page.Bindings.TryGetItemValue(row, field, out var value));
        return Assert.IsType<string>(value);
    }
}
#endif
