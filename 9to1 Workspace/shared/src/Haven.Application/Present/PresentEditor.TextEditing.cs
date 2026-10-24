using Haven.Core;

namespace Haven.Application;

public enum PresentTextSearchTargetKind { SlideElement, SpeakerNotes }
public enum PresentTextSearchCaseMode { Sensitive, Insensitive }

/// <summary>A semantic search hit addressed by stable slide/object/notes identity and a UTF-16 text range.</summary>
public sealed record PresentTextSearchMatch(Guid SlideId, Guid TargetId, PresentTextSearchTargetKind TargetKind,
    int Start, int Length, string MatchedText);

/// <summary>Structured outcome for one atomic presentation-wide replacement.</summary>
public sealed record PresentTextReplaceResult(string Code, int ReplacedMatches, IReadOnlyList<Guid> ChangedElementIds,
    IReadOnlyList<Guid> ChangedNotesIds, IReadOnlyList<Guid> SkippedLockedElementIds);

/// <summary>Outcome for selecting a still-current semantic search target without changing document content.</summary>
public sealed record PresentTextSearchNavigationResult(bool Navigated, string Code);

public sealed partial class PresentEditor
{
    private HistorySnapshot? _liveTextEditBefore;
    private bool _liveTextEditDirty;
    private string? _liveDocumentTitleOriginalValue;
    private readonly Dictionary<(Guid SlideId, Guid? ElementId), string?> _liveTextEditOriginalValues = [];
    private readonly Dictionary<Guid, string> _liveNotesEditOriginalValues = [];

    public bool IsLiveTextEditActive => _liveTextEditBefore is not null;

    public void BeginLiveTextEdit()
    {
        if (_liveTextEditBefore is not null) return;
        _liveTextEditBefore = CaptureHistory();
        _liveTextEditDirty = false;
    }

    public bool PreviewDocumentTitle(string? title)
    {
        var value = title ?? string.Empty;
        if (string.Equals(Document.Title, value, StringComparison.Ordinal)) return false;
        BeginLiveTextEdit();
        _liveDocumentTitleOriginalValue ??= Document.Title;
        Document.Title = value;
        RefreshLiveTextEditDirty();
        return true;
    }

    public bool PreviewSlideTitle(Guid slideId, string? title)
    {
        var slide = RequireSlide(slideId);
        var value = title ?? string.Empty;
        if (string.Equals(slide.Title, value, StringComparison.Ordinal)) return false;
        BeginLiveTextEdit();
        _liveTextEditOriginalValues.TryAdd((slideId, null), slide.Title);
        slide.Title = value;
        RefreshLiveTextEditDirty();
        return true;
    }

    public bool PreviewElementText(Guid slideId, Guid elementId, string? text)
    {
        var slide = RequireSlide(slideId);
        var element = slide.Elements.FirstOrDefault(item => item.Id == elementId)
            ?? throw new ArgumentOutOfRangeException(nameof(elementId));
        if (element.Locked) return false;
        var value = text ?? string.Empty;
        if (string.Equals(element.Text, value, StringComparison.Ordinal)) return false;
        BeginLiveTextEdit();
        _liveTextEditOriginalValues.TryAdd((slideId, elementId), element.Text);
        element.Text = value;
        RefreshLiveTextEditDirty();
        return true;
    }

    /// <summary>Preview speaker-note edits in the same history boundary as canvas text.</summary>
    public bool PreviewSpeakerNotes(Guid slideId, string? notes)
    {
        var slide = RequireSlide(slideId);
        var value = notes ?? string.Empty;
        if (string.Equals(slide.SpeakerNotes, value, StringComparison.Ordinal)) return false;
        BeginLiveTextEdit();
        _liveNotesEditOriginalValues.TryAdd(slideId, slide.SpeakerNotes);
        slide.SpeakerNotes = value;
        RefreshLiveTextEditDirty();
        return true;
    }

    /// <summary>Search text-bearing PresentElement.Text fields and speaker-note strings in presentation order. Target IDs are stable
    /// ElementIDs or NotesIDs; Start and Length are zero-based UTF-16 offsets into the exact matched string.
    /// Shared structured/rich-text objects require their owning Home text-search adapter and are not traversed here.</summary>
    public IReadOnlyList<PresentTextSearchMatch> SearchText(string query, PresentTextSearchCaseMode caseMode)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        var comparison = caseMode switch
        {
            PresentTextSearchCaseMode.Sensitive => StringComparison.Ordinal,
            PresentTextSearchCaseMode.Insensitive => StringComparison.OrdinalIgnoreCase,
            _ => throw new ArgumentOutOfRangeException(nameof(caseMode))
        };
        var matches = new List<PresentTextSearchMatch>();
        foreach (var slide in Document.Slides)
        {
            foreach (var element in slide.Elements.Where(value => value.Kind is PresentElementKind.Text or PresentElementKind.Shape))
                AddTextMatches(matches, slide.Id, element.Id, PresentTextSearchTargetKind.SlideElement, element.Text, query, comparison);
            AddTextMatches(matches, slide.Id, slide.NotesId, PresentTextSearchTargetKind.SpeakerNotes, slide.SpeakerNotes, query, comparison);
        }
        return Array.AsReadOnly(matches.ToArray());
    }

    /// <summary>Navigate to a current search match by stable target identity and exact UTF-16 range.
    /// Notes navigation selects the slide and clears object selection; the caller retains the range for notes focus.</summary>
    public PresentTextSearchNavigationResult NavigateToSearchMatch(PresentTextSearchMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (IsLiveTextEditActive) return new(false, "TextEditInProgress");
        if (match.SlideId == Guid.Empty || match.TargetId == Guid.Empty || match.Start < 0 || match.Length < 1 ||
            match.MatchedText is null || match.MatchedText.Length != match.Length)
            return new(false, "InvalidSearchMatch");
        var slide = Document.Slides.SingleOrDefault(value => value.Id == match.SlideId);
        if (slide is null) return new(false, "StaleSearchResult");

        switch (match.TargetKind)
        {
            case PresentTextSearchTargetKind.SlideElement:
            {
                var element = slide.Elements.SingleOrDefault(value => value.Id == match.TargetId &&
                    value.Kind is PresentElementKind.Text or PresentElementKind.Shape);
                if (element is null || !RangeMatches(element.Text, match)) return new(false, "StaleSearchResult");
                SelectSlide(slide.Id);
                SelectElements([element.Id]);
                return new(true, "Navigated");
            }
            case PresentTextSearchTargetKind.SpeakerNotes:
                if (slide.NotesId != match.TargetId || !RangeMatches(slide.SpeakerNotes, match))
                    return new(false, "StaleSearchResult");
                SelectSlide(slide.Id);
                _selectedElementIds.Clear();
                return new(true, "Navigated");
            default:
                return new(false, "InvalidSearchMatch");
        }
    }

    private static bool RangeMatches(string? source, PresentTextSearchMatch match) =>
        source is not null && match.Start <= source.Length - match.Length &&
        string.Equals(source.Substring(match.Start, match.Length), match.MatchedText, StringComparison.Ordinal);

    /// <summary>Replace matching element-text/note ranges in one undoable operation. Locked object targets
    /// are skipped and returned explicitly; stable notes/object IDs and unrelated selection remain unchanged.</summary>
    public PresentTextReplaceResult ReplaceAllText(string query, string replacement, PresentTextSearchCaseMode caseMode)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var matches = SearchText(query, caseMode);
        if (matches.Count == 0) return new("NoMatches", 0, [], [], []);
        var groups = matches.GroupBy(match => (match.SlideId, match.TargetId, match.TargetKind)).ToArray();
        var elementChanges = new Dictionary<(Guid SlideId, Guid ElementId), (PresentElement Element, string Text, int Count)>();
        var notesChanges = new Dictionary<(Guid SlideId, Guid NotesId), (PresentSlide Slide, string Text, int Count)>();
        var skippedLocked = new HashSet<Guid>();
        var editableMatchCount = 0;
        foreach (var group in groups)
        {
            var slide = Document.Slides.SingleOrDefault(value => value.Id == group.Key.SlideId);
            if (slide is null) return new("StaleSearchResult", 0, [], [], []);
            if (group.Key.TargetKind == PresentTextSearchTargetKind.SlideElement)
            {
                var element = slide.Elements.SingleOrDefault(value => value.Id == group.Key.TargetId &&
                    (value.Kind is PresentElementKind.Text or PresentElementKind.Shape));
                if (element is null) return new("StaleSearchResult", 0, [], [], []);
                if (element.Locked) { skippedLocked.Add(element.Id); continue; }
                editableMatchCount += group.Count();
                if (!TryReplaceMatches(element.Text, group, replacement, out var updatedElementText, out var elementReplacementCount))
                    return new("StaleSearchResult", 0, [], [], []);
                if (elementReplacementCount > 0)
                    elementChanges.Add((slide.Id, element.Id), (element, updatedElementText, elementReplacementCount));
            }
            else
            {
                editableMatchCount += group.Count();
                if (slide.NotesId != group.Key.TargetId)
                    return new("StaleSearchResult", 0, [], [], []);
                if (!TryReplaceMatches(slide.SpeakerNotes, group, replacement, out var updatedNotes, out var notesReplacementCount))
                    return new("StaleSearchResult", 0, [], [], []);
                if (notesReplacementCount > 0)
                    notesChanges.Add((slide.Id, slide.NotesId), (slide, updatedNotes, notesReplacementCount));
            }
        }

        var replacedMatches = elementChanges.Values.Sum(value => value.Count) + notesChanges.Values.Sum(value => value.Count);
        if (replacedMatches == 0)
            return new(editableMatchCount == 0 && skippedLocked.Count > 0 ? "NoEditableMatches" : "NoChanges",
                0, [], [], skippedLocked.Order().ToArray());
        Mutate(() =>
        {
            foreach (var value in elementChanges.Values) value.Element.Text = value.Text;
            foreach (var value in notesChanges.Values) value.Slide.SpeakerNotes = value.Text;
        });
        return new("Replaced", replacedMatches,
            elementChanges.Keys.OrderBy(value => value.SlideId).ThenBy(value => value.ElementId).Select(value => value.ElementId).ToArray(),
            notesChanges.Keys.OrderBy(value => value.SlideId).ThenBy(value => value.NotesId).Select(value => value.NotesId).ToArray(),
            skippedLocked.Order().ToArray());
    }

    private static void AddTextMatches(List<PresentTextSearchMatch> output, Guid slideId, Guid targetId,
        PresentTextSearchTargetKind targetKind, string? text, string query, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(text)) return;
        var start = 0;
        while (start <= text.Length - query.Length)
        {
            var index = text.IndexOf(query, start, comparison);
            if (index < 0) break;
            output.Add(new(slideId, targetId, targetKind, index, query.Length, text.Substring(index, query.Length)));
            start = index + query.Length;
        }
    }

    private static bool TryReplaceMatches(string source, IEnumerable<PresentTextSearchMatch> matches,
        string replacement, out string updated, out int replacedMatches)
    {
        updated = source;
        replacedMatches = 0;
        foreach (var match in matches.OrderByDescending(value => value.Start))
        {
            if (match.Start < 0 || match.Length < 1 || match.Start > updated.Length - match.Length ||
                !string.Equals(updated.Substring(match.Start, match.Length), match.MatchedText, StringComparison.Ordinal))
                return false;
            if (string.Equals(match.MatchedText, replacement, StringComparison.Ordinal)) continue;
            updated = updated.Remove(match.Start, match.Length).Insert(match.Start, replacement);
            replacedMatches++;
        }
        return true;
    }

    private void RefreshLiveTextEditDirty()
    {
        _liveTextEditDirty = _liveTextEditBefore is not null &&
            (_liveDocumentTitleOriginalValue is not null && !string.Equals(_liveDocumentTitleOriginalValue, Document.Title, StringComparison.Ordinal) ||
            (_liveTextEditOriginalValues.Any(pair =>
            {
                var slide = Document.Slides.SingleOrDefault(value => value.Id == pair.Key.SlideId);
                if (slide is null) return true;
                var current = pair.Key.ElementId is { } id
                    ? slide.Elements.SingleOrDefault(value => value.Id == id)?.Text
                    : slide.Title;
                return !string.Equals(pair.Value, current, StringComparison.Ordinal);
            }) || _liveNotesEditOriginalValues.Any(pair =>
            {
                var slide = Document.Slides.SingleOrDefault(value => value.Id == pair.Key);
                return slide is null || !string.Equals(pair.Value, slide.SpeakerNotes, StringComparison.Ordinal);
            })));
    }

    public bool CommitLiveTextEdit()
        => CommitLiveTextEdit(notifyChanged: true);

    private bool CommitLiveTextEdit(bool notifyChanged)
    {
        if (_liveTextEditBefore is null) return false;
        var before = _liveTextEditBefore;
        _liveTextEditBefore = null;
        _liveTextEditDirty = false;
        _liveDocumentTitleOriginalValue = null;
        _liveTextEditOriginalValues.Clear();
        _liveNotesEditOriginalValues.Clear();
        Document.Normalize();
        var after = Snapshot(Document);
        if (string.Equals(before.Document, after, StringComparison.Ordinal)) return false;
        Push(_undo, before);
        _redo.Clear();
        Document.UpdatedAt = _timeProvider.GetUtcNow();
        EnsureSelectionIsValid();
        if (notifyChanged) Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool CancelLiveTextEdit()
    {
        if (_liveTextEditBefore is null) return false;
        var before = _liveTextEditBefore;
        _liveTextEditBefore = null;
        _liveTextEditDirty = false;
        _liveDocumentTitleOriginalValue = null;
        _liveTextEditOriginalValues.Clear();
        _liveNotesEditOriginalValues.Clear();
        // Cancel only the previewed document change. Navigation/selection performed
        // while editing is session state, not part of the text rollback.
        var selectedSlideId = _selectedSlideId;
        var selectedElementIds = _selectedElementIds.ToArray();
        Restore(before);
        _selectedSlideId = selectedSlideId;
        _selectedElementIds.Clear();
        _selectedElementIds.AddRange(selectedElementIds);
        EnsureSelectionIsValid();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
