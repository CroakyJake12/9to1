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
        AssertRetainedExpectedCause(fixture, receipt, observerFailure);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Domain_failure_and_result_observer_failure_both_survive_with_or_without_replacement(bool replace) => RunAsync(async fixture =>
    {
        var root = fixture.Mount();
        var domainFailure = new IOException("Exact domain failure before publication.");
        var publicationFailure = new InvalidOperationException("Exact result observer failure.");
        fixture.ExpectedFailures.AddRange([domainFailure, publicationFailure]);
        Control? replacement = null;
        Find<TextBlock>(root, "outcome").PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBlock.TextProperty && Equals(args.NewValue, "Failed"))
            {
                if (replace) replacement = fixture.Mount();
                throw publicationFailure;
            }
        };
        var receipt = fixture.Start(root);
        fixture.Dispatcher.Fail(domainFailure);
        await Assert.ThrowsAnyAsync<Exception>(() => receipt);
        var causes = receipt.Exception!.Flatten().InnerExceptions;
        Assert.Contains(causes, cause => ReferenceEquals(cause, domainFailure));
        AssertRetainedExpectedCause(fixture, receipt, publicationFailure);
        Assert.True(fixture.Dispatcher.OriginalTask.IsFaulted);
        if (replace)
        {
            Assert.NotNull(replacement); AssertStartUnchanged(replacement!);
            Assert.Empty(fixture.Loader.RuntimeDiagnostics);
        }
        else
        {
            Assert.Equal("Failed", Find<TextBlock>(root, "outcome").Text);
            Assert.True(Find<Control>(root, "start").IsVisible);
            Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
        }
    });

    [Fact]
    public Task Domain_failure_and_declarative_set_guard_failure_both_remain_in_original_receipt() => RunAsync(async fixture =>
    {
        var domainFailure = new IOException("Exact domain failure before Set.");
        var guardFailure = new InvalidOperationException("Exact outcome Set guard failure.");
        fixture.ExpectedFailures.AddRange([domainFailure, guardFailure]);
        fixture.Loader.SetBindingContext(new GuardContext(fixture.Model, () => throw guardFailure));
        var root = fixture.Mount(Markup.Replace("When=\"{Binding OpenResult.Succeeded}\"", "When=\"{Binding RetireAllowed}\""));
        var receipt = fixture.Start(root);
        fixture.Dispatcher.Fail(domainFailure);
        await Assert.ThrowsAnyAsync<Exception>(() => receipt);
        var causes = receipt.Exception!.Flatten().InnerExceptions;
        Assert.Contains(causes, cause => ReferenceEquals(cause, domainFailure));
        Assert.Contains(causes, cause => ReferenceEquals(cause, guardFailure));
        Assert.Equal("Failed", Find<TextBlock>(root, "outcome").Text);
        Assert.True(Find<Control>(root, "start").IsVisible);
        Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Actual_canceled_or_faulted_OCE_task_and_result_observer_failure_are_retained(bool faulted) => RunAsync(async fixture =>
    {
        var root = fixture.Mount();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var exactFault = new OperationCanceledException("Exact faulted cancellation payload.", cancellation.Token);
        var publicationFailure = new IOException("Exact cancellation outcome observer failure.");
        fixture.ExpectedFailures.Add(publicationFailure);
        Find<TextBlock>(root, "outcome").PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBlock.TextProperty && Equals(args.NewValue, "Cancelled")) throw publicationFailure;
        };
        var receipt = fixture.Start(root);
        var original = fixture.Dispatcher.OriginalTask;
        if (faulted) fixture.Dispatcher.Fail(exactFault);
        else fixture.Dispatcher.Cancel(cancellation.Token);
        await Assert.ThrowsAnyAsync<Exception>(() => receipt);
        var causes = receipt.Exception!.Flatten().InnerExceptions;
        var actualCancellation = Assert.Single(causes.OfType<OperationCanceledException>());
        Assert.Equal(cancellation.Token, actualCancellation.CancellationToken);
        if (faulted) Assert.Same(exactFault, actualCancellation);
        fixture.ExpectedFailures.Add(actualCancellation); // exact observed receipt, never a close waiver
        AssertRetainedExpectedCause(fixture, receipt, publicationFailure);
        Assert.Equal(faulted, original.IsFaulted); Assert.Equal(!faulted, original.IsCanceled);
        Assert.True(receipt.IsFaulted); // retains faults; never relabels original task state
        Assert.True(Find<Control>(root, "start").IsVisible);
        Assert.Equal(0d, Find<Control>(root, "editor").Opacity);
    });

    [Fact]
    public void Diagnostic_attestation_and_cleanup_reject_lost_cloned_opaque_sibling_and_unowned_group_failures()
    {
        var expected = new InvalidOperationException("Original observer identity.");
        var diagnostic = new CuiDiagnostic("CUIR053", CuiDiagnosticSeverity.Error, "Structured property failure.", default);
        var retained = new CuiRuntimeLoadException(diagnostic, expected);
        Assert.True(IsExactExpectedCause(retained, expected));
        Assert.Same(diagnostic, retained.Diagnostic); Assert.Same(expected, retained.InnerException);
        Assert.False(IsExactExpectedCause(new CuiRuntimeLoadException(diagnostic), expected));
        Assert.False(IsExactExpectedCause(new CuiRuntimeLoadException(diagnostic, new InvalidOperationException(expected.Message)), expected));
        Assert.False(IsExactExpectedCause(new CuiRuntimeLoadException(diagnostic, new Exception("Opaque source.", expected)), expected));
        Exception excessive = expected;
        for (var i = 0; i < 33; i++) excessive = new CuiRuntimeLoadException(diagnostic, excessive);
        Assert.False(IsExactExpectedCause(excessive, expected));
        var actualGroup = new AggregateException(expected, retained);
        var allowed = new List<Exception> { expected, retained };
        var knownGroups = new HashSet<Exception>(ReferenceEqualityComparer.Instance) { actualGroup };
        var opaque = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        Assert.True(MatchFailureGraph([actualGroup], allowed, knownGroups, opaque, false, out _));
        Assert.False(MatchFailureGraph([new AggregateException(expected, retained)], allowed, knownGroups, opaque, false, out _));
        Assert.False(MatchFailureGraph([actualGroup, new IOException("Unknown sibling.")], allowed, knownGroups, opaque, false, out _));
        var opaqueSource = new AggregateException("Opaque source, not runtime composition.", expected);
        opaque.Add(opaqueSource);
        Assert.False(MatchFailureGraph([opaqueSource], allowed, knownGroups, opaque, true, out _));
        Assert.False(MatchFailureGraph([new AggregateException()], allowed, knownGroups, opaque, true, out _));
    }

    private const string LocalParameterMarkup = """
        <Cui>
          <Variable Type="commandstate" ID="Receipt" />
          <Actions>
            <Action name="observe" command="parameter.observe" result="Receipt" />
            <Action name="use" command="parameter.use" parameter="{Binding Receipt.Value}" />
          </Actions>
          <Container Type="Vertical Stack">
            <Button id="observe-parameter" Action="observe" Content="Observe" />
            <Button id="use-parameter" Action="use" Content="Use original" />
          </Container>
        </Cui>
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Local_command_result_parameter_keeps_exact_issued_identity_for_button_and_effect(bool effect) => RunAsync(async fixture =>
    {
        var issued = new object(); var source = new ParameterDispatcher(issued);
        fixture.Loader.SetActionDispatcher(source);
        var markup = effect ? LocalParameterMarkup.Replace("</Actions>",
            "</Actions><Effects><Effect Condition=\"{Binding Receipt.Succeeded}\" Action=\"use\" /></Effects>") : LocalParameterMarkup;
        var root = fixture.Mount(markup);
        Click(Find<Button>(root, "observe-parameter")); await fixture.Loader.WhenActionsIdleAsync();
        if (!effect) Click(Find<Button>(root, "use-parameter"));
        await fixture.Loader.WhenActionsIdleAsync();
        Assert.True(ReferenceEquals(issued, Assert.Single(source.Parameters)),
            "CUI_LOCAL_COMMAND_RESULT_PARAMETER_IDENTITY_LOST: forward the exact issued opaque local result, without a host mirror or clone.");
    });

    [Fact]
    public Task Local_command_result_parameter_reads_latest_receipt_on_the_same_wired_button() => RunAsync(async fixture =>
    {
        var first = new object(); var latest = new object(); var source = new ParameterDispatcher(first, latest);
        fixture.Loader.SetActionDispatcher(source); var root = fixture.Mount(LocalParameterMarkup);
        var observe = Find<Button>(root, "observe-parameter"); var use = Find<Button>(root, "use-parameter");
        Click(observe); await fixture.Loader.WhenActionsIdleAsync(); Click(use); await fixture.Loader.WhenActionsIdleAsync();
        Click(observe); await fixture.Loader.WhenActionsIdleAsync();
        Assert.Same(use, Find<Button>(root, "use-parameter")); Click(use); await fixture.Loader.WhenActionsIdleAsync();
        Assert.Equal(2, source.Parameters.Count); Assert.Same(first, source.Parameters[0]); Assert.Same(latest, source.Parameters[1]);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task Local_command_result_parameter_never_borrows_a_colliding_external_path(int state) => RunAsync(async fixture =>
    {
        var spoof = new object(); var issued = new object();
        fixture.Model.Set("Receipt.Value", spoof);
        var source = new ParameterDispatcher(new object?[] { state == 2 ? issued : null });
        fixture.Loader.SetActionDispatcher(source); var root = fixture.Mount(LocalParameterMarkup);
        if (state != 0) { Click(Find<Button>(root, "observe-parameter")); await fixture.Loader.WhenActionsIdleAsync(); }
        Click(Find<Button>(root, "use-parameter")); await fixture.Loader.WhenActionsIdleAsync();
        var actual = Assert.Single(source.Parameters);
        if (state == 2) Assert.Same(issued, actual); else Assert.Null(actual);
        Assert.NotSame(spoof, actual);
    });

    [Fact]
    public Task Local_parameter_repair_preserves_latest_external_repeat_row_and_its_query_owner() => RunAsync(async fixture =>
    {
        var first = new Dictionary<string, object?> { ["Id"] = "stable", ["Name"] = "First" };
        var latest = new Dictionary<string, object?> { ["Id"] = "stable", ["Name"] = "Latest" };
        var source = new ParameterDispatcher(); fixture.Loader.SetActionDispatcher(source);
        fixture.Model.Set("Source", new ParameterRows(first));
        var root = fixture.Mount("""
            <Cui><Actions><Action name="use-row" command="parameter.use" parameter="{Binding row}" /></Actions>
              <Container Type="QueryViewport" Source="{Binding Source}">
                <Repeat Source="{Binding Items}" As="row" Key="{Binding row.Id}">
                  <Button id="parameter-row" Action="use-row" Content="{Binding row.Name}" />
                </Repeat>
              </Container>
            </Cui>
            """);
        var button = Find<Button>(root, "parameter-row");
        Click(button); await fixture.Loader.WhenActionsIdleAsync();
        fixture.Model.Set("Source", new ParameterRows(latest));
        fixture.Window.UpdateLayout();
        Assert.Same(button, Find<Button>(root, "parameter-row"));
        // Detaching native ancestry cannot replace the captured authored query/Repeat scope.
        fixture.Window.Content = null; fixture.Model.Set("row", new object());
        Click(button); await fixture.Loader.WhenActionsIdleAsync();
        Assert.Equal(2, source.Parameters.Count); Assert.Same(first, source.Parameters[0]); Assert.Same(latest, source.Parameters[1]);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Local_command_result_parameter_old_controls_cannot_read_replaced_or_disposed_frame(bool dispose) => RunAsync(async fixture =>
    {
        var first = new object(); var latest = new object(); var source = new ParameterDispatcher(first, latest);
        fixture.Loader.SetActionDispatcher(source); var root = fixture.Mount(LocalParameterMarkup);
        Click(Find<Button>(root, "observe-parameter")); await fixture.Loader.WhenActionsIdleAsync();
        var old = Find<Button>(root, "use-parameter");
        if (dispose) fixture.Loader.Dispose();
        else
        {
            var replacement = fixture.Mount(LocalParameterMarkup);
            Click(Find<Button>(replacement, "observe-parameter")); await fixture.Loader.WhenActionsIdleAsync();
        }
        Click(old); await fixture.Loader.WhenActionsIdleAsync(); Assert.Empty(source.Parameters);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Local_command_result_parameter_getter_reentry_cannot_dispatch_after_frame_retirement(bool dispose) => RunAsync(async fixture =>
    {
        Control? replacement = null; var retained = new object(); var latest = new object();
        var getter = new ParameterMember(() =>
        {
            Assert.False(fixture.Loader.WhenActionsIdleAsync().IsCompleted);
            if (dispose) fixture.Loader.Dispose(); else replacement = fixture.Mount(LocalParameterMarkup);
            return retained;
        });
        var source = new ParameterDispatcher(getter, latest); fixture.Loader.SetActionDispatcher(source);
        var markup = LocalParameterMarkup.Replace("parameter=\"{Binding Receipt.Value}\"", "parameter=\"{Binding Receipt.Value.Payload}\"");
        var root = fixture.Mount(markup);
        Click(Find<Button>(root, "observe-parameter")); await fixture.Loader.WhenActionsIdleAsync();
        Click(Find<Button>(root, "use-parameter")); await fixture.Loader.WhenActionsIdleAsync();
        Assert.Equal(1, getter.Reads); Assert.Empty(source.Parameters);
        if (!dispose)
        {
            Assert.NotNull(replacement);
            Click(Find<Button>(replacement!, "observe-parameter")); await fixture.Loader.WhenActionsIdleAsync();
            Click(Find<Button>(replacement!, "use-parameter")); await fixture.Loader.WhenActionsIdleAsync();
            Assert.Same(latest, Assert.Single(source.Parameters));
        }
    });

    private sealed class ParameterDispatcher(params object?[] results) : ICuiResultReportingDispatcher
    {
        private readonly Queue<object?> _results = new(results);
        internal List<object?> Parameters { get; } = [];
        public ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (command == "parameter.observe")
                return new(new CuiCommandOutcome(true, false, "Observed", null, _results.Dequeue()));
            Assert.Equal("parameter.use", command); Parameters.Add(parameter);
            return new(new CuiCommandOutcome(true, false, "Used", null, null));
        }
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { _ = await DispatchWithOutcomeAsync(command, parameter, cancellationToken); }
    }

    private sealed class ParameterRows(object row) : ICuiPagedSource
    {
        public CuiPagedResult Evaluate(string? query, CuiPageCursor? cursor) => new(new object[] { row }, 1, 0, null, null);
    }

    private sealed class ParameterMember(Func<object?> read)
    {
        internal int Reads;
        public object? Payload { get { ++Reads; return read(); } }
    }

    private static bool IsExactExpectedCause(Exception observed, Exception expected)
    {
        for (var depth = 0; depth < 32; depth++)
        {
            if (ReferenceEquals(observed, expected)) return true;
            // Only this known structured property diagnostic may carry the original observer.
            // Unknown wrappers, absent/cloned causes and aggregates are never traversed here.
            if (observed is not CuiRuntimeLoadException { InnerException: { } inner } diagnostic ||
                diagnostic.Diagnostic.Code != "CUIR053") return false;
            observed = inner;
        }
        return false;
    }
    private static void AssertRetainedExpectedCause(Fixture fixture, Task receipt, Exception expected)
    {
        var actual = Assert.Single(ObservedActionLeaves(fixture, receipt),
            cause => IsExactExpectedCause(cause, expected));
        // Attest only this exact observed wrapper AFTER verifying the reference-identical
        // original inside it. Cleanup does not waive arbitrary diagnostics by type/message.
        if (!ReferenceEquals(actual, expected)) fixture.ExpectedFailures.Add(actual);
    }
    private static IReadOnlyList<Exception> ObservedActionLeaves(Fixture fixture, Task receipt)
    {
        var sourcePayloads = fixture.Dispatcher.OriginalTask.Exception?.InnerExceptions.AsEnumerable()
            ?? Enumerable.Empty<Exception>();
        var opaque = new HashSet<Exception>(sourcePayloads, ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>(receipt.Exception!.InnerExceptions);
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var leaves = new List<Exception>();
        while (pending.Count != 0)
        {
            var current = pending.Pop(); if (!seen.Add(current)) continue;
            if (seen.Count > 4096) throw new InvalidOperationException("Actual action receipt exceeds bounded test inspection.");
            if (current is AggregateException { InnerExceptions.Count: > 0 } group && !opaque.Contains(current))
                foreach (var child in group.InnerExceptions) pending.Push(child);
            else leaves.Add(current);
        }
        return leaves;
    }
    private static bool MatchFailureGraph(IEnumerable<Exception> roots, IReadOnlyList<Exception> expected,
        HashSet<Exception> knownGroups, HashSet<Exception> opaqueSources, bool capture,
        out HashSet<Exception> capturedGroups)
    {
        capturedGroups = new(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>(roots);
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (seen.Count > 4096) return false;
            if (expected.Any(cause => ReferenceEquals(cause, current))) continue;
            if (current is not AggregateException group || group.InnerExceptions.Count == 0 ||
                opaqueSources.Contains(group) || !capture && !knownGroups.Contains(group)) return false;
            capturedGroups.Add(group);
            foreach (var child in group.InnerExceptions) pending.Push(child);
        }
        return seen.Count != 0;
    }

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
        private readonly List<Task> _actionReceipts = [];
        private readonly HashSet<Exception> _knownFailureGroups = new(ReferenceEqualityComparer.Instance);

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
            _actionReceipts.Add(receipt);
            return receipt;
        }

        internal async Task CloseAndDrainAsync()
        {
            var failures = new List<Exception>();
            Dispatcher.ReleaseForCleanup();
            var opaqueSources = new HashSet<Exception>(Dispatcher.OriginalTask.Exception?.InnerExceptions.AsEnumerable()
                ?? Enumerable.Empty<Exception>(), ReferenceEqualityComparer.Instance);
            // Only actual completed action snapshots can attest compound runtime wrappers.
            // Original domain aggregate payloads remain opaque; later cleanup wrappers cannot
            // borrow a passing action's leaf exceptions to become an accepted receipt.
            foreach (var receipt in _actionReceipts.Where(task => task.IsFaulted))
                if (MatchFailureGraph(receipt.Exception!.InnerExceptions, ExpectedFailures,
                    _knownFailureGroups, opaqueSources, true, out var observed))
                    _knownFailureGroups.UnionWith(observed);
            try { Window.Close(); } catch (Exception error) { failures.Add(error); }
            try { Loader.Dispose(); } catch (Exception error) { failures.Add(error); }
            // Snapshot only AFTER retirement has published its physical-close receipts.
            var drain = Loader.WhenActionsIdleAsync();
            try { await drain; }
            catch (Exception error)
            {
                if (drain.Exception is not { } actual ||
                    !MatchFailureGraph(actual.InnerExceptions, ExpectedFailures, _knownFailureGroups,
                        opaqueSources, false, out _))
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
        internal Task<CuiCommandOutcome> OriginalTask => _completion.Task;
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
        internal void Cancel(CancellationToken token) => _completion.SetCanceled(token);
    }
}
