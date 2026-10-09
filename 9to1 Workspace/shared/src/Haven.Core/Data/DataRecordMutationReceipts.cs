using System.Text.Json;

namespace Haven.Core;

public sealed record DataRecordMutationOrigin(Guid FormID, Guid FormVersionID, Guid ResponseID, long ResponseRevision, Guid? SourceStoreID = null);
public sealed record DataRecordMutationReceipt(Guid OperationID, string PayloadSHA256, Guid TableID, Guid RecordID,
    int BaseVersion, Guid BaseRevisionID, DateTimeOffset AppliedAt, DataRecordMutationOrigin? Origin, int SourceAdmissionVersion = 0);

/// <summary>Receipts live in the existing canonical workbook and commit with its cells. They are
/// operation history, not an authority grant or a substitute for the retained Forms response.</summary>
public static class DataRecordMutationReceipts
{
    public const string MetadataKey = "haven.data.recordMutationReceipts.v1";
    public const int MaximumReceipts = 10000;
    private const int MaximumBytes = 8 * 1024 * 1024;

    public static IReadOnlyList<DataRecordMutationReceipt> Read(DataWorkbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (!workbook.Metadata.TryGetValue(MetadataKey, out var json)) return [];
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Data mutation receipt history exceeds its bound.");
        DataRecordMutationReceipt[] records;
        try { records = JsonSerializer.Deserialize<DataRecordMutationReceipt[]>(json) ?? throw new InvalidDataException("Missing Data mutation receipt history."); }
        catch (JsonException error) { throw new InvalidDataException("Unreadable Data mutation receipt history.", error); }
        if (records.Length > MaximumReceipts || records.Any(item => item is null) || records.Select(item => item.OperationID).Distinct().Count() != records.Length)
            throw new InvalidDataException("Invalid Data mutation operation identity history.");
        foreach (var record in records) Validate(record);
        return Array.AsReadOnly(records);
    }

    public static void Add(DataWorkbook workbook, DataRecordMutationReceipt receipt)
    {
        Validate(receipt);
        var existing = Read(workbook);
        if (existing.Any(item => item.OperationID == receipt.OperationID)) throw new InvalidOperationException("DataMutationOperationAlreadyRecorded");
        if (existing.Count == MaximumReceipts) throw new InvalidOperationException("DataMutationReceiptCapacityReached");
        var json = JsonSerializer.Serialize(existing.Append(receipt));
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidOperationException("DataMutationReceiptCapacityReached");
        workbook.Metadata[MetadataKey] = json;
    }

    private static void Validate(DataRecordMutationReceipt receipt)
    {
        if (receipt is null || receipt.OperationID == Guid.Empty || receipt.TableID == Guid.Empty || receipt.RecordID == Guid.Empty
            || receipt.BaseVersion < 1 || receipt.BaseRevisionID == Guid.Empty || receipt.AppliedAt == default
            || receipt.PayloadSHA256 is not { Length: 64 } || receipt.PayloadSHA256.Any(value => !char.IsAsciiHexDigit(value))
            || receipt.SourceAdmissionVersion is not (0 or 1)
            || receipt.SourceAdmissionVersion == 1 && (receipt.Origin?.SourceStoreID is not { } sourceID || sourceID == Guid.Empty)
            || receipt.Origin is { } origin && (origin.FormID == Guid.Empty || origin.FormVersionID == Guid.Empty
                || origin.ResponseID == Guid.Empty || origin.ResponseRevision < 1 || origin.SourceStoreID == Guid.Empty))
            throw new InvalidDataException("Invalid canonical Data record mutation receipt.");
    }
}
