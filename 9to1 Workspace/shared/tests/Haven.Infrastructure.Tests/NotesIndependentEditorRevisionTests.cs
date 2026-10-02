using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class NotesIndependentEditorRevisionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Independent_physical_editors_cannot_overwrite_same_revision_or_adopt_new_current_after_recovery(bool recover)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var ct = deadline.Token;
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            await using var diagnostics = new ProductionDiagnostics(paths);
            NotesRepository Create() => new(paths, new NotesDocumentValidator(), diagnostics);
            var first = Create(); var second = Create();
            var seed = NotesDocument.Create("Original"); var seedSave = await first.SaveAsync(seed, "Seed", ct);
            if (recover)
            {
                seed.Title = "Version two"; await first.SaveAsync(seed, "Actual second version", ct);
                await File.WriteAllTextAsync(seedSave.CurrentPath, "Unreadable current", ct);
                var oldRecovery = Assert.IsType<NotesDocument>(await first.LoadAsync(seed.Id, ct));
                var actualRecovery = Assert.IsType<NotesDocument>(await second.LoadAsync(seed.Id, ct));
                Assert.True(oldRecovery.Recovery.HasUnsavedRecovery);
                actualRecovery.Title = "Independent actual repair";
                var repair = await second.SaveAsync(actualRecovery, "Repair exact physical backup", ct);
                var bytes = await File.ReadAllBytesAsync(repair.CurrentPath, ct);
                oldRecovery.Title = "Stale recovered editor";
                await Assert.ThrowsAsync<NotesRevisionConflictException>(() => first.SaveAsync(oldRecovery, "Stale recovery", ct));
                Assert.Equal(bytes, await File.ReadAllBytesAsync(repair.CurrentPath, ct));
                Assert.Equal("Independent actual repair", (await Create().LoadAsync(seed.Id, ct))!.Title);
            }
            else
            {
                var left = Assert.IsType<NotesDocument>(await first.LoadAsync(seed.Id, ct));
                var right = Assert.IsType<NotesDocument>(await second.LoadAsync(seed.Id, ct));
                left.Title = "Left"; right.Title = "Right";
                async Task<bool> Save(NotesRepository repository, NotesDocument document)
                {
                    try { await repository.SaveAsync(document, "Actual competing save", ct); return true; }
                    catch (NotesRevisionConflictException) { return false; }
                }
                var results = await Task.WhenAll(Save(first, left), Save(second, right));
                Assert.Single(results.Where(result => result));
                var current = Assert.IsType<NotesDocument>(await Create().LoadAsync(seed.Id, ct));
                Assert.Equal(2, current.Version); Assert.Contains(current.Title, new[] { "Left", "Right" });
                Assert.Equal(seed.Id, current.Id);
            }
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }
    [Fact]
    public async Task Unrecoverable_actual_current_cannot_be_replaced_by_zero_version_candidate_and_CAS_does_not_quarantine_it()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60)); var ct = deadline.Token;
        var paths = new Paths(); Directory.CreateDirectory(paths.DataDirectory);
        try
        {
            await using var diagnostics = new ProductionDiagnostics(paths);
            var actual = new NotesRepository(paths, new NotesDocumentValidator(), diagnostics);
            var seed = NotesDocument.Create("Existing identity"); var saved = await actual.SaveAsync(seed, "Seed", ct);
            Directory.Delete(Path.GetDirectoryName(saved.VersionPath)!, true);
            await File.WriteAllTextAsync(saved.CurrentPath, "Unrecoverable original current bytes", ct);
            var original = await File.ReadAllBytesAsync(saved.CurrentPath, ct);
            var filesBefore = Directory.GetFiles(Path.GetDirectoryName(saved.CurrentPath)!, "*", SearchOption.AllDirectories);
            var candidate = NotesDocument.Create("Unrelated zero-version proposal"); candidate.Id = seed.Id;
            await Assert.ThrowsAsync<InvalidDataException>(() => actual.SaveAsync(candidate, "Must refuse", ct));
            Assert.Equal(original, await File.ReadAllBytesAsync(saved.CurrentPath, ct));
            Assert.Equal(filesBefore, Directory.GetFiles(Path.GetDirectoryName(saved.CurrentPath)!, "*", SearchOption.AllDirectories));
            Assert.Equal(0, candidate.Version); Assert.False(Directory.Exists(Path.GetDirectoryName(saved.VersionPath)!));
        }
        finally { Directory.Delete(paths.DataDirectory, true); }
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-notes-cas-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    }
}
