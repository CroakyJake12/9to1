using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiInputSubmitRuntimeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private const string Source = """
        <Cui>
          <Actions><Action name="Submit" command="chat.submit" parameter="{Binding Event}"/></Actions>
          <Page><Input id="message" Type="Text" AcceptsReturn="True" SubmitAction="Submit"/></Page>
        </Cui>
        """;

    [Fact]
    public async Task Named_submit_action_receives_exact_input_and_supported_modifier_identity()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await CuiHeadlessDispatch.RunAsync(session, async () =>
        {
            var requests = new List<CuiInputSubmitRequest>(); using var loader = NewLoader((_, parameter, _) =>
            { requests.Add(Assert.IsType<CuiInputSubmitRequest>(parameter)); return ValueTask.CompletedTask; });
            var (root, input) = LoadInput(loader);
            var window = new Window { Width = 420, Height = 180, Content = root };
            try
            {
                window.Show(); Pump(window); Assert.True(input.Focus());
                input.Text = "first";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                input.Text = "second";
                window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                input.Text = "third";
                window.KeyPress(Key.Enter, RawInputModifiers.Alt, PhysicalKey.Enter, null); await loader.WhenActionsIdleAsync().WaitAsync(Bound);

                Assert.Equal(3, requests.Count);
                Assert.All(requests, request => { Assert.Same(input, request.Sender); Assert.Equal(Key.Enter, request.Key); });
                Assert.Equal(CuiInputSubmitModifiers.None, requests[0].Modifiers);
                Assert.Equal(CuiInputSubmitModifiers.Control, requests[1].Modifiers);
                Assert.Equal(CuiInputSubmitModifiers.Alt, requests[2].Modifiers);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Shift_enter_and_unsupported_modifier_chords_preserve_native_editor_behavior_and_do_not_submit()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await CuiHeadlessDispatch.RunAsync(session, async () =>
        {
            var calls = 0; using var loader = NewLoader((_, _, _) => { calls++; return ValueTask.CompletedTask; });
            var (root, input) = LoadInput(loader);
            var window = new Window { Width = 420, Height = 180, Content = root };
            using var plainLoader = new CuiControlLoader();
            var (plainRoot, plainInput) = LoadInput(plainLoader, submit: false);
            var plainWindow = new Window { Width = 420, Height = 180, Content = plainRoot };
            try
            {
                plainWindow.Show(); Pump(plainWindow); Assert.True(plainInput.Focus());
                plainInput.Text = "draft"; plainInput.CaretIndex = plainInput.Text.Length;
                foreach (var modifiers in new[] { RawInputModifiers.Shift, RawInputModifiers.Control | RawInputModifiers.Alt, RawInputModifiers.Meta })
                {
                    plainWindow.KeyPress(Key.Enter, modifiers, PhysicalKey.Enter, null);
                    plainWindow.KeyRelease(Key.Enter, modifiers, PhysicalKey.Enter, null);
                }
                window.Show(); Pump(window); Assert.True(input.Focus()); input.Text = "draft"; input.CaretIndex = input.Text.Length;
                window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
                Assert.Equal("draft\n", input.Text);
                window.KeyPress(Key.Enter, RawInputModifiers.Control | RawInputModifiers.Alt, PhysicalKey.Enter, null);
                window.KeyPress(Key.Enter, RawInputModifiers.Meta, PhysicalKey.Enter, null);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Equal(0, calls);
                Assert.Equal(plainInput.Text, input.Text);
            }
            finally { window.Close(); plainWindow.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Active_ime_preedit_disabled_and_readonly_inputs_never_dispatch_submit()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await CuiHeadlessDispatch.RunAsync(session, async () =>
        {
            var calls = 0; using var loader = NewLoader((_, _, _) => { calls++; return ValueTask.CompletedTask; });
            var (root, input) = LoadInput(loader);
            var window = new Window { Width = 420, Height = 180, Content = root };
            try
            {
                window.Show(); Pump(window); Assert.True(input.Focus());
                var presenter = Assert.Single(input.GetVisualDescendants().OfType<TextPresenter>());
                presenter.PreeditText = "にほん";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                presenter.PreeditText = null;
                input.IsReadOnly = true;
                Assert.True(input.IsReadOnly);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                input.IsReadOnly = false;
                Assert.True(CuiLifecycleProperties.SetLiteral(input, "Interactive", "False", CuiPropertyLevel.Instance, "test"));
                Assert.False(input.IsEffectivelyEnabled);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Equal(0, calls);
                Assert.True(CuiLifecycleProperties.SetLiteral(input, "Interactive", "True", CuiPropertyLevel.Instance, "test"));
                Assert.True(input.Focus()); input.Text = "ready again";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Equal(1, calls);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_second_enter_is_not_dispatched_until_the_first_submit_action_settles()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await CuiHeadlessDispatch.RunAsync(session, async () =>
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0; using var loader = NewLoader((_, _, _) =>
            { calls++; entered.TrySetResult(); return new ValueTask(release.Task); });
            var (root, input) = LoadInput(loader);
            var window = new Window { Width = 420, Height = 180, Content = root };
            try
            {
                window.Show(); Pump(window); Assert.True(input.Focus());
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); await entered.Task.WaitAsync(Bound);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.Equal(1, calls);
                Assert.False(loader.WhenActionsIdleAsync().IsCompleted);
                release.SetResult(); await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            }
            finally { release.TrySetResult(); window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Edited_input_admits_a_second_submit_while_the_first_source_task_is_retained()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await CuiHeadlessDispatch.RunAsync(session, async () =>
        {
            var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            using var loader = NewLoader((_, parameter, _) =>
            {
                Assert.IsType<CuiInputSubmitRequest>(parameter);
                var call = Interlocked.Increment(ref calls);
                if (call == 1) { firstEntered.TrySetResult(); return new ValueTask(firstRelease.Task); }
                if (call == 2) { secondEntered.TrySetResult(); return new ValueTask(secondRelease.Task); }
                throw new InvalidOperationException("An unchanged text revision was dispatched twice.");
            });
            var (root, input) = LoadInput(loader);
            var window = new Window { Width = 420, Height = 180, Content = root };
            try
            {
                window.Show(); Pump(window); Assert.True(input.Focus());
                input.Text = "first draft";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await firstEntered.Task.WaitAsync(Bound);

                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.Equal(1, calls); // repeat on same exact edit revision is suppressed

                input.Text = "second draft";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await secondEntered.Task.WaitAsync(Bound);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.Equal(2, calls); // the second revision is also deduplicated
                Assert.False(loader.WhenActionsIdleAsync().IsCompleted);

                firstRelease.SetResult();
                await Task.Yield();
                Assert.False(loader.WhenActionsIdleAsync().IsCompleted); // second source remains retained
                secondRelease.SetResult();
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
            }
            finally
            {
                firstRelease.TrySetResult(); secondRelease.TrySetResult(); window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Retyping_identical_text_after_a_real_edit_is_a_new_submit_admission()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await CuiHeadlessDispatch.RunAsync(session, async () =>
        {
            var calls = 0;
            using var loader = NewLoader((_, _, _) => { calls++; return ValueTask.CompletedTask; });
            var (root, input) = LoadInput(loader);
            var window = new Window { Width = 420, Height = 180, Content = root };
            try
            {
                window.Show(); Pump(window); Assert.True(input.Focus());
                input.Text = "same draft";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                input.Text = string.Empty;
                input.Text = "same draft";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await loader.WhenActionsIdleAsync().WaitAsync(Bound);
                Assert.Equal(2, calls);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static CuiControlLoader NewLoader(Func<string, object?, CancellationToken, ValueTask> dispatch)
    {
        var loader = new CuiControlLoader(); loader.SetActionDispatcher(new Dispatcher(dispatch));
        return loader;
    }

    private static (Control Root, TextBox Input) LoadInput(CuiControlLoader loader, bool submit = true)
    {
        var loaded = loader.LoadMarkup(submit ? Source : Source.Replace(" SubmitAction=\"Submit\"", ""));
        Assert.Empty(loaded.Diagnostics); var root = Assert.IsAssignableFrom<Control>(loaded.Root);
        var input = Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
        loader.WireBindings(root); return (root, input);
    }

    private static void Pump(Window window) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

    private sealed class Dispatcher(Func<string, object?, CancellationToken, ValueTask> dispatch) : ICuiActionDispatcher
    {
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Assert.Equal("chat.submit", command); return dispatch(command, parameter, cancellationToken);
        }
    }
}
