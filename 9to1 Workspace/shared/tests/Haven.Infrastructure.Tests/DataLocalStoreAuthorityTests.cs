using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Core.Forms;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Infrastructure.Tests;

public sealed class DataLocalStoreAuthorityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lying_scalar_collection_is_bounded_before_actual_display_review_without_pending_or_writes(bool duplicate)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets.Single(); sheet.SetCell(0, 0, "Amount");
        var table = new DataTableDefinition { Name = "Amounts", SheetId = sheet.Id, Range = new() { EndRow = 0 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "Amount", DataFieldType.Integer, false)], []).Workbook!;
        var saved = await fixture.Repository.SaveAsync(workbook, "Typed amounts", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var before = await File.ReadAllBytesAsync(saved.CurrentPath);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var creator = new DataHomeRecordCreator(fixture.Repository, fixture.Authority!, fixture.Actor!, broker);
        var display = await creator.LoadForDisplayAsync(workbook.Id);
        var values = new LyingRecordValues(table.Fields[0].FieldID, duplicate);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => creator.ReviewAsync(display.Selection, workbook.Id, table.Id,
            Guid.NewGuid(), workbook.Version, workbook.RevisionId, values));
        Assert.Equal(duplicate ? 2 : 257, values.Consumed);
        var intentValues = new LyingRecordValues(table.Fields[0].FieldID, duplicate);
        Assert.ThrowsAny<ArgumentException>(() => DataRecordCreateIntent.Capture(fixture.StoreID, workbook, table.Id, Guid.NewGuid(), intentValues));
        Assert.Equal(duplicate ? 2 : 257, intentValues.Consumed);
        var coreValues = new LyingRecordValues(table.Fields[0].FieldID, duplicate);
        if (duplicate)
            Assert.ThrowsAny<ArgumentException>(() => DataRecordCreation.Prepare(workbook, table.Id, Guid.NewGuid(), workbook.Version, workbook.RevisionId, coreValues));
        else Assert.False(DataRecordCreation.Prepare(workbook, table.Id, Guid.NewGuid(), workbook.Version, workbook.RevisionId, coreValues).Success);
        Assert.Equal(duplicate ? 2 : 257, coreValues.Consumed);
        Assert.Empty((await fixture.Permissions!.GetSnapshotAsync()).PendingRequests);
        Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath));
    }

    private sealed class LyingRecordValues(Guid originalField, bool duplicate) : IReadOnlyDictionary<Guid, DataScalarRecordValue>
    {
        public int Consumed { get; private set; }
        public int Count => 1;
        public IEnumerable<Guid> Keys => throw new InvalidOperationException("Caller metadata must not be enumerated.");
        public IEnumerable<DataScalarRecordValue> Values => throw new InvalidOperationException("Caller metadata must not be enumerated.");
        public DataScalarRecordValue this[Guid key] => throw new NotSupportedException();
        public bool ContainsKey(Guid key) => throw new NotSupportedException();
        public bool TryGetValue(Guid key, out DataScalarRecordValue value) { value = null!; throw new NotSupportedException(); }
        public IEnumerator<KeyValuePair<Guid, DataScalarRecordValue>> GetEnumerator()
        {
            for (var index = 0; index < 1_000_000; index++)
            {
                Consumed++;
                yield return new(duplicate || index == 0 ? originalField : Guid.NewGuid(),
                    new(DataCellKind.Number, JsonSerializer.SerializeToElement(7)));
            }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Originally_displayed_selection_denies_replacement_before_review_even_with_colliding_workbook_identity(bool replaceStore)
    {
        using var fixture = new Fixture(); using var foreign = new Fixture(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        await fixture.InitializeAsync(token); await foreign.InitializeAsync(token);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets.Single(); sheet.SetCell(0, 0, "Amount");
        var table = new DataTableDefinition { Name = "Amounts", SheetId = sheet.Id, Range = new() { EndRow = 0 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "Amount", DataFieldType.Integer, false)], []).Workbook!;
        var saved = await fixture.Repository.SaveAsync(workbook, "Typed amounts", (await fixture.CaptureAsync(token))!, token);
        var before = await File.ReadAllBytesAsync(saved.CurrentPath, token);
        // Collision is deliberate setup in a distinct, genuinely owned filesystem root.
        var foreignPath = Path.Combine(foreign.Paths.DataDirectory, "Data", "Workbooks", workbook.Id.ToString("D"), "current.json");
        Directory.CreateDirectory(Path.GetDirectoryName(foreignPath)!); await File.WriteAllBytesAsync(foreignPath, before, token);
        var repositories = new SwitchableDisplayRepository(fixture.Repository);
        var authorities = new SwitchableDisplayAuthority(fixture.Authority!);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(repositories, authorities)]), fixture.Permissions!);
        var creator = new DataHomeRecordCreator(repositories, authorities, fixture.Actor!, broker);
        var display = await creator.LoadForDisplayAsync(workbook.Id, token);
        Assert.Equal(workbook.Version, display.Workbook.Version); Assert.Equal(workbook.RevisionId, display.Workbook.RevisionId);
        if (replaceStore)
        {
            repositories.Current = foreign.Repository; authorities.Current = foreign.Authority!;
            fixture.Actor!.Current = foreign.Actor!.Current;
            Assert.NotNull(await authorities.CaptureAsync(foreign.StoreID, workbook.Id, workbook.Version, workbook.RevisionId,
                DataRecordCreateIntent.ActionID, fixture.Actor.Current, token));
        }
        else fixture.Actor!.Current = fixture.Actor.Current with { AuthenticationRevision = "replacement-before-review" };
        var values = new Dictionary<Guid, DataScalarRecordValue> { [table.Fields[0].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(7)) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => creator.ReviewAsync(display.Selection, workbook.Id, table.Id,
            Guid.NewGuid(), workbook.Version, workbook.RevisionId, values, token));
        // Neither caller-controlled IDs nor the legacy fresh-identity overload can bypass the origin.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => creator.ReviewAsync(workbook.Id, table.Id,
            Guid.NewGuid(), workbook.Version, workbook.RevisionId, values, token));
        Assert.Empty((await fixture.Permissions!.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
        Assert.Empty((await foreign.Permissions!.GetSnapshotAsync(cancellationToken: token)).PendingRequests);
        Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath, token));
        Assert.Equal(before, await File.ReadAllBytesAsync(foreignPath, token));
    }

    private sealed class SwitchableDisplayAuthority(IDataWorkbookCommitAuthority original) : IDataWorkbookCommitAuthority
    {
        public IDataWorkbookCommitAuthority Current { get; set; } = original;
        public ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(Guid storeID, Guid workbookID, int version, Guid revision,
            string action, AuthenticatedResourceActor? actor = null, CancellationToken token = default)
            => Current.CaptureAsync(storeID, workbookID, version, revision, action,
                actor ?? throw new InvalidOperationException("The actual display fixture requires a bound actor."), token);
    }
    private sealed class SwitchableDisplayRepository(IDataGuardedWorkbookRepository original) : IDataGuardedWorkbookRepository
    {
        public IDataGuardedWorkbookRepository Current { get; set; } = original;
        public Task<DataWorkbook?> LoadAsync(Guid id, CancellationToken token) => Current.LoadAsync(id, token);
        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken token) => Current.ListAsync(token);
        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken token) => Current.SaveAsync(workbook, reason, token);
        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, IDataWorkbookCommitAdmission admission, CancellationToken token) => Current.SaveAsync(workbook, reason, admission, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => Current.DeleteAsync(id, token);
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => Current.GetStoreIdentityAsync(token);
        public ValueTask<DataWorkbookStoreEvidence?> ReadStoreEvidenceAsync(CancellationToken token) => Current.ReadStoreEvidenceAsync(token);
    }

    [Fact]
    public async Task Actual_record_review_captures_caller_scalars_before_held_physical_load_and_approval()
    {
        using var fixture = new Fixture(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25)); var token = timeout.Token;
        await fixture.InitializeAsync(token);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets.Single(); sheet.SetCell(0, 0, "Amount");
        var table = new DataTableDefinition { Name = "Amounts", SheetId = sheet.Id, Range = new() { EndRow = 0 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "Amount", DataFieldType.Integer, false)], []).Workbook!;
        var saved = await fixture.Repository.SaveAsync(workbook, "Typed amounts", (await fixture.CaptureAsync(token))!, token);
        var before = await File.ReadAllBytesAsync(saved.CurrentPath, token);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var held = new RecordLoadBarrierRepository(fixture.Repository);
        var creator = new DataHomeRecordCreator(held, fixture.Authority!, fixture.Actor!, broker);
        var display = await creator.LoadForDisplayAsync(workbook.Id, token); held.Arm();
        using var scalar = JsonDocument.Parse("7");
        var values = new Dictionary<Guid, DataScalarRecordValue> { [table.Fields[0].FieldID] = new(DataCellKind.Number, scalar.RootElement) };
        var recordID = Guid.NewGuid();
        var pending = creator.ReviewAsync(display.Selection, workbook.Id, table.Id, recordID, workbook.Version, workbook.RevisionId, values, token);
        await held.Entered.Task.WaitAsync(token);
        scalar.Dispose(); values[table.Fields[0].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(9)); held.Release.TrySetResult();
        var review = await pending; Assert.Equal(7, review.Intent.Values[table.Fields[0].FieldID].Value.GetInt32());
        Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath, token));
        Assert.True((await fixture.Permissions!.DecideAsync(review.RequestID, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        Assert.True((await creator.CommitAsync(review, token)).Committed);
        var actual = (await fixture.Repository.LoadAsync(workbook.Id, token))!;
        Assert.Equal("7", DataTableIdentity.ReadCell(actual, table.Id, recordID, table.Fields[0].FieldID)!.Value);
        Assert.Equal(3, actual.Version);
    }

    private sealed class RecordLoadBarrierRepository(IDataGuardedWorkbookRepository inner) : IDataGuardedWorkbookRepository
    {
        private bool _armed;
        public void Arm() => _armed = true;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<DataWorkbook?> LoadAsync(Guid workbookID, CancellationToken token)
        {
            var workbook = await inner.LoadAsync(workbookID, token);
            if (_armed) { _armed = false; Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return workbook;
        }
        public Task<IReadOnlyList<DataWorkbookSummary>> ListAsync(CancellationToken token) => inner.ListAsync(token);
        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, CancellationToken token) => inner.SaveAsync(workbook, reason, token);
        public Task<DataSaveResult> SaveAsync(DataWorkbook workbook, string reason, IDataWorkbookCommitAdmission admission, CancellationToken token) => inner.SaveAsync(workbook, reason, admission, token);
        public Task DeleteAsync(Guid workbookID, CancellationToken token) => inner.DeleteAsync(workbookID, token);
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) => inner.GetStoreIdentityAsync(token);
        public ValueTask<DataWorkbookStoreEvidence?> ReadStoreEvidenceAsync(CancellationToken token) => inner.ReadStoreEvidenceAsync(token);
    }

    [Fact]
    public async Task Actual_record_creator_reviews_defaults_and_dependent_calculation_before_atomic_persistence()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets.Single(); sheet.SetCell(0, 0, "ID"); sheet.SetCell(0, 1, "Code");
        sheet.SetCell(0, 3, "old cache", "=A2+NOW()", DataCellKind.Formula);
        var table = new DataTableDefinition { Name = "People", SheetId = sheet.Id, Range = new() { EndRow = 0, EndColumn = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var key = new DataKeyDefinition(Guid.NewGuid(), "ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false), new(table.Fields[1].FieldID, "Code", DataFieldType.Text, false, "007")], [key]).Workbook!;
        var saved = await fixture.Repository.SaveAsync(workbook, "Typed people", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var before = await File.ReadAllBytesAsync(saved.CurrentPath);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var creator = new DataHomeRecordCreator(fixture.Repository, fixture.Authority!, fixture.Actor!, broker);
        var display = await creator.LoadForDisplayAsync(workbook.Id);
        var recordID = Guid.NewGuid(); var review = await creator.ReviewAsync(display.Selection, workbook.Id, table.Id, recordID, workbook.Version, workbook.RevisionId,
            new Dictionary<Guid, DataScalarRecordValue> { [table.Fields[0].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(1)) });
        Assert.Equal(1, review.Intent.Arguments.GetProperty("recordsCreated").GetInt32());
        var effect = Assert.Single(review.Intent.Arguments.GetProperty("formulaEffects").EnumerateArray());
        Assert.Equal("old cache", effect.GetProperty("before").GetProperty("Value").GetString());
        Assert.False((await creator.CommitAsync(review)).Committed); Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath));
        Assert.True((await fixture.Permissions!.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
        var result = await creator.CommitAsync(review); Assert.True(result.Committed); Assert.True(result.AuditRecorded);
        var actual = (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!; Assert.Equal(3, actual.Version);
        Assert.Equal(recordID, Assert.Single(actual.Tables.Single().Records).RecordID);
        var actualDefault = DataTableIdentity.ReadCell(actual, table.Id, recordID, table.Fields[1].FieldID)!;
        Assert.Equal("007", actualDefault.Value); Assert.Equal(DataCellKind.Text, actualDefault.Kind);
        Assert.Equal(effect.GetProperty("after").GetProperty("Value").GetString(), actual.Sheets.Single().GetCell(0, 3)!.Value);
        Assert.NotNull(await new DataRecordCreateRecovery(fixture.Repository, fixture.Authority!, fixture.Actor!).ReadAsync(review.Intent));
        Assert.Equal("DataRecordAlreadyCreated", (await creator.CommitAsync(review)).Code);
        Assert.Equal(3, (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!.Version);
        await Assert.ThrowsAsync<InvalidDataException>(() => creator.ReviewAsync(display.Selection, actual.Id, table.Id, Guid.NewGuid(), actual.Version, actual.RevisionId,
            new Dictionary<Guid, DataScalarRecordValue> { [table.Fields[0].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(1)) }));
        Assert.Equal(3, (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Record_create_commit_rechecks_actual_actor_or_binding_inside_canonical_workbook_lease(bool revokeBinding)
    {
        using var fixture = new Fixture(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token; await fixture.InitializeAsync(token);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "ID"); sheet.SetCell(1, 0, "1");
        var table = new DataTableDefinition { Name = "People", SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var key = new DataKeyDefinition(Guid.NewGuid(), "ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false)], [key]).Workbook!;
        var saved = await fixture.Repository.SaveAsync(workbook, "Typed people", (await fixture.CaptureAsync(token))!, token);
        var intent = DataRecordCreateIntent.Capture(fixture.StoreID, workbook, table.Id, Guid.NewGuid(),
            new Dictionary<Guid, DataScalarRecordValue> { [table.Fields[0].FieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(2)) });
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var request = await broker.AuthorizeAsync("data", DataRecordCreateIntent.ActionID, intent.Scopes, intent.Arguments,
            "Create record", null, "actual-record-create-lease", token);
        Assert.True((await fixture.Permissions!.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var capability = (await broker.BeginExecutionCapabilityAsync(request.RequestId, intent.Arguments, token))!;
        var before = await File.ReadAllBytesAsync(saved.CurrentPath, token);
        var lockPath = Path.Combine(fixture.Paths.DataDirectory, "Data", "Workbooks", ".locks", workbook.Id.ToString("D") + ".lock");
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var barrier = new SchemaCaptureBarrier(fixture.Authority!);
        var pending = new DataHomeRecordCreateOperation(fixture.Repository, barrier, broker).ExecuteAsync(intent, capability, token);
        await barrier.Captured.Task.WaitAsync(token);
        Assert.False(pending.IsCompleted);
        if (!revokeBinding) fixture.Actor!.Current = fixture.Actor.Current with { AuthenticationRevision = "revoked-record-create-session" };
        else
        {
            var record = Assert.Single((await fixture.Home.ReadAsync(token)).State!.Records, item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await fixture.Home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision, token)).IsSuccess);
        }
        lease.Dispose(); var result = await pending;
        Assert.False(result.Committed); Assert.Equal("PermissionDenied", result.Code); Assert.True(result.AuditRecorded);
        Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath, token));
        Assert.Single((await fixture.Repository.LoadAsync(workbook.Id, token))!.Tables.Single().Records);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(saved.CurrentPath)!, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Actual_junction_owner_creates_one_canonical_association_table_without_copying_source_records()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "ID"); sheet.SetCell(1, 0, "1");
        var table = new DataTableDefinition { Name = "People", SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var key = new DataKeyDefinition(Guid.NewGuid(), "Person ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false)], [key]).Workbook!;
        await fixture.Repository.SaveAsync(workbook, "Typed people", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var beforeTable = JsonSerializer.Serialize(workbook.Tables.Single(item => item.Id == table.Id));
        var beforeSheet = JsonSerializer.Serialize(workbook.Sheets.Single(item => item.Id == sheet.Id));
        var endpoint = new DataJunctionEndpoint(table.Id, key.KeyID);
        var definition = DataJunctionTableDesign.CreateDefinition(workbook, "Connections", endpoint, endpoint);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var designer = new DataHomeJunctionTableDesigner(fixture.Repository, fixture.Authority!, fixture.Actor!, broker);
        var review = await designer.ReviewAsync(workbook.Id, workbook.Version, workbook.RevisionId, definition);
        Assert.Equal(0, review.Intent.Arguments.GetProperty("sourceRecordsCopied").GetInt32());
        Assert.Equal(0, review.Intent.Arguments.GetProperty("recordsChanged").GetInt32());
        Assert.False((await designer.CommitAsync(review)).Committed);
        Assert.True((await fixture.Permissions!.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
        var result = await designer.CommitAsync(review); Assert.True(result.Committed); Assert.True(result.AuditRecorded);
        var actual = (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!;
        Assert.Equal(3, actual.Version); Assert.Equal(2, actual.Tables.Count); Assert.Equal(2, actual.Sheets.Count);
        Assert.Equal(beforeTable, JsonSerializer.Serialize(actual.Tables.Single(item => item.Id == table.Id)));
        Assert.Equal(beforeSheet, JsonSerializer.Serialize(actual.Sheets.Single(item => item.Id == sheet.Id)));
        Assert.Empty(actual.Tables.Single(item => item.Id == definition.TableID).Records);
        Assert.Equal(2, actual.Relationships.Count); Assert.Empty(DataRelationalSchema.Inspect(actual));
        var recovered = await new DataJunctionTableMutationRecovery(fixture.Repository, fixture.Authority!, fixture.Actor!).ReadAsync(review.Intent);
        Assert.NotNull(recovered); Assert.Equal(definition.TableID, recovered!.TableID);
        Assert.Equal("DataJunctionTableAlreadyCommitted", (await designer.CommitAsync(review)).Code);
        Assert.Equal(3, (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Junction_commit_rechecks_actual_actor_or_binding_inside_canonical_workbook_lease(bool revokeBinding)
    {
        using var fixture = new Fixture(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token; await fixture.InitializeAsync(token);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "ID"); sheet.SetCell(1, 0, "1");
        var table = new DataTableDefinition { Name = "People", SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var key = new DataKeyDefinition(Guid.NewGuid(), "ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false)], [key]).Workbook!;
        var saved = await fixture.Repository.SaveAsync(workbook, "Typed people", (await fixture.CaptureAsync(token))!, token);
        var endpoint = new DataJunctionEndpoint(table.Id, key.KeyID);
        var definition = DataJunctionTableDesign.CreateDefinition(workbook, "Connections", endpoint, endpoint);
        var intent = DataJunctionTableUpdateIntent.Capture(fixture.StoreID, workbook, definition);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var request = await broker.AuthorizeAsync("data", DataJunctionTableUpdateIntent.ActionID, intent.Scopes, intent.Arguments,
            "Create association table", null, "actual-junction-lease", token);
        Assert.True((await fixture.Permissions!.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var capability = (await broker.BeginExecutionCapabilityAsync(request.RequestId, intent.Arguments, token))!;
        var before = await File.ReadAllBytesAsync(saved.CurrentPath, token);
        var lockPath = Path.Combine(fixture.Paths.DataDirectory, "Data", "Workbooks", ".locks", workbook.Id.ToString("D") + ".lock");
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var barrier = new SchemaCaptureBarrier(fixture.Authority!);
        var pending = new DataHomeJunctionTableUpdateOperation(fixture.Repository, barrier, broker).ExecuteAsync(intent, capability, token);
        await barrier.Captured.Task.WaitAsync(token);
        Assert.False(pending.IsCompleted);
        if (!revokeBinding) fixture.Actor!.Current = fixture.Actor.Current with { AuthenticationRevision = "revoked-junction-session" };
        else
        {
            var record = Assert.Single((await fixture.Home.ReadAsync(token)).State!.Records, item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await fixture.Home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision, token)).IsSuccess);
        }
        lease.Dispose(); var result = await pending;
        Assert.False(result.Committed); Assert.Equal("PermissionDenied", result.Code); Assert.True(result.AuditRecorded);
        Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath, token));
        Assert.Single((await fixture.Repository.LoadAsync(workbook.Id, token))!.Tables);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(saved.CurrentPath)!, "*.tmp", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Exact_Home_record_operation_commits_once_or_reports_validation_failure(bool invalid, bool fromForm)
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
        var table = new DataTableDefinition { SheetId = sheet.Id, HasHeaders = true, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        if (invalid) workbook.Validations.Add(new() { SheetId = sheet.Id, Range = new() { StartRow = 1, EndRow = 1 },
            Kind = DataValidationKind.WholeNumber, Minimum = "0", Maximum = "10" });
        await fixture.Repository.SaveAsync(workbook, "Canonical table", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var fieldID = table.Fields[0].FieldID; var recordID = table.Records[0].RecordID;
        using var source = JsonDocument.Parse("42");
        var values = new Dictionary<Guid, DataScalarRecordValue> { [fieldID] = new(DataCellKind.Number, source.RootElement) };
        var intent = DataRecordUpdateIntent.Capture(fixture.StoreID, workbook.Id, workbook.Version, workbook.RevisionId, table.Id, recordID, values);
        if (fromForm)
        {
            var now = DateTimeOffset.UtcNow; var form = FormProjectEditor.Create("Bound response", FormModeKind.Form, now);
            var field = new FormField(Guid.NewGuid(), FormFieldKind.Number, "Amount", null, JsonSerializer.SerializeToElement(new { }), true, new());
            form = FormProjectEditor.AddField(form, form.Revision, form.Pages[0].PageID, field, now);
            form = FormProjectEditor.BindData(form, form.Revision, new(Guid.NewGuid(), field.FieldID, workbook.Id,
                table.Id, fieldID, FormDataBindingKind.UpdateRecord), now);
            var runtime = new FormResponseRuntime(form, Guid.NewGuid());
            var answered = runtime.Answer(1, field.FieldID, JsonSerializer.SerializeToElement(42));
            var submitted = runtime.Submit(answered.Response.Revision); Assert.True(submitted.Success);
            var plan = FormDataRecordUpdateProjection.Prepare(form, submitted.Response, fixture.StoreID, workbook, table.Id, recordID);
            Assert.Equal(submitted.Response.ResponseID, plan.ResponseID);
            intent = plan.Intent;
        }
        source.Dispose(); values[fieldID] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(999));
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var pending = await broker.AuthorizeAsync("data", DataRecordUpdateIntent.ActionId, intent.Scopes, intent.Arguments,
            "Update canonical amount", null, "actual-data-session");
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
        Assert.Null(await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
        Assert.True((await fixture.Permissions!.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = (await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments))!;
        Assert.NotNull(capability);
        var operation = new DataHomeRecordUpdateOperation(fixture.Repository, fixture.Authority!, broker);
        var changed = DataRecordUpdateIntent.Capture(fixture.StoreID, workbook.Id, workbook.Version, workbook.RevisionId, table.Id, recordID, values);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(changed, capability));
        var result = await operation.ExecuteAsync(intent, capability);
        var committed = !invalid && !fromForm;
        Assert.Equal(committed, result.Committed);
        Assert.Equal(!invalid, result.OutcomeKnown); Assert.Equal(!invalid, result.AuditRecorded);
        Assert.Equal(fromForm ? "SourceAuthorityUnavailable" : invalid ? "DataOutcomeUnconfirmed" : "DataRecordUpdated", result.Code);
        if (invalid)
        {
            var originalFinish = await operation.FinishAsync(capability);
            Assert.False(originalFinish.OutcomeKnown); Assert.False(originalFinish.AuditRecorded);
        }
        var reopened = (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!;
        Assert.Equal(committed ? 3 : 2, reopened.Version);
        Assert.Equal(committed ? "42" : "1", DataTableIdentity.ReadCell(reopened, table.Id, recordID, fieldID)!.Value);
        Assert.Equal(DataCellKind.Number, DataTableIdentity.ReadCell(reopened, table.Id, recordID, fieldID)!.Kind);
        var recovery = new DataRecordMutationRecovery(new DataWorkbookRepository(fixture.Paths), fixture.Authority!, fixture.Actor!);
        var receipt = await recovery.ReadAsync(intent);
        if (!committed) Assert.Null(receipt);
        else
        {
            Assert.NotNull(receipt); Assert.Equal(intent.OperationID, receipt!.OperationID);
            Assert.Equal(intent.PayloadSHA256, receipt.PayloadSHA256); Assert.Equal(intent.Origin, receipt.Origin);
            Assert.Equal(fromForm, receipt.Origin is not null);
            var receiptConflict = DataRecordUpdateIntent.Capture(fixture.StoreID, workbook.Id, intent.Version,
                intent.RevisionID, table.Id, recordID, values, intent.OperationID, intent.Origin);
            await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ReadAsync(receiptConflict));
            Assert.Equal(3, (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!.Version);
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(intent, capability));
        Assert.Equal(invalid ? HomePermissionRequestState.Executing : committed ? HomePermissionRequestState.Succeeded : HomePermissionRequestState.Failed,
            (await fixture.Permissions.GetAuthorizationAsync(pending.RequestId)).State);
        var completedAudits = (await fixture.Permissions.GetSnapshotAsync()).RecentAuditEvents.Where(
            item => item.RequestId == pending.RequestId && item.Kind == HomePermissionAuditKind.ExecutionCompleted);
        if (invalid) Assert.Empty(completedAudits); else Assert.Single(completedAudits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Relationship_commit_rechecks_actual_actor_or_binding_under_physical_workbook_lease(bool revokeBinding)
    {
        using var fixture = new Fixture(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token; await fixture.InitializeAsync(token);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0]; sheet.SetCell(0, 0, "ID"); sheet.SetCell(1, 0, "1");
        var table = new DataTableDefinition { Name = "Items", SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var key = new DataKeyDefinition(Guid.NewGuid(), "ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "ID", DataFieldType.Integer, false)], [key]).Workbook!;
        var saved = await fixture.Repository.SaveAsync(workbook, "Typed table", (await fixture.CaptureAsync(token))!, token);
        var definition = new DataRelationshipDefinition(Guid.NewGuid(), "Self", table.Id, [table.Fields[0].FieldID],
            table.Id, key.KeyID, DataRelationshipCardinality.OneToOne, false, 0);
        var intent = DataRelationshipUpdateIntent.Capture(fixture.StoreID, workbook, DataRelationshipMutationKind.Upsert, definition, null);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var request = await broker.AuthorizeAsync("data", DataRelationshipUpdateIntent.ActionID, intent.Scopes, intent.Arguments,
            "Add self reference", null, "actual-relationship-lease", token);
        Assert.True((await fixture.Permissions!.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded);
        var capability = (await broker.BeginExecutionCapabilityAsync(request.RequestId, intent.Arguments, token))!;
        var before = await File.ReadAllBytesAsync(saved.CurrentPath, token);
        var lockPath = Path.Combine(fixture.Paths.DataDirectory, "Data", "Workbooks", ".locks", workbook.Id.ToString("D") + ".lock");
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var captured = new SchemaCaptureBarrier(fixture.Authority!);
        var execution = new DataHomeRelationshipUpdateOperation(fixture.Repository, captured, broker).ExecuteAsync(intent, capability, token);
        await captured.Captured.Task.WaitAsync(token); Assert.False(execution.IsCompleted);
        if (!revokeBinding) fixture.Actor!.Current = fixture.Actor.Current with { AuthenticationRevision = "renewed" };
        else
        {
            var record = Assert.Single((await fixture.Home.ReadAsync(token)).State!.Records, item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await fixture.Home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision, token)).IsSuccess);
        }
        lease.Dispose(); var result = await execution;
        Assert.False(result.Committed); Assert.Equal("PermissionDenied", result.Code); Assert.True(result.AuditRecorded);
        Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath, token));
        Assert.Empty((await fixture.Repository.LoadAsync(workbook.Id, token))!.Relationships);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(saved.CurrentPath)!, "*.tmp"));
    }

    [Fact]
    public async Task Actual_relationship_owner_port_creates_edits_and_removes_self_reference_without_changing_records()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Employee"); sheet.SetCell(0, 1, "Manager");
        sheet.SetCell(1, 0, "1"); sheet.SetCell(1, 1, "2"); sheet.SetCell(2, 0, "2"); sheet.SetCell(2, 1, "1");
        var table = new DataTableDefinition { Name = "Employees", SheetId = sheet.Id, Range = new() { EndRow = 2, EndColumn = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var key = new DataKeyDefinition(Guid.NewGuid(), "Employee ID", DataKeyKind.Primary, [table.Fields[0].FieldID]);
        workbook = DataTableDesign.SetSchema(workbook, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "Employee", DataFieldType.Integer, false),
             new(table.Fields[1].FieldID, "Manager", DataFieldType.Integer, false)], [key]).Workbook!;
        await fixture.Repository.SaveAsync(workbook, "Canonical employee schema", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var cells = JsonSerializer.Serialize(workbook.Sheets.Select(item => item.Cells));
        var records = table.Records.Select(record => record.RecordID).ToArray();
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var designer = new DataHomeRelationshipDesigner(fixture.Repository, fixture.Authority!, fixture.Actor!, broker);
        var definition = new DataRelationshipDefinition(Guid.NewGuid(), "Reports to", table.Id, [table.Fields[1].FieldID],
            table.Id, key.KeyID, DataRelationshipCardinality.OneToMany, false, 0);
        var first = await designer.ReviewAsync(workbook.Id, workbook.Version, workbook.RevisionId,
            DataRelationshipMutationKind.Upsert, definition, null);
        Assert.Equal("ApprovalRequired", (await designer.CommitAsync(first)).Code);
        Assert.True((await fixture.Permissions!.DecideAsync(first.RequestID, HomeApprovalChoice.Accept)).Succeeded);
        var created = await designer.CommitAsync(first); Assert.True(created.Committed); Assert.True(created.AuditRecorded);
        workbook = created.Workbook!; Assert.Equal(3, workbook.Version);
        definition = Assert.Single(workbook.Relationships); Assert.Equal(1, definition.Revision);
        var update = await designer.ReviewAsync(workbook.Id, workbook.Version, workbook.RevisionId,
            DataRelationshipMutationKind.Upsert, definition with { Name = "Manager link", Optional = true }, definition.Revision);
        Assert.True((await fixture.Permissions.DecideAsync(update.RequestID, HomeApprovalChoice.Accept)).Succeeded);
        var updated = await designer.CommitAsync(update); Assert.True(updated.Committed);
        workbook = updated.Workbook!; Assert.Equal(4, workbook.Version);
        definition = Assert.Single(workbook.Relationships); Assert.Equal(2, definition.Revision); Assert.Equal("Manager link", definition.Name);
        var remove = await designer.ReviewAsync(workbook.Id, workbook.Version, workbook.RevisionId,
            DataRelationshipMutationKind.Remove, definition, definition.Revision);
        Assert.True((await fixture.Permissions.DecideAsync(remove.RequestID, HomeApprovalChoice.Accept)).Succeeded);
        var removed = await designer.CommitAsync(remove); Assert.True(removed.Committed);
        workbook = removed.Workbook!; Assert.Equal(5, workbook.Version); Assert.Empty(workbook.Relationships);
        Assert.Equal(cells, JsonSerializer.Serialize(workbook.Sheets.Select(item => item.Cells)));
        Assert.Equal(records, workbook.Tables.Single(item => item.Id == table.Id).Records.Select(record => record.RecordID));
        var recovered = await designer.CommitAsync(remove); Assert.True(recovered.Committed);
        Assert.Equal("DataRelationshipAlreadyCommitted", recovered.Code); Assert.Equal(5, recovered.Workbook!.Version);
        Assert.Equal(DataRelationshipMutationKind.Remove, (await new DataRelationshipMutationRecovery(fixture.Repository,
            fixture.Authority!, fixture.Actor!).ReadAsync(remove.Intent))!.Kind);
    }

    [Fact]
    public async Task Caller_captured_schema_cannot_misrepresent_actual_record_impact_in_Home_review()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
        var table = new DataTableDefinition { SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var saved = await fixture.Repository.SaveAsync(workbook, "Canonical table", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var fabricated = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!; fabricated.Normalize();
        DataSpreadsheetOperations.InsertRows(fabricated.Sheets[0], 1);
        var intent = DataTableSchemaUpdateIntent.Capture(fixture.StoreID, fabricated, table.Id, null,
            [new(table.Fields[0].FieldID, "Amount", DataFieldType.Integer)], []);
        Assert.Equal(2, intent.Arguments.GetProperty("recordsValidated").GetInt32());
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var request = await broker.AuthorizeAsync("data", DataTableSchemaUpdateIntent.ActionID, intent.Scopes,
            intent.Arguments, "Misrepresented record impact", null, "actual-data-schema-preview-test");
        Assert.True((await fixture.Permissions!.DecideAsync(request.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = (await broker.BeginExecutionCapabilityAsync(request.RequestId, intent.Arguments))!;
        var before = await File.ReadAllBytesAsync(saved.CurrentPath);
        var result = await new DataHomeTableSchemaUpdateOperation(fixture.Repository, fixture.Authority!, broker).ExecuteAsync(intent, capability);
        Assert.False(result.Committed); Assert.Equal("DataSchemaPreviewConflict", result.Code); Assert.True(result.AuditRecorded);
        Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath));
        Assert.Null(await new DataSchemaMutationRecovery(fixture.Repository, fixture.Authority!, fixture.Actor!).ReadAsync(intent));
    }

    [Fact]
    public async Task Actual_owner_designer_reviews_saved_schema_and_recovers_committed_receipt_without_replaying()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
        var table = new DataTableDefinition { SheetId = sheet.Id, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        await fixture.Repository.SaveAsync(workbook, "Canonical table", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var designer = new DataHomeTableSchemaDesigner(fixture.Repository, fixture.Authority!, fixture.Actor!, broker);
        var review = await designer.ReviewAsync(workbook.Id, table.Id, workbook.Version, workbook.RevisionId, null,
            [new(table.Fields[0].FieldID, "Amount", DataFieldType.Integer, false)],
            [new(Guid.NewGuid(), "Amount", DataKeyKind.Primary, [table.Fields[0].FieldID])]);
        Assert.Equal("ApprovalRequired", (await designer.CommitAsync(review)).Code);
        Assert.Equal(2, (await fixture.Repository.LoadAsync(workbook.Id, CancellationToken.None))!.Version);
        Assert.True((await fixture.Permissions!.DecideAsync(review.RequestID, HomeApprovalChoice.Accept)).Succeeded);
        var result = await designer.CommitAsync(review); Assert.True(result.Committed); Assert.True(result.AuditRecorded);
        Assert.Equal(3, result.Workbook!.Version);
        var recovered = await designer.CommitAsync(review); Assert.True(recovered.Committed);
        Assert.Equal("DataSchemaAlreadyCommitted", recovered.Code); Assert.Equal(3, recovered.Workbook!.Version);
        Assert.Single((await fixture.Permissions.GetSnapshotAsync()).RecentAuditEvents,
            item => item.RequestId == review.RequestID && item.Kind == HomePermissionAuditKind.ExecutionCompleted);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Exact_schema_approval_preserves_actual_records_and_rechecks_owner_under_writer_lease(int change)
    {
        using var fixture = new Fixture(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var token = timeout.Token; await fixture.InitializeAsync(token);
        var workbook = fixture.Workbook; var sheet = workbook.Sheets[0];
        sheet.SetCell(0, 0, "Amount"); sheet.SetCell(1, 0, "1", kind: DataCellKind.Number);
        var table = new DataTableDefinition { SheetId = sheet.Id, HasHeaders = true, Range = new() { EndRow = 1 } };
        DataTableIdentity.Initialize(workbook, table); workbook.Tables.Add(table);
        var saved = await fixture.Repository.SaveAsync(workbook, "Canonical table", (await fixture.CaptureAsync(token))!, token);
        var fieldID = table.Fields[0].FieldID; var recordID = table.Records[0].RecordID;
        var fields = new List<DataFieldDefinition> { new(fieldID, "Amount", DataFieldType.Integer, false) };
        var key = new DataKeyDefinition(Guid.NewGuid(), "Amount", DataKeyKind.Primary, [fieldID]);
        var intent = DataTableSchemaUpdateIntent.Capture(fixture.StoreID, workbook, table.Id, null, fields, [key]);
        fields[0] = new(fieldID, "Changed after review", DataFieldType.Text);
        Assert.Equal("Amount", Assert.Single(intent.Schema.Fields).Name);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(fixture.Actor!,
            [new DataWorkbookMutationAccessResolver(fixture.Repository, fixture.Authority!)]), fixture.Permissions!);
        var pending = await broker.AuthorizeAsync("data", DataTableSchemaUpdateIntent.ActionID, intent.Scopes, intent.Arguments,
            "Declare typed table key", null, "actual-data-schema-session");
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
        Assert.Null(await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
        Assert.True((await fixture.Permissions!.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var capability = (await broker.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments))!;
        Assert.NotNull(capability);
        var before = await File.ReadAllBytesAsync(saved.CurrentPath, token);
        var lockPath = Path.Combine(fixture.Paths.DataDirectory, "Data", "Workbooks", ".locks", workbook.Id.ToString("D") + ".lock");
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var captured = new SchemaCaptureBarrier(fixture.Authority!);
        var operation = new DataHomeTableSchemaUpdateOperation(fixture.Repository, captured, broker);
        var execution = operation.ExecuteAsync(intent, capability, token);
        await captured.Captured.Task.WaitAsync(token);
        Assert.False(execution.IsCompleted);
        if (change == 0) fixture.Actor!.Current = fixture.Actor.Current with { AuthenticationRevision = "revoked" };
        if (change == 1)
        {
            var record = Assert.Single((await fixture.Home.ReadAsync(token)).State!.Records, item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await fixture.Home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision, token)).IsSuccess);
        }
        lease.Dispose();
        var result = await execution; Assert.Equal(change == -1, result.Committed); Assert.True(result.AuditRecorded);
        var reopened = (await fixture.Repository.LoadAsync(workbook.Id, token))!;
        Assert.Equal(change == -1 ? 3 : 2, reopened.Version);
        Assert.Equal("1", DataTableIdentity.ReadCell(reopened, table.Id, recordID, fieldID)!.Value);
        var recovery = new DataSchemaMutationRecovery(new DataWorkbookRepository(fixture.Paths), fixture.Authority!, fixture.Actor!);
        if (change == -1)
        {
            Assert.Equal(key.KeyID, Assert.Single(reopened.Tables.Single(item => item.Id == table.Id).RelationalSchema!.Keys).KeyID);
            Assert.Equal(intent.PayloadSHA256, (await recovery.ReadAsync(intent, token))!.PayloadSHA256);
            Assert.Equal(3, (await fixture.Repository.LoadAsync(workbook.Id, token))!.Version);
        }
        else
        {
            Assert.Equal("PermissionDenied", result.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(saved.CurrentPath, token));
            if (change == 0) Assert.Null(await recovery.ReadAsync(intent, token)); // A fresh authenticated session can read its own unchanged workbook.
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => recovery.ReadAsync(intent, token));
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(intent, capability, token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Held_workbook_lease_rechecks_actor_binding_and_actual_root(int change)
    {
        using var fixture = new Fixture(); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var token = timeout.Token; await fixture.InitializeAsync(token);
        var workbook = fixture.Workbook; var revision = workbook.RevisionId;
        var current = fixture.Saved!.CurrentPath; var before = await File.ReadAllBytesAsync(current, token);
        var admission = await fixture.CaptureAsync(token); Assert.NotNull(admission);
        var lockPath = Path.Combine(fixture.Paths.DataDirectory, "Data", "Workbooks", ".locks", workbook.Id.ToString("D") + ".lock");
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        workbook.Title = "Denied pending write";
        var pending = fixture.Repository.SaveAsync(workbook, "Must be denied", admission!, token);
        Assert.False(pending.IsCompleted);
        if (change == 0) fixture.Actor!.Current = fixture.Actor.Current with { AuthenticationRevision = "changed" };
        else if (change == 1) fixture.Actor!.Current = fixture.Actor.Current with { ActorId = "other-principal" };
        else if (change == 2)
        {
            var record = Assert.Single((await fixture.Home.ReadAsync()).State!.Records, item => item.RecordType == "home.local-store-ownership");
            var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await fixture.Home.WriteAsync(record with { Revision = record.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked" }) }, record.Revision)).IsSuccess);
        }
        else
        {
            using var foreign = new Paths(); var settings = new VersionedAtomicSettingsStore(foreign);
            var identity = await settings.GetStoreIdentityAsync(token); Assert.NotEqual(fixture.StoreID, identity.StoreId);
            File.Copy(Path.Combine(foreign.DataDirectory, "settings.json"), Path.Combine(fixture.Paths.DataDirectory, "settings.json"), true);
        }
        lease.Dispose();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => pending);
        Assert.Equal(before, await File.ReadAllBytesAsync(current, token));
        Assert.Equal(1, workbook.Version); Assert.Equal(revision, workbook.RevisionId);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(current)!, "*.tmp"));
    }

    [Fact]
    public async Task Final_publication_denial_preserves_current_and_backup_after_temporary_preparation()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var second = await fixture.Repository.SaveAsync(fixture.Workbook, "Second", (await fixture.CaptureAsync(CancellationToken.None))!, CancellationToken.None);
        var current = File.ReadAllBytes(second.CurrentPath); var backup = File.ReadAllBytes(second.BackupPath);
        var admission = (await fixture.CaptureAsync(CancellationToken.None))!;
        var phases = new List<DataWorkbookCommitPhase>();
        var switcher = new SwitchAtPublication(admission, fixture.Actor!, phases);
        fixture.Workbook.Title = "Denied after flush";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Repository.SaveAsync(fixture.Workbook, "Denied", switcher, CancellationToken.None));
        Assert.Equal(new[] { DataWorkbookCommitPhase.Admission, DataWorkbookCommitPhase.Publication }, phases);
        Assert.Equal(current, File.ReadAllBytes(second.CurrentPath)); Assert.Equal(backup, File.ReadAllBytes(second.BackupPath));
        Assert.Equal(2, fixture.Workbook.Version);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(second.CurrentPath)!, "*.tmp"));
    }

    [Fact]
    public async Task Real_Home_receipt_allows_exact_commit_without_reentering_Data_evidence()
    {
        using var fixture = new Fixture(); await fixture.InitializeAsync(CancellationToken.None);
        var admission = (await fixture.CaptureAsync(CancellationToken.None))!;
        fixture.Evidence!.RejectReads = true;
        fixture.Workbook.Title = "Authorized";
        var saved = await fixture.Repository.SaveAsync(fixture.Workbook, "Exact owner commit", admission, CancellationToken.None);
        Assert.Equal(2, saved.Version);
        Assert.Equal("Authorized", (await new DataWorkbookRepository(fixture.Paths).LoadAsync(fixture.Workbook.Id, CancellationToken.None))!.Title);
    }

    [Fact]
    public async Task Existing_unreadable_Data_is_not_reported_as_empty_for_binding()
    {
        using var paths = new Paths(); var directory = Path.Combine(paths.DataDirectory, "Data", "Workbooks", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "current.json"), "unreadable");
        var repository = new DataWorkbookRepository(paths); var identity = await repository.GetStoreIdentityAsync(CancellationToken.None);
        Assert.Null(await new DataLocalStoreEvidenceProvider(repository).ReadAsync(identity.StoreId.ToString("D"), CancellationToken.None));
    }

    private sealed class SwitchAtPublication(IDataWorkbookCommitAdmission inner, MutableActor actor,
        List<DataWorkbookCommitPhase> phases) : IDataWorkbookCommitAdmission
    {
        public ValueTask<bool> CheckAsync(DataWorkbookCommitContext context, CancellationToken cancellationToken)
        {
            phases.Add(context.Phase);
            if (context.Phase == DataWorkbookCommitPhase.Publication) actor.Current = actor.Current with { AuthenticationRevision = "changed-after-flush" };
            return inner.CheckAsync(context, cancellationToken);
        }
    }
    private sealed class MutableActor(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = actor;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Evidence(DataLocalStoreEvidenceProvider inner) : IHomeLocalStoreEvidenceProvider
    {
        public string ResourceKind => "data";
        public bool RejectReads { get; set; }
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken) => RejectReads
            ? throw new InvalidOperationException("Data evidence was reentered under the workbook lease.") : inner.ReadAsync(storeId, cancellationToken);
    }
    private sealed class SchemaCaptureBarrier(IDataWorkbookCommitAuthority actual) : IDataWorkbookCommitAuthority
    {
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(Guid storeID, Guid workbookID, int expectedVersion,
            Guid expectedRevisionID, string actionID, AuthenticatedResourceActor expectedActor, CancellationToken cancellationToken)
        {
            var admission = await actual.CaptureAsync(storeID, workbookID, expectedVersion, expectedRevisionID,
                actionID, expectedActor, cancellationToken);
            if (admission is not null) Captured.TrySetResult();
            return admission;
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Paths Paths { get; } = new();
        public FileHomeCoreStateStore Home { get; }
        public DataWorkbookRepository Repository { get; }
        public DataWorkbook Workbook { get; } = DataWorkbook.Create("Owned");
        public MutableActor? Actor { get; private set; }
        public Evidence? Evidence { get; private set; }
        public DataLocalStoreAuthority? Authority { get; private set; }
        public HomePermissionTrustService? Permissions { get; private set; }
        public Guid StoreID { get; private set; }
        public DataSaveResult? Saved { get; private set; }
        public Fixture() { Home = new(Path.Combine(Paths.DataDirectory, "home.json")); Repository = new(Paths); }
        public async Task InitializeAsync(CancellationToken token)
        {
            var profiles = new HomeLocalProfileIdentity(Home, new OperatingSystemPrincipalSource());
            Actor = new((await profiles.GetCurrentAsync(token))!);
            Evidence = new(new DataLocalStoreEvidenceProvider(Repository));
            var policies = new DataMutationActionPolicies(); Permissions = new(Home, policies.TryGet);
            var ownership = new HomeLocalStoreOwnership(Home, profiles, new HomeLocalStoreEvidenceRegistry([Evidence]), Permissions);
            Authority = new(Repository, Actor, new HomeResourceStoreOwnershipAuthority(ownership, Actor));
            StoreID = (await Repository.GetStoreIdentityAsync(token)).StoreId;
            Assert.Null(await Authority.CaptureAsync(StoreID, Workbook.Id, 0, Guid.Empty, "data.workbook.create", Actor.Current, token));
            await ownership.BindNewEmptyAsync("data", StoreID.ToString("D"));
            var admission = await Authority.CaptureAsync(StoreID, Workbook.Id, 0, Guid.Empty, "data.workbook.create", Actor.Current, token);
            Assert.NotNull(admission);
            Saved = await Repository.SaveAsync(Workbook, "Create owned", admission!, token);
        }
        public ValueTask<IDataWorkbookCommitAdmission?> CaptureAsync(CancellationToken token) =>
            Authority!.CaptureAsync(StoreID, Workbook.Id, Workbook.Version, Workbook.RevisionId, "data.workbook.save", Actor!.Current, token);
        public void Dispose() => Paths.Dispose();
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-data-home", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
