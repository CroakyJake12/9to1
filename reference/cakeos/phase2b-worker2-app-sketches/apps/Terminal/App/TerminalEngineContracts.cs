namespace HavenOS.Apps.Terminal;

public sealed record TerminalSize(int Cols, int Rows);
public sealed record TerminalCell(char Codepoint, int Width, TerminalAttr Attr);
public sealed record TerminalAttr(
    bool Bold, bool Italic, bool Underline, bool Strikethrough, bool Blink, bool Reverse,
    int Foreground, int Background,
    bool ForegroundDefault, bool BackgroundDefault);

public sealed record TerminalLine(int Row, IReadOnlyList<TerminalCell> Cells);
public sealed record TerminalSnapshot(IReadOnlyList<TerminalLine> Lines, TerminalSize Size, TerminalCursor Cursor);
public sealed record TerminalCursor(int Row, int Col, bool Visible, bool Blink, TerminalAttr Attr);

public interface ITerminalEngine : IAsyncDisposable
{
    Task<TerminalSession> CreateSessionAsync(string command, string[] args, string workingDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default);
    Task<TerminalSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TerminalSession>> ListSessionsAsync(CancellationToken cancellationToken = default);
    void SetEventCallback(Action<TerminalEngineEvent> callback);
}

public sealed record TerminalSession(
    string Id,
    string Command,
    int Pid,
    int ExitCode,
    bool Running,
    TerminalSize Size,
    TerminalSnapshot? LastSnapshot);

public enum TerminalEngineEventType
{
    SessionCreated = 0,
    SessionExited = 1,
    SessionOutput = 2,
    SessionResize = 3,
    Bell = 4,
}

public sealed record TerminalEngineEvent(TerminalEngineEventType Type, string SessionId, string Payload);

public interface ITerminalSession : IAsyncDisposable
{
    string Id { get; }
    TerminalSize Size { get; }
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default);
    Task<TerminalSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);
    Task<int> WaitExitAsync(CancellationToken cancellationToken = default);
    void SetOutputCallback(Action<ReadOnlyMemory<byte>> callback);
}

public sealed class TerminalAppService(ITerminalEngine engine)
{
    public ITerminalEngine Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
}