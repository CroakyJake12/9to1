using System.Collections;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.HavenUI.Components;
using Haven.Desktop.HavenUI.Creative;
using Haven.Desktop.HavenUI.GenerativeUi;
using Haven.Desktop.Views.Pages.Chat;
using Haven.UI;
using HavenInput = Haven.UI.Components.Input;
using HavenText = Haven.UI.Components.Text;

namespace Haven.Desktop.Tests;

// Actual producer controls only. These do not configure a provider, actor,
// permission issuer, Tasks recovery grant or an installed native presentation.
public sealed class GenUiOriginalRetirementTests
{
    [AvaloniaFact]
    public async Task Actual_scene_agent_task_keeps_same_close_pending_and_direct_fault_siblings_retained()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var entered = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        HavenGenUiWhiteboard? board = null;
        Task? operation = null;
        Task? close = null;
        var first = new IOException("actual provider cause one");
        var second = new InvalidOperationException("actual provider cause two");
        try
        {
            board = new HavenGenUiWhiteboard(Component(), null, _ => { }, payload =>
            { entered.TrySetResult(payload); return provider.Task; });
            var input = Field<HavenInput>(board, "_agentInput");
            input.Text = "Retain the actual whiteboard operation";
            operation = board.SubmitInputAsync(input);
            var payload = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(input.Text, payload.GetProperty("instruction").GetString());
            Assert.True(payload.TryGetProperty("canvasState", out _));
            close = board.CloseAndDrainAsync();
            Assert.Same(close, board.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.False(board.OwnsInput(input));
            Assert.Throws<ObjectDisposedException>(() => { _ = board.SubmitInputAsync(input); });
            provider.SetException(new Exception[] { first, second });
            _ = await Record.ExceptionAsync(() => operation!);
            var failure = await Record.ExceptionAsync(() => close!);
            Assert.NotNull(failure);
            Assert.True(close.IsFaulted);
            Assert.True(provider.Task.IsFaulted);
            var causes = ActualCauses(close.Exception!);
            Assert.Contains(first, causes);
            Assert.Contains(second, causes);
            inspectedFailures.Add(close);
            if (operation.IsFaulted) inspectedFailures.Add(operation);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            provider.TrySetResult();
            await JoinFixtureAsync(() => operation, inspectedFailures, failures);
            await JoinFixtureAsync(() => close ?? board?.CloseAndDrainAsync(), inspectedFailures, failures);
        }
        ThrowFixture(failures);
    }

    [AvaloniaFact]
    public async Task Held_scene_agent_cannot_publish_through_a_replaced_component_generation()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var component = Component();
        HavenGenUiWhiteboard? board = null;
        Task? operation = null;
        try
        {
            board = new HavenGenUiWhiteboard(component, null, _ => { }, _ =>
            { entered.TrySetResult(); return provider.Task; });
            var input = Field<HavenInput>(board, "_agentInput");
            var status = Field<HavenText>(board, "_status");
            input.Text = "Original generation only";
            operation = board.SubmitInputAsync(input);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            board.Update(component with
            {
                Properties = new Dictionary<string, JsonElement>(component.Properties)
                { ["title"] = JsonSerializer.SerializeToElement("Replacement component") }
            });
            var acknowledgedReplacementStatus = status.Content;
            provider.SetResult();
            var failure = await Record.ExceptionAsync(() => operation!);
            Assert.NotNull(failure);
            Assert.True(operation.IsFaulted);
            Assert.True(provider.Task.IsCompletedSuccessfully);
            Assert.Equal(acknowledgedReplacementStatus, status.Content);
            Assert.NotEqual("Haven received the whiteboard request.", status.Content);
            inspectedFailures.Add(operation);
            var originalClose = board.CloseAndDrainAsync();
            Assert.NotNull(await Record.ExceptionAsync(() => originalClose));
            Assert.True(originalClose.IsFaulted);
            inspectedFailures.Add(originalClose);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            provider.TrySetResult();
            await JoinFixtureAsync(() => operation, inspectedFailures, failures);
            await JoinFixtureAsync(() => board?.CloseAndDrainAsync(), inspectedFailures, failures);
        }
        ThrowFixture(failures);
    }

    [AvaloniaFact]
    public async Task Actual_mount_is_captured_before_retirement_refuses_child_publication()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var store = new GenUiInstanceStore();
        var router = new GenerativeUiEventRouter([new GenUiLocalActionRegistry()], new BoundedGenUiEventAuditSink(), store);
        var resolver = new ChatGenUiNativeControlResolver();
        ChatGenUiSurfaceMount? acquired = null;
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => ChatGenUiSurfaceMount.Create(
                new GenUiRenderingDecision(GenUiRenderingLayer.Native, "original acquisition control"),
                router, store, resolver, actual => { acquired = actual; actual.RequestRetirement(); }));
            Assert.NotNull(acquired);
            Assert.NotNull(acquired!.OriginalClose);
            Assert.False(resolver.TryCreate(acquired.Root, out _));
            var actualClose = acquired.CloseAndDrainAsync();
            Assert.Same(actualClose, acquired.CloseAndDrainAsync());
            Assert.NotNull(await Record.ExceptionAsync(() => actualClose));
            Assert.True(actualClose.IsFaulted);
            inspectedFailures.Add(actualClose);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            await JoinFixtureAsync(() => acquired?.CloseAndDrainAsync(), inspectedFailures, failures);
        }
        ThrowFixture(failures);
    }

    [AvaloniaFact]
    public async Task Real_fullscreen_Ask_callback_cannot_await_its_parent_close_and_external_close_ends_dialog()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        GeneratedWhiteboardControl? board = null;
        var callbackRan = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Window? owner = null;
        Task? opening = null;
        Task? close = null;
        try
        {
            board = new GeneratedWhiteboardControl("Original child owner", "Keep the original dialog", 420, null, _ => { }, originalRequest =>
            {
                // Real native input occurs outside the opening parent's async flow.
                Assert.Throws<InvalidOperationException>(() => { _ = board!.CloseAndDrainAsync(); });
                callbackRan.TrySetResult();
                return Task.CompletedTask;
            });
            owner = new Window(); // Capture before any native setter/Show.
            owner.Width = 960; owner.Height = 720; owner.Content = board; owner.Show();
            opening = InvokeTask(board, "OpenFullscreenAsync");
            await Dispatcher.UIThread.InvokeAsync(() => { });
            var child = OriginalFullscreenWindow(board);
            Assert.True(child.IsVisible);
            var instruction = Assert.Single(child.GetVisualDescendants().OfType<HavenTextInput>(),
                control => control.PlaceholderText == "Ask Haven to add, mark, or refine something");
            instruction.Text = "Actual child callback";
            var ask = Assert.Single(child.GetVisualDescendants().OfType<HavenPrimaryButton>(),
                button => Equals(button.Content, "Ask Haven"));
            ask.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await callbackRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(opening.IsCompleted);
            Assert.True(child.IsVisible);
            close = board.CloseAndDrainAsync();
            Assert.Same(close, board.CloseAndDrainAsync());
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            await opening.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(child.IsVisible);
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(opening.IsCompletedSuccessfully);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            await JoinFixtureAsync(() => close ?? board?.CloseAndDrainAsync(), inspectedFailures, failures);
            await JoinFixtureAsync(() => opening, inspectedFailures, failures);
            if (owner is not null)
            {
                AttemptFixture(() => owner.Content = null, failures);
                AttemptFixture(owner.Close, failures);
            }
        }
        ThrowFixture(failures);
    }

    [AvaloniaFact]
    public async Task Optional_input_owner_null_preserves_real_stroke_and_sealed_owner_refuses_new_pointer_effects()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var restored = UnifiedCanvasStateCodec.Restore(null);
        var standalone = new UnifiedCanvasSurface(restored.Controller);
        standalone.SetTool(UnifiedCanvasTool.Pen);
        var standaloneHost = new HavenSceneControl { Root = standalone };
        Window? owner = null;
        HavenGenUiWhiteboard? board = null;
        Task? boardClose = null;
        try
        {
            owner = new Window(); // Capture before subsequent constructor/setter failures.
            board = new HavenGenUiWhiteboard(Component(), null, _ => { }, null);
            owner.Width = 960; owner.Height = 720; owner.Content = standaloneHost; owner.Show();
            owner.UpdateLayout();
            var router = new HavenInputRouter(standalone);
            router.PointerPressed(new HavenPoint(40, 40), HavenPointerKind.Pen);
            router.PointerMoved(new HavenPoint(80, 65), HavenPointerKind.Pen);
            router.PointerReleased(new HavenPoint(120, 90));
            var state = UnifiedCanvasStateCodec.ToJson(restored.Controller, standalone.Tool, standalone.ShowGrid);
            Assert.Equal(3, state.GetProperty("version").GetInt32());
            Assert.Single(state.GetProperty("board").GetProperty("strokes").EnumerateArray());
            var guarded = Assert.Single(board.DescendantsAndSelf().OfType<UnifiedCanvasSurface>());
            var before = guarded.Controller;
            var actualBefore = UnifiedCanvasStateCodec.ToJson(before, guarded.Tool, guarded.ShowGrid).GetRawText();
            boardClose = board.CloseAndDrainAsync();
            await boardClose;
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = guarded.PointerPressed(new HavenPointerInput(new HavenPoint(40, 40), new HavenPoint(40, 40), HavenPointerKind.Pen));
            });
            Assert.Equal(actualBefore, UnifiedCanvasStateCodec.ToJson(before, guarded.Tool, guarded.ShowGrid).GetRawText());
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            await JoinFixtureAsync(() => boardClose ?? board?.CloseAndDrainAsync(), inspectedFailures, failures);
            if (owner is not null)
            {
                AttemptFixture(() => owner.Content = null, failures);
                AttemptFixture(owner.Close, failures);
            }
        }
        ThrowFixture(failures);
    }

    [AvaloniaFact]
    public async Task Live_scene_structural_rebuild_acknowledges_actual_active_child_gesture_before_replacement()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var rebuilds = new List<Task>();
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        HavenGenUiSceneSurface? surface = null;
        Window? window = null;
        Task? close = null;
        try
        {
            var document = ActualCanvasDocument(local, store);
            surface = new HavenGenUiSceneSurface(router, store);
            surface.Present(document);
            window = new Window();
            window.Width = 960; window.Height = 720;
            window.Content = new HavenSceneControl { Root = surface.Root }; window.Show(); window.UpdateLayout();
            var oldChild = Assert.Single(surface.Root.DescendantsAndSelf().OfType<HavenGenUiWhiteboard>());
            var oldCanvas = Assert.Single(oldChild.DescendantsAndSelf().OfType<UnifiedCanvasSurface>());
            BeginActualGesture(oldCanvas);
            var stroke = Assert.Single(oldCanvas.Controller.Board.Strokes);
            var pointCount = stroke.Points.Count;
            Assert.True(pointCount >= 2);
            Assert.False(store.TryGet(document.Origin.InstanceId)!.State.ContainsKey("canvas.original.canvas"));
            Assert.True(store.ApplyDocumentChange(Guid.NewGuid(), document.Origin.InstanceId, actual => actual with
            {
                Root = actual.Root with { Children = actual.Root.Children.Concat(new[]
                {
                    new GenUiComponent("original.added", "HavenText", new Dictionary<string, JsonElement>
                    { ["text"] = JsonSerializer.SerializeToElement("Actual structural replacement") }, [], [])
                }).ToArray() }
            }, DateTimeOffset.UtcNow));
            var firstRebuild = NullableField<Task>(surface, "_pendingRebuild");
            Assert.NotNull(firstRebuild);
            rebuilds.Add(firstRebuild!);
            await JoinActualSceneRebuildsAsync(surface, rebuilds);
            Assert.Null(surface.OriginalClose); // Rebuild did not permanently close its live parent.
            Assert.NotNull(oldChild.OriginalClose);
            Assert.True(oldChild.OriginalClose!.IsCompletedSuccessfully);
            var acknowledged = store.TryGet(document.Origin.InstanceId)!;
            Assert.Equal(document.Origin.InstanceId, acknowledged.Origin.InstanceId);
            var restored = UnifiedCanvasStateCodec.Restore(acknowledged.State["canvas.original.canvas"]);
            var savedStroke = Assert.Single(restored.Controller.Board.Strokes);
            Assert.Equal(stroke.Id, savedStroke.Id);
            Assert.Equal(pointCount, savedStroke.Points.Count); // No synthetic pointer-release endpoint.
            var replacement = Assert.Single(surface.Root.DescendantsAndSelf().OfType<HavenGenUiWhiteboard>());
            Assert.NotSame(oldChild, replacement);
            var replacementCanvas = Assert.Single(replacement.DescendantsAndSelf().OfType<UnifiedCanvasSurface>());
            Assert.Equal(stroke.Id, Assert.Single(replacementCanvas.Controller.Board.Strokes).Id);
            Assert.Throws<ObjectDisposedException>(() => { _ = oldCanvas.PointerPressed(ActualPointer(160, 160)); });
            close = surface.CloseAndDrainAsync();
            await close.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            foreach (var actual in rebuilds) await JoinFixtureAsync(() => actual, inspectedFailures, failures);
            await JoinFixtureAsync(() => close ?? surface?.CloseAndDrainAsync(), inspectedFailures, failures);
            if (window is not null)
            {
                AttemptFixture(() => window.Content = null, failures);
                AttemptFixture(window.Close, failures);
            }
        }
        ThrowFixture(failures);
    }

    [AvaloniaFact]
    public async Task Permanent_scene_close_acknowledges_same_active_child_state_without_new_input()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        HavenGenUiSceneSurface? surface = null;
        Window? window = null;
        Task? close = null;
        try
        {
            var document = ActualCanvasDocument(local, store);
            surface = new HavenGenUiSceneSurface(router, store);
            surface.Present(document);
            window = new Window();
            window.Width = 960; window.Height = 720;
            window.Content = new HavenSceneControl { Root = surface.Root }; window.Show(); window.UpdateLayout();
            var child = Assert.Single(surface.Root.DescendantsAndSelf().OfType<HavenGenUiWhiteboard>());
            var canvas = Assert.Single(child.DescendantsAndSelf().OfType<UnifiedCanvasSurface>());
            BeginActualGesture(canvas);
            var stroke = Assert.Single(canvas.Controller.Board.Strokes);
            var pointCount = stroke.Points.Count;
            close = surface.CloseAndDrainAsync();
            Assert.Same(close, surface.CloseAndDrainAsync());
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(child.OriginalClose!.IsCompletedSuccessfully);
            var acknowledged = store.TryGet(document.Origin.InstanceId)!;
            Assert.Equal(document.Origin.InstanceId, acknowledged.Origin.InstanceId);
            var saved = UnifiedCanvasStateCodec.Restore(acknowledged.State["canvas.original.canvas"]);
            var savedStroke = Assert.Single(saved.Controller.Board.Strokes);
            Assert.Equal(stroke.Id, savedStroke.Id);
            Assert.Equal(pointCount, savedStroke.Points.Count);
            Assert.Throws<ObjectDisposedException>(() => { _ = canvas.PointerMoved(ActualPointer(160, 160)); });
            Assert.Equal(pointCount, Assert.Single(canvas.Controller.Board.Strokes).Points.Count);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            await JoinFixtureAsync(() => close ?? surface?.CloseAndDrainAsync(), inspectedFailures, failures);
            if (window is not null)
            {
                AttemptFixture(() => window.Content = null, failures);
                AttemptFixture(window.Close, failures);
            }
        }
        ThrowFixture(failures);
    }

    [AvaloniaFact]
    public async Task Actual_native_grid_resize_has_no_layout_effect_after_seal_while_real_action_is_held()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var entered = new TaskCompletionSource<GenUiEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource<GenUiActionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        GenerativeUiSurface? surface = null;
        Window? window = null;
        Task? operation = null;
        Task? close = null;
        GenUiEvent? actualEvent = null;
        try
        {
            var document = ActualGridDocument(local, store);
            local.RegisterOrReplace("original.hold", (actual, _) => { entered.TrySetResult(actual); return held.Task; });
            store.Register(document);
            surface = new GenerativeUiSurface(router, store);
            surface.PresentExisting(document); // The actual supported no-reveal restoration path.
            window = new Window();
            window.Width = 960; window.Height = 720; window.Content = surface; window.Show(); window.UpdateLayout();
            var controls = Field<Dictionary<string, Control>>(surface, "_controls");
            var grid = Assert.IsType<Grid>(controls["original.grid"]);
            var button = Assert.IsAssignableFrom<Button>(controls["original.hold.button"]);
            var component = document.Root.Children.Single(node => node.ComponentId == "original.grid")
                .Children.Single(node => node.ComponentId == "original.hold.button");
            operation = InvokeActualNativeEmit(surface, component, button);
            actualEvent = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(document.Origin.InstanceId, actualEvent.Origin.InstanceId);
            Assert.Equal(component.ComponentId, actualEvent.ComponentId);
            var columns = grid.ColumnDefinitions;
            var rows = grid.RowDefinitions;
            var placements = grid.Children.Select(child => (Grid.GetColumn(child), Grid.GetRow(child))).ToArray();
            close = surface.CloseAndDrainAsync();
            Assert.Same(close, surface.CloseAndDrainAsync());
            await Dispatcher.UIThread.InvokeAsync(() => { }); // Stop dispatcher ran; the real handler task is still held.
            Assert.False(close.IsCompleted);
            grid.Arrange(new Avalonia.Rect(0, 0, 240, 420));
            Assert.Same(columns, grid.ColumnDefinitions);
            Assert.Same(rows, grid.RowDefinitions);
            Assert.Equal(placements, grid.Children.Select(child => (Grid.GetColumn(child), Grid.GetRow(child))).ToArray());
            Assert.False(held.Task.IsCompleted);
            held.SetResult(GenerativeUiEventRouter.Result(actualEvent, GenUiActionStatus.Completed, "Actual held handler returned"));
            Assert.NotNull(await Record.ExceptionAsync(() => operation!));
            Assert.NotNull(await Record.ExceptionAsync(() => close!));
            Assert.True(operation.IsFaulted);
            Assert.True(close.IsFaulted);
            Assert.True(held.Task.IsCompletedSuccessfully);
            Assert.Contains(ActualCauses(close.Exception!), cause => cause is OperationCanceledException);
            inspectedFailures.Add(operation);
            inspectedFailures.Add(close);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            if (actualEvent is not null)
                held.TrySetResult(GenerativeUiEventRouter.Result(actualEvent, GenUiActionStatus.Completed, "Fixture independent release"));
            else held.TrySetException(new InvalidOperationException("The actual handler was not acquired before fixture cleanup."));
            await JoinFixtureAsync(() => operation, inspectedFailures, failures);
            await JoinFixtureAsync(() => close ?? surface?.CloseAndDrainAsync(), inspectedFailures, failures);
            if (window is not null)
            {
                AttemptFixture(() => window.Content = null, failures);
                AttemptFixture(window.Close, failures);
            }
        }
        ThrowFixture(failures);
    }

    [AvaloniaFact]
    public async Task Actual_native_grid_first_layout_notification_retirement_prevents_later_layout_writes()
    {
        var failures = new List<Exception>();
        var inspectedFailures = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var store = new GenUiInstanceStore();
        var local = new GenUiLocalActionRegistry();
        var router = new GenerativeUiEventRouter([local], new BoundedGenUiEventAuditSink(), store);
        GenerativeUiSurface? surface = null;
        Grid? grid = null;
        Control? subscribedChild = null;
        Window? window = null;
        Task? close = null;
        EventHandler<Avalonia.AvaloniaPropertyChangedEventArgs>? retirement = null;
        try
        {
            var document = ActualGridDocument(local, store);
            store.Register(document);
            surface = new GenerativeUiSurface(router, store);
            surface.PresentExisting(document);
            window = new Window();
            window.Width = 960; window.Height = 720; window.Content = surface; window.Show(); window.UpdateLayout();
            grid = Assert.IsType<Grid>(Field<Dictionary<string, Control>>(surface, "_controls")["original.grid"]);
            Assert.Equal(3, grid.ColumnDefinitions.Count);
            var secondChild = grid.Children[1];
            var thirdChild = grid.Children[2];
            var secondRow = Grid.GetRow(secondChild);
            var thirdColumn = Grid.GetColumn(thirdChild);
            var thirdRow = Grid.GetRow(thirdChild);
            var entered = false;
            retirement = (_, actual) =>
            {
                if (actual.Property != Grid.ColumnProperty || entered) return;
                entered = true;
                surface.RequestRetirement(); // Real notifying attached write reenters the SAME owner.
            };
            subscribedChild = secondChild;
            secondChild.PropertyChanged += retirement;
            var failure = Record.Exception(() => grid.Arrange(new Avalonia.Rect(0, 0, 240, 420)));
            Assert.True(entered);
            Assert.NotNull(failure);
            Assert.Equal(0, Grid.GetColumn(secondChild)); // The notifying write itself was acknowledged.
            Assert.Equal(secondRow, Grid.GetRow(secondChild));
            Assert.Equal(thirdColumn, Grid.GetColumn(thirdChild));
            Assert.Equal(thirdRow, Grid.GetRow(thirdChild));
            close = surface.CloseAndDrainAsync();
            Assert.Same(close, surface.OriginalClose);
            Assert.NotNull(await Record.ExceptionAsync(() => close!));
            Assert.True(close.IsFaulted);
            Assert.Contains(ActualCauses(close.Exception!), cause => ActualCauses(failure!).Any(original => ReferenceEquals(original, cause)));
            inspectedFailures.Add(close);
        }
        catch (Exception cause) { Add(failures, cause); }
        finally
        {
            if (subscribedChild is not null && retirement is not null)
                AttemptFixture(() => subscribedChild.PropertyChanged -= retirement, failures);
            await JoinFixtureAsync(() => close ?? surface?.CloseAndDrainAsync(), inspectedFailures, failures);
            if (window is not null)
            {
                AttemptFixture(() => window.Content = null, failures);
                AttemptFixture(window.Close, failures);
            }
        }
        ThrowFixture(failures);
    }

    private static GenUiDocument ActualCanvasDocument(GenUiLocalActionRegistry local, GenUiInstanceStore store) =>
        new CustomTemplateRuntime(local, store).Create(Guid.NewGuid(), "chat", new Dictionary<string, JsonElement>
        {
            ["title"] = JsonSerializer.SerializeToElement("Actual original canvas"),
            ["components"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { id = "original.stack", type = "HavenStack", children = new object[]
                {
                    new { id = "original.canvas", type = "HavenCanvas", props = new { minHeight = 420 } }
                } }
            })
        });
    private static GenUiDocument ActualGridDocument(GenUiLocalActionRegistry local, GenUiInstanceStore store) =>
        new CustomTemplateRuntime(local, store).Create(Guid.NewGuid(), "chat", new Dictionary<string, JsonElement>
        {
            ["title"] = JsonSerializer.SerializeToElement("Actual responsive grid"),
            ["components"] = JsonSerializer.SerializeToElement(new object[]
            {
                new { id = "original.stack", type = "HavenStack", children = new object[]
                {
                    new { id = "original.grid", type = "HavenGrid", props = new { columns = 3, responsive = true, itemMinWidth = 180 },
                        children = new object[]
                        {
                            new { id = "original.hold.button", type = "HavenButton", props = new { label = "Hold actual action" },
                                actions = new object[] { new { id = "original.hold" } } },
                            new { id = "original.text.one", type = "HavenText", props = new { text = "First actual child" } },
                            new { id = "original.text.two", type = "HavenText", props = new { text = "Second actual child" } }
                        } }
                } }
            })
        });
    private static HavenPointerInput ActualPointer(double x, double y) =>
        new(new HavenPoint(x, y), new HavenPoint(x, y), HavenPointerKind.Pen);
    private static void BeginActualGesture(UnifiedCanvasSurface canvas)
    {
        Assert.Equal(UnifiedCanvasTool.Pen, canvas.Tool);
        Assert.True(canvas.PointerPressed(ActualPointer(40, 40)));
        Assert.True(canvas.PointerMoved(ActualPointer(80, 65)));
        // Intentionally no PointerReleased: the actual owning stop must settle this gesture.
    }
    private static T? NullableField<T>(object owner, string name) where T : class =>
        (T?)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner);
    private static async Task JoinActualSceneRebuildsAsync(HavenGenUiSceneSurface surface, List<Task> originals)
    {
        for (var observed = 0; observed < 8; observed++)
        {
            var actual = NullableField<Task>(surface, "_pendingRebuild");
            if (actual is null) return;
            if (!originals.Any(known => ReferenceEquals(known, actual))) originals.Add(actual);
            await actual.WaitAsync(TimeSpan.FromSeconds(5));
            await Dispatcher.UIThread.InvokeAsync(() => { });
        }
        throw new InvalidOperationException("The actual captured rebuild chain did not retire within the bounded control cohort.");
    }
    private static Task InvokeActualNativeEmit(GenerativeUiSurface surface, GenUiComponent component, Control control) =>
        (Task)(typeof(GenerativeUiSurface).GetMethod("EmitAsync", BindingFlags.NonPublic | BindingFlags.Instance)?
            .Invoke(surface, new object?[] { component, GenUiEventType.ActionInvoked, null, control })
            ?? throw new InvalidOperationException("The actual native event original was not returned."));

    private static async Task JoinFixtureAsync(Func<Task?> acquire, HashSet<Task> inspectedFailures, List<Exception> failures)
    {
        Task? actual = null;
        try { actual = acquire(); if (actual is not null) await actual; }
        catch (Exception cause)
        {
            if (actual is not null && actual.IsFaulted && inspectedFailures.Contains(actual)) return;
            if (actual?.Exception is { InnerExceptions.Count: > 0 } group)
                foreach (var direct in group.InnerExceptions) Add(failures, direct);
            else Add(failures, cause);
        }
    }
    private static void AttemptFixture(Action cleanup, List<Exception> failures)
    { try { cleanup(); } catch (Exception cause) { Add(failures, cause); } }
    private static void Add(List<Exception> failures, Exception cause)
    { if (!failures.Any(actual => ReferenceEquals(actual, cause))) failures.Add(cause); }
    private static void ThrowFixture(List<Exception> failures)
    { if (failures.Count != 0) throw new AggregateException("Original fixture body or independently joined cleanup failed.", failures); }

    private static GenUiComponent Component() => new("original.canvas", "HavenCanvas",
        new Dictionary<string, JsonElement>
        { ["title"] = JsonSerializer.SerializeToElement("Actual original canvas"), ["minHeight"] = JsonSerializer.SerializeToElement(420) }, [], []);
    private static T Field<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(owner)
            ?? throw new InvalidOperationException("Missing original field: " + name));
    private static Task InvokeTask(object owner, string name) =>
        (Task)(owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(owner, null)
            ?? throw new InvalidOperationException("Missing original producer: " + name));
    private static Window OriginalFullscreenWindow(GeneratedWhiteboardControl owner)
    {
        var children = Field<IEnumerable>(owner, "_originalWindows").Cast<object>();
        return Field<Window>(Assert.Single(children), "_window");
    }
    private static IReadOnlyList<Exception> ActualCauses(Exception cause)
    {
        var result = new List<Exception>();
        void Visit(Exception actual)
        {
            if (actual is AggregateException group && group.InnerExceptions.Count > 0)
                foreach (var direct in group.InnerExceptions) Visit(direct);
            else result.Add(actual);
        }
        Visit(cause);
        return result;
    }
}
