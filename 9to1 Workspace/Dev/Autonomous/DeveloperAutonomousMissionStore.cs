using System.Security.Cryptography;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Apps.Dev.Autonomous;

/// <summary>
/// Durable atomic store for autonomous development missions, hash-chained canonical transcripts,
/// checkpoints, context epochs, verification/review/integration receipts and event logs.
/// </summary>
public sealed class DeveloperAutonomousMissionStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions LineJsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _rootDirectory;
    private readonly object _gate = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessMissionLeases = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const int LockUnlock = 8;

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(int fileDescriptor, int operation);

    public DeveloperAutonomousMissionStore(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_rootDirectory);
    }

    public string RootDirectory => _rootDirectory;

    /// <summary>
    /// Claims the live-writer lease for a mission. The lock is stored separately from mission.json,
    /// is exclusive across processes, and is released by the OS if its owner exits unexpectedly.
    /// </summary>
    public IDisposable AcquireMissionLease(string missionId)
    {
        ValidateId(missionId, nameof(missionId));
        var leasePath = Path.Combine(_rootDirectory, ".leases", missionId + ".lock");
        var key = Path.GetFullPath(leasePath);
        var processLease = ProcessMissionLeases.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!processLease.Wait(0))
            throw new InvalidOperationException($"Mission '{missionId}' is already owned by a live engine in this process.");

        FileStream? stream = null;
        var flockHeld = false;
        var byteRangeLockHeld = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(leasePath)!);
            stream = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                var fd = stream.SafeFileHandle.DangerousGetHandle().ToInt32();
                if (Flock(fd, LockExclusive | LockNonBlocking) != 0)
                {
                    var errno = Marshal.GetLastPInvokeError();
                    throw new IOException($"Mission lease is held by another process (flock errno {errno}).");
                }
                flockHeld = true;
            }
            else if (OperatingSystem.IsWindows())
            {
                stream.Lock(0, 1);
                byteRangeLockHeld = true;
            }
            else
            {
                throw new PlatformNotSupportedException("Mission leases require flock on Unix or byte-range locks on Windows.");
            }
            return new MissionLease(stream, processLease, flockHeld, byteRangeLockHeld);
        }
        catch (IOException ex)
        {
            if (flockHeld) { try { Flock(stream!.SafeFileHandle.DangerousGetHandle().ToInt32(), LockUnlock); } catch { } }
            if (byteRangeLockHeld && OperatingSystem.IsWindows()) { try { stream!.Unlock(0, 1); } catch { } }
            stream?.Dispose();
            processLease.Release();
            throw new InvalidOperationException($"Mission '{missionId}' is already owned by a live engine.", ex);
        }
        catch
        {
            if (flockHeld) { try { Flock(stream!.SafeFileHandle.DangerousGetHandle().ToInt32(), LockUnlock); } catch { } }
            if (byteRangeLockHeld && OperatingSystem.IsWindows()) { try { stream!.Unlock(0, 1); } catch { } }
            stream?.Dispose();
            processLease.Release();
            throw;
        }
    }

    private sealed class MissionLease(
        FileStream stream,
        SemaphoreSlim processLease,
        bool flockHeld,
        bool byteRangeLockHeld) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                if (flockHeld) Flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), LockUnlock);
                if (byteRangeLockHeld && OperatingSystem.IsWindows()) stream.Unlock(0, 1);
            }
            catch { }
            finally
            {
                stream.Dispose();
                processLease.Release();
            }
        }
    }

    public static string ResolveDefaultRootDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9to1", "Dev", "AutonomousMissions");

    public string GetMissionDirectory(string missionId)
    {
        ValidateId(missionId, nameof(missionId));
        return Path.Combine(_rootDirectory, missionId);
    }

    public IReadOnlyList<DeveloperAutonomousMission> ListMissions()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_rootDirectory)) return [];
            var result = new List<DeveloperAutonomousMission>();
            foreach (var dir in Directory.EnumerateDirectories(_rootDirectory))
            {
                var file = Path.Combine(dir, "mission.json");
                if (!File.Exists(file)) continue;
                try
                {
                    var mission = JsonSerializer.Deserialize<DeveloperAutonomousMission>(File.ReadAllText(file), JsonOptions);
                    if (mission is not null) result.Add(mission);
                }
                catch (JsonException) { }
            }
            return result.OrderByDescending(m => m.UpdatedAt).ToArray();
        }
    }

    public DeveloperAutonomousMission? FindLatestResumableMissionForRepository(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var normalized = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return ListMissions().FirstOrDefault(m =>
            !string.IsNullOrWhiteSpace(m.RepositoryRoot)
            && string.Equals(
                Path.GetFullPath(m.RepositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalized,
                StringComparison.Ordinal)
            && m.State != DeveloperAutonomousMissionState.Completed);
    }

    public DeveloperAutonomousMission? LoadMission(string missionId)
    {
        lock (_gate)
        {
            var file = Path.Combine(GetMissionDirectory(missionId), "mission.json");
            if (!File.Exists(file)) return null;
            var mission = JsonSerializer.Deserialize<DeveloperAutonomousMission>(File.ReadAllText(file), JsonOptions)
                ?? throw new InvalidDataException($"Mission snapshot is empty or invalid: {file}");
            if (!string.Equals(mission.MissionId, missionId, StringComparison.Ordinal))
                throw new InvalidDataException("Mission ID mismatch on reload.");
            return mission;
        }
    }

    public void SaveMission(DeveloperAutonomousMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        lock (_gate)
        {
            var dir = EnsureMissionDirectory(mission.MissionId);
            AtomicWriteJson(Path.Combine(dir, "mission.json"), mission);
        }
    }

    public DeveloperMissionMessage AppendTranscriptMessage(
        string missionId,
        string role,
        string content,
        string? reasoningContent = null,
        string? operation = null,
        string? target = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        content ??= "";
        lock (_gate)
        {
            var dir = EnsureMissionDirectory(missionId);
            var existing = ReadTranscriptUnlocked(dir);
            var index = existing.Count;
            var prevChain = index == 0 ? "" : existing[^1].ChainSha256;
            var msgSha = Sha256Hex($"{role}\n{content}\n{reasoningContent ?? ""}");
            var chainSha = Sha256Hex($"{missionId}|{index}|{msgSha}|{prevChain}");
            var record = new DeveloperMissionMessage(
                Index: index,
                Role: role,
                Content: content,
                ReasoningContent: reasoningContent,
                Operation: operation,
                Target: target,
                Timestamp: DateTimeOffset.UtcNow,
                Sha256: msgSha,
                ChainSha256: chainSha);
            var line = JsonSerializer.Serialize(record, LineJsonOptions);
            File.AppendAllText(Path.Combine(dir, "transcript.jsonl"), line + "\n", Encoding.UTF8);
            return record;
        }
    }

    public IReadOnlyList<DeveloperMissionMessage> ReadTranscript(string missionId)
    {
        lock (_gate)
        {
            var dir = GetMissionDirectory(missionId);
            return ReadTranscriptUnlocked(dir);
        }
    }

    public bool VerifyTranscriptIntegrity(string missionId, out string? error)
    {
        lock (_gate)
        {
            error = null;
            var dir = GetMissionDirectory(missionId);
            var messages = ReadTranscriptUnlocked(dir);
            var prevChain = "";
            for (var i = 0; i < messages.Count; i++)
            {
                var m = messages[i];
                if (m.Index != i)
                {
                    error = $"Transcript index discontinuity at {i} (found {m.Index}).";
                    return false;
                }
                var expectedMsg = Sha256Hex($"{m.Role}\n{m.Content}\n{m.ReasoningContent ?? ""}");
                if (!StringComparer.Ordinal.Equals(expectedMsg, m.Sha256))
                {
                    error = $"Transcript message hash mismatch at index {i}.";
                    return false;
                }
                var expectedChain = Sha256Hex($"{missionId}|{i}|{m.Sha256}|{prevChain}");
                if (!StringComparer.Ordinal.Equals(expectedChain, m.ChainSha256))
                {
                    error = $"Transcript hash chain broken at index {i}.";
                    return false;
                }
                prevChain = m.ChainSha256;
            }
            return true;
        }
    }

    public void SaveCheckpoint(DeveloperMissionCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ValidateId(checkpoint.CheckpointId, nameof(checkpoint.CheckpointId));
        lock (_gate)
        {
            var dir = Path.Combine(EnsureMissionDirectory(checkpoint.MissionId), "checkpoints");
            Directory.CreateDirectory(dir);
            AtomicWriteJson(Path.Combine(dir, checkpoint.CheckpointId + ".json"), checkpoint);
        }
    }

    public IReadOnlyList<DeveloperMissionCheckpoint> LoadCheckpoints(string missionId)
    {
        lock (_gate)
        {
            var dir = Path.Combine(GetMissionDirectory(missionId), "checkpoints");
            if (!Directory.Exists(dir)) return [];
            var list = new List<DeveloperMissionCheckpoint>();
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                var cp = JsonSerializer.Deserialize<DeveloperMissionCheckpoint>(File.ReadAllText(file), JsonOptions);
                if (cp is not null) list.Add(cp);
            }
            return list.OrderBy(c => c.CreatedAt).ToArray();
        }
    }

    public void SaveContextEpoch(DeveloperMissionContextEpoch epoch)
    {
        ArgumentNullException.ThrowIfNull(epoch);
        ValidateId(epoch.EpochId, nameof(epoch.EpochId));
        lock (_gate)
        {
            var dir = Path.Combine(EnsureMissionDirectory(epoch.MissionId), "epochs");
            Directory.CreateDirectory(dir);
            AtomicWriteJson(Path.Combine(dir, epoch.EpochId + ".json"), epoch);
        }
    }

    public DeveloperMissionContextEpoch? LoadContextEpoch(string missionId, string epochId)
    {
        ValidateId(epochId, nameof(epochId));
        lock (_gate)
        {
            var file = Path.Combine(GetMissionDirectory(missionId), "epochs", epochId + ".json");
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<DeveloperMissionContextEpoch>(File.ReadAllText(file), JsonOptions);
        }
    }

    public void SaveReceipt<T>(string missionId, string name, T receipt)
    {
        ValidateId(name, nameof(name));
        lock (_gate)
        {
            var dir = Path.Combine(EnsureMissionDirectory(missionId), "receipts");
            Directory.CreateDirectory(dir);
            AtomicWriteJson(Path.Combine(dir, name + ".json"), receipt);
        }
    }

    public DeveloperMissionEvent AppendEvent(
        string missionId,
        string kind,
        string message,
        string? taskId = null,
        string? modelId = null,
        string? details = null)
    {
        lock (_gate)
        {
            var dir = EnsureMissionDirectory(missionId);
            var file = Path.Combine(dir, "events.jsonl");
            var seq = File.Exists(file) ? File.ReadLines(file).Count() + 1 : 1;
            var ev = new DeveloperMissionEvent(seq, DateTimeOffset.UtcNow, kind, message, taskId, modelId, details);
            File.AppendAllText(file, JsonSerializer.Serialize(ev, LineJsonOptions) + "\n", Encoding.UTF8);
            return ev;
        }
    }

    public IReadOnlyList<DeveloperMissionEvent> ReadEvents(string missionId, int maxTail = 200)
    {
        lock (_gate)
        {
            var file = Path.Combine(GetMissionDirectory(missionId), "events.jsonl");
            if (!File.Exists(file)) return [];
            var lines = File.ReadLines(file).TakeLast(Math.Max(1, maxTail));
            var list = new List<DeveloperMissionEvent>();
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var ev = JsonSerializer.Deserialize<DeveloperMissionEvent>(line, LineJsonOptions);
                    if (ev is not null) list.Add(ev);
                }
                catch (JsonException) { }
            }
            return list;
        }
    }

    private List<DeveloperMissionMessage> ReadTranscriptUnlocked(string missionDir)
    {
        var file = Path.Combine(missionDir, "transcript.jsonl");
        if (!File.Exists(file)) return [];
        var list = new List<DeveloperMissionMessage>();
        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var item = JsonSerializer.Deserialize<DeveloperMissionMessage>(line, LineJsonOptions);
            if (item is not null) list.Add(item);
        }
        return list;
    }

    private string EnsureMissionDirectory(string missionId)
    {
        var dir = GetMissionDirectory(missionId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void AtomicWriteJson<T>(string path, T value)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var json = JsonSerializer.Serialize(value, JsonOptions);
        using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static void ValidateId(string id, string paramName)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128
            || !id.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new ArgumentException($"Invalid identifier '{id}'.", paramName);
        }
    }
}
