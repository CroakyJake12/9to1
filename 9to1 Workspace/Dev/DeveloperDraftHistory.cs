namespace HavenOS.Apps.Dev;

/// <summary>Bounded, document-local text history. The editor's original draft owner still applies
/// every transition and owns revisions, review invalidation and recovery. This carries no saved
/// source, permission or review authority. Callers serialize access with their draft owner.</summary>
public sealed class DeveloperDraftHistory
{
    public const int MaximumStates = 100;
    public const int MaximumRetainedCharacters = 8_000_000;
    private readonly int _maximumStates, _maximumCharacters;
    private readonly List<string> _states = [];
    private int _position;
    private long _characters;

    public DeveloperDraftHistory(string initialText, int maximumStates = MaximumStates,
        int maximumRetainedCharacters = MaximumRetainedCharacters)
    {
        ArgumentNullException.ThrowIfNull(initialText);
        if (maximumStates < 2) throw new ArgumentOutOfRangeException(nameof(maximumStates));
        if (maximumRetainedCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maximumRetainedCharacters));
        _maximumStates = maximumStates; _maximumCharacters = maximumRetainedCharacters;
        Reset(initialText);
    }

    public bool CanUndo(string currentText) { Synchronize(currentText); return _position > 0; }
    public bool CanRedo(string currentText) { Synchronize(currentText); return _position + 1 < _states.Count; }

    public void RecordChange(string previousText, string revisedText)
    {
        ArgumentNullException.ThrowIfNull(revisedText); Synchronize(previousText);
        if (StringComparer.Ordinal.Equals(previousText, revisedText)) return;
        for (var i = _states.Count - 1; i > _position; i--)
        { _characters -= _states[i].Length; _states.RemoveAt(i); }
        _states.Add(revisedText); _characters += revisedText.Length; _position++;
        // Always retain the complete current draft, even if that one state exceeds the budget.
        // Prune only older history; never truncate text or reject a legitimate editor change.
        while (_states.Count > 1 && (_states.Count > _maximumStates || _characters > _maximumCharacters))
        { _characters -= _states[0].Length; _states.RemoveAt(0); _position--; }
    }

    public bool TryUndo(string currentText, Action<string> applyDraft) => Move(currentText, applyDraft, -1);
    public bool TryRedo(string currentText, Action<string> applyDraft) => Move(currentText, applyDraft, 1);

    private bool Move(string currentText, Action<string> applyDraft, int direction)
    {
        ArgumentNullException.ThrowIfNull(applyDraft); Synchronize(currentText);
        var next = _position + direction;
        if (next < 0 || next >= _states.Count) return false;
        // A refused/stale owner transition leaves the history cursor unchanged.
        applyDraft(_states[next]); _position = next;
        return true;
    }

    private void Synchronize(string currentText)
    {
        ArgumentNullException.ThrowIfNull(currentText);
        // Recovery/adoption/native edits outside this history cannot resurrect an earlier draft.
        if (!StringComparer.Ordinal.Equals(currentText, _states[_position])) Reset(currentText);
    }
    private void Reset(string text)
    { _states.Clear(); _states.Add(text); _position = 0; _characters = text.Length; }
}
