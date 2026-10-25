using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>
/// The Build development workbench MUST consume the genuine V1 compiled CUI artifact through the
/// typed Core/Runtime transport — never a silent raw-source reparse fallback and never a loose
/// source/snapshot model. These tests exercise the ACTUAL embedded artifact and the typed
/// semantics/action round-trip, then verify structural and host-support admission fails closed on
/// malformed, missing, unknown-version, language, registry, ABI (contradictory and consistently
/// unsupported), duplicate-identity, missing-required-collection and capability rejections before
/// any scene effect.
/// </summary>
/// <remarks>
/// Every mutation is a structural edit of the ACTUAL embedded bytes decoded through
/// <see cref="System.Text.Json.Nodes.JsonNode"/> (round-tripped through a real JSON writer), so the
/// mutated payload is always valid JSON and exactly the intended field changed. Unsupported-value
/// cases (language/registry/ABI/capability) therefore reach the admission boundary as well-formed
/// artifacts rather than being masked by malformed JSON.
/// </remarks>
public sealed class DeveloperWorkbenchCompiledTransportTests
{
    private const string WorkbenchSource = "UI/DeveloperWorkbench.cui";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static byte[] Embedded() => DeveloperWorkbenchCuiDocument.ReadEmbeddedArtifact();

    private static CuiV1LoadedArtifact Decode(byte[] bytes) =>
        CuiV1ArtifactReader.ReadArtifact(bytes);

    private static JsonObject Root() => JsonNode.Parse(Embedded())!.AsObject();

    private static byte[] Rebuild(JsonObject root) =>
        Encoding.UTF8.GetBytes(root.ToJsonString(Indented));

    private static JsonObject DocumentAt(JsonObject root, int index) => (JsonObject)root["documents"]![index]!;

    private static JsonObject CompilationAt(JsonObject root, int index) => (JsonObject)DocumentAt(root, index)["compilation"]!;

    private static JsonObject MetadataAt(JsonObject root, int index) => (JsonObject)CompilationAt(root, index)["Metadata"]!;

    private static JsonObject EnvelopeMetadata(JsonObject root) => (JsonObject)root["metadata"]!;

    private static int WorkbenchIndex(JsonObject root)
    {
        var docs = (JsonArray)root["documents"]!;
        for (int i = 0; i < docs.Count; i++)
            if (DocumentAt(root, i)["source"]?.GetValue<string>() == WorkbenchSource) return i;
        throw new InvalidOperationException("Workbench document not present in embedded artifact.");
    }

    private static void AssertWorkbench(byte[] bytes)
    {
        var artifact = Decode(bytes);
        Assert.Equal(CuiV1ArtifactReader.FormatName, artifact.Format);
        Assert.Equal(CuiV1ArtifactReader.FormatVersion, artifact.FormatVersion);

        // The workbench is genuinely stateless: minimum ABI1, no local-state variables/actions.
        Assert.Equal("1", artifact.Metadata.RuntimeAbiVersion);
        Assert.Equal(CuiRuntimeCompatibility.LanguageVersion, artifact.Metadata.LanguageVersion);
        Assert.Equal(CuiControlRegistry.ComponentRegistryVersion, artifact.Metadata.ComponentRegistryVersion);

        var document = Assert.Single(artifact.Documents, d => d.Source == WorkbenchSource);
        Assert.False(string.IsNullOrWhiteSpace(document.Sha256));
        Assert.Equal("1", document.Metadata.RuntimeAbiVersion);
        Assert.NotNull(document.Semantics);
        Assert.NotNull(document.Semantics.Components);
        Assert.NotEmpty(document.Semantics.Components);
    }

    [Fact]
    public void Embedded_genuine_v1_bundle_decodes_to_typed_semantics_with_all_authored_actions()
    {
        var bytes = Embedded();
        AssertWorkbench(bytes);

        var artifact = Decode(bytes);
        var document = Assert.Single(artifact.Documents, d => d.Source == WorkbenchSource);

        // True typed semantic/action round-trip from the embedded artifact, never a source string.
        var commands = Descendants(document.Semantics.Components)
            .SelectMany(value => value.Actions.Values)
            .Select(value => value.Name)
            .ToArray();
        string[] expected = [
            "dev.refresh", "dev.tree", "dev.git-status", "dev.git-diff",
            "dev.git-branches", "dev.project-folder-up", "dev.open-project-entry", "dev.project-entries-previous",
            "dev.project-entries-next", "dev.open-source", "dev.search", "dev.open-search-result",
            "dev.select-source-tab", "dev.close-source-tab", "dev.toggle-source-tab-pin", "dev.inspect-recovered-draft",
            "dev.use-merged-recovered-draft", "dev.reconcile-recovered-draft", "dev.go-to-source-location", "dev.find-previous-source",
            "dev.find-next-source", "dev.replace-source-match", "dev.replace-all-source", "dev.undo-source-edit",
            "dev.redo-source-edit", "dev.analyze-cui-draft", "dev.go-to-cui-problem", "dev.go-to-cui-element", "dev.preview-edit",
            "dev.apply-edit", "dev.save-reviewed", "dev.revert-pass", "dev.run-command",
            "dev.run-tests", "dev.select-build-stage", "dev.run-stage", "dev.create-branch",
            "dev.switch-branch"];
        Assert.Equal(expected.OrderBy(value => value), commands.OrderBy(value => value));

        // Genuinely stateless: no document-root local variables.
        Assert.Empty(document.Semantics.Variables);
    }

    [Fact]
    public void Load_returns_typed_document_that_preserves_every_existing_action()
    {
        var document = DeveloperWorkbenchCuiDocument.Load();
        Assert.NotNull(document);
        var commands = Descendants(document.Components)
            .SelectMany(value => value.Actions.Values)
            .Select(value => value.Name)
            .ToArray();
        string[] expected = [
            "dev.refresh", "dev.tree", "dev.git-status", "dev.git-diff",
            "dev.git-branches", "dev.project-folder-up", "dev.open-project-entry", "dev.project-entries-previous",
            "dev.project-entries-next", "dev.open-source", "dev.search", "dev.open-search-result",
            "dev.select-source-tab", "dev.close-source-tab", "dev.toggle-source-tab-pin", "dev.inspect-recovered-draft",
            "dev.use-merged-recovered-draft", "dev.reconcile-recovered-draft", "dev.go-to-source-location", "dev.find-previous-source",
            "dev.find-next-source", "dev.replace-source-match", "dev.replace-all-source", "dev.undo-source-edit",
            "dev.redo-source-edit", "dev.analyze-cui-draft", "dev.go-to-cui-problem", "dev.go-to-cui-element", "dev.preview-edit",
            "dev.apply-edit", "dev.save-reviewed", "dev.revert-pass", "dev.run-command",
            "dev.run-tests", "dev.select-build-stage", "dev.run-stage", "dev.create-branch",
            "dev.switch-branch"];
        Assert.Equal(expected.OrderBy(value => value), commands.OrderBy(value => value));
    }

    [Fact]
    public void Minimum_abi1_stateless_bundle_is_admitted_on_stateful_aware_runtime2()
    {
        // Truthful current/minimum ABI split: the runtime advertises stateful ABI2 while minimum
        // stateless ABI1 stays within the supported range.
        Assert.Equal(1, CuiRuntimeCompatibility.MinStatelessAbi);
        Assert.Equal(2, CuiRuntimeCompatibility.CurrentAbi);
        Assert.Equal("2", CuiRuntimeCompatibility.RuntimeAbiVersion);

        // Genuine V1 (ABI1) must pass real whole-bundle host support without throwing.
        var artifact = Decode(Embedded());
        var span = CuiSourceSpan.At("test", 1, 1);
        CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, span);

        // And the full compiled consumer path must succeed.
        Assert.NotNull(DeveloperWorkbenchCuiDocument.Load(Embedded()));
    }

    [Fact]
    public void Admission_rejects_malformed_artifact_before_effects()
    {
        // Truncated bytes => malformed JSON syntax, caught by the reader as CUIR070.
        var truncated = Embedded()[..(Embedded().Length / 2)];
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(truncated));
        Assert.Equal("CUIR070", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unknown_artifact_format_before_effects()
    {
        var root = Root();
        Assert.Equal("cui-build-compilation", root["format"]!.GetValue<string>());
        root["format"] = "bogus";
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_format_version_before_effects()
    {
        var root = Root();
        Assert.Equal(1, root["formatVersion"]!.GetValue<int>());
        root["formatVersion"] = 999;
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_missing_workbench_document_before_effects()
    {
        var root = Root();
        var document = DocumentAt(root, WorkbenchIndex(root));
        Assert.Equal(WorkbenchSource, document["source"]!.GetValue<string>());
        // Rename the workbench document so the bundle loads structurally but no longer contains the
        // selected source; the consumer must fail with CUIR062 (missing selected document).
        document["source"] = "UI/NotTheWorkbench.cui";
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR062", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_missing_per_document_metadata_before_effects()
    {
        var root = Root();
        var compilation = CompilationAt(root, WorkbenchIndex(root));
        Assert.NotNull(compilation["Metadata"]);
        compilation.Remove("Metadata");
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR072", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_missing_source_identity_before_effects()
    {
        var root = Root();
        var document = DocumentAt(root, WorkbenchIndex(root));
        Assert.Equal(WorkbenchSource, document["source"]!.GetValue<string>());
        document.Remove("source");
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR072", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_missing_required_compilation_collection_before_effects()
    {
        var root = Root();
        var compilation = CompilationAt(root, WorkbenchIndex(root));
        Assert.NotNull(compilation["Components"]);
        compilation.Remove("Components");
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR072", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_wrong_required_shape_before_effects()
    {
        var root = Root();
        var compilation = CompilationAt(root, WorkbenchIndex(root));
        Assert.NotNull(compilation["Semantics"]);
        compilation["Semantics"] = "not-an-object";
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR072", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_duplicate_source_identity_before_effects()
    {
        var root = Root();
        var docs = (JsonArray)root["documents"]!;
        int index = WorkbenchIndex(root);
        var clone = ((JsonObject)docs[index]!).DeepClone();
        docs.Add(clone);
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR072", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_language_version_before_effects()
    {
        var root = Root();
        var envelope = EnvelopeMetadata(root);
        Assert.Equal("1", envelope["LanguageVersion"]!.GetValue<string>());
        envelope["LanguageVersion"] = "99";
        for (int i = 0; i < ((JsonArray)root["documents"]!).Count; i++)
            MetadataAt(root, i)["LanguageVersion"] = "99";
        var bytes = Rebuild(root);
        // Reach host support with an internally coherent bundle, not CUIR072 reconciliation.
        var artifact = Decode(bytes);
        Assert.Equal("99", artifact.Metadata.LanguageVersion);
        Assert.All(artifact.Documents, document => Assert.Equal("99", document.Metadata.LanguageVersion));
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_registry_version_before_effects()
    {
        var root = Root();
        var envelope = EnvelopeMetadata(root);
        Assert.Equal(CuiControlRegistry.ComponentRegistryVersion, envelope["ComponentRegistryVersion"]!.GetValue<string>());
        envelope["ComponentRegistryVersion"] = "9.9";
        for (int i = 0; i < ((JsonArray)root["documents"]!).Count; i++)
            MetadataAt(root, i)["ComponentRegistryVersion"] = "9.9";
        var bytes = Rebuild(root);
        var artifact = Decode(bytes);
        Assert.Equal("9.9", artifact.Metadata.ComponentRegistryVersion);
        Assert.All(artifact.Documents, document => Assert.Equal("9.9", document.Metadata.ComponentRegistryVersion));
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR066", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_abi_on_required_sibling_before_effects()
    {
        // The selected workbench remains ABI1. Its required sibling needs ABI999, reflected in
        // the envelope aggregate, so the structurally valid bundle reaches host rejection CUIR065.
        var root = Root();
        var docs = (JsonArray)root["documents"]!;
        int index = WorkbenchIndex(root);
        Assert.Equal("1", MetadataAt(root, index)["RuntimeAbiVersion"]!.GetValue<string>());
        var sibling = ((JsonObject)docs[index]!).DeepClone();
        sibling["source"] = "UI/Sibling.cui";
        ((JsonObject)sibling["compilation"]!)["Metadata"]!.AsObject()["RuntimeAbiVersion"] = "999";
        docs.Add(sibling);
        EnvelopeMetadata(root)["RuntimeAbiVersion"] = "999";
        var bytes = Rebuild(root);
        var artifact = Decode(bytes);
        Assert.Equal("999", artifact.Metadata.RuntimeAbiVersion);
        Assert.Equal("1", Assert.Single(artifact.Documents, document => document.Source == WorkbenchSource).Metadata.RuntimeAbiVersion);
        Assert.Equal("999", Assert.Single(artifact.Documents, document => document.Source == "UI/Sibling.cui").Metadata.RuntimeAbiVersion);
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Structural_admission_rejects_document_abi_above_envelope_before_effects()
    {
        // Retain the original contradictory fixture as structural coverage, independently of
        // unsupported-host coverage: document ABI999 cannot exceed its ABI1 envelope aggregate.
        var root = Root();
        Assert.Equal("1", EnvelopeMetadata(root)["RuntimeAbiVersion"]!.GetValue<string>());
        var documentMetadata = MetadataAt(root, WorkbenchIndex(root));
        Assert.Equal("1", documentMetadata["RuntimeAbiVersion"]!.GetValue<string>());
        documentMetadata["RuntimeAbiVersion"] = "999";
        var bytes = Rebuild(root);
        var structuralError = Assert.Throws<CuiV1AdmissionException>(() => Decode(bytes));
        Assert.Equal("CUIR072", structuralError.Code);
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR072", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_consistently_unsupported_abi_before_effects()
    {
        // Envelope AND document both ABI999: consistently unsupported, rejected at the envelope with
        // CUIR065 (deterministic diagnostic precedence).
        var root = Root();
        EnvelopeMetadata(root)["RuntimeAbiVersion"] = "999";
        MetadataAt(root, WorkbenchIndex(root))["RuntimeAbiVersion"] = "999";
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unknown_required_capability_before_effects()
    {
        // Inject an unsupported capability into the envelope's required-capability set.
        var root = Root();
        var envelope = EnvelopeMetadata(root);
        Assert.Empty((JsonArray)envelope["RequiredCapabilities"]!);
        envelope["RequiredCapabilities"] = new JsonArray("future-capability");
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(Rebuild(root)));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_capability_on_required_sibling_before_effects()
    {
        // A required sibling (not the selected workbench document) demands a capability this host
        // does not support; whole-bundle admission must reject the bundle with CUIR067 even though
        // the selected document alone is clean. Envelope stays consistent (union of capabilities).
        var root = Root();
        var docs = (JsonArray)root["documents"]!;
        int index = WorkbenchIndex(root);
        var sibling = ((JsonObject)docs[index]!).DeepClone();
        sibling["source"] = "UI/Sibling.cui";
        ((JsonObject)sibling["compilation"]!)["Metadata"]!.AsObject()["RequiredCapabilities"] = new JsonArray("future-capability");
        docs.Add(sibling);
        var envelope = EnvelopeMetadata(root);
        Assert.Empty((JsonArray)envelope["RequiredCapabilities"]!);
        envelope["RequiredCapabilities"] = new JsonArray("future-capability");
        var bytes = Rebuild(root);
        var artifact = Decode(bytes);
        Assert.Contains("future-capability", artifact.Metadata.RequiredCapabilities);
        Assert.DoesNotContain("future-capability", Assert.Single(artifact.Documents, document => document.Source == WorkbenchSource).Metadata.RequiredCapabilities);
        Assert.Contains("future-capability", Assert.Single(artifact.Documents, document => document.Source == "UI/Sibling.cui").Metadata.RequiredCapabilities);
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> nodes)
    {
        foreach (var node in nodes)
        { yield return node; foreach (var child in Descendants(node.Children)) yield return child; }
    }
}
