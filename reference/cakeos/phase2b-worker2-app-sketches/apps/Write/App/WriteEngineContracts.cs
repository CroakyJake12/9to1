namespace HavenOS.Apps.Write;

public sealed record WriteDocumentInfo(string Path, string Title, int PageCount, long Size);
public sealed record WriteCursorPosition(int Page, int Paragraph, int Offset);
public sealed record WriteSelection(WriteCursorPosition Start, WriteCursorPosition End);
public sealed record WriteTextContent(string Text, bool IsRichText);
public sealed record WriteFormatRange(WriteCursorPosition Start, WriteCursorPosition End, WriteFormat Format);
public sealed record WriteFormat(bool Bold, bool Italic, bool Underline, string FontFamily, int FontSize, string Color);
public sealed record WritePageInfo(int PageNumber, int WidthTwips, int HeightTwips);

public interface IWriteEngine : IAsyncDisposable
{
    void Open(string documentPathOrUrl);
    void Close();
    bool IsOpen();
    WriteDocumentInfo GetDocumentInfo();
    IReadOnlyList<WritePageInfo> GetPages();
    WriteTextContent GetText(WriteCursorPosition start, WriteCursorPosition end);
    void SetText(WriteCursorPosition position, string text);
    void InsertText(WriteCursorPosition position, string text);
    void DeleteText(WriteCursorPosition start, WriteCursorPosition end);
    void ApplyFormat(WriteFormatRange range);
    void Save();
    void SaveAs(string destinationPathOrUrl, string format = "odt");
    void Print();
    void Undo();
    void Redo();
    void SetCursorPosition(WriteCursorPosition position);
    WriteCursorPosition GetCursorPosition();
    void SetSelection(WriteSelection selection);
    WriteSelection? GetSelection();
    void SetEventCallback(Action<WriteEngineEvent> callback);
}

public enum WriteEngineEventType
{
    TextChanged = 0,
    CursorMoved = 1,
    SelectionChanged = 2,
    DocumentSaved = 3,
    DocumentLoaded = 4,
    Error = 5,
}

public sealed record WriteEngineEvent(WriteEngineEventType Type, string Payload);

public sealed class WriteAppService(IWriteEngine engine)
{
    public IWriteEngine Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
}