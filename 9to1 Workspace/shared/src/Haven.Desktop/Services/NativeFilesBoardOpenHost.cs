using System.Runtime.ExceptionServices;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Core;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Services;

/// <summary>Per-tab native opening bridge. It borrows the original Files/Home
/// graph and the same prepared-read/write registries already consumed by Boards.</summary>
internal sealed class NativeFilesBoardOpenHost : IFilesNativeBoardOpenHost
{
    private readonly FilesNativeBrowserService _browser;
    private readonly AuthenticatedResourceActor _actor;
    private readonly NativeHomeFilesBoardReadSource _reads;
    private readonly NativeHomeFilesBoardWriteSource? _writes;
    private readonly Func<bool> _hostAlive;
    private readonly Func<CancellationToken, Task<bool>> _authorityCurrent;
    private readonly Func<HomePermissionAuthorization, CancellationToken, Task>? _presentReview;
    private readonly Action<Action> _hostScope;
    private readonly Action<Task> _retainHostTask;
    private readonly Func<AppContentAddress, Task, Task?> _queueOriginalNavigation;

    internal NativeFilesBoardOpenHost(FilesNativeBrowserService browser, AuthenticatedResourceActor actor,
        NativeHomeFilesBoardReadSource reads, NativeHomeFilesBoardWriteSource? writes,
        Func<bool> hostAlive, Func<CancellationToken, Task<bool>> authorityCurrent,
        Func<HomePermissionAuthorization, CancellationToken, Task>? presentReview,
        Action<Action> hostScope, Action<Task> retainHostTask,
        Func<AppContentAddress, Task, Task?> queueOriginalNavigation)
    {
        _browser = browser ?? throw new ArgumentNullException(nameof(browser));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _reads = reads ?? throw new ArgumentNullException(nameof(reads)); _writes = writes;
        _hostAlive = hostAlive ?? throw new ArgumentNullException(nameof(hostAlive));
        _authorityCurrent = authorityCurrent ?? throw new ArgumentNullException(nameof(authorityCurrent));
        _presentReview = presentReview;
        _hostScope = hostScope ?? throw new ArgumentNullException(nameof(hostScope));
        _retainHostTask = retainHostTask ?? throw new ArgumentNullException(nameof(retainHostTask));
        _queueOriginalNavigation = queueOriginalNavigation ?? throw new ArgumentNullException(nameof(queueOriginalNavigation));
    }

    public bool IsAvailable => _hostAlive() && _browser.HasOriginalBoardDocumentReadOwner;

    public async Task<FilesNativeBoardOpenPreparation> PrepareAsync(FilesNativeBrowserPage page,
        HostedItemMetadata row, AuthenticatedResourceActor actor, Action<Action> originalScope,
        Action<Task> retainOriginalTask, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(originalScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        if (actor != _actor || page.IsTrash || !page.Items.Any(item => ReferenceEquals(item, row)) ||
            !FilesNativeBrowserService.IsBoardDocumentCandidate(row) || row.CurrentRevisionId is null ||
            row.ParentId?.Value != page.ParentID)
            throw new UnauthorizedAccessException("Open the exact current Files board row with its original profile.");

        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalOwner(this); originals.BindOriginalCallerCallback(originalScope);
        Prepared? pending = null;
        try
        {
            return await originals.RunToOriginalSettlementAsync(async () =>
            {
                void Own(Action body) => originals.Invoke(() => { body(); return true; });
                void Retain(Task actual) { originals.Track(actual); retainOriginalTask(actual); }
                await originals.AwaitAsync(originals.Invoke(() => DemandHostAsync(token)));
                var outcome = await originals.AwaitAsync(originals.Invoke(() => _reads.ReadSelectedOutcomeAsync(
                    page, row, _hostAlive, _authorityCurrent, Own, Retain, originals, token)));
                if (outcome.Refusal is { } refusal) return new FilesNativeBoardOpenPreparation(null, refusal.Message);
                var read = outcome.Lease ?? throw new InvalidDataException("The original Files board read returned no lease.");
                var writable = _writes is not null && _presentReview is not null;
                Guid? issued = null;
                var transferred = false;
                Exception? failure = null;
                try
                {
                    // Validate the maintained bounded document envelope before navigation;
                    // its bytes, document identity and Files revision are never rewritten.
                    var document = HavenBoardDocumentCodec.Deserialize(read.Bytes);
                    if (document.DocumentId == Guid.Empty)
                        throw new InvalidDataException("The selected board has no canonical document identity.");
                    await originals.AwaitAsync(originals.Invoke(() => _browser.RevalidateAsync(page, _actor, token)));
                    await originals.AwaitAsync(originals.Invoke(() => DemandHostAsync(token)));
                    issued = originals.Invoke(() => writable
                        ? _writes!.RegisterPrepared(page, row, read, _hostAlive, _presentReview!,
                            "files-board:" + row.Id.Value.ToString("D"), _hostScope, _retainHostTask)
                        : _reads.RegisterPrepared(read));
                    var address = writable
                        ? BoardsBrowseAppContentAddress.ForWritableFile(read.StoreId, read.ParentFolderId, read.FileId, read.RevisionId, issued.Value)
                        : BoardsBrowseAppContentAddress.ForFile(read.StoreId, read.ParentFolderId, read.FileId, read.RevisionId, issued.Value);
                    var prepared = new Prepared(this, address, issued.Value, writable);
                    pending = prepared;
                    transferred = true;
                    return new FilesNativeBoardOpenPreparation(prepared, null);
                }
                catch (Exception error) { failure = error; throw; }
                finally
                {
                    if (!transferred)
                    {
                        try
                        {
                            if (issued is { } id) await DiscardAsync(id, writable);
                            else await read.DisposeAsync();
                        }
                        catch (Exception cleanup) when (failure is not null)
                        { throw new AggregateException("The original Files board preparation and byte cleanup failed.", failure, cleanup); }
                    }
                }
            });
        }
        catch (Exception failure)
        {
            // A retained sibling can still fail final ledger settlement after the
            // body prepared its lease. No caller received custody in that case.
            if (pending is not null)
            {
                try { await pending.DisposeAsync(); }
                catch (Exception cleanup)
                { throw new AggregateException("The original board preparation settlement and cleanup failed.", failure, cleanup); }
            }
            throw;
        }
    }

    private async Task DemandHostAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!_hostAlive() || !await _authorityCurrent(token) || !_hostAlive())
            throw new UnauthorizedAccessException("The original native Files/Boards profile authority retired.");
        token.ThrowIfCancellationRequested();
    }

    private Task DiscardAsync(Guid id, bool writable) => writable
        ? _writes!.DiscardPreparedAsync(id) : _reads.DiscardPreparedAsync(id);

    private sealed class Prepared(NativeFilesBoardOpenHost owner, AppContentAddress address,
        Guid id, bool writable) : IFilesNativePreparedBoardOpen
    {
        private readonly object _gate = new();
        private Task? _discard;
        private bool _queued;

        public bool QueueAfter(Task originalSourceSettlement)
        {
            ArgumentNullException.ThrowIfNull(originalSourceSettlement);
            lock (_gate)
            {
                if (_queued || _discard is not null)
                    throw new InvalidOperationException("This prepared Files board opening was already transferred or discarded.");
                if (!owner.IsAvailable)
                    throw new UnauthorizedAccessException("The original Boards opening host retired before navigation.");
                var navigation = owner._queueOriginalNavigation(address, originalSourceSettlement);
                if (navigation is null) return false;
                _queued = true;
                // This distinct task belongs to the original host, not to the Files
                // sender: navigation must be able to wait for that sender to settle.
                owner._retainHostTask(ReleaseAfterNavigationAsync(navigation));
                return true;
            }
        }

        public ValueTask DisposeAsync() => new(DiscardOnceAsync());

        private Task DiscardOnceAsync()
        {
            lock (_gate) return _discard ??= owner.DiscardAsync(id, writable);
        }

        private async Task ReleaseAfterNavigationAsync(Task navigation)
        {
            Exception? failure = null;
            try { await navigation.ConfigureAwait(false); }
            catch (Exception error) { failure = error; }
            try { await DiscardOnceAsync().ConfigureAwait(false); }
            catch (Exception cleanup) when (failure is not null)
            { throw new AggregateException("The original Boards navigation and prepared-read cleanup failed.", failure, cleanup); }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
