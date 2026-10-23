using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

// Canonical Core/queue controls with explicit local native-source doubles. These are not
// native transport, installed browser, real Home/Files permission or profile certification.
public sealed class BrowseDownloadReviewTests
{
    [Fact]
    public async Task OnlyExactLiveNativeOwnedPendingDownloadsEnterTheSnapshot()
    {
        using var paths = new ReviewPaths(); var queue = new ReviewQueue(); var factory = new ReviewFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory, queue);
        await chrome.NavigateAsync("https://downloads.example/");
        var owned = Action("own.txt"); var foreign = Action("foreign-private-secret.txt") with { Origin = "private" };
        var submit = Action("submit") with { Kind = BrowserActionKind.SubmitElement };
        queue.Pending.AddRange([owned, foreign, submit]); factory.Hosts.Last().Owned.Add(owned);
        var snapshot = await chrome.RefreshDownloadsAsync();
        var observed = Assert.Single(snapshot.Downloads.Pending);
        Assert.Same(owned, observed.Action); Assert.Equal(snapshot.SelectedTabId, observed.TabId);
        Assert.DoesNotContain(snapshot.Downloads.Pending, value => value.Action == foreign || value.Action == submit);
        factory.Hosts.Last().Retired = true;
        Assert.Empty(chrome.State.Downloads.Pending);
        Assert.False(chrome.IsCurrentPendingDownload(observed, approving: true));
    }

    [Fact]
    public async Task StandardAndPrivateContextsNeverExposeOrApproveEachOthersOwnedRequests()
    {
        using var paths = new ReviewPaths(); var queue = new ReviewQueue(); var factory = new ReviewFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory, queue);
        await chrome.NavigateAsync("https://downloads.example/"); var standardId = chrome.State.SelectedTabId;
        var standard = Action("standard.txt"); queue.Pending.Add(standard); factory.Hosts.Last().Owned.Add(standard);
        await chrome.NewTabAsync(isPrivate: true); await chrome.NavigateAsync("https://private.example/");
        var privateAction = Action("private-only.txt") with { Origin = "private" };
        queue.Pending.Add(privateAction); factory.Hosts.Last().Owned.Add(privateAction);
        var privateObservation = Assert.Single((await chrome.RefreshDownloadsAsync()).Downloads.Pending);
        Assert.Same(privateAction, privateObservation.Action);
        await chrome.SelectTabAsync(standardId);
        Assert.Empty(chrome.State.Downloads.Pending);
        var standardObservation = Assert.Single((await chrome.RefreshDownloadsAsync()).Downloads.Pending);
        Assert.Same(standard, standardObservation.Action);
        await chrome.ApprovePendingDownloadAsync(privateObservation);
        Assert.Empty(queue.Approved); Assert.Equal(2, queue.Pending.Count);
    }

    [Fact]
    public async Task ChangedQueueRecordAndWithdrawnOwnerCannotApproveAStaleObservation()
    {
        using var paths = new ReviewPaths(); var queue = new ReviewQueue(); var factory = new ReviewFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory, queue);
        await chrome.NavigateAsync("https://downloads.example/");
        var action = Action("notes.txt"); queue.Pending.Add(action); factory.Hosts.Last().Owned.Add(action);
        var observed = Assert.Single((await chrome.RefreshDownloadsAsync()).Downloads.Pending);
        queue.Pending[0] = action with { SuggestedFileName = "changed.bin", UpdatedAt = action.UpdatedAt.AddSeconds(1) };
        await chrome.ApprovePendingDownloadAsync(observed);
        Assert.Empty(queue.Approved); Assert.Empty(chrome.State.Downloads.Pending);
        queue.Pending[0] = action;
        observed = Assert.Single((await chrome.RefreshDownloadsAsync()).Downloads.Pending);
        factory.Hosts.Last().Retired = true;
        await chrome.RejectPendingDownloadAsync(observed);
        Assert.Empty(queue.Rejected); Assert.Single(queue.Pending);
    }

    [Fact]
    public async Task ExpiredPendingDownloadCanBeRejectedButNeverApproved()
    {
        using var paths = new ReviewPaths(); var queue = new ReviewQueue(); var factory = new ReviewFactory();
        await using var chrome = await BrowseChrome.CreateAsync(paths, factory, queue);
        await chrome.NavigateAsync("https://downloads.example/");
        var action = Action("expired.txt") with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        queue.Pending.Add(action); factory.Hosts.Last().Owned.Add(action);
        var observed = Assert.Single((await chrome.RefreshDownloadsAsync()).Downloads.Pending);
        Assert.False(chrome.IsCurrentPendingDownload(observed, approving: true));
        Assert.True(chrome.IsCurrentPendingDownload(observed, approving: false));
        await chrome.ApprovePendingDownloadAsync(observed); Assert.Empty(queue.Approved);
        await chrome.RejectPendingDownloadAsync(observed);
        Assert.Equal(action.Id, Assert.Single(queue.Rejected)); Assert.Empty(queue.Pending);
        Assert.Contains("Rejected", chrome.State.Downloads.ReviewStatus);
    }

    [Fact]
    public async Task ActualFailureResultRemainsVisibleAfterPendingAndCompletedRefresh()
    {
        using var paths = new ReviewPaths(); var queue = new ReviewQueue { ApprovalState = BrowserActionState.Failed };
        var factory = new ReviewFactory(); await using var chrome = await BrowseChrome.CreateAsync(paths, factory, queue);
        await chrome.NavigateAsync("https://downloads.example/");
        var action = Action("failed.txt"); queue.Pending.Add(action); factory.Hosts.Last().Owned.Add(action);
        var observed = Assert.Single((await chrome.RefreshDownloadsAsync()).Downloads.Pending);
        await chrome.ApprovePendingDownloadAsync(observed);
        Assert.Equal(action.Id, Assert.Single(queue.Approved)); Assert.Empty(chrome.State.Downloads.Pending);
        Assert.Empty(chrome.State.Downloads.Items); Assert.Contains("Failed", chrome.State.Downloads.ReviewStatus);
        await chrome.RefreshDownloadsAsync(); Assert.Contains("Failed", chrome.State.Downloads.ReviewStatus);
    }

    private static BrowserPendingAction Action(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), BrowserActionKind.Download, "https://downloads.example", "Download " + name,
            "native:test", name, BrowserActionState.Pending, now, now.AddMinutes(10), now, null);
    }
    private sealed class ReviewQueue : IBrowserAutomationService
    {
        internal List<BrowserPendingAction> Pending = []; internal List<Guid> Approved = [], Rejected = [];
        internal BrowserActionState ApprovalState = BrowserActionState.Executed;
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>(Pending.ToArray());
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);
        public Task<BrowserActionExecutionResult> ApproveAsync(Guid id, CancellationToken token)
        { Approved.Add(id); Pending.RemoveAll(value => value.Id == id); return Task.FromResult(new BrowserActionExecutionResult(id, ApprovalState, "Explicit queue fixture result")); }
        public Task<BrowserActionExecutionResult> RejectAsync(Guid id, CancellationToken token)
        { Rejected.Add(id); Pending.RemoveAll(value => value.Id == id); return Task.FromResult(new BrowserActionExecutionResult(id, BrowserActionState.Rejected, "Explicit queue fixture rejection")); }
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>([]);
        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> NavigateAsync(string value, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickReferenceAsync(string value, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FillReferenceAsync(string name, string value, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserPendingAction> RequestDownloadAsync(string value, string? name, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ReviewFactory : IBrowseEngineHostFactory, IBrowseEngineAvailability
    {
        internal List<ReviewEngine> Hosts = [];
        public bool IsSupported => true; public string UnsupportedReason => "Explicit engine fixture";
        public bool IsEngineSupported(BrowseEngineKind engine) => true;
        public string UnsupportedReasonFor(BrowseEngineKind engine) => string.Empty;
        public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token)
        { var host = new ReviewEngine(request); Hosts.Add(host); return Task.FromResult<IBrowseEngineTab>(host); }
    }
    private sealed class ReviewEngine(BrowseEngineTabRequest request) : IBrowseEngineTab, IBrowseOriginalPendingDownloadSource
    {
        internal List<BrowserPendingAction> Owned = []; internal bool Retired;
        public bool IsOriginalPendingDownload(BrowserPendingAction action) => !Retired && Owned.Count(value => value == action) == 1;
        public BrowserSnapshot State { get; private set; } = new(request.InitialAddress, "Download fixture", false, false, false, "Ready fixture");
        public event EventHandler<BrowserSnapshot>? StateChanged;
        public event EventHandler<BrowsePopupRequest>? PopupRequested { add { } remove { } }
        public event EventHandler<BrowseEngineCrash>? Crashed { add { } remove { } }
        public Task NavigateAsync(Uri address, CancellationToken token)
        { State = State with { Address = address }; StateChanged?.Invoke(this, State); return Task.CompletedTask; }
        public Task GoBackAsync(CancellationToken token) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token) => Task.FromResult<string?>(null);
        public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Retired = true; return ValueTask.CompletedTask; }
    }
    private sealed class ReviewPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "browse-download-review-" + Guid.NewGuid().ToString("N"));
        public ReviewPaths() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "fixture.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
