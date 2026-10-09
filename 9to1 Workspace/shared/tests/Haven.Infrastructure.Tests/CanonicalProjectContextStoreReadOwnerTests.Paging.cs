using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

// PRIVATE additive controls over the SAME real SQLite/Home fixture, UNCOMPILED/UNRUN.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Source_owned_pages_reach_older_Studio_beneath_more_than_32_actual_Tasks() => Run(async rig =>
    {
        await SeedRecentTasks(rig, 40);
        await rig.Import("canonical.sqlite");
        var first = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 32,
            rig.Scope, rig.Retain, rig.Token, includeStudioContexts: true));
        Assert.Equal(32, first.Conversations.Count); Assert.True(first.HasMore);
        Assert.DoesNotContain(first.Conversations, value => value.Id == rig.Studio.Id);
        var cursor = Assert.IsAssignableFrom<ICanonicalProjectContextStoreContinuation>(first.NextContinuation);
        Assert.True(rig.Reads.IsIssuedOriginalContinuation(cursor));
        var second = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 32,
            rig.Scope, rig.Retain, rig.Token, cursor, includeStudioContexts: true));
        Assert.Equal(11, second.Conversations.Count); Assert.False(second.HasMore); Assert.Null(second.NextContinuation);
        Assert.Contains(second.Conversations, value => value == rig.Studio);
        Assert.Equal(43, first.Conversations.Concat(second.Conversations).Select(value => value.Id).Distinct().Count());
        Assert.False(second.Conversations is Conversation[]); Assert.False(second.Containers is ContainerDefinition[]);
        // Selecting an old page must validate its own exact window, not the latest32.
        await rig.Keep(rig.Reads.RevalidateOriginalObservationWithinSourceAsync(second, rig.Actor, rig.Scope, rig.Retain, rig.Token));
        var known = await rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 1,
            rig.Scope, rig.Retain, rig.Token, rig.Studio.Id));
        Assert.Equal(rig.Studio, Assert.Single(known.Conversations));
    });

    [LinuxOriginalStoreFact]
    public Task Actual_persisted_timestamp_and_ID_TEXT_boundaries_never_repeat_or_skip() => Run(async rig =>
    {
        var seeded = await SeedRecentTasks(rig, 5);
        // A valid historical SQLite timestamp has no fractional precision. Reformatting
        // it as round-trip DateTimeOffset or normalizing uppercase GUID TEXT changes
        // the actual historical SQLite keyset order around the page boundary.
        await using (var connection = await rig.Database.OpenAsync(rig.Token))
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE conversations SET updated_at=$at,id=upper(id) WHERE id IN (" +
                string.Join(',', seeded.Select((_, index) => "$id" + index)) + ");";
            command.Parameters.AddWithValue("$at", "2099-02-03T10:00:00+02:00");
            for (var index = 0; index < seeded.Count; index++) command.Parameters.AddWithValue("$id" + index, seeded[index].Id.ToString("D"));
            Assert.Equal(5, await rig.Keep(command.ExecuteNonQueryAsync(rig.Token)));
        }
        await rig.Import("canonical.sqlite");
        var found = new List<Guid>(); ICanonicalProjectContextStoreContinuation? cursor = null;
        for (var page = 0; page < 8; page++)
        {
            var observed = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 1,
                rig.Scope, rig.Retain, rig.Token, cursor, includeStudioContexts: true));
            found.Add(Assert.Single(observed.Conversations).Id);
            cursor = observed.NextContinuation;
            if (cursor is null) break;
        }
        Assert.Null(cursor); Assert.Equal(8, found.Count); Assert.Equal(8, found.Distinct().Count());
        Assert.Equal(seeded.Select(value => value.Id.ToString("D")).Order(StringComparer.Ordinal),
            found.Take(5).Select(value => value.ToString("D")));
        Assert.Contains(rig.Studio.Id, found);
    });

    [LinuxOriginalStoreFact]
    public Task Source_search_is_literal_and_reaches_actual_older_title_without_ID_authority() => Run(async rig =>
    {
        await SeedRecentTasks(rig, 40);
        var wanted = rig.Studio with { Title = @"old project 50%_\literal" };
        await rig.Keep(rig.Conversations.UpsertConversationAsync(wanted, rig.Token));
        await rig.Import("canonical.sqlite");
        var observed = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 8,
            rig.Scope, rig.Retain, rig.Token, includeStudioContexts: true, searchText: @"%_\"));
        Assert.Equal(wanted, Assert.Single(observed.Conversations)); Assert.False(observed.HasMore);
        Assert.Null(observed.NextContinuation);
        await rig.Keep(rig.Reads.RevalidateOriginalObservationWithinSourceAsync(observed, rig.Actor, rig.Scope, rig.Retain, rig.Token));
    });

    [LinuxOriginalStoreFact]
    public Task Foreign_cursor_refuses_before_metadata_and_same_cause_alias_is_not_acknowledged() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite"); var before = rig.MetadataReaderTasks;
        var raw = rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 1,
            rig.Scope, rig.Retain, rig.Token, new ForeignContinuation(), includeStudioContexts: true));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => raw); rig.Expect(error);
        Assert.Equal(before, rig.MetadataReaderTasks); Assert.True(rig.Reads.IsAcknowledgedOriginalReadRefusal(raw));
        var alias = Task.FromException<ICanonicalProjectContextStoreObservation>(raw.Exception!.InnerExceptions[0]);
        _ = rig.Keep(alias); Assert.False(rig.Reads.IsAcknowledgedOriginalReadRefusal(alias));
        var observed = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 1,
            rig.Scope, rig.Retain, rig.Token, includeStudioContexts: true));
        Assert.NotNull(observed.NextContinuation);
        await rig.Keep(rig.Store.CloseAndDrainAsync()); Assert.True(rig.Store.OriginalClose!.IsCompletedSuccessfully);
    });

    [LinuxOriginalStoreFact]
    public Task Changing_search_or_actor_never_repurposes_an_issued_cursor() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        var first = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 1,
            rig.Scope, rig.Retain, rig.Token, includeStudioContexts: true));
        Assert.NotNull(first.NextContinuation); var before = rig.MetadataReaderTasks;
        var changedSearch = rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 1,
            rig.Scope, rig.Retain, rig.Token, first.NextContinuation, includeStudioContexts: true, searchText: "studio"));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => changedSearch); rig.Expect(error);
        Assert.True(rig.Reads.IsAcknowledgedOriginalReadRefusal(changedSearch)); Assert.Equal(before, rig.MetadataReaderTasks);
        var changedActor = rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(
            rig.Actor with { AuthenticationRevision = rig.Actor.AuthenticationRevision + ":different-current-revision" }, 1,
            rig.Scope, rig.Retain, rig.Token, first.NextContinuation, includeStudioContexts: true));
        var actorError = await Assert.ThrowsAnyAsync<Exception>(() => changedActor); rig.Expect(actorError);
        Assert.True(rig.Reads.IsAcknowledgedOriginalReadRefusal(changedActor)); Assert.Equal(before, rig.MetadataReaderTasks);
        var next = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 1,
            rig.Scope, rig.Retain, rig.Token, first.NextContinuation, includeStudioContexts: true));
        Assert.NotEqual(Assert.Single(first.Conversations).Id, Assert.Single(next.Conversations).Id);
    });

    [LinuxOriginalStoreFact]
    public Task Legacy_import_and_saved_search_never_grant_canonical_title_IO() => Run(async rig =>
    {
        await rig.Import("legacy.saved-agents");
        var raw = rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 8,
            rig.Scope, rig.Retain, rig.Token, includeStudioContexts: true, searchText: "studio"));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => raw); rig.Expect(error);
        Assert.Equal(0, rig.MetadataReaderTasks); Assert.True(rig.Reads.IsAcknowledgedOriginalReadRefusal(raw));
        await rig.Import("canonical.sqlite");
        var observed = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 8,
            rig.Scope, rig.Retain, rig.Token, includeStudioContexts: true, searchText: "studio"));
        Assert.Equal(rig.Studio, Assert.Single(observed.Conversations));
    });

    [LinuxOriginalStoreFact]
    public Task Changed_older_page_rows_refuse_selection_in_the_original_window() => Run(async rig =>
    {
        await SeedRecentTasks(rig, 40); await rig.Import("canonical.sqlite");
        var first = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 32,
            rig.Scope, rig.Retain, rig.Token, includeStudioContexts: true));
        var second = await rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 32,
            rig.Scope, rig.Retain, rig.Token, first.NextContinuation, includeStudioContexts: true));
        Assert.Contains(second.Conversations, value => value.Id == rig.Studio.Id);
        await rig.Keep(rig.Conversations.UpsertConversationAsync(rig.Studio with { Title = "changed original studio title" }, rig.Token));
        var raw = rig.Keep(rig.Reads.RevalidateOriginalObservationWithinSourceAsync(second, rig.Actor,
            rig.Scope, rig.Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => raw); rig.Expect(error);
        Assert.False(rig.Reads.IsAcknowledgedOriginalReadRefusal(raw));
    });

    [LinuxOriginalStoreFact]
    public Task Cursor_refusal_never_acknowledges_an_independent_original_callback_fault() => Run(async rig =>
    {
        var io = new IOException("actual caller declined cursor publication");
        void Scope(Action body) { body(); throw io; }
        var raw = rig.Keep(rig.Reads.ReadOriginalProjectContextPageWithinSourceAsync(rig.Actor, 1,
            Scope, rig.Retain, rig.Token, new ForeignContinuation(), includeStudioContexts: true));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => raw); rig.Expect(error); rig.Expect(io);
        Assert.Contains(io, References(error)); Assert.Equal(0, rig.MetadataReaderTasks);
        Assert.False(rig.Reads.IsAcknowledgedOriginalReadRefusal(raw));
    });

    private sealed class ForeignContinuation : ICanonicalProjectContextStoreContinuation { }
    private static async Task<IReadOnlyList<Conversation>> SeedRecentTasks(Rig rig, int count)
    {
        var now = DateTimeOffset.UtcNow.AddMinutes(10);
        var container = new ContainerDefinition(Guid.NewGuid(), HavenMode.Tasks, "newer actual task project",
            Path.Combine(rig.Root, "newer-tasks"), "actual fixture-owned saved metadata", "instructions", now, now);
        await rig.Keep(rig.Reads.OriginalContainers.UpsertAsync(container, rig.Token));
        var rows = new List<Conversation>();
        for (var index = 0; index < count; index++)
        {
            var at = now.AddMinutes(index);
            var row = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task, "newer task " + index,
                container.Id, null, false, false, at, at);
            Assert.True(await rig.Keep(rig.Conversations.TryCreateConversationAsync(row, rig.Token))); rows.Add(row);
        }
        return rows.AsReadOnly();
    }
}
