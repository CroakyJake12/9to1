using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Inspectable record of one native app-content admission that this chrome witnessed
/// being refused by its own trusted composition owner. The chrome was inside its own
/// <see cref="IBrowseAppContentFactory.CreateSession"/> call for the exact recorded
/// request and that call returned no session, so the admission created no owner, view
/// or subscription. The failed action and the exact refusal stay inspectable for the
/// lifetime of the chrome; only the retirement obligation is withdrawn. This is never
/// reported as a successful open or a successful retirement.
/// </summary>
public sealed class BrowseAdmissionRefusalRecord
{
    internal BrowseAdmissionRefusalRecord(BrowseAppContentRequest request, Exception refusal)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Refusal = refusal ?? throw new ArgumentNullException(nameof(refusal));
        RefusedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>The exact refusal instance, with its original diagnostic identity.</summary>
    public Exception Refusal { get; }

    /// <summary>The exact chrome-minted admission this refusal answers.</summary>
    public BrowseAppContentRequest Request { get; }

    public AppContentAddress Address => Request.Address;

    public Guid TabId => Request.TabId;

    public DateTimeOffset RefusedAtUtc { get; }
}

public sealed partial class BrowseChrome
{
    private readonly List<BrowseAdmissionRefusalRecord> _admissionRefusals = [];

    // The witness is not just an exception instance: it is the exact live content admission
    // that produced it. Retirement is only ever considered for that admission's own
    // propagation chain, so a later bound factory or owner that forwards, rethrows or
    // aggregates the same refusal keeps its full obligation.
    private readonly List<(BrowseAdmissionRefusalRecord Record, ContentTransition? Fence)> _admissionProvenance = [];

    // Causal lineage, not temporal overlap. An edge records that one outer action task
    // directly awaits one specific inner operation task, asserted only by the caller that
    // actually performs that await. Membership in an admission's propagation chain is proved
    // by following those edges until a task the admission's own content fence retained is
    // reached. A task that merely happened to be pending at the witness has no such edge and
    // keeps its full retirement obligation.
    private readonly Dictionary<Task, List<Task>> _propagationEdges = new(ReferenceEqualityComparer.Instance);

    /// <summary>Records that an outer action task directly drives an inner operation task.
    /// Only the caller that actually awaits the inner task may assert this edge, and the owner
    /// that later drops the outer source unlinks it. An edge can only ever widen the chain of
    /// a task its owner genuinely awaits, so it cannot be claimed for an unrelated source.</summary>
    public void LinkAdmissionPropagation(Task outerAction, Task drivenOperation)
    {
        ArgumentNullException.ThrowIfNull(outerAction); ArgumentNullException.ThrowIfNull(drivenOperation);
        if (ReferenceEquals(outerAction, drivenOperation)) return;
        lock (_nativeSourceGate)
        {
            if (!_propagationEdges.TryGetValue(outerAction, out var driven))
                _propagationEdges[outerAction] = driven = [];
            if (!driven.Contains(drivenOperation, ReferenceEqualityComparer.Instance)) driven.Add(drivenOperation);
        }
    }

    /// <summary>Withdraws an outer action's declared lineage once its owner no longer retains
    /// that source. Losing an edge only ever makes retirement more conservative.</summary>
    public void UnlinkAdmissionPropagation(Task outerAction)
    {
        ArgumentNullException.ThrowIfNull(outerAction);
        lock (_nativeSourceGate) _propagationEdges.Remove(outerAction);
    }

    /// <summary>Native admissions this chrome's own composition owner refused before
    /// returning any session, oldest first. They remain failed and inspectable and are
    /// never presented as a successful open or a clean retirement.</summary>
    public IReadOnlyList<BrowseAdmissionRefusalRecord> AdmissionRefusals
    { get { lock (_nativeSourceGate) return _admissionRefusals.ToArray(); } }

    /// <summary>Re-validates that the supplied failure is the exact refusal instance this
    /// chrome witnessed for one of its own admissions. A replayed, wrapped, aggregated or
    /// hand-made exception returns false, so only the genuine admission's own proof can
    /// settle the CUI action retirement obligation.</summary>
    public bool IsVerifiedSettledActionFailure(Exception failure)
    {
        if (failure is not BrowseNativeAdmissionRefusal refusal) return false;
        lock (_nativeSourceGate)
            return _admissionRefusals.Any(record => ReferenceEquals(record.Refusal, refusal));
    }

    /// <summary>
    /// Withdraws the retirement obligation of one retained source, and only when that
    /// source belongs to the propagation chain of a specific admission this chrome
    /// witnessed: the source must either be a task the chrome retained for that exact live
    /// content admission (or an enclosing one), or an outer action whose declared await
    /// lineage reaches such a task, and the source's COMPLETE original fault graph
    /// (task.Exception, including every sibling) must contain nothing but that admission's
    /// own refusal. The refusal stays failed and inspectable in the ledger; the source
    /// leaves the retirement sets so it cannot permanently block close or consume bounded
    /// unresolved-source capacity.
    /// A later task that rethrows, wraps or aggregates a captured refusal, an unrelated
    /// source that was merely pending at the witness, a genuine sibling, a hand-made
    /// wrapper, a pinned contract source and any other fault all return false and keep
    /// their full retirement obligation.
    /// </summary>
    public bool TryRetireAdmissionSource(Task source)
    { lock (_nativeSourceGate) return TryRetireAdmissionSourceLocked(source); }

    private bool TryRetireAdmissionSourceLocked(Task source)
    {
        if (!IsRetirableAdmissionSourceLocked(source)) return false;
        _originalNativeSources.Remove(source);
        _originalContentTransitions.Remove(source);
        return true;
    }

    /// <summary>Pure decision for one retained source; it never mutates any set, so the
    /// prune loop below stays the single owner of removal.</summary>
    private bool IsRetirableAdmissionSourceLocked(Task source)
    {
        if (source is not { IsFaulted: true } || source.Exception is not { } graph) return false;
        if (_contractSources.Contains(source)) return false;
        foreach (var (record, fence) in _admissionProvenance)
            if (IsOwnedByAdmissionChain(fence, source) && IsExactRefusalGraph(graph, record.Refusal)) return true;
        return false;
    }

    /// <summary>
    /// Witnesses the trusted composition proof for the admission this chrome is performing
    /// right now. Called only from the catch around this chrome's own CreateSession call,
    /// so the chrome itself establishes that no session was returned. The proof must be the
    /// composition-issued type, must name this exact chrome-minted request and must come
    /// from this chrome's own factory. The chrome also records which live content admission
    /// was executing; that fence, and only the lineage that reaches it, is the propagation
    /// chain retirement may later consider. Returns false for every other fault, which then
    /// keeps its ordinary conservative handling.
    /// </summary>
    private bool TryWitnessAdmissionRefusal(Exception failure, BrowseAppContentRequest request)
    {
        if (failure is not BrowseNativeAdmissionRefusal refusal) return false;
        if (!ReferenceEquals(refusal.Request, request)) return false;
        if (!ReferenceEquals(refusal.IssuingFactory, _appContentFactory)) return false;
        var fence = _executingContentTransition.Value;
        lock (_nativeSourceGate)
        {
            foreach (var existing in _admissionProvenance)
                if (ReferenceEquals(existing.Record.Refusal, refusal)) return true;
            var record = new BrowseAdmissionRefusalRecord(request, refusal);
            _admissionRefusals.Add(record);
            _admissionProvenance.Add((record, fence));
        }
        return true;
    }

    /// <summary>True only for the admission's own retained tasks and for outer actions whose
    /// asserted await lineage reaches one of them. Reachability is walked forward through the
    /// declared edges, so temporal overlap alone never joins the chain.</summary>
    private bool IsOwnedByAdmissionChain(ContentTransition? fence, Task source)
    {
        var retained = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        for (; fence is not null; fence = fence.Parent)
            foreach (var task in fence.Retained) retained.Add(task);
        if (retained.Count == 0) return false;
        var seen = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Task>();
        pending.Push(source);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (retained.Contains(current)) return true;
            if (_propagationEdges.TryGetValue(current, out var driven))
                foreach (var inner in driven) pending.Push(inner);
        }
        return false;
    }

    /// <summary>
    /// Walks the complete fault graph. Every node must be either the exact refusal of one
    /// witnessed admission or a task aggregate produced by task composition over such
    /// refusals. Any other node, including a wrapper that captured a refusal and any
    /// genuine sibling cause, makes the whole graph conservative.
    /// </summary>
    private static bool IsExactRefusalGraph(Exception graph, Exception refusal)
    {
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>();
        var leaves = 0;
        pending.Push(graph);
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (current is AggregateException group)
            {
                if (group.InnerExceptions.Count == 0) return false;
                foreach (var nested in group.InnerExceptions) pending.Push(nested);
                continue;
            }
            if (!ReferenceEquals(current, refusal) || current.InnerException is not null) return false;
            leaves++;
        }
        return leaves > 0;
    }

    /// <summary>Withdraws provably no-effect admission failures from a retirement set.
    /// Called while <see cref="_nativeSourceGate"/> is held.</summary>
    private void PruneAdmissionRefusalsLocked(List<Task> sources)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
            if (IsRetirableAdmissionSourceLocked(sources[index])) sources.RemoveAt(index);
    }
}
