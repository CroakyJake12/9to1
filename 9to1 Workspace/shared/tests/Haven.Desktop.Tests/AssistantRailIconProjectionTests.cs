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
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
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
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.Available, result.State);
        Assert.Equal("MostUsed", result.Selection.Kind);
        Assert.Equal(a.Identity, result.Selection.Identity);
    }

    [Fact]
    public async Task None_configured_returns_generic_selection()
    {
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue())));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
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
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.NoneConfigured, result.State);
    }

    [Fact]
    public async Task Unavailable_catalogue_is_distinct_from_none()
    {
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(null)));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.Unavailable, result.State);
        Assert.Equal("Generic", result.Selection.Kind);
    }

    [Fact]
    public async Task Access_denial_is_distinct_from_none()
    {
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => throw new UnauthorizedAccessException("no access")));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.Unavailable, result.State);
    }

    [Fact]
    public async Task Usage_not_recorded_is_distinct_when_configured_but_no_usage_aggregate()
    {
        var a = Assistant("a");
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a))));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.UsageNotRecorded, result.State);
        Assert.Equal("Generic", result.Selection.Kind);
        Assert.Null(result.Selection.Identity);
    }

    [Fact]
    public async Task Running_read_fault_is_unavailable_and_never_downgraded_to_mostused()
    {
        var a = Assistant("a"); var b = Assistant("b");
        // Running read faults; a naive projection would swallow it as "no running" and then
        // select most-used despite being unable to establish running-first precedence.
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a, b)),
            running: (configured, _) => throw new UnauthorizedAccessException("no running access"),
            mostUsed: (configured, _) => Task.FromResult<AssistantDefinitionSnapshot?>(configured[0])));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.Unavailable, result.State);
        Assert.Equal("RunningReadFailed", result.Code);
        Assert.Equal("Generic", result.Selection.Kind);
        Assert.Null(result.Selection.Identity);
    }

    [Fact]
    public async Task Usage_read_fault_is_unavailable_distinct_from_usage_not_recorded()
    {
        var a = Assistant("a");
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a)),
            mostUsed: (configured, _) => throw new UnauthorizedAccessException("no usage access")));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.Unavailable, result.State);
        Assert.Equal("UsageReadFailed", result.Code);
        Assert.Null(result.Selection.Identity);
    }

    [Fact]
    public async Task Catalogue_cancellation_is_cancelled_distinct_from_none_or_cached_success()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => throw new OperationCanceledException(cts.Token)));
        var result = await projection.ResolveAsync(cts.Token);
        Assert.Equal(AssistantRailIconState.Cancelled, result.State);
        Assert.Equal("Cancelled", result.Code);
        Assert.Equal("Generic", result.Selection.Kind);
    }

    [Fact]
    public async Task Running_cancellation_is_cancelled_and_never_selects_mostused()
    {
        var a = Assistant("a"); var b = Assistant("b");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a, b)),
            running: (configured, _) => throw new OperationCanceledException(cts.Token),
            mostUsed: (configured, _) => Task.FromResult<AssistantDefinitionSnapshot?>(configured[0])));
        var result = await projection.ResolveAsync(cts.Token);
        Assert.Equal(AssistantRailIconState.Cancelled, result.State);
        Assert.Null(result.Selection.Identity);
    }

    [Fact]
    public async Task Stale_running_snapshot_identity_is_not_published()
    {
        var a = Assistant("a"); var b = Assistant("b");
        // Source returns a running snapshot that is NOT the current configured catalogue.
        var stale = Assistant("stale");
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a, b)),
            running: (configured, _) => Task.FromResult<AssistantDefinitionSnapshot?>(stale)));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.Unavailable, result.State);
        Assert.Equal("StaleRunningSnapshot", result.Code);
        Assert.Null(result.Selection.Identity);
    }

    [Fact]
    public async Task Stale_usage_snapshot_revision_is_not_published()
    {
        var a = Assistant("a");
        // Source returns a usage snapshot whose revision is not in the current catalogue.
        var staleRevision = new AssistantDefinitionSnapshot(a.Identity, a.Revision + 1, a.Kind, a.Configuration, a.Capabilities);
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a)),
            mostUsed: (configured, _) => Task.FromResult<AssistantDefinitionSnapshot?>(staleRevision)));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.Unavailable, result.State);
        Assert.Equal("StaleUsageSnapshot", result.Code);
        Assert.Null(result.Selection.Identity);
    }

    [Fact]
    public async Task Running_snapshot_is_published_only_within_current_configured_catalogue()
    {
        var a = Assistant("a"); var b = Assistant("b");
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a, b)),
            running: (configured, _) => Task.FromResult<AssistantDefinitionSnapshot?>(configured[1])));
        var result = await projection.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AssistantRailIconState.Available, result.State);
        Assert.Equal("Running", result.Selection.Kind);
        Assert.Equal(b.Identity, result.Selection.Identity);
        Assert.Equal(b.Revision, result.Selection.Revision);
    }

    [Fact]
    public async Task Ignored_cancellation_with_none_configured_is_cancelled_not_none()
    {
        // Source IGNORES cancellation and returns a truthful empty catalogue; the projection
        // must still honour the caller's cancellation rather than publish NoneConfigured.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue())));
        cts.Cancel();
        var result = await projection.ResolveAsync(cts.Token);
        Assert.Equal(AssistantRailIconState.Cancelled, result.State);
        Assert.Equal("Cancelled", result.Code);
        Assert.Equal("Generic", result.Selection.Kind);
    }

    [Fact]
    public async Task Ignored_cancellation_with_running_selection_is_cancelled_not_available()
    {
        var a = Assistant("a");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a)),
            running: (configured, _) => Task.FromResult<AssistantDefinitionSnapshot?>(configured[0])));
        cts.Cancel();
        var result = await projection.ResolveAsync(cts.Token);
        Assert.Equal(AssistantRailIconState.Cancelled, result.State);
        Assert.Equal("Cancelled", result.Code);
        Assert.Null(result.Selection.Identity);
    }

    [Fact]
    public async Task Ignored_cancellation_with_no_usage_is_cancelled_not_usage_not_recorded()
    {
        var a = Assistant("a");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var projection = new AssistantRailIconProjection(new FakeSource(
            _ => Task.FromResult<AssistantCatalogueObservation?>(Catalogue(a))));
        cts.Cancel();
        var result = await projection.ResolveAsync(cts.Token);
        Assert.Equal(AssistantRailIconState.Cancelled, result.State);
        Assert.Equal("Cancelled", result.Code);
        Assert.Equal("Generic", result.Selection.Kind);
    }
}
#endif
