using System.Text.Json;
using Haven.Application;
using Haven.Core.Forms;

namespace Haven.Infrastructure.Tests;

public sealed class FormPublicationTests
{
    [Fact]
    public async Task Draft_changes_are_isolated_from_published_versions_across_actual_store_restart()
    {
        using var paths = new Paths();
        var store = new VersionedAtomicSettingsStore(paths);
        var authority = new Authority();
        var service = new FormPublicationService(store, store, authority, new FixtureValidator());
        var id = Guid.NewGuid();
        var field = Guid.NewGuid();
        Assert.True((await service.CreateAsync(id, Project(id, field, "Original"))).Success);
        var initial = (await service.PublishAsync(id, 1)).Publication!;
        var version = Assert.Single(initial.Versions);
        Assert.True((await service.SaveDraftAsync(id, 2, Project(id, field, "Renamed"))).Success);
        store = new VersionedAtomicSettingsStore(paths);
        service = new(store, store, authority, new FixtureValidator());
        var restored = (await service.ReadAsync(id)).Publication!;
        Assert.Equal("Renamed", Label(restored.Draft));
        Assert.Equal("Original", Label(Assert.Single(restored.Versions).Project));
        Assert.Equal(version.FormVersionID, restored.ActiveVersionID);
        var next = (await service.PublishAsync(id, 3)).Publication!;
        Assert.Equal(2, next.Versions.Count);
        Assert.NotEqual(version.FormVersionID, next.ActiveVersionID);
        var closed = (await service.CloseAsync(id, 4)).Publication!;
        Assert.Equal(FormPublicationState.Closed, closed.State);
        Assert.Equal(2, closed.Versions.Count);
        Assert.Equal(next.ActiveVersionID, closed.ActiveVersionID);
    }

    [Fact]
    public async Task Revoke_between_preparation_and_commit_and_stale_revision_preserve_active_version()
    {
        using var paths = new Paths();
        var store = new VersionedAtomicSettingsStore(paths);
        var authority = new Authority();
        var service = new FormPublicationService(store, store, authority, new FixtureValidator());
        var id = Guid.NewGuid();
        Assert.True((await service.CreateAsync(id, Project(id, Guid.NewGuid(), "Question"))).Success);
        var initial = (await service.PublishAsync(id, 1)).Publication!;
        Assert.Equal("RevisionConflict", (await service.PublishAsync(id, 1)).Code);
        authority.RemainingAllowed = 1;
        Assert.Equal("PermissionDenied", (await service.PublishAsync(id, 2)).Code);
        Assert.False((await service.ReadAsync(id)).Success);
        authority.RemainingAllowed = int.MaxValue;
        var unchanged = (await service.ReadAsync(id)).Publication!;
        Assert.Equal(initial.ActiveVersionID, unchanged.ActiveVersionID);
        Assert.Single(unchanged.Versions);
        Assert.Equal(initial.Revision, unchanged.Revision);
    }

    [Fact]
    public async Task Failed_publication_write_leaves_previous_active_version_recoverable_on_disk()
    {
        using var paths = new Paths();
        var root = new VersionedAtomicSettingsStore(paths);
        var fault = new FaultStore(root);
        var authority = new Authority();
        var service = new FormPublicationService(fault, root, authority, new FixtureValidator());
        var id = Guid.NewGuid();
        Assert.True((await service.CreateAsync(id, Project(id, Guid.NewGuid(), "Question"))).Success);
        var initial = (await service.PublishAsync(id, 1)).Publication!;
        fault.FailWrites = true;
        Assert.Equal("StorageUnavailable", (await service.PublishAsync(id, 2)).Code);
        var restoredRoot = new VersionedAtomicSettingsStore(paths);
        var restored = (await new FormPublicationService(restoredRoot, restoredRoot, authority, new FixtureValidator()).ReadAsync(id)).Publication!;
        Assert.Equal(initial.ActiveVersionID, restored.ActiveVersionID);
        Assert.Equal(initial.Revision, restored.Revision);
        Assert.Single(restored.Versions);
    }

    [Fact]
    public async Task Independent_actual_store_instances_cannot_publish_the_same_expected_revision_twice()
    {
        using var paths = new Paths();
        var firstRoot = new VersionedAtomicSettingsStore(paths);
        var secondRoot = new VersionedAtomicSettingsStore(paths);
        var initialService = new FormPublicationService(firstRoot, firstRoot, new Authority(), new FixtureValidator());
        var id = Guid.NewGuid();
        Assert.True((await initialService.CreateAsync(id, Project(id, Guid.NewGuid(), "Question"))).Success);
        var barrier = new PublicationBarrier();
        var first = new FormPublicationService(firstRoot, firstRoot, barrier, new FixtureValidator());
        var second = new FormPublicationService(secondRoot, secondRoot, barrier, new FixtureValidator());
        var attempts = await Task.WhenAll(first.PublishAsync(id, 1), second.PublishAsync(id, 1));
        Assert.Single(attempts, item => item.Success);
        Assert.Single(attempts, item => item.Code == "RevisionConflict");
        var reopened = new VersionedAtomicSettingsStore(paths);
        var result = (await new FormPublicationService(reopened, reopened, new Authority(), new FixtureValidator()).ReadAsync(id)).Publication!;
        Assert.Equal(2, result.Revision);
        Assert.Single(result.Versions);
        Assert.Equal(attempts.Single(item => item.Success).Publication!.ActiveVersionID, result.ActiveVersionID);
    }

    private sealed class PublicationBarrier : IFormStoreCommitAuthority
    {
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ISettingsCommitAdmission?>(new FixtureAdmission(() => true));

        private int _calls;
        private readonly TaskCompletionSource _bothPrepared = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken token)
        {
            if (Interlocked.Increment(ref _calls) == 2) _bothPrepared.TrySetResult();
            await _bothPrepared.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
            return true;
        }
    }

    private sealed class FaultStore(IVersionedSettingsStore inner) : IVersionedSettingsStore, IVersionedSettingsGuardedCompareExchange
    {
        public bool FailWrites { get; set; }
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson, string? replacementJson,
            IReadOnlyDictionary<string, string?> guards, CancellationToken token) =>
            ((IVersionedSettingsGuardedCompareExchange)inner).CompareExchangeGuardedAsync(key, expectedJson, replacementJson, guards, token);
        public Task<SettingsGuardedCompareExchangeResult> CompareExchangeGuardedAsync(string key, string? expectedJson, string? replacementJson,
            IReadOnlyDictionary<string, string?> guards, ISettingsCommitAdmission admission, CancellationToken token) =>
            FailWrites ? Task.FromException<SettingsGuardedCompareExchangeResult>(new IOException("Injected persistence failure"))
                : ((IVersionedSettingsGuardedCompareExchange)inner).CompareExchangeGuardedAsync(key, expectedJson, replacementJson, guards, admission, token);

        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => inner.GetAsync<T>(key, token);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class =>
            FailWrites ? Task.FromException(new IOException("Injected persistence failure")) : inner.SetAsync(key, value, token);
        public Task<SettingsCompareExchangeResult> CompareExchangeAsync(string key, string? expectedJson, string? replacementJson, CancellationToken token) =>
            FailWrites ? Task.FromException<SettingsCompareExchangeResult>(new IOException("Injected persistence failure"))
                : ((IVersionedSettingsCompareExchange)inner).CompareExchangeAsync(key, expectedJson, replacementJson, token);
        public Task RemoveAsync(string key, CancellationToken token) => inner.RemoveAsync(key, token);
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => inner.ExportAsync(token);
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken token) => inner.ImportAsync(manifest, token);
    }

    private static string? Label(JsonElement value) => value.GetProperty("Fields")[0].GetProperty("Label").GetString();
    private static JsonElement Project(Guid id, Guid field, string label) => JsonSerializer.SerializeToElement(new
    { FormID = id, Fields = new[] { new { FieldID = field, Label = label } }, Theme = new { Accent = "blue" } });
    private sealed class Authority : IFormStoreCommitAuthority
    {
        public ValueTask<ISettingsCommitAdmission?> CaptureCommitAdmissionAsync(Guid storeID, Guid formID, long revision,
            string actionID, AuthenticatedResourceActor? expectedActor, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ISettingsCommitAdmission?>(new FixtureAdmission(() => RemainingAllowed > 0));

        public int RemainingAllowed { get; set; } = int.MaxValue;
        public ValueTask<bool> AuthorizeAsync(Guid storeID, Guid formID, long revision, string actionID, CancellationToken cancellationToken)
            => ValueTask.FromResult(storeID != Guid.Empty && formID != Guid.Empty && RemainingAllowed-- > 0);
    }
    private sealed class FixtureValidator : IFormProjectPublicationValidator
    {
        public void Validate(Guid id, JsonElement project)
        {
            FormPublicationValidation.ValidateProject(id, project);
            Assert.NotEqual(Guid.Empty, project.GetProperty("Fields")[0].GetProperty("FieldID").GetGuid());
        }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("forms-publication-").FullName;
        public string DataDirectory => _root;
        public string DatabasePath => Path.Combine(_root, "data.db");
        public string BrowserProfileDirectory => Path.Combine(_root, "browser");
        public string AttachmentsDirectory => Path.Combine(_root, "attachments");
        public string LogsDirectory => Path.Combine(_root, "logs");
        public string LegacyStatePath => Path.Combine(_root, "legacy.json");
        public void Dispose() => Directory.Delete(_root, true);
    }
    private sealed class FixtureAdmission(Func<bool> allowed) : ISettingsCommitAdmission
    {
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken cancellationToken) => ValueTask.FromResult(allowed());
    }

}
