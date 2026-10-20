#if !ANDROID
using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class AssistantRailIconProjectionTests
{
    private static AssistantDefinitionSnapshot Assistant(string id, string icon = "avares://Haven/Assets/assistant.svg",
        bool enabled = true, bool archived = false, ConfiguredIdentityKind kind = ConfiguredIdentityKind.Assistant) =>
        new(new AssistantIdentity("den-" + id, "personal", "def-" + id), 3, kind,
            new AssistantConfiguration { Name = "Assistant " + id, IconResourceId = icon, Enabled = enabled, Archived = archived }, []);

    private static AssistantCatalogueObservation Catalogue(params AssistantDefinitionSnapshot[] definitions) =>
        new(definitions, []);

    private sealed class FakeSource(
        Func<CancellationToken, Task<AssistantCatalogueObservation?>> catalogue,
        Func<IReadOnlyList<AssistantDefinitionSnapshot>, CancellationToken, Task<AssistantDefinitionSnapshot?>>? running = null,
        Func<IReadOnlyList<AssistantDefinitionSnapshot>, CancellationToken, Task<AssistantDefinitionSnapshot?>>? mostUsed = null)
        : IAssistantRailIconSource
    {
        public Task<AssistantCatalogueObservation?> ReadConfiguredCatalogueAsync(CancellationToken token) => catalogue(token);
        public Task<AssistantDefinitionSnapshot?> ReadCurrentRunningAsync(IReadOnlyList<AssistantDefinitionSnapshot> configured, CancellationToken token)
            => running is null ? Task.FromResult<AssistantDefinitionSnapshot?>(null) : running(configured, token);
        public Task<AssistantDefinitionSnapshot?> ReadMostUsedAsync(IReadOnlyList<AssistantDefinitionSnapshot> configured, CancellationToken token)
            => mostUsed is null ? Task.FromResult<AssistantDefinitionSnapshot?>(null) : mostUsed(configured, token);
    }

    [Fact]
    public async Task Running_assistant_is_chosen_first()
    {
        var a = Assistant("a"); var b = Assistant("b");
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a, b)),
            running: (configured, _) => Task.FromResult<AssistantDefinitionSnapshot?>(configured[1])));
        var result = await projection.ResolveAsync();
        Assert.Equal(AssistantRailIconState.Available, result.State);
        Assert.Equal("Running", result.Selection.Kind);
        Assert.Equal(b.Identity, result.Selection.Identity);
        Assert.Equal(b.Revision, result.Selection.Revision);
        Assert.Equal(b.Configuration.IconResourceId, result.Selection.IconResourceId);
    }

    [Fact]
    public async Task Most_used_is_chosen_when_nothing_runs()
    {
        var a = Assistant("a"); var b = Assistant("b");
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a, b)),
            mostUsed: (configured, _) => Task.FromResult<AssistantDefinitionSnapshot?>(configured[0])));
        var result = await projection.ResolveAsync();
        Assert.Equal(AssistantRailIconState.Available, result.State);
        Assert.Equal("MostUsed", result.Selection.Kind);
        Assert.Equal(a.Identity, result.Selection.Identity);
    }

    [Fact]
    public async Task None_configured_returns_generic_selection()
    {
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue())));
        var result = await projection.ResolveAsync();
        Assert.Equal(AssistantRailIconState.NoneConfigured, result.State);
        Assert.Equal("Generic", result.Selection.Kind);
        Assert.Null(result.Selection.Identity);
    }

    [Fact]
    public async Task Disabled_or_archived_assistants_are_not_configured()
    {
        var disabled = Assistant("disabled", enabled: false);
        var archived = Assistant("archived", archived: true);
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(disabled, archived))));
        var result = await projection.ResolveAsync();
        Assert.Equal(AssistantRailIconState.NoneConfigured, result.State);
    }

    [Fact]
    public async Task Unavailable_catalogue_is_distinct_from_none()
    {
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(null)));
        var result = await projection.ResolveAsync();
        Assert.Equal(AssistantRailIconState.Unavailable, result.State);
        Assert.Equal("Generic", result.Selection.Kind);
    }

    [Fact]
    public async Task Access_denial_is_distinct_from_none()
    {
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => throw new UnauthorizedAccessException("no access")));
        var result = await projection.ResolveAsync();
        Assert.Equal(AssistantRailIconState.Unavailable, result.State);
    }

    [Fact]
    public async Task Usage_not_recorded_is_distinct_when_configured_but_no_usage_aggregate()
    {
        var a = Assistant("a");
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a))));
        var result = await projection.ResolveAsync();
        Assert.Equal(AssistantRailIconState.UsageNotRecorded, result.State);
        Assert.Equal("Generic", result.Selection.Kind);
        Assert.Null(result.Selection.Identity);
    }
}
#endif
