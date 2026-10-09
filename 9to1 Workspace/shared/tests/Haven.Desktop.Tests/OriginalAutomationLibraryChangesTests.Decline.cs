#if !ANDROID
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application.Automations;
using Haven.Desktop.Views.Pages.Automations;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    [AvaloniaFact(SkipUnless = nameof(LinuxLibraryFixtureSupported), Skip = "Actual native Linux custody fixture; installed Windows acceptance is separate.")]
    public Task Saved_library_actual_manual_decline_keeps_rows_and_closes_view_process_and_Home_originals_healthy() =>
        Run(async rig =>
        {
            var (source, repository) = rig.CreateOriginalAutomationLibrary();
            var definitions = await SeedSavedAutomationLibrary(rig, repository, 2);
            await WithActualAutomationChanges(rig, Assert.IsType<CanonicalAutomationLibraryOriginalReadOwner>(source), async graph =>
            {
                await WithActualChangeLibrary(rig, graph, async (window, page) =>
                {
                    var row = SavedLibraryRows(page).Single(value => value.Name == definitions[1].Name);
                    var before = await graph.SelectedRaw(definitions[1].Id);
                    var selection = page.GetVisualDescendants().OfType<Button>().Single(value =>
                        Label(value) == "View saved settings" && IsRowButton(value, row.Name));
                    selection.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await rig.Keep(page.Bindings.OriginalCommand!); await PumpSavedLibrary(window);
                    var change = page.GetVisualDescendants().OfType<Button>().Single(value => Label(value) == "Disable saved automation");
                    Assert.True(change.IsEnabled); change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var actualView = rig.Keep(page.Bindings.OriginalCommand ?? throw new InvalidOperationException("No actual CUI review command."));
                    var request = await graph.WaitReview(actualView);
                    Assert.Equal(HomeCanonicalAutomationDefinitionWriteSource.DisableAction, request.Scope.ActionName);
                    Assert.True(request.Policy.RequiresPerActionApproval); Assert.False(actualView.IsCompleted);
                    Assert.Equal(before, await graph.SelectedRaw(definitions[1].Id));
                    Assert.True((await rig.Keep(graph.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Decline,
                        cancellationToken: rig.Token))).Succeeded);
                    await actualView; await PumpSavedLibrary(window);
                    Assert.True(actualView.IsCompletedSuccessfully);
                    Assert.Equal("The change was declined. Saved settings were kept.", BindingValue<string>(page, "ChangeStatus"));
                    var observation = page.Bindings.OriginalChangeObservation ?? throw new InvalidOperationException("No actual issued delivery was retained.");
                    Assert.True(graph.Writer.IsIssuedOriginalChangeObservation(observation));
                    var actualWait = rig.Keep(observation.WaitOriginalCompletionAsync(rig.Token));
                    var outcome = await actualWait;
                    Assert.True(observation.IsIssuedOriginalCompletion(outcome));
                    Assert.Same(observation, outcome.OriginalObservation);
                    Assert.Equal(CanonicalAutomationOriginalChangeCompletionKind.DeclinedBeforeEffect, outcome.Kind);
                    Assert.Null(outcome.Acknowledgment);
                    Assert.True(observation.OriginalClose!.IsCompletedSuccessfully);
                    Assert.Equal(before, await graph.SelectedRaw(definitions[1].Id));
                    var saved = await rig.Keep(repository.GetAllAsync(rig.Token));
                    Assert.Equal(definitions.Length, saved.Count);
                    foreach (var original in definitions)
                        Assert.Equal(original, Assert.Single(saved, value => value.Id == original.Id));
                    var pageClose = rig.Keep(page.CloseAndDrainAsync()); await pageClose;
                    Assert.Same(pageClose, page.OriginalClose); Assert.Null(page.Content);
                    Assert.True(page.OriginalBindingsClose!.IsCompletedSuccessfully);
                    Assert.True(page.OriginalLoaderIdle!.IsCompletedSuccessfully);
                    Assert.True(page.OriginalLoaderDispose!.IsCompletedSuccessfully);
                    Assert.True(page.OriginalLoaderTerminal!.IsCompletedSuccessfully);
                    // The actual business driver stays with the process writer.
                    // Its exact known-noSQL proof must permit the WHOLE original
                    // process and Home cohort to close, not just this delivery.
                    var processClose = rig.Keep(graph.Writer.CloseAndDrainOriginalAsync()); await processClose;
                    Assert.Same(processClose, graph.Writer.OriginalClose);
                    Assert.Same(processClose, graph.Writer.CloseAndDrainOriginalAsync());
                    var homeClose = rig.Keep(graph.Writes.CloseAndDrainOriginalAsync()); await homeClose;
                    Assert.Same(homeClose, graph.Writes.OriginalClose);
                    Assert.Same(homeClose, graph.Writes.CloseAndDrainOriginalAsync());
                });
            });
        });
}
#endif
