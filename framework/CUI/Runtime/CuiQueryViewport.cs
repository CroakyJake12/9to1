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
/// exposes the resulting window to authored child bindings. It is the canonical runtime state/query
/// contract for pickers: the authored document composes a search input (two-way to
/// <c>Query</c>), a keyed Repeat over <c>Items</c>, and Prev/Next controls bound to the opaque
/// cursor. All pagination/query data filtering stays behind a typed <see cref="ICuiPagedSource"/>;
/// this viewport holds NO per-view page cache beyond the single current window and NEVER owns
/// control trees (children are authored in CUI).
/// </summary>
public sealed class CuiQueryViewport : CuiContainerControl,
    ICuiWritableBindingContext, ICuiRepeatItemBindingContext, ICuiActionDispatcher, INotifyPropertyChanged
{
    private static readonly object[] EmptyItems = [];
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

    public CuiQueryViewport() : base("Vertical Stack")
    {
    }

    /// <summary>The typed data port. Assigning it re-evaluates the current window and republishes
    /// all result metadata to authored bindings.</summary>
    public ICuiPagedSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            Reevaluate();
        }
    }

    /// <summary>Delegate invoked after re-evaluation so the owning loader refreshes authored bindings
    /// (Repeat items, pager visibility, page label). Set by the loader at mount.</summary>
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

    private void Reevaluate()
    {
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
            _items.Clear();
            if (result.Items is not null) _items.AddRange(result.Items);
            _totalCount = result.TotalCount;
            _startOffset = result.StartOffset;
            _prev = result.Prev;
            _next = result.Next;
        }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
        RefreshHost?.Invoke();
    }

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "Items": value = _items; return true;
            case "TotalCount": value = _totalCount; return true;
            case "HasPrevious": value = _prev is not null; return true;
            case "HasNext": value = _next is not null; return true;
            case "PageStart": value = _startOffset + 1; return true;
            case "PageEnd":
                value = _startOffset + _items.Count;
                return true;
            case "PageLabel":
                value = _totalCount == 0
                    ? "No emoji found"
                    : $"{_startOffset + 1}–{_startOffset + _items.Count} of {_totalCount}";
                return true;
            case "Query": value = _query; return true;
        }
        value = null;
        return _innerContext is not null && _innerContext.TryGetValue(path, out value);
    }

    public bool TrySetValue(string path, object? value)
    {
        if (path == "Query")
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
            case "composer.emoji-prev" when _prev is not null:
                _cursor = _prev;
                Reevaluate();
                return ValueTask.CompletedTask;
            case "composer.emoji-next" when _next is not null:
                _cursor = _next;
                Reevaluate();
                return ValueTask.CompletedTask;
        }
        if (_innerDispatcher is not null)
            return _innerDispatcher.DispatchAsync(command, parameter, cancellationToken);
        return ValueTask.FromException(new InvalidOperationException(
            $"Chat composer CUI command '{command}' has no handler."));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
