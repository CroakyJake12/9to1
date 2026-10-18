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

    /// <summary>Native admissions this chrome's own composition owner refused before
    /// returning any session, oldest first. They remain failed and inspectable and are
    /// never presented as a successful open or a clean retirement.</summary>
    public IReadOnlyList<BrowseAdmissionRefusalRecord> AdmissionRefusals
    { get { lock (_nativeSourceGate) return _admissionRefusals.ToArray(); } }

    /// <summary>
    /// Withdraws the retirement obligation of one retained source, and only when this
    /// chrome personally witnessed that source's refusal at its own app-content creation
    /// for the exact recorded request, and the source's COMPLETE original fault graph
    /// (task.Exception, including every sibling) contains nothing else. The refusal stays
    /// failed and inspectable in the ledger; the source leaves the retirement sets so it
    /// cannot permanently block close or consume bounded unresolved-source capacity.
    /// A genuine sibling, a hand-made wrapper reusing a captured refusal, an unwitnessed
    /// refusal, a pinned contract source and any other fault all return false and keep
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
        return IsWitnessedAdmissionGraph(graph);
    }

    /// <summary>
    /// Witnesses the trusted composition proof for the admission this chrome is performing
    /// right now. Called only from the catch around this chrome's own CreateSession call,
    /// so the chrome itself establishes that no session was returned. The proof must be the
    /// composition-issued type, must name this exact chrome-minted request and must come
    /// from this chrome's own factory. Returns false for every other fault, which then
    /// keeps its ordinary conservative handling.
    /// </summary>
    private bool TryWitnessAdmissionRefusal(Exception failure, BrowseAppContentRequest request)
    {
        if (failure is not BrowseNativeAdmissionRefusal refusal) return false;
        if (!ReferenceEquals(refusal.Request, request)) return false;
        if (!ReferenceEquals(refusal.IssuingFactory, _appContentFactory)) return false;
        lock (_nativeSourceGate)
        {
            foreach (var existing in _admissionRefusals)
                if (ReferenceEquals(existing.Refusal, refusal)) return true;
            _admissionRefusals.Add(new BrowseAdmissionRefusalRecord(request, refusal));
        }
        return true;
    }

    /// <summary>
    /// Walks the complete fault graph. Every node must be either a refusal this chrome
    /// witnessed for one of its own admissions, or a task aggregate produced by task
    /// composition over such refusals. Any other node, including a hand-made wrapper that
    /// captured a genuine refusal and any genuine sibling cause, makes the whole graph
    /// conservative.
    /// </summary>
    private bool IsWitnessedAdmissionGraph(Exception graph)
    {
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Exception>();
        var witnessed = 0;
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
            if (current is not BrowseNativeAdmissionRefusal) return false;
            if (current.InnerException is not null) return false;
            if (!IsWitnessedRefusal(current)) return false;
            witnessed++;
        }
        return witnessed > 0;
    }

    private bool IsWitnessedRefusal(Exception failure)
    {
        foreach (var record in _admissionRefusals)
            if (ReferenceEquals(record.Refusal, failure)) return true;
        return false;
    }

    /// <summary>Withdraws provably no-effect admission failures from a retirement set.
    /// Called while <see cref="_nativeSourceGate"/> is held.</summary>
    private void PruneAdmissionRefusalsLocked(List<Task> sources)
    {
        for (var index = sources.Count - 1; index >= 0; index--)
            if (IsRetirableAdmissionSourceLocked(sources[index])) sources.RemoveAt(index);
    }
}
