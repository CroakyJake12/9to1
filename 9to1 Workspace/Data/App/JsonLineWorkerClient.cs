using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HavenOS.Apps.Data;

internal sealed class JsonLineWorkerClient : IAsyncDisposable
{
    private const int MaxStderrTailCharacters = 8192;

    private readonly Process _process;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _stderrCancellation = new();
    private readonly StringBuilder _stderrTail = new();
    private readonly object _stderrLock = new();
    private readonly Task _stderrPump;
    private int _nextId;
    private bool _faulted;

    public JsonLineWorkerClient(string executable, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment)
                start.Environment[pair.Key] = pair.Value;

        _process = Process.Start(start) ?? throw new InvalidOperationException($"Failed to start worker '{executable}'.");
        _stderrPump = PumpStandardErrorAsync(_stderrCancellation.Token);
    }

    public async Task<T> CallAsync<T>(string method, object? parameters, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var requestMayHaveBeenSent = false;
        try
        {
            if (_faulted)
                throw new InvalidOperationException($"Worker protocol is faulted and cannot be reused.{FormatStderrTail()}");
            if (_process.HasExited)
                throw new InvalidOperationException($"Worker exited with code {_process.ExitCode}.{FormatStderrTail()}");

            var id = Interlocked.Increment(ref _nextId);
            var request = JsonSerializer.Serialize(new { id, method, @params = parameters }, _json);

            // Cancellation after writing begins makes request/response alignment uncertain.
            // If that happens, fail closed and terminate the worker rather than allowing a
            // stale response to be consumed by the next call.
            requestMayHaveBeenSent = true;
            await _process.StandardInput.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException($"Worker closed stdout before replying.{FormatStderrTail()}");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id)
            {
                await FaultWorkerAsync().ConfigureAwait(false);
                throw new InvalidDataException("Worker response id did not match the request; worker was terminated to prevent protocol desynchronisation.");
            }
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new InvalidOperationException(error.GetString() ?? "Worker operation failed.");
            if (!root.TryGetProperty("result", out var result))
            {
                await FaultWorkerAsync().ConfigureAwait(false);
                throw new InvalidDataException("Worker response did not contain a result; worker was terminated.");
            }
            return result.Deserialize<T>(_json) ?? throw new InvalidDataException("Worker returned an empty result.");
        }
        catch (OperationCanceledException) when (requestMayHaveBeenSent && cancellationToken.IsCancellationRequested)
        {
            await FaultWorkerAsync().ConfigureAwait(false);
            throw;
        }
        catch (JsonException)
        {
            await FaultWorkerAsync().ConfigureAwait(false);
            throw;
        }
        catch (EndOfStreamException)
        {
            await FaultWorkerAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited && !_faulted)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    _ = await CallAsync<object>("shutdown", null, shutdown.Token).ConfigureAwait(false);
                    await _process.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
                }
                catch
                {
                    await FaultWorkerAsync().ConfigureAwait(false);
                }
            }
            else if (!_process.HasExited)
            {
                await FaultWorkerAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _stderrCancellation.Cancel();
            try { await _stderrPump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            _stderrCancellation.Dispose();
            _process.Dispose();
            _gate.Dispose();
        }
    }

    private async Task FaultWorkerAsync()
    {
        _faulted = true;
        if (_process.HasExited)
            return;
        try { _process.Kill(entireProcessTree: true); }
        catch { }
        try { await _process.WaitForExitAsync().ConfigureAwait(false); }
        catch { }
    }

    private async Task PumpStandardErrorAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                var line = await _process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) return;
                lock (_stderrLock)
                {
                    _stderrTail.AppendLine(line);
                    if (_stderrTail.Length > MaxStderrTailCharacters)
                        _stderrTail.Remove(0, _stderrTail.Length - MaxStderrTailCharacters);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private string FormatStderrTail()
    {
        lock (_stderrLock)
        {
            return _stderrTail.Length == 0 ? string.Empty : $" Worker stderr: {_stderrTail.ToString().Trim()}";
        }
    }
}
