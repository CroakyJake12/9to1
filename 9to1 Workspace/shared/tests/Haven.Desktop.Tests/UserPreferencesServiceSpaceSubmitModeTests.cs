using Haven.Application;
using Haven.Desktop;

namespace Haven.Desktop.Tests;

public sealed class UserPreferencesServiceSpaceSubmitModeTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public void Per_space_submit_mode_persists_isolates_and_restores_applicable_defaults()
    {
        var researchSpace = Guid.NewGuid();
        var chatSpace = Guid.NewGuid();
        var unrelatedSpace = Guid.NewGuid();
        var preferences = new UserPreferencesService(_paths);

        Assert.Equal(SpaceSubmitMode.Steer, preferences.GetSpaceSubmitMode(researchSpace, isResearch: true));
        Assert.Null(preferences.GetSpaceSubmitMode(chatSpace, isResearch: false)); // preserve DEC-006 outside Research

        preferences.SetSpaceSubmitMode(researchSpace, SpaceSubmitMode.Queue);
        preferences.SetSpaceSubmitMode(chatSpace, SpaceSubmitMode.Steer);
        var reopened = new UserPreferencesService(_paths);

        Assert.Equal(SpaceSubmitMode.Queue, reopened.GetSpaceSubmitMode(researchSpace, isResearch: true));
        Assert.Equal(SpaceSubmitMode.Steer, reopened.GetSpaceSubmitMode(chatSpace, isResearch: false));
        Assert.Null(reopened.GetSpaceSubmitMode(unrelatedSpace, isResearch: false));
        Assert.Equal(SpaceSubmitMode.Steer, reopened.GetSpaceSubmitMode(unrelatedSpace, isResearch: true));

        Assert.True(reopened.ClearSpaceSubmitMode(researchSpace));
        Assert.False(reopened.ClearSpaceSubmitMode(researchSpace));
        Assert.Equal(SpaceSubmitMode.Steer, reopened.GetSpaceSubmitMode(researchSpace, isResearch: true));
        Assert.True(reopened.ClearSpaceSubmitMode(chatSpace));
        Assert.Null(reopened.GetSpaceSubmitMode(chatSpace, isResearch: false));
    }

    [Fact]
    public void Existing_preferences_without_space_modes_keep_their_values_and_receive_no_nonresearch_default()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var preferencePath = Path.Combine(_paths.DataDirectory, "preferences.json");
        File.WriteAllText(preferencePath, "{\"defaultModel\":\"existing-model\",\"defaultEffort\":\"High\"}");
        var preferences = new UserPreferencesService(_paths);
        var space = Guid.NewGuid();

        Assert.Equal("existing-model", preferences.DefaultModel);
        Assert.Equal("High", preferences.DefaultEffort.ToString());
        Assert.Equal(SpaceSubmitMode.Steer, preferences.GetSpaceSubmitMode(space, isResearch: true));
        Assert.Null(preferences.GetSpaceSubmitMode(space, isResearch: false));
    }

    [Fact]
    public void Invalid_space_ids_and_unsupported_modes_are_rejected()
    {
        var preferences = new UserPreferencesService(_paths);
        Assert.Throws<ArgumentException>(() => preferences.GetSpaceSubmitMode(Guid.Empty, isResearch: true));
        Assert.Throws<ArgumentException>(() => preferences.SetSpaceSubmitMode(Guid.Empty, SpaceSubmitMode.Steer));
        Assert.Throws<ArgumentOutOfRangeException>(() => preferences.SetSpaceSubmitMode(Guid.NewGuid(), (SpaceSubmitMode)99));
    }

    [Fact]
    public void Unknown_saved_mode_and_future_fields_survive_other_preference_updates()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var space = Guid.NewGuid();
        var preferencePath = Path.Combine(_paths.DataDirectory, "preferences.json");
        File.WriteAllText(preferencePath,
            $"{{\"spaceSubmitModes\":{{\"{space:D}\":99}},\"futureHomeSetting\":{{\"version\":37,\"payload\":\"keep\"}}}}");
        var preferences = new UserPreferencesService(_paths);

        Assert.Equal(SpaceSubmitMode.Steer, preferences.GetSpaceSubmitMode(space, isResearch: true));
        Assert.Null(preferences.GetSpaceSubmitMode(space, isResearch: false));
        preferences.SetDefaultTabAppKey("keep-existing-preference");

        using var saved = System.Text.Json.JsonDocument.Parse(File.ReadAllText(preferencePath));
        Assert.Equal(99, saved.RootElement.GetProperty("spaceSubmitModes").GetProperty(space.ToString("D")).GetInt32());
        Assert.Equal("keep", saved.RootElement.GetProperty("futureHomeSetting").GetProperty("payload").GetString());
    }

    [Fact]
    public void Unknown_future_mode_name_is_reported_as_corrupt_and_left_untouched()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var space = Guid.NewGuid();
        var preferencePath = Path.Combine(_paths.DataDirectory, "preferences.json");
        var original = $"{{\"defaultModel\":\"preserve\",\"spaceSubmitModes\":{{\"{space:D}\":\"Pause\"}}}}";
        File.WriteAllText(preferencePath, original);

        var preferences = new UserPreferencesService(_paths);

        Assert.Equal(UserPreferenceRecoveryState.Corrupt, preferences.RecoveryState);
        Assert.Equal(original, File.ReadAllText(preferencePath));
        Assert.Equal(SpaceSubmitMode.Steer, preferences.GetSpaceSubmitMode(space, isResearch: true));
        Assert.Null(preferences.GetSpaceSubmitMode(space, isResearch: false));
    }

    [Fact]
    public void Failed_persistence_does_not_publish_an_uncommitted_space_override()
    {
        var preferences = new UserPreferencesService(_paths);
        var space = Guid.NewGuid();
        var preferencePath = Path.Combine(_paths.DataDirectory, "preferences.json");
        // Simulate another preferences owner committing this same key after our baseline read.
        File.WriteAllText(preferencePath, $"{{\"spaceSubmitModes\":{{\"{space:D}\":1}}}}");

        var failure = Record.Exception(() => preferences.SetSpaceSubmitMode(space, SpaceSubmitMode.Queue));
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Null(preferences.GetSpaceSubmitMode(space, isResearch: false));
        Assert.Equal(SpaceSubmitMode.Steer, preferences.GetSpaceSubmitMode(space, isResearch: true));
        var reopened = new UserPreferencesService(_paths);
        Assert.Equal(SpaceSubmitMode.Steer, reopened.GetSpaceSubmitMode(space, isResearch: false));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-space-submit-preference-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
