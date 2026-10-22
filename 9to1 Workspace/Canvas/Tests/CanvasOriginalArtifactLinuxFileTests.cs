using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

/// <summary>Actual Linux kernel custody controls. These require openat2/statx and
/// must not be reclassified as Windows, ARM-device or complete app acceptance.</summary>
[SupportedOSPlatform("linux")]
public sealed class CanvasOriginalArtifactLinuxFileTests
{
    [LinuxCanvasFact]
    public async Task Exclusive_nested_write_flushes_owned_bytes_and_reopens_the_same_regular_revision()
    {
        using var rig = new Rig();
        var expected = Encoding.UTF8.GetBytes("Canvas revision: structured ink remains editable.");
        const string relative = ".9to1-artifacts/document/revision.9to1c";
        await using (var written = await CanvasOriginalArtifactFile.WriteAsync(rig.Root, relative, expected, CancellationToken.None))
        {
            written.DemandUnchanged(expected.Length);
            var mode = File.GetUnixFileMode(rig.Path(relative));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
        await using var opened = CanvasOriginalArtifactFile.OpenRead(rig.Root, relative);
        Assert.Equal(expected, await opened.ReadAsync(expected.Length, CancellationToken.None));
    }

    [LinuxCanvasFact]
    public async Task Write_captures_caller_memory_once_before_the_awaited_native_owner_uses_it()
    {
        using var rig = new Rig();
        var expected = Encoding.UTF8.GetBytes("A single immutable operation snapshot");
        using var memory = new SingleCaptureMemory(expected);
        await using var written = await CanvasOriginalArtifactFile.WriteAsync(rig.Root, "captured.9to1c",
            memory.CaptureInput(), CancellationToken.None);
        written.DemandUnchanged(expected.Length);
        Assert.Equal(1, memory.Captures);
        Assert.Equal(expected, File.ReadAllBytes(rig.Path("captured.9to1c")));
    }

    [LinuxCanvasFact]
    public async Task Existing_candidate_is_never_truncated_or_overwritten()
    {
        using var rig = new Rig();
        var original = Encoding.UTF8.GetBytes("Existing durable candidate");
        File.WriteAllBytes(rig.Path("existing.9to1c"), original);
        await Assert.ThrowsAsync<IOException>(() => CanvasOriginalArtifactFile.WriteAsync(rig.Root, "existing.9to1c",
            new byte[] { 7, 8, 9 }, CancellationToken.None));
        Assert.Equal(original, File.ReadAllBytes(rig.Path("existing.9to1c")));
    }

    [LinuxCanvasFact]
    public void Root_escape_empty_component_and_foreign_path_spellings_are_refused()
    {
        using var rig = new Rig();
        foreach (var relative in new[] { "../outside.9to1c", "/tmp/outside.9to1c", "a/../b", "a//b", "a/./b", "a\\b", "" })
            Assert.Throws<InvalidDataException>(() => CanvasOriginalArtifactFile.OpenRead(rig.Root, relative));
        Assert.Throws<InvalidDataException>(() => CanvasOriginalArtifactFile.OpenRead("/", "tmp/file"));
    }

    [LinuxCanvasFact]
    public async Task Symlink_leaf_parent_and_ancestor_are_rejected_without_touching_their_target()
    {
        using var rig = new Rig();
        var target = Path.Combine(rig.Sandbox, "outside.9to1c");
        var original = Encoding.UTF8.GetBytes("Unrelated owner");
        File.WriteAllBytes(target, original);
        File.CreateSymbolicLink(rig.Path("alias.9to1c"), target);
        Assert.Throws<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.OpenRead(rig.Root, "alias.9to1c"));
        var writeRefusal = await Record.ExceptionAsync(() => CanvasOriginalArtifactFile.WriteAsync(rig.Root,
            "alias.9to1c", new byte[] { 1 }, CancellationToken.None));
        // O_EXCL may report EEXIST before final-component symlink resolution.
        Assert.True(writeRefusal is UnauthorizedAccessException or IOException);
        Directory.CreateSymbolicLink(rig.Path("redirect"), rig.Sandbox);
        Assert.Throws<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.OpenRead(rig.Root, "redirect/outside.9to1c"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.WriteAsync(rig.Root,
            "redirect/new.9to1c", new byte[] { 1 }, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(rig.Sandbox, "new.9to1c")));
        var ancestorAlias = Path.Combine(rig.Sandbox, "root-alias");
        Directory.CreateSymbolicLink(ancestorAlias, rig.Root);
        Assert.Throws<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.OpenRead(ancestorAlias, "alias.9to1c"));
        Assert.Equal(original, File.ReadAllBytes(target));
    }

    [LinuxCanvasFact]
    public void Hard_link_alias_is_rejected_as_nonexclusive_custody()
    {
        using var rig = new Rig();
        var target = rig.Path("original.9to1c");
        File.WriteAllBytes(target, new byte[] { 1, 2, 3 });
        Assert.Equal(0, Link(target, rig.Path("alias.9to1c")));
        Assert.Throws<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.OpenRead(rig.Root, "original.9to1c"));
        Assert.Throws<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.OpenRead(rig.Root, "alias.9to1c"));
    }

    [LinuxCanvasFact]
    public async Task A_new_hard_link_after_write_refuses_later_publication_custody()
    {
        using var rig = new Rig();
        await using var held = await CanvasOriginalArtifactFile.WriteAsync(rig.Root, "target.9to1c",
            new byte[] { 1, 2, 3 }, CancellationToken.None);
        Assert.Equal(0, Link(rig.Path("target.9to1c"), rig.Path("alias.9to1c")));
        Assert.Throws<UnauthorizedAccessException>(() => held.DemandUnchanged(3));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(rig.Path("target.9to1c")));
    }

    [LinuxCanvasFact]
    public async Task Fifo_admission_is_nonblocking_and_directory_is_not_a_regular_artifact()
    {
        using var rig = new Rig();
        Assert.Equal(0, MakeFifo(rig.Path("fifo.9to1c"), 0x180));
        var failure = await Task.Run(() => Record.Exception(() =>
        {
            var unexpected = CanvasOriginalArtifactFile.OpenRead(rig.Root, "fifo.9to1c");
            unexpected.DisposeAsync().AsTask().GetAwaiter().GetResult();
        })).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.IsType<UnauthorizedAccessException>(failure);
        Directory.CreateDirectory(rig.Path("directory.9to1c"));
        Assert.Throws<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.OpenRead(rig.Root, "directory.9to1c"));
    }

    [LinuxCanvasFact]
    public async Task Replaced_leaf_never_retargets_a_retained_read()
    {
        using var rig = new Rig();
        var expected = new byte[] { 1, 2, 3 };
        File.WriteAllBytes(rig.Path("target.9to1c"), expected);
        await using var held = CanvasOriginalArtifactFile.OpenRead(rig.Root, "target.9to1c");
        File.Move(rig.Path("target.9to1c"), rig.Path("retired.9to1c"));
        File.WriteAllBytes(rig.Path("target.9to1c"), new byte[] { 9, 9, 9 });
        Assert.Throws<UnauthorizedAccessException>(() => held.DemandUnchanged(expected.Length));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => held.ReadAsync(expected.Length, CancellationToken.None));
        Assert.Equal(expected, File.ReadAllBytes(rig.Path("retired.9to1c")));
        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(rig.Path("target.9to1c")));
    }

    [LinuxCanvasFact]
    public async Task Renamed_and_rebound_root_is_refused_even_when_replacement_has_the_same_file_name()
    {
        using var rig = new Rig();
        File.WriteAllBytes(rig.Path("target.9to1c"), new byte[] { 1, 2, 3 });
        await using var held = CanvasOriginalArtifactFile.OpenRead(rig.Root, "target.9to1c");
        var moved = rig.Root + "-retired";
        Directory.Move(rig.Root, moved);
        Directory.CreateDirectory(rig.Root);
        File.WriteAllBytes(rig.Path("target.9to1c"), new byte[] { 9, 9, 9 });
        Assert.Throws<UnauthorizedAccessException>(() => held.DemandUnchanged(3));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(moved, "target.9to1c")));
    }

    [LinuxCanvasFact]
    public async Task Renamed_and_rebound_parent_is_refused_without_retargeting_the_leaf()
    {
        using var rig = new Rig();
        Directory.CreateDirectory(rig.Path("parent"));
        File.WriteAllBytes(rig.Path("parent/target.9to1c"), new byte[] { 1, 2, 3 });
        await using var held = CanvasOriginalArtifactFile.OpenRead(rig.Root, "parent/target.9to1c");
        Directory.Move(rig.Path("parent"), rig.Path("retired-parent"));
        Directory.CreateDirectory(rig.Path("parent"));
        File.WriteAllBytes(rig.Path("parent/target.9to1c"), new byte[] { 9, 9, 9 });
        Assert.Throws<UnauthorizedAccessException>(() => held.DemandUnchanged(3));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(rig.Path("retired-parent/target.9to1c")));
    }

    [LinuxCanvasFact]
    public async Task Candidate_parent_renamed_after_write_is_refused_before_the_callers_publication_fence()
    {
        using var rig = new Rig();
        await using var held = await CanvasOriginalArtifactFile.WriteAsync(rig.Root, "new-parent/target.9to1c",
            new byte[] { 1, 2, 3 }, CancellationToken.None);
        Directory.Move(rig.Path("new-parent"), rig.Path("retired-parent"));
        Directory.CreateDirectory(rig.Path("new-parent"));
        File.WriteAllBytes(rig.Path("new-parent/target.9to1c"), new byte[] { 9, 9, 9 });
        Assert.Throws<UnauthorizedAccessException>(() => held.DemandUnchanged(3));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(rig.Path("retired-parent/target.9to1c")));
    }

    [LinuxCanvasFact]
    public async Task Same_length_in_place_change_with_restored_mtime_is_not_a_valid_read_revision()
    {
        using var rig = new Rig();
        var path = rig.Path("target.9to1c");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        var modified = File.GetLastWriteTimeUtc(path);
        await using var held = CanvasOriginalArtifactFile.OpenRead(rig.Root, "target.9to1c");
        File.WriteAllBytes(path, new byte[] { 9, 9, 9 });
        File.SetLastWriteTimeUtc(path, modified.AddSeconds(1));
        File.SetLastWriteTimeUtc(path, modified);
        Assert.Throws<InvalidDataException>(() => held.DemandUnchanged(3));
        await Assert.ThrowsAsync<InvalidDataException>(() => held.ReadAsync(3, CancellationToken.None));
    }

    [LinuxCanvasFact]
    public async Task Bounds_and_size_mismatch_refuse_before_allocating_or_reading_unbounded_content()
    {
        using var rig = new Rig();
        File.WriteAllBytes(rig.Path("target.9to1c"), new byte[] { 1, 2, 3 });
        await using var held = CanvasOriginalArtifactFile.OpenRead(rig.Root, "target.9to1c");
        await Assert.ThrowsAsync<InvalidDataException>(() => held.ReadAsync(0, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => held.ReadAsync(512L * 1024 * 1024 + 1, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => held.ReadAsync(4, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => CanvasOriginalArtifactFile.WriteAsync(rig.Root, "empty.9to1c", ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        Assert.False(File.Exists(rig.Path("empty.9to1c")));
    }

    [LinuxCanvasFact]
    public async Task Precancelled_read_and_write_preserve_existing_content_and_create_no_candidate()
    {
        using var rig = new Rig();
        File.WriteAllBytes(rig.Path("target.9to1c"), new byte[] { 1, 2, 3 });
        await using var held = CanvasOriginalArtifactFile.OpenRead(rig.Root, "target.9to1c");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => held.ReadAsync(3, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CanvasOriginalArtifactFile.WriteAsync(rig.Root,
            "canceled.9to1c", new byte[] { 9 }, cancellation.Token));
        Assert.False(File.Exists(rig.Path("canceled.9to1c")));
        Assert.Equal(new byte[] { 1, 2, 3 }, await held.ReadAsync(3, CancellationToken.None));
    }

    [LinuxCanvasFact]
    public async Task Successful_close_and_failed_acquisition_release_only_their_owned_descriptors()
    {
        using var rig = new Rig();
        File.WriteAllBytes(rig.Path("target.9to1c"), new byte[] { 1, 2, 3 });
        await using (var held = CanvasOriginalArtifactFile.OpenRead(rig.Root, "target.9to1c"))
        {
            Assert.True(OwnedDescriptorCount(rig.Sandbox) >= 2);
            held.DemandUnchanged(3);
        }
        Assert.Equal(0, OwnedDescriptorCount(rig.Sandbox));
        Directory.CreateDirectory(rig.Path("not-a-file"));
        Assert.Throws<UnauthorizedAccessException>(() => CanvasOriginalArtifactFile.OpenRead(rig.Root, "not-a-file"));
        Assert.Equal(0, OwnedDescriptorCount(rig.Sandbox));
    }

    private static int OwnedDescriptorCount(string root)
        => Directory.EnumerateFileSystemEntries("/proc/self/fd").Count(path =>
        {
            try { return new FileInfo(path).LinkTarget?.StartsWith(root + "/", StringComparison.Ordinal) == true; }
            catch (IOException) { return false; } // The enumerator's own short-lived directory descriptor may retire.
        });

    private sealed class Rig : IDisposable
    {
        private readonly DirectoryInfo _sandbox = Directory.CreateTempSubdirectory("9to1-canvas-linux-");
        internal Rig() { Root = System.IO.Path.Combine(_sandbox.FullName, "owner"); Directory.CreateDirectory(Root); }
        internal string Root { get; }
        internal string Sandbox => _sandbox.FullName;
        internal string Path(string relative) => System.IO.Path.Combine(Root, relative);
        public void Dispose() => _sandbox.Delete(recursive: true);
    }

    // An owned snapshot may read the source span once. Retaining caller memory for
    // hashing or an awaited file write deterministically fails this positive control.
    private sealed class SingleCaptureMemory(byte[] original) : MemoryManager<byte>
    {
        private readonly byte[] _bytes = original.ToArray();
        private bool _armed;
        internal int Captures { get; private set; }
        internal ReadOnlyMemory<byte> CaptureInput()
        {
            var input = Memory;
            _armed = true; Captures = 0;
            return input;
        }
        public override Span<byte> GetSpan()
        {
            if (_armed && ++Captures > 1) throw new InvalidOperationException("Caller memory was borrowed after its one operation capture.");
            return _bytes;
        }
        public override MemoryHandle Pin(int elementIndex = 0) => throw new InvalidOperationException("Caller memory cannot be retained across native I/O.");
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }

    public sealed class LinuxCanvasFactAttribute : FactAttribute
    {
        // This owning project currently uses xUnit2. Keep source-info-compatible
        // arguments without assuming xUnit3's distinct FactAttribute constructor.
        public LinuxCanvasFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
        {
            if (!OperatingSystem.IsLinux()) Skip = "Requires the actual Linux openat2/statx kernel; Windows/ARM-device acceptance is separate.";
        }
    }
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string target);
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);
}
