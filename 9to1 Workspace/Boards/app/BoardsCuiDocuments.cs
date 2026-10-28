using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace CakeOS.Apps.Boards.App;

/// <summary>
/// Reads authored Boards views from the project's embedded compiled CUI artifact.
/// Runtime startup never searches for source files or reparses markup.
/// </summary>
public static class BoardsCuiDocuments
{
    public const string MainSourceName = "Boards.cui";
    public const string GraphSlidersSourceName = "GraphSliderCui.cui";
    public const string BlockGripSourceName = "BlockGripCui.cui";
    public const string BoardPropertiesSourceName = "BoardProperties.cui";
    public const string StartupFailureSourceName = "BoardsStartupFailure.cui";
    private static readonly Lazy<IReadOnlyDictionary<string, CuiDocument>> CompiledDocuments =
        new(LoadEmbeddedDocuments, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Creates the actual Boards host registry, including its authored folder icon.</summary>
    public static CuiControlRegistry CreateControlRegistry()
    {
        var registry = new CuiControlRegistry();
        registry.RegisterImageSourceResolver(BoardsCuiIconResolver.Resolve);
        return registry;
    }

    /// <summary>Creates a loader with the Boards-owned palette and accent identity.</summary>
    public static CuiControlLoader CreateLoader() => CreateLoader(BoardsTheme.SurfaceThemeContext);

    public static CuiControlLoader CreateLoader(CuiThemePreferenceContext? context)
    {
        var loader = new CuiControlLoader(CreateControlRegistry());
        loader.SetSurface("Boards", "app.boards");
        if (context is not null) loader.SetThemePreferenceContext(context);
        return loader;
    }

    public static CuiDocument Load(string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        return CompiledDocuments.Value.TryGetValue(sourceName, out var document)
            ? document
            : throw RuntimeAdmission("CUIR062", $"The embedded compiled CUI bundles do not contain '{sourceName}'.", sourceName);
    }

    private static IReadOnlyDictionary<string, CuiDocument> LoadEmbeddedDocuments()
    {
        var artifactAssembly = typeof(BoardsCuiDocuments).Assembly;
        var documents = new List<(string Source, CuiDocument Document)>();
        var v2Bytes = ReadEmbeddedArtifact(artifactAssembly, CuiCompiledArtifactCodec.ResourceLogicalName);
        if (v2Bytes is not null)
        {
            CuiCompiledArtifact artifact;
            try { artifact = CuiCompiledArtifactCodec.ReadArtifact(v2Bytes); }
            catch (CuiV2AdmissionException admission)
            {
                throw RuntimeAdmission(admission.Code, admission.Message, MainSourceName);
            }
            CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, Span(MainSourceName));
            documents.AddRange(artifact.Documents.Select(document =>
                (document.Source, document.Semantics.ToCuiDocument())));
        }

        var v1Bytes = ReadEmbeddedArtifact(artifactAssembly, CuiCompiledArtifactCodec.V1ResourceLogicalName);
        if (v1Bytes is not null)
        {
            CuiV1LoadedArtifact artifact;
            try { artifact = CuiV1ArtifactReader.ReadArtifact(v1Bytes); }
            catch (CuiV1AdmissionException admission)
            {
                throw RuntimeAdmission(admission.Code, admission.Message, MainSourceName);
            }
            CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, Span(MainSourceName));
            documents.AddRange(artifact.Documents.Select(document => (document.Source, document.Semantics)));
        }

        var result = new Dictionary<string, CuiDocument>(StringComparer.Ordinal);
        foreach (var (source, document) in documents)
        {
            if (document.Components.Count == 0)
                throw RuntimeAdmission("CUIR063", $"The compiled semantic model for '{source}' is empty.", source);
            if (!result.TryAdd(source, document))
                throw RuntimeAdmission("CUIR062", $"The compiled CUI bundles contain duplicate source '{source}'.", source);
        }
        if (result.Count == 0)
            throw RuntimeAdmission("CUIR062", "The application assembly contains no compiled CUI documents.", MainSourceName);
        return result;
    }

    private static byte[]? ReadEmbeddedArtifact(System.Reflection.Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static CuiRuntimeLoadException RuntimeAdmission(string code, string message, string sourceName) =>
        new(new CuiDiagnostic(code, CuiDiagnosticSeverity.Error, message, Span(sourceName)));

    private static CuiSourceSpan Span(string sourceName) => CuiSourceSpan.At(sourceName, 1, 1);
}
