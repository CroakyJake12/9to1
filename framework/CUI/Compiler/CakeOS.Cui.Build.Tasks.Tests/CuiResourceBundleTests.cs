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

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(_root, recursive: true);
    }

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
    public void SnapshotBytesKeepTheOriginalResourceIdentity()
    {
        var original = WriteResource("UI/Page.cui", "<Page/>");
        var snapshot = WriteResource("obj/Cui/sources/hash.cui", "<Page/>");
        File.WriteAllText(original, "changed after compilation");
        var entries = CuiResourceBundle.CollectSources(_root, [new CuiResourceSource(snapshot, original)], _log);
        var entry = Assert.Single(entries);
        Assert.Equal("UI.Page.cui", entry.LogicalName);
        Assert.Equal(File.ReadAllBytes(snapshot), entry.Data);
        Assert.Empty(_log.Warnings);
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
    public void BytePackagingDoesNotClaimSemanticValidity()
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
    public void BytePackagingPreservesMalformedInputWithoutPretendingToCompileIt()
    {
        var path = WriteResource("broken.cui", "<Cui><Text (Definition</Text></Cui>");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Empty(_log.Warnings);

        var roundTrip = Assert.Single(ReadBundle(WriteBundle(entries)));
        Assert.Equal("broken.cui", roundTrip.LogicalName);
        Assert.Equal(File.ReadAllBytes(path), roundTrip.Data);
    }

    [Fact]
    public void BytePackagingDoesNotRunADuplicateParser()
    {
        var path = WriteResource("unclosed.cui", "<Cui><Text>Open</Paragraph></Cui>");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Empty(_log.Warnings);
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
    public void BytePackagingDoesNotApplyAnObsoleteRootRule()
    {
        var path = WriteResource("bar.cui-prefab", "<Cui><Prefab (Definition) ID=\"AIBar\"/></Cui>");

        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        Assert.Empty(_log.Warnings);
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

    [Fact]
    public void DuplicateLogicalNameIsReportedAsAnErrorAndTheFirstEntryIsKept()
    {
        var nested = WriteResource("nested/callout.cui", "<Cui><Text>Nested</Text></Cui>");
        var flattened = WriteResource("nested.callout.cui", "<Cui><Text>Flattened</Text></Cui>");

        var entries = CuiResourceBundle.Collect(_root, new[] { nested, flattened }, _log);

        var error = Assert.Single(_log.Errors);
        Assert.Contains("Duplicate CUI resource logical name", error);
        Assert.Contains("nested.callout.cui", error);
        Assert.Contains(Path.Combine("nested", "callout.cui"), error);

        var entry = Assert.Single(entries);
        Assert.Equal("nested.callout.cui", entry.LogicalName);
        Assert.Equal(File.ReadAllBytes(nested), entry.Data);
    }

    [Fact]
    public void SuccessfulWriteStagesInDestinationDirectoryAndLeavesOnlyTheBundle()
    {
        var path = WriteResource("panel.cui", "<Cui><Text>Panel</Text></Cui>");
        var entries = CuiResourceBundle.Collect(_root, new[] { path }, _log);

        var directory = Path.Combine(_root, "out");
        var output = Path.Combine(directory, "bundle.bin");
        CuiResourceBundle.Write(output, entries, _log);

        var roundTrip = Assert.Single(ReadBundle(output));
        Assert.Equal("panel.cui", roundTrip.LogicalName);
        Assert.Equal(File.ReadAllBytes(path), roundTrip.Data);
        Assert.Equal(new[] { "bundle.bin" }, FileNames(directory));
    }

    [Fact]
    public void OverwritingAnExistingBundleReplacesItAndLeavesNoStagingFile()
    {
        var directory = Path.Combine(_root, "out");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "bundle.bin");

        CuiResourceBundle.Write(output, new[] { new CuiResourceEntry("first.cui", new byte[] { 1 }) }, _log);
        CuiResourceBundle.Write(output, new[] { new CuiResourceEntry("second.cui", new byte[] { 2, 3 }) }, _log);

        var roundTrip = Assert.Single(ReadBundle(output));
        Assert.Equal("second.cui", roundTrip.LogicalName);
        Assert.Equal(new byte[] { 2, 3 }, roundTrip.Data);
        Assert.Equal(new[] { "bundle.bin" }, FileNames(directory));
    }

    [Fact]
    public void BundleBytesMatchTheDocumentedFormatExactly()
    {
        var directory = Path.Combine(_root, "out");
        var output = Path.Combine(directory, "bundle.bin");

        CuiResourceBundle.Write(output, new[] { new CuiResourceEntry("Home.cui", new byte[] { 60, 67, 117, 105, 62 }) }, _log);

        var expected = new byte[]
        {
            1, 0, 0, 0, 8,
            (byte)'H', (byte)'o', (byte)'m', (byte)'e', (byte)'.', (byte)'c', (byte)'u', (byte)'i',
            5, 0, 0, 0,
            60, 67, 117, 105, 62,
        };

        Assert.Equal(expected, File.ReadAllBytes(output));
        Assert.Equal(new[] { "bundle.bin" }, FileNames(directory));
    }

    [Fact]
    public void FailedSerializationPreservesThePriorBundleAndRemovesTheStagingFile()
    {
        var directory = Path.Combine(_root, "out");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "bundle.bin");

        CuiResourceBundle.Write(output, new[] { new CuiResourceEntry("prior.cui", new byte[] { 1, 2, 3 }) }, _log);
        var prior = File.ReadAllBytes(output);

        Assert.ThrowsAny<Exception>(() =>
            CuiResourceBundle.Write(output, new[] { new CuiResourceEntry(null!, new byte[] { 4, 5 }) }, _log));

        Assert.Equal(prior, File.ReadAllBytes(output));
        Assert.Equal(new[] { "bundle.bin" }, FileNames(directory));

        var roundTrip = Assert.Single(ReadBundle(output));
        Assert.Equal("prior.cui", roundTrip.LogicalName);
        Assert.Equal(new byte[] { 1, 2, 3 }, roundTrip.Data);
    }

    [Fact]
    public void FailedReplacementPreservesThePriorBundleAndRemovesTheStagingFile()
    {
        var directory = Path.Combine(_root, "out");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "bundle.bin");

        CuiResourceBundle.Write(output, new[] { new CuiResourceEntry("prior.cui", new byte[] { 1, 2, 3 }) }, _log);
        var prior = File.ReadAllBytes(output);

        File.SetAttributes(output, FileAttributes.ReadOnly);
        File.SetAttributes(directory, FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            using (var held = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Action write = () => CuiResourceBundle.Write(output, new[] { new CuiResourceEntry("next.cui", new byte[] { 4 }) }, _log);
                // Windows rejects replacement of the locked destination. Unix
                // rejects staging creation in the deliberately unwritable directory.
                if (OperatingSystem.IsWindows()) Assert.ThrowsAny<IOException>(write);
                else Assert.Throws<UnauthorizedAccessException>(write);
            }

            Assert.Equal(prior, File.ReadAllBytes(output));
            Assert.Equal(new[] { "bundle.bin" }, FileNames(directory));

            var roundTrip = Assert.Single(ReadBundle(output));
            Assert.Equal("prior.cui", roundTrip.LogicalName);
            Assert.Equal(new byte[] { 1, 2, 3 }, roundTrip.Data);
        }
        finally
        {
            File.SetAttributes(output, FileAttributes.Normal);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else
                File.SetAttributes(directory, FileAttributes.Normal);
        }
    }

    [Fact]
    public void FailedWriteToANewDestinationLeavesNoPartialBundle()
    {
        var directory = Path.Combine(_root, "new");
        var output = Path.Combine(directory, "bundle.bin");

        Assert.ThrowsAny<Exception>(() =>
            CuiResourceBundle.Write(output, new[] { new CuiResourceEntry(null!, new byte[] { 1 }) }, _log));

        Assert.False(File.Exists(output));
        Assert.Empty(FileNames(directory));
    }

    private static string[] FileNames(string directory) =>
        Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    private string WriteResource(string name, string content)
    {
        var path = Path.Combine(_root, name);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
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

        public List<string> Errors { get; } = [];

        public void LogWarning(string message) => Warnings.Add(message);

        public void LogError(string message) => Errors.Add(message);

        public void LogMessage(MessageImportance importance, string message)
        {
        }
    }
}
