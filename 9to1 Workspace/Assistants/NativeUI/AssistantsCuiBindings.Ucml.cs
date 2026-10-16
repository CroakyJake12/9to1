using System.Text.Json;
using HavenOS.Apps.Assistants.Memory;
using NineToOne.Dulche.Context;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsMemoryCuiBindings
{
    private bool _supportsUcml;
    private IReadOnlyList<UcmlImportRow> _ucmlRows = [];
    private AssistantMemoryView? _ucmlReviewView;
    private UcmlImportRow? _ucmlSelected;
    internal IReadOnlyList<UcmlImportRow> OriginalUcmlRows => _ucmlRows;
    internal bool HasUnconfirmedUcml => _ucmlRows.Any(row => row.State is UcmlImportState.Ready or UcmlImportState.Pending or UcmlImportState.Unconfirmed);
    internal void SetUcmlSupport(bool supported) { _supportsUcml = supported; Refresh(); }
    internal void SetUcmlReview(AssistantMemoryView view, IReadOnlyList<AssistantUcmlFile> files)
    {
        if (!Current || !ReferenceEquals(view, _view) || HasUnconfirmedUcml || files.Count > AssistantUcmlFilePicker.MaximumFiles)
            throw new InvalidOperationException("Retain the current authorized memory and settle its existing review first.");
        var rows = new List<UcmlImportRow>(); var ids = new HashSet<Guid>();
        foreach (var file in files)
        {
            UcmlDocument? document = null; var error = file.Error;
            if (error is null)
                try
                {
                    if (file.Content is null || file.Content.Length > AssistantUcmlFilePicker.MaximumFileCharacters)
                        throw new ArgumentException("This file exceeds the review limit or contains no UCML text.");
                    document = UcmlCodec.Parse(file.Content);
                    AssistantUcmlImportPolicy.Validate(document, view.Definition.Identity.DefinitionId);
                    if (!ids.Add(document.Metadata.Id)) throw new ArgumentException("Duplicate record ID within this selection; review the first file with this ID.");
                }
                catch (Exception failure) when (failure is ArgumentException or FormatException or JsonException or UnauthorizedAccessException)
                { error = failure.Message; }
            rows.Add(new(file.Name, document, error));
        }
        _ucmlRows = rows.AsReadOnly(); _ucmlReviewView = view; _ucmlSelected = rows.FirstOrDefault();
        _status = $"Reviewed {rows.Count} files locally. {rows.Count(row => row.State == UcmlImportState.Ready)} ready for separate Home decisions.";
        _error = ""; Refresh();
    }

    internal void SelectUcmlRow(UcmlImportRow row)
    {
        if (!_ucmlRows.Any(actual => ReferenceEquals(actual, row))) throw new InvalidOperationException("Select the actual reviewed file.");
        _ucmlSelected = row; Refresh();
    }
    internal void BeginUcmlImport(UcmlImportRow row)
    {
        if (!Current || !ReferenceEquals(_ucmlReviewView, _view) || !_ucmlRows.Any(actual => ReferenceEquals(actual, row)) || row.State != UcmlImportState.Ready)
            throw new InvalidOperationException("Review this exact file in the current memory scope before submitting it.");
        row.State = UcmlImportState.Pending; row.Result = "Preparing this exact file for Home review.";
        _ucmlSelected = row; _status = "Import in progress. Open Home to decide the current memory request."; Refresh();
    }
    internal void SetUcmlPreview(UcmlImportRow row, AssistantMemoryWritePreview preview)
    {
        if (row.State != UcmlImportState.Pending || preview.OperationId != row.OperationId || preview.Candidate.Id != row.Document?.Metadata.Id ||
            preview.Candidate.Summary != row.Document.Markdown || preview.Candidate.Title != row.Document.Metadata.Title)
            throw new InvalidOperationException("The prepared memory must match the exact reviewed UCML file.");
        row.Preview = preview; Refresh();
    }
    internal void ObserveUcml(UcmlImportRow row, AssistantMemoryWriteObservation observation)
    {
        if (row.State != UcmlImportState.Pending || observation.OperationId != row.OperationId) return;
        row.Result = observation.Reason; row.ApprovalRequest = observation.HomeApprovalRequestId ?? ""; Refresh();
    }
    internal void AcknowledgeUcml(UcmlImportRow row, AssistantMemorySaveResult result)
    {
        if (row.State != UcmlImportState.Pending || row.Preview is null || (result.Saved && result.Record?.Id != row.Preview.Candidate.Id))
            throw new InvalidOperationException("The original commit must acknowledge this same reviewed record.");
        row.State = result.Saved ? UcmlImportState.Saved : UcmlImportState.Declined;
        row.Result = result.Reason; row.ApprovalRequest = "";
        _status = $"UCML import: {_ucmlRows.Count(item => item.State == UcmlImportState.Saved)} saved; {_ucmlRows.Count(item => item.State == UcmlImportState.Declined)} declined; {_ucmlRows.Count(item => item.State == UcmlImportState.Denied)} invalid or unsupported.";
        Refresh();
    }
    internal void MarkUcmlUnconfirmed(UcmlImportRow row, string reason)
    { row.State = UcmlImportState.Unconfirmed; row.Result = "Outcome unconfirmed: " + reason; _error = row.Result; Refresh(); }
    internal void ClearUcmlReview()
    {
        if (_busy || _ucmlRows.Any(row => row.State is UcmlImportState.Pending or UcmlImportState.Unconfirmed))
            throw new InvalidOperationException("Retain the original in-flight memory operations.");
        _ucmlRows = []; _ucmlReviewView = null; _ucmlSelected = null; Refresh();
    }
    internal void SetUcmlExportStatus(bool saved)
    { _status = saved ? "Exported the current UCML record to the local file you selected. This file contains private plaintext memory." : "Export canceled."; Refresh(); }

    private bool IsUcmlActionAvailable(string command) => _supportsUcml && _hasOwner && (command switch
    {
        "assistants.memory.ucml.pick" => _view?.IsAvailable == true && !_busy && !HasUnconfirmedChanges,
        "assistants.memory.ucml.confirm" => _view?.IsAvailable == true && !_busy && !HasUnconfirmedWriteChanges && !HasUnconfirmedImport &&
            ReferenceEquals(_ucmlReviewView, _view) && _ucmlRows.Any(row => row.State == UcmlImportState.Ready) &&
            !_ucmlRows.Any(row => row.State is UcmlImportState.Pending or UcmlImportState.Unconfirmed),
        "assistants.memory.ucml.inspect" => _ucmlRows.Count != 0,
        "assistants.memory.ucml.clear" => !_busy && _ucmlRows.Count != 0 && !_ucmlRows.Any(row => row.State is UcmlImportState.Pending or UcmlImportState.Unconfirmed),
        "assistants.memory.ucml.export" => _view?.IsAvailable == true && !_busy && !HasUnconfirmedChanges,
        _ => false
    });
    private bool CanExportUcmlRow(MemoryRow row) => IsUcmlActionAvailable("assistants.memory.ucml.export") &&
        _memoryRows.Any(actual => ReferenceEquals(actual, row)) && row.Original.LearnedBecause.StartsWith("ucml.v1:", StringComparison.Ordinal);
    private void RefreshUcml()
    {
        Set("HasUcmlTransfer", _supportsUcml); Set("UcmlFiles", _ucmlRows);
        Set("HasUcmlFiles", _ucmlRows.Count != 0); Set("HasUcmlDetails", _ucmlSelected is not null);
        Set("UcmlDetailName", _ucmlSelected?.Name ?? ""); Set("UcmlDetailMetadata", _ucmlSelected?.Metadata ?? "");
        Set("UcmlDetailBody", _ucmlSelected?.Document?.Markdown ?? "");
        Set("UcmlDetailResult", _ucmlSelected?.Result ?? "");
        var pending = _ucmlRows.FirstOrDefault(row => row.State is UcmlImportState.Pending or UcmlImportState.Unconfirmed);
        Set("UcmlApprovalRequest", pending?.ApprovalRequest ?? "");
        Set("HasUcmlApprovalRequest", !string.IsNullOrEmpty(pending?.ApprovalRequest));
        Set("UcmlReviewCount", $"{_ucmlRows.Count} files · {_ucmlRows.Count(row => row.State == UcmlImportState.Ready)} ready · {_ucmlRows.Count(row => row.State == UcmlImportState.Saved)} saved");
        foreach (var (binding, action) in new[] { ("CanUcmlPick", "pick"), ("CanUcmlConfirm", "confirm"), ("CanUcmlClear", "clear") })
            Set(binding, IsActionAvailable("assistants.memory.ucml." + action) == true);
    }
    private bool TryGetUcmlItemValue(object item, string path, out object? value)
    {
        value = null;
        if (!Current || item is not UcmlImportRow row || !_ucmlRows.Any(actual => ReferenceEquals(actual, row))) return false;
        value = path switch { "Id" => row.Id, "Name" => row.Name, "Title" => row.Document?.Metadata.Title ?? row.Name,
            "Policy" => row.Policy, "Result" => row.State + ": " + row.Result, "CanInspect" => true, _ => null };
        return value is not null;
    }
    internal enum UcmlImportState { Ready, Denied, Pending, Saved, Declined, Unconfirmed }
    internal sealed class UcmlImportRow(string name, UcmlDocument? document, string? error) : System.ComponentModel.INotifyPropertyChanged
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Name { get; } = name;
        internal Guid OperationId { get; } = Guid.NewGuid();
        internal UcmlDocument? Document { get; } = document;
        internal string Serialized { get; } = document is null ? "" : UcmlCodec.Serialize(document);
        internal string Metadata { get; } = document is null ? "" : UcmlCodec.SerializeMetadata(document.Metadata);
        internal string Policy => Document is { Metadata: var m }
            ? $"Category: {m.Category} · {m.Classification} · scope {m.Scope.Kind}:{m.Scope.Id} · recipient {string.Join(", ", m.Consent.Recipients)} · confidence {m.Confidence:0.##} · {m.Sources.Count} sources"
            : "No valid UCML metadata.";
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private UcmlImportState _state = error is null ? UcmlImportState.Ready : UcmlImportState.Denied;
        private string _result = error ?? "Ready for your review; not imported.";
        internal UcmlImportState State
        {
            get => _state;
            set { if (_state == value) return; _state = value; PropertyChanged?.Invoke(this, new("Result")); }
        }
        internal string Result
        {
            get => _result;
            set { if (_result == value) return; _result = value; PropertyChanged?.Invoke(this, new("Result")); }
        }
        internal string ApprovalRequest { get; set; } = "";
        internal AssistantMemoryWritePreview? Preview { get; set; }
    }
}
