using CakeOS.Cui.Compiler;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

/// <summary>
/// Focused validation for the bounded local-state slice: document-root typed bool variables and
/// pure-local Toggle actions. Nested/unsupported/non-bool declarations and undeclared or
/// non-bool toggle targets are rejected before a compiled artifact is emitted, and only valid
/// documents announce the local-state-toggle capability.
/// </summary>
public sealed class CuiLocalStateCompilationTests
{
    private static CuiCompilerOptions Options() => new(new CuiCompilationMetadata("1.0", "1.0", "2.0", ["layout"]));

    [Fact]
    public void Compile_emits_local_state_toggle_capability_for_valid_document()
    {
        var source = """
            <Cui>
              <Variable Type="bool" ID="FormattingOpen">False</Variable>
              <Actions><Action name="composer.toggle-formatting"><Toggle Target="FormattingOpen" /></Action></Actions>
              <Page><Button Action="composer.toggle-formatting" Content="Aa" /></Page>
            </Cui>
            """;
        var result = new CuiCompiler().Compile(source, "local-state.cui", Options());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
        Assert.Contains(result.Output.Metadata.RequiredCapabilities, capability => capability == "local-state-toggle");
    }

    [Fact]
    public void Compile_rejects_unsupported_local_variable()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Variable Type=\"complex\" ID=\"Count\">0</Variable><Page /></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC030");
    }

    [Fact]
    public void Compile_rejects_local_bool_variable_with_non_literal_value()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Variable Type=\"bool\" ID=\"Flag\">banana</Variable><Page /></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC031");
    }

    [Fact]
    public void Compile_rejects_toggle_targeting_undeclared_variable()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Actions><Action name=\"toggle\"><Toggle Target=\"missing\" /></Action></Actions><Page /></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC033");
    }

    [Fact]
    public void Compile_rejects_toggle_targeting_non_bool_declared_variable()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Variable Type=\"int\" ID=\"Count\">0</Variable>" +
            "<Actions><Action name=\"toggle\"><Toggle Target=\"Count\" /></Action></Actions><Page /></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC034");
    }

    [Fact]
    public void Compile_rejects_nested_variable_declaration_outside_document_root()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Page><Variable Type=\"bool\" ID=\"Flag\">False</Variable></Page></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC032");
    }

    [Fact]
    public void Compile_emits_random_select_capability_for_valid_random_once_string_variable()
    {
        var source = """
            <Cui>
              <Variable Type="string" ID="EmojiIcon" Source="{Binding Composer.EmojiIconCandidates}" Selection="RandomOnce"/>
              <Page />
            </Cui>
            """;
        var result = new CuiCompiler().Compile(source, "random-once.cui", Options());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
        Assert.Contains(result.Output.Metadata.RequiredCapabilities, capability => capability == "local-state-random-select");
        Assert.True(result.Output.Semantics.Variables.TryGetValue("EmojiIcon", out var variable));
        Assert.Equal("string", variable!.Type);
        Assert.Equal("Composer.EmojiIconCandidates", variable.Source);
        Assert.Equal("RandomOnce", variable.Selection);
    }

    [Fact]
    public void Compile_rejects_random_once_with_non_string_type()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Variable Type=\"bool\" ID=\"EmojiIcon\" Source=\"{Binding Candidates}\" Selection=\"RandomOnce\"/><Page/></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC036");
    }

    [Fact]
    public void Compile_rejects_random_once_without_source()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Variable Type=\"string\" ID=\"EmojiIcon\" Selection=\"RandomOnce\"/><Page/></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC037");
    }

    [Fact]
    public void Compile_rejects_unsupported_selection_strategy()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Variable Type=\"string\" ID=\"EmojiIcon\" Source=\"{Binding Candidates}\" Selection=\"EveryTime\"/><Page/></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC035");
    }

    [Fact]
    public void Compile_rejects_random_once_with_literal_initializer()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Variable Type=\"string\" ID=\"EmojiIcon\" Source=\"{Binding Candidates}\" Selection=\"RandomOnce\">😀</Variable><Page/></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC038");
    }

    [Fact]
    public void Compile_rejects_random_once_source_that_is_not_a_binding()
    {
        var result = new CuiCompiler().Compile(
            "<Cui><Variable Type=\"string\" ID=\"EmojiIcon\" Source=\"plain-path\" Selection=\"RandomOnce\"/><Page/></Cui>",
            "bad.cui", Options());

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUI056");
    }
}
