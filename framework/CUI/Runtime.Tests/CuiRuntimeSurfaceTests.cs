using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CakeOS.Cui.Language;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiRuntimeSurfaceTests
{
    [Fact]
    public async Task Applied_surface_dispatches_named_actions_with_the_current_typed_parameter()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var model = new CuiViewModel();
            model.Set("Draft", new object());
            var dispatcher = new RecordingDispatcher();
            using var surface = new CuiRuntimeSurface(new ContentControl(), bindingContext: model, actionDispatcher: dispatcher);
            Assert.True(surface.TryApply(Parse("""
                <Cui>
                  <Actions><Action name="Save" command="document.save" parameter="{Binding Draft}" /></Actions>
                  <Button id="save" action="Save">Save</Button>
                </Cui>
                """), out var diagnostics), string.Join("; ", diagnostics));

            var currentDraft = new object();
            model.Set("Draft", currentDraft);
            Assert.IsType<Button>(surface.CurrentRoot).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            var call = Assert.Single(dispatcher.Calls);
            Assert.Equal("document.save", call.Command);
            Assert.Same(currentDraft, call.Parameter);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Throwing_candidate_releases_its_subscription_and_keeps_the_previous_surface_live()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var failure = new InvalidOperationException("The candidate renderer failed.");
            var registry = new CuiControlRegistry();
            registry.RegisterObjectRenderer("broken", _ => throw failure);
            var model = new ObservableBindings();
            var dispatcher = new RecordingDispatcher();
            using var surface = new CuiRuntimeSurface(new ContentControl(), registry, model, dispatcher);
            Assert.True(surface.TryApply(Parse("<Cui><Button action=\"retained\" Text=\"{Binding Label}\" /></Cui>"), out var diagnostics),
                string.Join("; ", diagnostics));
            var previous = Assert.IsType<Button>(surface.CurrentRoot);

            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
                surface.TryApply(Parse("<Cui><Object Type=\"broken\" /></Cui>"), out _)));

            Assert.Same(previous, surface.CurrentRoot);
            Assert.Equal(2, model.Adds);
            Assert.Equal(1, model.Removes);
            Assert.Equal(1, model.Subscribers);
            model.SetLabel("Still live");
            Assert.Equal("Still live", previous.Content);
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("retained", Assert.Single(dispatcher.Calls).Command);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Replacement_and_disposal_revoke_only_the_retired_controls_and_subscriptions()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var host = new ContentControl();
            var model = new ObservableBindings();
            var dispatcher = new RecordingDispatcher();
            using var surface = new CuiRuntimeSurface(host, bindingContext: model, actionDispatcher: dispatcher);
            Assert.True(surface.TryApply(ButtonDocument("first"), out _));
            var first = Assert.IsType<Button>(surface.CurrentRoot);
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.False(surface.TryApply(Parse("<Cui><UnknownComponent /></Cui>"), out var rejected));
            Assert.NotEmpty(rejected);
            Assert.Same(first, surface.CurrentRoot);
            Assert.False(Assert.Single(dispatcher.Tokens).IsCancellationRequested);
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, dispatcher.Calls.Count);

            Assert.True(surface.TryApply(ButtonDocument("second"), out _));
            var second = Assert.IsType<Button>(surface.CurrentRoot);
            Assert.NotSame(first, second);
            Assert.Equal(1, model.Subscribers);
            Assert.All(dispatcher.Tokens, token => Assert.True(token.IsCancellationRequested));
            model.SetLabel("Current label");
            Assert.Equal("Initial", first.Content);
            Assert.Equal("Current label", second.Content);
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, dispatcher.Calls.Count);
            second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("second", dispatcher.Calls[2].Command);
            Assert.False(dispatcher.Tokens[2].IsCancellationRequested);

            surface.Dispose();
            Assert.Null(host.Content);
            Assert.Equal(0, model.Subscribers);
            Assert.All(dispatcher.Tokens, token => Assert.True(token.IsCancellationRequested));
            second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(3, dispatcher.Calls.Count);
            Assert.Throws<ObjectDisposedException>(() => surface.TryApply(ButtonDocument("later"), out _));
            surface.Dispose();
            Assert.Equal(model.Adds, model.Removes);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Candidate_and_cleanup_failures_are_both_retained_without_retiring_the_previous_surface()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var original = new InvalidOperationException("The candidate renderer failed.");
            var cleanup = new IOException("The candidate binding unsubscribe failed.");
            var registry = new CuiControlRegistry();
            registry.RegisterObjectRenderer("broken", _ => throw original);
            var model = new ObservableBindings();
            using var surface = new CuiRuntimeSurface(new ContentControl(), registry, model);
            Assert.True(surface.TryApply(ButtonDocument("first"), out _));
            var previous = surface.CurrentRoot;
            model.NextRemoveFailure = cleanup;

            var failure = Assert.Throws<AggregateException>(() =>
                surface.TryApply(Parse("<Cui><Object Type=\"broken\" /></Cui>"), out _));

            Assert.Collection(failure.InnerExceptions,
                error => Assert.Same(original, error), error => Assert.Same(cleanup, error));
            Assert.Same(previous, surface.CurrentRoot);
            Assert.Equal(1, model.Subscribers);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Previous_loader_cleanup_failure_does_not_orphan_the_newly_published_surface()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var model = new ObservableBindings();
            var dispatcher = new RecordingDispatcher();
            using var surface = new CuiRuntimeSurface(new ContentControl(), bindingContext: model, actionDispatcher: dispatcher);
            Assert.True(surface.TryApply(ButtonDocument("first"), out _));
            var previous = surface.CurrentRoot;
            var cleanup = new IOException("The previous binding unsubscribe failed.");
            model.NextRemoveFailure = cleanup;

            Assert.Same(cleanup, Assert.Throws<IOException>(() => surface.TryApply(ButtonDocument("second"), out _)));

            var current = Assert.IsType<Button>(surface.CurrentRoot);
            Assert.NotSame(previous, current);
            Assert.Equal(1, model.Subscribers);
            model.SetLabel("Still owned");
            Assert.Equal("Still owned", current.Content);
            current.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("second", Assert.Single(dispatcher.Calls).Command);
            surface.Dispose();
            Assert.Equal(0, model.Subscribers);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Disposal_from_a_native_publication_notification_retires_both_loaders()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var host = new ContentControl();
            var model = new ObservableBindings();
            var dispatcher = new RecordingDispatcher();
            using var surface = new CuiRuntimeSurface(host, bindingContext: model, actionDispatcher: dispatcher);
            Assert.True(surface.TryApply(ButtonDocument("first"), out _));
            var first = Assert.IsType<Button>(surface.CurrentRoot);
            Button? candidate = null;
            host.PropertyChanged += (sender, change) =>
            {
                if (change.Property != ContentControl.ContentProperty || host.Content is not Button { Name: "second" } button)
                    return;
                candidate = button;
                surface.Dispose();
                first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                candidate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };

            Assert.Throws<ObjectDisposedException>(() => surface.TryApply(ButtonDocument("second"), out _));

            Assert.Null(host.Content);
            Assert.Equal(0, model.Subscribers);
            Assert.Equal(2, model.Removes);
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.NotNull(candidate);
            candidate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(dispatcher.Calls);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_throwing_native_notification_keeps_the_published_candidate_owned_and_rejects_nested_applies()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var host = new ContentControl();
            var model = new ObservableBindings();
            var dispatcher = new RecordingDispatcher();
            using var surface = new CuiRuntimeSurface(host, bindingContext: model, actionDispatcher: dispatcher);
            Assert.True(surface.TryApply(ButtonDocument("first"), out _));
            var original = new InvalidOperationException("The native notification failed after publication.");
            host.PropertyChanged += (sender, change) =>
            {
                if (change.Property != ContentControl.ContentProperty || host.Content is not Button { Name: "second" })
                    return;
                Assert.Throws<InvalidOperationException>(() => surface.TryApply(ButtonDocument("nested"), out _));
                throw original;
            };

            Assert.Same(original, Assert.Throws<InvalidOperationException>(() => surface.TryApply(ButtonDocument("second"), out _)));

            var candidate = Assert.IsType<Button>(surface.CurrentRoot);
            Assert.Equal("second", candidate.Name);
            Assert.Equal(1, model.Subscribers);
            candidate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("second", Assert.Single(dispatcher.Calls).Command);
            surface.Dispose();
            Assert.Null(host.Content);
            Assert.Equal(0, model.Subscribers);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Disposal_preserves_content_substituted_by_the_host_owner()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(() =>
        {
            var host = new ContentControl();
            var model = new ObservableBindings();
            var dispatcher = new RecordingDispatcher();
            using var surface = new CuiRuntimeSurface(host, bindingContext: model, actionDispatcher: dispatcher);
            Assert.True(surface.TryApply(ButtonDocument("owned"), out _));
            var owned = Assert.IsType<Button>(surface.CurrentRoot);
            var external = new TextBlock { Text = "Host-owned replacement" };
            host.Content = external;

            surface.Dispose();

            Assert.Same(external, host.Content);
            Assert.Equal(0, model.Subscribers);
            owned.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(dispatcher.Calls);
        }, CancellationToken.None);
    }

    private static CuiDocument ButtonDocument(string command) =>
        Parse($"<Cui><Button id=\"{command}\" action=\"{command}\" Text=\"{{Binding Label}}\" /></Cui>");

    private static CuiDocument Parse(string markup)
    {
        var parser = new CuiRichParser();
        var document = parser.Parse(markup, "runtime-surface.cui");
        Assert.DoesNotContain(parser.Diagnostics.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
        return document;
    }

    private sealed class RecordingDispatcher : ICuiActionDispatcher
    {
        public List<(string Command, object? Parameter)> Calls { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls.Add((command, parameter));
            Tokens.Add(cancellationToken);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ObservableBindings : ICuiBindingContext, INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _changed;
        private string _label = "Initial";
        public int Adds { get; private set; }
        public int Removes { get; private set; }
        public int Subscribers => _changed?.GetInvocationList().Length ?? 0;
        public Exception? NextRemoveFailure { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add { Adds++; _changed += value; }
            remove
            {
                Removes++;
                _changed -= value;
                var failure = NextRemoveFailure;
                NextRemoveFailure = null;
                if (failure is not null) throw failure;
            }
        }

        public bool TryGetValue(string path, out object? value)
        {
            value = path == "Label" ? _label : null;
            return path == "Label";
        }

        public void SetLabel(string label)
        {
            _label = label;
            _changed?.Invoke(this, new PropertyChangedEventArgs("Label"));
        }
    }
}
