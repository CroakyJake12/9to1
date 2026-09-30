using System.Collections.Frozen;
using System.Text.Json;
using System.Xml.Linq;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>One authorized display acquisition. This value does not permit later reads, navigation or mutation.</summary>
public sealed class HomePreparedArtifactReference : IDisposable
{
    private ResolvedProductivityArtifactReference? _resolved;
    private HomePreparedArtifactReference(ResolvedProductivityArtifactReference resolved) => _resolved = resolved;
    internal ResolvedProductivityArtifactReference GetAcquired() => _resolved
        ?? throw new ObjectDisposedException(nameof(HomePreparedArtifactReference));
    public void Dispose() => Interlocked.Exchange(ref _resolved, null);

    public static async Task<HomePreparedArtifactReference> PrepareAsync(IProductivityArtifactReferenceResolver resolver,
        ProductivityArtifactReferenceSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        HomeArtifactReferenceObjectHandler.ValidateReference(source);
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = await resolver.ResolveAsync(source, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (resolved.Source != source || string.IsNullOrWhiteSpace(resolved.DisplayName) ||
            resolved.DisplayName.Length > 4096 || string.IsNullOrWhiteSpace(resolved.ArtifactType) || resolved.ArtifactType.Length > 256)
            throw new InvalidDataException("The owning reference resolver returned a different source or invalid display metadata.");
        return new(resolved);
    }
}

/// <summary>Read-only reference display over an existing canonical Space context. No synthetic artifact conversion.</summary>
public sealed class HomeArtifactReferenceObjectHandler(HomePreparedArtifactReference prepared)
    : IHomeProductivityObjectHandler, IHomeProductivityObjectCloneHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static JsonElement Empty => JsonSerializer.SerializeToElement(new { });
    public HomeProductivityObjectSchema Schema { get; } = new("artifact.reference", 1, true,
        JsonSerializer.SerializeToElement(new { type = "object", required = new[] { "reference" }, additionalProperties = true }),
        new[] { "object.render" }.ToFrozenSet(StringComparer.Ordinal));
    public IReadOnlyList<HomeProductivityObjectActionDescriptor> Actions => [];
    public static JsonElement ReferenceContent(ProductivityArtifactReferenceSource source) =>
        JsonSerializer.SerializeToElement(new { reference = source }, JsonOptions);

    internal static void ValidateReference(ProductivityArtifactReferenceSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.SpaceId == Guid.Empty || source.SpaceRevision < 1 || source.FilesRevisionId == Guid.Empty ||
            source.Context is null || source.Context.ContextId == Guid.Empty || source.Context.HostedFileId is not { } file || file == Guid.Empty ||
            string.IsNullOrWhiteSpace(source.Context.OwnerAppId) || string.IsNullOrWhiteSpace(source.Context.CanonicalEntityId) ||
            string.IsNullOrWhiteSpace(source.Context.RevisionToken))
            throw new InvalidDataException("A shared artifact reference requires exact existing Space, context, Files and owning revision identities.");
    }

    private static ProductivityArtifactReferenceSource Read(Guid objectId, JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object || !content.TryGetProperty("reference", out var value) ||
            value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("context", out var context) || context.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The canonical artifact reference is absent.");
        var source = value.Deserialize<ProductivityArtifactReferenceSource>(JsonOptions)
            ?? throw new InvalidDataException("The canonical artifact reference is invalid.");
        ValidateReference(source);
        if (objectId != source.Context.ContextId)
            throw new InvalidDataException("The shared object ID must equal the existing canonical context ID.");
        return source;
    }

    public HomeProductivityObject Create(Guid objectId, JsonElement content)
    {
        _ = Read(objectId, content);
        return new(objectId, Schema.ObjectType, Schema.SchemaVersion, content.Clone(), Empty, Empty, [], Empty);
    }

    public HomeProductivityObjectRenderResult Render(HomeProductivityObject source)
    {
        if (source.ObjectType != Schema.ObjectType || source.SchemaVersion != Schema.SchemaVersion ||
            source.Formatting.ValueKind != JsonValueKind.Object || source.Layout.ValueKind != JsonValueKind.Object ||
            source.Accessibility.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported shared artifact reference schema.");
        var reference = Read(source.ObjectId, source.Content);
        var resolved = prepared.GetAcquired();
        if (resolved.Source != reference) throw new UnauthorizedAccessException("The displayed object differs from the authorized reference acquisition.");
        var text = new XElement("TextBlock", new XAttribute("id", "artifact-" + source.ObjectId.ToString("N")),
            new XAttribute("text", resolved.DisplayName + " · " + resolved.ArtifactType), new XAttribute("text-wrapping", "Wrap"),
            new XAttribute("accessible-name", "Referenced artifact: " + resolved.DisplayName));
        var referenceFields = new HashSet<string>(["spaceId", "spaceRevision", "context", "filesRevisionId"], StringComparer.Ordinal);
        var contextFields = new HashSet<string>(["contextId", "kind", "ownerAppId", "canonicalEntityId", "revisionToken", "permission", "indexState", "includedAutomatically", "addedAt", "hostedFileId"], StringComparer.Ordinal);
        var rawReference = source.Content.GetProperty("reference");
        var unsupported = source.Content.EnumerateObject().Where(property => property.Name != "reference").Select(property => "content." + property.Name)
            .Concat(rawReference.EnumerateObject().Where(property => !referenceFields.Contains(property.Name)).Select(property => "content.reference." + property.Name))
            .Concat(rawReference.GetProperty("context").EnumerateObject().Where(property => !contextFields.Contains(property.Name)).Select(property => "content.reference.context." + property.Name))
            .Concat(source.Formatting.EnumerateObject().Select(property => "formatting." + property.Name))
            .Concat(source.Layout.EnumerateObject().Select(property => "layout." + property.Name))
            .Concat(source.Accessibility.EnumerateObject().Select(property => "accessibility." + property.Name))
            .Append("artifact.navigation requires current owning-app dispatch").ToArray();
        return new(new XElement("Cui", text).ToString(SaveOptions.DisableFormatting), unsupported);
    }

    public HomeProductivityObject Transform(HomeProductivityObject source, HomeProductivityAction action) =>
        throw new NotSupportedException("Artifact mutation must use its canonical owner and current Home authorization.");
    public HomeProductivityObject CloneForPaste(HomeProductivityObject source, Guid newObjectId) =>
        throw new NotSupportedException("Creating a new artifact context requires the canonical Space owner.");
}
