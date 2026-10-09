#if !ANDROID
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using HavenOS.Apps.Assistants.NativeUI;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    private static readonly List<object[]> FailedGeneratedUiOwners = [];

    [AvaloniaFact]
    public Task Saved_canonical_message_opens_shared_generated_surface_routes_local_action_and_reopens_same_payload() => RunAsync(async rig =>
    {
        const string payload = """
            # Fictional local exercise

            This is an explicitly supplied user declaration, not a provider response.

            ```haven-ui
            {"version":1,"template":"custom","title":"Fictional exercise","components":[{"id":"answer","type":"HavenText","props":{"text":"Before local interaction"}},{"id":"answer-button","type":"HavenButton","props":{"label":"Show fictional answer"},"actions":[{"id":"answer.show","patches":[{"target":"answer","path":"text","value":"Fictional local answer"}]}]}]}
            ```
            """;
        var binding = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional generated UI helper" });
        var message = new ChatMessage(Guid.NewGuid(), binding.Conversation.Id, MessageRole.User, payload,
            null, null, null, DateTimeOffset.UtcNow);
        var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write;
        await WithOriginalGeneratedUiSurface(rig, async (window, surface, controller, instances) =>
        {
            await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
            await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
            var generated = Assert.Single(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
            var actualDocument = generated.Document!;
            Assert.Equal(binding.Conversation.Id, actualDocument.Origin.ThreadId);
            Assert.Equal("assistants", actualDocument.Origin.AppKey);
            Assert.DoesNotContain("```haven-ui", Assert.Single(window.GetVisualDescendants().OfType<CakeOS.Cui.Runtime.CuiMarkdownView>()).Text);
            var button = Assert.Single(generated.GetVisualDescendants().OfType<Button>(), item => Equals(item.Content, "Show fictional answer"));
            var completion = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            generated.ActionCompleted += (_, actual) => completion.TrySetResult(actual);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.Equal(GenUiActionStatus.Completed, result.Status); Assert.Equal(actualDocument.Origin, result.Origin);
            await FlushNativeMemoryUi(window);
            Assert.Contains(generated.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Fictional local answer");
            Assert.Equal(actualDocument.Origin, instances.TryGet(actualDocument.Origin.InstanceId)!.Origin);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("Interaction state lasts for this view", StringComparison.Ordinal) == true);
            var actualConversation = Assert.IsType<AssistantConversationData>(controller.Snapshot.Conversation);
            Assert.Equal(message, Assert.Single(actualConversation.Messages));
            Assert.Null(actualConversation.CanonicalTask); Assert.Empty(controller.Snapshot.Models);
            Assert.False(surface.HasUnsavedChanges);
        });
        await rig.ReopenAsync();
        await WithOriginalGeneratedUiSurface(rig, async (window, surface, controller, _) =>
        {
            await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
            await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<GenerativeUiSurface>().Any());
            var generated = Assert.Single(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
            Assert.Contains(generated.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Before local interaction");
            Assert.Equal(binding.Conversation.Id, generated.Document!.Origin.ThreadId);
            var actualConversation = Assert.IsType<AssistantConversationData>(controller.Snapshot.Conversation);
            Assert.Equal(message, Assert.Single(actualConversation.Messages));
            Assert.Null(actualConversation.CanonicalTask); Assert.Empty(controller.Snapshot.Models);
        });
    });

    [AvaloniaFact]
    public Task Invalid_saved_generated_declaration_keeps_actual_message_and_can_close_without_a_surface() => RunAsync(async rig =>
    {
        const string payload = "Usable fictional explanation.\n```haven-ui\n{\"version\":1,\"template\":\"unknown-template\",\"inputs\":{}}\n```";
        var binding = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional invalid UI helper" });
        var message = new ChatMessage(Guid.NewGuid(), binding.Conversation.Id, MessageRole.User, payload,
            null, null, null, DateTimeOffset.UtcNow);
        var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write;
        await WithOriginalGeneratedUiSurface(rig, async (window, surface, controller, instances) =>
        {
            await OpenActualSavedMemoryConversation(surface, binding.Definition.Identity, binding.Conversation.Id, window);
            await AwaitOriginalGeneratedTree(window, surface, () => window.GetVisualDescendants().OfType<TextBlock>().Any(text =>
                text.Text?.StartsWith("Generated UI was not opened:", StringComparison.Ordinal) == true));
            Assert.Empty(window.GetVisualDescendants().OfType<GenerativeUiSurface>());
            Assert.Empty(instances.GetForThread(binding.Conversation.Id));
            Assert.Equal(payload, Assert.Single(window.GetVisualDescendants().OfType<CakeOS.Cui.Runtime.CuiMarkdownView>()).Text);
            Assert.Equal(message, Assert.Single(controller.Snapshot.Conversation!.Messages));
            Assert.False(surface.HasUnsavedChanges);
        });
    });

    [AvaloniaFact]
    public async Task Actual_panel_callback_fault_retains_source_graph_and_healthy_child_registration_on_host_close()
    {
        var rig = new Rig();
        AssistantsWorkspaceController? controller = null; OriginalAssistantGeneratedUiHost? host = null;
        GenUiInstanceStore? instances = null; GenUiLocalActionRegistry? local = null;
        Window? window = null; IAssistantGeneratedUiMount? mount = null;
        var raws = new List<Task>(); var causes = new List<Exception>();
        try
        {
            await rig.InitializeAsync(true);
            var binding = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional failed publication witness" });
            var message = new ChatMessage(Guid.NewGuid(), binding.Conversation.Id, MessageRole.User, GeneratedCustodyPayload,
                null, null, null, DateTimeOffset.UtcNow);
            var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write;
            controller = new(rig.Bridge);
            await controller.InitializeAsync(Token); await controller.OpenAssistantAsync(binding.Definition.Identity, Token);
            var opened = await controller.OpenConversationAsync(binding.Conversation.Id, Token);
            var actualBinding = Assert.IsType<AssistantConversationBinding>(opened.ConversationBinding);
            var projection = new AssistantConversationPresentation();
            _ = projection.Bind(Assert.IsType<AssistantConversationData>(opened.Conversation));
            var presented = Assert.Single(projection.Messages);
            instances = new(); local = new();
            var custom = new CustomTemplateRuntime(local, instances);
            var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), instances);
            host = new(controller, router, instances, new(local, instances), new(local, instances), new(local), custom);
            window = new() { Width = 1000, Height = 720 }; window.Show();
            var cancellation = new OperationCanceledException("actual panel publication");
            var io = new IOException("actual panel publication sibling"); var originalFailure = new AggregateException(cancellation, io);
            var create = host.CreateOriginalMessageAsync(actualBinding, presented, () => true, actual =>
            {
                mount = actual;
                var panel = Assert.IsType<StackPanel>(actual.View);
                ((System.Collections.Specialized.INotifyCollectionChanged)panel.Children).CollectionChanged += (_, _) => throw originalFailure;
            }, Token);
            raws.Add(create);
            var failure = await Record.ExceptionAsync(() => create.WaitAsync(TimeSpan.FromSeconds(15), Token));
            Assert.NotNull(failure); Assert.True(create.IsFaulted); Assert.False(create.IsCanceled);
            causes.Add(create.Exception!);
            var panel = Assert.IsType<StackPanel>(Assert.IsAssignableFrom<IAssistantGeneratedUiMount>(mount).View);
            var child = Assert.Single(panel.Children.OfType<GenerativeUiSurface>());
            var document = Assert.IsType<GenUiDocument>(child.Document);
            var action = GeneratedCustodyComponents(document.Root).SelectMany(component => component.Actions).Single();
            Assert.True(local.CanHandle(action.TargetKey)); Assert.Same(document, instances.TryGet(document.Origin.InstanceId));
            var childClose = child.CloseAndDrainAsync(); raws.Add(childClose); await childClose.WaitAsync(TimeSpan.FromSeconds(15), Token);
            Assert.True(childClose.IsCompletedSuccessfully);
            var cohort = Assert.Single(Assert.IsType<List<CloudflareOriginalTaskLedger>>(typeof(OriginalAssistantGeneratedUiHost)
                .GetField("_sourceCohorts", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(host)));
            var originalSources = cohort.OriginalTasks.ToArray(); Assert.NotEmpty(originalSources);
            await cohort.ObserveAllOriginalTasksAsync();
            Assert.Contains(cohort.OriginalErrors, cause => ReferenceEquals(cause, originalFailure));
            causes.AddRange(cohort.OriginalErrors);
            var close = host.CloseAndDrainAsync(); raws.Add(close);
            Assert.NotNull(await Record.ExceptionAsync(() => close.WaitAsync(TimeSpan.FromSeconds(15), Token)));
            Assert.Same(close, host.CloseAndDrainAsync()); Assert.True(close.IsFaulted); Assert.False(close.IsCanceled);
            AssertKnownGeneratedCauseGraph(close.Exception!, causes);
            Assert.True(childClose.IsCompletedSuccessfully);
            Assert.All(originalSources, actual => Assert.True(actual.IsCompletedSuccessfully));
            Assert.True(local.CanHandle(action.TargetKey)); Assert.Same(document, instances.TryGet(document.Origin.InstanceId));
            Assert.Equal(message, Assert.Single(controller.Snapshot.Conversation!.Messages));
        }
        finally
        {
            // This intentional unknown owner keeps its actual source/store/Den
            // dependencies. No failed host receipt authorizes controller teardown.
            lock (FailedGeneratedUiOwners) FailedGeneratedUiOwners.Add([rig, controller!, host!, instances!, local!, window!, mount!, raws, causes]);
        }
    }

    private const string GeneratedCustodyPayload = """
        Explicitly fictional user declaration for local source custody.
        ```haven-ui
        {"version":1,"template":"custom","components":[{"id":"witness","type":"HavenButton","props":{"label":"Actual local witness"},"actions":[{"id":"witness.local","patches":[]}]}]}
        ```
        """;
    private static IEnumerable<GenUiComponent> GeneratedCustodyComponents(GenUiComponent component)
    { yield return component; foreach (var child in component.Children) foreach (var nested in GeneratedCustodyComponents(child)) yield return nested; }
    private static void AssertKnownGeneratedCauseGraph(Exception actual, IEnumerable<Exception> independentlyObserved)
    {
        var known = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        void Observe(Exception cause)
        { if (!known.Add(cause)) return; if (cause is AggregateException group) foreach (var child in group.InnerExceptions) Observe(child); }
        foreach (var cause in independentlyObserved) Observe(cause);
        void Demand(Exception cause)
        {
            if (known.Contains(cause)) return;
            var group = Assert.IsType<AggregateException>(cause); Assert.NotEmpty(group.InnerExceptions);
            foreach (var child in group.InnerExceptions) Demand(child);
        }
        Demand(actual);
    }

    private static async Task WithOriginalGeneratedUiSurface(Rig rig,
        Func<Window, AssistantsNativeCuiSurface, AssistantsWorkspaceController, GenUiInstanceStore, Task> body)
    {
        var controller = new AssistantsWorkspaceController(rig.Bridge);
        var instances = new GenUiInstanceStore(); var local = new GenUiLocalActionRegistry();
        var calculator = new CalculatorTemplateRuntime(local, instances);
        var checklist = new ChecklistTemplateRuntime(local, instances); var grid = new DataGridTemplateRuntime(local);
        var custom = new CustomTemplateRuntime(local, instances);
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), instances);
        var host = new OriginalAssistantGeneratedUiHost(controller, router, instances, calculator, checklist, grid, custom);
        var window = new Window { Width = 1320, Height = 960 }; window.Show();
        AssistantsNativeCuiSurface? surface = null; var errors = new List<Exception>(); var rawCloses = new List<Task>();
        try
        {
            surface = new(controller, new MemoryFixtureReadiness(), captureOriginalOwner: original => surface = original);
            surface.BindOriginalGeneratedUiHost(host); window.Content = surface;
            await surface.InitializeAsync(Token); await FlushNativeMemoryUi(window);
            await body(window, surface, controller, instances);
            Assert.True(await surface.PrepareToCloseAsync(Token));
        }
        catch (Exception cause) { errors.Add(cause); }
        if (surface is not null)
        {
            try { surface.RequestRetirement(); } catch (Exception cause) { errors.Add(cause); }
            Task? raw = null;
            try { raw = surface.CloseAndDrainAsync(); rawCloses.Add(raw); await raw; }
            catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
        }
        Task? hostClose = null;
        try { hostClose = host.CloseAndDrainAsync(); rawCloses.Add(hostClose); await hostClose; }
        catch (Exception cause) { errors.Add(hostClose?.Exception ?? cause); }
        if (hostClose?.IsCompletedSuccessfully == true && surface?.OriginalClose?.IsCompletedSuccessfully == true)
        {
            Task? raw = null;
            try { raw = controller.CloseAndDrainAsync(); rawCloses.Add(raw); await raw; }
            catch (Exception cause) { errors.Add(raw?.Exception ?? cause); }
        }
        if (errors.Count == 0) window.Close();
        else lock (FailedGeneratedUiOwners) FailedGeneratedUiOwners.Add([rig, controller, host, instances, router, window, surface!, rawCloses, errors]);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual generated UI fixture retained all original owners and closes.", errors);
    }

    private static async Task AwaitOriginalGeneratedTree(Window window, AssistantsNativeCuiSurface surface, Func<bool> observed)
    {
        var until = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (!observed())
        {
            foreach (var actual in surface.OriginalGeneratedUiRenderTasks)
                if (actual.IsCompleted) await actual; // Surface the SAME original source failure before a tree timeout.
            if (surface.OriginalClose is { IsCompleted: true } close) await close;
            if (DateTimeOffset.UtcNow >= until) throw new TimeoutException("The actual generated source did not publish its tree.");
            await FlushNativeMemoryUi(window); await Task.Delay(10, Token);
        }
        foreach (var actual in surface.OriginalGeneratedUiRenderTasks) await actual;
        await FlushNativeMemoryUi(window);
    }
}
#endif
