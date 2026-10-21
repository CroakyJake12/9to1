using System.Text.Json;

namespace HavenOS.Apps.Browse;

/// <summary>
/// A multiplexed, owned Chromium DevTools endpoint shared by every page target of
/// one engine-isolated profile. Commands carry an optional session id; browser
/// commands use none. Raw CDP events are surfaced through <see cref="Recv"/>.
/// </summary>
internal interface IChromiumEndpoint : IAsyncDisposable
{
    /// <summary>Sends a correlated CDP command and awaits its result.</summary>
    Task<JsonElement> SendAsync(string? sessionId, string method, object? parameters, CancellationToken cancellationToken);

    /// <summary>Creates a page target and returns its target id.</summary>
    Task<string> CreateTargetAsync(string url, CancellationToken cancellationToken);

    /// <summary>Attaches to a target and returns its flattened session id.</summary>
    Task<string> AttachAsync(string targetId, CancellationToken cancellationToken);

    /// <summary>Raised for every raw CDP event from the shared socket.</summary>
    event EventHandler<CdpEvent>? Recv;
}
