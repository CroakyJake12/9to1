// Report-owned regression source only. This file is deliberately outside the owning
// product/test projects and was not compiled or executed; port these cases when the
// production Forms/Core test paths are assigned.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application;
using Haven.Core.Forms;
using Haven.Infrastructure;
using Xunit;

public sealed class FormTableInputBoundedValidationRegressions
{
    private static FormTableInputDefinition Definition(Guid? fieldID = null, int columnCount = 1) => new(
        fieldID ?? Guid.NewGuid(),
        Enumerable.Range(0, columnCount)
            .Select(index => new FormTableColumn(Guid.NewGuid(), $"Column {index}", FormTableCellType.Text)).ToArray());

    [Fact]
    public void NullRowsReturnsTypedIssue()
    {
        var definition = Definition();
        var issues = FormTableInput.Validate(definition,
            new FormTableInputResponse(definition.FieldID, null!));

        Assert.Collection(issues, issue =>
        {
            Assert.Equal("MissingRows", issue.Code);
            Assert.Equal(definition.FieldID, issue.FieldID);
            Assert.Null(issue.RowID);
        });
    }

    [Fact]
    public void NullRowReturnsTypedIssueAndDoesNotThrow()
    {
        var definition = Definition();
        var issues = FormTableInput.Validate(definition,
            new FormTableInputResponse(definition.FieldID, new FormTableResponseRow[] { null! }));

        Assert.Contains(issues, issue => issue.Code == "InvalidRowIdentity" && issue.FieldID == definition.FieldID);
    }

    [Fact]
    public void NullCellsReturnsTypedIssueWithStableRowIdentity()
    {
        var definition = Definition();
        var rowID = Guid.NewGuid();
        var issues = FormTableInput.Validate(definition,
            new FormTableInputResponse(definition.FieldID, new[] { new FormTableResponseRow(rowID, null!) }));

        Assert.Contains(issues, issue => issue.Code == "MissingCells"
            && issue.FieldID == definition.FieldID && issue.RowID == rowID);
    }

    [Fact]
    public void NullSchemaColumnIsRejected()
    {
        var definition = new FormTableInputDefinition(Guid.NewGuid(), new FormTableColumn[] { null! });

        Assert.Throws<ArgumentException>(() => FormTableInput.ValidateDefinition(definition));
    }

    [Fact]
    public void PreviouslySerializableUnusedColumnConstraintsRemainValidInSchemaV1()
    {
        static FormTableInputDefinition Definition(FormTableColumn column) =>
            new(Guid.NewGuid(), new[] { column });

        var textWithUnusedNumericBounds = Definition(new FormTableColumn(Guid.NewGuid(), "Text", FormTableCellType.Text,
            Minimum: 1, Maximum: 10));
        var numberWithUnusedChoiceIDs = Definition(new FormTableColumn(Guid.NewGuid(), "Number", FormTableCellType.Number,
            ChoiceIDs: new[] { "legacy-option" }));
        var textWithUnusedReferenceTable = Definition(new FormTableColumn(Guid.NewGuid(), "Text", FormTableCellType.Text,
            ReferencedTableID: Guid.NewGuid()));

        FormTableInput.ValidateDefinition(textWithUnusedNumericBounds);
        FormTableInput.ValidateDefinition(numberWithUnusedChoiceIDs);
        FormTableInput.ValidateDefinition(textWithUnusedReferenceTable);
    }

    [Fact]
    public void SchemaV1RoundTripPreservesPreviouslySerializableUnusedConstraints()
    {
        var fieldID = Guid.NewGuid();
        var columns = new[]
        {
            new FormTableColumn(Guid.NewGuid(), "Text", FormTableCellType.Text, Minimum: 1, Maximum: 10),
            new FormTableColumn(Guid.NewGuid(), "Number", FormTableCellType.Number, ChoiceIDs: new[] { "old-option" }),
            new FormTableColumn(Guid.NewGuid(), "Text with legacy reference metadata", FormTableCellType.Text,
                ReferencedTableID: Guid.NewGuid())
        };
        var table = new FormTableInputDefinition(fieldID, columns);
        var now = DateTimeOffset.UtcNow;
        var project = new FormProject(FormProject.CurrentSchemaVersion, Guid.NewGuid(), "Legacy-compatible",
            new FormModeDefinition(Guid.NewGuid(), FormModeKind.Form), Array.Empty<FormVersionReference>(),
            new[] { new FormPage(Guid.NewGuid(), "Page", new[] { new FormChildReference(FormChildKind.Field, fieldID) }, new FormLayout()) },
            new[] { new FormField(fieldID, FormFieldKind.TableInput, "Rows", null,
                JsonSerializer.SerializeToElement(new { legacySchema = true }), false, new FormLayout(), Table: table) },
            Array.Empty<FormComponent>(), new FormThemeReference("default"), null, Array.Empty<FormDataBinding>(),
            new FormMarkingScheme(Array.Empty<FormGradeBand>(), FormResultRelease.AfterSubmission),
            Array.Empty<FormQuestionBank>(), new FormPublishingSettings(false),
            new FormAccessPolicy(FormRespondentAccess.OwnerOnly, Array.Empty<Guid>()), new FormRuntimeSettings(),
            now, now, 1);

        var decoded = FormProjectCodec.Decode(FormProjectCodec.Encode(project));
        var decodedTable = Assert.Single(decoded.Fields).Table;
        Assert.NotNull(decodedTable);
        var decodedColumns = decodedTable!.Columns;

        Assert.Equal((decimal?)1, decodedColumns[0].Minimum);
        Assert.Equal((decimal?)10, decodedColumns[0].Maximum);
        Assert.Equal("old-option", Assert.Single(decodedColumns[1].ChoiceIDs!));
        Assert.NotNull(decodedColumns[2].ReferencedTableID);
    }

    [Fact]
    public void SubmittedProjectionPreservesLegacyReferenceAnswerBytes()
    {
        var fieldID = Guid.NewGuid();
        var formID = Guid.NewGuid();
        var tableID = Guid.NewGuid();
        var columnID = Guid.NewGuid();
        var rowID = Guid.NewGuid();
        var table = new FormTableInputDefinition(fieldID, new[]
        {
            new FormTableColumn(columnID, "Related record", FormTableCellType.Reference, ReferencedTableID: tableID)
        });
        var now = DateTimeOffset.UtcNow;
        var project = new FormProject(FormProject.CurrentSchemaVersion, formID, "Legacy response",
            new FormModeDefinition(Guid.NewGuid(), FormModeKind.Form), Array.Empty<FormVersionReference>(),
            new[] { new FormPage(Guid.NewGuid(), "Page", new[] { new FormChildReference(FormChildKind.Field, fieldID) }, new FormLayout()) },
            new[] { new FormField(fieldID, FormFieldKind.TableInput, "Rows", null,
                JsonSerializer.SerializeToElement(new { schema = true }), false, new FormLayout(), Table: table) },
            Array.Empty<FormComponent>(), new FormThemeReference("default"), null, Array.Empty<FormDataBinding>(),
            new FormMarkingScheme(Array.Empty<FormGradeBand>(), FormResultRelease.AfterSubmission),
            Array.Empty<FormQuestionBank>(), new FormPublishingSettings(false),
            new FormAccessPolicy(FormRespondentAccess.OwnerOnly, Array.Empty<Guid>()), new FormRuntimeSettings(),
            now, now, 1);
        var legacyAnswer = JsonSerializer.SerializeToElement(new FormTableInputResponse(fieldID, new[]
        {
            new FormTableResponseRow(rowID, new Dictionary<Guid, JsonElement>
            {
                [columnID] = JsonSerializer.SerializeToElement(new { tableID, recordID = "school-42" })
            })
        }));
        var started = now;
        var submitted = now.AddSeconds(1);
        var response = new FormResponse(Guid.NewGuid(), formID, Guid.NewGuid(), project.Revision, 1,
            FormResponseState.Submitted, started, submitted, new[] { new FormAnswer(fieldID, legacyAnswer) },
            new Dictionary<Guid, FormMarkingResult>(), null, null, null, null, FormResponseDataWriteState.NotRequested);

        var projected = FormResponseSubmissionProjection.Create(project, response);

        Assert.Equal(legacyAnswer.GetRawText(), projected.Answers[fieldID.ToString("D")].Value.GetRawText());
    }

    [Fact]
    public void RequiredTextCellRejectsWhitespaceOnlyValue()
    {
        var fieldID = Guid.NewGuid();
        var columnID = Guid.NewGuid();
        var requiredDefinition = new FormTableInputDefinition(fieldID, new[]
        {
            new FormTableColumn(columnID, "Required text", FormTableCellType.Text, Required: true)
        });
        var optionalDefinition = new FormTableInputDefinition(fieldID, new[]
        {
            new FormTableColumn(columnID, "Optional text", FormTableCellType.Text)
        });
        var rowID = Guid.NewGuid();
        var response = new FormTableInputResponse(fieldID, new[]
        {
            new FormTableResponseRow(rowID,
                new Dictionary<Guid, JsonElement> { [columnID] = JsonSerializer.SerializeToElement(" \t") })
        });

        var required = FormTableInput.Validate(requiredDefinition, response);
        var optional = FormTableInput.Validate(optionalDefinition, response);
        var optionalBlank = FormTableInput.Validate(optionalDefinition, new FormTableInputResponse(fieldID, new[]
        {
            new FormTableResponseRow(rowID,
                new Dictionary<Guid, JsonElement> { [columnID] = JsonSerializer.SerializeToElement(string.Empty) })
        }));

        Assert.Contains(required, issue => issue.Code == "InvalidCellTypeOrValue"
            && issue.FieldID == fieldID && issue.RowID == rowID && issue.ColumnID == columnID);
        Assert.Empty(optional);
        Assert.Empty(optionalBlank);
    }

    [Fact]
    public void ReferenceCellPreservesLegacyIDsAndAcceptsCanonicalDataIDs()
    {
        var fieldID = Guid.NewGuid();
        var columnID = Guid.NewGuid();
        var tableID = Guid.NewGuid();
        var definition = new FormTableInputDefinition(fieldID, new[]
        {
            new FormTableColumn(columnID, "Related record", FormTableCellType.Reference, ReferencedTableID: tableID)
        });
        var rowID = Guid.NewGuid();
        JsonElement Cell(string recordID) => JsonSerializer.SerializeToElement(new { tableID, recordID });

        var invalid = FormTableInput.Validate(definition, new FormTableInputResponse(fieldID, new[]
        {
            new FormTableResponseRow(rowID, new Dictionary<Guid, JsonElement> { [columnID] = Cell("  ") })
        }));
        var legacy = FormTableInput.Validate(definition, new FormTableInputResponse(fieldID, new[]
        {
            new FormTableResponseRow(rowID, new Dictionary<Guid, JsonElement> { [columnID] = Cell("school-42") })
        }));
        var validRecordID = Guid.NewGuid();
        var valid = FormTableInput.Validate(definition, new FormTableInputResponse(fieldID, new[]
        {
            new FormTableResponseRow(rowID, new Dictionary<Guid, JsonElement>
            { [columnID] = Cell(validRecordID.ToString("B")) })
        }));

        Assert.Contains(invalid, issue => issue.Code == "InvalidCellTypeOrValue" && issue.RowID == rowID
            && issue.ColumnID == columnID && issue.FieldID == fieldID);
        Assert.Empty(legacy);
        Assert.Empty(valid);
    }

    [Theory]
    [InlineData("D")]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("P")]
    [InlineData("X")]
    public void CanonicalDataRecordIDTextFormatsRemainAccepted(string format)
    {
        var fieldID = Guid.NewGuid();
        var columnID = Guid.NewGuid();
        var tableID = Guid.NewGuid();
        var definition = new FormTableInputDefinition(fieldID, new[]
        {
            new FormTableColumn(columnID, "Related record", FormTableCellType.Reference, ReferencedTableID: tableID)
        });
        var recordID = Guid.NewGuid();
        var cell = JsonSerializer.SerializeToElement(new { tableID, recordID = recordID.ToString(format) });

        var issues = FormTableInput.Validate(definition, new FormTableInputResponse(fieldID, new[]
        {
            new FormTableResponseRow(Guid.NewGuid(), new Dictionary<Guid, JsonElement> { [columnID] = cell })
        }));

        Assert.Empty(issues);
    }

    [Fact]
    public void ReferenceUniquenessUsesCanonicalGuidIdentity()
    {
        var fieldID = Guid.NewGuid();
        var columnID = Guid.NewGuid();
        var tableID = Guid.NewGuid();
        var recordID = Guid.NewGuid();
        var definition = new FormTableInputDefinition(fieldID, new[]
        {
            new FormTableColumn(columnID, "Related record", FormTableCellType.Reference, ReferencedTableID: tableID)
        }, UniqueColumnIDs: new[] { columnID });
        JsonElement Cell(string id) => JsonSerializer.SerializeToElement(new { tableID, recordID = id });
        var firstRowID = Guid.NewGuid();
        var secondRowID = Guid.NewGuid();

        var issues = FormTableInput.Validate(definition, new FormTableInputResponse(fieldID, new[]
        {
            new FormTableResponseRow(firstRowID,
                new Dictionary<Guid, JsonElement> { [columnID] = Cell(recordID.ToString("D")) }),
            new FormTableResponseRow(secondRowID,
                new Dictionary<Guid, JsonElement> { [columnID] = Cell(recordID.ToString("B")) })
        }));

        Assert.Contains(issues, issue => issue.Code == "DuplicateRow" && issue.FieldID == fieldID
            && issue.RowID == secondRowID && issue.ColumnID is null);
    }

    [Fact]
    public void MismatchedFieldReturnsTypedIssue()
    {
        var definition = Definition();
        var issues = FormTableInput.Validate(definition,
            new FormTableInputResponse(Guid.NewGuid(), Array.Empty<FormTableResponseRow>()));

        Assert.Contains(issues, issue => issue.Code == "FieldIdentityMismatch"
            && issue.FieldID == definition.FieldID);
    }

    [Fact]
    public void OversizedUnknownCellsRefuseBeforeEnumerationAndPreserveSource()
    {
        var definition = Definition(columnCount: 256);
        var rowID = Guid.NewGuid();
        var cells = Enumerable.Range(0, 257)
            .ToDictionary(_ => Guid.NewGuid(), _ => JsonSerializer.SerializeToElement("unchanged"));
        var row = new FormTableResponseRow(rowID, cells);

        var issues = FormTableInput.Validate(definition,
            new FormTableInputResponse(definition.FieldID, new[] { row }));

        Assert.Collection(issues, issue =>
        {
            Assert.Equal("UnexpectedCellCount", issue.Code);
            Assert.Equal(definition.FieldID, issue.FieldID);
            Assert.Equal(rowID, issue.RowID);
        });
        Assert.Equal(257, cells.Count);
        Assert.All(cells.Values, value => Assert.Equal("unchanged", value.GetString()));
    }

    [Fact]
    public void UnknownCellWithinBoundIsReportedRatherThanDropped()
    {
        var definition = Definition();
        var rowID = Guid.NewGuid();
        var unknownColumnID = Guid.NewGuid();
        var value = JsonSerializer.SerializeToElement("retain");
        var cells = new Dictionary<Guid, JsonElement> { [unknownColumnID] = value };

        var issues = FormTableInput.Validate(definition,
            new FormTableInputResponse(definition.FieldID, new[] { new FormTableResponseRow(rowID, cells) }));

        Assert.Collection(issues, issue =>
        {
            Assert.Equal("UnknownColumn", issue.Code);
            Assert.Equal(definition.FieldID, issue.FieldID);
            Assert.Equal(rowID, issue.RowID);
            Assert.Equal(unknownColumnID, issue.ColumnID);
        });
        Assert.Equal("retain", cells[unknownColumnID].GetString());
    }

    [Fact]
    public void OversizedCellsAreRejectedWithoutEnumeratingUnknownKeys()
    {
        var definition = Definition(columnCount: 256);
        var rowID = Guid.NewGuid();
        var row = new FormTableResponseRow(rowID, new OverLimitDictionary());

        var issues = FormTableInput.Validate(definition,
            new FormTableInputResponse(definition.FieldID, new[] { row }));

        Assert.Collection(issues, issue =>
        {
            Assert.Equal("UnexpectedCellCount", issue.Code);
            Assert.Equal(definition.FieldID, issue.FieldID);
            Assert.Equal(rowID, issue.RowID);
        });
    }

    [Fact]
    public async Task EmptyDataOperationIDIsRejectedBeforeAccessingDependencies()
    {
        var service = new FormDataResponseWriteService(null!, null!, null!);

        var result = await service.ReconcileAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);

        Assert.False(result.Success);
        Assert.Equal("InvalidArgument", result.Code);
        Assert.Null(result.Attempt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InaccessibleStoreIdentityReadReturnsUnknownEvidence(bool unauthorized)
    {
        Exception failure = unauthorized ? new UnauthorizedAccessException() : new IOException();
        var store = new EvidenceStore(Guid.NewGuid(), identityFailure: failure);
        var provider = new FormLocalStoreEvidenceProvider(store, store);

        var evidence = await provider.ReadAsync(store.StoreID.ToString("D"), CancellationToken.None);

        Assert.Null(evidence);
    }

    [Fact]
    public async Task CancelledStoreIdentityReadStillPropagatesCancellation()
    {
        var store = new EvidenceStore(Guid.NewGuid(), identityFailure: new OperationCanceledException());
        var provider = new FormLocalStoreEvidenceProvider(store, store);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await provider.ReadAsync(store.StoreID.ToString("D"), CancellationToken.None);
        });
    }

    [Fact]
    public async Task CancellationDuringHeldExportPropagatesBeforeEvidenceIsPublished()
    {
        using var cancellation = new CancellationTokenSource();
        var exportCompletion = new TaskCompletionSource<SettingsExportManifest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new EvidenceStore(Guid.NewGuid(), exportCompletion: exportCompletion);
        var provider = new FormLocalStoreEvidenceProvider(store, store);
        var pending = provider.ReadAsync(store.StoreID.ToString("D"), cancellation.Token).AsTask();
        await store.ExportStarted.Task;

        cancellation.Cancel();
        exportCompletion.SetResult(new SettingsExportManifest
        {
            StoreIdentity = new SettingsStoreIdentity(1, store.StoreID, DateTimeOffset.UtcNow),
            Settings = new Dictionary<string, string>()
        });

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await pending;
        });
    }

    [Fact]
    public async Task MatchingDurableIdentityAndExportProduceEvidence()
    {
        var store = new EvidenceStore(Guid.NewGuid());
        var provider = new FormLocalStoreEvidenceProvider(store, store);

        var evidence = await provider.ReadAsync(store.StoreID.ToString("D"), CancellationToken.None);

        Assert.NotNull(evidence);
        Assert.Equal("forms", evidence!.ResourceKind);
        Assert.Equal(store.StoreID.ToString("D"), evidence.StoreId);
        Assert.Equal("1", evidence.Revision);
        Assert.True(evidence.IsEmpty);
        Assert.True(evidence.AccessibleToCurrentOsPrincipal);
    }

    [Fact]
    public async Task MismatchedExportStoreIDProducesUnknownEvidence()
    {
        var store = new EvidenceStore(Guid.NewGuid(), exportedStoreID: Guid.NewGuid());
        var provider = new FormLocalStoreEvidenceProvider(store, store);

        var evidence = await provider.ReadAsync(store.StoreID.ToString("D"), CancellationToken.None);

        Assert.Null(evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InaccessibleSettingsExportReturnsUnknownEvidence(bool unauthorized)
    {
        Exception failure = unauthorized ? new UnauthorizedAccessException() : new IOException();
        var store = new EvidenceStore(Guid.NewGuid(), exportFailure: failure);
        var provider = new FormLocalStoreEvidenceProvider(store, store);

        var evidence = await provider.ReadAsync(store.StoreID.ToString("D"), CancellationToken.None);

        Assert.Null(evidence);
    }

    private sealed class OverLimitDictionary : IReadOnlyDictionary<Guid, JsonElement>
    {
        private static InvalidOperationException Enumerated() => new("Oversized cell keys must not be enumerated.");
        public int Count => 257;
        public IEnumerable<Guid> Keys => throw Enumerated();
        public IEnumerable<JsonElement> Values => throw Enumerated();
        public JsonElement this[Guid key] => throw Enumerated();
        public bool ContainsKey(Guid key) => throw Enumerated();
        public bool TryGetValue(Guid key, out JsonElement value) { value = default; throw Enumerated(); }
        public IEnumerator<KeyValuePair<Guid, JsonElement>> GetEnumerator() => throw Enumerated();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class EvidenceStore(Guid storeID, Exception? identityFailure = null, Exception? exportFailure = null,
        TaskCompletionSource<SettingsExportManifest>? exportCompletion = null, Guid? exportedStoreID = null)
        : IVersionedSettingsStore, IResourceStoreIdentitySource
    {
        public Guid StoreID { get; } = storeID;
        public TaskCompletionSource<bool> ExportStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken cancellationToken) =>
            identityFailure is not null
                ? new ValueTask<ResourceStoreIdentity>(Task.FromException<ResourceStoreIdentity>(identityFailure))
                : ValueTask.FromResult(new ResourceStoreIdentity(1, StoreID, DateTimeOffset.UtcNow, false));

        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) =>
            exportFailure is not null
                ? Task.FromException<SettingsExportManifest>(exportFailure)
                : exportCompletion is not null
                    ? StartAndWaitForExportAsync()
                : Task.FromResult(new SettingsExportManifest
                {
                    StoreIdentity = new SettingsStoreIdentity(1, exportedStoreID ?? StoreID, DateTimeOffset.UtcNow),
                    Settings = new Dictionary<string, string>()
                });

        private Task<SettingsExportManifest> StartAndWaitForExportAsync()
        {
            ExportStarted.TrySetResult(true);
            return exportCompletion!.Task;
        }

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class => throw new NotSupportedException();
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class => throw new NotSupportedException();
        public Task RemoveAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
