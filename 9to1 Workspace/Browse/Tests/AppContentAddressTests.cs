using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class AppContentAddressTests
{
    [Fact]
    public void Native_resource_routes_preserve_real_namespace_and_opaque_resource_identity()
    {
        const string resource = "8ea449db-8b22-43a5-9f61-73dc3e011b4b";
        var address = AppContentAddress.Parse("9to1://Write/" + resource);
        Assert.Equal(AppContentNamespace.NativeApp, address.Namespace);
        Assert.Equal("write", address.Authority);
        Assert.Equal(resource, Assert.Single(address.ResourceSegments));
        Assert.Equal("9to1://write/" + resource, address.Value);
        Assert.Null(address.WebUri);
        Assert.False(address.IsWeb);
        Assert.Equal(address, AppContentAddress.Parse(address.Value));
        Assert.Equal(address.GetHashCode(), AppContentAddress.Parse(address.Value).GetHashCode());
    }

    [Theory]
    [InlineData("https://example.com/a?q=chat#result", AppContentNamespace.Web)]
    [InlineData("http://example.com/", AppContentNamespace.Web)]
    [InlineData("browse://newtab", AppContentNamespace.Browse)]
    [InlineData("BROWSE://History/", AppContentNamespace.Browse)]
    [InlineData("cui://example.extension/settings", AppContentNamespace.Cui)]
    [InlineData("9to1://Spaces", AppContentNamespace.NativeApp)]
    public void Namespace_classification_never_confers_another_namespace(string text, AppContentNamespace expected)
    {
        var address = AppContentAddress.Parse(text);
        Assert.Equal(expected, address.Namespace);
        Assert.Equal(expected == AppContentNamespace.Web, address.WebUri is not null);
        Assert.Equal(expected != AppContentNamespace.Web, AppContentAddress.IsReserved(text));
    }

    [Theory]
    [InlineData("9to1://write/../secrets")]
    [InlineData("9to1://write/%2e%2e/secrets")]
    [InlineData("9to1://write/%252e%252e/secrets")]
    [InlineData("9to1://write/a%2fb")]
    [InlineData("9to1://user@write/a")]
    [InlineData("9to1://write:80/a")]
    [InlineData("9to1://write/a?actor=owner")]
    [InlineData("9to1://write/a#other")]
    [InlineData("9to1://write//resource")]
    [InlineData("9to1://write/\\resource")]
    [InlineData("9to1://write/line\nfeed")]
    [InlineData("browse://settings@evil.example")]
    [InlineData("cui://extension/https://example.com")]
    [InlineData("9to1:write")]
    public void Malformed_reserved_routes_remain_reserved_and_cannot_fall_through_to_search(string text)
    {
        Assert.False(AppContentAddress.TryParse(text, out var address));
        Assert.Null(address);
        Assert.True(AppContentAddress.IsReserved(text));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("ordinary search")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://username:password@example.com/")]
    public void Non_addresses_and_other_schemes_do_not_enter_content_namespace(string text) =>
        Assert.False(AppContentAddress.TryParse(text, out _));

    [Fact]
    public void Parser_does_not_authorize_unregistered_apps_or_extensions()
    {
        // Admission is deliberately a later registry/permission decision.
        Assert.Equal("missing-app", AppContentAddress.Parse("9to1://missing-app").Authority);
        Assert.Equal(AppContentNamespace.Cui, AppContentAddress.Parse("cui://write").Namespace);
        Assert.NotEqual(AppContentAddress.Parse("cui://write"), AppContentAddress.Parse("9to1://write"));
    }
}
