using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiOriginalSurfaceSlotTests
{
    [Fact]
    public async Task Outer_loader_cannot_detach_inner_bindings_actions_or_claim_its_pending_work()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var owner = new OriginalOwner();
            using var inner = new CuiControlLoader();
            inner.SetBindingContext(owner); inner.SetActionDispatcher(owner);
            var child = inner.LoadMarkup("""
                <Cui><Page ID="original-page"><Container Type="Vertical Stack">
                  <Input ID="original-input" Type="Text" Value="{Binding draft, mode=TwoWay, type=string}" />
                  <Button ID="original-send" Action="send" Content="Send" />
                </Container></Page></Cui>
                """).Root!;
            inner.WireBindings(child);
            var slot = new CuiOriginalSurfaceSlot(); slot.SetOriginalSurface(child);
            var registry = new CuiControlRegistry(); registry.RegisterObjectRenderer("OriginalSurface", _ => slot);
            using var outer = new CuiControlLoader(registry);
            var frame = outer.LoadMarkup("<Cui><Page ID=\"outer-frame\"><Object Type=\"OriginalSurface\" /></Page></Cui>").Root!;
            var window = new Window { Width = 700, Height = 500, Content = frame }; window.Show(); window.UpdateLayout();
            try
            {
                var input = inner.FindAuthoredControl<TextBox>(child, "original-input");
                var send = inner.FindAuthoredControl<Button>(child, "original-send");
                outer.Dispose(); await outer.WhenActionsIdleAsync();
                owner.Draft = "Still owned by the child";
                Assert.Equal(owner.Draft, input.Text);
                send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(1, owner.Calls);
                var childPending = inner.WhenActionsIdleAsync(); Assert.False(childPending.IsCompleted);
                // Closing the original loader still joins its real pending callback.
                inner.Dispose(); var childClose = inner.WhenActionsIdleAsync(); Assert.False(childClose.IsCompleted);
                owner.Completion.SetResult(); await childPending; await childClose;
                Assert.Same(child, slot.OriginalSurface);
                slot.SetOriginalSurface(null); Assert.Null(child.Parent);
            }
            finally { owner.Completion.TrySetResult(); window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Transfer_rejects_still_parented_candidate_before_detaching_current_original()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var first = new Control(); var replacement = new Control();
            var otherOwner = new ContentControl { Content = replacement };
            var slot = new CuiOriginalSurfaceSlot(); slot.SetOriginalSurface(first);
            Assert.Throws<InvalidOperationException>(() => slot.SetOriginalSurface(replacement));
            Assert.Same(first, slot.OriginalSurface); Assert.Same(slot, first.Parent);
            Assert.Same(replacement, otherOwner.Content);
            slot.SetOriginalSurface(null); otherOwner.Content = null;
        }, CancellationToken.None);
    }

    private sealed class OriginalOwner : ICuiWritableBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
    {
        private string _draft = "Original";
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public string Draft { get => _draft; set { _draft = value; PropertyChanged?.Invoke(this, new("draft")); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value) { value = path == "draft" ? Draft : null; return value is not null; }
        public bool TrySetValue(string path, object? value) { if (path != "draft" || value is not string text) return false; Draft = text; return true; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        { Calls++; return new(Completion.Task); }
    }
}
