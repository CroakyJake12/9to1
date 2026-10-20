using System.ComponentModel;
using System.Globalization;
using CakeOS.Cui;

namespace CakeOS.Cui.Runtime;

/// <summary>Opaque pagination cursor. Only the query engine constructs/decodes cursors; callers
/// treat tokens as opaque and never derive page positions from them.</summary>
public sealed class CuiPageCursor
{
    /// <summary>Construct an opaque cursor. Intended for typed port adapters converting between
    /// engine cursor representations; ordinary CUI consumers treat cursors as opaque tokens.</summary>
    public CuiPageCursor(int offset) => Offset = offset;
    /// <summary>The opaque offset backing this cursor. Exposed only for typed port adapters to
    /// convert between engine cursor representations; consumers never derive page positions from it.</summary>
    public int Offset { get; }
}

/// <summary>One bounded windowed result from a typed paged source: a fixed window of items plus
/// truthful total/count metadata and opaque prev/next continuation cursors.</summary>
public sealed record CuiPagedResult(
    IReadOnlyList<object> Items,
    int TotalCount,
    int StartOffset,
    CuiPageCursor? Prev,
    CuiPageCursor? Next);

/// <summary>Typed, data-only paged query port. The authored CUI/runtime viewport owns the query
/// string and cursor tokens; the engine filters/paginates the underlying data and returns only a
/// bounded window. Implementations MUST NOT own per-view presentation state or control trees.</summary>
public interface ICuiPagedSource
{
    CuiPagedResult Evaluate(string? query, CuiPageCursor? cursor);
}

/// <summary>
/// A generic CUI runtime scope that owns a bounded paged query (query text + opaque cursor) and
/// exposes the resulting window to authored child bindings. It is the generic runtime state/query
/// contract for pickers: the authored document composes a search input (two-way to
/// <c>Query</c>), a keyed Repeat over <c>Items</c>, pager controls bound to <c>HasPrevious</c>/
/// <c>HasNext</c>, and numeric range/empty metadata. All pagination/query data filtering stays behind
/// a typed <see cref="ICuiPagedSource"/>; this viewport holds NO per-view page cache beyond the
/// single current window, produces no app-specific wording, and NEVER owns control trees (children
/// are authored in CUI). The generic paging commands are <c>query.previous</c>/<c>query.next</c>;
/// the authored document maps its own action names to them, and unrelated commands are forwarded to
/// the scene host dispatcher.
/// </summary>
public sealed class CuiQueryViewport : CuiContainerControl,
    ICuiWritableBindingContext, ICuiRepeatItemBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
{
    /// <summary>Binding keys published to authored child bindings by this viewport.</summary>
    public const string QueryKey = "Query";
    public const string ItemsKey = "Items";
    public const string TotalCountKey = "TotalCount";
    public const string HasPreviousKey = "HasPrevious";
    public const string HasNextKey = "HasNext";
    public const string PageStartKey = "PageStart";
    public const string PageEndKey = "PageEnd";
    public const string IsEmptyKey = "IsEmpty";

    /// <summary>Generic runtime paging commands routed through the nearest owning query scope.</summary>
    public const string PreviousCommand = "query.previous";
    public const string NextCommand = "query.next";

    private readonly List<object> _items = [];
    private ICuiPagedSource? _source;
    private string _query = "";
    private CuiPageCursor? _cursor;
    private int _totalCount;
    private int _startOffset;
    private CuiPageCursor? _prev;
    private CuiPageCursor? _next;
    private ICuiBindingContext? _innerContext;
    private ICuiActionDispatcher? _innerDispatcher;
    private event PropertyChangedEventHandler? _propertyChanged;
    private bool _withdrawn;

    public CuiQueryViewport() : base("Vertical Stack")
    {
    }

    /// <summary>The typed data port. Assigning it re-evaluates the current window and republishes
    /// all result metadata to authored bindings. A non-null value that is not an
    /// <see cref="ICuiPagedSource"/> is rejected; null clears the window.
    /// Assigning the SAME source reference is idempotent: it is a safe no-op that does not
    /// re-evaluate, republish, or reenter the viewport, so the loader's ordinary refresh path
    /// (which reapplies the parent Source binding) cannot feed back into this viewport's own
    /// producer indefinitely.</summary>
    public ICuiPagedSource? Source
    {
        get => _source;
        set
        {
            if (value is not null && value is not ICuiPagedSource)
                throw new InvalidOperationException("CUI QueryViewport Source must be an ICuiPagedSource.");
            if (ReferenceEquals(value, _source)) return;
            _source = value;
            Reevaluate();
        }
    }

    /// <summary>Delegate invoked after re-evaluation so the owning loader refreshes authored bindings
    /// (Repeat items, pager visibility, range/empty metadata). Set by the loader at mount; cleared
    /// by <see cref="Withdraw"/> on frame retirement.</summary>
    public Action? RefreshHost { get; set; }

    /// <summary>The binding context this viewport defers non-local lookups to (the scene/app host).
    /// Item-field resolution for repeated domain items also delegates here.</summary>
    internal ICuiBindingContext? InnerContext
    {
        get => _innerContext;
        set => _innerContext = value;
    }

    /// <summary>The action dispatcher this viewport forwards non-pager commands to.</summary>
    internal ICuiActionDispatcher? InnerDispatcher
    {
        get => _innerDispatcher;
        set => _innerDispatcher = value;
    }

    /// <summary>True once this viewport has been withdrawn (its owning loader frame retired).</summary>
    internal bool IsWithdrawn => _withdrawn;

    /// <summary>Withdraw all registrations/references on frame retirement (reload/close/failure).
    /// Clears the source, inner context, inner dispatcher and refresh callback without raising any
    /// notification, invoking evaluation, or dispatching. A withdrawn viewport is inert: it can no
    /// longer evaluate an old source, refresh a newer frame, or dispatch old host actions.</summary>
    internal void Withdraw()
    {
        _withdrawn = true;
        _source = null;
        _innerContext = null;
        _innerDispatcher = null;
        RefreshHost = null;
        _items.Clear();
        _totalCount = 0;
        _startOffset = 0;
        _prev = null;
        _next = null;
        _cursor = null;
        _query = "";
    }

    private void Reevaluate()
    {
        if (_withdrawn) return;
        var source = _source;
        if (source is null)
        {
            _items.Clear();
            _totalCount = 0;
            _startOffset = 0;
            _prev = null;
            _next = null;
        }
        else
        {
            var result = source.Evaluate(_query, _cursor);
            // Fence the evaluation against a nested update (source replacement or withdrawal) that
            // arrived while producing this window. Such a nested update has already published its
            // own coherent result; publishing this now-stale outer window would overwrite the newer
            // owner's state with an older producer's snapshot, so abort without mutating.
            if (_withdrawn || !ReferenceEquals(_source, source)) return;
            _items.Clear();
            if (result.Items is not null) _items.AddRange(result.Items);
            _totalCount = result.TotalCount;
            _startOffset = result.StartOffset;
            _prev = result.Prev;
            _next = result.Next;
        }
        // Publish every typed binding key through the observable contract. Emitting each key lets the
        // loader refresh exactly the authored bindings that observe that path leaf.
        Publish(QueryKey);
        Publish(ItemsKey);
        Publish(TotalCountKey);
        Publish(HasPreviousKey);
        Publish(HasNextKey);
        Publish(PageStartKey);
        Publish(PageEndKey);
        Publish(IsEmptyKey);
        RefreshHost?.Invoke();
    }

    private void Publish(string key) => _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(key));

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case QueryKey: value = _query; return true;
            case ItemsKey: value = _items; return true;
            case TotalCountKey: value = _totalCount; return true;
            case HasPreviousKey: value = _prev is not null; return true;
            case HasNextKey: value = _next is not null; return true;
            case PageStartKey: value = _totalCount == 0 ? 1 : _startOffset + 1; return true;
            case PageEndKey: value = _startOffset + _items.Count; return true;
            case IsEmptyKey: value = _items.Count == 0; return true;
        }
        value = null;
        return _innerContext is not null && _innerContext.TryGetValue(path, out value);
    }

    public bool TrySetValue(string path, object? value)
    {
        if (path == QueryKey)
        {
            var query = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (string.Equals(query, _query, StringComparison.Ordinal)) return true;
            _query = query;
            _cursor = null; // a new query restarts at the first window
            Reevaluate();
            return true;
        }
        return _innerContext is ICuiWritableBindingContext writable && writable.TrySetValue(path, value);
    }

    bool ICuiRepeatItemBindingContext.TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        return _innerContext is ICuiRepeatItemBindingContext provider && provider.TryGetItemValue(item, path, out value);
    }

    bool ICuiRepeatItemBindingContext.TrySetItemValue(object item, string path, object? value) =>
        _innerContext is ICuiRepeatItemBindingContext provider && provider.TrySetItemValue(item, path, value);

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        switch (command)
        {
            case PreviousCommand:
                if (!_withdrawn && _prev is not null)
                {
                    _cursor = _prev;
                    Reevaluate();
                }
                return ValueTask.CompletedTask;
            case NextCommand:
                if (!_withdrawn && _next is not null)
                {
                    _cursor = _next;
                    Reevaluate();
                }
                return ValueTask.CompletedTask;
            default:
                return _innerDispatcher is not null
                    ? _innerDispatcher.DispatchAsync(command, parameter, cancellationToken)
                    : ValueTask.FromException(new InvalidOperationException(
                        $"The generic query viewport has no host dispatcher for command '{command}'."));
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }
}
