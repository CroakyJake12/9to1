using System.Xml.Linq;
using HavenOS.Files.NativeUI;

namespace HavenOS.Files.NativeUI.Tests;

public sealed class FilesDonorLayoutTests
{
    [Fact]
    public void Authored_Files_shell_has_separate_tabs_navigation_sidebar_list_and_contextual_details()
    {
        using var stream = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream("HavenOS.Files.NativeUI.UI.FilesBrowser.cui");
        Assert.NotNull(stream);
        var doc = XDocument.Load(stream);
        XElement Find(string id) => Assert.Single(doc.Descendants(), element => (string?)element.Attribute("id") == id);
        Assert.Equal("Auto Auto 1fr Auto", (string?)Find("files-browser-shell").Attribute("RowSizes"));
        Assert.Equal("1", (string?)Find("files-navigation").Attribute("Row"));
        Assert.Equal("48", (string?)Find("files-navigation").Attribute("MinHeight"));
        Assert.Equal("240", (string?)Find("files-sidebar").Attribute("Width"));
        Assert.Equal("{Binding SidebarVisible}", (string?)Find("files-sidebar").Attribute("IsVisible"));
        Assert.Equal("{Binding HasSelection}", (string?)Find("files-details-pane").Attribute("IsVisible"));
        Assert.Equal("List", (string?)Find("files-canonical-list").Attribute("Type"));
        Assert.Equal("40", (string?)Find("files-list-header").Attribute("Height"));
        Assert.Equal("32", (string?)Find("files-status").Parent!.Attribute("Height"));
        Assert.Contains(doc.Descendants("Action"), action => (string?)action.Attribute("name") == "OpenSidebarFolder" &&
            (string?)action.Attribute("command") == "9to1.Files.OpenSidebarFolder");
        Assert.Equal("PreviewSelectedText", (string?)Find("files-preview").Attribute("Action"));
        Assert.Equal("9to1.Files.Apply", (string?)Find("files-apply").Attribute("action"));
        using var artwork = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream("HavenOS.Files.NativeUI.Assets.FolderIcon.png");
        Assert.NotNull(artwork);
    }
}
