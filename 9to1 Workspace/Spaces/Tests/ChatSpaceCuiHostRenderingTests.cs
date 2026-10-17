using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// Merge stage M3 shell wiring: the Chat Space document hosted by a real
/// <see cref="CuiSceneHost"/> through <see cref="CuiNativeScene"/>.
///
/// <see cref="ChatSpaceCuiRenderingTests"/> proves the authored markup binds through a bare
/// <c>CuiControlLoader</c>. That is not the path the shell takes. The native page loads the
/// document from the embedded resource, publishes it through <see cref="CuiSceneHost"/>, and
/// relies on the scene's availability handshake and its own property-change subscription to
/// keep the transcript live. Those two mechanisms are exactly what is covered here, so a host
/// that loads but never re-renders cannot pass.
/// </summary>
public sealed class ChatSpaceCuiHostRenderingTests
{
    private sealed class TestApplication : Application
    {
        public static Application Instance { get; } = new TestApplication();
    }

    private sealed class ReadyReadiness : ICuiSceneReadiness
    {
        public int Calls { get; private set; }
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return ValueTask.FromResult(
                new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Chat Space is ready."));
        }
    }

    private static ChatSpaceViewState State(params ChatSpaceMessage[] messages) =>
        ChatSpaceViewState.Empty with
        {
            Conversation = new ChatSpaceConversation(
                Guid.NewGuid(), "Space chat", messages, [], null),
            Composer = new ChatSpaceComposer(string.Empty, [], "model-a", false),
            Status = new ChatSpaceStatus("Ready", ChatSpaceStatusTone.Neutral, false)
        };

    private static ChatSpaceMessage Message(Guid id, string speaker, string content) =>
        new(id, speaker == "You" ? MessageRole.User : MessageRole.Assistant, content, speaker, "now",
            null, [], [], [], false);

    private static string[] RenderedText(Control root) =>
        root.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? string.Empty)
            .Concat(root.GetVisualDescendants().OfType<CuiMarkdownView>().Select(view => view.Text)).ToArray();

    [Fact]
    public async Task The_shell_host_path_publishes_the_chat_space_as_a_ready_scene()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var state = State(Message(Guid.NewGuid(), "You", "Hello"), Message(Guid.NewGuid(), "Haven", "Hi there"));
            var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);
            var readiness = new ReadyReadiness();

            // The exact composition ChatSpacePage builds, including the embedded-resource load.
            var document = ChatSpaceCuiDocument.Load();
            using var host = new CuiSceneHost();
            using var mounted = new MountedHost(host);
            var availability = await host.ShowAsync(new CuiNativeScene(
                "spaces.chat", "Chat Space", "Chat", document, bindings, bindings, readiness));

            Assert.Equal(CuiSceneAvailabilityState.Ready, availability.State);
            Assert.Equal(1, readiness.Calls);

            // The handshake must not be the only thing that happened: prove real authored
            // content reached native controls, with no error diagnostics left behind.
            Assert.DoesNotContain(host.Diagnostics,
                diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
            for (var pass = 0; pass < 3; pass++) { mounted.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = mounted.Window.CaptureRenderedFrame(); }
            var root = Assert.IsAssignableFrom<Control>(host.Content);
            var rendered = RenderedText(root);
            Assert.Contains("Hello", rendered);
            Assert.Contains("Hi there", rendered);
            Assert.Contains("Space chat", rendered);
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task Republished_state_re_renders_through_the_host_notification_path()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var state = State(Message(Guid.NewGuid(), "You", "First question"));
            var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

            using var host = new CuiSceneHost();
            using var mounted = new MountedHost(host);
            await host.ShowAsync(new CuiNativeScene(
                "spaces.chat", "Chat Space", "Chat", ChatSpaceCuiDocument.Load(), bindings, bindings,
                new ReadyReadiness()));

            for (var pass = 0; pass < 3; pass++) { mounted.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = mounted.Window.CaptureRenderedFrame(); }
            var root = Assert.IsAssignableFrom<Control>(host.Content);
            Assert.Contains("First question", RenderedText(root));

            // The page forwards the projection's PropertyChanged as its own, because the page -
            // not the projection - is the scene's binding context. If that forwarding regresses,
            // this is where it shows: a new message that never appears.
            state = State(
                Message(Guid.NewGuid(), "You", "First question"),
                Message(Guid.NewGuid(), "Haven", "The answer arrives later"));
            bindings.Refresh();

            var settled = false;
            for (var attempt = 0; attempt < 20 && !settled; attempt++)
            {
                settled = RenderedText(root).Contains("The answer arrives later");
                if (!settled) await Task.Delay(25);
            }

            Assert.True(settled,
                $"the republished message never rendered. Rendered: {string.Join(" | ", RenderedText(root))}");
            return true;
        }, lifetime.Token);
    }

    [Fact]
    public async Task A_degraded_handshake_does_not_publish_the_chat_document()
    {
        using var lifetime = new CancellationTokenSource();
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SpacesTestApp));
        await session.Dispatch<bool>(async () =>
        {
            var state = State(Message(Guid.NewGuid(), "You", "must not appear"));
            var bindings = new ChatSpaceCuiBindings(() => state, (_, _, _) => ValueTask.CompletedTask);

            using var host = new CuiSceneHost();
            using var mounted = new MountedHost(host);
            var availability = await host.ShowAsync(new CuiNativeScene(
                "spaces.chat", "Chat Space", "Chat", ChatSpaceCuiDocument.Load(), bindings, bindings,
                new UnavailableReadiness()));

            Assert.Equal(CuiSceneAvailabilityState.Unavailable, availability.State);
            for (var pass = 0; pass < 3; pass++) { mounted.Window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = mounted.Window.CaptureRenderedFrame(); }
            var root = Assert.IsAssignableFrom<Control>(host.Content);
            Assert.DoesNotContain("must not appear", RenderedText(root));
            return true;
        }, lifetime.Token);
    }

    private sealed class MountedHost : IDisposable
    {
        public Window Window { get; }
        public MountedHost(Control host) { Window = new() { Content = host, Width = 1280, Height = 900 }; Window.Show(); }
        public void Dispose() => Window.Close();
    }

    private sealed class UnavailableReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(
                CuiSceneAvailabilityState.Unavailable, "no-backend", "The Chat Space backend is unavailable."));
    }
}