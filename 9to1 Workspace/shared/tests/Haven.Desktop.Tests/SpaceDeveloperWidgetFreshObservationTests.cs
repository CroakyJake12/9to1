using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

// Owning real Dev/coordinator/page source with explicit controlled repository observations.
// This tests fresh binding/publication only; controlled history is no physical effect,
// authenticated Home, native rendered-frame, available-model or command acceptance witness.
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [AvaloniaTheory]
    [InlineData("current")]
    [InlineData("replace-task")]
    [InlineData("replace-run")]
    [InlineData("move-context")]
    [InlineData("withdraw-presentation")]
    public async Task Widget_joins_project_reopen_and_presents_fresh_same_ID_history_or_refuses_replacement(string control)
    {
        var f = await Fixture.CreateAsync();
        var spaces = new SpaceRegistry(new WidgetExistingSpaceSettings());
        var space = await spaces.CreateAsync("Existing widget project Space", cancellationToken: TestContext.Current.CancellationToken);
        f.ConversationSource.Current = f.ConversationSource.Current with { SpaceId = space.Id };
        var source = new SpaceTaskWorkspaceService(spaces, f.ConversationSource, f.Tasks);
        var development = new SpaceDevelopmentWorkspace(spaces, source, f.Dev);
        var captured = f.Current;
        var attached = await development.AttachAsync(space.Id, space.Revision, captured.ContextId,
            captured.TaskId, captured.ExecutionId, f.Reference, TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.BeforeRead = () => { entered.TrySetResult(); return release.Task; };
        SpaceTaskWidgetPage? page = null;
        Task? activation = null;
        Exception? primary = null;
        var observedRefusal = false;
        try
        {
            page = new(source, f.Tasks, space.Id, captured.ContextId, new NoPresentationReadiness(), development,
                attached.ContextReferenceId, expectedOriginalTaskId: captured.TaskId, expectedOriginalExecutionId: captured.ExecutionId);
            activation = page.ActivateAsync(TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(activation.IsCompleted); Assert.False(release.Task.IsCompleted);
            var actionId = Guid.NewGuid();
            var recorded = new TaskPlanNode(actionId, null, "Controlled newer accepted observation", TaskPlanNodeState.Completed,
                TaskActionInterruptionPolicy.AtomicCommit, 1)
                { Acceptance = new(f.Attempt.AttemptId, "controlled history only; no physical grant", captured.UpdatedAt.AddSeconds(1)) };
            var latest = captured with { PersistenceRevision = captured.PersistenceRevision + 1,
                CheckpointId = Guid.NewGuid(), Plan = [.. captured.Plan, recorded], UpdatedAt = captured.UpdatedAt.AddSeconds(1) };
            f.Repository.Current = control switch
            {
                "replace-task" => latest with { TaskId = Guid.NewGuid() },
                "replace-run" => latest with { ExecutionId = Guid.NewGuid() },
                "move-context" => latest with { ContextId = Guid.NewGuid() },
                _ => latest
            };
            if (control == "withdraw-presentation") page.Deactivate();
            release.TrySetResult();
            if (control == "current")
            {
                await activation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                Assert.True(activation.IsCompletedSuccessfully);
                Assert.True(page.TryGetValue("Identities", out var identities));
                Assert.Contains($"Saved revision {latest.PersistenceRevision}", Assert.IsType<string>(identities));
                Assert.True(page.TryGetValue("Checkpoint", out var checkpoint));
                Assert.Contains(latest.CheckpointId!.Value.ToString("D"), Assert.IsType<string>(checkpoint));
                Assert.True(page.TryGetValue("Plan", out var plan));
                Assert.Contains(actionId.ToString("D"), Assert.IsType<string>(plan));
                Assert.Contains(f.Attempt.AttemptId.ToString("D"), (string)plan!);
                Assert.True(page.TryGetValue("Project", out var project));
                Assert.Contains(f.Reference.ProjectId.ToString("D"), Assert.IsType<string>(project));
                Assert.Equal(captured.TaskId, latest.TaskId); Assert.Equal(captured.ExecutionId, latest.ExecutionId);
                Assert.Equal(captured.ContextId, latest.ContextId); Assert.Same(recorded.Acceptance, latest.Plan[^1].Acceptance);
            }
            else
            {
                await Assert.ThrowsAnyAsync<Exception>(() => activation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                observedRefusal = true; Assert.True(activation.IsFaulted);
                Assert.True(page.TryGetValue("Identities", out var identities));
                Assert.Equal("No canonical task is recorded.", identities);
                Assert.True(page.TryGetValue("Checkpoint", out var checkpoint));
                Assert.Equal("No acknowledged checkpoint is recorded.", checkpoint);
            }
            Assert.True(release.Task.IsCompletedSuccessfully);
            Assert.Equal(0, f.Tools.WriteCalls);
            Assert.Equal(attached.Link.Project, f.Reference);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            release.TrySetResult();
            var errors = new List<Exception>();
            if (activation is not null)
                try { await activation; } catch (Exception error) { if (!observedRefusal) errors.Add(activation.Exception ?? error); }
            if (page is not null)
            {
                page.RequestRetirement(); var actual = page.CloseAndDrainAsync();
                try { await actual; }
                catch (Exception error) { if (!observedRefusal) errors.Add(actual.Exception ?? error); else Assert.True(ContainsType<InvalidOperationException>(actual.Exception ?? error)); }
            }
            f.Store.BeforeRead = null;
            try { await f.Dev.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) { if (primary is not null) errors.Insert(0, primary); throw new AggregateException("Actual widget/Dev fixture originals did not drain.", errors); }
        }
    }

    private sealed class WidgetExistingSpaceSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class
        { token.ThrowIfCancellationRequested(); return Task.FromResult(_values.TryGetValue(key, out var value) ? (T)value : null); }
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class
        { token.ThrowIfCancellationRequested(); _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token)
        { token.ThrowIfCancellationRequested(); _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => throw new NotSupportedException();
    }
}
