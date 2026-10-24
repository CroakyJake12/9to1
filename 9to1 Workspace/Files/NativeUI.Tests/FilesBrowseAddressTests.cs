using Haven.Core;
using Haven.Desktop.Services;

namespace HavenOS.Files.NativeUI.Tests;

public sealed class FilesBrowseAddressTests
{
    [Fact]
    public void Files_locations_preserve_store_and_folder_identity()
    {
        Assert.Equal(new FilesBrowseAppContentAddress(null, null), FilesBrowseAppContentAddress.Parse(AppContentAddress.Parse("9to1://files")));
        var store = Guid.NewGuid(); var folder = Guid.NewGuid();
        var address = FilesBrowseAppContentAddress.For(store, folder);
        Assert.Equal($"9to1://files/store/{store:D}/folder/{folder:D}", address.Value);
        Assert.Equal(new FilesBrowseAppContentAddress(store, folder), FilesBrowseAppContentAddress.Parse(address));
        Assert.Equal(new FilesBrowseAppContentAddress(store, null), FilesBrowseAppContentAddress.Parse(FilesBrowseAppContentAddress.For(store, null)));
    }

    [Fact]
    public void Text_preview_addresses_preserve_all_four_original_identities_and_round_trip()
    {
        var store = Guid.NewGuid(); var folder = Guid.NewGuid(); var file = Guid.NewGuid(); var revision = Guid.NewGuid();
        var address = FilesBrowseAppContentAddress.ForTextPreview(store, folder, file, revision);
        Assert.Equal($"9to1://files/store/{store:D}/folder/{folder:D}/file/{file:D}/revision/{revision:D}", address.Value);
        Assert.Equal(new FilesBrowseAppContentAddress(store, folder, file, revision), FilesBrowseAppContentAddress.Parse(address));
        Assert.NotEqual(FilesBrowseAppContentAddress.For(store, folder), address);
        Assert.Throws<ArgumentException>(() => FilesBrowseAppContentAddress.ForTextPreview(store, folder, file, Guid.Empty));
    }

    [Theory]
    [InlineData("9to1://files/store/01900000-0000-0000-0000-000000000001/folder/01900000-0000-0000-0000-000000000002/file/01900000-0000-0000-0000-000000000003/revision/00000000-0000-0000-0000-000000000000")]
    [InlineData("9to1://files/store/01900000-0000-0000-0000-000000000001/folder/01900000-0000-0000-0000-000000000002/file/01900000-0000-0000-0000-000000000003/revision/not-a-revision")]
    [InlineData("9to1://files/store/01900000-0000-0000-0000-000000000001/file/01900000-0000-0000-0000-000000000003/revision/01900000-0000-0000-0000-000000000004")]
    public void Text_preview_requires_a_real_parent_and_complete_revision_target(string value) =>
        Assert.Throws<FormatException>(() => FilesBrowseAppContentAddress.Parse(AppContentAddress.Parse(value)));

    [Theory]
    [InlineData("9to1://files/folder/01900000-0000-0000-0000-000000000001")]
    [InlineData("9to1://files/store/00000000-0000-0000-0000-000000000000")]
    [InlineData("9to1://files/store/not-a-store")]
    [InlineData("9to1://files/store/01900000-0000-0000-0000-000000000001/file/01900000-0000-0000-0000-000000000002")]
    public void Ambiguous_or_noncanonical_locations_are_refused(string address) =>
        Assert.Throws<FormatException>(() => FilesBrowseAppContentAddress.Parse(AppContentAddress.Parse(address)));

    [Theory]
    [InlineData("cui://files")]
    [InlineData("browse://files")]
    [InlineData("9to1://home")]
    public void Other_namespace_or_app_cannot_reuse_Files_location_authority(string address) =>
        Assert.Throws<UnauthorizedAccessException>(() => FilesBrowseAppContentAddress.Parse(AppContentAddress.Parse(address)));
}
