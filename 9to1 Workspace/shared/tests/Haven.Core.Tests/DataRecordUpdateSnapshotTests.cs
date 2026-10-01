using System.Text.Json;
using Haven.Application;

namespace Haven.Core.Tests;

public sealed class DataRecordUpdateSnapshotTests
{
    [Fact]
    public void Pending_operation_roundtrip_retains_exact_approval_payload_and_does_not_create_a_capability()
    {
        var intent = Create();
        var snapshot = DataRecordUpdateSnapshot.Capture(intent);
        var reopened = JsonSerializer.Deserialize<DataRecordUpdateSnapshot>(JsonSerializer.Serialize(snapshot))!.Restore();
        Assert.Equal(intent.OperationID, reopened.OperationID);
        Assert.Equal(intent.Arguments.GetRawText(), reopened.Arguments.GetRawText());
        Assert.Equal(intent.PayloadSHA256, reopened.PayloadSHA256);
        Assert.Equal(intent.Origin, reopened.Origin);
    }

    [Fact]
    public void Altered_stored_value_or_future_schema_cannot_restore_the_original_reviewed_operation()
    {
        var snapshot = DataRecordUpdateSnapshot.Capture(Create());
        var key = Assert.Single(snapshot.Values).Key;
        var altered = snapshot with { Values = new Dictionary<Guid, DataScalarRecordValue>
        { [key] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(999)) } };
        Assert.Throws<InvalidDataException>(() => altered.Restore());
        Assert.Throws<InvalidDataException>(() => (snapshot with { SchemaVersion = 2 }).Restore());
    }

    private static DataRecordUpdateIntent Create() => DataRecordUpdateIntent.Capture(Guid.NewGuid(), Guid.NewGuid(), 2,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new Dictionary<Guid, DataScalarRecordValue>
        { [Guid.NewGuid()] = new(DataCellKind.Number, JsonSerializer.SerializeToElement(42)) }, Guid.NewGuid(),
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3));
}
