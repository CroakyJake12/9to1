using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantOriginalDevelopmentOwner
{
    // Selection metadata chooses the owning path only. Its private issuer and fresh
    // Home/store/READ/WRITE sources independently authorize every later operation.
    internal HavenMode ObserveOriginalProjectChoiceMode(AssistantOriginalProjectChoice sameChoice) =>
        RequireChoice(sameChoice).Container.Mode;

    internal sealed record CompatibleTaskContextObservation(ICanonicalProjectTaskContextCreation Creation,
        DeveloperResolvedProject Project, Conversation StudioConversation, ContainerDefinition StudioContainer);

    internal Task<CompatibleTaskContextObservation> CreateOriginalCompatibleTaskContextWithinSourceAsync(
        AssistantOriginalProjectChoice sameChoice, Guid newConversationId, string title, Guid operationId,
        Func<ICanonicalProjectTaskContextCreationIntent, Task> reservePendingMembership,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(async () =>
        {
            // Preserve the existing Task<binding> path: it never returns a declined
            // stage and its failure continues to retain the pending Den original.
            var stage = await CreateCompatibleTaskContextCoreAsync(sameChoice, newConversationId,
                title, operationId, reservePendingMembership, scope, retain, token, allowDeclinedStage: false).ConfigureAwait(false);
            return new CompatibleTaskContextObservation(stage.Creation
                ?? throw new InvalidOperationException("No actual compatible Tasks creation was acknowledged."),
                stage.Project, stage.StudioConversation, stage.StudioContainer);
        });

    internal Task<CompatibleTaskContextCreationStage> CreateOriginalCompatibleTaskContextCheckpointWithinSourceAsync(
        AssistantOriginalProjectChoice sameChoice, Guid newConversationId, string title, Guid operationId,
        Func<ICanonicalProjectTaskContextCreationIntent, Task> reservePendingMembership,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(() =>
            CreateCompatibleTaskContextCoreAsync(sameChoice, newConversationId, title, operationId,
                reservePendingMembership, scope, retain, token, allowDeclinedStage: true));

    internal Task<CompatibleTaskContextCreationStage> ResumeOriginalCompatibleTaskContextCheckpointWithinSourceAsync(
        AssistantCompatibleConversationCheckpoint sameCheckpoint, AssistantOriginalProjectChoice freshChoice,
        Guid newCommandOperationId, Func<ICanonicalProjectTaskContextCreationIntent, Task> reserveAttemptCallback,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(() =>
        {
            _ = RequireCompatibleCheckpoint(sameCheckpoint);
            if (newCommandOperationId == Guid.Empty || newCommandOperationId == sameCheckpoint.LastCommandOperationId)
                throw new AssistantCommandRefusedException("Resume requires a distinct explicit command identity.");
            return CreateCompatibleTaskContextCoreAsync(freshChoice, sameCheckpoint.PlannedConversationId,
                sameCheckpoint.Title, sameCheckpoint.OriginalCreationOperationId, reserveAttemptCallback,
                scope, retain, token, allowDeclinedStage: true, sameCheckpoint, newCommandOperationId);
        });

    private async Task<CompatibleTaskContextCreationStage> CreateCompatibleTaskContextCoreAsync(
        AssistantOriginalProjectChoice sameChoice, Guid newConversationId, string title, Guid operationId,
        Func<ICanonicalProjectTaskContextCreationIntent, Task> reservePendingMembership,
        Action<Action> scope, Action<Task> retain, CancellationToken token, bool allowDeclinedStage,
        AssistantCompatibleConversationCheckpoint? resumeCheckpoint = null, Guid resumeCommandOperationId = default)
    {
        var creator = _compatibleTaskContexts ?? throw new AssistantCommandRefusedException(
            "The actual canonical compatible Tasks context WRITE owner is not configured.");
        var selected = RequireChoice(sameChoice);
        var sources = new Sources(_originals, scope, retain);
        if (selected.Context.Mode != HavenMode.Studio || selected.Context.Kind != ConversationKind.StudioChat ||
            selected.Container.Mode != HavenMode.Studio || newConversationId == Guid.Empty ||
            newConversationId == selected.Context.Id || operationId == Guid.Empty)
            throw new AssistantCommandRefusedException("Choose the SAME source-owned Studio project and a new canonical Tasks conversation identity.");
        var home = await sources.Take(() => _home.OpenWithinOriginalSourceAsync(sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        var actor = home.Actor;
        if (actor != selected.Actor)
            throw new AssistantCommandRefusedException("The actual Studio project/Home actor changed before compatible context preparation.");
        var metadata = await ReadOriginalProjectContextWithinSourceAsync(selected.Context.Id, actor!, sources, token).ConfigureAwait(false);
        if (metadata.Context != selected.Context || metadata.Container != selected.Container)
            throw new AssistantCommandRefusedException("The SAME source-selected Studio context/container changed before compatible context creation.");

        var borrowers = new ReadBorrowers(_projectReads, _originals);
        Exception? primary = null;
        var cleanup = new List<Exception>();
        CompatibleTaskContextCreationStage? result = null;
        Exception? acknowledgedSqlDecline = null;
        try
        {
            Task<IDeveloperOriginalProjectCommandRead>? preparation = null;
            var read = await sources.Capture(() => preparation = _projectReads.AcquireOriginalCommandReadWithinSourceAsync(
                metadata.Context, metadata.Container, JsonSerializer.Serialize(selected.Reference, Json),
                sources.Scope, sources.Retain, token), borrowers.RetainRead).ConfigureAwait(false);
            sources.Scope(() =>
            {
                if (!_projectReads.IsOwnedOriginalCommandRead(read) || !_projectReads.IsIssuedOriginalCommandRead(read) ||
                    !ReferenceEquals(read.OriginalPreparation, preparation))
                    throw new UnauthorizedAccessException("The actual Home source did not issue the SAME live Studio project READ.");
            });
            var native = await sources.Capture(() => _projectReads.CaptureOriginalCommandNativeReadWithinSourceAsync(
                read, sources.Scope, sources.Retain, token), borrowers.RetainNative).ConfigureAwait(false);
            sources.Scope(() =>
            {
                if (!_projectReads.IsOwnedOriginalCommandNativeRead(read, native) ||
                    !_projectReads.IsIssuedOriginalCommandNativeRead(read, native))
                    throw new UnauthorizedAccessException("The actual Studio project READ did not issue its native document.");
            });
            await sources.Take(() => _projectReads.ValidateOriginalCommandNativeReadWithinSourceAsync(
                read, native, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            DeveloperResolvedProject? project = null;
            sources.Scope(() => project = ObserveAuthorizedMetadata(selected.Reference, read, native, actor!));
            var sameProject = project ?? throw new InvalidOperationException("No actual current Studio project was observed.");

            ResumeSelection? resumeSelection = null;
            ICanonicalProjectTaskContextCreationIntent intent;
            if (resumeCheckpoint is null)
                intent = await sources.Take(() => creator.PrepareOriginalCreationIntentWithinSourceAsync(
                    metadata.Store, read, newConversationId, title, operationId, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            else
            {
                var origin = RequireCompatibleCheckpoint(resumeCheckpoint);
                if (selected.Reference != resumeCheckpoint.Project || sameProject.Reference != resumeCheckpoint.Project ||
                    metadata.Context != origin.Metadata.OriginalStudioConversation || metadata.Container != origin.Metadata.OriginalStudioContainer ||
                    metadata.Store.OriginalStoreIdentity != resumeCheckpoint.OriginalStoreIdentity)
                    throw new AssistantCommandRefusedException("Resume requires the SAME current original Studio project and canonical store.");
                var resumeCreator = creator as ICanonicalProjectTaskContextResumeCreateSource
                    ?? throw new AssistantCommandRefusedException("The actual compatible Tasks producer has no configured resume source.");
                resumeSelection = RequireResumeSelection(await sources.Take(() => PrepareOriginalResumeSelectionWithinSourceAsync(
                    resumeCheckpoint, actor!, sources.Scope, sources.Retain, token)).ConfigureAwait(false));
                intent = await sources.Take(() => resumeCreator.PrepareOriginalResumedCreationIntentWithinSourceAsync(
                    resumeSelection, metadata.Store, read, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
                sources.Scope(() =>
                {
                    if (intent.TaskConversation != resumeCheckpoint.OriginalTaskConversation ||
                        intent.TaskContainer != resumeCheckpoint.OriginalTaskContainer ||
                        creator.GetOriginalCreationIntentDigest(intent) != origin.Metadata.OriginalCreationIntentSha256)
                        throw new UnauthorizedAccessException("The resumed producer changed the exact originally allocated pair or digest.");
                });
            }
            sources.Scope(() => DemandCompatibleTaskIntent(creator, intent, metadata, read, sameProject, actor!,
                newConversationId, operationId));
            // This caller-owned actual Den reserve completes before the separate
            // manual Home WRITE and atomic canonical row creation are entered.
            await sources.Take(() => reservePendingMembership(intent)).ConfigureAwait(false);
            if (resumeSelection is not null)
                await BindOriginalAcknowledgedResumeAttemptAsync(resumeSelection, resumeCommandOperationId,
                    sources, token).ConfigureAwait(false);
            await RevalidateStudioAsync().ConfigureAwait(false);
            Task<ICanonicalProjectTaskContextCreation>? sameRawCommit = null;
            ICanonicalProjectTaskContextCreation? created = null;
            try
            {
                // Take returns SAME productive factory Task, not an await proxy.
                created = await sources.TakeOwnerAcknowledgedRefusal(() => sameRawCommit = creator.CommitOriginalCreationWithinSourceAsync(
                    intent, read, sources.Scope, sources.Retain, token), creator.IsAcknowledgedOriginalCreationRefusal,
                    normalizePreEffect: false).ConfigureAwait(false);
            }
            catch (Exception cause)
            {
                // Only this privately issued raw occurrence, with its exact bare
                // child cause and independently acknowledged no-SQL-effect proof,
                // may become a successful *stage* for a later durable Den checkpoint.
                // No receipt acknowledges the enclosing Den reservation here.
                if (!allowDeclinedStage || sameRawCommit is null ||
                    sameRawCommit.Exception is not { InnerExceptions.Count: 1 } payload ||
                    !ReferenceEquals(payload.InnerExceptions[0], cause) ||
                    !sources.Work.AcknowledgeOriginalExternalPreEffectRefusal(sameRawCommit,
                        creator.IsAcknowledgedOriginalCreationRefusal)) throw;
                acknowledgedSqlDecline = cause;
            }
            if (created is not null)
            {
                sources.Scope(() => DemandCompatibleTaskCreation(creator, created, intent));
                await sources.Take(() => creator.RevalidateOriginalCreationWithinSourceAsync(
                    created, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            }
            await RevalidateStudioAsync().ConfigureAwait(false);
            var digest = "";
            sources.Scope(() => digest = creator.GetOriginalCreationIntentDigest(intent));
            result = new(this, intent, digest, created, sameProject, metadata.Context, metadata.Container,
                created is null ? sameRawCommit : null, created is null ? acknowledgedSqlDecline : null);

            async Task RevalidateStudioAsync()
            {
                var current = await ReadOriginalProjectContextWithinSourceAsync(metadata.Context.Id, actor!, sources, token).ConfigureAwait(false);
                if (current.Context != metadata.Context || current.Container != metadata.Container ||
                    (await sources.Take(() => _home.OpenWithinOriginalSourceAsync(sources.Scope, sources.Retain, token)).ConfigureAwait(false)).Actor != actor)
                    throw new AssistantCommandRefusedException("The original Studio context/container/Home actor changed during compatible Tasks creation.");
                await sources.Take(() => _projectReads.ValidateOriginalCommandNativeReadWithinSourceAsync(
                    read, native, sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            }
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            // These finite READ products never become Task input or business
            // owners. Their actual closes are joined independently even after
            // a pending/failed canonical write; nothing is deleted or replayed.
            try { await borrowers.JoinAsync().ConfigureAwait(false); }
            catch (Exception failure) { cleanup.Add(failure); }
        }
        if (primary is null && cleanup.Count == 0 && acknowledgedSqlDecline is not null)
        {
            // The parent ledger retained deep Home/store raws separately. Join and
            // transfer only the configured producer's exact occurrence receipts before
            // this successful finite stage can support a durable normal-close checkpoint.
            try { await sources.JoinAndAcknowledgeRetainedSourceCohortAsync(
                creator.IsAcknowledgedOriginalCreationSourceRefusal).ConfigureAwait(false); }
            catch (Exception cause) { cleanup.Add(cause); }
        }
        if (primary is not null) cleanup.Insert(0, primary);
        // A failed finite READ/native cleanup cannot settle the enclosing stage.
        // Keep the exact acknowledged child cause alongside that unknown failure.
        if (cleanup.Count != 0 && acknowledgedSqlDecline is not null)
            cleanup.Insert(0, acknowledgedSqlDecline);
        var causes = cleanup.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (causes.Length == 1) ExceptionDispatchInfo.Capture(causes[0]).Throw();
        if (causes.Length > 1) throw new AggregateException("Compatible canonical context and independent original READ cleanup failed.", causes);
        var actualStage = result ?? throw new InvalidOperationException("No actual compatible canonical Tasks stage was observed.");
        _compatibleTaskStages.Add(actualStage, actualStage); // Only after real finite borrowers joined successfully.
        return actualStage;
    }

    private static void DemandCompatibleTaskIntent(ICanonicalProjectTaskContextCreateSource creator,
        ICanonicalProjectTaskContextCreationIntent intent, ProjectContextStoreObservation metadata,
        IDeveloperOriginalProjectCommandRead read, DeveloperResolvedProject project, AuthenticatedResourceActor actor,
        Guid newConversationId, Guid operationId)
    {
        var conversation = intent.TaskConversation;
        var container = intent.TaskContainer;
        if (!creator.IsIssuedOriginalCreationIntent(intent) || !ReferenceEquals(intent.OriginalStoreObservation, metadata.Store) ||
            !ReferenceEquals(intent.OriginalProjectRead, read) ||
            intent.Actor != actor || intent.OperationId != operationId || intent.OriginalStudioConversation != metadata.Context ||
            intent.OriginalStudioContainer != metadata.Container || conversation.Id != newConversationId ||
            conversation.Mode != HavenMode.Tasks || conversation.Kind != ConversationKind.Task ||
            conversation.ContainerId != container.Id || conversation.SpaceId is not null || conversation.LessonId is not null ||
            conversation.IsArchived || conversation.IsTemporary || container.Id == Guid.Empty || container.Id == metadata.Container.Id ||
            container.Mode != HavenMode.Tasks || container.IsArchived || container.RootPath != read.OriginalDescriptor.RegisteredProjectRoot ||
            container.Instructions != metadata.Container.Instructions ||
            JsonSerializer.Deserialize<DeveloperProjectReference>(container.Context, Json) != project.Reference)
            throw new UnauthorizedAccessException("The actual WRITE source did not prepare the SAME project in a distinct genuine compatible Tasks context.");
    }
    private static void DemandCompatibleTaskCreation(ICanonicalProjectTaskContextCreateSource creator,
        ICanonicalProjectTaskContextCreation creation, ICanonicalProjectTaskContextCreationIntent intent)
    {
        if (!creator.IsIssuedOriginalCreation(creation) || !ReferenceEquals(creation.OriginalIntent, intent) ||
            creation.Actor != intent.Actor || creation.OperationId != intent.OperationId ||
            creation.TaskConversation != intent.TaskConversation || creation.TaskContainer != intent.TaskContainer)
            throw new UnauthorizedAccessException("The actual canonical WRITE source returned no exact SAME create-only pair receipt.");
    }
}
