using System.Xml.Linq;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Files.NativeUI;

namespace HavenOS.Files.NativeUI.Tests;

public sealed class FilesDonorLayoutTests
{
    [Fact]
    public void Authored_Files_shell_keeps_one_item_owner_with_view_controls_and_contextual_details()
    {
        using var stream = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream("HavenOS.Files.NativeUI.UI.FilesBrowser.cui");
        Assert.NotNull(stream);
        var doc = XDocument.Load(stream);
        XElement Find(string id) => Assert.Single(doc.Descendants(), element => (string?)element.Attribute("id") == id);
        Assert.Equal("Auto 1fr Auto", (string?)Find("files-browser-shell").Attribute("RowSizes"));
        Assert.Equal("0", (string?)Find("files-navigation").Attribute("Row"));
        Assert.Equal("48", (string?)Find("files-navigation").Attribute("MinHeight"));
        Assert.Equal("{Binding SidebarWidth}", (string?)Find("files-sidebar").Attribute("Width"));
        Assert.Equal("{Binding SidebarVisible}", (string?)Find("files-sidebar").Attribute("IsVisible"));
        Assert.Equal("{Binding DetailsShown}", (string?)Find("files-details-pane").Attribute("IsVisible"));
        Assert.Equal("List", (string?)Find("files-canonical-list").Attribute("Type"));
        Assert.Equal("40", (string?)Find("files-list-header").Attribute("Height"));
        Assert.Equal("32", (string?)Find("files-status").Parent!.Attribute("MinHeight"));
        Assert.Equal("9to1.Files.View.List", (string?)Find("files-view-list").Attribute("Action"));
        Assert.Equal("9to1.Files.View.Grid", (string?)Find("files-view-grid").Attribute("Action"));
        Assert.Equal("9to1.Files.Open", (string?)Find("files-details-open").Attribute("Action"));
        Assert.Equal("{Binding ReviewVisible}", (string?)Find("files-review-panel").Attribute("IsVisible"));
        Assert.Equal("Availability", (string?)Find("files-availability-heading").Attribute("Text"));
        Assert.Single(doc.Descendants("Input"), element => (string?)element.Attribute("Type") == "List");
        Assert.Equal("True", (string?)Assert.Single(doc.Descendants("Page")).Attribute("OwnsBackdrop"));
        Assert.Contains(doc.Descendants("Action"), action => (string?)action.Attribute("name") == "OpenSidebarFolder" &&
            (string?)action.Attribute("command") == "9to1.Files.OpenSidebarFolder");
        Assert.Equal("PreviewSelectedText", (string?)Find("files-preview").Attribute("Action"));
        Assert.Equal("9to1.Files.Apply", (string?)Find("files-apply").Attribute("action"));
        using var artwork = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream("HavenOS.Files.NativeUI.Assets.FolderIcon.png");
        Assert.NotNull(artwork);
    }

    [Theory]
    [InlineData("FilesBrowser.cui")]
    [InlineData("FilesDetailsRow.cui")]
    public void Canonical_Files_documents_have_no_parser_errors_or_duplicate_stable_identities(string file)
    {
        using var stream = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream("HavenOS.Files.NativeUI.UI." + file);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), file);
        Assert.NotEmpty(document.Components);
        Assert.DoesNotContain(parser.Diagnostics.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
    }
}
