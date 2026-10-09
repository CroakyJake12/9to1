using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Application;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Local draft and projection of the actual scoped memory owner. It never
/// constructs a memory input, Home permission, saved record or recovery receipt.</summary>
public sealed partial class AssistantsMemoryCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly CuiViewModel _values = new();
    private readonly bool _hasOwner;
    private readonly string _unavailableReason;
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Action<Action> _publish;
    private readonly Func<bool> _isCurrent;
    private bool _revoked, _busy, _commitPending, _outcomeUnconfirmed;
    private AssistantMemoryView? _view;
    private AssistantMemoryWritePreview? _preview;
    private AssistantMemorySaveResult? _result;
    private string _title = "", _summary = "", _status = "", _error = "", _approval = "";
    private Guid _operation;
    private bool _supportsRevisions;
    private KnowledgeRecord? _selected;
    private CanonicalAssistantMemoryMutationKind _mutationKind;

    public AssistantsMemoryCuiBindings(bool hasOwner, string unavailableReason,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Action<Action> publish, Func<bool> isCurrent)
    {
        _hasOwner = hasOwner; _unavailableReason = unavailableReason;
        _dispatch = dispatch; _publish = publish; _isCurrent = isCurrent;
        _values.PropertyChanged += (_, args) => { if (Current) PropertyChanged?.Invoke(this, args); };
        Refresh();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Current => !_revoked && _isCurrent();
    internal AssistantMemoryView? OriginalView => _view;
    internal AssistantMemoryWritePreview? OriginalPreview => _preview;
    internal string OriginalTitle => _title;
    internal string OriginalSummary => _summary;
    internal Guid OriginalOperationId => _operation;
    internal KnowledgeRecord? OriginalSelectedRecord => _selected;
    internal CanonicalAssistantMemoryMutationKind OriginalMutationKind => _mutationKind;
    internal void SetRevisionSupport(bool available) { _supportsRevisions = available; Refresh(); }
    internal void StartRevision(MemoryRow row, CanonicalAssistantMemoryMutationKind kind)
    {
        if (!Current || !_supportsRevisions || HasUnconfirmedChanges || _view is null ||
            !CanChangeOriginalMemoryRow(row) ||
            !_view.Records.Any(record => ReferenceEquals(record, row.Original)) ||
            kind is not (CanonicalAssistantMemoryMutationKind.Correct or CanonicalAssistantMemoryMutationKind.Reject))
            throw new InvalidOperationException("Select a current permitted memory after resolving the original draft.");
        _selected = row.Original; _mutationKind = kind; _title = row.Title; _summary = row.Summary;
        _operation = Guid.NewGuid(); _preview = null; _result = null; _approval = ""; _error = "";
        _status = kind == CanonicalAssistantMemoryMutationKind.Correct ? "Edit this local correction, then review it." : "Review stopping recall of this memory. Its original history is retained."; Refresh();
    }
    internal bool HasUnconfirmedChanges => HasUnconfirmedImport || HasUnconfirmedWriteChanges;
    private bool HasUnconfirmedWriteChanges => _commitPending || _outcomeUnconfirmed ||
        _result?.Saved != true && (_preview is not null || _title.Length != 0 || _summary.Length != 0);
    public void Revoke() => _revoked = true;
    internal void SetBusy(bool value) { _busy = value; Refresh(); }
    internal void SetError(string reason) { _error = reason; Refresh(); }
    internal void SetView(AssistantMemoryView actual)
    { _view = actual; _status = actual.Reason; _error = ""; Refresh(); }
    internal void SetUnavailable(string reason) { _view = null; _status = reason; Refresh(); }
    internal void SetPreview(AssistantMemoryWritePreview actual)
    {
        if (actual.OperationId != _operation || actual.MutationKind != _mutationKind ||
            actual.OriginalRecord?.Id != _selected?.Id) throw new InvalidOperationException("The actual preview belongs to another memory draft.");
        _preview = actual; _result = null; _status = actual.Explanation; _error = ""; Refresh();
    }
    internal void BeginCommit()
    { _commitPending = true; _status = "Requesting the separate Home approval for this exact record."; _error = ""; Refresh(); }
    internal void Observe(AssistantMemoryWriteObservation actual)
    {
        if (_preview is null || actual.OperationId != _preview.OperationId)
            throw new InvalidOperationException("The observed memory write belongs to another preview.");
        _status = actual.Reason; _approval = actual.HomeApprovalRequestId ?? "";
        _outcomeUnconfirmed |= actual.State == AssistantMemoryWriteState.OutcomeUnconfirmed; Refresh();
    }
    internal void Acknowledge(AssistantMemoryWritePreview samePreview, AssistantMemorySaveResult actual)
    {
        if (!ReferenceEquals(_preview, samePreview) || actual.Saved &&
            (actual.Record is null || actual.Record.Id != samePreview.Candidate.Id))
            throw new InvalidOperationException("The actual saved record does not acknowledge this original preview.");
        _result = actual; _commitPending = false; _status = actual.Reason; _error = ""; Refresh();
    }
    internal void MarkUnconfirmed(string reason)
    { _commitPending = false; _outcomeUnconfirmed = true; _error = reason; Refresh(); }
    internal void ReviseDraft()
    {
        if (_commitPending || _outcomeUnconfirmed) throw new InvalidOperationException("The original memory write must settle before another draft.");
        _preview = null; _result = null; _operation = Guid.NewGuid(); _approval = ""; _error = "";
        _status = "Edit this local draft, then review a new explicit operation."; Refresh();
    }
    internal void DiscardDraft()
    {
        if (_commitPending || _outcomeUnconfirmed) throw new InvalidOperationException("The pending or unconfirmed original must remain retained.");
        _title = ""; _summary = ""; _operation = Guid.Empty; _selected = null; _mutationKind = CanonicalAssistantMemoryMutationKind.Create; _preview = null; _result = null;
        _approval = ""; _error = ""; _status = "The local draft was discarded. Saved memories were unchanged."; Refresh();
    }
    public bool TryGetValue(string path, out object? value)
    {
        if (Current) return _values.TryGetValue(path, out value);
        value = path.StartsWith("Can", StringComparison.Ordinal) || path.StartsWith("Has", StringComparison.Ordinal) ? false : null; return true;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (path == "MemorySearch") return TrySetMemorySearch(value);
        if (!Current || !_hasOwner || _view?.IsAvailable != true || _mutationKind == CanonicalAssistantMemoryMutationKind.Reject || _busy || _commitPending || _preview is not null || value is not string text) return false;
        var accepted = false;
        _publish(() =>
        {
            if (!Current || _busy || _preview is not null) return;
            if (path == "MemoryDraftTitle" && text.Length <= 160) { _title = text; accepted = true; }
            else if (path == "MemoryDraftSummary" && text.Length <= 4000) { _summary = text; accepted = true; }
            if (accepted) { if (_operation == Guid.Empty) _operation = Guid.NewGuid(); _result = null; Refresh(); }
        });
        return accepted;
    }
    public bool? IsActionAvailable(string command) => Current && (command switch
    {
        "assistants.memory.correct" or "assistants.memory.reject" => _supportsRevisions && _view?.IsAvailable == true && !_busy && !HasUnconfirmedChanges,
        "assistants.memory.status" => _hasOwner && _preview is not null,
        "assistants.memory.back" => !_busy && !HasUnconfirmedChanges,
        "assistants.memory.refresh" => _hasOwner && !_busy && !HasUnconfirmedChanges,
        "assistants.memory.review" => _view?.IsAvailable == true && !_busy && !_commitPending && _preview is null &&
            _title.Trim().Length > 0 && _summary.Trim().Length > 0 && _operation != Guid.Empty,
        "assistants.memory.confirm" => _preview is not null && _result is null && !_busy && !_commitPending && !_outcomeUnconfirmed,
        "assistants.memory.revise" => _preview is not null && !_busy && !_commitPending && !_outcomeUnconfirmed,
        "assistants.memory.discard" => !_busy && !_commitPending && !_outcomeUnconfirmed &&
            (_preview is not null || _title.Length != 0 || _summary.Length != 0),
        _ => IsImportActionAvailable(command) || IsPagingActionAvailable(command)
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) =>
        IsActionAvailable(command) == true &&
        (command is not ("assistants.memory.correct" or "assistants.memory.reject") ||
            parameter is MemoryRow row && CanChangeOriginalMemoryRow(row))
            ? _dispatch(command, parameter, token) :
            ValueTask.FromException(new InvalidOperationException("This action is unavailable for the current memory review."));
    private void Set(string path, object? value) => _publish(() => { if (Current) _values.Set(path, value); });
    private void Refresh()
    {
        RefreshMemoryPaging();
        RefreshMemoryImport();
        Set("MemoryHeading", _view is null ? "Assistant memory" : _view.Definition.Configuration.Name + " · memory");
        Set("MemoryStatus", _hasOwner ? _status : _unavailableReason); Set("MemoryError", _error); Set("HasMemoryError", _error.Length != 0);
        PublishMemoryRepeatRows();
        Set("CanMemoryChangeRecord", IsActionAvailable("assistants.memory.correct") == true);
        RefreshMemoryOriginActions();
        Set("MemoryDraftHeading", _mutationKind switch { CanonicalAssistantMemoryMutationKind.Correct => "Correct selected preference", CanonicalAssistantMemoryMutationKind.Reject => "Stop recalling selected preference", _ => "Add a private preference" });
        Set("HasMemoryRecords", _view?.Records.Count > 0); Set("CanEditMemoryDraft", _mutationKind != CanonicalAssistantMemoryMutationKind.Reject && _hasOwner && _view?.IsAvailable == true && !_busy && !_commitPending && _preview is null);
        Set("MemoryDraftTitle", _title); Set("MemoryDraftSummary", _summary); Set("HasMemoryPreview", _preview is not null);
        Set("MemoryPreviewTitle", _preview?.Candidate.Title ?? ""); Set("MemoryPreviewSummary", _preview?.Candidate.Summary ?? "");
        Set("MemoryOperation", _preview?.OperationId.ToString("D") ?? ""); Set("MemoryApprovalRequest", _approval); Set("HasMemoryApprovalRequest", _approval.Length != 0);
        Set("MemoryPreviewExplanation", _preview?.Explanation ?? "");
        foreach (var pair in new[] { ("CanMemoryBack", "back"), ("CanMemoryRefresh", "refresh"), ("CanMemoryReview", "review"),
            ("CanMemoryConfirm", "confirm"), ("CanMemoryRevise", "revise"), ("CanMemoryDiscard", "discard"), ("CanMemoryStatus", "status") })
            Set(pair.Item1, IsActionAvailable("assistants.memory." + pair.Item2) == true);
    }
    public sealed record MemoryRow(KnowledgeRecord Original)
    {
        public Guid Id => Original.Id;
        public string Title => Original.Title;
        public string Summary => Original.Summary;
        public string Privacy => Original.PrivacyClass.ToString();
    }
}
