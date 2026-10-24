using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class PrivacyPreferenceStoreTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task DefaultsDisableLearningAndSharingAndPersistLocalOnlyChoice()
    {
        var store = new PrivacyPreferenceStore(_paths);
        Assert.False(store.Current.BackgroundLearningEnabled);
        Assert.False(store.Current.BackgroundLearningCloudDisclosureEnabled);
        Assert.False(store.Current.ModelImprovementSharingEnabled);

        var allowedApps = new List<string> { "app.docs" };
        await store.UpdateAsync(
            store.Current with
            {
                LocalOnlyMode = true,
                BackgroundLearningEnabled = true,
                BackgroundLearningCloudDisclosureEnabled = true,
                BackgroundLearningPolicy = new BackgroundLearningContributorPolicy(
                    RestrictApps: true, AllowedAppIds: allowedApps,
                    RestrictProjects: true, AllowedProjectIds: ["project-a"],
                    AllowCrossDeviceSync: false)
            },
            CancellationToken.None);

        allowedApps[0] = "app.mail";
        Assert.True(store.Current.BackgroundLearningPolicy.Allows("app.docs", "project-a"));
        Assert.False(store.Current.BackgroundLearningPolicy.Allows("app.mail", "project-a"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)store.Current.BackgroundLearningPolicy.AllowedAppIds!).Add("app.mail"));

        var restarted = new PrivacyPreferenceStore(_paths);
        Assert.True(restarted.Current.LocalOnlyMode);
        Assert.True(restarted.Current.BackgroundLearningEnabled);
        Assert.True(restarted.Current.BackgroundLearningCloudDisclosureEnabled);
        Assert.False(restarted.Current.BackgroundLearningPolicy.AllowCrossDeviceSync);
        Assert.True(restarted.Current.BackgroundLearningPolicy.Allows("app.docs", "project-a"));
        Assert.False(restarted.Current.BackgroundLearningPolicy.Allows("app.mail", "project-a"));
        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)restarted.Current.BackgroundLearningPolicy.AllowedAppIds!).Add("app.mail"));
        Assert.False(restarted.Current.BackgroundLearningPolicy.Allows("app.docs", "project-b"));
        Assert.False(restarted.Current.ModelImprovementSharingEnabled);
        Assert.True(restarted.Current.UpdatedAt > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void Null_contributor_policy_recovers_fail_closed_without_rewriting_the_original_file()
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        const string original = "{\"backgroundLearningEnabled\":true,\"backgroundLearningCloudDisclosureEnabled\":true,\"backgroundLearningPolicy\":null}";
        File.WriteAllText(Path.Combine(_paths.DataDirectory, "privacy-preferences.json"), original);

        var recovered = new PrivacyPreferenceStore(_paths);

        Assert.False(recovered.Current.BackgroundLearningEnabled);
        Assert.False(recovered.Current.BackgroundLearningCloudDisclosureEnabled);
        Assert.Equal(original, File.ReadAllText(Path.Combine(_paths.DataDirectory, "privacy-preferences.json")));
    }

    public void Dispose() => _paths.Dispose();

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-privacy-tests-" + Guid.NewGuid().ToString("N"));
            DatabasePath = Path.Combine(DataDirectory, "haven.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }
        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }
        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

public sealed class LocalMotionPreferenceRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "haven-motion-preference-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Missing_invalid_oversized_and_failed_save_observations_are_bounded_and_conservative()
    {
        var absentPath = Path.Combine(_directory, "absent", "prefs.json");
        var absent = new LocalMotionPreferencesService(absentPath);
        var missingObservation = await absent.ReadAsync();
        Assert.True(missingObservation.IsAvailable);
        Assert.False(missingObservation.ReduceAnimations);

        var writablePath = Path.Combine(_directory, "writable", "prefs.json");
        var writable = new LocalMotionPreferencesService(writablePath);
        var successfulWrites = 0;
        writable.Changed += (_, _) => successfulWrites++;
        writable.SetReduceAnimations(true);
        Assert.True(writable.ReduceAnimations);
        Assert.Equal(1, writable.ObservationRevision);
        Assert.Equal(1, successfulWrites);
        Assert.True(new LocalMotionPreferencesService(writablePath).ReduceAnimations);

        Directory.CreateDirectory(_directory);
        var malformedPath = Path.Combine(_directory, "malformed.json");
        File.WriteAllText(malformedPath, "{");
        var malformed = new LocalMotionPreferencesService(malformedPath);
        Assert.True(malformed.ReduceAnimations);
        var malformedObservation = await malformed.ReadAsync();
        Assert.False(malformedObservation.IsAvailable);
        Assert.True(malformedObservation.ReduceAnimations);

        var cachedPath = Path.Combine(_directory, "cached.json");
        File.WriteAllText(cachedPath, "{\"reduceAnimations\":false}");
        var cached = new LocalMotionPreferencesService(cachedPath);
        File.WriteAllText(cachedPath, "{");
        var cachedRevision = cached.ObservationRevision;
        var failedObservation = await cached.ReadAsync();
        Assert.False(failedObservation.IsAvailable);
        Assert.False(failedObservation.ReduceAnimations);
        Assert.Equal(cachedRevision, cached.ObservationRevision);

        var nullPath = Path.Combine(_directory, "null.json");
        File.WriteAllText(nullPath, "null");
        var nullPreferences = new LocalMotionPreferencesService(nullPath);
        Assert.True(nullPreferences.ReduceAnimations);
        Assert.False((await nullPreferences.ReadAsync()).IsAvailable);
        File.WriteAllText(cachedPath, "null");
        var nullObservation = await cached.ReadAsync();
        Assert.False(nullObservation.IsAvailable);
        Assert.False(nullObservation.ReduceAnimations);
        Assert.Equal(cachedRevision, cached.ObservationRevision);

        var oversizedPath = Path.Combine(_directory, "oversized.json");
        File.WriteAllText(oversizedPath, new string('x', 65_537));
        var oversized = new LocalMotionPreferencesService(oversizedPath);
        Assert.True(oversized.ReduceAnimations);
        Assert.False((await oversized.ReadAsync()).IsAvailable);

        var inaccessiblePath = Path.Combine(_directory, "unreadable-directory");
        Directory.CreateDirectory(inaccessiblePath);
        var failedSave = new LocalMotionPreferencesService(inaccessiblePath);
        Assert.True(failedSave.ReduceAnimations);
        var revision = failedSave.ObservationRevision;
        var notifications = 0;
        failedSave.Changed += (_, _) => notifications++;
        failedSave.SetReduceAnimations(false);
        Assert.True(failedSave.ReduceAnimations);
        Assert.Equal(revision, failedSave.ObservationRevision);
        Assert.Equal(0, notifications);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
