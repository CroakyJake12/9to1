using CakeOS.Cui.Build.Tasks;
using Microsoft.Build.Framework;
using Xunit;

namespace CakeOS.Cui.Build.Tasks.Tests;

public sealed class CuiResourceBundleTests : IDisposable
{
    private readonly string _root;
    private readonly RecordingLog _log = new();

    public CuiResourceBundleTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cakeos-cui-build-tasks-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void CanonicalDefinitionDeclarationIsValidAndItsOriginalBytesArePreserved()
    {
        var path = WriteResource("callout.cui", "<Cui>\n  <Text (Definition) ID=\"Callout\">Reusable</Text>\n</Cui>\n");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Empty(_log.Warnings);
        var entry = Assert.Single(entries);
        Assert.Equal("callout.cui", entry.LogicalName);
        Assert.Equal(File.ReadAllBytes(path), entry.Data);

        var roundTrip = Assert.Single(ReadBundle(WriteBundle(entries)));
        Assert.Equal("callout.cui", roundTrip.LogicalName);
        Assert.Equal(File.ReadAllBytes(path), roundTrip.Data);
    }

    [Fact]
    public void QuotedLiteralDefinitionIsNotTreatedAsTheDefinitionModifier()
    {
        var literal = new CuiLexicalValidator().Validate("<Cui><Text Name=\"(Definition)\">Shared</Text></Cui>");

        Assert.True(literal.IsValid);
        Assert.Contains("Name=\"(Definition)\"", literal.Normalized);
        Assert.DoesNotContain("Definition=\"True\"", literal.Normalized);

        var declaration = new CuiLexicalValidator().Validate("<Cui><Text (Definition) ID=\"Callout\"/></Cui>");

        Assert.True(declaration.IsValid);
        Assert.Contains("Definition=\"True\"", declaration.Normalized);
    }

    [Fact]
    public void OrdinaryXmlIsValidAndIsEmbeddedUnchanged()
    {
        var path = WriteResource("panel.cui", "<Cui><Stack gap=\"8\"><Text>Hello</Text></Stack></Cui>");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Empty(_log.Warnings);
        var roundTrip = Assert.Single(ReadBundle(WriteBundle(entries)));
        Assert.Equal("panel.cui", roundTrip.LogicalName);
        Assert.Equal(File.ReadAllBytes(path), roundTrip.Data);
    }

    [Fact]
    public void CanonicalShorthandFormsAreNotRejectedAsRawXml()
    {
        var source = "<Cui><Page><Text (Definition) ID=\"Callout\"/><Callout/><$Color:Light_Green/>" +
            "<&Submit{::Callout}/><@Fade Duration:250/><# note #><Text:state:static></Page></Cui>";
        var path = WriteResource("shorthand.cui", source);

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Empty(_log.Warnings);
        Assert.Equal(File.ReadAllBytes(path), Assert.Single(entries).Data);
    }

    [Fact]
    public void MalformedSyntaxWarnsButTheOriginalBytesAreStillEmbedded()
    {
        var path = WriteResource("broken.cui", "<Cui><Text (Definition</Text></Cui>");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        var warning = Assert.Single(_log.Warnings);
        Assert.Contains("invalid CUI syntax", warning);
        Assert.Contains("broken.cui", warning);

        var roundTrip = Assert.Single(ReadBundle(WriteBundle(entries)));
        Assert.Equal("broken.cui", roundTrip.LogicalName);
        Assert.Equal(File.ReadAllBytes(path), roundTrip.Data);
    }

    [Fact]
    public void UnclosedElementWarnsAndIsStillEmbedded()
    {
        var path = WriteResource("unclosed.cui", "<Cui><Text>Open</Paragraph></Cui>");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Single(_log.Warnings);
        Assert.Equal(File.ReadAllBytes(path), Assert.Single(entries).Data);
    }

    [Fact]
    public void PrefabDefinitionDeclarationIsValidForItsProjectItemKind()
    {
        var path = WriteResource("aibar.cui-prefab", "<Prefab (Definition) Type=\"dynamic\" ID=\"AIBar\"><Text ID=\"t\">x</Text></Prefab>");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Empty(_log.Warnings);
        Assert.Equal("aibar.cui-prefab", Assert.Single(entries).LogicalName);
    }

    [Fact]
    public void WrongRootElementStillWarns()
    {
        var path = WriteResource("bar.cui-prefab", "<Cui><Prefab (Definition) ID=\"AIBar\"/></Cui>");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        var warning = Assert.Single(_log.Warnings);
        Assert.Contains("<Prefab> root element", warning);
        Assert.Equal(File.ReadAllBytes(path), Assert.Single(entries).Data);
    }

    [Fact]
    public void BinaryResourcesAreEmbeddedWithoutLexicalValidation()
    {
        var bytes = new byte[] { 0, 1, 2, 3, 255, 254 };
        var path = Path.Combine(_root, "font.ttf");
        File.WriteAllBytes(path, bytes);

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Empty(_log.Warnings);
        Assert.Equal(bytes, Assert.Single(entries).Data);
    }

    [Fact]
    public void MissingResourceIsSkippedWithoutWarning()
    {
        var entries = CuiResourceBundle.Collect(_root, new[] { Path.Combine(_root, "absent.cui") }, _log);

        Assert.Empty(entries);
        Assert.Empty(_log.Warnings);
    }

    private string WriteResource(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private string WriteBundle(List<CuiResourceEntry> entries)
    {
        var output = Path.Combine(_root, "bundle.bin");
        CuiResourceBundle.Write(output, entries, _log);
        return output;
    }

    private static List<CuiResourceEntry> ReadBundle(string output)
    {
        using var stream = new FileStream(output, FileMode.Open, FileAccess.Read);
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8);

        var result = new List<CuiResourceEntry>();
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
            result.Add(new CuiResourceEntry(reader.ReadString(), reader.ReadBytes(reader.ReadInt32())));

        return result;
    }

    private sealed class RecordingLog : ICuiResourceLog
    {
        public List<string> Warnings { get; } = [];

        public void LogWarning(string message) => Warnings.Add(message);

        public void LogMessage(MessageImportance importance, string message)
        {
        }
    }
}
