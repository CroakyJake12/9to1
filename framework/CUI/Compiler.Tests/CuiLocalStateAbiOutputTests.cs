using System.Reflection;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
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
    private static CuiCompilerOptions Options() => new(new CuiCompilationMetadata("1.0", "1", "2.0", ["layout"]));

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
    public void Bool_variable_with_source_without_selection_is_rejected_with_cuic039()
    {
        // A bool literal cannot carry a Source-driven candidate pool without a Selection; the
        // compiler must reject it with CUIC039 at the declaration, matching the runtime's
        // CUIR077 rejection, rather than failing on an unrelated Page/property diagnostic.
        var source = """
            <Cui>
              <Variable Type="bool" ID="Flag" Source="{Binding Candidates}">true</Variable>
              <Page />
            </Cui>
            """;
        var result = new CuiCompiler().Compile(source, "bool-source.cui", Options());
        Assert.False(result.Succeeded);
        Assert.Null(result.Output);
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
        Assert.Equal("CUIC039", error.Code);
        Assert.Contains("Flag", error.Message);
        Assert.Contains("Source", error.Message);
    }

    [Fact]
    public void Public_compatibility_record_keeps_core_clr_identity_via_forwarder()
    {
        // CuiCompilationMetadata historically lived in the Compiler assembly; it now lives in Core
        // and the Compiler assembly forwards its CLR identity. Resolving the type must yield the
        // Core assembly, not the Compiler assembly.
        var assembly = typeof(CakeOS.Cui.Compiler.CuiCompilationMetadata).Assembly;
        Assert.Equal("CakeOS.Cui.Core", assembly.GetName().Name);

        var compilerAssembly = typeof(CakeOS.Cui.Compiler.CuiCompiler).Assembly;

        // Real forwarding evidence #1: the Compiler assembly's forwarded-type table must expose the
        // Core type (this is the metadata that actually redirects an old reference). GetForwardedTypes
        // can throw if any unrelated forwarded type fails to load; tolerate that, but the target type
        // must always be present.
        Type[] forwarded;
        try { forwarded = compilerAssembly.GetForwardedTypes(); }
        catch (ReflectionTypeLoadException ex) { forwarded = ex.Types.OfType<Type>().ToArray(); }
        Assert.Contains(forwarded, t => t == typeof(CuiCompilationMetadata));

        // Real forwarding evidence #2: the historical assembly-qualified name used by precompiled
        // V1 consumers must resolve to the live type in the Core assembly — not fail, and never land
        // in the Compiler assembly.
        var resolved = Type.GetType("CakeOS.Cui.Compiler.CuiCompilationMetadata, CakeOS.Cui.Compiler", throwOnError: false);
        Assert.NotNull(resolved);
        Assert.Equal(typeof(CuiCompilationMetadata), resolved);
        Assert.Equal("CakeOS.Cui.Core", resolved!.Assembly.GetName().Name);
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

    [Fact]
    public void Four_argument_action_definition_clr_constructor_and_deconstruct_are_preserved()
    {
        // Binary compatibility: old precompiled consumers referenced the four-parameter CLR
        // constructor and the four-output Deconstruct generated by the old four-parameter record.
        // A later optional fifth primary-constructor argument does NOT preserve either CLR member,
        // so both must exist explicitly. This is an exact reflection-signature assertion (an
        // authentic old-reference binary fixture requires a separately compiled consumer assembly).
        var type = typeof(CuiActionDefinition);
        var ctor = type.GetConstructors().SingleOrDefault(c => c.GetParameters().Length == 4);
        Assert.NotNull(ctor);
        var ps = ctor!.GetParameters();
        Assert.Equal([typeof(string), typeof(string), typeof(CuiValue), typeof(CuiSourceSpan)],
            ps.Select(p => p.ParameterType).ToArray());

        var deconstruct = type.GetMethods()
            .FirstOrDefault(m => m.Name == "Deconstruct" && m.GetParameters().Length == 4);
        Assert.NotNull(deconstruct);
        // The four-output Deconstruct is generated by the record as `out` parameters, so CLR
        // reflection reports ByRef parameter types (string&, string&, CuiValue&, CuiSourceSpan&).
        // Expect the exact ByRef element types rather than the ordinary types.
        Assert.Equal([
            typeof(string).MakeByRefType(),
            typeof(string).MakeByRefType(),
            typeof(CuiValue).MakeByRefType(),
            typeof(CuiSourceSpan).MakeByRefType()],
            deconstruct!.GetParameters().Select(p => p.ParameterType).ToArray());
    }
}
