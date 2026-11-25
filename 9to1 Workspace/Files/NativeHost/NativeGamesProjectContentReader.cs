using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Haven.Application.Games;
using Haven.Application.Files;
using Haven.Application.Projects;
namespace Haven.Desktop.NativeHost;
public sealed class NativeGamesProjectContentReader : ICanonicalGamesProjectStore
{
    private readonly IFilesReader _reader;
    private readonly GamesProjectCodec _codec;
    public NativeGamesProjectContentReader(IFilesReader reader, GamesProjectCodec codec)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
    }
    public async Task<CanonicalGamesProjectReadResult> ReadAsync(string appId, string projectId, string structuralRevision, string contentRevision, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(projectId))
            return CanonicalGamesProjectReadResult.Refused("missing-identity");
        if (string.IsNullOrWhiteSpace(structuralRevision) || string.IsNullOrWhiteSpace(contentRevision))
            return CanonicalGamesProjectReadResult.Refused("missing-revision");
        var read = await _reader.ReadAsync("games", appId, projectId, structuralRevision, contentRevision, cancellationToken).ConfigureAwait(false);
        if (read == null || read.Status != FilesReadStatus.Success)
            return CanonicalGamesProjectReadResult.Refused("denied-or-missing");
        if (read.AppId != appId || read.ProjectId != projectId)
            return CanonicalGamesProjectReadResult.Refused("wrong-app-or-type");
        if (read.StructuralRevision != structuralRevision || read.ContentRevision != contentRevision)
            return CanonicalGamesProjectReadResult.Refused("revision-mismatch");
        var bytes = read.Bytes;
        if (bytes == null || bytes.Length == 0)
            return CanonicalGamesProjectReadResult.Refused("missing-bytes");
        var decoded = _codec.Decode(bytes);
        if (decoded == null || decoded.ProjectId != projectId)
            return CanonicalGamesProjectReadResult.Refused("decode-mismatch");
        var stored = new GamesStoredProject(decoded.ProjectId, decoded.SceneId, structuralRevision, contentRevision, bytes);
        return CanonicalGamesProjectReadResult.Success(stored, bytes);
    }
}
