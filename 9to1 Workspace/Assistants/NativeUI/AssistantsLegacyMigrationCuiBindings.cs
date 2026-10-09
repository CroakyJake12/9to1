using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.Migration;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Read-only observations and an explicit local review; never an import or Den owner.</summary>
public sealed partial class AssistantsLegacyMigrationCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly CuiViewModel _values = new();
    private readonly Func<string, object?, CancellationToken, ValueTask> _dispatch;
    private readonly Action<Action> _publish;
    private readonly Func<bool> _isCurrent;
    private readonly bool _hasOwner;
    private readonly string _unavailableReason;
    private IReadOnlyList<LegacyRow> _rows = [];
    private string? _nextCursor;
    private LegacyAgentMigrationDraft? _draft;
    private LegacyAgentMigrationResult? _result;
    private LegacyAgentRecoveryPreview? _recovery;
    private IReadOnlyList<LegacyAgentLink> _links = [];
    private string? _nextLinks;
    private string _linkKind = "runs";
    private string _status = "";
    private string _error = "";
    private bool _busy;
    private bool _revoked;
    private long _generation;

    public AssistantsLegacyMigrationCuiBindings(bool hasOriginalOwner, string unavailableReason,
        Func<string, object?, CancellationToken, ValueTask> dispatch, Action<Action> publish, Func<bool> isCurrent)
    {
        _hasOwner = hasOriginalOwner; _unavailableReason = unavailableReason;
        _dispatch = dispatch; _publish = publish; _isCurrent = isCurrent;
        _values.PropertyChanged += (_, args) => { if (Current) PropertyChanged?.Invoke(this, args); };
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Current => !_revoked && _isCurrent();
    internal LegacyAgentMigrationDraft? OriginalDraft => _draft;
    internal LegacyAgentMigrationResult? OriginalResult => _result;
    internal HavenOS.Apps.Assistants.Contracts.AssistantDefinitionSnapshot? OriginalClassifiedDefinition =>
        _result?.Definition ?? _draft?.Preview.ClassifiedDefinition;
    internal LegacyAgentRecoveryPreview? OriginalRecovery => _recovery;
    internal long ReviewGeneration => _generation;
    internal bool HasUnconfirmedChanges => _draft?.HasUnconfirmedChanges == true || _importPreview?.State is LegacyAgentImportState.AuditPending or LegacyAgentImportState.OutcomeUnconfirmed;
    internal string? NextCursor => _nextCursor;
    internal string? NextLinksCursor => _nextLinks;
    internal string LinkKind => _linkKind;
    public void Revoke() => _revoked = true;

    internal bool IsReviewCurrent(LegacyAgentMigrationPreview original, long generation) => Current &&
        _generation == generation && ReferenceEquals(_draft?.Preview, original);
    internal bool IsRecoveryCurrent(LegacyAgentRecoveryPreview original, long generation) => Current &&
        _generation == generation && ReferenceEquals(_recovery, original);
    internal LegacyAgentMigrationItem DemandListItem(object? parameter)
    {
        if (parameter is not LegacyRow row || !_rows.Any(actual => ReferenceEquals(actual, row)))
            throw new InvalidOperationException("Choose a definition from this original review list.");
        return row.Original;
    }
    internal void SetBusy(bool value) { _busy = value; Refresh(); }
    internal void SetStatus(string message) { _status = message; Refresh(); }
    internal void SetError(string message) { _error = message; Refresh(); }
    internal void SetPage(LegacyAgentMigrationPage page)
    {
        _generation = checked(_generation + 1); _draft = null; _result = null; _recovery = null;
        _rows = page.Items.Select(item => new LegacyRow(item)).ToArray(); _nextCursor = page.NextCursor;
        _links = []; _nextLinks = null; _status = "Your existing definitions stay preserved until you confirm a choice.";
        _error = ""; Refresh();
    }
    internal void SetPreview(LegacyAgentMigrationPreview actual)
    {
        _generation = checked(_generation + 1); _draft = new(actual); _result = null; _recovery = null;
        _links = []; _nextLinks = null; _linkKind = "runs"; _error = "";
        _status = actual.PendingClassification is null
            ? "Choose the type that fits this identity. Your source and history will remain preserved."
            : "This review has a recorded choice. Continue the same migration to finish it.";
        Refresh();
    }
    internal void ChooseKind(HavenOS.Apps.Assistants.Contracts.ConfiguredIdentityKind kind)
    { (_draft ?? throw new InvalidOperationException("Open a definition to review.")).ChooseKind(kind); Refresh(); }
    internal void SetLinks(string kind, LegacyAgentLinkPage page)
    { _linkKind = kind; _links = page.Items; _nextLinks = page.NextCursor; Refresh(); }
    internal void SetResult(LegacyAgentMigrationDraft.Submission submitted, LegacyAgentMigrationResult receipt)
    {
        (_draft ?? throw new InvalidOperationException("The original review is unavailable.")).Acknowledge(submitted, receipt);
        _result = receipt; _status = $"{receipt.Definition.Configuration.Name} is now a configured {receipt.Definition.Kind}. Its original source remains preserved.";
        _error = ""; Refresh();
    }
    internal void SetRecovery(LegacyAgentRecoveryPreview actual)
    { _generation = checked(_generation + 1); _recovery = actual; _error = ""; Refresh(); }
    internal void SetUndoResult(LegacyAgentRecoveryResult actual)
    {
        if (_recovery is null || actual.Identity != _recovery.Identity || !actual.OriginalSourceRetained || !actual.ClassifiedDefinitionRemoved)
            throw new InvalidOperationException("The recovery receipt does not acknowledge this original recovery review.");
        _generation = checked(_generation + 1); _recovery = null; _draft = null; _result = null;
        _status = "The conversion was undone. Your original definition and preserved history are available for review.";
        _error = ""; Refresh();
    }
    internal void CancelReview()
    {
        _generation = checked(_generation + 1); _draft = null; _result = null; _recovery = null;
        _links = []; _nextLinks = null; _error = "";
        _status = "Review closed. No new migration or recovery was requested."; Refresh();
    }

    public bool TryGetValue(string path, out object? value)
    {
        if (Current) return _values.TryGetValue(path, out value);
        value = path.StartsWith("Can", StringComparison.Ordinal) || path.StartsWith("Has", StringComparison.Ordinal) ||
            path.StartsWith("Show", StringComparison.Ordinal) ? false : null; return true;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (!Current || !_hasOwner || _busy || value is not string text || _draft is null || _result is not null) return false;
        var changed = false;
        _publish(() => { if (Current && !_busy) { changed = _draft.TrySetText(path, text); if (changed) Refresh(); } });
        return changed;
    }
    public bool? IsActionAvailable(string command) => Current && !_busy && IsImportContentActionAdmitted(command) && (command switch
    {
        "assistants.legacy.back" => !HasUnconfirmedChanges,
        "assistants.legacy.cancel" => _draft is not null || _recovery is not null,
        "assistants.legacy.list" or "assistants.legacy.preview" => _hasOwner && !HasUnconfirmedChanges,
        "assistants.legacy.next" => _hasOwner && !HasUnconfirmedChanges && _nextCursor is not null,
        "assistants.legacy.kind.assistant" or "assistants.legacy.kind.specialist" => _hasOwner &&
            _draft is { IsContinuation: false, Preview.CanClassify: true } && _result is null,
        "assistants.legacy.confirm" => _hasOwner && _draft?.CanConfirm == true && _result is null,
        "assistants.legacy.links.runs" or "assistants.legacy.links.memories" or "assistants.legacy.links.references" or
            "assistants.legacy.links.conversations" => _hasOwner && _draft is not null,
        "assistants.legacy.links.next" => _hasOwner && _draft is not null && _nextLinks is not null,
        "assistants.legacy.recovery" => _hasOwner && OriginalClassifiedDefinition is not null && !HasUnconfirmedChanges,
        "assistants.legacy.undo" => _hasOwner && _recovery?.CanUndo == true && !HasUnconfirmedChanges,
        "assistants.legacy.result.open" => OriginalClassifiedDefinition?.Kind == HavenOS.Apps.Assistants.Contracts.ConfiguredIdentityKind.Assistant && !HasUnconfirmedChanges,
        _ => IsImportActionAvailable(command)
    });
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) =>
        IsActionAvailable(command) == true &&
        (command != "assistants.legacy.preview" || parameter is LegacyRow row && _rows.Any(actual => ReferenceEquals(actual, row)))
            ? _dispatch(command, parameter, token) :
            ValueTask.FromException(new InvalidOperationException("This migration action is unavailable in the current review."));

    private void Set(string path, object? value) => _publish(() => { if (Current) _values.Set(path, value); });
    private void Refresh()
    {
        RefreshImportBindings();
        var preview = _draft?.Preview; var configuration = _draft?.Configuration;
        Set("MigrationTitle", "Review existing definitions"); Set("MigrationStatus", _hasOwner ? _status : _unavailableReason);
        Set("MigrationError", _error); Set("HasMigrationError", _error.Length != 0);
        Set("MigrationRows", _rows); Set("HasMigrationRows", _rows.Count != 0);
        Set("ShowMigrationList", _draft is null && _recovery is null && (!_supportsImport || _importPreview?.CanBrowse == true)); Set("ShowMigrationReview", _draft is not null && _result is null && _recovery is null);
        Set("ShowMigrationResult", _result is not null && _recovery is null); Set("ShowMigrationRecovery", _recovery is not null);
        Set("MigrationName", configuration?.Name ?? ""); Set("MigrationDescription", configuration?.Description ?? "");
        Set("MigrationPurpose", configuration?.Purpose ?? ""); Set("MigrationRole", configuration?.Role ?? "");
        Set("MigrationInstructions", configuration?.Instructions ?? ""); Set("CanEditMigration", !_busy && preview?.CanClassify == true && _draft?.IsContinuation == false);
        Set("HasClassifiedDestination", OriginalClassifiedDefinition is not null);
        Set("MigrationKind", OriginalClassifiedDefinition?.Kind.ToString() ?? _draft?.Kind?.ToString() ?? "Choose a type");
        Set("MigrationState", preview?.State.ToString() ?? ""); Set("HasMigrationNotes", preview?.CompatibilityNotes.Count > 0);
        PublishMigrationRepeatRows(preview);
        Set("MigrationPreservation", preview is null ? "" : (preview.Preserved.RunHistoryAvailable ? $"{preview.Preserved.Runs} run records" : "Run history is unavailable") + " · " + (preview.Preserved.MemoryMetadataAvailable ? $"{preview.Preserved.ScopedMemories} memory records" : "Memory metadata is unavailable") + ". Original source fields are preserved.");
        Set("MigrationModel", configuration is null ? "" : string.IsNullOrWhiteSpace(configuration.Model.ModelId) ? "No model preference saved" : $"Saved model preference: {configuration.Model.ProviderId} {configuration.Model.ModelId}");
        Set("MigrationResources", configuration is null ? "" : $"Saved preferences: {configuration.ToolIds.Count} tools, {configuration.KnowledgeResourceIds.Count} resources, {configuration.ProjectReferences.Count} projects. Effective access is checked separately.");
        Set("HasMigrationLinks", _links.Count != 0); Set("MigrationLinkHeading", "Preserved " + _linkKind);
        Set("MigrationResultName", _result?.Definition.Configuration.Name ?? ""); Set("MigrationResultKind", _result?.Definition.Kind.ToString() ?? "");
        Set("MigrationRecoveryName", _recovery?.Name ?? ""); Set("MigrationRecoveryReason", _recovery?.Reason ?? "");
        Set("MigrationConfirmationLabel", _draft?.IsContinuation == true ? "Continue recorded migration" : "Confirm conversion");
        foreach (var pair in new[] { ("CanMigrationBack", "assistants.legacy.back"), ("CanMigrationCancel", "assistants.legacy.cancel"),
            ("CanMigrationList", "assistants.legacy.list"), ("CanMigrationNext", "assistants.legacy.next"), ("CanMigrationChooseAssistant", "assistants.legacy.kind.assistant"),
            ("CanMigrationChooseSpecialist", "assistants.legacy.kind.specialist"), ("CanMigrationConfirm", "assistants.legacy.confirm"), ("CanMigrationLinks", "assistants.legacy.links.runs"),
            ("CanMigrationNextLinks", "assistants.legacy.links.next"), ("CanMigrationRecovery", "assistants.legacy.recovery"), ("CanMigrationUndo", "assistants.legacy.undo"),
            ("CanMigrationOpenResult", "assistants.legacy.result.open") }) Set(pair.Item1, IsActionAvailable(pair.Item2) == true);
    }

    public sealed class LegacyRow
    {
        internal LegacyRow(LegacyAgentMigrationItem original) => Original = original;
        internal LegacyAgentMigrationItem Original { get; }
        public Guid Id => Original.LegacyAgentId;
        public string Name => Original.Name;
        public string Description => Original.Description;
        public string State => Original.State.ToString();
    }
    public sealed record NoteRow(int Id, string Text);
    public sealed record LinkRow(int Id, string Kind, string Title, string Related);
}
