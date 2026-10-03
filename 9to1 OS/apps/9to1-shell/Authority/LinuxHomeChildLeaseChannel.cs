using System.Runtime.Versioning;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

// This protocol observes the maintained child's actually held original lease. It does not
// authenticate arbitrary wire actors, install a package, or grant owner resource scopes.
internal sealed record LinuxHomeLeaseChallenge(int Schema, Guid Correlation);
internal sealed record LinuxHomeLeaseReply(int Schema, Guid Correlation, string LeaseIdentity,
    string LeasePath, AuthenticatedResourceActor OriginalHomeActor);

[SupportedOSPlatform("linux")]
internal static class LinuxHomeChildLeaseChannel
{
    private const int MaximumFrame = 16384;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 8 };

    public static async Task ServeOriginalLeaseAsync(string protectedAdministratorSocket,
        HomeNativeSessionLease originalLease, IAuthenticatedResourceActorSource actors,
        IAppPaths paths, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() || !Path.IsPathFullyQualified(protectedAdministratorSocket) ||
            Path.GetFullPath(protectedAdministratorSocket) != protectedAdministratorSocket ||
            !LinuxRootOwnedFiles.DirectoryImmutable(Path.GetDirectoryName(protectedAdministratorSocket)!))
            throw new UnauthorizedAccessException("Original protected administrator endpoint required.");
        var originalActor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (originalActor is null || originalActor.OrganisationId is not null ||
            originalActor.ProfileId != originalLease.ProfileId || !originalLease.IsHeld)
            throw new UnauthorizedAccessException("Actual original personal Home lease required.");
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(protectedAdministratorSocket), ct).ConfigureAwait(false);
        var administrator = HomeNativePeerObservation.FromAcceptedUnixSocket(socket);
        if (administrator?.OperatingSystemPrincipalId != "unix-euid:0")
            throw new UnauthorizedAccessException("Kernel-observed administrator connection required.");
        using var stream = new NetworkStream(socket, ownsSocket: false);
        var leasePath = Path.GetFullPath(Path.Combine(paths.DataDirectory, "Home", "Runtime",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalActor.ProfileId))) + ".lease"));
        while (true)
        {
            var challenge = await LinuxOriginalControlFrameReader.ReadAsync<LinuxHomeLeaseChallenge>(stream, ct).ConfigureAwait(false);
            using var idleDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            idleDeadline.CancelAfter(TimeSpan.FromSeconds(60));
            
            if (challenge.Schema != 1 || challenge.Correlation == Guid.Empty || !originalLease.IsHeld ||
                originalActor != await actors.GetCurrentAsync(idleDeadline.Token).ConfigureAwait(false) || !originalLease.IsHeld)
                throw new UnauthorizedAccessException("The original Home lease or actor retired.");
            // The SAME actual lease object is retained; no acquisition, replacement or aliasing.
            await WriteAsync(stream, new LinuxHomeLeaseReply(1, challenge.Correlation,
                originalLease.LeaseIdentity.ToString("N"), leasePath, originalActor), idleDeadline.Token).ConfigureAwait(false);
            if (!originalLease.IsHeld || originalActor != await actors.GetCurrentAsync(idleDeadline.Token).ConfigureAwait(false) ||
                !originalLease.IsHeld) throw new UnauthorizedAccessException("Original Home context retired after reply.");
        }
    }

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size is < 1 or > MaximumFrame) throw new InvalidDataException("Bounded lease frame required.");
        var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Null lease frame denied.");
    }
    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length is < 1 or > MaximumFrame) throw new InvalidDataException("Bounded lease frame required.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false); await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
