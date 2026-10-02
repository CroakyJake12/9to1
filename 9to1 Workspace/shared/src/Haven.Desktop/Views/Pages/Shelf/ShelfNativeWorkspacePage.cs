using System.ComponentModel;
using CakeOS.Cui.Runtime;
using Haven.Core.Shelf;
using Haven.Infrastructure;
namespace Haven.Desktop.Views.Pages.Shelf;

/// <summary>Actual library caller. Public IDs select retained reviews; they never confer authority.</summary>
public sealed class ShelfNativeWorkspacePage : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, IDisposable
{
    private readonly ShelfLibraryWorkspace _workspace;
    private readonly Func<string, Task> _reviewHome;
    private readonly Func<Action, Task> _render;
    private string _name = "", _address = "", _selected = "", _status = "Ready";
    public ShelfNativeWorkspacePage(ShelfLibraryWorkspace workspace, Func<string, Task> reviewHome, Func<Action, Task> render)
    { _workspace = workspace; _reviewHome = reviewHome; _render = render; }
    public IReadOnlyList<ShelfWorkspaceReview> Reviews => _workspace.Reviews;
    public string PresentationStatus => _status;
    public string PresentedRequestID => _selected;
    public event PropertyChangedEventHandler? PropertyChanged;
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch {
            "Name" => _name, "Address" => _address, "SelectedRequest" => _selected,
            "Status" => _status,
            "Library" => string.Join(Environment.NewLine, _workspace.Snapshot.Library.Items.Select(x => $"{x.Name} ({x.Id:D})")),
            "Requests" => string.Join(Environment.NewLine, _workspace.Reviews.Select(x => x.RequestID)),
            _ => null };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_workspace.IsClosed) return false;
        if (path == "Name") _name = Convert.ToString(value) ?? "";
        else if (path == "Address") _address = Convert.ToString(value) ?? "";
        else if (path == "SelectedRequest") _selected = Convert.ToString(value) ?? "";
        else return false;
        return true;
    }
    public bool? IsActionAvailable(string command) => command switch {
        "Review" or "Reload" => !_workspace.IsClosed,
        "Apply" => !_workspace.IsClosed && HasSelected(),
        "Home" or "Finish" => HasSelected(), _ => false };
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
            case "Home": await _reviewHome(selected); status = "Home review opened."; break;
            case "Apply":
                var applied = await _workspace.ApplyOrRecoverAsync(selected, cancellationToken);
                status = applied.Code; break;
            case "Finish":
                var finished = await _workspace.FinishAsync(selected, cancellationToken);
                status = finished.Code; break;
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
