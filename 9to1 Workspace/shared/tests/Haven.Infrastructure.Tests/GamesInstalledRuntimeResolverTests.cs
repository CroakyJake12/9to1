using System.Security.Cryptography;
using Haven.Core.Games;
using Haven.Infrastructure.Games;

namespace Haven.Infrastructure.Tests;

public sealed class GamesInstalledRuntimeResolverTests
{
    [Fact]
    public async Task Missing_invalid_and_changed_packages_are_unavailable_without_fallback()
    {
        Assert.False((await new GamesInstalledRuntimeResolver(null).ResolveAsync()).Available);
        Assert.False((await new GamesInstalledRuntimeResolver(new("godot", new string('0', 64))).ResolveAsync()).Available);
        var directory = Directory.CreateTempSubdirectory("games-package-");
        var path = Path.Combine(directory.FullName, "runtime");
        try
        {
            await File.WriteAllTextAsync(path, "reviewed package bytes");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
            var resolver = new GamesInstalledRuntimeResolver(new(path, hash));
            var available = await resolver.ResolveAsync();
            Assert.True(available.Available);
            Assert.Null(available.UnavailableReason);
            await File.WriteAllTextAsync(path, "replaced package bytes");
            Assert.False((await resolver.ResolveAsync()).Available);
            var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1,
                [GodotSceneRuntimeTests.Node(Guid.NewGuid(), null, "Root", new(0, 0, 0))], []);
            await Assert.ThrowsAsync<InvalidDataException>(() => available.Runtime!.ObserveAsync(scene));
            File.Delete(path);
            Assert.False((await resolver.ResolveAsync()).Available);
        }
        finally { directory.Delete(true); }
    }
}
