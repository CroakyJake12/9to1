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

    public void Dispose() => Directory.Delete(_prefix, recursive: true);

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

    private List<CuiResourceEntry> RunTask(string root, params string[] fullPaths)
    {
        var task = new GenerateCuiResourcesTask
        {
            BuildEngine = _engine,
            Root = root,
            Output = Path.Combine(_prefix, "bundle.bin"),
            Resources = fullPaths.Select(path => (ITaskItem)new FakeTaskItem(path)).ToArray(),
        };

        Assert.True(task.Execute());
        return ReadBundle(task.Output);
    }

    private static string WriteResource(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
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
