using Haven.Application;
using HavenOS.Apps.Browse;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed class BrowseEnginePreferencesStoreTests
{
    [Fact]
    public async Task VersionedPreferencesRoundTripDefaultSiteAndTabChoices()
    {
        using var paths = new TestPaths();
        var store = new BrowseEnginePreferencesStore(paths);
        var tabId = Guid.NewGuid();
        var expected = new BrowseEnginePreferences(
            BrowseEngineKind.Chromium,
            new Dictionary<string, BrowseEngineKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["example.test"] = BrowseEngineKind.Gecko
            },
            new Dictionary<Guid, BrowseEngineKind> { [tabId] = BrowseEngineKind.Chromium });

        await store.SaveAsync(expected);
        var restored = await store.LoadAsync();

        Assert.Equal(BrowseEngineKind.Chromium, restored.DefaultEngine);
        Assert.Equal(BrowseEngineKind.Gecko, restored.SiteOverrides["EXAMPLE.TEST"]);
        Assert.Equal(BrowseEngineKind.Chromium, restored.TabOverrides![tabId]);
    }

    [Fact]
    public async Task InvalidStoredPreferencesFallBackWithoutOverwritingTheFile()
    {
        using var paths = new TestPaths();
        var path = Path.Combine(paths.DataDirectory, "browse-engine-preferences.json");
        const string invalid = "{ not valid json";
        await File.WriteAllTextAsync(path, invalid);

        var restored = await new BrowseEnginePreferencesStore(paths).LoadAsync();

        Assert.Equal(BrowseEngineKind.Gecko, restored.DefaultEngine);
        Assert.Equal(invalid, await File.ReadAllTextAsync(path));
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "browse-engine-preferences-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "state.json");

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
