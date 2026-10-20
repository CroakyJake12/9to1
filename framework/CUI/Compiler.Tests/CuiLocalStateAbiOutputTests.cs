using System.Reflection;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

/// <summary>
/// Lane 2 compiler-output tests: stateful documents (local bool Toggle variables and RandomOnce
/// string sources) must emit the distinct stateful ABI2/V2 metadata from their typed semantics,
/// while plain stateless documents stay ABI1/V1 eligible. The public compatibility record must keep
/// its CLR identity in the Core assembly (forwarder) and the four-argument action constructor form.
/// </summary>
public sealed class CuiLocalStateAbiOutputTests
{
    private static CuiCompilerOptions Options() => new(new CuiCompilationMetadata("1.0", "1.0", "2.0", ["layout"]));

    [Fact]
    public void Stateful_toggle_document_emits_abi2_and_local_state_toggle_capability()
    {
        var source = """
            <Cui>
              <Variable Type="bool" ID="FormattingOpen">False</Variable>
              <Actions><Action name="composer.toggle-formatting"><Toggle Target="FormattingOpen" /></Action></Actions>
              <Page><Button Action="composer.toggle-formatting" Content="Aa" /></Page>
            </Cui>
            """;
        var result = new CuiCompiler().Compile(source, "stateful.cui", Options());
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var output = Assert.IsType<CuiCompiledDocument>(result.Output);
        Assert.Equal(CuiCompiledArtifactCodec.V2RuntimeAbiVersion, output.Metadata.RuntimeAbiVersion);
        Assert.Contains(output.Metadata.RequiredCapabilities, capability => capability == "local-state-toggle");
        Assert.True(CuiCompiledArtifactCodec.RequiresAbi2(CuiV2Semantics.From(output.Semantics)));
    }

    [Fact]
    public void Stateful_random_once_document_emits_abi2_and_random_select_capability()
    {
        var source = """
            <Cui>
              <Variable Type="string" ID="EmojiIcon" Source="{Binding Composer.EmojiIconCandidates}" Selection="RandomOnce"/>
              <Page />
            </Cui>
            """;
        var result = new CuiCompiler().Compile(source, "random-once.cui", Options());
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var output = Assert.IsType<CuiCompiledDocument>(result.Output);
        Assert.Equal(CuiCompiledArtifactCodec.V2RuntimeAbiVersion, output.Metadata.RuntimeAbiVersion);
        Assert.Contains(output.Metadata.RequiredCapabilities, capability => capability == "local-state-random-select");
        Assert.True(CuiCompiledArtifactCodec.RequiresAbi2(CuiV2Semantics.From(output.Semantics)));
    }

    [Fact]
    public void Stateless_document_stays_abi1_and_v1_eligible()
    {
        var source = "<Cui><Page><Text Text=\"hi\" /></Page></Cui>";
        var result = new CuiCompiler().Compile(source, "stateless.cui", Options());
        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        var output = Assert.IsType<CuiCompiledDocument>(result.Output);
        Assert.Equal("1", output.Metadata.RuntimeAbiVersion);
        Assert.DoesNotContain(output.Metadata.RequiredCapabilities, capability => capability is "local-state-toggle" or "local-state-random-select");
        Assert.False(CuiCompiledArtifactCodec.RequiresAbi2(CuiV2Semantics.From(output.Semantics)));
    }

    [Fact]
    public void Public_compatibility_record_keeps_core_clr_identity_via_forwarder()
    {
        // CuiCompilationMetadata historically lived in the Compiler assembly; it now lives in Core
        // and the Compiler assembly forwards its CLR identity. Resolving the type must yield the
        // Core assembly, not the Compiler assembly.
        var assembly = typeof(CakeOS.Cui.Compiler.CuiCompilationMetadata).Assembly;
        Assert.Equal("CakeOS.Cui.Core", assembly.GetName().Name);
    }

    [Fact]
    public void Four_argument_action_definition_constructor_form_is_preserved()
    {
        // Source-level four-argument form remains valid (LocalTarget is optional and null here).
        var action = new CuiActionDefinition("toggle", "composer.toggle", null, new CuiSourceSpan("a.cui", new(1, 1), new(1, 2)));
        Assert.Equal("toggle", action.Name);
        Assert.Equal("composer.toggle", action.Command);
        Assert.Null(action.LocalTarget);
        Assert.Null(action.Parameter);

        // The LocalTarget-bearing constructor form (used by the composer) must also remain valid.
        var targeted = new CuiActionDefinition("toggle", "composer.toggle", null, new CuiSourceSpan("a.cui", new(1, 1), new(1, 2)), "FormattingOpen");
        Assert.Equal("FormattingOpen", targeted.LocalTarget);
    }
}
