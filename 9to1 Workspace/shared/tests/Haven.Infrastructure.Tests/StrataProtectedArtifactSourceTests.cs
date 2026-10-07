using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Microsoft.Win32.SafeHandles;
using Xunit;
namespace Haven.Infrastructure.Tests;

// Actual kernel/file/parser observations, no approved installation or model-use grant is
// synthesized. Native CUDA/assets and a positive installed receipt stay separately UNRUN.
public sealed class StrataProtectedArtifactSourceTests
{
    [Fact]
    public async Task Missing_installation_source_refuses_before_any_task_or_file_lookup()
    {
        var source = new StrataNativeArtifactSource(null!, null!); var scope = new Scope();
        var model = new ModelIdentity("strata", "model", "not-an-issued-revision");
        var raw = source.AcquireOriginalAsync(model, Admission(), scope, default);
        Exception? primary = null; InferenceEngineException? known = null;
        try
        {
            var error = await Assert.ThrowsAsync<InferenceEngineException>(() => raw); known = error;
            Assert.Equal(DulcheErrorCode.ProviderUnavailable, error.Error.Code);
            Assert.Equal("STRATA_VERIFIED_INSTALLATION_SOURCE_REQUIRED", error.Error.Message);
            Assert.Equal(model.StableKey, error.Error.Target);
            Assert.NotEmpty(scope.Raw); Assert.True(raw.IsFaulted);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            Task? close = null;
            try
            {
                close = source.DisposeAsync().AsTask();
                var failure = await Assert.ThrowsAsync<AggregateException>(() => close);
                Assert.NotNull(known); Assert.NotEmpty(failure.Flatten().InnerExceptions);
                Assert.All(failure.Flatten().InnerExceptions, cause => Assert.Same(known, cause));
            }
            catch (Exception cause)
            {
                var causes = new List<Exception>(); if (primary is not null) causes.Add(primary); causes.Add(cause);
                if (close?.Exception is { } group) { causes.Add(group); causes.AddRange(group.InnerExceptions); }
                throw new AggregateException("Actual installation-refusal fixture body and source cleanup failed.", causes);
            }
        }
    }
    [Fact]
    public async Task Taskless_observation_cannot_use_a_model_ID_as_permission()
    {
        var artifacts = new StrataNativeArtifactSource(null!, null!); var scope = new Scope();
        var source = new StrataRuntimeObservationSource(artifacts, () => null);
        var error = await Assert.ThrowsAsync<InferenceEngineException>(() => source.ObserveOriginalAsync(new("strata", "model"), scope, default));
        Assert.Equal("STRATA_ORIGINAL_REQUEST_MODEL_USE_ADMISSION_REQUIRED", error.Error.Message);
        Assert.Empty(scope.Raw); await artifacts.DisposeAsync();
    }
    [Fact]
    public async Task Synchronous_observation_callback_cancellation_is_faulted_without_a_canceled_original_task()
    {
        var artifacts = new StrataNativeArtifactSource(null!, null!); var scope = new Scope();
        var original = new OperationCanceledException("actual admission getter fault");
        var source = new StrataRuntimeObservationSource(artifacts, () => throw original);
        var actual = source.ObserveOriginalAsync(new("strata", "model"), scope, default);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Same(original, Assert.Single(failure.InnerExceptions)); Assert.True(actual.IsFaulted);
        Assert.False(actual.IsCanceled); Assert.Empty(scope.Raw); await artifacts.DisposeAsync();
    }
    [Fact]
    public async Task Post_callback_scope_cancellation_stays_faulted_and_cannot_start_artifact_acquisition()
    {
        var artifacts = new StrataNativeArtifactSource(null!, null!);
        var original = new OperationCanceledException("actual post-callback scope fault");
        var scope = new CancelAfterScope(original); var calls = 0;
        var source = new StrataRuntimeObservationSource(artifacts, () => { calls++; return Admission(); });
        var actual = source.ObserveOriginalAsync(new("strata", "model"), scope, default);
        var failure = await Assert.ThrowsAsync<AggregateException>(() => actual);
        Assert.Same(original, Assert.Single(failure.InnerExceptions)); Assert.Equal(1, calls);
        Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled); Assert.Empty(scope.Raw); await artifacts.DisposeAsync();
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public void Held_root_observation_refuses_actual_path_replacement()
    {
        var path = Directory.CreateTempSubdirectory("strata-kernel-").FullName; object? root = null;
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original protected Strata fixture requires Linux.");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            root = Root(path, false); Call(root, "DemandCurrent");
            Directory.Move(path, path + "-old"); Directory.CreateDirectory(path);
            var error = Assert.Throws<TargetInvocationException>(() => Call(root, "DemandCurrent"));
            Assert.IsType<IOException>(error.InnerException);
        }
        finally { if (root is IDisposable disposable) disposable.Dispose(); if (Directory.Exists(path)) Directory.Delete(path); if (Directory.Exists(path + "-old")) Directory.Delete(path + "-old"); }
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public void Symlinked_artifact_root_never_substitutes_for_the_actual_held_directory()
    {
        var path = Directory.CreateTempSubdirectory("strata-kernel-").FullName;
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original protected Strata fixture requires Linux.");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateSymbolicLink(path + "-link", path);
            var error = Assert.Throws<TargetInvocationException>(() => Root(path + "-link", false));
            var kernel = Assert.IsType<System.ComponentModel.Win32Exception>(error.InnerException);
            Assert.Equal(40, kernel.NativeErrorCode); // Actual ELOOP, not an unavailable kernel fallback.
        }
        finally { if (Directory.Exists(path + "-link")) Directory.Delete(path + "-link"); Directory.Delete(path); }
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Matching_digest_of_a_mutable_file_is_not_protected_installation()
    {
        var path = Directory.CreateTempSubdirectory("strata-kernel-").FullName; object? root = null;
        var scope = new Scope(); var work = Work(scope); var bytes = Encoding.ASCII.GetBytes("actual mutable bytes");
        try
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original protected Strata fixture requires Linux.");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllBytes(Path.Combine(path, "worker"), bytes);
            File.SetUnixFileMode(Path.Combine(path, "worker"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            root = Root(path, false);
            var read = (Task)root.GetType().GetMethod("ReadFileAsync")!.Invoke(root,
                [new StrataInstalledFile("worker", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))), work, CancellationToken.None, true])!;
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => read);
            Assert.True(failure is UnauthorizedAccessException or PlatformNotSupportedException);
            Assert.Empty(scope.Raw); // Refused before hash/native execution despite matching bytes.
        }
        finally { if (root is IDisposable disposable) disposable.Dispose(); Directory.Delete(path, true); }
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Actual_safetensors_payload_overlap_and_oversized_header_are_rejected()
    {
        var validHeader = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        { ["tensor"] = new { dtype = "U8", shape = new[] { 1 }, data_offsets = new[] { 0, 1 } } });
        var overlapHeader = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        { ["one"] = new { dtype = "U8", shape = new[] { 1 }, data_offsets = new[] { 0, 1 } },
            ["two"] = new { dtype = "U8", shape = new[] { 1 }, data_offsets = new[] { 0, 1 } } });
        await Check(validHeader, payload: [7], valid: true);
        await Check(overlapHeader, payload: [7], valid: false);
        var oversized = BitConverter.GetBytes(8UL * 1024 * 1024 + 1);
        await Check(null, oversized, valid: false);
        static async Task Check(byte[]? header, byte[] payload, bool valid)
        {
            var file = Path.GetTempFileName(); var scope = new Scope();
            try
            {
                var bytes = header is null ? payload : BitConverter.GetBytes((ulong)header.Length).Concat(header).Concat(payload).ToArray();
                await File.WriteAllBytesAsync(file, bytes); using var handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read);
                var read = (Task)RootType.GetMethod("ValidateSafetensorsAsync", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, [handle, (ulong)bytes.Length, Work(scope), CancellationToken.None])!;
                if (valid) await read; else await Assert.ThrowsAsync<InvalidDataException>(() => read);
                Assert.NotEmpty(scope.Raw); Assert.All(scope.Raw, raw => Assert.True(raw.IsCompleted));
            }
            finally { File.Delete(file); }
        }
    }
    [Fact]
    public async Task Hardware_source_callback_saved_after_scope_return_cannot_later_acquire_a_raw_task()
    {
        var delayed = new DelayScope(); var custodyType = typeof(StrataHardwareObservationSource).GetNestedType("ProbeCustody", BindingFlags.NonPublic)!;
        var custody = Activator.CreateInstance(custodyType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [delayed], null)!;
        var calls = 0; Func<Task<int>> factory = () => { calls++; return Task.FromResult(1); };
        var readMethod = custodyType.GetMethods().Single(method => method.Name == "ReadAsync" && method.IsGenericMethod).MakeGenericMethod(typeof(int));
        var read = (Task<int>)readMethod.Invoke(custody, [factory])!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => read);
        Assert.Equal(0, calls); Assert.NotNull(delayed.Delayed);
        Assert.Throws<InvalidOperationException>(() => delayed.Delayed!()); Assert.Equal(0, calls);
        Assert.Empty(delayed.Raw);
    }
    [Fact]
    public async Task Actual_gated_source_retention_callback_cannot_join_before_start_release()
    {
        var source = new StrataNativeArtifactSource(null!, null!); var scope = new RetentionScope(source);
        var model = new ModelIdentity("strata", "unsupported-installation");
        var actual = source.AcquireOriginalAsync(model, Admission(), scope, default);
        InferenceEngineException? expected = null; Exception? primary = null; Task? close = null;
        try
        {
            expected = await Assert.ThrowsAsync<InferenceEngineException>(() => actual);
            Assert.Equal(1, scope.Refused); Assert.NotNull(scope.Retained); Assert.True(scope.Retained!.IsFaulted);
            var works = (System.Collections.IEnumerable)typeof(StrataNativeArtifactSource)
                .GetField("_work", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source)!;
            Assert.Same(scope.Retained, Assert.Single(works.Cast<object>()).GetType().GetField("Driver")!.GetValue(Assert.Single(works.Cast<object>())));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            try
            {
                close = source.DisposeAsync().AsTask(); Assert.Same(close, source.DisposeAsync().AsTask());
                var error = await Assert.ThrowsAsync<AggregateException>(() => close);
                Assert.NotNull(expected); Assert.All(error.Flatten().InnerExceptions, cause => Assert.Same(expected, cause));
            }
            catch (Exception cause)
            {
                var causes = new List<Exception>(); if (primary is not null) causes.Add(primary); causes.Add(cause);
                if (close?.Exception is { } group) { causes.Add(group); causes.AddRange(group.InnerExceptions); }
                throw new AggregateException("Actual retained publication and source cleanup failed.", causes);
            }
        }
    }
    [Fact]
    public async Task Actual_retention_callback_OCE_faults_published_driver_without_a_canceled_original_task()
    {
        var source = new StrataNativeArtifactSource(null!, null!); var original = new OperationCanceledException("actual raw task retention fault");
        var scope = new RetentionCancellationScope(original);
        var actual = source.AcquireOriginalAsync(new ModelIdentity("strata", "unsupported-installation"), Admission(), scope, default);
        Exception? primary = null; Task? close = null;
        try
        {
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual);
            Assert.Same(original, Assert.Single(error.Flatten().InnerExceptions));
            Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled); Assert.NotNull(scope.Retained);
            Assert.True(scope.Retained!.IsFaulted); Assert.False(scope.Retained.IsCanceled);
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            try
            {
                close = source.DisposeAsync().AsTask(); Assert.Same(close, source.DisposeAsync().AsTask());
                var error = await Assert.ThrowsAsync<AggregateException>(() => close);
                Assert.NotEmpty(error.Flatten().InnerExceptions); Assert.All(error.Flatten().InnerExceptions, cause => Assert.Same(original, cause));
            }
            catch (Exception cause)
            {
                var causes = new List<Exception>(); if (primary is not null) causes.Add(primary); causes.Add(cause);
                if (close?.Exception is { } group) { causes.Add(group); causes.AddRange(group.InnerExceptions); }
                throw new AggregateException("Actual publication callback fault and source cleanup failed.", causes);
            }
        }
    }
    [Fact]
    public async Task Source_scope_pre_and_post_callback_code_keeps_physical_guard_across_restored_context()
    {
        var source = new StrataNativeArtifactSource(null!, null!); var context = ExecutionContext.Capture()!;
        var scope = new BoundaryScope(source, context); var work = Work(scope, source);
        var original = Task.FromResult(7); var calls = 0;
        var method = work.GetType().GetMethods().Single(candidate => candidate.Name == "ReadAsync" && candidate.IsGenericMethod).MakeGenericMethod(typeof(int));
        var actual = (Task<int>)method.Invoke(work, [(Func<Task<int>>)(() => { calls++; return original; })])!;
        Exception? primary = null;
        try
        {
            Assert.Equal(7, await actual); Assert.Equal(1, calls); Assert.Equal(1, scope.Before); Assert.Equal(1, scope.After);
            Assert.Same(original, Assert.Single(scope.Raw)); Assert.True(original.IsCompletedSuccessfully);
            Assert.Null(typeof(StrataNativeArtifactSource).GetField("_close", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(source));
        }
        catch (Exception cause) { primary = cause; throw; }
        finally
        {
            var causes = new List<Exception>(); Task? close = null;
            try { close = source.DisposeAsync().AsTask(); } catch (Exception cause) { Capture(null, cause); }
            try { await actual; } catch (Exception cause) { Capture(actual, cause); }
            if (close is not null) try { await close; } catch (Exception cause) { Capture(close, cause); }
            if (causes.Count != 0)
            {
                if (primary is not null && !causes.Any(cause => ReferenceEquals(cause, primary))) causes.Insert(0, primary);
                throw new AggregateException("Actual outer-scope fixture body and independently joined source cleanup failed.", causes);
            }
            void Capture(Task? raw, Exception cause)
            {
                Add(cause); if (raw?.Exception is { } group) { Add(group); foreach (var direct in group.InnerExceptions) Add(direct); }
            }
            void Add(Exception cause) { if (!causes.Any(originalCause => ReferenceEquals(originalCause, cause))) causes.Add(cause); }
        }
    }
    private static Type RootType => typeof(StrataNativeArtifactSource).GetNestedType("ProtectedRoot", BindingFlags.NonPublic)!;
    [Fact]
    public async Task Actual_async_installation_close_cannot_join_its_encompassing_source_close_phase()
    {
        var source = new StrataNativeArtifactSource(null!, null!); var scope = new Scope(); var work = Work(scope);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var installation = new UnissuedClosingInstallation(source, entered, release); var workType = work.GetType();
        Func<Task> body = async () => { await (Task)workType.GetMethod("CloseAsync")!.Invoke(work, [installation])!; };
        var close = (Task)typeof(StrataNativeArtifactSource).GetMethod("RunClosePhaseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(source, [body])!;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(close.IsCompleted);
            release.TrySetResult(); await close.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(installation.OriginalClose); Assert.True(installation.OriginalClose!.IsFaulted);
            Assert.Contains(scope.Raw, raw => ReferenceEquals(raw, installation.OriginalClose));
            Assert.IsType<InvalidOperationException>(installation.OriginalClose.Exception!.InnerException);
        }
        finally { release.TrySetResult(); await close; await source.DisposeAsync(); }
    }
    [Fact]
    public void Missing_verified_build_inventory_does_not_turn_a_requested_feature_into_engine_support()
    {
        var hardware = new StrataOriginalHardwareObservation(new("actual-control", "Linux", "X64", 1,
            [], new HashSet<string>()), "strata/actual-control-build", 1, 1, true, false, ["actual-registration"], []);
        var support = (InferenceEngineSupport)typeof(StrataRuntimeObservationSource)
            .GetMethod("ObserveBuildSupport", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [hardware, null])!;
        var model = new InferenceModelRequirements(new("strata", "model"), "source-control", "request-architecture", "request-family",
            "Safetensors", "request-quantization", new HashSet<string> { "invented-feature" }, 0, 0, 1, NativeRegistration: "actual-registration");
        var compatibility = InferenceCompatibilityRegistry.Inspect(model, hardware.Hardware, support);
        Assert.False(support.Available); Assert.Empty(support.Features); Assert.Empty(support.Architectures);
        Assert.Contains(compatibility.Unmet, unmet => unmet.Requirement == "runtime.feature");
        Assert.Contains(compatibility.Unmet, unmet => unmet.Requirement == "engine.available");
    }
    private sealed class UnissuedClosingInstallation(StrataNativeArtifactSource source, TaskCompletionSource entered,
        TaskCompletionSource release) : StrataVerifiedInstallationLease
    {
        public Task? OriginalClose;
        public override ValueTask DisposeAsync() { OriginalClose ??= CloseAsync(); return new(OriginalClose); }
        private async Task CloseAsync() { entered.TrySetResult(); await release.Task.ConfigureAwait(false); await source.DisposeAsync(); }
        public override TaskRunAttemptAdmission OriginalAdmission => throw new UnauthorizedAccessException();
        public override ModelIdentity OriginalModel => throw new UnauthorizedAccessException();
        public override string OriginalWorkerRoot => throw new UnauthorizedAccessException();
        public override StrataInstalledFile OriginalWorkerFile => throw new UnauthorizedAccessException();
        public override string OriginalCheckpointRoot => throw new UnauthorizedAccessException();
        public override IReadOnlyList<StrataInstalledFile> OriginalCheckpointFiles => throw new UnauthorizedAccessException();
        public override InferenceModelRequirements OriginalRequirements => throw new UnauthorizedAccessException();
        public override IReadOnlyList<int> OriginalCudaDeviceIndices => throw new UnauthorizedAccessException();
        public override void DemandCurrentOriginalInstallation() => throw new UnauthorizedAccessException();
    }
    private static object Root(string path, bool immutable) => Activator.CreateInstance(RootType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [path, immutable], null)!;
    private static void Call(object instance, string method) => instance.GetType().GetMethod(method)!.Invoke(instance, null);
    private static object Work(Scope scope, StrataNativeArtifactSource? sameSource = null) => Activator.CreateInstance(typeof(StrataNativeArtifactSource).GetNestedType("Work", BindingFlags.NonPublic)!,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [sameSource ?? new StrataNativeArtifactSource(null!, null!), scope], null)!;
    private static TaskRunAttemptAdmission Admission()
    {
        var snapshot = new TaskExecutionSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "unconfigured negative",
            TaskExecutionLifecycle.Running, TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        return new(snapshot, Guid.NewGuid(), new UnissuedLease());
    }
    private sealed class UnissuedLease : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => throw new UnauthorizedAccessException();
        public Guid AttemptId => Guid.Empty; public TaskRunRouteCandidate Candidate => throw new UnauthorizedAccessException();
        public string ReceiptReference => "unissued"; public ValueTask RevalidateAsync(CancellationToken token) => throw new UnauthorizedAccessException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private class Scope : IInferenceEngineOriginalSourceScope
    {
        public readonly List<Task> Raw = [];
        public virtual T InvokeOriginalFactory<T>(Func<T> factory) => factory();
        public T InvokeOriginalCleanup<T>(Func<T> cleanup) => cleanup();
        public virtual void RetainOriginalTask(Task raw) => Raw.Add(raw);
    }
    private sealed class RetentionScope(StrataNativeArtifactSource source) : Scope
    {
        public Task? Retained; public int Refused;
        public override void RetainOriginalTask(Task raw)
        {
            base.RetainOriginalTask(raw); Retained = raw;
            Assert.Throws<InvalidOperationException>((Action)(() => { _ = source.DisposeAsync(); })); Refused++;
        }
    }
    private sealed class RetentionCancellationScope(OperationCanceledException original) : Scope
    {
        public Task? Retained;
        public override void RetainOriginalTask(Task raw) { base.RetainOriginalTask(raw); Retained = raw; throw original; }
    }
    private sealed class BoundaryScope(StrataNativeArtifactSource source, ExecutionContext originalContext) : Scope
    {
        public int Before, After;
        public override T InvokeOriginalFactory<T>(Func<T> factory)
        {
            Refuse(); Before++; var result = factory(); Refuse(); After++; return result;
        }
        private void Refuse() => ExecutionContext.Run(originalContext, state =>
            Assert.Throws<InvalidOperationException>((Action)(() => { _ = source.DisposeAsync(); })), null);
    }
    private sealed class DelayScope : Scope
    {
        public Action? Delayed;
        public override T InvokeOriginalFactory<T>(Func<T> factory) { Delayed = () => { _ = factory(); }; return default!; }
    }
    private sealed class CancelAfterScope(OperationCanceledException original) : Scope
    {
        public override T InvokeOriginalFactory<T>(Func<T> factory) { _ = factory(); throw original; }
    }
}
