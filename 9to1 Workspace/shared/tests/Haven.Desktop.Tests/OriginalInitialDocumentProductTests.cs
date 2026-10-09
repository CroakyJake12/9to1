using System.Reflection;

namespace Haven.Desktop.Tests;

public sealed class OriginalInitialDocumentProductTests
{
    [Theory]
    [InlineData("write")]
    [InlineData("present")]
    [InlineData("files")]
    [InlineData("sites")]
    [InlineData("dev")]
    public void One_supported_compiled_route_is_preserved(string product) =>
        Assert.Equal(product, App.ValidateOriginalInitialProductDeclaration([new("9to1.InitialApp", product)]));

    [Fact]
    public void Missing_declaration_keeps_the_original_generic_startup() =>
        Assert.Null(App.ValidateOriginalInitialProductDeclaration([new("unrelated", "write")]));

    [Fact]
    public void Duplicate_declarations_cannot_choose_a_product() =>
        Assert.Throws<InvalidDataException>(() => App.ValidateOriginalInitialProductDeclaration(
            [new("9to1.InitialApp", "write"), new("9to1.InitialApp", "present")]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Write")]
    [InlineData("unknown")]
    public void Unsupported_declarations_cannot_enter_a_document_route(string? value) =>
        Assert.Throws<InvalidDataException>(() => App.ValidateOriginalInitialProductDeclaration([new("9to1.InitialApp", value)]));
}
