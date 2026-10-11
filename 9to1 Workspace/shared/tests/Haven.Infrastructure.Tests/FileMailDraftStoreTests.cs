using System.Text.Json;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class FileMailDraftStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "HavenMailDraftStoreTests", Guid.NewGuid().ToString("N"));
    private readonly FileMailDraftStore _store;

    private sealed class TestPaths(string dataDirectory) : IAppPaths
    {
        public string DataDirectory { get; } = dataDirectory;
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }

    public FileMailDraftStoreTests() => _store = new FileMailDraftStore(new TestPaths(_dataDirectory));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mismatched_or_empty_file_identity_is_omitted_without_hiding_valid_drafts(bool emptyIdentity)
    {
        var invalidFileId = Guid.NewGuid();
        var invalidDraft = Draft(emptyIdentity ? Guid.Empty : Guid.NewGuid(), "must not be returned");
        var validDraft = Draft(Guid.NewGuid(), "valid draft remains readable");
        var directory = Path.Combine(_dataDirectory, "MailDrafts");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, invalidFileId.ToString("N") + ".json"), JsonSerializer.Serialize(invalidDraft, JsonOptions));
        await _store.UpsertAsync(validDraft, CancellationToken.None);

        Assert.Null(await _store.GetAsync(invalidFileId, CancellationToken.None));
        var all = await _store.GetAllAsync(CancellationToken.None);
        var recovered = Assert.Single(all);
        Assert.Equal(validDraft.LocalId, recovered.LocalId);
        Assert.Equal("valid draft remains readable", recovered.Subject);
        Assert.Equal(validDraft.LocalId, (await _store.GetAsync(validDraft.LocalId, CancellationToken.None))?.LocalId);
    }

    private static MailDraft Draft(Guid localId, string subject) => new(
        Guid.NewGuid(), null, MailResponseKind.New, null, null, [], [], [], subject, "body", false, [],
        LocalId: localId, PersistenceState: MailDraftPersistenceState.Saved);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dataDirectory)) Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
