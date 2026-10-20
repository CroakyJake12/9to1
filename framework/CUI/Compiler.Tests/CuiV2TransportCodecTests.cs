using System.Security.Cryptography;
using System.Text;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

/// <summary>
/// Lane 2 transport tests: the distinct typed V2 codec must round-trip the real compiled semantic
/// model (variables, actions/LocalTarget, bindings/resources, conditionals/repeats, templates,
/// regions, static-mode properties) without any source-parser or compiler dependence at read time,
/// and must reject unknown/missing discriminators, malformed payloads and metadata/payload
/// contradictions before effects. The genuine V1 reader path is also preserved and must reject V2.
/// </summary>
public sealed class CuiV2TransportCodecTests
{
    private static CuiCompilerOptions Options() => new(new CuiCompilationMetadata("1", "1", "2.0", ["layout"]));

    private static (CuiCompiledDocument Output, CuiV2Semantics Dto) Compile(string source)
    {
        var result = new CuiCompiler().Compile(source, "sample.cui", Options());
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var output = Assert.IsType<CuiCompiledDocument>(result.Output);
        return (output, CuiV2Semantics.From(output.Semantics));
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static CuiCompiledArtifact MakeArtifact(CuiCompiledArtifactDocument document) =>
        new(CuiCompiledArtifactCodec.FormatName, CuiCompiledArtifactCodec.FormatVersion, "compiler",
            document.Metadata, [document]);

    private static CuiCompilationMetadata Meta(string language, string abi) =>
        new(language, abi, "2.0", []);

    private static CuiV2Span SpanAt() => new("sample.cui", 1, 1, 1, 2);

    [Fact]
    public void Typed_v2_round_trip_preserves_every_semantic_distinction()
    {
        const string source = """
            <Cui id="sample" version="1">
              <Actions>
                <Action name="toggle-x"><Toggle Target="Flag" /></Action>
                <Action name="do" command="do-thing" parameter="{Binding Item.Id}" />
              </Actions>
              <Variable Type="bool" ID="Flag">True</Variable>
              <Variable Type="string" ID="Pick" Source="{Binding Candidates}" Selection="RandomOnce" />
              <Page id="root">
                <Container id="box" Type="Vertical Stack" CellGap="8" CompoundFocus="true" OwnsBackdrop="true">
                  <If Test="{Binding Show}"><Text Text="Hi" /></If>
                  <Repeat Source="{Binding Items}" As="item" Key="{Binding item.Id}">
                    <Button action="do" content="{Binding item.Label}" />
                  </Repeat>
                </Container>
              </Page>
            </Cui>
            """;

        var (output, dto) = Compile(source);
        var sha = Hash(source);
        var artifact = MakeArtifact(CuiV2Projection.ToDocument(output, "sample.cui", sha));

        var bytes = CuiCompiledArtifactCodec.WriteArtifact(artifact);
        var decoded = CuiCompiledArtifactCodec.ReadArtifact(bytes);
        var doc = Assert.Single(decoded.Documents);
        Assert.Equal("sample.cui", doc.Source);
        Assert.Equal(sha, doc.Sha256);

        var semantics = doc.Semantics.ToCuiDocument();

        // Variables: bool toggle + string RandomOnce with source pool survive exactly.
        Assert.True(semantics.Variables.TryGetValue("Flag", out var flag));
        Assert.Equal("bool", flag!.Type);
        Assert.Equal("True", flag.Value);
        Assert.True(semantics.Variables.TryGetValue("Pick", out var pick));
        Assert.Equal("string", pick!.Type);
        Assert.Equal("RandomOnce", pick.Selection);
        Assert.Equal("Candidates", pick.Source);

        // Actions: pure-local toggle keeps its LocalTarget; parameter binding survives.
        Assert.True(semantics.Actions.TryGetValue("toggle-x", out var toggle));
        Assert.Equal("Flag", toggle!.LocalTarget);
        Assert.True(semantics.Actions.TryGetValue("do", out var doAction));
        var parameter = Assert.IsType<CuiBindingValue>(doAction!.Parameter);
        Assert.Equal("Item.Id", parameter.Path);

        // Conditionals, repeats, templates/IDs, regions and static-mode properties survive.
        var root = Assert.Single(semantics.Components);
        Assert.Equal("Page", root.Type);
        Assert.Equal("root", root.Name);
        var box = Assert.Single(root.Children);
        Assert.Equal("Container", box.Type);
        Assert.Equal("box", box.Name);
        Assert.True(box.Properties.ContainsKey("CellGap"));
        Assert.True(box.Properties.ContainsKey("CompoundFocus"));
        Assert.True(box.Properties.ContainsKey("OwnsBackdrop"));

        var condition = Assert.Single(box.Children, child => child.Condition is not null);
        Assert.NotNull(condition.Condition!.Test);
        var repeat = Assert.Single(box.Children, child => child.Repeat is not null);
        Assert.Equal("item", repeat.Repeat!.ItemName);
        Assert.NotNull(repeat.Repeat.Source);
        Assert.NotNull(repeat.Repeat.Key);
        var itemButton = Assert.Single(repeat.Children);
        Assert.Equal("Button", itemButton.Type);

        // The reconstructed payload has no source-parser/compiler dependence: it must be directly
        // consumable as a typed model without any CuiRichParser or CuiCompiler involvement.
        Assert.NotNull(doc.Semantics);
        Assert.Equal(2, semantics.Variables.Count);
        Assert.Equal(2, semantics.Actions.Count);
    }

    [Fact]
    public void Unknown_value_kind_is_rejected_before_effects()
    {
        var (output, dto) = Compile("<Cui><Page /></Cui>");
        var artifact = MakeArtifact(CuiV2Projection.ToDocument(output, "sample.cui", Hash("x")));
        var doc = artifact.Documents[0];
        var badValue = new CuiV2Value("bogus-kind", LiteralValue: "x", Span: new CuiV2Span("sample.cui", 1, 1, 1, 2));
        var corrupt = doc with { Semantics = dto with { RootProperties = new Dictionary<string, CuiV2Value> { ["V"] = badValue } } };
        var rewritten = artifact with { Documents = [corrupt] };

        var bytes = CuiCompiledArtifactCodec.WriteArtifact(rewritten);
        var error = Assert.Throws<CuiV2AdmissionException>(() => CuiCompiledArtifactCodec.ReadArtifact(bytes));
        Assert.Equal("CUIR071", error.Code);
    }

    [Fact]
    public void Missing_required_field_is_rejected()
    {
        var (output, _) = Compile("<Cui><Page /></Cui>");
        var artifact = MakeArtifact(CuiV2Projection.ToDocument(output, "sample.cui", Hash("x")));
        var corrupt = artifact.Documents[0] with { Semantics = null! };
        var bytes = CuiCompiledArtifactCodec.WriteArtifact(artifact with { Documents = [corrupt] });
        var error = Assert.Throws<CuiV2AdmissionException>(() => CuiCompiledArtifactCodec.ReadArtifact(bytes));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Malformed_payload_is_rejected()
    {
        var bytes = Encoding.UTF8.GetBytes("{ this is not json !!!");
        var error = Assert.Throws<CuiV2AdmissionException>(() => CuiCompiledArtifactCodec.ReadArtifact(bytes));
        Assert.Equal("CUIR070", error.Code);
    }

    [Fact]
    public void Duplicate_document_source_identity_is_rejected()
    {
        var (output, _) = Compile("<Cui><Page /></Cui>");
        var doc = CuiV2Projection.ToDocument(output, "same.cui", Hash("x"));
        var artifact = new CuiCompiledArtifact(CuiCompiledArtifactCodec.FormatName, CuiCompiledArtifactCodec.FormatVersion,
            "compiler", doc.Metadata, [doc, doc]);
        var bytes = CuiCompiledArtifactCodec.WriteArtifact(artifact);
        var error = Assert.Throws<CuiV2AdmissionException>(() => CuiCompiledArtifactCodec.ReadArtifact(bytes));
        Assert.Equal("CUIR073", error.Code);
    }

    [Fact]
    public void Envelope_document_abi_contradiction_is_rejected()
    {
        var (output, _) = Compile("<Cui><Page /></Cui>");
        var doc = CuiV2Projection.ToDocument(output, "sample.cui", Hash("x"));
        var envelope = new CuiCompilationMetadata("1", "2", "2.0", doc.Metadata.RequiredCapabilities);
        var artifact = new CuiCompiledArtifact(CuiCompiledArtifactCodec.FormatName, CuiCompiledArtifactCodec.FormatVersion,
            "compiler", envelope, [doc]);
        var bytes = CuiCompiledArtifactCodec.WriteArtifact(artifact);
        var error = Assert.Throws<CuiV2AdmissionException>(() => CuiCompiledArtifactCodec.ReadArtifact(bytes));
        Assert.Equal("CUIR075", error.Code);
    }

    [Fact]
    public void Stateful_document_requires_abi2_by_inspecting_typed_semantics()
    {
        var (output, _) = Compile(
            "<Cui><Variable Type=\"bool\" ID=\"Flag\">True</Variable>" +
            "<Actions><Action name=\"t\"><Toggle Target=\"Flag\" /></Action></Actions><Page /></Cui>");
        // The compiler must have emitted ABI2 purely from the typed semantics.
        Assert.Equal(CuiCompiledArtifactCodec.V2RuntimeAbiVersion, output.Metadata.RuntimeAbiVersion);

        // Even if the capability is stripped, the typed semantics are stateful => ABI2 is required.
        var doc = CuiV2Projection.ToDocument(output, "sample.cui", Hash("x"));
        Assert.True(CuiCompiledArtifactCodec.RequiresAbi2(doc.Semantics));
    }

    [Fact]
    public void Genuine_v1_reader_consumes_stateless_v1_and_rejects_v2()
    {
        var (output, _) = Compile("<Cui><Page><Text Text=\"hi\" /></Page></Cui>");
        Assert.Equal("1", output.Metadata.RuntimeAbiVersion);
        Assert.Equal(CuiCompiledArtifactCodec.V1ResourceLogicalName, CuiCompiledArtifactCodec.V1ResourceLogicalName);

        var v1 = CuiV1ArtifactCodec.WriteBundle(
            [new CuiV1ArtifactCodec.CuiV1Compiled(output, "sample.cui", Hash("stateless"))], "compiler");
        var text = Encoding.UTF8.GetString(v1);
        // Genuine V1 retains the full typed compiled projection (typed value markers, source maps).
        Assert.Contains("CuiLiteralValue", text);
        Assert.Contains("SourceMap", text);

        var loaded = CuiV1ArtifactCodec.ReadArtifact(v1);
        Assert.Equal(1, loaded.FormatVersion);
        var doc = Assert.Single(loaded.Documents);
        Assert.Equal("sample.cui", doc.Source);
        var root = Assert.Single(doc.Semantics.Components);
        Assert.Equal("Page", root.Type);

        // A V2 artifact must not be silently consumed by the genuine V1 reader.
        var (statefulOutput, _) = Compile(
            "<Cui><Variable Type=\"bool\" ID=\"Flag\">True</Variable><Page /></Cui>");
        var v2Doc = CuiV2Projection.ToDocument(statefulOutput, "sample2.cui", Hash("stateful"));
        var v2 = CuiCompiledArtifactCodec.WriteArtifact(
            new CuiCompiledArtifact(CuiCompiledArtifactCodec.FormatName, CuiCompiledArtifactCodec.FormatVersion,
                "compiler", v2Doc.Metadata, [v2Doc]));
        Assert.Throws<InvalidDataException>(() => CuiV1ArtifactCodec.ReadArtifact(v2));
    }

    [Fact]
    public void Genuine_v1_span_round_trip_preserves_source_coordinates()
    {
        var (output, _) = Compile("<Cui><Page ID=\"root\"><Text Text=\"hi\" /></Page></Cui>");
        var v1 = CuiV1ArtifactCodec.WriteBundle(
            [new CuiV1ArtifactCodec.CuiV1Compiled(output, "sample.cui", Hash("stateless"))], "compiler");
        var loaded = CuiV1ArtifactCodec.ReadArtifact(v1);
        var doc = Assert.Single(loaded.Documents);

        var originalRoot = output.Semantics.Components[0];
        var root = Assert.Single(doc.Semantics.Components);
        // The genuine V1 reader must recover the actual authored coordinates (not a default (1,1)).
        Assert.Equal(originalRoot.Span.Start.Line, root.Span.Start.Line);
        Assert.Equal(originalRoot.Span.Start.Column, root.Span.Start.Column);
        Assert.Equal(originalRoot.Span.End.Line, root.Span.End.Line);
    }

    [Fact]
    public void Case_distinct_symbols_round_trip_without_collapsing()
    {
        var span = SpanAt();
        var value = new CuiV2Value(CuiV2ValueKind.Literal, LiteralValue: "True", Span: span);
        var doc = new CuiV2Semantics(
            "sample.cui",
            new Dictionary<string, CuiV2Resource>(StringComparer.Ordinal)
            {
                ["Flag"] = new CuiV2Resource("Flag", value, span),
                ["flag"] = new CuiV2Resource("flag", value, span),
            },
            Array.Empty<CuiV2Style>(),
            new Dictionary<string, CuiV2Action>(StringComparer.Ordinal),
            new Dictionary<string, CuiV2Template>(StringComparer.Ordinal),
            Array.Empty<CuiV2Component>(),
            span,
            new Dictionary<string, CuiV2Value>(StringComparer.Ordinal),
            Array.Empty<CuiV2Import>(),
            new Dictionary<string, CuiV2Variable>(StringComparer.Ordinal)
            {
                ["Flag"] = new CuiV2Variable("Flag", "bool", "True", span),
                ["flag"] = new CuiV2Variable("flag", "bool", "False", span),
            });
        var document = new CuiCompiledArtifactDocument("sample.cui", Hash("x"), Meta("1", "2"),
            doc, Array.Empty<CuiV2SourceMapEntry>(), new CuiV2DependencyGraph(Array.Empty<CuiV2Dependency>()));
        var artifact = MakeArtifact(document);
        var bytes = CuiCompiledArtifactCodec.WriteArtifact(artifact);
        var decoded = Assert.Single(CuiCompiledArtifactCodec.ReadArtifact(bytes).Documents).Semantics;

        // Distinct symbols differing only by case must both survive; nothing collapses to
        // OrdinalIgnoreCase or broadens a wrong-case lookup.
        Assert.Equal(2, decoded.Resources.Count);
        Assert.True(decoded.Resources.ContainsKey("Flag"));
        Assert.True(decoded.Resources.ContainsKey("flag"));
        Assert.Equal(2, decoded.Variables.Count);
        Assert.True(decoded.Variables.ContainsKey("Flag"));
        Assert.True(decoded.Variables.ContainsKey("flag"));

        var reconstructed = decoded.ToCuiDocument();
        Assert.True(reconstructed.Resources.ContainsKey("Flag"));
        Assert.True(reconstructed.Resources.ContainsKey("flag"));
        Assert.True(reconstructed.Variables.ContainsKey("Flag"));
        Assert.True(reconstructed.Variables.ContainsKey("flag"));
    }

    [Fact]
    public void Null_styles_collection_is_rejected()
    {
        var span = SpanAt();
        var doc = new CuiV2Semantics(
            "sample.cui",
            new Dictionary<string, CuiV2Resource>(StringComparer.Ordinal),
            null!,
            new Dictionary<string, CuiV2Action>(StringComparer.Ordinal),
            new Dictionary<string, CuiV2Template>(StringComparer.Ordinal),
            Array.Empty<CuiV2Component>(),
            span,
            new Dictionary<string, CuiV2Value>(StringComparer.Ordinal),
            Array.Empty<CuiV2Import>(),
            new Dictionary<string, CuiV2Variable>(StringComparer.Ordinal));
        var artifact = MakeArtifact(new CuiCompiledArtifactDocument("sample.cui", Hash("x"), Meta("1", "1"),
            doc, Array.Empty<CuiV2SourceMapEntry>(), new CuiV2DependencyGraph(Array.Empty<CuiV2Dependency>())));
        var bytes = CuiCompiledArtifactCodec.WriteArtifact(artifact);
        var error = Assert.Throws<CuiV2AdmissionException>(() => CuiCompiledArtifactCodec.ReadArtifact(bytes));
        Assert.Equal("CUIR072", error.Code);
    }

    [Fact]
    public void Unsupported_dependency_kind_is_rejected()
    {
        var span = SpanAt();
        var doc = new CuiV2Semantics(
            "sample.cui",
            new Dictionary<string, CuiV2Resource>(StringComparer.Ordinal),
            Array.Empty<CuiV2Style>(),
            new Dictionary<string, CuiV2Action>(StringComparer.Ordinal),
            new Dictionary<string, CuiV2Template>(StringComparer.Ordinal),
            Array.Empty<CuiV2Component>(),
            span,
            new Dictionary<string, CuiV2Value>(StringComparer.Ordinal),
            Array.Empty<CuiV2Import>(),
            new Dictionary<string, CuiV2Variable>(StringComparer.Ordinal));
        var graph = new CuiV2DependencyGraph([new CuiV2Dependency("gen:v1:1", "BogusKind", "y")]);
        var artifact = MakeArtifact(new CuiCompiledArtifactDocument("sample.cui", Hash("x"), Meta("1", "1"),
            doc, Array.Empty<CuiV2SourceMapEntry>(), graph));
        var bytes = CuiCompiledArtifactCodec.WriteArtifact(artifact);
        var error = Assert.Throws<CuiV2AdmissionException>(() => CuiCompiledArtifactCodec.ReadArtifact(bytes));
        Assert.Equal("CUIR071", error.Code);
    }

    [Fact]
    public void Missing_value_span_is_rejected_before_consumer_use()
    {
        var span = SpanAt();
        var badValue = new CuiV2Value(CuiV2ValueKind.Literal, LiteralValue: "x", Span: null);
        var doc = new CuiV2Semantics(
            "sample.cui",
            new Dictionary<string, CuiV2Resource>(StringComparer.Ordinal),
            Array.Empty<CuiV2Style>(),
            new Dictionary<string, CuiV2Action>(StringComparer.Ordinal),
            new Dictionary<string, CuiV2Template>(StringComparer.Ordinal),
            Array.Empty<CuiV2Component>(),
            span,
            new Dictionary<string, CuiV2Value>(StringComparer.Ordinal) { ["V"] = badValue },
            Array.Empty<CuiV2Import>(),
            new Dictionary<string, CuiV2Variable>(StringComparer.Ordinal));
        var artifact = MakeArtifact(new CuiCompiledArtifactDocument("sample.cui", Hash("x"), Meta("1", "1"),
            doc, Array.Empty<CuiV2SourceMapEntry>(), new CuiV2DependencyGraph(Array.Empty<CuiV2Dependency>())));
        var bytes = CuiCompiledArtifactCodec.WriteArtifact(artifact);
        var error = Assert.Throws<CuiV2AdmissionException>(() => CuiCompiledArtifactCodec.ReadArtifact(bytes));
        Assert.Equal("CUIR072", error.Code);
    }
}
