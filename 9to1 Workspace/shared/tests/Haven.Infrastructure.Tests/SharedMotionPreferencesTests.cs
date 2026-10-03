using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class SharedMotionPreferencesTests
{
    [Fact]
    public async Task Existing_boolean_schema_and_writer_are_shared_with_read_only_playback_consumers()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-motion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ui-preferences.json");
            await File.WriteAllTextAsync(path, "{\"reduceAnimations\":true}");
            var owner = new LocalMotionPreferencesService(path);
            var services = new ServiceCollection().AddHavenInfrastructure();
            services.AddSingleton(owner);
            await using var graph = services.BuildServiceProvider();
            var reader = graph.GetRequiredService<IMotionPreferenceSource>();
            var writer = graph.GetRequiredService<IMotionPreferences>();
            Assert.Same(owner, reader); Assert.Same(owner, writer);
            Assert.True(reader.ReduceAnimations);
            var changes = 0; reader.Changed += (_, _) => changes++;
            writer.SetReduceAnimations(false);
            Assert.Equal(1, changes);
            Assert.False((await reader.ReadAsync()).ReduceAnimations);
            var reopened = new LocalMotionPreferencesService(path);
            Assert.False(reopened.ReduceAnimations);
            var json = await File.ReadAllTextAsync(path);
            Assert.Contains("\"reduceAnimations\": false", json, StringComparison.Ordinal);
            Assert.DoesNotContain("profile", json, StringComparison.OrdinalIgnoreCase);
            writer.SetReduceAnimations(false); Assert.Equal(1, changes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Actual_external_changes_are_observed_and_corruption_does_not_become_motion_permission()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-motion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "ui-preferences.json");
            var owner = new LocalMotionPreferencesService(path);
            Assert.Equal(new VisualMotionPreferenceSnapshot(false, true), await owner.ReadAsync());
            var changes = 0; owner.Changed += (_, _) => changes++;
            await File.WriteAllTextAsync(path, "{\"reduceAnimations\":true}");
            Assert.Equal(new VisualMotionPreferenceSnapshot(true, true), await owner.ReadAsync()); Assert.Equal(1, changes);
            await File.WriteAllTextAsync(path, "broken");
            Assert.Equal(new VisualMotionPreferenceSnapshot(true, false), await owner.ReadAsync()); Assert.Equal(1, changes);
            await File.WriteAllTextAsync(path, new string(' ', 65537));
            Assert.Equal(new VisualMotionPreferenceSnapshot(true, false), await owner.ReadAsync());
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.ReadAsync(cancelled.Token).AsTask());
        }
        finally { Directory.Delete(root, true); }
    }
}
