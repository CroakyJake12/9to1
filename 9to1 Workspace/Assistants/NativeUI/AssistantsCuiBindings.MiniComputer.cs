using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.MiniComputer;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsCuiBindings
{
    internal bool IsMiniComputerVisible => Current && _route == "mini-computer";
    internal void ShowMiniComputer() { _route = "mini-computer"; Refresh(); }
}

/// <summary>Finite projection of original VM choices and exact Home action previews.
/// The binding cannot mint a VM identity, provider state or approval.</summary>
public sealed partial class AssistantsMiniComputerCuiBindings : ICuiBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private readonly CuiViewModel _values = new();
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Action<Action> _publish;
    private readonly Func<bool> _isCurrent;
    private readonly bool _hasOwner;
    private readonly string _unavailable;
    private bool _revoked, _busy, _pending, _unconfirmed;
    private string _status = "", _request = "", _error = "";
    private IReadOnlyList<VirtualMachineRow> _rows = [];
    internal AssistantMiniComputerImportPreview? OriginalImport { get; private set; }
    internal AssistantMiniComputerView? OriginalView { get; private set; }
    internal AssistantMiniComputerOperationPreview? OriginalPreview { get; private set; }
    private AssistantMiniComputerOperationResult? _result;
    private Task<AssistantMiniComputerOperationResult>? _originalOperation;
    private AssistantMiniComputerOperationPreview? _originalOperationPreview;
    internal void RetainOriginalOperation(AssistantMiniComputerOperationPreview preview, Task<AssistantMiniComputerOperationResult> actual)
    {
        if (!ReferenceEquals(OriginalPreview, preview) || _originalOperation is not null && !ReferenceEquals(_originalOperation, actual))
            throw new InvalidOperationException("Retain the SAME accepted VM preview and actual execution Task.");
        _originalOperationPreview = preview; _originalOperation = actual;
    }
    internal bool ObserveOriginalSettlement(AssistantMiniComputerOperationPreview preview, Task<AssistantMiniComputerOperationResult> actual)
    {
        if (!ReferenceEquals(preview, _originalOperationPreview) || !ReferenceEquals(preview, OriginalPreview) ||
            !ReferenceEquals(actual, _originalOperation) || !actual.IsCompletedSuccessfully) return false;
        // Custody observation survives presentation revocation. This mutates no
        // control, grants no operation and cannot waive a failed source/publication.
        _result = actual.Result; _pending = false; return true;
    }
    internal bool HasUnconfirmedChanges => HasIdentitySetupChanges || _pending || _unconfirmed || OriginalPreview is not null && _result is null;
    private bool Current => !_revoked && _isCurrent();
    public AssistantsMiniComputerCuiBindings(bool hasOwner, string unavailable,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Action<Action> publish, Func<bool> isCurrent)
    {
        _hasOwner = hasOwner; _unavailable = unavailable; _dispatch = dispatch; _publish = publish; _isCurrent = isCurrent;
        _values.PropertyChanged += (_, args) => { if (Current) PropertyChanged?.Invoke(this, args); };
        Refresh();
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Revoke() => _revoked = true;
    internal void SetView(AssistantMiniComputerView value)
    { OriginalView = value; _rows = value.Choices.Select(choice => new VirtualMachineRow(choice)).ToArray(); _status = value.Reason; _error = ""; Refresh(); }
    internal void SetImport(AssistantMiniComputerImportPreview preview) { OriginalImport = preview; Refresh(); }
    internal void SetUnavailable(string reason) { OriginalView = null; _rows = []; _status = reason; Refresh(); }
    internal void SetBusy(bool value) { _busy = value; Refresh(); }
    internal void SetError(string reason) { _error = reason; Refresh(); }
    internal void SetPreview(AssistantMiniComputerOperationPreparation value)
    { OriginalPreview = value.Preview; _originalOperation = null; _originalOperationPreview = null; _result = null; _request = ""; _status = value.Reason; Refresh(); }
    internal void BeginOperation() { _pending = true; _status = "Waiting for the separate Home approval for this exact VM action."; Refresh(); }
    internal void Observe(AssistantMiniComputerPendingObservation value) { _request = value.RequestId ?? ""; _status = value.Reason; Refresh(); }
    internal void SetResult(AssistantMiniComputerOperationResult value)
    { _pending = false; _result = value; _status = value.Reason; Refresh(); }
    internal void MarkUnconfirmed(string reason) { _pending = false; _unconfirmed = true; _error = reason; Refresh(); }
    internal void ClearPreview()
    {
        if (_pending || _unconfirmed) throw new InvalidOperationException("The original VM action must settle before changing its preview.");
        OriginalPreview = null; _originalOperation = null; _originalOperationPreview = null; _result = null; _request = ""; Refresh();
    }
    internal bool IsOriginalRow(VirtualMachineRow row) => Current && _rows.Any(value => ReferenceEquals(value, row)) &&
        OriginalView?.IsOriginalChoice(row.Original) == true;
    public bool TryGetValue(string path, out object? value)
    { if (Current) return _values.TryGetValue(path, out value); value = path.StartsWith("Can", StringComparison.Ordinal) || path.StartsWith("Has", StringComparison.Ordinal) ? false : null; return true; }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not VirtualMachineRow row || !IsOriginalRow(row)) return false;
        value = path switch { "Id" => row.Original.VirtualMachineId, "Name" => row.Original.Name,
            "Guest" => row.Original.Guest, "Provider" => row.Original.ProviderId.ToString("D"),
            "SavedState" => "Last saved state: " + row.Original.LastObservedState,
            "CanChoose" => IsActionAvailable("assistants.mini.choose") == true, _ => null };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public bool? IsActionAvailable(string command) => Current && (command switch
    {
        "assistants.mini.identity.prepare" => _hasOwner && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.identity.request" => CanRequestIdentitySetup,
        "assistants.mini.identity.discard" => CanDiscardIdentitySetup,
        "assistants.mini.import.inspect" => _hasOwner && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.import.request" => OriginalImport?.CanRequest == true && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.import.refresh" => OriginalImport is not null && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.import.complete" => OriginalImport?.CanComplete == true && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.import.retry" => OriginalImport?.CanRetryAudit == true && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.back" => !_busy && !HasUnconfirmedChanges,
        "assistants.mini.refresh" => _hasOwner && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.choose" => OriginalView?.CanBrowse == true && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.inspect" or "assistants.mini.start" or "assistants.mini.pause" or
        "assistants.mini.resume" or "assistants.mini.save" or "assistants.mini.shutdown" =>
            OriginalView?.Selected is not null && !_busy && !HasUnconfirmedChanges,
        "assistants.mini.confirm" => OriginalPreview is not null && _result is null && !_busy && !_pending && !_unconfirmed,
        "assistants.mini.discard" => OriginalPreview is not null && !_busy && !_pending && !_unconfirmed,
        _ => false
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) =>
        IsActionAvailable(command) == true && (command != "assistants.mini.choose" || parameter is VirtualMachineRow row && IsOriginalRow(row))
            ? _dispatch(command, parameter, token)
            : ValueTask.FromException(new InvalidOperationException("This VM action is unavailable for the current original view."));
    private void Set(string key, object? value) => _publish(() => { if (Current) _values.Set(key, value); });
    private void Refresh()
    {
        RefreshIdentitySetup();
        Set("MiniImportStatus", OriginalImport?.Reason ?? "Review the configured catalogue in Home before its VM records are available.");
        Set("MiniImportRequest", OriginalImport?.RequestId ?? "");
        Set("HasMiniImportRequest", OriginalImport?.RequestId is not null);
        foreach (var item in new[] { ("Inspect", "inspect"), ("Request", "request"), ("Refresh", "refresh"), ("Complete", "complete"), ("Retry", "retry") })
            Set("CanMiniImport" + item.Item1, IsActionAvailable("assistants.mini.import." + item.Item2) == true);
        foreach (var row in _rows) _publish(() => { if (Current && IsOriginalRow(row)) row.RefreshAvailability(); });
        Set("MiniHeading", OriginalView is null ? "Mini Computer" : OriginalView.Definition.Configuration.Name + " · Mini Computer");
        Set("MiniStatus", _hasOwner ? _status : _unavailable); Set("MiniError", _error); Set("HasMiniError", _error.Length != 0);
        Set("MiniMachines", _rows); Set("HasMiniMachines", _rows.Count != 0);
        Set("MiniSelected", OriginalView?.Selected?.Name ?? "No permitted VM is selected.");
        Set("MiniPreview", OriginalPreview is null ? "" : OriginalPreview.Action + " · " + OriginalPreview.VirtualMachineName);
        Set("HasMiniPreview", OriginalPreview is not null); Set("MiniRequest", _request); Set("HasMiniRequest", _request.Length != 0);
        Set("MiniObservedState", _result?.State ?? "Not observed in this view");
        Set("MiniObservedAt", _result?.ObservedAt?.ToString("u") ?? "");
        Set("MiniOutcome", _result?.IsPending == true ? "Provider transition pending" : _result?.Reason ?? "");
        foreach (var pair in new[] { ("Back", "back"), ("Refresh", "refresh"), ("Inspect", "inspect"), ("Start", "start"),
            ("Pause", "pause"), ("Resume", "resume"), ("Save", "save"), ("Shutdown", "shutdown"), ("Confirm", "confirm"), ("Discard", "discard") })
            Set("CanMini" + pair.Item1, IsActionAvailable("assistants.mini." + pair.Item2) == true);
    }
    public sealed class VirtualMachineRow(AssistantMiniComputerChoice original) : INotifyPropertyChanged
    {
        public AssistantMiniComputerChoice Original { get; } = original;
        public event PropertyChangedEventHandler? PropertyChanged;
        internal void RefreshAvailability() => PropertyChanged?.Invoke(this, new("CanChoose"));
    }
}
