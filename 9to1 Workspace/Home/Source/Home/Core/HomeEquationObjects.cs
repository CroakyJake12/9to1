using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Haven.Core;
using NineToOne.Cui.AI;

namespace HavenOS.Home.Core;

public sealed record HomeProductivityEquationBinding(string ControlId, Guid ObjectId, JsonElement CanonicalBlock);

/// <summary>Existing canonical Notes equation payload, projected without interpreting or replacing its
/// visual authoring graph. Native mathematical layout is a separate presentation of the retained source.</summary>
public sealed class HomeEquationObjectHandler : IHomeProductivityObjectHandler, IHomeProductivityObjectCloneHandler
{
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    public HomeProductivityObjectSchema Schema { get; } = new("math.equation", 1, true,
        Json("{\"type\":\"object\",\"properties\":{\"Id\":{\"type\":\"string\"},\"Kind\":{\"type\":\"integer\"},\"Equation\":{\"type\":\"object\"}},\"required\":[\"Id\",\"Kind\",\"Equation\"],\"additionalProperties\":true}"),
        new[] { "object.create", "object.render", "equation.source" }.ToFrozenSet(StringComparer.Ordinal));
    public IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions { get; } =
        [new("equation.source", 1, Json("{\"type\":\"object\",\"properties\":{\"source\":{\"type\":\"string\",\"maxLength\":32768},\"alternative\":{\"type\":\"string\",\"maxLength\":4096}},\"required\":[\"source\",\"alternative\"],\"additionalProperties\":false}"))];
    public static HomeProductivityObject Project(NotesBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return new HomeEquationObjectHandler().Create(block.Id, JsonSerializer.SerializeToElement(block));
    }
    public HomeProductivityObject Create(Guid objectId, JsonElement content)
    {
        ReadCanonical(content, objectId);
        return new(objectId, Schema.ObjectType, Schema.SchemaVersion, content.Clone(), Json("{}"), Json("{}"), [], Json("{}"));
    }
    public static NotesEquationData ReadCanonical(JsonElement content, Guid objectId)
    {
        if (objectId == Guid.Empty || content.ValueKind != JsonValueKind.Object || content.GetRawText().Length > 1024 * 1024 ||
            !content.TryGetProperty("Id", out var id) || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var actual) || actual != objectId ||
            !content.TryGetProperty("Kind", out var kind) || !kind.TryGetInt32(out var value) || value != (int)NotesBlockKind.Equation ||
            !content.TryGetProperty("Equation", out var equation) || equation.ValueKind != JsonValueKind.Object ||
            !equation.TryGetProperty("Source", out var source) || source.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("A shared equation requires its existing canonical block identity and explicit source.");
        RequireExistingIds(content, "Runs");
        RequireExistingIds(equation, "SourceStrokes");
        var data = JsonSerializer.Deserialize<NotesEquationData>(equation) ?? throw new InvalidDataException("Missing canonical equation payload.");
        if (!Enum.IsDefined(data.ViewMode) || data.Source is null || data.Source.Length > 32768 ||
            data.AccessibleAlternative is null || data.AccessibleAlternative.Length > 4096 || data.Macros is null ||
            data.Macros.Count > 64 || data.Macros.Any(macro => macro.Key.Length > 128 || macro.Value is null || macro.Value.Length > 4096) ||
            data.References is null || data.References.Count > 1000 || data.SourceStrokes is null || data.SourceStrokes.Count > 10000)
            throw new InvalidDataException("Canonical equation data exceeds supported bounds.");
        return data;
    }
    private static void RequireExistingIds(JsonElement owner, string property)
    {
        if (!owner.TryGetProperty(property, out var items)) return;
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 10000)
            throw new InvalidDataException("Canonical equation children require a bounded array.");
        var ids = new HashSet<Guid>();
        foreach (var item in items.EnumerateArray())
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("Id", out var identity) ||
                identity.ValueKind != JsonValueKind.String || !identity.TryGetGuid(out var id) || id == Guid.Empty || !ids.Add(id))
                throw new InvalidDataException("Canonical equation child identities must be explicit and unique.");
    }
    private NotesEquationData Validate(HomeProductivityObject value)
    {
        if (value.ObjectType != Schema.ObjectType || value.SchemaVersion != Schema.SchemaVersion)
            throw new InvalidDataException("Equation schema mismatch.");
        return ReadCanonical(value.Content, value.ObjectId);
    }
    public IReadOnlyList<string> GetReferencedStyleIds(HomeProductivityObject value)
    { Validate(value); return value.Content.TryGetProperty("StyleId", out var style) && style.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(style.GetString()) ? [style.GetString()!] : []; }
    public HomeProductivityObject Transform(HomeProductivityObject value, HomeProductivityAction action)
    {
        var equationData = Validate(value);
        if (action.ActionId != "equation.source" || action.ObjectType != Schema.ObjectType || action.Version != 1 || !action.ObjectIds.Contains(value.ObjectId) ||
            !ActionJsonSchemaValidator.Validate(Actions[0].ArgumentSchema.GetRawText(), action.Arguments, out _))
            throw new InvalidDataException("Unsupported canonical equation action.");
        // A source-only edit cannot silently erase an independently authored visual graph.
        if (!string.IsNullOrWhiteSpace(equationData.VisualStructureJson) && equationData.VisualStructureJson.Trim() != "{}")
            throw new NotSupportedException("Visual equation graph synchronization is required before editing this source.");
        var content = JsonNode.Parse(value.Content.GetRawText())!.AsObject();
        var equation = content["Equation"]!.AsObject();
        equation["Source"] = action.Arguments.GetProperty("source").GetString();
        equation["AccessibleAlternative"] = action.Arguments.GetProperty("alternative").GetString();
        equation["RenderedText"] = string.Empty; equation["Error"] = string.Empty;
        var updated = value with { Content = JsonSerializer.SerializeToElement(content) };
        Validate(updated); return updated;
    }
    public HomeProductivityObjectRenderResult Render(HomeProductivityObject value)
    {
        var equationData = Validate(value); var id = "equation-" + value.ObjectId.ToString("N");
        var unsupported = new List<string>();
        if (GetReferencedStyleIds(value).Count != 0) unsupported.Add("content.StyleId");
        if (equationData.Macros.Count != 0) unsupported.Add("content.Equation.Macros rendering");
        if (equationData.References.Count != 0) unsupported.Add("content.Equation.References activation");
        if (equationData.SourceStrokes.Count != 0) unsupported.Add("content.Equation.SourceStrokes editing");
        if (equationData.VisualStructureJson is not (null or "" or "{}")) unsupported.Add("content.Equation.VisualStructureJson editing");
        var known = typeof(NotesEquationData).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        unsupported.AddRange(value.Content.GetProperty("Equation").EnumerateObject().Where(property => !known.Contains(property.Name)).Select(property => "content.Equation." + property.Name));
        var knownBlock = typeof(NotesBlock).GetProperties().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        unsupported.AddRange(value.Content.EnumerateObject().Where(property => !knownBlock.Contains(property.Name)).Select(property => "content." + property.Name));
        unsupported.AddRange(value.Layout.EnumerateObject().Select(property => "layout." + property.Name));
        unsupported.AddRange(value.Formatting.EnumerateObject().Select(property => "formatting." + property.Name));
        return new(new XElement("Cui", new XElement("Object", new XAttribute("Type", "spe.equation"), new XAttribute("id", id))).ToString(SaveOptions.DisableFormatting), unsupported)
        { EquationBindings = [new(id, value.ObjectId, value.Content.Clone())] };
    }
    public HomeProductivityObject CloneForPaste(HomeProductivityObject source, Guid newObjectId) =>
        throw new NotSupportedException("Canonical equation references and source ink require an owning clone mapping.");
}
