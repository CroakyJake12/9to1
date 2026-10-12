using CakeOS.Cui.Runtime;
using Xunit;

public sealed class CuiDocumentSourceTests
{
    [Fact]
    public void DeferredImmutableMarkupIsValidatedBeforeDisplayAndReused()
    {
        var source = new CuiDocumentSource("<Cui><Page><Text Text=\"Hello\" /></Page></Cui>", "editor.cui", true);
        Assert.False(source.IsLoaded);
        var document = source.Document;
        Assert.True(source.IsLoaded);
        Assert.Same(document, source.Document);
    }

    [Fact]
    public void InvalidOptionalSurfaceCannotBypassValidation()
    {
        Assert.Throws<InvalidDataException>(() => new CuiDocumentSource("<Cui><Page>", "broken.cui"));
        var source = new CuiDocumentSource("<Cui><Page>", "broken.cui", true);
        Assert.False(source.IsLoaded);
        Assert.Throws<InvalidDataException>(() => source.Document);
        Assert.Throws<InvalidDataException>(() => source.Document);
    }
}
