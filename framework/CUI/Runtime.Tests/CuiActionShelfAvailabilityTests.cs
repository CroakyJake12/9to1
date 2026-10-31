using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiActionShelfAvailabilityTests
{
    [Fact]
    public async Task Empty_and_unavailable_shelf_actions_do_not_dispatch_and_same_owner_can_enable_current_action()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await CuiHeadlessDispatch.RunAsync(session, async () =>
        {
            var owner = new Owner();
            using var loader = new CuiControlLoader();
            loader.SetBindingContext(owner);
            loader.SetActionDispatcher(owner);
            var loaded = loader.LoadMarkup("""
                <Cui><Actions><Action name="activate" command="shelf.activate" /></Actions>
                  <Page><Container Type="ActionShelf" Source="{Binding Eligible}">
                    <Button id="front" Action="activate" Content="{Binding Shelf.FrontLabel}"
                      IsVisible="{Binding Shelf.HasFront}" />
                  </Container></Page>
                </Cui>
                """);
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            loader.WireBindings(root);
            var window = new Window { Content = root, Width = 300, Height = 180 };
            try
            {
                window.Show(); window.UpdateLayout();
                // An empty shelf retains the authored cell while removing it
                // from both native and logical layout until it becomes visible.
                var shelf = Assert.Single(root.GetSelfAndVisualDescendants().OfType<CuiActionShelf>());
                var front = Assert.Single(shelf.AuthoredChildren.OfType<Button>(), button => button.Name == "front");
                async Task Activate()
                {
                    front.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, front));
                    await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                }
                Assert.False(front.IsVisible);
                await Activate();
                Assert.Empty(owner.Calls);

                var parameter = new object();
                owner.Replace(new CuiActionShelfItem("send", "composer.send", "Send", "", "Send draft", parameter));
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.True(front.IsVisible);
                Assert.True(front.IsEffectivelyEnabled);
                Assert.Contains(front, root.GetVisualDescendants());
                Assert.True(front.Bounds.Width > 0 && front.Bounds.Height > 0);
                await Activate();
                Assert.Empty(owner.Calls);

                // Availability is checked at activation, without recreating the
                // shelf, item identity, owner, or button between attempts.
                owner.Available = true;
                await Activate();
                var call = Assert.Single(owner.Calls);
                Assert.Equal("composer.send", call.Command);
                Assert.Same(parameter, call.Parameter);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class Owner : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
    {
        private IReadOnlyList<CuiActionShelfItem> _eligible = [];
        internal bool Available;
        internal List<(string Command, object? Parameter)> Calls { get; } = [];
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value)
        {
            value = _eligible;
            return path == "Eligible";
        }
        public bool? IsActionAvailable(string command) => command == "composer.send" && Available;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls.Add((command, parameter));
            return ValueTask.CompletedTask;
        }
        internal void Replace(CuiActionShelfItem item)
        {
            _eligible = [item];
            PropertyChanged?.Invoke(this, new(null));
        }
    }
}
