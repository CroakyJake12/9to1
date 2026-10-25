using System.Security.Cryptography;
using System.Text;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiActionShelfCapabilityTests
{
    private const string Source = """
        <Cui><Actions><Action name="shelf.activate" command="shelf.activate" /></Actions>
          <Page><Container Type="ActionShelf" Source="{Binding Eligible}">
            <Button action="shelf.activate" content="{Binding Shelf.FrontLabel}" />
          </Container></Page>
        </Cui>
        """;
    [Fact]
    public void Shelf_semantics_derive_required_capability_and_reject_stripped_or_unsupported_hosts()
    {
        var result = new CuiCompiler().Compile(Source, "shelf.cui", new(new CuiCompilationMetadata("1.0", "1", "2.0")));
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var output = Assert.IsType<CuiCompiledDocument>(result.Output);
        Assert.Contains("action-shelf", output.Metadata.RequiredCapabilities);
        var document = CuiV2Projection.ToDocument(output, "shelf.cui",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source))).ToLowerInvariant());
        var artifact = new CuiCompiledArtifact(CuiCompiledArtifactCodec.FormatName, CuiCompiledArtifactCodec.FormatVersion,
            "compiler", document.Metadata, [document]);
        var span = new CuiSourceSpan("shelf.cui", new(1, 1), new(1, 2));
        CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, span);
        var unsupported = CuiRuntimeCompatibility.Capabilities.Where(capability => capability != "action-shelf").ToHashSet(StringComparer.Ordinal);
        Assert.Equal("CUIR067", Assert.Throws<CuiRuntimeLoadException>(() =>
            CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, span, unsupported)).Diagnostic.Code);
        var metadata = new CuiCompilationMetadata(document.Metadata.LanguageVersion, document.Metadata.RuntimeAbiVersion,
            document.Metadata.ComponentRegistryVersion, unsupported);
        var stripped = artifact with { Metadata = metadata, Documents = [document with { Metadata = metadata }] };
        var failure = Assert.Throws<CuiV2AdmissionException>(() =>
            CuiCompiledArtifactCodec.ReadArtifact(CuiCompiledArtifactCodec.WriteArtifact(stripped)));
        Assert.Equal("CUIR076", failure.Code);
        Assert.Contains("action-shelf", failure.Message);
    }
}
