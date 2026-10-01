using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class DataRecordMutationReceiptTests
{
    [Fact]
    public void Receipt_and_origin_survive_canonical_workbook_roundtrip_and_operation_identity_cannot_repeat()
    {
        var workbook = DataWorkbook.Create(); var receipt = Receipt();
        DataRecordMutationReceipts.Add(workbook, receipt);
        var restored = JsonSerializer.Deserialize<DataWorkbook>(JsonSerializer.Serialize(workbook))!;
        Assert.Equal(receipt, Assert.Single(DataRecordMutationReceipts.Read(restored)));
        var before = restored.Metadata[DataRecordMutationReceipts.MetadataKey];
        Assert.Throws<InvalidOperationException>(() => DataRecordMutationReceipts.Add(restored, receipt with { PayloadSHA256 = new('B', 64) }));
        Assert.Equal(before, restored.Metadata[DataRecordMutationReceipts.MetadataKey]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("{}")]
    [InlineData("not-json")]
    public void Malformed_history_does_not_become_empty_success(string json)
    {
        var workbook = DataWorkbook.Create(); workbook.Metadata[DataRecordMutationReceipts.MetadataKey] = json;
        Assert.Throws<InvalidDataException>(() => DataRecordMutationReceipts.Read(workbook));
    }

    [Fact]
    public void Exact_operation_payload_includes_source_provenance_and_survives_recapture()
    {
        var store = Guid.NewGuid(); var workbook = Guid.NewGuid(); var revision = Guid.NewGuid(); var table = Guid.NewGuid();
        var record = Guid.NewGuid(); var operation = Guid.NewGuid(); var origin = new DataRecordMutationOrigin(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2);
        var values = new Dictionary<Guid, DataScalarRecordValue> { [Guid.NewGuid()] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(42)) };
        var first = DataRecordUpdateIntent.Capture(store, workbook, 3, revision, table, record, values, operation, origin);
        var same = DataRecordUpdateIntent.Capture(store, workbook, 3, revision, table, record, values, operation, origin);
        Assert.Equal(first.PayloadSHA256, same.PayloadSHA256); Assert.Equal(first.Arguments.GetRawText(), same.Arguments.GetRawText());
        var changed = DataRecordUpdateIntent.Capture(store, workbook, 3, revision, table, record, values, operation, origin with { ResponseRevision = 3 });
        Assert.NotEqual(first.PayloadSHA256, changed.PayloadSHA256);
    }

    private static DataRecordMutationReceipt Receipt() => new(Guid.NewGuid(), new('A', 64), Guid.NewGuid(), Guid.NewGuid(), 2,
        Guid.NewGuid(), DateTimeOffset.UtcNow, new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2));
}
