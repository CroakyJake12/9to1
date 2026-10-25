namespace Haven.Application;

/// <summary>Explicit request-source allocation, not a tokenizer result or an access grant.</summary>
public sealed record OriginalSpaceContextBudget
{
    public const int MaximumSupportedItems = 1_000;
    public const int MaximumSupportedUtf8Bytes = 131_072;
    public static OriginalSpaceContextBudget Default { get; } = new(MaximumSupportedUtf8Bytes);
    public OriginalSpaceContextBudget(int maximumUtf8Bytes, int maximumItems = MaximumSupportedItems)
    {
        if (maximumUtf8Bytes is < 1 or > MaximumSupportedUtf8Bytes)
            throw new ArgumentOutOfRangeException(nameof(maximumUtf8Bytes));
        if (maximumItems is < 0 or > MaximumSupportedItems)
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        MaximumUtf8Bytes = maximumUtf8Bytes; MaximumItems = maximumItems;
    }
    public int MaximumUtf8Bytes { get; }
    public int MaximumItems { get; }
    // A conservative source-byte allocation derived from the configured context setting.
    // This is not a proof of remaining token capacity: prompt, system material, history
    // and output still need the provider's tokenizer and whole-request admission.
    public static OriginalSpaceContextBudget ForRequest(int contextLimit)
    {
        if (contextLimit < 1) throw new ArgumentOutOfRangeException(nameof(contextLimit));
        return new((int)Math.Clamp((long)contextLimit / 2, 1, MaximumSupportedUtf8Bytes));
    }
}

public enum OriginalSpaceContextOmissionReason { RequestByteBudget, SourceOwnerUnavailable }
public sealed record OriginalSpaceContextOmission(Guid ContextId, OriginalSpaceContextOmissionReason Reason);

/// <summary>Deterministic whole-source selection. Body reads remain with the genuine owning
/// adapter; this class neither opens sources nor invents excerpts, grants or source content.</summary>
public sealed class OriginalSpaceContextCollection
{
    private readonly Guid _spaceId;
    private readonly string _configuration;
    private readonly OriginalSpaceContextBudget _budget;
    private readonly int _availableCount;
    private readonly List<OriginalSpaceContextItem> _items = [];
    private readonly List<OriginalSpaceContextOmission> _omissions = [];
    private readonly HashSet<Guid> _settled = [];

    public OriginalSpaceContextCollection(Guid spaceId, string configuration,
        IReadOnlyList<SpaceContextReference> sources, int? maximumRetrievedItems, OriginalSpaceContextBudget budget)
    {
        ArgumentNullException.ThrowIfNull(sources); ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(configuration);
        if (maximumRetrievedItems is < 0 or > OriginalSpaceContextBudget.MaximumSupportedItems)
            throw new ArgumentOutOfRangeException(nameof(maximumRetrievedItems));
        _spaceId = spaceId; _configuration = configuration; _budget = budget;
        if (sources.Any(row => row is null)) throw new ArgumentException("Configured sources cannot be null.", nameof(sources));
        var available = sources.Where(row => row.IncludedAutomatically).ToArray();
        if (available.Select(row => row.ContextId).Distinct().Count() != available.Length)
            throw new ArgumentException("Configured context identities must be unique.", nameof(sources));
        _availableCount = available.Length;
        // Preserve the canonical configured order. A byte-heavy source may be omitted without
        // excluding a later small source, but no source body is partially truncated.
        RequestedSources = Array.AsReadOnly(available.Take(Math.Min(maximumRetrievedItems ?? budget.MaximumItems, budget.MaximumItems)).ToArray());
        if (OriginalSpaceCapturedContext.MeasureRegisteredBytes(configuration, [], _availableCount, budget) > budget.MaximumUtf8Bytes)
            throw new SpaceRequestPolicyBlockedException("SPACE_CONTEXT_REQUEST_BUDGET_EXCEEDED",
                "The canonical Space configuration alone exceeds this request's source allocation. Review its configuration or increase the request context limit.");
        _ = new OriginalSpaceCapturedContext(spaceId, configuration, [], _availableCount, budget, []);
    }
    public IReadOnlyList<SpaceContextReference> RequestedSources { get; }
    public int OmittedByItemLimit => _availableCount - RequestedSources.Count;
    public void OmitUnavailable(SpaceContextReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        DemandRequested(reference.ContextId);
        _omissions.Add(new(reference.ContextId, OriginalSpaceContextOmissionReason.SourceOwnerUnavailable));
    }
    public bool TryInclude(OriginalSpaceContextItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var reference = RequestedSources.SingleOrDefault(row => row.ContextId == item.ContextId)
            ?? throw new InvalidOperationException("The body does not belong to a requested canonical source.");
        if (item.Kind != OriginalSpaceContextItemKind.ConfiguredSource || item.OwnerAppId != reference.OwnerAppId ||
            item.CanonicalEntityId != reference.CanonicalEntityId ||
            reference.RevisionToken is { Length: > 0 } revision && revision != item.Revision)
            throw new InvalidOperationException("The returned body does not match its requested canonical source.");
        // Validate the bounded single body even when it will not fit the final request.
        if (!OriginalSpaceCapturedContext.IsValidItem(item))
            throw new ArgumentException("The returned source body is invalid or exceeds its explicit item bounds.", nameof(item));
        DemandRequested(item.ContextId);
        var candidate = _items.Append(item).ToArray();
        if (OriginalSpaceCapturedContext.MeasureRegisteredBytes(_configuration, candidate, _availableCount - candidate.Length, _budget) > _budget.MaximumUtf8Bytes)
        {
            _omissions.Add(new(item.ContextId, OriginalSpaceContextOmissionReason.RequestByteBudget));
            return false;
        }
        _items.Add(item);
        return true;
    }
    public OriginalSpaceCapturedContext Complete()
    {
        if (_settled.Count != RequestedSources.Count)
            throw new InvalidOperationException("Every requested source must have an included body or an explicit omission before capture completes.");
        return new(_spaceId, _configuration, _items, _availableCount - _items.Count, _budget, _omissions);
    }
    private void DemandRequested(Guid contextId)
    {
        if (!RequestedSources.Any(row => row.ContextId == contextId) || !_settled.Add(contextId))
            throw new InvalidOperationException("The source is not requested or already has a retrieval outcome.");
    }
}
