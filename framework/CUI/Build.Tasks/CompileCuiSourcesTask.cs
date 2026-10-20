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
    /// <summary>Effective SDK DefaultCopyToPublishDirectoryMetadata switch. When true (the SDK
    /// default), an item missing CopyToPublishDirectory inherits publish from a real output-copy mode;
    /// when false, no default publish is inferred and only explicit valid modes publish.</summary>
    public bool DefaultCopyToPublishDirectoryMetadata { get; set; } = true;
    public string[] RequiredCapabilities { get; set; } = [];
    public ITaskItem[] ResourcesToSnapshot { get; set; } = [];
    [Output] public ITaskItem[] CompiledArtifacts { get; private set; } = [];
    [Output] public ITaskItem[] SnapshotResources { get; private set; } = [];
    [Output] public ITaskItem[] SourceSnapshots { get; private set; } = [];
    [Output] public ITaskItem[] StatefulPackagedSources { get; private set; } = [];
    [Output] public ITaskItem[] StaleOwnedOutputs { get; private set; } = [];
    [Output] public ITaskItem[] PreservedForeignOutputs { get; private set; } = [];

    public override bool Execute()
    {
        CompiledArtifacts = [];
        SnapshotResources = [];
        SourceSnapshots = [];
        StatefulPackagedSources = [];
        StaleOwnedOutputs = [];
        PreservedForeignOutputs = [];
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
            // never-copied item types create no ownership entry. Publish is modelled separately:
            // explicit CopyToPublishDirectory values are eligible only for the SDK's real gather modes
            // (Always/PreserveNewest/IfDifferent), and a missing value default-inherits from a real
            // output-copy mode only when the SDK's effective DefaultCopyToPublishDirectoryMetadata switch
            // is on. Each entry records the exact artifact bytes expected at the destination, so stale
            // cleanup refuses to delete a path whose current contents are no longer that owned artifact
            // (preserving unrelated same-path files).
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
            var previous = ReadManifestStrict(manifestPath);
            var previousOwned = previous.Owned;
            var previousPreserved = previous.PreservedForeign;

            // Write provenance snapshots (benign intermediates) so ownership hashes are real bytes.
            foreach (var input in inputs) Publish(snapshots[input.Path], input.Bytes);

            var currentOwned = new List<OwnedRaw>();
            foreach (var item in mappedResources)
                SnapshotOwned(currentOwned, outputRoot, publishRoot, root, item, sourcePaths);

            var stale = ComputeStaleOwnedOutputs(previousOwned, currentOwned, root, outputRoot, publishRoot, protectedPaths);
            var stillPreserved = ComputeStillPreservedForeign(previousPreserved, currentOwned, root, outputRoot, publishRoot, protectedPaths);

            // All path/ownership validation succeeded. Now stage and commit the compiled artifact
            // outputs (a validation failure above would have returned without touching them).
            foreach (var (destination, bytes) in pendingPublishes) Publish(destination, bytes);

            // Delete only stale build-owned outputs whose current bytes still equal the owned artifact,
            // and never a source or a preserved replacement. Only actually-deleted destinations are
            // reported as cleaned build outputs (FileWrites).
            var deletedOwned = new List<ITaskItem>();
            var preservedForeignDestinations = new HashSet<string>(StringComparer.Ordinal);
            // Previously-preserved foreign outputs are kept out of the SDK's clean bookkeeping for as
            // long as they still exist within an allocated root and have not been legitimately
            // reclaimed as owned outputs.
            foreach (var path in stillPreserved) preservedForeignDestinations.Add(path);
            foreach (var owned in stale)
            {
                if (!File.Exists(owned.Destination)) continue;
                var full = Path.GetFullPath(owned.Destination);
                if (protectedPaths.Contains(full) || string.Equals(full, root, StringComparison.Ordinal)) continue;
                if (FileHashMatches(owned))
                {
                    File.Delete(owned.Destination);
                    Log.LogMessage(MessageImportance.Low, "Removed stale build-owned raw CUI output: {0}", owned.Destination);
                    deletedOwned.Add(new TaskItem(owned.Destination));
                }
                else
                {
                    // A previously-owned destination now holds bytes that are no longer the owned
                    // artifact (user/foreign data). Never delete it. Report it so the build's
                    // targets can scrub this exact identity from the SDK's persisted clean
                    // bookkeeping, keeping the foreign file alive across incremental builds and
                    // explicit clean/rebuild. This is not an ownership claim; the path is kept out
                    // of the owned list and out of FileWrites, so it is never claimed as produced.
                    Log.LogMessage(MessageImportance.Low, "Preserved stale build-owned raw CUI output with foreign bytes: {0}", owned.Destination);
                    preservedForeignDestinations.Add(full);
                }
            }
            var preservedForeign = preservedForeignDestinations
                .Order(StringComparer.Ordinal)
                .Select(path => (ITaskItem)new TaskItem(path))
                .ToList();
            StaleOwnedOutputs = deletedOwned.ToArray();
            PreservedForeignOutputs = preservedForeign.ToArray();
            WriteManifest(manifestPath, currentOwned, preservedForeignDestinations);

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
            if (string.Equals(full, projectRoot, PathComparison)) continue;
            // Never treat a source path, a preserved replacement, or an arbitrary/outside-root path
            // as a stale owned output.
            if (protectedPaths.Contains(full)) continue;
            var underAnyRoot = validRoots.Any(root => IsPhysicallyInside(full, root));
            if (underAnyRoot) stale.Add(owned);
        }
        return stale;
    }

    /// <summary>Returns previously-preserved foreign destinations that must remain excluded from the
    /// SDK's persisted clean bookkeeping on this build: they still exist physically, remain inside an
    /// allocated root, are not a source/protected path, and have not been legitimately reclaimed as
    /// owned outputs. These identities are scrubbed from the SDK's clean lists — they are not
    /// ownership claims and never enter the owned list or FileWrites.</summary>
    private static List<string> ComputeStillPreservedForeign(
        IReadOnlyList<string> previousPreserved, List<OwnedRaw> currentOwned, string projectRoot,
        string outputRoot, string? publishRoot, ISet<string> protectedPaths)
    {
        if (previousPreserved.Count == 0) return new List<string>();
        var validRoots = new List<string>();
        if (!string.IsNullOrEmpty(outputRoot)) validRoots.Add(outputRoot);
        if (!string.IsNullOrEmpty(publishRoot)) validRoots.Add(publishRoot!);
        var currentDestinations = currentOwned.Select(owned => owned.Destination).ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var path in previousPreserved)
        {
            if (!File.Exists(path)) continue;
            string full;
            try { full = Path.GetFullPath(path); }
            catch { continue; }
            if (string.Equals(full, projectRoot, PathComparison)) continue;
            if (protectedPaths.Contains(full)) continue;
            if (currentDestinations.Contains(full)) continue;
            var underAnyRoot = validRoots.Any(root => IsPhysicallyInside(full, root));
            if (underAnyRoot) result.Add(full);
        }
        return result;
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

    private static readonly HashSet<string> ValidPublishModes = new(StringComparer.OrdinalIgnoreCase)
        { "Always", "PreserveNewest", "IfDifferent" };

    /// <summary>Publish eligibility mirrors the ACTUAL SDK gather rules (Microsoft.NET.Publish.targets
    /// 927–947). An explicit CopyToPublishDirectory is eligible only when it is a real gather mode
    /// (Always/PreserveNewest/IfDifferent); Never or any other value (e.g. a typo) produces no publish
    /// custody entry. When CopyToPublishDirectory is absent, the SDK's DefaultCopyToPublishDirectoryMetadata
    /// target defaults it from a real CopyToOutputDirectory copy mode, but only when the effective
    /// defaulting switch is true — we gate on that same switch so disabled defaulting never invents a
    /// publish destination.</summary>
    private bool IsPublishEligible(ITaskItem item)
    {
        // Only the item types the SDK actually gathers at publish (Content/None/EmbeddedResource) can
        // ever acquire publish custody; AvaloniaResource/CuiMarkup (and any other type) never do.
        var type = item.GetMetadata("CuiItemType");
        if (type is not ("Content" or "None" or "EmbeddedResource")) return false;
        var copyToPublish = item.GetMetadata("CopyToPublishDirectory");
        if (!string.IsNullOrWhiteSpace(copyToPublish))
            return ValidPublishModes.Contains(copyToPublish);
        // Missing CopyToPublishDirectory: default-inherit only when the SDK's effective defaulting flag
        // is on AND the item has a real output-copy mode (Content/None/EmbeddedResource).
        return DefaultCopyToPublishDirectoryMetadata && IsOutputCopyEligible(item);
    }

    private void SnapshotOwned(
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
    /// a successful package. Returns both the previously-owned outputs and any previously-preserved
    /// foreign destinations (shielded from the platform's incremental clean).</summary>
    private static ManifestSnapshot ReadManifestStrict(string path)
    {
        if (!File.Exists(path)) return new ManifestSnapshot(new List<OwnedRaw>(), new List<string>());
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
        var owned = model.Owned
            .Select(entry => new OwnedRaw(entry.Original ?? "", entry.Destination ?? "", entry.Sha256 ?? ""))
            .ToList();
        var preserved = model.PreservedForeign?.Where(path => !string.IsNullOrEmpty(path)).Select(path => path!).ToList()
            ?? new List<string>();
        return new ManifestSnapshot(owned, preserved);
    }

    private static void WriteManifest(string path, List<OwnedRaw> owned, IEnumerable<string> preservedForeign)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var model = new ManifestModel
        {
            Owned = owned.Select(entry => new OwnedRawModel(entry.Original, entry.Destination, entry.Sha256)).ToArray(),
            PreservedForeign = preservedForeign.ToArray(),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Validates that allocated output/publish roots are distinct from the project root so
    /// generated products cannot land beside/lap sources and risk accidental deletion.</summary>
    private static void ValidateOwnedRoots(string outputRoot, string? publishRoot, string projectRoot)
    {
        if (string.Equals(outputRoot, projectRoot, PathComparison))
            throw new ArgumentException("The CUI output directory overlaps the project root; refusing unsafe packaging.");
        if (publishRoot is not null && string.Equals(publishRoot, projectRoot, PathComparison))
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
        var rootNorm = NormalizeAbsolutePath(outputRoot);
        var full = Path.GetFullPath(Path.Combine(rootNorm, target));
        if (!full.StartsWith(rootNorm + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(full, rootNorm, StringComparison.Ordinal))
            throw new ArgumentException($"Unsafe TargetPath '{target}' escapes output root '{outputRoot}'.");
        if (!IsPhysicallyInside(full, outputRoot))
            throw new ArgumentException($"Unsafe TargetPath '{target}' resolves outside output root '{outputRoot}' (link/junction escape).");
        // Lexical source overlap, plus physical source aliases (a destination that resolves to the
        // same canonical file as a source through a link/junction must also be rejected).
        if (sourcePaths.Contains(full))
            throw new ArgumentException($"Unsafe TargetPath '{target}' overlaps a compiled CUI source '{full}'.");
        var physicalFull = ResolvePhysicalPath(full);
        foreach (var source in sourcePaths)
        {
            if (string.Equals(ResolvePhysicalPath(source), physicalFull, PathComparison))
                throw new ArgumentException($"Unsafe TargetPath '{target}' aliases a compiled CUI source '{source}' (physical identity).");
        }
        return full;
    }

    /// <summary>Physical containment check: resolves EVERY existing ancestor of the path (not just a
    /// final link component) to canonical physical identity, then compares it against the canonical
    /// physical identity of the root. This catches escapes through an ancestor symlink even when the
    /// final path is absent or a plain file (for example bin/link/new.cui where bin/link resolves
    /// outside the root).</summary>
    private static bool IsPhysicallyInside(string fullPath, string root)
    {
        try
        {
            var physical = ResolvePhysicalPath(fullPath);
            var physicalRoot = ResolvePhysicalPath(root);
            return physical.StartsWith(physicalRoot + Path.DirectorySeparatorChar, PathComparison)
                || string.Equals(physical, physicalRoot, PathComparison);
        }
        catch
        {
            // Resolution failure means we cannot prove containment -> treat as unsafe.
            return false;
        }
    }

    /// <summary>Canonical physical path comparison follows platform casing semantics so that a source
    /// alias, an ancestor link and a root identity are recognized regardless of case for the host
    /// filesystem (case-insensitive on Windows, case-sensitive elsewhere).</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Returns the canonical physical identity of a path by following every EXISTING ancestor
    /// component's link target (file or directory) and appending any trailing nonexistent components
    /// safely. Missing components are not resolved; their lexical value is preserved under the
    /// already-resolved parent, which matches actual platform path semantics.</summary>
    private static string ResolvePhysicalPath(string path)
    {
        var full = NormalizeAbsolutePath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) root = Path.DirectorySeparatorChar.ToString();
        var segments = new List<string>();
        var remaining = full;
        while (remaining.Length > root.Length)
        {
            var name = Path.GetFileName(remaining);
            if (string.IsNullOrEmpty(name)) break;
            segments.Insert(0, name);
            var parent = Path.GetDirectoryName(remaining);
            if (parent is null) break;
            remaining = parent;
        }
        var resolved = root;
        foreach (var segment in segments)
        {
            resolved = Path.Combine(resolved, segment);
            resolved = ResolveExistingLink(resolved);
        }
        return resolved;
    }

    /// <summary>Follows one existing path component's link/junction target. A component that does not
    /// exist is a legitimate trailing suffix and keeps its lexical value. A component that EXISTS but
    /// whose physical identity cannot be resolved must fail closed (propagating a bounded IO/Argument
    /// error) rather than being treated as proven safe by lexical containment.</summary>
    private static string ResolveExistingLink(string path)
    {
        if (File.Exists(path))
            return File.ResolveLinkTarget(path, true)?.FullName ?? path;
        if (Directory.Exists(path))
            return Directory.ResolveLinkTarget(path, true)?.FullName ?? path;
        return path;
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

    /// <summary>Returns the fully-qualified form of a path with any trailing directory separator
    /// removed, preserving the path root (for example "C:\" or "/"). Configured output/publish roots
    /// often end in a separator; stripping it keeps containment prefix comparisons consistent without
    /// ever reducing a bare root to an empty/relative form.</summary>
    private static string NormalizeAbsolutePath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root)) root = Path.DirectorySeparatorChar.ToString();
        while (full.Length > root.Length
               && (full[^1] == Path.DirectorySeparatorChar || full[^1] == Path.AltDirectorySeparatorChar))
            full = full[..^1];
        return full;
    }

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
        public string[] PreservedForeign { get; set; } = [];
    }
    private sealed record OwnedRawModel(string Original, string Destination, string Sha256);
    private sealed record ManifestSnapshot(List<OwnedRaw> Owned, List<string> PreservedForeign);
}
