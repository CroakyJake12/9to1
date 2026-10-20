using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>
/// Causal tests for the shared Home-owned Dulche runtime contracts and lifecycle owner.
/// Each axis is genuinely observed: verification is a real re-hash against a valid SHA-256,
/// and Ready is reached only when package + model + transport + live process + healthy endpoint
/// + a verified capability probe are all positive. No axis is inferred from the platform name.
/// </summary>
public sealed class HomeDulcheRuntimeServiceTests
{
    private static readonly string Sha64 = new('a', 64);
    private static readonly Uri Endpoint = new("http://127.0.0.1:9477/v1/", UriKind.Absolute);
    private static readonly HomeDulcheBrokerObservation BrokerObservation = new(42, "http-loopback", Endpoint, DateTimeOffset.UtcNow);

    // ---------------------------------------------------------------- hash validation

    [Fact]
    public void IsValidSha256Hex_rejects_non_hex_wrong_length_and_uppercase()
    {
        Assert.True(HomeDulcheHash.IsValidSha256Hex(Sha64));
        Assert.False(HomeDulcheHash.IsValidSha256Hex(null));
        Assert.False(HomeDulcheHash.IsValidSha256Hex(""));
        Assert.False(HomeDulcheHash.IsValidSha256Hex(new string('a', 63)));
        Assert.False(HomeDulcheHash.IsValidSha256Hex(new string('a', 65)));
        Assert.False(HomeDulcheHash.IsValidSha256Hex(new string('z', 64)));
        Assert.False(HomeDulcheHash.IsValidSha256Hex(Sha64.ToUpperInvariant()));
        // A benign 64-char string that is not hex is NOT a valid SHA-256.
        Assert.False(HomeDulcheHash.IsValidSha256Hex(new string('g', 64)));
    }

    // ---------------------------------------------------------------- observation composition

    private static HomeDulcheCapabilityObservation AdmittedCapability(bool capable = true) =>
        new(capable, "llamacpp", "local-model-q4", DateTimeOffset.UtcNow, "test-admission",
            BrokerObservation, Sha64);

    private static HomeDulcheRuntimeObservationSource Source(
        IHomeDulcheRuntimePackageSource package,
        IHomeDulcheBrokerAdapter broker,
        IHomeDulcheModelEvidenceSource models,
        HomeDulcheCapabilityObservation? alreadyAdmittedCapability = null) =>
        new(package, broker, models, alreadyAdmittedCapability);

    private static HomeDulcheRuntimeObservationSource ReadySource() => Source(
        new FakePackageSource(installed: true, verified: true),
        new FakeBrokerAdapter(Supported: true, BrokerObservation),
        new FakeModelEvidenceSource(VerifiedEvidence()),
        AdmittedCapability());

    private static HomeDulcheModelEvidence VerifiedEvidence() =>
        new("local-model-q4", "llamacpp", Sha64, 1234, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Ready_when_every_observed_axis_is_positive()
    {
        var observation = await ReadySource().ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Ready, observation.Lifecycle);
        Assert.True(observation.RuntimeInstalled);
        Assert.True(observation.RuntimeVerified);
        Assert.True(observation.ModelInstalled);
        Assert.True(observation.ModelVerified);
        Assert.True(observation.ProcessRunning);
        Assert.True(observation.EndpointAvailable);
        Assert.True(observation.TransportSupported);
        Assert.True(observation.CapabilitySupported);
        Assert.Empty(observation.Deficiencies);
    }

    [Fact]
    public async Task Missing_capability_probe_never_reaches_ready()
    {
        // No capability probe is configured: generation capability is a truthful blocked axis.
        var source = Source(
            new FakePackageSource(installed: true, verified: true),
            new FakeBrokerAdapter(Supported: true, BrokerObservation),
            new FakeModelEvidenceSource(VerifiedEvidence()));
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Degraded, observation.Lifecycle);
        Assert.False(observation.CapabilitySupported);
        Assert.Contains(observation.Deficiencies, d => d.Contains("capability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Healthy_endpoint_alone_does_not_prove_generation_capability()
    {
        var source = Source(
            new FakePackageSource(installed: true, verified: true),
            new FakeBrokerAdapter(Supported: true, BrokerObservation),
            new FakeModelEvidenceSource(VerifiedEvidence()),
            AdmittedCapability(capable: false));
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Degraded, observation.Lifecycle);
        Assert.True(observation.ProcessRunning);
        Assert.True(observation.EndpointAvailable);
        Assert.False(observation.CapabilitySupported);
        Assert.Contains(observation.Deficiencies, d => d.Contains("capability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_live_broker_observation_blocks_ready_even_when_transport_is_supported()
    {
        var source = Source(
            new FakePackageSource(installed: true, verified: true),
            new FakeBrokerAdapter(Supported: true, null),
            new FakeModelEvidenceSource(VerifiedEvidence()),
            AdmittedCapability());
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Degraded, observation.Lifecycle);
        Assert.True(observation.TransportSupported);
        Assert.False(observation.ProcessRunning);
        Assert.False(observation.EndpointAvailable);
        Assert.Contains(observation.Deficiencies, d => d.Contains("broker", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Missing_verified_model_blocks_ready()
    {
        var source = Source(
            new FakePackageSource(installed: true, verified: true),
            new FakeBrokerAdapter(Supported: true, BrokerObservation),
            new FakeModelEvidenceSource(null),
            AdmittedCapability());
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Degraded, observation.Lifecycle);
        Assert.False(observation.ModelInstalled);
        Assert.False(observation.ModelVerified);
        Assert.Contains(observation.Deficiencies, d => d.Contains("model", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unverified_runtime_package_blocks_ready_despite_installed_files()
    {
        var source = Source(
            new FakePackageSource(installed: true, verified: false),
            new FakeBrokerAdapter(Supported: true, BrokerObservation),
            new FakeModelEvidenceSource(VerifiedEvidence()),
            AdmittedCapability());
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Degraded, observation.Lifecycle);
        Assert.True(observation.RuntimeInstalled);
        Assert.False(observation.RuntimeVerified);
    }

    [Fact]
    public async Task Unsupported_platform_adapters_never_report_ready()
    {
        var source = Source(
            new FakePackageSource(installed: false, verified: false, "No packaged Dulche runtime adapter exists for platform 'unknown'."),
            new FakeBrokerAdapter(Supported: false, null),
            new FakeModelEvidenceSource(null));
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Degraded, observation.Lifecycle);
        Assert.False(observation.TransportSupported);
        Assert.False(observation.ProcessRunning);
        Assert.False(observation.EndpointAvailable);
        Assert.False(observation.CapabilitySupported);
        Assert.Contains(observation.Deficiencies, d => d.Contains("transport", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- packaged model evidence

    [Fact]
    public async Task Packaged_model_evidence_rehashes_the_actual_blob()
    {
        using var store = new ModelStore();
        var (root, sha, _) = store.WriteVerifiedModel();
        var source = new HomeDulchePackagedModelEvidenceSource(root);

        var evidence = await source.FindEligibleAsync(CancellationToken.None);

        Assert.NotNull(evidence);
        Assert.Equal("local-model-q4", evidence!.ModelId);
        Assert.Equal(sha, evidence.ArtifactSha256);
        Assert.True(evidence.IsVerifiedArtifact);
    }

    [Fact]
    public async Task Tampered_model_blob_is_never_admitted_as_verified()
    {
        using var store = new ModelStore();
        var (root, _, _) = store.WriteVerifiedModel();
        store.TamperBlob();
        var source = new HomeDulchePackagedModelEvidenceSource(root);

        var evidence = await source.FindEligibleAsync(CancellationToken.None);

        Assert.Null(evidence);
    }

    [Fact]
    public async Task Non_hex_manifest_sha256_is_refused()
    {
        using var store = new ModelStore();
        var (root, _, _) = store.WriteVerifiedModel();
        store.RewriteManifestSha(new string('g', 64));
        var source = new HomeDulchePackagedModelEvidenceSource(root);

        var evidence = await source.FindEligibleAsync(CancellationToken.None);

        Assert.Null(evidence);
    }

    [Fact]
    public async Task Manifest_blob_path_escaping_the_store_is_refused()
    {
        using var store = new ModelStore();
        var (root, _, _) = store.WriteVerifiedModel();
        store.RewriteManifestBlob("../../../../etc/passwd");
        var source = new HomeDulchePackagedModelEvidenceSource(root);

        var evidence = await source.FindEligibleAsync(CancellationToken.None);

        Assert.Null(evidence);
    }

    // ---------------------------------------------------------------- service lifecycle

    [Fact]
    public async Task Service_publishes_ready_observation_after_a_ready_start()
    {
        await using var service = new HomeDulcheRuntimeService(ReadySource());
        await service.StartAsync(CancellationToken.None);

        Assert.NotNull(service.CurrentObservation);
        Assert.Equal(HomeServiceLifecycleState.Ready, service.CurrentObservation!.Lifecycle);
    }

    [Fact]
    public async Task Service_throws_structured_unavailable_when_observation_is_degraded()
    {
        var degradedSource = Source(
            new FakePackageSource(installed: true, verified: true),
            new FakeBrokerAdapter(Supported: true, BrokerObservation),
            new FakeModelEvidenceSource(null),
            AdmittedCapability());
        await using var service = new HomeDulcheRuntimeService(degradedSource);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));

        Assert.Contains("HomeDulcheRuntimeUnavailable", error.Message);
        // The truthful degraded observation is retained for inspection even when start reports it.
        Assert.Equal(HomeServiceLifecycleState.Degraded, service.CurrentObservation!.Lifecycle);
        Assert.False(service.CurrentObservation.ModelVerified);
    }

    [Fact]
    public async Task Concurrent_starts_share_one_underlying_observation()
    {
        var gate = new TaskCompletionSource<HomeDulcheRuntimeObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new DelegatingObservationSource(_ => gate.Task);
        await using var service = new HomeDulcheRuntimeService(held);

        var first = service.StartAsync(CancellationToken.None);
        var second = service.StartAsync(CancellationToken.None); // reuses first, in-flight start
        Assert.False(second.IsCompleted);
        gate.SetResult(await ReadySource().ObserveAsync(CancellationToken.None));

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, held.ObserveCalls);
    }

    [Fact]
    public async Task Stop_discards_a_stale_start_completion()
    {
        var gate = new TaskCompletionSource<HomeDulcheRuntimeObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new DelegatingObservationSource(_ => gate.Task);
        await using var service = new HomeDulcheRuntimeService(held);

        var start = service.StartAsync(CancellationToken.None);
        var stopping = service.StopAsync(CancellationToken.None); // bumps generation before the held read settles
        Assert.False(start.IsCompleted);

        // Release the held observe only AFTER Stop began; Stop joins the in-flight start, and the
        // stale borrower must be DENIED success (the generation already moved on) while Stop itself
        // remains a truthful clean stop.
        gate.SetResult(await ReadySource().ObserveAsync(CancellationToken.None));

        var denial = await RecordExceptionAsync(() => start);
        Assert.NotNull(denial);
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Null(service.CurrentObservation);
    }

    [Fact]
    public async Task Stop_drains_attached_original_work_before_settling()
    {
        await using var service = new HomeDulcheRuntimeService(ReadySource());
        await service.StartAsync(CancellationToken.None);

        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.AttachOriginalWork(held.Task);
        var stopping = service.StopAsync(CancellationToken.None);

        Assert.False(stopping.IsCompleted); // Stop joins the retained original work
        held.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(service.CurrentObservation);
    }

    // ---------------------------------------------------------------- pass 3 regressions

    [Fact]
    public async Task Package_deficiencies_are_preserved_not_dropped()
    {
        // The real source can return Degraded with all positive probes except package.Verified=false.
        // Its package deficiencies must propagate, so the lifecycle owner never runs empty-diagnostic
        // fail-open.
        var source = Source(
            new FakePackageSource(installed: true, verified: false,
                "The packaged runtime executable has no verified SHA-256 pin; runtime verification is blocked."),
            new FakeBrokerAdapter(Supported: true, BrokerObservation),
            new FakeModelEvidenceSource(VerifiedEvidence()),
            AdmittedCapability());
        var observation = await source.ObserveAsync(CancellationToken.None);

        Assert.Equal(HomeServiceLifecycleState.Degraded, observation.Lifecycle);
        Assert.True(observation.RuntimeInstalled);
        Assert.False(observation.RuntimeVerified);
        Assert.True(observation.ModelVerified);
        Assert.True(observation.ProcessRunning);
        Assert.True(observation.CapabilitySupported);
        Assert.Contains(observation.Deficiencies, d => d.Contains("SHA-256", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Non_ready_observation_with_empty_deficiencies_still_fails_closed()
    {
        // Fail closed is independent of diagnostic-list contents: a Degraded observation without any
        // recorded deficiency must NOT start successfully (HomeCoreRuntime would promote it to Ready).
        var emptyDegraded = new HomeDulcheRuntimeObservation(
            HomeServiceLifecycleState.Degraded, true, true, true, true, true, true, true, true,
            "linux", "x64", "local-model-q4", []);
        var source = new DelegatingObservationSource(_ => Task.FromResult(emptyDegraded));
        await using var service = new HomeDulcheRuntimeService(source);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));

        Assert.Contains("HomeDulcheRuntimeUnavailable", error.Message);
        Assert.Equal(HomeServiceLifecycleState.Degraded, service.CurrentObservation!.Lifecycle);
    }

    [Fact]
    public async Task AttachOriginalWork_is_refused_after_stop_begins()
    {
        await using var service = new HomeDulcheRuntimeService(ReadySource());
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => service.AttachOriginalWork(Task.CompletedTask));
    }

    [Fact]
    public async Task Stop_preserves_original_fault_by_identity()
    {
        var service = new HomeDulcheRuntimeService(ReadySource());
        Exception expected = null!;
        try
        {
            await service.StartAsync(CancellationToken.None);

            expected = new InvalidOperationException("original child fault");
            service.AttachOriginalWork(Task.FromException(expected));

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync(CancellationToken.None));
            AssertOriginalPreserved(error, expected);
        }
        finally
        {
            // Dispose reuses the SAME cached close and surfaces the SAME original fault; observe it
            // explicitly instead of letting implicit dispose rethrow outside the asserted scope.
            var disposeError = await RecordExceptionAsync(() => service.DisposeAsync().AsTask());
            if (expected is not null)
            {
                Assert.NotNull(disposeError);
                AssertOriginalPreserved(disposeError!, expected);
            }
        }
    }

    [Fact]
    public async Task Repeated_stop_returns_the_same_cached_close()
    {
        await using var service = new HomeDulcheRuntimeService(ReadySource());
        await service.StartAsync(CancellationToken.None);

        var first = service.StopAsync(CancellationToken.None);
        var second = service.StopAsync(CancellationToken.None);
        Assert.True(ReferenceEquals(first, second));
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(service.CurrentObservation);
    }

    [Fact]
    public async Task Repeated_start_after_degraded_refusal_coalesces_one_source_operation()
    {
        // Item 5/6: the service reuses the SAME shared start even after it completed with a
        // truthful refusal, so repeated Starts never spawn new observations/owners. The two public
        // StartAsync wrappers are distinct caller references; proof of coalescing is ONE actual
        // source observation plus the SAME retained refusal cause by identity.
        var composed = Source(
            new FakePackageSource(installed: true, verified: true),
            new FakeBrokerAdapter(Supported: true, BrokerObservation),
            new FakeModelEvidenceSource(null));
        var counting = new DelegatingObservationSource(ct => composed.ObserveAsync(ct));
        await using var service = new HomeDulcheRuntimeService(counting);

        var first = service.StartAsync(CancellationToken.None);
        var firstError = await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        var second = service.StartAsync(CancellationToken.None);
        var secondError = await Assert.ThrowsAsync<InvalidOperationException>(() => second);

        Assert.Same(firstError, secondError);
        Assert.Equal(1, counting.ObserveCalls);
        Assert.Equal(HomeServiceLifecycleState.Degraded, service.CurrentObservation!.Lifecycle);
    }

    [Fact]
    public async Task Stop_succeeds_after_expected_readiness_refusal_start()
    {
        // Item 5/6: a truthful non-Ready start refusal is a startup diagnostic, not an
        // acquired-resource cleanup failure, so a normal close must succeed after it.
        var degradedSource = Source(
            new FakePackageSource(installed: true, verified: true),
            new FakeBrokerAdapter(Supported: true, BrokerObservation),
            new FakeModelEvidenceSource(null));
        await using var service = new HomeDulcheRuntimeService(degradedSource);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(CancellationToken.None));
        await service.StopAsync(CancellationToken.None); // must NOT rethrow the availability refusal
        Assert.Null(service.CurrentObservation);
    }

    [Fact]
    public async Task Genuine_start_fault_fails_close()
    {
        var boom = new InvalidOperationException("boom");
        var source = new DelegatingObservationSource(_ => Task.FromException<HomeDulcheRuntimeObservation>(boom));
        var service = new HomeDulcheRuntimeService(source);
        try
        {
            var startError = await RecordExceptionAsync(() => service.StartAsync(CancellationToken.None));
            Assert.Same(boom, startError);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync(CancellationToken.None));
            AssertOriginalPreserved(error, boom);
        }
        finally
        {
            // Dispose reuses the SAME cached close and surfaces the SAME genuine start fault; observe
            // it explicitly rather than letting implicit dispose rethrow outside the asserted scope.
            var disposeError = await RecordExceptionAsync(() => service.DisposeAsync().AsTask());
            Assert.NotNull(disposeError);
            AssertOriginalPreserved(disposeError!, boom);
        }
    }

    [Fact]
    public async Task Unix_socket_broker_forwards_expected_pid_to_health_probe()
    {
        // The adapter binds the systemd MainPID to the real AF_UNIX health probe so the peer
        // credential check can match the accepted socket against the observed broker process.
        long? observedPid = null;
        var adapter = new HomeDulcheLinuxBrokerAdapter(
            socketPath: "/run/user/1000/haven/inference.sock",
            brokerPidSource: _ => Task.FromResult<long?>(42),
            healthProbe: (_, pid, _) => { observedPid = pid; return Task.FromResult(true); });

        var observation = await adapter.ObserveAsync(CancellationToken.None);

        Assert.NotNull(observation);
        Assert.Equal(42, observation!.BrokerPid);
        Assert.Equal(42, observedPid);
    }

    [Fact]
    public async Task Unix_socket_broker_observation_is_positive_when_pid_and_health_observed()
    {
        // The real adapter reports a positive observation only for the actual AF_UNIX socket when
        // systemd reports a live MainPID and the socket health probe succeeds.
        var adapter = new HomeDulcheLinuxBrokerAdapter(
            socketPath: "/run/user/1000/haven/inference.sock",
            brokerPidSource: _ => Task.FromResult<long?>(42),
            healthProbe: (_, _, _) => Task.FromResult(true));
        Assert.True(adapter.Supported);

        var observation = await adapter.ObserveAsync(CancellationToken.None);

        Assert.NotNull(observation);
        Assert.Equal(42, observation!.BrokerPid);
        Assert.True(observation.IsUnixSocket);
        Assert.Equal("unix-socket", observation.EndpointKind);
        Assert.Equal("/run/user/1000/haven/inference.sock", observation.SocketPath);
    }

    [Fact]
    public async Task Unix_socket_broker_is_null_when_health_probe_fails()
    {
        var adapter = new HomeDulcheLinuxBrokerAdapter(
            socketPath: "/run/user/1000/haven/inference.sock",
            brokerPidSource: _ => Task.FromResult<long?>(42),
            healthProbe: (_, _, _) => Task.FromResult(false));

        var observation = await adapter.ObserveAsync(CancellationToken.None);

        Assert.Null(observation);
    }

    // ---------------------------------------------------------------- pass 5 causal additions

    [Fact]
    public async Task One_borrower_cancelled_wait_does_not_cancel_shared_start()
    {
        // Item 6: one caller cancelling its WAIT must not cancel another borrower's shared start.
        var gate = new TaskCompletionSource<HomeDulcheRuntimeObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new DelegatingObservationSource(_ => gate.Task);
        await using var service = new HomeDulcheRuntimeService(held);

        using var cts = new CancellationTokenSource();
        var borrowerA = service.StartAsync(cts.Token);
        var borrowerB = service.StartAsync(CancellationToken.None);

        cts.Cancel(); // cancels borrower A's wait only
        var aError = await RecordExceptionAsync(() => borrowerA);
        Assert.IsAssignableFrom<OperationCanceledException>(aError);
        Assert.False(borrowerB.IsCompleted); // the shared start is NOT cancelled

        gate.SetResult(await ReadySource().ObserveAsync(CancellationToken.None));
        await borrowerB.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, held.ObserveCalls);
    }

    [Fact]
    public async Task Genuine_prefix_looking_source_fault_still_fails_close()
    {
        // Item 7: classification of a readiness refusal is bound to the actual private service-created
        // receipt TYPE, never to message text. A genuine source fault whose message happens to begin
        // with the availability prefix must remain observable and fail close by identity.
        var boom = new InvalidOperationException("HomeDulcheRuntimeUnavailable: genuine source failure");
        var source = new DelegatingObservationSource(_ => Task.FromException<HomeDulcheRuntimeObservation>(boom));
        var service = new HomeDulcheRuntimeService(source);
        try
        {
            var startError = await RecordExceptionAsync(() => service.StartAsync(CancellationToken.None));
            Assert.Same(boom, startError);

            var stopError = await RecordExceptionAsync(() => service.StopAsync(CancellationToken.None));
            Assert.NotNull(stopError);
            AssertOriginalPreserved(stopError!, boom);
        }
        finally
        {
            var disposeError = await RecordExceptionAsync(() => service.DisposeAsync().AsTask());
            Assert.NotNull(disposeError);
            AssertOriginalPreserved(disposeError!, boom);
        }
    }

    [Fact]
    public void Restarted_broker_with_same_ids_is_not_same_generation()
    {
        // Item 9: same provider/model names never prove the same generation. The capability receipt
        // must bind to the SAME observed broker process/endpoint and SAME verified model artifact;
        // after a broker restart the same-ID evidence fails CLOSED.
        var brokerRunning = new HomeDulcheBrokerObservation(42, "http-loopback", Endpoint, DateTimeOffset.UtcNow);
        var brokerRestarted = new HomeDulcheBrokerObservation(99, "http-loopback", Endpoint, DateTimeOffset.UtcNow);
        var admitted = new HomeDulcheCapabilityObservation(true, "llamacpp", "local-model-q4",
            DateTimeOffset.UtcNow, "test-admission", brokerRunning, Sha64);
        var model = VerifiedEvidence();

        Assert.True(admitted.IsSameGeneration(model, brokerRunning));
        Assert.False(admitted.IsSameGeneration(model, brokerRestarted)); // PID changed => not same process generation
        // Absent a genuine broker/artifact binding the axis fails CLOSED (never inferred from names).
        var unbound = new HomeDulcheCapabilityObservation(true, "llamacpp", "local-model-q4",
            DateTimeOffset.UtcNow, "test-admission");
        Assert.False(unbound.IsSameGeneration(model, brokerRunning));
    }

    private static void AssertOriginalPreserved(Exception root, Exception expected)
    {
        if (root is AggregateException aggregate)
            Assert.Contains(expected, aggregate.InnerExceptions, ReferenceEqualityComparer.Instance);
        else
            Assert.Same(expected, root);
    }

    private static async Task<Exception?> RecordExceptionAsync(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }

    // ---------------------------------------------------------------- fakes and fixtures

    private sealed class FakePackageSource(bool installed, bool verified, params string[] deficiencies)
        : IHomeDulcheRuntimePackageSource
    {
        private readonly string[] _deficiencies = deficiencies;
        public ValueTask<HomeDulchePackageResult> ProbeAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new HomeDulchePackageResult(
                installed, verified, installed ? "/usr/lib/haven/llama.cpp/llama-server" : null,
                verified ? Sha64 : null, _deficiencies));
    }

    private sealed record FakeBrokerAdapter(bool Supported, HomeDulcheBrokerObservation? Observation)
        : IHomeDulcheBrokerAdapter
    {
        public string Platform => Supported ? "linux" : "unknown";
        public ValueTask<HomeDulcheBrokerObservation?> ObserveAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Observation);
    }

    private sealed record FakeModelEvidenceSource(HomeDulcheModelEvidence? Evidence)
        : IHomeDulcheModelEvidenceSource
    {
        public ValueTask<HomeDulcheModelEvidence?> FindEligibleAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(Evidence);
    }

    private sealed class DelegatingObservationSource(Func<CancellationToken, Task<HomeDulcheRuntimeObservation>> impl)
        : IHomeDulcheRuntimeObservationSource
    {
        private readonly Func<CancellationToken, Task<HomeDulcheRuntimeObservation>> _impl = impl;
        private int _calls;
        public int ObserveCalls => Volatile.Read(ref _calls);
        public Task<HomeDulcheRuntimeObservation> ObserveAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return _impl(cancellationToken);
        }
    }

    private sealed class ModelStore : IDisposable
    {
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "9to1-dulche-model-test-" + Guid.NewGuid().ToString("N"));
        private string _manifestPath = null!;
        private string _blobPath = null!;

        public ModelStore() => Directory.CreateDirectory(_root);

        public (string Root, string Sha256, string BlobRelative) WriteVerifiedModel()
        {
            var content = System.Text.Encoding.UTF8.GetBytes("fake-model-artifact-" + Guid.NewGuid().ToString("N"));
            var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var relative = Path.Combine(sha[..2], sha + ".gguf");
            _blobPath = Path.Combine(_root, "blobs", "sha256", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(_blobPath)!);
            File.WriteAllBytes(_blobPath, content);

            _manifestPath = Path.Combine(_root, "manifests", "local-model-q4.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(new
            {
                id = "local-model-q4",
                displayName = "Local Q4",
                sha256 = sha,
                blob = relative.Replace('\\', '/'),
                size = content.LongLength,
                license = "test",
                source = "test",
            }));
            return (_root, sha, relative.Replace('\\', '/'));
        }

        public void TamperBlob() => File.WriteAllText(_blobPath, "tampered-artifact-content");
        public void RewriteManifestSha(string sha) => PatchManifest(sha: sha);
        public void RewriteManifestBlob(string blob) => PatchManifest(blob: blob);

        private void PatchManifest(string? sha = null, string? blob = null)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_manifestPath));
            var root = document.RootElement;
            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(new
            {
                id = root.GetProperty("id").GetString(),
                displayName = root.GetProperty("displayName").GetString(),
                sha256 = sha ?? root.GetProperty("sha256").GetString(),
                blob = blob ?? root.GetProperty("blob").GetString(),
                size = root.GetProperty("size").GetInt64(),
                license = root.GetProperty("license").GetString(),
                source = root.GetProperty("source").GetString(),
            }));
        }

        public void Dispose()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }
}
