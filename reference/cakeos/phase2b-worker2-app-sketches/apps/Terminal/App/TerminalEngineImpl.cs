using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HavenOS.Apps.Terminal;

public sealed class TerminalEngine : ITerminalEngine
{
    private readonly ConcurrentDictionary<string, TerminalSessionImpl> _sessions = new();
    private Action<TerminalEngineEvent>? _eventCallback;
    private int _sessionCounter = 0;

    public TerminalEngine() { }

    public async Task<TerminalSession> CreateSessionAsync(string command, string[] args, string workingDirectory, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken = default)
    {
        var sessionId = $"session-{Interlocked.Increment(ref _sessionCounter)}";
        var session = new TerminalSessionImpl(sessionId, command, args, workingDirectory, environment);
        await session.StartAsync(cancellationToken);
        _sessions[sessionId] = session;
        _eventCallback?.Invoke(new TerminalEngineEvent(TerminalEngineEventType.SessionCreated, sessionId, $"Started: {command}"));
        return session.GetSessionInfo();
    }

    public async Task<TerminalSession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (_sessions.TryGetValue(sessionId, out var session))
            return session.GetSessionInfo();
        return null;
    }

    public async Task<IReadOnlyList<TerminalSession>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var list = new List<TerminalSession>();
        foreach (var session in _sessions.Values)
            list.Add(session.GetSessionInfo());
        return list;
    }

    public void SetEventCallback(Action<TerminalEngineEvent> callback) => _eventCallback = callback;

    public ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
            await session.DisposeAsync();
        _sessions.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class TerminalSessionImpl : ITerminalSession
    {
        private readonly string _id;
        private readonly string _command;
        private readonly string[] _args;
        private readonly string _workingDirectory;
        private readonly IReadOnlyDictionary<string, string> _environment;
        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;
        private TerminalSize _size = new(80, 24);
        private Action<ReadOnlyMemory<byte>>? _outputCallback;
        private readonly VTermScreen _screen;
        private int _exitCode = -1;
        private bool _running = false;

        public TerminalSessionImpl(string id, string command, string[] args, string workingDirectory, IReadOnlyDictionary<string, string> environment)
        {
            _id = id;
            _command = command;
            _args = args;
            _workingDirectory = workingDirectory;
            _environment = environment;
            _screen = new VTermScreen(_size.Cols, _size.Rows);
        }

        public string Id => _id;
        public TerminalSize Size => _size;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _command,
                Arguments = string.Join(" ", _args),
                WorkingDirectory = _workingDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var kvp in _environment)
                psi.Environment[kvp.Key] = kvp.Value;

            _process = Process.Start(psi);
            if (_process == null)
                throw new InvalidOperationException($"Failed to start process: {_command}");

            _stdin = _process.StandardInput;
            _stdout = _process.StandardOutput;
            _running = true;

            // Read output asynchronously
            _ = Task.Run(async () =>
            {
                var buffer = new char[4096];
                while (_running && !_process!.HasExited)
                {
                    try
                    {
                        int read = await _stdout!.ReadAsync(buffer, cancellationToken);
                        if (read > 0)
                        {
                            var data = new string(buffer, 0, read);
                            _screen.Feed(data);
                            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
                            _outputCallback?.Invoke(bytes);
                        }
                    }
                    catch { break; }
                }
                _running = false;
                _exitCode = _process!.ExitCode;
            }, cancellationToken);
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            if (_stdin != null && _running)
            {
                await _stdin.BaseStream.WriteAsync(data, cancellationToken);
                await _stdin.BaseStream.FlushAsync(cancellationToken);
            }
        }

        public async Task ResizeAsync(TerminalSize size, CancellationToken cancellationToken = default)
        {
            _size = size;
            _screen.Resize(size.Cols, size.Rows);
            // Would send SIGWINCH to process
            await Task.CompletedTask;
        }

        public async Task<TerminalSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var lines = new List<TerminalLine>();
            for (int row = 0; row < _size.Rows; row++)
            {
                var cells = new List<TerminalCell>();
                for (int col = 0; col < _size.Cols; col++)
                {
                    var cell = _screen.GetCell(row, col);
                    cells.Add(new TerminalCell(cell.Codepoint, cell.Width, new TerminalAttr(
                        cell.Attr.Bold, cell.Attr.Italic, cell.Attr.Underline, cell.Attr.Strikethrough,
                        cell.Attr.Blink, cell.Attr.Reverse, cell.Attr.Foreground, cell.Attr.Background,
                        cell.Attr.ForegroundDefault, cell.Attr.BackgroundDefault)));
                }
                lines.Add(new TerminalLine(row, cells));
            }
            var cursor = _screen.GetCursor();
            return new TerminalSnapshot(lines, _size, new TerminalCursor(cursor.Row, cursor.Col, cursor.Visible, cursor.Blink, cursor.Attr));
        }

        public async Task<int> WaitExitAsync(CancellationToken cancellationToken = default)
        {
            if (_process != null)
            {
                await _process.WaitForExitAsync(cancellationToken);
                return _exitCode;
            }
            return _exitCode;
        }

        public void SetOutputCallback(Action<ReadOnlyMemory<byte>> callback) => _outputCallback = callback;

        public TerminalSession GetSessionInfo() => new TerminalSession(
            _id, _command, _process?.Id ?? 0, _exitCode, _running, _size, null);

        public async ValueTask DisposeAsync()
        {
            _running = false;
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync();
            }
            _stdin?.Dispose();
            _stdout?.Dispose();
            _process?.Dispose();
        }
    }

    // Simplified VTerm screen implementation
    private sealed class VTermScreen
    {
        private readonly List<VTermCell> _cells;
        private int _cols, _rows;
        private VTermCursor _cursor = new(0, 0, true, false, new VTermAttr());

        public VTermScreen(int cols, int rows)
        {
            _cols = cols;
            _rows = rows;
            _cells = new List<VTermCell>(cols * rows);
            for (int i = 0; i < cols * rows; i++)
                _cells.Add(new VTermCell());
        }

        public void Resize(int cols, int rows)
        {
            _cols = cols;
            _rows = rows;
            _cells.Clear();
            _cells.Capacity = cols * rows;
            for (int i = 0; i < cols * rows; i++)
                _cells.Add(new VTermCell());
        }

        public void Feed(string data)
        {
            // Simplified - would parse ANSI escape sequences
            foreach (char c in data)
            {
                if (c >= 32 && c < 127)
                {
                    var idx = _cursor.Row * _cols + _cursor.Col;
                    if (idx >= 0 && idx < _cells.Count)
                        _cells[idx] = new VTermCell(c, 1, _cursor.Attr);
                    _cursor.Col++;
                    if (_cursor.Col >= _cols)
                    {
                        _cursor.Col = 0;
                        _cursor.Row++;
                        if (_cursor.Row >= _rows) _cursor.Row = _rows - 1;
                    }
                }
                else if (c == '\n')
                {
                    _cursor.Row++;
                    if (_cursor.Row >= _rows) _cursor.Row = _rows - 1;
                }
                else if (c == '\r')
                {
                    _cursor.Col = 0;
                }
            }
        }

        public VTermCell GetCell(int row, int col)
        {
            var idx = row * _cols + col;
            if (idx >= 0 && idx < _cells.Count)
                return _cells[idx];
            return new VTermCell();
        }

        public VTermCursor GetCursor() => _cursor;
    }

    private struct VTermCell
    {
        public char Codepoint;
        public int Width;
        public VTermAttr Attr;

        public VTermCell(char codepoint = ' ', int width = 1, VTermAttr attr = default)
        {
            Codepoint = codepoint;
            Width = width;
            Attr = attr;
        }
    }

    private struct VTermCursor
    {
        public int Row;
        public int Col;
        public bool Visible;
        public bool Blink;
        public VTermAttr Attr;

        public VTermCursor(int row, int col, bool visible, bool blink, VTermAttr attr)
        {
            Row = row; Col = col; Visible = visible; Blink = blink; Attr = attr;
        }
    }

    private struct VTermAttr
    {
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strikethrough;
        public bool Blink;
        public bool Reverse;
        public int Foreground;
        public int Background;
        public bool ForegroundDefault;
        public bool BackgroundDefault;
    }
}