using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseOwnedCatalogueTests
{
    [Fact]
    public async Task Actual_original_discovery_host_cannot_be_replaced_by_colliding_document_and_tool_on_click()
    {
        using var browser = new BrowserSessionService(new Paths()); var original = new Host(); browser.Attach(original);
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        var discovered = await registry.DiscoverForDisplayAsync(actors.Actor); using var catalogue = discovered.Catalogue;
        Assert.Single(discovered.Tools);
        var replacement = new Host(); browser.Attach(replacement); // Identical reported document/tool/schema, different actual host.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.SelectDisplayedToolAsync(catalogue, "actual-tool"));
        Assert.Equal(0, replacement.Reads); Assert.Equal(0, replacement.LegacyCalls);
    }
    [Fact]
    public async Task Closing_actual_discovery_catalogue_revokes_all_derived_tool_selections()
    {
        using var browser = new BrowserSessionService(new Paths()); browser.Attach(new Host());
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        var discovered = await registry.DiscoverForDisplayAsync(actors.Actor);
        using var selected = await registry.SelectDisplayedToolAsync(discovered.Catalogue, "actual-tool");
        Assert.True((await registry.EvaluateAsync(actors.Actor, BrowseOwnedDocumentRegistry.ActionID, selected.Scope, default)).Allowed);
        discovered.Catalogue.Dispose();
        Assert.False((await registry.EvaluateAsync(actors.Actor, BrowseOwnedDocumentRegistry.ActionID, selected.Scope, default)).Allowed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => registry.SelectDisplayedToolAsync(discovered.Catalogue, "actual-tool"));
    }
    [Fact]
    public async Task Original_host_change_while_actual_discovery_read_suspended_never_returns_adopted_catalogue()
    {
        using var browser = new BrowserSessionService(new Paths()); var host = new Host { Hold = true }; browser.Attach(host);
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        var discovery = registry.DiscoverForDisplayAsync(actors.Actor);
        await host.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); browser.Attach(new Host()); host.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => discovery);
        Assert.Equal(0, host.LegacyCalls);
    }
    [Fact]
    public async Task Same_attached_host_navigation_with_colliding_page_metadata_cannot_adopt_displayed_catalogue()
    {
        using var browser = new BrowserSessionService(new Paths()); var host = new Host(); browser.Attach(host);
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        var displayed = await registry.DiscoverForDisplayAsync(actors.Actor); using var catalogue = displayed.Catalogue;
        Assert.Single(displayed.Tools); var reads = host.Reads;
        // Same host and exact same document/tool/schema observations; only trusted native epoch differs.
        await host.NavigateAsync(new Uri("https://example.test/replacement"), default);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.SelectDisplayedToolAsync(catalogue, "actual-tool"));
        Assert.Equal(reads, host.Reads); Assert.Equal(0, host.LegacyCalls);
        // New genuine display can observe the replacement, without reviving the prior catalogue.
        var current = await registry.DiscoverForDisplayAsync(actors.Actor); using var currentCatalogue = current.Catalogue;
        using var selected = await registry.SelectDisplayedToolAsync(currentCatalogue, "actual-tool");
        Assert.True((await registry.EvaluateAsync(actors.Actor, BrowseOwnedDocumentRegistry.ActionID, selected.Scope, default)).Allowed);
    }
    [Fact]
    public async Task Same_host_navigation_while_discovery_observation_is_held_cannot_publish_a_replacement_catalogue()
    {
        using var browser = new BrowserSessionService(new Paths()); var host = new Host { Hold = true }; browser.Attach(host);
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        var discovery = registry.DiscoverForDisplayAsync(actors.Actor);
        try
        {
            await host.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await host.NavigateAsync(new Uri("https://example.test/replacement"), default);
        }
        finally { host.Release.TrySetResult(); }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => discovery);
        Assert.Equal(0, host.LegacyCalls);
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Actor { get; } = new("actor", "profile", null, null, "actual-controlled-session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor);
    }
    // Actual BrowserSession/selection boundary with controlled native observations, never a permission grant.
    private sealed class Host : IOriginalSessionOwnedBrowserHost, IBrowserNativeEntryObservation
    {
        private long _documentGeneration;
        private sealed record Document(Host Issuer, long Generation) : IBrowserNativeDocumentSelection;
        public IBrowserNativeDocumentSelection CaptureDocumentSelection() => new Document(this, _documentGeneration);
        private void RequireDocument(IBrowserNativeDocumentSelection selected)
        {
            if (selected is not Document document || !ReferenceEquals(document.Issuer, this) || document.Generation != _documentGeneration)
                throw new UnauthorizedAccessException("Original native document changed.");
        }
        public bool Hold { get; set; }
        public int Reads { get; private set; }
        public int LegacyCalls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BrowserSnapshot State { get; } = new(null, "controlled", false, false, false, "controlled");
        public event EventHandler<BrowserSnapshot>? StateChanged { add { } remove { } }
        public async Task<string?> ExecuteOwnedScriptAsync(IBrowserNativeDocumentSelection originalDocument, string script, IBrowserOwnedScriptDispatchAdmission admission, CancellationToken token)
        {
            RequireDocument(originalDocument);
            await using var lease = await admission.AcquireAsync(this, token) ?? throw new UnauthorizedAccessException();
            if (!await lease.CheckAsync(token)) throw new UnauthorizedAccessException();
            RequireDocument(originalDocument);
            Reads++; Entered.TrySetResult(); if (Hold) await Release.Task.WaitAsync(token);
            var document = new WebMcpDocument("https://example.test", "same-document", "controlled-browser", "navigator.modelContextTesting", true, false);
            var schema = JsonSerializer.SerializeToElement(new { type = "object" });
            var result = script.Contains("Tools:supported", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { Document = document, Tools = new[] { new { name = "actual-tool", description = "untrusted", inputSchema = schema } } })
                : JsonSerializer.Serialize(new { Document = document, ToolName = "actual-tool", InputSchema = schema });
            if (!await lease.CheckAsync(token)) throw new UnauthorizedAccessException();
            RequireDocument(originalDocument);
            return result;
        }
        public Task<string?> EvaluateObservationAsync(string script, CancellationToken token) => throw new InvalidOperationException("No nested observation.");
        public Task<string?> ExecuteScriptGuardedAsync(string script, IBrowserScriptDispatchAdmission admission, CancellationToken token) => throw new NotSupportedException();
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken token) { LegacyCalls++; throw new InvalidOperationException("No legacy fallback."); }
        public Task NavigateAsync(Uri address, CancellationToken token) { _documentGeneration++; return Task.CompletedTask; }
        public Task GoBackAsync(CancellationToken token) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken token) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public Task OpenDeveloperToolsAsync(CancellationToken token) => Task.CompletedTask;
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory => "/unused"; public string DatabasePath => "/unused/database";
        public string BrowserProfileDirectory => "/unused/browser"; public string AttachmentsDirectory => "/unused/attachments";
        public string LogsDirectory => "/unused/logs"; public string LegacyStatePath => "/unused/legacy";
    }
}
