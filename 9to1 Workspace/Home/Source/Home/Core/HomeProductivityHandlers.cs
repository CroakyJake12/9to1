using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NineToOne.Cui.AI;

namespace HavenOS.Home.Core;

public sealed record HomeProductivityObjectActionDescriptor(string ActionId, int Version, JsonElement ArgumentSchema);
public sealed record HomeProductivityObjectRenderResult(string CuiSource, IReadOnlyList<string> RetainedUnsupportedProperties)
{
    public IReadOnlyList<HomeProductivityRasterBinding> RasterBindings { get; init; } = [];
    public IReadOnlyList<HomeProductivityNotesBinding> NotesBindings { get; init; } = [];
    public IReadOnlyList<HomeProductivityVectorBinding> VectorBindings { get; init; } = [];
    public IReadOnlyList<HomeProductivityEquationBinding> EquationBindings { get; init; } = [];
}

/// <summary>One app-neutral object implementation. Pure transformations do not grant artifact write permission.</summary>
public interface IHomeProductivityObjectHandler
{
    HomeProductivityObjectSchema Schema { get; }
    IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions { get; }
    HomeProductivityObject Create(Guid objectId, JsonElement content);
    HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAction action);
    HomeProductivityObjectRenderResult Render(HomeProductivityObject source);
    IReadOnlyList<string> GetReferencedStyleIds(HomeProductivityObject source) => [];
}

/// <summary>Object families with embedded canonical identities clone those identities together. Target-layer or
/// resource remapping remains an explicit owning-app operation; copying never claims target access.</summary>
public interface IHomeProductivityObjectCloneHandler
{
    HomeProductivityObject CloneForPaste(HomeProductivityObject source, Guid newObjectId);
}

/// <summary>
/// The canonical artifact owner rechecks actual targets/revision/ACL and Home consent. It computes/validates
/// every transformation before one atomic persisted transaction, then reports Committed with the actual
/// observed owner revision and EXACT full target set. Partial success is not supported by this port.
/// If commit acknowledgement is uncertain, report NeedsRecovery and retain observed revision/effect evidence;
/// never claim the old revision or no effect. This port is not exposed through native discovery IPC.
/// </summary>
public interface IHomeProductivityArtifactActionProvider
{
    string AppId { get; }
    ValueTask<HomeProductivityActionResult> ApplyAsync(HomeProductivityContext context, HomeProductivityAction action,
        Func<HomeProductivityObject, HomeProductivityObject> sharedTransformation, CancellationToken cancellationToken);
}

/// <summary>The same canonical artifact owner inserts the captured shared objects atomically, claiming Home consent
/// and expected revision immediately before commit. Reject existing ObjectIDs; operationId binds owner idempotency.
/// This capability does not authorise insertion and is never exposed by discovery IPC.</summary>
public interface IHomeProductivityArtifactInsertionProvider : IHomeProductivityArtifactActionProvider
{
    ValueTask<HomeProductivityActionResult> InsertAsync(HomeProductivityContext context,
        IReadOnlyList<HomeProductivityObject> objects, string operationId, CancellationToken cancellationToken);
}

/// <summary>Concrete paragraph semantics shared by all supported surfaces, including actual formatting and CUI rendering.</summary>
public sealed class HomeParagraphObjectHandler : IHomeProductivityObjectHandler
{
    private static JsonElement Parse(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    public HomeProductivityObjectSchema Schema { get; } = new("text.paragraph", 1, true,
        Parse("{\"type\":\"object\",\"properties\":{\"text\":{\"type\":\"string\"}},\"required\":[\"text\"],\"additionalProperties\":true}"),
        new[] { "object.create", "object.render", "format.bold" }.ToFrozenSet(StringComparer.Ordinal));
    public IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions { get; } = [new("format.bold", 1,
        Parse("{\"type\":\"object\",\"properties\":{\"value\":{\"type\":\"boolean\"}},\"required\":[\"value\"],\"additionalProperties\":false}"))];
    public HomeProductivityObject Create(Guid objectId, JsonElement content)
    {
        if (objectId == Guid.Empty || !ActionJsonSchemaValidator.Validate(Schema.Schema.GetRawText(), content, out _))
            throw new InvalidDataException("The shared paragraph requires a stable object ID and valid text content.");
        return new(objectId, Schema.ObjectType, Schema.SchemaVersion, content.Clone(), Parse("{}"), Parse("{}"), [], Parse("{}"));
    }
    public HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAction action)
    {
        Validate(source);
        var descriptor = Actions.SingleOrDefault(item => item.ActionId == action.ActionId && item.Version == action.Version);
        if (descriptor is null || !action.ObjectIds.Contains(source.ObjectId) ||
            !ActionJsonSchemaValidator.Validate(descriptor.ArgumentSchema.GetRawText(), action.Arguments, out _))
            throw new InvalidDataException("This shared paragraph action is unsupported or malformed.");
        var formatting = JsonNode.Parse(source.Formatting.GetRawText()) as JsonObject
            ?? throw new InvalidDataException("Paragraph formatting must remain a typed object.");
        formatting["bold"] = action.Arguments.GetProperty("value").GetBoolean();
        return source with { Formatting = JsonSerializer.SerializeToElement(formatting) };
    }
    public HomeProductivityObjectRenderResult Render(HomeProductivityObject source)
    {
        Validate(source);
        var text = new XElement("TextBlock", new XAttribute("text", source.Content.GetProperty("text").GetString() ?? ""),
            new XAttribute("text-wrapping", "Wrap"), new XAttribute("accessible-name", "Shared paragraph"));
        if (source.Formatting.TryGetProperty("bold", out var bold) && bold.ValueKind == JsonValueKind.True)
            text.Add(new XAttribute("font-weight", "Bold"));
        var unsupported = source.Content.EnumerateObject().Where(property => property.Name != "text").Select(property => "content." + property.Name)
            .Concat(source.Formatting.EnumerateObject().Where(property => property.Name != "bold").Select(property => "formatting." + property.Name))
            .Concat(source.Layout.EnumerateObject().Select(property => "layout." + property.Name)).ToArray();
        return new(new XElement("Cui", text).ToString(SaveOptions.DisableFormatting), unsupported);
    }
    private void Validate(HomeProductivityObject source)
    {
        if (source.ObjectType != Schema.ObjectType || source.SchemaVersion != Schema.SchemaVersion || source.ObjectId == Guid.Empty ||
            source.Formatting.ValueKind != JsonValueKind.Object || source.Layout.ValueKind != JsonValueKind.Object ||
            !ActionJsonSchemaValidator.Validate(Schema.Schema.GetRawText(), source.Content, out _))
            throw new InvalidDataException("The shared paragraph schema is incompatible; content was preserved.");
    }
}
