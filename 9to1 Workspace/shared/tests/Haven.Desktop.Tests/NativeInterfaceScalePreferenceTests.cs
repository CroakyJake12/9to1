using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Services;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed class NativeInterfaceScalePreferenceTests
{
    [AvaloniaFact]
    public async Task Scale_preview_is_scoped_to_the_original_view_and_only_a_confirmed_choice_changes_the_profile()
    {
        var directory = Directory.CreateTempSubdirectory("haven-interface-scale-");
        try
        {
            var file = Path.Combine(directory.FullName, "preferences.json");
            var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "scale-owner");
            var saved = HomeThemePreference.Default(scope) with
            {
                Revision = 7,
                Accessibility = new(ReduceMotion: true, DisplayScale: 1.1, InterfaceFontFamily: "Montserrat"),
                Display = new(HomeDisplayMode.Dark, 67, 38),
                Accent = new(HomeThemeAccentOverrideMode.IndividualApps, "#7145FF", new Dictionary<string, string> { ["app.write"] = "#00B89C" })
            };
            var envelope = JsonSerializer.SerializeToNode(saved, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            envelope["accessibility"]!["futurePreference"] = "keep";
            var original = new JsonObject
            {
                [HomeNativePreferenceFile.ThemeKey] = envelope, ["homeThemePreferenceRevision"] = 7,
                ["defaultModel"] = "retained", ["notesEditor"] = new JsonObject { ["fontSize"] = 21 }
            };
            await File.WriteAllTextAsync(file, original.ToJsonString());
            var firstBytes = await File.ReadAllBytesAsync(file);

            // Hold watcher callbacks so the test observes the exact interval between a confirmed
            // file receipt and its independent authorised presentation refresh.
            var callbacks = new ConcurrentQueue<Action>();
            bool authorised = true;
            NativeHomeThemePreferences Open() => new(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
                _ => ValueTask.FromResult(authorised), callbacks.Enqueue);
            var page = new object();
            var otherPage = new object();
            await using (var source = Open())
            {
                var unavailable = source.SettingsBindings();
                Assert.Equal("Unavailable", unavailable["HomeInterfaceScaleText"]);
                Assert.Null(unavailable["HomeHighContrastTarget"]);
                Assert.Null(unavailable["HomeReduceMotionTarget"]);
                await source.RefreshAsync();
                var values = source.SettingsBindings();
                var choices = Assert.IsType<Dictionary<string, object?>[]>(values["HomeInterfaceScaleChoices"]);
                Assert.Equal(new[] { "100%", "125%", "150%", "175%" }, choices.Select(choice => (string)choice["Name"]!));
                Assert.Equal("110%", values["HomeInterfaceScaleText"]);
                Assert.DoesNotContain(choices, choice => choice["IsSelected"] is true);
                var motion = Assert.IsType<Dictionary<string, object?>[]>(values["HomeAccessibilityChoices"]);
                Assert.Same(values["HomeHighContrastTarget"], motion.Single(choice => Equals(choice["Name"], "High contrast"))["Target"]);
                Assert.Same(values["HomeReduceMotionTarget"], motion.Single(choice => Equals(choice["Name"], "Reduce motion"))["Target"]);
                Assert.False(source.Current.Accessibility.HighContrast);
                Assert.True(source.Current.Accessibility.ReduceMotion);

                var published = new List<double>();
                var committedObservations = 0;
                source.Context.Changed += (_, _) => published.Add(source.Context.Current.Accessibility.DisplayScale);
                source.Changed += (_, _) => committedObservations++;
                var scale125 = choices[1]["Target"];
                var scale150 = choices[2]["Target"];
                Assert.True(source.TryPreviewInterfaceScale(page, scale125));
                var retiredChoice = source.CaptureInterfaceScaleDraft(page, scale125);
                Assert.NotNull(retiredChoice);
                Assert.Null(source.CaptureInterfaceScaleDraft(otherPage, scale125));
                Assert.False(source.TryPreviewInterfaceScale(otherPage, scale150));
                source.EndInterfaceScalePreview(otherPage);
                Assert.False(source.TryPreviewInterfaceScale(page, values["HomeHighContrastTarget"]));
                Assert.Equal(1.25, source.Context.Current.Accessibility.DisplayScale);
                Assert.Equal(1.1, source.Current.Accessibility.DisplayScale);
                Assert.Equal(7, source.Current.Revision);
                Assert.Equal(0, committedObservations);
                source.EndInterfaceScalePreview(page);
                Assert.Equal(1.1, source.Context.Current.Accessibility.DisplayScale);
                Assert.False((await source.ApplySettingsAsync(retiredChoice)).Succeeded);
                Assert.False((await source.ApplySettingsAsync(scale125)).Succeeded);
                Assert.True(source.TryPreviewInterfaceScale(otherPage, scale125));
                Assert.False((await source.ApplySettingsAsync(retiredChoice)).Succeeded);
                source.EndInterfaceScalePreview(otherPage);
                Assert.Equal(firstBytes, await File.ReadAllBytesAsync(file));

                Assert.True(source.TryPreviewInterfaceScale(page, scale150));
                authorised = false;
                var refusal = await source.ApplySettingsAsync(source.CaptureInterfaceScaleDraft(page, scale150));
                Assert.False(refusal.Succeeded);
                Assert.Equal("PermissionDenied", refusal.Code);
                source.EndInterfaceScalePreview(page);
                Assert.Equal(firstBytes, await File.ReadAllBytesAsync(file));
                Assert.Equal(1.1, source.Context.Current.Accessibility.DisplayScale);
                authorised = true;
                Assert.True(source.TryPreviewInterfaceScale(page, scale150));
                var beforeSave = published.Count;
                var receipt = await source.ApplySettingsAsync(source.CaptureInterfaceScaleDraft(page, scale150));
                Assert.True(receipt.Succeeded);
                Assert.Equal(saved with { Revision = 8, Accessibility = saved.Accessibility with { DisplayScale = 1.5 } }, receipt.Value!.Preference);
                source.EndInterfaceScalePreview(page);
                Assert.Equal(1.5, source.Context.Current.Accessibility.DisplayScale);
                await source.RefreshAsync();
                Assert.Equal(1.5, source.Current.Accessibility.DisplayScale);
                Assert.All(published.Skip(beforeSave), scale => Assert.Equal(1.5, scale));
                Assert.True(committedObservations > 0);
                var committedBytes = await File.ReadAllBytesAsync(file);
                Assert.False(source.TryPreviewInterfaceScale(page, scale125));
                Assert.False((await source.ApplySettingsAsync(scale125)).Succeeded);
                Assert.Equal(committedBytes, await File.ReadAllBytesAsync(file));
                var next = Assert.IsType<Dictionary<string, object?>[]>(source.SettingsBindings()["HomeInterfaceScaleChoices"]);
                Assert.True(source.TryPreviewInterfaceScale(page, next[3]["Target"]));
                var contrast = await source.ApplySettingsAsync(source.SettingsBindings()["HomeHighContrastTarget"]);
                Assert.True(contrast.Succeeded);
                await source.RefreshAsync();
                Assert.Equal(1.5, source.Context.Current.Accessibility.DisplayScale);
                Assert.True(source.Context.Current.Accessibility.HighContrast);
                Assert.False(source.TryPreviewInterfaceScale(page, next[3]["Target"]));
            }

            await using var reopened = Open();
            await reopened.RefreshAsync();
            Assert.Equal(1.5, reopened.Current.Accessibility.DisplayScale);
            var read = HomeNativePreferenceFile.Read(file);
            Assert.Equal("retained", read["defaultModel"]!.GetValue<string>());
            Assert.Equal(21, read["notesEditor"]!["fontSize"]!.GetValue<int>());
            Assert.Equal("keep", read[HomeNativePreferenceFile.ThemeKey]!["accessibility"]!["futurePreference"]!.GetValue<string>());
            var restored = HomeNativePreferenceFile.Resolve(read, scope, UserPreferencesService.ProjectLegacyHomeTheme);
            Assert.Equal(saved with { Revision = 9, Accessibility = saved.Accessibility with { HighContrast = true, DisplayScale = 1.5 } }, restored);
            var reopenedChoices = Assert.IsType<Dictionary<string, object?>[]>(reopened.SettingsBindings()["HomeInterfaceScaleChoices"]);
            var reopenedTarget = reopenedChoices[3]["Target"];
            await using var anotherSource = Open();
            await anotherSource.RefreshAsync();
            Assert.False(anotherSource.TryPreviewInterfaceScale(otherPage, reopenedTarget));
            Assert.True(reopened.TryPreviewInterfaceScale(page, reopenedTarget));
            var finalDraft = reopened.CaptureInterfaceScaleDraft(page, reopenedTarget);
            await reopened.DisposeAsync();
            Assert.Equal(1.5, reopened.Context.Current.Accessibility.DisplayScale);
            Assert.False(reopened.TryPreviewInterfaceScale(page, reopenedTarget));
            Assert.Null(reopened.CaptureInterfaceScaleDraft(page, reopenedTarget));
            Assert.False((await anotherSource.ApplySettingsAsync(finalDraft)).Succeeded);
        }
        finally { directory.Delete(recursive: true); }
    }
}
