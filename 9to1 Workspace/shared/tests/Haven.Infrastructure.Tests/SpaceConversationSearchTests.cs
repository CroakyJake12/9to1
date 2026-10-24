using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

/// <summary>Real canonical SQLite title lookup; no provider or alternate history store.</summary>
public sealed class SpaceConversationSearchTests : IDisposable
{
    private readonly Paths _paths = new();

    [Fact]
    public async Task Finds_a_saved_title_older_than_the_twenty_visible_recent_chats()
    {
        var repository = await RepositoryAsync();
        var space = Guid.NewGuid();
        var old = Row(space, "Revision plan", 0);
        await repository.UpsertConversationAsync(old, CancellationToken.None);
        for (var i = 1; i <= 25; i++)
            await repository.UpsertConversationAsync(Row(space, "Recent " + i, i), CancellationToken.None);
        Assert.DoesNotContain(await repository.GetBySpaceAsync(space, 20, CancellationToken.None), row => row.Id == old.Id);
        Assert.Equal(old.Id, Assert.Single(await repository.FindSavedTitlesBySpaceAsync(space, "revision", 50, CancellationToken.None)).Id);
        Assert.Equal(old, await repository.GetAsync(old.Id, CancellationToken.None)); // Read leaves all metadata intact.
    }

    [Fact]
    public async Task Scope_saved_status_and_limit_are_applied_before_the_results_are_returned()
    {
        var repository = await RepositoryAsync();
        var space = Guid.NewGuid();
        var expected = new List<Conversation>();
        for (var i = 0; i < 55; i++)
        {
            var row = Row(space, "Target " + i, i);
            expected.Add(row);
            await repository.UpsertConversationAsync(row, CancellationToken.None);
        }
        foreach (var excluded in new[] {
            Row(Guid.NewGuid(), "Target foreign", 100), Row(null, "Target unassigned", 101),
            Row(space, "Target temporary", 102) with { IsTemporary = true },
            Row(space, "Target archived", 103) with { IsArchived = true } })
            await repository.UpsertConversationAsync(excluded, CancellationToken.None);
        var actual = await repository.FindSavedTitlesBySpaceAsync(space, "target", 50, CancellationToken.None);
        Assert.Equal(expected.OrderByDescending(row => row.UpdatedAt).Take(50).Select(row => row.Id), actual.Select(row => row.Id));
        Assert.All(actual, row => { Assert.Equal(space, row.SpaceId); Assert.False(row.IsArchived); Assert.False(row.IsTemporary); });
    }

    [Theory]
    [InlineData("100%_\\", "Annual 100%_\\work", "Annual 100abwork")]
    [InlineData("ÉCOLE", "École notes", "Other notes")]
    [InlineData("' OR 1=1 --", "Literal ' OR 1=1 -- title", "Other title")]
    public async Task Search_is_literal_and_ordinal_case_insensitive(string query, string title, string other)
    {
        var repository = await RepositoryAsync();
        var space = Guid.NewGuid();
        var expected = Row(space, title, 0);
        await repository.UpsertConversationAsync(expected, CancellationToken.None);
        await repository.UpsertConversationAsync(Row(space, other, 1), CancellationToken.None);
        Assert.Equal(expected.Id, Assert.Single(await repository.FindSavedTitlesBySpaceAsync(space, query, 50, CancellationToken.None)).Id);
    }

    [Fact]
    public async Task Invalid_requests_and_original_cancellation_are_refused()
    {
        var repository = await RepositoryAsync();
        var space = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => repository.FindSavedTitlesBySpaceAsync(Guid.Empty, "title", 50, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => repository.FindSavedTitlesBySpaceAsync(space, "  ", 50, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.FindSavedTitlesBySpaceAsync(space, new string('x', 257), 50, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.FindSavedTitlesBySpaceAsync(space, "title", 0, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repository.FindSavedTitlesBySpaceAsync(space, "title", 51, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repository.FindSavedTitlesBySpaceAsync(space, "title", 50, cancellation.Token));
    }

    [Fact]
    public async Task Actual_usage_tracking_wrapper_forwards_the_same_read_capability()
    {
        var repository = await RepositoryAsync();
        var space = Guid.NewGuid();
        var expected = Row(space, "Tracked history", 0);
        await repository.UpsertConversationAsync(expected, CancellationToken.None);
        // This read cannot enter any usage/provider path; those collaborators are deliberately
        // unavailable, so accidental usage or model work fails rather than reporting success.
        var tracked = new UsageTrackingConversationRepository(repository, null!, null!, null!, null!);
        Assert.True(tracked.IsBoundToOriginalRepository(repository));
        var search = Assert.IsAssignableFrom<ISpaceConversationSearch>(tracked);
        Assert.Equal(expected, Assert.Single(await search.FindSavedTitlesBySpaceAsync(space, "history", 50, CancellationToken.None)));
    }

    private async Task<ConversationRepository> RepositoryAsync()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        return new ConversationRepository(database);
    }

    private static Conversation Row(Guid? space, string title, int minutes) => new(
        Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, title, null, null, false, false,
        DateTimeOffset.UnixEpoch.AddMinutes(minutes), DateTimeOffset.UnixEpoch.AddMinutes(minutes), SpaceId: space);

    public void Dispose() => _paths.Dispose();

    private sealed class Paths : IAppPaths, IDisposable
    {
        public Paths() { Directory.CreateDirectory(DataDirectory); }
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "spaces-title-search-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "history.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
