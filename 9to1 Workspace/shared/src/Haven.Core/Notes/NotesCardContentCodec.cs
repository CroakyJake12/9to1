using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Core.Mathematics;

namespace Haven.Core;

/// <summary>Editable Cards faces reuse the owning Notes block engine and the exact canonical mathematical graph body.</summary>
/// <summary>A supported owned Cards payload must never be silently replaced by legacy recovery or flattened export.</summary>
public sealed class NotesCardContentException(string code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public string Code { get; } = code;
}

public enum NotesCardFaceItemKind { Block = 1, Graph = 2 }

public sealed record NotesCardFaceItem(Guid ItemId, NotesCardFaceItemKind Kind, NotesBlock? Block, GraphDefinition? Graph);
public sealed record NotesCardFace(IReadOnlyList<NotesCardFaceItem> Items, string Background);
public sealed record NotesCardStructuredContent(int SchemaVersion, Guid BlockId, Guid CardId,
    string FrontFallbackSha256, string BackFallbackSha256, NotesCardFace Front, NotesCardFace Back);

/// <summary>Versioned owner content inside existing NotesBlock.Metadata. Prior Notes serializers retain its entire string;
/// they do not need to understand new properties. Legacy text is a bound fallback projection, never the graph/ink authority.</summary>
public static class NotesCardContentCodec
{
    public const string MetadataKey = "9to1.Cards.content.v1";
    public const int SchemaVersion = 1;
    public const int MaximumContentBytes = 4 * 1024 * 1024;
    public const int MaximumFaceItems = 256;
    public const long MaximumTextCharacters = 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 64,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static NotesCardStructuredContent? Read(NotesBlock originalCard)
    {
        ArgumentNullException.ThrowIfNull(originalCard);
        if (originalCard.Metadata is null) return null; // Preserve ordinary legacy blocks with no Cards envelope.
        var matches = originalCard.Metadata.Where(pair =>
            pair.Key.StartsWith("9to1.Cards.content.", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) return null;
        if (originalCard.Kind != NotesBlockKind.Flashcard || originalCard.Flashcard is null)
            throw new NotesCardContentException("UnsupportedCardContent", "Structured Cards metadata must belong to the same actual Flashcard block.");
        if (matches.Length != 1 || matches[0].Key != MetadataKey || matches[0].Value is not { } raw)
            throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
        return Decode(originalCard, raw);
    }

    public static NotesCardStructuredContent Decode(NotesBlock originalCard, string originalPayload)
    {
        ArgumentNullException.ThrowIfNull(originalCard);
        ArgumentNullException.ThrowIfNull(originalPayload);
        if (originalPayload.Length > MaximumContentBytes || Encoding.UTF8.GetByteCount(originalPayload) > MaximumContentBytes)
            throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
        try
        {
            using var parsed = JsonDocument.Parse(originalPayload, new() { MaxDepth = 64 });
            RejectDuplicates(parsed.RootElement);
            ValidateOriginalGraphNumbers(parsed.RootElement);
            var content = JsonSerializer.Deserialize<NotesCardStructuredContent>(originalPayload, Options)
                ?? throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
            Validate(originalCard, content);
            return content;
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException)
        {
            throw new NotesCardContentException("UnsupportedCardContent",
                "The complete structured Cards payload failed exact decoding; retain the original source.", error);
        }
    }

    /// <summary>Returns a complete replacement payload; the original card is not mutated before owner CAS/admission.</summary>
    public static string Encode(NotesBlock originalCard, NotesCardStructuredContent content)
    {
        Validate(originalCard, content);
        var raw = JsonSerializer.Serialize(content, Options);
        if (Encoding.UTF8.GetByteCount(raw) > MaximumContentBytes)
            throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
        _ = Decode(originalCard, raw);
        return raw;
    }

    public static string FallbackFingerprint(string originalText) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalText ?? throw new ArgumentNullException(nameof(originalText)))));

    public static void Validate(NotesBlock originalCard, NotesCardStructuredContent content)
    {
        ArgumentNullException.ThrowIfNull(originalCard);
        ArgumentNullException.ThrowIfNull(content);
        if (originalCard.Kind != NotesBlockKind.Flashcard || originalCard.Flashcard is null ||
            originalCard.Flashcard.CardId == Guid.Empty || content.SchemaVersion != SchemaVersion ||
            content.CardId != originalCard.Flashcard.CardId || content.Front is null || content.Back is null)
            throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
        if ((long)originalCard.Flashcard.Front.Length + originalCard.Flashcard.Back.Length > MaximumTextCharacters)
            throw new NotesCardContentException("UnsupportedCardContent", "The retained Cards fallback exceeds the complete text bound.");
        if (content.BlockId != originalCard.Id)
            throw new NotesCardContentException("RevisionConflict", "The rich face payload belongs to a different canonical owning block; do not silently migrate or replace its identity.");
        if (content.FrontFallbackSha256 != FallbackFingerprint(originalCard.Flashcard.Front) ||
            content.BackFallbackSha256 != FallbackFingerprint(originalCard.Flashcard.Back))
            throw new NotesCardContentException("RevisionConflict",
                "Legacy Front or Back changed after rich content was bound; recover or explicitly rebind the owning card before replacement.");
        var identities = new HashSet<Guid> { originalCard.Id, originalCard.Flashcard.CardId };
        if (originalCard.Id == Guid.Empty || identities.Count != 2)
            throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
        long textCharacters = (long)originalCard.Flashcard.Front.Length + originalCard.Flashcard.Back.Length;
        var graphBodies = new Dictionary<(Guid, long), byte[]>();
        Face(content.Front);
        Face(content.Back);

        void Identity(Guid id)
        {
            if (id == Guid.Empty || !identities.Add(id)) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
        }
        void Text(string? text)
        {
            if (text is null) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
            textCharacters += text.Length;
            if (textCharacters > MaximumTextCharacters) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
        }
        void Ink(NotesInkStroke stroke)
        {
            if (stroke is null || stroke.Points is null || stroke.Points.Count is < 1 or > 4096)
                throw new NotesCardContentException("UnsupportedCardContent", "Cards ink exceeds its exact point bound.");
            Identity(stroke.Id);
            Text(stroke.Tool); Text(stroke.Colour); Text(stroke.RecognitionText);
            if (!double.IsFinite(stroke.BaseWidth) || !double.IsFinite(stroke.Opacity) ||
                !double.IsFinite(stroke.RecognitionConfidence))
                throw new NotesCardContentException("UnsupportedCardContent", "Cards ink values must be finite.");
        }
        void Face(NotesCardFace face)
        {
            if (face.Items is null || face.Items.Count is < 1 or > MaximumFaceItems || !Colour(face.Background))
                throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
            foreach (var item in face.Items)
            {
                if (item is null || !Enum.IsDefined(item.Kind))
                    throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                if (item.Kind == NotesCardFaceItemKind.Graph)
                {
                    if (item.Block is not null || item.Graph is null)
                        throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                    Identity(item.ItemId);
                    var bytes = MathObjectCodec.Encode(item.Graph);
                    // Use the same complete-card text accumulator after canonical graph validation.
                    Text(item.Graph.AccessibleDescription);
                    Text(item.Graph.Axes.XLabel); Text(item.Graph.Axes.YLabel);
                    foreach (var expression in item.Graph.Expressions)
                    {
                        Text(expression.LaTeX); Text(expression.AccessibleDescription);
                    }
                    foreach (var function in item.Graph.Primitives.OfType<GraphFunction>()) Text(function.Variable);
                    var key = (item.Graph.GraphID, item.Graph.Revision);
                    if (graphBodies.TryGetValue(key, out var previous) && !previous.AsSpan().SequenceEqual(bytes))
                        throw new NotesCardContentException("RevisionConflict", "Different graph bodies have the same canonical graph identity and revision.");
                    graphBodies.TryAdd(key, bytes);
                }
                else
                {
                    if (item.Block is null || item.Graph is not null || item.ItemId != item.Block.Id ||
                        item.Block.Kind == NotesBlockKind.Flashcard || item.Block.Flashcard is not null)
                        throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                    var block = item.Block;
                    if (block.Metadata is null || block.Metadata.Keys.Any(key =>
                        key.StartsWith("9to1.Cards.content.", StringComparison.OrdinalIgnoreCase)))
                        throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                    if (block.List is not null && block.Kind != NotesBlockKind.List ||
                        block.Table is not null && block.Kind != NotesBlockKind.Table ||
                        block.Media is not null && block.Kind is not (NotesBlockKind.Image or NotesBlockKind.Audio or NotesBlockKind.Video) ||
                        block.Equation is not null && block.Kind != NotesBlockKind.Equation ||
                        block.Html is not null && block.Kind != NotesBlockKind.HtmlWidget ||
                        block.Canvas is not null && block.Kind != NotesBlockKind.Canvas ||
                        block.VectorShape is not null && block.Kind != NotesBlockKind.Shape)
                        throw new NotesCardContentException("UnsupportedCardContent", "A Cards face cannot hide a different block payload from owning validation.");
                    foreach (var pair in block.Metadata) { Text(pair.Key); Text(pair.Value); }
                    Identity(block.Id);
                    if (block.Order < 0 || block.Runs is null || block.Paragraph is null || !Enum.IsDefined(block.Kind))
                        throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                    Text(block.PlainText);
                    foreach (var run in block.Runs)
                    {
                        if (run is null) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                        Identity(run.Id); Text(run.Text);
                    }
                    if (block.List is { } list)
                    {
                        if (list.Items is null || list.Items.Count > 4096) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                        foreach (var entry in list.Items) { if (entry is null) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source."); Identity(entry.Id); Text(entry.Text); }
                    }
                    if (block.Table is { } table)
                    {
                        if (table.Rows is null || table.Rows.Count > 256) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                        foreach (var row in table.Rows)
                        {
                            if (row is null || row.Cells is null || row.Cells.Count > 256) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                            Identity(row.Id);
                            foreach (var cell in row.Cells) { if (cell is null) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source."); Identity(cell.Id); Text(cell.Text); }
                        }
                    }
                    if (block.Equation is { } equation)
                    {
                        Text(equation.Source); Text(equation.AccessibleAlternative);
                        if (equation.SourceStrokes is null || equation.SourceStrokes.Count > 4096)
                            throw new NotesCardContentException("UnsupportedCardContent", "Equation ink exceeds the Cards face bound.");
                        foreach (var stroke in equation.SourceStrokes) Ink(stroke);
                    }
                    if (block.Media is { } media) { Text(media.StoredPath); Text(media.Caption); Text(media.AltText); }
                    if (block.Html is { } html) { Text(html.HtmlSource); Text(html.CssSource); Text(html.JavaScriptSource); Text(html.FallbackText); }
                    if (block.Canvas is { } canvas)
                    {
                        if (canvas.Objects is null || canvas.Strokes is null || canvas.GhostLayers is null ||
                            canvas.Objects.Count > 4096 || canvas.Strokes.Count > 4096 || canvas.GhostLayers.Count > 256 ||
                            canvas.Objects.Any(value => value is null) || canvas.Strokes.Any(value => value is null) ||
                            canvas.GhostLayers.Any(value => value is null))
                            throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                        var strokeIds = canvas.Strokes.Select(stroke => stroke.Id).ToHashSet();
                        var objectIds = canvas.Objects.Select(value => value.Id).ToHashSet();
                        var ghostIds = canvas.GhostLayers.Select(layer => layer.Id).ToHashSet();
                        if (canvas.Strokes.Any(stroke => stroke.GhostLayerId is { } id && !ghostIds.Contains(id)))
                            throw new NotesCardContentException("UnsupportedCardContent", "Cards ink must bind the same retained ghost layer.");
                        foreach (var obj in canvas.Objects)
                        {
                            if (obj is null || !double.IsFinite(obj.X) || !double.IsFinite(obj.Y) ||
                                !double.IsFinite(obj.Width) || !double.IsFinite(obj.Height))
                                throw new NotesCardContentException("UnsupportedCardContent", "Cards canvas object geometry must be finite.");
                            Identity(obj.Id); Text(obj.Text); Text(obj.StyleJson);
                        }
                        foreach (var stroke in canvas.Strokes)
                        {
                            Ink(stroke);
                        }
                        foreach (var layer in canvas.GhostLayers)
                        {
                            if (layer is null || layer.Masks is null || layer.Masks.Count > 256) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                            Identity(layer.Id); Text(layer.Name); Text(layer.Hint);
                            if (layer.StrokeIds is null || layer.ObjectIds is null ||
                                layer.StrokeIds.Count > 4096 || layer.ObjectIds.Count > 4096 ||
                                layer.StrokeIds.Distinct().Count() != layer.StrokeIds.Count ||
                                layer.ObjectIds.Distinct().Count() != layer.ObjectIds.Count ||
                                layer.StrokeIds.Any(id => !strokeIds.Contains(id)) ||
                                layer.ObjectIds.Any(id => !objectIds.Contains(id)))
                                throw new NotesCardContentException("UnsupportedCardContent", "Cards ghost references must bind the same retained canvas objects.");
                            foreach (var mask in layer.Masks) { if (mask is null) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source."); Identity(mask.Id); Text(mask.Answer); }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Search/statistics projection only. The exact block/graph payload remains the editable authority.</summary>
    public static IEnumerable<string> EnumerateSearchText(NotesBlock originalCard)
    {
        var content = Read(originalCard);
        if (content is null) yield break;
        foreach (var face in new[] { content.Front, content.Back })
            foreach (var item in face.Items)
            {
                if (item.Kind == NotesCardFaceItemKind.Graph)
                {
                    var graph = item.Graph!;
                    yield return graph.AccessibleDescription;
                    yield return graph.Axes.XLabel;
                    yield return graph.Axes.YLabel;
                    foreach (var expression in graph.Expressions) yield return expression.LaTeX;
                    continue;
                }
                var block = item.Block!;
                yield return block.PlainText;
                foreach (var run in block.Runs) yield return run.Text;
                if (block.List is { } list) foreach (var entry in list.Items) yield return entry.Text;
                if (block.Table is { } table)
                    foreach (var cell in table.Rows.SelectMany(row => row.Cells)) yield return cell.Text;
                if (block.Media is { } media) { yield return media.Caption; yield return media.AltText; }
                if (block.Equation is { } equation)
                { yield return equation.Source; yield return equation.AccessibleAlternative; }
                if (block.Html is { } html) yield return html.FallbackText;
                if (block.Canvas is { } canvas)
                {
                    foreach (var value in canvas.Objects) yield return value.Text;
                    foreach (var stroke in canvas.Strokes) yield return stroke.RecognitionText;
                    foreach (var layer in canvas.GhostLayers)
                    {
                        yield return layer.Name; yield return layer.Hint;
                        foreach (var mask in layer.Masks) { yield return mask.Label; yield return mask.Answer; }
                    }
                }
            }
    }

    private static bool Colour(string? text) => text is { Length: 7 or 9 } && text[0] == '#' && text[1..].All(Uri.IsHexDigit);

    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new NotesCardContentException("UnsupportedCardContent", "The structured Cards payload is unsupported or invalid; retain the original source.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }

    /// <summary>Checks original decimal graph tokens before the maintained default decimal reader could round them.</summary>
    private static void ValidateOriginalGraphNumbers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name == "graph" && property.Value.ValueKind != JsonValueKind.Null)
                    GraphNumbers(property.Value);
                else ValidateOriginalGraphNumbers(property.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) ValidateOriginalGraphNumbers(item);
    }

    private static void GraphNumbers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number &&
                    property.Name is "x" or "y" or "xMinimum" or "xMaximum" or "yMinimum" or "yMaximum" or
                        "xGridSpacing" or "yGridSpacing" or "minimum" or "maximum")
                    _ = MathNumericLiteral.Read(property.Value.GetRawText());
                GraphNumbers(property.Value);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) GraphNumbers(item);
    }
}
