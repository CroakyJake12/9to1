using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>Real awaited domain actions keep their original receipts, while authored results and
/// Set transitions belong only to the still-current originating control/frame.</summary>
public sealed class CuiActionFrameCompletionTests
{
    private const string Markup = """
        <Cui>
          <Variable Type="bool" ID="ShowStart">True</Variable>
          <Variable Type="commandstate" ID="OpenResult" />
          <Actions>
            <Action name="open" command="domain.open" result="OpenResult">
              <Set Target="ShowStart" Value="False" When="{Binding OpenResult.Succeeded}" />
            </Action>
            <Action name="back"><Set Target="ShowStart" Value="True" /></Action>
          </Actions>
          <Container Type="Vertical Stack">
            <Container id="start" Type="Vertical Stack" Active="{Binding ShowStart}">
              <If Condition="{Binding OriginVisible}">
                <Button id="open" Action="open" Content="Open" />
              </If>
            </Container>
            <Container id="editor" Type="Vertical Stack" Hidden="{Binding ShowStart}">
              <Button id="back" Action="back" Content="All documents" />
            </Container>
            <Text id="outcome" Text="{Binding OpenResult.Code}" />
          </Container>
        </Cui>
        """;

    [Fact]
    public Task Current_genuine_success_applies_result_and_authored_transition_then_local_back_works() => RunAsync(async fixture =>
    {
        var root = fixture.Mount();
        var receipt = fixture.Start(root);
        Assert.True(Find<Control>(root, "start").IsVisible);
        Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
        fixture.Dispatcher.Complete(new(true, false, "Published", null, null));
        await receipt;
        Assert.False(Find<Control>(root, "start").IsVisible);
        Assert.Equal(1d, Find<Control>(root, "editor").Opacity);
        Assert.Equal("Published", Find<TextBlock>(root, "outcome").Text);
        Click(Find<Button>(root, "back"));
        await fixture.Loader.WhenActionsIdleAsync();
        Assert.True(Find<Control>(root, "start").IsVisible);
        Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
        Assert.Equal(1, fixture.Dispatcher.Calls);
    });

    [Fact]
    public Task Pending_domain_request_does_not_pretend_to_publish_a_session() => RunAsync(async fixture =>
    {
        var root = fixture.Mount();
        var receipt = fixture.Start(root);
        fixture.Dispatcher.Complete(new(false, false, "PendingHomeApproval", null, null));
        await receipt;
        Assert.True(Find<Control>(root, "start").IsVisible);
        Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
        Assert.Equal("PendingHomeApproval", Find<TextBlock>(root, "outcome").Text);
    });

    [Fact]
    public Task Old_success_cannot_write_same_named_results_or_sets_into_replacement_frame() => RunAsync(async fixture =>
    {
        var original = fixture.Mount();
        var receipt = fixture.Start(original);
        var replacement = fixture.Mount();
        Assert.NotSame(original, replacement);
        fixture.Dispatcher.Complete(new(true, false, "OldPublished", null, null));
        await receipt;
        AssertStartUnchanged(replacement);
        Assert.Equal(1, fixture.Dispatcher.Calls);
    });

    [Fact]
    public Task Rejected_load_attempt_preserves_original_frame_and_its_real_completion() => RunAsync(async fixture =>
    {
        var original = fixture.Mount();
        var receipt = fixture.Start(original);
        var (rejected, diagnostics) = fixture.Loader.LoadMarkup("""
            <Cui><Variable Type="bool" ID="Invalid">not-a-bool</Variable><Container /></Cui>
            """, "rejected-frame.cui");
        Assert.Null(rejected);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR077");
        Assert.Same(original, fixture.Window.Content);
        fixture.Dispatcher.Complete(new(true, false, "RetainedPublished", null, null));
        await receipt;
        Assert.False(Find<Control>(original, "start").IsVisible);
        Assert.Equal("RetainedPublished", Find<TextBlock>(original, "outcome").Text);
    });

    [Fact]
    public Task Disposed_loader_drains_actual_action_without_publishing_its_late_success() => RunAsync(async fixture =>
    {
        var original = fixture.Mount();
        var receipt = fixture.Start(original);
        var start = Find<Control>(original, "start");
        var editor = Find<Control>(original, "editor");
        var outcome = Find<TextBlock>(original, "outcome");
        fixture.Loader.Dispose();
        var retiredState = (start.IsVisible, editor.Opacity, outcome.Text);
        Assert.False(receipt.IsCompleted);
        fixture.Dispatcher.Complete(new(true, false, "LatePublished", null, null));
        await receipt;
        await fixture.Loader.WhenActionsIdleAsync();
        Assert.Equal(retiredState, (start.IsVisible, editor.Opacity, outcome.Text));
    });

    [Fact]
    public Task Retiring_originating_conditional_branch_withdraws_its_late_ui_transition() => RunAsync(async fixture =>
    {
        var root = fixture.Mount();
        var receipt = fixture.Start(root);
        fixture.Model.Set("OriginVisible", false);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "open");
        fixture.Dispatcher.Complete(new(true, false, "RetiredPublished", null, null));
        await receipt;
        AssertStartUnchanged(root);
    });

    [Fact]
    public Task Reentrant_result_observer_cannot_apply_remaining_sets_to_new_frame() => RunAsync(async fixture =>
    {
        var original = fixture.Mount(Markup.Replace(" When=\"{Binding OpenResult.Succeeded}\"", ""));
        Control? replacement = null;
        Find<TextBlock>(original, "outcome").PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBlock.TextProperty && Equals(args.NewValue, "ReplaceNow"))
                replacement = fixture.Mount();
        };
        var receipt = fixture.Start(original);
        fixture.Dispatcher.Complete(new(true, false, "ReplaceNow", null, null));
        await receipt;
        Assert.NotNull(replacement);
        AssertStartUnchanged(replacement!);
    });

    [Fact]
    public Task Result_observer_retiring_origin_in_same_frame_withdraws_remaining_sets() => RunAsync(async fixture =>
    {
        var root = fixture.Mount();
        Find<TextBlock>(root, "outcome").PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBlock.TextProperty && Equals(args.NewValue, "RetireNow"))
                fixture.Model.Set("OriginVisible", false);
        };
        var receipt = fixture.Start(root);
        fixture.Dispatcher.Complete(new(true, false, "RetireNow", null, null));
        await receipt;
        Assert.True(Find<Control>(root, "start").IsVisible);
        Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
        Assert.Equal("RetireNow", Find<TextBlock>(root, "outcome").Text);
        Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "open");
    });

    [Fact]
    public Task Root_conditional_wrapper_preserves_frame_owned_declarative_effects() => RunAsync(async fixture =>
    {
        var markup = Markup
            .Replace("<Set Target=\"ShowStart\" Value=\"False\" When=\"{Binding OpenResult.Succeeded}\" />", "")
            .Replace("<Action name=\"back\">", "<Action name=\"after-open\"><Set Target=\"ShowStart\" Value=\"False\" /></Action><Action name=\"back\">")
            .Replace("</Actions>", "</Actions><Effects><Effect Condition=\"{Binding OpenResult.Succeeded}\" Action=\"after-open\" /></Effects><If Condition=\"{Binding OriginVisible}\">")
            .Replace("</Cui>", "</If></Cui>");
        var root = fixture.Mount(markup);
        Assert.IsType<ContentControl>(root); // actual loader-owned root If wrapper
        var receipt = fixture.Start(root);
        fixture.Dispatcher.Complete(new(true, false, "EffectPublished", null, null));
        await receipt;
        await fixture.Loader.WhenActionsIdleAsync();
        Assert.False(Find<Control>(root, "start").IsVisible);
        Assert.Equal(1d, Find<Control>(root, "editor").Opacity);
        Assert.Equal("EffectPublished", Find<TextBlock>(root, "outcome").Text);
    });

    [Fact]
    public Task Later_set_guard_retirement_preserves_observable_prior_writes_without_remaining_writes() => RunAsync(async fixture =>
    {
        fixture.Loader.SetBindingContext(new GuardContext(fixture.Model,
            () => fixture.Model.Set("OriginVisible", false)));
        var markup = Markup
            .Replace("<Variable Type=\"commandstate\"", "<Variable Type=\"bool\" ID=\"SecondFlag\">True</Variable><Variable Type=\"commandstate\"")
            .Replace("<Set Target=\"ShowStart\" Value=\"False\" When=\"{Binding OpenResult.Succeeded}\" />",
                "<Set Target=\"ShowStart\" Value=\"False\" /><Set Target=\"SecondFlag\" Value=\"False\" When=\"{Binding RetireAllowed}\" />")
            .Replace("<Text id=\"outcome\"", "<Container id=\"second\" Type=\"Vertical Stack\" Active=\"{Binding SecondFlag}\" /><Text id=\"outcome\"");
        var root = fixture.Mount(markup);
        var receipt = fixture.Start(root);
        fixture.Dispatcher.Complete(new(true, false, "Published", null, null));
        await receipt;
        Assert.False(Find<Control>(root, "start").IsVisible);
        Assert.Equal(1d, Find<Control>(root, "editor").Opacity);
        Assert.True(Find<Control>(root, "second").IsVisible);
        Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => button.Name == "open");
    });

    [Fact]
    public Task Original_set_guard_failure_and_later_refresh_failure_both_remain_in_actual_receipt() => RunAsync(async fixture =>
    {
        var guardFailure = new IOException("Original guard failed.");
        var observerFailure = new InvalidOperationException("The committed-state observer failed.");
        fixture.ExpectedFailures.AddRange([guardFailure, observerFailure]);
        fixture.Loader.SetBindingContext(new GuardContext(fixture.Model, () => throw guardFailure));
        var markup = Markup.Replace("<Set Target=\"ShowStart\" Value=\"False\" When=\"{Binding OpenResult.Succeeded}\" />",
            "<Set Target=\"ShowStart\" Value=\"False\" /><Set Target=\"ShowStart\" Value=\"True\" When=\"{Binding RetireAllowed}\" />");
        var root = fixture.Mount(markup);
        Find<Control>(root, "editor").PropertyChanged += (_, args) =>
        {
            if (args.Property == Control.OpacityProperty && Equals(args.NewValue, 1d)) throw observerFailure;
        };
        var receipt = fixture.Start(root);
        fixture.Dispatcher.Complete(new(true, false, "Published", null, null));
        await Assert.ThrowsAnyAsync<Exception>(() => receipt);
        var causes = receipt.Exception!.Flatten().InnerExceptions;
        Assert.Contains(causes, error => ReferenceEquals(error, guardFailure));
        Assert.Contains(causes, error => ReferenceEquals(error, observerFailure));
    });

    [Fact]
    public Task Failure_observer_reloading_then_throwing_keeps_both_causes_without_new_frame_diagnostics() => RunAsync(async fixture =>
    {
        var original = fixture.Mount();
        var receipt = fixture.Start(original);
        var domainFailure = new IOException("Original domain failure.");
        var observerFailure = new InvalidOperationException("Original error observer failed.");
        fixture.ExpectedFailures.AddRange([domainFailure, observerFailure]);
        Control? replacement = null;
        fixture.Loader.ActionFailed += (_, _) =>
        {
            replacement = fixture.Mount();
            throw observerFailure;
        };
        fixture.Dispatcher.Fail(domainFailure);
        await Assert.ThrowsAnyAsync<Exception>(() => receipt);
        Assert.NotNull(replacement);
        AssertStartUnchanged(replacement!);
        Assert.Empty(fixture.Loader.RuntimeDiagnostics);
        var causes = receipt.Exception!.Flatten().InnerExceptions;
        Assert.Contains(causes, error => ReferenceEquals(error, domainFailure));
        Assert.Contains(causes, error => ReferenceEquals(error, observerFailure));
    });

    [Fact]
    public Task Same_frame_failure_observer_still_publishes_its_diagnostic_and_retains_both_causes() => RunAsync(async fixture =>
    {
        var root = fixture.Mount();
        var receipt = fixture.Start(root);
        var domainFailure = new IOException("Current domain failure.");
        var observerFailure = new InvalidOperationException("Current error observer failed.");
        fixture.ExpectedFailures.AddRange([domainFailure, observerFailure]);
        fixture.Loader.ActionFailed += (_, _) => throw observerFailure;
        fixture.Dispatcher.Fail(domainFailure);
        await Assert.ThrowsAnyAsync<Exception>(() => receipt);
        Assert.Contains(fixture.Loader.RuntimeDiagnostics, diagnostic => diagnostic.Code == "CUIA_FAILED");
        Assert.Contains(fixture.Loader.RuntimeDiagnostics, diagnostic => diagnostic.Code == "CUIA_OBSERVER_FAILED");
        Assert.True(Find<Control>(root, "start").IsVisible);
        Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
        var causes = receipt.Exception!.Flatten().InnerExceptions;
        Assert.Contains(causes, error => ReferenceEquals(error, domainFailure));
        Assert.Contains(causes, error => ReferenceEquals(error, observerFailure));
    });

    [Fact]
    public Task Rejected_load_keeps_actual_declarative_effect_armed_for_pending_original_completion() => RunAsync(async fixture =>
    {
        var markup = Markup
            .Replace("<Set Target=\"ShowStart\" Value=\"False\" When=\"{Binding OpenResult.Succeeded}\" />", "")
            .Replace("<Action name=\"back\">", "<Action name=\"after-open\"><Set Target=\"ShowStart\" Value=\"False\" /></Action><Action name=\"back\">")
            .Replace("</Actions>", "</Actions><Effects><Effect Condition=\"{Binding OpenResult.Succeeded}\" Action=\"after-open\" /></Effects>");
        var original = fixture.Mount(markup);
        var receipt = fixture.Start(original);
        var (rejected, diagnostics) = fixture.Loader.LoadMarkup("""
            <Cui><Variable Type="bool" ID="Invalid">not-a-bool</Variable><Container /></Cui>
            """, "rejected-effect-frame.cui");
        Assert.Null(rejected);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR077");
        Assert.Same(original, fixture.Window.Content);
        Assert.True(Find<Control>(original, "start").IsVisible);
        fixture.Dispatcher.Complete(new(true, false, "EffectAfterRejection", null, null));
        await receipt;
        await fixture.Loader.WhenActionsIdleAsync();
        // No direct command Set remains: only the actually retained Effect can change this state.
        Assert.False(Find<Control>(original, "start").IsVisible);
        Assert.Equal(1d, Find<Control>(original, "editor").Opacity);
        Assert.Equal("EffectAfterRejection", Find<TextBlock>(original, "outcome").Text);
    });

    [Fact]
    public Task Old_failure_keeps_original_receipt_without_reporting_failure_on_replacement() => RunAsync(async fixture =>
    {
        var original = fixture.Mount();
        var receipt = fixture.Start(original);
        var replacement = fixture.Mount();
        var reported = 0;
        fixture.Loader.ActionFailed += (_, _) => reported++;
        var originalFailure = new IOException("Original domain operation failed.");
        fixture.ExpectedFailures.Add(originalFailure);
        fixture.Dispatcher.Fail(originalFailure);
        await Assert.ThrowsAnyAsync<Exception>(() => receipt);
        Assert.Contains(receipt.Exception!.Flatten().InnerExceptions, error => ReferenceEquals(error, originalFailure));
        Assert.Equal(0, reported);
        AssertStartUnchanged(replacement);
    });

    private static void AssertStartUnchanged(Control root)
    {
        Assert.True(Find<Control>(root, "start").IsVisible);
        Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
        Assert.True(string.IsNullOrEmpty(Find<TextBlock>(root, "outcome").Text));
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.Single(AuthoredTree(root).OfType<T>(), control => control.Name == name);

    // Active=False retains authored cells outside native layout. Test their real controls and
    // states instead of assuming hidden controls remain visual descendants of the window.
    private static IEnumerable<Control> AuthoredTree(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container
            ? container.AuthoredChildren
            : root.GetVisualChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var descendant in AuthoredTree(child)) yield return descendant;
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task RunAsync(Func<Fixture, Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<int>(async () =>
        {
            var fixture = new Fixture();
            var failures = new List<Exception>();
            try { await test(fixture); }
            catch (Exception error) { failures.Add(error); }
            try { await fixture.CloseAndDrainAsync(); }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException(failures);
            return 0;
        }, CancellationToken.None);
    }

    private sealed class Fixture
    {
        internal CuiControlLoader Loader { get; } = new();
        internal CuiViewModel Model { get; } = new();
        internal DelayedDispatcher Dispatcher { get; } = new();
        internal Window Window { get; } = new() { Width = 600, Height = 400 };
        internal List<Exception> ExpectedFailures { get; } = [];

        internal Fixture()
        {
            Model.Set("OriginVisible", true);
            Loader.SetBindingContext(Model);
            Loader.SetActionDispatcher(Dispatcher);
            Loader.SetSurface("Canvas");
            Loader.SetAppearance(CuiAppearance.Dark);
        }

        internal Control Mount(string markup = Markup)
        {
            var (root, diagnostics) = Loader.LoadMarkup(markup, "frame-completion.cui");
            Assert.Empty(diagnostics);
            Assert.NotNull(root);
            Window.Content = root;
            Loader.WireBindings(root!);
            Window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            return root!;
        }

        internal Task Start(Control root)
        {
            Click(Find<Button>(root, "open"));
            Assert.Equal(1, Dispatcher.Calls);
            var receipt = Loader.WhenActionsIdleAsync();
            Assert.False(receipt.IsCompleted);
            return receipt;
        }

        internal async Task CloseAndDrainAsync()
        {
            var failures = new List<Exception>();
            Dispatcher.ReleaseForCleanup();
            try { Window.Close(); } catch (Exception error) { failures.Add(error); }
            try { Loader.Dispose(); } catch (Exception error) { failures.Add(error); }
            // Snapshot only AFTER retirement has published its physical-close receipts.
            var drain = Loader.WhenActionsIdleAsync();
            try { await drain; }
            catch (Exception error)
            {
                if (drain.Exception is not { } actual ||
                    !actual.Flatten().InnerExceptions.All(item => ExpectedFailures.Any(expected => ReferenceEquals(item, expected))))
                    failures.Add(error);
            }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException(failures);
        }
    }

    private sealed class GuardContext(CuiViewModel model, Action retire) : ICuiBindingContext, System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        { add => model.PropertyChanged += value; remove => model.PropertyChanged -= value; }
        public bool TryGetValue(string path, out object? value)
        {
            if (path == "RetireAllowed") { retire(); value = true; return true; }
            return model.TryGetValue(path, out value);
        }
    }

    private sealed class DelayedDispatcher : ICuiResultReportingDispatcher
    {
        private readonly TaskCompletionSource<CuiCommandOutcome> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        public ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("domain.open", command);
            Calls++;
            // Deliberately finish independently of cancellation: UI retirement must not claim
            // that a domain owner stopped, or lose its actual terminal receipt.
            return new(_completion.Task);
        }
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { _ = await DispatchWithOutcomeAsync(command, parameter, cancellationToken); }
        internal void ReleaseForCleanup() => _completion.TrySetResult(new(false, true, "TestCleanup", null, null));
        internal void Complete(CuiCommandOutcome outcome) => _completion.SetResult(outcome);
        internal void Fail(Exception failure) => _completion.SetException(failure);
    }
}
