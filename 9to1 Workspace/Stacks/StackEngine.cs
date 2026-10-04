using System.Text.Json;

namespace HavenOS.Apps.Stacks;

/// <summary>
/// Implements Stack's canonical project/domain rules over a durable project store.
/// Provider and UI adapters call these same operations; they must not implement parallel mutation rules.
/// </summary>
public sealed class StackEngine
{
    private readonly IStackProjectStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private StackManifest? _manifest;

    public StackEngine(IStackProjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<StackDomainSnapshot> CreateProjectAsync(
        StackProjectConfiguration configuration,
        StackActor? actor = null,
        CancellationToken cancellationToken = default)
    {
        configuration = configuration.Validate();
        actor ??= StackActor.System;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            Guid projectId = Guid.NewGuid();
            Guid mainId = Guid.NewGuid();
            Guid revisionId = Guid.NewGuid();
            var main = new StackDomainRecord
            {
                Id = mainId,
                ProjectId = projectId,
                Name = "main",
                Kind = StackDomainKind.Main,
                BaseRevisionId = revisionId,
                HeadRevisionId = revisionId,
                IsActive = true,
            };
            var manifest = new StackManifest
            {
                ProjectId = projectId,
                Name = configuration.Name,
                StorageMode = configuration.StorageMode,
                AuthorityMode = configuration.AuthorityMode,
                RequireTwoFactorForPrune = configuration.RequireTwoFactorForPrune,
                RequireTwoFactorForPurge = configuration.RequireTwoFactorForPurge,
                DeletedRetentionTicks = (configuration.DeletedRetention ?? TimeSpan.FromDays(30)).Ticks,
                MainDomainId = mainId,
                ActiveDomainId = mainId,
                RevisionSequence = 1,
                Domains = [main],
                Revisions = [new StackRevision(revisionId, 1, mainId, null, now, actor.ActorId, "Initialize Stack project", [])],
            };
            AddAudit(manifest, actor, "ProjectCreated", projectId, mainId, null, revisionId, "Succeeded");
            await _store.CreateAsync(manifest, cancellationToken).ConfigureAwait(false);
            _manifest = manifest;
            return ToSnapshot(main);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackDomainSnapshot> OpenProjectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _manifest = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
            ValidateProjectState(_manifest);
            StackDomainRecord active = FindDomain(_manifest, _manifest.ActiveDomainId ?? _manifest.MainDomainId);
            return ToSnapshot(active);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StackDomainSnapshot>> GetLineageAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            return state.Domains.Where(static domain => !domain.IsDeleted)
                .OrderBy(static domain => domain.Kind)
                .ThenBy(static domain => domain.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToSnapshot)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackDomainSnapshot> CreateDomainAsync(
        Guid parentDomainId,
        string name,
        StackActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Demand(actor, StackCapability.CreateDomain, parentDomainId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackDomainRecord parent = FindDomain(state, parentDomainId);
            if (parent.IsDeleted)
            {
                throw Failure(StackFailureCode.DomainNotFound, "Deleted domains cannot own new development domains.", parentDomainId);
            }

            StackDomainKind childKind = StackHierarchy.ChildKind(parent.Kind);
            var child = new StackDomainRecord
            {
                Id = Guid.NewGuid(),
                ProjectId = state.ProjectId,
                Name = name.Trim(),
                Kind = childKind,
                ParentId = parent.Id,
                BaseRevisionId = parent.HeadRevisionId ?? parent.BaseRevisionId,
                BaseTree = ToNullableTree(BuildEffectiveTree(state, parent.Id)),
            };
            parent.ChildDomainIds.Add(child.Id);
            state.Domains.Add(child);
            AddAudit(state, actor, $"{childKind}Created", state.ProjectId, child.Id, parent.HeadRevisionId, child.BaseRevisionId, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return ToSnapshot(child);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackDomainSnapshot> CreateBranchAsync(Guid projectId, string name, StackActor actor, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Guid mainId;
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            if (state.ProjectId != projectId)
            {
                throw Failure(StackFailureCode.ProjectNotFound, "The supplied project identity does not match this Stack project.", projectId);
            }

            mainId = state.MainDomainId;
        }
        finally
        {
            _gate.Release();
        }

        return await CreateExpectedChildAsync(mainId, StackDomainKind.Main, name, actor, cancellationToken).ConfigureAwait(false);
    }

    public Task<StackDomainSnapshot> CreateTwigAsync(Guid branchId, string name, StackActor actor, CancellationToken cancellationToken = default) =>
        CreateExpectedChildAsync(branchId, StackDomainKind.Branch, name, actor, cancellationToken);

    public Task<StackDomainSnapshot> CreateLeafAsync(Guid twigId, string name, StackActor actor, CancellationToken cancellationToken = default) =>
        CreateExpectedChildAsync(twigId, StackDomainKind.Twig, name, actor, cancellationToken);

    public async Task RenameDomainAsync(Guid domainId, string name, StackActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Demand(actor, StackCapability.CreateDomain, domainId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackDomainRecord domain = FindDomain(state, domainId);
            if (domain.Kind == StackDomainKind.Main || domain.IsDeleted)
            {
                throw Failure(StackFailureCode.InvalidHierarchy, "Main cannot be renamed and deleted domains cannot be renamed.", domainId);
            }

            domain.Name = name.Trim();
            AddAudit(state, actor, "DomainRenamed", state.ProjectId, domainId, domain.HeadRevisionId, domain.HeadRevisionId, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetActiveDomainAsync(Guid domainId, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.ViewSource, domainId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackDomainRecord selected = FindDomain(state, domainId);
            if (selected.IsDeleted)
            {
                throw Failure(StackFailureCode.DomainNotFound, "A deleted domain cannot become active.", domainId);
            }

            foreach (StackDomainRecord domain in state.Domains)
            {
                domain.IsActive = domain.Id == selected.Id;
            }

            state.ActiveDomainId = selected.Id;
            AddAudit(state, actor, "ActiveDomainChanged", state.ProjectId, selected.Id, selected.HeadRevisionId, selected.HeadRevisionId, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackEffectiveTreeSnapshot> GetEffectiveTreeAsync(Guid domainId, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.ViewSource, domainId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            await CascadeAsync(state, FindDomain(state, state.MainDomainId), actor, cancellationToken).ConfigureAwait(false);
            StackDomainRecord domain = FindDomain(state, domainId);
            Guid revision = domain.HeadRevisionId ?? domain.BaseRevisionId;
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return new StackEffectiveTreeSnapshot(domain.Id, revision, CloneTree(BuildEffectiveTree(state, domain.Id)));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ApplyChangeAsync(Guid domainId, StackMutation mutation, StackActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        Demand(actor, StackCapability.Contribute, domainId.ToString("D"));
        mutation = NormalizeMutation(mutation);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            await CascadeAsync(state, FindDomain(state, state.MainDomainId), actor, cancellationToken).ConfigureAwait(false);
            StackDomainRecord domain = FindDomain(state, domainId);
            EnsureWritableDomain(domain);
            EnsureNotFrozen(state, domain, mutation.Path, actor);
            EnsureRootEditable(state, domain, mutation.Path);
            ApplyWorkingMutation(domain, mutation);
            AddAudit(state, actor, "SourceChanged", state.ProjectId, domainId, domain.HeadRevisionId, domain.HeadRevisionId, "Succeeded", mutation.Path);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackRevision> CreateCommitAsync(
        Guid domainId,
        string message,
        StackActor actor,
        IReadOnlyCollection<string>? changeSelection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Demand(actor, StackCapability.Contribute, domainId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            await CascadeAsync(state, FindDomain(state, state.MainDomainId), actor, cancellationToken).ConfigureAwait(false);
            StackDomainRecord domain = FindDomain(state, domainId);
            EnsureWritableDomain(domain);
            if (state.Conflicts.Any(conflict => conflict.DomainId == domainId && !conflict.IsResolved))
            {
                throw Failure(StackFailureCode.ConflictUnresolved, "A commit cannot include a domain with unresolved upstream conflicts.", domainId);
            }

            string[] selected = changeSelection is null
                ? domain.WorkingChanges.Keys.ToArray()
                : changeSelection.Select(StackPath.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected.Length == 0)
            {
                throw Failure(StackFailureCode.RevisionConflict, "There are no eligible uncommitted changes to commit.", domainId);
            }

            foreach (string path in selected)
            {
                if (!domain.WorkingChanges.TryGetValue(path, out _))
                {
                    throw Failure(StackFailureCode.RevisionConflict, $"The selected working change no longer exists: {path}.", domainId, retryable: true);
                }

                EnsureNotFrozen(state, domain, path, actor);
                EnsureRootEditable(state, domain, path);
            }

            foreach (string path in selected)
            {
                StackMutation change = domain.WorkingChanges[path];
                domain.WorkingChanges.Remove(path);
                domain.LocalChanges[path] = change;
            }

            StackRevision revision = NewRevision(state, domain, actor, message.Trim(), selected);
            domain.HeadRevisionId = revision.Id;
            AddAudit(state, actor, "CommitCreated", state.ProjectId, domainId, revision.ParentRevisionId, revision.Id, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackDomainChanges> GetDomainChangesAsync(Guid domainId, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.ViewSource, domainId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            await CascadeAsync(state, FindDomain(state, state.MainDomainId), actor, cancellationToken).ConfigureAwait(false);
            StackDomainRecord domain = FindDomain(state, domainId);
            var effective = BuildEffectiveTree(state, domainId);
            int added = 0;
            int deleted = 0;
            int modified = 0;
            foreach (string path in domain.BaseTree.Keys.Union(effective.Keys, StringComparer.OrdinalIgnoreCase))
            {
                domain.BaseTree.TryGetValue(path, out StackResource? before);
                effective.TryGetValue(path, out StackResource? after);
                if (Equivalent(before, after))
                {
                    continue;
                }

                if (before is null && after is not null) added++;
                else if (before is not null && after is null) deleted++;
                else if (before is not null && after is not null) modified++;
            }

            StackMutation[] changes = domain.LocalChanges.Values.Concat(domain.WorkingChanges.Values)
                .OrderBy(static mutation => mutation.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            int renamed = changes.Count(static mutation => mutation.Kind == StackMutationKind.Rename);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return new StackDomainChanges(added, deleted, modified, renamed, changes);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StackConflict>> GetConflictsAsync(Guid? domainId, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.ViewSource, domainId?.ToString("D") ?? "project");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            return state.Conflicts.Where(conflict => domainId is null || conflict.DomainId == domainId).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<StackConflictProposal>> GetConflictProposalsAsync(StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.ViewSource, "conflict-proposals");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            return state.ConflictProposals.ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResolveConflictAsync(
        Guid conflictId,
        StackConflictResolutionAction action,
        StackResource? manualResource,
        bool completeMerge,
        StackActor actor,
        CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.Contribute, conflictId.ToString("D"));
        if (action == StackConflictResolutionAction.UseManualContent && manualResource is null)
        {
            throw new ArgumentNullException(nameof(manualResource));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackConflict conflict = FindConflict(state, conflictId);
            if (conflict.IsResolved)
            {
                throw Failure(StackFailureCode.RevisionConflict, "This conflict has already been resolved.", conflictId);
            }

            StackDomainRecord domain = FindDomain(state, conflict.DomainId);
            StackResource? chosen = action switch
            {
                StackConflictResolutionAction.KeepLocal => conflict.Local,
                StackConflictResolutionAction.TakeIncoming => conflict.Incoming,
                StackConflictResolutionAction.UseManualContent => manualResource!.Copy(),
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };

            domain.BaseTree[conflict.Path] = conflict.Incoming?.Copy();
            domain.WorkingChanges[conflict.Path] = chosen is null
                ? new StackMutation(StackMutationKind.Delete, conflict.Path)
                : new StackMutation(StackMutationKind.Upsert, conflict.Path, chosen.Copy());
            conflict = conflict with { IsResolved = true, MergeCompleted = completeMerge };
            Replace(state.Conflicts, conflict);

            if (completeMerge)
            {
                StackMutation resolved = domain.WorkingChanges[conflict.Path];
                domain.WorkingChanges.Remove(conflict.Path);
                domain.LocalChanges[conflict.Path] = resolved;
                StackRevision revision = NewRevision(state, domain, actor, $"Resolve conflict: {conflict.Path}", [conflict.Path]);
                domain.HeadRevisionId = revision.Id;
                AddAudit(state, actor, "ConflictResolvedAndMerged", state.ProjectId, conflictId, conflict.BaseRevisionId, revision.Id, "Succeeded", conflict.Path);
            }
            else
            {
                AddAudit(state, actor, "ConflictResolvedWithoutMerge", state.ProjectId, conflictId, conflict.BaseRevisionId, domain.HeadRevisionId, "Succeeded", conflict.Path);
            }

            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackConflictProposal> ProposeConflictResolutionAsync(
        Guid conflictId,
        string summary,
        StackResource? proposedResource,
        string proposedBy,
        StackActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedBy);
        Demand(actor, StackCapability.Contribute, conflictId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackConflict conflict = FindConflict(state, conflictId);
            if (conflict.IsResolved)
            {
                throw Failure(StackFailureCode.RevisionConflict, "A proposal cannot target an already resolved conflict.", conflictId);
            }

            var proposal = new StackConflictProposal(Guid.NewGuid(), conflictId, summary.Trim(), proposedResource?.Copy(), StackConflictProposalState.PendingReview, DateTimeOffset.UtcNow, proposedBy);
            state.ConflictProposals.Add(proposal);
            AddAudit(state, actor, "ConflictProposalCreated", state.ProjectId, proposal.Id, conflict.BaseRevisionId, conflict.IncomingRevisionId, "PendingReview");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return proposal;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReviewConflictProposalAsync(Guid proposalId, bool accept, bool merge, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.Contribute, proposalId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackConflictProposal proposal = state.ConflictProposals.FirstOrDefault(item => item.Id == proposalId)
                ?? throw Failure(StackFailureCode.DomainNotFound, "The conflict proposal was not found.", proposalId);
            if (proposal.State != StackConflictProposalState.PendingReview)
            {
                throw Failure(StackFailureCode.RevisionConflict, "The conflict proposal has already been reviewed.", proposalId);
            }

            if (!accept)
            {
                Replace(state.ConflictProposals, proposal with { State = StackConflictProposalState.Rejected });
                AddAudit(state, actor, "ConflictProposalRejected", state.ProjectId, proposalId, null, null, "Rejected");
                await SaveAsync(state, cancellationToken).ConfigureAwait(false);
                return;
            }

            StackConflict conflict = FindConflict(state, proposal.ConflictId);
            if (conflict.IsResolved)
            {
                throw Failure(StackFailureCode.RevisionConflict, "The conflict changed after the proposal was prepared; create a new proposal for its current revision.", conflict.Id, retryable: true);
            }

            StackDomainRecord domain = FindDomain(state, conflict.DomainId);
            domain.BaseTree[conflict.Path] = conflict.Incoming?.Copy();
            domain.WorkingChanges[conflict.Path] = proposal.ProposedResource is null
                ? new StackMutation(StackMutationKind.Delete, conflict.Path)
                : new StackMutation(StackMutationKind.Upsert, conflict.Path, proposal.ProposedResource.Copy());
            conflict = conflict with { IsResolved = true, MergeCompleted = merge };
            Replace(state.Conflicts, conflict);
            Replace(state.ConflictProposals, proposal with { State = merge ? StackConflictProposalState.AcceptedAndMerged : StackConflictProposalState.AcceptedWithoutMerge });

            if (merge)
            {
                StackMutation resolved = domain.WorkingChanges[conflict.Path];
                domain.WorkingChanges.Remove(conflict.Path);
                domain.LocalChanges[conflict.Path] = resolved;
                StackRevision revision = NewRevision(state, domain, actor, $"Accept conflict resolution: {conflict.Path}", [conflict.Path]);
                domain.HeadRevisionId = revision.Id;
                AddAudit(state, actor, "ConflictProposalAcceptedAndMerged", state.ProjectId, proposalId, conflict.BaseRevisionId, revision.Id, "Succeeded", conflict.Path);
            }
            else
            {
                AddAudit(state, actor, "ConflictProposalAcceptedWithoutMerge", state.ProjectId, proposalId, conflict.BaseRevisionId, domain.HeadRevisionId, "Succeeded", conflict.Path);
            }

            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackRoot> CreateRootAsync(Guid ownerDomainId, IEnumerable<string> paths, StackActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Demand(actor, StackCapability.ManageRoots, ownerDomainId.ToString("D"));
        string[] normalized = paths.Select(StackPath.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("A Root must protect at least one path.", nameof(paths));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            FindDomain(state, ownerDomainId);
            if (normalized.Any(IsManagedPath))
            {
                throw new StackFailureException(StackFailureCode.InvalidPath, "Stack infrastructure cannot be registered as project source Roots.", string.Join(",", normalized));
            }

            var root = new StackRoot(Guid.NewGuid(), ownerDomainId, normalized, DateTimeOffset.UtcNow, actor.ActorId);
            state.Roots.Add(root);
            AddAudit(state, actor, "RootCreated", state.ProjectId, root.Id, null, null, "Succeeded", string.Join(",", normalized));
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return root;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackRevision> UpdateRootAsync(Guid rootId, IEnumerable<StackMutation> changes, string message, StackActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Demand(actor, StackCapability.ManageRoots, rootId.ToString("D"));
        StackMutation[] normalized = changes.Select(NormalizeMutation).ToArray();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackRoot root = FindRoot(state, rootId);
            if (!root.IsActive)
            {
                throw Failure(StackFailureCode.RootLocked, "An inactive Root cannot be updated.", rootId);
            }

            if (normalized.Length == 0 || normalized.Any(change => !root.Paths.Any(path => StackPath.IsWithin(change.Path, path))))
            {
                throw Failure(StackFailureCode.RootLocked, "Root updates may change only paths explicitly protected by that Root.", rootId);
            }

            StackDomainRecord owner = FindDomain(state, root.OwnerDomainId);
            foreach (StackMutation change in normalized)
            {
                EnsureWritableDomain(owner);
                EnsureNotFrozen(state, owner, change.Path, actor);
                owner.WorkingChanges[change.Path] = change;
            }

            StackRevision revision = CommitWorkingChanges(state, owner, normalized.Select(static change => change.Path).ToArray(), message, actor);
            AddAudit(state, actor, "RootUpdated", state.ProjectId, rootId, revision.ParentRevisionId, revision.Id, "Succeeded", string.Join(",", normalized.Select(static change => change.Path)));
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackSubroot> CreateSubrootAsync(
        Guid ownerDomainId,
        IEnumerable<Guid> rootIds,
        IEnumerable<string>? requiredChecks,
        int requiredApprovals,
        StackActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rootIds);
        Demand(actor, StackCapability.ManageRoots, ownerDomainId.ToString("D"));
        if (requiredApprovals < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requiredApprovals));
        }

        Guid[] rootsToClaim = rootIds.Distinct().ToArray();
        if (rootsToClaim.Length == 0)
        {
            throw new ArgumentException("A Subroot must claim at least one Root.", nameof(rootIds));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackDomainRecord owner = FindDomain(state, ownerDomainId);
            if (owner.Kind == StackDomainKind.Main)
            {
                throw Failure(StackFailureCode.InvalidHierarchy, "Main may contain Roots but cannot contain Subroots.", ownerDomainId);
            }

            foreach (Guid rootId in rootsToClaim)
            {
                StackRoot root = FindRoot(state, rootId);
                if (root.OwnerDomainId != ownerDomainId || !root.IsActive)
                {
                    throw Failure(StackFailureCode.RootClaimed, "A Subroot can claim only active Roots owned by its own domain.", rootId);
                }

                if (state.Subroots.Any(subroot => subroot.IsActive && subroot.RootIds.Contains(rootId)))
                {
                    throw Failure(StackFailureCode.RootClaimed, "This Root is already claimed by another active Subroot in the domain.", rootId);
                }
            }

            var subroot = new StackSubroot(Guid.NewGuid(), ownerDomainId, rootsToClaim, [],
                (requiredChecks ?? []).ToHashSet(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), requiredApprovals,
                new HashSet<string>(StringComparer.Ordinal), false, DateTimeOffset.UtcNow, actor.ActorId);
            state.Subroots.Add(subroot);
            AddAudit(state, actor, "SubrootCreated", state.ProjectId, subroot.Id, owner.HeadRevisionId, owner.HeadRevisionId, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return subroot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordSubrootChangesAsync(Guid subrootId, IEnumerable<StackMutation> changes, StackActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Demand(actor, StackCapability.ManageRoots, subrootId.ToString("D"));
        StackMutation[] normalized = changes.Select(NormalizeMutation).ToArray();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackSubroot subroot = FindSubroot(state, subrootId);
            if (!subroot.IsActive)
            {
                throw Failure(StackFailureCode.RootClaimed, "An inactive Subroot cannot be edited.", subrootId);
            }

            foreach (StackMutation change in normalized)
            {
                EnsureNotFrozen(state, FindDomain(state, subroot.OwnerDomainId), change.Path, actor);
                if (!subroot.RootIds.Select(id => FindRoot(state, id)).Any(root => root.Paths.Any(path => StackPath.IsWithin(change.Path, path))))
                {
                    throw Failure(StackFailureCode.RootLocked, "Subroot changes must remain inside Roots claimed by that Subroot.", subrootId);
                }
            }

            Replace(state.Subroots, subroot with { ProposedChanges = subroot.ProposedChanges.Concat(normalized).ToArray() });
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackSubroot> RecordSubrootCheckAsync(Guid subrootId, string checkId, bool passed, StackActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(checkId);
        Demand(actor, StackCapability.ManageRoots, subrootId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackSubroot subroot = FindSubroot(state, subrootId);
            if (!subroot.RequiredChecks.Contains(checkId))
            {
                throw Failure(StackFailureCode.RequiredCheckFailed, "The check is not part of this Subroot's configured policy.", checkId);
            }

            var completed = subroot.CompletedChecks.ToHashSet(StringComparer.Ordinal);
            if (passed) completed.Add(checkId); else completed.Remove(checkId);
            subroot = subroot with { CompletedChecks = completed };
            Replace(state.Subroots, subroot);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return subroot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackSubroot> ApproveSubrootAsync(Guid subrootId, StackActor reviewer, CancellationToken cancellationToken = default)
    {
        Demand(reviewer, StackCapability.ManageRoots, subrootId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackSubroot subroot = FindSubroot(state, subrootId);
            StackSubroot updated = subroot with { Approvals = subroot.Approvals.Append(reviewer.ActorId).ToHashSet(StringComparer.Ordinal) };
            Replace(state.Subroots, updated);
            AddAudit(state, reviewer, "SubrootApproved", state.ProjectId, subrootId, null, null, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackRevision> CompleteSubrootAsync(Guid subrootId, string message, StackActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Demand(actor, StackCapability.ManageRoots, subrootId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackSubroot subroot = FindSubroot(state, subrootId);
            if (!subroot.IsActive || subroot.ProposedChanges.Count == 0)
            {
                throw Failure(StackFailureCode.RootClaimed, "Only an active Subroot with proposed Root changes can be completed.", subrootId);
            }

            if (subroot.HasUnresolvedConflicts || state.Conflicts.Any(conflict => conflict.DomainId == subroot.OwnerDomainId && !conflict.IsResolved)
                || subroot.CompletedChecks.Count != subroot.RequiredChecks.Count || subroot.Approvals.Count < subroot.RequiredApprovals)
            {
                throw Failure(StackFailureCode.RequiredCheckFailed, "The Subroot cannot complete until configured checks, conflict resolution and approvals pass.", subrootId);
            }

            foreach (StackMutation change in subroot.ProposedChanges)
            {
                if (!subroot.RootIds.Select(id => FindRoot(state, id)).Any(root => root.Paths.Any(path => StackPath.IsWithin(change.Path, path))))
                {
                    throw Failure(StackFailureCode.RootLocked, "The Subroot contains a change outside its claimed Roots.", change.Path);
                }
            }

            StackDomainRecord owner = FindDomain(state, subroot.OwnerDomainId);
            EnsureWritableDomain(owner);
            foreach (StackMutation change in subroot.ProposedChanges)
            {
                EnsureNotFrozen(state, owner, change.Path, actor);
            }
            StackRevision revision = CommitMutations(state, owner, subroot.ProposedChanges, message, actor);
            Replace(state.Subroots, subroot with { IsActive = false });
            AddAudit(state, actor, "SubrootCompleted", state.ProjectId, subrootId, revision.ParentRevisionId, revision.Id, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return revision;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AbandonSubrootAsync(Guid subrootId, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.ManageRoots, subrootId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackSubroot subroot = FindSubroot(state, subrootId);
            Replace(state.Subroots, subroot with { IsActive = false, ProposedChanges = [] });
            AddAudit(state, actor, "SubrootAbandoned", state.ProjectId, subrootId, null, null, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackFreeze> CreateFreezeAsync(
        Guid targetDomainId,
        string? targetPath,
        IEnumerable<Guid> selectedDescendantDomainIds,
        string reason,
        DateTimeOffset? expiresAt,
        string? expiryCondition,
        StackActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selectedDescendantDomainIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Demand(actor, StackCapability.ManageFreezes, targetDomainId.ToString("D"));
        if (targetPath is not null) targetPath = StackPath.Normalize(targetPath);
        if (expiresAt is not null && expiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "A Freeze expiry must be in the future.");
        }

        if (expiresAt is not null && !string.IsNullOrWhiteSpace(expiryCondition))
        {
            throw new ArgumentException("A Freeze uses either a time expiry or a condition expiry, not both.", nameof(expiryCondition));
        }

        Guid[] scope = selectedDescendantDomainIds.Distinct().ToArray();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            FindDomain(state, targetDomainId);
            if (scope.Length == 0 || scope.Any(id => id == targetDomainId || !IsDescendant(state, id, targetDomainId)))
            {
                throw Failure(StackFailureCode.InvalidHierarchy, "A Freeze must explicitly select one or more strict descendants of its target domain.", targetDomainId);
            }

            var freeze = new StackFreeze(Guid.NewGuid(), targetDomainId, targetPath, scope.ToHashSet(), DateTimeOffset.UtcNow,
                actor.ActorId, reason.Trim(), expiresAt, expiryCondition?.Trim(), StackFreezeState.Active);
            state.Freezes.Add(freeze);
            AddAudit(state, actor, "FreezeCreated", state.ProjectId, freeze.Id, null, null, "Active", targetPath);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return freeze;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ThawFreezeAsync(Guid freezeId, bool early, bool hasInProgressOperations, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.ManageFreezes, freezeId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackFreeze freeze = FindFreeze(state, freezeId);
            if (freeze.State != StackFreezeState.Active)
            {
                return;
            }

            if (!early && freeze.ExpiresAt is not null && DateTimeOffset.UtcNow < freeze.ExpiresAt)
            {
                throw Failure(StackFailureCode.PermissionDenied, "The Freeze has not reached its configured expiry time.", freezeId);
            }

            if (!early && freeze.ExpiresAt is null && !string.IsNullOrWhiteSpace(freeze.ExpiryCondition))
            {
                throw Failure(StackFailureCode.FreezeActive, "This Freeze expires only when its configured condition is satisfied.", freezeId);
            }

            if (!early && freeze.ExpiresAt is null && string.IsNullOrWhiteSpace(freeze.ExpiryCondition))
            {
                throw Failure(StackFailureCode.PermissionDenied, "This Freeze has no automatic expiry and requires an authorised early thaw.", freezeId);
            }

            if (hasInProgressOperations)
            {
                throw new StackFailureException(StackFailureCode.FreezeActive,
                    "The Freeze cannot expire or thaw until in-progress operations and queued upstream changes have been reconciled.", freezeId.ToString("D"), recoverable: true, retryable: true);
            }

            Replace(state.Freezes, freeze with { State = early ? StackFreezeState.Thawed : StackFreezeState.Expired });
            AddAudit(state, actor, early ? "FreezeThawed" : "FreezeExpired", state.ProjectId, freezeId, null, null, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ExpireFreezeOnConditionAsync(Guid freezeId, string satisfiedCondition, bool hasInProgressOperations, StackActor actor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(satisfiedCondition);
        Demand(actor, StackCapability.ManageFreezes, freezeId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackFreeze freeze = FindFreeze(state, freezeId);
            if (freeze.State != StackFreezeState.Active || !StringComparer.Ordinal.Equals(freeze.ExpiryCondition, satisfiedCondition))
            {
                throw Failure(StackFailureCode.FreezeActive, "The supplied condition does not satisfy this active Freeze's expiry policy.", freezeId);
            }

            if (hasInProgressOperations)
            {
                throw new StackFailureException(StackFailureCode.FreezeActive,
                    "The Freeze condition is met, but editability remains locked until in-progress operations and queued upstream changes are reconciled.", freezeId.ToString("D"), recoverable: true, retryable: true);
            }

            Replace(state.Freezes, freeze with { State = StackFreezeState.Expired });
            AddAudit(state, actor, "FreezeExpired", state.ProjectId, freezeId, null, null, "ConditionSatisfied");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackDomainSnapshot> RestorePrunedDomainAsync(Guid domainId, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.CreateDomain, domainId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackDomainRecord domain = FindDomain(state, domainId);
            if (!domain.IsDeleted)
            {
                return ToSnapshot(domain);
            }

            TimeSpan retention = TimeSpan.FromTicks(state.DeletedRetentionTicks);
            if (domain.DeletedAt is null || DateTimeOffset.UtcNow - domain.DeletedAt > retention)
            {
                throw Failure(StackFailureCode.RestoreIncomplete, "The configured recovery retention period has elapsed.", domainId);
            }

            domain.IsDeleted = false;
            domain.DeletedAt = null;
            StackDomainRecord? parent = domain.ParentId is Guid parentId ? FindDomain(state, parentId) : null;
            parent?.ChildDomainIds.Add(domain.Id);
            AddAudit(state, actor, "DomainRestored", state.ProjectId, domain.Id, domain.HeadRevisionId, domain.HeadRevisionId, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return ToSnapshot(domain);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task PruneDomainAsync(Guid domainId, StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.Prune, domainId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackDomainRecord domain = FindDomain(state, domainId);
            if (domain.Kind == StackDomainKind.Main)
            {
                throw Failure(StackFailureCode.InvalidHierarchy, "Main cannot be pruned.", domainId);
            }

            if (state.RequireTwoFactorForPrune && !actor.TwoFactorVerified)
            {
                throw Failure(StackFailureCode.PermissionDenied, "This project's prune policy requires a verified 2FA decision.", domainId);
            }

            if (domain.ChildDomainIds.Select(id => FindDomain(state, id)).Any(static child => !child.IsDeleted))
            {
                throw Failure(StackFailureCode.PurgeBlocked, "A domain with active descendants cannot be pruned until they are swept or pruned.", domainId);
            }

            if (state.Subroots.Any(subroot => subroot.IsActive && subroot.OwnerDomainId == domainId))
            {
                throw Failure(StackFailureCode.PurgeBlocked, "A domain with an active Subroot cannot be pruned.", domainId);
            }

            domain.IsDeleted = true;
            domain.DeletedAt = DateTimeOffset.UtcNow;
            domain.IsActive = false;
            if (domain.ParentId is Guid parentId) FindDomain(state, parentId).ChildDomainIds.Remove(domainId);
            if (state.ActiveDomainId == domainId) state.ActiveDomainId = state.MainDomainId;
            AddAudit(state, actor, "DomainPruned", state.ProjectId, domainId, domain.HeadRevisionId, domain.HeadRevisionId, "RecoverableDeleted");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StackProjectSummary> GetProjectSummaryAsync(StackActor actor, CancellationToken cancellationToken = default)
    {
        Demand(actor, StackCapability.ViewSource, "project");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            return new StackProjectSummary(state.ProjectId, state.Name, state.StorageMode, state.AuthorityMode,
                state.ActiveDomainId ?? state.MainDomainId, state.Domains.Count(static domain => !domain.IsDeleted),
                state.Conflicts.Count(static conflict => !conflict.IsResolved), state.Freezes.Count(static freeze => freeze.State == StackFreezeState.Active),
                state.RevisionSequence);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StackDomainSnapshot> CreateExpectedChildAsync(Guid parentId, StackDomainKind expectedParentKind, string name, StackActor actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Demand(actor, StackCapability.CreateDomain, parentId.ToString("D"));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StackManifest state = await GetStateAsync(cancellationToken).ConfigureAwait(false);
            StackDomainRecord parent = FindDomain(state, parentId);
            if (parent.Kind != expectedParentKind)
            {
                throw Failure(StackFailureCode.InvalidHierarchy, $"A {expectedParentKind} is required for this operation.", parentId);
            }

            StackDomainKind childKind = StackHierarchy.ChildKind(parent.Kind);
            var child = new StackDomainRecord
            {
                Id = Guid.NewGuid(),
                ProjectId = state.ProjectId,
                Name = name.Trim(),
                Kind = childKind,
                ParentId = parent.Id,
                BaseRevisionId = parent.HeadRevisionId ?? parent.BaseRevisionId,
                BaseTree = ToNullableTree(BuildEffectiveTree(state, parent.Id)),
            };
            parent.ChildDomainIds.Add(child.Id);
            state.Domains.Add(child);
            AddAudit(state, actor, $"{childKind}Created", state.ProjectId, child.Id, parent.HeadRevisionId, child.BaseRevisionId, "Succeeded");
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return ToSnapshot(child);
        }
        finally
        {
            _gate.Release();
        }

    }

    private async Task<StackManifest> GetStateAsync(CancellationToken cancellationToken)
    {
        _manifest ??= await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        ValidateProjectState(_manifest);
        byte[] cloneBytes = JsonSerializer.SerializeToUtf8Bytes(_manifest);
        return JsonSerializer.Deserialize<StackManifest>(cloneBytes)
            ?? throw new StackFailureException(StackFailureCode.SourceCorrupt, "The in-memory Stack manifest could not be copied for an atomic operation.", JsonFileStackProjectStore.ManifestRelativePath, recoverable: true);
    }

    private async Task SaveAsync(StackManifest state, CancellationToken cancellationToken)
    {
        await _store.SaveAsync(state, cancellationToken).ConfigureAwait(false);
        _manifest = state;
    }

    private async Task CascadeAsync(StackManifest state, StackDomainRecord parent, StackActor actor, CancellationToken cancellationToken)
    {
        foreach (Guid childId in parent.ChildDomainIds.ToArray())
        {
            StackDomainRecord child = FindDomain(state, childId);
            if (child.IsDeleted) continue;

            Dictionary<string, StackResource> incoming = BuildEffectiveTree(state, parent.Id);
            HashSet<string> previouslyUnresolved = state.Conflicts
                .Where(conflict => conflict.DomainId == child.Id && !conflict.IsResolved)
                .Select(static conflict => conflict.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var changedPaths = child.BaseTree.Keys.Union(incoming.Keys, StringComparer.OrdinalIgnoreCase)
                .Where(path => !Equivalent(child.BaseTree.GetValueOrDefault(path), incoming.GetValueOrDefault(path)))
                .ToArray();

            foreach (string path in changedPaths)
            {
                if (previouslyUnresolved.Contains(path)) continue;
                if (OwnsPath(child, path))
                {
                    var conflict = new StackConflict(Guid.NewGuid(), state.ProjectId, child.Id, path,
                        child.BaseTree.GetValueOrDefault(path)?.Copy(), BuildEffectiveTree(state, child.Id).GetValueOrDefault(path)?.Copy(),
                        incoming.GetValueOrDefault(path)?.Copy(), child.BaseRevisionId,
                        parent.HeadRevisionId ?? parent.BaseRevisionId, DateTimeOffset.UtcNow);
                    state.Conflicts.Add(conflict);
                    AddAudit(state, actor, "UpstreamConflictDetected", state.ProjectId, conflict.Id, conflict.BaseRevisionId, conflict.IncomingRevisionId, "Unresolved", path);
                    continue;
                }

                child.BaseTree[path] = incoming.GetValueOrDefault(path)?.Copy();
            }

            if (previouslyUnresolved.Count == 0 || changedPaths.All(path => !previouslyUnresolved.Contains(path)))
            {
                child.BaseRevisionId = parent.HeadRevisionId ?? parent.BaseRevisionId;
            }

            await CascadeAsync(state, child, actor, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, StackResource> BuildEffectiveTree(StackManifest state, Guid domainId)
    {
        StackDomainRecord domain = FindDomain(state, domainId);
        var tree = new Dictionary<string, StackResource>(StringComparer.OrdinalIgnoreCase);
        foreach ((string path, StackResource? resource) in domain.BaseTree)
        {
            if (resource is not null) tree[path] = resource.Copy();
        }

        foreach (StackMutation mutation in domain.LocalChanges.Values.Concat(domain.WorkingChanges.Values))
        {
            ApplyToTree(tree, mutation);
        }

        foreach (StackConflict conflict in state.Conflicts.Where(conflict => conflict.DomainId == domainId && !conflict.IsResolved))
        {
            if (conflict.Local is null) tree.Remove(conflict.Path);
            else tree[conflict.Path] = conflict.Local.Copy();
        }

        return tree;
    }

    private static void ApplyToTree(IDictionary<string, StackResource> tree, StackMutation mutation)
    {
        switch (mutation.Kind)
        {
            case StackMutationKind.Upsert:
                tree[mutation.Path] = mutation.Resource!.Copy();
                break;
            case StackMutationKind.Delete:
                tree.Remove(mutation.Path);
                break;
            case StackMutationKind.Rename:
                StackResource? resource = mutation.Resource?.Copy() ?? (tree.TryGetValue(mutation.Path, out StackResource? existing) ? existing.Copy() : null);
                tree.Remove(mutation.Path);
                if (resource is not null) tree[mutation.RenameTo!] = resource;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private static StackMutation NormalizeMutation(StackMutation mutation)
    {
        string path = StackPath.Normalize(mutation.Path);
        switch (mutation.Kind)
        {
            case StackMutationKind.Upsert when mutation.Resource is null:
                throw new ArgumentException("An upsert requires resource content.", nameof(mutation));
            case StackMutationKind.Delete when mutation.Resource is not null:
                throw new ArgumentException("A delete cannot include replacement content.", nameof(mutation));
            case StackMutationKind.Rename when string.IsNullOrWhiteSpace(mutation.RenameTo):
                throw new ArgumentException("A rename requires a destination path.", nameof(mutation));
        }

        string? destination = mutation.RenameTo is null ? null : StackPath.Normalize(mutation.RenameTo);
        if (destination is not null && destination.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A rename must change the path.", nameof(mutation));
        }

        return mutation with { Path = path, RenameTo = destination, Resource = mutation.Resource?.Copy() };
    }

    private static void ApplyWorkingMutation(StackDomainRecord domain, StackMutation mutation)
    {
        if (mutation.Kind == StackMutationKind.Rename)
        {
            domain.WorkingChanges[mutation.Path] = mutation;
        }
        else
        {
            domain.WorkingChanges[mutation.Path] = mutation;
        }
    }

    private static bool IsManagedPath(string path)
    {
        string first = StackPath.Normalize(path).Split('/')[0];
        return new[] { ".git", ".branches", ".source", ".roots" }.Contains(first, StringComparer.OrdinalIgnoreCase);
    }

    private static bool OwnsPath(StackDomainRecord domain, string path) =>
        domain.LocalChanges.ContainsKey(path) || domain.WorkingChanges.ContainsKey(path)
        || domain.LocalChanges.Values.Concat(domain.WorkingChanges.Values).Any(mutation =>
            mutation.Kind == StackMutationKind.Rename && StringComparer.OrdinalIgnoreCase.Equals(mutation.RenameTo, path));

    private static void EnsureRootEditable(StackManifest state, StackDomainRecord domain, string path)
    {
        foreach (StackRoot root in state.Roots.Where(static root => root.IsActive))
        {
            if (!IsDescendantOrSelf(state, domain.Id, root.OwnerDomainId)) continue;
            if (root.Paths.Any(rootPath => StackPath.IsWithin(path, rootPath)))
            {
                throw new StackFailureException(StackFailureCode.RootLocked,
                    "This path is protected by an authoritative Root. Update it through the owning Root/Subroot workflow.", path);
            }
        }
    }

    private static void EnsureNotFrozen(StackManifest state, StackDomainRecord domain, string path, StackActor actor)
    {
        foreach (StackFreeze freeze in state.Freezes.Where(static freeze => freeze.State == StackFreezeState.Active))
        {
            bool expired = freeze.ExpiresAt is DateTimeOffset expiry && expiry <= DateTimeOffset.UtcNow;
            if (expired)
            {
                Replace(state.Freezes, freeze with { State = StackFreezeState.Expired });
                AddAudit(state, actor, "FreezeExpired", state.ProjectId, freeze.Id, null, null, "TimeReached");
                continue;
            }

            bool inScope = freeze.DescendantDomainIds.Contains(domain.Id);
            bool pathMatches = freeze.TargetPath is null || StackPath.IsWithin(path, freeze.TargetPath);
            if (inScope && pathMatches)
            {
                throw new StackFailureException(StackFailureCode.FreezeActive,
                    "This resource is frozen in the selected descendant scope.", freeze.Id.ToString("D"), recoverable: true);
            }
        }
    }

    private static StackRevision CommitMutations(StackManifest state, StackDomainRecord domain, IEnumerable<StackMutation> mutations, string message, StackActor actor)
    {
        string[] paths = [];
        foreach (StackMutation mutation in mutations)
        {
            StackMutation normalized = NormalizeMutation(mutation);
            domain.WorkingChanges[normalized.Path] = normalized;
            paths = [.. paths, normalized.Path];
        }

        return CommitWorkingChanges(state, domain, paths, message, actor);
    }

    private static StackRevision CommitWorkingChanges(StackManifest state, StackDomainRecord domain, IReadOnlyCollection<string> paths, string message, StackActor actor)
    {
        string[] canonicalPaths = paths.Select(StackPath.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (string path in canonicalPaths)
        {
            if (!domain.WorkingChanges.Remove(path, out StackMutation? change))
            {
                throw Failure(StackFailureCode.RevisionConflict, "A selected change disappeared before the commit was written.", path, retryable: true);
            }

            domain.LocalChanges[path] = change;
        }

        StackRevision revision = NewRevision(state, domain, actor, message, canonicalPaths);
        domain.HeadRevisionId = revision.Id;
        return revision;
    }

    private static StackRevision NewRevision(StackManifest state, StackDomainRecord domain, StackActor actor, string message, IReadOnlyList<string> paths)
    {
        var revision = new StackRevision(Guid.NewGuid(), ++state.RevisionSequence, domain.Id, domain.HeadRevisionId ?? domain.BaseRevisionId,
            DateTimeOffset.UtcNow, actor.ActorId, message, paths.ToArray());
        state.Revisions.Add(revision);
        return revision;
    }

    private static void EnsureWritableDomain(StackDomainRecord domain)
    {
        if (domain.IsDeleted)
        {
            throw Failure(StackFailureCode.DomainNotFound, "Deleted domains are read-only until restored.", domain.Id);
        }
    }

    private static void Demand(StackActor? actor, StackCapability capability, string target)
    {
        if (actor is null || !actor.Can(capability))
        {
            throw new StackFailureException(StackFailureCode.PermissionDenied, $"The caller is not permitted to {capability} in Stack.", target);
        }
    }

    private static StackDomainRecord FindDomain(StackManifest state, Guid id) => state.Domains.FirstOrDefault(domain => domain.Id == id)
        ?? throw Failure(StackFailureCode.DomainNotFound, "The Stack domain was not found.", id);

    private static StackConflict FindConflict(StackManifest state, Guid id) => state.Conflicts.FirstOrDefault(conflict => conflict.Id == id)
        ?? throw Failure(StackFailureCode.DomainNotFound, "The Stack conflict was not found.", id);

    private static StackRoot FindRoot(StackManifest state, Guid id) => state.Roots.FirstOrDefault(root => root.Id == id)
        ?? throw Failure(StackFailureCode.RootLocked, "The Stack Root was not found.", id);

    private static StackSubroot FindSubroot(StackManifest state, Guid id) => state.Subroots.FirstOrDefault(subroot => subroot.Id == id)
        ?? throw Failure(StackFailureCode.RootClaimed, "The Stack Subroot was not found.", id);

    private static StackFreeze FindFreeze(StackManifest state, Guid id) => state.Freezes.FirstOrDefault(freeze => freeze.Id == id)
        ?? throw Failure(StackFailureCode.FreezeActive, "The Stack Freeze was not found.", id);

    private static void Replace<T>(List<T> items, T item) where T : class
    {
        int index = items.FindIndex(candidate => candidate switch
        {
            StackConflict conflict when item is StackConflict replacement => conflict.Id == replacement.Id,
            StackConflictProposal proposal when item is StackConflictProposal replacement => proposal.Id == replacement.Id,
            StackSubroot subroot when item is StackSubroot replacement => subroot.Id == replacement.Id,
            StackFreeze freeze when item is StackFreeze replacement => freeze.Id == replacement.Id,
            _ => ReferenceEquals(candidate, item),
        });
        if (index < 0) throw new InvalidOperationException("Cannot replace a Stack object that is not in its manifest.");
        items[index] = item;
    }

    private static void AddAudit(StackManifest state, StackActor actor, string action, Guid projectId, Guid? target, Guid? before, Guid? after, string outcome, string? detail = null)
    {
        state.Audit.Add(new StackAuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, actor.ActorId, action, projectId, target, before, after, outcome, detail));
    }

    private static StackDomainSnapshot ToSnapshot(StackDomainRecord domain) => new(domain.Id, domain.ProjectId, domain.Name, domain.Kind,
        domain.ParentId, domain.BaseRevisionId, domain.HeadRevisionId, domain.IsActive, domain.IsDeleted,
        CloneNullableTree(domain.BaseTree), domain.LocalChanges.Values.Concat(domain.WorkingChanges.Values).ToArray());

    private static IReadOnlyDictionary<string, StackResource?> CloneNullableTree(IReadOnlyDictionary<string, StackResource?> source) =>
        source.ToDictionary(static pair => pair.Key, static pair => pair.Value?.Copy(), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, StackResource?> ToNullableTree(IReadOnlyDictionary<string, StackResource> source) =>
        source.ToDictionary(static pair => pair.Key, static pair => (StackResource?)pair.Value.Copy(), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, StackResource> CloneTree(IReadOnlyDictionary<string, StackResource> source) =>
        source.ToDictionary(static pair => pair.Key, static pair => pair.Value.Copy(), StringComparer.OrdinalIgnoreCase);

    private static bool Equivalent(StackResource? left, StackResource? right) =>
        left is null ? right is null : right is not null && left.Visibility == right.Visibility && left.IsBinary == right.IsBinary && left.Content.AsSpan().SequenceEqual(right.Content);

    private static bool IsDescendant(StackManifest state, Guid candidateId, Guid ancestorId) =>
        candidateId != ancestorId && IsDescendantOrSelf(state, candidateId, ancestorId);

    private static bool IsDescendantOrSelf(StackManifest state, Guid candidateId, Guid ancestorId)
    {
        StackDomainRecord? cursor = state.Domains.FirstOrDefault(domain => domain.Id == candidateId);
        while (cursor is not null)
        {
            if (cursor.Id == ancestorId) return true;
            cursor = cursor.ParentId is Guid parentId ? state.Domains.FirstOrDefault(domain => domain.Id == parentId) : null;
        }

        return false;
    }

    private static StackFailureException Failure(StackFailureCode code, string message, Guid target, bool retryable = false) =>
        new(code, message, target.ToString("D"), recoverable: false, retryable);

    private static StackFailureException Failure(StackFailureCode code, string message, string target, bool retryable = false) =>
        new(code, message, target, recoverable: false, retryable);

    private static void ValidateProjectState(StackManifest state)
    {
        if (state.SchemaVersion != StackManifest.CurrentSchemaVersion)
        {
            throw new StackFailureException(StackFailureCode.SchemaVersionUnsupported, "The loaded Stack manifest schema is not supported.", JsonFileStackProjectStore.ManifestRelativePath, recoverable: true);
        }

        if (state.Domains.Count(static domain => domain.Kind == StackDomainKind.Main) != 1 || state.Domains.All(domain => domain.Id != state.MainDomainId))
        {
            throw new StackFailureException(StackFailureCode.SourceCorrupt, "The project state has no valid unique Main domain.", JsonFileStackProjectStore.ManifestRelativePath, recoverable: true);
        }
    }
}

public sealed record StackProjectSummary(Guid ProjectId, string Name, StackStorageMode StorageMode, StackAuthorityMode AuthorityMode,
    Guid ActiveDomainId, int ActiveDomainCount, int UnresolvedConflictCount, int ActiveFreezeCount, long RevisionSequence);
