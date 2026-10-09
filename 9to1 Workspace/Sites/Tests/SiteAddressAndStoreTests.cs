using System.Text;
using System.Text.Json.Nodes;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.Domain;
using HavenOS.Apps.Sites.Infrastructure;
using Xunit;

namespace HavenOS.Apps.Sites.Tests;

public sealed class SiteAddressAndStoreTests
{
    [Theory]
    [InlineData("studio", "studio")]
    [InlineData("my-site", "my-site")]
    public void NormalizeSlug_accepts_only_canonical_lowercase_names(string input, string expected) =>
        Assert.Equal(expected, SiteAddressRules.NormalizeSlug(input));

    [Theory]
    [InlineData("MySite")]
    [InlineData("my--site")]
    [InlineData("-site")]
    [InlineData("site-")]
    [InlineData("a1")]
    [InlineData("admin")]
    public void NormalizeSlug_rejects_invalid_or_reserved_names(string input) =>
        Assert.Throws<SiteOperationException>(() => SiteAddressRules.NormalizeSlug(input));

    [Fact]
    public void NormalizeSlug_rejects_names_longer_than_one_dns_label() =>
        Assert.Throws<SiteOperationException>(() => SiteAddressRules.NormalizeSlug(new string('a', 64)));

    [Fact]
    public void NormalizeRoutePath_preserves_segments_and_supports_parameters()
    {
        var segments = SiteAddressRules.NormalizeRoutePath("/blog/:post-id");

        Assert.Equal(new[] { "blog", ":post-id" }, segments);
    }

    [Fact]
    public void NormalizeHostname_canonicalizes_idn_to_ascii()
    {
        var normalized = SiteAddressRules.NormalizeHostname("bücher.example");

        Assert.Equal("xn--bcher-kva.example", normalized.AsciiName);
        Assert.Equal("bücher.example", normalized.DisplayName);
    }

    [Fact]
    public void NormalizeHostname_rejects_single_label_names() =>
        Assert.Throws<SiteOperationException>(() => SiteAddressRules.NormalizeHostname("localhost"));

    [Fact]
    public async Task Concurrent_slug_reservations_cannot_claim_the_same_name()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileSiteWorkspaceStore(directory.Path);
        var first = ReserveAsync(store, Guid.NewGuid());
        var second = ReserveAsync(store, Guid.NewGuid());
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, outcome => outcome);
        var saved = await store.ReadAsync(state => state.SlugReservations);
        Assert.Single(saved);
        Assert.Equal("studio", saved[0].Slug);
    }

    [Fact]
    public async Task Unsupported_schema_is_rejected_without_rewriting_the_index()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, ".9to1-sites-index.json");
        var original = Encoding.UTF8.GetBytes("{\"schemaVersion\":999,\"projects\":[],\"deployments\":[],\"domains\":[],\"nameVerifications\":[],\"domainChallenges\":[],\"slugReservations\":[]}");
        await File.WriteAllBytesAsync(path, original);
        var store = new FileSiteWorkspaceStore(directory.Path);

        await Assert.ThrowsAsync<SiteOperationException>(() => store.ReadAsync(state => state));

        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Theory]
    [InlineData("projects")]
    [InlineData("deployments")]
    [InlineData("domains")]
    [InlineData("nameVerifications")]
    [InlineData("domainChallenges")]
    [InlineData("slugReservations")]
    public async Task Null_index_collection_is_rejected_without_rewriting_the_index(string collection)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, ".9to1-sites-index.json");
        var index = JsonNode.Parse("{\"schemaVersion\":1,\"projects\":[],\"deployments\":[],\"domains\":[],\"nameVerifications\":[],\"domainChallenges\":[],\"slugReservations\":[]}")!;
        index[collection] = null;
        var original = Encoding.UTF8.GetBytes(index.ToJsonString());
        await File.WriteAllBytesAsync(path, original);
        var store = new FileSiteWorkspaceStore(directory.Path);

        var failure = await Assert.ThrowsAsync<SiteOperationException>(() => store.ReadAsync(state => state));

        Assert.Equal("SitesMetadataInvalid", failure.Error.Code);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Unsupported_project_schema_is_rejected_before_an_unrelated_mutation()
    {
        using var directory = new TemporaryDirectory();
        var store = new FileSiteWorkspaceStore(directory.Path);
        var created = await new SiteProjectService(store).CreateProjectAsync(new(Guid.NewGuid(), null, null,
            "source-1", "Preserved project", "9to1-native", "preserved"));
        Assert.True(created.IsSuccess);
        var path = Path.Combine(directory.Path, ".9to1-sites-index.json");
        var index = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        index["projects"]![0]!["schemaVersion"] = 999;
        var original = Encoding.UTF8.GetBytes(index.ToJsonString());
        await File.WriteAllBytesAsync(path, original);
        var mutationReached = false;

        var failure = await Assert.ThrowsAsync<SiteOperationException>(() => store.MutateAsync(state =>
        {
            mutationReached = true;
            return (state, true);
        }));

        Assert.Equal("SitesSchemaUnsupported", failure.Error.Code);
        Assert.False(mutationReached);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    private static async Task<bool> ReserveAsync(FileSiteWorkspaceStore store, Guid siteId)
    {
        try
        {
            await store.MutateAsync(state =>
            {
                var reservation = new SiteSlugReservation("studio", siteId, SiteNameVerificationState.Passed, DateTimeOffset.UtcNow, null);
                return (state with { SlugReservations = [.. state.SlugReservations, reservation] }, true);
            });
            return true;
        }
        catch (SiteOperationException)
        {
            return false;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sites-tests-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
