using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Actual_private_saved_project_binding_is_reused_while_other_root_metadata_cannot_mint_one()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        var raw = new List<Task>(); var errors = new List<Exception>(); var expected = new List<Exception>();
        Task<IDeveloperWorkspaceOriginalExecutionBinding>? wrong = null;
        try
        {
            await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
            var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture,
                rig.Permission, intent.Steps[^1], token);
            var workspace = saved.OriginalStoreResult.Workspace;
            var project = new DeveloperResolvedProject(Reference(intent), workspace, workspace.Projects.Single(), workspace.Roots.Single(), null);
            void Retain(Task actual) { lock (raw) raw.Add(actual); }
            var source = Assert.IsAssignableFrom<IDeveloperWorkspaceOriginalProjectExecutionBindingSource>(rig.Effector);
            var first = await source.ResolveOriginalProjectBindingAsync(project, body => body(), Retain, token);
            var second = await source.ResolveOriginalProjectBindingAsync(project, body => body(), Retain, token);
            Assert.Same(first, second);
            Assert.True(source.IsIssuedOriginalBinding(first));
            Assert.Equal(project.Root.RootId, first.RootId);
            Assert.Equal(project.Root.Location, first.CanonicalRoot);
            await source.ValidateOriginalProjectBindingAsync(project, first, body => body(), Retain, token);
            var copied = project with { Reference = project.Reference with { RootId = Guid.NewGuid() } };
            wrong = source.ResolveOriginalProjectBindingAsync(copied, body => body(), Retain, token);
            var refusal = await Assert.ThrowsAnyAsync<Exception>(() => wrong);
            var leaves = PreparedProjectLeaves(wrong.Exception ?? refusal).ToArray();
            Assert.Single(leaves);
            Assert.IsType<UnauthorizedAccessException>(leaves[0]);
            expected.AddRange(leaves);
            Assert.True(source.IsIssuedOriginalBinding(first));
            Assert.Equal(project.Root.RootId, first.RootId);
            // The source observes ownership; no Home execute consent or process exists.
            Assert.All(raw, task => Assert.True(task.IsCompleted));
        }
        catch (Exception error) { Capture(null, error); }
        finally
        {
            if (wrong is not null) try { await wrong; } catch (Exception error) { Capture(wrong, error); }
            Task[] originals; lock (raw) originals = raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var original in originals) try { await original; } catch (Exception error) { Capture(original, error); }
            Task? close = null; try { close = rig.DisposeAsync().AsTask(); } catch (Exception error) { Capture(null, error); }
            if (close is not null) try { await close; } catch (Exception error) { Capture(close, error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Capture(Task? original, Exception error)
        {
            foreach (var leaf in PreparedProjectLeaves(original?.Exception ?? error))
                if (!expected.Any(value => ReferenceEquals(value, leaf)) && !errors.Any(value => ReferenceEquals(value, leaf))) errors.Add(leaf);
        }
    }
    [LinuxDirectoryFact]
    public async Task Empty_original_parent_failure_remains_the_same_unknown_cause_and_stops_actual_binding_factory()
    {
        var rig = await Rig.Create(true, true); var token = TestContext.Current.CancellationToken;
        var original = new AggregateException("actual empty parent fault");
        var raw = new List<Task>(); var errors = new List<Exception>(); Task<IDeveloperWorkspaceOriginalExecutionBinding>? actual = null;
        try
        {
            await AcknowledgeBeforeWorkspaceSave(rig, token); var intent = rig.Prepared.Intent;
            var saved = await rig.Effector.ExecuteOriginalWorkspaceMetadataStepAsync(rig.Prepared, rig.Capture,
                rig.Permission, intent.Steps[^1], token);
            var workspace = saved.OriginalStoreResult.Workspace;
            var project = new DeveloperResolvedProject(Reference(intent), workspace, workspace.Projects.Single(), workspace.Roots.Single(), null);
            var consents = new UnreachablePreparedConsentSource(); var tool = new UnreachablePreparedToolOwner();
            var trust = new FilesDeveloperOriginalProjectExecutionTrust(rig.Effector, consents, tool);
            actual = trust.ResolveOriginalBindingAsync(project, body => { body(); throw original; },
                task => raw.Add(task), token);
            var caught = await Assert.ThrowsAnyAsync<Exception>(() => actual);
            Assert.True(actual.IsFaulted);
            Assert.False(actual.IsCanceled);
            Assert.Same(original, Assert.Single(PreparedProjectLeaves(actual.Exception ?? caught)));
            Assert.Same(actual, Assert.Single(raw));
            // Inspection-only: the actual configured private source must never have
            // reached its binding factory after failed publication. No receipt is minted.
            var bindings = (System.Collections.IEnumerable)typeof(FilesDeveloperOriginalFolderSetupProducer)
                .GetField("_originalExecutionBindings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(rig.Effector)!;
            Assert.Empty(bindings.Cast<object>());
            Assert.Equal(0, consents.Acquisitions);
            Assert.Equal(0, tool.Preparations);
            Assert.Equal(0, tool.Executions);
        }
        catch (Exception error) { Capture(null, error); }
        finally
        {
            if (actual is not null) try { await actual; } catch (Exception error) { Capture(actual, error); }
            foreach (var source in raw.Distinct<Task>(ReferenceEqualityComparer.Instance))
                try { await source; } catch (Exception error) { Capture(source, error); }
            Task? close = null; try { close = rig.DisposeAsync().AsTask(); } catch (Exception error) { Capture(null, error); }
            if (close is not null) try { await close; } catch (Exception error) { Capture(close, error); }
        }
        if (errors.Count != 0) throw new AggregateException(errors);
        void Capture(Task? source, Exception error)
        {
            foreach (var cause in PreparedProjectLeaves(source?.Exception ?? error))
                if (!ReferenceEquals(cause, original) && !errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
        }
    }
    // Denying counters only. No action preparation, consent, entry or result is issued.
    private sealed class UnreachablePreparedConsentSource : IWorkspaceOriginalProcessStartConsentSource
    {
        internal int Acquisitions;
        public Task<IWorkspaceOriginalProcessStartConsent> AcquireOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding binding,
            IWorkspaceToolActionPreparation preparation, TaskExecutionSnapshot current, Action<Action> scope, CancellationToken token)
        { Acquisitions++; throw new InvalidOperationException("No consent is available in the negative publication control."); }
        public bool IsIssuedOriginalConsent(IWorkspaceOriginalProcessStartConsent consent, ITaskRunToolActionPreparation preparation) => false;
        public Task ValidateOriginalConsentAsync(IWorkspaceOriginalProcessStartConsent consent, ITaskRunToolActionPreparation preparation, CancellationToken token) => throw new InvalidOperationException();
        public Task<IWorkspaceOriginalProcessStartEntry> EnterOriginalProcessStartAsync(IWorkspaceOriginalProcessStartConsent consent, ITaskRunToolActionPreparation preparation, CancellationToken token) => throw new InvalidOperationException();
        public bool IsIssuedOriginalEntry(IWorkspaceOriginalProcessStartConsent consent, ITaskRunToolActionPreparation preparation, IWorkspaceOriginalProcessStartEntry entry) => false;
        public void DemandExternalOriginalProcessStartConsentJoin() { }
    }
    private sealed class UnreachablePreparedToolOwner : ITaskRunToolActionOwner
    {
        internal int Preparations, Executions;
        public bool SupportsCanonicalInvocation(ToolRuntimeKind kind, string name) => false;
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission attempt, TaskExecutionSnapshot current,
            Guid actionId, OllamaToolCall call, ToolRuntimeKind runtime, PermissionMode mode, string? root, CancellationToken token)
        { Preparations++; throw new InvalidOperationException(); }
        public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation preparation, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token)
        { Executions++; throw new InvalidOperationException(); }
        public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token) => throw new InvalidOperationException();
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation preparation, TaskRunToolActionResult result, CancellationToken token) => throw new InvalidOperationException();
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation preparation, TaskExecutionSnapshot current, CancellationToken token) => throw new InvalidOperationException();
    }
    private static IEnumerable<Exception> PreparedProjectLeaves(Exception error) => error is AggregateException { InnerExceptions.Count: > 0 } group
        ? group.InnerExceptions.SelectMany(PreparedProjectLeaves) : new[] { error };
}
