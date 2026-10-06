using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure;

/// <summary>Source-authenticated recovery journal on the SAME configured personal SQLite store.
/// Missing key, insecure store, uncertain claim and unsupported effect boundaries fail closed.</summary>
public sealed partial class SqliteTaskRunColdRecoveryJournal(SqliteDatabase database, IAppPaths paths,
    HostLocalTaskActorSource actors, IDatabaseMaintenance maintenance,
    Func<ITaskRunOriginalColdCaptureSource> originalCaptureSource)
    : ITaskRunColdRecoveryJournal, ITaskRunColdContextAuthority, ITaskRunColdAuthoritySourceScope, ITaskRunColdAcceptedBoundarySource
{
    private sealed record CallerSourceFrame(TaskRunColdOriginalSourceScope Original, TaskRunColdOriginalSourceScope Scoped);
    private readonly AsyncLocal<CallerSourceFrame?> _callerSource = new();
    private TaskRunColdOriginalSourceScope OriginalSources(TaskRunColdOriginalSourceScope actual) =>
        _callerSource.Value is { } frame && ReferenceEquals(frame.Original, actual) ? frame.Scoped : actual;
    public bool HasOriginalTaskActorSource(IAuthenticatedResourceActorSource sameSource) => ReferenceEquals(actors, sameSource);
    private async Task WithinOriginalSourceAsync(TaskRunColdOriginalSourceScope original, Action<Action> caller,
        Action<Task> retain, Func<Task> actualBody)
    {
        var prior = _callerSource.Value;
        _callerSource.Value = new(original, original.WithinOriginalCaller(caller, retain));
        try
        {
            await AwaitActualAsync(OriginalSources(original), actualBody).ConfigureAwait(false);
        }
        finally { _callerSource.Value = prior; }
    }
    public Task ValidateOriginalClaimWithinSourceAsync(ITaskRunColdJournalClaim claim, TaskExecutionSnapshot expected,
        Action<Action> caller, Action<Task> retain, CancellationToken token) =>
        WithinOriginalSourceAsync(RequireClaim(claim).Entry.Sources, caller, retain, () => ValidateOriginalClaimAsync(claim, expected, token));
    public ValueTask ValidateOriginalContextWithinSourceAsync(ITaskRunColdContextLease context, TaskExecutionSnapshot expected,
        Action<Action> caller, Action<Task> retain, CancellationToken token) =>
        new(WithinOriginalSourceAsync(RequireContext(context).Claim.Entry.Sources, caller, retain, () => ValidateOriginalAsync(context, expected, token).AsTask()));
    public ValueTask ValidateOriginalClosedContextWithinSourceAsync(ITaskRunColdContextLease context, ITaskRunColdJournalAcknowledgment ack,
        Action<Action> caller, Action<Task> retain, CancellationToken token) =>
        new(WithinOriginalSourceAsync(RequireContext(context).Claim.Entry.Sources, caller, retain, () => ValidateOriginalClosedContextAsync(context, ack, token).AsTask()));
    public Task ValidateOriginalAcknowledgmentWithinSourceAsync(ITaskRunColdJournalAcknowledgment ack,
        Action<Action> caller, Action<Task> retain, CancellationToken token) =>
        WithinOriginalSourceAsync(RequireAcknowledgment(ack).Claim.Entry.Sources, caller, retain, () => ValidateOriginalAcknowledgmentAsync(ack, token));
    private ContextLease RequireContext(ITaskRunColdContextLease context) => context is ContextLease actual && ReferenceEquals(actual.Owner, this)
        ? actual : throw new UnauthorizedAccessException("The actual SAME private context lease is required.");

    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private bool _initialized;

    private sealed class Entry(SqliteTaskRunColdRecoveryJournal owner, string payload, byte[] tag,
        TaskRunColdOriginalSourceScope sources) : ITaskRunColdJournalEntry
    {
        internal readonly SqliteTaskRunColdRecoveryJournal Owner = owner;
        internal readonly string Payload = payload;
        internal readonly byte[] Tag = tag.ToArray();
        internal readonly TaskRunColdOriginalSourceScope Sources = sources;
        internal Claim? Claim;
        // Callers receive only a detached observation. Mutation cannot alter the privately
        // authenticated original or any later validator's canonical basis.
        public TaskRunColdCapsule Capsule => UnifiedPersistenceJson.Read<TaskRunColdCapsule>(Payload);
    }
    private sealed class Claim(SqliteTaskRunColdRecoveryJournal owner, Entry entry, string expected, Guid id,
        SqliteConnection commitConnection, byte[] key)
        : ITaskRunColdJournalClaim
    {
        internal readonly SqliteTaskRunColdRecoveryJournal Owner = owner;
        internal readonly Entry Entry = entry;
        internal readonly string Expected = expected;
        internal readonly Guid Id = id;
        internal Acknowledgment? Acknowledgment;
        internal readonly SqliteConnection CommitConnection = commitConnection;
        internal readonly byte[] Key = key;
        internal Task? Close;
        public ITaskRunColdJournalEntry OriginalEntry => Entry;
        public TaskExecutionSnapshot OriginalExpected => UnifiedPersistenceJson.Read<TaskExecutionSnapshot>(Expected);
        public Guid ClaimId => Id;
        public ValueTask DisposeAsync()
        {
            lock (this)
            {
                if (Close is not null) return new(Close);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Close = CloseOriginalAsync(start.Task); start.SetResult(); return new(Close);
            }
        }
        private async Task CloseOriginalAsync(Task start)
        {
            await start.ConfigureAwait(false);
            try { await SqliteTaskRunColdRecoveryJournal.CloseOriginalAsync(Entry.Sources, CommitConnection).ConfigureAwait(false); }
            finally { CryptographicOperations.ZeroMemory(Key); }
        }
    }
    private sealed class Acknowledgment(SqliteTaskRunColdRecoveryJournal owner, Claim claim, string acknowledged)
        : ITaskRunColdJournalAcknowledgment
    {
        internal readonly SqliteTaskRunColdRecoveryJournal Owner = owner;
        internal readonly Claim Claim = claim;
        internal readonly string Payload = acknowledged;
        public ITaskRunColdJournalClaim OriginalClaim => Claim;
        public TaskExecutionSnapshot AcknowledgedTask => UnifiedPersistenceJson.Read<TaskExecutionSnapshot>(Payload);
    }
    private sealed class ContextLease(SqliteTaskRunColdRecoveryJournal owner, Claim claim,
        AuthenticatedResourceActor actor, NativePersonalTaskRecoveryStore store) : ITaskRunColdContextLease
    {
        internal readonly SqliteTaskRunColdRecoveryJournal Owner = owner;
        internal readonly Claim Claim = claim;
        internal readonly NativePersonalTaskRecoveryStore Store = store;
        internal bool Closed;
        internal Task? Close;
        public ITaskRunColdJournalClaim OriginalClaim => Claim;
        public AuthenticatedResourceActor CurrentActor { get; } = actor;
        public ValueTask DisposeAsync()
        {
            lock (this)
            {
                if (Close is not null) return new(Close);
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Close = CloseOriginalAsync(start.Task); start.SetResult(); return new(Close);
            }
        }
        private async Task CloseOriginalAsync(Task start)
        { await start.ConfigureAwait(false); Claim.Entry.Sources.Invoke(() => { Store.Dispose(); Closed = true; return true; }); }
    }

    public bool HasOriginalComposition(SqliteDatabase sameDatabase, IAppPaths samePaths,
        HostLocalTaskActorSource sameActors) => ReferenceEquals(database, sameDatabase)
        && ReferenceEquals(paths, samePaths) && ReferenceEquals(actors, sameActors);

    private async Task InitializeOriginalAsync(TaskRunColdOriginalSourceScope sources, CancellationToken token)
    {
        await AwaitActualAsync(sources, () => _migrationGate.WaitAsync(token)).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            using var store = sources.Invoke(() => NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath));
            // Actual maintained backup/integrity owner runs before additive migration.
            await AwaitActualAsync(sources, () => maintenance.PrepareForMigrationAsync(TaskRunColdRecoveryMigration.Version, token)).ConfigureAwait(false);
            var connection = await OpenOriginalAsync(sources, token).ConfigureAwait(false);
            try
            {
                using var integrity = sources.Invoke(connection.CreateCommand);
                integrity.CommandText = "PRAGMA integrity_check;";
                if (Convert.ToString(await AwaitActualAsync(sources, () => integrity.ExecuteScalarAsync(token)).ConfigureAwait(false)) != "ok")
                    throw new InvalidDataException("The actual personal database failed integrity check.");
                using var query = sources.Invoke(connection.CreateCommand);
                query.CommandText = "SELECT EXISTS(SELECT 1 FROM schema_migrations WHERE version=$version);";
                query.Parameters.AddWithValue("$version", TaskRunColdRecoveryMigration.Version);
                var exists = Convert.ToInt64(await AwaitActualAsync(sources, () => query.ExecuteScalarAsync(token)).ConfigureAwait(false)) == 1;
                if (!exists)
                {
                    var transaction = sources.Invoke(() => connection.BeginTransaction(deferred: false));
                    try
                    {
                        using var migration = sources.Invoke(connection.CreateCommand); migration.Transaction = transaction;
                        migration.CommandText = TaskRunColdRecoveryMigration.Sql;
                        await AwaitActualAsync(sources, () => migration.ExecuteNonQueryAsync(token)).ConfigureAwait(false);
                        migration.CommandText = "INSERT INTO schema_migrations(version,applied_at) VALUES($version,$now);";
                        migration.Parameters.AddWithValue("$version", TaskRunColdRecoveryMigration.Version);
                        migration.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
                        await AwaitActualAsync(sources, () => migration.ExecuteNonQueryAsync(token)).ConfigureAwait(false);
                        await AwaitActualAsync(sources, () => transaction.CommitAsync(token)).ConfigureAwait(false);
                    }
                    finally { sources.Invoke(() => { transaction.Dispose(); return true; }); }
                }
                sources.Invoke(() => { store.Validate(); return true; });
                _initialized = true;
            }
            finally { await CloseOriginalAsync(sources, connection).ConfigureAwait(false); }
        }
        finally { sources.Invoke(() => { _migrationGate.Release(); return true; }); }
    }

    public async Task PublishOriginalAsync(TaskRunOriginalColdCapture sameCapture,
        TaskRunColdOriginalSourceScope sources, CancellationToken token)
    {
        var source = sources.Invoke(originalCaptureSource);
        if (!sources.Invoke(() => source.IsIssuedOriginalColdCapture(sameCapture)))
            throw new UnauthorizedAccessException("Only the configured actual Chat's safe capture may publish a capsule.");
        await AwaitActualAsync(sources, () => source.ValidateOriginalColdCaptureAsync(sameCapture, token)).ConfigureAwait(false);
        var capsule = sameCapture.Capsule;
        TaskRunColdRecoveryBoundary.DemandRestorableBoundary(capsule, capsule.AcknowledgedTask);
        await InitializeOriginalAsync(sources, token).ConfigureAwait(false);
        using var store = sources.Invoke(() => NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath));
        var key = sources.Invoke(() => store.ReadOrCreateAuthenticationKey(create: true));
        try
        {
            var payload = UnifiedPersistenceJson.Write(capsule);
            if (Encoding.UTF8.GetByteCount(payload) > 1024 * 1024)
                throw new InvalidOperationException("The bounded original capsule requires manual inspection; no truncated input is published.");
            var tag = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
            var current = await ReadTaskAsync(sources, capsule.AcknowledgedTask.TaskId, token).ConfigureAwait(false);
            if (UnifiedPersistenceJson.Write(current) != UnifiedPersistenceJson.Write(capsule.AcknowledgedTask))
                throw new InvalidOperationException("The actual Task changed before its safe capsule write.");
            await DemandInputAsync(sources, capsule, token).ConfigureAwait(false);
            await DemandActorAsync(sources, capsule.AcknowledgedTask.OwnerBinding!, token).ConfigureAwait(false);
            var connection = await OpenOriginalAsync(sources, token).ConfigureAwait(false);
            try
            {
                using var command = sources.Invoke(connection.CreateCommand);
                command.CommandText = """
                    INSERT INTO task_run_recovery_journal(capsule_id,task_id,context_id,execution_id,expected_revision,capsule_json,authentication_tag,state_authentication_tag,created_at)
                    SELECT $capsule,$task,$context,$run,$revision,$payload,$tag,$stateTag,$now
                    WHERE EXISTS(SELECT 1 FROM task_execution_state WHERE task_id=$task AND context_id=$context
                        AND execution_id=$run AND persistence_revision=$revision AND payload_json=$expected);
                    """;
                AddIdentity(command, capsule.AcknowledgedTask);
                command.Parameters.AddWithValue("$capsule", capsule.CapsuleId.ToString());
                command.Parameters.AddWithValue("$payload", payload); command.Parameters.AddWithValue("$tag", tag);
                command.Parameters.AddWithValue("$stateTag", StateTag(key, payload, 0, null, null, null));
                command.Parameters.AddWithValue("$now", capsule.CapturedAt.ToString("O"));
                command.Parameters.AddWithValue("$expected", UnifiedPersistenceJson.Write(capsule.AcknowledgedTask));
                if (await AwaitActualAsync(sources, () => command.ExecuteNonQueryAsync(token)).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("The SAME safe task capsule was not acknowledged.");
                sources.Invoke(() => { store.Validate(); return true; });
            }
            finally { await CloseOriginalAsync(sources, connection).ConfigureAwait(false); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public async Task<ITaskRunColdJournalEntry?> ReadOriginalAsync(Guid taskId, Guid expectedRunId,
        TaskRunColdOriginalSourceScope sources, CancellationToken token)
    {
        await InitializeOriginalAsync(sources, token).ConfigureAwait(false);
        using var store = sources.Invoke(() => NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath));
        var key = sources.Invoke(() => store.ReadOrCreateAuthenticationKey(create: false));
        try
        {
            var connection = await OpenOriginalAsync(sources, token).ConfigureAwait(false);
            try
            {
                using var command = sources.Invoke(connection.CreateCommand);
                command.CommandText = "SELECT capsule_json,authentication_tag,claim_state,claim_id,acknowledged_json,terminal_json,state_authentication_tag FROM task_run_recovery_journal WHERE task_id=$task AND execution_id=$run;";
                command.Parameters.AddWithValue("$task", taskId.ToString()); command.Parameters.AddWithValue("$run", expectedRunId.ToString());
                var reader = await AwaitActualAsync(sources, () => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
                string payload; byte[] tag;
                try
                {
                    if (!await AwaitActualAsync(sources, () => reader.ReadAsync(token)).ConfigureAwait(false)) return null;
                    payload = reader.GetString(0); tag = (byte[])reader.GetValue(1);
                    if (Encoding.UTF8.GetByteCount(payload) > 1024 * 1024 || tag.Length != 32
                        || !CryptographicOperations.FixedTimeEquals(tag, HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))))
                        throw new UnauthorizedAccessException("The original capsule provenance authentication failed.");
                    DemandStateAuthentication(reader, key, payload);
                    if (reader.GetInt32(2) != 0) throw new InvalidOperationException("The original recovery claim is retained/consumed; a crash, timeout or missing process never permits replay.");
                }
                finally { await AwaitActualAsync(sources, () => reader.DisposeAsync().AsTask()).ConfigureAwait(false); }
                var entry = new Entry(this, payload, tag, sources);
                var current = await ReadTaskAsync(sources, taskId, token).ConfigureAwait(false);
                var capsule = entry.Capsule;
                if (current.ExecutionId != expectedRunId || UnifiedPersistenceJson.Write(current) != UnifiedPersistenceJson.Write(capsule.AcknowledgedTask))
                    throw new InvalidOperationException("The source-attested Task/run/revision no longer matches current state.");
                TaskRunColdRecoveryBoundary.DemandRestorableBoundary(capsule, current);
                await DemandInputAsync(sources, capsule, token).ConfigureAwait(false);
                await DemandActorAsync(sources, capsule.AcknowledgedTask.OwnerBinding!, token).ConfigureAwait(false);
                sources.Invoke(() => { store.Validate(); return true; });
                return entry;
            }
            finally { await CloseOriginalAsync(sources, connection).ConfigureAwait(false); }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public bool IsIssuedOriginalEntry(ITaskRunColdJournalEntry sameEntry) => sameEntry is Entry entry && ReferenceEquals(entry.Owner, this);
    private Entry RequireEntry(ITaskRunColdJournalEntry entry) => entry is Entry actual && ReferenceEquals(actual.Owner, this)
        ? actual : throw new UnauthorizedAccessException("The SAME journal's private entry is required.");
    private Claim RequireClaim(ITaskRunColdJournalClaim claim) => claim is Claim actual && ReferenceEquals(actual.Owner, this)
        && ReferenceEquals(actual.Entry.Claim, actual) ? actual : throw new UnauthorizedAccessException("No same source-issued original claim exists.");
    private Acknowledgment RequireAcknowledgment(ITaskRunColdJournalAcknowledgment ack) => ack is Acknowledgment actual
        && ReferenceEquals(actual.Owner, this) && ReferenceEquals(actual.Claim.Acknowledgment, actual)
        ? actual : throw new UnauthorizedAccessException("No original claim/task CAS acknowledgment exists.");

    public async Task<ITaskRunColdJournalClaim> ClaimOriginalAsync(ITaskRunColdJournalEntry sameEntry,
        TaskExecutionSnapshot actualExpected, CancellationToken token)
    {
        var entry = RequireEntry(sameEntry); var sources = OriginalSources(entry.Sources);
        if (UnifiedPersistenceJson.Write(actualExpected) != UnifiedPersistenceJson.Write(entry.Capsule.AcknowledgedTask))
            throw new InvalidOperationException("The actual expected checkpoint differs from the authenticated original.");
        await DemandInputAsync(sources, entry.Capsule, token).ConfigureAwait(false);
        await DemandActorAsync(sources, actualExpected.OwnerBinding!, token).ConfigureAwait(false);
        var id = Guid.NewGuid();
        using var store = sources.Invoke(() => NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath));
        var key = sources.Invoke(() => store.ReadOrCreateAuthenticationKey(create: false));
        SqliteConnection? commitConnection = null; var issued = false;
        try
        {
            await ChangeClaimAsync(entry, "UPDATE task_run_recovery_journal SET claim_id=$claim,claim_state=1,state_authentication_tag=$nextStateTag WHERE capsule_id=$capsule AND claim_state=0 AND capsule_json=$payload AND authentication_tag=$tag AND state_authentication_tag=$previousStateTag AND EXISTS(SELECT 1 FROM task_execution_state WHERE task_id=$task AND persistence_revision=$revision AND payload_json=$expected);",
                id, actualExpected, null, StateTag(key, entry.Payload, 0, null, null, null), StateTag(key, entry.Payload, 1, id.ToString(), null, null), token).ConfigureAwait(false);
            // Actual connection acquisition precedes the pure owner pin. No key/file/actor/
            // context read is needed inside the later finite transaction/CAS.
            commitConnection = await OpenOriginalAsync(sources, token).ConfigureAwait(false);
            var claim = new Claim(this, entry, UnifiedPersistenceJson.Write(actualExpected), id, commitConnection, key);
            lock (entry) { if (entry.Claim is not null) throw new InvalidOperationException("An actual claim is already retained."); entry.Claim = claim; }
            issued = true; return claim;
        }
        finally
        {
            if (!issued)
            {
                try { if (commitConnection is not null) await CloseOriginalAsync(sources, commitConnection).ConfigureAwait(false); }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
        }
    }

    public async Task ValidateOriginalClaimAsync(ITaskRunColdJournalClaim sameClaim, TaskExecutionSnapshot actualExpected, CancellationToken token)
    {
        var claim = RequireClaim(sameClaim);
        if (UnifiedPersistenceJson.Write(actualExpected) != claim.Expected) throw new InvalidOperationException("The original expected claim basis changed.");
        await DemandClaimRowAsync(claim, state: 1, token).ConfigureAwait(false);
        var current = await ReadTaskAsync(OriginalSources(claim.Entry.Sources), actualExpected.TaskId, token).ConfigureAwait(false);
        if (UnifiedPersistenceJson.Write(current) != claim.Expected) throw new InvalidOperationException("The actual Task changed during the one-use claim.");
        await DemandInputAsync(OriginalSources(claim.Entry.Sources), claim.Entry.Capsule, token).ConfigureAwait(false);
        await DemandActorAsync(OriginalSources(claim.Entry.Sources), actualExpected.OwnerBinding!, token).ConfigureAwait(false);
    }

    public async Task<ITaskRunColdJournalAcknowledgment> CommitOriginalAsync(ITaskRunColdJournalClaim sameClaim,
        TaskExecutionSnapshot proposedNext, CancellationToken token)
    {
        var claim = RequireClaim(sameClaim); var sources = OriginalSources(claim.Entry.Sources); var expected = claim.OriginalExpected;
        var nextOwner = proposedNext.OwnerBinding ?? throw new UnauthorizedAccessException("No next owner observation exists.");
        var previousOwner = expected.OwnerBinding!;
        if (nextOwner.TaskId != expected.TaskId || nextOwner.ContextId != expected.ContextId || nextOwner.ExecutionId != expected.ExecutionId
            || nextOwner.ActorId != previousOwner.ActorId || nextOwner.ProfileId != previousOwner.ProfileId
            || nextOwner.AccountId != previousOwner.AccountId || nextOwner.OrganisationId != previousOwner.OrganisationId
            || string.IsNullOrWhiteSpace(nextOwner.AuthenticationRevision) || string.IsNullOrWhiteSpace(nextOwner.AuthorizationReceiptReference)
            || proposedNext.PersistenceRevision != checked(expected.PersistenceRevision + 1)
            || UnifiedPersistenceJson.Write(proposedNext with { OwnerBinding = previousOwner,
                PersistenceRevision = expected.PersistenceRevision, UpdatedAt = expected.UpdatedAt }) != claim.Expected)
            throw new InvalidOperationException("Cold CAS changes only the exact SAME owner activation/revision, never work/history/IDs.");
        if (claim.Close is not null) throw new InvalidOperationException("The original CAS connection is closing/closed.");
        var connection = claim.CommitConnection;
        var transaction = sources.Invoke(() => connection.BeginTransaction(deferred: false));
        try
        {
            using var taskWrite = sources.Invoke(connection.CreateCommand); taskWrite.Transaction = transaction;
            taskWrite.CommandText = "UPDATE task_execution_state SET payload_json=$next,persistence_revision=$nextRevision,updated_at=$now WHERE task_id=$task AND context_id=$context AND execution_id=$run AND persistence_revision=$revision AND payload_json=$expected;";
            AddIdentity(taskWrite, expected); taskWrite.Parameters.AddWithValue("$expected", claim.Expected);
            var next = UnifiedPersistenceJson.Write(proposedNext);
            taskWrite.Parameters.AddWithValue("$next", next); taskWrite.Parameters.AddWithValue("$nextRevision", proposedNext.PersistenceRevision);
            taskWrite.Parameters.AddWithValue("$now", proposedNext.UpdatedAt.ToString("O"));
            if (await AwaitActualAsync(sources, () => taskWrite.ExecuteNonQueryAsync(token)).ConfigureAwait(false) != 1)
                throw new TaskExecutionRevisionConflictException(expected.TaskId, expected.PersistenceRevision, proposedNext.PersistenceRevision);
            using var claimWrite = sources.Invoke(connection.CreateCommand); claimWrite.Transaction = transaction;
            claimWrite.CommandText = "UPDATE task_run_recovery_journal SET claim_state=2,acknowledged_json=$next,state_authentication_tag=$nextStateTag WHERE capsule_id=$capsule AND claim_id=$claim AND claim_state=1 AND capsule_json=$payload AND authentication_tag=$tag AND state_authentication_tag=$previousStateTag;";
            AddEntry(claimWrite, claim.Entry); claimWrite.Parameters.AddWithValue("$claim", claim.Id.ToString()); claimWrite.Parameters.AddWithValue("$next", next);
            claimWrite.Parameters.AddWithValue("$previousStateTag", StateTag(claim.Key, claim.Entry.Payload, 1, claim.Id.ToString(), null, null));
            claimWrite.Parameters.AddWithValue("$nextStateTag", StateTag(claim.Key, claim.Entry.Payload, 2, claim.Id.ToString(), next, null));
            if (await AwaitActualAsync(sources, () => claimWrite.ExecuteNonQueryAsync(token)).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The SAME one-use journal claim did not acknowledge its task CAS.");
            await AwaitActualAsync(sources, () => transaction.CommitAsync(token)).ConfigureAwait(false);
            var acknowledgment = new Acknowledgment(this, claim, next); claim.Acknowledgment = acknowledgment;
            return acknowledgment;
        }
        finally
        {
            sources.Invoke(() => { transaction.Dispose(); return true; });
        }
    }

    public bool IsIssuedOriginalAcknowledgment(ITaskRunColdJournalAcknowledgment sameAcknowledgment, ITaskRunColdJournalClaim sameClaim) =>
        sameAcknowledgment is Acknowledgment ack && sameClaim is Claim claim && ReferenceEquals(ack.Owner, this)
        && ReferenceEquals(claim.Owner, this) && ReferenceEquals(ack.Claim, claim) && ReferenceEquals(claim.Acknowledgment, ack);

    public async Task ValidateOriginalAcknowledgmentAsync(ITaskRunColdJournalAcknowledgment sameAcknowledgment, CancellationToken token)
    {
        var ack = RequireAcknowledgment(sameAcknowledgment); var claim = ack.Claim;
        await DemandClaimRowAsync(claim, state: 2, token).ConfigureAwait(false);
        var current = await ReadTaskAsync(OriginalSources(claim.Entry.Sources), ack.AcknowledgedTask.TaskId, token).ConfigureAwait(false);
        if (UnifiedPersistenceJson.Write(current) != ack.Payload) throw new InvalidOperationException("The acknowledged cold binding is no longer current.");
        await DemandInputAsync(OriginalSources(claim.Entry.Sources), claim.Entry.Capsule, token).ConfigureAwait(false);
        await DemandActorAsync(OriginalSources(claim.Entry.Sources), current.OwnerBinding!, token).ConfigureAwait(false);
    }

    public async Task RecordOriginalTerminalAsync(ITaskRunColdJournalAcknowledgment sameAcknowledgment,
        TaskExecutionSnapshot actualTerminal, CancellationToken token)
    {
        var ack = RequireAcknowledgment(sameAcknowledgment); var expected = ack.AcknowledgedTask;
        if (actualTerminal.TaskId != expected.TaskId || actualTerminal.ContextId != expected.ContextId
            || actualTerminal.ExecutionId != expected.ExecutionId || actualTerminal.OwnerBinding != expected.OwnerBinding
            || actualTerminal.PersistenceRevision < expected.PersistenceRevision)
            throw new InvalidOperationException("Another terminal Task/Run cannot consume this original claim.");
        var current = await ReadTaskAsync(OriginalSources(ack.Claim.Entry.Sources), expected.TaskId, token).ConfigureAwait(false);
        if (UnifiedPersistenceJson.Write(current) != UnifiedPersistenceJson.Write(actualTerminal))
            throw new InvalidOperationException("The SAME actual business terminal acknowledgment changed.");
        var sources = ack.Claim.Entry.Sources;
        using var store = sources.Invoke(() => NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath));
        var key = sources.Invoke(() => store.ReadOrCreateAuthenticationKey(create: false));
        try
        {
            var terminal = UnifiedPersistenceJson.Write(actualTerminal);
            await ChangeClaimAsync(ack.Claim.Entry,
                "UPDATE task_run_recovery_journal SET claim_state=3,terminal_json=$terminal,state_authentication_tag=$nextStateTag WHERE capsule_id=$capsule AND claim_id=$claim AND claim_state=2 AND capsule_json=$payload AND authentication_tag=$tag AND state_authentication_tag=$previousStateTag;",
                ack.Claim.Id, actualTerminal, terminal, StateTag(key, ack.Claim.Entry.Payload, 2, ack.Claim.Id.ToString(), ack.Payload, null),
                StateTag(key, ack.Claim.Entry.Payload, 3, ack.Claim.Id.ToString(), ack.Payload, terminal), token).ConfigureAwait(false);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public async Task ValidateOriginalRestoredInputAsync(ITaskRunColdJournalAcknowledgment sameAcknowledgment,
        TaskExecutionSnapshot actualCurrent, TaskRunColdOriginalSourceScope currentSources, CancellationToken token)
    {
        var ack = RequireAcknowledgment(sameAcknowledgment); var original = ack.AcknowledgedTask;
        if (actualCurrent.TaskId != original.TaskId || actualCurrent.ContextId != original.ContextId
            || actualCurrent.ExecutionId != original.ExecutionId || actualCurrent.OwnerBinding != original.OwnerBinding
            || actualCurrent.PersistenceRevision < original.PersistenceRevision)
            throw new InvalidOperationException("The current restored input no longer belongs to its genuine cold owner/Task/run.");
        // Only the actual new invocation provides current callback/task custody. Historical
        // prepared operation Tasks are never relabeled or revived for later body reads.
        await DemandClaimRowAsync(ack.Claim, state: 2, token, currentSources).ConfigureAwait(false);
        var current = await ReadTaskAsync(currentSources, original.TaskId, token).ConfigureAwait(false);
        if (UnifiedPersistenceJson.Write(current) != UnifiedPersistenceJson.Write(actualCurrent))
            throw new InvalidOperationException("The actual same-run input basis changed during restored body validation.");
        await DemandInputAsync(currentSources, ack.Claim.Entry.Capsule, token).ConfigureAwait(false);
        await DemandActorAsync(currentSources, current.OwnerBinding!, token).ConfigureAwait(false);
    }

    public async ValueTask<ITaskRunColdContextLease> AuthorizeOriginalAsync(ITaskRunColdJournalClaim sameClaim,
        TaskExecutionSnapshot actualExpected, CancellationToken token)
    {
        var claim = RequireClaim(sameClaim);
        await ValidateOriginalClaimAsync(claim, actualExpected, token).ConfigureAwait(false);
        var store = OriginalSources(claim.Entry.Sources).Invoke(() => NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath));
        try
        {
            var actor = await DemandActorAsync(OriginalSources(claim.Entry.Sources), actualExpected.OwnerBinding!, token).ConfigureAwait(false);
            OriginalSources(claim.Entry.Sources).Invoke(() => { store.Validate(); return true; });
            return new ContextLease(this, claim, actor, store);
        }
        catch { OriginalSources(claim.Entry.Sources).Invoke(() => { store.Dispose(); return true; }); throw; }
    }
    public bool IsIssuedOriginal(ITaskRunColdContextLease sameLease, ITaskRunColdJournalClaim sameClaim) =>
        sameLease is ContextLease lease && ReferenceEquals(lease.Owner, this) && ReferenceEquals(lease.Claim, sameClaim);
    public async ValueTask ValidateOriginalAsync(ITaskRunColdContextLease sameLease, TaskExecutionSnapshot actualExpected, CancellationToken token)
    {
        if (sameLease is not ContextLease lease || !ReferenceEquals(lease.Owner, this) || lease.Closed)
            throw new UnauthorizedAccessException("No same live personal-store context lease exists.");
        await ValidateOriginalClaimAsync(lease.Claim, actualExpected, token).ConfigureAwait(false);
        OriginalSources(lease.Claim.Entry.Sources).Invoke(() => { lease.Store.Validate(); return true; });
        if (await DemandActorAsync(OriginalSources(lease.Claim.Entry.Sources), actualExpected.OwnerBinding!, token).ConfigureAwait(false) != lease.CurrentActor)
            throw new UnauthorizedAccessException("The actual current local activation changed.");
    }
    public async ValueTask ValidateOriginalClosedContextAsync(ITaskRunColdContextLease sameLease,
        ITaskRunColdJournalAcknowledgment sameAcknowledgment, CancellationToken token)
    {
        var ack = RequireAcknowledgment(sameAcknowledgment);
        if (sameLease is not ContextLease lease || !ReferenceEquals(lease.Owner, this)
            || !ReferenceEquals(lease.Claim, ack.Claim) || !lease.Closed || lease.Close is not { IsCompletedSuccessfully: true }
            || lease.Claim.Close is not { IsCompletedSuccessfully: true })
            throw new UnauthorizedAccessException("The SAME acquired context/CAS connection originals must close successfully before activation.");
        await ValidateOriginalAcknowledgmentAsync(ack, token).ConfigureAwait(false);
        if (await DemandActorAsync(OriginalSources(lease.Claim.Entry.Sources), ack.AcknowledgedTask.OwnerBinding!, token).ConfigureAwait(false) != lease.CurrentActor)
            throw new UnauthorizedAccessException("The actual current activation changed after the acknowledged reads.");
    }

    private async Task DemandClaimRowAsync(Claim claim, int state, CancellationToken token,
        TaskRunColdOriginalSourceScope? currentSources = null)
    {
        var sources = currentSources ?? OriginalSources(claim.Entry.Sources); var connection = await OpenOriginalAsync(sources, token).ConfigureAwait(false);
        try
        {
            using var command = sources.Invoke(connection.CreateCommand);
            command.CommandText = "SELECT capsule_json,authentication_tag,claim_state,claim_id,acknowledged_json,terminal_json,state_authentication_tag FROM task_run_recovery_journal WHERE capsule_id=$capsule AND claim_id=$claim AND claim_state=$state AND capsule_json=$payload AND authentication_tag=$tag;";
            AddEntry(command, claim.Entry); command.Parameters.AddWithValue("$claim", claim.Id.ToString()); command.Parameters.AddWithValue("$state", state);
            var reader = await AwaitActualAsync(sources, () => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            try
            {
                if (!await AwaitActualAsync(sources, () => reader.ReadAsync(token)).ConfigureAwait(false))
                    throw new InvalidOperationException("The actual original claim is changed, uncertain or consumed.");
                using var store = sources.Invoke(() => NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath));
                var key = sources.Invoke(() => store.ReadOrCreateAuthenticationKey(create: false));
                try { DemandStateAuthentication(reader, key, claim.Entry.Payload); }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            finally { await AwaitActualAsync(sources, () => reader.DisposeAsync().AsTask()).ConfigureAwait(false); }
        }
        finally { await CloseOriginalAsync(sources, connection).ConfigureAwait(false); }
    }
    private async Task ChangeClaimAsync(Entry entry, string sql, Guid claimId, TaskExecutionSnapshot expected,
        string? terminal, byte[] previousStateTag, byte[] nextStateTag, CancellationToken token)
    {
        var connection = await OpenOriginalAsync(entry.Sources, token).ConfigureAwait(false);
        try
        {
            using var command = entry.Sources.Invoke(connection.CreateCommand); command.CommandText = sql;
            AddEntry(command, entry); AddIdentity(command, expected); command.Parameters.AddWithValue("$claim", claimId.ToString());
            command.Parameters.AddWithValue("$expected", UnifiedPersistenceJson.Write(expected));
            if (terminal is not null) command.Parameters.AddWithValue("$terminal", terminal);
            command.Parameters.AddWithValue("$previousStateTag", previousStateTag); command.Parameters.AddWithValue("$nextStateTag", nextStateTag);
            if (await AwaitActualAsync(entry.Sources, () => command.ExecuteNonQueryAsync(token)).ConfigureAwait(false) != 1)
                throw new InvalidOperationException("The SAME original one-use claim changed; it cannot be automatically replayed.");
        }
        finally { await CloseOriginalAsync(entry.Sources, connection).ConfigureAwait(false); }
    }
    private static void AddIdentity(SqliteCommand command, TaskExecutionSnapshot snapshot)
    {
        command.Parameters.AddWithValue("$task", snapshot.TaskId.ToString()); command.Parameters.AddWithValue("$context", snapshot.ContextId.ToString());
        command.Parameters.AddWithValue("$run", snapshot.ExecutionId.ToString()); command.Parameters.AddWithValue("$revision", snapshot.PersistenceRevision);
    }
    private static void AddEntry(SqliteCommand command, Entry entry)
    {
        command.Parameters.AddWithValue("$capsule", entry.Capsule.CapsuleId.ToString()); command.Parameters.AddWithValue("$payload", entry.Payload);
        command.Parameters.AddWithValue("$tag", entry.Tag);
    }
    private static byte[] StateTag(byte[] key, string payload, int state, string? claim, string? acknowledged, string? terminal) =>
        HMACSHA256.HashData(key, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { payload, state, claim, acknowledged, terminal }));
    private static void DemandStateAuthentication(SqliteDataReader reader, byte[] key, string payload)
    {
        var actual = (byte[])reader.GetValue(6);
        var expected = StateTag(key, payload, reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5));
        if (actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(actual, expected))
            throw new UnauthorizedAccessException("The actual capsule claim/ACK state authentication failed; public state fields cannot restore provenance.");
    }
    private async Task<SqliteConnection> OpenOriginalAsync(TaskRunColdOriginalSourceScope sources, CancellationToken token)
    {
        using var store = sources.Invoke(() => NativePersonalTaskRecoveryStore.Acquire(paths.DataDirectory, paths.DatabasePath));
        // This journal owns its actual open/PRAGMA Tasks on the exact configured file. It
        // does not reconstruct a second model store or reduce those Tasks through a repository wrapper.
        var connection = sources.Invoke(() => new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath, Mode = SqliteOpenMode.ReadWrite, Cache = SqliteCacheMode.Shared,
            Pooling = true, ForeignKeys = true
        }.ToString()));
        try
        {
            await AwaitActualAsync(sources, () => connection.OpenAsync(token)).ConfigureAwait(false);
            using var pragma = sources.Invoke(connection.CreateCommand);
            pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            await AwaitActualAsync(sources, () => pragma.ExecuteNonQueryAsync(token)).ConfigureAwait(false);
            if (Path.GetFullPath(connection.DataSource) != Path.GetFullPath(paths.DatabasePath))
                throw new UnauthorizedAccessException("The configured actual database/source identity changed.");
            sources.Invoke(() => { store.Validate(); return true; }); return connection;
        }
        catch { await CloseOriginalAsync(sources, connection).ConfigureAwait(false); throw; }
    }
    private async Task<TaskExecutionSnapshot> ReadTaskAsync(TaskRunColdOriginalSourceScope sources, Guid taskId, CancellationToken token)
    {
        var connection = await OpenOriginalAsync(sources, token).ConfigureAwait(false);
        try
        {
            using var command = sources.Invoke(connection.CreateCommand);
            command.CommandText = "SELECT task_id,context_id,execution_id,payload_json,persistence_revision FROM task_execution_state WHERE task_id=$task;";
            command.Parameters.AddWithValue("$task", taskId.ToString());
            var reader = await AwaitActualAsync(sources, () => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            try
            {
                if (!await AwaitActualAsync(sources, () => reader.ReadAsync(token)).ConfigureAwait(false))
                    throw new KeyNotFoundException("The SAME persisted Task does not exist.");
                var snapshot = sources.Invoke(() => UnifiedPersistenceJson.Read<TaskExecutionSnapshot>(reader.GetString(3)));
                if (snapshot.TaskId != Guid.Parse(reader.GetString(0)) || snapshot.ContextId != Guid.Parse(reader.GetString(1))
                    || snapshot.ExecutionId != Guid.Parse(reader.GetString(2)) || snapshot.PersistenceRevision != reader.GetInt64(4)
                    || snapshot.PersistenceRevision < 0)
                    throw new InvalidDataException("The canonical Task columns and payload disagree.");
                return snapshot;
            }
            finally { await AwaitActualAsync(sources, () => reader.DisposeAsync().AsTask()).ConfigureAwait(false); }
        }
        finally { await CloseOriginalAsync(sources, connection).ConfigureAwait(false); }
    }
    private async Task DemandInputAsync(TaskRunColdOriginalSourceScope sources, TaskRunColdCapsule capsule, CancellationToken token)
    {
        var connection = await OpenOriginalAsync(sources, token).ConfigureAwait(false);
        try
        {
            using var command = sources.Invoke(connection.CreateCommand);
            command.CommandText = "SELECT * FROM conversations WHERE id=$conversation;";
            command.Parameters.AddWithValue("$conversation", capsule.AcceptedConversation.Id.ToString());
            var reader = await AwaitActualAsync(sources, () => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            try
            {
                if (!await AwaitActualAsync(sources, () => reader.ReadAsync(token)).ConfigureAwait(false))
                    throw new InvalidOperationException("The original Conversation no longer exists.");
                var actual = sources.Invoke(() => new Conversation(reader.Guid("id"), (HavenMode)reader.Int32("mode"),
                    (ConversationKind)reader.Int32("kind"), reader.String("title"), reader.NullableGuid("container_id"),
                    reader.NullableGuid("lesson_id"), reader.Boolean("is_pinned"), reader.Boolean("is_temporary"),
                    reader.DateTimeOffset("created_at"), reader.DateTimeOffset("updated_at"), reader.Boolean("is_archived"),
                    reader.NullableGuid("parent_conversation_id"), reader.NullableDateTimeOffset("compacted_at"), reader.NullableGuid("space_id")));
                if (actual != capsule.AcceptedConversation) throw new InvalidOperationException("The exact accepted Conversation changed.");
            }
            finally { await AwaitActualAsync(sources, () => reader.DisposeAsync().AsTask()).ConfigureAwait(false); }
            // Only a single initial input with its one genuine main branch is supported.
            // Extra/forked/versioned history requires its owning checkpoint reconciliation.
            command.CommandText = "SELECT COUNT(*) FROM messages WHERE conversation_id=$conversation;";
            if (Convert.ToInt64(await AwaitActualAsync(sources, () => command.ExecuteScalarAsync(token)).ConfigureAwait(false)) != 1)
                throw new InvalidOperationException("Additional accepted history cannot be reconstructed as an initial input.");
            command.CommandText = "SELECT COUNT(*) FROM conversation_branches WHERE conversation_id=$conversation;";
            if (Convert.ToInt64(await AwaitActualAsync(sources, () => command.ExecuteScalarAsync(token)).ConfigureAwait(false)) != 1)
                throw new InvalidOperationException("Forked or absent branch ownership requires reconciliation.");
            command.CommandText = "SELECT id FROM conversation_branches WHERE conversation_id=$conversation AND is_current=1 AND parent_branch_id IS NULL AND forked_from_message_id IS NULL;";
            var branch = await AwaitActualAsync(sources, () => command.ExecuteScalarAsync(token)).ConfigureAwait(false);
            if (branch is not string branchId) throw new InvalidOperationException("The acknowledged main branch changed.");
            command.Parameters.AddWithValue("$branch", branchId);
            command.CommandText = "SELECT COUNT(*) FROM message_versions v JOIN messages m ON m.id=v.message_id WHERE m.conversation_id=$conversation;";
            if (Convert.ToInt64(await AwaitActualAsync(sources, () => command.ExecuteScalarAsync(token)).ConfigureAwait(false)) != 1)
                throw new InvalidOperationException("Edited/versioned input cannot reuse the original initial-input capsule.");
            command.CommandText = """
                SELECT m.id,m.conversation_id,m.role,v.content,m.agent_name,m.model_name,
                       COALESCE(v.metadata_json,m.metadata_json) AS metadata_json,m.created_at,m.is_compacted
                  FROM conversation_branch_messages bm JOIN messages m ON m.id=bm.message_id
                  JOIN message_versions v ON v.message_id=m.id AND v.branch_id=bm.branch_id AND v.is_current=1
                 WHERE bm.branch_id=$branch AND m.conversation_id=$conversation;
                """;
            reader = await AwaitActualAsync(sources, () => command.ExecuteReaderAsync(token)).ConfigureAwait(false);
            try
            {
                if (!await AwaitActualAsync(sources, () => reader.ReadAsync(token)).ConfigureAwait(false))
                    throw new InvalidOperationException("The exact accepted branch input is missing.");
                var actual = sources.Invoke(() => new ChatMessage(reader.Guid("id"), reader.Guid("conversation_id"),
                    (MessageRole)reader.Int32("role"), reader.String("content"), reader.NullableString("agent_name"),
                    reader.NullableString("model_name"), reader.NullableString("metadata_json"), reader.DateTimeOffset("created_at"), reader.Boolean("is_compacted")));
                if (actual != capsule.AcceptedUserMessage || await AwaitActualAsync(sources, () => reader.ReadAsync(token)).ConfigureAwait(false))
                    throw new InvalidOperationException("The exact acknowledged User/history changed; retained text cannot replace it.");
            }
            finally { await AwaitActualAsync(sources, () => reader.DisposeAsync().AsTask()).ConfigureAwait(false); }
        }
        finally { await CloseOriginalAsync(sources, connection).ConfigureAwait(false); }
    }
    private async Task<AuthenticatedResourceActor> DemandActorAsync(TaskRunColdOriginalSourceScope sources, TaskExecutionOwnerBinding previous, CancellationToken token)
    {
        var actor = await AwaitActualAsync(sources, () => actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("No actual native local principal is available.");
        if (actor.ActorId != previous.ActorId || actor.ProfileId != previous.ProfileId || actor.AccountId != previous.AccountId
            || actor.OrganisationId != previous.OrganisationId || actor.AccountId is not null || actor.OrganisationId is not null)
            throw new UnauthorizedAccessException("Only the actual same local personal-store principal can authorize this qualified context; cloud/browser/Home grants remain separate.");
        return actor;
    }
    private static Task CloseOriginalAsync(TaskRunColdOriginalSourceScope sources, SqliteConnection connection) =>
        AwaitActualAsync(sources, () => connection.DisposeAsync().AsTask());
    private static async Task<T> AwaitActualAsync<T>(TaskRunColdOriginalSourceScope sources, Func<Task<T>> callback)
    {
        Task<T>? actual = null; Exception? acquisitionFailure = null; Exception? originalFailure = null;
        T result = default!;
        try { _ = sources.Invoke(() => { actual = callback() ?? throw new InvalidOperationException("No actual cold journal source Task was returned."); return actual; }); }
        catch (Exception cause) { acquisitionFailure = cause; }
        // A caller scope may fault AFTER the raw factory returned. Join that SAME Task
        // independently before reporting scope failure or disposing borrowed resources.
        if (actual is not null)
            try { result = await actual.ConfigureAwait(false); }
            catch (Exception cause) { originalFailure = actual.IsFaulted && actual.Exception is { } envelope ? envelope : cause; }
        if (acquisitionFailure is not null)
            throw new AggregateException("Original cold source acquisition/caller scope failed; actual raw source was joined.",
                originalFailure is null ? new[] { acquisitionFailure } : new[] { acquisitionFailure, originalFailure });
        if (originalFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailure).Throw();
        return result;
    }
    private static async Task AwaitActualAsync(TaskRunColdOriginalSourceScope sources, Func<Task> callback)
    {
        Task? actual = null; Exception? acquisitionFailure = null; Exception? originalFailure = null;
        try { _ = sources.Invoke(() => { actual = callback() ?? throw new InvalidOperationException("No actual cold journal source Task was returned."); return actual; }); }
        catch (Exception cause) { acquisitionFailure = cause; }
        if (actual is not null)
            try { await actual.ConfigureAwait(false); }
            catch (Exception cause) { originalFailure = actual.IsFaulted && actual.Exception is { } envelope ? envelope : cause; }
        if (acquisitionFailure is not null)
            throw new AggregateException("Original cold source acquisition/caller scope failed; actual raw source was joined.",
                originalFailure is null ? new[] { acquisitionFailure } : new[] { acquisitionFailure, originalFailure });
        if (originalFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(originalFailure).Throw();
    }
}
