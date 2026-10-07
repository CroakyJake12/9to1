using Haven.Application;
using Haven.Core;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>Real Dev service/coordinator/runtime with synthetic policy and filesystem providers.
/// These controls do not certify Home/Windows permissions or native execution.</summary>
public sealed partial class DeveloperTaskWorkspaceServiceTests
{
    [Fact]
    public async Task Actual_file_store_reopen_resolves_same_existing_project_without_copy_or_create()
    {
        var f = await Fixture.CreateAsync();
        var directory = Path.Combine(Path.GetTempPath(), "dev-canonical-reopen-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileDeveloperWorkspaceStore(directory);
            Assert.True((await store.CreateAsync(f.Store.Workspace, cancellationToken: OriginalTestBodyToken)).Succeeded);
            var service = new DeveloperTaskWorkspaceService(new FileDeveloperWorkspaceStore(directory),
                new(new Conversations(f.Conversation), f.Containers), f.Tasks, f.Owner, new(f.Tools));
            var opened = await service.ResolveAsync(f.Reference, cancellationToken: OriginalTestBodyToken);
            var reopened = await service.ResolveAsync(f.Reference, cancellationToken: OriginalTestBodyToken);
            Assert.True(opened.Succeeded);
            Assert.True(reopened.Succeeded);
            Assert.Equal(f.Reference, reopened.Value!.Reference);
            Assert.Equal(opened.Value!.Root, reopened.Value.Root);
            Assert.Equal(opened.Value.Repository, reopened.Value.Repository);
            Assert.Equal(f.Current.ContextId, f.Conversation.Id);
            Assert.Equal(0, f.Tools.ReadCalls);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
    [Fact]
    public async Task Resolve_preserves_existing_workspace_project_root_and_repository_identity()
    {
        var f = await Fixture.CreateAsync();
        var result = await f.Dev.ResolveAsync(f.Reference, cancellationToken: OriginalTestBodyToken);
        Assert.True(result.Succeeded);
        Assert.Same(f.Store.Workspace, result.Value!.Workspace);
        Assert.Equal(f.Reference.ProjectId, result.Value.Project.ProjectId);
        Assert.Equal("repo-existing", result.Value.Repository!.CanonicalRepositoryId);
        Assert.Equal(0, f.Store.CreateCalls);
        Assert.Equal(0, f.Tools.ReadCalls);
    }

    [Fact]
    public async Task Stale_workspace_and_project_revisions_refuse_before_any_tool_preparation()
    {
        var f = await Fixture.CreateAsync();
        var staleWorkspace = await f.Dev.ResolveAsync(f.Reference with { WorkspaceRevision = 2 }, cancellationToken: OriginalTestBodyToken);
        var staleProject = await f.Dev.ResolveAsync(f.Reference with { ProjectRevision = 2 }, cancellationToken: OriginalTestBodyToken);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, staleWorkspace.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, staleProject.Error!.Code);
        Assert.Equal(0, f.Owner.PrepareCalls);
    }

    [Fact]
    public async Task Caller_project_metadata_cannot_select_a_different_actual_conversation_root()
    {
        var f = await Fixture.CreateAsync();
        f.Containers.Container = f.Containers.Container with { RootPath = Path.GetFullPath("other-root") };
        var result = await f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken);
        Assert.Equal(DeveloperOperationErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Equal(0, f.Owner.PrepareCalls);
        Assert.Equal(0, f.Tools.ReadCalls);
    }

    [Fact]
    public async Task Source_read_does_not_inherit_execution_trust_but_process_requires_it()
    {
        var f = await Fixture.CreateAsync(trusted: false);
        var read = await f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken);
        Assert.True(read.Succeeded);
        Assert.Equal("original source", read.Value!.OriginalToolResult!.Output);
        var run = await f.Dev.RunTestsAsync(f.Reference, f.Context(), "dotnet test", cancellationToken: OriginalTestBodyToken);
        Assert.Equal(DeveloperOperationErrorCode.PermissionRequired, run.Error!.Code);
        Assert.Equal(0, f.Tools.ProcessCalls);
    }

    [Fact]
    public async Task Wrong_context_or_stale_task_revision_does_not_dispatch()
    {
        var f = await Fixture.CreateAsync();
        var wrong = await f.Dev.ReadFileAsync(f.Reference, f.Context() with { ContextId = Guid.NewGuid() }, f.Document, cancellationToken: OriginalTestBodyToken);
        var stale = await f.Dev.ReadFileAsync(f.Reference, f.Context() with { PersistenceRevision = f.Current.PersistenceRevision + 1 }, f.Document, cancellationToken: OriginalTestBodyToken);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, wrong.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, stale.Error!.Code);
        Assert.Equal(0, f.Owner.ExecuteCalls);
    }

    [Fact]
    public async Task Identical_submit_revalidates_observation_of_same_business_original_and_runs_tool_once()
    {
        var f = await Fixture.CreateAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Owner.BeforeBody = release.Task;
        var context = f.Context();
        var first = f.Dev.ReadFileAsync(f.Reference, context, f.Document, cancellationToken: OriginalTestBodyToken);
        Task<DeveloperOperationResult<DeveloperActionObservation>>? duplicate = null;
        try
        {
            await f.Owner.Entered.Task;
            duplicate = f.Dev.ReadFileAsync(f.Reference, context, f.Document, cancellationToken: OriginalTestBodyToken);
            Assert.NotSame(first, duplicate); // fresh authority driver; SAME business body is not redispatched
            Assert.Equal(1, f.Owner.ExecuteCalls);
            Assert.False(first.IsCompleted);
        }
        finally { release.TrySetResult(); try { await first; } finally { if (duplicate is not null) await duplicate; } }
        Assert.Same(await first, await duplicate!);
        Assert.Equal(1, f.Tools.ReadCalls);
        Assert.Equal(context.TaskId, (await first).Value!.AcknowledgedTask.TaskId);
        Assert.Equal(context.ExecutionId, (await first).Value!.AcknowledgedTask.ExecutionId);
    }

    [Fact]
    public async Task Accepted_action_reopen_observes_original_ledger_without_running_again()
    {
        var f = await Fixture.CreateAsync();
        var context = f.Context();
        var first = await f.Dev.ReadFileAsync(f.Reference, context, f.Document, cancellationToken: OriginalTestBodyToken);
        var second = await f.Dev.ReadFileAsync(f.Reference, context, f.Document, cancellationToken: OriginalTestBodyToken);
        Assert.True(first.Succeeded);
        Assert.True(second.Value!.AlreadyAcknowledged);
        Assert.Null(second.Value.OriginalToolResult);
        Assert.Equal(1, f.Tools.ReadCalls);
        Assert.Equal(context.ActionId, second.Value.Action!.ActionId);
    }

    [Fact]
    public async Task Preparation_revision_callback_cannot_replace_project_before_dispatch()
    {
        var f = await Fixture.CreateAsync();
        f.Owner.OnPrepare = () => f.Store.Workspace = f.Store.Workspace with { Revision = 2 };
        var actual = f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken);
        await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.Equal(0, f.Tools.ReadCalls);
        Assert.Equal(0, f.Owner.ExecuteCalls);
        await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Git_prefix_resolution_is_owned_before_callback_and_close_waits_same_original()
    {
        var f = await Fixture.CreateAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.BeforeRead = async () => { entered.TrySetResult(); await release.Task; };
        var actual = f.Dev.GitAsync(f.Reference, f.Context(), DeveloperGitOperation.Status, cancellationToken: OriginalTestBodyToken);
        Task? close = null;
        try
        {
            await entered.Task;
            close = f.Dev.CloseAndDrainAsync();
            Assert.Same(close, f.Dev.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.False(actual.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            try { await actual; } finally { if (close is not null) await close; }
        }
        Assert.Equal(1, f.Tools.ProcessCalls);
    }

    [Fact]
    public async Task Callback_join_is_refused_but_request_does_not_cancel_admitted_business_body()
    {
        var f = await Fixture.CreateAsync();
        var denials = 0;
        f.Owner.OnPrepare = () =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = f.Dev.CloseAndDrainAsync(); });
            denials++;
            f.Dev.RequestRetirement();
        };
        var result = await f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken);
        Assert.True(result.Succeeded);
        Assert.Equal(1, denials);
        Assert.Equal(1, f.Tools.ReadCalls);
        await f.Dev.CloseAndDrainAsync();
        Assert.Throws<InvalidOperationException>(() => { _ = f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken); });
    }

    [Fact]
    public async Task Original_compound_store_fault_retains_both_exact_causes_and_no_tool_effect()
    {
        var f = await Fixture.CreateAsync();
        var first = new IOException("first actual source failure");
        var second = new InvalidDataException("second actual source failure");
        var failed = new TaskCompletionSource<DeveloperOperationResult<DeveloperWorkspace>>();
        failed.SetException([first, second]);
        f.Store.ActualRead = failed.Task;
        var actual = f.Dev.GitAsync(f.Reference, f.Context(), DeveloperGitOperation.Status, cancellationToken: OriginalTestBodyToken);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.True(Contains(error, first));
        Assert.True(Contains(error, second));
        var closing = await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
        Assert.True(Contains(closing, first));
        Assert.True(Contains(closing, second));
        Assert.Equal(0, f.Tools.ProcessCalls);
    }

    [Fact]
    public async Task Nonzero_test_exit_remains_real_failed_result_despite_owner_accepting_process()
    {
        var f = await Fixture.CreateAsync();
        f.Tools.Process = new(1, "one test failed", "", TimeSpan.FromMilliseconds(2), false);
        var result = await f.Dev.RunTestsAsync(f.Reference, f.Context(), "dotnet test", cancellationToken: OriginalTestBodyToken);
        Assert.True(result.Succeeded);
        Assert.Same(f.Tools.Process, result.Value!.OriginalProcessResult);
        Assert.False(result.Value.ProcessSucceeded);
        Assert.Equal(TaskPlanNodeState.Completed, result.Value.Action!.State);
        Assert.NotNull(result.Value.OwnerReceiptReference);
        Assert.Contains("Exit code: 1", result.Value.OriginalToolResult!.Output);
    }

    [Fact]
    public async Task Unknown_owner_effect_is_not_accepted_or_replayed_and_blocks_clean_drain()
    {
        var f = await Fixture.CreateAsync();
        f.Owner.UnknownOutcome = true;
        var context = f.Context();
        var edit = new DeveloperReviewedTextEdit(f.Document, new string('a', 64), "replacement");
        var actual = f.Dev.ApplyEditAsync(f.Reference, context, edit, cancellationToken: OriginalTestBodyToken);
        var result = await actual;
        Assert.Equal(TaskPlanNodeState.RequiresReexecution, result.Value!.Action!.State);
        Assert.Null(result.Value.OwnerReceiptReference);
        Assert.True(result.Value.RequiresOutcomeInspection);
        // Observe the real retained producer, rather than treating a public authority-validation
        // wrapper as that producer. Both old work and unknown outcome must be conserved.
        var originals = Assert.IsAssignableFrom<System.Collections.IDictionary>(typeof(DeveloperTaskWorkspaceService)
            .GetField("_originals", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(f.Dev));
        var retained = Assert.Single(originals.Values.Cast<object>());
        var originalTask = Assert.IsAssignableFrom<Task>(retained.GetType().GetField("Original")!.GetValue(retained));
        Assert.Same(actual, originalTask);
        var originalSources = Assert.IsAssignableFrom<List<Task>>(retained.GetType().GetField("Sources")!.GetValue(retained));
        var originalOwnerTask = Assert.Single(originalSources.OfType<Task<TaskRunToolActionResult>>());
        var originalPreparationCount = f.Owner.PrepareCalls;
        var revalidations = ((Lease)f.Attempt.Lease).Revalidations;
        var observation = f.Dev.ApplyEditAsync(f.Reference, context, edit, cancellationToken: OriginalTestBodyToken);
        Assert.NotSame(actual, observation);
        Assert.Same(result, await observation);
        Assert.Same(originalTask, retained.GetType().GetField("Original")!.GetValue(retained));
        Assert.Same(originalOwnerTask, Assert.Single(originalSources.OfType<Task<TaskRunToolActionResult>>()));
        Assert.True(((Lease)f.Attempt.Lease).Revalidations > revalidations);
        Assert.Equal(originalPreparationCount, f.Owner.PrepareCalls);
        Assert.Equal(1, f.Owner.ExecuteCalls);
        Assert.Equal(0, f.Tools.WriteCalls);
        // Copied IDs after actual actor/attempt retirement do not disclose that old result.
        ((Lease)f.Attempt.Lease).Retired = true;
        var denied = f.Dev.ApplyEditAsync(f.Reference, context with { }, edit, cancellationToken: OriginalTestBodyToken);
        var originalRefusal = await Assert.ThrowsAnyAsync<Exception>(() => denied);
        Assert.True(ContainsType<UnauthorizedAccessException>(originalRefusal));
        Assert.Equal(1, f.Owner.ExecuteCalls);
        Assert.Equal(originalPreparationCount, f.Owner.PrepareCalls);
        Assert.Equal(TaskPlanNodeState.RequiresReexecution, f.Current.Plan.Single(value => value.ActionId == context.ActionId).State);
        var originalCloseFailure = await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
        Assert.True(Contains(originalCloseFailure, originalRefusal));
    }

    [Theory]
    [InlineData("../outside.cs")]
    [InlineData("src/../outside.cs")]
    public async Task Traversal_is_rejected_before_store_or_tool_access(string path)
    {
        var f = await Fixture.CreateAsync();
        var reads = f.Store.GetCalls;
        Assert.Throws<ArgumentException>(() => { _ = f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document with { RelativePath = path }, cancellationToken: OriginalTestBodyToken); });
        Assert.Equal(reads, f.Store.GetCalls);
        Assert.Equal(0, f.Tools.ReadCalls);
    }

    [Fact]
    public async Task Copied_known_context_does_not_disclose_original_after_actor_retirement()
    {
        var f = await Fixture.CreateAsync(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Owner.BeforeBody = release.Task; var context = f.Context();
        var first = f.Dev.ReadFileAsync(f.Reference, context, f.Document, cancellationToken: OriginalTestBodyToken);
        Task<DeveloperOperationResult<DeveloperActionObservation>>? duplicate = null;
        try
        {
            await f.Owner.Entered.Task;
            await f.Attempt.Lease.DisposeAsync();
            duplicate = f.Dev.ReadFileAsync(f.Reference with { }, context with { }, f.Document, cancellationToken: OriginalTestBodyToken);
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => duplicate);
            Assert.True(ContainsType<UnauthorizedAccessException>(refused));
            Assert.Equal(1, f.Owner.ExecuteCalls);
            Assert.False(first.IsCompleted);
        }
        finally { release.TrySetResult(); try { await first; } catch { } if (duplicate is not null) try { await duplicate; } catch { } }
        var originalError = await Assert.ThrowsAnyAsync<Exception>(() => first);
        Assert.True(ContainsType<UnauthorizedAccessException>(originalError));
        await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Retirement_between_original_join_and_observation_disclosure_refuses_result()
    {
        var f = await Fixture.CreateAsync(); var releaseBody = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var heldValidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Owner.BeforeBody = releaseBody.Task; var context = f.Context();
        var first = f.Dev.ReadFileAsync(f.Reference, context, f.Document, cancellationToken: OriginalTestBodyToken);
        Task<DeveloperOperationResult<DeveloperActionObservation>>? duplicate = null;
        try
        {
            await f.Owner.Entered.Task;
            var lease = (Lease)f.Attempt.Lease;
            lease.BeforeRevalidate = () =>
            {
                if (lease.Revalidations == 4) { validationEntered.TrySetResult(); return heldValidation.Task; }
                return Task.CompletedTask;
            };
            duplicate = f.Dev.ReadFileAsync(f.Reference, context with { }, f.Document, cancellationToken: OriginalTestBodyToken);
            releaseBody.TrySetResult(); Assert.True((await first).Succeeded);
            await validationEntered.Task; // actual post-join disclosure validation is now held
            await lease.DisposeAsync(); heldValidation.TrySetResult();
            var denied = await Assert.ThrowsAnyAsync<Exception>(() => duplicate);
            Assert.True(ContainsType<UnauthorizedAccessException>(denied));
            Assert.Equal(1, f.Tools.ReadCalls);
        }
        finally { releaseBody.TrySetResult(); heldValidation.TrySetResult(); try { await first; } catch { } if (duplicate is not null) try { await duplicate; } catch { } }
        await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Close_joins_actual_observation_driver_even_after_business_original_finishes()
    {
        var f = await Fixture.CreateAsync(); var releaseBody = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseValidation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var validationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Owner.BeforeBody = releaseBody.Task; var context = f.Context();
        var first = f.Dev.ReadFileAsync(f.Reference, context, f.Document, cancellationToken: OriginalTestBodyToken);
        Task<DeveloperOperationResult<DeveloperActionObservation>>? duplicate = null; Task? close = null;
        try
        {
            await f.Owner.Entered.Task;
            var lease = (Lease)f.Attempt.Lease;
            lease.BeforeRevalidate = () => { validationEntered.TrySetResult(); return releaseValidation.Task; };
            duplicate = f.Dev.ReadFileAsync(f.Reference, context, f.Document, cancellationToken: OriginalTestBodyToken);
            await validationEntered.Task;
            close = f.Dev.CloseAndDrainAsync(); Assert.Same(close, f.Dev.CloseAndDrainAsync());
            lease.BeforeRevalidate = null; releaseBody.TrySetResult(); Assert.True((await first).Succeeded);
            Assert.False(close.IsCompleted); Assert.False(duplicate.IsCompleted);
        }
        finally { releaseBody.TrySetResult(); releaseValidation.TrySetResult(); await first; if (duplicate is not null) await duplicate; if (close is not null) await close; }
        Assert.Equal(1, f.Tools.ReadCalls);
    }

    [Fact]
    public async Task Actual_driver_final_validation_is_joined_and_its_callback_cannot_self_join()
    {
        var f = await Fixture.CreateAsync(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var lease = (Lease)f.Attempt.Lease;
        lease.BeforeRevalidate = () =>
        {
            if (++calls == 2)
            {
                Assert.Throws<InvalidOperationException>(() => { _ = f.Dev.CloseAndDrainAsync(); });
                entered.TrySetResult(); return release.Task;
            }
            return Task.CompletedTask;
        };
        var actual = f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken); Task? close = null;
        try { await entered.Task; close = f.Dev.CloseAndDrainAsync(); Assert.False(actual.IsCompleted); Assert.False(close.IsCompleted); }
        finally { release.TrySetResult(); await actual; if (close is not null) await close; }
        Assert.Equal(1, f.Tools.ReadCalls);
    }

    [Fact]
    public async Task Real_conversation_selection_change_during_container_await_refuses_old_root()
    {
        var f = await Fixture.CreateAsync(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Containers.BeforeRead = () => { entered.TrySetResult(); return release.Task; };
        var actual = f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken);
        try
        {
            await entered.Task;
            f.ConversationSource.Current = f.Conversation with { ContainerId = Guid.NewGuid() };
            release.TrySetResult();
            var result = await actual;
            Assert.Equal(DeveloperOperationErrorCode.PermissionDenied, result.Error!.Code);
            Assert.Equal(0, f.Owner.PrepareCalls); Assert.Equal(0, f.Tools.ReadCalls);
        }
        finally { release.TrySetResult(); await actual; }
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Actual_container_root_change_during_final_conversation_read_refuses_stale_selection()
    {
        var f = await Fixture.CreateAsync(); var reads = 0;
        f.ConversationSource.BeforeRead = () =>
        {
            if (++reads == 2) f.Containers.Container = f.Containers.Container with { RootPath = Path.GetFullPath("changed-root") };
            return Task.CompletedTask;
        };
        var result = await f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken);
        Assert.Equal(DeveloperOperationErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Equal(0, f.Owner.PrepareCalls); Assert.Equal(0, f.Tools.ReadCalls);
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Restored_context_repository_callback_after_await_cannot_join_its_actual_owner()
    {
        var f = await Fixture.CreateAsync(); var originalContext = ExecutionContext.Capture()!;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var conversationReads = 0; var containerReads = 0; var denials = 0;
        f.ConversationSource.BeforeRead = () =>
        {
            if (++conversationReads == 2) { entered.TrySetResult(); return release.Task; }
            return Task.CompletedTask;
        };
        f.Containers.BeforeRead = () =>
        {
            if (++containerReads == 2) ExecutionContext.Run(originalContext, _ =>
            { Assert.Throws<InvalidOperationException>(() => { _ = f.Dev.CloseAndDrainAsync(); }); denials++; }, null);
            return Task.CompletedTask;
        };
        var actual = f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken);
        try { await entered.Task; release.TrySetResult(); Assert.True((await actual).Succeeded); }
        finally { release.TrySetResult(); await actual; }
        Assert.Equal(1, denials); await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Actual_raw_container_task_is_enrolled_before_await_and_close_joins_it()
    {
        var f = await Fixture.CreateAsync(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<IReadOnlyList<ContainerDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Containers.OriginalReadSource = () => { entered.TrySetResult(); return raw.Task; };
        var actual = f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken); Task? close = null;
        try
        {
            await entered.Task;
            var field = typeof(DeveloperTaskWorkspaceService).GetField("_originals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var originals = (System.Collections.IDictionary)field.GetValue(f.Dev)!;
            var metadataGate = typeof(DeveloperTaskWorkspaceService).GetField("_gate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(f.Dev)!;
            var timer = System.Diagnostics.Stopwatch.StartNew(); var enrolled = false;
            while (!enrolled)
            {
                lock (metadataGate)
                {
                    var invocation = originals.Values.Cast<object>().Single();
                    var retained = (IEnumerable<Task>)invocation.GetType().GetField("Sources")!.GetValue(invocation)!;
                    enrolled = retained.Any(value => ReferenceEquals(value, raw.Task));
                }
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), "The actual raw repository Task was not enrolled before await.");
                if (!enrolled) await Task.Yield();
            }
            Assert.True(enrolled);
            close = f.Dev.CloseAndDrainAsync(); Assert.False(close.IsCompleted); Assert.False(actual.IsCompleted);
        }
        finally { raw.TrySetResult([f.Containers.Container]); try { await actual; } finally { if (close is not null) await close; } }
        Assert.Equal(1, f.Tools.ReadCalls);
    }

    [Fact]
    public async Task Raw_container_sibling_faults_survive_business_and_independent_close()
    {
        var f = await Fixture.CreateAsync(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raw = new TaskCompletionSource<IReadOnlyList<ContainerDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new IOException("actual repository first"); var second = new OperationCanceledException("faulted repository sibling");
        f.Containers.OriginalReadSource = () => { entered.TrySetResult(); return raw.Task; };
        var actual = f.Dev.ReadFileAsync(f.Reference, f.Context(), f.Document, cancellationToken: OriginalTestBodyToken); Task? close = null;
        try
        {
            await entered.Task; close = f.Dev.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
            raw.TrySetException([first, second]);
            var bodyError = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.True(Contains(bodyError, first)); Assert.True(Contains(bodyError, second)); Assert.False(actual.IsCanceled);
            var closeError = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(Contains(closeError, first)); Assert.True(Contains(closeError, second)); Assert.Equal(0, f.Tools.ReadCalls);
        }
        finally { raw.TrySetException([first, second]); try { await actual; } catch { } if (close is not null) try { await close; } catch { } }
    }

    [Fact]
    public async Task Git_transport_uses_literal_platform_syntax_and_same_registered_process()
    {
        var f = await Fixture.CreateAsync(trusted: true);
        var result = await f.Dev.GitAsync(f.Reference, f.Context(), DeveloperGitOperation.Status, cancellationToken: OriginalTestBodyToken);
        Assert.True(result.Succeeded); Assert.Equal(1, f.Tools.ProcessCalls);
        var request = Assert.IsType<ProcessRequest>(f.Tools.LastProcessRequest);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("powershell.exe", request.FileName);
            var encoded = request.Arguments.Split(' ').Last();
            var command = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
            Assert.StartsWith("& 'git' '--no-pager' '-C' ", command);
        }
        else
        {
            Assert.Equal("/bin/sh", request.FileName); Assert.Equal("", request.Arguments);
            Assert.Equal("-c", request.ArgumentList![0]); Assert.StartsWith("'git' '--no-pager' '-C' ", request.ArgumentList[1]);
            Assert.Contains("'status' '--porcelain=v1' '--branch'", request.ArgumentList[1]);
        }
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Direct_edit_records_same_run_checkpoint_before_non_git_file_effect_and_restores()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dev-checkpoint-control-" + Guid.NewGuid().ToString("N"));
        try
        {
            var f = await Fixture.CreateAsync(rootLocation: directory);
            Directory.CreateDirectory(Path.Combine(directory, "src"));
            var path = Path.Combine(directory, "src", "code.cs");
            await File.WriteAllTextAsync(path, f.Tools.TextValue, OriginalTestBodyToken);
            var context = f.Context(); var edit = ReviewedEdit(f, "changed source");
            f.Tools.OnWrite = () =>
            {
                Assert.NotNull(f.Current.CheckpointId);
                Assert.Equal(f.CheckpointStore.LastSaved!.Id, f.Current.CheckpointId);
                Assert.True(f.CheckpointStore.OriginalSave!.IsCompletedSuccessfully);
                Assert.Empty(f.CheckpointStore.Versions);
            };
            var result = await f.Dev.ApplyEditAsync(f.Reference, context, edit, OriginalTestBodyToken);
            Assert.True(result.Succeeded); Assert.True(result.Value!.OriginalToolResult!.Activity.Succeeded);
            Assert.Equal(context.TaskId, f.Current.TaskId); Assert.Equal(context.ExecutionId, f.Current.ExecutionId);
            var checkpoint = Assert.IsType<CheckpointInfo>(f.CheckpointStore.LastSaved);
            Assert.Equal(context.ContextId, checkpoint.ConversationId);
            Assert.Equal(f.Conversation.ContainerId, checkpoint.ContainerId);
            Assert.Equal(directory, checkpoint.WorkspaceRoot);
            Assert.Equal(0, checkpoint.StartSequence);
            Assert.Equal(checkpoint.Id, f.Current.CheckpointId);
            Assert.Same(checkpoint, await f.Checkpoints.GetOriginalCheckpointAsync(context.ExecutionId, checkpoint.Id, OriginalTestBodyToken));
            var version = Assert.Single(f.CheckpointStore.Versions).Version;
            Assert.Equal(context.ContextId, version.ConversationId); Assert.Equal(checkpoint.ContainerId, version.ContainerId);
            Assert.Equal("original source", version.BeforeContent); Assert.Equal("changed source", version.AfterContent);
            Assert.Equal("changed source", await File.ReadAllTextAsync(path, OriginalTestBodyToken));
            Assert.False(Directory.Exists(Path.Combine(directory, ".git"))); Assert.Equal(0, f.Tools.ProcessCalls);
            // The accepted action is observed rather than replayed, even with its old input revision.
            var again = await f.Dev.ApplyEditAsync(f.Reference, context, edit, OriginalTestBodyToken);
            Assert.True(again.Value!.AlreadyAcknowledged); Assert.Equal(1, f.CheckpointStore.SaveCalls); Assert.Equal(1, f.Tools.WriteCalls);
            f.Tools.OnWrite = null;
            Assert.Equal(new[] { f.Document.RelativePath }, await f.Checkpoints.RestoreCheckpointAsync(checkpoint.Id, OriginalTestBodyToken));
            Assert.Equal("original source", await File.ReadAllTextAsync(path, OriginalTestBodyToken));
            await f.Dev.CloseAndDrainAsync();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task Direct_edit_held_original_checkpoint_save_refuses_early_effect_and_retirement_waits()
    {
        var f = await Fixture.CreateAsync(); var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.CheckpointStore.ActualSave = save.Task;
        var actual = f.Dev.ApplyEditAsync(f.Reference, f.Context(), ReviewedEdit(f, "changed source"), OriginalTestBodyToken); Task? close = null;
        try
        {
            await f.CheckpointStore.SaveEntered.Task;
            Assert.Same(save.Task, f.CheckpointStore.OriginalSave);
            Assert.Equal(0, f.Tools.WriteCalls); Assert.Null(f.Current.CheckpointId);
            close = f.Dev.CloseAndDrainAsync(); Assert.False(close.IsCompleted); Assert.False(actual.IsCompleted);
            save.TrySetResult(); Assert.True((await actual).Succeeded); await close;
            Assert.Equal(1, f.Tools.WriteCalls); Assert.Equal(1, f.CheckpointStore.SaveCalls);
        }
        finally { save.TrySetResult(); try { await actual; } catch { } if (close is not null) try { await close; } catch { } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Direct_edit_faulted_original_save_never_mutates_replays_or_borrows_saved_row(bool savedBeforeFault)
    {
        var directory = Path.Combine(Path.GetTempPath(), "dev-checkpoint-fault-" + Guid.NewGuid().ToString("N"));
        try
        {
            var f = await Fixture.CreateAsync(rootLocation: directory);
            Directory.CreateDirectory(Path.Combine(directory, "src")); var path = Path.Combine(directory, "src", "code.cs");
            await File.WriteAllTextAsync(path, f.Tools.TextValue, OriginalTestBodyToken);
            var originalFault = new OperationCanceledException("faulted original checkpoint source OCE", new CancellationToken(canceled: true));
            var sibling = new InvalidDataException("original checkpoint sibling failure");
            var faulted = new TaskCompletionSource(); faulted.SetException([originalFault, sibling]);
            f.CheckpointStore.ActualSave = faulted.Task; f.CheckpointStore.PersistOnSave = savedBeforeFault;
            var context = f.Context(); var edit = ReviewedEdit(f, "changed source");
            var actual = f.Dev.ApplyEditAsync(f.Reference, context, edit, OriginalTestBodyToken);
            var failure = await Assert.ThrowsAnyAsync<Exception>(async () => await actual);
            Assert.True(Contains(failure, originalFault)); Assert.True(Contains(failure, sibling));
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Same(faulted.Task, f.CheckpointStore.OriginalSave); Assert.True(f.CheckpointStore.OriginalSave!.IsFaulted);
            Assert.Equal(savedBeforeFault, f.CheckpointStore.Checkpoints.Count != 0);
            // The test persistence provider really writes the record before returning
            // the faulted original Task; disk/row existence cannot publish an ACK.
            var savedCheckpoint = f.CheckpointStore.LastSaved!;
            var savedPath = Path.Combine(f.CheckpointStore.PersistenceDirectory!, savedCheckpoint.Id.ToString("N") + ".json");
            Assert.Equal(savedBeforeFault, File.Exists(savedPath));
            Assert.Null(f.Current.CheckpointId); Assert.Null(f.Current.LastCheckpointActionId); Assert.Equal(0, f.Tools.WriteCalls);
            Assert.Null(await f.Checkpoints.GetOriginalCheckpointAsync(context.ExecutionId, savedCheckpoint.Id, OriginalTestBodyToken));
            var sameAction = f.Dev.ApplyEditAsync(f.Reference, context with { }, edit, OriginalTestBodyToken);
            await Assert.ThrowsAnyAsync<Exception>(async () => await sameAction);
            // A fresh action under the SAME execution cannot turn row existence into an ACK or launch another save.
            var nextAction = f.Dev.ApplyEditAsync(f.Reference, f.Context(), edit, OriginalTestBodyToken);
            var refused = await Assert.ThrowsAnyAsync<Exception>(async () => await nextAction);
            Assert.Contains("second save is refused", refused.ToString(), StringComparison.Ordinal);
            Assert.Equal(1, f.CheckpointStore.SaveCalls); Assert.Equal(0, f.Tools.WriteCalls);
            Assert.Equal("original source", await File.ReadAllTextAsync(path, OriginalTestBodyToken));
            var drain = await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
            Assert.True(Contains(drain, originalFault)); Assert.True(Contains(drain, sibling));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("root")]
    [InlineData("actor")]
    [InlineData("revision")]
    [InlineData("policy")]
    public async Task Direct_edit_checkpoint_callbacks_cannot_replace_original_dispatch_basis(string change)
    {
        var f = await Fixture.CreateAsync(); var save = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.CheckpointStore.ActualSave = save.Task;
        var actual = f.Dev.ApplyEditAsync(f.Reference, f.Context(), ReviewedEdit(f, "changed source"), OriginalTestBodyToken);
        try
        {
            await f.CheckpointStore.SaveEntered.Task; Assert.Equal(0, f.Tools.WriteCalls);
            if (change == "root") f.Containers.Container = f.Containers.Container with { RootPath = Path.GetFullPath("different-root") };
            if (change == "actor") ((Lease)f.Attempt.Lease).Retired = true;
            if (change == "revision") await f.Repository.UpsertAsync(f.Current with { PersistenceRevision = f.Current.PersistenceRevision + 1 }, OriginalTestBodyToken);
            if (change == "policy") f.Checkpoints.Mode = CheckpointMode.Off;
            save.TrySetResult(); await Assert.ThrowsAnyAsync<Exception>(async () => await actual);
            Assert.Equal(0, f.Tools.WriteCalls); Assert.Null(f.Current.LastCheckpointActionId);
            await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
        }
        finally { save.TrySetResult(); try { await actual; } catch { } }
    }

    [Fact]
    public async Task Direct_edit_root_replacement_during_post_checkpoint_actor_check_refuses_before_effect()
    {
        var f = await Fixture.CreateAsync();
        ((Lease)f.Attempt.Lease).BeforeRevalidate = () =>
        {
            if (f.Current.CheckpointId is not null)
                f.Containers.Container = f.Containers.Container with { RootPath = Path.GetFullPath("replaced-after-checkpoint-root") };
            return Task.CompletedTask;
        };
        await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.ApplyEditAsync(f.Reference, f.Context(), ReviewedEdit(f, "changed source"), OriginalTestBodyToken));
        Assert.NotNull(f.Current.CheckpointId); Assert.Null(f.Current.LastCheckpointActionId); Assert.Equal(0, f.Tools.WriteCalls);
        await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Direct_edit_actual_policy_off_skips_checkpoint_and_preserves_actual_container_history()
    {
        var f = await Fixture.CreateAsync(); f.Checkpoints.Mode = CheckpointMode.Off;
        var context = f.Context(); var result = await f.Dev.ApplyEditAsync(f.Reference, context, ReviewedEdit(f, "changed source"), OriginalTestBodyToken);
        Assert.True(result.Succeeded); Assert.Equal(1, f.Tools.WriteCalls); Assert.Equal(0, f.CheckpointStore.SaveCalls);
        Assert.Null(f.Current.CheckpointId);
        Assert.Equal(f.Conversation.ContainerId, Assert.Single(f.CheckpointStore.Versions).Version.ContainerId);
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Direct_edit_legacy_missing_producer_refuses_before_mutation_while_read_still_works()
    {
        var f = await Fixture.CreateAsync();
        var legacy = new DeveloperTaskWorkspaceService(f.Store, new(f.ConversationSource, f.Containers), f.Tasks, f.Owner, new(f.Tools), new Trust(true));
        Assert.True((await legacy.ReadFileAsync(f.Reference, f.Context(), f.Document, OriginalTestBodyToken)).Succeeded);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => legacy.ApplyEditAsync(f.Reference, f.Context(), ReviewedEdit(f, "changed source"), OriginalTestBodyToken));
        Assert.Contains("checkpoint producer is unavailable", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(0, f.CheckpointStore.SaveCalls);
        await Assert.ThrowsAnyAsync<Exception>(() => legacy.CloseAndDrainAsync());
        await f.Dev.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Direct_edit_rejects_checkpoint_from_another_producer_or_cached_container()
    {
        var f = await Fixture.CreateAsync();
        var other = new CheckpointService(f.CheckpointStore, new Restore(f.Tools));
        var service = new DeveloperTaskWorkspaceService(f.Store, new(f.ConversationSource, f.Containers), f.Tasks, f.Owner,
            new(f.Tools, f.CheckpointStore), new Trust(true), other, f.ConversationSource);
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => service.ApplyEditAsync(f.Reference, f.Context(), ReviewedEdit(f, "changed source"), OriginalTestBodyToken));
        Assert.Contains("same original execution", failure.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, f.Tools.WriteCalls); Assert.Null(f.Current.CheckpointId);
        await Assert.ThrowsAnyAsync<Exception>(() => service.CloseAndDrainAsync()); await f.Dev.CloseAndDrainAsync();
        var cached = await Fixture.CreateAsync();
        await cached.Checkpoints.EnsureBeforeMutationAsync(cached.Current.ExecutionId, cached.Current.ContextId, Guid.NewGuid(),
            cached.Document.WorkspaceRoot, cached.Checkpoints.Mode, OriginalTestBodyToken);
        var mismatch = await Assert.ThrowsAnyAsync<Exception>(() => cached.Dev.ApplyEditAsync(cached.Reference, cached.Context(), ReviewedEdit(cached, "changed source"), OriginalTestBodyToken));
        Assert.Contains("different current edit workspace/context", mismatch.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, cached.CheckpointStore.SaveCalls); Assert.Equal(0, cached.Tools.WriteCalls); Assert.Null(cached.Current.CheckpointId);
        await Assert.ThrowsAnyAsync<Exception>(() => cached.Dev.CloseAndDrainAsync());
    }

    [Fact]
    public async Task Direct_edit_checkpoint_record_saved_then_faulted_never_dispatches_file_runtime()
    {
        var f = await Fixture.CreateAsync(); var fault = new IOException("checkpoint task row saved then faulted");
        var original = Task.FromException(fault); f.Repository.CheckpointWriteSource = original;
        var actual = f.Dev.ApplyEditAsync(f.Reference, f.Context(), ReviewedEdit(f, "changed source"), OriginalTestBodyToken);
        var failure = await Assert.ThrowsAnyAsync<Exception>(async () => await actual);
        Assert.True(Contains(failure, fault)); Assert.NotNull(f.Current.CheckpointId); Assert.Null(f.Current.LastCheckpointActionId);
        Assert.Equal(0, f.Tools.WriteCalls); Assert.Equal(1, f.CheckpointStore.SaveCalls);
        var drain = await Assert.ThrowsAnyAsync<Exception>(() => f.Dev.CloseAndDrainAsync()); Assert.True(Contains(drain, fault));
    }

    private static DeveloperReviewedTextEdit ReviewedEdit(Fixture f, string replacement) =>
        new(f.Document, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(f.Tools.TextValue))), replacement);

    // This whole fixture is compiled by Dev.Tests (xUnit 2, net10) and linked
    // by Desktop.Tests (xUnit 3, net10-windows). WINDOWS is the owning target symbol.
    private static CancellationToken OriginalTestBodyToken
    {
        get
        {
#if WINDOWS
            return Xunit.TestContext.Current.CancellationToken;
#else
            return CancellationToken.None;
#endif
        }
    }

    private static bool ContainsType<T>(Exception error) where T : Exception => error is T ||
        error is AggregateException compound && compound.InnerExceptions.Any(value => ContainsType<T>(value));

    private static bool Contains(Exception envelope, Exception actual) => ReferenceEquals(envelope, actual) ||
        envelope is AggregateException compound && compound.InnerExceptions.Any(value => Contains(value, actual));

    private sealed class Fixture
    {
        public Store Store = null!; public Containers Containers = null!; public Tools Tools = new(); public Owner Owner = null!;
        public Conversation Conversation = null!; public Conversations ConversationSource = null!;
        public TaskRepository Repository = new(); public TaskExecutionCoordinator Tasks = null!; public DeveloperTaskWorkspaceService Dev = null!;
        public CheckpointHistory CheckpointStore = new(); public CheckpointService Checkpoints = null!;
        public DeveloperProjectReference Reference = null!; public DeveloperCodeDocument Document = null!; public TaskRunAttemptAdmission Attempt = null!;
        public TaskExecutionSnapshot Current => Repository.Current!;
        public DeveloperCanonicalActionContext Context() => new(Current.TaskId, Current.ExecutionId, Current.ContextId,
            Attempt.AttemptId, Current.PersistenceRevision, Guid.NewGuid());
        public static async Task<Fixture> CreateAsync(bool trusted = true, string? rootLocation = null)
        {
            var f = new Fixture(); var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), rootLocation ?? Path.GetFullPath("dev-accepted-root"));
            f.Tools.PhysicalFiles = rootLocation is not null;
            f.CheckpointStore.PersistenceDirectory = rootLocation is null ? null : Path.Combine(rootLocation, ".checkpoint-test-records");
            var project = new DeveloperProject(Guid.NewGuid(), "existing", "existing project", [root.RootId], "C#", null, "dotnet", [], [], [], [], null);
            var workspace = DeveloperWorkspace.Create([root]) with { Projects = [project], SourceControlBindings = [new("scm-existing", root.RootId, "git", "repo-existing")] };
            f.Store = new() { Workspace = workspace };
            f.Reference = new(workspace.WorkspaceId, 1, project.ProjectId, 1, root.RootId, "scm-existing");
            f.Document = new(workspace.WorkspaceId, project.ProjectId, Guid.NewGuid(), "file-existing", root.Location, "src/code.cs", 1);
            var now = DateTimeOffset.UtcNow; var container = new ContainerDefinition(Guid.NewGuid(), HavenMode.Tasks, "accepted", root.Location, "", "", now, now);
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task, "original context", container.Id, null, false, false, now, now);
            f.Conversation = conversation;
            f.Containers = new() { Container = container }; f.Owner = new(f.Tools);
            f.Checkpoints = new(f.CheckpointStore, new Restore(f.Tools));
            f.Tasks = new(f.Repository, new Events(), admissionAuthority: new Authority(), toolActionOwner: f.Owner,
                checkpointRepository: f.CheckpointStore, checkpointObservationSource: f.Checkpoints);
            var started = await f.Tasks.BeginAuthorizedAsync(conversation.Id, Guid.NewGuid(), "original task", TaskExecutionDurability.PersistedPlan, [], default);
            f.Attempt = await f.Tasks.StartAttemptAsync(started.TaskId, started.ExecutionId, new("local", 1, "synthetic", "synthetic", null, false, []), default);
            f.ConversationSource = new(conversation);
            f.Dev = new(f.Store, new(f.ConversationSource, f.Containers), f.Tasks, f.Owner, new(f.Tools, f.CheckpointStore),
                new Trust(trusted), f.Checkpoints, f.ConversationSource);
            return f;
        }
    }

    private sealed class Store : IDeveloperWorkspaceStore
    {
        public DeveloperWorkspace Workspace = null!; public int CreateCalls; public int GetCalls;
        public Func<Task>? BeforeRead; public Task<DeveloperOperationResult<DeveloperWorkspace>>? ActualRead;
        public Task<DeveloperOperationResult<DeveloperWorkspace>> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            GetCalls++; if (ActualRead is not null) return ActualRead;
            return Read();
            async Task<DeveloperOperationResult<DeveloperWorkspace>> Read()
            { if (BeforeRead is not null) await BeforeRead(); return DeveloperOperationResult<DeveloperWorkspace>.Success(Workspace); }
        }
        public Task<DeveloperOperationResult<DeveloperWorkspace>> CreateAsync(DeveloperWorkspace workspace, CancellationToken cancellationToken = default)
        { CreateCalls++; Workspace = workspace; return Task.FromResult(DeveloperOperationResult<DeveloperWorkspace>.Success(workspace)); }
        public Task<DeveloperOperationResult<DeveloperWorkspace>> SaveAsync(DeveloperWorkspace workspace, long expectedRevision, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Trust(bool trusted) : IDeveloperWorkspaceTrustService
    { public Task<bool> IsTrustedAsync(Guid workspaceId, CancellationToken token) => Task.FromResult(trusted); }
    private sealed class TaskRepository : ITaskExecutionRepository
    {
        public TaskExecutionSnapshot? Current; public Task? CheckpointWriteSource;
        public Task UpsertAsync(TaskExecutionSnapshot snapshot, CancellationToken token)
        { if (snapshot.PersistenceRevision != (Current?.PersistenceRevision ?? 0) + 1) throw new InvalidOperationException("Synthetic CAS conflict"); Current = snapshot; return snapshot.CheckpointId is not null && CheckpointWriteSource is not null ? CheckpointWriteSource : Task.CompletedTask; }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Current?.TaskId == id ? Current : null);
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => Task.FromResult(Current?.ContextId == id ? Current : null);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(Current is null ? [] : [Current]);
    }
    private sealed class Events : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
    private sealed class Authority : ITaskRunCommandAuthority
    {
        private TaskExecutionOwnerBinding? _owner;
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot task, CancellationToken token)
        {
            var owner = new TaskExecutionOwnerBinding(task.TaskId, task.ContextId, task.ExecutionId, "synthetic actor", "synthetic profile", null, null, "revision", "synthetic receipt");
            _owner = owner; return Task.FromResult(owner);
        }
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot task, Guid id, TaskRunRouteCandidate candidate, Guid? previous, CancellationToken token) => Task.FromResult<ITaskRunAdmissionLease>(new Lease(task.OwnerBinding!, id, candidate));
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot task, Guid attempt, Guid action, string receipt, CancellationToken token) => Task.CompletedTask;
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot task, string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (_owner is null || task.OwnerBinding != _owner) throw new UnauthorizedAccessException("The synthetic original task-command owner changed.");
            return Task.CompletedTask;
        }
    }
    private sealed class Lease(TaskExecutionOwnerBinding owner, Guid id, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner; public Guid AttemptId => id; public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "synthetic attempt";
        public bool Retired; public Func<Task>? BeforeRevalidate; public int Revalidations;
        public async ValueTask RevalidateAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Revalidations++;
            if (Retired) throw new UnauthorizedAccessException("synthetic actor/attempt retired");
            if (BeforeRevalidate is not null) await BeforeRevalidate();
            if (Retired) throw new UnauthorizedAccessException("synthetic actor/attempt retired during validation");
        }
        public ValueTask DisposeAsync() { Retired = true; return ValueTask.CompletedTask; }
    }
    private sealed class Owner(Tools tools) : ITaskRunToolActionOwner
    {
        public int PrepareCalls; public int ExecuteCalls; public Action? OnPrepare; public Task? BeforeBody; public bool UnknownOutcome;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string name) => runtime == ToolRuntimeKind.Workspace;
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission attempt, TaskExecutionSnapshot current, Guid action,
            OllamaToolCall call, ToolRuntimeKind runtime, PermissionMode permission, string? root, CancellationToken token)
        { PrepareCalls++; OnPrepare?.Invoke(); return Task.FromResult<ITaskRunToolActionPreparation>(new Preparation(tools, attempt, action, root!, call)); }
        public async Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation preparation, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token)
        {
            ExecuteCalls++; Entered.TrySetResult(); if (BeforeBody is not null) await BeforeBody;
            var actual = UnknownOutcome ? new WorkspaceToolResult(new(Guid.NewGuid(), "unknown", "unknown", false, TimeSpan.Zero, DateTimeOffset.UtcNow), "unknown") : await body(token);
            var readOnly = preparation.InterruptionPolicy == TaskActionInterruptionPolicy.ReadOnlyCancellable;
            return new(actual, !readOnly && !UnknownOutcome ? "synthetic-owned-effect" : null, readOnly && !UnknownOutcome, false);
        }
        public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class Preparation(Tools tools, TaskRunAttemptAdmission attempt, Guid action, string root, OllamaToolCall call) : IWorkspaceToolActionPreparation
    {
        public TaskRunAttemptAdmission OriginalAttempt => attempt; public Guid ActionId => action;
        public TaskActionInterruptionPolicy InterruptionPolicy => call.Name is "read_file" or "preview_change_set" ? TaskActionInterruptionPolicy.ReadOnlyCancellable : TaskActionInterruptionPolicy.AtomicCommit;
        public IReadOnlyList<string> RequiredPermissionScopes => ["workspace:" + call.Name];
        public TaskOriginalToolIntent OriginalToolIntent => new(ToolRuntimeKind.Workspace.ToString(), call.Name, root, WorkspaceToolOriginalDigest.Call(call));
        public string CanonicalWorkspaceRoot => root; public OllamaToolCall OriginalCall => call;
        public IWorkspaceOriginalInvocation? OriginalInvocation => null;
        public bool IsIssuedOriginalRuntime(IWorkspaceToolService actual, string actualRoot, OllamaToolCall actualCall) => ReferenceEquals(actual, tools) && actualRoot == root && WorkspaceToolOriginalDigest.Call(actualCall) == WorkspaceToolOriginalDigest.Call(call);
        public Task<WorkspaceToolResult> RunOriginalRuntimeAsync(IWorkspaceToolService service, string actualRoot, OllamaToolCall actualCall, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token) => body(token);
    }
    private sealed class Tools : IWorkspaceToolService
    {
        public int ReadCalls; public int WriteCalls; public string TextValue = "original source"; public Task? BeforeWrite; public int ProcessCalls; public ProcessRequest? LastProcessRequest; public ProcessResult Process = new(0, "observed", "", TimeSpan.Zero, false);
        public bool PhysicalFiles; public Action? OnWrite;
        public string ResolveWorkspacePath(string root, string path) => Path.GetFullPath(Path.Combine(root, path));
        public async Task<string> ReadTextAsync(string root, string path, CancellationToken token)
        { ReadCalls++; token.ThrowIfCancellationRequested(); return PhysicalFiles ? await File.ReadAllTextAsync(ResolveWorkspacePath(root, path), token) : TextValue; }
        public async Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token)
        {
            if (BeforeWrite is not null) await BeforeWrite; token.ThrowIfCancellationRequested(); OnWrite?.Invoke();
            if (PhysicalFiles)
            {
                var destination = ResolveWorkspacePath(root, path); var temporary = destination + ".dev-test-" + Guid.NewGuid().ToString("N");
                try { await File.WriteAllTextAsync(temporary, content, token); File.Move(temporary, destination, overwrite: true); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            WriteCalls++; TextValue = content;
        }
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string search, CancellationToken token) => throw new NotSupportedException();
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) { ProcessCalls++; LastProcessRequest = request; return Task.FromResult(Process); }
    }
    // Explicit test persistence/restore providers; the production CheckpointService,
    // TaskCoordinator and WorkspaceToolRuntime remain the SAME maintained owners.
    // This control does not certify SQLite, native resource permission or Home policy.
    private sealed class CheckpointHistory : ICheckpointRepository, IWorkspaceStateRepository
    {
        public Dictionary<Guid, CheckpointInfo> Checkpoints = []; public List<(long Sequence, WorkspaceVersion Version)> Versions = [];
        public int SaveCalls; public bool PersistOnSave = true; public CheckpointInfo? LastSaved; public Task? ActualSave; public Task? OriginalSave;
        public string? PersistenceDirectory;
        public TaskCompletionSource SaveEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SaveAsync(CheckpointInfo checkpoint, CancellationToken token)
        {
            SaveCalls++; LastSaved = checkpoint;
            if (PersistOnSave)
            {
                if (PersistenceDirectory is not null)
                {
                    Directory.CreateDirectory(PersistenceDirectory);
                    File.WriteAllText(Path.Combine(PersistenceDirectory, checkpoint.Id.ToString("N") + ".json"), System.Text.Json.JsonSerializer.Serialize(checkpoint));
                }
                Checkpoints[checkpoint.Id] = checkpoint;
            }
            OriginalSave = ActualSave ?? Task.CompletedTask; SaveEntered.TrySetResult(); return OriginalSave;
        }
        public Task<CheckpointInfo?> GetLatestAsync(Guid? conversation, string root, CancellationToken token) =>
            Task.FromResult(Checkpoints.Values.LastOrDefault(value => value.ConversationId == conversation && value.WorkspaceRoot == root));
        public Task<CheckpointInfo?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Checkpoints.GetValueOrDefault(id));
        public Task<long> GetLatestVersionSequenceAsync(string root, CancellationToken token) => Task.FromResult(Versions.Where(value => value.Version.WorkspaceRoot == root).Select(value => value.Sequence).DefaultIfEmpty().Max());
        public Task<IReadOnlyList<WorkspaceRestoreEntry>> GetVersionsSinceAsync(string root, long sequence, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<WorkspaceRestoreEntry>>(Versions.Where(value => value.Sequence > sequence && value.Version.WorkspaceRoot == root)
                .Select(value => new WorkspaceRestoreEntry(value.Sequence, value.Version.RelativePath, (int)value.Version.Kind, value.Version.BeforeContent, value.Version.AfterContent)).ToArray());
        public Task<WorkspaceRestoreEntry?> GetLatestVersionAsync(string root, CancellationToken token) =>
            Task.FromResult(Versions.Where(value => value.Version.WorkspaceRoot == root).Select(value => new WorkspaceRestoreEntry(value.Sequence, value.Version.RelativePath, (int)value.Version.Kind, value.Version.BeforeContent, value.Version.AfterContent)).LastOrDefault());
        public Task AddVersionAsync(WorkspaceVersion version, CancellationToken token) { Versions.Add((Versions.Count + 1, version)); return Task.CompletedTask; }
        public Task<IReadOnlyList<WorkspaceVersion>> GetVersionsAsync(Guid? container, string? path, int limit, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<WorkspaceVersion>>(Versions.Select(value => value.Version).Where(value => value.ContainerId == container && (path is null || value.RelativePath == path)).TakeLast(limit).ToArray());
        public Task<IReadOnlyList<ReusableTaskDefinition>> GetReusableTasksAsync(Guid? container, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertReusableTaskAsync(ReusableTaskDefinition macro, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteReusableTaskAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<DecisionRecord>> GetDecisionsAsync(Guid container, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertDecisionAsync(DecisionRecord decision, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteDecisionAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Restore(Tools tools) : ICheckpointRestorer
    {
        public async Task<IReadOnlyList<string>> RestoreAsync(string root, CheckpointRestorePlan plan, CancellationToken token)
        { foreach (var value in plan.PathToBeforeContent) await tools.WriteTextAtomicAsync(root, value.Key, value.Value, token); return plan.PathToBeforeContent.Keys.ToArray(); }
    }
    private sealed class Conversations(Conversation actual) : IConversationRepository
    {
        public Conversation Current = actual; public Func<Task>? BeforeRead;
        public async Task<Conversation?> GetAsync(Guid id, CancellationToken token)
        { if (BeforeRead is not null) await BeforeRead(); return id == Current.Id ? Current : null; }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) => throw new NotSupportedException();
        public Task AddMessageAsync(ChatMessage value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Containers : IContainerRepository
    {
        public ContainerDefinition Container = null!; public Func<Task>? BeforeRead;
        public Func<Task<IReadOnlyList<ContainerDefinition>>>? OriginalReadSource;
        public Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken token) =>
            OriginalReadSource is not null ? OriginalReadSource() : ReadCoreAsync();
        private async Task<IReadOnlyList<ContainerDefinition>> ReadCoreAsync()
        { if (BeforeRead is not null) await BeforeRead(); return [Container]; }
        public Task UpsertAsync(ContainerDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task<Lesson> CreateSubjectAsync(ContainerDefinition value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertLessonAsync(Lesson value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteLessonAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
}
