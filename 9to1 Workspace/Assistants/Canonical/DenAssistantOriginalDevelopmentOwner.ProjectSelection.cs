using System.Text.Json;
using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Dev;

namespace HavenOS.Apps.Assistants.Canonical;

public sealed partial class DenAssistantOriginalDevelopmentOwner : IAssistantOriginalProjectSelectionOwner, IAssistantOriginalProjectCataloguePagingOwner
{
    private readonly object _projectChoiceIssuer = new();
    private readonly ConditionalWeakTable<AssistantOriginalProjectCandidate, ProjectCandidateOriginal> _projectCandidates = new();
    private readonly ConditionalWeakTable<AssistantOriginalProjectChoice, ProjectCandidateOriginal> _projectChoices = new();
    private readonly ConditionalWeakTable<AssistantOriginalProjectCatalogueContinuation, ProjectPageOriginal> _projectPageContinuations = new();
    private sealed record ProjectPageOriginal(ICanonicalProjectContextStoreContinuation Original,
        AuthenticatedResourceActor Actor, int Maximum, string? Search);
    private sealed record ProjectCandidateOriginal(Conversation Context, ContainerDefinition Container,
        DeveloperProjectReference Reference, AuthenticatedResourceActor Actor,
        ICanonicalProjectContextStoreObservation StoreObservation);
    internal sealed record ProjectChoiceObservation(Conversation Context, ContainerDefinition Container,
        DeveloperResolvedProject Project, AuthenticatedResourceActor Actor);

    public Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesAsync(int maximum, CancellationToken token) =>
        ReadOriginalProjectCandidatesPageAsync(maximum, null, null, token);

    public Task<AssistantOriginalProjectCatalogue> ReadOriginalProjectCandidatesPageAsync(int maximum,
        AssistantOriginalProjectCatalogueContinuation? continuation, string? searchText, CancellationToken token) =>
        _originals.Admit(async () =>
        {
            if (maximum is < 1 or > 64)
                throw new AssistantCommandRefusedException("Choose between 1 and 64 current saved project contexts.");
            var store = _projectContexts ?? throw new AssistantCommandRefusedException(
                "The actual canonical saved-project store read owner is not configured.");
            var sources = new Sources(_originals, static body => body(), static _ => { });
            var home = await sources.Take(() => _home.OpenWithinOriginalSourceAsync(sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            var actor = home.Actor; // Only this actual Home actor can authorize catalogue READ.
            var search = string.IsNullOrWhiteSpace(searchText) ? null : searchText.Trim();
            if (search?.Length > 256) throw new AssistantCommandRefusedException("Use at most 256 characters to search saved project titles.");
            ICanonicalProjectContextStoreContinuation? actualContinuation = null;
            sources.Scope(() =>
            {
                if (continuation is null) return;
                if (!ReferenceEquals(continuation.Issuer, _projectChoiceIssuer) ||
                    !_projectPageContinuations.TryGetValue(continuation, out var prior) ||
                    !ReferenceEquals(continuation.Original, prior) || prior.Actor != actor ||
                    prior.Maximum != maximum || prior.Search != search || !store.IsIssuedOriginalContinuation(prior.Original))
                    throw new AssistantCommandRefusedException("Use the SAME current saved-project page cursor with its original actor and search.");
                actualContinuation = prior.Original;
            });
            // Obtain display metadata only through the actual store READ issuer.
            // Its private source owns SQLite identity + fresh Home ownership/import;
            // a matching Den actor, config ID, path or DI registration cannot grant it.
            var observation = await sources.TakeOwnerAcknowledgedRefusal(() => store.ReadOriginalProjectContextPageWithinSourceAsync(
                actor, maximum, sources.Scope, sources.Retain, token, actualContinuation, includeStudioContexts: true, searchText: search), store.IsAcknowledgedOriginalReadRefusal,
                normalizePreEffect: true).ConfigureAwait(false);
            sources.Scope(() => DemandIssuedStoreObservation(store, observation, actor));
            var rows = observation.Conversations;
            var containers = observation.Containers;
            if (rows.Count > maximum || rows.Select(value => value.Id).Distinct().Count() != rows.Count ||
                containers.Select(value => value.Id).Distinct().Count() != containers.Count)
                throw new InvalidDataException("The actual canonical metadata source returned an unbounded or ambiguous observation.");
            var candidates = new List<AssistantOriginalProjectCandidate>();
            foreach (var context in rows.Take(maximum))
            {
                if (!(context.Mode == HavenMode.Tasks && context.Kind == ConversationKind.Task ||
                    context.Mode == HavenMode.Studio && context.Kind == ConversationKind.StudioChat) || context.IsArchived ||
                    context.IsTemporary || context.SpaceId is not null || context.LessonId is not null || context.ContainerId is not { } id)
                    continue;
                var matching = containers.Where(value => value.Id == id && value.Mode == context.Mode && !value.IsArchived).ToArray();
                if (matching.Length != 1) continue;
                var container = matching[0];
                DeveloperProjectReference? reference;
                try { reference = JsonSerializer.Deserialize<DeveloperProjectReference>(container.Context, Json); }
                catch (JsonException) { continue; }
                if (reference is null || reference.WorkspaceId == Guid.Empty || reference.ProjectId == Guid.Empty ||
                    reference.RootId == Guid.Empty || reference.WorkspaceRevision < 1 || reference.ProjectRevision < 1) continue;
                var original = new ProjectCandidateOriginal(context, container, reference, actor, observation);
                var candidate = new AssistantOriginalProjectCandidate(_projectChoiceIssuer, original, container.Name, reference);
                lock (_gate) _projectCandidates.Add(candidate, original);
                // Pure display/selection metadata is weakly registered. It owns no
                // resource/source Task and never consumes unresolved-original capacity.
                candidates.Add(candidate);
            }
            if ((await sources.Take(() => _home.OpenWithinOriginalSourceAsync(sources.Scope, sources.Retain, token)).ConfigureAwait(false)).Actor != actor)
                throw new AssistantCommandRefusedException("The actual Home actor changed while saved project candidates were listed.");
            await sources.Take(() => store.RevalidateOriginalObservationWithinSourceAsync(observation, actor,
                sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            sources.Scope(() => DemandIssuedStoreObservation(store, observation, actor));
            AssistantOriginalProjectCatalogueContinuation? next = null;
            sources.Scope(() =>
            {
                if (observation.NextContinuation is not { } actualNext) return;
                if (!observation.HasMore || !store.IsIssuedOriginalContinuation(actualNext))
                    throw new InvalidDataException("The actual canonical metadata source did not issue this page cursor.");
                var original = new ProjectPageOriginal(actualNext, actor, maximum, search);
                next = new AssistantOriginalProjectCatalogueContinuation(_projectChoiceIssuer, original);
                _projectPageContinuations.Add(next, original);
            });
            return new AssistantOriginalProjectCatalogue(candidates, next is not null,
                "Saved Tasks and Studio projects. Search titles or read older contexts. Selecting requires fresh Home READ; creating a Tasks context for Studio requires separate Home WRITE.")
                { NextContinuation = next };
        });

    private static void DemandIssuedStoreObservation(ICanonicalProjectContextStoreReadSource store,
        ICanonicalProjectContextStoreObservation observation, AuthenticatedResourceActor actor)
    {
        if (!store.IsIssuedOriginalObservation(observation) || observation.Actor != actor)
            throw new UnauthorizedAccessException("The actual canonical store source did not issue this SAME actor's metadata observation.");
    }

    private sealed record ProjectContextStoreObservation(ICanonicalProjectContextStoreObservation Store,
        Conversation Context, ContainerDefinition Container);
    private async Task<ProjectContextStoreObservation> ReadOriginalProjectContextWithinSourceAsync(Guid contextId,
        AuthenticatedResourceActor actor, Sources sources, CancellationToken token)
    {
        var store = _projectContexts ?? throw new AssistantCommandRefusedException(
            "The actual canonical saved-project store read owner is not configured.");
        var observation = await sources.TakeOwnerAcknowledgedRefusal(() => store.ReadOriginalProjectContextsWithinSourceAsync(
            actor, 1, sources.Scope, sources.Retain, token, contextId), store.IsAcknowledgedOriginalReadRefusal,
            normalizePreEffect: true).ConfigureAwait(false);
        sources.Scope(() => DemandIssuedStoreObservation(store, observation, actor));
        var context = observation.Conversations.SingleOrDefault(value => value.Id == contextId);
        if (observation.Conversations.Count != 1 || context is null || context.ContainerId is not { } id ||
            context.Mode is not (HavenMode.Tasks or HavenMode.Studio) || context.IsArchived || context.IsTemporary ||
            context.SpaceId is not null || context.LessonId is not null)
            throw new AssistantCommandRefusedException("The SAME current canonical store READ has no eligible known project context.");
        var containers = observation.Containers.Where(value => value.Id == id && value.Mode == context.Mode && !value.IsArchived).ToArray();
        if (observation.Containers.Count != 1 || containers.Length != 1)
            throw new AssistantCommandRefusedException("The SAME current canonical store READ has no unique actual project container.");
        return new(observation, context, containers[0]);
    }

    private ProjectCandidateOriginal RequireCandidate(AssistantOriginalProjectCandidate candidate)
    {
        lock (_gate)
            return candidate is not null && ReferenceEquals(candidate.Issuer, _projectChoiceIssuer) &&
                _projectCandidates.TryGetValue(candidate, out var actual) && ReferenceEquals(candidate.Original, actual)
                ? actual : throw new AssistantCommandRefusedException("This actual canonical source did not issue the selected project candidate.");
    }
    private ProjectCandidateOriginal RequireChoice(AssistantOriginalProjectChoice choice)
    {
        lock (_gate)
            return choice is not null && ReferenceEquals(choice.Issuer, _projectChoiceIssuer) &&
                _projectChoices.TryGetValue(choice, out var actual) && ReferenceEquals(choice.Original, actual)
                ? actual : throw new AssistantCommandRefusedException("This actual Home project READ source did not issue the selected project choice.");
    }
    public bool IsIssuedOriginalProjectChoice(AssistantOriginalProjectChoice choice)
    {
        lock (_gate) return choice is not null && ReferenceEquals(choice.Issuer, _projectChoiceIssuer) &&
            _projectChoices.TryGetValue(choice, out var actual) && ReferenceEquals(choice.Original, actual);
    }
    public Task<AssistantOriginalProjectChoice> AuthorizeOriginalProjectChoiceWithinSourceAsync(AssistantOriginalProjectCandidate candidate,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) => _originals.Admit(async () =>
        {
            var original = RequireCandidate(candidate);
            var sources = new Sources(_originals, originalSynchronousScope, retainOriginalTask);
            var observed = await ObserveProjectChoiceWithinSourceAsync(original, sources, token).ConfigureAwait(false);
            var choice = new AssistantOriginalProjectChoice(_projectChoiceIssuer, original, observed.Project);
            lock (_gate)
            {
                _projectChoices.Add(choice, original); // Healthy READ/native sources were independently joined before issuance.
            }
            return choice;
        });

    internal Task<ProjectChoiceObservation> ValidateOriginalProjectChoiceWithinSourceAsync(AssistantOriginalProjectChoice choice,
        Action<Action> scope, Action<Task> retain, CancellationToken token) => _originals.Admit(() =>
            ObserveProjectChoiceWithinSourceAsync(RequireChoice(choice), new Sources(_originals, scope, retain), token));

    private async Task<ProjectChoiceObservation> ObserveProjectChoiceWithinSourceAsync(ProjectCandidateOriginal original,
        Sources sources, CancellationToken token)
    {
        var home = await sources.Take(() => _home.OpenWithinOriginalSourceAsync(sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        var actor = home.Actor;
        if (actor != original.Actor)
            throw new AssistantCommandRefusedException("The exact selected project's actual Home actor changed.");
        var store = _projectContexts ?? throw new AssistantCommandRefusedException(
            "The actual canonical saved-project store read owner is not configured.");
        sources.Scope(() => DemandIssuedStoreObservation(store, original.StoreObservation, actor!));
        await sources.Take(() => store.RevalidateOriginalObservationWithinSourceAsync(original.StoreObservation, actor!,
            sources.Scope, sources.Retain, token)).ConfigureAwait(false);
        var metadata = await ReadOriginalProjectContextWithinSourceAsync(original.Context.Id, actor!, sources, token).ConfigureAwait(false);
        var context = metadata.Context;
        var container = metadata.Container;
        if (context != original.Context || container != original.Container || context is null || container is null ||
            context.ContainerId != container.Id || container.IsArchived)
            throw new AssistantCommandRefusedException("Refresh the saved project selection after its canonical context or container changed.");
        var project = await ReadProjectWithinSourceAsync(context, container, original.Reference, actor!, sources, token, async () =>
        {
            await sources.Take(() => store.RevalidateOriginalObservationWithinSourceAsync(original.StoreObservation, actor!,
                sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            sources.Scope(() => DemandIssuedStoreObservation(store, original.StoreObservation, actor!));
            var current = await ReadOriginalProjectContextWithinSourceAsync(context.Id, actor!, sources, token).ConfigureAwait(false);
            if (current.Context != context || current.Container != container ||
                (await sources.Take(() => _home.OpenWithinOriginalSourceAsync(sources.Scope, sources.Retain, token)).ConfigureAwait(false)).Actor != actor)
                throw new AssistantCommandRefusedException("The actual selected project/context/actor changed during Home READ.");
        }).ConfigureAwait(false);
        return new(context, container, project, actor!);
    }
    private readonly Dictionary<ITaskRunColdOriginalProjectInput, InitialProjectInputOriginal> _initialProjectInputs =
        new(ReferenceEqualityComparer.Instance);
    private sealed class InitialProjectInputOriginal(ITaskRunColdProjectRestorationLease actualBorrower)
    {
        private readonly object _gate = new();
        private Task? _close;
        private bool _closeRequested;
        private bool _handedOff;
        internal Task? OriginalClose { get { lock (_gate) return _close; } }
        internal void DemandPreparedCapabilityAdmission()
        {
            lock (_gate)
                if (_closeRequested || _close is not null || _handedOff)
                    throw new AssistantCommandRefusedException("Retain the SAME live project input before its canonical Task dispatch or retirement.");
        }
        internal void ObserveIssuerClosedReceipt(Task sameActualOriginalClose)
        {
            lock (_gate)
            {
                if (_close is not null && !ReferenceEquals(_close, sameActualOriginalClose))
                    throw new InvalidOperationException("The actual Home issuer observed a different cached initial input close.");
                _closeRequested = true;
                _close = sameActualOriginalClose;
            }
        }
        internal void MarkActualDispatchAcquisition()
        {
            lock (_gate)
            {
                if (_closeRequested || _close is not null)
                    throw new AssistantCommandRefusedException("The actual prepared project input has already retired.");
                if (_handedOff)
                    throw new AssistantCommandRefusedException("The SAME prepared project input already entered its canonical Task dispatch.");
                _handedOff = true;
            }
        }
        internal bool TryRetireUndispatched()
        {
            lock (_gate)
            {
                if (_handedOff) return false;
                _closeRequested = true;
                return true;
            }
        }
        internal Task CloseAndDrainAsync()
        {
            actualBorrower.DemandExternalOriginalJoin();
            lock (_gate)
            {
                _closeRequested = true;
                return _close ??= actualBorrower.CloseAndDrainOriginalAsync();
            }
        }
    }

    private InitialProjectInputOriginal RequireOriginalProjectInput(ITaskRunColdOriginalProjectInput actualInput)
    {
        if (!_projectReads.IsOwnedOriginalProjectInput(actualInput))
            throw new UnauthorizedAccessException("The SAME actual Home issuer did not own this original project input.");
        lock (_gate)
            return _initialProjectInputs.TryGetValue(actualInput, out var original) ? original :
                throw new AssistantCommandRefusedException("This process-owned source did not retain the SAME prepared project input.");
    }

    // The factory-entered marker precedes the actual Chat acquisition. Even a
    // synchronous/faulted acquisition can have accepted business work, so only the
    // process-owned cohort may close its input after genuine Chat/Task joins.
    internal Task<TaskRunOriginalInitialChatObservationLease> AcquireOriginalProjectTaskDispatchWithinSourceAsync(
        ITaskRunColdOriginalProjectInput actualInput, Func<Task<TaskRunOriginalInitialChatObservationLease>> actualDispatch,
        Action<Action> scope, Action<Task> retain) => _originals.Admit(async () =>
        {
            var original = RequireOriginalProjectInput(actualInput);
            var sources = new Sources(_originals, scope, retain);
            return await sources.Take(() =>
            {
                if (!_projectReads.IsIssuedOriginalProjectInput(actualInput))
                    throw new UnauthorizedAccessException("The SAME actual Home input has no current issued preparation.");
                original.MarkActualDispatchAcquisition();
                return actualDispatch();
            }).ConfigureAwait(false);
        });

    // A guard may refuse between preparation and entering Chat. This independent
    // cleanup never closes input after the actual factory has entered; retirement
    // of an Assistant presentation does not cancel borrowed canonical business.
    internal Task<bool> CloseUndispatchedOriginalProjectInputWithinSourceAsync(ITaskRunColdOriginalProjectInput actualInput,
        Action<Action> scope, Action<Task> retain) => _originals.Admit(async () =>
        {
            var original = RequireOriginalProjectInput(actualInput);
            var sources = new Sources(_originals, scope, retain);
            var shouldClose = false;
            sources.Scope(() => shouldClose = original.TryRetireUndispatched());
            if (!shouldClose) return false;
            await sources.Take(original.CloseAndDrainAsync).ConfigureAwait(false);
            return true;
        });

    private void PruneHealthyOriginalProjectInputs(Sources sources)
    {
        KeyValuePair<ITaskRunColdOriginalProjectInput, InitialProjectInputOriginal>[] entries;
        lock (_gate) entries = _initialProjectInputs.ToArray();
        foreach (var entry in entries)
        {
            Task? closed = null; var healthy = false;
            sources.Scope(() => healthy = _projectReads.TryObserveClosedOwnedOriginalProjectInput(entry.Key, out closed));
            if (!healthy || closed is null) continue;
            var sameActualClose = closed;
            sources.Retain(sameActualClose); sameActualClose.GetAwaiter().GetResult(); // SAME already terminal actual issuer close, independently joined.
            sources.Scope(() => healthy = _projectReads.IsClosedOwnedOriginalProjectInput(entry.Key, sameActualClose));
            if (!healthy) continue;
            // SAME independently joined issuer receipt also settles the retained
            // original observation; dictionary pruning alone would retain its
            // historical closure and permanently consume source capacity.
            sources.Scope(() => entry.Value.ObserveIssuerClosedReceipt(sameActualClose));
            lock (_gate)
                if (_initialProjectInputs.TryGetValue(entry.Key, out var same) && ReferenceEquals(same, entry.Value))
                    _initialProjectInputs.Remove(entry.Key);
        }
    }

    // This is acquired by the process-owned canonical source. Presentation close
    // never closes a successfully transferred initial input while its Task runs.
    // App shutdown must join the actual Chat/Task business before this owner.
    internal Task<ITaskRunColdOriginalProjectInput> PrepareOriginalProjectTaskInputWithinSourceAsync(
        AssistantConversationBinding binding, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        _originals.Admit(async () =>
        {
            var membership = RequireMembership(binding);
            var sources = new Sources(_originals, scope, retain);
            PruneHealthyOriginalProjectInputs(sources);
            sources.Scope(() =>
            {
                if (!_tasks.HasOriginalColdRecoveryComposition(_journal, _coldContext) ||
                    _journal is not ITaskRunColdConfiguredProjectResourceSource projectSource ||
                    !ReferenceEquals(projectSource.RequireOriginalProjectResourceSource(), _projectReads))
                    throw new AssistantCommandRefusedException("The SAME protected canonical journal has no paired actual Home project input owner.");
            });
            var before = await sources.Take(() => membership.ValidateOriginalWithinSourceAsync(binding,
                sources.Scope, sources.Retain, token)).ConfigureAwait(false);
            if (!before.Definition.Configuration.Enabled || before.Definition.Configuration.Archived ||
                before.Conversation.Mode != HavenMode.Tasks || before.Conversation.Kind != ConversationKind.Task ||
                before.Conversation.IsArchived || before.Conversation.ContainerId is not { } id ||
                before.OriginalProjectReference is not { } reference)
                throw new AssistantCommandRefusedException("This membership has no source-issued active project task input.");
            var actor = before.Actor;
            var taskActor = await ReadOriginalTaskActorWithinSourceAsync(sources, token).ConfigureAwait(false);
            if (await sources.Take(() => _tasks.GetByContextAsync(before.Conversation.Id, token)).ConfigureAwait(false) is not null)
                throw new AssistantCommandRefusedException("This conversation already owns a canonical Task; use its actual steering or original recovery controls.");
            var metadata = await ReadOriginalProjectContextWithinSourceAsync(before.Conversation.Id, actor!, sources, token).ConfigureAwait(false);
            var container = metadata.Container;
            if (metadata.Context != before.Conversation || container.Id != id)
                throw new AssistantCommandRefusedException("The SAME store READ project context/container differs from actual membership.");
            Task<ITaskRunColdOriginalProjectInput>? preparation = null;
            InitialProjectInputOriginal? retained = null;
            try
            {
                var input = await sources.Capture(() => preparation = _projectReads.PrepareOriginalProjectInputWithinSourceAsync(
                    before.Conversation, container, JsonSerializer.Serialize(reference, Json), sources.Scope, sources.Retain, token), actual =>
                {
                    if (!_projectReads.IsOwnedOriginalProjectInput(actual) || actual is not ITaskRunColdProjectRestorationLease borrower)
                        throw new UnauthorizedAccessException("The actual Home issuer returned no genuine historical project input cleanup custody.");
                    retained = new InitialProjectInputOriginal(borrower);
                    lock (_gate)
                    {
                        if (_initialProjectInputs.Count >= 128)
                            throw new AssistantCommandRefusedException("Settle retained initial project inputs before another acquisition.");
                        _initialProjectInputs.Add(actual, retained);
                    }
                    var same = retained;
                    _originals.Observe(() => Physical(_originals, same.CloseAndDrainAsync), () => same.OriginalClose);
                }).ConfigureAwait(false);
                if (!_projectReads.IsIssuedOriginalProjectInput(input) || !ReferenceEquals(input.OriginalPreparation, preparation) ||
                    !ReferenceEquals(input.OriginalConversation, before.Conversation) || input.OriginalContainer != container ||
                    input.OriginalIdentity.WorkspaceId != reference.WorkspaceId || input.OriginalIdentity.ProjectId != reference.ProjectId ||
                    input.OriginalIdentity.WorkspaceRevision != reference.WorkspaceRevision || input.OriginalIdentity.ProjectRevision != reference.ProjectRevision ||
                    input.OriginalIdentity.RootId != reference.RootId || input.OriginalIdentity.RepositoryBindingId != reference.RepositoryBindingId ||
                    input.OriginalIdentity.OriginalHomeResourceActor != actor)
                    throw new UnauthorizedAccessException("The actual initial Home input is not the SAME source-selected conversation/project/actor/preparation.");
                var after = await sources.Take(() => membership.ValidateOriginalWithinSourceAsync(binding,
                    sources.Scope, sources.Retain, token)).ConfigureAwait(false);
                var current = await ReadOriginalProjectContextWithinSourceAsync(before.Conversation.Id, actor!, sources, token).ConfigureAwait(false);
                if (after.Actor != actor || after.Conversation != before.Conversation || after.OriginalProjectReference != reference ||
                    await ReadOriginalTaskActorWithinSourceAsync(sources, token).ConfigureAwait(false) != taskActor ||
                    current.Context != before.Conversation || current.Container != container)
                    throw new AssistantCommandRefusedException("The actual project membership/actor/container changed before canonical dispatch.");
                return input;
            }
            catch (Exception primary)
            {
                if (retained is null) throw;
                Task? close = null;
                try { close = Physical(_originals, retained.CloseAndDrainAsync); _originals.Retain(close); await close.ConfigureAwait(false); }
                catch (Exception cleanup)
                {
                    var actual = close?.Exception ?? cleanup;
                    throw new AggregateException("Initial project input and independent actual cleanup failed.", primary, actual);
                }
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
                throw;
            }
        });

}
