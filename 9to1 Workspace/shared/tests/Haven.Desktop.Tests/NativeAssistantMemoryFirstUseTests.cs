#if !ANDROID
using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

// Links the maintained genuine Memory Rig into the existing declared Desktop
// graph. No duplicate store/permission issuer, model call or installed-app proof.
namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Rendered_memory_checkbox_saves_current_membership_enables_first_write_and_survives_reopen() => RunAsync(async rig =>
    {
        AssistantIdentity? identity = null; Guid conversationId = Guid.Empty; Guid memoryId = Guid.Empty;
        await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
        {
            await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
            await FlushNativeMemoryUi(window);
            Assert.False(MemoryToggle(window, "Enable Assistant memory").IsChecked == true);
            Assert.False(surface.Bindings.Draft!.Configuration.Memory.Enabled);
            Assert.False(surface.Bindings.Draft.Configuration.Memory.IncludeProjectContext);
            Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional first-use helper"));
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            identity = controller.Snapshot.SelectedAssistant!.Identity;
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token);
            await FlushNativeMemoryUi(window);
            var oldBinding = controller.Snapshot.ConversationBinding!; conversationId = oldBinding.Conversation.Id;
            var beforeTables = await rig.CountMemoryTablesAsync();
            await surface.Bindings.DispatchAsync("assistants.memory.open", null, Token);
            Assert.False((await management.ReadAsync(oldBinding, Token)).IsAvailable);
            Assert.Equal(beforeTables, await rig.CountMemoryTablesAsync());
            Assert.False(surface.MemoryBindings.TrySetValue("MemoryDraftSummary", "Unenabled memory"));
            await surface.MemoryBindings.DispatchAsync("assistants.memory.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token);
            await FlushNativeMemoryUi(window);
            var generation = surface.PresentationGeneration;
            var toggle = MemoryToggle(window, "Enable Assistant memory"); Assert.True(toggle.IsEnabled);
            toggle.IsChecked = true;
            Assert.True(surface.Bindings.Draft!.Configuration.Memory.Enabled);
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            // No extra dispatcher pump: the save itself must publish the fresh binding.
            var fresh = surface.Bindings.Snapshot.ConversationBinding!;
            Assert.NotSame(oldBinding, fresh); Assert.Same(controller.Snapshot.ConversationBinding, fresh);
            Assert.Equal(conversationId, fresh.Conversation.Id); Assert.True(surface.PresentationGeneration > generation);
            Assert.Equal(identity, fresh.Definition.Identity); Assert.True(fresh.Definition.Configuration.Memory.Enabled);
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.memory.open", null, Token);
            Assert.True((await management.ReadAsync(fresh, Token)).IsAvailable);
            Assert.True(surface.MemoryBindings.TrySetValue("MemoryDraftTitle", "Examples"));
            Assert.True(surface.MemoryBindings.TrySetValue("MemoryDraftSummary", "Use short concrete examples"));
            await surface.MemoryBindings.DispatchAsync("assistants.memory.review", null, Token);
            var confirm = surface.MemoryBindings.DispatchAsync("assistants.memory.confirm", null, Token).AsTask();
            await DecideFirstUseMemory(rig, confirm);
            await confirm;
            var record = Assert.Single((await management.ReadAsync(fresh, Token)).Records); memoryId = record.Id;
            Assert.Equal("Use short concrete examples", record.Summary);
            var memoryBeforeDisable = await rig.CaptureMemoryAsync();
            await surface.MemoryBindings.DispatchAsync("assistants.memory.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token);
            await FlushNativeMemoryUi(window); MemoryToggle(window, "Enable Assistant memory").IsChecked = false;
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            var disabled = controller.Snapshot.ConversationBinding!;
            Assert.Equal(conversationId, disabled.Conversation.Id); Assert.Equal(identity, disabled.Definition.Identity);
            Assert.False((await management.ReadAsync(disabled, Token)).IsAvailable);
            Assert.Equal(memoryBeforeDisable, await rig.CaptureMemoryAsync());
        });
        await rig.ReopenAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
        {
            await OpenActualSavedMemoryConversation(surface, identity!, conversationId, window);
            Assert.False(controller.Snapshot.SelectedAssistant!.Configuration.Memory.Enabled);
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token);
            await FlushNativeMemoryUi(window); Assert.False(MemoryToggle(window, "Enable Assistant memory").IsChecked == true);
            MemoryToggle(window, "Enable Assistant memory").IsChecked = true;
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.memory.open", null, Token);
            var binding = controller.Snapshot.ConversationBinding!;
            Assert.Equal(identity, binding.Definition.Identity); Assert.Equal(conversationId, binding.Conversation.Id);
            Assert.Equal(memoryId, Assert.Single((await management.ReadAsync(binding, Token)).Records).Id);
            Assert.Single(controller.Snapshot.Conversations);
        });
    });

    [AvaloniaFact]
    public Task Project_context_preference_is_editable_persisted_and_refused_without_a_project_memory_source() => RunAsync(async rig =>
    {
        AssistantIdentity? identity = null; Guid conversationId = Guid.Empty;
        await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
        {
            await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
            Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional project preference"));
            await FlushNativeMemoryUi(window);
            MemoryToggle(window, "Enable Assistant memory").IsChecked = true;
            MemoryToggle(window, "Request project memory context").IsChecked = true;
            Assert.True(surface.Bindings.Draft!.Configuration.Memory.IncludeProjectContext);
            Assert.True(surface.Bindings.TryGetValue("MemoryPreferenceStatus", out var status));
            Assert.Contains("unavailable", Assert.IsType<string>(status));
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            identity = controller.Snapshot.SelectedAssistant!.Identity;
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token); await FlushNativeMemoryUi(window);
            var binding = controller.Snapshot.ConversationBinding!; conversationId = binding.Conversation.Id;
            var callbacks = 0;
            var input = await rig.Memory.PrepareOriginalAssistantMemoryInputWithinSourceAsync(binding, binding.Definition,
                body => { callbacks++; body(); }, rig.Retain, Token);
            Assert.False(input.IsPrepared); Assert.Contains("Project", input.Reason); Assert.Equal(0, callbacks);
            Assert.False((await management.ReadAsync(binding, Token)).IsAvailable);
        });
        await rig.ReopenAsync();
        await WithActualMemorySurface(rig, async (window, surface, controller, _) =>
        {
            await OpenActualSavedMemoryConversation(surface, identity!, conversationId, window);
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token); await FlushNativeMemoryUi(window);
            Assert.True(MemoryToggle(window, "Enable Assistant memory").IsChecked == true);
            Assert.True(MemoryToggle(window, "Request project memory context").IsChecked == true);
            MemoryToggle(window, "Request project memory context").IsChecked = false;
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            Assert.True(controller.Snapshot.SelectedAssistant!.Configuration.Memory.Enabled);
            Assert.False(controller.Snapshot.SelectedAssistant.Configuration.Memory.IncludeProjectContext);
        });
    });

    [AvaloniaFact]
    public Task Saving_native_memory_preference_does_not_turn_a_catalogue_import_into_memory_permission() => RunAsync(async rig =>
    {
        await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
        {
            await surface.Bindings.DispatchAsync("assistants.create.start", null, Token);
            Assert.True(surface.Bindings.TrySetValue("DraftName", "Fictional ungranted preference"));
            await FlushNativeMemoryUi(window); MemoryToggle(window, "Enable Assistant memory").IsChecked = true;
            await surface.Bindings.DispatchAsync("assistants.configuration.save", null, Token);
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await surface.Bindings.DispatchAsync("assistants.conversation.new", null, Token); await FlushNativeMemoryUi(window);
            await surface.Bindings.DispatchAsync("assistants.memory.open", null, Token);
            var view = await management.ReadAsync(controller.Snapshot.ConversationBinding!, Token);
            Assert.False(view.IsAvailable); Assert.Contains("Home", view.Reason);
            Assert.False(surface.MemoryBindings.TrySetValue("MemoryDraftSummary", "This preference grants no access"));
            var permissions = await rig.Permissions.GetSnapshotAsync(cancellationToken: Token);
            Assert.DoesNotContain(permissions.PendingRequests, request => request.Scope.ActionName == HomeCanonicalAssistantMemoryWriteSource.WriteAction);
        });
    }, importMemory: false);

    private static async Task WithActualMemorySurface(Rig rig,
        Func<Window, AssistantsNativeCuiSurface, AssistantsWorkspaceController, AssistantMemoryManagementController, Task> body)
    {
        var controller = new AssistantsWorkspaceController(rig.Bridge);
        var management = new AssistantMemoryManagementController(rig.Memory, rig.Bridge);
        var window = new Window(); window.Show(); AssistantsNativeCuiSurface? surface = null; var errors = new List<Exception>();
        try
        {
            surface = new(controller, new MemoryFixtureReadiness(), captureOriginalOwner: original => surface = original, memoryManagement: management);
            window.Content = surface; await surface.InitializeAsync(Token); await FlushNativeMemoryUi(window);
            await body(window, surface, controller, management);
            Assert.True(await surface.PrepareToCloseAsync(Token));
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            // Fixture teardown retains every failure; it never reports an unknown close
            // as success or mutates/deletes the retained SQLite/Den history.
            if (surface is not null)
            {
                surface.RequestRetirement();
                try { await surface.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            }
            try { await management.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { await controller.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (surface?.OriginalClose?.IsCompletedSuccessfully == true && controller.OriginalClose?.IsCompletedSuccessfully == true) window.Close();
        }
        if (errors.Count != 0) throw new AggregateException("Genuine native first-use sources and fixtures retained.", errors);
    }
    private static async Task OpenActualSavedMemoryConversation(AssistantsNativeCuiSurface surface, AssistantIdentity identity, Guid id, Window window)
    {
        Assert.True(surface.Bindings.TryGetValue("Assistants", out var observed));
        var row = Assert.IsType<AssistantsCuiBindings.AssistantRow[]>(observed).Single(value => value.Identity == identity);
        await surface.Bindings.DispatchAsync("assistants.open", row, Token); await FlushNativeMemoryUi(window);
        Assert.True(surface.Bindings.TryGetValue("SelectedConversations", out observed));
        var conversation = Assert.IsType<AssistantsCuiBindings.ConversationRow[]>(observed).Single(value => value.Id == id);
        await surface.Bindings.DispatchAsync("assistants.conversation.open", conversation, Token); await FlushNativeMemoryUi(window);
    }
    private static CheckBox MemoryToggle(Window window, string name) => window.GetVisualDescendants().OfType<CheckBox>()
        .Single(value => AutomationProperties.GetName(value) == name);
    private static async Task FlushNativeMemoryUi(Window window) =>
        await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Background);
    private static async Task DecideFirstUseMemory(Rig rig, Task actual)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(15))
        {
            var snapshot = await rig.Permissions.GetSnapshotAsync(cancellationToken: Token);
            var requests = snapshot.PendingRequests.Where(request => request.Scope.ActionName == HomeCanonicalAssistantMemoryWriteSource.WriteAction).ToArray();
            if (requests.Length != 0)
            {
                var request = Assert.Single(requests); Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                Assert.True(request.Policy.RequiresPerActionApproval);
                Assert.True((await rig.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded); return;
            }
            if (actual.IsCompleted) { await actual; throw new InvalidOperationException("The native memory write finished without its actual Home review."); }
            await Task.Delay(10, Token);
        }
        throw new TimeoutException("The native first-use Home write review was not observed.");
    }
    private sealed class MemoryFixtureReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => ValueTask.FromResult(
            new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "HeadlessSourceControl", "Local native fixture only; no installed-app qualification."));
    }
}
#endif
