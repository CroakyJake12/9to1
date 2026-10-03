using HavenOS.Files.NativeHost;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed partial class CanvasHomeNativeInsertionOperationTests
{
    [Fact]
    public async Task Actual_registered_owner_display_reads_original_payload_without_any_Home_or_Files_effect()
    {
        await using var f=await Fixture.Create();var home=await f.HomeBytes();var drive=await File.ReadAllBytesAsync(f.StatePath);
        using var display=await f.CaptureDisplay();
        var read=await f.Files.OpenOriginalDisplayAsync(display.Opened.OriginalSelection!);
        Assert.Equal(display.Opened.CasRevisionId,read.CasRevisionId);
        Assert.Equal(CanvasArtifactCodec.Serialize(display.Opened.Artifact),CanvasArtifactCodec.Serialize(read.Artifact));
        Assert.Equal(home,await f.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(f.StatePath));
    }
    [Fact]
    public async Task Legacy_store_without_owner_issuer_denies_before_private_display_is_minted()
    {
        await using var f=await Fixture.Create();var home=await f.HomeBytes();var drive=await File.ReadAllBytesAsync(f.StatePath);
        await Assert.ThrowsAsync<NotSupportedException>(()=>f.CaptureUsing(f.SubstitutedClaimedStore()));
        Assert.Equal(home,await f.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(f.StatePath));
        using var same=await f.CaptureDisplay();Assert.NotNull(same.Opened.OriginalSelection);
    }
    [Fact]
    public async Task Foreign_actual_issuer_with_same_authority_cannot_pair_with_registered_owner_before_payload_read()
    {
        await using var f=await Fixture.Create();var home=await f.HomeBytes();var drive=await File.ReadAllBytesAsync(f.StatePath);
        var foreign=new CanvasHomeClaimedNativeInsertionStore(f.Files,f.Home,f.HomeStore,f.ActualProfiles,
            f.OwnershipAuthority,()=>f.WritesAllowed,new FilesArtifactResourceResolver(f.NativeAuthority));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.CaptureUsing(foreign));
        Assert.Equal(home,await f.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(f.StatePath));
        using var same=await f.CaptureDisplay();Assert.NotNull(same.Opened.OriginalSelection);
    }
    [Fact]
    public async Task Actual_foreign_drive_snapshot_at_original_path_denies_payload_and_old_context_stays_retired_after_restore()
    {
        await using var f=await Fixture.Create();await using var foreign=await Fixture.Create();
        using var original=await f.CaptureDisplay();var home=await f.HomeBytes();var drive=await File.ReadAllBytesAsync(f.StatePath);
        var foreignDrive=await File.ReadAllBytesAsync(foreign.StatePath);
        Assert.NotEqual(original.Opened.StoreId,(await foreign.ActualProvider.GetStoreEvidenceAsync(default)).StoreId);
        try
        {
            await File.WriteAllBytesAsync(f.StatePath,foreignDrive);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Files.OpenOriginalDisplayAsync(original.Opened.OriginalSelection!));
            Assert.Equal(foreignDrive,await File.ReadAllBytesAsync(f.StatePath));Assert.Equal(home,await f.HomeBytes());
        }
        finally {await File.WriteAllBytesAsync(f.StatePath,drive);}
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Files.OpenOriginalDisplayAsync(original.Opened.OriginalSelection!));
        using var fresh=await f.CaptureDisplay();var opened=await f.Files.OpenOriginalDisplayAsync(fresh.Opened.OriginalSelection!);
        Assert.Equal(original.Opened.CasRevisionId,opened.CasRevisionId);
        Assert.Equal(CanvasArtifactCodec.Serialize(original.Opened.Artifact),CanvasArtifactCodec.Serialize(opened.Artifact));
        Assert.Equal(drive,await File.ReadAllBytesAsync(f.StatePath));Assert.Equal(home,await f.HomeBytes());
    }

    [Fact]
    public async Task Genuine_registered_binding_replacement_denies_before_payload_and_retired_display_cannot_resurrect()
    {
        await using var f=await Fixture.Create();using var original=await f.CaptureDisplay();
        var actor=f.OriginalWorkspace.Actor;var profile=Guid.Parse(actor.ProfileId);var folder=f.OriginalWorkspace.Configuration.AppFolders["canvas"];
        var bindingPath=Path.Combine(Path.GetDirectoryName(f.StatePath)!,"bindings.json");var bindings=await File.ReadAllBytesAsync(bindingPath);
        var home=await f.HomeBytes();var drive=await File.ReadAllBytesAsync(f.StatePath);var replacement=Path.Combine(f.RootDirectory,"replacement-mapping");Directory.CreateDirectory(replacement);
        try
        {
            Assert.True((await f.OriginalWorkspace.Directories.RegisterProfileAsync(profile,folder,"canvas",replacement)).IsSuccess);
            Assert.NotEqual(bindings,await File.ReadAllBytesAsync(bindingPath));
            // Empty replacement has no payload: UnauthorizedAccess (rather than a FileNotFound read)
            // demonstrates exact private binding rejection before opening replacement bytes.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Files.OpenOriginalDisplayAsync(original.Opened.OriginalSelection!));
            Assert.Empty(Directory.EnumerateFileSystemEntries(replacement));Assert.Equal(home,await f.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(f.StatePath));
        }
        finally {await File.WriteAllBytesAsync(bindingPath,bindings);}
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>f.Files.OpenOriginalDisplayAsync(original.Opened.OriginalSelection!));
        using var fresh=await f.CaptureDisplay();var opened=await f.Files.OpenOriginalDisplayAsync(fresh.Opened.OriginalSelection!);
        Assert.Equal(original.Opened.CasRevisionId,opened.CasRevisionId);
        Assert.Equal(CanvasArtifactCodec.Serialize(original.Opened.Artifact),CanvasArtifactCodec.Serialize(opened.Artifact));
        Assert.Equal(home,await f.HomeBytes());Assert.Equal(drive,await File.ReadAllBytesAsync(f.StatePath));Assert.Equal(bindings,await File.ReadAllBytesAsync(bindingPath));
    }

}
