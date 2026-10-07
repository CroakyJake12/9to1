using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using NineToOne.Web.Services;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>SAME canonical Task repository, constrained atomically to an actual profile's persisted
/// Tasks conversation. Legacy unbound Task bytes remain intact and unavailable; no account/Space UUID is a ContextId.</summary>
[SupportedOSPlatform("browser")]
public sealed class IndexedDbProfileTaskExecutionRepository : ITaskExecutionRepository
{
    private readonly IndexedDbTaskExecutionRepository _canonical;
    public IndexedDbProfileTaskExecutionRepository(BrowserTaskExecutionTransport sameTransport, BrowserTaskActorSource sameActors)
        : this(sameTransport, sameActors, captureDriver: null) { }
    internal IndexedDbProfileTaskExecutionRepository(BrowserTaskExecutionTransport sameTransport, BrowserTaskActorSource sameActors,
        Action<Task<BrowserAuthenticatedStorageReply>>? captureDriver) =>
        _canonical = new(new ProfileTransport(sameTransport, sameActors, captureDriver));
    private sealed class ProfileTransport(BrowserTaskExecutionTransport transport, BrowserTaskActorSource actors,
        Action<Task<BrowserAuthenticatedStorageReply>>? captureDriver) : ITaskExecutionBrowserTransport
    {
        public async Task<JsonElement> InvokeAsync(string action, JsonElement arguments, CancellationToken cancellationToken)
        {
            var actual = transport.InvokeAuthenticatedAsync(actors, action, arguments, cancellationToken);
            captureDriver?.Invoke(actual); // SAME Task associated before awaiting; private bookkeeping, not authority.
            return (await actual).Reply;
        }
    }
    public Task UpsertAsync(TaskExecutionSnapshot snapshot, CancellationToken token) => _canonical.UpsertAsync(snapshot, token);
    public Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken token) => _canonical.GetAsync(taskId, token);
    public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken token) => _canonical.GetByContextAsync(contextId, token);
    public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => _canonical.GetResumableAsync(token);
}
