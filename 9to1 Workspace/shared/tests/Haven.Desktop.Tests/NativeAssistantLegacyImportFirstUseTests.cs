#if !ANDROID
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Migration.Tests;

public sealed partial class LegacySavedAgentMigrationTests
{
    [AvaloniaFact]
    public Task Rendered_first_use_import_requires_Home_decision_and_explicit_kind_then_reopens_the_same_canonical_identity() => RunImportAsync(async rig =>
    {
        var before = await rig.CaptureLegacyAsync(); AssistantIdentity? identity = null;
        await WithActualImportSurface(rig, async (window, surface, controller) =>
        {
            await ClickImportControl(window, surface, "Review legacy definitions", () =>
                surface.MigrationBindings.TryGetValue("CanRequestLegacyImport", out var value) && value is true);
            Assert.Equal(nameof(LegacyAgentImportState.RequiresReview), ImportValue(surface, "LegacyImportState"));
            Assert.Empty(controller.Snapshot.Assistants);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), value => value.IsEffectivelyVisible && value.Text == "Fictional migration helper");
            await ClickImportControl(window, surface, "Request Home import review", () =>
                Equals(ImportValue(surface, "LegacyImportState"), nameof(LegacyAgentImportState.PendingApproval)) &&
                surface.MigrationBindings.IsActionAvailable("assistants.legacy.import.refresh") == true);
            var pendingRequest = Assert.IsType<string>(ImportValue(surface, "LegacyImportRequest"));
            Assert.NotEmpty(pendingRequest); Assert.Equal(false, ImportValue(surface, "CanCompleteLegacyImport"));
            Assert.False(VisibleImportButton(window, "Complete approved import").IsEnabled);
            Assert.Equal(before, await rig.CaptureLegacyAsync());
            var request = Assert.Single((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                value => value.RequestId == pendingRequest);
            Assert.Equal("home.profile.importStore", request.Scope.ActionName);
            Assert.Equal(HomePermissionRequestState.PendingApproval, request.State); Assert.True(request.Policy.RequiresPerActionApproval);
            // Actual Home manual decision; there is no native migration approval shortcut.
            Assert.True((await rig.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept,
                cancellationToken: Token)).Succeeded);
            await ClickImportControl(window, surface, "Check Home decision", () =>
                surface.MigrationBindings.IsActionAvailable("assistants.legacy.import.complete") == true);
            Assert.Equal(pendingRequest, ImportValue(surface, "LegacyImportRequest"));
            await ClickImportControl(window, surface, "Complete approved import", () =>
                Equals(ImportValue(surface, "LegacyImportState"), nameof(LegacyAgentImportState.Imported)) &&
                surface.MigrationBindings.IsActionAvailable("assistants.legacy.preview") == true &&
                surface.MigrationBindings.TryGetValue("MigrationRows", out var rows) && rows is IReadOnlyList<AssistantsLegacyMigrationCuiBindings.LegacyRow> { Count: 2 });
            Assert.True(surface.MigrationBindings.TryGetValue("MigrationRows", out var observed));
            var rows = Assert.IsAssignableFrom<IReadOnlyList<AssistantsLegacyMigrationCuiBindings.LegacyRow>>(observed);
            var selected = Assert.Single(rows, row => row.Id == rig.First);
            var buttons = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsEffectivelyVisible && button.Content as string == "Review definition").ToArray();
            Assert.Equal(rows.Count, buttons.Length);
            var index = rows.Select((row, position) => (row, position)).Single(value => ReferenceEquals(value.row, selected)).position;
            await ClickImportControl(window, surface, buttons[index], () =>
                Equals(ImportValue(surface, "MigrationName"), "Fictional migration helper") &&
                surface.MigrationBindings.IsActionAvailable("assistants.legacy.kind.assistant") == true);
            Assert.Equal("Choose a type", ImportValue(surface, "MigrationKind"));
            Assert.False(VisibleImportButton(window, "Confirm conversion").IsEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible &&
                text.Text is { } value && value.Contains("1 run records", StringComparison.Ordinal));
            await ClickImportControl(window, surface, "Choose Assistant", () =>
                surface.MigrationBindings.IsActionAvailable("assistants.legacy.confirm") == true);
            await ClickImportControl(window, surface, "Confirm conversion", () =>
                controller.Snapshot.Assistants.Any(definition => definition.Identity.DefinitionId == rig.First.ToString("D")) &&
                surface.MigrationBindings.IsActionAvailable("assistants.legacy.result.open") == true);
            var result = Assert.Single(controller.Snapshot.Assistants, definition => definition.Identity.DefinitionId == rig.First.ToString("D")); identity = result.Identity;
            Assert.Equal(rig.First.ToString("D"), identity.DefinitionId);
            Assert.Equal(ConfiguredIdentityKind.Assistant, result.Kind);
            Assert.Equal(before, await rig.CaptureLegacyAsync());
            await ClickImportControl(window, surface, "Open Assistant", () => controller.Snapshot.SelectedAssistant?.Identity == identity &&
                surface.Bindings.TryGetValue("ShowLegacyMigration", out var visible) && visible is false);
            Assert.Equal(identity, controller.Snapshot.SelectedAssistant!.Identity);
            Assert.Equal("Original role and instructions", controller.Snapshot.SelectedAssistant.Configuration.Instructions);
        });
        await rig.ReopenAsync();
        Assert.True((await rig.Controller.InspectImportAsync(Token)).CanBrowse);
        var persisted = Assert.Single((await rig.Bridge.ListAsync(Token)).Definitions);
        Assert.Equal(identity, persisted.Identity);
        Assert.Equal(LegacyAgentMigrationState.Unselected, (await rig.Controller.PreviewAsync(rig.Second, Token)).State);
        Assert.Equal(before, await rig.CaptureLegacyAsync());
    });

    private static async Task WithActualImportSurface(Rig rig,
        Func<Window, AssistantsNativeCuiSurface, AssistantsWorkspaceController, Task> body)
    {
        var controller = new AssistantsWorkspaceController(rig.Bridge);
        var window = new Window(); window.Show(); AssistantsNativeCuiSurface? surface = null; var failures = new List<Exception>();
        var originalSceneCloses = new List<Task>();
        try
        {
            surface = new(controller, new ImportFixtureReadiness(), captureOriginalOwner: original => surface = original,
                migration: rig.Controller);
            window.Content = surface; await surface.InitializeAsync(Token);
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            await body(window, surface, controller);
            Assert.True(await surface.PrepareToCloseAsync(Token));
        }
        catch (Exception cause) { failures.Add(cause); }
        finally
        {
            if (surface is not null)
            {
                surface.RequestRetirement();
                try { await surface.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            }
            // A retained dirty draft can stop native close before child scene drainage.
            // Join these SAME fixture-owned loaders independently so their original
            // dispatcher/observer causes cannot be hidden by a safe CUI status banner.
            // This neither discards the draft nor acknowledges the failed native close.
            foreach (var scene in window.GetVisualDescendants().OfType<CuiSceneHost>().ToArray())
            {
                Task? actualClose = null;
                try
                {
                    actualClose = scene.CloseOriginalAsync(); originalSceneCloses.Add(actualClose);
                    await actualClose;
                }
                catch (Exception cause) { failures.Add(actualClose?.Exception ?? cause); }
            }
            try { await rig.Controller.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            try { await controller.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            if (surface?.OriginalClose?.IsCompletedSuccessfully == true && controller.OriginalClose?.IsCompletedSuccessfully == true)
                window.Close();
        }
        if (failures.Count != 0) throw new AggregateException("Actual native import source/scene originals retained.", failures);
    }
    private static object? ImportValue(AssistantsNativeCuiSurface surface, string name)
    { Assert.True(surface.MigrationBindings.TryGetValue(name, out var value)); return value; }
    private static Button VisibleImportButton(Window window, string label) => Assert.Single(window.GetVisualDescendants().OfType<Button>(),
        button => button.IsEffectivelyVisible && button.Content as string == label);
    private static Task ClickImportControl(Window window, AssistantsNativeCuiSurface surface, string label, Func<bool> settled) =>
        ClickImportControl(window, surface, VisibleImportButton(window, label), settled);
    private static async Task ClickImportControl(Window window, AssistantsNativeCuiSurface surface, Button button, Func<bool> settled)
    {
        Assert.True(button.IsEnabled);
        // The entry action belongs to the Home scene's main bindings, while the
        // review actions belong to the child migration scene. Observe the SAME
        // loaded root/context rather than inferring ownership from command text.
        var host = button.GetVisualAncestors().OfType<CuiSceneHost>().First();
        var root = Assert.IsAssignableFrom<Control>(host.Content);
        var context = root.DataContext;
        Assert.True(ReferenceEquals(context, surface.Bindings) || ReferenceEquals(context, surface.MigrationBindings),
            "The rendered import control must belong to this retained native view.");
        var availability = Assert.IsAssignableFrom<ICuiActionAvailability>(context);
        var authored = AssistantsCuiScenes.ReadDocument(ReferenceEquals(context, surface.MigrationBindings)
            ? AssistantsCuiScene.LegacyMigration : AssistantsCuiScene.Home);
        var reference = Assert.IsType<string>(button.Tag);
        // Match the canonical loader's single document-local alias resolution;
        // repeated ReviewLegacyDefinition retains its actual current row dispatch.
        var command = authored.Actions.TryGetValue(reference, out var action) ? action.Command : reference;
        Assert.True(availability.IsActionAvailable(command),
            "The rendered enabled control must match its actual scene owner and command: " + command);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            var failure = window.GetVisualDescendants().OfType<CuiSceneHost>().Select(host => host.LastActionFailure).FirstOrDefault(value => value is not null);
            Assert.Null(failure);
            if (settled()) return;
            if (surface.IsRetiring) throw new InvalidOperationException("The actual native import retired before its command settled.");
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The actual rendered import command did not publish its expected state.");
        // Final fixture close independently joins every actual loader/native/controller source,
        // so an observed intermediate UI state cannot hide a later source or cleanup failure.
    }
    private sealed class ImportFixtureReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "HeadlessImportSourceControl", "Fictional native fixture only; no installed-app qualification."));
    }
}
#endif
