using System.Text.Json;
using Haven.Application;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseOwnedDocumentRegistryTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_original_session_or_principal_replacement_during_observation_cannot_issue_resource_selection(bool actorChange)
    {
        using var browser = new BrowserSessionService(new Paths());
        var host = new Host { Hold = true }; browser.Attach(host);
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        var loading = registry.LoadForDisplayAsync(actors.Current, Document, Tool());
        await host.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (actorChange) actors.Current = actors.Current with { AuthenticationRevision = "replacement" };
        else browser.Attach(new Host());
        host.Release.TrySetResult();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => loading);
        Assert.Equal(0, host.LegacyCalls);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Resolver_reobserves_actual_tool_schema_and_denies_disposed_or_changed_original_declaration(bool dispose)
    {
        using var browser = new BrowserSessionService(new Paths()); var host = new Host(); browser.Attach(host);
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        using var selected = await registry.LoadForDisplayAsync(actors.Current, Document, Tool());
        var scope = selected.Scope;
        Assert.True((await registry.EvaluateAsync(actors.Current, BrowseOwnedDocumentRegistry.ActionID, scope, default)).Allowed);
        if (dispose) selected.Dispose(); else host.Schema = "{\"type\":\"object\",\"required\":[\"changed\"]}";
        Assert.False((await registry.EvaluateAsync(actors.Current, BrowseOwnedDocumentRegistry.ActionID, scope, default)).Allowed);
        Assert.Equal(0, host.LegacyCalls);
    }
    [Fact]
    public async Task Public_scope_ID_revision_and_access_cannot_manufacture_private_association()
    {
        using var browser = new BrowserSessionService(new Paths()); browser.Attach(new Host());
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        using var selected = await registry.LoadForDisplayAsync(actors.Current, Document, Tool());
        foreach (var proposed in new[] { selected.Scope with { Id = "foreign" }, selected.Scope with { Revision = "foreign" },
            selected.Scope with { Access = ResourceAccess.Read } })
            Assert.False((await registry.EvaluateAsync(actors.Current, BrowseOwnedDocumentRegistry.ActionID, proposed, default)).Allowed);
    }
    [Fact]
    public async Task Actual_Browser_owned_wrapper_unwraps_encoded_native_string_before_registry_observation()
    {
        using var browser = new BrowserSessionService(new Paths());
        var host = new Host { EncodedResult = true }; browser.Attach(host);
        var actors = new Actors(); var registry = new BrowseOwnedDocumentRegistry(browser, actors);
        using var selected = await registry.LoadForDisplayAsync(actors.Current, Document, Tool());
        Assert.True((await registry.EvaluateAsync(actors.Current, BrowseOwnedDocumentRegistry.ActionID,
            selected.Scope, default)).Allowed);
        Assert.Equal(0, host.LegacyCalls);
    }
    private static readonly WebMcpDocument Document = new("https://example.test", "actual-document", "controlled-browser",
        "navigator.modelContextTesting", true, false);
    private static WebMcpTool Tool()
    {
        using var schema = JsonDocument.Parse("{\"type\":\"object\"}");
        return new("actual-tool", "Untrusted description", schema.RootElement.Clone());
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current { get; set; } = new("actor", "profile", null, null, "original");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    // Controlled actual BrowserSession host boundary, not installed native engine or Home grant.
    private sealed class Host : IOriginalSessionOwnedBrowserHost, IBrowserNativeEntryObservation
    {
        private long _documentGeneration;
        private sealed record NativeDocument(Host Issuer, long Generation) : IBrowserNativeDocumentSelection;
        public IBrowserNativeDocumentSelection CaptureDocumentSelection() => new NativeDocument(this, _documentGeneration);
        private void RequireDocument(IBrowserNativeDocumentSelection originalDocument)
        {
            if (originalDocument is not NativeDocument selected || !ReferenceEquals(selected.Issuer, this) || selected.Generation != _documentGeneration)
                throw new UnauthorizedAccessException("Original controlled native document changed.");
        }
        public bool Hold { get; set; }
        public bool EncodedResult { get; set; }
        public string Schema { get; set; } = "{\"type\":\"object\"}";
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
            Entered.TrySetResult(); if (Hold) await Release.Task.WaitAsync(token);
            using var schema = JsonDocument.Parse(Schema);
            var observed = JsonSerializer.Serialize(new { Document, ToolName = "actual-tool", InputSchema = schema.RootElement });
            if (!await lease.CheckAsync(token)) throw new UnauthorizedAccessException();
            RequireDocument(originalDocument);
            RequireDocument(originalDocument);
            return EncodedResult ? JsonSerializer.Serialize(observed) : observed;
        }
        public Task<string?> EvaluateObservationAsync(string script, CancellationToken token) => throw new InvalidOperationException("Resolver never requests nested observation.");
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
        public string DataDirectory => "/unused";
        public string DatabasePath => "/unused/database";
        public string BrowserProfileDirectory => "/unused/browser";
        public string AttachmentsDirectory => "/unused/attachments";
        public string LogsDirectory => "/unused/logs";
        public string LegacyStatePath => "/unused/legacy";
    }
}
