using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
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

    [AvaloniaFact]
    public async Task Rendered_finish_records_real_Home_completion_without_owner_execution_or_admission_replay()
    {
        var token = TestContext.Current.CancellationToken;
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(15));
        var root = Path.Combine(Path.GetTempPath(), "astra-cui-finish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fault = new AuditFaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            var prompt = new ManuallyAcknowledgedPrompt();
            var home = new HomeAppAiServices(new ModelProviderRegistry([]), fault,
                new("profile", "Owning profile", "os", "session", true), new AuditGraph(), new AuditInvocations(),
                promptPresenter: prompt);
            var owner = new DurableAuditOwner(Path.Combine(root, "owner.txt"));
            using var state = home.Create(owner, owner);
            state.SetWriteMode();
            var readiness = new Readiness(true);
            using var surface = new ContextualAiCuiSurface(state, readiness);
            Assert.Equal(CuiSceneAvailabilityState.Ready, (await surface.InitializeAsync("files", "Files", bounded.Token)).State);
            var window = new Window { Content = surface, Width = 800, Height = 600 }; window.Show();
            Task<AppAiActionResult>? execution = null;
            Exception? primary = null;
            var cleanup = new List<Exception>();
            try
            {
                fault.FailCompletion = true;
                execution = state.ExecuteActionAsync(new("files", "files.save",
                    JsonSerializer.SerializeToElement(new { exact = "committed" }), null, "original-operation", AppAiAccessMode.Write), bounded.Token).AsTask();
                string? requestID = null;
                for (var attempt = 0; attempt < 200 && requestID is null; attempt++)
                {
                    requestID = (await home.Permissions.GetSnapshotAsync(cancellationToken: bounded.Token)).PendingRequests.SingleOrDefault()?.RequestId;
                    if (requestID is null) await Task.Delay(10, bounded.Token);
                }
                Assert.NotNull(requestID);
                Assert.Equal(requestID, await prompt.Displayed.Task.WaitAsync(bounded.Token));
                Assert.False(execution.IsCompleted);
                Assert.True((await home.Permissions.DecideAsync(requestID!, HomeApprovalChoice.Accept, cancellationToken: bounded.Token)).Succeeded);
                prompt.ReleaseReview(); // Display acknowledgment never makes the durable decision.
                var result = await execution;
                Assert.True(result.Succeeded);
                Assert.True(state.HasPendingActionAudit);
                Assert.Equal(1, owner.Executions);
                Assert.Equal("committed", await File.ReadAllTextAsync(owner.Path, bounded.Token));
                for (var attempt = 0; attempt < 100 && !surface.GetVisualDescendants().OfType<Button>().Any(button => Equals(button.Content, "Finish audit")); attempt++)
                    await Task.Delay(10, bounded.Token);
                var finish = Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Finish audit"));
                readiness.Ready = false; // Completion must not replay or depend on a fresh app admission.
                finish.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                for (var attempt = 0; attempt < 200 && state.HasPendingActionAudit; attempt++) await Task.Delay(10, bounded.Token);
                Assert.False(state.HasPendingActionAudit);
                Assert.Equal(1, owner.Executions);
                Assert.Equal("committed", await File.ReadAllTextAsync(owner.Path, bounded.Token));
                Assert.Equal(HomePermissionRequestState.Succeeded, (await home.Permissions.GetAuthorizationAsync(requestID!, bounded.Token)).State);
                var snapshot = await home.Permissions.GetSnapshotAsync(cancellationToken: bounded.Token);
                Assert.Empty(snapshot.PendingRequests);
                Assert.Single(snapshot.RecentAuditEvents, entry => entry.ResultCode == "HOME_ACTION_SUCCEEDED");
                Assert.Single(snapshot.RecentAuditEvents, entry => entry.ResultCode == "HOME_EXECUTION_STARTED");
                await Assert.ThrowsAsync<InvalidOperationException>(async () => await state.FinishActionAuditAsync(bounded.Token));
                Assert.Equal(1, owner.Executions);
            }
            catch (Exception error) { primary = error; }
            finally
            {
                // Release the SAME pending review and await the SAME original action on every refusal path.
                try { prompt.ReleaseReview(); }
                catch (Exception error) { cleanup.Add(error); }
                try { if (execution is not null) await execution; }
                catch (Exception error)
                { if (!ReferenceEquals(error, primary) && !cleanup.Any(item => ReferenceEquals(item, error))) cleanup.Add(error); }
                try { window.Close(); }
                catch (Exception error)
                { if (!ReferenceEquals(error, primary) && !cleanup.Any(item => ReferenceEquals(item, error))) cleanup.Add(error); }
            }
            if (cleanup.Count != 0) throw new AggregateException("Original finish-audit test and cleanup failures retained.",
                primary is null ? cleanup : new[] { primary }.Concat(cleanup));
            if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
        finally { Directory.Delete(root, true); }
    }

    // Controlled display-only port for this audit test. The real broker decision remains the explicit call above.
    private sealed class ManuallyAcknowledgedPrompt : IHomeApprovalPromptPresenter
    {
        public TaskCompletionSource<string> Displayed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> ShowPendingRequestAsync(string requestId, CancellationToken cancellationToken)
        {
            if (!Displayed.TrySetResult(requestId)) throw new InvalidOperationException("The original review cannot be replayed.");
            await _released.Task.WaitAsync(cancellationToken);
            return true;
        }
        public void ReleaseReview() => _released.TrySetResult();
    }

    private sealed class DurableAuditOwner(string path) : IAppAiContext, IAppAiActions
    {
        public string Path { get; } = path;
        public int Executions { get; private set; }
        public IReadOnlyList<AppAiActionDescriptor> Actions { get; } =
            [new("files.save", "Save", "Controlled durable owning action", AppAiActionRisk.ReversibleChange, true,
                "{\"type\":\"object\"}", AffectedObjectIds: ["file-1"])];
        public ValueTask<AppAiContextSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new AppAiContextSnapshot("files", "test", "file-1", "Controlled owner", null,
                new Dictionary<string, JsonElement>(), AppAiDataSensitivity.UserContent, DateTimeOffset.UtcNow));
        public async ValueTask<AppAiActionResult> ExecuteAsync(AppAiActionRequest request, CancellationToken cancellationToken)
        {
            Assert.Equal("files.save", request.ActionId);
            Executions++;
            await File.WriteAllTextAsync(Path, "committed", cancellationToken);
            return AppAiActionResult.Success("Owner already committed");
        }
    }
    private sealed class AuditFaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool FailCompletion;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default) => inner.ReadAsync(cancellationToken);
        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRevision, CancellationToken cancellationToken = default)
        {
            if (FailCompletion && record.Payload.GetRawText().Contains("HOME_ACTION_SUCCEEDED", StringComparison.Ordinal))
            { FailCompletion = false; throw new UnauthorizedAccessException("Controlled completion acknowledgment fault."); }
            return inner.WriteAsync(record, expectedRevision, cancellationToken);
        }
    }
    private sealed class AuditInvocations : IInvocationResolver
    {
        public ValueTask<IReadOnlyList<InvocationToken>> ResolveAsync(IReadOnlyList<InvocationToken> tokens, CancellationToken cancellationToken) => ValueTask.FromResult(tokens);
    }
    private sealed class AuditGraph : IExecutionEventRepository
    {
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExecutionEvent>>([]);
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExecutionSummary>>([]);
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
