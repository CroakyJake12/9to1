using System.Text.Json;
using Haven.Application;
using Haven.Browser;

namespace HavenOS.Apps.Browse;

public interface IBrowseOwnedDocumentCatalogue : IDisposable { }
public sealed record BrowseOwnedDocumentDiscovery(WebMcpDocument Document,
    IReadOnlyList<WebMcpTool> Tools, IBrowseOwnedDocumentCatalogue Catalogue);

public sealed partial class BrowseOwnedDocumentRegistry
{
    private sealed class Catalogue(BrowseOwnedDocumentRegistry issuer, AuthenticatedResourceActor actor,
        IOriginalBrowserSessionSelection session, WebMcpDocument document, WebMcpTool[] tools)
        : IBrowseOwnedDocumentCatalogue
    {
        public BrowseOwnedDocumentRegistry Issuer { get; } = issuer;
        public AuthenticatedResourceActor Actor { get; } = actor;
        public IOriginalBrowserSessionSelection Session { get; } = session;
        public WebMcpDocument Document { get; } = document;
        public WebMcpTool[] Tools { get; } = tools;
        public List<IBrowseOwnedDocumentSelection> Children { get; } = new();
        public object Gate { get; } = new();
        public bool Revoked { get; private set; }
        public void Dispose()
        {
            lock (Gate)
            {
                if (Revoked) return;
                Revoked = true; Session.Dispose();
                foreach (var child in Children) child.Dispose();
            }
        }
    }
    public async Task<BrowseOwnedDocumentDiscovery> DiscoverForDisplayAsync(AuthenticatedResourceActor originalClickActor,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(originalClickActor);
        var session = _browser.CaptureOriginalSession(); // Before discovery's first await.
        var temporary = new Catalogue(this, originalClickActor, session, new("", "", "", "", false, false), []);
        try
        {
            var raw = await _browser.ExecuteOriginalSessionOwnedScriptAsync(session, DiscoveryScript,
                new CatalogueObservationAdmission(temporary, _actors), token).ConfigureAwait(false);
            using var observed = JsonDocument.Parse(raw);
            var document = observed.RootElement.GetProperty("Document").Deserialize<WebMcpDocument>()
                ?? throw new InvalidDataException("Actual _browser capability report required.");
            var tools = new Dictionary<string, WebMcpTool>(StringComparer.Ordinal);
            if (document.Supported && document.Origin != "null")
                foreach (var value in observed.RootElement.GetProperty("Tools").EnumerateArray())
                {
                    var name = value.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(name) || tools.ContainsKey(name)) throw new InvalidDataException("Invalid actual tool identity.");
                    var supplied = value.GetProperty("inputSchema");
                    JsonElement schema;
                    if (supplied.ValueKind == JsonValueKind.String)
                    { using var json = JsonDocument.Parse(supplied.GetString()!); schema = json.RootElement.Clone(); }
                    else schema = supplied.Clone();
                    if (schema.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid actual tool schema.");
                    tools.Add(name, new(name, value.TryGetProperty("description", out var description)
                        ? description.GetString() ?? "" : "", schema));
                }
            if (await _actors.GetCurrentAsync(token).ConfigureAwait(false) != originalClickActor)
                throw new UnauthorizedAccessException("Original discovery actor changed.");
            var catalogue = new Catalogue(this, originalClickActor, session, document, tools.Values.ToArray());
            return new(document, Array.AsReadOnly(catalogue.Tools), catalogue);
        }
        catch { temporary.Dispose(); throw; }
    }
    public async Task<IBrowseOwnedDocumentSelection> SelectDisplayedToolAsync(IBrowseOwnedDocumentCatalogue originalCatalogue,
        string toolName, CancellationToken token = default)
    {
        if (originalCatalogue is not Catalogue catalogue || !ReferenceEquals(catalogue.Issuer, this))
            throw new UnauthorizedAccessException("Private original displayed catalogue required.");
        WebMcpTool tool;
        IOriginalBrowserSessionSelection session;
        lock (catalogue.Gate)
        {
            if (catalogue.Revoked) throw new ObjectDisposedException(nameof(IBrowseOwnedDocumentCatalogue));
            tool = catalogue.Tools.SingleOrDefault(value => value.Name == toolName)
                ?? throw new UnauthorizedAccessException("Tool was not in the actual original catalogue.");
            session = _browser.ForkOriginalSession(catalogue.Session); // Never ambient replacement capture.
        }
        var revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(catalogue.Document) + "\n" + tool.Name + "\n" + tool.InputSchema.GetRawText())));
        var selected = new Selection(this, Guid.NewGuid().ToString("N"), catalogue.Actor, session, catalogue.Document, tool, revision);
        try
        {
            await RequireCurrentAsync(selected, token).ConfigureAwait(false);
            lock (catalogue.Gate)
            {
                if (catalogue.Revoked) throw new ObjectDisposedException(nameof(IBrowseOwnedDocumentCatalogue));
                lock (_gate) _selections.Add(selected.Scope.Id, selected);
                catalogue.Children.Add(selected);
            }
            return selected;
        }
        catch { selected.Dispose(); throw; }
    }
    internal (AuthenticatedResourceActor Actor, WebMcpDocument Document, WebMcpTool Tool) GetDisplayedMetadata(IBrowseOwnedDocumentSelection selected)
    { var value = RequireIssued(selected); return (value.Actor, value.Document, value.Tool); }
    private const string DiscoveryScript = """
        (() => {
          const key = Symbol.for('9to1.webmcp.document');
          if (!document[key]) Object.defineProperty(document,key,{value:crypto.randomUUID()});
          const api = navigator.modelContextTesting;
          const supported = !!api && typeof api.listTools === 'function' && typeof api.executeTool === 'function';
          return JSON.stringify({Document:{Origin:location.origin,DocumentID:document[key],BrowserVersion:navigator.userAgent,
            Capability:'navigator.modelContextTesting',Supported:supported,CancellationSupported:false},Tools:supported ? api.listTools() : []});
        })()
        """;
    private sealed class CatalogueObservationAdmission(Catalogue catalogue, IAuthenticatedResourceActorSource source)
        : IBrowserOwnedScriptDispatchAdmission
    {
        public ValueTask<IBrowserScriptDispatchLease?> AcquireAsync(IBrowserNativeEntryObservation entry, CancellationToken token)
            => ValueTask.FromResult<IBrowserScriptDispatchLease?>(new CatalogueObservationLease(catalogue, source));
    }
    private sealed class CatalogueObservationLease(Catalogue catalogue, IAuthenticatedResourceActorSource source)
        : IBrowserScriptDispatchLease
    {
        private int _disposed;
        public async ValueTask<bool> CheckAsync(CancellationToken token)
        {
            lock (catalogue.Gate) if (catalogue.Revoked || Volatile.Read(ref _disposed) != 0) return false;
            var current = await source.GetCurrentAsync(token).ConfigureAwait(false);
            lock (catalogue.Gate) return current == catalogue.Actor && !catalogue.Revoked && Volatile.Read(ref _disposed) == 0;
        }
        public ValueTask DisposeAsync() { Interlocked.Exchange(ref _disposed, 1); return ValueTask.CompletedTask; }
    }
}
