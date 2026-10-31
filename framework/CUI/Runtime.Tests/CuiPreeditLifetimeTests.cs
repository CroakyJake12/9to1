using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiPreeditLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Plain_and_password_preedit_use_current_presenter_decorations_and_block_submit(bool password)
    {
        // Establish the native shared decoration on an earlier UI lifetime.
        await using (var prior = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication)))
            await prior.Dispatch(() => { _ = TextDecorations.Underline; }, CancellationToken.None);

        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await CuiHeadlessDispatch.RunAsync(session, async () =>
        {
            var dispatcher = new SubmitDispatcher();
            using var loader = new CuiControlLoader();
            loader.SetActionDispatcher(dispatcher);
            var loaded = loader.LoadMarkup("""
                <Cui><Actions><Action name="Submit" command="composer.submit" parameter="{Binding Event}"/></Actions>
                  <Page><Input Type="Text" Value="draft" SubmitAction="Submit"/></Page>
                </Cui>
                """);
            Assert.Empty(loaded.Diagnostics);
            var root = Assert.IsAssignableFrom<Control>(loaded.Root);
            var input = root as TextBox ?? Assert.Single(root.GetVisualDescendants().OfType<TextBox>());
            if (password) input.PasswordChar = '*';
            loader.WireBindings(root);
            var window = new Window { Content = root, Width = 360, Height = 160 };
            try
            {
                window.Show(); window.UpdateLayout();
                Assert.True(input.Focus());
                var presenter = Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>());
                presenter.PreeditText = "にほん";
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(0, dispatcher.Calls);
                Assert.Equal("draft", input.Text);
                var decorations = presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns)
                    .SelectMany(run => run.Properties?.TextDecorations ?? new TextDecorationCollection()).ToArray();
                Assert.NotEmpty(decorations);
                Assert.All(decorations, decoration => Assert.Same(presenter.Dispatcher, decoration.Dispatcher));
                Assert.Contains(decorations, decoration => decoration.Location == TextDecorationLocation.Underline);

                presenter.PreeditText = null;
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(1, dispatcher.Calls);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private sealed class SubmitDispatcher : ICuiActionDispatcher
    {
        internal int Calls;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Assert.Equal("composer.submit", command);
            Calls++;
            return ValueTask.CompletedTask;
        }
    }
}
