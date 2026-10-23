using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>Actual loader/headless controls and retained dispatcher tasks. These are framework
/// activation regressions, not native app or provider acceptance.</summary>
public sealed class CuiInitialEffectActivationTests
{
    private const string Markup = """
        <Cui>
          <Variable Type="bool" ID="Ready">True</Variable>
          <Variable Type="bool" ID="Other">False</Variable>
          <Variable Type="commandstate" ID="Result" />
          <Actions>
            <Action name="inspect" command="domain.inspect" parameter="{Binding RestoredID}" result="Result" />
            <Action name="follow" command="domain.follow" />
            <Action name="toggle"><Toggle Target="Ready" /></Action>
            <Action name="other"><Toggle Target="Other" /></Action>
          </Actions>
          <Effects>
            <Effect Condition="{Binding Ready}" Action="inspect" />
            <Effect Condition="{Binding Result.Succeeded}" Action="follow" />
          </Effects>
          <Container Type="Vertical Stack">
            <Button id="toggle" Action="toggle" Content="Toggle" />
            <Container Active="False"><Button id="other" Action="other" Content="Other" /></Container>
            <Text id="outcome" Text="{Binding Result.Code}" />
          </Container>
        </Cui>
        """;

    [Fact]
    public Task Initially_true_effect_waits_for_the_complete_wired_root_and_uses_restored_context() => Run(async f =>
    {
        var root = f.Load(Markup.Replace("Condition=\"{Binding Ready}\"", "Condition=\"True\""));
        Assert.Empty(f.Calls);
        f.Model.Set("RestoredID", "restored-document");
        f.OnDispatch = (command, parameter, _) =>
        {
            Assert.All(Tree(root).OfType<Button>(), button => Assert.True(f.Loader.Inspect(button)!.ActionsWired));
            if (command == "domain.inspect") Assert.Equal("restored-document", parameter);
            return ValueTask.FromResult(Success);
        };
        f.Loader.WireBindings(root);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(new[] { "domain.inspect", "domain.follow" }, f.Calls);
        Assert.Equal("Inspected", Find<TextBlock>(root, "outcome").Text);
        f.Loader.WireBindings(root);
        Click(Find<Button>(root, "other"));
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(2, f.Calls.Count);
    });

    [Fact]
    public Task Initially_false_effect_and_later_rising_edges_keep_existing_one_dispatch_per_edge_semantics() => Run(async f =>
    {
        var root = f.Mount(Markup.Replace("ID=\"Ready\">True", "ID=\"Ready\">False"));
        await f.Loader.WhenActionsIdleAsync();
        Assert.Empty(f.Calls);
        Click(Find<Button>(root, "toggle"));
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(2, f.Calls.Count);
        Click(Find<Button>(root, "other"));
        f.Loader.WireBindings(root);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(2, f.Calls.Count);
        Click(Find<Button>(root, "toggle"));
        await f.Loader.WhenActionsIdleAsync();
        Click(Find<Button>(root, "toggle"));
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(new[] { "domain.inspect", "domain.follow", "domain.inspect" }, f.Calls);
    });

    [Fact]
    public Task Foreign_descendant_and_replaced_roots_cannot_activate_the_current_documents_effects() => Run(async f =>
    {
        var old = f.Load();
        f.Loader.WireBindings(new Button());
        f.Loader.WireBindings(Find<Button>(old, "toggle"));
        Assert.Empty(f.Calls);
        var current = f.Load();
        f.Loader.WireBindings(old);
        Assert.Empty(f.Calls);
        f.Loader.WireBindings(current);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(2, f.Calls.Count);
    });

    [Fact]
    public Task Failed_replacement_cannot_resurrect_the_retired_root_effects() => Run(async f =>
    {
        var old = f.Load();
        var cause = new IOException("renderer failed before mount completed");
        f.Loader.RegisterObjectRenderer("ThrowingRenderer", _ => throw cause);
        var actual = Record.Exception(() => f.Loader.LoadMarkup("""
            <Cui><Object Type="ThrowingRenderer" /></Cui>
            """));
        Assert.NotNull(actual);
        f.Loader.WireBindings(old);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Empty(f.Calls);
    });

    [Fact]
    public Task Reentrant_renderer_replacement_cannot_publish_the_outer_generations_effects() => Run(async f =>
    {
        f.Loader.RegisterObjectRenderer("ReplacingRenderer", _ =>
        {
            f.Retire(false);
            return new TextBlock();
        });
        var outer = f.Loader.LoadMarkup(Markup.Replace("<Container Type=\"Vertical Stack\">",
            "<Container Type=\"Vertical Stack\"><Object Type=\"ReplacingRenderer\" />"));
        Assert.Null(outer.Root);
        Assert.Contains(outer.Diagnostics, diagnostic => diagnostic.Code == "CUIR073");
        Assert.NotNull(f.Replacement);
        f.Loader.WireBindings(f.Replacement!);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Empty(f.Calls);
    });

    [Fact]
    public Task Initially_true_pure_local_effect_runs_without_an_external_dispatcher() => Run(async f =>
    {
        using var loader = new CuiControlLoader();
        var loaded = loader.LoadMarkup("""
            <Cui>
              <Variable Type="string" ID="Status">before</Variable>
              <Actions><Action name="start"><Set Target="Status" Value="after" /></Action></Actions>
              <Effects><Effect Condition="True" Action="start" /></Effects>
              <Text Text="{Binding Status}" />
            </Cui>
            """);
        Assert.Empty(loaded.Diagnostics);
        var root = Assert.IsType<TextBlock>(loaded.Root);
        Assert.Equal("before", root.Text);
        loader.WireBindings(root);
        await loader.WhenActionsIdleAsync();
        Assert.Equal("after", root.Text);
        Assert.Empty(f.Calls);
    });

    [Fact]
    public Task Rejected_preflight_preserves_initial_pending_action_and_its_result_effect() => Run(async f =>
    {
        var completion = f.DelayInspection();
        var root = f.Mount();
        Assert.Equal(new[] { "domain.inspect" }, f.Calls);
        var receipt = f.Loader.WhenActionsIdleAsync();
        Assert.False(receipt.IsCompleted);
        var rejected = f.Loader.LoadMarkup("""
            <Cui><Variable Type="bool" ID="Invalid">invalid</Variable><Container /></Cui>
            """);
        Assert.Null(rejected.Root);
        Assert.Contains(rejected.Diagnostics, diagnostic => diagnostic.Code == "CUIR077");
        f.Loader.WireBindings(root);
        Assert.Single(f.Calls);
        completion.SetResult(Success);
        await receipt;
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(new[] { "domain.inspect", "domain.follow" }, f.Calls);
        Assert.Equal("Inspected", Find<TextBlock>(root, "outcome").Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Initial_condition_callback_replacement_or_disposal_withdraws_all_old_effects(bool dispose) => Run(async f =>
    {
        var callback = new CallbackContext(f.Model, "InitialAllowed", () => f.Retire(dispose));
        f.Loader.SetBindingContext(callback);
        var root = f.Load(Markup.Replace("Condition=\"{Binding Ready}\"", "Condition=\"{Binding InitialAllowed}\"")
            .Replace("Condition=\"{Binding Result.Succeeded}\"", "Condition=\"True\""));
        f.Loader.WireBindings(root);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Empty(f.Calls);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Initial_parameter_callback_replacement_or_disposal_cannot_dispatch_a_stale_command(bool dispose) => Run(async f =>
    {
        f.Loader.SetBindingContext(new CallbackContext(f.Model, "RestoredID", () => f.Retire(dispose)));
        var root = f.Load();
        f.Loader.WireBindings(root);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Empty(f.Calls);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task First_initial_dispatch_callback_cannot_run_remaining_effects_after_retirement(bool dispose) => Run(async f =>
    {
        f.OnDispatch = (_, _, _) => { f.Retire(dispose); return ValueTask.FromResult(Success); };
        var root = f.Load(Markup.Replace("Condition=\"{Binding Result.Succeeded}\"", "Condition=\"True\""));
        f.Loader.WireBindings(root);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(new[] { "domain.inspect" }, f.Calls);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Original_initial_task_is_drained_but_late_success_cannot_publish_into_replacement_or_closed_loader(bool dispose) => Run(async f =>
    {
        var completion = f.DelayInspection();
        var root = f.Mount();
        Assert.Single(f.Calls);
        var receipt = f.Loader.WhenActionsIdleAsync();
        Assert.False(receipt.IsCompleted);
        var oldOutcome = Find<TextBlock>(root, "outcome");
        f.Retire(dispose);
        Assert.Equal(dispose, f.LastToken.IsCancellationRequested);
        var retiredText = oldOutcome.Text;
        Assert.False(receipt.IsCompleted);
        completion.SetResult(Success);
        await receipt;
        await f.Loader.WhenActionsIdleAsync();
        Assert.Equal(new[] { "domain.inspect" }, f.Calls);
        Assert.Equal(retiredText, oldOutcome.Text);
        if (f.Replacement is not null) Assert.Equal("replacement", Assert.IsType<TextBlock>(f.Replacement).Text);
    });

    [Fact]
    public Task Closed_loader_cannot_start_an_unwired_initial_effect() => Run(async f =>
    {
        var root = f.Load();
        f.Loader.Dispose();
        f.Loader.WireBindings(root);
        await f.Loader.WhenActionsIdleAsync();
        Assert.Empty(f.Calls);
    });

    [Fact]
    public Task Initial_fault_retains_original_opaque_task_payloads_and_observer_failure() => Run(async f =>
    {
        var completion = f.DelayInspection();
        var opaque = new AggregateException("opaque source payload");
        var sibling = new IOException("source sibling");
        var observer = new InvalidOperationException("failure observer");
        f.ExpectedFailures.AddRange([opaque, sibling, observer]);
        f.Loader.ActionFailed += (_, _) => throw observer;
        f.Mount();
        Assert.Single(f.Calls);
        var receipt = f.Loader.WhenActionsIdleAsync();
        f.FaultReceipts.Add(receipt);
        completion.SetException([opaque, sibling]);
        await Assert.ThrowsAnyAsync<Exception>(() => receipt);
        var leaves = FailureLeaves(receipt.Exception!.InnerExceptions, f.OpaquePayloads);
        Assert.Contains(leaves, error => ReferenceEquals(error, opaque));
        Assert.Contains(leaves, error => ReferenceEquals(error, sibling));
        Assert.Contains(leaves, error => ReferenceEquals(error, observer));
        Assert.Single(f.Calls); // failed result never satisfies Result.Succeeded
    });

    private static CuiCommandOutcome Success => new(true, false, "Inspected", null, null);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Find<T>(Control root, string name) where T : Control => Assert.Single(Tree(root).OfType<T>(), c => c.Name == name);
    private static IEnumerable<Control> Tree(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container ? container.AuthoredChildren : root.GetVisualChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var descendant in Tree(child)) yield return descendant;
    }

    private static async Task Run(Func<Fixture, Task> test)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<int>(async () =>
        {
            var f = new Fixture();
            var failures = new List<Exception>();
            try { await test(f); } catch (Exception error) { failures.Add(error); }
            try { await f.CloseAndDrain(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException(failures);
            return 0;
        }, CancellationToken.None);
    }

    private sealed class Fixture : ICuiResultReportingDispatcher
    {
        internal CuiControlLoader Loader { get; } = new();
        internal CuiViewModel Model { get; } = new();
        internal List<string> Calls { get; } = [];
        internal Func<string, object?, CancellationToken, ValueTask<CuiCommandOutcome>>? OnDispatch { get; set; }
        internal CancellationToken LastToken { get; private set; }
        internal Control? Replacement { get; private set; }
        internal List<Exception> ExpectedFailures { get; } = [];
        internal List<Task> FaultReceipts { get; } = [];
        private readonly List<TaskCompletionSource<CuiCommandOutcome>> _completions = [];
        internal HashSet<Exception> OpaquePayloads => new(_completions.SelectMany(c => c.Task.Exception?.InnerExceptions.AsEnumerable()
            ?? Enumerable.Empty<Exception>()), ReferenceEqualityComparer.Instance);

        internal Fixture()
        {
            Model.Set("RestoredID", "original-document");
            Loader.SetBindingContext(Model);
            Loader.SetActionDispatcher(this);
            Loader.SetSurface("Boards");
            Loader.SetAppearance(CuiAppearance.Dark);
        }

        internal Control Load(string markup = Markup)
        {
            var loaded = Loader.LoadMarkup(markup, "initial-effects.cui");
            Assert.Empty(loaded.Diagnostics);
            return Assert.IsAssignableFrom<Control>(loaded.Root);
        }

        internal Control Mount(string markup = Markup)
        {
            var root = Load(markup);
            Loader.WireBindings(root);
            return root;
        }

        internal void Retire(bool dispose)
        {
            if (dispose) Loader.Dispose();
            else Replacement = Mount("<Cui><Text Text=\"replacement\" /></Cui>");
        }

        internal TaskCompletionSource<CuiCommandOutcome> DelayInspection()
        {
            var completion = new TaskCompletionSource<CuiCommandOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            _completions.Add(completion);
            OnDispatch = (command, _, _) => command == "domain.inspect" ? new(completion.Task) : ValueTask.FromResult(Success);
            return completion;
        }

        public ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls.Add(command);
            LastToken = cancellationToken;
            return OnDispatch?.Invoke(command, parameter, cancellationToken) ?? ValueTask.FromResult(Success);
        }
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            _ = await DispatchWithOutcomeAsync(command, parameter, cancellationToken);

        internal async Task CloseAndDrain()
        {
            foreach (var completion in _completions) completion.TrySetResult(new(false, true, "Cleanup", null, null));
            var knownGroups = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
            foreach (var receipt in FaultReceipts.Where(task => task.IsFaulted))
                if (MatchFailureGraph(receipt.Exception!.InnerExceptions, ExpectedFailures, knownGroups, OpaquePayloads, true, out var observed))
                    knownGroups.UnionWith(observed);
            Loader.Dispose();
            var drain = Loader.WhenActionsIdleAsync();
            try { await drain; }
            catch
            {
                if (drain.Exception is not { } actual ||
                    !MatchFailureGraph(actual.InnerExceptions, ExpectedFailures, knownGroups, OpaquePayloads, false, out _)) throw;
            }
        }
    }

    private sealed class CallbackContext(CuiViewModel model, string path, Action callback) : ICuiBindingContext
    {
        private bool _called;
        public bool TryGetValue(string requested, out object? value)
        {
            if (requested == path)
            {
                if (!_called) { _called = true; callback(); }
                value = requested == "InitialAllowed" ? true : "restored-document";
                return true;
            }
            return model.TryGetValue(requested, out value);
        }
    }

    private static IReadOnlyList<Exception> FailureLeaves(IEnumerable<Exception> roots, HashSet<Exception> opaque)
    {
        var pending = new Stack<Exception>(roots);
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var leaves = new List<Exception>();
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            Assert.True(seen.Count <= 4096);
            if (current is AggregateException { InnerExceptions.Count: > 0 } group && !opaque.Contains(current))
                foreach (var child in group.InnerExceptions) pending.Push(child);
            else leaves.Add(current);
        }
        return leaves;
    }

    // Same bounded, identity-based failure custody as the existing action-frame suite. Original
    // aggregate payloads are opaque; cleanup can reuse only groups attested by an actual receipt.
    private static bool MatchFailureGraph(IEnumerable<Exception> roots, IReadOnlyList<Exception> expected,
        HashSet<Exception> knownGroups, HashSet<Exception> opaque, bool capture, out HashSet<Exception> observed)
    {
        observed = new(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>(roots);
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (seen.Count > 4096) return false;
            if (expected.Any(cause => ReferenceEquals(cause, current))) continue;
            if (current is not AggregateException group || group.InnerExceptions.Count == 0 ||
                opaque.Contains(group) || !capture && !knownGroups.Contains(group)) return false;
            observed.Add(group);
            foreach (var child in group.InnerExceptions) pending.Push(child);
        }
        return seen.Count != 0;
    }
}
