using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [Fact]
    public async Task Rich_project_trust_from_another_tool_owner_refuses_before_preparation_or_action_registration()
    {
        var f = await Fixture.CreateAsync(); var trust = new PreparedTrust(f.Owner) { DifferentOwner = true };
        var service = new DeveloperTaskWorkspaceService(f.Store, new(f.ConversationSource, f.Containers), f.Tasks, f.Owner, new(f.Tools), trust);
        Task<DeveloperOperationResult<DeveloperActionObservation>>? actual = null; var errors = new List<Exception>(); Exception? expected = null;
        try
        {
            actual = service.RunTerminalAsync(f.Reference, f.Context(), "controlled command", cancellationToken: OriginalTestBodyToken);
            var observed = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            expected = Assert.Single(PreparedLeaves(actual.Exception ?? observed), cause => cause is UnauthorizedAccessException);
            Assert.Equal(0, trust.ResolveCalls);
            Assert.Equal(0, f.Owner.PrepareCalls);
            Assert.Equal(0, f.Owner.ExecuteCalls);
            Assert.Empty(f.Current.Plan);
        }
        catch (Exception error) { Capture(null, error); }
        finally
        {
            if (actual is not null) try { await actual; } catch (Exception error) { Capture(actual, error); }
            foreach (var owner in new[] { service, f.Dev })
            {
                Task? close = null; try { close = owner.CloseAndDrainAsync(); } catch (Exception error) { Capture(null, error); }
                if (close is not null) try { await close; } catch (Exception error) { Capture(close, error); }
            }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Capture(Task? source, Exception error)
        {
            foreach (var leaf in PreparedLeaves(source?.Exception ?? error))
                if (!ReferenceEquals(leaf, expected) && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
    }

    [Fact]
    public async Task Held_same_preparation_consent_retains_actual_faulted_OCE_and_sibling_without_CAS_or_body_dispatch()
    {
        var f = await Fixture.CreateAsync(); var trust = new PreparedTrust(f.Owner);
        var service = new DeveloperTaskWorkspaceService(f.Store, new(f.ConversationSource, f.Containers), f.Tasks, f.Owner, new(f.Tools), trust);
        trust.JoinOwner = service; var context = f.Context(); var before = f.Current.PersistenceRevision;
        var originalOce = new OperationCanceledException("actual consent fault, not canceled Task");
        var sibling = new IOException("actual consent sibling"); var errors = new List<Exception>();
        Task<DeveloperOperationResult<DeveloperActionObservation>>? actual = null;
        try
        {
            actual = service.RunTerminalAsync(f.Reference, context, "controlled command", cancellationToken: OriginalTestBodyToken);
            await trust.Enrolled.Task.WaitAsync(TimeSpan.FromSeconds(5), OriginalTestBodyToken);
            Assert.False(actual.IsCompleted);
            Assert.Same(f.Attempt, trust.Preparation!.OriginalAttempt);
            Assert.Equal(context.ActionId, trust.Preparation.ActionId);
            Assert.Same(trust.Raw.Task, trust.Retained);
            Assert.Equal(1, f.Owner.PrepareCalls);
            Assert.Equal(0, f.Owner.ExecuteCalls);
            Assert.Equal(before, f.Current.PersistenceRevision);
            Assert.DoesNotContain(f.Current.Plan, node => node.ActionId == context.ActionId);
            trust.Raw.TrySetException([originalOce, sibling]);
            var observed = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            var leaves = PreparedLeaves(actual.Exception ?? observed).ToArray();
            Assert.True(trust.Raw.Task.IsFaulted);
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            Assert.Contains(leaves, cause => ReferenceEquals(cause, originalOce));
            Assert.Contains(leaves, cause => ReferenceEquals(cause, sibling));
            Assert.All(leaves, cause => Assert.True(ReferenceEquals(cause, originalOce) || ReferenceEquals(cause, sibling)));
            Assert.Equal(0, f.Owner.ExecuteCalls);
            Assert.DoesNotContain(f.Current.Plan, node => node.ActionId == context.ActionId);
        }
        catch (Exception error) { Capture(null, error); }
        finally
        {
            trust.Raw.TrySetException([originalOce, sibling]);
            foreach (var source in new Task?[] { actual, trust.Retained })
                if (source is not null) try { await source.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
                    catch (Exception error) { Capture(source, error); }
            foreach (var owner in new[] { service, f.Dev })
            {
                Task? close = null; try { close = owner.CloseAndDrainAsync(); } catch (Exception error) { Capture(null, error); }
                if (close is not null) try { await close; } catch (Exception error) { Capture(close, error); }
            }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Capture(Task? source, Exception error)
        {
            foreach (var leaf in PreparedLeaves(source?.Exception ?? error))
                if (!ReferenceEquals(leaf, originalOce) && !ReferenceEquals(leaf, sibling) && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
    }

    // Synthetic optional trust only controls admission ordering/refusal. No consent,
    // Home actor, native pin, process result, or successful authority is ever returned.
    private sealed class PreparedTrust(ITaskRunToolActionOwner owner) : IDeveloperWorkspaceOriginalProjectExecutionTrustService
    {
        internal bool DifferentOwner; internal int ResolveCalls; internal DeveloperTaskWorkspaceService? JoinOwner;
        internal IWorkspaceToolActionPreparation? Preparation; internal Task? Retained;
        internal readonly TaskCompletionSource<IWorkspaceOriginalProcessStartConsent> Raw = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Enrolled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsBoundToOriginalToolOwner(ITaskRunToolActionOwner sameOwner) => !DifferentOwner && ReferenceEquals(owner, sameOwner);
        public Task<bool> IsTrustedAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("The richer caller must not use the ID-only path.");
        public Task<IDeveloperWorkspaceOriginalExecutionBinding> ResolveOriginalBindingAsync(DeveloperResolvedProject project,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        { ResolveCalls++; return Task.FromResult<IDeveloperWorkspaceOriginalExecutionBinding>(new PreparedBinding(project)); }
        public Task ValidateOriginalBindingAsync(DeveloperResolvedProject project, IDeveloperWorkspaceOriginalExecutionBinding binding,
            Action<Action> scope, Action<Task> retain, CancellationToken token) => Task.CompletedTask;
        public Task<IWorkspaceOriginalProcessStartConsent> AcquireAndBindOriginalConsentAsync(DeveloperResolvedProject project,
            IDeveloperWorkspaceOriginalExecutionBinding binding, IWorkspaceToolActionPreparation preparation, TaskExecutionSnapshot current,
            Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            scope(() =>
            {
                Assert.Throws<InvalidOperationException>((Action)(() => { _ = JoinOwner!.CloseAndDrainAsync(); }));
                Preparation = preparation; Retained = Raw.Task; retain(Raw.Task); Enrolled.TrySetResult();
            });
            return Raw.Task;
        }
        public void DemandExternalOriginalExecutionTrustJoin() { }
    }
    private sealed class PreparedBinding(DeveloperResolvedProject project) : IDeveloperWorkspaceOriginalExecutionBinding
    {
        public Guid WorkspaceId => project.Reference.WorkspaceId;
        public Guid ProjectId => project.Reference.ProjectId;
        public Guid RootId => project.Reference.RootId;
        public long WorkspaceRevision => project.Reference.WorkspaceRevision;
        public string CanonicalRoot => project.Root.Location;
        public AuthenticatedResourceActor OriginalActor => throw new NotSupportedException("This negative control issues no Home actor authority.");
    }
    private static IEnumerable<Exception> PreparedLeaves(Exception error) => error is AggregateException { InnerExceptions.Count: > 0 } group
        ? group.InnerExceptions.SelectMany(PreparedLeaves) : new[] { error };
}
