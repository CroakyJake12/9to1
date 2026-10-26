using Xunit;
using System.Runtime.Versioning;

namespace Haven.Infrastructure.Tests;

// These tests exercise filesystem preparation only: no child/server, model load,
// socket connection, inference request or service-manager operation is started.
[SupportedOSPlatform("linux")]
public sealed class LlamaCppSocketDirectoryTests
{
    private sealed class LinuxSocketFactAttribute : FactAttribute
    { public LinuxSocketFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires actual Linux socket-directory preparation."; } }

    [LinuxSocketFact]
    public void Missing_socket_parent_is_created_private_without_creating_socket()
    {
        var root = Path.Combine(Path.GetTempPath(), "llama-dir-" + Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "haven");
        var socket = Path.Combine(parent, "worker.sock");
        try
        {
            LlamaCppLaunchPlanner.EnsureUnixSocketParent(socket);
            Assert.True(Directory.Exists(parent));
            Assert.False(File.Exists(socket));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(parent));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [LinuxSocketFact]
    public void Existing_parent_permissions_and_unrelated_socket_path_bytes_are_preserved()
    {
        var root = Path.Combine(Path.GetTempPath(), "llama-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var socket = Path.Combine(root, "worker.sock");
        File.WriteAllText(socket, "pre-existing owner bytes");
        var mode = File.GetUnixFileMode(root);
        try
        {
            LlamaCppLaunchPlanner.EnsureUnixSocketParent(socket);
            Assert.Equal(mode, File.GetUnixFileMode(root));
            Assert.Equal("pre-existing owner bytes", File.ReadAllText(socket));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LinuxSocketFact]
    public void Linked_socket_parent_is_refused_without_modifying_external_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "llama-dir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var external = Path.Combine(root, "external");
        Directory.CreateDirectory(external);
        var link = Path.Combine(root, "link");
        Directory.CreateSymbolicLink(link, external);
        try
        {
            Assert.Throws<IOException>(() => LlamaCppLaunchPlanner.EnsureUnixSocketParent(Path.Combine(link, "worker.sock")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(external));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Loopback_tcp_plan_does_not_prepare_any_socket_directory() =>
        LlamaCppLaunchPlanner.EnsureUnixSocketParent(null);
}
