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

/// <summary>Actual native UI owner must show a destructive impact preview and
/// collect user approval; permission is checked separately by the canonical
/// Home admission owner at dispatch and commit.</summary>
public interface ICardCuiDeleteReviewer
{
    Task<bool> ApproveAsync(CardDeletePreview preview, CancellationToken cancellationToken);
}

/// <summary>Creates both real structured sides with the existing shared editor.
/// A cancelled draft must not create an empty card or a saved artifact.</summary>
public interface ICardCuiCardCreator
{
    Task<CardDraft?> CreateAsync(Guid setId, CancellationToken cancellationToken);
}

/// <summary>The Home preferences owner stores authorised per-user Cards settings.
/// This adapter MUST bind settings to the current authenticated principal and
/// canonical SetID rather than accepting an arbitrary caller-supplied identity.</summary>
public interface ICardCuiPreferencesOwner
{
    Task<CardViewPreferences?> ReadCurrentAsync(Guid setId, CancellationToken cancellationToken);
    Task<CardPreferencesWriteReceipt> SaveCurrentAsync(Guid setId,
        CardViewPreferences preferences, CancellationToken cancellationToken);
}

public sealed record CardPreferencesWriteReceipt(bool Committed, string Code);

/// <summary>
/// CUI action and binding context. The scene always reflects an observed saved
/// set and refuses to announce a write until the canonical owner acknowledges.
/// Native/app catalogue setup and real CUI rendering remain separate host work.
/// </summary>
public sealed class CardCuiWorkspace(
    ICardCuiSetSource sets,
    CardMutationGateway mutations,
    ICardCuiRichEditor? richEditor = null,
    ICardCuiReviewOwner? reviews = null,
    ICardCuiPreferencesOwner? preferences = null,
    ICardCuiCardCreator? creator = null,
    ICardCuiDeleteReviewer? deletion = null)
    : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private CardSet? _set;
    private readonly CardNavigationSession _navigation = new();
    private bool _busy;
    private string _status = "Open an authorised Cards set";

    public event PropertyChangedEventHandler? PropertyChanged;
    public string SetTitle => _set?.Title ?? "Cards";
    public string Status => _status;
    public string SearchText => _navigation.SearchText;
    public string ModeLabel => _navigation.Mode == CardInteractionMode.Edit ? "Switch to view" : "Switch to edit";
    public string OrientationLabel => _navigation.Preferences.Navigation == CardNavigationDirection.Vertical
        ? "Navigation: vertical" : "Navigation: horizontal";
    public bool CanChangeOrientation => !_busy && _set is not null;
    public string GroupModeLabel => _navigation.Preferences.Grouping switch
    {
        CardGroupingKind.Subject => "Grouping: subject",
        CardGroupingKind.Topic => "Grouping: topic",
        _ => "Grouping: set",
    };
    public IReadOnlyList<string> GroupNames => _navigation.GroupNames;
    public int SelectedGroupIndex => GroupNames.ToList().FindIndex(name =>
        string.Equals(name, _navigation.SelectedGroup, StringComparison.Ordinal));
    public bool CanGroup => !_busy && _set is not null;
    public bool CanSelectGroup => CanGroup && GroupNames.Count > 0;
    public string SideLabel => _navigation.FrontVisible ? "Front" : "Back";
    public bool CanOpen => !_busy;
    public bool CanChangeMode => !_busy && _set is not null;
    public bool CanFlip => !_busy && _navigation.Current is not null;
    public bool CanEdit => !_busy && _navigation.Mode == CardInteractionMode.Edit
        && richEditor is not null && _navigation.Current is not null;
    public bool CanCreate => !_busy && _navigation.Mode == CardInteractionMode.Edit
        && _set is not null && creator is not null;
    public bool CanDuplicate => !_busy && _navigation.Mode == CardInteractionMode.Edit
        && _navigation.Current is not null;
    public bool CanDelete => !_busy && _navigation.Mode == CardInteractionMode.Edit
        && _navigation.Current is not null && deletion is not null;
    public bool CanRate => !_busy && reviews is not null && _navigation.Current is not null;
    public bool CanPrevious => !_busy && _navigation.Index > 0;
    public bool CanNext => !_busy && _navigation.Index >= 0
        && _navigation.Index < _navigation.Count - 1;
    public Guid? SelectedCardId => _navigation.SelectedCardId;
    public string PositionLabel => _navigation.Index < 0
        ? "No cards" : $"{_navigation.Index + 1} / {_navigation.Count}";
    public string PreviousPreview => _set is null ? ""
        : _navigation.Window().Previous?.Front.SearchText ?? "";
    public string NextPreview => _set is null ? ""
        : _navigation.Window().Next?.Front.SearchText ?? "";
    public string ContentHint => _navigation.Current is null ? "No card selected"
        : "Structured rich content is owned by the shared productivity renderer.";
    private CardEntry? Selected => _navigation.Current;

    /// <summary>Native keyboard/touch input must call this once the host binds its
    /// original input stream. This is deterministic presentation, not a write.</summary>
    public bool Navigate(CardNavigationInput input)
    {
        if (_busy || _set is null) return false;
        bool changed = _navigation.Navigate(input);
        if (changed) Changed();
        return changed;
    }

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
            "ModeLabel" => ModeLabel, "OrientationLabel" => OrientationLabel,
            "CanChangeOrientation" => CanChangeOrientation, "GroupModeLabel" => GroupModeLabel,
            "GroupNames" => GroupNames, "SelectedGroupIndex" => SelectedGroupIndex,
            "CanGroup" => CanGroup, "CanSelectGroup" => CanSelectGroup,
            "SideLabel" => SideLabel,
            "PreviousPreview" => PreviousPreview, "NextPreview" => NextPreview,
            "ContentHint" => ContentHint, "PositionLabel" => PositionLabel,
            "CanOpen" => CanOpen, "CanChangeMode" => CanChangeMode, "CanFlip" => CanFlip,
            "CanEdit" => CanEdit, "CanCreate" => CanCreate,
            "CanDuplicate" => CanDuplicate, "CanDelete" => CanDelete,
            "CanRate" => CanRate,
            "CanPrevious" => CanPrevious,
            "CanNext" => CanNext,
            _ => null,
        };
        return path is "SetTitle" or "Status" or "SearchText" or "ModeLabel"
            or "OrientationLabel" or "CanChangeOrientation"
            or "GroupModeLabel" or "GroupNames" or "SelectedGroupIndex"
            or "CanGroup" or "CanSelectGroup" or "SideLabel"
            or "PreviousPreview" or "NextPreview" or "ContentHint" or "PositionLabel"
            or "CanOpen" or "CanChangeMode" or "CanFlip" or "CanEdit" or "CanRate"
            or "CanPrevious" or "CanNext" or "CanCreate" or "CanDuplicate" or "CanDelete";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (_busy) return false;
        if (path == "SelectedGroupIndex" && CanSelectGroup && value is int index
            && index >= 0 && index < GroupNames.Count)
        {
            if (!_navigation.SelectGroup(GroupNames[index])) return false;
            Changed();
            return true;
        }
        if (path != "SearchText" || value is not string text || text.Length > 256)
            return false;
        _navigation.SetSearch(text);
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
        "9to1.Cards.ToggleOrientation" => CanChangeOrientation,
        "9to1.Cards.CycleGrouping" => CanGroup,
        "9to1.Cards.EditSide" => CanEdit,
        "9to1.Cards.Add" => CanCreate,
        "9to1.Cards.Duplicate" => CanDuplicate,
        "9to1.Cards.Delete" => CanDelete,
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
                        if (!_navigation.Move(command.EndsWith("Next", StringComparison.Ordinal) ? 1 : -1))
                            throw new InvalidOperationException("The Cards focus changed before navigation.");
                        _status = PositionLabel;
                        break;
                    case "9to1.Cards.Flip":
                        if (!_navigation.Flip()) throw new InvalidOperationException("No card is focused.");
                        break;
                    case "9to1.Cards.CycleGrouping":
                        _navigation.SetGrouping(_navigation.Preferences.Grouping switch
                        {
                            CardGroupingKind.Set => CardGroupingKind.Subject,
                            CardGroupingKind.Subject => CardGroupingKind.Topic,
                            _ => CardGroupingKind.Set,
                        });
                        _status = GroupModeLabel + " (session only; settings persistence not connected)";
                        break;
                    case "9to1.Cards.ToggleOrientation":
                        await ToggleOrientationAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case "9to1.Cards.ToggleMode":
                        _navigation.SetMode(_navigation.Mode == CardInteractionMode.Edit
                            ? CardInteractionMode.View : CardInteractionMode.Edit);
                        _status = _navigation.Mode == CardInteractionMode.Edit
                            ? "Editing enabled; changes require Home approval"
                            : "View mode; content editing disabled";
                        break;
                    case "9to1.Cards.EditSide":
                        await EditAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case "9to1.Cards.Add":
                        await AddAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case "9to1.Cards.Duplicate":
                        await DuplicateAsync(cancellationToken).ConfigureAwait(false);
                        break;
                    case "9to1.Cards.Delete":
                        await DeleteAsync(cancellationToken).ConfigureAwait(false);
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
        CardViewPreferences? savedPreferences = preferences is null ? null
            : await preferences.ReadCurrentAsync(loaded.SetId, token).ConfigureAwait(false);
        if (savedPreferences is not null)
            _navigation.ApplyPreferences(savedPreferences);
        _set = loaded;
        _navigation.Load(loaded);
        _status = $"Opened {loaded.Title} at revision {loaded.Revision}";
    }

    private async Task ToggleOrientationAsync(CancellationToken token)
    {
        CardSet set = _set ?? throw new InvalidOperationException("Open the set first.");
        CardNavigationDirection next = _navigation.Preferences.Navigation == CardNavigationDirection.Vertical
            ? CardNavigationDirection.Horizontal : CardNavigationDirection.Vertical;
        CardViewPreferences proposed = _navigation.Preferences with { Navigation = next };
        if (preferences is null)
        {
            _navigation.ApplyPreferences(proposed);
            _status = "Orientation changed for this session; persistent settings unavailable";
            return;
        }

        CardPreferencesWriteReceipt receipt = await preferences.SaveCurrentAsync(set.SetId,
            proposed, token).ConfigureAwait(false);
        if (!receipt.Committed)
        {
            _status = $"Orientation not changed: {receipt.Code}";
            return;
        }
        // A signed store receipt reports the exact requested settings as committed;
        // no physical CUI orientation claim is made until the native host uses them.
        _navigation.ApplyPreferences(proposed);
        _status = "Orientation setting saved";
    }

    private async Task AddAsync(CancellationToken token)
    {
        CardSet set = _set ?? throw new InvalidOperationException("Open the set first.");
        CardDraft? draft = await creator!.CreateAsync(set.SetId, token).ConfigureAwait(false);
        if (draft is null)
        {
            _status = "New card cancelled; nothing saved";
            return;
        }
        var request = new CardMutation.AddCards(Guid.NewGuid().ToString("D"), set.SetId,
            set.Revision, CardInteractionMode.Edit, [draft]);
        CardMutationOutcome receipt = await mutations.ExecuteAsync(request, token).ConfigureAwait(false);
        await ReloadAfterCommitAsync(set, receipt, "Card created", token).ConfigureAwait(false);
    }

    private async Task DuplicateAsync(CancellationToken token)
    {
        CardSet set = _set ?? throw new InvalidOperationException("Open the set first.");
        CardEntry card = Selected ?? throw new InvalidOperationException("Select a card first.");
        var request = new CardMutation.Duplicate(Guid.NewGuid().ToString("D"), set.SetId,
            set.Revision, CardInteractionMode.Edit, card.CardId);
        CardMutationOutcome receipt = await mutations.ExecuteAsync(request, token).ConfigureAwait(false);
        await ReloadAfterCommitAsync(set, receipt, "Card duplicated", token).ConfigureAwait(false);
    }

    private async Task DeleteAsync(CancellationToken token)
    {
        CardSet set = _set ?? throw new InvalidOperationException("Open the set first.");
        CardEntry card = Selected ?? throw new InvalidOperationException("Select a card first.");
        CardDeletePreview preview = CardSetOperations.PreviewDelete(set, [card.CardId]);
        bool approved = await deletion!.ApproveAsync(preview, token).ConfigureAwait(false);
        if (!approved)
        {
            _status = "Deletion cancelled; no changes saved";
            return;
        }
        // A UI confirmation alone grants nothing. Home authorisation and a
        // compare-and-swap commit still run in the canonical mutation gateway.
        var request = new CardMutation.Delete(Guid.NewGuid().ToString("D"), set.SetId,
            preview.ExpectedSetRevision, CardInteractionMode.Edit, preview.CardIds);
        CardMutationOutcome receipt = await mutations.ExecuteAsync(request, token).ConfigureAwait(false);
        await ReloadAfterCommitAsync(set, receipt, "Card moved to recoverable deletion",
            token).ConfigureAwait(false);
    }

    private async Task ReloadAfterCommitAsync(CardSet original, CardMutationOutcome receipt,
        string successLabel, CancellationToken token)
    {
        if (!receipt.Succeeded)
        {
            _status = $"No changes saved: {receipt.Code}";
            return;
        }
        CardSet? committed = await sets.OpenCurrentAsync(token).ConfigureAwait(false);
        if (committed is null || committed.SetId != original.SetId
            || committed.Revision != receipt.CommittedRevision)
        {
            _status = "Save acknowledged; reopen the canonical revision to refresh";
            return;
        }
        CardSetOperations.Validate(committed);
        _set = committed;
        _navigation.Load(committed);
        _status = $"{successLabel}; saved revision {committed.Revision}";
    }

    private async Task EditAsync(CancellationToken token)
    {
        CardSet set = _set ?? throw new InvalidOperationException("Open the set first.");
        CardEntry card = Selected ?? throw new InvalidOperationException("Select a card first.");
        CardSide current = _navigation.FrontVisible ? card.Front : card.Back;
        CardSide? replacement = await richEditor!.EditAsync(set.SetId, card.CardId,
            _navigation.FrontVisible, current, token).ConfigureAwait(false);
        if (replacement is null) { _status = "Editing cancelled; no changes saved"; return; }
        var request = new CardMutation.BulkEdit(Guid.NewGuid().ToString("D"), set.SetId,
            set.Revision, CardInteractionMode.Edit,
            [new(card.CardId, card.Revision, _navigation.FrontVisible ? replacement : null,
                _navigation.FrontVisible ? null : replacement)]);
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
        _navigation.Load(saved);
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

    private void Changed() => PropertyChanged?.Invoke(this,
        new PropertyChangedEventArgs(string.Empty));
}
