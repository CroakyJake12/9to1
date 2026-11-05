using CakeOS.Apps.Boards.Contract;

// Minimal rich-notes session contract for the Boards CUI app.
// The persistence worker owns the real `RichBoardSession` (schema v2 `.9to1board`).
// This interface keeps all persistence access behind a one-line swap:
// register the real session where the app currently registers InMemoryRichBoardSession.

namespace CakeOS.Apps.Boards.App;

/// <summary>Optional lifetime signal used by UI event adapters to ignore queued events after owner disposal.</summary>
internal interface IBoardSessionLifetime
{
    bool IsDisposed { get; }
}

/// <summary>Document model surfaced to the CUI Boards editor.</summary>
public sealed class RichBoardDocument
{
    public string Title { get; set; } = "Untitled board";
    /// <summary>Board identity icon (canonical v2 line 9421 <c>Icon?</c>), projected for the landing card
    /// and the board switcher. The contract remains the owner: a view-model copy never writes it back.</summary>
    public string? IconKey { get; set; }
    public string? IconColor { get; set; }
    public string? IconEmoji { get; set; }
    /// <summary>Emoji used as an icon in this board, most recent first, projected from the contract so the
    /// picker's Recent section is the same data on screen and on disk.</summary>
    public List<string> RecentIconEmoji { get; set; } = [];
    public List<RichBoardSection> Sections { get; set; } = [];
    /// <summary>
    /// Trashed pages available for restore, projected from the contract so the panel cannot show something
    /// the file does not hold. Group roots only: a trashed subtree is offered as one action.
    /// </summary>
    public List<RichBoardTrashEntry> Trash { get; set; } = [];
}

/// <summary>One row in the Trash panel.</summary>
public sealed class RichBoardTrashEntry
{
    public required string PageId { get; init; }
    /// <summary>The trashed action this page travelled with, so a subtree restores as one unit.</summary>
    public required string GroupId { get; init; }
    public required string Title { get; init; }
    public string? IconKey { get; init; }
    public string? IconEmoji { get; init; }
    public required string SectionId { get; init; }
    public required string SectionTitle { get; init; }
    public int SubtreePageCount { get; init; }
    public int BlockCount { get; init; }
    public DateTimeOffset TrashedUtc { get; init; }

    /// <summary>
    /// The pages this Trash row is holding: the row's page plus everything under it, each with the parent and
    /// position it had when it left navigation. It lives on the row rather than in a side list because the
    /// document is what undo and redo restore - a retained page kept outside the document would survive an undo
    /// as a duplicate waiting to be restored twice. A store-backed session leaves this empty and restores
    /// through the contract instead.
    /// </summary>
    public List<RichBoardTrashedPage> Retained { get; init; } = [];

    public string TrashedLabel => TrashedUtc.LocalDateTime.ToString("g");
    public string IconGlyph => IconKey == CakeOS.Apps.Boards.Contract.HavenRichSectionIcons.Emoji
        ? IconEmoji ?? string.Empty
        : string.Empty;
    public bool IconIsEmoji => IconKey == CakeOS.Apps.Boards.Contract.HavenRichSectionIcons.Emoji;
}

/// <summary>One retained page inside a Trash row, with where it was.</summary>
public sealed class RichBoardTrashedPage
{
    public required RichBoardPage Page { get; init; }
    public string? ParentPageId { get; init; }
    public int SortIndex { get; init; }
}

/// <summary>A section (group of pages) in the rich-notes document.</summary>
public sealed class RichBoardSection
{
    // Canonical Board identity, matching the contract model (HavenRichBoardContract.cs:31 "sec-" + 12).
    public string Id { get; set; } = "sec-" + Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "Untitled section";
    public string IconKey { get; set; } = HavenRichSectionIcons.Default;
    public string? IconColor { get; set; }
    /// <summary>Unicode emoji kept when <see cref="IconKey"/> is "emoji".</summary>
    public string? IconEmoji { get; set; }
    public HavenRichSectionGeneratedIcon? GeneratedIcon { get; set; }
    public List<RichBoardPage> Pages { get; set; } = [];
}

/// <summary>A page holding an ordered block list.</summary>
public sealed class RichBoardPage
{
    public string? TabberOwnerBlockId { get; set; }
    // Canonical Board identity, matching the contract model (HavenRichBoardContract.cs:72 "page-" + 12).
    public string Id { get; set; } = "page-" + Guid.NewGuid().ToString("N")[..12];
    public string? ParentPageId { get; set; }
    public string Title { get; set; } = "Untitled page";
    /// <summary>
    /// Page identity icon (canonical v2 line 9423 <c>Icon?</c>). The vocabulary and the rules are the
    /// section's, so one picker and one renderer serve both; "file" is what every page drew before the
    /// field existed and remains the default.
    /// </summary>
    public string IconKey { get; set; } = HavenRichSectionIcons.PageDefault;
    public string? IconColor { get; set; }
    /// <summary>Unicode emoji kept when <see cref="IconKey"/> is "emoji".</summary>
    public string? IconEmoji { get; set; }
    public string PageType { get; set; } = HavenRichPageTypes.Native;
    public Haven.Application.BoardEmbeddedArtifactReference? EmbeddedArtifact { get; set; }
    public bool IsCanvasLocked { get; set; }
    /// <summary>The page's independent Snap aid (see the contract page property of the same name):
    /// where a spatial manipulation commits, never whether it is allowed.</summary>
    public bool SnapToGrid { get; set; }
    public string LayoutMode { get; set; } = HavenRichPageLayoutModes.Locked;
    public List<RichBoardBlock> Blocks { get; set; } = [];
    public double InkPanX { get; set; }
    public double InkPanY { get; set; }
    public double InkZoom { get; set; } = 1;
}

/// <summary>One editable block: heading, paragraph, checklist item, table, ink, image, graph, divider.</summary>
public sealed class RichBoardBlock
{
    /// <summary>
    /// Canonical Board block identity, the same shape the contract model uses
    /// (HavenRichBoardContract.cs:174 "block-" + 12 hex digits). The bare 32-hex default made every app
    /// -created block that did not set an Id explicitly - checklist rows, tab panes, freeform boxes,
    /// conversion-created blocks and database links - diverge in identity shape from inserted paragraphs,
    /// which is the divergence the carried-forward Boards review item names. Nothing in Boards parses a
    /// block id as a Guid, and retained documents keep whatever id string they already have, so the
    /// prefix is safe for existing boards.
    /// </summary>
    public string Id { get; set; } = "block-" + Guid.NewGuid().ToString("N")[..12];
    public string Kind { get; set; } = "paragraph";
    public string Layout { get; set; } = HavenRichBlockLayouts.Flow;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 120;
    public int ZOrder { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<HavenRichTextRun> Runs { get; set; } = [];
    public string StyleId { get; set; } = "normal";
    public bool IsChecked { get; set; }
    public int Level { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public string Baseline { get; set; } = "normal";
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string Foreground { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string Alignment { get; set; } = "inherit";
    public double LineSpacing { get; set; }
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; }
    public int IndentLevel { get; set; } = -1;
    public Dictionary<string, string> TableCells { get; set; } = new(StringComparer.Ordinal);
    public RichTableStyleView TableStyle { get; set; } = new();
    public Dictionary<string, RichCellStyleView> CellStyles { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, RichTableCellSpanView> CellSpans { get; set; } = new(StringComparer.Ordinal);
    public bool HasMergedCells { get; set; }
    public int TableRows { get; set; }
    public int TableCols { get; set; }
    public List<double> ColumnWidths { get; set; } = [];
    public List<double> RowHeights { get; set; } = [];
    public RichImageView? Image { get; set; }
    public RichDividerView? Divider { get; set; }
    public RichGraphView? Graph { get; set; }
    public HavenBoardDatabaseReference? Database { get; set; }
    public HavenTabber? Tabber { get; set; }
    public string? AttachmentId { get; set; }
    public string? AttachmentName { get; set; }
    public long AttachmentSize { get; set; }
    public int InkStrokeCount { get; set; }
}

/// <summary>Read-only page-level drawing data for the overlay renderer.</summary>
public sealed class InkStrokeView
{
    public List<InkPointView> Points { get; set; } = [];
    public double Width { get; set; } = 2.5;
    public string Color { get; set; } = "#FF111111";
    public string Tool { get; set; } = "Pen";
    public bool Selected { get; set; }
}

public sealed class InkPointView
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Pressure { get; set; } = 0.5;
}

/// <summary>Table-level formatting surfaced to the editor.</summary>
public sealed class RichTableStyleView
{
    public string Background { get; set; } = string.Empty;
    public string BorderColor { get; set; } = string.Empty;
    public double BorderWidth { get; set; }
    public string BorderStyle { get; set; } = "inherit";
    public double CornerRadius { get; set; }
    public bool AlternatingRows { get; set; }
    public bool HasHeaderRow { get; set; }
    public bool HasHeaderColumn { get; set; }
    public double Width { get; set; }
    public string Alignment { get; set; } = "inherit";
}

/// <summary>Per-cell formatting surfaced to the editor.</summary>
public sealed class RichCellStyleView
{
    public string AlignH { get; set; } = "inherit";
    public string AlignV { get; set; } = "inherit";
    public string Background { get; set; } = string.Empty;
    public string Foreground { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string Baseline { get; set; } = "normal";
}

/// <summary>Structural table-cell extent projected from the canonical cell IDs.</summary>
public sealed class RichTableCellSpanView
{
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
    public string? CoveredByCellId { get; set; }
    public bool CanMergeRight { get; set; }
    public bool CanMergeDown { get; set; }
    public bool CanSplit { get; set; }
}

/// <summary>Image payload surfaced to the editor (bytes only while importing).</summary>
public sealed class RichImageView
{
    public string DisplayName { get; set; } = "image";
    public string MediaType { get; set; } = "image/png";
    public string? DataBase64 { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool KeepAspectRatio { get; set; } = true;
    public double RotationDegrees { get; set; }
    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    public string FitMode { get; set; } = "contain";
    public string Alignment { get; set; } = "inherit";
    public string AltText { get; set; } = string.Empty;
    public string Caption { get; set; } = string.Empty;
    public string LinkUri { get; set; } = string.Empty;
}

public sealed class RichDividerView
{
    public double Thickness { get; set; } = 1;
    public string LineStyle { get; set; } = "solid";
    public string Color { get; set; } = "#FF5F6368";
}

/// <summary>Editable graph state surfaced to the editor (never a flattened image).</summary>
public sealed class RichGraphView
{
    public string GraphId { get; set; } = "graph-" + Guid.NewGuid().ToString("N")[..12];
    public List<RichGraphExpressionView> Expressions { get; set; } = [];
    public List<RichGraphSliderView> Sliders { get; set; } = [];
    public double XMin { get; set; } = -10;
    public double XMax { get; set; } = 10;
    public double YMin { get; set; } = -10;
    public double YMax { get; set; } = 10;
    public bool ShowGrid { get; set; } = true;
    public bool ShowAxes { get; set; } = true;
    public string XLabel { get; set; } = "x";
    public string YLabel { get; set; } = "y";
    public List<RichGraphPointView> Points { get; set; } = [];
}

public sealed class RichGraphSliderView
{
    public string VariableId { get; set; } = string.Empty;
    public string Name { get; set; } = "a";
    public double Minimum { get; set; } = -5;
    public double Maximum { get; set; } = 5;
    public double Step { get; set; } = 0.1;
    public double DefaultValue { get; set; }
}

public sealed class RichGraphExpressionView
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "y = x";
    public bool Visible { get; set; } = true;
    public string Color { get; set; } = "#FF1A73E8";
    public double LineWidth { get; set; } = 2;
    public double? DomainMin { get; set; }
    public double? DomainMax { get; set; }
}

public sealed class RichGraphPointView
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>Style catalog entry for the style/add menus.</summary>
public sealed class RichStyleView
{
    public string Id { get; set; } = "normal";
    public string Name { get; set; } = "Paragraph";
    public bool IsBuiltIn { get; set; } = true;
    public string BlockKind { get; set; } = "paragraph";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public string Baseline { get; set; } = "normal";
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public double LineSpacing { get; set; }
    public string Foreground { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string Alignment { get; set; } = "inherit";
    public int IndentLevel { get; set; } = -1;
}

/// <summary>
/// Minimal session surface the Boards app codes against.
/// Expected real implementation: `RichBoardSession` in
/// `CakeOS.Apps.Boards.Contract` with OpenAsync/SaveAsync/DisposeAsync,
/// Document/Snapshot/Status, and a status event.
/// </summary>
public interface IRichBoardSession : IAsyncDisposable
{
    RichBoardDocument Document { get; }
    string Snapshot { get; }
    string Status { get; }
    string? FilePath { get; }
    event EventHandler<string>? StatusChanged;
    ValueTask OpenAsync(string? path, CancellationToken cancellationToken = default);
    /// <summary>Durable save. Sets Status to "Saved HH:MM:SS" on success,
    /// "Save failed: ..." on error, and raises StatusChanged in both cases.</summary>
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>Mark the document dirty; the session debounces a background autosave.</summary>
    void MarkDirty();
    IReadOnlyList<RichStyleView> Styles { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    ValueTask<bool> UndoAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> RedoAsync(CancellationToken cancellationToken = default);
    ValueTask SetSectionIconAsync(string sectionId, string iconKey, string? iconColor,
        CancellationToken cancellationToken = default);
    ValueTask SetSectionGeneratedIconAsync(string sectionId, HavenRichSectionGeneratedIcon generatedIcon,
        string? iconColor, CancellationToken cancellationToken = default);
    ValueTask SetSectionIconEmojiAsync(string sectionId, string emoji, string? iconColor,
        CancellationToken cancellationToken = default);
    ValueTask SetPageIconAsync(string pageId, string iconKey, string? iconColor,
        CancellationToken cancellationToken = default);
    /// <summary>
    /// Chooses the Board's own identity icon (canonical v2 line 9421 <c>Icon?</c>). Passing a null key is the
    /// Remove action: the Board goes back to the maintained mark its card draws when nothing is chosen.
    /// </summary>
    ValueTask SetBoardIconAsync(string? iconKey, string? iconEmoji, string? iconColor,
        CancellationToken cancellationToken = default);

    ValueTask SetPageIconEmojiAsync(string pageId, string emoji, string? iconColor,
        CancellationToken cancellationToken = default);
    ValueTask ClearPageIconAsync(string pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// What would leave navigation if this page were trashed - the counts the confirmation has to state,
    /// taken from the same operation the action performs, so a confirmation can never disagree with what
    /// actually happens.
    /// </summary>
    ValueTask<BoardsTrashImpact?> GetTrashImpactAsync(string pageId, CancellationToken cancellationToken = default);

    /// <summary>Moves a page, with its subtree, into the Trash. Refusals carry their reason.</summary>
    ValueTask<BoardsTrashResult> MovePageToTrashAsync(string pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts a trashed page and its subtree back. The result says whether the page returned to the place it
    /// was recorded at or had to be relocated, which is what lets the status line tell the owner instead of
    /// leaving them to discover it.
    /// </summary>
    ValueTask<BoardsTrashRestoreResult> RestorePageFromTrashAsync(string pageId,
        CancellationToken cancellationToken = default);
}

/// <summary>The impact figures a Trash confirmation must state (UX27-BOARD-015).</summary>
/// <summary>
/// What a Trash would mean for this page. The refusal travels with the counts on purpose: the application has
/// to know before it opens a confirmation that the operation itself will refuse, rather than drawing a Move
/// button that does nothing.
/// </summary>
public sealed record BoardsTrashImpact(int SubpageCount, int BlockCount, string? Refusal)
{
    public bool CanTrash => Refusal is null;
}

/// <summary>
/// Thrown inside a session mutation so the session's rollback path runs: a refused Trash or Restore attempt
/// must not consume an undo step or leave a half-applied tree. The message is the reason shown to the owner.
/// </summary>
public sealed class BoardsTrashRefusalException : InvalidOperationException
{
    public BoardsTrashRefusalException(string reason) : base(reason) { }
}

/// <summary>Outcome of a trash action; a refusal always carries the reason the owner should see.</summary>
public sealed record BoardsTrashResult(bool Succeeded, string? Reason)
{
    public static BoardsTrashResult Refused(string reason) => new(false, reason);
}

/// <summary>Outcome of a restore, including whether the page came back where it was recorded.</summary>
public sealed record BoardsTrashRestoreResult(bool Succeeded, string? Reason, string? SectionTitle,
    bool RelocatedToSectionRoot)
{
    public static BoardsTrashRestoreResult Refused(string reason) => new(false, reason, null, false);
}

/// <summary>Optional session capability contract for immutable embedded document views.</summary>
public interface IBoardSessionCapabilities
{
    bool CanEdit { get; }
    bool CanSwitchBoard => CanEdit;
    bool CanSaveAs => CanEdit;
}


public enum BoardTextMark { Unavailable, Off, On, Mixed }
public enum BoardTextFormat { Bold, Italic, Underline, StrikeThrough }

/// <summary>Transient, same-owner range observation. It is never saved as board
/// content or treated as Home authority. Only its issuing session can apply it.</summary>
public sealed record BoardTextSelection(Guid OwnerId, long Revision, string PageId,
    string BlockId, string Text, string ViewStamp, int Anchor, int Caret,
    BoardTextMark Bold, BoardTextMark Italic, BoardTextMark Underline, BoardTextMark StrikeThrough);

public sealed record BoardTextActionResult(bool Applied, string Code);

/// <summary>Optional capability of the genuine current rich-board owner, not a
/// shadow Notes editor. The deny-only predicate fences the originating UI lifetime.</summary>
public interface IBoardTextRangeSession
{
    BoardTextSelection? CaptureTextSelection(string pageId, string blockId, int anchor, int caret);
    ValueTask<BoardTextActionResult> ToggleTextSelectionAsync(BoardTextSelection selection,
        BoardTextFormat format, Func<bool> originStillCurrent, CancellationToken cancellationToken = default);
}
