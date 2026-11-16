using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("Dulche.DenAdmission.Tests")]

namespace NineToOne.Dulche.Training;

/// <summary>Deployment-owned files approved for the local software self-check.</summary>
public sealed record LocalTrainingWorkerRegistration(
    string PythonPath, string PythonSha256, string CliArchivePath, string CliArchiveSha256,
    string OperatorConfigPath, string OperatorConfigSha256);

public enum LocalTrainingClientFailure
{
    RegistrationInvalid,
    StartFailed,
    TimedOut,
    TransportCancelled,
    ProtocolInvalid,
    TransportFailed
}

public sealed class LocalTrainingClientException(
    LocalTrainingClientFailure failure, string message, LocalTrainingAttempt? acceptedAttempt = null,
    bool outcomeUnknown = false, bool cleanupConfirmed = true) : IOException(message)
{
    public LocalTrainingClientFailure Failure { get; } = failure;
    public LocalTrainingAttempt? AcceptedAttempt { get; } = acceptedAttempt;
    public bool OutcomeUnknown { get; } = outcomeUnknown;
    public bool CleanupConfirmed { get; } = cleanupConfirmed;
}

public sealed record LocalTrainingResponse(LocalTrainingEvent Terminal, LocalTrainingAttempt? AcceptedAttempt)
{
    public bool OutcomeUnknown { get; init; } = Terminal.Kind == LocalTrainingEventKind.Error && AcceptedAttempt is not null;
}

public interface ILocalTrainingClient
{
    Task<LocalTrainingResponse> ExecuteAsync(
        LocalTrainingCommand command, LocalTrainingAttempt? attempt = null, LocalTrainingArtifact? checkpoint = null,
        int? limit = null, IProgress<LocalTrainingEvent>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts the registered local CPU self-check worker. Use a separate Pause command
/// for cooperative pause. Cancelling this transport closes its owned process and
/// leaves an unconfirmed dispatch outcome for the caller to check.
/// </summary>
public sealed class LocalTrainingClient(LocalTrainingWorkerRegistration registration) : ILocalTrainingClient
{
    private const int MaximumStderrBytes = 64 * 1024;
    private static readonly TimeSpan QueryDeadline = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan JobDeadline = TimeSpan.FromSeconds(70);
    private static readonly TimeSpan ReapDeadline = TimeSpan.FromSeconds(5);
    private readonly LocalTrainingWorkerRegistration _registration =
        registration ?? throw new ArgumentNullException(nameof(registration));

    public async Task<LocalTrainingResponse> ExecuteAsync(
        LocalTrainingCommand command, LocalTrainingAttempt? attempt = null, LocalTrainingArtifact? checkpoint = null,
        int? limit = null, IProgress<LocalTrainingEvent>? progress = null, CancellationToken cancellationToken = default)
    {
        var request = LocalTrainingProtocol.Request(command, attempt, checkpoint, limit);
        if (request.Length > LocalTrainingProtocol.MaximumLineBytes)
            throw new ArgumentException("The self-check request exceeds its byte limit.", nameof(command));
        cancellationToken.ThrowIfCancellationRequested();
        var work = command is LocalTrainingCommand.Launch or LocalTrainingCommand.Resume;
        using var deadline = new CancellationTokenSource(work ? JobDeadline : QueryDeadline);
        using var transport = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = transport.Token;

        PinnedInstallation installation;
        try
        {
            installation = await PinnedInstallation.OpenAsync(_registration, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LocalTrainingClientException(LocalTrainingClientFailure.TimedOut, "Local worker verification timed out.");
        }
        using var pinned = installation;
        cancellationToken.ThrowIfCancellationRequested();
        if (deadline.IsCancellationRequested)
            throw new LocalTrainingClientException(LocalTrainingClientFailure.TimedOut, "Local worker verification timed out.");
        if (!OperatingSystem.IsWindows())
            throw new LocalTrainingClientException(LocalTrainingClientFailure.StartFailed, "The local self-check worker requires Windows.");

        var conversation = new Conversation(command, attempt, checkpoint, limit, progress);
        using var process = new Process { StartInfo = CreateStartInfo(pinned) };
        try
        {
            if (!process.Start())
                throw new LocalTrainingClientException(LocalTrainingClientFailure.StartFailed, "The registered local worker did not start.");
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            var cleaned = !HasOwnedProcess(process) || await ReapOwnedProcessAsync(process).ConfigureAwait(false);
            throw new LocalTrainingClientException(LocalTrainingClientFailure.StartFailed,
                "The registered local worker did not start.", cleanupConfirmed: cleaned);
        }

        var active = new List<Task>(4);
        try
        {
            active.Add(conversation.ReadOutputAsync(process.StandardOutput.BaseStream, token));
            active.Add(DrainErrorsAsync(process.StandardError.BaseStream, token));
            active.Add(process.WaitForExitAsync(token));
            active.Add(WriteRequestAsync(process.StandardInput, request, token));
            var tasks = new List<Task>(active);
            while (tasks.Count != 0)
            {
                var completed = await Task.WhenAny(tasks).WaitAsync(token).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                tasks.Remove(completed);
            }
            return conversation.Complete(process.ExitCode);
        }
        catch (Exception error)
        {
            // Once started, a lost acknowledgement leaves the dispatch uncertain,
            // even when the CLI never delivered an attempt identifier.
            transport.Cancel();
            var cleaned = await ReapOwnedProcessAsync(process).ConfigureAwait(false);
            ObserveFaults(active);
            var failure = error switch
            {
                OperationCanceledException when cancellationToken.IsCancellationRequested => LocalTrainingClientFailure.TransportCancelled,
                OperationCanceledException => LocalTrainingClientFailure.TimedOut,
                InvalidDataException => LocalTrainingClientFailure.ProtocolInvalid,
                _ => LocalTrainingClientFailure.TransportFailed
            };
            var message = failure switch
            {
                LocalTrainingClientFailure.TransportCancelled => "The local worker request was interrupted.",
                LocalTrainingClientFailure.TimedOut => "The local worker request timed out.",
                LocalTrainingClientFailure.ProtocolInvalid => "The local worker returned an invalid response.",
                _ => "The local worker connection failed."
            };
            throw new LocalTrainingClientException(failure, message, conversation.Accepted,
                outcomeUnknown: work || command == LocalTrainingCommand.Pause, cleanupConfirmed: cleaned);
        }
    }

    private static ProcessStartInfo CreateStartInfo(PinnedInstallation pinned)
    {
        var installation = Path.GetDirectoryName(pinned.PythonPath)!;
        var start = new ProcessStartInfo(pinned.PythonPath)
        {
            WorkingDirectory = installation,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-I", "-S", "-B", pinned.CliPath, "--operator-config", pinned.ConfigPath })
            start.ArgumentList.Add(argument);
        var system = Environment.SystemDirectory;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length == 0) windows = Path.GetDirectoryName(system) ?? system;
        start.Environment.Clear();
        start.Environment["SYSTEMROOT"] = windows;
        start.Environment["WINDIR"] = windows;
        start.Environment["PATH"] = string.Join(Path.PathSeparator, installation, system, windows);
        start.Environment["CUDA_VISIBLE_DEVICES"] = "-1";
        return start;
    }

    private static async Task WriteRequestAsync(StreamWriter writer, byte[] request, CancellationToken cancellationToken)
    {
        await writer.BaseStream.WriteAsync(request, cancellationToken).ConfigureAwait(false);
        writer.Close();
    }

    internal static async Task DrainErrorsAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new byte[4096];
        var total = 0;
        int count;
        while ((count = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > MaximumStderrBytes)
                throw new InvalidDataException("The local worker exceeded its diagnostic output limit.");
        }
    }

    private static async Task<bool> ReapOwnedProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception or AggregateException or NotSupportedException)
        {
            // Closing the supervisor process also closes the worker's Windows Job.
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception fallback) when (fallback is InvalidOperationException or Win32Exception or NotSupportedException) { }
        }
        try
        {
            using var reap = new CancellationTokenSource(ReapDeadline);
            await process.WaitForExitAsync(reap.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private static bool HasOwnedProcess(Process process)
    {
        try { _ = process.Id; return true; }
        catch (InvalidOperationException) { return false; }
    }

    private static void ObserveFaults(IEnumerable<Task> tasks)
    {
        foreach (var task in tasks)
            _ = task.ContinueWith(static failed => { _ = failed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    internal sealed class Conversation(
        LocalTrainingCommand command, LocalTrainingAttempt? requested = null, LocalTrainingArtifact? checkpoint = null,
        int? limit = null, IProgress<LocalTrainingEvent>? progress = null)
    {
        private const int MaximumEvents = 64;
        private const int MaximumStdoutBytes = 1024 * 1024;
        private const int SelfCheckUpdates = 2;
        private readonly bool _work = command is LocalTrainingCommand.Launch or LocalTrainingCommand.Resume;
        private LocalTrainingAttempt? _accepted;
        private LocalTrainingEvent? _terminal;
        private int _updates = command == LocalTrainingCommand.Resume ? 1 : 0;

        public LocalTrainingAttempt? Accepted => Volatile.Read(ref _accepted);

        public async Task ReadOutputAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            var chunk = new byte[4096];
            var line = new byte[LocalTrainingProtocol.MaximumLineBytes + 1];
            var length = 0;
            var frames = 0;
            var total = 0;
            int count;
            while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
            {
                total += count;
                if (total > MaximumStdoutBytes)
                    throw new InvalidDataException("The local worker exceeded its output limit.");
                for (var index = 0; index < count; index++)
                {
                    var value = chunk[index];
                    if (value == (byte)'\n')
                    {
                        if (++frames > MaximumEvents) throw new InvalidDataException("Too many worker events.");
                        var frameLength = length > 0 && line[length - 1] == (byte)'\r' ? length - 1 : length;
                        Receive(Parse(line.AsMemory(0, frameLength)));
                        length = 0;
                    }
                    else
                    {
                        if (length == line.Length) throw new InvalidDataException("A worker event exceeded its line limit.");
                        line[length++] = value;
                    }
                }
            }
            if (length != 0) throw new InvalidDataException("The worker ended with a partial event.");
        }

        private static LocalTrainingEvent Parse(ReadOnlyMemory<byte> line)
        {
            try { return LocalTrainingProtocol.ParseEvent(line); }
            catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException or KeyNotFoundException)
            { throw new InvalidDataException("The worker event does not follow the local protocol."); }
        }

        private void Receive(LocalTrainingEvent frame)
        {
            if (_terminal is not null) throw new InvalidDataException("The worker emitted an event after its result.");
            switch (frame.Kind)
            {
                case LocalTrainingEventKind.Accepted:
                    var accepted = LocalTrainingProtocol.ReadAttempt(frame.Payload);
                    if (!_work || Accepted is not null || (checkpoint is not null &&
                        (accepted.RunId != checkpoint.Attempt.RunId || accepted.AttemptId == checkpoint.Attempt.AttemptId)))
                        throw new InvalidDataException("The worker acknowledged an unexpected attempt.");
                    Volatile.Write(ref _accepted, accepted);
                    progress?.Report(frame);
                    return;
                case LocalTrainingEventKind.Progress:
                    var count = CompletedUpdates(frame.Payload);
                    if (!_work || Accepted is null || LocalTrainingProtocol.ReadAttempt(frame.Payload, exact: false) != Accepted ||
                        frame.Payload.GetProperty("status").GetString() != "running" ||
                        count is not int completed || completed <= _updates)
                        throw new InvalidDataException("The worker reported unexpected progress.");
                    _updates = completed;
                    progress?.Report(frame);
                    return;
                case LocalTrainingEventKind.Result:
                    ValidateResult(frame.Payload);
                    break;
            }
            _terminal = frame;
        }

        private void ValidateResult(JsonElement payload)
        {
            if (command == LocalTrainingCommand.List)
            {
                ValidateList(payload);
                return;
            }
            if (command == LocalTrainingCommand.Status && requested is null)
            {
                if (!payload.TryGetProperty("ready", out _)) throw new InvalidDataException("Expected a worker readiness result.");
                return;
            }
            var expected = _work ? Accepted : requested;
            if (expected is null || !payload.TryGetProperty("run_id", out _) ||
                LocalTrainingProtocol.ReadAttempt(payload, exact: false) != expected)
                throw new InvalidDataException("The worker result belongs to another attempt.");
            var state = payload.GetProperty("status").GetString();
            if (_work && state is ("prepared" or "running"))
                throw new InvalidDataException("The worker returned before the attempt reached a terminal state.");
            var updates = CompletedUpdates(payload);
            if (_work && updates is int completed && completed < _updates)
                throw new InvalidDataException("The worker result regressed previously observed updates.");
            if (state == "passed" && updates != SelfCheckUpdates)
                throw new InvalidDataException("The worker did not report both software self-check updates.");
        }

        private void ValidateList(JsonElement payload)
        {
            if (!payload.TryGetProperty("attempts", out var attempts) || attempts.GetArrayLength() > (limit ?? 20))
                throw new InvalidDataException("Expected a bounded retained attempt page.");
            var previous = requested;
            foreach (var element in attempts.EnumerateArray())
            {
                var current = LocalTrainingProtocol.ReadAttempt(element, exact: false);
                var updates = CompletedUpdates(element);
                if (element.GetProperty("status").GetString() == "passed" && updates != SelfCheckUpdates)
                    throw new InvalidDataException("A passed self-check did not report both updates.");
                if (previous is not null && Compare(previous, current) >= 0)
                    throw new InvalidDataException("The retained attempt page does not follow its cursor.");
                previous = current;
            }
            var next = payload.GetProperty("next");
            if (next.ValueKind != JsonValueKind.Null &&
                (attempts.GetArrayLength() == 0 || LocalTrainingProtocol.ReadAttempt(next) != previous))
                throw new InvalidDataException("The retained attempt page has an unexpected continuation cursor.");
        }

        private static int? CompletedUpdates(JsonElement payload)
        {
            var value = payload.GetProperty("completed_updates");
            if (value.ValueKind == JsonValueKind.Null) return null;
            var count = value.GetInt32();
            if (count is < 0 or > SelfCheckUpdates)
                throw new InvalidDataException("The worker update count is outside the self-check limit.");
            return count;
        }

        private static int Compare(LocalTrainingAttempt left, LocalTrainingAttempt right)
        {
            var runs = left.RunId.CompareTo(right.RunId);
            return runs == 0 ? left.AttemptId.CompareTo(right.AttemptId) : runs;
        }

        public LocalTrainingResponse Complete(int exitCode)
        {
            if (_terminal is null || _terminal.Kind != (exitCode == 0 ? LocalTrainingEventKind.Result : LocalTrainingEventKind.Error))
                throw new InvalidDataException("The worker exit did not confirm its terminal response.");
            return new(_terminal, Accepted)
            {
                OutcomeUnknown = _terminal.Kind == LocalTrainingEventKind.Error && (Accepted is not null || command == LocalTrainingCommand.Pause)
            };
        }
    }

    internal sealed class PinnedInstallation : IDisposable
    {
        private readonly List<FileStream> _handles = new(3);
        public string PythonPath { get; private set; } = "";
        public string CliPath { get; private set; } = "";
        public string ConfigPath { get; private set; } = "";

        public static async Task<PinnedInstallation> OpenAsync(LocalTrainingWorkerRegistration registration, CancellationToken token)
        {
            var pinned = new PinnedInstallation();
            try
            {
                pinned.PythonPath = LocalPath(registration.PythonPath, ".exe");
                pinned.CliPath = LocalPath(registration.CliArchivePath, ".zip");
                pinned.ConfigPath = LocalPath(registration.OperatorConfigPath, ".json");
                var installation = Path.GetDirectoryName(pinned.PythonPath)!;
                RejectReparsePoints(pinned.PythonPath);
                VerifyPythonStartup(installation);
                await pinned.PinAsync(pinned.PythonPath, registration.PythonSha256, 128L * 1024 * 1024, token).ConfigureAwait(false);
                await pinned.PinAsync(pinned.CliPath, registration.CliArchiveSha256, 1024 * 1024, token).ConfigureAwait(false);
                var config = await pinned.PinAsync(pinned.ConfigPath, registration.OperatorConfigSha256, 32 * 1024, token).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(config, new JsonDocumentOptions { MaxDepth = 12 }, token).ConfigureAwait(false);
                var settings = document.RootElement;
                if (!string.Equals(LocalPath(settings.GetProperty("python").GetString(), ".exe"), pinned.PythonPath,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                    settings.GetProperty("python_sha256").GetString() != registration.PythonSha256)
                    throw new InvalidDataException("The operator configuration targets a different Python executable.");
                VerifyPythonStartup(installation);
                return pinned;
            }
            catch (OperationCanceledException)
            {
                pinned.Dispose();
                throw;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
                or JsonException or InvalidOperationException or KeyNotFoundException or NotSupportedException)
            {
                pinned.Dispose();
                throw new LocalTrainingClientException(LocalTrainingClientFailure.RegistrationInvalid,
                    "The registered local worker files could not be verified.");
            }
        }

        private async Task<FileStream> PinAsync(string path, string hash, long maximumBytes, CancellationToken token)
        {
            if (hash is null || hash.Length != 64 || !hash.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
                throw new InvalidDataException("An exact SHA-256 pin is required.");
            RejectReparsePoints(path);
            var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            _handles.Add(file);
            RejectReparsePoints(path);
            if (file.Length is <= 0 || file.Length > maximumBytes)
                throw new InvalidDataException("A registered file exceeds its allowed size.");
            var actual = await SHA256.HashDataAsync(file, token).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(hash)))
                throw new InvalidDataException("A registered file has changed.");
            file.Position = 0;
            return file;
        }

        private static string LocalPath(string? path, string extension)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
                path.Split(['\\', '/']).Any(static part => part is "." or "..") ||
                (OperatingSystem.IsWindows() && (path.Length < 3 || !char.IsAsciiLetter(path[0]) ||
                    path[1] != ':' || path.AsSpan(2).Contains(':'))))
                throw new InvalidDataException("The registration requires an absolute local file path.");
            var full = Path.GetFullPath(path);
            if (!string.Equals(Path.GetExtension(full), extension, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The registered file type is invalid.");
            return full;
        }

        private static void RejectReparsePoints(string file)
        {
            for (string? component = file; component is not null; component = Path.GetDirectoryName(component))
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("A registered path contains a reparse point.");
        }

        private static void VerifyPythonStartup(string installation)
        {
            if (Directory.EnumerateFileSystemEntries(installation, "*._pth").Any() ||
                File.Exists(Path.Combine(installation, "pyvenv.cfg")) ||
                (Path.GetDirectoryName(installation) is { } parent && File.Exists(Path.Combine(parent, "pyvenv.cfg"))))
                throw new InvalidDataException("The registered Python install overrides startup paths.");
        }

        public void Dispose()
        {
            foreach (var handle in _handles) handle.Dispose();
        }
    }
}
