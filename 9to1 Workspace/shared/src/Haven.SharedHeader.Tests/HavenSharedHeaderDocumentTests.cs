using System.Text.Json;
using System.Text.Json.Nodes;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.SharedHeader;
using Xunit;

namespace Haven.SharedHeader.Tests;

/// <summary>
/// Focused tests for the genuine V1 compiled-artifact consumption path of the Haven shared-header
/// component (the persistent shared-panel shell and notifications documents). They verify the actual
/// packaged resource/emitted document identities, positive typed loading of both authored documents
/// and their genuine authored actions, and strict refusal of missing/malformed/wrong-format/ABI/
/// capability sibling inputs BEFORE any controls/effects.
/// </summary>
public sealed class HavenSharedHeaderDocumentTests
{
    // ---- Actual packaged resource / emitted document identities ----

    [Fact]
    public void Compiled_artifact_is_embedded_under_the_genuine_V1_identity()
    {
        var assembly = typeof(HavenSharedHeaderDocuments).Assembly;
        using var stream = assembly.GetManifestResourceStream(HavenSharedHeaderDocuments.CompiledArtifactResource);
        Assert.NotNull(stream);
        Assert.Equal("CUI.Compilation.v1", HavenSharedHeaderDocuments.CompiledArtifactResource);
    }

    [Fact]
    public void Emitted_V1_artifact_declares_both_shared_panel_document_identities()
    {
        var artifact = ReadEmbedded();
        Assert.Equal(CuiV1ArtifactCodec.FormatName, artifact.Format);
        Assert.Equal(CuiV1ArtifactCodec.FormatVersion, artifact.FormatVersion);
        Assert.Equal("1", artifact.Metadata.RuntimeAbiVersion);
        Assert.Contains(artifact.Documents, document => document.Source == "Documents/HavenNotifications.cui");
        Assert.Contains(artifact.Documents, document => document.Source == "Documents/HavenSharedPanelShell.cui");
    }

    // ---- Positive typed mounting of shell/notifications and genuine authored action ----

    [Fact]
    public void Shell_loads_typed_semantics_with_the_named_panel_hosts()
    {
        var document = HavenSharedHeaderDocuments.LoadShell();
        Assert.False(document.Components.Count == 0);
        var names = document.Components.SelectMany(component => component.DescendantsAndSelf())
            .Select(component => component.Name).ToArray();
        Assert.Contains("shared-scene-host", names);
        Assert.Contains("shared-panel-host", names);
        Assert.Contains("shared-frame-root", names);
        Assert.Contains("shared-panel-frame", names);
        Assert.Contains("shared-panel-close", names);
        Assert.Equal("9to1.Haven.ClosePanel", document.Actions["CloseSharedPanel"].Command);
    }

    [Fact]
    public void Notifications_loads_typed_semantics_with_the_genuine_authored_actions()
    {
        var document = HavenSharedHeaderDocuments.LoadNotifications();
        Assert.False(document.Components.Count == 0);
        Assert.True(document.Actions.ContainsKey("CloseNotifications"));
        Assert.True(document.Actions.ContainsKey("MarkRead"));
        Assert.True(document.Actions.ContainsKey("Dismiss"));
        Assert.Equal("9to1.Haven.ClosePanel", document.Actions["CloseNotifications"].Command);
        Assert.Equal("9to1.Haven.MarkRead", document.Actions["MarkRead"].Command);
        Assert.Equal("9to1.Haven.Dismiss", document.Actions["Dismiss"].Command);
        Assert.NotNull(document.Actions["MarkRead"].Parameter);
        Assert.NotNull(document.Actions["Dismiss"].Parameter);
    }

    [Fact]
    public void Shell_compiles_a_persistent_accessible_cancel_action_outside_the_scroll_host()
    {
        var document = HavenSharedHeaderDocuments.LoadShell();
        var components = document.Components.SelectMany(component => component.DescendantsAndSelf()).ToArray();
        var frame = Assert.Single(components, component => component.Name == "shared-panel-frame");
        var host = Assert.Single(components, component => component.Name == "shared-panel-host");
        var close = Assert.Single(components, component => component.Name == "shared-panel-close");
        Assert.Equal("9to1.Haven.ClosePanel", document.Actions["CloseSharedPanel"].Command);
        Assert.Contains(close.Properties, property => property.Key.Equals("CancelAction", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiLiteralValue literal && bool.TryParse(literal.Value, out var enabled) && enabled);
        Assert.Contains(close.Properties, property => property.Key == "AccessibleName" && property.Value is CuiLiteralValue { Value: "Close shared panel" });
        Assert.Contains(host.Properties, property => property.Key.Equals("VerticalScrolling", StringComparison.OrdinalIgnoreCase)
            && property.Value is CuiLiteralValue literal && bool.TryParse(literal.Value, out var enabled) && enabled);
        Assert.DoesNotContain(host.DescendantsAndSelf(), component => ReferenceEquals(component, close));
        Assert.Contains(frame.DescendantsAndSelf(), component => ReferenceEquals(component, host));
        Assert.Contains(frame.DescendantsAndSelf(), component => ReferenceEquals(component, close));
    }

    // ---- Refusal before controls/effects ----

    [Fact]
    public void Missing_source_is_refused_before_effects()
    {
        var error = Assert.Throws<CuiRuntimeLoadException>(
            () => HavenSharedHeaderDocuments.Load("Documents/Nonexistent.cui"));
        Assert.Equal("CUIR062", error.Diagnostic.Code);
    }

    [Fact]
    public void Malformed_artifact_is_refused_before_effects()
    {
        var error = Assert.Throws<CuiRuntimeLoadException>(
            () => HavenSharedHeaderDocuments.Load("Documents/HavenNotifications.cui", new byte[] { 0x00, 0x01, 0xFF }));
        Assert.Equal("CUIR070", error.Diagnostic.Code);
    }

    [Fact]
    public void Wrong_artifact_version_is_refused_before_effects()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes("{\"format\":\"cui-build-compilation\",\"formatVersion\":999}");
        var error = Assert.Throws<CuiRuntimeLoadException>(
            () => HavenSharedHeaderDocuments.Load("Documents/HavenNotifications.cui", bytes));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Fact]
    public void Unsupported_sibling_capability_is_refused_before_effects()
    {
        // The requested shell document is clean, but the whole-bundle envelope declares an
        // unsupported capability; admission must refuse the sibling escape BEFORE any control effect.
        var bytes = MutateEnvelope(node => node["RequiredCapabilities"] = new JsonArray("unsupported-cap"));
        var error = Assert.Throws<CuiRuntimeLoadException>(
            () => HavenSharedHeaderDocuments.Load("Documents/HavenSharedPanelShell.cui", bytes));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    [Fact]
    public void Unsupported_abi_is_refused_before_effects()
    {
        var bytes = MutateEnvelope(node => node["RuntimeAbiVersion"] = "999");
        var error = Assert.Throws<CuiRuntimeLoadException>(
            () => HavenSharedHeaderDocuments.Load("Documents/HavenNotifications.cui", bytes));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    // ---- Helpers ----

    private static CuiV1ArtifactCodec.CuiV1LoadedArtifact ReadEmbedded() =>
        CuiV1ArtifactCodec.ReadArtifact(HavenSharedHeaderDocuments.ReadEmbeddedArtifact());

    private static byte[] MutateEnvelope(Action<JsonObject> mutate)
    {
        var root = JsonNode.Parse(HavenSharedHeaderDocuments.ReadEmbeddedArtifact())!;
        mutate((JsonObject)root["metadata"]!);
        return JsonSerializer.SerializeToUtf8Bytes(root);
    }
}
