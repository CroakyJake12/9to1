using System.Collections.Concurrent;

namespace Dulche.Runtime.Agents;

public sealed record BudgetDemand(
    long? Tokens = null,
    TimeSpan? Time = null,
    long? Steps = null,
    long? ToolCalls = null,
    decimal? Cost = null)
{
    public AgentFailure? Validate(string target)
    {
        if (Tokens is < 0 || Time is { Ticks: < 0 } || Steps is < 0 || ToolCalls is < 0 || Cost is < 0)
            return new(AgentFailureCode.InvalidInvocationContext, "Budget reservations cannot be negative.", target);
        if (Cost is { } cost && (double.IsNaN((double)cost) || double.IsInfinity((double)cost)))
            return new(AgentFailureCode.InvalidInvocationContext, "Cost reservations must be finite.", target);
        return null;
    }

    public decimal? Get(BudgetKind kind) => kind switch
    {
        BudgetKind.Tokens => Tokens,
        BudgetKind.Time => Time?.Ticks,
        BudgetKind.Steps => Steps,
        BudgetKind.ToolCalls => ToolCalls,
        BudgetKind.Cost => Cost,
        _ => null
    };
}

public sealed record BudgetSettlement(
    UsageValue<long> Tokens,
    UsageValue<TimeSpan> Time,
    UsageValue<long> Steps,
    UsageValue<long> ToolCalls,
    UsageValue<decimal> Cost)
{
    public UsageAvailability GetAvailability(BudgetKind kind) => kind switch
    {
        BudgetKind.Tokens => Tokens.Availability,
        BudgetKind.Time => Time.Availability,
        BudgetKind.Steps => Steps.Availability,
        BudgetKind.ToolCalls => ToolCalls.Availability,
        BudgetKind.Cost => Cost.Availability,
        _ => UsageAvailability.ProviderUnavailable
    };

    public decimal? Get(BudgetKind kind) => kind switch
    {
        BudgetKind.Tokens when Tokens.Value is { } value => value,
        BudgetKind.Time when Time.Value is { } value => value.Ticks,
        BudgetKind.Steps when Steps.Value is { } value => value,
        BudgetKind.ToolCalls when ToolCalls.Value is { } value => value,
        BudgetKind.Cost when Cost.Value is { } value => value,
        _ => null
    };
}

/// <summary>
/// Tracks a hierarchy as one ledger so each invocation is attributed once at the
/// leaf and included once in each ancestor's aggregate.
/// </summary>
public sealed class AgentBudgetLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, BudgetNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReservationState> _reservations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SettlementState> _settlements = new(StringComparer.Ordinal);

    public AgentResult<Unit> RegisterRoot(string executionId, AgentBudgetLimits limits)
    {
        if (string.IsNullOrWhiteSpace(executionId))
            return Fail<Unit>(AgentFailureCode.InvalidInvocationContext, "Execution identity is required.", "budget.root");
        if (limits.Validate(executionId) is { } invalid) return AgentResult<Unit>.Failure(invalid);
        lock (_gate)
        {
            if (_nodes.ContainsKey(executionId))
                return Fail<Unit>(AgentFailureCode.RevisionConflict, "The budget root already exists.", executionId, recoverable: true);
            _nodes.Add(executionId, new(executionId, null, limits));
            return AgentResult<Unit>.Success(Unit.Value);
        }
    }

    public AgentResult<AgentBudgetLimits> RegisterChild(
        string parentExecutionId,
        string childExecutionId,
        AgentBudgetLimits requestedLimits)
    {
        if (requestedLimits.Validate(childExecutionId) is { } invalid)
            return AgentResult<AgentBudgetLimits>.Failure(invalid);
        lock (_gate)
        {
            if (!_nodes.TryGetValue(parentExecutionId, out var parent))
                return Fail<AgentBudgetLimits>(AgentFailureCode.AgentRunNotFound, "The parent budget scope was not found.", parentExecutionId);
            if (_nodes.ContainsKey(childExecutionId))
                return Fail<AgentBudgetLimits>(AgentFailureCode.RevisionConflict, "The child budget scope already exists.", childExecutionId, recoverable: true);
            var effective = AgentBudgetLimits.Narrow(parent.Limits, requestedLimits);
            _nodes.Add(childExecutionId, new(childExecutionId, parentExecutionId, effective) { ParentNode = parent });
            return AgentResult<AgentBudgetLimits>.Success(effective);
        }
    }

    public AgentResult<IReadOnlyList<BudgetReservation>> Reserve(
        string executionId,
        string invocationId,
        BudgetDemand demand,
        DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(invocationId))
            return Fail<IReadOnlyList<BudgetReservation>>(AgentFailureCode.InvalidInvocationContext, "Invocation identity is required for budget reservation.", executionId);
        if (demand.Validate(invocationId) is { } invalid)
            return AgentResult<IReadOnlyList<BudgetReservation>>.Failure(invalid);

        lock (_gate)
        {
            if (!_nodes.TryGetValue(executionId, out var node))
                return Fail<IReadOnlyList<BudgetReservation>>(AgentFailureCode.AgentRunNotFound, "The budget execution scope was not found.", executionId);

            var proposed = Enum.GetValues<BudgetKind>()
                .Select(kind => (Kind: kind, Amount: demand.Get(kind)))
                .Where(item => item.Amount.HasValue)
                .ToArray();
            var identity = ReservationIdentity(executionId, invocationId);
            var existing = proposed.Select(item => _reservations.GetValueOrDefault(identity + ":" + item.Kind)).Where(item => item is not null).ToArray();
            if (existing.Length > 0)
            {
                var same = existing.Length == proposed.Length && proposed.All(item =>
                    _reservations.TryGetValue(identity + ":" + item.Kind, out var current) && current.Amount == item.Amount);
                return same
                    ? AgentResult<IReadOnlyList<BudgetReservation>>.Success(existing.Select(item => item!.Reservation).ToArray())
                    : Fail<IReadOnlyList<BudgetReservation>>(AgentFailureCode.IdempotencyMismatch, "The invocation reservation identity was reused with different limits.", invocationId);
            }

            foreach (var item in proposed)
            {
                var limit = node.Limits.Get(item.Kind);
                if (limit is null) continue;
                var demandAmount = item.Amount!.Value;
                foreach (var ancestor in Ancestors(node))
                {
                    var ancestorLimit = ancestor.Limits.Get(item.Kind);
                    if (ancestorLimit is null) continue;
                    var used = ancestor.Settled.GetValueOrDefault(item.Kind) + ancestor.Reserved.GetValueOrDefault(item.Kind);
                    if (used + demandAmount > ancestorLimit.Value)
                        return Fail<IReadOnlyList<BudgetReservation>>(AgentFailureCode.BudgetExceeded,
                            $"The {item.Kind} budget is exhausted before invocation dispatch.", executionId,
                            recoverable: true, retryable: false,
                            details: new Dictionary<string, string>
                            {
                                ["budget"] = item.Kind.ToString(),
                                ["limit"] = ancestorLimit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                ["used"] = used.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                ["requested"] = demandAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            });
                }
            }

            var reservations = new List<BudgetReservation>();
            foreach (var item in proposed)
            {
                var id = identity + ":" + item.Kind;
                var reservation = new BudgetReservation(id, executionId, invocationId, item.Kind, item.Amount!.Value, nowUtc, null, null, null);
                _reservations.Add(id, new(node, item.Kind, item.Amount.Value, reservation));
                foreach (var ancestor in Ancestors(node))
                    ancestor.Reserved[item.Kind] = ancestor.Reserved.GetValueOrDefault(item.Kind) + item.Amount.Value;
                reservations.Add(reservation);
            }
            return AgentResult<IReadOnlyList<BudgetReservation>>.Success(reservations);
        }
    }

    public AgentResult<IReadOnlyList<BudgetReservation>> Settle(
        string executionId,
        string invocationId,
        BudgetSettlement actual,
        DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(executionId, out var node))
                return Fail<IReadOnlyList<BudgetReservation>>(AgentFailureCode.AgentRunNotFound, "The budget execution scope was not found.", executionId);
            var identity = ReservationIdentity(executionId, invocationId);
            if (_settlements.TryGetValue(identity, out var prior))
                return prior.Fingerprint == Fingerprint(actual)
                    ? AgentResult<IReadOnlyList<BudgetReservation>>.Success(prior.Reservations)
                    : Fail<IReadOnlyList<BudgetReservation>>(AgentFailureCode.IdempotencyMismatch, "An invocation was settled twice with different telemetry.", invocationId);
            var reservations = Enum.GetValues<BudgetKind>()
                .Select(kind => _reservations.GetValueOrDefault(identity + ":" + kind))
                .Where(item => item is not null)
                .Cast<ReservationState>()
                .ToArray();
            if (reservations.Length == 0)
                return Fail<IReadOnlyList<BudgetReservation>>(AgentFailureCode.RevisionConflict, "No matching reservation exists for this invocation.", invocationId, recoverable: true);

            var settled = new List<BudgetReservation>();
            foreach (var reservation in reservations)
            {
                var amount = actual.Get(reservation.Kind);
                var availability = actual.GetAvailability(reservation.Kind);
                var charged = availability switch
                {
                    UsageAvailability.Measured when amount.HasValue => amount.Value,
                    UsageAvailability.Empty => 0,
                    UsageAvailability.ProviderUnavailable => reservation.Amount,
                    _ => reservation.Amount
                };
                foreach (var ancestor in Ancestors(reservation.Node))
                {
                    ancestor.Reserved[reservation.Kind] = Math.Max(0, ancestor.Reserved.GetValueOrDefault(reservation.Kind) - reservation.Amount);
                    ancestor.Settled[reservation.Kind] = ancestor.Settled.GetValueOrDefault(reservation.Kind) + charged;
                    if (availability == UsageAvailability.ProviderUnavailable)
                        ancestor.UnavailableCounts[reservation.Kind] = ancestor.UnavailableCounts.GetValueOrDefault(reservation.Kind) + 1;
                    else
                        ancestor.KnownSubtotals[reservation.Kind] = ancestor.KnownSubtotals.GetValueOrDefault(reservation.Kind) + charged;
                }

                var updated = reservation.Reservation with
                {
                    SettledAtUtc = nowUtc,
                    ActualAmount = amount,
                    ActualAvailability = availability
                };
                reservation.Reservation = updated;
                _reservations.Remove(updated.ReservationId);
                settled.Add(updated);
            }
            _settlements[identity] = new(node, Fingerprint(actual), settled.ToArray());
            return AgentResult<IReadOnlyList<BudgetReservation>>.Success(settled);
        }
    }

    public AgentResult<AgentBudgetUsage> Snapshot(string executionId)
    {
        lock (_gate)
        {
            if (!_nodes.TryGetValue(executionId, out var node))
                return Fail<AgentBudgetUsage>(AgentFailureCode.AgentRunNotFound, "The budget execution scope was not found.", executionId);

            UsageValue<long> LongValue(BudgetKind kind) => node.UnavailableCounts.GetValueOrDefault(kind) > 0
                ? UsageValue<long>.Unavailable($"known-subtotal:{node.KnownSubtotals.GetValueOrDefault(kind)}")
                : UsageValue<long>.Measured(decimal.ToInt64(node.Settled.GetValueOrDefault(kind)));
            UsageValue<TimeSpan> TimeValue() => node.UnavailableCounts.GetValueOrDefault(BudgetKind.Time) > 0
                ? UsageValue<TimeSpan>.Unavailable($"known-ticks:{node.KnownSubtotals.GetValueOrDefault(BudgetKind.Time)}")
                : UsageValue<TimeSpan>.Measured(TimeSpan.FromTicks(decimal.ToInt64(node.Settled.GetValueOrDefault(BudgetKind.Time))));
            UsageValue<decimal> CostValue() => node.UnavailableCounts.GetValueOrDefault(BudgetKind.Cost) > 0
                ? UsageValue<decimal>.Unavailable($"known-subtotal:{node.KnownSubtotals.GetValueOrDefault(BudgetKind.Cost)}")
                : UsageValue<decimal>.Measured(node.Settled.GetValueOrDefault(BudgetKind.Cost));

            var active = _reservations.Values.Where(item => IsDescendantOrSelf(item.Node, node)).ToArray();
            var tokenReserved = active.Where(item => item.Kind == BudgetKind.Tokens).Sum(item => item.Amount);
            var timeReserved = active.Where(item => item.Kind == BudgetKind.Time).Sum(item => item.Amount);
            var stepReserved = active.Where(item => item.Kind == BudgetKind.Steps).Sum(item => item.Amount);
            var toolReserved = active.Where(item => item.Kind == BudgetKind.ToolCalls).Sum(item => item.Amount);
            var costReserved = active.Where(item => item.Kind == BudgetKind.Cost).Sum(item => item.Amount);
            var unavailable = node.UnavailableCounts.Values.Sum();
            var invocations = _settlements.Values.Count(item => IsDescendantOrSelf(item.Node, node)) +
                active.Select(item => item.Reservation.InvocationId).Distinct(StringComparer.Ordinal).Count();
            return AgentResult<AgentBudgetUsage>.Success(new(
                LongValue(BudgetKind.Tokens),
                TimeValue(),
                LongValue(BudgetKind.Steps),
                LongValue(BudgetKind.ToolCalls),
                CostValue(),
                decimal.ToInt64(tokenReserved),
                TimeSpan.FromTicks(decimal.ToInt64(timeReserved)),
                decimal.ToInt64(stepReserved),
                decimal.ToInt64(toolReserved),
                costReserved,
                decimal.ToInt64(node.KnownSubtotals.GetValueOrDefault(BudgetKind.Tokens)),
                decimal.ToInt64(node.KnownSubtotals.GetValueOrDefault(BudgetKind.ToolCalls)),
                node.KnownSubtotals.GetValueOrDefault(BudgetKind.Cost),
                invocations,
                unavailable));
        }
    }

    private IEnumerable<BudgetNode> Ancestors(BudgetNode node)
    {
        for (BudgetNode? current = node; current is not null; current = current.ParentId is { } parentId ? _nodes[parentId] : null)
            yield return current;
    }

    private static bool IsDescendantOrSelf(BudgetNode candidate, BudgetNode parent)
    {
        for (BudgetNode? current = candidate; current is not null; current = current.ParentNode)
            if (ReferenceEquals(current, parent)) return true;
        return false;
    }

    private static string ReservationIdentity(string executionId, string invocationId) => executionId + ":" + invocationId;
    private static string Fingerprint(BudgetSettlement value) => string.Join("|", Enum.GetValues<BudgetKind>().Select(kind => $"{kind}:{value.GetAvailability(kind)}:{value.Get(kind)}"));

    private static AgentResult<T> Fail<T>(AgentFailureCode code, string message, string target, bool recoverable = false,
        bool retryable = false, IReadOnlyDictionary<string, string>? details = null) =>
        AgentResult<T>.Failure(new(code, message, target, recoverable, retryable, details));

    private sealed class BudgetNode(string id, string? parentId, AgentBudgetLimits limits)
    {
        public string Id { get; } = id;
        public string? ParentId { get; } = parentId;
        public AgentBudgetLimits Limits { get; } = limits;
        public BudgetNode? ParentNode { get; set; }
        public Dictionary<BudgetKind, decimal> Reserved { get; } = [];
        public Dictionary<BudgetKind, decimal> Settled { get; } = [];
        public Dictionary<BudgetKind, decimal> KnownSubtotals { get; } = [];
        public Dictionary<BudgetKind, int> UnavailableCounts { get; } = [];
    }

    private sealed class ReservationState(BudgetNode node, BudgetKind kind, decimal amount, BudgetReservation reservation)
    {
        public BudgetNode Node { get; } = node;
        public BudgetKind Kind { get; } = kind;
        public decimal Amount { get; } = amount;
        public BudgetReservation Reservation { get; set; } = reservation;
    }

    private sealed record SettlementState(BudgetNode Node, string Fingerprint, IReadOnlyList<BudgetReservation> Reservations);
}

internal static class AgentBudgetLimitsExtensions
{
    public static decimal? Get(this AgentBudgetLimits limits, BudgetKind kind) => kind switch
    {
        BudgetKind.Tokens => limits.MaxTokens,
        BudgetKind.Time => limits.MaxTime?.Ticks,
        BudgetKind.Steps => limits.MaxSteps,
        BudgetKind.ToolCalls => limits.MaxToolCalls,
        BudgetKind.Cost => limits.MaxCost,
        _ => null
    };
}
