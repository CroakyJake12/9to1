using System.Text;
using System.Text.RegularExpressions;

namespace Haven.Application;

public sealed record TerminalRetainedOutput(long Sequence, TerminalSessionOutput Output, int RetainedBytes);

public sealed record TerminalOutputSnapshot(
    IReadOnlyList<TerminalRetainedOutput> Entries,
    int RetainedBytes,
    int MaximumBytes,
    long TruncatedThroughSequence,
    int TruncatedPrefixBytes)
{
    public bool IsTruncated => TruncatedThroughSequence > 0 || TruncatedPrefixBytes > 0;
    public string DisplayText => (IsTruncated ? "[Earlier terminal output was discarded. Search and scrollback start at the retained boundary.]\n" : string.Empty)
        + string.Join(string.Empty, Entries.Select(static entry => entry.Output.Text));
}

public sealed record TerminalOutputSearchMatch(long Sequence, int TextIndex, string Context);

public sealed record TerminalOutputSearchResult(
    IReadOnlyList<TerminalOutputSearchMatch> Matches,
    TerminalFailure? Failure = null);

/// <summary>
/// Thread-safe, bounded retained output for one Terminal session. The process stream remains
/// independent; trimming this buffer never writes to, pauses, or restarts the PTY.
/// </summary>
public sealed class TerminalOutputBuffer
{
    public const int DefaultMaximumBytes = 8 * 1024 * 1024;
    private const int MaximumSearchResults = 2_000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(150);
    private readonly object _gate = new();
    private readonly LinkedList<TerminalRetainedOutput> _entries = new();
    private readonly int _maximumBytes;
    private int _retainedBytes;
    private long _nextSequence;
    private long _truncatedThroughSequence;
    private int _truncatedPrefixBytes;

    public TerminalOutputBuffer(int maximumBytes = DefaultMaximumBytes)
    {
        if (maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        _maximumBytes = maximumBytes;
    }

    public int MaximumBytes => _maximumBytes;

    public long Append(TerminalSessionOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var textBytes = Encoding.UTF8.GetByteCount(output.Text ?? string.Empty);
        var rawByteCount = output.RawBytes?.Length ?? 0;
        var byteCount = Math.Max(rawByteCount, textBytes);
        lock (_gate)
        {
            var sequence = ++_nextSequence;
            var retainedOutput = output;
            if (byteCount > _maximumBytes)
            {
                if (output.RawBytes is { Length: > 0 } raw && raw.Length > _maximumBytes)
                {
                    var removeCount = raw.Length - _maximumBytes;
                    var retained = raw.Slice(removeCount).ToArray();
                    retainedOutput = output with
                    {
                        Text = Encoding.UTF8.GetString(retained),
                        RawBytes = retained
                    };
                }
                else
                {
                    var text = output.Text ?? string.Empty;
                    var encodedText = Encoding.UTF8.GetBytes(text);
                    var retained = encodedText.AsSpan(Math.Max(0, encodedText.Length - _maximumBytes)).ToArray();
                    retainedOutput = output with
                    {
                        Text = Encoding.UTF8.GetString(retained),
                        RawBytes = output.RawBytes
                    };
                }
                _truncatedPrefixBytes += Math.Max(0, byteCount - _maximumBytes);
                byteCount = _maximumBytes;
            }

            _entries.AddLast(new TerminalRetainedOutput(sequence, retainedOutput, byteCount));
            _retainedBytes += byteCount;
            while (_retainedBytes > _maximumBytes && _entries.First is { } first)
            {
                _retainedBytes -= first.Value.RetainedBytes;
                _truncatedThroughSequence = first.Value.Sequence;
                _entries.RemoveFirst();
            }
            return sequence;
        }
    }

    public TerminalOutputSnapshot Snapshot()
    {
        lock (_gate)
            return new(_entries.ToArray(), _retainedBytes, _maximumBytes, _truncatedThroughSequence, _truncatedPrefixBytes);
    }

    public TerminalOutputSearchResult Search(string query, bool caseSensitive = false, bool regularExpression = false, int maximumResults = 100)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new([], new("InvalidSearchQuery", "Enter text or a regular expression to search.", "terminal.output.search", true));
        if (maximumResults is < 1 or > MaximumSearchResults)
            return new([], new("InvalidSearchLimit", $"Search result limits must be between 1 and {MaximumSearchResults}.", "terminal.output.search", true));

        var snapshot = Snapshot();
        try
        {
            var matches = new List<TerminalOutputSearchMatch>();
            if (regularExpression)
            {
                var options = RegexOptions.CultureInvariant | (caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                var expression = new Regex(query, options, RegexTimeout);
                foreach (var entry in snapshot.Entries)
                {
                    foreach (Match match in expression.Matches(entry.Output.Text))
                    {
                        if (!match.Success) continue;
                        matches.Add(new(entry.Sequence, match.Index, Context(entry.Output.Text, match.Index, match.Length)));
                        if (matches.Count >= maximumResults) return new(matches);
                    }
                }
            }
            else
            {
                var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                foreach (var entry in snapshot.Entries)
                {
                    var start = 0;
                    while (start < entry.Output.Text.Length)
                    {
                        var index = entry.Output.Text.IndexOf(query, start, comparison);
                        if (index < 0) break;
                        matches.Add(new(entry.Sequence, index, Context(entry.Output.Text, index, query.Length)));
                        if (matches.Count >= maximumResults) return new(matches);
                        start = index + Math.Max(query.Length, 1);
                    }
                }
            }
            return new(matches);
        }
        catch (ArgumentException ex)
        {
            return new([], new("InvalidRegularExpression", ex.Message, "terminal.output.search", true));
        }
        catch (RegexMatchTimeoutException)
        {
            return new([], new("SearchTimedOut", "The regular expression took too long. Narrow the expression and try again.", "terminal.output.search", true, true));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _retainedBytes = 0;
            _truncatedThroughSequence = 0;
            _truncatedPrefixBytes = 0;
        }
    }

    private static string Context(string text, int index, int length)
    {
        const int radius = 48;
        var start = Math.Max(0, index - radius);
        var end = Math.Min(text.Length, index + length + radius);
        return text[start..end];
    }
}
