using System.Collections;
using CakeOS.Cui.Build.Tasks;
using Microsoft.Build.Framework;
using Xunit;

namespace CakeOS.Cui.Build.Tasks.Tests;

public sealed class GenerateCuiResourcesTaskTests : IDisposable
{
    private readonly string _prefix;
    private readonly FakeBuildEngine _engine = new();

    public GenerateCuiResourcesTaskTests()
    {
        _prefix = Path.Combine(Path.GetTempPath(), "cakeos-cui-build-tasks-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_prefix);
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_prefix, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(_prefix, recursive: true);
    }

    [Fact]
    public void RootWithoutTrailingSeparatorIsARealDirectoryBoundary()
    {
        var root = Path.Combine(_prefix, "repo");
        Directory.CreateDirectory(root);
        var inside = WriteResource(root, "callout.cui", "<Cui><Text>Inside</Text></Cui>");

        var entries = RunTask(root, inside);

        Assert.Empty(_engine.RecordedWarnings);
        var entry = Assert.Single(entries);
        Assert.Equal("callout.cui", entry.LogicalName);
        Assert.Equal(File.ReadAllBytes(inside), entry.Data);
    }

    [Fact]
    public void RootWithTrailingSeparatorIsTheSameBoundary()
    {
        var root = Path.Combine(_prefix, "repo");
        Directory.CreateDirectory(root);
        var inside = WriteResource(root, "nested/callout.cui", "<Cui><Text>Inside</Text></Cui>");

        var entries = RunTask(root + Path.DirectorySeparatorChar, inside);

        Assert.Empty(_engine.RecordedWarnings);
        var entry = Assert.Single(entries);
        Assert.Equal("nested.callout.cui", entry.LogicalName);
        Assert.Equal(File.ReadAllBytes(inside), entry.Data);
    }

    [Fact]
    public void SimilarlyPrefixedSiblingIsNotInsideRoot()
    {
        var root = Path.Combine(_prefix, "repo");
        var sibling = Path.Combine(_prefix, "repo-other");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);

        var inside = WriteResource(root, "callout.cui", "<Cui><Text>Inside</Text></Cui>");
        var outside = WriteResource(sibling, "callout.cui", "<Cui><Text>Outside</Text></Cui>");

        var entries = RunTask(root, inside, outside);

        Assert.Equal("callout.cui", entries[0].LogicalName);

        var siblingEntry = entries[1];
        Assert.NotEqual("repo-other.callout.cui", siblingEntry.LogicalName);
        Assert.Equal(Path.GetFullPath(outside).Replace('\\', '.').Replace('/', '.'), siblingEntry.LogicalName);
        Assert.Equal(File.ReadAllBytes(outside), siblingEntry.Data);

        var warning = Assert.Single(_engine.RecordedWarnings);
        Assert.Contains("outside the CUI resource root", warning);
        Assert.Contains("repo-other", warning);
    }

    [Fact]
    public void DuplicateLogicalNameIsRejectedBeforeTheOutputBundleIsReplaced()
    {
        var root = Path.Combine(_prefix, "repo");
        Directory.CreateDirectory(root);

        var nested = WriteResource(root, "nested/callout.cui", "<Cui><Text>Nested</Text></Cui>");
        var flattened = WriteResource(root, "nested.callout.cui", "<Cui><Text>Flattened</Text></Cui>");

        var output = Path.Combine(_prefix, "bundle.bin");
        var existingBundle = new byte[] { 1, 2, 3, 4 };
        File.WriteAllBytes(output, existingBundle);

        var task = CreateTask(root, output, nested, flattened);

        Assert.False(task.Execute());

        var error = Assert.Single(_engine.RecordedErrors);
        Assert.Contains("Duplicate CUI resource logical name", error);
        Assert.Contains("nested.callout.cui", error);
        Assert.Contains(Path.Combine("nested", "callout.cui"), error);
        Assert.Contains("unique logical names", error);

        Assert.Equal(existingBundle, File.ReadAllBytes(output));
    }

    [Fact]
    public void TheSameResourceListedTwiceIsRejectedAsADuplicateIdentity()
    {
        var root = Path.Combine(_prefix, "repo");
        Directory.CreateDirectory(root);
        var resource = WriteResource(root, "callout.cui", "<Cui><Text>Shared</Text></Cui>");

        var task = CreateTask(root, Path.Combine(_prefix, "bundle.bin"), resource, resource);

        Assert.False(task.Execute());

        var error = Assert.Single(_engine.RecordedErrors);
        Assert.Contains("Duplicate CUI resource logical name", error);
        Assert.Contains("callout.cui", error);
    }

    [Fact]
    public void ValidMultiResourceBundleIsGeneratedWithEveryOriginalBytePreserved()
    {
        var root = Path.Combine(_prefix, "repo");
        Directory.CreateDirectory(root);

        var home = WriteResource(root, "Home.cui", "<Cui><Text>Home</Text></Cui>");
        var theme = WriteResource(root, "Themes/Dark.cui-theme", "<Theme><Color Name=\"Light_Green\">#8FCD6A</Color></Theme>");
        var prefab = WriteResource(root, "Prefabs/AIBar.cui-prefab", "<Prefab (Definition) Type=\"dynamic\" ID=\"AIBar\"><Text ID=\"t\">x</Text></Prefab>");
        var font = Path.Combine(root, "Assets", "font.ttf");
        Directory.CreateDirectory(Path.GetDirectoryName(font)!);
        var fontBytes = new byte[] { 0, 1, 2, 3, 255, 254 };
        File.WriteAllBytes(font, fontBytes);

        var entries = RunTask(root, home, theme, prefab, font);

        Assert.Empty(_engine.RecordedWarnings);
        Assert.Empty(_engine.RecordedErrors);
        Assert.Equal(4, entries.Count);

        Assert.Equal("Home.cui", entries[0].LogicalName);
        Assert.Equal("Themes.Dark.cui-theme", entries[1].LogicalName);
        Assert.Equal("Prefabs.AIBar.cui-prefab", entries[2].LogicalName);
        Assert.Equal("Assets.font.ttf", entries[3].LogicalName);

        Assert.Equal(File.ReadAllBytes(home), entries[0].Data);
        Assert.Equal(File.ReadAllBytes(theme), entries[1].Data);
        Assert.Equal(File.ReadAllBytes(prefab), entries[2].Data);
        Assert.Equal(fontBytes, entries[3].Data);
    }

    [Fact]
    public void ExistingWarningsAreNotPromotedIntoErrorsAndTheBundleIsStillGenerated()
    {
        var root = Path.Combine(_prefix, "repo");
        var sibling = Path.Combine(_prefix, "repo-other");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);

        var inside = WriteResource(root, "panel.cui", "<Cui><Text>Inside</Text></Cui>");
        var malformed = WriteResource(root, "broken.cui", "<Cui><Text (Definition</Text></Cui>");
        var outside = WriteResource(sibling, "extra.cui", "<Cui><Text>Outside</Text></Cui>");

        var entries = RunTask(root, inside, malformed, outside);

        Assert.Equal(3, entries.Count);
        Assert.Equal(2, _engine.RecordedWarnings.Count);
        Assert.Empty(_engine.RecordedErrors);
        Assert.Contains("invalid CUI syntax", _engine.RecordedWarnings[0]);
        Assert.Contains("outside the CUI resource root", _engine.RecordedWarnings[1]);
        Assert.Equal(File.ReadAllBytes(malformed), entries[1].Data);
        Assert.Equal(File.ReadAllBytes(outside), entries[2].Data);
    }

    [Fact]
    public void FailedReplacementThroughTheTaskPreservesTheExistingBundleAndLeavesNoStagingFile()
    {
        var root = Path.Combine(_prefix, "repo");
        Directory.CreateDirectory(root);
        var resource = WriteResource(root, "callout.cui", "<Cui><Text>Shared</Text></Cui>");

        var directory = Path.Combine(_prefix, "out");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "bundle.bin");

        Assert.True(CreateTask(root, output, resource).Execute());
        var prior = File.ReadAllBytes(output);

        File.SetAttributes(output, FileAttributes.ReadOnly);
        File.SetAttributes(directory, FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            using (var held = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.ThrowsAny<IOException>(() => CreateTask(root, output, resource).Execute());

            Assert.Equal(prior, File.ReadAllBytes(output));
            Assert.Equal(new[] { "bundle.bin" }, FileNames(directory));

            var roundTrip = Assert.Single(ReadBundle(output));
            Assert.Equal("callout.cui", roundTrip.LogicalName);
            Assert.Equal(File.ReadAllBytes(resource), roundTrip.Data);
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

    private static string[] FileNames(string directory) =>
        Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray();

    private GenerateCuiResourcesTask CreateTask(string root, string output, params string[] fullPaths)
    {
        return new GenerateCuiResourcesTask
        {
            BuildEngine = _engine,
            Root = root,
            Output = output,
            Resources = fullPaths.Select(path => (ITaskItem)new FakeTaskItem(path)).ToArray(),
        };
    }

    private List<CuiResourceEntry> RunTask(string root, params string[] fullPaths)
    {
        var task = CreateTask(root, Path.Combine(_prefix, "bundle.bin"), fullPaths);

        Assert.True(task.Execute());
        return ReadBundle(task.Output);
    }

    private static string WriteResource(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
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

    private sealed class FakeTaskItem : ITaskItem
    {
        private readonly Dictionary<string, string> _metadata = new();

        public FakeTaskItem(string itemSpec) => ItemSpec = itemSpec;

        public string ItemSpec { get; set; }

        public int ColumnNumber => 0;

        public int LineNumber => 0;

        public int MetadataCount => _metadata.Count;

        public ICollection MetadataNames => _metadata.Keys;

        public Dictionary<string, string> GetMetadata() => _metadata;

        public string GetMetadata(string metadataName)
            => metadataName == "FullPath" ? Path.GetFullPath(ItemSpec)
                : _metadata.TryGetValue(metadataName, out var value) ? value : string.Empty;

        public bool HasMetadata(string metadataName) => metadataName == "FullPath" || _metadata.ContainsKey(metadataName);

        public void SetMetadata(string metadataName, string metadataValue) => _metadata[metadataName] = metadataValue;

        public void RemoveMetadata(string metadataName) => _metadata.Remove(metadataName);

        public IDictionary CloneCustomMetadata() => new Hashtable(_metadata);

        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            foreach (var metadata in _metadata)
                destinationItem.SetMetadata(metadata.Key, metadata.Value);
        }

        public string GetPathValue(int partNumber)
        {
            var parts = (Path.GetDirectoryName(ItemSpec) ?? string.Empty)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return partNumber < parts.Length ? parts[partNumber] : Path.GetFileName(ItemSpec);
        }
    }

    private sealed class FakeBuildEngine : IBuildEngine
    {
        public List<string> RecordedWarnings { get; } = [];

        public List<string> RecordedErrors { get; } = [];

        public bool ContinueOnError => false;

        public string Errors => string.Join(Environment.NewLine, RecordedErrors);

        public int WarningsNumber => RecordedWarnings.Count;

        public bool CustomLogging => true;

        public DateTime? ProjectStartTime => null;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public void EmergencyShutdown(string reason)
        {
        }

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;

        public void LogEvent(BuildEventArgs e)
        {
            if (e is BuildWarningEventArgs warning) RecordedWarnings.Add(warning.Message);
            else if (e is BuildErrorEventArgs error) RecordedErrors.Add(error.Message);
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => RecordedErrors.Add(e.Message);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e) => RecordedWarnings.Add(e.Message);
    }
}
