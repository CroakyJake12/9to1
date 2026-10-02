using System.Text.Json;
using Haven.Application;
namespace HavenOS.Apps.Browse;

/// <summary>Correlated page protocol only. Script text is never Home approval, a session
/// capability or proof of completion. The owning caller must retain its private origins.</summary>
internal sealed class BrowseWebMcpInvocationScripts
{
    public string InvocationID { get; }
    private readonly string _request;
    public BrowseWebMcpInvocationScripts(string actualIssuerInvocationID, WebMcpDocument document, WebMcpTool tool, JsonElement arguments)
    {
        var schema = tool.InputSchema.Clone(); var captured = arguments.Clone();
        var candidate = new WebMcpInvocationRequest(document.Origin, document.DocumentID,
            document.BrowserVersion, document.Capability, document.Supported, tool.Name, schema, captured);
        if (!candidate.IsValid()) throw new ArgumentException("Actual observed WebMCP schema/arguments required.");
        if (string.IsNullOrWhiteSpace(actualIssuerInvocationID))
            throw new ArgumentException("Actual private dispatch correlation required.", nameof(actualIssuerInvocationID));
        InvocationID = actualIssuerInvocationID;
        _request = JsonSerializer.Serialize(new { document.Origin, document.DocumentID,
            Name = tool.Name, Schema = schema, Arguments = captured, InvocationID });
    }
    // The owner invokes this at most once, through actual original-session final admission.
    // The document map additionally prevents duplicate tool scheduling for the same operation.
    public string Dispatch => $$"""
        (() => {
          const request = {{_request}};
          if (location.origin !== request.Origin || document[Symbol.for('9to1.webmcp.document')] !== request.DocumentID)
            throw new Error('DocumentChanged');
          const current = navigator.modelContextTesting.listTools().find(tool => tool.name === request.Name);
          const schema = current && (typeof current.inputSchema === 'string' ? JSON.parse(current.inputSchema) : current.inputSchema);
          if (!current || JSON.stringify(schema) !== JSON.stringify(request.Schema)) throw new Error('ToolDefinitionChanged');
          const key = Symbol.for('9to1.webmcp.pending');
          if (!document[key]) Object.defineProperty(document, key, {value: new Map()});
          if (document[key].has(request.InvocationID)) return JSON.stringify({started:true, existing:true});
          document[key].set(request.InvocationID, {status:'pending'});
          Promise.resolve().then(() => navigator.modelContextTesting.executeTool(request.Name, JSON.stringify(request.Arguments)))
            .then(result => { document[key].set(request.InvocationID, {status:'completed', result: result ?? null}); },
                  () => { document[key].set(request.InvocationID, {status:'failed'}); });
          return JSON.stringify({started:true});
        })()
        """;
    // Observation never schedules execution and NEVER deletes a terminal response. A lost
    // observation return is retried against the same operation, without any JS effect replay.
    public string Observe => $$"""
        (() => {
          const request = {{_request}};
          if (location.origin !== request.Origin || document[Symbol.for('9to1.webmcp.document')] !== request.DocumentID)
            return JSON.stringify({status:'document-changed'});
          const value = document[Symbol.for('9to1.webmcp.pending')]?.get(request.InvocationID);
          return JSON.stringify(value ?? {status:'unobserved'});
        })()
        """;
}
