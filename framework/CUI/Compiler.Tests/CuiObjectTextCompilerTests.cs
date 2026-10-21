using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Compiler.Tests;

public sealed class CuiObjectTextCompilerTests
{
    private static CuiCompilerOptions Options() => new(
        new CuiCompilationMetadata("1.0", "1.0", "2.0", ["layout"]));

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
                $"<Page><{elementName} Text=\"{{Binding message.Content}}\" /></Page>",
                "misplaced-text.cui", Options());
            Assert.False(misplaced.Succeeded);
            Assert.Contains(misplaced.Diagnostics, diagnostic => diagnostic.Code == "CUIC013");
            Assert.True(registry.TryResolveElement(elementName, out var element));
            Assert.DoesNotContain("Text", element.AllowedProperties);
        }
    }

    [Fact]
    public void Genuine_default_compiler_accepts_neutral_readonly_source_contract_with_authored_monospace_font()
    {
        // No injected registry: the genuine default compiler/registry serve the shared readonly
        // source contract, so an authored FontFamily=monospace on a neutral ReadOnlyText Object
        // compiles (the exact pattern the Build workbench ships). This is not a test-only registry.
        var result = new CuiCompiler().Compile(
            "<Page><Object type=\"ReadOnlyText\" id=\"readonly\" text=\"{Binding BeforeText}\" font-family=\"monospace\" min-height=\"100\" accessible-name=\"Exact source before this pass\" /></Page>",
            "readonly-source.cui", Options());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.NotNull(result.Output);
        var node = Assert.Single(Assert.Single(result.Output.Components).Children);
        Assert.Equal("Object", node.Type);
        Assert.Contains(node.Properties, pair => pair.Key.Equals("Type", StringComparison.OrdinalIgnoreCase)
            && pair.Value.Value is CuiLiteralValue literal && literal.Value == "ReadOnlyText");
        Assert.Equal("monospace", Assert.IsType<CuiLiteralValue>(node.Properties["font-family"].Value).Value);
        Assert.Contains(node.Properties, pair => pair.Value.Value is CuiBindingValue bound && bound.Path == "BeforeText");
    }

    [Fact]
    public void Genuine_default_registry_admits_only_the_declared_neutral_readonly_property_contract()
    {
        var registry = CuiControlRegistry.Default;
        Assert.True(registry.IsRegisteredRuntimeType("ReadOnlyText"));
        Assert.True(registry.TryResolveElement("Object", out var element));
        Assert.True(registry.TryGetProperty("FontFamily", out var font));
        Assert.True(registry.TryGetProperty("Text", out var text));
        // The declared text + typography contract is admitted on the neutral readonly source…
        Assert.True(registry.SupportsProperty(element, "ReadOnlyText", font));
        Assert.True(registry.SupportsProperty(element, "ReadOnlyText", text));
        // …but an unknown object type is still refused (no global allow on every Object).
        Assert.False(registry.SupportsProperty(element, "UnregisteredReadonly", font));
    }

    [Fact]
    public void Default_compiler_still_refuses_unknown_property_on_neutral_readonly_source()
    {
        var result = new CuiCompiler().Compile(
            "<Page><Object type=\"ReadOnlyText\" texxt=\"{Binding BeforeText}\" /></Page>",
            "unknown-property.cui", Options());
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC012");
    }

    [Fact]
    public void Default_compiler_still_refuses_font_on_unregistered_object_type()
    {
        var result = new CuiCompiler().Compile(
            "<Page><Object type=\"UnregisteredReadonly\" font-family=\"monospace\" /></Page>",
            "unregistered-object.cui", Options());
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CUIC013");
    }
}
