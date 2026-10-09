using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>Supplemental producer controls. Source proposal only; no installed/native trust claim.</summary>
public sealed class CuiSceneHostOriginalPublicationTests
{
    [Fact]
    public async Task Held_original_readiness_finishes_before_retired_publication_refuses()
    {
        await RunAsync(async rig =>
        {
            var readiness = new HeldReadiness();
            _ = rig.Track(readiness.Original);
            rig.Release(() => readiness.Complete());
            var current = true;
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(readiness: readiness) with
            { IsPublicationCurrent = () => current })));
            Assert.False(show!.IsCompleted);
            Assert.False(readiness.Original.IsCompleted);
            await rig.Ui(() => current = false);
            readiness.Complete();
            var error = await Record.ExceptionAsync(() => show!);
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            rig.Expect(error!);
            await rig.Ui(() =>
            {
                Assert.Null(rig.Host.Content);
                Assert.Null(rig.Host.Availability);
                Assert.Empty(rig.Host.Resources.MergedDictionaries);
            });
            Assert.Equal(1, readiness.Calls);
        });
    }

    [Theory]
    [InlineData("Background")]
    [InlineData("Foreground")]
    [InlineData("Content")]
    public async Task Retirement_from_actual_native_notification_stops_the_remaining_publication(string property)
    {
        await RunAsync(async rig =>
        {
            var current = true;
            var retired = false;
            var bindings = new OriginalBindings();
            var notifications = new List<string>();
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() =>
            {
                rig.Host.PropertyChanged += (_, args) =>
                {
                    notifications.Add(args.Property.Name);
                    if (!retired && args.Property.Name == property)
                    {
                        retired = true;
                        current = false;
                    }
                };
                show = rig.Track(rig.Host.ShowAsync(Scene(bindings: bindings) with
                { IsPublicationCurrent = () => current }));
            });
            var error = await Record.ExceptionAsync(() => show!);
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            rig.Expect(error!);
            Assert.True(retired);
            await rig.Ui(() =>
            {
                Assert.Null(rig.Host.Content);
                Assert.Null(rig.Host.Availability);
                Assert.Empty(rig.Host.Diagnostics);
                Assert.Empty(rig.Host.Resources.MergedDictionaries);
                if (property == "Background") Assert.DoesNotContain("Foreground", notifications);
                if (property != "Content") Assert.DoesNotContain("Content", notifications);
            });
            Assert.Equal(1, bindings.Adds);
            Assert.Equal(1, bindings.Removes);
            Assert.Equal(0, bindings.Subscribers);
        });
    }

    [Fact]
    public async Task Original_factory_failure_and_independent_partial_binding_close_are_both_retained()
    {
        var primary = new InvalidOperationException("Original factory.");
        var close = new IOException("Original binding unsubscribe.");
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("fault", _ => throw primary);
        await RunAsync(async rig =>
        {
            var bindings = new OriginalBindings { RemoveFailure = close };
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(bindings: bindings,
                markup: "<Cui><Object Type=\"fault\" /></Cui>") with { IsPublicationCurrent = () => true })));
            var error = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => show!));
            Assert.Equal(2, error.InnerExceptions.Count);
            Assert.Same(primary, error.InnerExceptions[0]);
            Assert.Same(close, error.InnerExceptions[1]);
            rig.Expect(error);
            await rig.Ui(() =>
            {
                Assert.Null(rig.Host.Content);
                Assert.Empty(rig.Host.Resources.MergedDictionaries);
            });
            Assert.Equal(1, bindings.Removes);
            Assert.Equal(0, bindings.Subscribers);
        }, registry);
    }

    [Fact]
    public async Task Held_original_action_started_by_content_notification_is_retained_until_owner_close_after_partial_mount_retirement()
    {
        await RunAsync(async rig =>
        {
            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            var current = true;
            var clicked = false;
            var notification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = rig.Track(notification.Task);
            rig.Release(() => notification.TrySetResult()); // Cleanup release is not a publication witness.
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() =>
            {
                rig.Host.PropertyChanged += (_, args) =>
                {
                    if (clicked || args.Property.Name != "Content" || rig.Host.Content is not Button button) return;
                    clicked = true;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    current = false;
                    notification.TrySetResult(); // SAME actual Content callback has now accepted the action.
                };
                show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with { IsPublicationCurrent = () => current }));
            });
            await Task.WhenAny(notification.Task, show!);
            if (!notification.Task.IsCompleted) await show!; // Preserve an earlier original Show failure.
            Assert.True(clicked);
            Assert.Equal(1, action.Calls);
            Assert.False(action.Original.IsCompleted);
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            Assert.False(close!.IsCompleted);
            Assert.Same(close, rig.Host.OriginalClose);
            Assert.Same(close, rig.Host.CloseOriginalAsync());
            action.Complete();
            var error = await Record.ExceptionAsync(() => show!);
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            rig.Expect(error!);
            Assert.Same(error, await Record.ExceptionAsync(() => close!));
            Assert.True(action.Original.IsCompletedSuccessfully);
            await rig.Ui(() =>
            {
                Assert.Null(rig.Host.Content);
                Assert.Empty(rig.Host.Resources.MergedDictionaries);
            });
        });
    }

    [Fact]
    public async Task Pending_original_action_retirement_keeps_the_existing_safe_observer_failure_limit_explicit()
    {
        await RunAsync(async rig =>
        {
            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            var current = true;
            Task<CuiSceneAvailability>? show = null;
            CuiControlLoader? loader = null;
            Control? originalRoot = null;
            Task? pipeline = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => current })));
            await show!;
            await rig.Ui(() =>
            {
                originalRoot = Assert.IsType<Button>(rig.Host.Content);
                loader = Field<CuiControlLoader>(rig.Host, "_loader");
                ((Button)originalRoot).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pipeline = rig.Track(loader.WhenActionsIdleAsync());
                current = false;
            });
            Assert.False(pipeline!.IsCompleted);
            var primary = new InvalidOperationException("Original action.");
            action.Fail(primary);
            Assert.Same(primary, await Record.ExceptionAsync(() => action.Original));
            rig.Expect(primary);
            var pipelineError = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => pipeline));
            Assert.True(pipeline!.IsFaulted);
            Assert.Same(pipelineError, Assert.Single(pipeline!.Exception!.InnerExceptions));
            Assert.Equal(2, pipelineError.InnerExceptions.Count);
            Assert.Same(primary, pipelineError.InnerExceptions[0]);
            var withdrawal = Assert.IsType<OperationCanceledException>(pipelineError.InnerExceptions[1]);
            AssertOriginalSourceWithdrawal(rig.Host, withdrawal);
            rig.Expect(pipelineError);
            await rig.Ui(() =>
            {
                Assert.Same(originalRoot, rig.Host.Content);
                Assert.Null(rig.Host.LastActionFailure);
                Assert.Empty(rig.Host.Diagnostics);
                var diagnostics = Field<List<CuiDiagnostic>>(loader!, "_runtimeDiagnostics");
                Assert.Contains(diagnostics, item => item.Code == "CUIA_FAILED");
                Assert.Contains(diagnostics, item => item.Code == "CUIA_OBSERVER_FAILED");
            });
            Assert.Equal(1, action.Calls);
            // The real loader pipeline keeps the dispatcher failure and this exact source withdrawal.
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            var closeError = await Record.ExceptionAsync(() => close!);
            AssertOriginalCauses(closeError!, pipelineError, withdrawal);
            Assert.Same(close, rig.Host.OriginalClose);
            Assert.Same(close, rig.Host.CloseOriginalAsync());
            rig.Expect(closeError!);
        });
    }

    [Fact]
    public async Task Action_banner_retirement_from_content_clear_stops_layout_and_bound_message_publication()
    {
        await RunAsync(async rig =>
        {
            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            var current = true;
            var retired = false;
            var clearing = false;
            Task<CuiSceneAvailability>? show = null;
            Task? pipeline = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => current })));
            await show!;
            await rig.Ui(() =>
            {
                rig.Host.PropertyChanged += (_, args) =>
                {
                    if (clearing && args.Property.Name == "Content" && rig.Host.Content is null)
                    { current = false; retired = true; }
                };
                var loader = Field<CuiControlLoader>(rig.Host, "_loader");
                Assert.IsType<Button>(rig.Host.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pipeline = rig.Track(loader.WhenActionsIdleAsync());
                clearing = true;
            });
            var primary = new InvalidOperationException("Original action.");
            action.Fail(primary);
            Assert.Same(primary, await Record.ExceptionAsync(() => action.Original));
            rig.Expect(primary);
            var pipelineError = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => pipeline!));
            Assert.True(pipeline!.IsFaulted);
            Assert.Same(pipelineError, Assert.Single(pipeline!.Exception!.InnerExceptions));
            Assert.Equal(2, pipelineError.InnerExceptions.Count);
            Assert.Same(primary, pipelineError.InnerExceptions[0]);
            var withdrawal = Assert.IsType<OperationCanceledException>(pipelineError.InnerExceptions[1]);
            AssertOriginalSourceWithdrawal(rig.Host, withdrawal);
            rig.Expect(pipelineError);
            Assert.True(retired);
            await rig.Ui(() =>
            {
                Assert.Null(rig.Host.Content);
                Assert.Null(OptionalField(rig.Host, "_failureLoader"));
                Assert.Null(OptionalField(rig.Host, "_failureModel"));
                Assert.Equal("CUIA_FAILED", rig.Host.LastActionFailure!.Code);
            });
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            var closeError = await Record.ExceptionAsync(() => close!);
            AssertOriginalCauses(closeError!, pipelineError, withdrawal);
            Assert.Same(close, rig.Host.OriginalClose);
            Assert.Same(close, rig.Host.CloseOriginalAsync());
            rig.Expect(closeError!);
        });
    }

    [Fact]
    public async Task Absent_optional_predicate_preserves_ready_scene_and_original_failure_banner()
    {
        await RunAsync(async rig =>
        {
            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            var scene = Scene(actions: action);
            Assert.Null(scene.IsPublicationCurrent);
            Task<CuiSceneAvailability>? show = null;
            Task? pipeline = null;
            CuiControlLoader? loader = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(scene)));
            var availability = await show!;
            Assert.Equal(CuiSceneAvailabilityState.Ready, availability.State);
            await rig.Ui(() =>
            {
                loader = Field<CuiControlLoader>(rig.Host, "_loader");
                Assert.IsType<Button>(rig.Host.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pipeline = rig.Track(loader.WhenActionsIdleAsync());
            });
            var primary = new InvalidOperationException("Original action.");
            action.Fail(primary);
            Assert.Same(primary, await Record.ExceptionAsync(() => action.Original));
            rig.Expect(primary);
            Assert.Same(primary, await Record.ExceptionAsync(() => pipeline!));
            Assert.True(pipeline!.IsFaulted);
            Assert.Same(primary, Assert.Single(pipeline!.Exception!.InnerExceptions));
            await rig.Ui(() =>
            {
                var layout = Assert.IsType<Grid>(rig.Host.Content);
                Assert.Equal(2, layout.Children.Count);
                Assert.Equal(rig.Host.LastActionFailure!.Message, Assert.IsType<TextBlock>(layout.Children[0]).Text);
                Assert.Equal(availability, rig.Host.Availability);
            });
            Task? disposal = null;
            await rig.Ui(() =>
            {
                rig.Host.Dispose(); // The unchanged legacy contract, on its actual UI owner.
                disposal = rig.Track(Field<Task>(loader!, "_disposalTask"));
                Assert.True((bool)OptionalField(rig.Host, "_disposed")!);
            });
            Assert.Same(primary, await Record.ExceptionAsync(() => disposal!));
            Assert.True(disposal!.IsFaulted);
            Assert.Same(primary, Assert.Single(disposal.Exception!.InnerExceptions));
            Assert.Same(disposal, Field<Task>(loader!, "_disposalTask"));
        });
    }


    [Fact]
    public async Task Prepared_root_alias_does_not_remove_the_previously_owned_scene()
    {
        var registry = new CuiControlRegistry();
        Control? sameRoot = null;
        var retire = false;
        var current = true;
        registry.RegisterObjectRenderer("shared-root", _ =>
        {
            if (retire) current = false;
            return sameRoot ??= new Border();
        });
        await RunAsync(async rig =>
        {
            var oldBindings = new OriginalBindings();
            var nextBindings = new OriginalBindings();
            var scene = Scene(bindings: oldBindings, markup: "<Cui><Object Type=\"shared-root\" /></Cui>") with
            { IsPublicationCurrent = () => current };
            Task<CuiSceneAvailability>? originalShow = null;
            await rig.Ui(() => originalShow = rig.Track(rig.Host.ShowAsync(scene)));
            await originalShow!;
            CuiControlLoader? originalLoader = null;
            await rig.Ui(() => originalLoader = Field<CuiControlLoader>(rig.Host, "_loader"));
            retire = true;
            Task<CuiSceneAvailability>? refused = null;
            await rig.Ui(() => refused = rig.Track(rig.Host.ShowAsync(scene with { Bindings = nextBindings })));
            var error = await Record.ExceptionAsync(() => refused!);
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            rig.Expect(error!);
            await rig.Ui(() =>
            {
                Assert.Same(sameRoot, rig.Host.Content);
                Assert.Same(originalLoader, Field<CuiControlLoader>(rig.Host, "_loader"));
            });
            Assert.Equal(0, oldBindings.Removes);
            Assert.Equal(1, nextBindings.Removes);
            // Loader-owned DataContext/binding writes remain outside host publication atomicity.
        }, registry);
    }

    [Fact]
    public async Task Actual_action_can_await_replacement_Show_without_a_self_predecessor_join()
    {
        await RunAsync(async rig =>
        {
            var action = new ReplacementAction();
            var current = true;
            Task<CuiSceneAvailability>? initial = null;
            await rig.Ui(() => initial = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => current })));
            await initial!;
            Task<CuiSceneAvailability>? replacement = null;
            action.Replace = () =>
            {
                replacement = rig.Track(rig.Host.ShowAsync(Scene(markup: "<Cui><TextBlock text=\"Replacement\" /></Cui>") with
                { IsPublicationCurrent = () => current }));
                return replacement;
            };
            CuiControlLoader? previous = null;
            Task? pipeline = null;
            await rig.Ui(() =>
            {
                previous = Field<CuiControlLoader>(rig.Host, "_loader");
                Assert.IsType<Button>(rig.Host.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pipeline = rig.Track(previous.WhenActionsIdleAsync());
                if (action.Original is { } original) rig.Track(original);
            });
            await pipeline!;
            Assert.NotNull(replacement);
            Assert.True(replacement!.IsCompletedSuccessfully);
            Assert.True(action.Original!.IsCompletedSuccessfully);
            Assert.Equal(1, action.Calls);
            await rig.Ui(() => Assert.IsType<TextBlock>(rig.Host.Content));
            // Owner retirement occurs AFTER the accepted action/Show dependency has settled.
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            Assert.Same(close, rig.Host.OriginalClose);
            Assert.Same(close, rig.Host.CloseOriginalAsync());
            await close!;
            Assert.True(pipeline.IsCompletedSuccessfully);
            await rig.Ui(() => Assert.Null(rig.Host.Content));
        });
    }

    [Fact]
    public async Task Owner_close_is_published_before_cancellation_reentry_and_joins_same_held_pipeline()
    {
        await RunAsync(async rig =>
        {
            var action = new ReentrantCloseAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => true })));
            await show!;
            Task? pipeline = null;
            await rig.Ui(() =>
            {
                action.Close = rig.Host.CloseOriginalAsync;
                var loader = Field<CuiControlLoader>(rig.Host, "_loader");
                Assert.IsType<Button>(rig.Host.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pipeline = rig.Track(loader.WhenActionsIdleAsync());
            });
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            // Wait for the actual cancellation callback, not just a queued UI close request.
            await action.CancellationObserved.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(close, action.ReentrantClose);
            Assert.Same(close, rig.Host.OriginalClose);
            Assert.False(close!.IsCompleted);
            Assert.False(pipeline!.IsCompleted);
            action.Complete();
            await close;
            Assert.True(pipeline.IsCompletedSuccessfully);
            Assert.True(action.Original.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task Optional_owner_refuses_mixed_lifetime_and_retained_capacity_before_new_readiness()
    {
        await RunAsync(async rig =>
        {
            for (var index = 0; index < 128; index++)
            {
                Task<CuiSceneAvailability>? show = null;
                await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene() with { IsPublicationCurrent = () => true })));
                await show!;
            }
            var readiness = new HeldReadiness();
            var refused = rig.Track(rig.Host.ShowAsync(Scene(readiness: readiness) with { IsPublicationCurrent = () => true }));
            var error = await Record.ExceptionAsync(() => refused);
            Assert.IsType<InvalidOperationException>(error);
            rig.Expect(error!);
            Assert.Equal(0, readiness.Calls);
            Task<Window>? unsupportedFactory = null;
            await rig.Ui(() => unsupportedFactory = rig.Track(CuiSceneHost.CreateWindowAsync(
                Scene(readiness: readiness) with { IsPublicationCurrent = () => true })));
            var factoryError = await Record.ExceptionAsync(() => unsupportedFactory!);
            Assert.IsType<InvalidOperationException>(factoryError);
            rig.Expect(factoryError!);
            Assert.Equal(0, readiness.Calls); // Refusal precedes actual native-host/readiness acquisition.
            var mixed = rig.Track(rig.Host.ShowAsync(Scene()));
            var mixedError = await Record.ExceptionAsync(() => mixed);
            Assert.IsType<InvalidOperationException>(mixedError);
            rig.Expect(mixedError!);
        });
        await RunAsync(async rig =>
        {
            Task<CuiSceneAvailability>? legacy = null;
            await rig.Ui(() => legacy = rig.Track(rig.Host.ShowAsync(Scene())));
            await legacy!;
            var readiness = new HeldReadiness();
            var refused = rig.Track(rig.Host.ShowAsync(Scene(readiness: readiness) with { IsPublicationCurrent = () => true }));
            var error = await Record.ExceptionAsync(() => refused);
            Assert.IsType<InvalidOperationException>(error);
            rig.Expect(error!);
            Assert.Equal(0, readiness.Calls);
        });
    }


    [Fact]
    public async Task Mounted_scene_Show_fault_and_later_banner_refusal_are_both_retained_by_same_owner_close()
    {
        var previousClose = new IOException("Original previous-loader binding close.");
        var primaryAction = new InvalidOperationException("Original repeated action.");
        await RunAsync(async rig =>
        {
            var previousBindings = new OriginalBindings { RemoveFailure = previousClose };
            Task<CuiSceneAvailability>? previousShow = null;
            await rig.Ui(() => previousShow = rig.Track(rig.Host.ShowAsync(Scene(bindings: previousBindings) with
            { IsPublicationCurrent = () => true })));
            await previousShow!;

            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            Task<CuiSceneAvailability>? mountedShow = null;
            await rig.Ui(() => mountedShow = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => true })));
            Assert.Same(previousClose, await Record.ExceptionAsync(() => mountedShow!));
            rig.Expect(previousClose);
            CuiControlLoader? mountedLoader = null;
            Button? actualButton = null;
            await rig.Ui(() =>
            {
                actualButton = Assert.IsType<Button>(rig.Host.Content);
                mountedLoader = Field<CuiControlLoader>(rig.Host, "_loader");
            });
            Assert.Equal(CuiSceneAvailabilityState.Ready, rig.Host.Availability!.State);
            Assert.Equal(1, previousBindings.Removes);

            action.Fail(primaryAction);
            Assert.Same(primaryAction, await Record.ExceptionAsync(() => action.Original));
            rig.Expect(primaryAction);
            // Two actual Shows +126 accepted banners fill the host's existing128 bound.
            // Every admitted raw dispatch and every real snapshot is retained and joined.
            var admitted = new List<Task>();
            Task? admissionRefusal = null;
            Exception? admissionRefusalError = null;
            Exception? laterRefusal = null;
            for (var index = 0; index < 127; index++) await DispatchAndJoinOriginalAsync(index);
            Assert.Equal(127, action.Calls);
            Assert.Equal(127, admitted.Count);
            var hostDiagnosticsAfterRefusal = 0;
            await rig.Ui(() =>
            {
                var records = Assert.IsAssignableFrom<System.Collections.IEnumerable>(OptionalField(rig.Host, "_originalPublications"));
                var originalOwner = records.Cast<object>().Single(item => ReferenceEquals(OptionalField(item, "Loader"), mountedLoader));
                var errors = Assert.IsType<List<Exception>>(
                    originalOwner.GetType().GetProperty("Errors", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(originalOwner));
                Assert.Equal(2, errors.Count);
                Assert.Same(previousClose, errors[0]);
                Assert.Same(laterRefusal, errors[1]);
                Assert.NotSame(previousClose, laterRefusal);
                var diagnostics = Field<List<CuiDiagnostic>>(mountedLoader!, "_runtimeDiagnostics");
                Assert.Contains(diagnostics, item => item.Code == "CUIA_OBSERVER_FAILED");
                hostDiagnosticsAfterRefusal = rig.Host.Diagnostics.Count;
            });

            // The next click is the128th actual dispatch. Click129 creates the loader's one
            // cached admission refusal; clicks130/131 reuse it without another dispatcher effect.
            for (var index = 127; index < 131; index++) await DispatchAndJoinOriginalAsync(index);
            Assert.Equal(128, action.Calls);
            Assert.Equal(128, admitted.Count);
            Assert.NotNull(admissionRefusal);
            Assert.NotNull(admissionRefusalError);
            await rig.Ui(() =>
            {
                Assert.Same(admissionRefusal, OptionalField(mountedLoader!, "_admissionFailureTask"));
                var records = Assert.IsAssignableFrom<System.Collections.IEnumerable>(OptionalField(rig.Host, "_originalPublications"));
                var originalOwner = records.Cast<object>().Single(item => ReferenceEquals(OptionalField(item, "Loader"), mountedLoader));
                var errors = Assert.IsType<List<Exception>>(
                    originalOwner.GetType().GetProperty("Errors", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(originalOwner));
                Assert.Equal(2, errors.Count);
                Assert.Same(previousClose, errors[0]);
                Assert.Same(laterRefusal, errors[1]);
                Assert.Equal(hostDiagnosticsAfterRefusal, rig.Host.Diagnostics.Count);
            });
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            Assert.Same(close, rig.Host.OriginalClose);
            Assert.Same(close, rig.Host.CloseOriginalAsync());
            var combined = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => close!));
            var dispatchCauses = admitted.SelectMany(task => task.Exception!.InnerExceptions)
                .Concat(admissionRefusal!.Exception!.InnerExceptions).Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
            var disposal = rig.Track(Field<Task>(mountedLoader!, "_disposalTask"));
            var disposalError = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => disposal));
            AssertOriginalCauses(disposalError, dispatchCauses);
            Assert.Same(disposalError, Assert.Single(disposal.Exception!.InnerExceptions));
            rig.Expect(disposalError);
            AssertOriginalCauses(combined, dispatchCauses.Concat([disposalError, previousClose, laterRefusal!]).ToArray());
            rig.Expect(combined);

            async Task DispatchAndJoinOriginalAsync(int index)
            {
                Task? snapshot = null;
                Task? actualDispatch = null;
                Task? actualAdmissionRefusal = null;
                await rig.Ui(() =>
                {
                    var pending = Field<HashSet<Task>>(mountedLoader!, "_pendingActionTasks");
                    var previous = pending.ToArray();
                    actualButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var additions = pending.Where(task => !previous.Any(prior => ReferenceEquals(prior, task))).ToArray();
                    if (index < 128)
                    {
                        actualDispatch = rig.Track(Assert.Single(additions));
                        admitted.Add(actualDispatch);
                    }
                    else Assert.Empty(additions);
                    actualAdmissionRefusal = OptionalField(mountedLoader!, "_admissionFailureTask") as Task;
                    if (index < 128) Assert.Null(actualAdmissionRefusal);
                    else if (admissionRefusal is null) admissionRefusal = rig.Track(actualAdmissionRefusal!);
                    else Assert.Same(admissionRefusal, actualAdmissionRefusal);
                    snapshot = rig.Track(mountedLoader!.WhenActionsIdleAsync());
                });
                if (actualDispatch is not null)
                {
                    var actualError = await Record.ExceptionAsync(() => actualDispatch);
                    Assert.True(actualDispatch.IsFaulted);
                    Assert.Same(actualError, Assert.Single(actualDispatch.Exception!.InnerExceptions));
                    if (index < 126) Assert.Same(primaryAction, actualError);
                    else
                    {
                        var group = Assert.IsType<AggregateException>(actualError);
                        Assert.Equal(2, group.InnerExceptions.Count);
                        Assert.Same(primaryAction, group.InnerExceptions[0]);
                        laterRefusal ??= Assert.IsType<InvalidOperationException>(OptionalField(rig.Host, "_capacityRefusal"));
                        Assert.Same(laterRefusal, group.InnerExceptions[1]);
                    }
                    rig.Expect(actualError!);
                }
                if (index == 128)
                {
                    var group = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => actualAdmissionRefusal!));
                    Assert.True(actualAdmissionRefusal!.IsFaulted);
                    Assert.Same(group, Assert.Single(actualAdmissionRefusal.Exception!.InnerExceptions));
                    Assert.Equal(2, group.InnerExceptions.Count);
                    Assert.IsType<InvalidOperationException>(group.InnerExceptions[0]);
                    Assert.NotSame(laterRefusal, group.InnerExceptions[0]);
                    Assert.Same(laterRefusal, group.InnerExceptions[1]);
                    admissionRefusalError = group;
                    rig.Expect(group);
                }
                var snapshotError = await Record.ExceptionAsync(() => snapshot!);
                Assert.Same(primaryAction, snapshotError);
                Assert.True(snapshot!.IsFaulted);
                var expected = admitted.SelectMany(task => task.Exception!.InnerExceptions)
                    .Concat(actualAdmissionRefusal is null ? Enumerable.Empty<Exception>() : actualAdmissionRefusal.Exception!.InnerExceptions).ToArray();
                AssertOriginalCauseOccurrences(snapshot.Exception!.InnerExceptions, expected);
            }
        });
    }


    [Fact]
    public async Task Request_only_close_from_actual_action_seals_observer_before_queued_UI_retirement()
    {
        var primary = new IOException("Original action after close request.");
        await RunAsync(async rig =>
        {
            var action = new RequestCloseThenFailAction(primary);
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => true })));
            await show!;
            Task? pipeline = null;
            Task? close = null;
            await rig.Ui(() =>
            {
                action.Close = rig.Host.CloseOriginalAsync;
                var loader = Field<CuiControlLoader>(rig.Host, "_loader");
                var diagnosticsBefore = rig.Host.Diagnostics.Count;
                Assert.IsType<Button>(rig.Host.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (action.Original is { } original) rig.Track(original);
                if (action.RequestedClose is { } requested) close = rig.Track(requested);
                pipeline = rig.Track(loader.WhenActionsIdleAsync());
                Assert.NotNull(close);
                Assert.Same(close, rig.Host.OriginalClose);
                Assert.False(close!.IsCompleted);
                Assert.False((bool)OptionalField(rig.Host, "_disposed")!); // Current UI callback has not yielded.
                Assert.Null(rig.Host.LastActionFailure);
                Assert.Equal(diagnosticsBefore, rig.Host.Diagnostics.Count);
            });
            Assert.Same(primary, await Record.ExceptionAsync(() => action.Original!));
            rig.Expect(primary);
            Assert.Same(primary, await Record.ExceptionAsync(() => pipeline!));
            Assert.True(pipeline!.IsFaulted);
            Assert.Same(primary, Assert.Single(pipeline!.Exception!.InnerExceptions));
            // Request-only retirement seals publication before the native owner joins this
            // independently settled action. Close must preserve its exact original failure.
            Assert.Same(primary, await Record.ExceptionAsync(() => close!));
            Assert.True(close!.IsFaulted);
            Assert.Same(primary, Assert.Single(close.Exception!.InnerExceptions));
            Assert.Same(close, rig.Host.OriginalClose);
            Assert.Same(close, rig.Host.CloseOriginalAsync());
        });
    }

    private static CuiNativeScene Scene(ICuiSceneReadiness? readiness = null, ICuiBindingContext? bindings = null,
        ICuiActionDispatcher? actions = null, string? markup = null) =>
        new("test.original-cui", "Original CUI", "Home",
            new CuiRichParser().Parse(markup ?? "<Cui><Actions><Action name=\"Run\" command=\"test.run\" /></Actions><Button action=\"Run\">Run</Button></Cui>"),
            bindings ?? new CuiViewModel(), actions ?? new CuiViewModel(), readiness ?? new ImmediateReadiness());

    private static T Field<T>(object owner, string name) where T : class =>
        Assert.IsAssignableFrom<T>(OptionalField(owner, name));

    private static object? OptionalField(object owner, string name) =>
        (owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(owner.GetType().FullName, name)).GetValue(owner);

    private static void AssertOriginalSourceWithdrawal(CuiSceneHost host, OperationCanceledException withdrawal)
    {
        var records = Assert.IsAssignableFrom<System.Collections.IEnumerable>(OptionalField(host, "_originalPublications"));
        var withdrawals = records.Cast<object>().SelectMany(record =>
            Assert.IsType<List<OperationCanceledException>>(record.GetType()
                .GetProperty("SourceWithdrawals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(record))).ToArray();
        Assert.Same(withdrawal, Assert.Single(withdrawals));
    }

    private static void AssertOriginalCauses(Exception ownerFailure, params Exception[] expected)
    {
        if (expected.Length == 1) Assert.Same(expected[0], ownerFailure);
        else AssertOriginalCauseOccurrences(Assert.IsType<AggregateException>(ownerFailure).InnerExceptions, expected);
    }

    // Only inspect the supplied actual CLR Task wrapper or the specific owner-produced
    // wrapper already asserted by this fixture. Foreign nested/empty groups stay opaque.
    private static void AssertOriginalCauseOccurrences(IEnumerable<Exception> actual, IEnumerable<Exception> expected)
    {
        var remaining = expected.ToList();
        foreach (var cause in actual)
        {
            var index = remaining.FindIndex(prior => ReferenceEquals(prior, cause));
            Assert.True(index >= 0, "The actual source retained an unexpected original cause.");
            remaining.RemoveAt(index);
        }
        Assert.Empty(remaining);
    }

    private static async Task RunAsync(Func<OriginalRig, Task> body, CuiControlRegistry? registry = null)
    {
        HeadlessUnitTestSession? session = null;
        var errors = new List<Exception>();
        try
        {
            session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
            var rig = new OriginalRig(session);
            // Retain one real headless application scope across every original async pipeline.
            await session.Dispatch(async () =>
            {
                try
                {
                    rig.Host = new CuiSceneHost(registry);
                    await body(rig);
                }
                catch (Exception error) { Add(error); }
                finally
                {
                    foreach (var release in rig.Releases)
                    {
                        try { release(); }
                        catch (Exception error) { Add(error); }
                    }
                    foreach (var original in rig.Originals)
                    {
                        try { await original; }
                        catch (Exception error) { if (!rig.Expected.Any(prior => ReferenceEquals(prior, error))) Add(error); }
                    }
                    try
                    {
                        if (rig.Host is { } host)
                        {
                            if (rig.GuardedOwner) await host.CloseOriginalAsync();
                            else host.Dispose();
                        }
                    }
                    catch (Exception error)
                    {
                        if (!rig.Expected.Any(prior => ReferenceEquals(prior, error))) Add(error);
                    }
                }
                return 0;
            }, CancellationToken.None);
        }
        catch (Exception error) { Add(error); }
        finally
        {
            if (session is not null)
            {
                try { await session.DisposeAsync(); }
                catch (Exception error) { Add(error); }
            }
        }
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException("Original fixture and independent cleanup failed.", errors);
        void Add(Exception error) { if (!errors.Any(prior => ReferenceEquals(prior, error))) errors.Add(error); }
    }

    private sealed class OriginalRig(HeadlessUnitTestSession session)
    {
        internal CuiSceneHost Host { get; set; } = null!;
        internal bool GuardedOwner => OptionalField(Host, "_ownerMode") is 2;
        internal List<Task> Originals { get; } = [];
        internal List<Action> Releases { get; } = [];
        internal List<Exception> Expected { get; } = [];
        internal Task Ui(Action callback)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return session.Dispatch(callback, CancellationToken.None);
            callback();
            return Task.CompletedTask;
        }
        internal T Track<T>(T original) where T : Task { Originals.Add(original); return original; }
        internal void Release(Action callback) => Releases.Add(callback);
        internal void Expect(Exception error) => Expected.Add(error);
    }

    private sealed class ImmediateReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Original ready."));
    }

    private sealed class HeldReadiness : ICuiSceneReadiness
    {
        private readonly TaskCompletionSource<CuiSceneAvailability> _original = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<CuiSceneAvailability> Original => _original.Task;
        internal int Calls { get; private set; }
        internal void Complete() => _original.TrySetResult(new(CuiSceneAvailabilityState.Ready, "ready", "Original ready."));
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        { Calls++; return new(Original); }
    }

    private sealed class HeldAction : ICuiActionDispatcher
    {
        private readonly TaskCompletionSource _original = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Original => _original.Task;
        internal int Calls { get; private set; }
        internal void Complete() => _original.TrySetResult();
        internal void Fail(Exception error) => _original.TrySetException(error);
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { Calls++; return new(Original); }
    }


    private sealed class ReplacementAction : ICuiActionDispatcher
    {
        internal Func<Task<CuiSceneAvailability>>? Replace { get; set; }
        internal Task? Original { get; private set; }
        internal int Calls { get; private set; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls++;
            var original = (Replace ?? throw new InvalidOperationException("Replacement was not configured."))();
            Original = original; // The SAME actual Show task, not an artificial success witness.
            return new(original);
        }
    }

    private sealed class ReentrantCloseAction : ICuiActionDispatcher
    {
        private readonly TaskCompletionSource _original = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Original => _original.Task;
        internal Task CancellationObserved => _cancelled.Task;
        internal Func<Task>? Close { get; set; }
        internal Task? ReentrantClose { get; private set; }
        internal void Complete() => _original.TrySetResult();
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            new(RunAsync(cancellationToken));
        private async Task RunAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(() =>
            {
                ReentrantClose = (Close ?? throw new InvalidOperationException("Close was not configured."))();
                _cancelled.TrySetResult();
            });
            await Original.ConfigureAwait(false); // Deliberately ignores cancellation until original work settles.
        }
    }


    private sealed class RequestCloseThenFailAction(Exception primary) : ICuiActionDispatcher
    {
        internal Func<Task>? Close { get; set; }
        internal Task? Original { get; private set; }
        internal Task? RequestedClose { get; private set; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            var original = Original = Task.FromException(primary); // Retain actual failure before reentrant close.
            RequestedClose = (Close ?? throw new InvalidOperationException("Close was not configured."))();
            return new(original); // Request-only: this accepted action never awaits its encompassing close.
        }
    }

    private sealed class OriginalBindings : ICuiBindingContext, INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _changed;
        internal int Adds { get; private set; }
        internal int Removes { get; private set; }
        internal int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        internal Exception? RemoveFailure { get; init; }
        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { Adds++; _changed += value; }
            remove { Removes++; _changed -= value; if (RemoveFailure is not null) throw RemoveFailure; }
        }
        public bool TryGetValue(string path, out object? value) { value = null; return false; }
    }
}
