namespace HavenOS.Apps.Cards;

public enum CardNavigationInput
{
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    Flip,
}

public sealed record CardFocusedWindow(
    Guid SetId,
    long SetRevision,
    Guid? SelectedCardId,
    CardEntry? Previous,
    CardEntry? Current,
    CardEntry? Next,
    int Position,
    int TotalCount,
    bool FrontVisible,
    CardViewPreferences Preferences);

/// <summary>
/// Pure, non-persistent interaction state over one authorised Cards snapshot.
/// The caller supplies saved preferences through the canonical settings owner;
/// changing a preference here NEVER claims it was durably saved.
/// A maximum of three card references is exposed to the visible CUI window.
/// </summary>
public sealed class CardNavigationSession
{
    private CardSet? _set;
    private CardEntry[] _visible = [];
    private string _search = string.Empty;
    private string? _groupKey;
    private Guid? _selected;
    private bool _front = true;
    private CardInteractionMode _mode = CardInteractionMode.View;
    private CardViewPreferences _preferences = new();

    public CardSet? Set => _set;
    public CardViewPreferences Preferences => _preferences;
    public CardInteractionMode Mode => _mode;
    public string SearchText => _search;
    public Guid? SelectedCardId => _selected;
    public bool FrontVisible => _front;
    public int Count => _visible.Length;
    public int Index => _selected is null ? -1 :
        Array.FindIndex(_visible, card => card.CardId == _selected.Value);
    public CardEntry? Current => Index >= 0 ? _visible[Index] : null;
    public IReadOnlyList<string> GroupNames => _set is null ? [] :
        CardSetOperations.Group(_set, _preferences.Grouping, _preferences.CustomGroupKey)
            .Keys.Order(StringComparer.Ordinal).ToArray();
    public string? SelectedGroup => _groupKey;

    public CardNavigationSession(CardViewPreferences? preferences = null)
    {
        ApplyPreferences(preferences ?? new());
    }

    public void Load(CardSet set)
    {
        CardSetOperations.Validate(set);
        bool changedSet = _set is null || _set.SetId != set.SetId;
        _set = set;
        if (changedSet)
        {
            _groupKey = null;
            _selected = null;
            _front = true;
            _mode = CardInteractionMode.View;
        }
        Refresh();
    }

    public void ApplyPreferences(CardViewPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!Enum.IsDefined(preferences.Navigation) || !Enum.IsDefined(preferences.Grouping)
            || (preferences.Grouping == CardGroupingKind.Custom
                && string.IsNullOrWhiteSpace(preferences.CustomGroupKey)))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "Unsupported Cards navigation preferences.");
        _preferences = preferences;
        _groupKey = null;
        Refresh();
    }

    public void SetGrouping(CardGroupingKind kind, string? customGroupKey = null)
    {
        ApplyPreferences(_preferences with { Grouping = kind, CustomGroupKey = customGroupKey });
    }

    public void SetDirection(CardNavigationDirection direction)
    {
        ApplyPreferences(_preferences with { Navigation = direction });
    }

    public void SetSearch(string search)
    {
        ArgumentNullException.ThrowIfNull(search);
        if (search.Length > 256)
            throw new CardOperationException(CardFailureCode.InvalidState,
                "Card search input is limited to 256 characters.");
        _search = search.Trim();
        Refresh();
    }

    public bool SelectGroup(string group)
    {
        if (string.IsNullOrWhiteSpace(group) || !GroupNames.Contains(group, StringComparer.Ordinal))
            return false;
        _groupKey = group;
        Refresh();
        return true;
    }

    public void SetMode(CardInteractionMode mode)
    {
        if (!Enum.IsDefined(mode))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "Unknown Cards interaction mode.");
        _mode = mode;
    }

    public bool Flip()
    {
        if (Current is null) return false;
        _front = !_front;
        return true;
    }

    public bool Move(int offset)
    {
        if (offset is not (-1 or 1) || Index < 0) return false;
        int next = Index + offset;
        if (next < 0 || next >= _visible.Length) return false;
        _selected = _visible[next].CardId;
        _front = true;
        return true;
    }

    public bool Navigate(CardNavigationInput input) => input switch
    {
        CardNavigationInput.Flip => Flip(),
        CardNavigationInput.ArrowUp when _preferences.Navigation == CardNavigationDirection.Vertical => Move(-1),
        CardNavigationInput.ArrowDown when _preferences.Navigation == CardNavigationDirection.Vertical => Move(1),
        CardNavigationInput.ArrowLeft when _preferences.Navigation == CardNavigationDirection.Horizontal => Move(-1),
        CardNavigationInput.ArrowRight when _preferences.Navigation == CardNavigationDirection.Horizontal => Move(1),
        _ => false,
    };

    public CardFocusedWindow Window()
    {
        if (_set is null)
            throw new CardOperationException(CardFailureCode.InvalidState,
                "Open an authorised Cards set before requesting a focused window.");
        int index = Index;
        return new(_set.SetId, _set.Revision, _selected,
            index > 0 ? _visible[index - 1] : null,
            index >= 0 ? _visible[index] : null,
            index >= 0 && index + 1 < _visible.Length ? _visible[index + 1] : null,
            index < 0 ? 0 : index + 1, _visible.Length, _front, _preferences);
    }

    private void Refresh()
    {
        if (_set is null)
        {
            _visible = [];
            _selected = null;
            return;
        }

        IReadOnlyDictionary<string, IReadOnlyList<CardEntry>> groups =
            CardSetOperations.Group(_set, _preferences.Grouping, _preferences.CustomGroupKey);
        string[] ordered = groups.Keys.Order(StringComparer.Ordinal).ToArray();
        if (_groupKey is null || !groups.ContainsKey(_groupKey))
            _groupKey = ordered.FirstOrDefault();

        _visible = _groupKey is null ? [] : groups[_groupKey]
            .Where(card => _search.Length == 0
                || card.Front.SearchText.Contains(_search, StringComparison.OrdinalIgnoreCase)
                || card.Back.SearchText.Contains(_search, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (_visible.Length == 0)
        {
            _selected = null;
            _front = true;
            return;
        }
        if (_selected is null || !_visible.Any(card => card.CardId == _selected))
        {
            _selected = _visible[0].CardId;
            _front = true;
        }
    }
}
