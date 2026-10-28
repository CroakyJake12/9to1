using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed class ProfileBoundNotesRepositoryTests
{
    [Fact]
    public async Task Actual_profile_change_refuses_before_any_original_library_read()
    {
        using var rig = new Rig();
        var actor = await rig.Profiles.GetCurrentAsync(default);
        var original = new Repository();
        var repository = rig.Bind(original, actor!);
        Assert.Same(original, repository.OriginalRepository);
        rig.Principal.Value = "different-principal";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.ListAsync(default));
        Assert.Equal(0, original.Calls);
    }

    [Fact]
    public async Task Changed_actual_profile_withholds_an_already_completed_document_read()
    {
        using var rig = new Rig();
        var actor = await rig.Profiles.GetCurrentAsync(default);
        var original = new Repository { AfterOperation = () => rig.Principal.Value = "different-principal" };
        var repository = rig.Bind(original, actor!);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.LoadAsync(original.Document.Id, default));
        Assert.Equal(1, original.Calls);
    }

    [Fact]
    public async Task Admitted_save_returns_the_same_real_receipt_even_if_profile_changes_at_commit()
    {
        using var rig = new Rig();
        var actor = await rig.Profiles.GetCurrentAsync(default);
        var original = new Repository { AfterOperation = () => rig.Principal.Value = "different-principal" };
        var repository = rig.Bind(original, actor!);
        var receipt = await repository.SaveAsync(original.Document, "Original save", default);
        Assert.Same(original.Receipt, receipt);
        Assert.Equal(1, original.Calls);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.SaveAsync(original.Document, "Retry", default));
        Assert.Equal(1, original.Calls);
    }

    [Fact]
    public async Task Full_interface_preserves_the_original_objects_and_features()
    {
        using var rig = new Rig();
        var actor = await rig.Profiles.GetCurrentAsync(default);
        var original = new Repository();
        var repository = rig.Bind(original, actor!);
        Assert.Same(original.Summaries, await repository.ListAsync(default));
        Assert.Same(original.Document, await repository.LoadAsync(original.Document.Id, default));
        Assert.Same(original.Versions, await repository.GetVersionsAsync(original.Document.Id, default));
        Assert.Same(original.Document, await repository.LoadVersionAsync(original.Document.Id, "1", default));
        Assert.Same(original.Document, await repository.RecoverLatestAsync(original.Document.Id, default));
        Assert.Same(original.Hits, await repository.SearchAsync("term", default));
        Assert.Same(original.Receipt, await repository.SaveAsync(original.Document, "save", default));
        await repository.DeleteAsync(original.Document.Id, default);
        Assert.Equal(8, original.Calls);
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "write-profile-" + Guid.NewGuid().ToString("N"));
        internal readonly Principal Principal = new();
        internal HomeLocalProfileIdentity Profiles { get; }
        internal Rig()
        {
            Directory.CreateDirectory(_directory);
            Profiles = new(new FileHomeCoreStateStore(Path.Combine(_directory, "home.json")), Principal);
        }
        internal ProfileBoundNotesRepository Bind(INotesRepository original, AuthenticatedResourceActor actor) =>
            new(original, Profiles, actor, body => body(), _ => { }, () => true);
        public void Dispose() => Directory.Delete(_directory, true);
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        internal string Value = "original-test-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => ValueTask.FromResult<string?>(Value);
    }
    private sealed class Repository : INotesRepository
    {
        internal readonly NotesDocument Document = NotesDocument.Create("Actual existing document");
        internal readonly IReadOnlyList<NotesDocumentSummary> Summaries = [];
        internal readonly IReadOnlyList<NotesVersionInfo> Versions = [];
        internal readonly IReadOnlyList<NotesSearchHit> Hits = [];
        internal readonly NotesSaveResult Receipt = new(Guid.NewGuid(), 1, DateTimeOffset.UtcNow, "actual-hash", "current", "version");
        internal Action? AfterOperation;
        internal int Calls;
        private T Complete<T>(T result) { Calls++; AfterOperation?.Invoke(); return result; }
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) => Task.FromResult(Complete(Summaries));
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => Task.FromResult<NotesDocument?>(Complete(Document));
        public Task<NotesSaveResult> SaveAsync(NotesDocument doc, string reason, CancellationToken token) => Task.FromResult(Complete(Receipt));
        public Task DeleteAsync(Guid id, CancellationToken token) { Complete(true); return Task.CompletedTask; }
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => Task.FromResult(Complete(Versions));
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string versionId, CancellationToken token) => Task.FromResult<NotesDocument?>(Complete(Document));
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => Task.FromResult<NotesDocument?>(Complete(Document));
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => Task.FromResult(Complete(Hits));
    }
}
