using System.Globalization;
using Avalonia.Automation;
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
    private readonly Dictionary<string, MapSavedJourney> _proposals = new();
    public IReadOnlyList<MapsJourneyWorkspaceReview> Reviews => _workspace.Reviews;
    public TextBox NameInput { get; } = new() { PlaceholderText = "Journey name" };
    public TextBox InstructionInput { get; } = new() { PlaceholderText = "Instruction or activity" };
    public ComboBox StepTypeInput { get; } = new() { ItemsSource = Array.AsReadOnly(new[] { "Instruction", "Wait", "Activity" }), SelectedIndex = 0 };
    public ComboBox StepIntentInput { get; } = new() { ItemsSource = Array.AsReadOnly(new[] { "Required", "Preferred", "Optional" }), SelectedIndex = 0 };
    public TextBox DurationInput { get; } = new() { Text = "5", PlaceholderText = "Duration in minutes", IsEnabled = false };
    public TextBlock ReviewDetails { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly List<MapJourneyStep> _draftSteps = new();
    public ListBox DraftSteps { get; } = new();
    public Button AddStepButton { get; } = new() { Content = "Add current step" };
    public Button RemoveStepButton { get; } = new() { Content = "Remove selected step" };
    public Button MoveStepUpButton { get; } = new() { Content = "Move step up" };
    public Button MoveStepDownButton { get; } = new() { Content = "Move step down" };
    public TextBox SearchInput { get; } = new() { Name = "journey-library-search", PlaceholderText = "Search displayed Journeys", MaxLength = 4096 };
    private IReadOnlyList<MapSavedJourney> _displayedJourneys = Array.Empty<MapSavedJourney>();
    public ComboBox JourneySelection { get; } = new() { DisplayMemberBinding = new Avalonia.Data.Binding(nameof(MapSavedJourney.Name)) };
    public TextBlock JourneyDetails { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
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
        AutomationProperties.SetName(SearchInput, "Search displayed Journeys");
        SearchInput.PropertyChanged += (_, change) =>
        {
            if (!_closed && change.Property == TextBox.TextProperty) RenderJourneys();
        };
        JourneySelection.SelectionChanged += (_, _) => RenderSelectedJourney();
        AutomationProperties.SetName(JourneySelection, "Inspect displayed Journey");
        children.Children.Add(SearchInput);
        RenderJourneys(); children.Children.Add(JourneyItems); children.Children.Add(JourneySelection);
        children.Children.Add(JourneyDetails); children.Children.Add(ReloadButton);
        children.Children.Add(NameInput);
        children.Children.Add(new TextBlock { Text = "Step type" }); children.Children.Add(StepTypeInput);
        children.Children.Add(InstructionInput);
        children.Children.Add(new TextBlock { Text = "Duration in minutes" }); children.Children.Add(DurationInput);
        children.Children.Add(new TextBlock { Text = "Step intent" }); children.Children.Add(StepIntentInput);
        AutomationProperties.SetName(StepTypeInput, "Journey step type");
        AutomationProperties.SetName(StepIntentInput, "Journey step intent");
        AutomationProperties.SetName(DurationInput, "Journey step duration in minutes");
        StepTypeInput.SelectionChanged += (_, _) => DurationInput.IsEnabled = !_closed && StepTypeInput.SelectedIndex is 1 or 2;
        children.Children.Add(AddStepButton); children.Children.Add(DraftSteps);
        children.Children.Add(RemoveStepButton); children.Children.Add(MoveStepUpButton); children.Children.Add(MoveStepDownButton);
        AutomationProperties.SetName(DraftSteps, "Ordered journey draft steps");
        AddStepButton.Click += (_, _) => Accept(AddStepAsync);
        RemoveStepButton.Click += (_, _) => Accept(() => ChangeDraftAsync(0));
        MoveStepUpButton.Click += (_, _) => Accept(() => ChangeDraftAsync(-1));
        MoveStepDownButton.Click += (_, _) => Accept(() => ChangeDraftAsync(1));
        children.Children.Add(ReviewButton); children.Children.Add(HomeButton);
        children.Children.Add(ReviewSelection);
        ReviewSelection.SelectionChanged += (_, _) =>
        {
            _requestID = ReviewSelection.SelectedItem as string;
            if (!_closed) RenderReviewDetails();
        };
        children.Children.Add(ReviewDetails); children.Children.Add(ApplyButton); children.Children.Add(FinishButton); children.Children.Add(Status);
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
        var query = (SearchInput.Text ?? "").Trim();
        _displayedJourneys = query.Length > 4096 ? Array.Empty<MapSavedJourney>()
            : Array.AsReadOnly(_workspace.Snapshot.Journeys.Where(journey => query.Length == 0
                || journey.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || journey.Steps.Any(step => step.Instruction.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray());
        // Refresh retires selection instead of adopting a same-ID replacement snapshot.
        JourneySelection.SelectedItem = null;
        JourneyDetails.Text = "";
        JourneySelection.ItemsSource = _displayedJourneys;
        foreach (var journey in _displayedJourneys)
            JourneyItems.Children.Add(new TextBlock { Text = journey.Name, Tag = journey.JourneyId });
    }
    private void RenderSelectedJourney()
    {
        if (_closed) return;
        if (JourneySelection.SelectedItem is not MapSavedJourney selected
            || !_displayedJourneys.Any(journey => ReferenceEquals(journey, selected)))
        { JourneyDetails.Text = ""; return; }
        JourneyDetails.Text = selected.Name + Environment.NewLine + string.Join(Environment.NewLine,
            selected.Steps.Select((step, index) => $"{index + 1}. {step.Kind}: {step.Instruction} ({step.Intent})"
                + (step.Duration is { } duration ? $" — {duration:c}" : "")));
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
        // Copy the ordered draft before the actual owner/Home await. Later edits cannot rewrite this review.
        var steps = _draftSteps.Count == 0 ? new[] { CaptureStep() } : _draftSteps.ToArray();
        var now = DateTimeOffset.UtcNow;
        var proposal = new MapSavedJourney(Guid.NewGuid(), NameInput.Text ?? "",
            Array.AsReadOnly(steps),
            MapObjectVisibility.Private, now, now, 0);
        MapJourneyLogic.Validate(proposal);
        var issuedID = await _workspace.ReviewSaveAsync(proposal);
        _requestID = issuedID;
        if (!_closed)
        {
            _proposals[issuedID] = proposal;
            ReviewSelection.ItemsSource = _workspace.Reviews.Select(value => value.RequestID).ToArray();
            ReviewSelection.SelectedItem = issuedID;
            RenderReviewDetails();
            Status.Text = "Home review: " + issuedID;
        }
    }
    private MapJourneyStep CaptureStep()
    {
        var kind = StepTypeInput.SelectedIndex switch
        { 0 => MapJourneyStepKind.ManualInstruction, 1 => MapJourneyStepKind.Wait, 2 => MapJourneyStepKind.Activity,
            _ => throw new InvalidOperationException("Choose a supported journey step type.") };
        if (StepIntentInput.SelectedIndex is < 0 or > 2) throw new InvalidOperationException("Choose a journey step intent.");
        var intent = (MapStepIntent)StepIntentInput.SelectedIndex;
        TimeSpan? duration = null;
        if (kind != MapJourneyStepKind.ManualInstruction && !string.IsNullOrWhiteSpace(DurationInput.Text))
        {
            if (!decimal.TryParse(DurationInput.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var minutes)
                || minutes < 0 || minutes > 1440)
                throw new InvalidOperationException("Enter a duration from zero to 1,440 minutes.");
            duration = TimeSpan.FromTicks(decimal.ToInt64(minutes * TimeSpan.TicksPerMinute));
        }
        if (kind == MapJourneyStepKind.Wait && duration is not { Ticks: > 0 })
            throw new InvalidOperationException("A wait step needs a positive duration in minutes.");
        var step = new MapJourneyStep(Guid.NewGuid(), kind, InstructionInput.Text ?? "", Intent: intent, Duration: duration);
        var now = DateTimeOffset.UtcNow;
        MapJourneyLogic.Validate(new(Guid.NewGuid(), "Draft validation", Array.AsReadOnly(new[] { step }),
            MapObjectVisibility.Private, now, now, 0));
        return step;
    }
    private Task AddStepAsync()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_draftSteps.Count >= 256) throw new InvalidOperationException("A native journey draft supports up to 256 steps.");
        _draftSteps.Add(CaptureStep()); RenderDraft(_draftSteps.Count - 1);
        return Task.CompletedTask;
    }
    private Task ChangeDraftAsync(int direction)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        var index = DraftSteps.SelectedIndex;
        if (index < 0 || index >= _draftSteps.Count) return Task.CompletedTask;
        if (direction == 0) { _draftSteps.RemoveAt(index); RenderDraft(Math.Min(index, _draftSteps.Count - 1)); }
        else if (index + direction >= 0 && index + direction < _draftSteps.Count)
        {
            (_draftSteps[index], _draftSteps[index + direction]) = (_draftSteps[index + direction], _draftSteps[index]);
            RenderDraft(index + direction);
        }
        return Task.CompletedTask;
    }
    private void RenderDraft(int selected)
    {
        DraftSteps.ItemsSource = _draftSteps.Select((step, index) => $"{index + 1}. {step.Kind}: {step.Instruction}").ToArray();
        DraftSteps.SelectedIndex = selected;
    }
    private void RenderReviewDetails()
    {
        ReviewDetails.Text = _requestID is not null && _proposals.TryGetValue(_requestID, out var original)
            ? original.Name + Environment.NewLine + string.Join(Environment.NewLine, original.Steps.Select(step =>
                $"{(step.Kind == MapJourneyStepKind.ManualInstruction ? "Instruction" : step.Kind.ToString())}: {step.Instruction} — {step.Intent}" +
                (step.Duration is { } duration ? $"; {duration.TotalMinutes.ToString("0.########", CultureInfo.CurrentCulture)} minutes" : "")))
            : "";
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
        _closed = true; _workspace.Dispose(); _proposals.Clear(); SearchInput.IsEnabled = false;
        JourneySelection.IsEnabled = false; JourneyDetails.Text = "";
        AddStepButton.IsEnabled = false; RemoveStepButton.IsEnabled = false;
        MoveStepUpButton.IsEnabled = false; MoveStepDownButton.IsEnabled = false; DraftSteps.IsEnabled = false;
        StepTypeInput.IsEnabled = false; StepIntentInput.IsEnabled = false; DurationInput.IsEnabled = false;
        ReviewButton.IsEnabled = false; ApplyButton.IsEnabled = false; ReloadButton.IsEnabled = false;
        // Keep accepted completion tasks and original request handles for audit recovery.
    }
}
