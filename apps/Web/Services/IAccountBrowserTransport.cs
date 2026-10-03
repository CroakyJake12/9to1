using System.Text.Json;

namespace NineToOne.Web.Services;

/// <summary>Presentation transport for the acknowledged account client's wire replies; no identity or entitlement authority.</summary>
public interface IAccountBrowserTransport
{
    Task<JsonElement> InvokeAsync(string action, JsonElement? arguments, CancellationToken cancellationToken);
}
