using System.ComponentModel;
using System.Security.Cryptography;
using Xunit;
using static Haven.Infrastructure.Tests.DeveloperOriginalWindowsTestHost;

namespace Haven.Infrastructure.Tests;

public sealed class DeveloperOriginalWindowsCaptureTests
{
    [WindowsNativeFact]
    public async Task Native_manifest_holds_actual_sources_and_directories_until_same_whole_close()
    {
        await using var rig = new Rig(); Directory.CreateDirectory(Path.Combine(rig.Project, "src"));
        var child = Path.Combine(rig.Project, "src", "child.cs"); File.WriteAllText(child, "actual child bytes");
        var capture = await rig.Capture(CancellationToken.None);
        Assert.Equal("src", Assert.Single(capture.OriginalFolderPaths)); Assert.Equal(2, capture.OriginalFiles.Length);
        var file = Assert.Single(capture.OriginalFiles.Where(value => value.RelativePath == "src/child.cs"));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(child))).ToLowerInvariant(), file.ContentSha256);
        Assert.True(rig.Source.IsIssuedOriginalCapture(capture, rig.Selections.Physical!, rig.Selections.Logical));
        Assert.ThrowsAny<IOException>(() => File.WriteAllText(child, "foreign replacement"));
        Assert.ThrowsAny<IOException>(() => Directory.Move(Path.Combine(rig.Project, "src"), Path.Combine(rig.Project, "moved")));
        Assert.ThrowsAny<IOException>(() => Directory.Move(rig.Project, rig.Project + "-foreign"));
        await rig.Source.RevalidateOriginalCaptureAsync(capture, CancellationToken.None);
        var close = rig.Source.CloseAndDrainOriginalCapturesAsync(); Assert.Same(close, rig.Source.CloseAndDrainOriginalCapturesAsync()); await close;
        Assert.False(rig.Source.IsIssuedOriginalCapture(capture, rig.Selections.Physical!, rig.Selections.Logical));
        File.WriteAllText(child, "allowed after original handles retire");
        Directory.Move(Path.Combine(rig.Project, "src"), Path.Combine(rig.Project, "moved"));
        Assert.Equal("allowed after original handles retire", File.ReadAllText(Path.Combine(rig.Project, "moved", "child.cs")));
    }
    [WindowsNativeFact]
    public async Task Refused_original_read_has_no_native_manifest_reads_and_preserves_original_fault()
    {
        await using var rig = new Rig(); var refusal = new UnauthorizedAccessException("original native cohort READ refused"); rig.Reads.Refusal = refusal;
        var task = rig.Capture(CancellationToken.None); var error = await Assert.ThrowsAnyAsync<Exception>(() => task);
        Assert.True(Contains(error, refusal)); Assert.Equal(0, rig.Reads.ReadStarts);
        var close = rig.Source.CloseAndDrainOriginalCapturesAsync(); var cleanup = await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.True(Contains(cleanup, refusal)); Assert.Same(close, rig.Source.CloseAndDrainOriginalCapturesAsync());
    }
    [WindowsNativeFact]
    public async Task Hardlinked_source_is_refused_without_partial_manifest()
    {
        await using var rig = new Rig();
        if (!CreateHardLink(Path.Combine(rig.Project, "alias.cs"), rig.File, IntPtr.Zero)) throw new Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Capture(CancellationToken.None));
        Assert.Equal("original source stays in place", File.ReadAllText(rig.File));
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.CloseAndDrainOriginalCapturesAsync());
    }
    [WindowsNativeFact]
    public async Task Actual_junction_entry_refuses_before_following_outside_source_content()
    {
        await using var rig = new Rig(); var outside = Path.Combine(rig.Root, "outside-source"); Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.cs"), "outside original source");
        var junction = Path.Combine(rig.Project, "redirected"); CreateActualJunction(junction, outside);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => rig.Capture(CancellationToken.None));
            Assert.Equal("outside original source", File.ReadAllText(Path.Combine(outside, "secret.cs")));
            await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.CloseAndDrainOriginalCapturesAsync());
        }
        finally { Directory.Delete(junction); }
    }
    [WindowsNativeFact]
    public async Task Existing_external_writer_and_alternate_stream_each_refuse_the_complete_manifest()
    {
        await using (var rig = new Rig())
        {
            using var writer = new FileStream(rig.File, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            await Assert.ThrowsAnyAsync<Exception>(() => rig.Capture(CancellationToken.None));
        }
        await using (var rig = new Rig())
        {
            File.WriteAllText(rig.File + ":foreign-stream", "source bytes outside the reviewed default stream");
            await Assert.ThrowsAnyAsync<Exception>(() => rig.Capture(CancellationToken.None));
            Assert.Equal("original source stays in place", File.ReadAllText(rig.File));
        }
    }
    [WindowsNativeFact]
    public async Task Whole_capture_bound_refuses_extra_entries_without_truncation_or_hidden_exclusions()
    {
        await using var rig = new Rig(); for (var i = 0; i < 128; i++) File.WriteAllText(Path.Combine(rig.Project, "source-" + i + ".cs"), "actual source");
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Capture(CancellationToken.None));
        Assert.Equal(129, Directory.GetFiles(rig.Project).Length);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.CloseAndDrainOriginalCapturesAsync());
    }
    [WindowsNativeFact]
    public async Task Real_native_timestamp_change_invalidates_retained_capture_version_without_rereading_a_new_manifest()
    {
        await using var rig = new Rig(); var capture = await rig.Capture(CancellationToken.None);
        var before = File.GetLastWriteTimeUtc(rig.File); File.SetLastWriteTimeUtc(rig.File, before.AddMinutes(2));
        Assert.NotEqual(before, File.GetLastWriteTimeUtc(rig.File));
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.RevalidateOriginalCaptureAsync(capture, CancellationToken.None));
        Assert.Equal("original source stays in place", File.ReadAllText(rig.File));
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.CaptureOriginalAsync(rig.Selections.Physical!, rig.Selections.Logical, rig.Reads.Admission, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Source.CloseAndDrainOriginalCapturesAsync());
    }
}
