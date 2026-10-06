using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Metadata from the SAME opened and whole-hashed GGUF and actual loaded process.
/// No model load, capability probe, installation receipt or second model reader is created.</summary>
public sealed partial class LlamaCppModelProvider : ILlamaCppOriginalRuntimeObservationSource
{
    private readonly ConditionalWeakTable<OriginalWork, GgufRuntimeMetadata> _runtimeMetadata = new();
    private readonly ConditionalWeakTable<LlamaCppOriginalRuntimeObservation, LocalModelEndpointObservation> _runtimeObservations = new();

    /// <summary>The old loaded catalogue, through the SAME deep source scope. This never
    /// starts a model or grants use; an enabled endpoint still needs its actual issued probe.</summary>
    public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsWithinOriginalSourceAsync(
        IInferenceEngineOriginalSourceScope originalScope, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(originalScope);
        return StartOriginalCore<IReadOnlyList<ProviderModelDescriptor>>(async ct =>
        {
            if (!InvokePhysical(() => _options.Enabled)) return [];
            var actual = await ObserveEndpointBodyAsync(ct).ConfigureAwait(false);
            return InvokePhysical<IReadOnlyList<ProviderModelDescriptor>>(() =>
            {
                ct.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    if (!IsOriginalEndpointObservation(actual)) throw new InvalidOperationException("The original loaded catalogue retired before disclosure.");
                    if (!actual.ObservedCapabilities.Contains(ToolCapability.Text)) return [];
                    return [new(Id, true, new(actual.ModelId, actual.ModelBytes, "llama.cpp", "", "",
                        actual.ObservedCapabilities, actual.ObservedAt), _context, actual.ModelId)];
                }
            });
        }, token, externalLive: false, out _, originalScope: originalScope, captureRuntimeMetadata: false);
    }

    public Task<LlamaCppOriginalRuntimeObservation> ObserveOriginalRuntimeWithinSourceAsync(
        ModelIdentity sameModel, IInferenceEngineOriginalSourceScope originalScope, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(sameModel);
        ArgumentNullException.ThrowIfNull(originalScope);
        return StartOriginalCore(async ct =>
        {
            var actual = await ObserveEndpointBodyAsync(ct).ConfigureAwait(false);
            var work = CurrentOriginalWork();
            if (!_runtimeMetadata.TryGetValue(work, out var metadata))
                throw new InvalidDataException("The SAME whole-hashed model did not issue its GGUF metadata.");
            var hardware = await ReadOriginalHostHardwareAsync(ct).ConfigureAwait(false);
            return InvokePhysical(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (sameModel.ProviderId != Id || sameModel.ModelId != actual.ModelId
                    || sameModel.ArtifactRevision is not null && !StringComparer.OrdinalIgnoreCase.Equals(sameModel.ArtifactRevision, actual.ConfiguredModelSha256)
                    || !actual.ObservedCapabilities.Contains(ToolCapability.Text)
                    || !actual.ObservedCapabilities.Contains(ToolCapability.Streaming))
                    throw new InvalidDataException("The SAME initialized model requires actual prior text and stream probes.");
                lock (_sync)
                {
                    if (!IsOriginalEndpointObservation(actual) || _context is not > 0)
                        throw new InvalidOperationException("The actual local process/model observation retired before metadata disclosure.");
                    var result = new LlamaCppOriginalRuntimeObservation(actual, metadata.Architecture,
                        metadata.FileType, _context.Value, hardware);
                    _runtimeObservations.Add(result, actual);
                    return result;
                }
            }, cleanup: false);
        }, token, externalLive: false, out _, originalScope: originalScope, captureRuntimeMetadata: true);
    }

    public bool IsIssuedOriginalRuntimeObservation(LlamaCppOriginalRuntimeObservation observation)
    {
        if (observation is null) return false;
        lock (_sync) return _runtimeObservations.TryGetValue(observation, out var endpoint)
            && ReferenceEquals(endpoint, observation.Endpoint) && IsOriginalEndpointObservation(endpoint);
    }

    // Called by the owning model hash method before it resets and whole-hashes this SAME stream.
    private void CaptureOriginalRuntimeMetadata(Stream sameOpenedModel, CancellationToken token)
    {
        var work = CurrentOriginalWork();
        var metadata = InvokePhysical(() => GgufRuntimeMetadata.Read(sameOpenedModel, token), cleanup: false);
        lock (_sync)
        {
            if (_runtimeMetadata.TryGetValue(work, out _))
                throw new InvalidOperationException("One original observation cannot replace its issued GGUF metadata.");
            _runtimeMetadata.Add(work, metadata);
        }
    }

    private async Task<InferenceHardware> ReadOriginalHostHardwareAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Actual local hardware observation requires Linux procfs.");
        var cpu = await ObserveOriginalAsync(() => File.ReadAllTextAsync("/proc/cpuinfo", token)).ConfigureAwait(false);
        var memory = await ObserveOriginalAsync(() => File.ReadAllTextAsync("/proc/meminfo", token)).ConfigureAwait(false);
        var boot = await ObserveOriginalAsync(() => File.ReadAllTextAsync("/proc/sys/kernel/random/boot_id", token)).ConfigureAwait(false);
        var groups = await ObserveOriginalAsync(() => File.ReadAllTextAsync("/proc/self/cgroup", token)).ConfigureAwait(false);
        var mounts = await ObserveOriginalAsync(() => File.ReadAllTextAsync("/proc/self/mountinfo", token)).ConfigureAwait(false);
        long total = ReadKiB(memory, "MemTotal:"), available = ReadKiB(memory, "MemAvailable:");
        if (available > total || !Guid.TryParse(boot.Trim(), out _))
            throw new InvalidDataException("The actual procfs RAM or boot identity is invalid.");
        var unified = groups.Split('\n').Where(line => line.StartsWith("0::/", StringComparison.Ordinal)).ToArray();
        if (unified.Length != 1) throw new NotSupportedException("An actual unified cgroup memory observation is required.");
        // Namespace-relative membership alone may not identify the mounted leaf (e.g. a
        // cgroup2 root of /..). Kernel cgroup.procs must identify THIS process, then every
        // visible ancestor limit is read. Unmapped or ambiguous membership refuses.
        var mounted = mounts.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(fields => Array.IndexOf(fields, "-") is var separator && separator >= 6
                && fields.Length > separator + 1 && fields[separator + 1] == "cgroup2").ToArray();
        if (mounted.Length != 1) throw new NotSupportedException("One actual cgroup2 mount mapping is required.");
        var mountPath = DecodeOriginalMountPath(mounted[0][4]);
        if (!Path.IsPathFullyQualified(mountPath) || Path.GetFullPath(mountPath) != mountPath)
            throw new InvalidDataException("The actual cgroup mount path is not canonical.");
        var pending = new Queue<string>(); pending.Enqueue(mountPath);
        string? leaf = null; var seen = 0;
        while (pending.Count != 0)
        {
            if (++seen > 256) throw new NotSupportedException("The finite actual cgroup membership search is full.");
            var directory = pending.Dequeue();
            var members = await ObserveOriginalAsync(() => File.ReadAllTextAsync(Path.Combine(directory, "cgroup.procs"), token)).ConfigureAwait(false);
            if (OriginalCgroupContainsProcess(members, Environment.ProcessId))
            {
                if (leaf is not null) throw new InvalidDataException("The actual process has ambiguous cgroup membership.");
                leaf = directory;
            }
            var children = InvokePhysical(() => Directory.EnumerateDirectories(directory).Take(257).ToArray());
            if (children.Length + pending.Count + seen > 256) throw new NotSupportedException("The finite actual cgroup membership search is full.");
            foreach (var child in children) pending.Enqueue(child);
        }
        if (leaf is null) throw new InvalidDataException("The actual process cgroup is not exposed by this mount.");
        var kind = await ObserveOriginalAsync(() => File.ReadAllTextAsync(Path.Combine(leaf, "cgroup.type"), token)).ConfigureAwait(false);
        if (kind.Trim() != "domain") throw new NotSupportedException("A genuine process-domain memory hierarchy is required.");
        var limits = new List<(string Maximum, string Current)>();
        for (var ancestor = leaf; ; ancestor = Path.GetDirectoryName(ancestor)
            ?? throw new InvalidDataException("The actual cgroup ancestry left its mount."))
        {
            if (limits.Count >= 128) throw new NotSupportedException("The actual memory hierarchy exceeds its finite bound.");
            var maximumPath = Path.Combine(ancestor, "memory.max");
            var maximum = await ObserveOriginalAsync(() => File.ReadAllTextAsync(maximumPath, token)).ConfigureAwait(false);
            var used = await ObserveOriginalAsync(() => File.ReadAllTextAsync(Path.Combine(ancestor, "memory.current"), token)).ConfigureAwait(false);
            var freshMaximum = await ObserveOriginalAsync(() => File.ReadAllTextAsync(maximumPath, token)).ConfigureAwait(false);
            if (maximum != freshMaximum) throw new InvalidOperationException("The actual memory limit changed during observation.");
            limits.Add((maximum, used));
            if (ancestor == mountPath) break;
            if (!ancestor.StartsWith(mountPath.TrimEnd('/') + "/", StringComparison.Ordinal))
                throw new InvalidDataException("The actual memory hierarchy left its mounted source.");
        }
        available = OriginalMemoryHeadroom(available, limits);
        var finalMembers = await ObserveOriginalAsync(() => File.ReadAllTextAsync(Path.Combine(leaf, "cgroup.procs"), token)).ConfigureAwait(false);
        var finalGroups = await ObserveOriginalAsync(() => File.ReadAllTextAsync("/proc/self/cgroup", token)).ConfigureAwait(false);
        var finalMounts = await ObserveOriginalAsync(() => File.ReadAllTextAsync("/proc/self/mountinfo", token)).ConfigureAwait(false);
        if (!OriginalCgroupContainsProcess(finalMembers, Environment.ProcessId) || finalGroups != groups || finalMounts != mounts)
            throw new InvalidOperationException("The actual process cgroup mapping changed during observation.");
        var cpuRecords = cpu.Replace("\r", "", StringComparison.Ordinal).Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        HashSet<string>? common = null;
        foreach (var record in cpuRecords)
        {
            var flagLines = record.Split('\n').Where(line => line.StartsWith("flags", StringComparison.Ordinal)
                || line.StartsWith("Features", StringComparison.Ordinal)).ToArray();
            var separator = flagLines.Length == 1 ? flagLines[0].IndexOf(':') : -1;
            if (separator < 0)
                throw new InvalidDataException("Every observed CPU must expose its actual feature inventory.");
            var flags = flagLines[0][(separator + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            if (common is null) common = flags; else common.IntersectWith(flags);
        }
        if (common is null || common.Count == 0) throw new InvalidDataException("The actual CPU feature inventory is unavailable.");
        var architecture = RuntimeInformation.ProcessArchitecture.ToString();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            "linux\n" + architecture + "\n" + boot.Trim() + "\n" + total.ToString(CultureInfo.InvariantCulture)
            + "\n" + string.Join('\n', cpu.Split('\n').Where(line => line.StartsWith("vendor_id", StringComparison.Ordinal)
                || line.StartsWith("model name", StringComparison.Ordinal) || line.StartsWith("CPU implementer", StringComparison.Ordinal)
                || line.StartsWith("CPU part", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            + "\n" + string.Join(',', common.Order(StringComparer.Ordinal))))).ToLowerInvariant();
        // RAM is a snapshot bounded by the actual visible process hierarchy and host availability.
        // It promises no allocation, hidden-ancestor authority or future resource reservation.
        // No GPU inventory is inferred from installed libraries or environment variables.
        return new(fingerprint, "Linux", architecture, available, [], common.ToFrozenSet(StringComparer.Ordinal));
    }

    private static bool OriginalCgroupContainsProcess(string members, int processId) => processId > 0
        && members.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(value => value == processId.ToString(CultureInfo.InvariantCulture));
    private static string DecodeOriginalMountPath(string path) => path.Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal).Replace("\\012", "\n", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);
    private static long OriginalMemoryHeadroom(long hostAvailable, IReadOnlyList<(string Maximum, string Current)> limits)
    {
        if (hostAvailable < 0 || limits.Count == 0) throw new InvalidDataException("Actual host and hierarchy memory observations are required.");
        var available = hostAvailable;
        foreach (var (maximum, used) in limits)
        {
            var current = ReadNonnegative(used.Trim());
            if (maximum.Trim() != "max") available = Math.Min(available, Math.Max(0, ReadNonnegative(maximum.Trim()) - current));
        }
        return available;
    }

    private static long ReadKiB(string memory, string key)
    {
        var lines = memory.Split('\n').Where(line => line.StartsWith(key, StringComparison.Ordinal)).ToArray();
        if (lines.Length != 1) throw new InvalidDataException("Missing or duplicate actual memory field: " + key);
        var pieces = lines[0][key.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (pieces.Length != 2 || pieces[1] != "kB") throw new InvalidDataException("Unknown actual memory unit.");
        return checked(ReadNonnegative(pieces[0]) * 1024);
    }
    private static long ReadNonnegative(string value) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 ? parsed : throw new InvalidDataException("Invalid actual memory count.");

    private sealed record GgufRuntimeMetadata(string Architecture, uint FileType)
    {
        public static GgufRuntimeMetadata Read(Stream stream, CancellationToken token)
        {
            if (!stream.CanRead || !stream.CanSeek) throw new InvalidDataException("The SAME model stream must support bounded metadata inspection.");
            stream.Position = 0;
            var parser = new MetadataReader(stream, token);
            if (parser.U32() != 0x46554747 || parser.U32() is not (2 or 3) || parser.U64() == 0)
                throw new InvalidDataException("Unsupported or empty actual GGUF.");
            var count = parser.U64();
            if (count > 65536) throw new InvalidDataException("GGUF metadata exceeds the finite inspection bound.");
            string? architecture = null; uint? fileType = null;
            for (ulong index = 0; index < count; index++)
            {
                var key = parser.Text(256); var type = parser.U32();
                if (key == "general.architecture")
                {
                    if (architecture is not null || type != 8) throw new InvalidDataException("Duplicate or mistyped actual GGUF architecture.");
                    architecture = parser.Text(256);
                    if (string.IsNullOrWhiteSpace(architecture)) throw new InvalidDataException("Empty actual GGUF architecture.");
                }
                else if (key == "general.file_type")
                {
                    if (fileType is not null || type is not (4 or 10)) throw new InvalidDataException("Duplicate or mistyped actual GGUF file type.");
                    fileType = type == 4 ? parser.U32() : checked((uint)parser.U64());
                }
                else parser.Skip(type, arrayAllowed: true);
            }
            return new(architecture ?? throw new InvalidDataException("Actual GGUF architecture is missing."),
                fileType ?? throw new InvalidDataException("Actual GGUF quantization file type is missing."));
        }
        private sealed class MetadataReader(Stream stream, CancellationToken token)
        {
            private const long MaximumInspectionBytes = 32 * 1024 * 1024;
            private readonly byte[] _scratch = new byte[8];
            private long _scanned;
            private void Bound(long count)
            {
                token.ThrowIfCancellationRequested();
                if (count < 0 || count > MaximumInspectionBytes - _scanned || count > stream.Length - stream.Position)
                    throw new InvalidDataException("Truncated or oversized actual GGUF metadata.");
                _scanned += count;
            }
            private void Exact(Span<byte> bytes) { Bound(bytes.Length); stream.ReadExactly(bytes); }
            public uint U32() { Exact(_scratch.AsSpan(0, 4)); return BinaryPrimitives.ReadUInt32LittleEndian(_scratch); }
            public ulong U64() { Exact(_scratch); return BinaryPrimitives.ReadUInt64LittleEndian(_scratch); }
            public string Text(int maximum)
            {
                var length = U64();
                if (length > (ulong)maximum) throw new InvalidDataException("Oversized actual GGUF string.");
                var bytes = new byte[(int)length]; Exact(bytes);
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            public void Skip(uint type, bool arrayAllowed)
            {
                var bytes = type switch { 0 or 1 or 7 => 1, 2 or 3 => 2, 4 or 5 or 6 => 4, 10 or 11 or 12 => 8, _ => 0 };
                if (bytes > 0) { Bound(bytes); stream.Position += bytes; return; }
                if (type == 8) { var length = U64(); if (length > 1024 * 1024) throw new InvalidDataException("Oversized skipped GGUF string."); Bound((long)length); stream.Position += (long)length; return; }
                if (type != 9 || !arrayAllowed) throw new InvalidDataException("Unknown or nested actual GGUF metadata type.");
                var element = U32(); var count = U64();
                if (element > 12 || element == 9 || count > 1024 * 1024) throw new InvalidDataException("Unknown or oversized actual GGUF metadata array.");
                for (ulong index = 0; index < count; index++) Skip(element, arrayAllowed: false);
            }
        }
    }
}
