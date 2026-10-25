using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Dev;

/// <summary>A genuine admitted V2 compilation of one captured draft. This is presentation data,
/// never project execution authority. The source itself is not a runtime artifact fallback.</summary>
public sealed record DeveloperCuiDraftPreview(string RelativePath, string SourceSha256,
    CuiDocument? Document, IReadOnlyList<CuiDiagnostic> Diagnostics)
{
    public bool Succeeded => Document is not null && Diagnostics.All(value => value.Severity != CuiDiagnosticSeverity.Error);
}

/// <summary>Bounded static authoring preview. XML is used only for a fail-closed subset/bounds
/// preflight; the maintained CUI compiler is the sole language/semantic authority. No project
/// import, binding, action, media, resource or external font can enter the child scene.</summary>
public static class DeveloperCuiDraftPreviewCompiler
{
    public const int MaximumSourceLength = 256_000;
    public const int MaximumElements = 500;
    public const int MaximumDepth = 32;
    public const string SupportedSubset = "Static CUI preview supports one Page with Container and Text, fixed text, layout and hex colours. Use the default font or monospace. Up to 256,000 characters, 500 elements and 32 nested levels. Bindings, resources, imports, actions, input, media and custom controls are unavailable. This preview does not run your application.";
    private static readonly HashSet<string> Attributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "Type", "text", "width", "height", "min-width", "min-height", "max-width", "max-height",
        "margin", "padding", "CellGap", "font-size", "font-family", "font-weight", "font-style",
        "text-wrapping", "text-alignment", "horizontal-alignment", "vertical-alignment",
        "foreground", "background", "border-brush", "border-thickness", "corner-radius", "accessible-name"
    };
    // This reads only the installed maintained compiler's own immutable module, never project
    // paths. The actual binary hash identifies the producer of the generated V2 artifact.
    private static readonly Lazy<string> CompilerSha256 = new(() =>
    {
        using var binary = File.OpenRead(typeof(CuiCompiler).Assembly.Location);
        return Convert.ToHexString(SHA256.HashData(binary)).ToLowerInvariant();
    });

    public static bool Supports(string relativePath, string text) =>
        relativePath.EndsWith(".cui", StringComparison.OrdinalIgnoreCase) && text.Length <= MaximumSourceLength;

    public static DeveloperCuiDraftPreview Compile(string relativePath, string source, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(relativePath); ArgumentNullException.ThrowIfNull(source);
        token.ThrowIfCancellationRequested();
        if (!Supports(relativePath, source)) return new(relativePath, "", null,
            [new("DEVCP001", CuiDiagnosticSeverity.Error, "Preview requires .cui source up to 256,000 characters. " + SupportedSubset, CuiSourceSpan.At(relativePath, 1, 1))]);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        DeveloperCuiDraftPreview Refuse(string code, string message, int line = 1, int column = 1) =>
            new(relativePath, hash, null, [new(code, CuiDiagnosticSeverity.Error, message, CuiSourceSpan.At(relativePath, line, column))]);
        try { Preflight(source, token); }
        catch (XmlException error) { return Refuse("DEVCP002", error.Message, Math.Max(1, error.LineNumber), Math.Max(1, error.LinePosition)); }
        catch (NotSupportedException error) { return Refuse("DEVCP003", error.Message); }

        token.ThrowIfCancellationRequested();
        // Each request has its own compiler; no mutable compiler state crosses concurrent owners.
        var options = new CuiCompilerOptions(new(CuiRuntimeCompatibility.LanguageVersion,
            CuiRuntimeCompatibility.RuntimeAbiVersion, CuiControlRegistry.ComponentRegistryVersion));
        var compilation = new CuiCompiler().Compile(source, relativePath, options);
        token.ThrowIfCancellationRequested();
        if (!compilation.Succeeded || compilation.Output is null)
            return new(relativePath, hash, null, compilation.Diagnostics);
        try
        {
            var projection = CuiV2Projection.ToDocument(compilation.Output, relativePath, hash);
            var encoded = CuiCompiledArtifactCodec.WriteArtifact(new(CuiCompiledArtifactCodec.FormatName,
                CuiCompiledArtifactCodec.FormatVersion, CompilerSha256.Value, projection.Metadata, [projection]));
            var artifact = CuiCompiledArtifactCodec.ReadArtifact(encoded);
            CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, CuiSourceSpan.At(relativePath, 1, 1));
            var document = artifact.Documents.Single();
            if (document.Source != relativePath || document.Sha256 != hash)
                return Refuse("DEVCP004", "The compiled preview does not identify this exact draft.");
            var semantics = document.Semantics.ToCuiDocument();
            DemandStaticSemantics(semantics);
            token.ThrowIfCancellationRequested();
            return new(relativePath, hash, semantics, compilation.Diagnostics);
        }
        catch (CuiV2AdmissionException error) { return Refuse(error.Code, error.Message); }
        catch (CuiRuntimeLoadException error) { return new(relativePath, hash, null, [error.Diagnostic]); }
        catch (NotSupportedException error) { return Refuse("DEVCP003", error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        { return Refuse("DEVCP005", "The installed compiler/artifact could not be admitted: " + error.Message); }
    }

    private static void Preflight(string source, CancellationToken token)
    {
        using var input = new StringReader(source);
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaximumSourceLength, IgnoreComments = true
        });
        var count = 0; var pages = 0;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.Depth > MaximumDepth) throw new NotSupportedException("Preview supports at most 32 nested levels.");
            if (reader.NodeType == XmlNodeType.ProcessingInstruction)
                throw new NotSupportedException("Preview does not load XML processing instructions or stylesheets.");
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (++count > MaximumElements) throw new NotSupportedException("Preview supports at most 500 elements.");
            if (reader.NamespaceURI.Length != 0 || reader.Prefix.Length != 0 ||
                (reader.Depth == 0 ? reader.Name != "Cui" : reader.Name is not ("Page" or "Container" or "Text")))
                throw new NotSupportedException("Unsupported preview element '" + reader.Name + "'. " + SupportedSubset);
            var element = reader.Name; var depth = reader.Depth;
            if (element == "Page" && (depth != 1 || ++pages != 1) || depth == 1 && element != "Page")
                throw new NotSupportedException("Preview requires exactly one top-level Page.");
            if (reader.MoveToFirstAttribute())
            {
                do
                {
                    var name = reader.Name; var value = reader.Value;
                    if (reader.NamespaceURI.Length != 0 || (depth == 0 ? name is not ("id" or "version" or "product") : !Attributes.Contains(name)))
                        throw new NotSupportedException("Unsupported preview attribute '" + name + "'. " + SupportedSubset);
                    if (value.Contains('{') || value.Contains('}'))
                        throw new NotSupportedException("Preview accepts literal attribute values only; bindings and resources require their real application host.");
                    ValidateLiteral(element, name, value);
                } while (reader.MoveToNextAttribute());
                reader.MoveToElement();
            }
        }
        if (pages != 1) throw new NotSupportedException("Preview requires exactly one top-level Page.");
    }

    private static void ValidateLiteral(string element, string name, string value)
    {
        var property = name.Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
        if (property == "type" && (element != "Container" || value is not ("Vertical Stack" or "Horizontal Stack" or "Wrap")))
            throw new NotSupportedException("Preview Container Type supports Vertical Stack, Horizontal Stack and Wrap only.");
        if (property == "fontfamily" && value != "monospace")
            throw new NotSupportedException("Preview supports only the literal monospace font family; omit FontFamily to use the host font.");
        if (property is "foreground" or "background" or "borderbrush")
        {
            if (!(value.Length is 7 or 9 && value[0] == '#' && value.AsSpan(1).ToString().All(Uri.IsHexDigit)))
                throw new NotSupportedException("Preview colours must be literal #RRGGBB or #AARRGGBB, without resources or media.");
        }
        if (property is "width" or "height" or "minwidth" or "minheight" or "maxwidth" or "maxheight" or "fontsize" or "cellgap" or "cornerradius" or "margin" or "padding" or "borderthickness")
        {
            var pieces = value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
            var multiple = property is "margin" or "padding" or "borderthickness" or "cornerradius";
            if (pieces.Length is < 1 or > 4 || !multiple && pieces.Length != 1 || pieces.Any(piece => !double.TryParse(piece, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < 0 || number > 4096))
                throw new NotSupportedException("Preview layout values must be finite literal numbers between 0 and 4096.");
            if (property == "fontsize" && double.Parse(pieces[0], CultureInfo.InvariantCulture) is < 8 or > 96)
                throw new NotSupportedException("Preview font size supports 8 through 96.");
        }
        var supported = property switch
        {
            "fontweight" => value is "Thin" or "ExtraLight" or "Light" or "Normal" or "Medium" or "SemiBold" or "Bold" or "ExtraBold" or "Black",
            "fontstyle" => value is "Normal" or "Italic" or "Oblique",
            "textwrapping" => value is "NoWrap" or "Wrap" or "WrapWithOverflow",
            "textalignment" => value is "Left" or "Center" or "Right" or "Justify",
            "horizontalalignment" => value is "Left" or "Center" or "Right" or "Stretch",
            "verticalalignment" => value is "Top" or "Center" or "Bottom" or "Stretch",
            _ => true
        };
        if (!supported) throw new NotSupportedException("Unsupported static preview value for '" + name + "'.");
    }

    private static void DemandStaticSemantics(CuiDocument document)
    {
        if (document.Resources.Count != 0 || document.Styles.Count != 0 || document.Actions.Count != 0 || document.Templates.Count != 0 ||
            document.Imports.Count != 0 || document.Variables.Count != 0 || document.Effects.Count != 0)
            throw new NotSupportedException("Preview cannot execute document declarations. " + SupportedSubset);
        void Visit(IEnumerable<CuiComponent> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Type is not ("Page" or "Container" or "Text") || node.Actions.Count != 0 || node.Condition is not null ||
                    node.List is not null || node.Repeat is not null || node.ElseChildren.Count != 0 || node.IsDefinition || node.PropertyRegion is not null ||
                    node.IsThemeScope || node.Classes.Count != 0 || node.Groups.Count != 0 || node.Properties.Values.Any(value => value is not CuiLiteralValue))
                    throw new NotSupportedException("The compiled draft contains non-static preview semantics. " + SupportedSubset);
                if (node.TextParts.Any(part => part is not CuiLiteralTextPart))
                    throw new NotSupportedException("Dynamic text requires its real application host and is not previewed.");
                Visit(node.Children);
            }
        }
        Visit(document.Components);
    }
}
