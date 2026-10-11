using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

/// <summary>
/// Home-scoped MCP authority for the authenticated host. Remote descriptions, titles and
/// annotations never create a grant; authority comes only from the central decision engine.
/// </summary>
public sealed class HomeMcpInvocationAuthorizer(IPermissionDecisionEngine engine) : IMcpInvocationAuthorizer
{
    public ValueTask<bool> AuthorizeAsync(ExternalConnection connection, string toolName,
        JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (connection is null || string.IsNullOrWhiteSpace(toolName))
            return new ValueTask<bool>(false);

        var scope = ExternalConnectionNaming.CapabilityKey(connection.Id);
        var decision = engine.Evaluate(scope, McpToolRuntime.RiskClassFor(toolName), true,
            "Home must grant this exact MCP connection before the remote action can run.");
        return new ValueTask<bool>(decision.Kind == PermissionDecisionKind.Allowed);
    }
}
