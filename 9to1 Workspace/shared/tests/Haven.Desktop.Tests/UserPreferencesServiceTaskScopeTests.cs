using System.Text.Json;
using Haven.Application;

namespace Haven.Desktop.Tests;

public sealed class UserPreferencesServiceTaskScopeTests : IDisposable
{
    private readonly Paths _paths = new();
    [Fact]
    public void Shown_tasks_defaults_to_task_space_only_and_persists_explicit_changes()
    {
        var preferences = new UserPreferencesService(_paths);
        Assert.Equal(TaskShownScope.TaskSpaceOnly, preferences.ShownTasks);
        preferences.SetShownTasks(TaskShownScope.AllTasks);
        Assert.Equal(TaskShownScope.AllTasks, new UserPreferencesService(_paths).ShownTasks);
        preferences.SetShownTasks(TaskShownScope.TaskSpaceOnly);
        Assert.Equal(TaskShownScope.TaskSpaceOnly, new UserPreferencesService(_paths).ShownTasks);
    }
    [Fact]
    public void Task_scope_is_isolated_to_the_existing_preference_store_and_preserves_other_space_settings()
    {
        using var other = new Paths();
        var first = new UserPreferencesService(_paths);
        var second = new UserPreferencesService(other);
        var space = Guid.NewGuid();
        first.SetSpaceSubmitMode(space, SpaceSubmitMode.Queue);
        first.SetShownTasks(TaskShownScope.AllTasks);
        Assert.Equal(TaskShownScope.TaskSpaceOnly, second.ShownTasks);
        Assert.Equal(SpaceSubmitMode.Queue, new UserPreferencesService(_paths).GetSpaceSubmitMode(space, false));
        Assert.Null(second.GetSpaceSubmitMode(space, false));
    }
    [Fact]
    public void Unsupported_saved_scope_is_not_applied_and_survives_unrelated_preference_saves()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var path = Path.Combine(_paths.DataDirectory, "preferences.json");
        File.WriteAllText(path, "{\"shownTasks\":99,\"futureOwnerPreference\":{\"keep\":true}}");
        var preferences = new UserPreferencesService(_paths);
        Assert.Equal(TaskShownScope.TaskSpaceOnly, preferences.ShownTasks);
        preferences.SetDefaultTabAppKey("keep-owner-choice");
        using var saved = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(99, saved.RootElement.GetProperty("shownTasks").GetInt32());
        Assert.True(saved.RootElement.GetProperty("futureOwnerPreference").GetProperty("keep").GetBoolean());
        Assert.Throws<ArgumentOutOfRangeException>(() => preferences.SetShownTasks((TaskShownScope)99));
    }
    public void Dispose() => _paths.Dispose();
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "tasks-scope-preferences-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
