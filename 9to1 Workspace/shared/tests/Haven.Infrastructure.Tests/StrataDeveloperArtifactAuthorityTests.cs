using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Real local Home/profile/store/manual claim/current-state checks, and independently signed
// synthetic inventory controls. No protected installation, CUDA/native process or model load
// is asserted. The coordinator's unissued-admission control must refuse before Home review.
public sealed class StrataDeveloperArtifactAuthorityTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public void Canonical_null_model_revision_is_preserved_separately_from_reviewed_artifact_hashes()
    {
        var selection = Selection() with { Model = Selection().Model with { ArtifactRevision = null } };
        var actual = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(selection, Json));
        Assert.Null(actual.Selection.Model.ArtifactRevision); Assert.Null(actual.Requirements.Model.ArtifactRevision);
        Assert.Equal(selection.WorkerFile, actual.Selection.WorkerFile); Assert.Equal(selection.Requirements.ArtifactFingerprint, actual.Requirements.ArtifactFingerprint);
        Assert.Null(actual.BuildSupport);
    }
    [Fact]
    public void Requested_features_without_an_independent_build_inventory_never_become_support()
    {
        var selection = Selection() with { Requirements = Selection().Requirements with { RequiredFeatures = ["Text", "Tools"] } };
        var actual = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(selection, Json));
        Assert.Null(actual.BuildSupport); Assert.Contains("Tools", actual.Requirements.RequiredFeatures);
        Assert.Equal(selection.Model, actual.Requirements.Model); Assert.Equal("Safetensors", actual.Requirements.WeightFormat);
    }
    [Fact]
    public void Exact_independent_signature_binds_inventory_to_worker_but_not_model_requests_or_publisher_trust()
    {
        using var key = RSA.Create(3072); var selection = SignedSelection(key);
        var actual = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(selection, Json));
        Assert.NotNull(actual.BuildSupport); Assert.Equal(InferenceEngine.Strata, actual.BuildSupport!.Engine);
        Assert.Equal(new[] { "Text", "Streaming" }.Order(), actual.BuildSupport.Features.Order());
        Assert.DoesNotContain("Tools", actual.BuildSupport.Features);
        Assert.Contains("fixture-independent-architecture", actual.BuildSupport.Architectures);
        Assert.DoesNotContain(selection.Requirements.Architecture, actual.BuildSupport.Architectures);
        var changed = selection with { WorkerFile = selection.WorkerFile with { Sha256 = new string('b', 64) } };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(changed, Json)));
    }
    [Fact]
    public void Inventory_payload_tampering_and_private_key_input_are_rejected_before_review()
    {
        using var key = RSA.Create(3072); var selection = SignedSelection(key); var envelope = selection.IndependentBuildInventory!;
        var payload = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Convert.FromBase64String(envelope.PayloadBase64))
            .Replace("fixture-independent-architecture", "tampered-independent-architecture", StringComparison.Ordinal));
        var changed = selection with { IndependentBuildInventory = envelope with { PayloadBase64 = Convert.ToBase64String(payload) } };
        Assert.Throws<UnauthorizedAccessException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(changed, Json)));
        var privateKey = selection with { IndependentBuildInventory = envelope with { DeveloperPublicKeyPem = key.ExportPkcs8PrivateKeyPem() } };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(privateKey, Json)));
    }
    [Fact]
    public void Duplicate_json_and_duplicate_or_traversing_inventory_are_not_reviewable()
    {
        var selection = Selection(); var json = JsonSerializer.Serialize(selection, Json);
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(json[..^1] + ",\"workerRoot\":\"/other\"}"));
        var duplicate = selection with { CheckpointFiles = [selection.CheckpointFiles[0], selection.CheckpointFiles[0]] };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(duplicate, Json)));
        var traversal = selection with { WorkerFile = selection.WorkerFile with { RelativeName = "../worker" } };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(traversal, Json)));
    }
    [Fact]
    public void Verified_candidate_signed_inventory_populates_profile_and_python_path()
    {
        using var key = RSA.Create(3072);
        var selection = CandidateSignedSelection(key) with { PythonInterpreterPath = "/usr/bin/python3" };
        var actual = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(selection, Json));
        Assert.Equal(new StrataNativeProfile("candidate", 2, StrataCandidateWorker.VerifiedCandidateSourceCommit), actual.Profile);
        Assert.Equal("/usr/bin/python3", actual.PythonInterpreterPath);
        Assert.Equal("GGUF", actual.Requirements.WeightFormat);
        Assert.NotNull(actual.BuildSupport);
        Assert.False(actual.BuildSupport!.RequiresCuda); // truthful no-CUDA candidate, no manufactured receipt
        Assert.Contains("qwen35moe", actual.BuildSupport.Families);
        Assert.DoesNotContain("Tools", actual.BuildSupport.Features);
    }
    [Fact]
    public void Verified_candidate_inventory_with_foreign_source_identity_fails_closed()
    {
        using var key = RSA.Create(3072);
        var changed = CandidateSignedSelection(key, sourceIdentity: "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(changed, Json)));
    }
    [Fact]
    public void Verified_candidate_inventory_with_wrong_abi_or_profile_fails_closed()
    {
        using var key = RSA.Create(3072);
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(CandidateSignedSelection(key, abiVersion: 1), Json)));
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(CandidateSignedSelection(key, profile: "legacy"), Json)));
    }
    [Fact]
    public void Verified_candidate_capture_retains_the_complete_bound_bundle_closure()
    {
        using var key = RSA.Create(3072);
        var selection = CandidateSignedSelection(key);
        var actual = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(selection, Json));
        Assert.Equal("/unread-engine-root", actual.Selection.EngineRoot);
        Assert.Equal(new StrataInstalledFile("strata", 42, new string('f', 64)), actual.Selection.EngineFile);
        Assert.Equal(4, actual.Selection.HelperFiles!.Count);
        Assert.Equal(new string[] { "strata_tokenizer_helper.py", "strata_tokenizer.py", "gguf_reader.py", "strata-runtime-dependencies.json" }.Order(),
            actual.Selection.HelperFiles.Select(file => file.RelativeName).Order());
    }
    [Fact]
    public void Candidate_selection_requires_exactly_one_GGUF_and_refuses_split_or_empty_models()
    {
        using var key = RSA.Create(3072);
        // Exactly one GGUF is the only admitted candidate closure (verified engine-native single file).
        var single = CandidateSignedSelection(key);
        var ok = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(single, Json));
        Assert.Equal(1, ok.Selection.CheckpointFiles.Count(file => file.RelativeName.EndsWith(".gguf", StringComparison.Ordinal)));
        // Split/multi-GGUF closures are precisely refused, never claimed universal.
        var split = single with { CheckpointFiles = [new("model-a.gguf", 32, new string('e', 64)), new("model-b.gguf", 33, new string('0', 63) + "1")] };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(split, Json)));
        // Zero GGUF (non-engine-native candidate) is refused too.
        var none = single with { CheckpointFiles = [new("config.json", 16, new string('b', 64))] };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(none, Json)));
    }
    [Fact]
    public void Candidate_closure_named_mapping_is_order_independent_and_binds_exact_fixed_helper_names()
    {
        using var key = RSA.Create(3072);
        // The signed inventory may list the four helpers in any order; the required name-set check
        // must accept a valid closure regardless of list position (execution never assumes an
        // index-0 script in the native/held wiring).
        var baseSel = CandidateSignedSelection(key);
        var helpers = baseSel.HelperFiles!.ToArray();
        var reordered = new[] { helpers[2], helpers[0], helpers[3], helpers[1] }; // gguf_reader.py first
        var captured = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(baseSel with { HelperFiles = reordered }, Json));
        Assert.Equal(4, captured.Selection.HelperFiles!.Count);
        Assert.Contains("strata_tokenizer_helper.py", captured.Selection.HelperFiles.Select(file => file.RelativeName));
        // The exact fixed helper names are still required: renaming one member to an extra name is
        // refused (never a silently different helper).
        var renamed = reordered.ToArray(); renamed[0] = renamed[0] with { RelativeName = "strata_tokenizer_extra.py" };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(baseSel with { HelperFiles = renamed }, Json)));
    }
    [Fact]
    public void Candidate_signed_inventory_missing_or_partial_closure_fails_closed()
    {
        using var key = RSA.Create(3072);
        // No closure at all (signed inventory and selection both empty) -> refused.
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(CandidateSignedSelection(key, withClosure: false), Json)));
        // Selection drops the engine member while the signed inventory keeps it -> refused.
        var noEngine = CandidateSignedSelection(key) with { EngineFile = null };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(noEngine, Json)));
        // Selection drops one helper member -> refused.
        var partialHelpers = CandidateSignedSelection(key) with { HelperFiles = CandidateSignedSelection(key).HelperFiles!.Take(3).ToArray() };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(partialHelpers, Json)));
    }
    [Fact]
    public void Candidate_closure_with_wrong_or_tampered_digest_fails_closed()
    {
        using var key = RSA.Create(3072);
        var baseSel = CandidateSignedSelection(key);
        var wrongEngine = baseSel with { EngineFile = baseSel.EngineFile! with { Sha256 = new string('0', 64) } };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(wrongEngine, Json)));
        var helpers = baseSel.HelperFiles!.ToArray(); helpers[0] = helpers[0] with { Sha256 = new string('0', 64) };
        var wrongHelper = baseSel with { HelperFiles = helpers };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(wrongHelper, Json)));
    }
    [Fact]
    public void Candidate_closure_rejects_path_escape_unknown_and_duplicate_members()
    {
        using var key = RSA.Create(3072);
        var baseSel = CandidateSignedSelection(key);
        // Engine relative name must be the pinned "strata" base under the engine root.
        var escapedEngine = baseSel with { EngineFile = baseSel.EngineFile! with { RelativeName = "sub/strata" } };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(escapedEngine, Json)));
        // Helper relative name must not traverse or contain separators.
        var escapedHelpers = baseSel.HelperFiles!.ToArray(); escapedHelpers[0] = escapedHelpers[0] with { RelativeName = "../strata_tokenizer_helper.py" };
        var escapedHelper = baseSel with { HelperFiles = escapedHelpers };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(escapedHelper, Json)));
        // Unknown helper member name is refused (must be exactly the four pinned members).
        var unknownHelpers = baseSel.HelperFiles!.ToArray(); unknownHelpers[0] = unknownHelpers[0] with { RelativeName = "strata_unknown_extra.py" };
        var unknownHelper = baseSel with { HelperFiles = unknownHelpers };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(unknownHelper, Json)));
        // Duplicate helper member is refused.
        var dup = baseSel.HelperFiles!.ToArray();
        var duplicateHelper = baseSel with { HelperFiles = [dup[0], dup[0], dup[1], dup[2]] };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(duplicateHelper, Json)));
        // Engine root must be a distinct canonical POSIX root.
        var badEngineRoot = baseSel with { EngineRoot = "/unread-worker-root" };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(badEngineRoot, Json)));
    }
    [Fact]
    public void Legacy_signed_inventory_rejects_candidate_closure_fields()
    {
        using var key = RSA.Create(3072);
        var legacyWithClosure = SignedSelection(key) with
        {
            EngineRoot = "/unread-engine-root",
            EngineFile = new("strata", 42, new string('f', 64)),
            HelperFiles = [new("strata_tokenizer_helper.py", 24, new string('1', 64))]
        };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(legacyWithClosure, Json)));
        // A candidate selection that carries no signed inventory must not smuggle a closure label either.
        var candidateWithoutInventory = Selection() with { EngineRoot = "/unread-engine-root", EngineFile = new("strata", 42, new string('f', 64)) };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(candidateWithoutInventory, Json)));
    }
    [Fact]
    public void Legacy_signed_inventory_keeps_null_profile_and_ignores_python_path()
    {
        using var key = RSA.Create(3072);
        var selection = SignedSelection(key) with { PythonInterpreterPath = "/usr/bin/python3" };
        var actual = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(selection, Json));
        Assert.Null(actual.Profile); Assert.Null(actual.PythonInterpreterPath);
        Assert.Equal("Safetensors", actual.Requirements.WeightFormat);
    }
    [Fact]
    public void Untrusted_config_python_path_without_verified_candidate_is_ignored()
    {
        var selection = Selection() with { PythonInterpreterPath = "/usr/bin/python3" };
        var actual = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(selection, Json));
        Assert.Null(actual.Profile); Assert.Null(actual.PythonInterpreterPath); Assert.Null(actual.BuildSupport);
    }
    [Fact]
    public void Invalid_python_interpreter_path_is_rejected_before_review()
    {
        var relative = Selection() with { PythonInterpreterPath = "python3" }; // not canonical absolute posix
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(relative, Json)));
        var traversal = Selection() with { PythonInterpreterPath = "../usr/bin/python3" };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(traversal, Json)));
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Approved_owner_retains_verified_candidate_profile_and_python_from_selection()
    {
        await using var fixture = await ReadFixture.Create();
        using var configurations = new ProviderConfigurationStore(new Paths(fixture.Root));
        var authority = new TaskRunPermissionAuthority(new HostLocalTaskActorSource(), new ModelProviderRegistry([]), configurations,
            new PrivacyPreferenceStore(new Paths(fixture.Root)), new ModelPermissionEvaluator(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(new Paths(fixture.Root)))));
        var source = new HomeApprovedStrataDeveloperArtifactSource(fixture.Home, new TaskExecutionCoordinator(null!, null!, null), authority);
        try
        {
            using var key = RSA.Create(3072);
            var selection = CandidateSignedSelection(key) with { PythonInterpreterPath = "/usr/bin/python3" };
            var revision = source.SelectOriginalDeveloperArtifacts(JsonSerializer.Serialize(selection, Json)); Assert.Equal(1, revision);
            var field = typeof(HomeApprovedStrataDeveloperArtifactSource).GetField("_selection", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var captured = (StrataDeveloperArtifactCapture)field.GetValue(source)!;
            Assert.Equal(new StrataNativeProfile("candidate", 2, StrataCandidateWorker.VerifiedCandidateSourceCommit), captured.Profile);
            Assert.Equal("/usr/bin/python3", captured.PythonInterpreterPath);
        }
        finally { await source.DisposeAsync(); }
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Actual_individual_Home_read_is_current_then_revocation_and_actor_change_refuse()
    {
        await using var fixture = await ReadFixture.Create(); var raw = new List<Task>();
        var claim = await fixture.Claim();
        fixture.Home.StateStore.DemandOriginalClaimedReadCurrent(fixture.Home.Broker, fixture.Home.Profiles, claim.Attestation, claim.Actor, body => body(), raw.Add);
        Assert.NotEmpty(raw); Assert.All(raw, task => Assert.True(task.IsCompletedSuccessfully));
        var foreign = claim.Actor with { AuthenticationRevision = "foreign-auth-revision" };
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Home.StateStore.DemandOriginalClaimedReadCurrent(
            fixture.Home.Broker, fixture.Home.Profiles, claim.Attestation, foreign, body => body(), raw.Add));
        Assert.True((await fixture.Home.Permissions.BlockCallerAsync(claim.Actor.ActorId)).Succeeded);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.Home.StateStore.DemandOriginalClaimedReadCurrent(
            fixture.Home.Broker, fixture.Home.Profiles, claim.Attestation, claim.Actor, body => body(), raw.Add));
        Assert.All(raw, task => Assert.True(task.IsCompletedSuccessfully));
        await fixture.Complete(claim.Capability);
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Current_read_never_reenters_an_owned_Home_lock_or_escapes_it_to_native_work()
    {
        await using var fixture = await ReadFixture.Create(); var raw = new List<Task>(); var claim = await fixture.Claim();
        var held = await fixture.Home.StateStore.AcquireLocalOperationLeaseAsync(fixture.Home.Profiles, claim.Actor);
        Assert.NotNull(held);
        try
        {
            Assert.Throws<InvalidOperationException>(() => fixture.Home.StateStore.DemandOriginalClaimedReadCurrent(
                fixture.Home.Broker, fixture.Home.Profiles, claim.Attestation, claim.Actor, body => body(), raw.Add));
            Assert.Empty(raw);
        }
        finally { await held!.DisposeAsync(); }
        fixture.Home.StateStore.DemandOriginalClaimedReadCurrent(fixture.Home.Broker, fixture.Home.Profiles, claim.Attestation, claim.Actor, body => body(), raw.Add);
        // An ordinary real Home write remains available after the finite fence returned.
        Assert.True((await fixture.Home.Permissions.UnblockCallerAsync(claim.Actor.ActorId)).Succeeded);
        await fixture.Complete(claim.Capability);
    }
    [WorkspaceOriginalLinuxTests.LinuxOriginalFact]
    public async Task Unissued_Task_record_and_matching_selection_never_obtain_a_Home_review_or_artifact_grant()
    {
        await using var fixture = await ReadFixture.Create(); using var configurations = new ProviderConfigurationStore(new Paths(fixture.Root));
        var authority = new TaskRunPermissionAuthority(new HostLocalTaskActorSource(), new ModelProviderRegistry([]), configurations,
            new PrivacyPreferenceStore(new Paths(fixture.Root)), new ModelPermissionEvaluator(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(new Paths(fixture.Root)))));
        var source = new HomeApprovedStrataDeveloperArtifactSource(fixture.Home, new TaskExecutionCoordinator(null!, null!, null), authority);
        var scope = new Scope(); var selected = Selection(); var revision = source.SelectOriginalDeveloperArtifacts(JsonSerializer.Serialize(selected, Json)); Assert.Equal(1, revision);
        var snapshot = new TaskExecutionSnapshot(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "unissued control", TaskExecutionLifecycle.Running,
            TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var admission = new TaskRunAttemptAdmission(snapshot, Guid.NewGuid(), new Unissued(selected.Model));
        Exception? original = null;
        try
        {
            var raw = source.AcquireOriginalAsync(selected.Model, admission, scope, default);
            original = await Assert.ThrowsAnyAsync<Exception>(() => raw);
            Assert.False(raw.IsCompletedSuccessfully); Assert.Empty((await fixture.Home.Permissions.GetSnapshotAsync()).PendingRequests);
            Assert.All(scope.Raw, task => Assert.True(task.IsCompleted));
        }
        finally
        {
            try
            {
                var failure = await Assert.ThrowsAsync<AggregateException>(() => source.CloseAndDrainOriginalAsync());
                Assert.NotNull(original); Assert.Contains(failure.Flatten().InnerExceptions, cause => ReferenceEquals(cause, original) ||
                    original is AggregateException group && group.Flatten().InnerExceptions.Any(value => ReferenceEquals(value, cause)));
            }
            finally { await authority.CloseAndDrainOwnerReauthenticationAsync(); }
        }
    }
    [Fact]
    public void Verified_candidate_capture_retains_the_KQ_DSO_native_library_closure()
    {
        using var key = RSA.Create(3072);
        var actual = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(CandidateSignedSelection(key), Json));
        var native = Assert.Single(actual.Selection.NativeLibraries!);
        Assert.Equal("libstrata_kq.so", native.RelativeName);
        Assert.Equal(new string('9', 64), native.Sha256);
    }
    [Fact]
    public void Candidate_signed_inventory_missing_the_KQ_DSO_fails_closed()
    {
        using var key = RSA.Create(3072);
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(
            JsonSerializer.Serialize(CandidateSignedSelection(key, withNativeLibrary: false), Json)));
    }
    [Fact]
    public void Candidate_selection_with_wrong_KQ_DSO_digest_fails_closed()
    {
        using var key = RSA.Create(3072);
        var baseSel = CandidateSignedSelection(key);
        var wrong = baseSel with { NativeLibraries = [baseSel.NativeLibraries![0] with { Sha256 = new string('0', 64) }] };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(wrong, Json)));
    }
    [Fact]
    public void Candidate_native_library_rejects_path_escape_unknown_and_duplicate_members()
    {
        using var key = RSA.Create(3072);
        // Path escape/traversal is refused.
        var escaped = CandidateSignedSelection(key, natives: [new("../libstrata_kq.so", 28, new string('9', 64))]);
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(escaped, Json)));
        // An unverified cross-platform DSO name is refused (Linux libstrata_kq.so is the only pinned name).
        var dll = CandidateSignedSelection(key, natives: [new("strata_kq.dll", 28, new string('9', 64))]);
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(dll, Json)));
        // Duplicate members are refused.
        var dup = CandidateSignedSelection(key, natives: [new("libstrata_kq.so", 28, new string('9', 64)), new("libstrata_kq.so", 29, new string('8', 64))]);
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(dup, Json)));
    }
    [Fact]
    public void Legacy_signed_inventory_rejects_native_library_field()
    {
        using var key = RSA.Create(3072);
        var legacyWithNative = SignedSelection(key) with { NativeLibraries = [new("libstrata_kq.so", 28, new string('9', 64))] };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(legacyWithNative, Json)));
    }
    [Fact]
    public void Legacy_V1_selection_canonical_serialization_matches_frozen_old_shape_when_native_library_absent()
    {
        // Frozen old-29 V1 shape: the same 12 fields/property order as the current record EXCEPT the new
        // nativeLibraries member is absent. The outer snapshot reuses the ACTUAL typed nested objects
        // (selection.Model, selection.WorkerFile, selection.Requirements, ...) so every computed JSON
        // property they declare — including ModelIdentity.StableKey — is included exactly as the real
        // selection serializes it. When NativeLibraries is null the new field is omitted (WhenWritingNull),
        // so the canonical Arguments and digest must be byte-identical to the old V1 approval tuple.
        var selection = Selection();
        var oldShape = new
        {
            model = selection.Model,
            workerRoot = selection.WorkerRoot,
            workerFile = selection.WorkerFile,
            checkpointRoot = selection.CheckpointRoot,
            checkpointFiles = selection.CheckpointFiles,
            requirements = selection.Requirements,
            cudaDeviceIndices = selection.CudaDeviceIndices,
            independentBuildInventory = selection.IndependentBuildInventory,
            pythonInterpreterPath = selection.PythonInterpreterPath,
            engineRoot = selection.EngineRoot,
            engineFile = selection.EngineFile,
            helperFiles = selection.HelperFiles
        };
        var oldJson = JsonSerializer.Serialize(oldShape, Json);
        var newJson = JsonSerializer.Serialize(selection, Json);
        Assert.Equal(oldJson, newJson); // EXACT byte-for-byte old-shape bytes (no nativeLibraries property).
        var capture = StrataDeveloperArtifactCapture.Capture(newJson);
        Assert.Equal(oldJson, capture.Arguments.GetRawText());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(oldJson))), capture.Digest);
        Assert.Null(capture.Selection.NativeLibraries);
    }
    [Fact]
    public void Candidate_non_null_native_library_stays_in_canonical_arguments_and_signed_inventory_payload()
    {
        using var key = RSA.Create(3072);
        var selection = CandidateSignedSelection(key);
        var capture = StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(selection, Json));
        var argsText = capture.Arguments.GetRawText();
        // The non-null candidate field must remain in the canonical arguments AND the signed inventory
        // bound payload (it must never be elided when present).
        Assert.Contains("\"nativeLibraries\"", argsText, StringComparison.Ordinal);
        Assert.Contains("\"libstrata_kq.so\"", argsText, StringComparison.Ordinal);
        var payload = Encoding.UTF8.GetString(Convert.FromBase64String(selection.IndependentBuildInventory!.PayloadBase64));
        Assert.Contains("\"nativeLibraries\"", payload, StringComparison.Ordinal);
        Assert.Contains("\"libstrata_kq.so\"", payload, StringComparison.Ordinal);
    }
    private static StrataDeveloperArtifactSelection Selection() => new(new("strata", "fixture-model", "fixture-model-revision"),
        "/unread-worker-root", new("worker", 32, new string('a', 64)), "/unread-checkpoint-root",
        [new("config.json", 16, new string('b', 64)), new("model.safetensors", 32, new string('c', 64))],
        new(new string('d', 64), "fixture-request-architecture", "fixture-family", "F16", ["Text"], 0, 0, 32, "fixture-native-registration"), [0]);
    private static StrataDeveloperArtifactSelection SignedSelection(RSA key)
    {
        var selection = Selection(); var inventory = new StrataDeveloperBuildInventory(1, selection.WorkerFile.Sha256,
            "strata/015b075079c51a7aec670ee24924f920f5e7bb2b/abi1/" + selection.WorkerFile.Sha256,
            ["fixture-independent-architecture"], ["fixture-family"], ["Safetensors"], ["F16"], ["Text", "Streaming"], ["Linux"], ["X64"], [], ["fixture-native-registration"], 7, 0, false);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(inventory, Json);
        return selection with { IndependentBuildInventory = new(key.ExportSubjectPublicKeyInfoPem(), Convert.ToBase64String(bytes),
            Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))) };
    }
    private static readonly StrataInstalledFile[] DefaultNativeLibraries =
        [new StrataInstalledFile("libstrata_kq.so", 28, new string('9', 64))];
    private static StrataDeveloperArtifactSelection CandidateSignedSelection(RSA key, string? sourceIdentity = null,
        int? abiVersion = null, string? profile = null, bool withClosure = true, bool withNativeLibrary = true,
        StrataInstalledFile[]? natives = null)
    {
        var selection = Selection() with { CheckpointFiles = [new("model.gguf", 32, new string('e', 64))] };
        var src = sourceIdentity ?? StrataCandidateWorker.VerifiedCandidateSourceCommit;
        var abi = abiVersion ?? 2;
        var prof = profile ?? "candidate";
        var runtime = "strata/011c9e600a93bbc9fd451c563501b5831c32bbca/abi2/" + selection.WorkerFile.Sha256;
        var engine = new StrataInstalledFile("strata", 42, new string('f', 64));
        var helpers = new[]
        {
            new StrataInstalledFile("strata_tokenizer_helper.py", 24, new string('1', 64)),
            new StrataInstalledFile("strata_tokenizer.py", 25, new string('2', 64)),
            new StrataInstalledFile("gguf_reader.py", 26, new string('3', 64)),
            new StrataInstalledFile("strata-runtime-dependencies.json", 27, new string('4', 64)),
        };
        var nativeLibraries = withClosure && withNativeLibrary ? (natives ?? DefaultNativeLibraries) : null;
        var inventory = new StrataDeveloperBuildInventory(2, selection.WorkerFile.Sha256, runtime,
            ["fixture-independent-architecture"], ["qwen35moe"], ["GGUF"], ["Q4_K_M"], ["Text", "Streaming"],
            ["Linux"], ["X64"], [], ["qwen35moe"], 0, 0, false,
            Profile: prof, AbiVersion: abi, SourceIdentity: src,
            EngineFile: withClosure ? engine : null, HelperFiles: withClosure ? helpers : null,
            NativeLibraries: nativeLibraries);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(inventory, Json);
        return selection with
        {
            EngineRoot = withClosure ? "/unread-engine-root" : null,
            EngineFile = withClosure ? engine : null,
            HelperFiles = withClosure ? helpers : null,
            NativeLibraries = nativeLibraries,
            IndependentBuildInventory = new(key.ExportSubjectPublicKeyInfoPem(),
                Convert.ToBase64String(bytes), Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)))
        };
    }
    private sealed class ReadFixture(string root, HomeLocalDomainComposition home) : IAsyncDisposable
    {
        public string Root { get; } = root; public HomeLocalDomainComposition Home { get; } = home;
        public static async Task<ReadFixture> Create()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The actual finite Home READ fixture requires Linux.");
            var root = Directory.CreateTempSubdirectory("strata-home-read-").FullName;
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            HomeLocalDomainComposition? home = null;
            try
            {
                home = new(new(Path.Combine(root, "state.json")), new OperatingSystemPrincipalSource(),
                    originalResourceResolvers: [new ControlledReadAcl()], originalActionPolicies: [new HomeStrataDeveloperArtifactActionPolicySource()]);
                await home.StartOriginalAsync(); return new(root, home);
            }
            catch
            {
                if (home is not null) await home.DisposeAsync(); Directory.Delete(root, true); throw;
            }
        }
        public async Task<(HomeResourceExecutionCapability Capability, HomeClaimedResourceAttestation Attestation, AuthenticatedResourceActor Actor)> Claim()
        {
            var actor = Home.GetOriginalStartedActor(); var scope = new ResourceScope(HomeApprovedStrataDeveloperArtifactSource.Kind, "controlled-read-only", "1", ResourceAccess.Read);
            var arguments = JsonSerializer.SerializeToElement(new { worker = "synthetic unread developer artifact", revision = 1 });
            var review = Home.Broker.PrepareReviewForActor(actor, "models", HomeApprovedStrataDeveloperArtifactSource.ReadAction, [scope], arguments,
                "Controlled Home READ capability only; no installed/native outcome.", null, "controlled-read-session");
            var submitted = await Home.Broker.AuthorizePreparedReviewAsync(review); Assert.Equal(HomePermissionRequestState.PendingApproval, submitted.Request!.State);
            Assert.True((await Home.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            Assert.Equal(HomePermissionRequestState.Approved, (await Home.Broker.ObservePreparedReviewAsync(review)).Request!.State);
            var capability = await Home.Broker.BeginExecutionCapabilityAsync(review.RequestId, arguments); Assert.NotNull(capability);
            var claim = await Home.Broker.ClaimExecutionObservedAsync(capability!, "models", HomeApprovedStrataDeveloperArtifactSource.ReadAction, [scope], arguments);
            Assert.Equal(HomeResourceClaimDisposition.Claimed, claim.Disposition);
            var attestation = Home.Broker.CaptureClaimedAttestation(capability!); Assert.NotNull(attestation);
            return (capability!, attestation!, actor);
        }
        public async Task Complete(HomeResourceExecutionCapability capability)
        { Assert.True((await Home.Broker.CompleteExecutionAsync(capability, new(HomePermissionRequestState.Succeeded, "CONTROLLED_READ_CLOSED", "Controlled finite READ closed; no model/native proof.", []))).Succeeded); }
        public async ValueTask DisposeAsync()
        {
            Exception? primary = null; try { await Home.DisposeAsync(); } catch (Exception cause) { primary = cause; }
            try { Directory.Delete(Root, true); } catch (Exception cleanup) { throw new AggregateException(primary is null ? new[] { cleanup } : new[] { primary, cleanup }); }
            if (primary is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(primary).Throw();
        }
    }
    private sealed class ControlledReadAcl : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => HomeApprovedStrataDeveloperArtifactSource.Kind;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string action, ResourceScope scope, CancellationToken token) =>
            ValueTask.FromResult(new ResourceAccessDecision(action == HomeApprovedStrataDeveloperArtifactSource.ReadAction && scope.Id == "controlled-read-only" && scope.Revision == "1" && scope.Access == ResourceAccess.Read,
                "CONTROLLED_UNIT_READ", actor.ActorId, scope.Revision, actor.OrganisationId));
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "test.db"); public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments"); public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
    private sealed class Scope : IInferenceEngineOriginalSourceScope
    {
        public readonly List<Task> Raw = []; public T InvokeOriginalFactory<T>(Func<T> body) => body();
        public T InvokeOriginalCleanup<T>(Func<T> body) => body(); public void RetainOriginalTask(Task raw) => Raw.Add(raw);
    }
    private sealed class Unissued(ModelIdentity model) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => throw new UnauthorizedAccessException("No owner was issued."); public Guid AttemptId => Guid.Empty;
        public TaskRunRouteCandidate Candidate => new("unissued", 1, model.ProviderId, model.ModelId, model.ArtifactRevision, false, ["Text"]);
        public string ReceiptReference => "unissued-observation"; public ValueTask RevalidateAsync(CancellationToken token) => throw new UnauthorizedAccessException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
