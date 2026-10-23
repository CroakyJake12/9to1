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
    public async Task Actual_button_can_capture_its_original_loader_pipeline_for_an_external_navigation_owner()
    {
        await RunAsync(async rig =>
        {
            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            Task<CuiSceneAvailability>? show = null;
            Task? settlement = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => true })));
            await show!;
            await rig.Ui(() =>
            {
                action.OnDispatch = () => settlement = rig.Track(rig.Host.CaptureOriginalActionSettlement(show!));
                Assert.IsType<Button>(rig.Host.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.NotNull(settlement);
                Assert.False(settlement!.IsCompleted);
                Assert.NotSame(action.Original, settlement); // Includes the real loader pipeline after dispatcher completion.
                Assert.Null(rig.Host.OriginalClose);
            });
            Assert.Equal(1, action.Calls);
            Assert.False(action.Original.IsCompleted);
            action.Complete();
            await settlement!; // External caller, never the included action itself.
            Assert.True(action.Original.IsCompletedSuccessfully);
            Assert.True(settlement!.IsCompletedSuccessfully);
            await rig.Ui(() => Assert.IsType<Button>(rig.Host.Content));
        });
    }

    [Fact]
    public async Task DispatcherWrapped_recovery_settlement_is_flattened_so_navigation_ack_waits_for_the_real_loader_action()
    {
        await RunAsync(async rig =>
        {
            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            Task<CuiSceneAvailability>? show = null;
            Task? settlement = null;
            Task? unflattenedUpcast = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => true })));
            await show!;
            await rig.Ui(() =>
            {
                ((Button)Assert.IsType<Button>(rig.Host.Content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // EXACT production shape (now flattened): an external navigation owner joins this
                // snapshot and must settle only after the real recovery loader action pipeline drains.
                settlement = rig.Track(Dispatcher.UIThread.InvokeAsync<Task>(
                    () => rig.Host.CaptureOriginalActionSettlement(show!)).GetTask().Unwrap());
                // Regression guard: the unflattened upcast (Task<Task> -> Task) completes when the
                // dispatcher callback RUNS, i.e. BEFORE the genuine loader action settles — the exact
                // navigation-ACK-before-settlement gap this fix closes.
                unflattenedUpcast = rig.Track(Dispatcher.UIThread.InvokeAsync<Task>(
                    () => rig.Host.CaptureOriginalActionSettlement(show!)).GetTask());
            });
            Assert.Equal(1, action.Calls);
            Assert.False(action.Original.IsCompleted);
            await unflattenedUpcast!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(unflattenedUpcast.IsCompleted); // buggy upcast ACK'd as soon as the callback ran
            Assert.False(settlement!.IsCompleted);       // flattened still waits for the held loader action
            action.Complete();
            await settlement;
            Assert.True(action.Original.IsCompletedSuccessfully);
            Assert.True(settlement.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task DispatcherWrapped_recovery_settlement_retains_the_genuine_loader_fault_by_identity()
    {
        await RunAsync(async rig =>
        {
            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            Task<CuiSceneAvailability>? show = null;
            Task? settlement = null;
            Task<Task>? captureAdmission = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => true })));
            await show!;
            await rig.Ui(() =>
            {
                ((Button)Assert.IsType<Button>(rig.Host.Content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                captureAdmission = rig.Track(Dispatcher.UIThread.InvokeAsync<Task>(
                    () => rig.Host.CaptureOriginalActionSettlement(show!)).GetTask());
                settlement = rig.Track(captureAdmission.Unwrap());
            });
            // Wait only for the dispatcher callback to capture the SAME live publication, not for
            // the held loader pipeline. Faulting first can publish the failure wrapper before capture.
            var captured = await Assert.IsAssignableFrom<Task<Task>>(captureAdmission)
                .WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, action.Calls);
            Assert.False(action.Original.IsCompleted);
            Assert.False(captured.IsCompleted);
            Assert.False(Assert.IsAssignableFrom<Task>(settlement).IsCompleted);
            var primary = new InvalidOperationException("genuine recovery loader fault");
            action.Fail(primary);
            Assert.Same(primary, await Record.ExceptionAsync(() => action.Original));
            rig.Expect(primary);
            Assert.Same(primary, await Record.ExceptionAsync(() => settlement!));
            Assert.True(settlement!.IsFaulted);
        });
    }

    [Fact]
    public async Task Actual_failure_banner_preserves_same_primary_capture_held_actions_repeated_banner_and_both_loader_drains()
    {
        await RunAsync(async rig =>
        {
            var cause = new InvalidOperationException("Original action behind the same mounted failure banner.");
            var action = new BannerSequenceAction(cause);
            _ = rig.Track(action.Failed); _ = rig.Track(action.Held.Task); rig.Release(() => action.Held.TrySetResult()); rig.Expect(cause);
            var bindings = new OriginalBindings();
            Task<CuiSceneAvailability>? show = null;
            CuiControlLoader? primary = null, banner = null; Button? root = null; Task? observed = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action, bindings: bindings) with
            { IsPublicationCurrent = () => true })));
            await show!;
            await rig.Ui(() =>
            {
                primary = Field<CuiControlLoader>(rig.Host, "_loader"); root = Assert.IsType<Button>(rig.Host.Content);
                root.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); observed = rig.Track(primary.WhenActionsIdleAsync());
            });
            Assert.Same(cause, await Record.ExceptionAsync(() => observed!));
            await rig.Ui(() =>
            {
                var layout = Assert.IsType<Grid>(rig.Host.Content);
                Assert.Same(root, layout.Children[1]); Assert.Same(primary, Field<CuiControlLoader>(rig.Host, "_loader"));
                banner = Field<CuiControlLoader>(rig.Host, "_failureLoader"); Assert.NotSame(primary, banner);
                observed = rig.Track(rig.Host.CaptureOriginalActionSettlement(show!));
            });
            Assert.Same(cause, await Record.ExceptionAsync(() => observed!)); // After banner publication, not before.
            await rig.Ui(() =>
            {
                root!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                observed = rig.Track(rig.Host.CaptureOriginalActionSettlement(show!));
                Assert.False(observed.IsCompleted); // SAME primary pipeline includes the second actual held source.
            });
            Assert.Equal(2, action.Calls); action.Held.SetResult();
            Assert.Same(cause, await Record.ExceptionAsync(() => observed!));
            await rig.Ui(() =>
            {
                root!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                observed = rig.Track(primary!.WhenActionsIdleAsync());
            });
            Assert.Same(cause, await Record.ExceptionAsync(() => observed!));
            await rig.Ui(() =>
            {
                Assert.Equal(3, action.Calls);
                Assert.Same(primary, Field<CuiControlLoader>(rig.Host, "_loader"));
                Assert.Same(banner, Field<CuiControlLoader>(rig.Host, "_failureLoader"));
                Assert.Same(root, Assert.IsType<Grid>(rig.Host.Content).Children[1]);
                observed = rig.Track(rig.Host.CaptureOriginalActionSettlement(show!));
            });
            Assert.Same(cause, await Record.ExceptionAsync(() => observed!));
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            Assert.Same(cause, await Record.ExceptionAsync(() => close!));
            await rig.Ui(() =>
            {
                Assert.Null(rig.Host.Content); Assert.Equal(1, bindings.Removes);
                var primaryDrain = Field<Task>(primary!, "_disposalTask"); var bannerDrain = Field<Task>(banner!, "_disposalTask");
                Assert.True(primaryDrain.IsFaulted); Assert.True(bannerDrain.IsCompletedSuccessfully);
                Assert.Same(close, rig.Host.CloseOriginalAsync());
                Assert.Same(primaryDrain, Field<Task>(primary!, "_disposalTask"));
                Assert.Same(bannerDrain, Field<Task>(banner!, "_disposalTask"));
            });
        });
    }

    [Theory]
    [InlineData("detached")]
    [InlineData("substituted-child")]
    [InlineData("owner-withdrawn")]
    [InlineData("replacement")]
    [InlineData("closing")]
    public async Task Failure_banner_never_allows_detached_substituted_retired_or_replaced_primary_capture(string change)
    {
        await RunAsync(async rig =>
        {
            var cause = new InvalidOperationException("Original banner guard control.");
            var action = new BannerSequenceAction(cause); _ = rig.Track(action.Failed); rig.Expect(cause);
            var current = true; Task<CuiSceneAvailability>? show = null; Task? pipeline = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with { IsPublicationCurrent = () => current })));
            await show!;
            await rig.Ui(() =>
            {
                Assert.IsType<Button>(rig.Host.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                pipeline = rig.Track(Field<CuiControlLoader>(rig.Host, "_loader").WhenActionsIdleAsync());
            });
            Assert.Same(cause, await Record.ExceptionAsync(() => pipeline!));
            Task<CuiSceneAvailability>? replacement = null; Task? close = null;
            await rig.Ui(() =>
            {
                var layout = Assert.IsType<Grid>(rig.Host.Content);
                switch (change)
                {
                    case "detached": rig.Host.Content = new TextBlock { Text = "Unowned replacement" }; break;
                    case "substituted-child": layout.Children.RemoveAt(1); layout.Children.Add(new Button()); break;
                    case "owner-withdrawn": current = false; break;
                    case "replacement": replacement = rig.Track(rig.Host.ShowAsync(Scene(markup: "<Cui><Text text=\"Replacement\" /></Cui>") with { IsPublicationCurrent = () => true })); break;
                    case "closing": close = rig.Track(rig.Host.CloseOriginalAsync()); break;
                }
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Host.CaptureOriginalActionSettlement(show!); });
            });
            if (replacement is not null) await replacement;
            if (close is not null) Assert.Same(cause, await Record.ExceptionAsync(() => close));
            await rig.Ui(() => Assert.Throws<InvalidOperationException>(() => { _ = rig.Host.CaptureOriginalActionSettlement(show!); }));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_refuses_old_Show_capture_without_substituting_its_new_loader_or_losing_the_old_snapshot(bool queuedStart)
    {
        await RunAsync(async rig =>
        {
            var action = new HeldAction();
            _ = rig.Track(action.Original);
            rig.Release(action.Complete);
            var readiness = new HeldReadiness();
            _ = rig.Track(readiness.Original);
            rig.Release(readiness.Complete);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Release(() => start.TrySetResult());
            Task<CuiSceneAvailability>? original = null, replacement = null;
            Task? settlement = null;
            Control? originalRoot = null;
            await rig.Ui(() => original = rig.Track(rig.Host.ShowAsync(Scene(actions: action) with
            { IsPublicationCurrent = () => true })));
            await original!;
            await rig.Ui(() =>
            {
                originalRoot = Assert.IsType<Button>(rig.Host.Content);
                ((Button)originalRoot).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                settlement = rig.Track(rig.Host.CaptureOriginalActionSettlement(original!));
                var next = Scene(readiness: readiness,
                    markup: "<Cui><Text text=\"Replacement\" /></Cui>") with { IsPublicationCurrent = () => true };
                replacement = rig.Track(queuedStart
                    ? AdmitHeldShow(rig.Host, next, start.Task)
                    : rig.Host.ShowAsync(next));
                if (queuedStart) Assert.Equal(0, readiness.Calls);
                Assert.False(replacement.IsCompleted);
                Assert.Same(originalRoot, rig.Host.Content); // A mounted old root alone is not currentness.
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Host.CaptureOriginalActionSettlement(original!); });
            });
            start.SetResult();
            readiness.Complete();
            await replacement!;
            Assert.Equal(1, readiness.Calls);
            await rig.Ui(() =>
            {
                Assert.IsType<TextBlock>(rig.Host.Content);
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Host.CaptureOriginalActionSettlement(original!); });
                var newSnapshot = rig.Track(rig.Host.CaptureOriginalActionSettlement(replacement!));
                Assert.True(newSnapshot.IsCompletedSuccessfully);
                Assert.False(settlement!.IsCompleted);
            });
            action.Complete();
            await settlement!;
            Assert.True(settlement!.IsCompletedSuccessfully);
        });
    }

    [Fact]
    public async Task Earlier_admitted_Show_cannot_retake_publication_when_its_start_runs_after_a_later_Show()
    {
        await RunAsync(async rig =>
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            rig.Release(() => start.TrySetResult());
            var readiness = new HeldReadiness();
            _ = rig.Track(readiness.Original);
            rig.Release(readiness.Complete);
            Task<CuiSceneAvailability>? earlier = null, later = null;
            Control? laterRoot = null;
            await rig.Ui(() =>
            {
                earlier = rig.Track(AdmitHeldShow(rig.Host, Scene(readiness: readiness) with
                    { IsPublicationCurrent = () => true }, start.Task));
                Assert.Equal(0, readiness.Calls);
                later = rig.Track(rig.Host.ShowAsync(Scene(markup: "<Cui><Text text=\"Later admission\" /></Cui>") with
                    { IsPublicationCurrent = () => true }));
            });
            await later!.WaitAsync(TimeSpan.FromSeconds(10));
            await rig.Ui(() => laterRoot = Assert.IsType<TextBlock>(rig.Host.Content));
            start.SetResult();
            readiness.Complete();
            Assert.Same(earlier, await Task.WhenAny(earlier!, Task.Delay(TimeSpan.FromSeconds(10))));
            var error = await Record.ExceptionAsync(() => earlier!);
            Assert.IsAssignableFrom<OperationCanceledException>(error);
            rig.Expect(error!);
            Assert.True(earlier!.IsCanceled);
            Assert.Equal(1, readiness.Calls);
            await rig.Ui(() =>
            {
                Assert.Same(laterRoot, rig.Host.Content);
                Assert.Equal("Later admission", ((TextBlock)laterRoot!).Text);
                Assert.True(rig.Track(rig.Host.CaptureOriginalActionSettlement(later)).IsCompletedSuccessfully);
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Host.CaptureOriginalActionSettlement(earlier); });
            });
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            Assert.Same(close, await Task.WhenAny(close!, Task.Delay(TimeSpan.FromSeconds(10))));
            Assert.Same(error, await Record.ExceptionAsync(() => close!));
        });
    }

    [Theory]
    [InlineData("withdrawn")]
    [InlineData("detached")]
    [InlineData("closing")]
    [InlineData("closing-from-predicate")]
    public async Task Original_action_capture_refuses_lost_publication_custody(string loss)
    {
        await RunAsync(async rig =>
        {
            var current = true;
            var closeFromPredicate = false;
            Task? close = null;
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene() with
            {
                IsPublicationCurrent = () =>
                {
                    if (closeFromPredicate) close = rig.Track(rig.Host.CloseOriginalAsync());
                    return current;
                }
            })));
            await show!;
            await rig.Ui(() =>
            {
                switch (loss)
                {
                    case "withdrawn": current = false; break;
                    case "detached": rig.Host.Content = new TextBlock { Text = "Unowned replacement" }; break;
                    case "closing": close = rig.Track(rig.Host.CloseOriginalAsync()); break;
                    case "closing-from-predicate": closeFromPredicate = true; break;
                }
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Host.CaptureOriginalActionSettlement(show!); });
            });
            if (close is not null) await close;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Original_action_capture_requires_the_exact_guarded_Show_task(bool legacy)
    {
        await RunAsync(async rig =>
        {
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(legacy ? Scene() : Scene() with
            { IsPublicationCurrent = () => true })));
            var availability = await show!;
            await rig.Ui(() => Assert.Throws<InvalidOperationException>(() =>
            { _ = rig.Host.CaptureOriginalActionSettlement(legacy ? show! : Task.FromResult(availability)); }));
        });
    }

    [Fact]
    public async Task Original_action_capture_requires_UI_access_and_retains_an_actual_owner_predicate_failure()
    {
        await RunAsync(async rig =>
        {
            var failure = new IOException("Original action settlement owner predicate.");
            var fail = false;
            Task<CuiSceneAvailability>? show = null;
            await rig.Ui(() => show = rig.Track(rig.Host.ShowAsync(Scene() with
            { IsPublicationCurrent = () => fail ? throw failure : true })));
            await show!;
            var wrongThread = await Task.Run(() => Record.Exception(() => { _ = rig.Host.CaptureOriginalActionSettlement(show!); }));
            Assert.IsType<InvalidOperationException>(wrongThread);
            await rig.Ui(() =>
            {
                fail = true;
                Assert.Same(failure, Record.Exception(() => { _ = rig.Host.CaptureOriginalActionSettlement(show!); }));
            });
            rig.Expect(failure);
            Task? close = null;
            await rig.Ui(() => close = rig.Track(rig.Host.CloseOriginalAsync()));
            Assert.Same(failure, await Record.ExceptionAsync(() => close!));
        });
    }

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
                replacement = rig.Track(rig.Host.ShowAsync(Scene(markup: "<Cui><Text text=\"Replacement\" /></Cui>") with
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

    // Hold the same start gate used by ShowAsync, after its actual locked admission and
    // original-task custody. This controls continuation order without scheduler timing.
    private static Task<CuiSceneAvailability> AdmitHeldShow(CuiSceneHost host, CuiNativeScene scene, Task start)
    {
        Assert.False(start.IsCompleted);
        var admit = typeof(CuiSceneHost).GetMethod("AdmitOriginalShow", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(CuiSceneHost).FullName, "AdmitOriginalShow");
        return Assert.IsAssignableFrom<Task<CuiSceneAvailability>>(
            admit.Invoke(host, [start, scene, scene.IsPublicationCurrent!, CancellationToken.None]));
    }

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

    private sealed class BannerSequenceAction(Exception cause) : ICuiActionDispatcher
    {
        internal Task Failed { get; } = Task.FromException(cause);
        internal TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            new(++Calls == 2 ? Held.Task : Failed);
    }

    private sealed class HeldAction : ICuiActionDispatcher
    {
        private readonly TaskCompletionSource _original = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task Original => _original.Task;
        internal int Calls { get; private set; }
        internal Action? OnDispatch { get; set; }
        internal void Complete() => _original.TrySetResult();
        internal void Fail(Exception error) => _original.TrySetException(error);
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { Calls++; OnDispatch?.Invoke(); return new(Original); }
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
