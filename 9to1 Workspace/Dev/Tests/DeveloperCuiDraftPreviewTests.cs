using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperCuiDraftPreviewTests
{
    [Fact]
    public void Existing_reviewed_new_file_CUI_template_is_immediately_previewable()
    {
        var prepared = DeveloperNewSourceFile.Prepare("NewPage.cui", true);
        Assert.True(prepared.Succeeded, prepared.Error?.Message);
        var template = prepared.Value!;
        var preview = DeveloperCuiDraftPreviewCompiler.Compile(template.RelativePath, template.Text);
        Assert.True(preview.Succeeded, string.Join(Environment.NewLine, preview.Diagnostics));
        var page = Assert.Single(preview.Document!.Components); Assert.Equal("page", page.AuthoredId);
        var title = Assert.Single(Assert.Single(page.Children).Children);
        Assert.True(title.TryGetLiteralAttribute("text", out var value)); Assert.Equal("New CUI page", value);
    }

    [Fact]
    public void Actual_compiler_and_V2_admission_preserve_exact_draft_identity_and_typed_literals()
    {
        const string source = "<Cui id='draft' version='1'>\r\n<!-- keep &amp; comments -->\r\n<Page id='page'><Container id='body' Type='Vertical Stack' margin='12' CellGap='4' background='#112233'><Text id='label' text='One &amp; &quot;two&quot;' foreground='#FF445566' font-family='monospace' font-size='18' /></Container></Page></Cui>";
        var preview = DeveloperCuiDraftPreviewCompiler.Compile("src/Draft.cui", source);
        Assert.True(preview.Succeeded, string.Join(Environment.NewLine, preview.Diagnostics));
        Assert.Equal("src/Draft.cui", preview.RelativePath);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant(), preview.SourceSha256);
        var page = Assert.Single(preview.Document!.Components); Assert.Equal("Page", page.Type);
        var body = Assert.Single(page.Children); Assert.Equal("body", body.AuthoredId);
        var text = Assert.Single(body.Children); Assert.Equal("Text", text.Type);
        Assert.True(text.TryGetLiteralAttribute("text", out var value)); Assert.Equal("One & \"two\"", value);
        Assert.Empty(preview.Document.Imports); Assert.Empty(preview.Document.Actions); Assert.Empty(preview.Document.Effects);
        Assert.Empty(preview.Document.Variables); Assert.Empty(preview.Document.Resources);
    }

    [Theory]
    [InlineData("<Cui><Page><Button action='app.run' /></Page></Cui>")]
    [InlineData("<Cui><Import Source='https://example.test/ui.cui' /><Page /></Cui>")]
    [InlineData("<Cui><Page><Image Source='https://example.test/picture.png' /></Page></Cui>")]
    [InlineData("<Cui><Page><Object Type='ReadOnlyText' text='host content' /></Page></Cui>")]
    [InlineData("<Cui><Page><Text text='{Binding PrivateText}' /></Page></Cui>")]
    [InlineData("<Cui><Page><Text text='&#123;Resource Secret&#125;' /></Page></Cui>")]
    [InlineData("<Cui><Page><Text font-family='https://example.test/font.ttf#Font' /></Page></Cui>")]
    [InlineData("<Cui><Page><Container background='url(https://example.test/pixel)' /></Page></Cui>")]
    [InlineData("<Cui><Page onload='app.run' /></Cui>")]
    [InlineData("<Cui><Page /><Page /></Cui>")]
    [InlineData("<Cui><Page><Container width='Infinity' /></Page></Cui>")]
    [InlineData("<Cui><Page><Text font-size='9000' /></Page></Cui>")]
    public void Unsupported_or_effectful_source_is_a_diagnostic_without_a_scene(string source)
    {
        var preview = DeveloperCuiDraftPreviewCompiler.Compile("Draft.cui", source);
        Assert.False(preview.Succeeded); Assert.Null(preview.Document); Assert.NotEmpty(preview.Diagnostics);
        Assert.All(preview.Diagnostics, diagnostic => Assert.Equal("DEVCP003", diagnostic.Code));
    }

    [Fact]
    public void Syntax_and_duplicate_identity_are_rejected_by_the_maintained_language_owner()
    {
        var malformed = DeveloperCuiDraftPreviewCompiler.Compile("Draft.cui", "<Cui><Page>");
        Assert.False(malformed.Succeeded); Assert.Null(malformed.Document); Assert.NotEmpty(malformed.Diagnostics);
        var duplicate = DeveloperCuiDraftPreviewCompiler.Compile("Draft.cui", "<Cui><Page><Text id='same' text='one' /><Text id='same' text='two' /></Page></Cui>");
        Assert.False(duplicate.Succeeded); Assert.Null(duplicate.Document);
        Assert.Contains(duplicate.Diagnostics, diagnostic => diagnostic.Code.StartsWith("CUI", StringComparison.Ordinal));
    }

    [Fact]
    public void DTD_depth_node_and_size_bounds_refuse_before_scene_acquisition()
    {
        var sources = new[]
        {
            "<!DOCTYPE Cui [<!ENTITY external SYSTEM 'file:///not-authorised'>]><Cui><Page><Text>&external;</Text></Page></Cui>",
            "<Cui><Page>" + string.Concat(Enumerable.Repeat("<Container>", 40)) + string.Concat(Enumerable.Repeat("</Container>", 40)) + "</Page></Cui>",
            "<Cui><Page>" + string.Concat(Enumerable.Repeat("<Text />", 500)) + "</Page></Cui>",
            new string(' ', DeveloperCuiDraftPreviewCompiler.MaximumSourceLength + 1)
        };
        foreach (var source in sources)
        {
            var preview = DeveloperCuiDraftPreviewCompiler.Compile("Draft.cui", source);
            Assert.False(preview.Succeeded); Assert.Null(preview.Document); Assert.NotEmpty(preview.Diagnostics);
        }
    }

    [Fact]
    public void Cancelled_compilation_does_not_return_an_admitted_scene_or_change_an_earlier_result()
    {
        const string source = "<Cui><Page><Text text='valid' /></Page></Cui>";
        var valid = DeveloperCuiDraftPreviewCompiler.Compile("Draft.cui", source);
        Assert.True(valid.Succeeded, string.Join(Environment.NewLine, valid.Diagnostics));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => { _ = DeveloperCuiDraftPreviewCompiler.Compile("Draft.cui", source, cancelled.Token); }));
        Assert.True(valid.Succeeded); Assert.Single(valid.Document!.Components);
    }
}
