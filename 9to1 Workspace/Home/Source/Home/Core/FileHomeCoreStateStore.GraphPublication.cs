using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Home.Core;

public sealed partial class FileHomeCoreStateStore
{
    // Only a trusted graph guard calls this while the same Home writer gate is already held.
    // Direct physical reread; never recursively acquires ReadAsync or creates/adopts replacement state.
    internal async ValueTask<bool> IsRawGraphPublicationStateCurrentAsync(HomeCoreStoredState expected, CancellationToken ct)
    {
        var read = await ReadUnlockedAsync(ct).ConfigureAwait(false);
        return read.IsSuccess && SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(read.State))
            .AsSpan().SequenceEqual(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(expected)));
    }
}
