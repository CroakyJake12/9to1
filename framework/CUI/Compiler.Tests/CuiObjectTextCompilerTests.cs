using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiObjectTextCompilerTests
{
    private static CuiCompilerOptions Options() => new(
        new CuiCompilationMetadata("1.0", "1.0", "1.0", ["layout"]));

    [Fact]
    public void Actual_assistant_message_renderer_binding_compiles_without_invoking_the_factory()
    {
        var registry = new CuiControlRegistry();
        var factoryCalls = 0;
        registry.RegisterObjectRenderer("AssistantMessage", _ =>
        {
            factoryCalls++;
            return new CuiMarkdownView();
        });

        var result = new CuiCompiler(registry).Compile(
            "<Page><Object id=\"message\" Type=\"AssistantMessage\" Text=\"{Binding message.Content}\" /></Page>",
            "assistant-message.cui", Options());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
        var message = Assert.Single(Assert.Single(result.Output.Components).Children);
        Assert.Equal("Object", message.Type);
        Assert.Equal("message.Content", Assert.IsType<CuiBindingValue>(message.Properties["Text"].Value).Path);
        Assert.Equal(0, factoryCalls);
        Assert.True(registry.TryResolveElement("Object", out var element));
        Assert.Contains("Text", element.AllowedProperties);
        Assert.True(registry.TryGetProperty("Text", out var property));
        Assert.Contains("Object", property.SupportedElementTypes);
    }

    [Fact]
    public void Object_text_keeps_unknown_property_and_media_property_refusals()
    {
        var registry = new CuiControlRegistry();
        var compiler = new CuiCompiler(registry);
        var unknown = compiler.Compile(
            "<Page><Object Type=\"AssistantMessage\" Texxt=\"{Binding message.Content}\" /></Page>",
            "unknown-property.cui", Options());
        Assert.False(unknown.Succeeded);
        Assert.Contains(unknown.Diagnostics, diagnostic => diagnostic.Code == "CUIC012");

        foreach (var elementName in new[] { "Image", "Audio", "Video" })
        {
            var misplaced = compiler.Compile(
                $"<Page><{elementName} Text=\"{Binding message.Content}\" /></Page>",
                "misplaced-text.cui", Options());
            Assert.False(misplaced.Succeeded);
            Assert.Contains(misplaced.Diagnostics, diagnostic => diagnostic.Code == "CUIC013");
            Assert.True(registry.TryResolveElement(elementName, out var element));
            Assert.DoesNotContain("Text", element.AllowedProperties);
        }
    }
}
