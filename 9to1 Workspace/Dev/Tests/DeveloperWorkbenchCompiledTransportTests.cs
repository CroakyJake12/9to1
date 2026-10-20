using System.Text;
using System.Text.RegularExpressions;
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
/// semantics/action round-trip, then verify host-support admission fails closed on malformed,
/// missing, unknown-version, language, ABI (contradictory and consistently unsupported) and
/// capability rejections before any scene effect.
/// </summary>
public sealed class DeveloperWorkbenchCompiledTransportTests
{
    private const string WorkbenchSource = "UI/DeveloperWorkbench.cui";

    private static byte[] Embedded() => DeveloperWorkbenchCuiDocument.ReadEmbeddedArtifact();

    private static CuiV1LoadedArtifact Decode(byte[] bytes) =>
        CuiV1ArtifactReader.ReadArtifact(bytes);

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
        string[] expected = ["dev.refresh", "dev.tree", "dev.git-status", "dev.git-diff", "dev.git-branches", "dev.open-source",
            "dev.search", "dev.preview-edit", "dev.apply-edit", "dev.revert-pass", "dev.run-command", "dev.run-tests",
            "dev.run-stage", "dev.create-branch", "dev.switch-branch"];
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
        string[] expected = ["dev.refresh", "dev.tree", "dev.git-status", "dev.git-diff", "dev.git-branches", "dev.open-source",
            "dev.search", "dev.preview-edit", "dev.apply-edit", "dev.revert-pass", "dev.run-command", "dev.run-tests",
            "dev.run-stage", "dev.create-branch", "dev.switch-branch"];
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
        var truncated = Embedded()[..(Embedded().Length / 2)];
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(truncated));
        Assert.Equal("CUIR070", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unknown_artifact_format_before_effects()
    {
        var bytes = Mutate(Embedded(), "\"format\"", "cui-build-compilation", "\"bogus\"");
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR060", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_format_version_before_effects()
    {
        var bytes = Mutate(Embedded(), "\"formatVersion\"", "1", "999");
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR061", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_missing_workbench_document_before_effects()
    {
        var bytes = Mutate(Embedded(), "\"source\"", WorkbenchSource, "\"UI/Missing.cui\"");
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR062", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unsupported_language_version_before_effects()
    {
        // Envelope metadata precedes the document metadata, so the first LanguageVersion is envelope.
        var bytes = Mutate(Embedded(), "\"LanguageVersion\"", "\"1\"", "\"99\"", occurrence: 1);
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR064", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_contradictory_document_abi_before_effects()
    {
        // Envelope stays ABI1 (valid) while the document declares ABI999: a contradiction that must
        // still fail whole-bundle admission with CUIR065 before any effect.
        var bytes = Mutate(Embedded(), "\"RuntimeAbiVersion\"", "\"1\"", "\"999\"", occurrence: 2);
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_consistently_unsupported_abi_before_effects()
    {
        // Envelope AND document both ABI999: consistently unsupported, rejected at the envelope with
        // CUIR065 (deterministic diagnostic precedence).
        var bytes = ReplaceAll(Embedded(), "\"RuntimeAbiVersion\"", "\"1\"", "\"999\"");
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR065", error.Diagnostic.Code);
    }

    [Fact]
    public void Admission_rejects_unknown_required_capability_before_effects()
    {
        // Inject an unsupported capability into the envelope's required-capability set (first
        // occurrence is the envelope), which must fail whole-bundle admission with CUIR067.
        var bytes = Mutate(Embedded(), "\"RequiredCapabilities\"", "[]", "[\"future-capability\"]", occurrence: 1);
        var error = Assert.Throws<CuiRuntimeLoadException>(() => DeveloperWorkbenchCuiDocument.Load(bytes));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
    }

    private static IEnumerable<CuiComponent> Descendants(IEnumerable<CuiComponent> nodes)
    {
        foreach (var node in nodes)
        { yield return node; foreach (var child in Descendants(node.Children)) yield return child; }
    }

    // Matches `"key" : value` (any insignificant whitespace around the colon).
    private static readonly Regex KeyValuePattern = new(
        "(?<key>\"[^\"]+\")\\s*:\\s*(?<value>\\[[^\\]]*\\]|\"[^\"]*\"|-?\\d+)",
        RegexOptions.Compiled);

    private static byte[] Mutate(byte[] source, string key, string oldValue, string newValue, int occurrence = 1)
    {
        var text = Encoding.UTF8.GetString(source);
        var pattern = new Regex(Regex.Escape(key) + @"\s*:\s*(?<value>" + Regex.Escape(oldValue) + ")");
        if (occurrence < 0) throw new ArgumentOutOfRangeException(nameof(occurrence));

        var matches = pattern.Matches(text);
        Assert.True(matches.Count >= occurrence, $"Expected '{key}: {oldValue}' (occurrence {occurrence}) in the embedded artifact.");
        var target = matches[occurrence - 1];
        var mutated = text[..target.Groups["value"].Index] + newValue + text[(target.Groups["value"].Index + target.Groups["value"].Length)..];
        return Encoding.UTF8.GetBytes(mutated);
    }

    private static byte[] ReplaceAll(byte[] source, string key, string oldValue, string newValue)
    {
        var text = Encoding.UTF8.GetString(source);
        var pattern = new Regex(Regex.Escape(key) + @"\s*:\s*(?<value>" + Regex.Escape(oldValue) + ")");
        var mutated = pattern.Replace(text, m => m.Groups["key"].Value + ": " + newValue);
        Assert.NotEqual(text, mutated);
        return Encoding.UTF8.GetBytes(mutated);
    }
}
