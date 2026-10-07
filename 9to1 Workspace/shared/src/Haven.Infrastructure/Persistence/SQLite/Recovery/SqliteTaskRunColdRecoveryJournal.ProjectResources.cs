using Haven.Application;
using Haven.Core;
using System.Runtime.ExceptionServices;

namespace Haven.Infrastructure;

public sealed partial class SqliteTaskRunColdRecoveryJournal : ITaskRunColdOriginalProjectClaimSource,
    ITaskRunColdOriginalProjectBoundarySource, ITaskRunColdConfiguredProjectResourceSource
{
    private readonly object _projectResourceGate = new();
    private ITaskRunColdProjectResourceSource? _projectResources;
    private bool _projectCompositionSealed;

    public void ConfigureOriginalProjectResourceSource(ITaskRunColdProjectResourceSource sameSource)
    {
        ArgumentNullException.ThrowIfNull(sameSource);
        // A configured source proves its exact references before our metadata lock. This
        // fixes composition only; it is neither a resource read nor a permission grant.
        if (!sameSource.HasOriginalColdProjectComposition(this, actors))
            throw new InvalidOperationException("The project source is not paired to this SAME journal/Task actor source.");
        lock (_projectResourceGate)
        {
            if (_projectCompositionSealed || _projectResources is not null)
                throw new InvalidOperationException("Project recovery composition must be fixed before any journal operation.");
            _projectResources = sameSource;
        }
    }

    public bool HasOriginalProjectResourceSource(ITaskRunColdProjectResourceSource sameSource)
    { lock (_projectResourceGate) return ReferenceEquals(_projectResources, sameSource) && _projectResources is not null; }

    public ITaskRunColdProjectResourceSource RequireOriginalProjectResourceSource()
    { lock (_projectResourceGate) return _projectResources ?? throw new InvalidOperationException("No genuine fresh project resource source is configured."); }

    private void SealOriginalProjectComposition()
    { lock (_projectResourceGate) _projectCompositionSealed = true; }

    private sealed class ProjectMaterial(SqliteTaskRunColdRecoveryJournal owner, Claim claim,
        ContextLease context, TaskExecutionSnapshot originalExpected, string capsulePayload)
        : ITaskRunColdOriginalProjectMaterial
    {
        internal readonly SqliteTaskRunColdRecoveryJournal Owner = owner;
        internal readonly Claim Claim = claim;
        internal readonly ContextLease Context = context;
        internal readonly TaskExecutionSnapshot ExpectedSource = originalExpected;
        internal readonly string CapsulePayload = capsulePayload;
        public ITaskRunColdJournalClaim OriginalClaim => Claim;
        public ITaskRunColdContextLease OriginalContext => Context;
        public TaskExecutionSnapshot OriginalExpected => ExpectedSource;
        public TaskRunColdChatInput OriginalInput => UnifiedPersistenceJson.Read<TaskRunColdCapsule>(CapsulePayload).OriginalInput;
        public TaskRunColdProjectIdentity OriginalProjectIdentity =>
            UnifiedPersistenceJson.Read<TaskRunColdCapsule>(CapsulePayload).OriginalProjectIdentity
            ?? throw new InvalidOperationException("The authenticated project descriptor is absent.");
    }

    private sealed partial class ContextLease
    {
        internal ProjectMaterial? ProjectMaterial;
        internal ITaskRunColdProjectResourceSource? ProjectSource;
        internal ITaskRunColdProjectRestorationLease? ProjectLease;
        internal Task? ProjectClose;
        internal bool ProjectPreparationFailed;
        internal bool ProjectLeaseOwned;
        [ThreadStatic] private static Dictionary<ContextLease, int>? _projectPhysical;
        private readonly AsyncLocal<ProjectCloseFrame?> _projectClosing = new();
        private sealed class ProjectCloseFrame(ProjectCloseFrame? parent)
        { internal ProjectCloseFrame? Parent => parent; internal bool Live = true; }

        internal void DemandExternalOriginalProjectContextJoin()
        {
            if (_projectPhysical?.ContainsKey(this) == true)
                throw new InvalidOperationException("An actual project Context callback cannot join its own close.");
            for (var frame = _projectClosing.Value; frame is not null; frame = frame.Parent)
                if (Volatile.Read(ref frame.Live))
                    throw new InvalidOperationException("An actual project Context close cannot join its own retained child.");
            // Historical ownership is cached only from the exact configured issuer.
            // This is a child self-join preflight, never live resource authority.
            if (ProjectLeaseOwned && ProjectLease is { } lease)
                ProjectPhysical(lease.DemandExternalOriginalJoin);
        }
        internal void ProjectPhysical(Action callback)
        {
            var active = _projectPhysical ??= []; active.TryGetValue(this, out var depth); active[this] = depth + 1;
            try { callback(); } finally { if (depth == 0) active.Remove(this); else active[this] = depth; }
        }
        internal T ProjectPhysical<T>(Func<T> callback)
        { T result = default!; ProjectPhysical(() => { result = callback(); }); return result; }
        internal async Task RunOriginalProjectClose(Func<Task> original)
        {
            var prior = _projectClosing.Value; var frame = new ProjectCloseFrame(prior); _projectClosing.Value = frame;
            try { await original().ConfigureAwait(false); }
            finally { Volatile.Write(ref frame.Live, false); _projectClosing.Value = prior; }
        }
    }

    private ProjectMaterial RequireProjectMaterial(ITaskRunColdOriginalProjectMaterial sameMaterial) =>
        sameMaterial is ProjectMaterial actual && ReferenceEquals(actual.Owner, this)
        && ReferenceEquals(actual.Context.ProjectMaterial, actual)
        && ReferenceEquals(actual.Context.Claim, actual.Claim)
        && ReferenceEquals(actual.Claim.Entry.Claim, actual.Claim)
        ? actual : throw new UnauthorizedAccessException("No SAME private journal project material exists.");

    public bool IsIssuedOriginalProjectMaterial(ITaskRunColdOriginalProjectMaterial sameMaterial,
        ITaskRunColdJournalClaim sameClaim, ITaskRunColdContextLease sameContext, TaskExecutionSnapshot sameExpected) =>
        sameMaterial is ProjectMaterial actual && ReferenceEquals(actual.Owner, this)
        && ReferenceEquals(actual.Claim, sameClaim) && ReferenceEquals(actual.Context, sameContext)
        && ReferenceEquals(actual.ExpectedSource, sameExpected) && ReferenceEquals(actual.Context.ProjectMaterial, actual)
        && ReferenceEquals(actual.Claim.Entry.Claim, actual.Claim);

    public async Task<ITaskRunColdOriginalProjectMaterial> GetOriginalProjectMaterialWithinSourceAsync(
        ITaskRunColdJournalClaim sameClaim, ITaskRunColdContextLease sameContext,
        TaskExecutionSnapshot actualExpected, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        var claim = RequireClaim(sameClaim); var context = RequireContext(sameContext);
        if (!ReferenceEquals(context.Claim, claim) || context.Closed || context.Close is not null)
            throw new UnauthorizedAccessException("Only the SAME provisional live project context can issue material.");
        ProjectMaterial? result = null;
        await WithinOriginalSourceAsync(claim.Entry.Sources, originalSynchronousScope, retainOriginalTask, async () =>
        {
            await ValidateOriginalClaimAsync(claim, actualExpected, token).ConfigureAwait(false);
            var capsule = claim.Entry.Capsule;
            TaskRunColdProjectBoundary.DemandOriginalProjectMaterial(capsule);
            OriginalSources(claim.Entry.Sources).Invoke(() =>
            {
                if (context.Closed || context.Close is not null || context.ProjectPreparationFailed)
                    throw new InvalidOperationException("The actual project context is retiring or its preparation is uncertain.");
                if (context.ProjectMaterial is { } prior)
                {
                    if (!ReferenceEquals(prior.ExpectedSource, actualExpected))
                        throw new UnauthorizedAccessException("The project material belongs to another actual expected source.");
                    result = prior;
                }
                else result = context.ProjectMaterial = new(this, claim, context, actualExpected, claim.Entry.Payload);
                return true;
            });
        }).ConfigureAwait(false);
        if (context.Closed || context.Close is not null || context.ProjectPreparationFailed)
            throw new InvalidOperationException("The actual project context retired before material disclosure.");
        return result ?? throw new InvalidOperationException("No actual project material was issued.");
    }

    public Task ValidateOriginalProjectMaterialWithinSourceAsync(ITaskRunColdOriginalProjectMaterial sameMaterial,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        var material = RequireProjectMaterial(sameMaterial);
        return WithinOriginalSourceAsync(material.Claim.Entry.Sources, originalSynchronousScope, retainOriginalTask, async () =>
        {
            if (material.Context.Closed || material.Context.Close is not null || material.Context.ProjectPreparationFailed)
                throw new UnauthorizedAccessException("The original project context is no longer live.");
            await ValidateOriginalClaimAsync(material.Claim, material.ExpectedSource, token).ConfigureAwait(false);
            TaskRunColdProjectBoundary.DemandOriginalProjectMaterial(material.Claim.Entry.Capsule);
            OriginalSources(material.Claim.Entry.Sources).Invoke(() =>
            {
                if (material.Context.Closed || material.Context.Close is not null
                    || UnifiedPersistenceJson.Write(material.ExpectedSource) != material.Claim.Expected)
                    throw new InvalidOperationException("The actual project material/context changed during source reads.");
                return true;
            });
        });
    }

    public Task ValidateOriginalClosedProjectMaterialWithinSourceAsync(ITaskRunColdOriginalProjectMaterial sameMaterial,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        var material = RequireProjectMaterial(sameMaterial); var ack = RequireAcknowledgment(sameAcknowledgment);
        if (!ReferenceEquals(ack.Claim, material.Claim))
            throw new UnauthorizedAccessException("Another claim ACK cannot close the project material.");
        return WithinOriginalSourceAsync(material.Claim.Entry.Sources, originalSynchronousScope, retainOriginalTask, async () =>
        {
            DemandOriginalHealthyClosedProject(OriginalSources(material.Claim.Entry.Sources), material.Context);
            // State=2, current row/input and fresh Task actor. This does not call the
            // open-claim validator or reconstitute an old lease/native Task.
            await ValidateOriginalAcknowledgmentAsync(ack, token).ConfigureAwait(false);
            DemandOriginalHealthyClosedProject(OriginalSources(material.Claim.Entry.Sources), material.Context);
        });
    }

    private static Action<Action> ProjectCaller(TaskRunColdOriginalSourceScope sources, ContextLease context) =>
        callback => context.ProjectPhysical(() => { _ = sources.Invoke(() => { callback(); return true; }); });
    private static Action<Task> ProjectRetainer(TaskRunColdOriginalSourceScope sources, ContextLease context) =>
        actual => context.ProjectPhysical(() => { _ = sources.Invoke(() => actual); });

    private async Task PrepareOriginalProjectContextAsync(ContextLease context,
        TaskExecutionSnapshot expected, CancellationToken token)
    {
        if (context.Claim.Entry.Capsule.OriginalProjectIdentity is null) return;
        var sources = OriginalSources(context.Claim.Entry.Sources);
        var source = sources.Invoke(RequireOriginalProjectResourceSource);
        var material = (ProjectMaterial)await GetOriginalProjectMaterialWithinSourceAsync(context.Claim, context,
            expected, ProjectCaller(sources, context), ProjectRetainer(sources, context), token).ConfigureAwait(false);
        context.ProjectSource = source;
        Task<ITaskRunColdProjectRestorationLease>? actual = null;
        Exception? acquisition = null; Exception? terminal = null;
        try
        {
            _ = context.ProjectPhysical(() => sources.Invoke(() => actual = source.PrepareOriginalProjectRestorationWithinSourceAsync(material,
                ProjectCaller(sources, context), ProjectRetainer(sources, context), token)
                ?? throw new InvalidOperationException("No actual project preparation Task was returned.")));
        }
        catch (Exception cause) { acquisition = cause; }
        if (actual is not null)
            try { context.ProjectLease = await actual.ConfigureAwait(false); }
            catch (Exception cause) { terminal = actual.IsFaulted && actual.Exception is { } group ? group : cause; }
        if (acquisition is not null || terminal is not null)
        {
            context.ProjectPreparationFailed = true;
            if (acquisition is not null) throw new AggregateException("The actual project acquisition and its returned raw Task require inspection.",
                terminal is null ? new[] { acquisition } : new[] { acquisition, terminal });
            ExceptionDispatchInfo.Capture(terminal!).Throw();
        }
        var lease = context.ProjectLease ?? throw new InvalidOperationException("No actual Home project lease was returned.");
        context.ProjectLeaseOwned = context.ProjectPhysical(() => sources.Invoke(() =>
            source.IsOwnedOriginalProjectRestoration(lease, material)));
        if (!context.ProjectLeaseOwned || !context.ProjectPhysical(() => sources.Invoke(() => source.IsIssuedOriginalProjectRestoration(lease, material)))
            || !ReferenceEquals(context.ProjectPhysical(() => sources.Invoke(() => lease.OriginalMaterial)), material))
            throw new UnauthorizedAccessException("The configured Home source did not issue this SAME project lease.");
        var original = context.ProjectPhysical(() => sources.Invoke(() => lease.OriginalPreparation));
        await AwaitActualAsync(sources, () => original).ConfigureAwait(false);
        if (!original.IsCompletedSuccessfully)
            throw new InvalidOperationException("The original project preparation has no successful terminal custody.");
    }

    public Task ValidateOriginalProjectBoundaryWithinSourceAsync(ITaskRunColdJournalClaim sameClaim,
        ITaskRunColdContextLease sameContext, TaskExecutionSnapshot actualExpected,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token)
    {
        var claim = RequireClaim(sameClaim); var context = RequireContext(sameContext);
        if (!ReferenceEquals(context.Claim, claim)) throw new UnauthorizedAccessException("Another context cannot validate this project claim.");
        var material = context.ProjectMaterial ?? throw new InvalidOperationException("No genuine project material was prepared.");
        return WithinOriginalSourceAsync(claim.Entry.Sources, originalSynchronousScope, retainOriginalTask, async () =>
        {
            await ValidateOriginalClaimAsync(claim, actualExpected, token).ConfigureAwait(false);
            if (!ReferenceEquals(material.ExpectedSource, actualExpected) || context.Close is not null
                || context.Closed || context.ProjectPreparationFailed)
                throw new UnauthorizedAccessException("No SAME live project context and expected source remain.");
            var source = context.ProjectSource!; var lease = context.ProjectLease!;
            var sources = OriginalSources(claim.Entry.Sources);
            if (!context.ProjectPhysical(() => sources.Invoke(() => source.IsIssuedOriginalProjectRestoration(lease, material))))
                throw new UnauthorizedAccessException("The SAME project lease is no longer live.");
            await AwaitActualAsync(sources, () => context.ProjectPhysical(() => source.ValidateOriginalProjectRestorationWithinSourceAsync(lease,
                material, ProjectCaller(sources, context), ProjectRetainer(sources, context), token))).ConfigureAwait(false);
            // Task actor is last after all productive resource/journal awaits; Home
            // validation above uses only its already-held native/currentness custody.
            await DemandActorAsync(sources, actualExpected.OwnerBinding!, token).ConfigureAwait(false);
            DemandOriginalProjectBoundaryCommit(claim, context, actualExpected);
        });
    }

    public void DemandOriginalProjectBoundaryCommit(ITaskRunColdJournalClaim sameClaim,
        ITaskRunColdContextLease sameContext, TaskExecutionSnapshot sameExpected)
    {
        var claim = RequireClaim(sameClaim); var context = RequireContext(sameContext);
        var material = context.ProjectMaterial;
        if (!ReferenceEquals(context.Claim, claim) || material is null || !ReferenceEquals(material.ExpectedSource, sameExpected)
            || UnifiedPersistenceJson.Write(sameExpected) != claim.Expected || context.Closed || context.Close is not null
            || context.ProjectPreparationFailed || context.ProjectLease is null || context.ProjectSource is null
            || !ReferenceEquals(claim.ProjectContext, context))
            throw new UnauthorizedAccessException("The SAME current Home project/native commit custody is absent.");
        context.ProjectPhysical(context.ProjectLease.DemandOriginalCommit);
    }

    private void DemandOriginalProjectCommitForClaim(Claim claim)
    {
        if (claim.Entry.Capsule.OriginalProjectIdentity is null) return;
        var context = claim.ProjectContext ?? throw new InvalidOperationException("No privately bound project Context owns this CAS.");
        DemandOriginalProjectBoundaryCommit(claim, context, context.ProjectMaterial!.ExpectedSource);
    }

    private void DemandOriginalHealthyClosedProject(TaskRunColdOriginalSourceScope sources, ContextLease context)
    {
        context.ProjectPhysical(() => sources.Invoke(() =>
        {
        if (!context.Closed || context.Close is not { IsCompletedSuccessfully: true }
            || context.Claim.Close is not { IsCompletedSuccessfully: true }
            || context.ProjectClose is not { IsCompletedSuccessfully: true } || context.ProjectPreparationFailed
            || context.ProjectMaterial is null || context.ProjectSource is null || context.ProjectLease is null
            || !context.ProjectSource.IsOwnedOriginalProjectRestoration(context.ProjectLease, context.ProjectMaterial))
            throw new UnauthorizedAccessException("The SAME project/context/native/CAS originals are not successfully closed.");
        return true;
        }));
    }

    public Task ValidateOriginalClosedProjectBoundaryWithinSourceAsync(ITaskRunColdContextLease sameContext,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        var context = RequireContext(sameContext); var ack = RequireAcknowledgment(sameAcknowledgment);
        if (!ReferenceEquals(context.Claim, ack.Claim)) throw new UnauthorizedAccessException("Another ACK cannot validate this project's closed context.");
        return WithinOriginalSourceAsync(context.Claim.Entry.Sources, originalSynchronousScope, retainOriginalTask, async () =>
        {
            DemandOriginalHealthyClosedProject(OriginalSources(context.Claim.Entry.Sources), context);
            await ValidateOriginalAcknowledgmentAsync(ack, token).ConfigureAwait(false);
            await ValidateOriginalClosedProjectBodyAsync(context, ack, ack.AcknowledgedTask,
                OriginalSources(context.Claim.Entry.Sources), token).ConfigureAwait(false);
            await DemandActorAsync(OriginalSources(context.Claim.Entry.Sources), ack.AcknowledgedTask.OwnerBinding!, token).ConfigureAwait(false);
            DemandOriginalHealthyClosedProject(OriginalSources(context.Claim.Entry.Sources), context);
        });
    }

    private async Task ValidateOriginalClosedProjectBodyAsync(ContextLease context, Acknowledgment ack,
        TaskExecutionSnapshot actualCurrent, TaskRunColdOriginalSourceScope sources, CancellationToken token)
    {
        DemandOriginalHealthyClosedProject(sources, context);
        await AwaitActualAsync(sources, () => context.ProjectPhysical(() => context.ProjectSource!.ValidateOriginalClosedProjectRestorationWithinSourceAsync(
            context.ProjectLease!, context.ProjectMaterial!, ack, actualCurrent,
            ProjectCaller(sources, context), ProjectRetainer(sources, context), token))).ConfigureAwait(false);
        DemandOriginalHealthyClosedProject(sources, context);
    }

    private Task CloseOriginalProjectContextAsync(ContextLease context) =>
        context.RunOriginalProjectClose(() => CloseOriginalProjectContextBodyAsync(context));

    private async Task CloseOriginalProjectContextBodyAsync(ContextLease context)
    {
        var sources = OriginalSources(context.Claim.Entry.Sources);
        var errors = new List<Exception>(); Task? close = null;
        if (context.ProjectLease is { } lease)
        {
            var owned = false;
            InvokeOriginalOwnedProjectCleanup(context, sources, () =>
            {
                owned = context.ProjectSource is { } source && context.ProjectMaterial is { } material
                    && ReferenceEquals(source, RequireOriginalProjectResourceSource())
                    && source.IsOwnedOriginalProjectRestoration(lease, material);
                context.ProjectLeaseOwned = owned;
                if (!owned) throw new UnauthorizedAccessException("The late project product is not owned by this SAME configured issuer/material.");
            }, errors);
            if (owned)
            {
                InvokeOriginalOwnedProjectCleanup(context, sources, lease.RequestOriginalRetirement, errors);
                InvokeOriginalOwnedProjectCleanup(context, sources, () =>
                {
                    close = lease.CloseAndDrainOriginalAsync()
                        ?? throw new InvalidOperationException("No actual project close Task was returned.");
                    context.ProjectClose = close;
                    return close;
                }, errors);
            }
            // An unrecognized result stays with its real issuer/global custody. No
            // typed interface or failed public acquisition authorizes arbitrary close.
        }
        // Fixed privately acquired native store cleanup is independent of Home close.
        InvokeOriginalOwnedProjectCleanup(context, sources, context.Store.Dispose, errors);
        if (close is not null)
            try { await close.ConfigureAwait(false); }
            catch (Exception cause) { KeepOriginalProjectCloseCause(errors, cause, close); }
        if (errors.Count != 0)
            throw new AggregateException("Actual project/context close requires complete original inspection.", errors);
        context.Closed = true;
    }

    private static void InvokeOriginalOwnedProjectCleanup(ContextLease context,
        TaskRunColdOriginalSourceScope sources, Action fixedOwnedCleanup, List<Exception> errors)
    {
        InvokeOriginalOwnedProjectCleanup(context, sources, () => { fixedOwnedCleanup(); return true; }, errors);
    }
    private static void InvokeOriginalOwnedProjectCleanup<T>(ContextLease context,
        TaskRunColdOriginalSourceScope sources, Func<T> fixedOwnedCleanup, List<Exception> errors)
    {
        var entered = false;
        try
        {
            context.ProjectPhysical(() => sources.Invoke(() =>
            { entered = true; return fixedOwnedCleanup(); }));
        }
        catch (Exception cause) { KeepOriginalProjectCloseCause(errors, cause); }
        // Refused productive/caller admission cannot suppress an owed fixed cleanup.
        // Never replay a callback which actually entered, including a faulted factory.
        if (!entered)
            try { context.ProjectPhysical(() => { _ = fixedOwnedCleanup(); }); }
            catch (Exception cause) { KeepOriginalProjectCloseCause(errors, cause); }
    }
    private static void KeepOriginalProjectCloseCause(List<Exception> errors, Exception cause, Task? actual = null)
    {
        static void Add(List<Exception> retained, Exception error)
        {
            if (error is AggregateException { InnerExceptions.Count: > 0 } group)
                foreach (var child in group.InnerExceptions) Add(retained, child);
            else if (!retained.Any(value => ReferenceEquals(value, error))) retained.Add(error);
        }
        Add(errors, cause);
        if (actual?.IsFaulted == true)
            foreach (var child in actual.Exception!.InnerExceptions) Add(errors, child);
    }
}
