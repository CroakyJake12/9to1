using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    [Fact]
    public async Task Actual_personal_Den_releases_completed_profile_snapshots_while_its_owner_remains_live()
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        var root = Directory.CreateTempSubdirectory("haven-ucml-den-retention-").FullName;
        PrepareOriginalPrivateHomeDirectory(root);
        OriginalAssistantPersonalDenHost? den = null;
        await using var home = new HomeLocalDomainComposition(
            new FileHomeCoreStateStore(Path.Combine(root, "Home", "state.json")),
            new OperatingSystemPrincipalSource(), configureOriginalStores: identity =>
            {
                den = new(new RetentionPaths(root), identity.Profiles);
                return new([den], new Dictionary<Type, object>());
            });
        await home.StartOriginalAsync();
        await using var actualDen = den!;
        var actor = await home.Profiles.GetCurrentAsync(token);
        Assert.NotNull(actor);
        await actualDen.CreateAndBindNewEmptyOriginalAsync(actor, home.LocalStoreOwnership, token);
        var opened = await actualDen.OpenExistingOriginalAsync(token);
        var observed = new List<WeakReference<Task>>();
        for (var index = 0; index < 140; index++)
        {
            var evidence = await actualDen.ReadWithinOriginalSourceAsync(opened.Store.Manifest.DenId,
                body => body(), raw =>
                {
                    if (raw is Task<HomeStateReadResult>) observed.Add(new(raw));
                }, token);
            Assert.NotNull(evidence);
        }

        Assert.True(observed.Count >= 140);
        // Keep the real host and Den alive. Only completed intermediate Home read
        // results should be collectible; process retirement cannot mask retention.
        var live = CountLiveCompletedProfileSnapshots(observed);
        Assert.True(live <= 32, $"The still-live personal Den retained {live} completed Home snapshots after 140 reads.");
        Assert.Null(actualDen.OriginalClose);
        var next = await actualDen.ReadWithinOriginalSourceAsync(opened.Store.Manifest.DenId,
            body => body(), _ => { }, token);
        Assert.NotNull(next);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int CountLiveCompletedProfileSnapshots(List<WeakReference<Task>> observed)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var live = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        foreach (var weak in observed) if (weak.TryGetTarget(out var actual)) live.Add(actual);
        return live.Count;
    }

    private sealed class RetentionPaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(root, "Attachments");
        public string LogsDirectory => Path.Combine(root, "Logs");
        public string LegacyStatePath => Path.Combine(root, "legacy-state.json");
    }
}
