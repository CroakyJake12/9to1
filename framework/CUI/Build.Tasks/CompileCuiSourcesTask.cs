using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CakeOS.Cui.Build;

/// <summary>
/// Compiles the complete declared CUI source set with the canonical compiler before
/// resources or assembly compilation. The artifact is build evidence and a direct
/// typed representation, not generated AXAML or a replacement runtime loader.
/// </summary>
public sealed class CompileCuiSourcesTask : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] Sources { get; set; } = [];
    [Required] public string ProjectDirectory { get; set; } = "";
    [Required] public string OutputFile { get; set; } = "";
    public string[] RequiredCapabilities { get; set; } = [];
    [Output] public ITaskItem[] CompiledArtifacts { get; private set; } = [];

    public override bool Execute()
    {
        CompiledArtifacts = [];
        try
        {
            var root = Path.GetFullPath(BuildPath(ProjectDirectory));
            var files = Sources.Select(item => Path.GetFullPath(BuildPath(item.ItemSpec), root))
                .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            var inputs = new List<Input>();
            foreach (var path in files)
            {
                if (!CuiProjectItems.IsCuiSourceFamily(path))
                {
                    Error("CUIB001", path, "Application source must use .cui, .cui-theme or .cui-prefab; .axaml, .xaml and .hui are forbidden.");
                    continue;
                }
                if (!File.Exists(path))
                {
                    Error("CUIB002", path, "Declared CUI source does not exist.");
                    continue;
                }
                // Snapshot once: indexing, compilation and hashes describe identical bytes.
                var bytes = File.ReadAllBytes(path);
                var text = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
                var name = Path.GetRelativePath(root, path).Replace('\\', '/');
                var document = new CuiRichParser().Parse(text, name);
                inputs.Add(new(path, name, bytes, text, document));
            }
            if (Log.HasLoggedErrors) return false;

            var imports = inputs.Select(input => new CuiImportedSymbols(input.Name,
                input.Document.Components.SelectMany(component => component.DescendantsAndSelf())
                    .Where(component => component.IsDefinition).Select(component => component.Name ?? component.Type)
                    .Concat(input.Document.Templates.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                input.Document.Imports.Select(import => import.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray())).ToArray();
            var metadata = new CuiCompilationMetadata(CuiRuntimeCompatibility.LanguageVersion,
                CuiRuntimeCompatibility.RuntimeAbiVersion, CuiControlRegistry.ComponentRegistryVersion, RequiredCapabilities);
            var documents = new List<object>();
            foreach (var input in inputs)
            {
                var compilation = new CuiCompiler().Compile(input.Text, input.Name, new(metadata, imports));
                foreach (var diagnostic in compilation.Diagnostics) Report(diagnostic, root);
                if (compilation.Succeeded && compilation.Output is { } output)
                    documents.Add(new { source = input.Name, sha256 = Hash(input.Bytes), compilation = output });
            }
            if (Log.HasLoggedErrors) return false;

            var compilerPath = typeof(CuiCompiler).Assembly.Location;
            var artifact = new
            {
                format = "cui-build-compilation", formatVersion = 1,
                compilerSha256 = Hash(File.ReadAllBytes(compilerPath)),
                metadata, documents
            };
            var bytesOut = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions());
            var destination = Path.GetFullPath(BuildPath(OutputFile), root);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // Failed compilation never publishes partial/current-success artifacts. A previous
            // artifact remains identifiable by its source hashes and is not returned to MSBuild.
            if (!File.Exists(destination) || !File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytesOut))
            {
                var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(staging, bytesOut);
                    File.Move(staging, destination, overwrite: true);
                }
                finally { if (File.Exists(staging)) File.Delete(staging); }
            }
            CompiledArtifacts = [new TaskItem(destination)];
            Log.LogMessage(MessageImportance.High, "CUI compiled {0} source(s): {1}", inputs.Count, destination);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            Error("CUIB003", ProjectDirectory, error.Message);
            return false;
        }
    }

    private void Report(CuiDiagnostic diagnostic, string root)
    {
        var span = diagnostic.Span;
        var path = Path.GetFullPath(span.SourceName, root);
        var message = diagnostic.Message + (diagnostic.RelatedSpan is { } related ? $" Related location: {related}." : "");
        if (diagnostic.Severity == CuiDiagnosticSeverity.Error)
            Log.LogError("CUI", diagnostic.Code, null, path, span.Start.Line, span.Start.Column, span.End.Line, span.End.Column, message);
        else if (diagnostic.Severity == CuiDiagnosticSeverity.Warning)
            Log.LogWarning("CUI", diagnostic.Code, null, path, span.Start.Line, span.Start.Column, span.End.Line, span.End.Column, message);
    }

    private void Error(string code, string path, string message) =>
        Log.LogError("CUI", code, null, path, 1, 1, 1, 1, message);

    private static string BuildPath(string value) => value.Replace('\\', Path.DirectorySeparatorChar);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static JsonSerializerOptions JsonOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            // Preserve typed values and text-expression payloads rather than serializing
            // only their abstract base Span. Discriminators are internal artifact v1 data.
            Type[] derived = info.Type == typeof(CuiValue)
                ? [typeof(CuiLiteralValue), typeof(CuiBindingValue), typeof(CuiResourceValue), typeof(CuiInvalidValue)]
                : info.Type == typeof(CuiTextPart)
                    ? [typeof(CuiLiteralTextPart), typeof(CuiExpressionTextPart)] : [];
            if (derived.Length == 0) return;
            info.PolymorphismOptions = new JsonPolymorphismOptions();
            foreach (var type in derived) info.PolymorphismOptions.DerivedTypes.Add(new JsonDerivedType(type, type.Name));
        });
        return new() { WriteIndented = true, TypeInfoResolver = resolver, Converters = { new JsonStringEnumConverter() } };
    }

    private sealed record Input(string Path, string Name, byte[] Bytes, string Text, CuiDocument Document);
}
