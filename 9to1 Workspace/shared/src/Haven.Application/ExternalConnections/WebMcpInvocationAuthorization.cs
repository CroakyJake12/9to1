using System.Text.Json;
using NineToOne.Cui.AI;

namespace Haven.Application;

/// <summary>Browser-observed identity, independent of the remote server MCP connection model.</summary>
public sealed record WebMcpInvocationRequest(string Origin, string DocumentId, string BrowserVersion,
    string Capability, bool Supported, string ToolName, JsonElement InputSchema, JsonElement Arguments)
{
    public bool IsValid()
    {
        if (!Supported || !Uri.TryCreate(Origin, UriKind.Absolute, out var origin) ||
            origin.Scheme is not ("https" or "http") || origin.UserInfo.Length != 0 ||
            origin.AbsolutePath != "/" || origin.Query.Length != 0 || origin.Fragment.Length != 0 ||
            string.IsNullOrWhiteSpace(DocumentId) || DocumentId.Length > 256 ||
            string.IsNullOrWhiteSpace(BrowserVersion) || BrowserVersion.Length > 4096 ||
            string.IsNullOrWhiteSpace(Capability) || Capability.Length > 256 ||
            string.IsNullOrWhiteSpace(ToolName) || ToolName.Length > 256 ||
            InputSchema.ValueKind != JsonValueKind.Object || Arguments.ValueKind != JsonValueKind.Object)
            return false;
        return ActionJsonSchemaValidator.Validate(InputSchema.GetRawText(), Arguments, out _);
    }
}

/// <summary>Only an authenticated host composes this authority. Every call requires its own Home approval.</summary>
public interface IWebMcpInvocationAuthorizer
{
    ValueTask<bool> AuthorizeAsync(WebMcpInvocationRequest request, CancellationToken cancellationToken);
}
