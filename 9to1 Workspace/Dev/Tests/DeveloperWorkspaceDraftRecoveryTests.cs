using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>Domain/storage controls only, authored NOT RUN in the implementation packet.
/// No fixture key or copied scope is evidence of genuine Home/task/native admission.</summary>
public sealed class DeveloperWorkspaceDraftRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-draft-recovery-tests", Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private static DeveloperDraftRecoveryScope Scope() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static DeveloperDraftRecoveryPayload Payload(string text = "private unsaved source — preserve me") =>
        new([new("src/Main.cui", new string('a', 64), text, true), new("readme.md", new string('b', 64), "another dirty draft", true)], "src/Main.cui");

    [Fact]
    public async Task Restart_RetainsEncryptedCompleteDraftsAndSelectionWithoutChangingSourceOrAuthority()
    {
        var (store, project) = await CreateAsync(); var scope = Scope(); var payload = Payload();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, payload, _key);
        var saved = await store.SaveDraftRecoveryAsync(project, snapshot, 0);
        Assert.True(saved.Succeeded, saved.Error?.Message);
        var onDisk = await File.ReadAllTextAsync(PathFor(project));
        Assert.DoesNotContain(payload.Tabs[0].DraftText, onDisk);
        Assert.DoesNotContain(payload.Tabs[1].DraftText, onDisk);
        Assert.DoesNotContain("src/Main.cui", onDisk); // Even recovery paths remain inside ciphertext.
        Assert.DoesNotContain(Convert.ToBase64String(_key), onDisk);
        var restarted = new FileDeveloperWorkspaceStore(_root);
        var loaded = await restarted.GetDraftRecoveryAsync(project, scope);
        Assert.True(loaded.Succeeded, loaded.Error?.Message); Assert.NotNull(loaded.Value);
        var recovered = FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope, loaded.Value!, _key);
        Assert.Equal(payload.Tabs.ToArray(), recovered.Tabs.ToArray());
        Assert.Equal(payload.SelectedRelativePath, recovered.SelectedRelativePath);
        var authority = (await restarted.GetAsync(project.Reference.WorkspaceId)).Value!;
        Assert.Equal(project.Workspace.Revision, authority.Revision);
        Assert.Equal(project.Workspace.ModifiedAt, authority.ModifiedAt);
        Assert.Empty(authority.OpenEditors); // No invented Files identities or restored permissions.
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "workspaces"), "*.json")); // Same document, no second store.
    }

    [Fact]
    public async Task WrongProfileOrTask_IsAbsentAndCannotDecryptByChangingCopiedHeader()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        var foreign = scope with { ProfileId = Guid.NewGuid() };
        var absent = await store.GetDraftRecoveryAsync(project, foreign);
        Assert.True(absent.Succeeded); Assert.Null(absent.Value);
        Assert.Throws<UnauthorizedAccessException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, foreign, snapshot, _key));
        // Header rewriting cannot turn copied IDs into a decryptable snapshot.
        Assert.ThrowsAny<CryptographicException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, foreign, snapshot with { Scope = foreign }, _key));
        var otherTask = scope with { TaskId = Guid.NewGuid() };
        Assert.Null((await store.GetDraftRecoveryAsync(project, otherTask)).Value);
        Assert.ThrowsAny<CryptographicException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, otherTask, snapshot with { Scope = otherTask }, _key));
    }

    [Fact]
    public async Task TamperedCiphertextGenerationOrKey_RefusesWithoutRewritingStoredEvidence()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        var before = await File.ReadAllBytesAsync(PathFor(project));
        var damaged = Convert.FromBase64String(snapshot.ProtectedPayload); damaged[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope,
            snapshot with { ProtectedPayload = Convert.ToBase64String(damaged) }, _key));
        Assert.ThrowsAny<CryptographicException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope,
            snapshot with { Generation = 2 }, _key));
        var wrongKey = RandomNumberGenerator.GetBytes(32);
        try { Assert.ThrowsAny<CryptographicException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope, snapshot, wrongKey)); }
        finally { CryptographicOperations.ZeroMemory(wrongKey); }
        Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project)));
    }

    [Fact]
    public async Task StaleGenerationInvalidCiphertextAndOversizePayload_PreservePreviousBytes()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        var before = await File.ReadAllBytesAsync(PathFor(project));
        var stale = await store.SaveDraftRecoveryAsync(project,
            FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload("stale writer"), _key), 0);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, stale.Error!.Code);
        var invalid = await store.SaveDraftRecoveryAsync(project, snapshot with { Generation = 2, ProtectedPayload = "not-base64" }, 1);
        Assert.Equal(DeveloperOperationErrorCode.InvalidInput, invalid.Error!.Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2,
            Payload(new string('x', FileDeveloperWorkspaceStore.MaximumDraftRecoveryPlaintextBytes)), _key));
        Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project)));
    }

    [Fact]
    public async Task NormalWorkspaceAndCleanSessionSaves_PreserveEncryptedRecoveryAndRebindOnlyUnchangedProject()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        Assert.True((await store.SaveEditorSessionAsync(project, scope.ProfileId, 0,
            [new("clean.cui", new string('c', 64), false)], "clean.cui")).Succeeded);
        Assert.Equal(snapshot, (await store.GetDraftRecoveryAsync(project, scope)).Value);
        var updated = await store.SaveAsync(project.Workspace with
        { Settings = project.Workspace.Settings with { DefaultAiReviewMode = "manual" } }, project.Workspace.Revision);
        Assert.True(updated.Succeeded, updated.Error?.Message);
        var rebound = project with { Workspace = updated.Value!, Reference = project.Reference with { WorkspaceRevision = updated.Value!.Revision } };
        var loaded = await store.GetDraftRecoveryAsync(rebound, scope);
        Assert.True(loaded.Succeeded, loaded.Error?.Message); Assert.Equal(snapshot, loaded.Value);
        Assert.Equal(Payload().Tabs.ToArray(), FileDeveloperWorkspaceStore.UnprotectDraftRecovery(rebound, scope, loaded.Value!, _key).Tabs.ToArray());
        Assert.NotNull((await store.GetEditorSessionAsync(rebound, scope.ProfileId)).Value);
        var renamed = rebound.Project with { Name = "Changed source project", Revision = rebound.Project.Revision + 1 };
        var changed = await store.SaveAsync(rebound.Workspace with { Projects = [renamed] }, rebound.Workspace.Revision);
        Assert.True(changed.Succeeded);
        var newProject = rebound with { Workspace = changed.Value!, Project = renamed,
            Reference = rebound.Reference with { WorkspaceRevision = changed.Value!.Revision, ProjectRevision = renamed.Revision } };
        var refused = await store.GetDraftRecoveryAsync(newProject, scope);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, refused.Error!.Code);
        Assert.Throws<UnauthorizedAccessException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(newProject, scope, snapshot, _key));
    }

    [Fact]
    public async Task ConcurrentStoreInstances_AdmitOneRecoveryGenerationWriter()
    {
        var (store, project) = await CreateAsync(); var scope = Scope(); var other = new FileDeveloperWorkspaceStore(_root);
        var first = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload("first complete draft"), _key);
        var second = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload("second complete draft"), _key);
        var results = await Task.WhenAll(store.SaveDraftRecoveryAsync(project, first, 0), other.SaveDraftRecoveryAsync(project, second, 0));
        Assert.Single(results.Where(value => value.Succeeded));
        Assert.Single(results.Where(value => value.Error?.Code == DeveloperOperationErrorCode.RevisionConflict));
        Assert.Equal(results.Single(value => value.Succeeded).Value,
            (await new FileDeveloperWorkspaceStore(_root).GetDraftRecoveryAsync(project, scope)).Value);
    }

    [Fact]
    public async Task FullCanonicalDocument_RefusesWithoutEvictingAnotherTasksRecovery()
    {
        var (store, project) = await CreateAsync(); var snapshots = new List<DeveloperDraftRecoverySnapshot>();
        for (var index = 0; index < 12; index++)
        {
            var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, Scope(), 1, Payload(new string('x', 120_000)), _key);
            var before = await File.ReadAllBytesAsync(PathFor(project));
            var result = await store.SaveDraftRecoveryAsync(project, snapshot, 0);
            if (!result.Succeeded)
            {
                Assert.Equal(DeveloperOperationErrorCode.CapabilityUnavailable, result.Error!.Code);
                Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project)));
                Assert.NotEmpty(snapshots);
                foreach (var retained in snapshots) Assert.Equal(retained, (await store.GetDraftRecoveryAsync(project, retained.Scope)).Value);
                return;
            }
            snapshots.Add(snapshot);
        }
        Assert.Fail("The existing one-MiB native document bound must refuse this complete set.");
    }

    [Fact]
    public async Task OrdinaryWorkspaceSave_CannotGrowPastNativeDocumentBoundAfterRecovery()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        var before = await File.ReadAllBytesAsync(PathFor(project));
        var huge = project.Workspace with
        { Settings = project.Workspace.Settings with { NonSecretPreferences = new Dictionary<string, string> { ["large-layout"] = new string('x', 1024 * 1024) } } };
        var refused = await store.SaveAsync(huge, project.Workspace.Revision);
        Assert.False(refused.Succeeded);
        Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project)));
        Assert.Equal(snapshot, (await store.GetDraftRecoveryAsync(project, scope)).Value);
    }

    [Fact]
    public async Task CorruptRecoveryCatalogue_BlocksNormalSaveAndPreservesOriginalBytes()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        var json = JsonNode.Parse(await File.ReadAllTextAsync(PathFor(project)))!;
        json["draftRecoveries"]![0]!["protectedPayload"] = "invalid-ciphertext";
        await File.WriteAllTextAsync(PathFor(project), json.ToJsonString());
        var corrupt = await File.ReadAllBytesAsync(PathFor(project));
        var saved = await store.SaveAsync(project.Workspace, project.Workspace.Revision);
        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, saved.Error!.Code);
        Assert.Equal(DeveloperOperationErrorCode.InvalidStoredData, (await store.GetDraftRecoveryAsync(project, scope)).Error!.Code);
        Assert.Equal(corrupt, await File.ReadAllBytesAsync(PathFor(project)));
    }

    [Fact]
    public async Task CancelledWriteAndInvalidDraftPaths_NeverReplacePreviousRecovery()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        var before = await File.ReadAllBytesAsync(PathFor(project));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var next = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2, Payload("new complete draft"), _key);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveDraftRecoveryAsync(project, next, 1, cancellation.Token));
        Assert.Throws<ArgumentException>(() => FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2,
            new([new("../escape.cui", new string('a', 64), "must not leave project", true)], "../escape.cui"), _key));
        Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project)));
    }

    [Fact]
    public async Task OriginalMetadataReceipt_UsesExactRawBeforeAndActualWriterAfterWithoutAuthorityChange()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        // Different valid formatting must affect the raw native document identity. Hashing a
        // reserialised old model instead would fail this control despite identical authority.
        var compact = JsonNode.Parse(await File.ReadAllTextAsync(PathFor(project)))!.ToJsonString() + "\n";
        await File.WriteAllTextAsync(PathFor(project), compact);
        var before = await File.ReadAllBytesAsync(PathFor(project));
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        var saved = await store.SaveDraftRecoveryAsync(project, snapshot, 0);
        Assert.True(saved.Succeeded, saved.Error?.Message);
        var source = (IDeveloperProjectOriginalMetadataTransitionStore)store;
        var receipt = source.GetOriginalMetadataTransition(saved.Value!);
        Assert.NotNull(receipt);
        Assert.True(source.IsIssuedOriginalMetadataTransition(receipt, saved.Value!));
        Assert.Same(store, receipt.OriginalStore); Assert.Same(saved.Value, receipt.OriginalCommittedValue);
        Assert.True(receipt.OriginalAtomicWriteTask.IsCompletedSuccessfully);
        Assert.Equal(project.Reference.WorkspaceId, receipt.WorkspaceId);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(before)).ToLowerInvariant(), receipt.BeforeDocumentSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(PathFor(project)))).ToLowerInvariant(), receipt.AfterDocumentSha256);
        Assert.Equal(64, receipt.WorkspaceAuthoritySha256.Length);
        var after = (await store.GetAsync(project.Reference.WorkspaceId)).Value!;
        Assert.Equal(project.Workspace.Revision, after.Revision);
        Assert.Equal(project.Workspace.ModifiedAt, after.ModifiedAt);
        Assert.Equal(JsonSerializer.Serialize(project.Workspace), JsonSerializer.Serialize(after));
    }

    [Fact]
    public async Task CopiedValueForeignStoreAndImpostorReceipt_DoNotGainOriginalMetadataProvenance()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        var source = (IDeveloperProjectOriginalMetadataTransitionStore)store;
        var receipt = source.GetOriginalMetadataTransition(snapshot)!;
        var copy = snapshot with { };
        Assert.Equal(snapshot, copy); Assert.NotSame(snapshot, copy);
        Assert.Null(source.GetOriginalMetadataTransition(copy));
        Assert.False(source.IsIssuedOriginalMetadataTransition(receipt, copy));
        var foreign = (IDeveloperProjectOriginalMetadataTransitionStore)new FileDeveloperWorkspaceStore(_root);
        Assert.Null(foreign.GetOriginalMetadataTransition(snapshot));
        Assert.False(foreign.IsIssuedOriginalMetadataTransition(receipt, snapshot));
        // Even reading an impostor's self-asserted observations would be a protocol error.
        Assert.False(source.IsIssuedOriginalMetadataTransition(new ImpostorMetadataTransition(), snapshot));
    }

    [Fact]
    public async Task StaleOrCancelledRecoveryWriter_NeverIssuesANewMetadataReceipt()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var first = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, first, 0)).Succeeded);
        var source = (IDeveloperProjectOriginalMetadataTransitionStore)store;
        var before = await File.ReadAllBytesAsync(PathFor(project));
        var stale = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload("stale complete draft"), _key);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, (await store.SaveDraftRecoveryAsync(project, stale, 0)).Error!.Code);
        Assert.Null(source.GetOriginalMetadataTransition(stale));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var cancelled = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2, Payload("cancelled complete draft"), _key);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveDraftRecoveryAsync(project, cancelled, 1, cancellation.Token));
        Assert.Null(source.GetOriginalMetadataTransition(cancelled));
        Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project)));
        Assert.True(source.IsIssuedOriginalMetadataTransition(source.GetOriginalMetadataTransition(first)!, first));
    }

    [Fact]
    public async Task OriginalDocumentPair_RequiresSameReceiptValueAndExactBeforeAfterBytes()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var before = await File.ReadAllTextAsync(PathFor(project));
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        var after = await File.ReadAllTextAsync(PathFor(project));
        var source = (IDeveloperProjectOriginalMetadataTransitionStore)store;
        var receipt = source.GetOriginalMetadataTransition(snapshot)!;
        Assert.True(source.VerifyOriginalMetadataTransitionDocuments(receipt, snapshot, before, after));
        Assert.False(source.VerifyOriginalMetadataTransitionDocuments(receipt, snapshot with { }, before, after));
        Assert.False(source.VerifyOriginalMetadataTransitionDocuments(new ImpostorMetadataTransition(), snapshot, before, after));
        Assert.False(source.VerifyOriginalMetadataTransitionDocuments(receipt, snapshot, before + "\n", after));
        Assert.False(source.VerifyOriginalMetadataTransitionDocuments(receipt, snapshot, before, after + "\n"));
        Assert.False(((IDeveloperProjectOriginalMetadataTransitionStore)new FileDeveloperWorkspaceStore(_root))
            .VerifyOriginalMetadataTransitionDocuments(receipt, snapshot, before, after));
    }

    [Fact]
    public async Task DocumentPair_RejectsAuthorityChangeAndLaterGenerationWithoutRewritingEvidence()
    {
        var (store, project) = await CreateAsync(); var scope = Scope(); var before = await File.ReadAllTextAsync(PathFor(project));
        var first = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, first, 0)).Succeeded);
        var firstAfter = await File.ReadAllTextAsync(PathFor(project));
        var source = (IDeveloperProjectOriginalMetadataTransitionStore)store; var firstReceipt = source.GetOriginalMetadataTransition(first)!;
        var altered = JsonNode.Parse(firstAfter)!; altered["workspace"]!["revision"] = project.Workspace.Revision + 1;
        Assert.False(source.VerifyOriginalMetadataTransitionDocuments(firstReceipt, first, before, altered.ToJsonString()));
        var next = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2, Payload("later draft"), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, next, 1)).Succeeded);
        var later = await File.ReadAllTextAsync(PathFor(project));
        Assert.False(source.VerifyOriginalMetadataTransitionDocuments(firstReceipt, first, before, later));
        Assert.Equal(later, await File.ReadAllTextAsync(PathFor(project))); // Pure validator never rewrites evidence.
    }

    private sealed class ImpostorMetadataTransition : IDeveloperProjectOriginalMetadataTransition
    {
        public IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore => throw new InvalidOperationException("Copied observation must not be consulted.");
        public IDeveloperProjectOriginalMetadataCommitValue OriginalCommittedValue => throw new InvalidOperationException("Copied observation must not be consulted.");
        public Guid WorkspaceId => throw new InvalidOperationException("Copied observation must not be consulted.");
        public string BeforeDocumentSha256 => throw new InvalidOperationException("Copied observation must not be consulted.");
        public string AfterDocumentSha256 => throw new InvalidOperationException("Copied observation must not be consulted.");
        public string WorkspaceAuthoritySha256 => throw new InvalidOperationException("Copied observation must not be consulted.");
        public Task OriginalAtomicWriteTask => throw new InvalidOperationException("Copied observation must not be consulted.");
    }

    private string PathFor(DeveloperResolvedProject project) => Path.Combine(_root, "workspaces", project.Reference.WorkspaceId.ToString("N") + ".json");
    private async Task<(FileDeveloperWorkspaceStore Store, DeveloperResolvedProject Project)> CreateAsync()
    {
        var store = new FileDeveloperWorkspaceStore(_root);
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(Path.GetTempPath()));
        var project = new DeveloperProject(Guid.NewGuid(), "cui-app", "Example", [root.RootId], "CUI", null,
            "cui", ["linux"], [], [], [], null);
        var created = await store.CreateAsync(DeveloperWorkspace.Create([root]) with { Projects = [project] });
        Assert.True(created.Succeeded, created.Error?.Message); var workspace = created.Value!;
        return (store, new(new(workspace.WorkspaceId, workspace.Revision, project.ProjectId, project.Revision, root.RootId), workspace, project, root, null));
    }
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(_key);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
