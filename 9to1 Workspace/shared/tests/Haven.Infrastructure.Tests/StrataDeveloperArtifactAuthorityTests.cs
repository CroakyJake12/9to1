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
