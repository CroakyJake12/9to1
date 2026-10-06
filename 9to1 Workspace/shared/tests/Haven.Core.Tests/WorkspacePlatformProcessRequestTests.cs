using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
namespace Haven.Core.Tests;

public sealed class WorkspacePlatformProcessRequestTests
{
    [Fact]
    public void Posix_command_is_one_exact_argument_without_manual_quoting()
    {
        const string command = "printf '%s\\n' 'space and $literal'; printf '%s' \"a\\\"b\";\nexit 7";
        var request = WorkspaceToolProcessRequestFactory.CreatePosixSh("/exact workspace", command, 9);
        Assert.Equal("/bin/sh", request.FileName); Assert.Equal("", request.Arguments);
        Assert.Equal(new[] { "-c", command }, request.ArgumentList); Assert.Equal(TimeSpan.FromSeconds(9), request.Timeout);
        Assert.Null(request.Environment); Assert.False(request.DetachGui);
        Assert.NotEqual(WorkspaceToolOriginalDigest.Process(request), WorkspaceToolOriginalDigest.Process(request with { ArgumentList = new[] { "-c", command + " extra" } }));
        Assert.Throws<ArgumentException>(() => WorkspaceToolOriginalDigest.Process(request with { Arguments = "conflicting" }));
    }
    [Fact]
    public void Windows_request_and_legacy_digest_are_byte_identical_to_previous_shape()
    {
        const string command = "Write-Output 'exact $command'";
        var request = WorkspaceToolProcessRequestFactory.CreateWindowsPowerShell("C:\\actual workspace", command, 120);
        Assert.Equal("powershell.exe", request.FileName); Assert.Null(request.ArgumentList);
        Assert.Equal("-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(command)), request.Arguments);
        var before = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { request.FileName, request.Arguments, request.WorkingDirectory, TimeoutTicks = request.Timeout.Ticks,
            request.DetachGui, Environment = (object?)null })));
        Assert.Equal(before, WorkspaceToolOriginalDigest.Process(request));
    }
    [Fact]
    public void Actual_host_factory_selects_maintained_platform_and_clamps_timeout()
    {
        var request = WorkspaceToolProcessRequestFactory.CreateOriginal(Path.GetTempPath(), "echo original", 9000);
        Assert.Equal(TimeSpan.FromSeconds(900), request.Timeout);
        if (OperatingSystem.IsWindows()) { Assert.Equal("powershell.exe", request.FileName); Assert.Null(request.ArgumentList); }
        else { Assert.Equal("/bin/sh", request.FileName); Assert.Equal(new[] { "-c", "echo original" }, request.ArgumentList); }
        Assert.Throws<ArgumentException>(() => WorkspaceToolProcessRequestFactory.CreatePosixSh("/tmp", "bad\0command", 1));
    }
}
