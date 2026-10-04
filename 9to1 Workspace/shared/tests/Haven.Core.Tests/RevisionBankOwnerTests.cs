using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class RevisionBankOwnerTests
{
    [Fact]
    public Task Membership_reopens_with_same_context_and_remove_preserves_source_reference() => InProfile(async profile =>
    {
        var request = new RevisionBankMutation(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(),
            RevisionBankMutationKind.Add, profile.Reference.ContextId,
            CategoryIds: [RevisionBankCategories.Notes]);
        var enrolled = await profile.Registry.MutateRevisionBankAsync(request, CancellationToken.None);
        var reopened = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None);
        Assert.NotNull(reopened);
        var member = Assert.Single(reopened!.Data.Members);
        Assert.Equal(profile.Reference.ContextId, member.ResourceId);
        Assert.Equal(new[] { RevisionBankCategories.Notes }, member.CategoryIds);
        Assert.Equal(profile.Reference, Assert.Single((await profile.Reopen().ReadExistingAsync(profile.Space.Id, CancellationToken.None))!.ContextReferences!));

        await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, enrolled.SpaceRevision, Guid.NewGuid(),
            RevisionBankMutationKind.Remove, profile.Reference.ContextId), CancellationToken.None);
        var removed = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None);
        Assert.Empty(removed!.Data.Members);
        Assert.Equal(profile.Reference, Assert.Single((await profile.Reopen().ReadExistingAsync(profile.Space.Id, CancellationToken.None))!.ContextReferences!));
        Assert.Equal("source sentinel", await File.ReadAllTextAsync(profile.SourceSentinel, CancellationToken.None));
    });

    [Fact]
    public Task Replay_survives_reopen_and_changed_request_cannot_reuse_operation() => InProfile(async profile =>
    {
        var request = new RevisionBankMutation(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(),
            RevisionBankMutationKind.Add, profile.Reference.ContextId);
        var first = await profile.Registry.MutateRevisionBankAsync(request, CancellationToken.None);
        var version = (await profile.Store.ExportAsync(CancellationToken.None)).Version;
        var second = await profile.Reopen().MutateRevisionBankAsync(request, CancellationToken.None);
        Assert.Equal(first, second);
        Assert.Equal(version, (await profile.Store.ExportAsync(CancellationToken.None)).Version);
        Assert.Single((await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None))!.Data.Operations);
        var before = await profile.ReadCurrentBytes();
        await Assert.ThrowsAsync<RevisionBankOperationConflictException>(() =>
            profile.Reopen().MutateRevisionBankAsync(request with { CategoryIds = new[] { RevisionBankCategories.Notes } }, CancellationToken.None));
        Assert.Equal(before, await profile.ReadCurrentBytes());
    });

    [Fact]
    public Task Independent_owners_enforce_one_expected_space_revision() => InProfile(async profile =>
    {
        var firstOwner = profile.Reopen();
        var secondOwner = profile.Reopen();
        var first = firstOwner.MutateRevisionBankAsync(new(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(),
            RevisionBankMutationKind.CreateCategory, CategoryId: Guid.NewGuid(), CategoryName: "First"), CancellationToken.None);
        var second = secondOwner.MutateRevisionBankAsync(new(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(),
            RevisionBankMutationKind.CreateCategory, CategoryId: Guid.NewGuid(), CategoryName: "Second"), CancellationToken.None);
        var failures = new List<Exception>();
        foreach (var task in new[] { first, second })
        {
            try { await task; }
            catch (Exception error) { failures.Add(error); }
        }
        Assert.Equal(1, new[] { first, second }.Count(task => task.IsCompletedSuccessfully));
        var conflict = Assert.IsType<SpaceRevisionConflictException>(Assert.Single(failures));
        Assert.Equal(profile.Space.Id, conflict.SpaceId);
        Assert.Equal(profile.Space.Revision, conflict.ExpectedRevision);
        Assert.Equal(profile.Space.Revision + 1, conflict.ActualRevision);
        var stored = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None);
        Assert.Equal(7, stored!.Data.Categories.Count);
        Assert.Single(stored.Data.Operations);
    });

    [Fact]
    public Task Publication_admission_refusal_preserves_current_whole_settings_and_bank() => InProfile(async profile =>
    {
        var before = await profile.ReadCurrentBytes();
        profile.Admission.RejectPublication = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => profile.Registry.MutateRevisionBankAsync(
            new(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(), RevisionBankMutationKind.Add,
                profile.Reference.ContextId), CancellationToken.None));
        Assert.Contains(SettingsCommitPhase.Admission, profile.Admission.Phases);
        Assert.Contains(SettingsCommitPhase.Publication, profile.Admission.Phases);
        Assert.Equal(before, await profile.ReadCurrentBytes());
        var fresh = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None);
        Assert.Empty(fresh!.Data.Members);
        Assert.Empty(fresh.Data.Operations);
        Assert.Equal(profile.Space.Revision, fresh.SpaceRevision);
    });

    [Fact]
    public Task Foreign_or_removed_context_refuses_without_a_membership_or_receipt() => InProfile(async profile =>
    {
        var other = await profile.Registry.CreateAsync("Other scope", cancellationToken: CancellationToken.None);
        var foreignReference = profile.Reference with { ContextId = Guid.NewGuid() };
        await profile.Registry.UpdateAsync(other with { ContextReferences = new[] { foreignReference } },
            other.Revision, CancellationToken.None);
        var before = await profile.ReadCurrentBytes();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profile.Registry.MutateRevisionBankAsync(
            new(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(), RevisionBankMutationKind.Add,
                foreignReference.ContextId), CancellationToken.None));
        Assert.Equal(before, await profile.ReadCurrentBytes());
        var without = await profile.Registry.UpdateAsync(profile.Space with { ContextReferences = Array.Empty<SpaceContextReference>() },
            profile.Space.Revision, CancellationToken.None);
        before = await profile.ReadCurrentBytes();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => profile.Registry.MutateRevisionBankAsync(
            new(profile.Space.Id, without.Revision, Guid.NewGuid(), RevisionBankMutationKind.Add,
                profile.Reference.ContextId), CancellationToken.None));
        Assert.Equal(before, await profile.ReadCurrentBytes());
        Assert.Empty((await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None))!.Data.Operations);
    });

    [Fact]
    public Task Categories_and_opt_in_classification_persist_without_duplicate_membership() => InProfile(async profile =>
    {
        var enrolled = await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, profile.Space.Revision,
            Guid.NewGuid(), RevisionBankMutationKind.Add, profile.Reference.ContextId), CancellationToken.None);
        Assert.Empty(Assert.Single((await profile.Registry.ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None))!.Data.Members).CategoryIds);
        var enabled = await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, enrolled.SpaceRevision,
            Guid.NewGuid(), RevisionBankMutationKind.SetAutomaticClassification, AutomaticallyClassifyTypes: true), CancellationToken.None);
        var repeated = await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, enabled.SpaceRevision,
            Guid.NewGuid(), RevisionBankMutationKind.SetAutomaticClassification, AutomaticallyClassifyTypes: true), CancellationToken.None);
        Assert.False(repeated.Changed);
        var categoryId = Guid.NewGuid();
        var created = await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, repeated.SpaceRevision,
            Guid.NewGuid(), RevisionBankMutationKind.CreateCategory, CategoryId: categoryId, CategoryName: "Mechanics"), CancellationToken.None);
        var classified = await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, created.SpaceRevision,
            Guid.NewGuid(), RevisionBankMutationKind.Classify, profile.Reference.ContextId, CategoryIds: [categoryId]), CancellationToken.None);
        var renamed = await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, classified.SpaceRevision,
            Guid.NewGuid(), RevisionBankMutationKind.RenameCategory, CategoryId: categoryId, CategoryName: "Exam"), CancellationToken.None);
        var snapshot = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None);
        Assert.Single(snapshot!.Data.Members);
        Assert.Equal(categoryId, Assert.Single(Assert.Single(snapshot.Data.Members).CategoryIds));
        Assert.Equal("Exam", snapshot.Data.Categories.Single(item => item.CategoryId == categoryId).Name);
        await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, renamed.SpaceRevision,
            Guid.NewGuid(), RevisionBankMutationKind.RemoveCategory, CategoryId: categoryId), CancellationToken.None);
        snapshot = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None);
        Assert.Empty(Assert.Single(snapshot!.Data.Members).CategoryIds);
        Assert.DoesNotContain(snapshot.Data.Categories, item => item.CategoryId == categoryId);
        Assert.Equal(new[] { RevisionBankCategories.Recommended, RevisionBankCategories.All, RevisionBankCategories.Flashcards,
            RevisionBankCategories.Notes, RevisionBankCategories.Papers, RevisionBankCategories.Quizzes }, snapshot.Data.Categories.Select(item => item.CategoryId));
    });

    [Fact]
    public Task Legacy_update_and_fork_preserve_membership_and_detach_replay_identity() => InProfile(async profile =>
    {
        var result = await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, profile.Space.Revision,
            Guid.NewGuid(), RevisionBankMutationKind.Add, profile.Reference.ContextId), CancellationToken.None);
        var current = (await profile.Registry.ReadExistingAsync(profile.Space.Id, CancellationToken.None))!;
        var updated = await profile.Registry.UpdateAsync(current with { Description = "legacy edit", RevisionBank = null },
            result.SpaceRevision, CancellationToken.None);
        Assert.Single(updated.RevisionBank!.Members);
        Assert.Single(updated.RevisionBank.Operations);
        var renamed = await profile.Registry.RenameAsync(updated.Id, "Renamed Bank", CancellationToken.None);
        Assert.Equal(profile.Reference.ContextId, Assert.Single(renamed.RevisionBank!.Members).ResourceId);
        var fork = await profile.Registry.ForkAsync(updated.Id, "Forked Bank", CancellationToken.None);
        Assert.NotEqual(updated.Id, fork.Id);
        Assert.Equal(profile.Reference.ContextId, Assert.Single(fork.RevisionBank!.Members).ResourceId);
        Assert.Empty(fork.RevisionBank.Operations);
        Assert.NotSame(renamed.RevisionBank.Members, fork.RevisionBank.Members);
        Assert.Equal(renamed.RevisionBank.Categories, fork.RevisionBank.Categories);
    });

    [Fact]
    public Task Future_registry_or_bank_schema_is_refused_without_current_file_replacement() => InProfile(async profile =>
    {
        await profile.Registry.MutateRevisionBankAsync(new(profile.Space.Id, profile.Space.Revision,
            Guid.NewGuid(), RevisionBankMutationKind.Add, profile.Reference.ContextId), CancellationToken.None);
        var export = await profile.Store.ExportAsync(CancellationToken.None);
        using (var stored = JsonDocument.Parse(export.Settings["spaces.registry"]))
            Assert.Equal(4, stored.RootElement.GetProperty("Version").GetInt32());
        var current = (await profile.Registry.ReadExistingAsync(profile.Space.Id, CancellationToken.None))!;
        var bankUnsupported = current with { RevisionBank = current.RevisionBank! with { SchemaVersion = 2 } };
        await profile.Store.SetAsync("spaces.registry", new PersistedRegistry(4, new[] { bankUnsupported }), CancellationToken.None);
        var before = await profile.ReadCurrentBytes();
        await Assert.ThrowsAsync<NotSupportedException>(() => profile.Reopen().GetAllAsync(cancellationToken: CancellationToken.None));
        Assert.Equal(before, await profile.ReadCurrentBytes());
        await profile.Store.SetAsync("spaces.registry", new PersistedRegistry(5, new[] { current }), CancellationToken.None);
        before = await profile.ReadCurrentBytes();
        await Assert.ThrowsAsync<InvalidDataException>(() => profile.Reopen().GetAllAsync(cancellationToken: CancellationToken.None));
        Assert.Equal(before, await profile.ReadCurrentBytes());
    });

    [Fact]
    public Task An_unguarded_registry_cannot_activate_revision_bank_mutations() => InProfile(async profile =>
    {
        var before = await profile.ReadCurrentBytes();
        var unguarded = new SpaceRegistry(profile.Store);
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            _ = unguarded.MutateRevisionBankAsync(
                new(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(), RevisionBankMutationKind.Add,
                    profile.Reference.ContextId), CancellationToken.None);
        });
        Assert.Equal(before, await profile.ReadCurrentBytes());
    });

    [Fact]
    public Task Schema_three_migrates_without_rewriting_existing_ids_references_or_revisions() => InProfile(async profile =>
    {
        var original = await profile.Registry.GetAllAsync(includeArchived: true, cancellationToken: CancellationToken.None);
        await profile.Store.SetAsync("spaces.registry", new PersistedRegistry(3, original), CancellationToken.None);
        var migrated = await profile.Reopen().GetAllAsync(includeArchived: true, cancellationToken: CancellationToken.None);
        Assert.Equal(original.Select(item => item.Id), migrated.Select(item => item.Id));
        foreach (var previous in original)
        {
            var current = migrated.Single(item => item.Id == previous.Id);
            Assert.Equal(previous.Revision, current.Revision);
            Assert.Equal(previous.CreatedAt, current.CreatedAt);
            Assert.Equal(previous.ContextReferences, current.ContextReferences);
            Assert.Null(current.RevisionBank);
        }
        using var stored = JsonDocument.Parse((await profile.Store.ExportAsync(CancellationToken.None)).Settings["spaces.registry"]);
        Assert.Equal(4, stored.RootElement.GetProperty("Version").GetInt32());
    });

    [Fact]
    public Task Saved_foreign_or_future_receipt_refuses_reopen_without_rewriting_current_owner() => InProfile(async profile =>
    {
        var request = new RevisionBankMutation(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(),
            RevisionBankMutationKind.Add, profile.Reference.ContextId);
        var committed = await profile.Registry.MutateRevisionBankAsync(request, CancellationToken.None);
        var current = (await profile.Registry.ReadExistingAsync(profile.Space.Id, CancellationToken.None))!;
        var originalReceipt = Assert.Single(current.RevisionBank!.Operations);
        Assert.Equal(committed, originalReceipt.Result);
        var corruptResults = new[]
        {
            committed with { SpaceId = Guid.NewGuid() },
            committed with { SpaceRevision = checked(current.Revision + 1) }
        };
        foreach (var corruptResult in corruptResults)
        {
            var corruptReceipt = originalReceipt with { Result = corruptResult };
            Assert.Equal(originalReceipt.RequestSha256, corruptReceipt.RequestSha256);
            var corruptSpace = current with
            {
                RevisionBank = current.RevisionBank! with { Operations = new[] { corruptReceipt } }
            };
            await profile.Store.SetAsync("spaces.registry", new PersistedRegistry(4, new[] { corruptSpace }), CancellationToken.None);
            var before = await profile.ReadCurrentBytes();
            var storeVersion = (await profile.Store.ExportAsync(CancellationToken.None)).Version;
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None));
            Assert.Equal(before, await profile.ReadCurrentBytes());
            Assert.Equal(storeVersion, (await profile.Store.ExportAsync(CancellationToken.None)).Version);
        }
    });

    [Fact]
    public Task Replay_binds_saved_historical_revision_resource_and_category_to_exact_request() => InProfile(async profile =>
    {
        var request = new RevisionBankMutation(profile.Space.Id, profile.Space.Revision, Guid.NewGuid(),
            RevisionBankMutationKind.Add, profile.Reference.ContextId);
        var committed = await profile.Registry.MutateRevisionBankAsync(request, CancellationToken.None);
        var current = (await profile.Registry.ReadExistingAsync(profile.Space.Id, CancellationToken.None))!;
        var originalReceipt = Assert.Single(current.RevisionBank!.Operations);
        Assert.Equal(committed, originalReceipt.Result);
        var corruptResults = new[]
        {
            committed with { SpaceRevision = request.ExpectedRevision },
            committed with { ResourceId = Guid.NewGuid() },
            committed with { CategoryId = RevisionBankCategories.Notes }
        };
        foreach (var corruptResult in corruptResults)
        {
            var corruptReceipt = originalReceipt with { Result = corruptResult };
            Assert.Equal(originalReceipt.RequestSha256, corruptReceipt.RequestSha256);
            var corruptSpace = current with
            {
                RevisionBank = current.RevisionBank! with { Operations = new[] { corruptReceipt } }
            };
            await profile.Store.SetAsync("spaces.registry", new PersistedRegistry(4, new[] { corruptSpace }), CancellationToken.None);
            var before = await profile.ReadCurrentBytes();
            var storeVersion = (await profile.Store.ExportAsync(CancellationToken.None)).Version;
            var readable = await profile.Reopen().ReadRevisionBankAsync(profile.Space.Id, CancellationToken.None);
            Assert.Equal(corruptResult, Assert.Single(readable!.Data.Operations).Result);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                profile.Reopen().MutateRevisionBankAsync(request, CancellationToken.None));
            Assert.Equal(before, await profile.ReadCurrentBytes());
            Assert.Equal(storeVersion, (await profile.Store.ExportAsync(CancellationToken.None)).Version);
        }
    });

    private sealed record PersistedRegistry(int Version, IReadOnlyList<SpaceDefinition> Spaces);

    private static async Task InProfile(Func<Profile, Task> body)
    {
        var profile = new Profile();
        var failures = new List<Exception>();
        try { await profile.Initialize(); await body(profile); }
        catch (Exception error) { failures.Add(error); }
        try { Directory.Delete(profile.DataDirectory, recursive: true); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    private sealed class Profile : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-revision-bank-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string SourceSentinel => Path.Combine(DataDirectory, "source-sentinel.txt");
        public VersionedAtomicSettingsStore Store { get; private set; } = null!;
        public SpaceRegistry Registry { get; private set; } = null!;
        public SpaceDefinition Space { get; private set; } = null!;
        public SpaceContextReference Reference { get; private set; } = null!;
        public ControlledAdmission Admission { get; private set; } = null!;

        public async Task Initialize()
        {
            Directory.CreateDirectory(DataDirectory);
            await File.WriteAllTextAsync(SourceSentinel, "source sentinel", CancellationToken.None);
            Store = new(this);
            var identity = await Store.GetStoreIdentityAsync(CancellationToken.None);
            Admission = new(identity.StoreId);
            Registry = new(Store, _ => ValueTask.FromResult<ISettingsCommitAdmission>(Admission));
            Space = await Registry.CreateAsync("Bank", cancellationToken: CancellationToken.None);
            Reference = new(Guid.NewGuid(), SpaceContextReferenceKind.WriteArtifact, "write", Guid.NewGuid().ToString("D"),
                "v1", SpaceContextPermission.Read, SpaceContextIndexState.NotRequired, false, DateTimeOffset.UtcNow);
            Space = await Registry.UpdateAsync(Space with { ContextReferences = new[] { Reference } }, Space.Revision, CancellationToken.None);
            Admission.Phases.Clear();
        }
        public SpaceRegistry Reopen() => new(new VersionedAtomicSettingsStore(this),
            _ => ValueTask.FromResult<ISettingsCommitAdmission>(Admission));
        public Task<byte[]> ReadCurrentBytes() => File.ReadAllBytesAsync(Path.Combine(DataDirectory, "settings.json"), CancellationToken.None);
    }

    // A trusted in-process fixture admission over the actual settings identity, not a fabricated Home receipt.
    private sealed class ControlledAdmission(Guid storeId) : ISettingsCommitAdmission
    {
        public bool RejectPublication { get; set; }
        public List<SettingsCommitPhase> Phases { get; } = [];
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (Phases) Phases.Add(context.Phase);
            return ValueTask.FromResult(context.StoreIdentity.SchemaVersion == 1 &&
                context.StoreIdentity.StoreId == storeId && !(RejectPublication && context.Phase == SettingsCommitPhase.Publication));
        }
    }
}
