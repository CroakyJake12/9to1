using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.Hui;

public sealed record HavenBoardAppliedGenerativePlan(
    Guid PlanId,
    HavenBoardSnapshot Before,
    HavenBoardSnapshot After);

/// <summary>
/// Safe application boundary for generative Boards mutations.
///
/// A model/provider may propose typed commands to <see cref="PreparePlan"/>, but it never receives
/// a storage/session reference. Prepared and applied state are retained privately by opaque plan ID;
/// callers receive detached review copies and cannot replace the commands or undo checkpoint later.
/// </summary>
public sealed class HavenBoardGenerativeUiCoordinator
{
    public const int MaxPreparedPlans = 128;
    public const int MaxUndoCheckpoints = 128;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, HavenBoardGenerativePlan> _prepared = [];
    private readonly Dictionary<Guid, HavenBoardAppliedGenerativePlan> _applied = [];
    private readonly Queue<Guid> _appliedOrder = new();

    public HavenBoardGenerativePlan PreparePlan(
        HavenBoardSnapshot snapshot,
        IEnumerable<HavenBoardCommand> proposedCommands)
    {
        var plan = HavenBoardGenerativePlanner.CreatePlan(snapshot, proposedCommands);
        var retained = ClonePlan(plan);

        lock (_gate)
        {
            if (_prepared.Count >= MaxPreparedPlans)
            {
                throw new InvalidOperationException(
                    $"At most {MaxPreparedPlans} generative board plans may be awaiting review.");
            }
            _prepared.Add(retained.Id, retained);
        }

        return ClonePlan(retained);
    }

    public bool CancelPlan(Guid planId)
    {
        lock (_gate)
            return _prepared.Remove(planId);
    }

    public bool DiscardUndo(Guid planId)
    {
        lock (_gate)
            return _applied.Remove(planId);
    }

    public async Task<HavenBoardAppliedGenerativePlan> ApplyAsync(
        HavenBoardsHuiSession session,
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        HavenBoardGenerativePlan plan;
        lock (_gate)
        {
            if (!_prepared.TryGetValue(planId, out plan!))
                throw new InvalidOperationException("The generative board plan is unknown, cancelled, or already applied.");
            plan = ClonePlan(plan);
        }

        // Rebuild the preview from the current base snapshot using the privately retained command
        // payload. This re-runs every allowlist, field bound, hierarchy, and geometry validation before
        // the session gets a chance to persist anything.
        if (session.Snapshot.Version != plan.BaseVersion)
        {
            throw new InvalidOperationException(
                $"Board version changed from prepared {plan.BaseVersion} to {session.Snapshot.Version}; prepare a new plan.");
        }

        var verified = HavenBoardGenerativePlanner.CreatePlan(session.Snapshot, plan.Commands);
        if (!SnapshotsEquivalent(verified.Preview, plan.Preview))
            throw new InvalidOperationException("The prepared generative board preview no longer matches its commands.");

        var before = CloneSnapshot(session.Snapshot);
        var after = await session.ExecuteBatchAsync(
            plan.Commands,
            plan.BaseVersion,
            cancellationToken).ConfigureAwait(false);

        var retainedApplied = new HavenBoardAppliedGenerativePlan(
            planId,
            CloneSnapshot(before),
            CloneSnapshot(after));

        lock (_gate)
        {
            _prepared.Remove(planId);
            _applied[planId] = retainedApplied;
            _appliedOrder.Enqueue(planId);
            while (_applied.Count > MaxUndoCheckpoints && _appliedOrder.Count > 0)
            {
                var oldest = _appliedOrder.Dequeue();
                _applied.Remove(oldest);
            }
        }

        return CloneApplied(retainedApplied);
    }

    public async Task<HavenBoardSnapshot> UndoAsync(
        HavenBoardsHuiSession session,
        Guid planId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        HavenBoardAppliedGenerativePlan retained;
        lock (_gate)
        {
            if (!_applied.TryGetValue(planId, out retained!))
                throw new InvalidOperationException("The generative board undo checkpoint is unknown, discarded, or already used.");
            retained = CloneApplied(retained);
        }

        var restored = await session.RestoreSnapshotAsync(
            retained.Before,
            retained.After.Version,
            cancellationToken).ConfigureAwait(false);

        lock (_gate)
            _applied.Remove(planId);

        return restored;
    }

    private static HavenBoardGenerativePlan ClonePlan(HavenBoardGenerativePlan plan) => new(
        plan.Id,
        plan.BaseVersion,
        Array.AsReadOnly(plan.Commands.ToArray()),
        CloneSnapshot(plan.Preview));

    private static HavenBoardAppliedGenerativePlan CloneApplied(HavenBoardAppliedGenerativePlan applied) => new(
        applied.PlanId,
        CloneSnapshot(applied.Before),
        CloneSnapshot(applied.After));

    private static HavenBoardSnapshot CloneSnapshot(HavenBoardSnapshot snapshot) => snapshot with
    {
        Groups = snapshot.Groups.Select(group => group with
        {
            Cards = group.Cards.Select(card => card with
            {
                Attachments = card.Attachments is null
                    ? null
                    : card.Attachments.Select(attachment => attachment with { }).ToArray()
            }).ToArray()
        }).ToArray(),
        Freeform = snapshot.Freeform is null
            ? null
            : new HavenBoardFreeformLayout(
                snapshot.Freeform.Items.Select(item => item with { }).ToArray())
    };

    private static bool SnapshotsEquivalent(HavenBoardSnapshot left, HavenBoardSnapshot right)
    {
        if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            || !string.Equals(left.Title, right.Title, StringComparison.Ordinal)
            || left.Version != right.Version
            || left.Groups.Count != right.Groups.Count)
            return false;

        for (var groupIndex = 0; groupIndex < left.Groups.Count; groupIndex++)
        {
            var leftGroup = left.Groups[groupIndex];
            var rightGroup = right.Groups[groupIndex];
            if (!string.Equals(leftGroup.Id, rightGroup.Id, StringComparison.Ordinal)
                || !string.Equals(leftGroup.Title, rightGroup.Title, StringComparison.Ordinal)
                || leftGroup.Cards.Count != rightGroup.Cards.Count)
                return false;

            for (var cardIndex = 0; cardIndex < leftGroup.Cards.Count; cardIndex++)
            {
                var leftCard = leftGroup.Cards[cardIndex];
                var rightCard = rightGroup.Cards[cardIndex];
                if (!string.Equals(leftCard.Id, rightCard.Id, StringComparison.Ordinal)
                    || !string.Equals(leftCard.Title, rightCard.Title, StringComparison.Ordinal)
                    || !string.Equals(leftCard.ParentCardId, rightCard.ParentCardId, StringComparison.Ordinal))
                    return false;

                var leftAttachments = leftCard.Attachments ?? [];
                var rightAttachments = rightCard.Attachments ?? [];
                if (leftAttachments.Count != rightAttachments.Count)
                    return false;
                for (var attachmentIndex = 0; attachmentIndex < leftAttachments.Count; attachmentIndex++)
                {
                    if (leftAttachments[attachmentIndex] != rightAttachments[attachmentIndex])
                        return false;
                }
            }
        }

        var leftFreeform = left.Freeform?.Items ?? [];
        var rightFreeform = right.Freeform?.Items ?? [];
        if (leftFreeform.Count != rightFreeform.Count)
            return false;
        for (var index = 0; index < leftFreeform.Count; index++)
        {
            if (leftFreeform[index] != rightFreeform[index])
                return false;
        }

        return true;
    }
}
