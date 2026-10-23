using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

// Actual authored CUI/loader/buttons with explicit local service and engine doubles. This
// proves the control route, not real CDP transfer, Home approval or installed browser acceptance.
public sealed partial class BrowseNativeUiTests
{
    [AvaloniaFact]
    public async Task Pending_download_CUI_approves_only_the_exact_original_owned_row_and_preserves_result()
    {
        var f = await PendingReviewFixture.Create();
        try
        {
            await f.Open(); var row = Assert.Single(PendingReviewRows(f.Workspace));
            Assert.True(f.Workspace.TryGetItemValue(row, "Label", out var label)); Assert.Equal("revision.pdf", label);
            Assert.DoesNotContain(f.Window.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text?.Contains("foreign-secret", StringComparison.Ordinal) == true);
            var approve = Find(f.Window, "BrowseApprovePendingDownloadButton"); Assert.True(approve.IsEnabled);
            await Click(f.Window, f.Workspace, approve);
            Assert.Equal(f.Queue.Owned.Id, Assert.Single(f.Queue.Approved)); Assert.Empty(f.Queue.Rejected);
            Assert.Empty(PendingReviewRows(f.Workspace)); Assert.Empty(f.Workspace.State.Downloads.Items);
            Assert.True(f.Workspace.TryGetValue("DownloadReviewStatus", out var status)); Assert.Contains("Executed", Assert.IsType<string>(status));
            var currentCommand = f.Workspace.OriginalCommand;
            await f.Workspace.DispatchAsync("9to1.Browse.ApprovePendingDownload", row.Target);
            Assert.Same(currentCommand, f.Workspace.OriginalCommand); Assert.Single(f.Queue.Approved);
            await Retire(f.Window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Pending_download_CUI_rejects_expired_request_without_approving_or_claiming_active_cancel()
    {
        var f = await PendingReviewFixture.Create(expired: true);
        try
        {
            await f.Open(); var row = Assert.Single(PendingReviewRows(f.Workspace));
            Assert.False(Find(f.Window, "BrowseApprovePendingDownloadButton").IsEnabled);
            Assert.True(f.Workspace.TryGetItemValue(row, "Expiry", out var expiry)); Assert.Contains("expired", Assert.IsType<string>(expiry));
            await Click(f.Window, f.Workspace, Find(f.Window, "BrowseRejectPendingDownloadButton"));
            Assert.Equal(f.Queue.Owned.Id, Assert.Single(f.Queue.Rejected)); Assert.Empty(f.Queue.Approved);
            Assert.Empty(PendingReviewRows(f.Workspace));
            Assert.True(f.Workspace.TryGetValue("DownloadReviewStatus", out var status)); Assert.Contains("Rejected", Assert.IsType<string>(status));
            await Retire(f.Window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); throw; }
    }

    [AvaloniaFact]
    public async Task Pending_download_CUI_hides_unowned_metadata_and_refuses_foreign_or_withdrawn_targets()
    {
        var f = await PendingReviewFixture.Create(); var other = await PendingReviewFixture.Create(owns: false);
        try
        {
            await f.Open(); await other.Open();
            var row = Assert.Single(PendingReviewRows(f.Workspace));
            Assert.Empty(PendingReviewRows(other.Workspace));
            var previous = other.Workspace.OriginalCommand;
            await other.Workspace.DispatchAsync("9to1.Browse.RejectPendingDownload", row.Target);
            Assert.Same(previous, other.Workspace.OriginalCommand); Assert.Empty(other.Queue.Rejected);
            f.Factory.Hosts.ForEach(host => host.Withdrawn = true);
            var current = f.Workspace.OriginalCommand;
            await f.Workspace.DispatchAsync("9to1.Browse.ApprovePendingDownload", row.Target);
            Assert.Same(current, f.Workspace.OriginalCommand); Assert.Empty(f.Queue.Approved);
            Assert.False(f.Workspace.TryGetItemValue(row, "Summary", out _));
            await Retire(f.Window); await Retire(other.Window);
        }
        catch (Exception failure) { RetainedFailedOwners.Add((f.Window, failure)); RetainedFailedOwners.Add((other.Window, failure)); throw; }
    }

    private static BrowseNativeWorkspace.PendingDownloadRow[] PendingReviewRows(BrowseNativeWorkspace workspace)
    {
        Assert.True(workspace.TryGetValue("PendingDownloads", out var value));
        return Assert.IsType<BrowseNativeWorkspace.PendingDownloadRow[]>(value);
    }
    private sealed record PendingReviewFixture(BrowseNativeWorkspace Workspace, BrowseNativeWindow Window,
        PendingReviewQueue Queue, PendingReviewFactory Factory)
    {
        internal static async Task<PendingReviewFixture> Create(bool expired = false, bool owns = true)
        {
            var queue = new PendingReviewQueue(expired); var factory = new PendingReviewFactory(queue, owns);
            var chrome = await BrowseChrome.CreateAsync(new Paths(), factory, queue);
            await chrome.NavigateAsync("https://downloads.example/");
            var workspace = new BrowseNativeWorkspace(chrome, _ => true);
            var window = BrowseNativeSurface.CreateWindow(workspace, new FixtureReadiness());
            RegisterProductionImageResolver(window);
            return new(workspace, window, queue, factory);
        }
        internal async Task Open()
        {
            Window.Show(); await Observe(Window.InitializeAsync(), Window);
            await Click(Window, Workspace, Find(Window, "BrowseOpenDownloadsButton"));
        }
    }
    private sealed class PendingReviewQueue : IBrowserAutomationService
    {
        internal BrowserPendingAction Owned { get; }
        internal List<BrowserPendingAction> Pending = []; internal List<Guid> Approved = [], Rejected = [];
        internal PendingReviewQueue(bool expired)
        {
            var now = DateTimeOffset.UtcNow;
            Owned = new(Guid.NewGuid(), BrowserActionKind.Download, "https://downloads.example", "Download revision.pdf", "native:component-fixture", "revision.pdf",
                BrowserActionState.Pending, now, now.AddMinutes(expired ? -1 : 10), now, null);
            Pending.Add(Owned);
            Pending.Add(Owned with { Id = Guid.NewGuid(), Origin = "private", SuggestedFileName = "foreign-secret.bin", Summary = "foreign-secret context" });
        }
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserPendingAction>>(Pending.ToArray());
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserDownloadRecord>>([]);
        public Task<BrowserActionExecutionResult> ApproveAsync(Guid id, CancellationToken token)
        { Approved.Add(id); Pending.RemoveAll(value => value.Id == id); return Task.FromResult(new BrowserActionExecutionResult(id, BrowserActionState.Executed, "Explicit component approval result")); }
        public Task<BrowserActionExecutionResult> RejectAsync(Guid id, CancellationToken token)
        { Rejected.Add(id); Pending.RemoveAll(value => value.Id == id); return Task.FromResult(new BrowserActionExecutionResult(id, BrowserActionState.Rejected, "Pending request rejected")); }
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<BrowserAuditEntry>>([]);
        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> NavigateAsync(string value, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickReferenceAsync(string value, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FillReferenceAsync(string name, string value, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserPendingAction> RequestDownloadAsync(string value, string? name, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class PendingReviewFactory(PendingReviewQueue queue, bool owns) : IBrowseEngineHostFactory, IBrowseEngineAvailability
    {
        internal List<PendingReviewEngine> Hosts = [];
        public bool IsSupported => true; public string UnsupportedReason => "Explicit UI engine fixture";
        public bool IsEngineSupported(BrowseEngineKind engine) => true;
        public string UnsupportedReasonFor(BrowseEngineKind engine) => string.Empty;
        public Task<IBrowseEngineTab> CreateAsync(BrowseEngineTabRequest request, CancellationToken token) => Dispatcher.UIThread.InvokeAsync(() =>
        { var engine = new PendingReviewEngine(request, queue, owns); Hosts.Add(engine); return (IBrowseEngineTab)engine; }).GetTask();
    }
    private sealed class PendingReviewEngine(BrowseEngineTabRequest request, PendingReviewQueue queue, bool owns)
        : IBrowseNativeEngineTab, IBrowseOriginalPendingDownloadSource
    {
        internal bool Withdrawn;
        public bool IsOriginalPendingDownload(BrowserPendingAction action) => owns && !Withdrawn && action == queue.Owned && queue.Pending.Contains(action);
        public Control OriginalNativeView { get; } = new Border(); public bool CanRetireOriginalView => true;
        public BrowserSnapshot State { get; private set; } = new(request.InitialAddress, "Download review fixture", false, false, false, "Ready fixture");
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
        public ValueTask DisposeAsync() { Withdrawn = true; return ValueTask.CompletedTask; }
    }
}
