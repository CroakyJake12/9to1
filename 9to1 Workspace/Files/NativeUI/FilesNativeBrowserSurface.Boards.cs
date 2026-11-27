using Haven.Application;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface
{
    private readonly IFilesNativeBoardOpenHost? _boards;

    private bool CanOpenBoardSelection => !InDrive && _boards?.IsAvailable == true &&
        _browser.HasOriginalBoardDocumentReadOwner && _page is { IsTrash: false } page &&
        _items.SelectedItem is HostedItemMetadata { CurrentRevisionId: not null } row &&
        page.Items.Any(item => ReferenceEquals(item, row)) &&
        FilesNativeBrowserService.IsBoardDocumentCandidate(row);

    private string BoardOpenUnavailableReason => _boards?.IsAvailable != true
        ? "Boards is unavailable in this Files view. The original file is unchanged."
        : !_browser.HasOriginalBoardDocumentReadOwner
            ? "The original Files board reader is unavailable. The original file is unchanged."
            : "This board has no current opening selection. Refresh its folder and select it again.";

    private async Task OpenBoardOriginalAsync(FilesNativeBrowserPage page, HostedItemMetadata row,
        long selectionEpoch, Task originalSettlement, CancellationToken token)
    {
        var host = _boards ?? throw new NotSupportedException("The original Boards opening host is unavailable.");
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        IFilesNativePreparedBoardOpen? prepared = null;
        var transferred = false;
        Exception? failure = null;
        try
        {
            await originals.RunToOriginalSettlementAsync(async () =>
            {
                RequireOriginalAlive(token);
                if (!SelectionCurrent())
                { _status = "The selected board changed before opening. Select it again."; return false; }
                Action<Action> own = body => originals.Invoke(() => { body(); return true; });
                Action<Task> retain = actual => originals.Track(actual);
                var result = await originals.AwaitAsync(originals.Invoke(() => host.PrepareAsync(
                    page, row, _originalActor, own, retain, token)));
                prepared = result.Prepared;
                if (prepared is null)
                {
                    if (string.IsNullOrWhiteSpace(result.Refusal))
                        throw new InvalidDataException("The original Boards host returned neither a prepared read nor its refusal.");
                    _status = result.Refusal;
                    return false;
                }
                await originals.AwaitAsync(originals.Invoke(() => _browser.RevalidateAsync(page, _originalActor, token)));
                var current = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files board page retired.");
                await originals.AwaitAsync(originals.Invoke(() => RequireReadyAsync(token)));
                await originals.AwaitAsync(originals.Invoke(() => RequireOriginalOwnerAsync(current, token)));
                RequireOriginalAlive(token);
                if (!SelectionCurrent())
                { _status = "The selected board changed while it was being read. Select it again."; return false; }
                // Browse waits for the SAME retained action, including its final native
                // publication and semaphore release. It never joins itself here.
                transferred = prepared.QueueAfter(originalSettlement);
                _status = transferred ? "Opening this revision in Boards…" : "Boards could not accept this opening. Try again when navigation has settled.";
                return transferred;
            });
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            if (!transferred && prepared is not null)
            {
                try { await prepared.DisposeAsync(); }
                catch (Exception cleanup) when (failure is not null)
                { throw new AggregateException("The original Files opening and prepared-board cleanup failed.", failure, cleanup); }
            }
        }

        bool SelectionCurrent() => selectionEpoch == _selectionEpoch &&
            ReferenceEquals(_page, page) && ReferenceEquals(_items.SelectedItem, row) &&
            !page.IsTrash && page.Items.Any(item => ReferenceEquals(item, row));
    }
}
