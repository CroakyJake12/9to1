using System.Text.Json;

namespace CakeOS.Apps.Boards.Contract;

/// <summary>Validates and decodes an exact 9to1 board document without requiring a filesystem path.</summary>
public static class HavenBoardDocumentCodec
{
    public const int MaximumDocumentBytes = 40 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Deserializes the supplied bounded document bytes, applying the same schema migrations and
    /// domain validators used by the local file store. Future schemas are refused explicitly.
    /// </summary>
    public static HavenBoardDocument Deserialize(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty) throw new InvalidDataException("The .9to1board document is empty.");
        if (content.Length > MaximumDocumentBytes)
            throw new InvalidDataException($"The .9to1board document exceeds the {MaximumDocumentBytes} byte read limit.");
        try
        {
            using var json = JsonDocument.Parse(content);
            return DeserializeRoot(json.RootElement, expectedBoardId: null, out _);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("The .9to1board contains invalid JSON.", error);
        }
        catch (NotSupportedException error)
        {
            throw new InvalidDataException("The .9to1board contains unsupported data.", error);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidDataException("The .9to1board contains invalid field data.", error);
        }
    }

    internal static HavenBoardDocument DeserializeRoot(JsonElement root, string? expectedBoardId,
        out bool migrated)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("format", out var format) ||
            !string.Equals(format.GetString(), HavenBoardDocument.FormatIdentity, StringComparison.Ordinal) ||
            !root.TryGetProperty("schemaVersion", out var versionElement) ||
            !versionElement.TryGetInt32(out var version))
            throw new InvalidDataException("The .9to1board is missing its required format identity or schema version.");
        if (version > HavenBoardDocument.CurrentSchemaVersion)
            throw new UnsupportedHavenBoardDocumentVersionException(version);
        if (version < 0)
            throw new InvalidDataException("The .9to1board schema version is invalid.");

        HavenBoardDocument? document = version switch
        {
            3 => root.Deserialize<HavenBoardDocument>(Json),
            2 => root.Deserialize<HavenBoardDocument>(Json),
            1 => MigrateSchemaOne(root),
            0 => MigrateSchemaZero(root),
            _ => null
        };
        if (document is null || document.DocumentId == Guid.Empty || document.CreatedUtc == default ||
            document.ModifiedUtc == default || (document.Snapshot is null && document.RichNotes is null))
            throw new InvalidDataException("The .9to1board is incomplete or fails validation.");
        if (document.Snapshot is not null)
        {
            if (expectedBoardId is not null &&
                !string.Equals(document.Snapshot.Id, expectedBoardId, StringComparison.Ordinal))
                throw new InvalidDataException("The .9to1board board identity does not match the requested board.");
            HavenBoardReducer.Validate(document.Snapshot);
        }
        if (document.RichNotes is not null)
        {
            if (version < 3) HavenRichNotesV3Migration.Upgrade(document.RichNotes);
            HavenRichNotesValidator.Validate(document.RichNotes);
        }
        migrated = version < HavenBoardDocument.CurrentSchemaVersion;
        return document;
    }

    private static HavenBoardDocument? MigrateSchemaOne(JsonElement root)
    {
        var document = root.Deserialize<HavenBoardDocument>(Json);
        if (document?.Snapshot is null) return null;
        return document with { RichNotes = HavenRichNotesSeeder.SeedFromSnapshot(document.Snapshot) };
    }

    private static HavenBoardDocument? MigrateSchemaZero(JsonElement root)
    {
        if (!root.TryGetProperty("snapshot", out var snapshotElement)) return null;
        var snapshot = snapshotElement.Deserialize<HavenBoardSnapshot>(Json);
        if (snapshot is null) return null;
        var now = DateTimeOffset.UtcNow;
        return new HavenBoardDocument(HavenBoardDocument.FormatIdentity, HavenBoardDocument.CurrentSchemaVersion,
            Guid.NewGuid(), now, now, snapshot, HavenRichNotesSeeder.SeedFromSnapshot(snapshot));
    }
}
