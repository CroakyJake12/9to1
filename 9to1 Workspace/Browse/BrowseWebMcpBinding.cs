using System.Text.Json;
using Haven.Browser;
using Haven.Application;

namespace HavenOS.Apps.Browse;

public sealed record WebMcpDocument(string Origin, string DocumentID, string BrowserVersion,
    string Capability, bool Supported, bool CancellationSupported);
public sealed record WebMcpTool(string Name, string Description, JsonElement InputSchema);
public sealed record WebMcpApprovalRequest(WebMcpDocument Document, WebMcpTool Tool, JsonElement Arguments);

/// <summary>Host adapter must route every invocation through Home permissions and approval.</summary>
public interface IWebMcpApprovalBroker
{
    Task<bool> ApproveAsync(WebMcpApprovalRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Browser-side tools are bound to the observed origin and live document, never a remote MCP endpoint.
/// Page descriptions and schemas are untrusted display data; the broker remains the authority.
/// </summary>
public sealed class BrowseWebMcpBinding
{
    private readonly Func<string, CancellationToken, Task<string>> _execute;
    private readonly IWebMcpApprovalBroker _broker;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebMcpDocument? _document;
    private Dictionary<string, WebMcpTool> _tools = new(StringComparer.Ordinal);
    private long _generation;

    public BrowseWebMcpBinding(BrowserSessionService session, IWebMcpApprovalBroker broker)
        : this(session.ExecuteUiScriptAsync, broker) { }

    public BrowseWebMcpBinding(Func<string, CancellationToken, Task<string>> execute, IWebMcpApprovalBroker broker)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    // Runtime feature detection records the browser's actual version, without guessing supported releases.
    private const string DetectScript = """
        (() => {
          const key = Symbol.for('9to1.webmcp.document');
          if (!document[key]) Object.defineProperty(document, key, {value: crypto.randomUUID()});
          const api = navigator.modelContextTesting;
          return JSON.stringify({Origin: location.origin, DocumentID: document[key],
            BrowserVersion: navigator.userAgent, Capability: 'navigator.modelContextTesting',
            Supported: !!api && typeof api.listTools === 'function' && typeof api.executeTool === 'function',
            CancellationSupported: false});
        })()
        """;

    public async Task<(WebMcpDocument Document, IReadOnlyList<WebMcpTool> Tools)> DiscoverAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            DisconnectCore();
            var document = await DetectAsync(ct).ConfigureAwait(false);
            if (!document.Supported || document.Origin == "null") return (document, Array.Empty<WebMcpTool>());
            var identity = JsonSerializer.Serialize(new { document.Origin, document.DocumentID });
            var json = await _execute($$"""
                (() => {
                  const expected = {{identity}};
                  if (location.origin !== expected.Origin || document[Symbol.for('9to1.webmcp.document')] !== expected.DocumentID)
                    throw new Error('DocumentChanged');
                  return JSON.stringify(navigator.modelContextTesting.listTools());
                })()
                """, ct).ConfigureAwait(false);
            using var parsed = JsonDocument.Parse(json);
            var tools = new Dictionary<string, WebMcpTool>(StringComparer.Ordinal);
            foreach (var value in parsed.RootElement.EnumerateArray())
            {
                var name = value.GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(name) || tools.ContainsKey(name)) throw new InvalidDataException("Invalid WebMCP tool identity.");
                var description = value.TryGetProperty("description", out var text) ? text.GetString() ?? "" : "";
                var suppliedSchema = value.GetProperty("inputSchema");
                JsonElement schema;
                if (suppliedSchema.ValueKind == JsonValueKind.String)
                {
                    using var schemaDocument = JsonDocument.Parse(suppliedSchema.GetString()!);
                    schema = schemaDocument.RootElement.Clone();
                }
                else schema = suppliedSchema.Clone();
                if (schema.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid WebMCP schema.");
                tools.Add(name, new(name, description, schema));
            }
            if (await DetectAsync(ct).ConfigureAwait(false) != document) throw new InvalidOperationException("DocumentChanged");
            _document = document;
            _tools = tools;
            return (document, tools.Values.ToArray());
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonElement> InvokeAsync(string toolName, JsonElement arguments, CancellationToken ct = default)
    {
        // Preserve the exact caller proposal across approval/storage waits, even if its JsonDocument
        // is disposed. Approval and browser dispatch consume this same detached snapshot.
        var capturedArguments = arguments.Clone();
        WebMcpDocument document;
        WebMcpTool tool;
        long generation;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            document = _document ?? throw new InvalidOperationException("Disconnected");
            tool = _tools.TryGetValue(toolName, out var found) ? found : throw new InvalidOperationException("ToolNotDiscovered");
            generation = Interlocked.Read(ref _generation);
        }
        finally { _gate.Release(); }
        var proposal = new WebMcpInvocationRequest(document.Origin, document.DocumentID, document.BrowserVersion,
            document.Capability, document.Supported, tool.Name, tool.InputSchema, capturedArguments);
        if (!proposal.IsValid()) throw new InvalidDataException("WebMCPInvocationProposalInvalid");
        if (!await _broker.ApproveAsync(new(document, tool, capturedArguments), ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException("WebMCP invocation denied by Home.");
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (generation != Interlocked.Read(ref _generation) || _document != document || await DetectAsync(ct).ConfigureAwait(false) != document)
                throw new InvalidOperationException("DocumentChangedOrRevoked");
            ct.ThrowIfCancellationRequested();
            var invocationID = Guid.NewGuid().ToString("N");
            var request = JsonSerializer.Serialize(new { document.Origin, document.DocumentID, Name = tool.Name, Schema = tool.InputSchema, Arguments = capturedArguments, InvocationID = invocationID });
            // Many embedded hosts cannot await a JavaScript Promise. Dispatch in the document, then
            // observe a correlated result through synchronous evaluations; never dispatch a retry.
            var started = await _execute($$"""
                (() => {
                  const request = {{request}};
                  if (location.origin !== request.Origin || document[Symbol.for('9to1.webmcp.document')] !== request.DocumentID)
                    throw new Error('DocumentChanged');
                  const current = navigator.modelContextTesting.listTools().find(tool => tool.name === request.Name);
                  const currentSchema = current && (typeof current.inputSchema === 'string' ? JSON.parse(current.inputSchema) : current.inputSchema);
                  if (!current || JSON.stringify(currentSchema) !== JSON.stringify(request.Schema)) throw new Error('ToolDefinitionChanged');
                  const key = Symbol.for('9to1.webmcp.pending');
                  if (!document[key]) Object.defineProperty(document, key, {value: new Map()});
                  document[key].set(request.InvocationID, {status:'pending'});
                  Promise.resolve().then(() => navigator.modelContextTesting.executeTool(request.Name, JSON.stringify(request.Arguments)))
                    .then(result => { if (document[key].has(request.InvocationID)) document[key].set(request.InvocationID, {status:'completed', result: result ?? null}); },
                          () => { if (document[key].has(request.InvocationID)) document[key].set(request.InvocationID, {status:'failed'}); });
                  return JSON.stringify({started:true});
                })()
                """, CancellationToken.None).ConfigureAwait(false);
            using (var acknowledgement = JsonDocument.Parse(started))
                if (!acknowledgement.RootElement.GetProperty("started").GetBoolean()) throw new InvalidOperationException("InvocationNotStarted");
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(30))
            {
                var json = await _execute($$"""
                    (() => {
                      const request = {{request}};
                      if (location.origin !== request.Origin || document[Symbol.for('9to1.webmcp.document')] !== request.DocumentID)
                        return JSON.stringify({status:'document-changed'});
                      const pending = document[Symbol.for('9to1.webmcp.pending')];
                      const value = pending?.get(request.InvocationID) ?? {status:'disconnected'};
                      if (value.status !== 'pending') pending?.delete(request.InvocationID);
                      return JSON.stringify(value);
                    })()
                    """, CancellationToken.None).ConfigureAwait(false);
                using var parsed = JsonDocument.Parse(json);
                var status = parsed.RootElement.GetProperty("status").GetString();
                if (status == "completed") return parsed.RootElement.GetProperty("result").Clone();
                if (status != "pending") throw new InvalidOperationException("WebMCPInvocation:" + status);
                await Task.Delay(25, CancellationToken.None).ConfigureAwait(false);
            }
            // Cancellation is not supported by this detected API. A timeout cannot prove the action stopped.
            throw new TimeoutException("WebMCPOutcomeUnknown: the page action may still be running; do not retry automatically.");
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        // Immediately invalidate approvals waiting on the broker, even while an earlier action is in flight.
        Interlocked.Increment(ref _generation);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { DisconnectCore(); }
        finally { _gate.Release(); }
    }

    private void DisconnectCore() { _document = null; _tools.Clear(); Interlocked.Increment(ref _generation); }
    private async Task<WebMcpDocument> DetectAsync(CancellationToken ct) =>
        JsonSerializer.Deserialize<WebMcpDocument>(await _execute(DetectScript, ct).ConfigureAwait(false))
        ?? throw new InvalidDataException("Browser returned no WebMCP capability report.");
}

/// <summary>Uses the canonical Home authorizer, with schema/arguments bound to each individual approval.</summary>
public sealed class HomeWebMcpApprovalBroker(IWebMcpInvocationAuthorizer authorizer) : IWebMcpApprovalBroker
{
    public async Task<bool> ApproveAsync(WebMcpApprovalRequest request, CancellationToken cancellationToken)
    {
        var document = request.Document;
        var invocation = new WebMcpInvocationRequest(document.Origin, document.DocumentID,
            document.BrowserVersion, document.Capability, document.Supported, request.Tool.Name,
            request.Tool.InputSchema, request.Arguments);
        return invocation.IsValid() && await authorizer.AuthorizeAsync(invocation, cancellationToken).ConfigureAwait(false);
    }
}
