using System.Text.Json;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Automation_library_requires_actual_canonical_import_and_keeps_existing_rows_read_only() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var source = ActualAutomationReadSource(rig, repository);
        await SeedAutomationReadRows(rig, repository);
        var before = await rig.Keep(repository.GetAllAsync(rig.Token));
        var rawBefore = rig.MetadataReaderTasks;
        var query = new AutomationLibraryQuery(Limit: 8);
        var absent = await rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(rig.Actor, query, rig.Scope, rig.Retain, rig.Token));
        Assert.True(source.IsIssuedOriginalObservation(absent));
        Assert.Equal(CanonicalAutomationLibraryOriginalReadOwner.LibraryState.SetupRequired, absent.State);
        Assert.Empty(absent.Definitions); Assert.Equal(rawBefore, rig.MetadataReaderTasks);
        await rig.Import("legacy.saved-agents");
        var separate = await rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(rig.Actor, query, rig.Scope, rig.Retain, rig.Token));
        Assert.Empty(separate.Definitions); Assert.Equal(rawBefore, rig.MetadataReaderTasks);
        await rig.Import("canonical.sqlite");
        var observed = await rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(rig.Actor, query, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(CanonicalAutomationLibraryOriginalReadOwner.LibraryState.Available, observed.State);
        Assert.Equal(rig.Identity, observed.OriginalStoreIdentity); Assert.Equal(rig.Actor, observed.Actor);
        Assert.Equal(3, observed.Definitions.Count); Assert.False(observed.Definitions is AutomationOwnerRead<AutomationDefinition>[]);
        Assert.All(observed.Definitions, row =>
        {
            Assert.True(row.RequiresRecovery); Assert.Null(row.Value.OwnerBinding);
            Assert.Equal("ORIGINAL_AUTOMATION_OWNER_REVIEW_REQUIRED", row.RecoveryCode);
            Assert.Contains("schedule_json", row.RetainedProtectedDescriptors.Keys);
            Assert.Null(row.RetainedProtectedDescriptors["lease_token"]);
        });
        Assert.Contains(observed.Definitions, row => !row.Value.IsEnabled);
        Assert.Contains(observed.Definitions, row => row.Value.IsEnabled);
        Assert.Equal(before, await rig.Keep(repository.GetAllAsync(rig.Token)));
        await rig.Keep(source.RevalidateOriginalObservationWithinSourceAsync(observed, rig.Actor, rig.Scope, rig.Retain, rig.Token));
        Assert.Same(repository, source.OriginalRepository); Assert.Same(rig.Store, source.OriginalStore);
    });

    [LinuxOriginalStoreFact]
    public Task Automation_opaque_pages_keep_raw_key_order_and_revalidate_the_older_search_window() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var source = ActualAutomationReadSource(rig, repository);
        var rows = await SeedAutomationReadRows(rig, repository);
        // Actual SQLite text identity remains source-owned; the cursor must not
        // normalize a parsed Guid/timestamp and skip or repeat an older row.
        await using (var connection = await rig.Keep(rig.Database.OpenAsync(rig.Token)))
        await using (var update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE automations SET id=upper(id) WHERE id=$id;";
            update.Parameters.AddWithValue("$id", rows[0].Id.ToString());
            Assert.Equal(1, await rig.Keep(update.ExecuteNonQueryAsync(rig.Token)));
        }
        await rig.Import("canonical.sqlite");
        var query = new AutomationLibraryQuery(Search: "100%_literal", Limit: 1);
        var first = await rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(rig.Actor, query, rig.Scope, rig.Retain, rig.Token));
        Assert.Single(first.Definitions); Assert.NotNull(first.NextContinuation);
        Assert.True(source.IsIssuedOriginalContinuation(first.NextContinuation));
        var second = await rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(rig.Actor, query, rig.Scope, rig.Retain, rig.Token, first.NextContinuation));
        Assert.Single(second.Definitions); Assert.NotEqual(first.Definitions[0].Value.Id, second.Definitions[0].Value.Id);
        Assert.Null(second.NextContinuation); // The wildcard-looking text was escaped, not expanded.
        await rig.Keep(repository.UpsertAsync(rows[0] with { Id = Guid.NewGuid(), Name = "100%_literal newest",
            CreatedAt = rows[0].CreatedAt.AddDays(1), UpdatedAt = rows[0].UpdatedAt.AddDays(1) }, rig.Token));
        // A new first page does not invalidate the unchanged original older window.
        await rig.Keep(source.RevalidateOriginalObservationWithinSourceAsync(second, rig.Actor, rig.Scope, rig.Retain, rig.Token));
        var fresh = await rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(rig.Actor, query, rig.Scope, rig.Retain, rig.Token));
        Assert.EndsWith("newest", Assert.Single(fresh.Definitions).Value.Name);
        Assert.Equal(new[] { rows[0].Id, rows[1].Id }.Order(),
            first.Definitions.Concat(second.Definitions).Select(row => row.Value.Id).Order());
    });

    [LinuxOriginalStoreFact]
    public Task Changed_automation_page_and_copied_observation_do_not_reissue_the_original_read_selection() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var source = ActualAutomationReadSource(rig, repository);
        var rows = await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite");
        var original = await rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(rig.Actor,
            new(Limit: 8), rig.Scope, rig.Retain, rig.Token));
        await rig.Keep(repository.UpsertAsync(rows[0] with { Instruction = "actual newer saved instruction" }, rig.Token));
        var stale = rig.Keep(source.RevalidateOriginalObservationWithinSourceAsync(original, rig.Actor, rig.Scope, rig.Retain, rig.Token));
        rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => stale));
        var copied = new CopiedAutomationLibrary(original);
        Assert.False(source.IsIssuedOriginalObservation(copied)); var before = rig.MetadataReaderTasks;
        var foreign = rig.Keep(source.RevalidateOriginalObservationWithinSourceAsync(copied, rig.Actor, rig.Scope, rig.Retain, rig.Token));
        rig.Expect(await Assert.ThrowsAnyAsync<Exception>(() => foreign));
        Assert.Equal(before, rig.MetadataReaderTasks);
        Assert.Equal("actual newer saved instruction", (await rig.Keep(repository.GetAllAsync(rig.Token))).Single(row => row.Id == rows[0].Id).Instruction);
    });

    [LinuxOriginalStoreFact]
    public Task A_swallowed_original_callback_failure_refuses_automation_metadata_before_any_reader_starts() => Run(async rig =>
    {
        var repository = new AutomationRepository(rig.Database);
        var source = ActualAutomationReadSource(rig, repository);
        await SeedAutomationReadRows(rig, repository);
        await rig.Import("canonical.sqlite");
        Task<CanonicalSqliteOriginalStoreLease>? actualLiveLease = null;
        Exception? sameProtocolFailure = null;
        var depth = 0; var injected = 0;
        void RetainActual(Task actual)
        {
            rig.Retain(actual);
            // The caller observes the SAME finite raw acquisition, not a fabricated
            // receipt or a replacement Task. Ignore already-closed identity reads.
            if (actual is Task<CanonicalSqliteOriginalStoreLease> lease && depth == 2)
                actualLiveLease = lease;
        }
        void ScopeActual(Action body)
        {
            var outer = ++depth == 1;
            var heldBeforeThisCallback = outer && actualLiveLease is { IsCompletedSuccessfully: true } &&
                actualLiveLease.Result.OriginalClose is null;
            try
            {
                body();
                if (heldBeforeThisCallback && Interlocked.CompareExchange(ref injected, 1, 0) == 0)
                {
                    try { body(); }
                    catch (Exception cause) { sameProtocolFailure = cause; } // Caller swallows the second invocation only.
                }
            }
            finally { depth--; }
        }
        var before = rig.MetadataReaderTasks;
        var read = rig.Keep(source.ReadOriginalLibraryWithinSourceAsync(rig.Actor,
            new(Limit: 8), ScopeActual, RetainActual, rig.Token));
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => read);
        rig.Expect(failure);
        Assert.Equal(1, injected); Assert.NotNull(sameProtocolFailure);
        var observedCauses = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        void ObserveGraph(Exception actual)
        {
            if (!observedCauses.Add(actual)) return;
            if (actual is AggregateException group) foreach (var child in group.InnerExceptions) ObserveGraph(child);
        }
        ObserveGraph(read.Exception ?? failure);
        Assert.Contains(sameProtocolFailure, observedCauses);
        Assert.Equal(before, rig.MetadataReaderTasks);
        // Actual store/lease/source originals are joined by the maintained Rig.
        // Expected protocol failure is retained; no schema/row or WRITE effect occurs.
    });

    private static CanonicalAutomationLibraryOriginalReadOwner ActualAutomationReadSource(Rig rig, AutomationRepository repository) =>
        new(rig.Store, rig.Database, rig.OriginalPaths, repository, rig.Reads.OriginalOwnershipAuthority);
    private static async Task<AutomationDefinition[]> SeedAutomationReadRows(Rig rig, AutomationRepository repository)
    {
        var now = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var rows = new[]
        {
            new AutomationDefinition(Guid.Parse("ab000000-0000-0000-0000-000000000001"), "100%_literal saved A", HavenMode.Chat,
                "original saved instruction", AutomationScheduleKind.Once, JsonSerializer.Serialize(new { at = now.AddDays(1) }), null, null, false, now, now),
            new AutomationDefinition(Guid.Parse("bb000000-0000-0000-0000-000000000002"), "100%_literal saved B", HavenMode.Chat,
                "original second instruction", AutomationScheduleKind.Daily, "{\"time\":\"08:00\"}", null, null, true, now, now),
            new AutomationDefinition(Guid.Parse("cb000000-0000-0000-0000-000000000003"), "100XXliteral decoy", HavenMode.Chat,
                "original third instruction", AutomationScheduleKind.Hourly, "{\"intervalHours\":2}", null, null, false, now, now)
        };
        foreach (var row in rows) await rig.Keep(repository.UpsertAsync(row, rig.Token));
        return rows;
    }
    private sealed class CopiedAutomationLibrary(CanonicalAutomationLibraryOriginalReadOwner.IOriginalLibraryObservation original)
        : CanonicalAutomationLibraryOriginalReadOwner.IOriginalLibraryObservation
    {
        public AuthenticatedResourceActor Actor => original.Actor;
        public ResourceStoreIdentity? OriginalStoreIdentity => original.OriginalStoreIdentity;
        public CanonicalAutomationLibraryOriginalReadOwner.LibraryState State => original.State;
        public string Detail => original.Detail;
        public IReadOnlyList<AutomationOwnerRead<AutomationDefinition>> Definitions => original.Definitions;
        public CanonicalAutomationLibraryOriginalReadOwner.IOriginalLibraryContinuation? NextContinuation => original.NextContinuation;
    }
}
