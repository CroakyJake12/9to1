using System.Text.Json;
using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;
/// <summary>Test-only broker approval; production uses Home's exact scope and dispatch recheck.</summary>
internal sealed class ApprovedMcpInvocation : IMcpInvocationAuthorizer
{
    public ValueTask<bool> AuthorizeAsync(ExternalConnection connection, string toolName, JsonElement arguments, CancellationToken cancellationToken) => ValueTask.FromResult(true);
}
