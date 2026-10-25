using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperCuiSourceAnalysisTests
{
    [Fact]
    public void Authoritative_parser_problems_use_the_common_diagnostic_model_and_exact_draft_identity()
    {
        const string text = "<Cui xmlns=\"urn:invalid\"><Page /></Cui>";
        var result = DeveloperCuiSourceAnalysis.Analyze("Main.cui", text);
        var error = Assert.Single(result.Problems, problem => problem.Code == "CUI002");
        Assert.Equal(CodeDiagnosticSeverity.Error, error.Severity); Assert.Equal("Main.cui", error.RelativePath);
        Assert.Equal("CUI draft", error.Source); Assert.Equal(0, error.Range.Start.Line);
        var repeated = DeveloperCuiSourceAnalysis.Analyze("Main.cui", text);
        Assert.Equal(result.ContentSha256, repeated.ContentSha256); Assert.NotEqual(result.Id, repeated.Id);
        Assert.NotEqual(result.ContentSha256, DeveloperCuiSourceAnalysis.Analyze("Main.cui", "<Cui />").ContentSha256);
        Assert.True(Assert.IsAssignableFrom<IList<CodeDiagnostic>>(result.Problems).IsReadOnly);
    }

    [Fact]
    public void Real_action_contract_and_component_tree_are_used_without_parsing_text_as_fake_symbols()
    {
        const string text = "<Cui><Page><Container id=\"body\"><Text id=\"label\" text=\"&lt;Button id='fake' /&gt;\" />" +
            "<Button id=\"go\"><ActionEvent Type=\"UnknownEvent\" /></Button></Container></Page></Cui>";
        var result = DeveloperCuiSourceAnalysis.Analyze("Main.cui", text);
        Assert.Contains(result.Problems, diagnostic => diagnostic.Code == "CUIE001");
        var body = Assert.Single(result.Outline, element => element.Name == "body");
        var label = Assert.Single(result.Outline, element => element.Name == "label");
        Assert.Equal(body.Depth + 1, label.Depth); Assert.Equal("Text", label.Type);
        Assert.DoesNotContain(result.Outline, element => element.Name == "fake");
        Assert.True(Assert.IsAssignableFrom<IList<DeveloperCuiSourceElement>>(result.Outline).IsReadOnly);
    }

    [Fact]
    public void Supported_empty_results_are_distinct_from_unavailable_languages_large_files_and_cancellation()
    {
        var valid = DeveloperCuiSourceAnalysis.Analyze("Empty.cui", "<Cui />");
        Assert.Empty(valid.Problems); Assert.Empty(valid.Outline); Assert.False(valid.HasMoreElements);
        Assert.Throws<NotSupportedException>(() => DeveloperCuiSourceAnalysis.Analyze("Main.cs", "class Main {}"));
        Assert.Throws<NotSupportedException>(() => DeveloperCuiSourceAnalysis.Analyze("Huge.cui", new string(' ', DeveloperCuiSourceAnalysis.MaximumAnalysisCharacters + 1)));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => DeveloperCuiSourceAnalysis.Analyze("Main.cui", "<Cui />", cancelled.Token));
    }
}
