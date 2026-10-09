using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperOriginalWorkspaceSourceReadTests
{
    [Fact]
    public async Task Actual_saved_store_parse_and_close_survive_parent_post_factory_oce_without_cancelled_status()
    {
        var root = Path.Combine(Path.GetTempPath(), "dev-source-read-" + Guid.NewGuid().ToString("N"));
        var errors = new List<Exception>(); var raw = new List<Task>(); Task? actual = null;
        var fault = new OperationCanceledException("Actual synchronous parent failure after raw parse enrollment.");
        try
        {
            var store = new FileDeveloperWorkspaceStore(root);
            var workspace = DeveloperWorkspace.Create([new DeveloperWorkspaceRoot(Guid.NewGuid(), root)]);
            Assert.True((await store.CreateAsync(workspace, CancellationToken.None)).Succeeded);
            var failed = false;
            actual = store.GetWithinOriginalSourceAsync(workspace.WorkspaceId, callback =>
            { callback(); if (!failed && raw.Count == 1) { failed = true; throw fault; } }, raw.Add, CancellationToken.None);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(Leaves(failure), cause => ReferenceEquals(cause, fault));
            Assert.True(raw.Count >= 2); Assert.All(raw, task => Assert.True(task.IsCompletedSuccessfully));
            var current = await store.GetAsync(workspace.WorkspaceId, CancellationToken.None);
            Assert.True(current.Succeeded); Assert.Equal(workspace.WorkspaceId, current.Value!.WorkspaceId);
        }
        catch (Exception error) { Record(null, error); }
        finally
        {
            if (actual is not null) try { await actual; } catch (Exception error) { Record(actual, error); }
            foreach (var task in raw) try { await task; } catch (Exception error) { Record(task, error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { Record(null, error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Record(Task? task, Exception error)
        {
            foreach (var cause in task?.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable())
                foreach (var leaf in Leaves(cause))
                    if (!ReferenceEquals(leaf, fault) && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
    }

    [Fact]
    public async Task Delayed_original_saved_store_callback_cannot_acquire_parse_or_open_a_document()
    {
        var root = Path.Combine(Path.GetTempPath(), "dev-source-read-" + Guid.NewGuid().ToString("N"));
        var errors = new List<Exception>(); var expected = new List<Exception>(); var raw = new List<Task>(); Task? actual = null;
        Action? delayed = null;
        try
        {
            var store = new FileDeveloperWorkspaceStore(root);
            var workspace = DeveloperWorkspace.Create([new DeveloperWorkspaceRoot(Guid.NewGuid(), root)]);
            Assert.True((await store.CreateAsync(workspace, CancellationToken.None)).Succeeded);
            actual = store.GetWithinOriginalSourceAsync(workspace.WorkspaceId, callback => delayed = callback,
                raw.Add, CancellationToken.None);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => actual); expected.AddRange(Leaves(failure));
            Assert.True(actual.IsFaulted); Assert.Empty(raw); Assert.NotNull(delayed);
            Assert.Throws<InvalidOperationException>((Action)(() => delayed!())); Assert.Empty(raw);
            Assert.True((await store.GetAsync(workspace.WorkspaceId, CancellationToken.None)).Succeeded);
        }
        catch (Exception error) { Record(null, error); }
        finally
        {
            if (actual is not null) try { await actual; } catch (Exception error) { Record(actual, error); }
            foreach (var task in raw) try { await task; } catch (Exception error) { Record(task, error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch (Exception error) { Record(null, error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Record(Task? task, Exception error)
        {
            foreach (var cause in task?.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable())
                foreach (var leaf in Leaves(cause))
                    if (!expected.Any(value => ReferenceEquals(value, leaf)) && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
    }
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group
        ? group.InnerExceptions.SelectMany(Leaves) : [error];
}
