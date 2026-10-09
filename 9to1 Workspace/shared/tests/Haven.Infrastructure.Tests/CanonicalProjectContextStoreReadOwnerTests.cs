using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Haven.Infrastructure.Tests;

// PRIVATE, UNCOMPILED/UNRUN. Real SQLite + OS principal + Home import/receipt/native
// handles. No fake repository, store, permission grant, model or product engine.
public sealed partial class CanonicalProjectContextStoreReadOwnerTests
{
    [LinuxOriginalStoreFact]
    public Task Unimported_store_returns_no_global_project_reader() => Run(async rig =>
    {
        var actual = rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 8, rig.Scope, rig.Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); rig.Expect(error);
        Assert.Equal(0, rig.MetadataReaderTasks);
        Assert.Empty((await rig.Permissions.GetSnapshotAsync()).PendingRequests);
    });
    [LinuxOriginalStoreFact]
    public Task Real_manual_canonical_import_issues_bounded_immutable_context_rows() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        var observed = await rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 1, rig.Scope, rig.Retain, rig.Token));
        Assert.True(rig.Reads.IsIssuedOriginalObservation(observed)); Assert.True(observed.HasMore);
        Assert.Equal(rig.Actor, observed.Actor); Assert.NotNull(observed.OriginalStoreOwnership.Receipt);
        var context = Assert.Single(observed.Conversations); var container = Assert.Single(observed.Containers);
        Assert.Equal(container.Id, context.ContainerId); Assert.Equal(HavenMode.Tasks, context.Mode);
        Assert.False(observed.Conversations is Conversation[]); Assert.False(observed.Containers is ContainerDefinition[]);
        await rig.Keep(rig.Reads.RevalidateOriginalObservationWithinSourceAsync(observed, rig.Actor, rig.Scope, rig.Retain, rig.Token));
        Assert.True(rig.MetadataReaderTasks > 0);
    });
    [LinuxOriginalStoreFact]
    public Task Legacy_import_never_authorizes_canonical_project_titles() => Run(async rig =>
    {
        await rig.Import("legacy.saved-agents");
        var actual = rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 8, rig.Scope, rig.Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); rig.Expect(error);
        Assert.Equal(0, rig.MetadataReaderTasks);
        Assert.Null(await rig.Ownership.GetVerifiedAsync("canonical.sqlite", rig.Identity.StoreId.ToString("D")));
    });
    [LinuxOriginalStoreFact]
    public Task Targeted_Studio_read_keeps_original_mode_and_filters_unrelated_modes() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        var observed = await rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 1,
            rig.Scope, rig.Retain, rig.Token, rig.Studio.Id));
        Assert.Equal(rig.Studio, Assert.Single(observed.Conversations));
        Assert.Equal(HavenMode.Studio, Assert.Single(observed.Containers).Mode); Assert.False(observed.HasMore);
        Assert.Equal(rig.Studio, await rig.Conversations.GetAsync(rig.Studio.Id, rig.Token));
    });
    [LinuxOriginalStoreFact]
    public Task Public_matching_metadata_cannot_mint_the_actual_source_observation() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        var original = await rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 4, rig.Scope, rig.Retain, rig.Token));
        var foreign = new ForeignObservation(original); Assert.False(rig.Reads.IsIssuedOriginalObservation(foreign));
        var before = rig.MetadataReaderTasks;
        var actual = rig.Keep(rig.Reads.RevalidateOriginalObservationWithinSourceAsync(foreign, rig.Actor, rig.Scope, rig.Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); rig.Expect(error);
        Assert.Equal(before, rig.MetadataReaderTasks);
    });
    [LinuxOriginalStoreFact]
    public Task Changed_actual_Home_profile_refuses_before_global_metadata_reader() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        var home = await rig.Home.ReadAsync(rig.Token);
        var old = Assert.Single(home.State!.Records, row => row.RecordId == "home.local-profile");
        var profile = old.Payload.Deserialize<HomeLocalProfile>()!;
        var changed = old with { Revision = old.Revision + 1, Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) };
        Assert.True((await rig.Home.WriteAsync(changed, old.Revision, rig.Token)).IsSuccess);
        var actual = rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 8, rig.Scope, rig.Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); rig.Expect(error);
        Assert.Equal(0, rig.MetadataReaderTasks);
        Assert.True((await rig.Home.WriteAsync(old with { Revision = changed.Revision + 1 }, changed.Revision, CancellationToken.None)).IsSuccess);
    });
    [LinuxOriginalStoreFact]
    public Task Held_snapshot_rechecks_fresh_identity_and_refuses_an_external_rekey() => Run(async rig =>
    {
        var lease = await rig.Keep(rig.Store.AcquireOriginalProtectedReadWithinSourceAsync(rig.Actor, false, rig.Scope, rig.Retain, rig.Token));
        await using (var external = await rig.Database.OpenAsync(rig.Token))
        {
            await using var command = external.CreateCommand();
            command.CommandText = "UPDATE settings SET value=$value WHERE key=$key;";
            command.Parameters.AddWithValue("$key", CanonicalSqliteOriginalStoreOwner.IdentityKey);
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(new { SchemaVersion = 1, StoreId = Guid.NewGuid(), CreatedAtUtc = rig.Identity.CreatedAtUtc }));
            Assert.Equal(1, await command.ExecuteNonQueryAsync(rig.Token));
        }
        var actual = rig.Keep(lease.RevalidateWithinSourceAsync(rig.Scope, rig.Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual); rig.Expect(error);
        var close = rig.Keep(lease.CloseAndDrainAsync());
        var closeError = await Assert.ThrowsAnyAsync<Exception>(() => close); rig.Expect(closeError);
    });
    [LinuxOriginalStoreFact]
    public Task Held_raw_fault_keeps_foreign_empty_and_nested_causes_until_actual_close() => Run(async rig =>
    {
        var lease = await rig.Keep(rig.Store.AcquireOriginalProtectedReadWithinSourceAsync(rig.Actor, false, rig.Scope, rig.Retain, rig.Token));
        var raw = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var empty = new AggregateException("foreign empty source cause");
        var nested = new AggregateException("foreign nested source cause", new IOException("foreign inner"));
        var actual = rig.Keep(lease.ReadOriginalSourceAsync(() => raw.Task)); Task? close = null;
        try { close = rig.Keep(lease.CloseAndDrainAsync()); Assert.False(close.IsCompleted); }
        finally { raw.TrySetException([empty, nested]); }
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.Contains(empty, References(error)); Assert.Contains(nested, References(error)); rig.Expect(empty); rig.Expect(nested); rig.Expect(error);
        var closeError = await Assert.ThrowsAnyAsync<Exception>(() => close!);
        Assert.Contains(empty, References(closeError)); Assert.Contains(nested, References(closeError)); rig.Expect(closeError);
        Assert.Same(close, lease.CloseAndDrainAsync()); Assert.True(actual.IsFaulted);
    });
    [LinuxOriginalStoreFact]
    public Task Long_healthy_original_lease_reads_join_and_release_actual_command_custody() => Run(async rig =>
    {
        var lease = await rig.Keep(rig.Store.AcquireOriginalProtectedReadWithinSourceAsync(rig.Actor, false, rig.Scope, rig.Retain, rig.Token));
        for (var index = 0; index < 600; index++)
        {
            var command = lease.CreateOriginalCommand();
            lease.InvokeOriginalSource(() => command.CommandText = "SELECT 1;");
            Assert.Equal(1L, Convert.ToInt64(await rig.Keep(lease.ReadOriginalSourceAsync(() => command.ExecuteScalarAsync(rig.Token)))));
            await rig.Keep(lease.CloseOriginalResourceAsync(command));
        }
        await rig.Keep(lease.CloseAndDrainAsync()); Assert.True(lease.OriginalClose!.IsCompletedSuccessfully);
    });

    [LinuxOriginalStoreFact]
    public Task Opted_in_studio_catalogue_preserves_both_saved_modes() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        var observed = await rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 8,
            rig.Scope, rig.Retain, rig.Token, includeStudioContexts: true));
        Assert.Equal(3, observed.Conversations.Count); Assert.False(observed.HasMore);
        Assert.Contains(observed.Conversations, value => value.Mode == HavenMode.Tasks && value.Kind == ConversationKind.Task);
        Assert.Contains(observed.Conversations, value => value == rig.Studio);
        await rig.Keep(rig.Reads.RevalidateOriginalObservationWithinSourceAsync(observed, rig.Actor, rig.Scope, rig.Retain, rig.Token));
    });
    [LinuxOriginalStoreFact]
    public Task Actual_command_captured_before_post_callback_refusal_is_closed_without_replay() => Run(async rig =>
    {
        var refuse = 0; var refusal = new IOException("actual original callback declined publication");
        void Scope(Action body) { body(); if (Interlocked.Exchange(ref refuse, 0) != 0) throw refusal; }
        var lease = await rig.Keep(rig.Store.AcquireOriginalProtectedReadWithinSourceAsync(rig.Actor, false, Scope, rig.Retain, rig.Token));
        SqliteCommand? actualCommand = null;
        Interlocked.Exchange(ref refuse, 1);
        var observed = Assert.Throws<IOException>(() => lease.InvokeOriginalSource(() =>
        {
            actualCommand = lease.Connection.CreateCommand(); actualCommand.Transaction = lease.Transaction; return actualCommand;
        }));
        Assert.Same(refusal, observed); rig.Expect(refusal); Assert.NotNull(actualCommand);
        var close = rig.Keep(lease.CloseOriginalResourceAsync(actualCommand!)); await close;
        Assert.Same(close, lease.CloseOriginalResourceAsync(actualCommand!));
        var whole = rig.Keep(lease.CloseAndDrainAsync()); var error = await Assert.ThrowsAnyAsync<Exception>(() => whole); rig.Expect(error);
        Assert.Contains(refusal, References(error)); Assert.True(close.IsCompletedSuccessfully);
    });

    [LinuxOriginalStoreFact]
    public Task Successful_finite_lease_is_closed_before_acquisition_publication_refusal_returns() => Run(async rig =>
    {
        await rig.Import("canonical.sqlite");
        Task<CanonicalSqliteOriginalStoreLease>? originalLease = null;
        var declined = new IOException("actual retained lease publication declined"); var refuse = 1;
        void Retain(Task actual)
        {
            rig.Retain(actual);
            if (actual is Task<CanonicalSqliteOriginalStoreLease> lease && Interlocked.Exchange(ref refuse, 0) != 0)
            { originalLease = lease; throw declined; }
        }
        var operation = rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 4, rig.Scope, Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => operation); rig.Expect(error); rig.Expect(declined);
        Assert.NotNull(originalLease);
        var acquired = await originalLease!;
        Assert.NotNull(acquired.OriginalClose); await acquired.OriginalClose!;
        Assert.True(acquired.OriginalClose!.IsCompletedSuccessfully);
        Assert.Contains(declined, References(error)); Assert.Equal(0, rig.MetadataReaderTasks);
    });

    [LinuxOriginalStoreFact]
    public Task Known_missing_import_refusal_can_be_fixed_without_poisoning_actual_owner_close() => Run(async rig =>
    {
        var declined = rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 4, rig.Scope, rig.Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => declined); rig.Expect(error);
        Assert.True(rig.Reads.IsAcknowledgedOriginalReadRefusal(declined));
        Assert.True(rig.Store.IsAcknowledgedOriginalCommandRefusal(declined)); Assert.Equal(0, rig.MetadataReaderTasks);
        var foreign = Task.FromException<ICanonicalProjectContextStoreObservation>(declined.Exception!.InnerExceptions[0]);
        _ = rig.Keep(foreign); Assert.False(rig.Reads.IsAcknowledgedOriginalReadRefusal(foreign));
        Assert.False(rig.Store.IsAcknowledgedOriginalCommandRefusal(foreign));
        await rig.Import("canonical.sqlite");
        var current = await rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 4, rig.Scope, rig.Retain, rig.Token));
        Assert.Equal(2, current.Conversations.Count);
        await rig.Keep(rig.Store.CloseAndDrainAsync()); Assert.True(rig.Store.OriginalClose!.IsCompletedSuccessfully);
        Assert.True(rig.Reads.IsAcknowledgedOriginalReadRefusal(declined));
    });
    [LinuxOriginalStoreFact]
    public Task Original_callback_io_failure_is_never_a_known_missing_import_refusal() => Run(async rig =>
    {
        var io = new IOException("independent actual source publication IO"); var once = 1;
        void Retain(Task actual)
        {
            rig.Retain(actual);
            if (actual is Task<VerifiedResourceStoreOwnership> && Interlocked.Exchange(ref once, 0) != 0) throw io;
        }
        var failed = rig.Keep(rig.Reads.ReadOriginalProjectContextsWithinSourceAsync(rig.Actor, 4, rig.Scope, Retain, rig.Token));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => failed); rig.Expect(error); rig.Expect(io);
        Assert.Contains(io, References(error)); Assert.False(rig.Reads.IsAcknowledgedOriginalReadRefusal(failed));
        Assert.False(rig.Store.IsAcknowledgedOriginalCommandRefusal(failed)); Assert.Equal(0, rig.MetadataReaderTasks);
    });

    public sealed class LinuxOriginalStoreFactAttribute : FactAttribute
    {
        public LinuxOriginalStoreFactAttribute()
        { if (!OperatingSystem.IsLinux()) Skip = "These owning controls require the actual Linux kernel/private file boundary; Windows remains separately unvalidated."; }
    }
    private static IEnumerable<Exception> References(Exception error)
    {
        yield return error;
        if (error is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var reference in References(child)) yield return reference;
    }
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = new Rig(); var errors = new List<Exception>();
        try { await rig.Initialize(); await body(rig); } catch (Exception cause) { errors.Add(cause); }
        await rig.Join(errors);
        // Keep the fixture/evidence path. No output/artifact cleanup under storage hold.
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Actual SQLite/Home store control failed; evidence retained at " + rig.Root, errors);
    }
    private sealed class ForeignObservation(ICanonicalProjectContextStoreObservation actual) : ICanonicalProjectContextStoreObservation
    {
        public AuthenticatedResourceActor Actor => actual.Actor;
        public ResourceStoreIdentity OriginalStoreIdentity => actual.OriginalStoreIdentity;
        public VerifiedResourceStoreOwnership OriginalStoreOwnership => actual.OriginalStoreOwnership;
        public IReadOnlyList<Conversation> Conversations => actual.Conversations;
        public IReadOnlyList<ContainerDefinition> Containers => actual.Containers;
        public bool HasMore => actual.HasMore;
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "canonical-store-control-" + Guid.NewGuid().ToString("N"));
        private readonly CancellationTokenSource _active = new(TimeSpan.FromSeconds(45));
        internal CancellationToken Token => _active.Token;
        internal void SetControlDeadline(TimeSpan timeout) => _active.CancelAfter(timeout);
        internal IAppPaths OriginalPaths = null!; internal SqliteDatabase Database = null!; internal FileHomeCoreStateStore Home = null!;
        internal HomeLocalProfileIdentity Profiles = null!; internal HomePermissionTrustService Permissions = null!;
        internal HomeLocalStoreOwnership Ownership = null!; internal CanonicalSqliteOriginalStoreOwner Store = null!;
        internal CanonicalProjectContextStoreReadOwner Reads = null!; internal ConversationRepository Conversations = null!;
        internal AuthenticatedResourceActor Actor = null!; internal ResourceStoreIdentity Identity = null!; internal Conversation Studio = null!;
        private readonly object _rawGate = new();
        private readonly List<Task> _raw = []; private readonly HashSet<Exception> _expected = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Task> _readers = new(ReferenceEqualityComparer.Instance);
        // Opt-in observation only: preserve every original Task, await, close and assertion.
        private readonly bool _diagnoseOriginalTasks = Environment.GetEnvironmentVariable("HAVEN_ORIGINAL_TASK_DIAGNOSTICS") == "1";
        private readonly Dictionary<Task, object> _diagnosticTasks = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _diagnosticSnapshots = [];
        private readonly List<string> _diagnosticErrors = [];
        private readonly object _diagnosticWriteGate = new();
        private System.Threading.Timer? _diagnosticTimer;
        private int _diagnosticSnapshotCount;
        private const int DiagnosticTaskLimit = 1024;
        // Opt-in SAME-owner field graph only. Never invoke source properties,
        // factories or callbacks; busy actual gates are observed and skipped.
        private readonly List<AutomationWriteGraph> _diagnosticAutomationGraphs = [];
        internal void RegisterOriginalAutomationGraph(AutomationWriteGraph actual)
        { if (_diagnoseOriginalTasks) lock (_rawGate) if (_diagnosticAutomationGraphs.Count < 8) _diagnosticAutomationGraphs.Add(actual); }
        private object SnapshotOriginalAutomationGraphs(List<Task> sameTasks)
        {
            const int nodeLimit = 128, edgeLimit = 1024, collectionLimit = 64, taskScanLimit = 1024;
            var ids = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            var pending = new Queue<object>(); var nodes = new List<object>(); var edges = new List<object>();
            var skipped = new List<object>();
            int Add(object actual)
            {
                if (ids.TryGetValue(actual, out var known)) return known;
                if (ids.Count >= nodeLimit) return 0;
                var id = ids.Count + 1; ids.Add(actual, id);
                nodes.Add(new { sourceObjectId = id, actualRuntimeType = actual.GetType().FullName,
                    actualTaskId = actual is Task task ? (int?)task.Id : null,
                    actualTaskStatus = actual is Task same ? same.Status.ToString() : null });
                if (actual is Task retained) sameTasks.Add(retained); else pending.Enqueue(actual); return id;
            }
            static object? Field(object actual, string name) => actual.GetType().GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)?.GetValue(actual);
            void Edge(object parent, string name, object? child)
            {
                if (child is null || edges.Count >= edgeLimit) return;
                var id = Add(child); if (id != 0) edges.Add(new { sourceObjectId = ids[parent], fieldOccurrence = name, targetObjectId = id });
            }
            foreach (var graph in _diagnosticAutomationGraphs) { Add(graph.Writer); Add(graph.Writes); }
            while (pending.TryDequeue(out var actual))
            {
                var type = actual.GetType(); var full = type.FullName;
                (string? Gate, string[] Fields) rule = full switch
                {
                    "Haven.Infrastructure.CanonicalAutomationDefinitionOriginalWriteOwner" => ("_gate", ["_commits", "_originals"]),
                    "Haven.Infrastructure.CanonicalAutomationDefinitionOriginalWriteOwner+Original" => (null, ["Raw", "Observation", "Source"]),
                    "Haven.Infrastructure.CanonicalAutomationDefinitionOriginalWriteOwner+Commit" => (null, ["Driver", "Inner", "Source", "Claim", "Pin", "Atomic", "Withdrawal", "NativeClose"]),
                    "Haven.Infrastructure.CanonicalSqliteOriginalSourceScope" => ("_gate", ["_raw", "_resources"]),
                    "Haven.Infrastructure.CanonicalSqliteOriginalStoreLease" => ("_gate", ["_source", "_operations", "_close"]),
                    "HavenOS.Home.Core.HomeCanonicalAutomationDefinitionWriteSource" => ("_gate", ["_active", "_pendingWithdrawals", "_close"]),
                    "HavenOS.Home.Core.HomeCanonicalAutomationDefinitionWriteSource+Claim" => ("_gate", ["_originalSettlementContext", "_settlement", "Acquisition", "_operations", "_contexts", "_acquisitionContext", "_originalPreparedReviewTask", "_entry", "_close"]),
                    "HavenOS.Home.Core.HomeCanonicalAutomationDefinitionWriteSource+Context" => (null, ["Errors", "_protocol"]),
                    "HavenOS.Home.Core.HomeOwnershipOriginalSourceCallbacks" => ("_originals", ["_originals"]),
                    "Haven.Application.CloudflareOriginalTaskLedger" => ("_sync", ["_tasks"]),
                    _ => (null, [])
                };
                if (rule.Fields.Length == 0) continue;
                var gate = rule.Gate is null ? null : Field(actual, rule.Gate);
                if (rule.Gate is not null && (gate is null || !Monitor.TryEnter(gate)))
                { skipped.Add(new { sourceObjectId = ids[actual], reason = "actual gate busy or absent" }); continue; }
                try
                {
                    foreach (var name in rule.Fields)
                    {
                        var child = Field(actual, name); if (child is null) continue;
                        var childType = child.GetType();
                        var generic = childType.IsGenericType ? childType.GetGenericTypeDefinition() : null;
                        if (childType == typeof(List<Task>))
                        {
                            // Scan only the actual whitelisted task list under its owner gate.
                            // Completed successes are counted without consuming graph nodes;
                            // every pending/faulted/cancelled occurrence retains its true index.
                            var scanned = 0; var omittedSuccessful = 0;
                            foreach (var task in (List<Task>)child)
                            {
                                if (scanned >= taskScanLimit) break;
                                var index = scanned++;
                                if (task.IsCompletedSuccessfully) { omittedSuccessful++; continue; }
                                Edge(actual, name + "[" + index + "]", task);
                            }
                            skipped.Add(new { sourceObjectId = ids[actual], fieldOccurrence = name,
                                scannedTaskOccurrences = scanned, omittedSuccessfulTaskOccurrences = omittedSuccessful,
                                reason = scanned < ((List<Task>)child).Count ? "actual task scan limit" : "successful actual tasks counted only" });
                        }
                        else if (generic == typeof(List<>) || generic == typeof(System.Runtime.CompilerServices.ConditionalWeakTable<,>))
                        {
                            // Only known runtime collections read under their SAME owner gate.
                            var index = 0;
                            foreach (var item in (System.Collections.IEnumerable)child)
                            {
                                if (index >= collectionLimit) { skipped.Add(new { sourceObjectId = ids[actual], reason = name + " collection limit" }); break; }
                                var value = generic == typeof(List<>) ? item : Field(item!, "value");
                                Edge(actual, name + "[" + index++ + "]", value);
                            }
                        }
                        else Edge(actual, name, child);
                    }
                }
                finally { if (gate is not null) Monitor.Exit(gate); }
            }
            return new { observationOnly = true, nodeLimit, edgeLimit, collectionLimit, taskScanLimit,
                limitReached = ids.Count == nodeLimit || edges.Count == edgeLimit,
                actualSourceObjects = nodes.ToArray(), retainedFieldOccurrences = edges.ToArray(), skippedActualGatesOrLimits = skipped.ToArray() };
        }
        private static object SnapshotOriginalTaskFaults(IEnumerable<Task> originals)
        {
            const int nodeLimit = 64, edgeLimit = 256, depthLimit = 8, childLimit = 16, textByteLimit = 16384;
            var ids = new Dictionary<Exception, int>(ReferenceEqualityComparer.Instance);
            var tasks = new HashSet<Task>(ReferenceEqualityComparer.Instance);
            var nodes = new List<object>(); var edges = new List<object>(); var roots = new List<object>();
            var textBytes = 0; var omitted = 0;
            string? Text(string? actual, int limit)
            {
                if (actual is null) return null;
                limit = Math.Min(limit, textByteLimit - textBytes); if (limit <= 0) return null;
                var count = Math.Min(actual.Length, limit);
                while (System.Text.Encoding.UTF8.GetByteCount(actual.AsSpan(0, count)) > limit) count /= 2;
                var captured = actual[..count]; textBytes += System.Text.Encoding.UTF8.GetByteCount(captured); return captured;
            }
            int Visit(Exception actual, int depth)
            {
                if (ids.TryGetValue(actual, out var same)) return same;
                if (ids.Count >= nodeLimit || depth > depthLimit) { omitted++; return 0; }
                var id = ids.Count + 1; ids.Add(actual, id); var type = actual.GetType();
                var standard = type == typeof(Exception) || type == typeof(InvalidOperationException) ||
                    type == typeof(ObjectDisposedException) || type == typeof(UnauthorizedAccessException) ||
                    type == typeof(IOException) || type == typeof(InvalidDataException) ||
                    type == typeof(OperationCanceledException) || type == typeof(TaskCanceledException) ||
                    type == typeof(TimeoutException) || type == typeof(ArgumentException) ||
                    type == typeof(ArgumentNullException) || type == typeof(ArgumentOutOfRangeException) ||
                    type == typeof(NotSupportedException);
                // Only exact known CLR bare types may expose virtual text getters.
                // Aggregate/unknown subclass Message, ToString and Flatten are never used.
                nodes.Add(new { actualExceptionObjectId = id, actualRuntimeType = type.FullName,
                    actualMessage = standard ? Text(actual.Message, 2048) : null,
                    actualStack = standard ? Text(actual.StackTrace, 4096) : null,
                    opaqueUnknownSubclass = !standard && type != typeof(AggregateException) });
                void Child(Exception child, string occurrence)
                {
                    if (edges.Count >= edgeLimit) { omitted++; return; }
                    var childId = Visit(child, depth + 1);
                    if (childId != 0) edges.Add(new { actualExceptionObjectId = id, innerOccurrence = occurrence, targetExceptionObjectId = childId });
                }
                if (type == typeof(AggregateException))
                {
                    var group = (AggregateException)actual; var count = Math.Min(group.InnerExceptions.Count, childLimit);
                    for (var i = 0; i < count; i++) Child(group.InnerExceptions[i], "InnerExceptions[" + i + "]");
                    omitted += group.InnerExceptions.Count - count;
                }
                else if (standard && actual.InnerException is { } inner) Child(inner, "InnerException");
                return id;
            }
            foreach (var task in originals)
            {
                if (!tasks.Add(task) || !task.IsFaulted) continue;
                // One immediate SAME Task.Exception observation; its CLR wrapper may be
                // materialized anew later. IDs describe actual refs in THIS snapshot only.
                var actual = task.Exception; if (actual is null) continue;
                var id = Visit(actual, 0);
                if (id != 0) roots.Add(new { actualTaskId = task.Id, actualExceptionObjectId = id });
            }
            return new { observationOnly = true, nodeLimit, edgeLimit, depthLimit, childLimit, textByteLimit,
                capturedTextBytes = textBytes, omittedNodesOrOccurrences = omitted,
                actualFaultedTaskRoots = roots.ToArray(), actualExceptionReferences = nodes.ToArray(), actualInnerOccurrences = edges.ToArray() };
        }
        private void ObserveOriginalTask(Task actual)
        {
            if (!_diagnoseOriginalTasks) return;
            try
            {
                lock (_rawGate)
                {
                    if (_diagnosticTasks.ContainsKey(actual) || _diagnosticTasks.Count >= DiagnosticTaskLimit) return;
                    var stack = new System.Diagnostics.StackTrace(1, false).ToString();
                    _diagnosticTasks.Add(actual, new
                    {
                        actualTaskId = actual.Id,
                        actualRuntimeType = actual.GetType().FullName,
                        firstObservedUtc = DateTimeOffset.UtcNow,
                        managedThreadId = Environment.CurrentManagedThreadId,
                        finiteRetentionStack = stack.Length <= 2048 ? stack : stack[..2048]
                    });
                }
            }
            catch (Exception cause) { KeepDiagnosticError(cause); }
        }
        private void KeepDiagnosticError(Exception cause)
        {
            lock (_rawGate)
                if (_diagnosticErrors.Count < 8) _diagnosticErrors.Add(cause.GetType().FullName + ": " + cause.Message);
        }
        private void SnapshotOriginalTasks(bool final)
        {
            if (!_diagnoseOriginalTasks) return;
            lock (_diagnosticWriteGate)
            {
                try
                {
                    object payload;
                    lock (_rawGate)
                    {
                        if (!final && _diagnosticSnapshotCount >= 12) return;
                        _diagnosticSnapshotCount++;
                        var sameTasks = _diagnosticTasks.Keys.ToList();
                        var sameOwnerGraph = SnapshotOriginalAutomationGraphs(sameTasks);
                        _diagnosticSnapshots.Add(new
                        {
                            observedUtc = DateTimeOffset.UtcNow,
                            final,
                            actualRetainedCount = _raw.Count,
                            actualTaskFaults = SnapshotOriginalTaskFaults(sameTasks),
                            actualTasks = _diagnosticTasks.Keys.Select(task => new
                            { actualTaskId = task.Id, actualStatus = task.Status.ToString() }).ToArray()
                        });
                        payload = new
                        {
                            fixtureRoot = Root,
                            observationOnly = true,
                            retentionTaskLimit = DiagnosticTaskLimit,
                            diagnosticLimitReached = _diagnosticTasks.Count == DiagnosticTaskLimit,
                            originalTasks = _diagnosticTasks.Values.ToArray(),
                            sameCohortSnapshots = _diagnosticSnapshots.ToArray(),
                            diagnosticErrors = _diagnosticErrors.ToArray(),
                            actualAutomationOwnerGraph = sameOwnerGraph
                        };
                    }
                    File.WriteAllText(Path.Combine(Root, "original-task-diagnostic.json"), JsonSerializer.Serialize(payload));
                }
                catch (Exception cause) { KeepDiagnosticError(cause); }
            }
        }
        internal int MetadataReaderTasks { get { lock (_rawGate) return _readers.Count; } }
        internal void Scope(Action body) => body();
        internal void Retain(Task actual) { lock (_rawGate) { _raw.Add(actual); if (actual is Task<SqliteDataReader>) _readers.Add(actual); } ObserveOriginalTask(actual); }
        internal Task<T> Keep<T>(Task<T> actual) { Retain(actual); return actual; }
        internal Task Keep(Task actual) { Retain(actual); return actual; }
        internal void Expect(Exception cause) { lock (_rawGate) foreach (var reference in References(cause)) _expected.Add(reference); }
        internal async Task Initialize()
        {
            Directory.CreateDirectory(Root);
            if (_diagnoseOriginalTasks)
                _diagnosticTimer = new System.Threading.Timer(_ => SnapshotOriginalTasks(final: false), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var paths = new Paths(Root); OriginalPaths = paths; Database = new(paths); await Keep(Database.InitializeAsync(Token));
            foreach (var path in new[] { paths.DatabasePath, paths.DatabasePath + "-wal", paths.DatabasePath + "-shm" })
                if (OperatingSystem.IsLinux() && File.Exists(path)) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Home = new(Path.Combine(Root, "home.json")); Profiles = new(Home, new OperatingSystemPrincipalSource());
            Actor = (await Keep(Profiles.GetCurrentAsync(Token).AsTask()))!;
            Permissions = new(Home, (target, action) => target == "9to1.home.local-profile" && action == "home.profile.importStore"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
            Store = new(Database, paths, Profiles);
            var canonical = new CanonicalSqliteOriginalStoreEvidenceProvider(Store, "canonical.sqlite");
            var legacy = new CanonicalSqliteOriginalStoreEvidenceProvider(Store, "legacy.saved-agents");
            Ownership = new(Home, Profiles, new HomeLocalStoreEvidenceRegistry([canonical, legacy]), Permissions);
            Conversations = new(Database); var containers = new ContainerRepository(Database, paths);
            Reads = new(Store, Database, Conversations, containers, new HomeResourceStoreOwnershipAuthority(Ownership, Profiles));
            Identity = await Keep(Store.GetStoreIdentityWithinOriginalSourceAsync(Actor, Scope, Retain, Token));
            var now = DateTimeOffset.UtcNow;
            foreach (var (mode, kind, name) in new[] { (HavenMode.Tasks, ConversationKind.Task, "task-a"),
                (HavenMode.Tasks, ConversationKind.Task, "task-b"), (HavenMode.Studio, ConversationKind.StudioChat, "studio") })
            {
                var container = new ContainerDefinition(Guid.NewGuid(), mode, name, Path.Combine(Root, name), "actual saved project reference metadata", "existing instructions", now, now);
                await Keep(containers.UpsertAsync(container, Token));
                var conversation = new Conversation(Guid.NewGuid(), mode, kind, name, container.Id, null, false, false, now, now);
                Assert.True(await Keep(Conversations.TryCreateConversationAsync(conversation, Token)));
                if (mode == HavenMode.Studio) Studio = conversation;
            }
        }
        internal async Task Import(string kind)
        {
            var authorization = await Keep(Ownership.RequestImportAsync(Actor, kind, Identity.StoreId.ToString("D"), Actor.AuthenticationRevision, Token));
            Assert.Equal(HomePermissionRequestState.PendingApproval, authorization.State);
            Assert.True((await Keep(Permissions.DecideAsync(authorization.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token))).Succeeded);
            await Keep(Ownership.CompleteImportAsync(authorization.RequestId, Token));
        }
        internal async Task Join(List<Exception> errors)
        {
            if (Store is not null)
            {
                Task? close = null;
                try { close = Store.CloseAndDrainAsync(); await close; }
                catch (Exception cause) { AddUnexpected(close?.Exception ?? cause, errors); }
            }
            Task[] sources; lock (_rawGate) sources = _raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var raw in sources)
                try { await raw; } catch (Exception cause) { AddUnexpected(raw.Exception ?? cause, errors); }
            if (Database is not null)
                try { await using var connection = await Database.OpenAsync(CancellationToken.None); SqliteConnection.ClearPool(connection); }
                catch (Exception cause) { errors.Add(cause); }
            if (_diagnosticTimer is { } timer) await timer.DisposeAsync();
            SnapshotOriginalTasks(final: true);
            _active.Dispose();
        }
        internal void AddUnexpected(Exception error, List<Exception> errors)
        {
            lock (_rawGate) if (_expected.Contains(error)) return;
            if (error is AggregateException { InnerExceptions.Count: > 0 } group)
            { foreach (var cause in group.InnerExceptions) AddUnexpected(cause, errors); return; }
            if (!errors.Any(prior => ReferenceEquals(prior, error))) errors.Add(error);
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
