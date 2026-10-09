using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed partial class ConversationProductionRepositoryTests
{
    [Fact]
    public async Task Create_only_collision_never_overwrites_existing_canonical_identity_or_scope()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var original = ConversationAt(DateTimeOffset.UtcNow);
        Assert.True(await repository.TryCreateConversationAsync(original, default));
        Assert.False(await repository.TryCreateConversationAsync(original with
            { Title = "foreign", Mode = HavenMode.Tasks, Kind = ConversationKind.Task, SpaceId = Guid.NewGuid() }, default));
        Assert.Equal(original, await repository.GetAsync(original.Id, default));
    }

    [Fact]
    public async Task Concurrent_create_only_writers_acknowledge_exactly_one_original_and_preserve_it()
    {
        var database = await CreateDatabaseAsync();
        var repository = new ConversationRepository(database);
        var prototype = ConversationAt(DateTimeOffset.UtcNow);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var originals = Enumerable.Range(0, 8).Select(AttemptAsync).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(originals);
        var winner = Assert.Single(results, result => result.Acknowledged);
        foreach (var actual in originals) Assert.True(actual.IsCompletedSuccessfully);
        Assert.Equal(winner.Original, await repository.GetAsync(prototype.Id, default));
        async Task<(bool Acknowledged, Conversation Original)> AttemptAsync(int index)
        {
            await start.Task;
            var original = prototype with { Title = "original-" + index };
            return (await repository.TryCreateConversationAsync(original, default), original);
        }
    }
}
