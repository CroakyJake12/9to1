using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;

namespace HavenOS.Apps.Browse;

/// <summary>Reachable CUI commands for page tools. A trusted native caller supplies the
/// SAME actual owner graph, retained original actor, UI dispatcher and canonical Home UI.</summary>
public sealed class BrowseOwnedToolsScene : IDisposable
{
    private readonly BrowseOwnedDocumentRegistry _documents;
    private readonly BrowseOwnedWebMcpBinding _owner;
    private readonly AuthenticatedResourceActor _originalActor;
    private readonly Func<Action, Task> _renderOnUi;
    private readonly Func<string, Task> _openActualHomeReview;
    private readonly object _tasksGate = new();
    private readonly object _stateGate = new();
    private long _discoveryVersion;
    private long _selectionVersion;
    private readonly HashSet<Task> _accepted = new();
    private readonly List<Pending> _pending = new();
    private IBrowseOwnedDocumentCatalogue? _catalogue;
    private BrowseOwnedToolWorkspace? _workspace;
    private int _disposed;
    private sealed record Pending(BrowseOwnedToolWorkspace Workspace, string RequestID);
    public Page Root { get; } = new() { Name = "Browse.Tools.Root", Layout = HavenLayout.Vertical };
    public Input Arguments { get; } = new() { Name = "Browse.Tools.Arguments", Text = "{}", Placeholder = "Tool arguments as JSON" };
    public Container Tools { get; } = new() { Name = "Browse.Tools.Discovered", Layout = HavenLayout.Vertical };
    public Container Reviews { get; } = new() { Name = "Browse.Tools.Reviews", Layout = HavenLayout.Vertical };
    public HuiText Status { get; } = new("Discover supported page tools.") { Name = "Browse.Tools.Status" };
    public HuiText ToolDeclaration { get; } = new("Select a displayed tool to inspect its untrusted declaration.") { Name = "Browse.Tools.Declaration" };
    public HuiButton DiscoverButton { get; }
    public HuiButton ReviewButton { get; }
    public BrowseOwnedToolsScene(BrowseOwnedDocumentRegistry documents, BrowseOwnedWebMcpBinding owner,
        AuthenticatedResourceActor actualOriginalActor, Func<Action, Task> renderOnActualUi,
        Func<string, Task> openActualHomeReview)
    {
        ArgumentNullException.ThrowIfNull(documents); ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(actualOriginalActor); ArgumentNullException.ThrowIfNull(renderOnActualUi);
        ArgumentNullException.ThrowIfNull(openActualHomeReview);
        _documents = documents; _owner = owner; _originalActor = actualOriginalActor;
        _renderOnUi = renderOnActualUi; _openActualHomeReview = openActualHomeReview;
        DiscoverButton = Button("Browse.Tools.Discover", "Discover page tools", DiscoverAsync);
        ReviewButton = Button("Browse.Tools.Review", "Review invocation in Home", ReviewAsync);
        Root.Add(DiscoverButton); Root.Add(Tools); Root.Add(ToolDeclaration); Root.Add(Arguments); Root.Add(ReviewButton); Root.Add(Reviews); Root.Add(Status);
    }
    public IReadOnlyList<string> PendingReviewIDs
    { get { lock (_stateGate) return _pending.Select(value => value.RequestID).ToArray(); } }
    public Task<BrowseWebMcpOwnerResult> FinishAuditAsync(string requestID, CancellationToken token = default)
    {
        Pending entry;
        lock (_stateGate) entry = _pending.SingleOrDefault(value => value.RequestID == requestID)
            ?? throw new UnauthorizedAccessException("This tool scene did not issue that review.");
        return entry.Workspace.FinishAuditAsync(requestID, token);
    }
    public Task WhenActionsIdleAsync()
    { lock (_tasksGate) return Task.WhenAll(_accepted.ToArray()); }
    private HuiButton Button(string name, string text, Func<Task> action)
    {
        var button = new HuiButton(text) { Name = name };
        button.Invoked += (_, _) => Accept(action);
        return button;
    }
    private void Accept(Func<Task> action)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_tasksGate) _accepted.Add(done.Task);
        _ = RunAsync(action, done);
    }
    private async Task RunAsync(Func<Task> action, TaskCompletionSource done)
    {
        try { await action(); }
        catch (Exception)
        {
            try { await RenderAsync(() => Status.Content = "This action is unavailable. Check the original page and Home review before trying again."); }
            catch (Exception) { } // Closed UI cannot discard the accepted pipeline completion.
        }
        finally { done.TrySetResult(); lock (_tasksGate) _accepted.Remove(done.Task); }
    }
    private Task RenderAsync(Action update) => _renderOnUi(() =>
    { if (Volatile.Read(ref _disposed) == 0) update(); });
    private async Task DiscoverAsync()
    {
        long version;
        lock (_stateGate)
        {
            version = ++_discoveryVersion;
            _selectionVersion++;
            _catalogue?.Dispose(); _catalogue = null; _workspace = null;
        }
        // Starts directly in the actual click pipeline. Registry captures original attached
        // host synchronously before its first await, retaining the original Home actor.
        var actual = await _documents.DiscoverForDisplayAsync(_originalActor);
        lock (_stateGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || version != _discoveryVersion)
            { actual.Catalogue.Dispose(); return; }
            _catalogue = actual.Catalogue;
        }
        await RenderAsync(() =>
        {
            if (version != Interlocked.Read(ref _discoveryVersion)) return;
            foreach (var previous in Tools.Children.ToArray()) Tools.Remove(previous);
            foreach (var tool in actual.Tools)
                Tools.Add(Button("Browse.Tools.Select." + tool.Name, tool.Name,
                    () => SelectAsync(actual.Catalogue, tool)));
            Status.Content = actual.Document.Supported
                ? "Choose a tool to inspect and review. Page declarations are untrusted."
                : "This browser or page does not support page tools.";
        });
    }
    private async Task SelectAsync(IBrowseOwnedDocumentCatalogue originalCatalogue, WebMcpTool actualDisplayedTool)
    {
        long version;
        lock (_stateGate)
        {
            if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(_catalogue, originalCatalogue))
                throw new UnauthorizedAccessException("The original displayed catalogue is unavailable.");
            version = ++_selectionVersion;
            _workspace = null; // An earlier visible declaration cannot review while this selection is loading.
        }
        var opened = await BrowseOwnedToolWorkspace.OpenAsync(_owner, originalCatalogue, actualDisplayedTool.Name);
        var published = false;
        try
        {
            await RenderAsync(() =>
            {
                lock (_stateGate)
                {
                    if (Volatile.Read(ref _disposed) != 0 || version != _selectionVersion
                        || !ReferenceEquals(_catalogue, originalCatalogue)) return;
                    ToolDeclaration.Content = actualDisplayedTool.Name + "\n" + actualDisplayedTool.Description
                        + "\nInput schema: " + actualDisplayedTool.InputSchema.GetRawText();
                    Status.Content = "Selected " + actualDisplayedTool.Name + ". Enter arguments and request Home review.";
                    // Publish the private review workspace in the SAME actual UI update
                    // as its declaration. Until then Review has no selected workspace.
                    _workspace = opened; published = true;
                }
            });
        }
        finally { if (!published) opened.Dispose(); }
    }
    private async Task ReviewAsync()
    {
        BrowseOwnedToolWorkspace selected;
        lock (_stateGate) selected = _workspace ?? throw new InvalidOperationException("Select an actual displayed tool first.");
        using var arguments = JsonDocument.Parse(Arguments.Text ?? "{}");
        var id = await selected.ReviewAsync(arguments.RootElement);
        // Retain an actually issued Home request even if close/navigation raced delivery.
        var pending = new Pending(selected, id);
        lock (_stateGate) _pending.Add(pending);
        await RenderAsync(() =>
        {
            var row = new Container { Layout = HavenLayout.Horizontal };
            row.Add(new HuiText("Home review " + id));
            row.Add(Button("Browse.Tools.Home." + id, "Open Home review", () => _openActualHomeReview(id)));
            row.Add(Button("Browse.Tools.Run." + id, "Run or observe", () => RunOrObserveAsync(pending)));
            row.Add(Button("Browse.Tools.Finish." + id, "Finish audit", () => FinishAsync(pending)));
            Reviews.Add(row); Status.Content = "Approve or decline this exact invocation in Home.";
        });
    }
    private async Task RunOrObserveAsync(Pending pending)
    {
        var result = await pending.Workspace.RunOrObserveAsync(pending.RequestID);
        await RenderAsync(() => Status.Content = result.Code == "WEBMCP_APPROVAL_PENDING"
            ? "This invocation is awaiting Home approval."
            : result.OutcomeKnown
            ? "The originating page reported an outcome."
            : "No confirmed outcome. A later observation will not run this invocation again.");
    }
    private async Task FinishAsync(Pending pending)
    {
        var result = await pending.Workspace.FinishAuditAsync(pending.RequestID);
        await RenderAsync(() => Status.Content = result.AuditRecorded
            ? "The retained outcome audit is recorded." : "The retained audit is not yet acknowledged.");
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (_stateGate)
        {
            _discoveryVersion++; _selectionVersion++;
            _catalogue?.Dispose(); _workspace?.Dispose();
            foreach (var pending in _pending) pending.Workspace.Dispose();
        }
        // Pending contexts/completion tasks remain retained; close never cancels a Home
        // request by itself or turns an unknown native evaluation into success/failure.
    }
}
