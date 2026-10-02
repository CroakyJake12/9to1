namespace Haven.Application.Canvas;

/// <summary>Structural limits shared by authoring previews and canonical transactions.
/// Passing these limits does not establish semantic validity, owner admission, permissions,
/// native donor correspondence, or permission to publish. Count actual retained entries,
/// not a caller-reported Count, and stop after one sentinel beyond the limit.</summary>
public static class CanvasStructuredStrokeTransactionLimits
{
    /// <summary>Each replacement, removal, or batch deletion set is independently bounded.</summary>
    public const int MaximumEntriesPerSet = 1024;
    public const int MaximumSegmentsPerPath = 8192;
    /// <summary>Sum of Samples.Count plus PathGeometry.Segments.Count across all replacement
    /// proposals. Repeated original input provenance in separate fragments counts each time.
    /// Start/control records are bounded separately by the fixed segment shape.</summary>
    public const int MaximumAggregateSampleAndSegmentEntries = 131072;
}
