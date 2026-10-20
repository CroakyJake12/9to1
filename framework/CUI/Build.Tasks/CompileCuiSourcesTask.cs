using System.Security.Cryptography;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace CakeOS.Cui.Build;

/// <summary>
/// Compiles the complete declared CUI source set with the canonical compiler before resources or
/// assembly compilation. It emits a DISTINCT typed V2 artifact when any document is stateful
/// (FormatVersion2 / ABI2 / "CUI.Compilation.v2"), otherwise the genuine V1 compiled projection
/// (FormatVersion1 / ABI1 / "CUI.Compilation.v1"). Statefulness is classified from validated typed
/// semantics and import/dependency closure — never from a filename. Stateful sources are excluded
/// from all shipped raw identities while stateless raw snapshots retain their V1 shape and metadata.
/// </summary>
public sealed class CompileCuiSourcesTask : Microsoft.Build.Utilities.Task
{
    [Required] public ITaskItem[] Sources { get; set; } = [];
    [Required] public string ProjectDirectory { get; set; } = "";
    [Required] public string OutputFile { get; set; } = "";
    [Required] public string IntermediateOutputPath { get; set; } = "";
    [Required] public string OutputDirectory { get; set; } = "";
    public string PublishDirectory { get; set; } = "";
    public string[] RequiredCapabilities { get; set; } = [];
    public ITaskItem[] ResourcesToSnapshot { get; set; } = [];
    [Output] public ITaskItem[] CompiledArtifacts { get; private set; } = [];
    [Output] public ITaskItem[] SnapshotResources { get; private set; } = [];
    [Output] public ITaskItem[] SourceSnapshots { get; private set; } = [];
    [Output] public ITaskItem[] StatefulPackagedSources { get; private set; } = [];
    [Output] public ITaskItem[] StaleOwnedOutputs { get; private set; } = [];

    public override bool Execute()
    {
        CompiledArtifacts = [];
        SnapshotResources = [];
        SourceSnapshots = [];
        StatefulPackagedSources = [];
        StaleOwnedOutputs = [];
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

            var compiled = new List<CompiledInput>();
            foreach (var input in inputs)
            {
                var compilation = new CuiCompiler().Compile(input.Text, input.Name, new(metadata, imports));
                foreach (var diagnostic in compilation.Diagnostics) Report(diagnostic, root);
                if (compilation.Succeeded && compilation.Output is { } output)
                {
                    var localStateful = (output.Semantics.Variables.Count > 0)
                        || output.Semantics.Actions.Values.Any(action => action.LocalTarget is not null);
                    compiled.Add(new CompiledInput(input, output, Hash(input.Bytes), localStateful));
                }
            }
            if (Log.HasLoggedErrors) return false;

            // Propagate statefulness through import/dependency closure: a document that imports a
            // stateful source becomes stateful itself. If a safe closure cannot be determined the
            // import would already have failed compilation; we only ever propagate between
            // successfully compiled inputs.
            var byName = compiled.ToDictionary(entry => entry.Input.Name, entry => entry, StringComparer.Ordinal);
            bool propagated = true;
            while (propagated)
            {
                propagated = false;
                foreach (var entry in compiled)
                {
                    if (entry.Stateful) continue;
                    var dependsOnStateful = entry.Output.Semantics.Imports.Any(import =>
                        byName.TryGetValue(import.Source, out var imported) && imported.Stateful);
                    if (dependsOnStateful)
                    {
                        entry.Stateful = true;
                        propagated = true;
                    }
                }
            }

            var hasStateful = compiled.Any(entry => entry.Stateful);
            var compilerPath = typeof(CuiCompiler).Assembly.Location;
            var compilerSha256 = Hash(File.ReadAllBytes(compilerPath));

            // Build the artifact before any effects. Failed compilation never publishes partial or
            // current-success artifacts, and never destroys recoverable prior evidence.
            byte[] artifactBytes;
            string resourceIdentity;
            if (hasStateful)
            {
                var envelope = AggregateMetadata(compiled.Select(entry => entry.Output.Metadata));
                var v2 = new CuiCompiledArtifact(
                    CuiCompiledArtifactCodec.FormatName,
                    CuiCompiledArtifactCodec.FormatVersion,
                    compilerSha256,
                    envelope,
                    compiled.Select(entry => CuiV2Projection.ToDocument(entry.Output, entry.Input.Name, entry.Sha256)).ToArray());
                artifactBytes = CuiCompiledArtifactCodec.WriteArtifact(v2);
                resourceIdentity = CuiCompiledArtifactCodec.ResourceLogicalName;
            }
            else
            {
                var bundle = compiled.Select(entry => new CuiV1Compiled(entry.Output, entry.Input.Name, entry.Sha256)).ToArray();
                artifactBytes = CuiV1ArtifactCodec.WriteBundle(bundle, compilerSha256);
                resourceIdentity = CuiCompiledArtifactCodec.V1ResourceLogicalName;
            }

            var destination = Path.GetFullPath(BuildPath(OutputFile), root);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Publish(destination, artifactBytes);

            // Provenance snapshots: exact original bytes for every source (intermediate build
            // evidence). These are never shipped as unsupported raw app source.
            var snapshots = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var sourcesDir = Path.Combine(Path.GetDirectoryName(destination)!, "sources");
            foreach (var input in inputs)
            {
                var snapshot = Path.Combine(sourcesDir, Hash(input.Bytes) + Path.GetExtension(input.Path).ToLowerInvariant());
                Publish(snapshot, input.Bytes);
                snapshots.Add(input.Path, snapshot);
            }

            // Map packaged source items to raw snapshots for STATELESS sources only. Stateful
            // sources remain compilation inputs and provenance snapshots but are excluded from all
            // shipped raw identities.
            var statefulByPath = compiled.Where(entry => entry.Stateful)
                .Select(entry => entry.Input.Path)
                .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var statefulPackaged = new List<ITaskItem>();
            var mappedResources = new List<ITaskItem>();
            foreach (var item in ResourcesToSnapshot)
            {
                var original = Path.GetFullPath(BuildPath(item.ItemSpec), root);
                if (!snapshots.TryGetValue(original, out var snapshot))
                    throw new ArgumentException($"Packaged CUI source '{original}' was not compiled.");
                if (statefulByPath.Contains(original))
                {
                    var statefulItem = new TaskItem(original, item.CloneCustomMetadata());
                    statefulItem.SetMetadata("CuiOriginalFullPath", original);
                    statefulPackaged.Add(statefulItem);
                    continue;
                }
                var mapped = new TaskItem(snapshot, item.CloneCustomMetadata());
                mapped.SetMetadata("CuiOriginalFullPath", original);
                var relative = Path.GetRelativePath(root, original).Replace('\\', '/');
                var link = mapped.GetMetadata("Link");
                if (string.IsNullOrEmpty(link)) mapped.SetMetadata("Link", relative);
                if (string.IsNullOrEmpty(mapped.GetMetadata("TargetPath")))
                    mapped.SetMetadata("TargetPath", !string.IsNullOrEmpty(link) ? link : relative.StartsWith("../", StringComparison.Ordinal) ? Path.GetFileName(original) : relative);
                mappedResources.Add(mapped);
            }

            // Incremental raw-output ownership: remove only stale build-owned raw destinations that
            // are no longer shipped (source became stateful or was removed), strictly under the
            // validated output/publish roots. Never delete sources or unrelated/user files.
            var manifestPath = Path.Combine(Path.GetFullPath(BuildPath(IntermediateOutputPath), root), "Cui", "packaged-raw-manifest.json");            var currentOwned = new List<OwnedRaw>();
            var outputRoot = Path.GetFullPath(BuildPath(OutputDirectory), root);
            foreach (var item in mappedResources)
            {
                var destinationRaw = ShippedDestination(outputRoot, root, item);
                if (destinationRaw is null) continue;
                currentOwned.Add(new OwnedRaw(item.GetMetadata("CuiOriginalFullPath"), destinationRaw));
            }
            if (!string.IsNullOrWhiteSpace(PublishDirectory))
            {
                var publishRoot = Path.GetFullPath(BuildPath(PublishDirectory), root);
                foreach (var item in mappedResources)
                {
                    var destinationRaw = ShippedDestination(publishRoot, root, item);
                    if (destinationRaw is null) continue;
                    currentOwned.Add(new OwnedRaw(item.GetMetadata("CuiOriginalFullPath"), destinationRaw));
                }
            }

            var stale = ComputeStaleOwnedOutputs(manifestPath, currentOwned, root, outputRoot);
            foreach (var owned in stale)
            {
                if (File.Exists(owned.Destination))
                {
                    File.Delete(owned.Destination);
                    Log.LogMessage(MessageImportance.Low, "Removed stale build-owned raw CUI output: {0}", owned.Destination);
                }
            }
            StaleOwnedOutputs = stale.Select(owned => (ITaskItem)new TaskItem(owned.Destination)).ToArray();
            WriteManifest(manifestPath, currentOwned);

            var artifactItem = new TaskItem(destination);
            artifactItem.SetMetadata("CuiResourceLogicalName", resourceIdentity);
            CompiledArtifacts = [artifactItem];
            SnapshotResources = mappedResources.ToArray();
            SourceSnapshots = snapshots.Values.Distinct(StringComparer.Ordinal).Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            StatefulPackagedSources = statefulPackaged.ToArray();
            Log.LogMessage(MessageImportance.High, "CUI compiled {0} source(s): {1} ({2})", inputs.Count, destination, resourceIdentity);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            Error("CUIB003", ProjectDirectory, error.Message);
            return false;
        }
    }

    private static CuiCompilationMetadata AggregateMetadata(IEnumerable<CuiCompilationMetadata> all)
    {
        var list = all.ToArray();
        var caps = new HashSet<string>(StringComparer.Ordinal);
        string? maxAbi = null;
        foreach (var entry in list)
        {
            caps.UnionWith(entry.RequiredCapabilities);
            maxAbi = MaxAbi(maxAbi, entry.RuntimeAbiVersion);
        }
        var first = list[0];
        return new CuiCompilationMetadata(first.LanguageVersion, maxAbi ?? first.RuntimeAbiVersion, first.ComponentRegistryVersion, caps);
    }

    private static string? MaxAbi(string? current, string candidate)
    {
        if (current is null) return candidate;
        if (int.TryParse(current, out var a) && int.TryParse(candidate, out var b))
            return b > a ? candidate : current;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0 ? candidate : current;
    }

    private static List<OwnedRaw> ComputeStaleOwnedOutputs(
        string manifestPath, List<OwnedRaw> currentOwned, string projectRoot, string outputRoot)
    {
        var previous = ReadManifest(manifestPath);
        if (previous.Count == 0)
            return new List<OwnedRaw>();

        var currentByOrig = currentOwned
            .GroupBy(owned => owned.Original, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(owned => owned.Destination).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);

        var stale = new List<OwnedRaw>();
        foreach (var owned in previous)
        {
            var stillShipped = currentByOrig.TryGetValue(owned.Original, out var destinations)
                && destinations.Contains(owned.Destination);
            if (stillShipped) continue;

            var full = Path.GetFullPath(owned.Destination);
            var isUnderOutput = full.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || string.Equals(full, outputRoot, StringComparison.Ordinal);
            var isSourceOrOutside = full.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !isUnderOutput;
            if (isUnderOutput && !isSourceOrOutside && !string.Equals(full, projectRoot, StringComparison.Ordinal))
                stale.Add(owned);
        }
        return stale;
    }

    private static List<OwnedRaw> ReadManifest(string path)
    {
        if (!File.Exists(path)) return new List<OwnedRaw>();
        try
        {
            var model = JsonSerializer.Deserialize<ManifestModel>(File.ReadAllText(path));
            return model?.Owned?.Select(entry => new OwnedRaw(entry.Original, entry.Destination)).ToList() ?? new List<OwnedRaw>();
        }
        catch (JsonException)
        {
            // A corrupt/unreadable intermediate manifest must never trigger arbitrary deletion; we
            // simply treat it as empty and overwrite it with the current owned set.
            return new List<OwnedRaw>();
        }
    }

    private static void WriteManifest(string path, List<OwnedRaw> owned)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var model = new ManifestModel { Owned = owned.Select(entry => new OwnedRawModel(entry.Original, entry.Destination)).ToArray() };
        File.WriteAllText(path, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string? ShippedDestination(string outputRoot, string projectRoot, ITaskItem item)
    {
        var target = item.GetMetadata("TargetPath");
        if (string.IsNullOrEmpty(target)) target = item.GetMetadata("Link");
        if (string.IsNullOrEmpty(target))
        {
            var original = item.GetMetadata("CuiOriginalFullPath");
            if (string.IsNullOrEmpty(original)) return null;
            target = Path.GetRelativePath(projectRoot, original);
        }
        var full = Path.GetFullPath(Path.Combine(outputRoot, target.Replace('\\', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, outputRoot, StringComparison.Ordinal))
            return null;
        return full;
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

    private static void Publish(string destination, byte[] bytes)
    {
        if (File.Exists(destination) && File.ReadAllBytes(destination).AsSpan().SequenceEqual(bytes)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var staging = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(staging, bytes);
            File.Move(staging, destination, overwrite: true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Input(string Path, string Name, byte[] Bytes, string Text, CuiDocument Document);
    private sealed class CompiledInput
    {
        public CompiledInput(Input input, CuiCompiledDocument output, string sha256, bool stateful)
        {
            Input = input;
            Output = output;
            Sha256 = sha256;
            Stateful = stateful;
        }

        public Input Input { get; }
        public CuiCompiledDocument Output { get; }
        public string Sha256 { get; }
        public bool Stateful { get; set; }
    }
    private sealed record OwnedRaw(string Original, string Destination);
    private sealed class ManifestModel
    {
        public OwnedRawModel[] Owned { get; set; } = [];
    }
    private sealed record OwnedRawModel(string Original, string Destination);
}
