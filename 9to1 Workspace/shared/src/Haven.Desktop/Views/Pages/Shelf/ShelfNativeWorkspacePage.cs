using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Core.Shelf;
using Haven.Application.Shelf;
using System.Globalization;
using Haven.Infrastructure;
namespace Haven.Desktop.Views.Pages.Shelf;

/// <summary>Actual library caller. Public IDs select retained reviews; they never confer authority.</summary>
public sealed class ShelfNativeWorkspacePage : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly ShelfLibraryWorkspace _workspace;
    private readonly Func<string, Task> _reviewHome;
    private readonly Func<Action, Task> _render;
    private string _collectionName = "";
    private string _editName = "", _editTags = "", _editOrder = "0";
    private bool _editFavourite;
    private int _editBehaviour;
    private string _smartTags = "";
    private bool _smartFavourites, _smartOffline;
    private int _smartKind;
    private Guid? _smartCollectionID;
    private Guid? _collectionID, _itemID;
    private string _name = "", _address = "", _selected = "", _status = "Ready", _search = "";
    private string? _searchError;
    public ShelfNativeWorkspacePage(ShelfLibraryWorkspace workspace, Func<string, Task> reviewHome, Func<Action, Task> render)
    { _workspace = workspace; _reviewHome = reviewHome; _render = render; }
    public IReadOnlyList<ShelfWorkspaceReview> Reviews => _workspace.Reviews;
    public string PresentationStatus => _status;
    public string PresentedRequestID => _selected;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch {
            "CollectionName" => _collectionName,
            "SmartTags" => _smartTags, "SmartFavourites" => _smartFavourites, "SmartOffline" => _smartOffline,
            "SmartKinds" => new[] { "All target kinds" }.Concat(Enum.GetValues<ShelfTargetKind>().Select(kind => kind switch {
                ShelfTargetKind.InstalledApplication => "Installed applications", ShelfTargetKind.NineToOneRoute => "9to1 pages",
                ShelfTargetKind.Project => "Projects", ShelfTargetKind.File => "Files", ShelfTargetKind.Folder => "Folders",
                ShelfTargetKind.WebAddress => "Web addresses", ShelfTargetKind.Shortcut => "Shortcuts",
                ShelfTargetKind.AndroidApplication => "Android applications", _ => kind.ToString() })).ToArray(),
            "SmartKindIndex" => _smartKind,
            "SmartCollectionNames" => new[] { "All items" }.Concat(ActiveSmartCollections().Select(x => x.Name)).ToArray(),
            "SelectedSmartCollectionIndex" => SmartCollectionIndex(),
            "EditBehaviourNames" => new[] { "Open", "Open with", "Reveal", "Launch" },
            "EditBehaviourIndex" => _editBehaviour,
            "EditName" => _editName, "EditTags" => _editTags, "EditOrder" => _editOrder, "EditFavourite" => _editFavourite,
            "CollectionNames" => ActiveCollections().Select(x => x.Name).ToArray(),
            "ItemNames" => ActiveItems().Select(x => x.Name).ToArray(),
            "SelectedCollectionIndex" => Array.FindIndex(ActiveCollections(), x => x.Id == _collectionID),
            "SelectedItemIndex" => Array.FindIndex(ActiveItems(), x => x.Id == _itemID),
            "Collections" => string.Join(Environment.NewLine, ActiveCollections().Select(x => x.Name)),
            "Memberships" => string.Join(Environment.NewLine, _workspace.Snapshot.Library.Memberships.Select(x =>
                $"{_workspace.Snapshot.Library.Collections.Single(c => c.Id == x.CollectionId).Name}: {_workspace.Snapshot.Library.Items.Single(i => i.Id == x.LaunchItemId).Name}")),
            "Name" => _name, "Address" => _address, "SelectedRequest" => _selected,
            "Status" => _status, "Search" => _search,
            "SearchSummary" => _searchError ?? $"{DisplayedItems().Count} matching Shelf items",
            "Library" => string.Join(Environment.NewLine, DisplayedItems().Select(x => $"{x.Name} ({x.Id:D})")),
            "Requests" => string.Join(Environment.NewLine, _workspace.Reviews.Select(x => x.RequestID)),
            _ => null };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_workspace.IsClosed) return false;
        if (path == "Search")
        {
            var query = Convert.ToString(value) ?? "";
            if (query.Length > 4096)
            {
                _searchError = "Search is too long. Shorten it to update the results.";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SearchSummary"));
                return false;
            }
            _search = query; _searchError = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Library"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SearchSummary"));
            return true;
        }

        if (path == "SelectedCollectionIndex" && value is int collectionIndex)
        {
            var values = ActiveCollections();
            if (collectionIndex < -1 || collectionIndex >= values.Length) return false;
            _collectionID = collectionIndex < 0 ? null : values[collectionIndex].Id; return true;
        }
        if (path == "SelectedItemIndex" && value is int itemIndex)
        {
            var values = ActiveItems();
            if (itemIndex < -1 || itemIndex >= values.Length) return false;
            _itemID = itemIndex < 0 ? null : values[itemIndex].Id;
            var item = itemIndex < 0 ? null : values[itemIndex];
            _editName = item?.Name ?? ""; _editTags = string.Join(", ", item?.Tags ?? []);
            _editBehaviour = (int)(item?.Behaviour ?? ShelfLaunchBehaviour.Open);
            _editFavourite = item?.IsFavourite ?? false; _editOrder = (item?.Order ?? 0).ToString(CultureInfo.InvariantCulture);
            foreach (var field in new[] { "EditName", "EditTags", "EditFavourite", "EditOrder", "EditBehaviourIndex" })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(field));
            return true;
        }
        if (path == "SmartTags")
        { var text = Convert.ToString(value) ?? ""; if (text.Length > 65536) return false; _smartTags = text; return true; }
        if (path == "SmartFavourites" && value is bool favourites) { _smartFavourites = favourites; return true; }
        if (path == "SmartOffline" && value is bool offline) { _smartOffline = offline; return true; }
        if (path == "SmartKindIndex" && value is int kindIndex)
        { if (kindIndex < 0 || kindIndex > Enum.GetValues<ShelfTargetKind>().Length) return false; _smartKind = kindIndex; return true; }
        if (path == "SelectedSmartCollectionIndex" && value is int smartIndex)
        {
            var smart = ActiveSmartCollections(); if (smartIndex < 0 || smartIndex > smart.Length) return false;
            _smartCollectionID = smartIndex == 0 ? null : smart[smartIndex - 1].Id;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Library"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SearchSummary")); return true;
        }
        if (path == "EditBehaviourIndex" && value is int behaviourIndex)
        { if (!Enum.IsDefined((ShelfLaunchBehaviour)behaviourIndex)) return false; _editBehaviour = behaviourIndex; return true; }
        if (path == "EditFavourite" && value is bool favourite) { _editFavourite = favourite; return true; }
        if (path is "EditName" or "EditTags" or "EditOrder")
        {
            var text = Convert.ToString(value) ?? "";
            if (text.Length > (path == "EditTags" ? 65536 : 4096)) return false;
            if (path == "EditName") _editName = text;
            else if (path == "EditTags") _editTags = text;
            else _editOrder = text;
            return true;
        }
        if (path == "CollectionName") _collectionName = Convert.ToString(value) ?? "";
        else if (path == "Name") _name = Convert.ToString(value) ?? "";
        else if (path == "Address") _address = Convert.ToString(value) ?? "";
        else if (path == "SelectedRequest") _selected = Convert.ToString(value) ?? "";
        else return false;
        return true;
    }
    public bool? IsActionAvailable(string command) => command switch {
        "Review" or "ReviewCollection" or "ReviewSmartCollection" or "Reload" => !_workspace.IsClosed,
        "ReviewEditItem" => !_workspace.IsClosed && _itemID is not null,
        "ReviewMembership" => !_workspace.IsClosed && _collectionID is not null && _itemID is not null,
        "Apply" => !_workspace.IsClosed && HasSelected(),
        "Home" or "Finish" => HasSelected(), _ => false };
    private ShelfCollection[] ActiveSmartCollections()
    {
        var snapshot = _workspace.Snapshot;
        return snapshot.Library.Collections.Where(x => x.Kind == ShelfCollectionKind.Smart
            && !snapshot.ArchivedCollectionIds.Contains(x.Id)).ToArray();
    }
    private int SmartCollectionIndex()
    {
        if (_smartCollectionID is null) return 0;
        var index = Array.FindIndex(ActiveSmartCollections(), x => x.Id == _smartCollectionID);
        return index < 0 ? -1 : index + 1;
    }
    private IReadOnlyList<ShelfLaunchItem> DisplayedItems()
    {
        if (_smartCollectionID is not null && !ActiveSmartCollections().Any(x => x.Id == _smartCollectionID)) return [];
        return ShelfLibraryService.Search(_workspace.Snapshot, _search, _smartCollectionID);
    }
    private ShelfCollection[] ActiveCollections()
    {
        var snapshot = _workspace.Snapshot;
        return snapshot.Library.Collections.Where(x => x.Kind == ShelfCollectionKind.Manual
            && !snapshot.ArchivedCollectionIds.Contains(x.Id)).ToArray();
    }
    private ShelfLaunchItem[] ActiveItems()
    {
        var snapshot = _workspace.Snapshot;
        return snapshot.Library.Items.Where(x => !snapshot.ArchivedItemIds.Contains(x.Id)).ToArray();
    }
    private bool HasSelected() => _workspace.Reviews.Any(x => x.RequestID == _selected);
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        // Capture every clicked proposal and request before the first owner await.
        var selected = _selected;
        if (IsActionAvailable(command) != true) throw new UnauthorizedAccessException("Shelf action unavailable.");
        string status; string? nextSelected = null;
        switch (command)
        {
            case "Review":
                var name = _name; var address = _address;
                if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
                    throw new ArgumentException("Enter an absolute HTTP or HTTPS address.");
                var item = new ShelfLaunchItem(Guid.NewGuid(), name,
                    new(ShelfTargetKind.WebAddress, uri.AbsoluteUri, Uri: uri.AbsoluteUri));
                var issued = await _workspace.ReviewSaveAsync(item, token: cancellationToken);
                // Retain delivered request even if close raced publication. Home/Finish remain reachable.
                nextSelected = issued; status = "Review this request in Home before applying."; break;
            case "ReviewEditItem":
                var editID = _itemID;
                var originalItem = ActiveItems().SingleOrDefault(x => x.Id == editID)
                    ?? throw new UnauthorizedAccessException("Select an active original Shelf item.");
                if (!int.TryParse(_editOrder, NumberStyles.None, CultureInfo.InvariantCulture, out var order) || order < 0)
                    throw new ArgumentException("Enter a nonnegative whole-number order.");
                var editTags = _editTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var edit = new ShelfItemEdit(originalItem.Id, _editName, editTags, _editFavourite, order, (ShelfLaunchBehaviour)_editBehaviour);
                nextSelected = await _workspace.ReviewEditItemAsync(edit, cancellationToken);
                status = "Review the item pin, tags and order in Home before applying."; break;
            case "ReviewSmartCollection":
                var smartTags = _smartTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                var kinds = Enum.GetValues<ShelfTargetKind>();
                if (_smartKind < 0 || _smartKind > kinds.Length) throw new ArgumentException("Choose a supported target kind.");
                var smartCriteria = new ShelfSmartCollectionCriteria(_smartKind == 0 ? null : new[] { kinds[_smartKind - 1] },
                    smartTags, _smartFavourites ? true : null, _smartOffline ? true : null);
                var smartCollection = new ShelfCollection(Guid.NewGuid(), _collectionName, ShelfCollectionKind.Smart,
                    ShelfPresentation.List, smartCriteria);
                nextSelected = await _workspace.ReviewCreateSmartCollectionAsync(smartCollection, cancellationToken);
                status = "Review the smart collection criteria in Home before applying."; break;
            case "ReviewCollection":
                var collection = new ShelfCollection(Guid.NewGuid(), _collectionName, ShelfCollectionKind.Manual, ShelfPresentation.List);
                nextSelected = await _workspace.ReviewCreateCollectionAsync(collection, cancellationToken);
                status = "Review the manual collection in Home before applying."; break;
            case "ReviewMembership":
                var collectionID = _collectionID; var itemID = _itemID;
                if (collectionID is null || itemID is null || !ActiveCollections().Any(x => x.Id == collectionID)
                    || !ActiveItems().Any(x => x.Id == itemID)) throw new UnauthorizedAccessException("Choose a current collection and item.");
                nextSelected = await _workspace.ReviewAddMembershipAsync(collectionID.Value, itemID.Value, token: cancellationToken);
                status = "Review the collection membership in Home before applying."; break;
            case "Home": await _reviewHome(selected); status = "Home review opened."; break;
            case "Apply":
                var applied = await _workspace.ApplyOrRecoverAsync(selected, cancellationToken);
                status = DescribeObservation(applied); break;
            case "Finish":
                var finished = await _workspace.FinishAsync(selected, cancellationToken);
                status = DescribeObservation(finished); break;
            case "Reload": await _workspace.ReloadAsync(cancellationToken); status = "Library refreshed."; break;
            default: throw new UnauthorizedAccessException("Unknown Shelf action.");
        }
        await _render(() => {
            // Retained owner request/outcome is independent of native presentation lifetime.
            // The check must run inside the actual queued UI callback, after any UI delay.
            if (_workspace.IsClosed) return;
            if (nextSelected is not null) _selected = nextSelected;
            _status = status; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        });
    }
    private static string DescribeObservation(ShelfLibraryCommit observed)
    {
        if (observed.Committed)
            return observed.AuditRecorded ? "Saved to Shelf." : "Saved to Shelf. Finish its pending Home audit.";
        if (observed.CompletionUnknown)
            return "The save outcome is unresolved. Recover this original request before making another save.";
        return observed.Code switch
        {
            "ApprovalRequired" => "Review this request in Home before applying it.",
            "NoAttemptedOutcome" => "No save has been attempted for this request.",
            "FinalClaimFenceUnavailable" => "This Shelf view cannot safely save. Reopen Shelf and review the request.",
            "OwnerAdmissionAuditPending" => "The save was refused. Finish its pending Home audit.",
            "OwnerAdmissionRejected" => "The save was refused. Reload Shelf and review a new request.",
            "RevisionConflict" => "Shelf changed. Reload it and review a new request.",
            _ => "This original request cannot continue. Check its decision in Home before reviewing a new request."
        };
    }
    // Explicit recovery preserves private issued context after native tab detach. It cannot Begin or write.
    public Task<Haven.Infrastructure.ShelfLibraryCommit> FinishRetainedAsync(string requestID, CancellationToken token = default)
        => _workspace.FinishAsync(requestID, token);
    public Task OpenRetainedHomeReviewAsync(string requestID)
    {
        if (!_workspace.Reviews.Any(x => x.RequestID == requestID))
            throw new UnauthorizedAccessException("This Shelf caller did not issue that request.");
        return _reviewHome(requestID);
    }
    public void Dispose() => _workspace.Dispose();
}
