using CakeOS.Apps.Boards.Contract;
using Haven.UI;
using Haven.UI.Components;
using HavenButton = Haven.UI.Components.Button;
using HavenText = Haven.UI.Components.Text;

namespace CakeOS.Apps.Boards.Hui;

public delegate void HavenBoardPlanRequestedHandler(object? sender, Guid planId);

/// <summary>
/// HUI-only review surface for a detached generative plan preview.
/// It never applies commands itself and emits only the opaque coordinator-owned plan ID.
/// </summary>
public sealed class HavenBoardGenerativeReviewHuiScene : IDisposable
{
    private Guid? _activePlanId;
    private bool _disposed;

    public HavenBoardGenerativeReviewHuiScene()
    {
        Root = new Page { Name = "BoardsGenerativeReviewRoot", Layout = HavenLayout.Vertical };
        Root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        Root.SetValue(HavenProperties.Padding, HavenThickness.Uniform(HavenLength.Px(20)));
        Root.SetValue(HavenProperties.Gap, HavenLength.Px(12));
        Root.SetValue(HavenProperties.Background, "Surface");
        Root.Accessibility.AccessibleName = "Review generated board changes";

        Title = new HavenText { Name = "GenerativeReviewTitle", Content = "Review generated board changes" };
        Title.SetValue(HavenProperties.FontSize, 20d);
        Title.SetValue(HavenProperties.FontWeight, 700);
        Root.Add(Title);

        Summary = new HavenText { Name = "GenerativeReviewSummary", Content = "No generated changes awaiting review." };
        Summary.SetValue(HavenProperties.Foreground, "TextSecondary");
        Root.Add(Summary);

        CommandList = new Container { Name = "GenerativeReviewCommands", Layout = HavenLayout.Vertical };
        CommandList.SetValue(HavenProperties.Gap, HavenLength.Px(6));
        CommandList.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        Root.Add(CommandList);

        var actions = new Container { Name = "GenerativeReviewActions", Layout = HavenLayout.Horizontal };
        actions.SetValue(HavenProperties.Gap, HavenLength.Px(8));

        ApplyButton = new HavenButton { Name = "GenerativeReviewApply", Content = "Apply", Variant = ButtonVariant.Primary };
        ApplyButton.Accessibility.AccessibleName = "Apply generated board changes";
        ApplyButton.Invoked += OnApplyInvoked;
        SetEnabled(ApplyButton, false);
        actions.Add(ApplyButton);

        CancelButton = new HavenButton { Name = "GenerativeReviewCancel", Content = "Cancel", Variant = ButtonVariant.Secondary };
        CancelButton.Accessibility.AccessibleName = "Cancel generated board changes";
        CancelButton.Invoked += OnCancelInvoked;
        SetEnabled(CancelButton, false);
        actions.Add(CancelButton);
        Root.Add(actions);

        Status = new HavenText { Name = "GenerativeReviewStatus", Content = string.Empty };
        Status.SetValue(HavenProperties.Foreground, "TextSecondary");
        Status.SetValue(HavenProperties.Visibility, HavenVisibility.Collapsed);
        Root.Add(Status);
    }

    public Page Root { get; }
    public HavenText Title { get; }
    public HavenText Summary { get; }
    public Container CommandList { get; }
    public HavenButton ApplyButton { get; }
    public HavenButton CancelButton { get; }
    public HavenText Status { get; }

    public event HavenBoardPlanRequestedHandler? ApplyRequested;
    public event HavenBoardPlanRequestedHandler? CancelRequested;

    public void SetPlan(HavenBoardGenerativePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ThrowIfDisposed();

        _activePlanId = plan.Id;
        Summary.Content = $"{plan.Commands.Count} proposed change{(plan.Commands.Count == 1 ? string.Empty : "s")} · board version {plan.BaseVersion}";
        ClearCommands();
        for (var index = 0; index < plan.Commands.Count; index++)
        {
            var item = new HavenText
            {
                Name = $"GenerativeReviewCommand_{index + 1}",
                Content = $"{index + 1}. {Describe(plan.Commands[index])}"
            };
            item.Accessibility.AccessibleName = item.Content;
            CommandList.Add(item);
        }

        SetEnabled(ApplyButton, true);
        SetEnabled(CancelButton, true);
        SetStatus("Review required before changes are saved.");
    }

    public void ClearPlan(string? status = null)
    {
        ThrowIfDisposed();
        _activePlanId = null;
        Summary.Content = "No generated changes awaiting review.";
        ClearCommands();
        SetEnabled(ApplyButton, false);
        SetEnabled(CancelButton, false);
        SetStatus(status);
    }

    public void SetStatus(string? status)
    {
        ThrowIfDisposed();
        Status.Content = status ?? string.Empty;
        Status.SetValue(
            HavenProperties.Visibility,
            string.IsNullOrWhiteSpace(status) ? HavenVisibility.Collapsed : HavenVisibility.Visible);
    }

    private void OnApplyInvoked(object? sender, EventArgs args)
    {
        if (_activePlanId is Guid planId)
            ApplyRequested?.Invoke(this, planId);
    }

    private void OnCancelInvoked(object? sender, EventArgs args)
    {
        if (_activePlanId is Guid planId)
            CancelRequested?.Invoke(this, planId);
    }

    private void ClearCommands()
    {
        foreach (var child in CommandList.Children.ToArray())
            CommandList.Remove(child);
    }

    private static string Describe(HavenBoardCommand command) => command switch
    {
        CreateCardCommand create => $"Create card '{create.Title}'",
        RenameCardCommand renameCard => $"Rename card to '{renameCard.Title}'",
        RemoveCardCommand => "Delete a card",
        CreateGroupCommand createGroup => $"Create group '{createGroup.Title}'",
        RemoveGroupCommand => "Delete a group",
        RenameGroupCommand rename => $"Rename group to '{rename.Title}'",
        MoveGroupCommand => "Reorder a group",
        MoveCardCommand => "Move a card",
        SetCardParentCommand parent => parent.ParentCardId is null ? "Remove card nesting" : "Change card nesting",
        SetFreeformCardFrameCommand => "Position a card on the freeform board",
        RemoveFreeformCardFrameCommand => "Remove a card from the freeform layout",
        _ => "Unsupported change"
    };

    private static void SetEnabled(HavenElement element, bool enabled)
    {
        element.SetValue(HavenProperties.Enabled, enabled);
        element.Accessibility.Enabled = enabled;
        element.SetState(HavenElementState.Disabled, !enabled);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ApplyButton.Invoked -= OnApplyInvoked;
        CancelButton.Invoked -= OnCancelInvoked;
        ApplyRequested = null;
        CancelRequested = null;
    }
}
