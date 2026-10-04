using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CakeOS.Images.Frontend;

public interface IPersistenceLayer
{
    Task LoadAsync(string imagePath, CancellationToken ct = default);
    Task SaveAnnotationsAsync(string imagePath, IReadOnlyList<Annotation> annotations, CancellationToken ct = default);
    Task SaveAiTagsAsync(string imagePath, IReadOnlyList<string> tags, CancellationToken ct = default);
    Task<IReadOnlyList<Annotation>> LoadAnnotationsAsync(string imagePath, CancellationToken ct = default);
    Task<IReadOnlyList<string>> LoadAiTagsAsync(string imagePath, CancellationToken ct = default);
}

public sealed class Annotation
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public AnnotationType Type { get; init; }
    public string Data { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public enum AnnotationType
{
    Ink,
    Shape,
    Text,
    AiTag,
}

public sealed class SidecarPersistenceLayer : IPersistenceLayer
{
    public async Task LoadAsync(string imagePath, CancellationToken ct = default)
    {
        var sidecarPath = GetSidecarPath(imagePath);
        if (File.Exists(sidecarPath))
        {
            // Load XMP sidecar
            var xmp = await File.ReadAllTextAsync(sidecarPath, ct).ConfigureAwait(false);
            // Parse and apply annotations/tags
        }
    }

    public async Task SaveAnnotationsAsync(string imagePath, IReadOnlyList<Annotation> annotations, CancellationToken ct = default)
    {
        var sidecarPath = GetSidecarPath(imagePath);
        var xmp = GenerateXmp(annotations, []);
        await File.WriteAllTextAsync(sidecarPath, xmp, ct).ConfigureAwait(false);
    }

    public async Task SaveAiTagsAsync(string imagePath, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        var sidecarPath = GetSidecarPath(imagePath);
        var existingAnnotations = await LoadAnnotationsAsync(imagePath, ct).ConfigureAwait(false);
        var xmp = GenerateXmp(existingAnnotations, tags);
        await File.WriteAllTextAsync(sidecarPath, xmp, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Annotation>> LoadAnnotationsAsync(string imagePath, CancellationToken ct = default)
    {
        var sidecarPath = GetSidecarPath(imagePath);
        if (!File.Exists(sidecarPath))
            return [];

        var xmp = await File.ReadAllTextAsync(sidecarPath, ct).ConfigureAwait(false);
        return ParseAnnotations(xmp);
    }

    public async Task<IReadOnlyList<string>> LoadAiTagsAsync(string imagePath, CancellationToken ct = default)
    {
        var sidecarPath = GetSidecarPath(imagePath);
        if (!File.Exists(sidecarPath))
            return [];

        var xmp = await File.ReadAllTextAsync(sidecarPath, ct).ConfigureAwait(false);
        return ParseAiTags(xmp);
    }

    private static string GetSidecarPath(string imagePath)
    {
        return Path.ChangeExtension(imagePath, ".xmp");
    }

    private static string GenerateXmp(IReadOnlyList<Annotation> annotations, IReadOnlyList<string> aiTags)
    {
        // Generate XMP with annotations and AI tags
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xpacket begin=\"\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        sb.AppendLine("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        sb.AppendLine("  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        sb.AppendLine("    <rdf:Description rdf:about=\"\">");
        
        if (aiTags.Count > 0)
        {
            sb.AppendLine("      <dc:subject xmlns:dc=\"http://purl.org/dc/elements/1.1/\">");
            sb.AppendLine("        <rdf:Bag>");
            foreach (var tag in aiTags)
            {
                sb.AppendLine($"          <rdf:li>{System.Security.SecurityElement.Escape(tag)}</rdf:li>");
            }
            sb.AppendLine("        </rdf:Bag>");
            sb.AppendLine("      </dc:subject>");
        }

        if (annotations.Count > 0)
        {
            sb.AppendLine("      <cakeos:annotations xmlns:cakeos=\"http://cakeos.local/ns/1.0/\">");
            sb.AppendLine("        <rdf:Seq>");
            foreach (var ann in annotations)
            {
                sb.AppendLine($"          <rdf:li rdf:parseType=\"Resource\">");
                sb.AppendLine($"            <cakeos:id>{ann.Id}</cakeos:id>");
                sb.AppendLine($"            <cakeos:type>{ann.Type}</cakeos:type>");
                sb.AppendLine($"            <cakeos:data>{System.Security.SecurityElement.Escape(ann.Data)}</cakeos:data>");
                sb.AppendLine($"            <cakeos:x>{ann.X}</cakeos:x>");
                sb.AppendLine($"            <cakeos:y>{ann.Y}</cakeos:y>");
                sb.AppendLine($"            <cakeos:width>{ann.Width}</cakeos:width>");
                sb.AppendLine($"            <cakeos:height>{ann.Height}</cakeos:height>");
                sb.AppendLine($"          </rdf:li>");
            }
            sb.AppendLine("        </rdf:Seq>");
            sb.AppendLine("      </cakeos:annotations>");
        }

        sb.AppendLine("    </rdf:Description>");
        sb.AppendLine("  </rdf:RDF>");
        sb.AppendLine("</x:xmpmeta>");
        sb.AppendLine("<?xpacket end=\"w\"?>");

        return sb.ToString();
    }

    private static IReadOnlyList<Annotation> ParseAnnotations(string xmp)
    {
        // Simplified parsing - real implementation would use XML parser
        return [];
    }

    private static IReadOnlyList<string> ParseAiTags(string xmp)
    {
        // Simplified parsing
        return [];
    }
}