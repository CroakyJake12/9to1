using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Browser;

namespace HavenOS.Apps.Browse;

public interface IBrowseOwnedDocumentSelection : IDisposable
{
    ResourceScope Scope { get; }
}

/// <summary>Runtime associations with actual original browser host selections. IDs are
/// lookup proposals, never storage UUIDs or permissions. This resolver has no Home dependency.</summary>
public sealed partial class BrowseOwnedDocumentRegistry(BrowserSessionService browser,
    IAuthenticatedResourceActorSource actors) : ICanonicalResourceAccessResolver
{
    private readonly BrowserSessionService _browser = browser;
    private readonly IAuthenticatedResourceActorSource _actors = actors;
    public const string ActionID = "webmcp.invoke";
    public string ResourceKind => "webmcp.document";
    private readonly Dictionary<string, Selection> _selections = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private sealed class Selection(BrowseOwnedDocumentRegistry issuer, string id,
        AuthenticatedResourceActor actor, IOriginalBrowserSessionSelection session,
        WebMcpDocument document, WebMcpTool tool, string revision) : IBrowseOwnedDocumentSelection
    {
        private int _revoked;
        public BrowseOwnedDocumentRegistry Issuer { get; } = issuer;
        public AuthenticatedResourceActor Actor { get; } = actor;
        public IOriginalBrowserSessionSelection Session { get; } = session;
        public WebMcpDocument Document { get; } = document;
        public WebMcpTool Tool { get; } = tool;
        public ResourceScope Scope { get; } = new("webmcp.document", id, revision, ResourceAccess.Execute);
        public bool Revoked => Volatile.Read(ref _revoked) != 0;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _revoked, 1) != 0) return;
            Session.Dispose();
            lock (Issuer._gate) Issuer._selections.Remove(Scope.Id);
        }
    }
    public async Task<IBrowseOwnedDocumentSelection> LoadForDisplayAsync(AuthenticatedResourceActor originalActor,
        WebMcpDocument displayedDocument, WebMcpTool displayedTool, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(displayedDocument);
        ArgumentNullException.ThrowIfNull(displayedTool);
        if (string.IsNullOrWhiteSpace(displayedTool.Name) || displayedTool.InputSchema.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Actual displayed tool schema required.");
        var tool = displayedTool with { InputSchema = displayedTool.InputSchema.Clone() };
        var session = _browser.CaptureOriginalSession(); // Before first actual principal/native await.
        var revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(displayedDocument) + "\n" + tool.Name + "\n" + tool.InputSchema.GetRawText())));
        var selected = new Selection(this, Guid.NewGuid().ToString("N"), originalActor, session, displayedDocument, tool, revision);
        try
        {
            await RequireCurrentAsync(selected, token).ConfigureAwait(false);
            lock (_gate) _selections.Add(selected.Scope.Id, selected);
            return selected;
        }
        catch { selected.Dispose(); throw; }
    }
    internal IOriginalBrowserSessionSelection GetOriginalSession(IBrowseOwnedDocumentSelection selected)
        => RequireIssued(selected).Session;
    internal bool IsRevoked(IBrowseOwnedDocumentSelection selected) => RequireIssued(selected).Revoked;
    private Selection RequireIssued(IBrowseOwnedDocumentSelection selected)
        => selected is Selection value && ReferenceEquals(value.Issuer, this) ? value
            : throw new UnauthorizedAccessException("Private original document selection required.");
    public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId,
        ResourceScope scope, CancellationToken token)
    {
        ResourceAccessDecision Deny() => new(false, "PermissionDenied", actor.ActorId, scope.Revision, actor.OrganisationId);
        Selection? selected;
        lock (_gate) _selections.TryGetValue(scope.Id, out selected);
        if (scope.Kind != ResourceKind || actionId != ActionID || scope.Access != ResourceAccess.Execute
            || selected is null || selected.Scope != scope || selected.Actor != actor || selected.Revoked) return Deny();
        try
        {
            await RequireCurrentAsync(selected, token).ConfigureAwait(false);
            return new(true, "Allowed", actor.ActorId, selected.Scope.Revision, actor.OrganisationId);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException
            or JsonException or IOException or NotSupportedException or OperationCanceledException)
        { return Deny(); }
    }
    private async Task RequireCurrentAsync(Selection selected, CancellationToken token)
    {
        if (selected.Revoked || await _actors.GetCurrentAsync(token).ConfigureAwait(false) != selected.Actor || selected.Revoked)
            throw new UnauthorizedAccessException("Original _browser principal changed.");
        var name = JsonSerializer.Serialize(selected.Tool.Name);
        var script = $$"""
            (() => {
              const api = navigator.modelContextTesting;
              const tool = api && typeof api.listTools === 'function' ? api.listTools().find(value => value.name === {{name}}) : null;
              const schema = tool && (typeof tool.inputSchema === 'string' ? JSON.parse(tool.inputSchema) : tool.inputSchema);
              return JSON.stringify({Document:{Origin:location.origin,DocumentID:document[Symbol.for('9to1.webmcp.document')] ?? '',
                BrowserVersion:navigator.userAgent,Capability:'navigator.modelContextTesting',
                Supported:!!api && typeof api.listTools === 'function' && typeof api.executeTool === 'function',CancellationSupported:false},
                ToolName:tool?.name ?? '',InputSchema:schema ?? null});
            })()
            """;
        var raw = await _browser.ExecuteOriginalSessionOwnedScriptAsync(selected.Session, script,
            new ObservationAdmission(selected, _actors), token).ConfigureAwait(false);
        using var actual = JsonDocument.Parse(raw);
        var document = actual.RootElement.GetProperty("Document").Deserialize<WebMcpDocument>();
        if (document != selected.Document || !selected.Document.Supported || selected.Document.Origin == "null"
            || actual.RootElement.GetProperty("ToolName").GetString() != selected.Tool.Name
            || !JsonElement.DeepEquals(actual.RootElement.GetProperty("InputSchema"), selected.Tool.InputSchema)
            || await _actors.GetCurrentAsync(token).ConfigureAwait(false) != selected.Actor || selected.Revoked)
            throw new UnauthorizedAccessException("Original _browser document/tool declaration changed.");
    }
    // Private fixed observational script only. This lease cannot enter an owning effect
    // through this resolver; actual dispatch still requires W1's issued Home lease.
    private sealed class ObservationAdmission(Selection selected, IAuthenticatedResourceActorSource source)
        : IBrowserOwnedScriptDispatchAdmission
    {
        public ValueTask<IBrowserScriptDispatchLease?> AcquireAsync(IBrowserNativeEntryObservation entry, CancellationToken token)
            => ValueTask.FromResult<IBrowserScriptDispatchLease?>(selected.Revoked ? null : new ObservationLease(selected, source));
    }
    private sealed class ObservationLease(Selection selected, IAuthenticatedResourceActorSource source) : IBrowserScriptDispatchLease
    {
        private int _disposed;
        public async ValueTask<bool> CheckAsync(CancellationToken token)
        {
            if (selected.Revoked || Volatile.Read(ref _disposed) != 0) return false;
            var actor = await source.GetCurrentAsync(token).ConfigureAwait(false);
            return actor == selected.Actor && !selected.Revoked && Volatile.Read(ref _disposed) == 0;
        }
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _disposed, 1); return ValueTask.CompletedTask; }
    }
}
