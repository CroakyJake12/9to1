using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Haven.Core;
using NineToOne.Cui.AI;

namespace HavenOS.Home.Core;

/// <summary>Lossless projection of existing canonical NotesBlock payloads. ObjectID is always NotesBlock.Id.</summary>
public static class HomeNotesSharedObjects
{
    public static string ObjectType(NotesBlock block) => block.Kind switch
    {
        NotesBlockKind.Paragraph => "text.paragraph", NotesBlockKind.Heading => "text.heading", NotesBlockKind.Code => "code.block",
        NotesBlockKind.List when block.List?.Kind == NotesListKind.Checklist => "text.checklist",
        NotesBlockKind.List => "text.list", NotesBlockKind.Table => "table",
        _ => throw new NotSupportedException("This Notes family does not yet have a shared semantic adapter.")
    };
    public static HomeProductivityObject Project(NotesBlock block) =>
        new HomeNotesObjectHandler(ObjectType(block)).Create(block.Id, JsonSerializer.SerializeToElement(block));
    public static NotesBlock Read(HomeProductivityObject value)
    {
        var block = JsonSerializer.Deserialize<NotesBlock>(value.Content) ?? throw new InvalidDataException("Missing canonical Notes block.");
        if (block.Id != value.ObjectId || ObjectType(block) != value.ObjectType || value.SchemaVersion != (block.Kind == NotesBlockKind.Paragraph ? 2 : 1))
            throw new InvalidDataException("Canonical Notes and shared object identity/schema disagree.");
        return block;
    }
}
public sealed record HomeProductivityNotesBinding(string ControlId, Guid ObjectId, JsonElement CanonicalBlock);

/// <summary>Shared typed formatting and editing over canonical Notes fields, retaining unrecognised JSON properties.</summary>
public sealed class HomeNotesObjectHandler : IHomeProductivityObjectHandler, IHomeProductivityObjectCloneHandler
{
    private static readonly string[] Types = ["text.paragraph", "text.heading", "text.list", "text.checklist", "code.block", "table"];
    public HomeProductivityObjectSchema Schema { get; }
    public IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions { get; }
    private static JsonElement Json(string value) { using var doc = JsonDocument.Parse(value); return doc.RootElement.Clone(); }
    public HomeNotesObjectHandler(string objectType)
    {
        if (!Types.Contains(objectType, StringComparer.Ordinal)) throw new ArgumentException("Unknown shared Notes family.", nameof(objectType));
        Actions = objectType switch
        {
            "table" => [new("table.cell.text", 1, Json("{\"type\":\"object\",\"properties\":{\"cellId\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"}},\"required\":[\"cellId\",\"text\"],\"additionalProperties\":false}"))],
            "text.checklist" => [new("checklist.check", 1, Json("{\"type\":\"object\",\"properties\":{\"itemId\":{\"type\":\"string\"},\"checked\":{\"type\":\"boolean\"}},\"required\":[\"itemId\",\"checked\"],\"additionalProperties\":false}"))],
            "text.list" => [new("list.item.text", 1, Json("{\"type\":\"object\",\"properties\":{\"itemId\":{\"type\":\"string\"},\"text\":{\"type\":\"string\"}},\"required\":[\"itemId\",\"text\"],\"additionalProperties\":false}"))],
            _ => [new("text.run.bold", 1, Json("{\"type\":\"object\",\"properties\":{\"runId\":{\"type\":\"string\"},\"value\":{\"type\":\"boolean\"}},\"required\":[\"runId\",\"value\"],\"additionalProperties\":false}"))]
        };
        Schema = new(objectType, objectType == "text.paragraph" ? 2 : 1, true, Json("{\"type\":\"object\",\"properties\":{\"Id\":{\"type\":\"string\"},\"Kind\":{\"type\":\"integer\"},\"Runs\":{\"type\":\"array\"}},\"required\":[\"Id\",\"Kind\",\"Runs\"],\"additionalProperties\":true}"),
            Actions.Select(action => action.ActionId).Append("object.create").Append("object.render").ToFrozenSet(StringComparer.Ordinal));
    }
    public HomeProductivityObject Create(Guid objectId, JsonElement content)
    {
        var value = new HomeProductivityObject(objectId, Schema.ObjectType, Schema.SchemaVersion, content.Clone(), Json("{}"), Json("{}"), [], Json("{}"));
        Validate(value); return value;
    }
    private NotesBlock Validate(HomeProductivityObject source)
    {
        if (source.ObjectId == Guid.Empty || source.ObjectType != Schema.ObjectType ||
            !ActionJsonSchemaValidator.Validate(Schema.Schema.GetRawText(), source.Content, out _))
            throw new InvalidDataException("Invalid shared Notes payload.");
        RequireChildIds(source.Content.GetProperty("Runs"));
        if (source.Content.TryGetProperty("List", out var list) && list.ValueKind == JsonValueKind.Object)
        {
            if (!list.TryGetProperty("Items", out var items)) throw new InvalidDataException("Missing canonical list items.");
            RequireChildIds(items);
        }
        if (source.Content.TryGetProperty("Table", out var table) && table.ValueKind == JsonValueKind.Object)
        {
            if (!table.TryGetProperty("Rows", out var rows)) throw new InvalidDataException("Missing canonical table rows.");
            RequireChildIds(rows);
            foreach (var row in rows.EnumerateArray())
            {
                if (!row.TryGetProperty("Cells", out var cells)) throw new InvalidDataException("Missing canonical table cells.");
                RequireChildIds(cells);
            }
        }
        var block = HomeNotesSharedObjects.Read(source);
        if (block.Runs is null || block.Paragraph is null || block.Runs.Any(run => run is null || run.Id == Guid.Empty || !double.IsFinite(run.FontSize) || run.FontSize <= 0) ||
            block.Runs.Select(run => run.Id).Distinct().Count() != block.Runs.Count)
            throw new InvalidDataException("Rich text requires stable unique runs and finite formatting.");
        if (block.Kind == NotesBlockKind.List && (block.List?.Items is null || !Enum.IsDefined(block.List.Kind) ||
            block.List.Items.Any(item => item is null || item.Id == Guid.Empty || item.Level is < 0 or > 100) ||
            block.List.Items.Select(item => item.Id).Distinct().Count() != block.List.Items.Count))
            throw new InvalidDataException("List items require stable unique identities and supported nesting.");
        if (block.Kind == NotesBlockKind.Table && (block.Table?.Rows is null || block.Table.Rows.Any(row => row is null || row.Id == Guid.Empty || row.Cells is null ||
            row.Cells.Any(cell => cell is null || cell.Id == Guid.Empty || cell.RowSpan < 1 || cell.ColumnSpan < 1)) ||
            block.Table.Rows.Select(row => row.Id).Distinct().Count() != block.Table.Rows.Count ||
            block.Table.Rows.SelectMany(row => row.Cells).Select(cell => cell.Id).Distinct().Count() != block.Table.Rows.Sum(row => row.Cells.Count)))
            throw new InvalidDataException("Tables require stable unique row/cell identities and valid spans.");
        return block;
    }
    private static void RequireChildIds(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 10000)
            throw new InvalidDataException("Canonical children require a bounded array.");
        var seen = new HashSet<Guid>();
        foreach (var item in items.EnumerateArray())
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Id", out var identity) ||
                identity.ValueKind != JsonValueKind.String || !identity.TryGetGuid(out var id) || id == Guid.Empty || !seen.Add(id))
                throw new InvalidDataException("Canonical child IDs must be explicit and unique; deserialisation cannot create them.");
    }
    public IReadOnlyList<string> GetReferencedStyleIds(HomeProductivityObject source)
    {
        var block = Validate(source);
        return string.IsNullOrWhiteSpace(block.StyleId) ? [] : [block.StyleId];
    }
    public HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAction action)
    {
        Validate(source);
        var descriptor = Actions.SingleOrDefault(item => item.ActionId == action.ActionId && item.Version == action.Version);
        if (descriptor is null || !action.ObjectIds.Contains(source.ObjectId) ||
            !ActionJsonSchemaValidator.Validate(descriptor.ArgumentSchema.GetRawText(), action.Arguments, out _))
            throw new InvalidDataException("Unsupported shared Notes action.");
        var content = JsonNode.Parse(source.Content.GetRawText())!.AsObject();
        var key = action.ActionId == "table.cell.text" ? "cellId" : action.ActionId == "text.run.bold" ? "runId" : "itemId";
        if (!Guid.TryParse(action.Arguments.GetProperty(key).GetString(), out var target)) throw new InvalidDataException("Invalid canonical child ID.");
        IEnumerable<JsonNode?> nodes = action.ActionId switch
        {
            "table.cell.text" => content["Table"]!["Rows"]!.AsArray().SelectMany(row => row!["Cells"]!.AsArray()),
            "text.run.bold" => content["Runs"]!.AsArray(),
            _ => content["List"]!["Items"]!.AsArray()
        };
        var matches = nodes.Where(node => Guid.TryParse(node?["Id"]?.GetValue<string>(), out var id) && id == target).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("The canonical child is missing or ambiguous.");
        if (action.ActionId == "text.run.bold") matches[0]!["Bold"] = action.Arguments.GetProperty("value").GetBoolean();
        else if (action.ActionId == "checklist.check") matches[0]!["Checked"] = action.Arguments.GetProperty("checked").GetBoolean();
        else matches[0]!["Text"] = action.Arguments.GetProperty("text").GetString();
        return source with { Content = JsonSerializer.SerializeToElement(content) };
    }
    public HomeProductivityObjectRenderResult Render(HomeProductivityObject source)
    {
        var block = Validate(source); var id = "notes-" + source.ObjectId.ToString("N");
        var unsupported = new List<string> { "content.StyleId", "content.Paragraph.LineSpacing" };
        if (block.Paragraph.FirstLineIndent != 0) unsupported.Add("content.Paragraph.FirstLineIndent");
        if (block.Paragraph.KeepWithNext) unsupported.Add("content.Paragraph.KeepWithNext");
        if (block.Paragraph.PageBreakBefore) unsupported.Add("content.Paragraph.PageBreakBefore");
        if (block.Runs.Any(run => run.Link is not null)) unsupported.Add("content.Runs.Link activation");
        if (block.Table is not null) { unsupported.Add("content.Table.Style"); if (block.Table.RepeatHeader) unsupported.Add("content.Table.RepeatHeader"); }
        var known = typeof(NotesBlock).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        unsupported.AddRange(source.Content.EnumerateObject().Where(property => !known.Contains(property.Name)).Select(property => "content." + property.Name));
        unsupported.AddRange(source.Layout.EnumerateObject().Select(property => "layout." + property.Name));
        unsupported.AddRange(source.Formatting.EnumerateObject().Select(property => "formatting." + property.Name));
        return new(new XElement("Cui", new XElement("Object", new XAttribute("Type", "spe.notes"), new XAttribute("id", id))).ToString(SaveOptions.DisableFormatting), unsupported)
        { NotesBindings = [new(id, source.ObjectId, source.Content.Clone())] };
    }
    public HomeProductivityObject CloneForPaste(HomeProductivityObject source, Guid newObjectId)
    {
        Validate(source); if (newObjectId == Guid.Empty) throw new ArgumentException("Missing clone identity.", nameof(newObjectId));
        var content = JsonNode.Parse(source.Content.GetRawText())!.AsObject(); content["Id"] = newObjectId;
        foreach (var run in content["Runs"]!.AsArray()) run!["Id"] = Guid.NewGuid();
        if (content["List"] is JsonObject list) foreach (var item in list["Items"]!.AsArray()) item!["Id"] = Guid.NewGuid();
        if (content["Table"] is JsonObject table) foreach (var row in table["Rows"]!.AsArray())
        { row!["Id"] = Guid.NewGuid(); foreach (var cell in row["Cells"]!.AsArray()) cell!["Id"] = Guid.NewGuid(); }
        return source with { ObjectId = newObjectId, Content = JsonSerializer.SerializeToElement(content) };
    }
}
