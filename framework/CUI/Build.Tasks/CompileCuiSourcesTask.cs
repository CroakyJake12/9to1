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
                CuiCompiledArtifactCodec.V1RuntimeAbiVersion, CuiControlRegistry.ComponentRegistryVersion, RequiredCapabilities);

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

            var compilerPath = typeof(CuiCompiler).Assembly.Location;
            var compilerSha256 = Hash(File.ReadAllBytes(compilerPath));

            // Promote ABI/capability metadata for every stateful entry (local or promoted through the
            // import/dependency closure) so its document genuinely declares the stateful ABI2 and the
            // union of required capabilities across the closure it participates in. A stateless-looking
            // importer of a stateful child must not keep declaring ABI1.
            PromoteStatefulMetadata(compiled, byName);

            var statefulEntries = compiled.Where(entry => entry.Stateful).ToArray();
            var statelessEntries = compiled.Where(entry => !entry.Stateful).ToArray();

            // Prepare artifact payloads BEFORE any effects so a validation failure never publishes or
            // replaces a previously successful artifact. A mixed project emits a DISTINCT V2 artifact
            // for the stateful subset and a genuine V1 bundle for the stateless subset, so precompiled
            // V1 consumers keep the stateless documents.
            var artifactItems = new List<ITaskItem>();
            var pendingPublishes = new List<(string Destination, byte[] Bytes)>();
            if (statefulEntries.Length > 0)
            {
                var envelope = AggregateMetadata(statefulEntries.Select(entry => entry.Output.Metadata));
                var v2 = new CuiCompiledArtifact(
                    CuiCompiledArtifactCodec.FormatName,
                    CuiCompiledArtifactCodec.FormatVersion,
                    compilerSha256,
                    envelope,
                    statefulEntries.Select(entry => CuiV2Projection.ToDocument(entry.Output, entry.Input.Name, entry.Sha256)).ToArray());
                var dest = Path.GetFullPath(BuildPath(V2OutputFile(OutputFile)), root);
                pendingPublishes.Add((dest, CuiCompiledArtifactCodec.WriteArtifact(v2)));
                artifactItems.Add(ArtifactItem(dest, CuiCompiledArtifactCodec.ResourceLogicalName));
            }
            if (statelessEntries.Length > 0)
            {
                var bundle = statelessEntries.Select(entry =>
                    new CuiV1ArtifactCodec.CuiV1Compiled(entry.Output, entry.Input.Name, entry.Sha256)).ToArray();
                var dest = Path.GetFullPath(BuildPath(V1OutputFile(OutputFile)), root);
                pendingPublishes.Add((dest, CuiV1ArtifactCodec.WriteBundle(bundle, compilerSha256)));
                artifactItems.Add(ArtifactItem(dest, CuiCompiledArtifactCodec.V1ResourceLogicalName));
            }

            // Provenance snapshots: exact original bytes for every source (intermediate build
            // evidence). These are never shipped as unsupported raw app source. Computed now;
            // written only after all path/ownership validation succeeds.
            var snapshots = new Dictionary<string, string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var sourcesDir = Path.Combine(Path.GetFullPath(BuildPath(IntermediateOutputPath), root), "Cui", "sources");
            foreach (var input in inputs)
            {
                var snapshot = Path.Combine(sourcesDir, Hash(input.Bytes) + Path.GetExtension(input.Path).ToLowerInvariant());
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

            // Incremental raw-output ownership: only claim destinations ACTUALLY produced by MSBuild
            // copy/publish semantics. Output copy is valid only for Content/None/EmbeddedResource with
            // a real copy mode (Always/PreserveNewest/IfDifferent); CopyToOutputDirectory=Never and
            // never-copied item types create no ownership entry. Publish is modelled separately,
            // honoring CopyToPublishDirectory defaults (Content/None publish unless Never) and
            // publish-only items. Each entry records the exact artifact bytes expected at the
            // destination, so stale cleanup refuses to delete a path whose current contents are no
            // longer that owned artifact (preserving unrelated same-path files).
            var manifestPath = Path.Combine(Path.GetFullPath(BuildPath(IntermediateOutputPath), root), "Cui", "packaged-raw-manifest.json");
            var outputRoot = Path.GetFullPath(BuildPath(OutputDirectory), root);
            var publishRoot = string.IsNullOrWhiteSpace(PublishDirectory)
                ? null : Path.GetFullPath(BuildPath(PublishDirectory), root);

            ValidateOwnedRoots(outputRoot, publishRoot, root);

            var sourcePaths = inputs
                .Select(input => Path.GetFullPath(input.Path))
                .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var protectedPaths = new HashSet<string>(sourcePaths, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var snapshot in snapshots.Values)
                protectedPaths.Add(Path.GetFullPath(snapshot));

            // Read the previous custody manifest strictly: a corrupt/ambiguous record must fail the
            // build (preserving evidence) instead of being erased by a successful package.
            var previousOwned = ReadManifestStrict(manifestPath);

            // Write provenance snapshots (benign intermediates) so ownership hashes are real bytes.
            foreach (var input in inputs) Publish(snapshots[input.Path], input.Bytes);

            var currentOwned = new List<OwnedRaw>();
            foreach (var item in mappedResources)
                SnapshotOwned(currentOwned, outputRoot, publishRoot, root, item, sourcePaths);

            var stale = ComputeStaleOwnedOutputs(previousOwned, currentOwned, root, outputRoot, publishRoot, protectedPaths);

            // All path/ownership validation succeeded. Now stage and commit the compiled artifact
            // outputs (a validation failure above would have returned without touching them).
            foreach (var (destination, bytes) in pendingPublishes) Publish(destination, bytes);

            // Delete only stale build-owned outputs whose current bytes still equal the owned artifact,
            // and never a source or a preserved replacement. Only actually-deleted destinations are
            // reported as cleaned build outputs (FileWrites).
            var deletedOwned = new List<ITaskItem>();
            foreach (var owned in stale)
            {
                if (!File.Exists(owned.Destination) || !FileHashMatches(owned)) continue;
                var full = Path.GetFullPath(owned.Destination);
                if (protectedPaths.Contains(full) || string.Equals(full, root, StringComparison.Ordinal)) continue;
                File.Delete(owned.Destination);
                Log.LogMessage(MessageImportance.Low, "Removed stale build-owned raw CUI output: {0}", owned.Destination);
                deletedOwned.Add(new TaskItem(owned.Destination));
            }
            StaleOwnedOutputs = deletedOwned.ToArray();
            WriteManifest(manifestPath, currentOwned);

            CompiledArtifacts = artifactItems.ToArray();
            SnapshotResources = mappedResources.ToArray();
            SourceSnapshots = snapshots.Values.Distinct(StringComparer.Ordinal).Select(path => (ITaskItem)new TaskItem(path)).ToArray();
            StatefulPackagedSources = statefulPackaged.ToArray();
            Log.LogMessage(MessageImportance.High, "CUI compiled {0} source(s): {1} compiled artifact(s)", inputs.Count, artifactItems.Count);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidDataException)
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

    private static void PromoteStatefulMetadata(List<CompiledInput> compiled, Dictionary<string, CompiledInput> byName)
    {
        foreach (var entry in compiled.Where(e => e.Stateful))
        {
            if (entry.PromotedMetadata) continue;
            var caps = new List<string>(entry.Output.Metadata.RequiredCapabilities);
            void AddImported(string source)
            {
                if (!byName.TryGetValue(source, out var imported)) return;
                if (imported.Stateful) caps.AddRange(imported.Output.Metadata.RequiredCapabilities);
                foreach (var sub in imported.Output.Semantics.Imports) AddImported(sub.Source);
            }
            foreach (var import in entry.Output.Semantics.Imports) AddImported(import.Source);
            var effective = new CuiCompilationMetadata(
                entry.Output.Metadata.LanguageVersion,
                CuiCompiledArtifactCodec.V2RuntimeAbiVersion,
                entry.Output.Metadata.ComponentRegistryVersion,
                caps.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            entry.Output = entry.Output with { Metadata = effective };
            entry.PromotedMetadata = true;
        }
    }

    private static string V2OutputFile(string outputFile) =>
        Path.Combine(Path.GetDirectoryName(outputFile) ?? "", "compilation.v2.cuic.json");

    private static string V1OutputFile(string outputFile) =>
        Path.Combine(Path.GetDirectoryName(outputFile) ?? "", "compilation.v1.cuic.json");

    private static ITaskItem ArtifactItem(string destination, string logicalName)
    {
        var item = new TaskItem(destination);
        item.SetMetadata("CuiResourceLogicalName", logicalName);
        return item;
    }

    private static List<OwnedRaw> ComputeStaleOwnedOutputs(
        List<OwnedRaw> previous, List<OwnedRaw> currentOwned, string projectRoot,
        string outputRoot, string? publishRoot, ISet<string> protectedPaths)
    {
        if (previous.Count == 0)
            return new List<OwnedRaw>();

        var validRoots = new List<string>();
        if (!string.IsNullOrEmpty(outputRoot)) validRoots.Add(outputRoot);
        if (!string.IsNullOrEmpty(publishRoot)) validRoots.Add(publishRoot!);

        var currentByOrig = currentOwned
            .GroupBy(owned => owned.Original, OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(owned => owned.Destination).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);

        var stale = new List<OwnedRaw>();
        foreach (var owned in previous)
        {
            var stillShipped = currentByOrig.TryGetValue(owned.Original, out var destinations)
                && destinations.Contains(owned.Destination);
            if (stillShipped) continue;

            string full;
            try { full = Path.GetFullPath(owned.Destination); }
            catch { continue; }
            if (string.Equals(full, projectRoot, StringComparison.Ordinal)) continue;
            // Never treat a source path, a preserved replacement, or an arbitrary/outside-root path
            // as a stale owned output.
            if (protectedPaths.Contains(full)) continue;
            var underAnyRoot = validRoots.Any(root => IsPhysicallyInside(full, root));
            if (underAnyRoot) stale.Add(owned);
        }
        return stale;
    }

    private static readonly HashSet<string> ValidCopyModes = new(StringComparer.OrdinalIgnoreCase)
        { "Always", "PreserveNewest", "IfDifferent" };

    /// <summary>Output-copy eligibility matches actual MSBuild semantics: only Content/None/
    /// EmbeddedResource with a real copy mode are copied to the output directory. A
    /// CopyToOutputDirectory=Never value is not a copy mode and produces no owned output.</summary>
    private static bool IsOutputCopyEligible(ITaskItem item)
    {
        var type = item.GetMetadata("CuiItemType");
        if (type is not ("Content" or "None" or "EmbeddedResource")) return false;
        return ValidCopyModes.Contains(item.GetMetadata("CopyToOutputDirectory"));
    }

    /// <summary>Publish eligibility honors CopyToPublishDirectory independently of output copy: an
    /// explicit Always/PreserveNewest/IfDifferent publishes; Never does not; Content/None publish by
    /// default while embedded-only item types do not. Publish-only items are therefore tracked.</summary>
    private static bool IsPublishEligible(ITaskItem item)
    {
        var copyToPublish = item.GetMetadata("CopyToPublishDirectory");
        if (string.Equals(copyToPublish, "Never", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(copyToPublish)) return true;
        var type = item.GetMetadata("CuiItemType");
        return type is "Content" or "None";
    }

    private static void SnapshotOwned(
        List<OwnedRaw> owned, string outputRoot, string? publishRoot, string projectRoot,
        ITaskItem item, ISet<string> sourcePaths)
    {
        var original = item.GetMetadata("CuiOriginalFullPath");
        if (string.IsNullOrEmpty(original)) return;
        var snapshotPath = item.ItemSpec;
        if (string.IsNullOrEmpty(snapshotPath)) return;
        var snapshotSha = File.Exists(snapshotPath) ? Hash(File.ReadAllBytes(snapshotPath)) : "";

        if (IsOutputCopyEligible(item))
        {
            var outputDest = ShippedDestination(outputRoot, projectRoot, item, sourcePaths);
            owned.Add(new OwnedRaw(original, outputDest, snapshotSha));
        }
        if (publishRoot is not null && IsPublishEligible(item))
        {
            var publishDest = ShippedDestination(publishRoot!, projectRoot, item, sourcePaths);
            if (!owned.Any(entry => string.Equals(entry.Destination, publishDest, StringComparison.Ordinal)))
                owned.Add(new OwnedRaw(original, publishDest, snapshotSha));
        }
    }

    private static bool FileHashMatches(OwnedRaw owned)
    {
        if (!File.Exists(owned.Destination)) return false;
        return string.Equals(Hash(File.ReadAllBytes(owned.Destination)), owned.Sha256, StringComparison.Ordinal);
    }

    /// <summary>Reads the previous custody manifest strictly. A corrupt/ambiguous record must fail
    /// the build (preserving its evidence) rather than being silently treated as empty and erased by
    /// a successful package.</summary>
    private static List<OwnedRaw> ReadManifestStrict(string path)
    {
        if (!File.Exists(path)) return new List<OwnedRaw>();
        ManifestModel? model;
        try
        {
            model = JsonSerializer.Deserialize<ManifestModel>(File.ReadAllText(path));
        }
        catch (Exception error) when (error is JsonException)
        {
            throw new InvalidDataException($"The packaged raw CUI custody manifest is corrupt and cannot be validated safely: {error.Message}");
        }
        if (model?.Owned is null)
            throw new InvalidDataException("The packaged raw CUI custody manifest is ambiguous (missing owned list); refusing an unsafe package.");
        return model.Owned
            .Select(entry => new OwnedRaw(entry.Original ?? "", entry.Destination ?? "", entry.Sha256 ?? ""))
            .ToList();
    }

    private static void WriteManifest(string path, List<OwnedRaw> owned)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var model = new ManifestModel { Owned = owned.Select(entry => new OwnedRawModel(entry.Original, entry.Destination, entry.Sha256)).ToArray() };
        File.WriteAllText(path, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Validates that allocated output/publish roots are distinct from the project root so
    /// generated products cannot land beside/lap sources and risk accidental deletion.</summary>
    private static void ValidateOwnedRoots(string outputRoot, string? publishRoot, string projectRoot)
    {
        if (string.Equals(outputRoot, projectRoot, StringComparison.Ordinal))
            throw new ArgumentException("The CUI output directory overlaps the project root; refusing unsafe packaging.");
        if (publishRoot is not null && string.Equals(publishRoot, projectRoot, StringComparison.Ordinal))
            throw new ArgumentException("The CUI publish directory overlaps the project root; refusing unsafe packaging.");
    }

    /// <summary>Resolves a destination under an allocated root, failing safely on escaping, absolute
    /// or physically link/junction-escaped TargetPath metadata instead of silently dropping custody
    /// while the item still reaches MSBuild copy items.</summary>
    private static string ShippedDestination(string outputRoot, string projectRoot, ITaskItem item, ISet<string> sourcePaths)
    {
        var target = item.GetMetadata("TargetPath");
        if (string.IsNullOrEmpty(target)) target = item.GetMetadata("Link");
        if (string.IsNullOrEmpty(target))
        {
            var original = item.GetMetadata("CuiOriginalFullPath");
            if (string.IsNullOrEmpty(original)) throw new ArgumentException("A packaged CUI item is missing its original source identity.");
            target = Path.GetRelativePath(projectRoot, original);
        }
        target = target.Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathFullyQualified(target))
            throw new ArgumentException($"Unsafe absolute TargetPath '{target}' for CUI item '{item.GetMetadata("CuiOriginalFullPath")}'.");
        var full = Path.GetFullPath(Path.Combine(outputRoot, target));
        if (!full.StartsWith(outputRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, outputRoot, StringComparison.Ordinal))
            throw new ArgumentException($"Unsafe TargetPath '{target}' escapes output root '{outputRoot}'.");
        if (!IsPhysicallyInside(full, outputRoot))
            throw new ArgumentException($"Unsafe TargetPath '{target}' resolves outside output root '{outputRoot}' (link/junction escape).");
        if (sourcePaths.Contains(full))
            throw new ArgumentException($"Unsafe TargetPath '{target}' overlaps a compiled CUI source '{full}'.");
        return full;
    }

    /// <summary>Physical containment check that follows a final symlink/junction component so a link
    /// pointing outside an allocated root is treated as unsafe.</summary>
    private static bool IsPhysicallyInside(string fullPath, string root)
    {
        try
        {
            if (File.Exists(fullPath) && File.ResolveLinkTarget(fullPath, true) is { } fileTarget)
                fullPath = fileTarget.FullName;
            else if (Directory.Exists(fullPath) && Directory.ResolveLinkTarget(fullPath, true) is { } dirTarget)
                fullPath = dirTarget.FullName;
        }
        catch
        {
            // Resolution failure means we cannot prove containment -> treat as unsafe.
            return false;
        }
        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || string.Equals(fullPath, root, StringComparison.Ordinal);
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
        public CuiCompiledDocument Output { get; set; }
        public string Sha256 { get; }
        public bool Stateful { get; set; }
        public bool PromotedMetadata { get; set; }
    }
    private sealed record OwnedRaw(string Original, string Destination, string Sha256);
    private sealed class ManifestModel
    {
        public OwnedRawModel[] Owned { get; set; } = [];
    }
    private sealed record OwnedRawModel(string Original, string Destination, string Sha256);
}
