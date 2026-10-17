using System.Text.Json.Nodes;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class UnifiedWorkspaceTabStateTests
{
    [Fact]
    public void Existing_document_state_and_rich_tab_metadata_survive_content_projection()
    {
        var original = Legacy("chat", """
            {"ConversationId":"acacacac-acac-acac-acac-acacacacacac",
             "extension":{"version":7,"state":[1,null,"unchanged"]}}
            """);
        Assert.True(UnifiedWorkspaceTabState.TryProject(original, "current-profile", out var projection));
        Assert.Equal("9to1://spaces/chat/acacacac-acac-acac-acac-acacacacacac", projection!.Address);
        var saved = UnifiedWorkspaceTabState.Merge(original, projection with { Title = "Renamed" });

        Assert.Equal(original.Id, saved.Id);
        Assert.Equal("spaces", saved.AppKey);
        Assert.Equal("Renamed", saved.Title);
        Assert.Equal(original.NavigationJson, saved.NavigationJson);
        Assert.Equal(original.ScrollOffset, saved.ScrollOffset);
        Assert.Equal(original.GroupId, saved.GroupId);
        Assert.Equal(original.IsPinned, saved.IsPinned);
        Assert.Equal(original.IsProtected, saved.IsProtected);
        Assert.Equal(original.CreatedAt, saved.CreatedAt);
        var before = JsonNode.Parse(original.StateJson)!;
        var after = JsonNode.Parse(saved.StateJson)!;
        Assert.True(JsonNode.DeepEquals(before["extension"], after["extension"]));
        Assert.True(JsonNode.DeepEquals(before["ConversationId"], after["ConversationId"]));
        Assert.True(UnifiedWorkspaceTabState.TryProject(saved, "different-profile", out var reopened));
        Assert.Equal("current-profile", reopened!.ProfileId);
        Assert.Equal(projection.Address, reopened.Address);
    }

    [Theory]
    [InlineData("home", "browse://home")]
    [InlineData("go", "browse://newtab")]
    [InlineData("assistants", "9to1://spaces/assistants")]
    [InlineData("CHAT", "9to1://spaces/chat")]
    [InlineData("files", "9to1://files")]
    public void Legacy_routes_keep_identity_and_use_current_namespaces(string appKey, string address)
    {
        var original = Legacy(appKey, "{}");
        Assert.True(UnifiedWorkspaceTabState.TryProject(original, "profile", out var projected));
        Assert.Equal(original.Id, projected!.Id);
        Assert.Equal(address, projected.Address);
        Assert.Equal(original.GroupId!.Value.ToString("D"), projected.Group);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{invalid")]
    [InlineData("{\"9to1.AppContent\":null}")]
    [InlineData("{\"9to1.AppContent\":{\"Version\":2,\"Address\":\"9to1://files\"}}")]
    [InlineData("{\"9to1.AppContent\":{\"Version\":1,\"Address\":\"9to1://files/../secret\"}}")]
    [InlineData("{\"9to1.AppContent\":{\"Version\":1,\"Address\":\"https://example.org\",\"ProfileId\":17}}")]
    public void Unsupported_state_remains_unprojected_instead_of_falling_back_to_legacy(string state)
    {
        var original = Legacy("home", state);
        Assert.False(UnifiedWorkspaceTabState.TryProject(original, "profile", out var projected));
        Assert.Null(projected);
        Assert.Equal(state, original.StateJson);
        Assert.ThrowsAny<Exception>(() => UnifiedWorkspaceTabState.Merge(original,
            Browser(original.Id, "browse://home")));
    }

    [Fact]
    public void Existing_empty_web_profile_round_trips_and_resolves_against_current_host()
    {
        var web = Browser(Guid.NewGuid(), "https://example.org/a");
        var saved = UnifiedWorkspaceTabState.Merge(null, web);
        Assert.True(UnifiedWorkspaceTabState.TryProject(saved, "host-profile", out var restored));
        Assert.Equal("host-profile", restored!.ProfileId);
        Assert.Equal(web.Address, restored.Address);
        Assert.False(saved.IsProtected);
    }

    [Fact]
    public void Private_and_wrong_identity_projections_cannot_change_a_canonical_row()
    {
        var original = Legacy("home", "{}");
        Assert.Throws<InvalidOperationException>(() => UnifiedWorkspaceTabState.Merge(original,
            Browser(original.Id, "https://example.org") with { Privacy = BrowserTabPrivacy.Private }));
        Assert.Throws<InvalidOperationException>(() => UnifiedWorkspaceTabState.Merge(original,
            Browser(Guid.NewGuid(), "browse://home")));
        Assert.Throws<InvalidOperationException>(() => UnifiedWorkspaceTabState.Merge(null,
            Browser(Guid.Empty, "browse://home")));
        Assert.Equal("{}", original.StateJson);
    }

    [Fact]
    public void Group_names_must_be_resolved_by_workspace_instead_of_silently_losing_membership()
    {
        Assert.Throws<InvalidOperationException>(() => UnifiedWorkspaceTabState.Merge(null,
            Browser(Guid.NewGuid(), "https://example.org") with { Group = "Research" }));
    }

    private static TabSessionSnapshot Legacy(string appKey, string state) => new(
        Guid.NewGuid(), appKey, "Original", state, "{\"history\":[1,2]}", 123.5,
        Guid.NewGuid(), true, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(1));

    private static BrowserTabState Browser(Guid id, string address) => new(
        id, "Title", address, BrowserTabPrivacy.Standard, string.Empty, DateTimeOffset.UnixEpoch.AddDays(1));
}
