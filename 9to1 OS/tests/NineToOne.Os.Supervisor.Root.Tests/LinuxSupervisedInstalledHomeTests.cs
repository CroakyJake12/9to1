using System.Runtime.Versioning;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text.Json;
using System.Security.Cryptography;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Supervisor.Root.Tests;

// Isolated HOSTED administrator fixture only. All inputs name an actual configured signed
// installed test package and isolated test user's Home; no keys, mock verifier, actor source,
// lease or endpoint is generated here. Missing platform/package prerequisites FAIL.
[SupportedOSPlatform("linux")]
public sealed class LinuxSupervisedInstalledHomeTests
{
    [Fact]
    public async Task ActualSignedChildIssuesOneOriginalContextAndPublicCopiesCannotAdoptIt()
    {
        await using var f = await Fixture.StartAsync();
        var original = f.Home.ObserveOriginalContext();
        Assert.True(await f.Home.IsOriginalIssuedContextCurrentAsync(original, default));
        Assert.Same(original, f.Home.ObserveOriginalContext());
        Assert.False(await f.Home.IsOriginalIssuedContextCurrentAsync(original with { }, default));
        Assert.False(await f.Home.IsOriginalIssuedContextCurrentAsync(original with { SessionLeaseIdentity = Guid.NewGuid().ToString("N") }, default));
        Assert.Equal(original.ProfileId, f.Home.OriginalHomeActorObservation.ProfileId);
        Assert.Equal("unix-euid:" + f.UserId.ToString(CultureInfo.InvariantCulture), original.OriginalHostPeer.OperatingSystemPrincipalId);
        var authority = new LinuxRootHomeControlledLaunchAuthority(f.Home);
        Assert.True(await authority.IsCurrentAsync(new(original.OriginalHostPeer.ProcessId,
            original.OriginalHostPeer.OperatingSystemPrincipalId, original.OriginalHostProcessStartIdentity,
            original.OriginalHostExecutableIdentity, original.ProfileId, original.SessionLeaseIdentity,
            "os.shell", "home.session-host"), default));
        Assert.True(await authority.IsCurrentAsync(new(original.OriginalHostPeer.ProcessId,
            original.OriginalHostPeer.OperatingSystemPrincipalId, original.OriginalHostProcessStartIdentity,
            original.OriginalHostExecutableIdentity, original.ProfileId, original.SessionLeaseIdentity,
            "os.shell", ""), default));
        Assert.False(await authority.IsCurrentAsync(new(original.OriginalHostPeer.ProcessId,
            original.OriginalHostPeer.OperatingSystemPrincipalId, original.OriginalHostProcessStartIdentity,
            original.OriginalHostExecutableIdentity, original.ProfileId, original.SessionLeaseIdentity,
            "unlaunched.owner", "home.widget-owner"), default));
    }
    [Fact]
    public async Task ActualAdmittedShutdownDrainsOriginalChildBeforeItsRouteCanBeRemoved()
    {
        await using var f = await Fixture.StartAsync(); var original = f.Home.ObserveOriginalContext();
        Assert.True(await f.Home.IsOriginalIssuedContextCurrentAsync(original, default));
        Assert.True(await f.Home.ShutdownOriginalAndDrainForAdministratorAsync());
        Assert.True(f.Home.OriginalExitObserved.IsCompletedSuccessfully);
        Assert.False(await f.Home.IsOriginalIssuedContextCurrentAsync(original, default));
    }
    [Fact]
    public async Task ActualOriginalPidfdTerminationIrrevocablyRetiresOriginalLeaseContext()
    {
        await using var f = await Fixture.StartAsync(); var original = f.Home.ObserveOriginalContext();
        Assert.True(await f.Home.IsOriginalIssuedContextCurrentAsync(original, default));
        Assert.True(await f.Home.TerminateOriginalForAdministratorAsync(default));
        await f.Home.OriginalExitObserved.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(await f.Home.IsOriginalIssuedContextCurrentAsync(original, default));
        Assert.False(await f.Home.IsOriginalIssuedContextCurrentAsync(original with { }, default));
    }
    [Fact]
    public async Task ActualPersistedProfileReplacementRetiresTheSameChildAndNeverRebindsItsContext()
    {
        await using var f = await Fixture.StartAsync(); var original = f.Home.ObserveOriginalContext();
        var path = Required("ASTRA_ROOT_TEST_HOME_STATE_PATH");
        var store = new FileHomeCoreStateStore(path); var read = await store.ReadAsync();
        Assert.True(read.IsSuccess); var record = Assert.Single(read.State!.Records, r => r.RecordId == "home.local-profile");
        var profile = record.Payload.Deserialize<HomeLocalProfile>()!; Assert.Equal(original.ProfileId, profile.ProfileId.ToString("D"));
        var replaced = profile with { ProfileId = Guid.NewGuid() };
        await f.ReplaceIsolatedProfileAsActualUserAsync(path, profile.ProfileId, replaced.ProfileId);
        try
        {
            Assert.False(await f.Home.IsOriginalIssuedContextCurrentAsync(original, default));
            Assert.False(await f.Home.IsOriginalIssuedContextCurrentAsync(original, default));
            var persisted = await store.ReadAsync();
            Assert.Equal(replaced.ProfileId, persisted.State!.Records.Single(r => r.RecordId == record.RecordId).Payload.Deserialize<HomeLocalProfile>()!.ProfileId);
        }
        finally
        {
            // Only this isolated fixture's authentic typed profile record is restored; no raw JSON edit.
            var current = await store.ReadAsync(); var currentRecord = current.State!.Records.Single(r => r.RecordId == record.RecordId);
            Assert.Equal(replaced.ProfileId, currentRecord.Payload.Deserialize<HomeLocalProfile>()!.ProfileId);
            await f.ReplaceIsolatedProfileAsActualUserAsync(path, replaced.ProfileId, profile.ProfileId);
        }
    }
    private static string Required(string name) => Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException("Missing actual isolated hosted fixture input: " + name);
    private sealed class Fixture : IAsyncDisposable
    {
        public LinuxRootSupervisedHome Home { get; }
        public uint UserId { get; }
        private readonly Socket _listener;
        private readonly uint _groupId;
        private readonly string _directory, _path;
        private Fixture(LinuxRootSupervisedHome home, uint uid, uint gid, Socket listener, string directory, string path)
        { Home = home; UserId = uid; _groupId = gid; _listener = listener; _directory = directory; _path = path; }
        public static async Task<Fixture> StartAsync()
        {
            Assert.Equal("unix-euid:0", await new OperatingSystemPrincipalSource().GetPrincipalAsync(default));
            var uid = uint.Parse(Required("ASTRA_ROOT_TEST_UID"), CultureInfo.InvariantCulture);
            var gid = uint.Parse(Required("ASTRA_ROOT_TEST_GID"), CultureInfo.InvariantCulture);
            var runtime = Required("ASTRA_ROOT_TEST_RUNTIME_DIRECTORY");
            Assert.True(LinuxRootOwnedFiles.DirectoryImmutable(runtime));
            var prepared = await LinuxRootHomeStartPreparation.ReadForAdministratorAsync(default);
            Assert.NotNull(prepared);
            var directory = Path.Combine(runtime, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            var path = Path.Combine(directory, "home.sock");
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                listener.Bind(new UnixDomainSocketEndPoint(path));
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead |
                    UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite); listener.Listen(8);
                var home = await LinuxRootSupervisedHome.StartPreparedAsync(prepared!, listener, path, uid, gid,
                    Required("ASTRA_ROOT_TEST_USER_HOME"), default);
                Assert.NotNull(home); return new(home!, uid, gid, listener, directory, path);
            }
            catch { listener.Dispose(); if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); throw; }
        }
        internal async Task ReplaceIsolatedProfileAsActualUserAsync(string path, Guid oldId, Guid newId)
        {
            var executable = Required("ASTRA_ROOT_TEST_PROFILE_TOOL");
            Assert.True(Path.IsPathFullyQualified(executable));
            await VerifySameOriginalProfileToolPayloadAsync(executable);
            var start = new ProcessStartInfo("/usr/bin/setpriv") { UseShellExecute = false };
            start.ArgumentList.Add("--reuid=" + UserId.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--regid=" + _groupId.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add("--clear-groups"); start.ArgumentList.Add("--no-new-privs"); start.ArgumentList.Add("--");
            start.ArgumentList.Add(executable); start.ArgumentList.Add(path);
            start.ArgumentList.Add(UserId.ToString(CultureInfo.InvariantCulture));
            start.ArgumentList.Add(oldId.ToString("D")); start.ArgumentList.Add(newId.ToString("D"));
            start.Environment.Clear(); start.Environment["HOME"] = Required("ASTRA_ROOT_TEST_USER_HOME");
            start.Environment["PATH"] = "/usr/bin:/bin"; start.Environment["DOTNET_EnableDiagnostics"] = "0";
            using var process = Process.Start(start); Assert.NotNull(process);
            await process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(0, process.ExitCode);
        }
        private static async Task VerifySameOriginalProfileToolPayloadAsync(string executable)
        {
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Actual Linux fixture payload required.");
            var directory = Assert.IsType<string>(Path.GetDirectoryName(executable));
            var manifestPath = Path.Combine(directory, "astra-fixture-tool-payload.json");
            var manifest = Assert.IsType<byte[]>(await LinuxRootOwnedFiles.ReadAsync(manifestPath, 2 * 1024 * 1024, CancellationToken.None));
            Assert.Equal(Required("ASTRA_ROOT_TEST_PROFILE_TOOL_MANIFEST_SHA256"), Convert.ToHexString(SHA256.HashData(manifest)).ToLowerInvariant());
            using var document = JsonDocument.Parse(manifest, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(directory, root.GetProperty("stageDirectory").GetString());
            Assert.Equal(executable, root.GetProperty("apphost").GetString());
            var expected = new HashSet<string>(StringComparer.Ordinal) { manifestPath };
            var files = root.GetProperty("files");
            Assert.InRange(files.GetArrayLength(), 1, 4096);
            foreach (var row in files.EnumerateArray())
            {
                var relative = Assert.IsType<string>(row.GetProperty("path").GetString());
                Assert.False(string.IsNullOrWhiteSpace(relative));
                Assert.False(Path.IsPathRooted(relative));
                Assert.DoesNotContain("\\", relative);
                Assert.All(relative.Split('/'), part => Assert.False(part is "" or "." or ".."));
                var path = Path.GetFullPath(Path.Combine(directory, relative));
                Assert.StartsWith(directory + Path.DirectorySeparatorChar, path, StringComparison.Ordinal);
                Assert.True(expected.Add(path));
                var size = row.GetProperty("bytes").GetInt64();
                var digest = Assert.IsType<string>(row.GetProperty("sha256").GetString());
                Assert.True(await LinuxRootOwnedFiles.MatchesAsync(path, size, digest, CancellationToken.None, elfExecutable: path == executable));
            }
            Assert.Contains(executable, expected);
            Assert.Equal(expected.OrderBy(path => path, StringComparer.Ordinal),
                Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal));
        }
        public async ValueTask DisposeAsync()
        {
            try { Assert.True(await Home.ShutdownOriginalAndDrainForAdministratorAsync()); }
            finally { Home.Dispose(); _listener.Dispose(); if (File.Exists(_path)) File.Delete(_path); Directory.Delete(_directory); }
        }
    }
}
