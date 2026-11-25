using System;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application.Games;
using Haven.Core.Games;

namespace Haven.Desktop.NativeHost;

public sealed class NativeGamesProjectContentReader : ICanonicalGamesProjectStore
{
    public Task<GamesStoredProject> OpenAsync(Guid fileID, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<GamesStoredProject> SaveAsync(Guid fileID, Guid expectedStructuralRevisionID, long expectedProjectRevision,
        GamesProjectDocument project, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
