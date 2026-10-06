using System.Diagnostics;
using System.Collections.Frozen;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed record StrataOriginalHardwareObservation(InferenceHardware Hardware, string RuntimeBuild,
    uint CudaRuntimeVersion, uint CudaDriverVersion, bool CudaBuilt, bool NcclBuilt,
    IReadOnlyList<string> NativeRegistrations, IReadOnlyList<int> ActualDeviceIndices);

/// <summary>Fixed original --hardware-probe protocol. No shell, model command, GPU index,
/// executable or environment is selected by a model. SAME privately issued installed worker
/// and actual admitted request are required; all native originals settle before disclosure.</summary>
public sealed class StrataHardwareObservationSource(IOriginalStrataWorkerSource workers)
{
    private const string Origin = "015b075079c51a7aec670ee24924f920f5e7bb2b";
    public Task<StrataOriginalHardwareObservation> ObserveOriginalAsync(TaskRunAttemptAdmission sameAdmission,
        IInferenceEngineOriginalSourceScope scope, CancellationToken token)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var custody = new ProbeCustody(scope); var actual = ObservePublishedAsync(start.Task, custody, sameAdmission, token);
        try { scope.RetainOriginalTask(actual); } catch (Exception error) { custody.Add(null, error); }
        finally { start.SetResult(); }
        return actual;
    }
    private async Task<StrataOriginalHardwareObservation> ObservePublishedAsync(Task start, ProbeCustody custody,
        TaskRunAttemptAdmission admission, CancellationToken token)
    {
        await start.ConfigureAwait(false); StrataOriginalWorkerLease? worker = null; Process? process = null;
        Task? exit = null; Task? stderr = null; var started = false; StrataOriginalHardwareObservation? result = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        try
        {
            custody.Throw();
            await custody.AcquireAsync(() => workers.AcquireOriginalAsync(admission, custody, linked.Token), value => worker = value).ConfigureAwait(false);
            custody.Invoke(() => { if (worker is null || !workers.IsIssuedOriginalWorkerLease(worker, admission))
                throw new UnauthorizedAccessException("The actual hardware probe requires the SAME privately issued original worker lease."); return 0; });
            var actualWorker = worker ?? throw new InvalidOperationException("The original protected worker lease was not acquired.");
            var original = custody.Invoke(() => { actualWorker.DemandCurrentOriginalBinding(); return actualWorker.OriginalWorker; });
            custody.Invoke(() =>
            {
                var info = new ProcessStartInfo(original.ExecutablePath) { UseShellExecute = false,
                    RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = false, CreateNoWindow = true };
                info.ArgumentList.Add("--hardware-probe"); info.Environment.Clear(); info.Environment["LANG"] = "C.UTF-8";
                process = new Process { StartInfo = info }; // Actual late successful product retained before scope exit.
                actualWorker.DemandCurrentOriginalBinding(); started = process.Start();
                if (!started) throw new InvalidOperationException("The original protected hardware worker did not start.");
                return 0;
            });
            var actualProcess = process ?? throw new InvalidOperationException("The original hardware process was not acquired.");
            _ = custody.Invoke(() => { exit = actualProcess.WaitForExitAsync(CancellationToken.None); custody.RetainOriginalTask(exit); return exit; });
            var stderrGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            stderr = DrainStderrAsync(stderrGate.Task, actualProcess.StandardError.BaseStream, custody);
            try { custody.RetainOriginalTask(stderr); } finally { stderrGate.SetResult(); }
            var hello = await ReadFrameAsync(actualProcess.StandardOutput.BaseStream, custody, linked.Token).ConfigureAwait(false);
            using var helloInput = Reader(hello.Body);
            if (hello.Type != 0 || helloInput.ReadUInt32() != 1 || Text(helloInput) != Origin)
                throw new InvalidDataException("The actual no-model worker ABI/origin handshake is invalid.");
            var cuda = Bool(helloInput); var nccl = Bool(helloInput);
            var registrations = Text(helloInput).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            End(helloInput);
            var frame = await ReadFrameAsync(actualProcess.StandardOutput.BaseStream, custody, linked.Token).ConfigureAwait(false);
            if (frame.Type != 6) throw new InvalidDataException("The fixed hardware probe has no actual terminal type6 record.");
            using var input = Reader(frame.Body); NativeErrors(input);
            var runtime = input.ReadUInt32(); var driver = input.ReadUInt32(); var count = input.ReadUInt32();
            if (!cuda || runtime == 0 || driver == 0 || count > 64) throw new InvalidDataException("The actual CUDA hardware response is missing or invalid.");
            var gpus = new List<InferenceGpu>(); var indices = new List<int>();
            for (uint i = 0; i < count; i++)
            {
                var index = checked((int)input.ReadUInt32()); var name = Text(input); var uuid = Text(input);
                var major = checked((int)input.ReadUInt32()); var minor = checked((int)input.ReadUInt32());
                var totalDevice = input.ReadUInt64(); var free = input.ReadUInt64(); var totalContext = input.ReadUInt64();
                if (index < 0 || indices.Contains(index) || uuid.Length != 32 || uuid.Any(value => value is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
                    gpus.Any(gpu => gpu.Identity == uuid) || string.IsNullOrWhiteSpace(name) || major <= 0 || minor < 0 ||
                    free > totalContext || totalContext > totalDevice || totalDevice > long.MaxValue)
                    throw new InvalidDataException("The actual CUDA identity/compute/memory observation is malformed.");
                indices.Add(index); gpus.Add(new(uuid, major, minor, checked((long)free)));
            }
            End(input);
            // Exactly two frames then EOF; no model session, hidden result, trailing command or optimistic exit.
            var suffix = new byte[1]; var trailing = await custody.ReadAsync(() => actualProcess.StandardOutput.BaseStream.ReadAsync(suffix, linked.Token).AsTask()).ConfigureAwait(false);
            if (trailing != 0) throw new InvalidDataException("The actual no-model worker returned extra protocol bytes.");
            if (exit is null) throw new InvalidOperationException("The actual native exit task was not acquired.");
            await custody.ReadAsync(() => exit.WaitAsync(linked.Token)).ConfigureAwait(false);
            if (actualProcess.ExitCode != 0) throw new InvalidDataException("The actual no-model hardware worker exited unsuccessfully.");
            var meminfo = await ReadProcAsync("/proc/meminfo", custody, linked.Token).ConfigureAwait(false);
            var cpuinfo = await ReadProcAsync("/proc/cpuinfo", custody, linked.Token).ConfigureAwait(false);
            var available = ParseAvailableRam(meminfo); var features = ParseCpuFeatures(cpuinfo);
            var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { Os = "Linux", Architecture = RuntimeInformation.ProcessArchitecture.ToString(), AvailableRam = available,
                Features = features.Order(StringComparer.Ordinal).ToArray(), Runtime = runtime, Driver = driver, Gpus = gpus }))).ToLowerInvariant();
            custody.Invoke(() => { actualWorker.DemandCurrentOriginalBinding(); return 0; });
            result = new(new(fingerprint, "Linux", RuntimeInformation.ProcessArchitecture.ToString(), available,
                gpus.AsReadOnly(), features), $"strata/{Origin}/abi1/{original.ExpectedSha256.ToLowerInvariant()}",
                runtime, driver, cuda, nccl, Array.AsReadOnly(registrations), indices.AsReadOnly());
        }
        catch (Exception cause) { custody.Add(null, cause); }
        finally
        {
            if (started && process is not null)
            {
                // Request only this original process. No healthy external worker/session is canceled.
                try { custody.Invoke(() => { if (!process.HasExited) process.Kill(entireProcessTree: true); return 0; }, cleanup: true); }
                catch (Exception cause) { custody.Add(null, cause); }
                if (exit is null)
                {
                    try { _ = custody.Invoke(() => { exit = process.WaitForExitAsync(CancellationToken.None); custody.RetainOriginalTask(exit); return exit; }, cleanup: true); }
                    catch (Exception cause) { custody.Add(exit, cause); }
                }
            }
            if (exit is not null) await custody.JoinAsync(exit).ConfigureAwait(false);
            if (stderr is not null) await custody.JoinAsync(stderr).ConfigureAwait(false);
            await custody.JoinAllAsync().ConfigureAwait(false);
            if (process is not null) custody.Cleanup(process.Dispose);
            if (worker is not null) await custody.CloseAsync(worker).ConfigureAwait(false);
            await custody.JoinAllAsync().ConfigureAwait(false);
        }
        custody.Throw(); return result ?? throw new InvalidDataException("No actual original hardware observation exists.");
    }
    private static async Task DrainStderrAsync(Task start, Stream stream, ProbeCustody custody)
    {
        await start.ConfigureAwait(false); var buffer = new byte[4096]; var bytes = 0;
        while (true)
        {
            var count = await custody.ReadAsync(() => stream.ReadAsync(buffer, CancellationToken.None).AsTask()).ConfigureAwait(false);
            if (count == 0) return;
            if ((bytes = checked(bytes + count)) > 65536) throw new InvalidDataException("Actual no-model worker stderr exceeded its finite bound.");
        }
    }
    private sealed record Frame(uint Type, byte[] Body);
    private static async Task<Frame> ReadFrameAsync(Stream stream, ProbeCustody custody, CancellationToken token)
    {
        var header = new byte[8]; await custody.ReadAsync(() => stream.ReadExactlyAsync(header, token).AsTask()).ConfigureAwait(false);
        var type = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header);
        var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (length > 1024 * 1024) throw new InvalidDataException("Actual hardware frame exceeds its bounded protocol.");
        var bytes = new byte[checked((int)length)]; await custody.ReadAsync(() => stream.ReadExactlyAsync(bytes, token).AsTask()).ConfigureAwait(false);
        return new(type, bytes);
    }
    private static BinaryReader Reader(byte[] bytes) => new(new MemoryStream(bytes, false), new UTF8Encoding(false, true), false);
    private static string Text(BinaryReader reader)
    { var count = reader.ReadUInt32(); if (count > 65536 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Actual native text is truncated/oversized."); return new UTF8Encoding(false, true).GetString(reader.ReadBytes(checked((int)count))); }
    private static bool Bool(BinaryReader reader)
    { var value = reader.ReadUInt32(); return value switch { 0 => false, 1 => true, _ => throw new InvalidDataException("Actual native boolean is malformed.") }; }
    private static void End(BinaryReader reader)
    { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Actual native frame has an extra suffix."); }
    private static void NativeErrors(BinaryReader reader)
    {
        var code = reader.ReadUInt32(); var count = reader.ReadUInt32(); if (count > 64) throw new InvalidDataException("Actual hardware error inventory exceeds its bound.");
        var causes = new List<string>(); for (uint index = 0; index < count; index++) causes.Add(Text(reader));
        if (code != 0) throw new InferenceEngineException(new(DulcheErrorCode.ProviderUnavailable,
            "Actual Strata hardware probe failed: " + string.Join("; ", causes), "Strata", false));
        if (count != 0) throw new InvalidDataException("Actual successful hardware response contains errors.");
    }
    private static async Task<string> ReadProcAsync(string path, ProbeCustody custody, CancellationToken token)
    {
        FileStream? stream = null; var output = new StringBuilder();
        try
        {
            _ = custody.Invoke(() => { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true); return stream; });
            var bytes = new byte[4096];
            while (true)
            {
                var count = await custody.ReadAsync(() => stream!.ReadAsync(bytes, token).AsTask()).ConfigureAwait(false);
                if (count == 0) break;
                if (output.Length + count > 65536) throw new InvalidDataException("Actual host observation exceeds its bounded source.");
                output.Append(Encoding.ASCII.GetString(bytes, 0, count));
            }
            return output.ToString();
        }
        finally { if (stream is not null) await custody.CloseAsync(stream).ConfigureAwait(false); }
    }
    private static long ParseAvailableRam(string text)
    {
        var lines = text.Split('\n').Where(line => line.StartsWith("MemAvailable:", StringComparison.Ordinal)).ToArray();
        if (lines.Length != 1) throw new InvalidDataException("Actual host available RAM is missing/ambiguous.");
        var parts = lines[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[2] != "kB" || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var kb) || kb < 0 || kb > long.MaxValue / 1024)
            throw new InvalidDataException("Actual host available RAM is malformed.");
        return kb * 1024;
    }
    private static IReadOnlySet<string> ParseCpuFeatures(string text)
    {
        var sets = text.Split('\n').Where(line => line.StartsWith("flags", StringComparison.Ordinal) || line.StartsWith("Features", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(':') + 1)..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal)).ToArray();
        if (sets.Length == 0) throw new InvalidDataException("Actual CPU feature observation is absent.");
        var common = sets[0]; foreach (var set in sets.Skip(1)) common.IntersectWith(set); return common.ToFrozenSet(StringComparer.Ordinal);
    }
    private sealed class ProbeCustody(IInferenceEngineOriginalSourceScope scope) : IInferenceEngineOriginalSourceScope
    {
        private readonly List<Task> _raw = []; private readonly List<Exception> _errors = [];
        public T Invoke<T>(Func<T> factory, bool cleanup = false)
        {
            var thread = Environment.CurrentManagedThreadId; var active = 1; var used = 0; T result = default!;
            try
            {
                T Once() { if (Volatile.Read(ref active) != 1 || Environment.CurrentManagedThreadId != thread || Interlocked.Exchange(ref used, 1) != 0)
                    throw new InvalidOperationException("Actual hardware source callback is inactive, foreign-thread or consumed."); return result = factory(); }
                result = cleanup ? scope.InvokeOriginalCleanup(Once) : scope.InvokeOriginalFactory(Once);
                if (used == 0) throw new InvalidOperationException("Actual hardware source callback was not invoked."); return result;
            }
            catch (OperationCanceledException error) { throw new AggregateException("Actual synchronous hardware source failure.", error); }
            finally { Interlocked.Exchange(ref active, 0); }
        }
        public T InvokeOriginalFactory<T>(Func<T> factory) => Invoke(factory);
        public T InvokeOriginalCleanup<T>(Func<T> cleanup) => Invoke(cleanup, cleanup: true);
        public void RetainOriginalTask(Task raw) { lock (_raw) if (!_raw.Any(item => ReferenceEquals(item, raw))) _raw.Add(raw); scope.RetainOriginalTask(raw); }
        public void Add(Task? raw, Exception error)
        { lock (_errors) { AddCause(error); if (raw?.Exception is { } group) { AddCause(group); foreach (var cause in group.InnerExceptions) AddCause(cause); } } }
        private void AddCause(Exception error) { if (!_errors.Any(item => ReferenceEquals(item, error))) _errors.Add(error); }
        public void Throw() { lock (_errors) { if (_errors.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_errors[0]).Throw(); if (_errors.Count > 1) throw new AggregateException(_errors.ToArray()); } }
        public async Task AcquireAsync<T>(Func<Task<T>> factory, Action<T> retain)
        {
            Task<T>? raw = null; try { _ = Invoke(() => { raw = factory(); RetainOriginalTask(raw); return raw; }); } catch (Exception cause) { Add(raw, cause); }
            if (raw is not null) try { retain(await raw.ConfigureAwait(false)); } catch (Exception cause) { Add(raw, cause); }
            Throw(); if (raw is null) throw new InvalidOperationException("No actual hardware source Task was acquired.");
        }
        public async Task<T> ReadAsync<T>(Func<Task<T>> factory) { T value = default!; await AcquireAsync(factory, result => value = result).ConfigureAwait(false); return value; }
        public async Task ReadAsync(Func<Task> factory)
        { Task? raw = null; try { _ = Invoke(() => { raw = factory(); RetainOriginalTask(raw); return raw; }); } catch (Exception error) { Add(raw, error); }
            if (raw is not null) await JoinAsync(raw).ConfigureAwait(false); Throw(); if (raw is null) throw new InvalidOperationException("No actual hardware source Task was acquired."); }
        public async Task CloseAsync(IAsyncDisposable resource)
        { Task? raw = null; try { _ = Invoke(() => { raw = resource.DisposeAsync().AsTask(); RetainOriginalTask(raw); return raw; }, cleanup: true); } catch (Exception error) { Add(raw, error); }
            if (raw is not null) await JoinAsync(raw).ConfigureAwait(false); }
        public void Cleanup(Action close) { try { _ = Invoke(() => { close(); return 0; }, cleanup: true); } catch (Exception error) { Add(null, error); } }
        public async Task JoinAsync(Task task) { try { await task.ConfigureAwait(false); } catch (Exception error) { Add(task, error); } }
        public async Task JoinAllAsync() { Task[] raw; lock (_raw) raw = _raw.ToArray(); foreach (var task in raw) await JoinAsync(task).ConfigureAwait(false); }
    }
}
