using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using CakeOS.Cui.Themes;
using Haven.Desktop.Services;
using HavenOS.Home.Core;

var temporary = Path.Combine(Path.GetTempPath(), "9to1-home-theme-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var file = Path.Combine(temporary, "preferences.json");
var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "fixture-device");
var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
HomeThemePreference Project(JsonObject value, HomeThemeScope observedScope) => new(
    HomeThemePreference.CurrentSchemaVersion, observedScope, value["havenUiThemeName"]?.GetValue<string>() ?? "Professional", 0,
    new(InterfaceFontFamily: value["fontFamilyName"]?.GetValue<string>())) { Appearance = "SuperDark" };
HomeThemePreference Read() => HomeNativePreferenceFile.Resolve(HomeNativePreferenceFile.Read(file), scope, Project);
Task<HomeThemeWriteResult> Save(HomeThemePreference candidate, long expected, Func<CancellationToken, ValueTask<bool>>? permit = null) =>
    HomeNativePreferenceFile.CompareExchangeAsync(file, scope, expected, candidate, Project, permit ?? (_ => ValueTask.FromResult(true)), CancellationToken.None);
try
{
    File.WriteAllText(file, "{\"HavenUiThemeName\":\"Glow\",\"futureSetting\":{\"x\":1},\"defaultModel\":\"existing-id\"}");
    var initialBytes = File.ReadAllBytes(file);
    Check(Read().ThemeName == "Glow" && Read().Revision == 0, "Existing case-insensitive legacy theme read without reinterpretation");
    Check(File.ReadAllBytes(file).SequenceEqual(initialBytes), "Initial observation does not migrate or rewrite user data");
    var first = Read() with { ThemeName = "Professional", Revision = 1, Accent = new(HomeThemeAccentOverrideMode.IndividualApps, individualApps: new Dictionary<string,string>{{"app.write","#123456"}}), CustomThemesJson = "[]" };
    Check((await Save(first, 0)).Succeeded, "Canonical compare-exchange commits exact revision");
    Check(Read() == first, "Canonical preference round-trips through existing native file");
    var savedJson = HomeNativePreferenceFile.Read(file);
    Check(savedJson["futureSetting"]?["x"]?.GetValue<int>() == 1 && savedJson["defaultModel"]?.GetValue<string>() == "existing-id", "Unknown and unrelated fields survive canonical save");
    Check((await Save(first with { ThemeName = "Glow", Revision = 1 }, 0)).Code == "RevisionConflict", "Stale revision cannot overwrite a newer selection");
    var baseline = HomeNativePreferenceFile.Read(file);
    var otherBaseline = (JsonObject)baseline.DeepClone();
    var firstLegacy = (JsonObject)baseline.DeepClone(); firstLegacy["defaultModel"] = "changed-model";
    HomeNativePreferenceFile.MergeLegacy(file, baseline, firstLegacy, Project);
    Check(Read().Revision == 1 && Equals(Read().Accent, first.Accent), "Unrelated legacy save preserves theme revision and additive choices");
    var otherLegacy = (JsonObject)otherBaseline.DeepClone(); otherLegacy["anotherSetting"] = "second-owner";
    HomeNativePreferenceFile.MergeLegacy(file, otherBaseline, otherLegacy, Project);
    Check(HomeNativePreferenceFile.Read(file)["defaultModel"]?.GetValue<string>() == "changed-model", "Independent legacy edits merge without losing newer unrelated data");
    var staleLegacy = (JsonObject)otherBaseline.DeepClone(); staleLegacy["defaultModel"] = "stale";
    var refused = false;
    try { HomeNativePreferenceFile.MergeLegacy(file, otherBaseline, staleLegacy, Project); } catch (InvalidOperationException) { refused = true; }
    Check(refused && HomeNativePreferenceFile.Read(file)["defaultModel"]?.GetValue<string>() == "changed-model", "Conflicting legacy edit fails without overwrite");
    baseline = HomeNativePreferenceFile.Read(file); var changed = (JsonObject)baseline.DeepClone(); changed["havenUiThemeName"] = "Glow";
    HomeNativePreferenceFile.MergeLegacy(file, baseline, changed, Project);
    Check(Read().ThemeName == "Glow" && Read().Revision == 2 && Equals(Read().Accent, first.Accent) && Read().CustomThemesJson == "[]", "Legacy theme selection advances canonical revision without erasing other theme fields");
    var accentFile = Path.Combine(temporary, "accent-preferences.json"); File.Copy(file, accentFile);
    var accentBaseline = HomeNativePreferenceFile.Read(accentFile);
    var accentChange = (JsonObject)accentBaseline.DeepClone(); accentChange["overrideAccentColour"] = false;
    HomeNativePreferenceFile.MergeLegacy(accentFile, accentBaseline, accentChange, Project);
    var disabledAccent = HomeNativePreferenceFile.Resolve(HomeNativePreferenceFile.Read(accentFile), scope, Project).Accent!;
    Check(disabledAccent.Mode == HomeThemeAccentOverrideMode.None && disabledAccent.IndividualApps["app.write"] == "#123456",
        "Legacy override mode changes preserve saved per-app colour choices");
    var bytesBeforeDenial = File.ReadAllBytes(file); var checks = 0;
    var denied = await Save(Read() with { ThemeName = "Bubble", Revision = 3 }, 2, _ => { checks++; return ValueTask.FromResult(false); });
    Check(denied.Code == "PermissionDenied" && checks == 1 && File.ReadAllBytes(file).SequenceEqual(bytesBeforeDenial), "Authority is checked at commit and denial preserves bytes");
    var wrong = scope with { OwnerId = "other-device" }; refused = false;
    try { HomeNativePreferenceFile.Resolve(HomeNativePreferenceFile.Read(file), wrong, Project); } catch (UnauthorizedAccessException) { refused = true; }
    Check(refused, "Different local profile cannot borrow saved scoped preference");
    baseline = HomeNativePreferenceFile.Read(file); baseline[HomeNativePreferenceFile.ThemeKey]!["schemaVersion"] = 99;
    File.WriteAllText(file, baseline.ToJsonString()); var unsupportedBytes = File.ReadAllBytes(file);
    changed = (JsonObject)baseline.DeepClone(); changed["anotherSetting"] = "attempt"; refused = false;
    try { HomeNativePreferenceFile.MergeLegacy(file, baseline, changed, Project); } catch (ArgumentException) { refused = true; }
    Check(refused && File.ReadAllBytes(file).SequenceEqual(unsupportedBytes), "Unsupported canonical schema cannot be erased by legacy settings");
    File.WriteAllBytes(file, bytesBeforeDenial);
    baseline = HomeNativePreferenceFile.Read(file); baseline[HomeNativePreferenceFile.ThemeKey]!["futureThemeField"] = new JsonObject { ["keep"] = true }; File.WriteAllText(file, baseline.ToJsonString());
    Check((await Save(Read() with { ThemeName = "Bubble", Revision = 3 }, 2)).Succeeded && HomeNativePreferenceFile.Read(file)[HomeNativePreferenceFile.ThemeKey]?["futureThemeField"]?["keep"]?.GetValue<bool>() == true, "Unknown additive theme fields survive compatible update");

    var callbacks = new ConcurrentQueue<Action>();
    var authorised = true;
    var source = new NativeHomeThemePreferences(file, scope, Project, _ => ValueTask.FromResult(authorised), action => callbacks.Enqueue(action));
    try
    {
        await source.RefreshAsync();
        using var second = new CuiThemePreferenceContext(source);
        Check(source.Context.Current.Theme.Name == "Bubble" && second.Current.Theme.Name == "Bubble", "Two CUI consumers inherit the restored canonical Home observation");
        await Save(Read() with { ThemeName = "Glow", Revision = 4 }, 3);
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until && source.Current.Revision < 4)
        {
            while (callbacks.TryDequeue(out var callback)) callback();
            await Task.Delay(10);
        }
        Check(source.Context.Current.Theme.Name == "Glow" && second.Current.Theme.Name == "Glow" && source.Current.Revision == 4, "Actual file invalidation propagates through canonical Home service to both borrowed contexts");
        var retained = source.Current;
        await source.DisposeAsync();
        await Save(Read() with { ThemeName = "Retro", Revision = 5 }, 4);
        while (callbacks.TryDequeue(out var callback)) callback();
        Check(source.Current == retained, "Retirement detaches file observation and leaves borrowed owner state intact");
    }
    finally { await source.DisposeAsync(); }
    await using (var restarted = new NativeHomeThemePreferences(file, scope, Project, _ => ValueTask.FromResult(true), action => callbacks.Enqueue(action)))
    {
        await restarted.RefreshAsync();
        Check(restarted.Context.Current.Theme.Name == "Retro" && restarted.Current.Revision == 5, "Restart reads exact latest persisted Home preference");
    }
    var beforeRace = Read();
    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var arrivals = 0;
    async Task<HomeThemeWriteResult> Race(string name)
    {
        if (Interlocked.Increment(ref arrivals) == 2) gate.SetResult();
        await gate.Task;
        return await Save(beforeRace with { ThemeName = name, Revision = 6 }, 5);
    }
    var race = await Task.WhenAll(Task.Run(() => Race("Glow")), Task.Run(() => Race("Professional")));
    Check(race.Count(result => result.Succeeded) == 1 && race.Count(result => result.Code == "RevisionConflict") == 1 && Read().Revision == 6,
        "Concurrent canonical saves retain exactly one committed revision");
    var unavailable = HomeNativePreferenceFile.Read(file);
    unavailable[HomeNativePreferenceFile.ThemeKey]!["themeName"] = "Uninstalled custom theme";
    File.WriteAllText(file, unavailable.ToJsonString()); var missingBytes = File.ReadAllBytes(file);
    await using (var fallback = new NativeHomeThemePreferences(file, scope, Project, _ => ValueTask.FromResult(true), action => callbacks.Enqueue(action)))
    {
        await fallback.RefreshAsync();
        Check(fallback.Context.Current.Theme.Name == "Professional" && fallback.Diagnostic is not null,
            "Missing custom theme resolves to Professional with a Home diagnostic");
        Check(File.ReadAllBytes(file).SequenceEqual(missingBytes), "Unavailable custom selection is preserved for recovery");
    }
    unavailable[HomeNativePreferenceFile.ThemeKey]!["themeName"] = "Glow";
    unavailable[HomeNativePreferenceFile.ThemeKey]!["customThemesJson"] = "[{}]";
    File.WriteAllText(file, unavailable.ToJsonString()); missingBytes = File.ReadAllBytes(file);
    await using (var fallback = new NativeHomeThemePreferences(file, scope, Project, _ => ValueTask.FromResult(true), action => callbacks.Enqueue(action)))
    {
        await fallback.RefreshAsync();
        Check(fallback.Context.Current.Theme.Name == "Professional" && fallback.Diagnostic is not null && File.ReadAllBytes(file).SequenceEqual(missingBytes),
            "Invalid custom library falls back without feeding invalid resources to CUI or rewriting user data");
    }
    var refusedSource = new NativeHomeThemePreferences(file, scope, Project, _ => ValueTask.FromResult(false), action => callbacks.Enqueue(action));
    refused = false;
    try { await refusedSource.RefreshAsync(); } catch (UnauthorizedAccessException) { refused = true; }
    Check(refused && refusedSource.Current.ScopeId == "presentation-only", "Unavailable current Home authority never publishes saved scope data");
    refused = false;
    try { await refusedSource.DisposeAsync(); } catch (AggregateException) { refused = true; }
    Check(refused, "Retirement retains a failed original preference read rather than reporting success");
    File.WriteAllText(file, "{ not valid json"); var corrupt = File.ReadAllBytes(file); refused = false;
    try { HomeNativePreferenceFile.MergeLegacy(file, new(), new() { ["defaultModel"] = "x" }, Project); } catch (InvalidDataException) { refused = true; }
    Check(refused && File.ReadAllBytes(file).SequenceEqual(corrupt), "Corrupt native preferences are preserved rather than reset to defaults");
    Console.WriteLine($"RESULT: {passed} passed, 0 failed");
}
finally { Directory.Delete(temporary, recursive: true); }
