using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class ExplicitChromiumRuntimeTests
{
    public static bool IsLinuxX64 => OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64;
    private sealed class LinuxX64FactAttribute : FactAttribute
    { public LinuxX64FactAttribute() { if (!IsLinuxX64) Skip = "Requires Linux x64 runtime files."; } }

    [LinuxX64Fact]
    public void Exact_private_manifest_resolves_only_matching_binary_and_closure()
    {
        using var fixture = new RuntimeFixture();
        Assert.Equal(fixture.Binary, ExplicitChromiumRuntime.Find(fixture.Manifest));
        File.AppendAllText(fixture.Binary, "changed");
        Assert.Null(ExplicitChromiumRuntime.Find(fixture.Manifest));
    }

    [LinuxX64Fact]
    public void Missing_or_unlisted_runtime_files_refuse_the_candidate()
    {
        using var fixture = new RuntimeFixture();
        Assert.Null(ExplicitChromiumRuntime.Find(null));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(fixture.Binary)!, "unlisted-library.so"), "not admitted");
        Assert.Null(ExplicitChromiumRuntime.Find(fixture.Manifest));
    }

    [LinuxX64Fact]
    public void Symlinked_manifest_cannot_select_a_runtime()
    {
        using var fixture = new RuntimeFixture();
        var link = Path.Combine(fixture.Root, "linked.json");
        File.CreateSymbolicLink(link, fixture.Manifest);
        Assert.Null(ExplicitChromiumRuntime.Find(link));
    }

    private sealed class RuntimeFixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "chromium-explicit-" + Guid.NewGuid().ToString("N"));
        internal string Binary => Path.Combine(Root, "chrome-linux64", "chrome");
        internal string Manifest => Path.Combine(Root, "runtime-manifest.json");
        internal RuntimeFixture()
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
            Directory.CreateDirectory(Path.GetDirectoryName(Binary)!);
            var bytes = new byte[20]; bytes[0] = 0x7f; bytes[1] = (byte)'E'; bytes[2] = (byte)'L'; bytes[3] = (byte)'F';
            bytes[4] = 2; bytes[5] = 1; bytes[18] = 62;
            File.WriteAllBytes(Binary, bytes);
            File.SetUnixFileMode(Binary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Manifest, JsonSerializer.Serialize(new
            {
                schema = "9to1.chromium-explicit-runtime/v1", platform = "linux-x64", binary = "chrome-linux64/chrome",
                files = new[] { new { path = "chrome-linux64/chrome", bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) } }
            }));
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
