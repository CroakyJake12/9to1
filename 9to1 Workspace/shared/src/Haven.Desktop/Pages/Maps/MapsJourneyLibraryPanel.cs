using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Desktop.Views.Pages.Maps;

/// <summary>Native Journey library authoring over one original owning display.
/// This panel does not replace Maps' Data-backed saved places or Planner integration.</summary>
public sealed class MapsJourneyLibraryPanel : UserControl, IDisposable
{
    private readonly MapsJourneyLibraryWorkspace _workspace;
    private readonly Func<string, Task> _openHomeReview;
    private readonly HashSet<Task> _accepted = new();
    private readonly object _tasks = new();
    private bool _closed;
    private string? _requestID;
    public IReadOnlyList<MapsJourneyWorkspaceReview> Reviews => _workspace.Reviews;
    public TextBox NameInput { get; } = new() { PlaceholderText = "Journey name" };
    public TextBox InstructionInput { get; } = new() { PlaceholderText = "Manual instruction" };
    public StackPanel JourneyItems { get; } = new();
    public Button ReloadButton { get; } = new() { Content = "Reload Journey library" };
    public Button ReviewButton { get; } = new() { Content = "Review new Journey" };
    public Button HomeButton { get; } = new() { Content = "Open Home review" };
    public Button ApplyButton { get; } = new() { Content = "Apply approved Journey" };
    public Button FinishButton { get; } = new() { Content = "Finish observation or audit" };
    public TextBlock Status { get; } = new();
    public ComboBox ReviewSelection { get; } = new();
    private MapsJourneyLibraryPanel(MapsJourneyLibraryWorkspace workspace, Func<string, Task> openHomeReview)
    {
        _workspace = workspace; _openHomeReview = openHomeReview;
        var children = new StackPanel();
        children.Children.Add(new TextBlock { Text = "Journey library" });
        RenderJourneys(); children.Children.Add(JourneyItems); children.Children.Add(ReloadButton);
        children.Children.Add(NameInput); children.Children.Add(InstructionInput);
        children.Children.Add(ReviewButton); children.Children.Add(HomeButton);
        children.Children.Add(ReviewSelection);
        ReviewSelection.SelectionChanged += (_, _) => _requestID = ReviewSelection.SelectedItem as string;
        children.Children.Add(ApplyButton); children.Children.Add(FinishButton); children.Children.Add(Status);
        Content = children;
        ReloadButton.Click += (_, _) => Accept(ReloadAsync);
        ReviewButton.Click += (_, _) => Accept(ReviewAsync);
        HomeButton.Click += (_, _) => Accept(OpenHomeAsync);
        ApplyButton.Click += (_, _) => Accept(ApplyAsync);
        FinishButton.Click += (_, _) => Accept(FinishAsync);
    }
    public static async Task<MapsJourneyLibraryPanel> CreateAsync(HomeMapsLibraryOwner actualOwner,
        AuthenticatedResourceActor originalClickActor, Func<string, Task> openActualHomeReview,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(openActualHomeReview);
        var workspace = await MapsJourneyLibraryWorkspace.OpenAsync(actualOwner, originalClickActor, token).ConfigureAwait(false);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() => new MapsJourneyLibraryPanel(workspace, openActualHomeReview));
        }
        catch { workspace.Dispose(); throw; }
    }
    public static async Task<MapsJourneyLibraryPanel> CreateForOriginalDisplayAsync(HomeMapsLibraryOwner actualOwner,
        IMapsLibraryDisplay originalDisplay, Func<string, Task> openActualHomeReview, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(openActualHomeReview);
        var workspace = await MapsJourneyLibraryWorkspace.OpenForOriginalDisplayAsync(actualOwner, originalDisplay, token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            return await Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                return new MapsJourneyLibraryPanel(workspace, openActualHomeReview);
            });
        }
        catch { workspace.Dispose(); throw; }
    }
    // Actual accepted routed-click pipelines, captured after click. Completion is not
    // successful mutation, Home authority or acknowledgement of a future click.
    public Task WhenActionsIdleAsync()
    { lock (_tasks) return Task.WhenAll(_accepted.ToArray()); }
    private void Accept(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_tasks) _accepted.Add(completion.Task);
        _ = RunAcceptedAsync(action, completion);
    }
    private async Task RunAcceptedAsync(Func<Task> action, TaskCompletionSource completion)
    {
        try { await action(); }
        catch (Exception error) { if (!_closed) Status.Text = error.Message; }
        finally
        {
            completion.TrySetResult();
            lock (_tasks) _accepted.Remove(completion.Task);
        }
    }
    private void RenderJourneys()
    {
        JourneyItems.Children.Clear();
        foreach (var journey in _workspace.Snapshot.Journeys)
            JourneyItems.Children.Add(new TextBlock { Text = journey.Name, Tag = journey.JourneyId });
    }
    private async Task ReloadAsync()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        await _workspace.ReloadAsync();
        if (!_closed) { RenderJourneys(); Status.Text = "Journey library reloaded."; }
    }
    private async Task ReviewAsync()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        var now = DateTimeOffset.UtcNow;
        var proposal = new MapSavedJourney(Guid.NewGuid(), NameInput.Text ?? "",
            [new(Guid.NewGuid(), MapJourneyStepKind.ManualInstruction, InstructionInput.Text ?? "")],
            MapObjectVisibility.Private, now, now, 0);
        var issuedID = await _workspace.ReviewSaveAsync(proposal);
        _requestID = issuedID;
        if (!_closed)
        {
            ReviewSelection.ItemsSource = _workspace.Reviews.Select(value => value.RequestID).ToArray();
            ReviewSelection.SelectedItem = issuedID;
            Status.Text = "Home review: " + issuedID;
        }
    }
    private Task OpenHomeAsync()
        => _requestID is null ? Task.CompletedTask : _openHomeReview(_requestID);
    private async Task ApplyAsync()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_requestID is null) return;
        var observed = await _workspace.ApplyOrRecoverAsync(_requestID);
        if (!_closed) Status.Text = observed.Code;
    }
    private async Task FinishAsync()
    {
        if (_requestID is null) return;
        var observed = await _workspace.FinishAsync(_requestID);
        if (!_closed) Status.Text = observed.Code;
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _workspace.Dispose();
        ReviewButton.IsEnabled = false; ApplyButton.IsEnabled = false; ReloadButton.IsEnabled = false;
        // Keep accepted completion tasks and original request handles for audit recovery.
    }
}
