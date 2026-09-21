namespace Haven.CUI.DevTools;

public sealed record LayoutSnapshot(
    CuiRect Bounds,
    CuiSize DesiredSize,
    CuiSize MinimumSize,
    CuiSize MaximumSize,
    CuiThickness Margin,
    string HorizontalAlignment,
    string VerticalAlignment,
    bool ClipsToBounds,
    string? Transform);

public sealed record StyleValueSnapshot(string Property, string Value, string? Rule, bool IsOverridden, bool IsInherited);

public sealed record StyleSnapshot(
    string Selector,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> PseudoClasses,
    IReadOnlyList<StyleValueSnapshot> Values);

public sealed record ResourceValueSnapshot(string Key, string Value, string Scope, bool WasFound);

public sealed record ResourceSnapshot(IReadOnlyList<ResourceValueSnapshot> Values);

public enum BindingStatus
{
    Active,
    Pending,
    MissingSource,
    InvalidPath,
    ConversionFailed,
    Faulted
}

public sealed record BindingValueSnapshot(
    string TargetProperty,
    string Path,
    string? SourceType,
    string? Value,
    BindingStatus Status,
    string? Error);

public sealed record BindingSnapshot(IReadOnlyList<BindingValueSnapshot> Values);

public sealed record StateSnapshot(
    IReadOnlyDictionary<string, string?> Values,
    IReadOnlyList<string> ActiveStates,
    long Revision);

public sealed record EventSubscriptionSnapshot(string Event, string Handler, string Phase, bool IsEnabled);

public sealed record EventOccurrenceSnapshot(string Event, DateTimeOffset Timestamp, bool Handled, string? Detail);

public sealed record EventSnapshot(
    IReadOnlyList<EventSubscriptionSnapshot> Subscriptions,
    IReadOnlyList<EventOccurrenceSnapshot> RecentEvents);

public sealed record FocusSnapshot(
    bool IsFocusable,
    bool IsFocused,
    bool ContainsFocus,
    int? TabIndex,
    string? FocusReason,
    IReadOnlyList<ElementId> FocusPath);

public sealed record AccessibilitySnapshot(
    string Role,
    string? Name,
    string? Description,
    string? Value,
    IReadOnlyList<string> Actions,
    IReadOnlyDictionary<string, string> States,
    bool IsHidden);

public sealed record InputSnapshot(
    bool IsHitTestVisible,
    bool IsPointerOver,
    bool CapturesPointer,
    IReadOnlyList<string> GestureRecognizers,
    IReadOnlyList<string> KeyboardShortcuts,
    string? Cursor);

public sealed record InvalidationReasonSnapshot(string Reason, long Count, DateTimeOffset LastOccurrence);

public sealed record InvalidationSnapshot(
    long MeasureCount,
    long ArrangeCount,
    long PaintCount,
    IReadOnlyList<InvalidationReasonSnapshot> Reasons);

public sealed record PerformanceSnapshot(
    double LastMeasureMilliseconds,
    double LastArrangeMilliseconds,
    double LastPaintMilliseconds,
    double AverageFrameMilliseconds,
    long DrawCommandCount,
    long EstimatedRetainedBytes,
    DateTimeOffset CapturedAt);

public sealed record PlatformSnapshot(
    string OperatingSystem,
    string Runtime,
    string Renderer,
    string Host,
    double Scale,
    IReadOnlyDictionary<string, bool> Capabilities);

public sealed record ElementDiagnosticsSnapshot(
    ElementId ElementId,
    long TreeRevision,
    CuiSourceLocation? Source,
    LayoutSnapshot Layout,
    StyleSnapshot Style,
    ResourceSnapshot Resources,
    BindingSnapshot Bindings,
    StateSnapshot State,
    EventSnapshot Events,
    FocusSnapshot Focus,
    AccessibilitySnapshot Accessibility,
    InputSnapshot Input,
    InvalidationSnapshot Invalidation,
    PerformanceSnapshot Performance,
    PlatformSnapshot Platform);

public interface IElementDiagnosticsSnapshotProvider
{
    ValueTask<ElementDiagnosticsSnapshot?> CaptureAsync(
        ElementId elementId,
        long treeRevision,
        CancellationToken cancellationToken = default);
}
