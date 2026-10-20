using System.Text;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// The Chat composer must consume the DISTINCT packaged V2 compiled CUI artifact with hard admission
/// checks — never a silent raw-source reparse fallback, and never a V1/raw fallback for this
/// stateful composer. These tests verify the real embedded V2 bundle round-trips through the shared
/// codec into the typed model and that unsupported formats/versions/language/ABI/capabilities and
/// metadata/payload contradictions fail before any scene effects occur.
/// </summary>
public sealed class ChatComposerCompiledTransportTests
{
    private const string ComposerSource = "UI/Chat/ChatComposer.cui";

    private static CuiCompiledArtifact ReadEmbeddedArtifact()
    {
        var artifact = CuiCompiledArtifactCodec.ReadArtifact(ChatComposerCuiDocument.ReadEmbeddedArtifact());
        Assert.Equal(CuiCompiledArtifactCodec.FormatName, artifact.Format);
        Assert.Equal(CuiCompiledArtifactCodec.FormatVersion, artifact.FormatVersion);
        Assert.Equal(CuiCompiledArtifactCodec.ResourceLogicalName, ChatComposerCuiDocument.CompiledArtifactResource);
        return artifact;
    }

    private static CuiCompiledArtifactDocument ComposerDocument(CuiCompiledArtifact artifact) =>
        Assert.Single(artifact.Documents, document => document.Source == ComposerSource);

    [Fact]
    public void Embedded_v2_bundle_round_trips_to_semantics_with_local_variable_and_toggle()
    {
        var artifact = ReadEmbeddedArtifact();
        var semantics = ChatComposerCuiDocument.Load();
        Assert.NotNull(semantics);

        Assert.True(semantics.Variables.TryGetValue("FormattingOpen", out var variable));
        Assert.Equal("bool", variable!.Type);
        Assert.False(bool.Parse(variable.Value));

        var toggle = Assert.Single(semantics.Actions.Values, action => action.Name == "composer.toggle-formatting");
        Assert.Equal("FormattingOpen", toggle.LocalTarget);

        // The stateful composer requires the distinct V2 ABI and announces the local-state capability.
        var doc = ComposerDocument(artifact);
        Assert.Equal(CuiCompiledArtifactCodec.V2RuntimeAbiVersion, doc.Metadata.RuntimeAbiVersion);
        Assert.Contains(doc.Metadata.RequiredCapabilities, capability => capability == "local-state-toggle");
    }

    [Fact]
    public void Embedded_v2_bundle_round_trips_to_choose_once_emoji_variable_and_capability()
    {
        var artifact = ReadEmbeddedArtifact();
        var semantics = ChatComposerCuiDocument.Load();
        Assert.NotNull(semantics);

        Assert.True(semantics.Variables.TryGetValue("EmojiIcon", out var variable));
        Assert.Equal("string", variable!.Type);
        Assert.Equal("RandomOnce", variable!.Selection);
        Assert.Equal("Composer.EmojiIconCandidates", variable!.Source);

        var doc = ComposerDocument(artifact);
        Assert.Contains(doc.Metadata.RequiredCapabilities, capability => capability == "local-state-random-select");
    }

    [Fact]
    public void Admission_rejects_unknown_artifact_format_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { Format = "legacy-format" };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(artifact)));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_format_version_before_effects()
    {
        var artifact = ReadEmbeddedArtifact() with { FormatVersion = 999 };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(artifact)));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_language_version_before_effects()
    {
        // A CONSISTENTLY unsupported language (envelope AND document both "99") passes the codec's
        // internal consistency checks but must fail runtime host-support admission as CUIR064.
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            "99", doc.Metadata.RuntimeAbiVersion, doc.Metadata.ComponentRegistryVersion, doc.Metadata.RequiredCapabilities);
        var badEnv = new CuiCompilationMetadata(
            "99", artifact.Metadata.RuntimeAbiVersion, artifact.Metadata.ComponentRegistryVersion, artifact.Metadata.RequiredCapabilities);
        var replaced = artifact with { Metadata = badEnv, Documents = ReplaceDocument(artifact, doc, doc with { Metadata = badMeta }) };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_envelope_document_language_contradiction_before_effects()
    {
        // A language CONTRADICTION (document "99" vs envelope "1") is an internal envelope/document
        // inconsistency and must be rejected by the codec as CUIR075 (not CUIR064), preserving both
        // causal checks rather than weakening strict consistency.
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            "99", doc.Metadata.RuntimeAbiVersion, doc.Metadata.ComponentRegistryVersion, doc.Metadata.RequiredCapabilities);
        var replaced = artifact with { Documents = ReplaceDocument(artifact, doc, doc with { Metadata = badMeta }) };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR075", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_component_registry_before_effects()
    {
        // A consistently unsupported component registry (envelope AND document) passes internal
        // consistency but fails runtime host-support admission as CUIR066.
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var badMeta = new CuiCompilationMetadata(
            doc.Metadata.LanguageVersion, doc.Metadata.RuntimeAbiVersion, "999.0", doc.Metadata.RequiredCapabilities);
        var badEnv = new CuiCompilationMetadata(
            artifact.Metadata.LanguageVersion, artifact.Metadata.RuntimeAbiVersion, "999.0", artifact.Metadata.RequiredCapabilities);
        var replaced = artifact with { Metadata = badEnv, Documents = ReplaceDocument(artifact, doc, doc with { Metadata = badMeta }) };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR066", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_stateful_document_without_abi2_even_when_capabilities_stripped()
    {
        // Defense in depth: statefulness is read from the typed semantics, not from capability
        // strings. Stripping the capability text AND downgrading the ABI cannot make a stateful
        // composer a harmless V1 document.
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var stripped = doc with { Metadata = BaseMeta(doc.Metadata, abi: "1") };
        var replaced = artifact with { Documents = ReplaceDocument(artifact, doc, stripped) };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR074", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_required_capability_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var caps = doc.Metadata.RequiredCapabilities.Append("future-capability").ToArray();
        var docWithCap = doc with { Metadata = BaseMeta(doc.Metadata, capabilities: caps) };
        var envelopeWithCap = BaseMeta(artifact.Metadata, capabilities: artifact.Metadata.RequiredCapabilities.Append("future-capability").ToArray());
        var replaced = artifact with { Metadata = envelopeWithCap, Documents = ReplaceDocument(artifact, doc, docWithCap) };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_envelope_document_abi_contradiction_before_effects()
    {
        var artifact = ReadEmbeddedArtifact();
        var envelopeWithWrongAbi = BaseMeta(artifact.Metadata, abi: "1");
        var replaced = artifact with { Metadata = envelopeWithWrongAbi };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(replaced)));
        Assert.Equal("CUIR075", error.Diagnostic.Code);
    }

    [Fact]
    public void Whole_bundle_abi999_stateless_sibling_is_rejected_before_effects()
    {
        // A stateless sibling declared with ABI999 (with an envelope-consistent max) passes the codec's
        // internal consistency, and the selected composer's own metadata stays valid. Host-support
        // admission must reject the required sibling's unsupported ABI before any reconstruction/effects.
        // This is whole-bundle admission (the sibling is appended to the bundle), not an import-closure
        // edge; the name reflects exactly what the fixture constructs.
        var artifact = ReadEmbeddedArtifact();
        var sibling = StatelessDocument("sibling.cui", "999");
        var combined = artifact with
        {
            Documents = artifact.Documents.Append(sibling).ToArray(),
            Metadata = EnvelopeFor(artifact.Documents.Append(sibling)),
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(combined)));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Sibling_only_unknown_capability_with_consistent_union_is_rejected()
    {
        // A sibling-only unknown capability reflected in the envelope's consistent union passes the
        // codec, but host-support admission must reject it as unsupported before any effects.
        var artifact = ReadEmbeddedArtifact();
        var sibling = StatelessDocument("sibling.cui", "2", ["future-capability"]);
        var combined = artifact with
        {
            Documents = artifact.Documents.Append(sibling).ToArray(),
            Metadata = EnvelopeFor(artifact.Documents.Append(sibling)),
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() => ChatComposerCuiDocument.Load(Serialize(combined)));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    [Fact]
    public void Genuine_abi1_stateless_sibling_plus_abi2_composer_succeeds()
    {
        // A genuine stateless ABI1 sibling alongside the stateful ABI2 composer must load: ABI1 is
        // within the supported stateless range and no unknown requirement is present.
        var artifact = ReadEmbeddedArtifact();
        var sibling = StatelessDocument("sibling.cui", "1");
        var combined = artifact with
        {
            Documents = artifact.Documents.Append(sibling).ToArray(),
            Metadata = EnvelopeFor(artifact.Documents.Append(sibling)),
        };
        var semantics = ChatComposerCuiDocument.Load(Serialize(combined));
        Assert.NotNull(semantics);
    }

    [Fact]
    public void Missing_compiled_composer_resource_has_no_raw_fallback_and_composer_is_not_in_v1()
    {
        // The stateful composer must never fall back to a raw embedded .cui resource and must never
        // appear in a genuine V1 bundle. A mixed Spaces assembly may still legitimately emit a V1
        // bundle for its stateless sibling documents — so we assert composer absence from V1 rather
        // than global V1 absence.
        var assembly = typeof(ChatComposerCuiDocument).Assembly;
        Assert.Null(assembly.GetManifestResourceStream("HavenOS.Apps.Spaces.UI.Chat.ChatComposer.cui"));
        Assert.NotNull(assembly.GetManifestResourceStream(ChatComposerCuiDocument.CompiledArtifactResource));

        using var v1 = assembly.GetManifestResourceStream(CuiCompiledArtifactCodec.V1ResourceLogicalName);
        if (v1 is not null)
        {
            using var buffer = new MemoryStream();
            v1.CopyTo(buffer);
            var loaded = CuiV1ArtifactCodec.ReadArtifact(buffer.ToArray());
            Assert.DoesNotContain(loaded.Documents, doc => doc.Source == ChatComposerCuiDocument.CompiledSourceName);
        }
    }

    [Fact]
    public void Composer_authored_controls_declare_44px_hit_targets_and_image_glyphs()
    {
        // The authored composer standardises every icon tool and the primary action on one
        // consistent 44x44 hit target (the accessible target), with the actual glyph delivered as
        // a genuine packaged-SVG image child so icon extent and hit target stay distinct.
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var semantics = doc.Semantics;

        string[] ids = ["add-button", "format-button", "emoji-button", "settings-button", "send-button"];
        foreach (var id in ids)
        {
            var button = FindComponent(semantics, id);
            Assert.NotNull(button);
            Assert.Equal("44", Literal(button!, "Width"));
            Assert.Equal("44", Literal(button!, "Height"));
        }

        // The add/glyph keeps its authored 6px padding and a packaged-SVG Image child.
        var add = FindComponent(semantics, "add-button")!;
        Assert.Equal("6", Literal(add, "Padding"));
        Assert.True(HasImageChild(add, "plus.svg"), "add tool must render the packaged plus.svg");

        // The settings control is a genuine SVG (not the intricate Unicode gear) and the primary
        // action is a circular-arrow SVG.
        var settings = FindComponent(semantics, "settings-button")!;
        Assert.True(HasImageChild(settings, "settings.svg"), "settings must render the packaged settings.svg gear");
        var send = FindComponent(semantics, "send-button")!;
        Assert.True(HasImageChild(send, "send.svg"), "primary action must render the packaged send.svg arrow");

        // Secondary icon tools share the quiet chromeless/transparent treatment as the emoji trigger.
        Assert.Equal("Transparent", Literal(FindComponent(semantics, "format-button")!, "background"));
        Assert.Equal("Transparent", Literal(FindComponent(semantics, "settings-button")!, "background"));
    }

    [Fact]
    public void Composer_settings_action_is_thread_settings_not_permission_selector()
    {
        // The authored settings control's real action is composer.settings and its panel is Thread
        // settings (Actions + Responses). It is NOT the canonical permission selector and must not
        // be relabelled Permissions or manufacture a policy/owner permission.
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var semantics = doc.Semantics;

        var settings = FindComponent(semantics, "settings-button")!;
        var settingsAction = settings.Actions["action"]!.Name;
        Assert.Equal("composer.settings", settingsAction);
        Assert.True(settings.Properties.TryGetValue("accessible-name", out var name) && name.LiteralValue == "Thread settings");
        Assert.True(settings.Properties.TryGetValue("tooltip", out var tip) && tip.LiteralValue!.Contains("allowed actions", StringComparison.Ordinal));

        // The thread-settings modes/actions exist and no command in the model is a permission selector.
        Assert.True(semantics.Actions.ContainsKey("composer.set-action-mode"));
        Assert.True(semantics.Actions.ContainsKey("composer.set-response-mode"));
        Assert.DoesNotContain(semantics.Actions.Keys, command => command.Contains("permission", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Composer_primary_send_action_is_authored_with_truthful_accessibility_bindings()
    {
        // The primary action stays the real composer.send, gated by the real CanSend availability and
        // labelled from the REAL current operation (normal Send / edit Update) via read-only
        // presentation bindings, never a fake arrow-only label.
        var artifact = ReadEmbeddedArtifact();
        var doc = ComposerDocument(artifact);
        var semantics = doc.Semantics;

        var send = FindComponent(semantics, "send-button")!;
        Assert.Equal("composer.send", send.Actions["action"]!.Name);
        Assert.True(send.Properties.TryGetValue("Interactive", out var interactive)
            && interactive.Kind == CuiV2ValueKind.Binding
            && interactive.BindingPath == "Composer.CanSend");
        Assert.True(send.Properties.TryGetValue("accessible-name", out var name)
            && name.Kind == CuiV2ValueKind.Binding
            && name.BindingPath == "Composer.SendAccessibleName");
        Assert.True(send.Properties.TryGetValue("tooltip", out var tip)
            && tip.Kind == CuiV2ValueKind.Binding
            && tip.BindingPath == "Composer.SendTooltip");
    }

    private static CuiV2Component? FindComponent(CuiV2Semantics semantics, string id)
    {
        foreach (var component in Flatten(semantics.Components))
        {
            if (string.Equals(component.Name, id, StringComparison.Ordinal)) return component;
        }
        return null;
    }

    private static IEnumerable<CuiV2Component> Flatten(IEnumerable<CuiV2Component> components)
    {
        foreach (var component in components)
        {
            yield return component;
            foreach (var child in Flatten(component.Children)) yield return child;
        }
    }

    private static string? Literal(CuiV2Component component, string property) =>
        component.Properties.TryGetValue(property, out var value) && value.Kind == CuiV2ValueKind.Literal
            ? value.LiteralValue
            : null;

    private static bool HasImageChild(CuiV2Component component, string asset)
    {
        foreach (var child in component.Children)
        {
            if (!string.Equals(child.Type, "Image", StringComparison.OrdinalIgnoreCase)) continue;
            if (child.Properties.TryGetValue("Source", out var source)
                && (source.LiteralValue?.Contains(asset, StringComparison.OrdinalIgnoreCase) ?? false))
                return true;
        }
        return false;
    }

    private static IReadOnlyList<CuiCompiledArtifactDocument> ReplaceDocument(
        CuiCompiledArtifact artifact, CuiCompiledArtifactDocument original, CuiCompiledArtifactDocument replacement) =>
        artifact.Documents.Select(d => ReferenceEquals(d, original) ? replacement : d).ToArray();

    private static CuiCompilationMetadata BaseMeta(
        CuiCompilationMetadata source, string? language = null, string? abi = null, IEnumerable<string>? capabilities = null) =>
        new(language ?? source.LanguageVersion, abi ?? source.RuntimeAbiVersion, source.ComponentRegistryVersion,
            capabilities ?? source.RequiredCapabilities);

    private static byte[] Serialize(CuiCompiledArtifact artifact) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(artifact, CuiCompiledArtifactCodec.Options));

    private static CuiCompiledArtifactDocument StatelessDocument(string source, string abi, IEnumerable<string>? capabilities = null)
    {
        var span = new CuiV2Span("UI/Chat/ChatComposer.cui", 1, 1, 1, 1);
        // The strict codec rejects null optional collections (CUIR072), so ElseChildren and TextParts
        // must be explicit empty arrays of their actual typed element types (never null).
        var component = new CuiV2Component(
            "Page", "root", Array.Empty<string>(),
            new Dictionary<string, CuiV2Value>(StringComparer.Ordinal),
            new Dictionary<string, CuiV2ActionReference>(StringComparer.Ordinal),
            Array.Empty<CuiV2Component>(), null, null, span,
            null, null, Array.Empty<CuiV2Component>(), null, false, null, "", Array.Empty<CuiV2TextPart>());
        var semantics = new CuiV2Semantics(
            source,
            new Dictionary<string, CuiV2Resource>(StringComparer.Ordinal),
            Array.Empty<CuiV2Style>(),
            new Dictionary<string, CuiV2Action>(StringComparer.Ordinal),
            new Dictionary<string, CuiV2Template>(StringComparer.Ordinal),
            new[] { component },
            span,
            new Dictionary<string, CuiV2Value>(StringComparer.Ordinal),
            Array.Empty<CuiV2Import>(),
            new Dictionary<string, CuiV2Variable>(StringComparer.Ordinal));
        var meta = new CuiCompilationMetadata("1", abi, "2.0", capabilities?.ToArray() ?? Array.Empty<string>());
        return new CuiCompiledArtifactDocument(
            source, "0000000000000000000000000000000000000000000000000000000000000000",
            meta, semantics, Array.Empty<CuiV2SourceMapEntry>(), new CuiV2DependencyGraph(Array.Empty<CuiV2Dependency>()));
    }

    private static CuiCompilationMetadata EnvelopeFor(IEnumerable<CuiCompiledArtifactDocument> docs)
    {
        var list = docs.ToArray();
        var caps = new HashSet<string>(StringComparer.Ordinal);
        string? maxAbi = null;
        foreach (var document in list)
        {
            caps.UnionWith(document.Metadata.RequiredCapabilities);
            maxAbi = MaxAbi(maxAbi, document.Metadata.RuntimeAbiVersion);
        }
        var first = list[0];
        return new CuiCompilationMetadata(
            first.Metadata.LanguageVersion, maxAbi ?? first.Metadata.RuntimeAbiVersion,
            first.Metadata.ComponentRegistryVersion, caps);
    }

    private static string? MaxAbi(string? current, string candidate)
    {
        if (current is null) return candidate;
        if (int.TryParse(current, out var a) && int.TryParse(candidate, out var b))
            return b > a ? candidate : current;
        return string.Compare(candidate, current, StringComparison.Ordinal) > 0 ? candidate : current;
    }
}
