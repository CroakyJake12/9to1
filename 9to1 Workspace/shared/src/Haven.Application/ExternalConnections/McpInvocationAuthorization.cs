using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

/// <summary>Home-scoped authority from the authenticated host, independent of remote descriptions and generic mode flags.</summary>
public interface IMcpInvocationAuthorizer
{
    ValueTask<bool> AuthorizeAsync(ExternalConnection connection, string toolName,
        JsonElement arguments, CancellationToken cancellationToken);
}
