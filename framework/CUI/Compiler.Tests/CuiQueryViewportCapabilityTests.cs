using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

/// <summary>
/// Lane 1 capability derivation/admission evidence for the bounded paged QueryViewport scope:
/// a real query-only document derives the <c>query-viewport</c> capability from its typed
/// component/template semantics (independent of local-state Variables/Toggle), carries it through
/// the compiler -> artifact codec, and a stripped capability string is rejected before any effect.
/// The real host-support admission boundary rejects an older host profile that lacks the capability
/// while the current host admits the same artifact unchanged.
/// </summary>
public sealed class CuiQueryViewportCapabilityTests
{
    private static CuiCompilerOptions Options() => new(new CuiCompilationMetadata("1.0", "1", "2.0"));

    private const string QueryOnlyMarkup = """
        <Cui>
          <Actions>
            <Action name="go-prev" command="query.previous" />
            <Action name="go-next" command="query.next" />
          </Actions>
          <Page>
            <Container Type="QueryViewport" Source="{Binding Catalogue}">
              <Input id="q" Type="Text" Value="{Binding Query}" />
              <Repeat Source="{Binding Items}" As="it" Key="{Binding it.Id}">
                <Text text="{Binding it.Label}" />
              </Repeat>
              <Button action="go-prev" content="‹" />
              <Button action="go-next" content="›" />
            </Container>
          </Page>
        </Cui>
        """;

    private static (CuiCompiledDocument Output, CuiV2Semantics Dto) Compile(string source, string name)
    {
        var result = new CuiCompiler().Compile(source, name, Options());
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var output = Assert.IsType<CuiCompiledDocument>(result.Output);
        return (output, CuiV2Semantics.From(output.Semantics));
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static CuiCompiledArtifact MakeArtifact(CuiCompiledArtifactDocument document) =>
        new(CuiCompiledArtifactCodec.FormatName, CuiCompiledArtifactCodec.FormatVersion,
            "compiler", document.Metadata, [document]);

    [Fact]
    public void Query_only_document_emits_query_viewport_capability_and_stays_abi1()
    {
        var (output, dto) = Compile(QueryOnlyMarkup, "query.cui");

        // Stateless query-only document keeps its ABI1 (no local-state Variables/Toggle).
        Assert.Equal("1", output.Metadata.RuntimeAbiVersion);
        Assert.False(CuiCompiledArtifactCodec.RequiresAbi2(dto));

        // The derived query capability must be present; local-state capabilities are absent.
        Assert.Contains(output.Metadata.RequiredCapabilities, capability => capability == "query-viewport");
        Assert.DoesNotContain(output.Metadata.RequiredCapabilities,
            capability => capability is "local-state-toggle" or "local-state-random-select");
    }

    [Fact]
    public void Stateful_document_with_query_viewport_emits_both_capabilities_and_abi2()
    {
        var source = """
            <Cui>
              <Variable Type="bool" ID="Open">False</Variable>
              <Actions>
                <Action name="toggle"><Toggle Target="Open" /></Action>
                <Action name="go-prev" command="query.previous" />
              </Actions>
              <Page>
                <Container Type="QueryViewport" Source="{Binding Catalogue}">
                  <Button action="go-prev" content="‹" />
                </Container>
              </Page>
            </Cui>
            """;
        var (output, dto) = Compile(source, "stateful-query.cui");
        Assert.Equal(CuiCompiledArtifactCodec.V2RuntimeAbiVersion, output.Metadata.RuntimeAbiVersion);
        Assert.True(CuiCompiledArtifactCodec.RequiresAbi2(dto));
        Assert.Contains(output.Metadata.RequiredCapabilities, capability => capability == "query-viewport");
        Assert.Contains(output.Metadata.RequiredCapabilities, capability => capability == "local-state-toggle");
    }

    [Fact]
    public void Template_definition_and_closure_routes_derive_query_viewport_capability()
    {
        var source = """
            <Cui>
              <Templates>
                <Template name="Picker">
                  <Container Type="QueryViewport" Source="{Binding Catalogue}">
                    <Repeat Source="{Binding Items}" As="it" Key="{Binding it.Id}"><Text text="{Binding it.Label}"/></Repeat>
                  </Container>
                </Template>
              </Templates>
              <Page><Picker/></Page>
            </Cui>
            """;
        var (output, _) = Compile(source, "template-query.cui");
        Assert.Contains(output.Metadata.RequiredCapabilities, capability => capability == "query-viewport");
    }

    [Fact]
    public void Stripped_query_viewport_capability_is_rejected_at_artifact_admission()
    {
        var (output, _) = Compile(QueryOnlyMarkup, "query.cui");
        var doc = CuiV2Projection.ToDocument(output, "query.cui", Hash(QueryOnlyMarkup));

        // Strip the derived query-viewport capability from both the document and envelope metadata.
        var strippedMeta = new CuiCompilationMetadata(
            doc.Metadata.LanguageVersion, doc.Metadata.RuntimeAbiVersion, doc.Metadata.ComponentRegistryVersion,
            doc.Metadata.RequiredCapabilities.Where(capability => capability != "query-viewport"));
        var corrupt = doc with { Metadata = strippedMeta };
        var artifact = MakeArtifact(corrupt);

        var error = Assert.Throws<CuiV2AdmissionException>(() =>
            CuiCompiledArtifactCodec.ReadArtifact(CuiCompiledArtifactCodec.WriteArtifact(artifact)));
        Assert.Equal("CUIR076", error.Code);
        Assert.Contains("query-viewport", error.Message);
    }

    [Fact]
    public void Unsupported_host_profile_rejects_query_viewport_artifact_before_any_effect()
    {
        var (output, _) = Compile(QueryOnlyMarkup, "query.cui");
        var doc = CuiV2Projection.ToDocument(output, "query.cui", Hash(QueryOnlyMarkup));
        var artifact = MakeArtifact(doc);
        var span = new CuiSourceSpan("query.cui", new(1, 1), new(1, 2));

        // An older host profile that predates the bounded paged QueryViewport scope must fail at
        // admission (CUIR067) before any reconstruction/control effect.
        var oldHost = new HashSet<string>(StringComparer.Ordinal)
        {
            "native-controls", "typed-properties", "live-bindings", "two-way-input",
            "live-conditionals", "stable-control-identity", "atomic-tree-replacement",
        };
        var error = Assert.Throws<CuiRuntimeLoadException>(() =>
            CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, span, oldHost));
        Assert.Equal("CUIR067", error.Diagnostic.Code);
        Assert.Contains("query-viewport", error.Message);

        // The current real host profile admits the exact same artifact unchanged.
        CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, span);
    }
}
