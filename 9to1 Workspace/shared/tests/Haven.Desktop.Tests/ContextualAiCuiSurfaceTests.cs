using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Services;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Tests;

public sealed class ContextualAiCuiSurfaceTests
{
    [AvaloniaFact]
    public async Task Retained_shared_scene_updates_the_same_session_and_dispatches_one_actual_prompt()
    {
        var token = TestContext.Current.CancellationToken;
        var document = new NotesDocument { Title = "Owning Write document" };
        var context = new ArtifactAiContext("write", () => (document.Id.ToString("N"), document));
        var client = new Client();
        using var state = new FloatingAiBarState(new(context, context, new NoApproval(), client));
        var readiness = new Readiness(true);
        using var surface = new ContextualAiCuiSurface(state, readiness);
        Assert.Equal(CuiSceneAvailabilityState.Ready, (await surface.InitializeAsync("write", "Write", token)).State);
        var window = new Window { Content = surface, Width = 800, Height = 600 };
        window.Show();
        try
        {
            var buttons = surface.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Single(buttons, item => Equals(item.Content, "✦")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var editor = Assert.Single(surface.GetVisualDescendants().OfType<TextBox>());
            editor.Text = "Summarise the document";
            for (var attempt = 0; attempt < 100 && state.Prompt != editor.Text; attempt++)
                await Task.Delay(10, token);
            Assert.Equal(editor.Text, state.Prompt);
            Assert.Single(buttons, item => Equals(item.Content, "Send")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 100 && state.Response != "Summary"; attempt++)
                await Task.Delay(10, token);
            Assert.Equal("Summary", state.Response);
            Assert.Equal(1, client.Requests);
            Assert.True(state.IsReadOnly);
            Assert.Contains(surface.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == "Summary");
            readiness.Ready = false;
            Assert.Single(buttons, item => Equals(item.Content, "Send")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var attempt = 0; attempt < 100 && surface.GetVisualDescendants().OfType<TextBox>().Any(); attempt++)
                await Task.Delay(10, token);
            Assert.Equal(1, client.Requests);
            Assert.Empty(surface.GetVisualDescendants().OfType<TextBox>());
            surface.Dispose();
            state.Prompt = "The owning session survives disposing its view";
            Assert.Equal("The owning session survives disposing its view", state.Prompt);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Unavailable_Home_readiness_never_mounts_prompt_or_send_controls()
    {
        var context = new ArtifactAiContext("write", () => (null, null));
        using var state = new FloatingAiBarState(new(context, context, new NoApproval(), new Client()));
        using var surface = new ContextualAiCuiSurface(state, new Readiness(false));
        Assert.Equal(CuiSceneAvailabilityState.Unavailable,
            (await surface.InitializeAsync("write", "Write", TestContext.Current.CancellationToken)).State);
        Assert.Empty(surface.GetVisualDescendants().OfType<TextBox>());
        Assert.DoesNotContain(surface.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Send"));
    }

    private sealed class Readiness(bool ready) : ICuiSceneReadiness
    {
        public bool Ready { get; set; } = ready;
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(Ready ? CuiSceneAvailabilityState.Ready : CuiSceneAvailabilityState.Unavailable,
                Ready ? "FixtureReady" : "HomeUnavailable", Ready ? "Controlled host fixture." : "Open Home to repair services."));
    }
    private sealed class NoApproval : IAppAiApprovalVerifier
    {
        public ValueTask<bool> VerifyAsync(string appId, string actionId, string approvalToken, CancellationToken cancellationToken) => ValueTask.FromResult(false);
    }
    private sealed class Client : IDulcheAppClient
    {
        public int Requests { get; private set; }
        public async IAsyncEnumerable<AppAiResponseChunk> StreamAsync(AppAiPrompt prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Requests++;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return new("Summary", true);
        }
    }
}
