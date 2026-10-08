using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Cards.CUI;

/// <summary>The host's canonical Files/permission owner supplies an authorised, current set.
/// Returning a set here does not mean that it has been modified or saved.</summary>
public interface ICardCuiSetSource
{
    Task<CardSet?> OpenCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Use the maintained Shared Productivity Engine. No plain-text fallback
/// may silently overwrite graphs, media, ink, typography or unknown content.</summary>
public interface ICardCuiRichEditor
{
    Task<CardSide?> EditAsync(Guid setId, Guid cardId, bool front, CardSide current,
        CancellationToken cancellationToken);
}

/// <summary>Must persist personal review evidence through the current caller's
/// canonical review owner; Study linking must use its existing policy.</summary>
public interface ICardCuiReviewOwner
{
    Task<CardReviewWriteReceipt> RateCurrentAsync(Guid setId, Guid cardId,
        CardReviewRating rating, CancellationToken cancellationToken);
}

public sealed record CardReviewWriteReceipt(bool Committed, string Code);

/// <summary>
/// CUI action and binding context. The scene always reflects an observed saved
/// set and refuses to announce a write until the canonical owner acknowledges.
/// Native/app catalogue setup and real CUI rendering remain separate host work.
/// </summary>
public sealed class CardCuiWorkspace(
    ICardCuiSetSource sets,
    CardMutationGateway mutations,
    ICardCuiRichEditor? richEditor = null,
    ICardCuiReviewOwner? reviews = null)
    : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private CardSet? _set;
    private Guid? _selected;
    private bool _front = true;
    private bool _editing;
    private bool _busy;
    private string _search = string.Empty;
    private CardGroupingKind _grouping = CardGroupingKind.Set;
    private int _selectedGroupIndex;
    private string _status = "Open an authorised Cards set";

    public event PropertyChangedEventHandler? PropertyChanged;
    public string SetTitle => _set?.Title ?? "Cards";
    public string Status => _status;
    public string SearchText => _search;
    public string ModeLabel => _editing ? "Switch to view" : "Switch to edit";
    public string GroupModeLabel => _grouping switch
    {
        CardGroupingKind.Subject => "Grouping: subject",
        CardGroupingKind.Topic => "Grouping: topic",
        _ => "Grouping: set",
    };
    public IReadOnlyList<string> GroupNames => _set is null ? []
        : CardSetOperations.Group(_set, _grouping).Keys.Order(StringComparer.Ordinal).ToArray();
    public int SelectedGroupIndex => _selectedGroupIndex;
    public bool CanGroup => !_busy && _set is not null;
    public bool CanSelectGroup => CanGroup && GroupNames.Count > 0;
    public string SideLabel => _front ? "Front" : "Back";
    public bool CanOpen => !_busy;
    public bool CanChangeMode => !_busy && _set is not null;
    public bool CanFlip => !_busy && Selected is not null;
    public bool CanEdit => !_busy && _editing && richEditor is not null && Selected is not null;
    public bool CanRate => !_busy && reviews is not null && Selected is not null;
    public bool CanPrevious => !_busy && SelectedIndex > 0;
    public bool CanNext => !_busy && SelectedIndex >= 0 && SelectedIndex < Visible.Count - 1;
    public Guid? SelectedCardId => _selected;
    public string PositionLabel => SelectedIndex < 0 ? "No cards" : $"{SelectedIndex + 1} / {Visible.Count}";
    public string PreviousPreview => SelectedIndex > 0 ? Summarise(Visible[SelectedIndex - 1]) : "";
    public string NextPreview => CanNext ? Summarise(Visible[SelectedIndex + 1]) : "";
    public string ContentHint => Selected is null ? "No card selected"
        : "Structured rich content is owned by the shared productivity renderer.";

    private IReadOnlyList<CardEntry> Visible
    {
        get
        {
            if (_set is null || _selectedGroupIndex < 0
                || _selectedGroupIndex >= GroupNames.Count) return [];
            string selectedGroup = GroupNames[_selectedGroupIndex];
            IReadOnlyList<CardEntry> group = CardSetOperations.Group(_set, _grouping)[selectedGroup];
            return group.Where(card => _search.Length == 0
                || card.Front.SearchText.Contains(_search, StringComparison.OrdinalIgnoreCase)
                || card.Back.SearchText.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }
    private int SelectedIndex => _selected is { } id
        ? Visible.ToList().FindIndex(card => card.CardId == id) : -1;
    private CardEntry? Selected => SelectedIndex is var index && index >= 0 ? Visible[index] : null;
    private string Summarise(CardEntry card) => card.Front.SearchText;

    public static CuiDocument LoadDocument()
    {
        const string name = "HavenOS.Apps.Cards.CUI.UI.CardsWorkspace.cui";
        using var source = typeof(CardCuiWorkspace).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException("Canonical Cards CUI source is missing.");
        using var reader = new StreamReader(source);
        var parser = new CuiRichParser();
        CuiDocument document = parser.Parse(reader.ReadToEnd(), name);
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Invalid Cards CUI: " +
                string.Join("; ", parser.Diagnostics.Diagnostics));
        return document;
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "SetTitle" => SetTitle, "Status" => Status, "SearchText" => SearchText,
            "ModeLabel" => ModeLabel, "GroupModeLabel" => GroupModeLabel,
            "GroupNames" => GroupNames, "SelectedGroupIndex" => SelectedGroupIndex,
            "CanGroup" => CanGroup, "CanSelectGroup" => CanSelectGroup,
            "SideLabel" => SideLabel,
            "PreviousPreview" => PreviousPreview, "NextPreview" => NextPreview,
            "ContentHint" => ContentHint, "PositionLabel" => PositionLabel,
            "CanOpen" => CanOpen, "CanChangeMode" => CanChangeMode, "CanFlip" => CanFlip,
            "CanEdit" => CanEdit, "CanRate" => CanRate, "CanPrevious" => CanPrevious,
            "CanNext" => CanNext,
            _ => null,
        };
        return path is "SetTitle" or "Status" or "SearchText" or "ModeLabel"
            or "GroupModeLabel" or "GroupNames" or "SelectedGroupIndex"
            or "CanGroup" or "CanSelectGroup" or "SideLabel"
            or "PreviousPreview" or "NextPreview" or "ContentHint" or "PositionLabel"
            or "CanOpen" or "CanChangeMode" or "CanFlip" or "CanEdit" or "CanRate"
            or "CanPrevious" or "CanNext";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (_busy) return false;
        if (path == "SelectedGroupIndex" && CanSelectGroup && value is int index
            && index >= 0 && index < GroupNames.Count)
        {
            _selectedGroupIndex = index;
            ReconcileSelection();
            Changed();
            return true;
        }
        if (path != "SearchText" || value is not string text || text.Length > 256)
            return false;
        _search = text;
        ReconcileSelection();
        Changed();
        return true;
    }

    public bool? IsActionAvailable(string action) => !_busy && (action switch
    {
        "9to1.Cards.Open" => CanOpen,
        "9to1.Cards.Next" => CanNext,
        "9to1.Cards.Previous" => CanPrevious,
        "9to1.Cards.Flip" => CanFlip,
        "9to1.Cards.ToggleMode" => CanChangeMode,
        "9to1.Cards.CycleGrouping" => CanGroup,
        "9to1.Cards.EditSide" => CanEdit,
        "9to1.Cards.RateRed" or "9to1.Cards.RateAmber" or "9to1.Cards.RateGreen" => CanRate,
        _ => false,
    });

    public async ValueTask DispatchAsync(string command, object? parameter,
        CancellationToken cancellationToken = default)
    {
        if (parameter is not null || IsActionAvailable(command) != true)
            throw new InvalidOperationException("The requested Cards action is unavailable.");
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsActionAvailable(command) != true)
                throw new InvalidOperationException("Cards action is no longer available.");
            _busy = true;
            Changed();
            try
            {
                switch (command)
                {
                    case "9to1.Cards.Open":
                        await OpenAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case "9to1.Cards.Next":
                    case "9to1.Cards.Previous":
                        int index = SelectedIndex + (command.EndsWith("Next", StringComparison.Ordinal) ? 1 : -1);
                        _selected = Visible[index].CardId;
                        _front = true;
                        _status = PositionLabel;
                        break;
                    case "9to1.Cards.Flip":
                        _front = !_front;
                        break;
                    case "9to1.Cards.CycleGrouping":
                        _grouping = _grouping switch
                        {
                            CardGroupingKind.Set => CardGroupingKind.Subject,
                            CardGroupingKind.Subject => CardGroupingKind.Topic,
                            _ => CardGroupingKind.Set,
                        };
                        _selectedGroupIndex = 0;
                        ReconcileSelection();
                        _status = GroupModeLabel + " (session only; settings persistence not connected)";
                        break;
                    case "9to1.Cards.ToggleMode":
                        _editing = !_editing;
                        _status = _editing ? "Editing enabled; changes require Home approval"
                            : "View mode; content editing disabled";
                        break;
                    case "9to1.Cards.EditSide":
                        await EditAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case "9to1.Cards.RateRed":
                    case "9to1.Cards.RateAmber":
                    case "9to1.Cards.RateGreen":
                        await RateAsync(command, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            finally
            {
                _busy = false;
                Changed();
            }
        }
        finally { _operations.Release(); }
    }

    private async Task OpenAsync(CancellationToken token)
    {
        CardSet? loaded = await sets.OpenCurrentAsync(token).ConfigureAwait(false);
        if (loaded is null)
        {
            _status = "The current authorised Cards set is unavailable";
            return;
        }
        CardSetOperations.Validate(loaded);
        _set = loaded;
        _selectedGroupIndex = 0;
        ReconcileSelection();
        _status = $"Opened {loaded.Title} at revision {loaded.Revision}";
    }

    private async Task EditAsync(CancellationToken token)
    {
        CardSet set = _set ?? throw new InvalidOperationException("Open the set first.");
        CardEntry card = Selected ?? throw new InvalidOperationException("Select a card first.");
        CardSide current = _front ? card.Front : card.Back;
        CardSide? replacement = await richEditor!.EditAsync(set.SetId, card.CardId,
            _front, current, token).ConfigureAwait(false);
        if (replacement is null) { _status = "Editing cancelled; no changes saved"; return; }
        var request = new CardMutation.BulkEdit(Guid.NewGuid().ToString("D"), set.SetId,
            set.Revision, CardInteractionMode.Edit,
            [new(card.CardId, card.Revision, _front ? replacement : null, _front ? null : replacement)]);
        CardMutationOutcome result = await mutations.ExecuteAsync(request, token).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _status = $"No changes saved: {result.Code}";
            return;
        }
        // Never assume the rich editor's draft or an optimistic local successor
        // equals the canonical committed revision.
        CardSet? saved = await sets.OpenCurrentAsync(token).ConfigureAwait(false);
        if (saved is null || saved.SetId != set.SetId || saved.Revision != result.CommittedRevision)
        {
            _status = "Save acknowledged; reopen the canonical revision to refresh";
            return;
        }
        CardSetOperations.Validate(saved);
        _set = saved;
        _status = $"Saved revision {saved.Revision}";
    }

    private async Task RateAsync(string action, CancellationToken token)
    {
        CardEntry card = Selected ?? throw new InvalidOperationException("Select a card first.");
        CardReviewRating rating = action.EndsWith("Red", StringComparison.Ordinal)
            ? CardReviewRating.Red : action.EndsWith("Amber", StringComparison.Ordinal)
                ? CardReviewRating.Amber : CardReviewRating.Green;
        CardReviewWriteReceipt receipt = await reviews!.RateCurrentAsync(_set!.SetId,
            card.CardId, rating, token).ConfigureAwait(false);
        _status = receipt.Committed ? $"Review saved: {rating}" :
            $"Review not saved: {receipt.Code}";
    }

    private void ReconcileSelection()
    {
        IReadOnlyList<CardEntry> visible = Visible;
        if (visible.Count == 0) { _selected = null; return; }
        if (_selected is null || visible.All(card => card.CardId != _selected.Value))
            _selected = visible[0].CardId;
    }

    private void Changed() => PropertyChanged?.Invoke(this,
        new PropertyChangedEventArgs(string.Empty));
}
