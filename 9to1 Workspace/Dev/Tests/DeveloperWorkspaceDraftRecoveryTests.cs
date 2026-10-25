using System.Security.Cryptography;
using System.Text;
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
    public async Task RestartAfterExternalMultiFileEdit_RetainsBothUnsavedDraftsAndCurrentDiskWithoutAdoption()
    {
        var sourceRoot = Path.Combine(_root, "source");
        var sourceDirectory = Path.Combine(sourceRoot, "src");
        Directory.CreateDirectory(sourceDirectory);
        var main = Path.Combine(sourceDirectory, "Main.cui");
        var other = Path.Combine(sourceRoot, "readme.md");
        const string originalMain = "<Page><Text>Saved version</Text></Page>";
        const string originalOther = "# Saved notes";
        await File.WriteAllTextAsync(main, originalMain);
        await File.WriteAllTextAsync(other, originalOther);
        var (store, project) = await CreateAsync(sourceRoot); var scope = Scope();
        static string Digest(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
        var payload = new DeveloperDraftRecoveryPayload([
            new("src/Main.cui", Digest(await File.ReadAllTextAsync(main)), "<Page><Text>Unsaved draft</Text></Page>", true),
            new("readme.md", Digest(await File.ReadAllTextAsync(other)), "# Unsaved notes", false)
        ], "src/Main.cui");
        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, payload, _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, snapshot, 0)).Succeeded);
        // Simulate another writer after the last protected recovery commit and a fresh store on restart.
        const string externalMain = "<Page><Text>External saved change</Text></Page>";
        await File.WriteAllTextAsync(main, externalMain);
        var sourceBeforeRecovery = await File.ReadAllBytesAsync(main);
        var metadataBeforeRecovery = await File.ReadAllBytesAsync(PathFor(project));
        var restarted = new FileDeveloperWorkspaceStore(_root);
        var retained = await restarted.GetDraftRecoveryAsync(project, scope);
        Assert.True(retained.Succeeded, retained.Error?.Message);
        var recovered = FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope, retained.Value!, _key);

        Assert.Equal(payload.Tabs.ToArray(), recovered.Tabs.ToArray());
        Assert.Equal(payload.SelectedRelativePath, recovered.SelectedRelativePath);
        Assert.NotEqual(recovered.Tabs[0].OriginalSha256, Digest(await File.ReadAllTextAsync(main)));
        Assert.Equal(recovered.Tabs[1].OriginalSha256, Digest(await File.ReadAllTextAsync(other)));
        Assert.Equal(sourceBeforeRecovery, await File.ReadAllBytesAsync(main));
        Assert.Equal(originalOther, await File.ReadAllTextAsync(other));
        Assert.Equal(metadataBeforeRecovery, await File.ReadAllBytesAsync(PathFor(project)));
        Assert.DoesNotContain(payload.Tabs[0].DraftText, await File.ReadAllTextAsync(PathFor(project)));
        Assert.Empty((await restarted.GetAsync(project.Reference.WorkspaceId)).Value!.OpenEditors);
        // Decryption retains conflicting versions; only the existing explicit source owner may adopt or write.
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
    public async Task RecoveryTabPaths_UseHostFilesystemCaseSemanticsAndNormalizeSelection()
    {
        var (_, project) = await CreateAsync(); var scope = Scope();
        var caseDistinctTabs = new DeveloperDraftRecoveryPayload(
            [new("src/Main.cui", new string('a', 64), "first", false),
             new("src/main.cui", new string('b', 64), "second", false)], "src/Main.cui");

        if (OperatingSystem.IsWindows())
            Assert.Throws<ArgumentException>(() => FileDeveloperWorkspaceStore.ProtectDraftRecovery(
                project, scope, 1, caseDistinctTabs, _key));
        else
        {
            var distinct = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, caseDistinctTabs, _key);
            Assert.Equal(2, FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope, distinct, _key).Tabs.Count);
        }

        var singleTabCaseVariantSelection = new DeveloperDraftRecoveryPayload(
            [new("src/Main.cui", new string('a', 64), "draft", false)], "SRC/MAIN.CUI");
        if (OperatingSystem.IsWindows())
        {
            var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1,
                singleTabCaseVariantSelection, _key);
            var reopened = FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope, snapshot, _key);
            Assert.Equal("src/Main.cui", reopened.SelectedRelativePath);
        }
        else Assert.Throws<ArgumentException>(() => FileDeveloperWorkspaceStore.ProtectDraftRecovery(
            project, scope, 1, singleTabCaseVariantSelection, _key));
    }

    [Fact]
    public async Task ProtectRecovery_ValidatesTheExactDetachedCallerPayloadItEncrypts()
    {
        var (_, project) = await CreateAsync(); var scope = Scope();
        var valid = new DeveloperDraftRecoveryTab("src/Main.cui", new string('a', 64), "complete draft", true);
        var changed = new DeveloperDraftRecoveryTab("../outside.cui", new string('b', 64), "invalid later view", true);
        var changingTabs = new ChangesAfterInitialValidEnumerations([valid], [changed]);
        var payload = new DeveloperDraftRecoveryPayload(changingTabs, valid.RelativePath);

        var snapshot = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, payload, _key);
        var recovered = FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope, snapshot, _key);

        Assert.Single(recovered.Tabs);
        Assert.Equal(valid, recovered.Tabs[0]);
        Assert.Equal(valid.RelativePath, recovered.SelectedRelativePath);
    }

    private sealed class ChangesAfterInitialValidEnumerations(
        IReadOnlyList<DeveloperDraftRecoveryTab> valid, IReadOnlyList<DeveloperDraftRecoveryTab> later) :
        IReadOnlyList<DeveloperDraftRecoveryTab>
    {
        private int _enumerations;
        public int Count => valid.Count;
        public DeveloperDraftRecoveryTab this[int index] =>
            (_enumerations < 3 ? valid : later)[index];
        public IEnumerator<DeveloperDraftRecoveryTab> GetEnumerator()
        {
            var observed = Interlocked.Increment(ref _enumerations) <= 3 ? valid : later;
            return observed.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
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
        Assert.Single(results, value => value.Succeeded);
        Assert.Single(results, value => value.Error?.Code == DeveloperOperationErrorCode.RevisionConflict);
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
    public async Task CancellationDuringProtectedKeyPreparation_PreservesReopenablePriorGeneration()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var first = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload("last durable draft"), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, first, 0)).Succeeded);
        var before = await File.ReadAllBytesAsync(PathFor(project));
        using var cancellation = new CancellationTokenSource();
        DeveloperDraftRecoverySnapshot? prepared = null;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.PrepareAndSaveDraftRecoveryAsync(project, scope, 1, (_, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                var next = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2,
                    Payload("prepared but cancelled"), _key);
                prepared = next;
                cancellation.Cancel();
                Assert.True(token.IsCancellationRequested);
                return Task.FromResult(next);
            }, cancellation.Token));

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project)));
        // A fresh store simulates reopening after the interrupted preparation. The old
        // authenticated generation remains available and the cancelled draft is absent.
        var reopened = new FileDeveloperWorkspaceStore(_root);
        var retained = await reopened.GetDraftRecoveryAsync(project, scope);
        Assert.True(retained.Succeeded, retained.Error?.Message);
        Assert.Equal(first, retained.Value);
        Assert.Equal("last durable draft", FileDeveloperWorkspaceStore
            .UnprotectDraftRecovery(project, scope, retained.Value!, _key).Tabs[0].DraftText);
        Assert.NotNull(prepared);
        Assert.Null(((IDeveloperProjectOriginalMetadataTransitionStore)store).GetOriginalMetadataTransition(prepared!));

        // Reopen does not relax the generation CAS: a later valid writer wins once,
        // while a stale recovery attempt cannot overwrite or strand that winner.
        var winner = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2, Payload("winner after reopen"), _key);
        Assert.True((await reopened.SaveDraftRecoveryAsync(project, winner, 1)).Succeeded);
        var conflict = await reopened.SaveDraftRecoveryAsync(project,
            FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2, Payload("stale after reopen"), _key), 1);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, conflict.Error!.Code);
        var afterConflict = await new FileDeveloperWorkspaceStore(_root).GetDraftRecoveryAsync(project, scope);
        Assert.Equal(winner, afterConflict.Value);
        Assert.Equal("winner after reopen", FileDeveloperWorkspaceStore
            .UnprotectDraftRecovery(project, scope, afterConflict.Value!, _key).Tabs[0].DraftText);
    }

    [Fact]
    public async Task CancellationWhileKeyPreparationAwaits_PreservesReceiptAndReleasesLockForRetry()
    {
        var (store, project) = await CreateAsync(); var scope = Scope();
        var first = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload("durable before async wait"), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, first, 0)).Succeeded);
        var originalStore = (IDeveloperProjectOriginalMetadataTransitionStore)store;
        var originalReceipt = originalStore.GetOriginalMetadataTransition(first);
        Assert.NotNull(originalReceipt);
        var before = await File.ReadAllBytesAsync(PathFor(project));
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = store.PrepareAndSaveDraftRecoveryAsync(project, scope, 1, async (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            entered.TrySetResult();
            // This is deliberately a genuinely pending original-owner operation. Once
            // cancellation is observed by the caller, releasing it returns a candidate;
            // the store's own post-await cancellation check must still protect the commit.
            await release.Task.ConfigureAwait(false);
            return FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2,
                Payload("prepared during async wait"), _key);
        }, cancellation.Token);

        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            Assert.False(pending.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project)));
        Assert.Same(originalReceipt, originalStore.GetOriginalMetadataTransition(first));
        Assert.True(originalStore.IsIssuedOriginalMetadataTransition(originalReceipt!, first));
        // Reopen the same canonical workspace directory (not a second recovery store).
        var reopened = new FileDeveloperWorkspaceStore(_root);
        var retained = await reopened.GetDraftRecoveryAsync(project, scope);
        Assert.True(retained.Succeeded, retained.Error?.Message);
        Assert.Equal(first, retained.Value);
        Assert.Equal("durable before async wait", FileDeveloperWorkspaceStore
            .UnprotectDraftRecovery(project, scope, retained.Value!, _key).Tabs[0].DraftText);

        // A post-cancellation retry on the same store proves the awaited owner released
        // both its process lock and cross-process lock; then stale CAS remains enforced.
        var winner = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2, Payload("retry after async cancellation"), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, winner, 1)).Succeeded);
        var stale = await store.SaveDraftRecoveryAsync(project,
            FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2, Payload("stale retry"), _key), 1);
        Assert.Equal(DeveloperOperationErrorCode.RevisionConflict, stale.Error!.Code);
        Assert.Equal(winner, (await reopened.GetDraftRecoveryAsync(project, scope)).Value);
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

    private static DeveloperDraftRecoveryScope ManualScope() => new(Guid.NewGuid(), Guid.Empty, Guid.Empty, Guid.Empty, IsManual: true);
    private static Task RevalidateRunMetadata(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }

    [Fact]
    public async Task First_reviewed_console_declaration_saves_same_store_without_creating_a_recovery_key_or_execution()
    {
        var (store, project) = await CreateAsync(); var scope = ManualScope(); var keyReads = 0;
        var reviewed = DeveloperRunConfigurationEdit.Prepare(project, "run", "printf newly-configured", 25).Value!;
        var saved = await store.SaveReviewedRunConfigurationAsync(project, reviewed, scope, 0,
            _ => { keyReads++; throw new InvalidOperationException("No recovery means no key request."); }, RevalidateRunMetadata, TestContext.Current.CancellationToken);
        Assert.True(saved.Succeeded, saved.Error?.Message); Assert.Equal(0, keyReads); Assert.False(saved.Value!.MigratedRecovery);
        Assert.Equal(project.Reference.WorkspaceRevision + 1, saved.Value.Project.Reference.WorkspaceRevision);
        Assert.Equal(project.Reference.ProjectRevision + 1, saved.Value.Project.Reference.ProjectRevision);
        Assert.Equal("run", Assert.Single(saved.Value.Project.Project.RunConfigurationIds));
        Assert.Null(Assert.Single(DeveloperRunConfigurationSelection.Observe(saved.Value.Project).Value!).UnavailableReason);
        Assert.Equal(project.Root, saved.Value.Project.Root); Assert.Equal(project.Repository, saved.Value.Project.Repository);
        Assert.Empty(saved.Value.Project.Workspace.OpenEditors);
        var metadata = await File.ReadAllBytesAsync(PathFor(project), TestContext.Current.CancellationToken);
        var stale = await store.SaveReviewedRunConfigurationAsync(project, reviewed, scope, 0,
            _ => throw new InvalidOperationException("Stale input must refuse before keys."), RevalidateRunMetadata, TestContext.Current.CancellationToken);
        Assert.False(stale.Succeeded); Assert.Equal(metadata, await File.ReadAllBytesAsync(PathFor(project), TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "workspaces"), "*.json"));
    }

    [Fact]
    public async Task Reviewed_target_migrates_exact_manual_ciphertext_payload_and_layout_under_new_authenticated_header_atomically()
    {
        var (store, project) = await CreateAsync(); var scope = ManualScope(); var payload = Payload();
        var original = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, payload, _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, original, 0, TestContext.Current.CancellationToken)).Succeeded);
        var tabs = new[] { new DeveloperCleanEditorTab("readme.md", new string('b', 64), true, false, "right") };
        Assert.True((await store.SaveEditorSessionAsync(project, scope.ProfileId, 0, tabs, tabs[0].RelativePath, TestContext.Current.CancellationToken)).Succeeded);
        var review = DeveloperRunConfigurationEdit.Prepare(project, "run", "printf migrated-target", 25).Value!;
        byte[]? suppliedKey = null;
        var saved = await store.SaveReviewedRunConfigurationAsync(project, review, scope, 1,
            _ => Task.FromResult(suppliedKey = _key.ToArray()), RevalidateRunMetadata, TestContext.Current.CancellationToken);
        Assert.True(saved.Succeeded, saved.Error?.Message); Assert.True(saved.Value!.MigratedRecovery);
        Assert.All(suppliedKey!, value => Assert.Equal(0, value));
        var current = saved.Value.Project; var loaded = await store.GetDraftRecoveryAsync(current, scope, TestContext.Current.CancellationToken);
        Assert.True(loaded.Succeeded); var migrated = Assert.IsType<DeveloperDraftRecoverySnapshot>(loaded.Value);
        Assert.Equal(2, migrated.Generation); Assert.NotEqual(original.ProjectStamp, migrated.ProjectStamp);
        Assert.Equal(current.Reference, migrated.ProjectReference); Assert.Equal(scope, migrated.Scope);
        Assert.NotEqual(original.ProtectedPayload, migrated.ProtectedPayload);
        var restored = FileDeveloperWorkspaceStore.UnprotectDraftRecovery(current, scope, migrated, _key);
        Assert.Equal(payload.Tabs.ToArray(), restored.Tabs.ToArray()); Assert.Equal(payload.SelectedRelativePath, restored.SelectedRelativePath);
        Assert.Throws<UnauthorizedAccessException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(current, scope, original, _key));
        var layout = await store.GetEditorSessionAsync(current, scope.ProfileId, TestContext.Current.CancellationToken);
        Assert.True(layout.Succeeded); Assert.Equal(2, layout.Value!.Generation); Assert.Equal(tabs, layout.Value.Tabs);
        Assert.Equal(current.Reference, layout.Value.ProjectReference); Assert.Equal(2, saved.Value.CleanSessionGeneration);
        var onDisk = await File.ReadAllTextAsync(PathFor(project), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(payload.Tabs[0].DraftText, onDisk); Assert.DoesNotContain(Convert.ToBase64String(_key), onDisk);
        // Authority-changing target edits never reuse presentation-only transition receipts.
        Assert.Null(((IDeveloperProjectOriginalMetadataTransitionStore)store).GetOriginalMetadataTransition(migrated));
    }

    [Fact]
    public async Task Editing_a_saved_target_uses_its_exact_previous_digest_and_migrates_recovery_again_without_changing_draft_payload()
    {
        var (store, project) = await CreateAsync(); var scope = ManualScope(); var token = TestContext.Current.CancellationToken;
        var original = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, original, 0, token)).Succeeded);
        var create = DeveloperRunConfigurationEdit.Prepare(project, "run", "printf first", 20).Value!;
        var first = (await store.SaveReviewedRunConfigurationAsync(project, create, scope, 1,
            _ => Task.FromResult(_key.ToArray()), RevalidateRunMetadata, token)).Value!;
        var edit = DeveloperRunConfigurationEdit.Prepare(first.Project, "run", "printf second", 30,
            DeveloperRunConfigurationSelection.Digest(first.Configuration)).Value!;
        var second = await store.SaveReviewedRunConfigurationAsync(first.Project, edit, scope, 2,
            _ => Task.FromResult(_key.ToArray()), RevalidateRunMetadata, token);
        Assert.True(second.Succeeded, second.Error?.Message); Assert.Equal(2, second.Value!.Configuration.Revision);
        Assert.Equal("printf second", second.Value.Configuration.Command); Assert.Equal(30, second.Value.Configuration.TimeoutSeconds);
        Assert.Equal(3, second.Value.RecoveryGeneration);
        var loaded = (await store.GetDraftRecoveryAsync(second.Value.Project, scope, token)).Value!;
        Assert.Equal(Payload().Tabs.ToArray(), FileDeveloperWorkspaceStore.UnprotectDraftRecovery(second.Value.Project, scope, loaded, _key).Tabs.ToArray());
        var before = await File.ReadAllBytesAsync(PathFor(project), token);
        Assert.False((await store.SaveReviewedRunConfigurationAsync(first.Project, edit, scope, 2,
            _ => throw new InvalidOperationException("A stale edit cannot request a key."), RevalidateRunMetadata, token)).Succeeded);
        Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project), token));
    }

    [Fact]
    public async Task Mutating_caller_owned_workspace_lists_during_key_IO_cannot_add_an_unreviewed_root_to_the_canonical_write()
    {
        var (store, originalProject) = await CreateAsync(); var scope = ManualScope(); var token = TestContext.Current.CancellationToken;
        var roots = originalProject.Workspace.Roots.ToList();
        var project = originalProject with { Workspace = originalProject.Workspace with { Roots = roots } };
        var original = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, original, 0, token)).Succeeded);
        var reviewed = DeveloperRunConfigurationEdit.Prepare(project, "run", "printf exact", 25).Value!;
        var saved = await store.SaveReviewedRunConfigurationAsync(project, reviewed, scope, 1, _ =>
        {
            roots[0] = roots[0] with { Location = Path.GetFullPath(Path.Combine(_root, "unreviewed-root")) };
            return Task.FromResult(_key.ToArray());
        }, RevalidateRunMetadata, token);
        Assert.True(saved.Succeeded, saved.Error?.Message);
        Assert.Equal(originalProject.Root, Assert.Single(saved.Value!.Project.Workspace.Roots));
        Assert.Equal(originalProject.Root, saved.Value.Project.Root);
        Assert.Equal(originalProject.Root, Assert.Single((await store.GetAsync(project.Reference.WorkspaceId, token)).Value!.Roots));
    }

    [Theory]
    [InlineData("foreign-profile")]
    [InlineData("task-partition")]
    [InlineData("wrong-key")]
    [InlineData("stale-generation")]
    [InlineData("missing-key")]
    [InlineData("corrupt-ciphertext")]
    [InlineData("tampered-header")]
    public async Task Unsafe_target_recovery_migration_preserves_the_exact_complete_old_document(string refusal)
    {
        var (store, project) = await CreateAsync(); var scope = ManualScope();
        var savedScope = refusal == "foreign-profile" ? ManualScope() : refusal == "task-partition" ? Scope() : scope;
        var original = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, savedScope, 1, Payload(), _key);
        if (refusal == "corrupt-ciphertext")
        {
            var encrypted = Convert.FromBase64String(original.ProtectedPayload); encrypted[^1] ^= 1;
            original = original with { ProtectedPayload = Convert.ToBase64String(encrypted) };
        }
        if (refusal == "tampered-header") original = original with { SavedAtUtc = original.SavedAtUtc.AddTicks(1) };
        Assert.True((await store.SaveDraftRecoveryAsync(project, original, 0, TestContext.Current.CancellationToken)).Succeeded);
        var before = await File.ReadAllBytesAsync(PathFor(project), TestContext.Current.CancellationToken); var reads = 0;
        var review = DeveloperRunConfigurationEdit.Prepare(project, "run", "printf should-not-save", 25).Value!;
        var observed = await store.SaveReviewedRunConfigurationAsync(project, review, scope, refusal == "stale-generation" ? 0 : 1,
            _ => { reads++; if (refusal == "missing-key") throw new InvalidDataException("The original key is missing; it was not recreated.");
                return Task.FromResult(refusal == "wrong-key" ? RandomNumberGenerator.GetBytes(32) : _key.ToArray()); }, RevalidateRunMetadata, TestContext.Current.CancellationToken);
        Assert.False(observed.Succeeded); Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project), TestContext.Current.CancellationToken));
        Assert.Equal(refusal is "wrong-key" or "missing-key" or "corrupt-ciphertext" or "tampered-header" ? 1 : 0, reads);
        var still = await store.GetDraftRecoveryAsync(project, savedScope, TestContext.Current.CancellationToken);
        Assert.True(still.Succeeded); Assert.Equal(original, still.Value);
        if (refusal is "corrupt-ciphertext" or "tampered-header")
            Assert.ThrowsAny<CryptographicException>(() => FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, savedScope, still.Value!, _key));
        else Assert.Equal(Payload().Tabs.ToArray(), FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, savedScope, still.Value!, _key).Tabs.ToArray());
    }

    [Fact]
    public async Task Raw_document_change_during_original_key_acquisition_is_not_overwritten_by_target_migration()
    {
        var (store, project) = await CreateAsync(); var scope = ManualScope();
        var original = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, original, 0, TestContext.Current.CancellationToken)).Succeeded);
        var review = DeveloperRunConfigurationEdit.Prepare(project, "run", "printf refused", 25).Value!;
        byte[] external = [];
        var observed = await store.SaveReviewedRunConfigurationAsync(project, review, scope, 1, async _ =>
        {
            var document = JsonNode.Parse(await File.ReadAllTextAsync(PathFor(project), TestContext.Current.CancellationToken))!;
            document["workspace"]!["revision"] = project.Workspace.Revision + 1;
            external = Encoding.UTF8.GetBytes(document.ToJsonString()); await File.WriteAllBytesAsync(PathFor(project), external, TestContext.Current.CancellationToken);
            return _key.ToArray();
        }, RevalidateRunMetadata, TestContext.Current.CancellationToken);
        Assert.False(observed.Succeeded); Assert.Equal(external, await File.ReadAllBytesAsync(PathFor(project), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Same_store_manual_start_exclusion_blocks_every_ordinary_metadata_writer_and_releases_after_its_real_pin()
    {
        var (store, project) = await CreateAsync(); var scope = ManualScope(); var token = TestContext.Current.CancellationToken;
        var original = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        Assert.True((await store.SaveDraftRecoveryAsync(project, original, 0, token)).Succeeded);
        var before = await File.ReadAllBytesAsync(PathFor(project), token);
        var pin = store.AcquireOriginalRunMetadataReadPin(project.Reference.WorkspaceId);
        try
        {
            store.DemandOriginalRunMetadataReadPin(project.Reference.WorkspaceId, pin);
            Assert.False((await store.SaveAsync(project.Workspace, project.Workspace.Revision, token)).Succeeded);
            Assert.False((await store.SaveEditorSessionAsync(project, scope.ProfileId, 0, [], null, token)).Succeeded);
            var later = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 2, Payload("later draft"), _key);
            Assert.False((await store.SaveDraftRecoveryAsync(project, later, 1, token)).Succeeded);
            var reviewed = DeveloperRunConfigurationEdit.Prepare(project, "run", "printf not-yet", 25).Value!;
            Assert.False((await store.SaveReviewedRunConfigurationAsync(project, reviewed, scope, 1,
                _ => throw new InvalidOperationException("No key request can pass an admitted Start pin."), RevalidateRunMetadata, token)).Succeeded);
            Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project), token));
        }
        finally { pin.Dispose(); }
        Assert.Throws<UnauthorizedAccessException>(() => store.DemandOriginalRunMetadataReadPin(project.Reference.WorkspaceId, pin));
        Assert.True((await store.SaveEditorSessionAsync(project, scope.ProfileId, 0, [], null, token)).Succeeded);
        Assert.Equal(Payload().Tabs.ToArray(), FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope,
            (await store.GetDraftRecoveryAsync(project, scope, token)).Value!, _key).Tabs.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admitted_metadata_writer_excludes_new_start_pins_and_settled_or_cancelled_writer_releases_exclusion(bool cancel)
    {
        var (store, project) = await CreateAsync(); var scope = ManualScope(); var before = await File.ReadAllBytesAsync(PathFor(project), TestContext.Current.CancellationToken);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = store.PrepareAndSaveDraftRecoveryAsync(project, scope, 0, async (_, ct) =>
        {
            entered.TrySetResult(); await release.Task.WaitAsync(ct);
            return FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, 1, Payload(), _key);
        }, cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Throws<InvalidOperationException>(() => store.AcquireOriginalRunMetadataReadPin(project.Reference.WorkspaceId));
            if (cancel) cancellation.Cancel(); else release.TrySetResult();
            if (cancel)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer);
                Assert.Equal(before, await File.ReadAllBytesAsync(PathFor(project), TestContext.Current.CancellationToken));
            }
            else Assert.True((await writer).Succeeded);
        }
        finally
        {
            release.TrySetResult();
            try { await writer; } catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }
        using var after = store.AcquireOriginalRunMetadataReadPin(project.Reference.WorkspaceId);
        store.DemandOriginalRunMetadataReadPin(project.Reference.WorkspaceId, after);
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
    private async Task<(FileDeveloperWorkspaceStore Store, DeveloperResolvedProject Project)> CreateAsync(string? sourceRoot = null)
    {
        var store = new FileDeveloperWorkspaceStore(_root);
        var root = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath(sourceRoot ?? Path.GetTempPath()));
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
