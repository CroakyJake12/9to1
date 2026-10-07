using System.ComponentModel;
using System.Runtime.InteropServices;
using Xunit;

namespace HavenOS.Apps.Canvas.NativeUI.Tests;

/// <summary>Real Windows file-handle custody only; these controls issue no Home/Files authority.</summary>
public sealed class CanvasOriginalArtifactFileTests
{
    private sealed class WindowsFactAttribute : FactAttribute
    { public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Requires real Windows retained file handles."; } }

    [WindowsFact]
    public async Task Actual_candidate_bytes_and_ancestors_stay_held_until_original_close_then_reopen()
    {
        var root = NewRoot(); var bytes = System.Text.Encoding.UTF8.GetBytes("original Canvas private bytes");
        var relative = Path.Combine(".9to1-artifacts", "actual-control", "candidate.9to1c");
        CanvasOriginalArtifactFile? write = null; CanvasOriginalArtifactFile? read = null;
        try
        {
            write = await CanvasOriginalArtifactFile.WriteAsync(root, relative, bytes, default);
            Assert.Throws<IOException>(() => Directory.Move(Path.Combine(root, ".9to1-artifacts"), Path.Combine(root, "renamed")));
            Assert.Throws<IOException>(() => File.WriteAllText(Path.Combine(root, relative), "substitution"));
            await write.DisposeAsync();
            read = CanvasOriginalArtifactFile.OpenRead(root, relative);
            Assert.Equal(bytes, await read.ReadAsync(bytes.Length, default));
            Assert.Throws<IOException>(() => File.Move(Path.Combine(root, relative), Path.Combine(root, "moved.9to1c")));
            await read.DisposeAsync();
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, relative)));
        }
        finally
        {
            if (read is not null) await read.DisposeAsync(); if (write is not null) await write.DisposeAsync();
            Directory.Delete(root, true);
        }
    }

    [WindowsFact]
    public async Task Actual_hard_link_read_is_refused_without_changing_either_file()
    {
        var root = NewRoot(); var original = Path.Combine(root, "original.9to1c"); var linked = Path.Combine(root, "linked.9to1c");
        try
        {
            await File.WriteAllTextAsync(original, "unchanged original");
            if (!CreateHardLinkW(linked, original, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            Assert.Throws<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.OpenRead(root, "linked.9to1c"));
            Assert.Equal("unchanged original", await File.ReadAllTextAsync(original));
            Assert.Equal("unchanged original", await File.ReadAllTextAsync(linked));
        }
        finally { Directory.Delete(root, true); }
    }

    [WindowsFact]
    public async Task Foreign_relative_path_refuses_before_creating_any_candidate()
    {
        var root = NewRoot();
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => CanvasOriginalArtifactFile.WriteAsync(root, "../foreign.9to1c", new byte[] { 1 }, default));
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }
    private static string NewRoot()
    { var root = Path.Combine(Path.GetTempPath(), "canvas-original-bytes-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string newPath, string originalPath, IntPtr security);
}
