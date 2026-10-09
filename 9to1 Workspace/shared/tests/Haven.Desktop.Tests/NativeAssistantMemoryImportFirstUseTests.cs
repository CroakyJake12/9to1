#if !ANDROID
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Rendered_memory_store_import_requires_separate_Home_decision_before_first_write_and_reopens() => RunAsync(async rig =>
    {
        AssistantIdentity? identity = null; Guid conversation = Guid.Empty; Guid recordId = Guid.Empty;
        var before = await rig.CaptureMemoryAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
        {
            await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
            Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional memory import helper"));
            await FlushNativeMemoryUi(window); MemoryToggle(window, "Enable Assistant memory").IsChecked = true;
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            identity = controller.Snapshot.SelectedAssistant!.Identity;
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
            conversation = controller.Snapshot.ConversationBinding!.Conversation.Id;
            await surface.Bindings.DispatchAsync("assistants.memory.open", null, Token); await FlushNativeMemoryUi(window);
            Assert.Equal(nameof(AssistantMemoryImportState.RequiresReview), MemoryImportValue(surface, "MemoryImportState"));
            Assert.False((await management.ReadAsync(controller.Snapshot.ConversationBinding!, Token)).IsAvailable);
            Assert.False(surface.MemoryBindings.TrySetValue("MemoryDraftTitle", "No grant yet"));
            Assert.Equal(before, await rig.CaptureMemoryAsync());
            await ClickMemoryImportControl(window, surface, "Request memory store review", () =>
                Equals(MemoryImportValue(surface, "MemoryImportState"), nameof(AssistantMemoryImportState.PendingApproval)) &&
                surface.MemoryBindings.IsActionAvailable("assistants.memory.import.refresh") == true);
            var requestId = Assert.IsType<string>(MemoryImportValue(surface, "MemoryImportRequest")); Assert.NotEmpty(requestId);
            Assert.False(MemoryImportButton(window, "Complete approved memory import").IsEnabled);
            var request = Assert.Single((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                value => value.RequestId == requestId);
            Assert.Equal("home.profile.importStore", request.Scope.ActionName);
            Assert.True(request.Policy.RequiresPerActionApproval); Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
            Assert.Equal(before, await rig.CaptureMemoryAsync());
            // The UI requests review; only the actual Home decision can accept it.
            Assert.True((await rig.Permissions.DecideAsync(requestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
            await ClickMemoryImportControl(window, surface, "Check memory store decision", () =>
                surface.MemoryBindings.IsActionAvailable("assistants.memory.import.complete") == true);
            Assert.Equal(requestId, MemoryImportValue(surface, "MemoryImportRequest"));
            await ClickMemoryImportControl(window, surface, "Complete approved memory import", () =>
                Equals(MemoryImportValue(surface, "MemoryImportState"), nameof(AssistantMemoryImportState.Imported)) &&
                MemoryImportValue(surface, "CanEditMemoryDraft") is true);
            Assert.Equal(before, await rig.CaptureMemoryAsync()); // No READ-time schema repair or seeding.
            Assert.True(surface.MemoryBindings.TrySetValue("MemoryDraftTitle", "Examples"));
            Assert.True(surface.MemoryBindings.TrySetValue("MemoryDraftSummary", "Use short examples"));
            await surface.MemoryBindings.DispatchAsync("assistants.memory.review", null, Token);
            var actualWrite = surface.MemoryBindings.DispatchAsync("assistants.memory.confirm", null, Token).AsTask();
            await DecideFirstUseMemory(rig, actualWrite); await actualWrite; // Distinct actual per-record WRITE action.
            var record = Assert.Single((await management.ReadAsync(controller.Snapshot.ConversationBinding!, Token)).Records);
            recordId = record.Id; Assert.Equal("Use short examples", record.Summary);
        });
        await rig.ReopenAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
        {
            await OpenActualSavedMemoryConversation(surface, identity!, conversation, window);
            await surface.Bindings.DispatchAsync("assistants.memory.open", null, Token); await FlushNativeMemoryUi(window);
            Assert.Equal(nameof(AssistantMemoryImportState.Imported), MemoryImportValue(surface, "MemoryImportState"));
            Assert.False(MemoryImportButton(window, "Request memory store review").IsEnabled);
            Assert.Equal(recordId, Assert.Single((await management.ReadAsync(controller.Snapshot.ConversationBinding!, Token)).Records).Id);
            Assert.Equal(identity, controller.Snapshot.SelectedAssistant!.Identity);
        });
    }, importMemory: false, bindMemoryImport: true);

    [Fact]
    public Task Memory_import_pending_request_survives_presentation_reopen_and_foreign_preview_cannot_replay_it() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(); var before = await rig.CaptureMemoryAsync();
        var first = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var second = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var failures = new List<Exception>();
        try
        {
            var preview = await first.InspectImportAsync(binding, Token); Assert.True(preview.CanRequest);
            var pending = await first.RequestImportAsync(preview, Token); Assert.Equal(AssistantMemoryImportState.PendingApproval, pending.State);
            await first.CloseAndDrainAsync();
            var reopened = await second.InspectImportAsync(binding, Token); Assert.Equal(pending.RequestId, reopened.RequestId);
            var foreign = await second.RequestImportAsync(pending, Token); Assert.Equal(AssistantMemoryImportState.Unavailable, foreign.State);
            var current = await second.InspectImportAsync(binding, Token); Assert.Equal(pending.RequestId, current.RequestId);
            var request = Assert.Single((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
                value => value.RequestId == pending.RequestId);
            Assert.True((await rig.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Decline, cancellationToken: Token)).Succeeded);
            var declined = await second.RefreshImportAsync(current, Token); Assert.Equal(AssistantMemoryImportState.Declined, declined.State);
            Assert.False((await second.ReadAsync(binding, Token)).IsAvailable);
            var retried = await second.RequestImportAsync(declined, Token);
            Assert.Equal(AssistantMemoryImportState.PendingApproval, retried.State); Assert.NotEqual(pending.RequestId, retried.RequestId);
            Assert.Equal(before, await rig.CaptureMemoryAsync());
            Assert.True((await rig.Permissions.DecideAsync(retried.RequestId!, HomeApprovalChoice.Decline, cancellationToken: Token)).Succeeded);
            Assert.Equal(AssistantMemoryImportState.Declined, (await second.RefreshImportAsync(retried, Token)).State);
        }
        catch (Exception failure) { failures.Add(failure); }
        try { await first.CloseAndDrainAsync(); } catch (Exception failure) { failures.Add(failure); }
        try { await second.CloseAndDrainAsync(); } catch (Exception failure) { failures.Add(failure); }
        if (failures.Count != 0) throw new AggregateException("Actual memory import sources retained.", failures);
    }, importMemory: false, bindMemoryImport: true);

    [Fact]
    public Task Disabled_memory_import_inspection_performs_no_store_query_or_review_request() => RunAsync(async rig =>
    {
        var binding = await rig.CreateAsync(new() { Name = "Fictional disabled memory", Memory = new(false) });
        var before = await rig.CaptureMemoryAsync(); var callbacks = 0;
        var prepared = await rig.Memory.PrepareOriginalAssistantMemoryInputWithinSourceAsync(binding, binding.Definition,
            body => { callbacks++; body(); }, rig.Retain, Token);
        Assert.False(prepared.IsPrepared); Assert.Contains("disabled", prepared.Reason); Assert.Equal(0, callbacks);
        await using var owner = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        Assert.Equal(AssistantMemoryImportState.Unavailable, (await owner.InspectImportAsync(binding, Token)).State);
        Assert.DoesNotContain((await rig.Permissions.GetSnapshotAsync(cancellationToken: Token)).PendingRequests,
            request => request.State == HomePermissionRequestState.PendingApproval);
        Assert.Equal(before, await rig.CaptureMemoryAsync());
    }, importMemory: false, bindMemoryImport: true);

    private static object? MemoryImportValue(AssistantsNativeCuiSurface surface, string name)
    { Assert.True(surface.MemoryBindings.TryGetValue(name, out var value)); return value; }
    private static Button MemoryImportButton(Window window, string label) => Assert.Single(window.GetVisualDescendants().OfType<Button>(),
        button => button.IsEffectivelyVisible && button.Content as string == label);
    private static async Task ClickMemoryImportControl(Window window, AssistantsNativeCuiSurface surface, string label, Func<bool> settled)
    {
        var button = MemoryImportButton(window, label); Assert.True(button.IsEnabled); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
            Assert.Null(window.GetVisualDescendants().OfType<CuiSceneHost>().Select(host => host.LastActionFailure).FirstOrDefault(value => value is not null));
            if (settled()) return;
            Assert.False(surface.IsRetiring); await Task.Delay(10, Token);
        }
        throw new TimeoutException("The actual rendered memory import action did not settle.");
        // Independent native/source/process joins remain mandatory after intermediate UI publication.
    }
}
#endif
