using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;
using NineToOne.Web;

internal static class AppearanceSettingsChecks
{
    internal static async Task Run()
    {
        var sourceMap = new Dictionary<string,string> { ["app.present"]="#db3545" };
        var immutable = new CuiAccentPreference(CuiAccentOverrideMode.IndividualApps,individualApps:sourceMap);
        sourceMap["app.present"]="#FFFFFF";
        Demand(immutable.ResolveColour("app.present")=="#DB3545", "Caller mutation changed accent projection");
        Demand(immutable.Equals(new CuiAccentPreference(CuiAccentOverrideMode.IndividualApps,individualApps:new Dictionary<string,string>{{"app.present","#DB3545"}})), "Equivalent CUI accent projections are not value equal");
        var preferences = new Preferences();
        var empty = new EmptyOwner();
        using var settings = new BrowserAppearanceSettings(empty, empty, preferences);
        settings.PropertyChanged += (_, _) => throw new InvalidOperationException("A retired view observer failed");
        await settings.DispatchAsync("ThemeGlow", null);
        Demand(preferences.Writes == 1 && preferences.Current.Name == "Glow", "Acknowledged theme write lost");
        Demand(settings.IsActionAvailable("ThemeProfessional") is true, "Observer failure latched busy state");
        Demand(settings.TryGetValue("AppearanceStatus", out var value) && value is string text && text.StartsWith("Appearance saved"), "Observer replaced durable result");
        preferences.Diagnostic = "Offline cache could not refresh.";
        preferences.Notify();
        Demand(settings.TryGetValue("AppearanceStatus", out value) && value is string updated && updated.StartsWith(preferences.Diagnostic), "Old success hid new provider diagnostic");

        preferences.Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = settings.DispatchAsync("ThemeProfessional", null).AsTask();
        Demand(!pending.IsCompleted, "Original preference write was not retained");
        settings.Dispose();
        Demand(!preferences.Disposed && !pending.IsCompleted, "View retirement disposed preference owner or fabricated settlement");
        preferences.Release.SetResult();
        await pending;
        Demand(preferences.Writes == 2 && preferences.Current.Name == "Professional", "Retirement dropped admitted write");

        var modes = new Preferences();
        using var appearanceSettings = new BrowserAppearanceSettings(empty, empty, modes);
        foreach (var mode in Enum.GetValues<CuiAppearance>())
        {
            await appearanceSettings.DispatchAsync("Appearance" + mode, null);
            Demand(modes.Current.Appearance == mode && modes.Current.Name == "Professional", "Brightness changed the theme or lost the selected mode");
            Demand(appearanceSettings.TryGetValue("AppearanceModeName", out var label) && label is string { Length: > 0 }, "Brightness choice is not observable");
        }
        await appearanceSettings.DispatchAsync("ThemeGlow", null);
        Demand(modes.Current.Appearance == CuiAppearance.SuperDark && modes.Current.Name == "Glow", "Changing theme discarded brightness");
        await appearanceSettings.DispatchAsync("AppearanceAccentAllApps", null);
        Demand(modes.Current.EffectiveAccent.Mode == CuiAccentOverrideMode.AllApps && modes.Current.EffectiveAccent.AllAppsColour == CuiAccentPreference.InitialAllAppsColour, "All Apps did not persist a visible initial colour");
        await appearanceSettings.DispatchAsync("AppearanceAccentColour", "#168B4F");
        Demand(modes.Current.EffectiveAccent.ResolveColour("app.present") == "#168B4F", "Global colour was not saved through the owner");
        await appearanceSettings.DispatchAsync("ThemeProfessional", null);
        Demand(modes.Current.EffectiveAccent.ResolveColour("app.write") == "#168B4F", "Theme change erased accent choice");
        Demand(appearanceSettings.TrySetValue("AppearanceAccentColour", "#12"), "Partial user colour entry was rejected");
        Demand(appearanceSettings.IsActionAvailable("AppearanceAccentApply") is false, "Incomplete colour could be persisted");
        Demand(appearanceSettings.TrySetValue("AppearanceAccentColour", "#aBcD12"), "Valid user colour entry failed");
        await appearanceSettings.DispatchAsync("AppearanceAccentApply", null);
        Demand(modes.Current.EffectiveAccent.AllAppsColour == "#ABCD12", "Custom colour was not canonicalized");
        await appearanceSettings.DispatchAsync("AppearanceAccentIndividualApps", null);
        Demand(modes.Current.EffectiveAccent.Mode == CuiAccentOverrideMode.IndividualApps && modes.Current.EffectiveAccent.ResolveColour("app.present") is null, "Individual mode leaked global colour");
        await appearanceSettings.DispatchAsync("AppearanceAccentNone", null);
        Demand(modes.Current.EffectiveAccent.Mode == CuiAccentOverrideMode.None && modes.Current.EffectiveAccent.AllAppsColour == "#ABCD12", "None erased saved user choice");
        Demand(appearanceSettings.IsActionAvailable("CreateTheme") is false, "Disconnected creator appeared available");
        var opened = false;
        using (var connectedCreator = new BrowserAppearanceSettings(empty, empty, modes, () => opened = true))
            await connectedCreator.DispatchAsync("CreateTheme", null);
        Demand(opened, "Connected Create Theme action did not open the real host callback");
        var expectedNames = new[] { "Professional", "Glow", "Playful", "Frosted Glass", "Bubble", "Metallic", "Retro", "Western" };
        Demand(Names().SequenceEqual(expectedNames), "Home picker does not expose the requested available built-in themes in order");
        foreach(var name in expectedNames)
        {
            await appearanceSettings.DispatchAsync("AppearanceSelectTheme", name);
            Demand(modes.Current.Name==name,"Dynamic theme selection did not use the real catalog: "+name);
        }
        var custom = CuiCustomThemeContract.CreateDefault("My saved design");
        modes.Catalog.UpsertCustomTheme(custom);
        Demand(Names().Contains(custom.Name),"Saved custom theme is missing from the picker");
        await appearanceSettings.DispatchAsync("AppearanceSelectTheme",custom.Name);
        Demand(modes.Current.Name==custom.Name,"Custom theme did not route through the preference owner");
        modes.Catalog.RemoveCustomTheme(custom.Id);
        var beforeStaleChoice=modes.Writes;
        await appearanceSettings.DispatchAsync("AppearanceSelectTheme",custom.Name);
        await appearanceSettings.DispatchAsync("AppearanceSelectTheme","9to1 Draft Theme Preview");
        Demand(modes.Writes==beforeStaleChoice,"Missing/private preview theme was written as a durable choice");
        await modes.SetAsync("Cinematic");
        Demand(Names().Contains("Cinematic"),"Existing Cinematic preference became inaccessible");
        await appearanceSettings.DispatchAsync("AppearanceSelectTheme","Professional");
        Demand(!Names().Contains("Cinematic"),"Legacy-only theme displaced the requested choices");
        var unavailableCustom=await ((IBrowserHomeThemePreferences)modes).SaveCustomThemeAsync(custom,modes.Current);
        Demand(!unavailableCustom.Succeeded && unavailableCustom.Code=="ThemePreferenceUnavailable","Disconnected custom save fabricated success");
        string[] Names()
        {
            Demand(appearanceSettings.TryGetValue("AppearanceThemeChoices",out var rows),"Missing theme choices");
            return ((System.Collections.IEnumerable)rows!).Cast<ICuiBindingContext>().Select(row=>row.TryGetValue("Name",out var name)?(string)name!:string.Empty).ToArray();
        }
        var writesBeforeUnavailable = modes.Writes;
        modes.AllowChanges = false;
        Demand(appearanceSettings.IsActionAvailable("AppearanceBright") is false, "Brightness bypassed owner write availability");
        await appearanceSettings.DispatchAsync("AppearanceBright", null);
        Demand(modes.Writes == writesBeforeUnavailable, "Unavailable brightness action wrote to the owner");
        await RenderCanonicalSettings();
    }
    private static async Task RenderCanonicalSettings()
    {
        var location = new DirectoryInfo(AppContext.BaseDirectory);
        while (location is not null && !File.Exists(Path.Combine(location.FullName,"apps/Web/Accounts/Accounts.cui"))) location = location.Parent;
        Demand(location is not null,"Cannot locate canonical Accounts.cui source");
        var path = Path.Combine(location!.FullName,"apps/Web/Accounts/Accounts.cui");
        var parser = new CuiRichParser(); var document = parser.Parse(await File.ReadAllTextAsync(path),path);
        Demand(!parser.Diagnostics.Diagnostics.Any(d=>d.Severity==CuiDiagnosticSeverity.Error),"Accounts CUI parse failed");
        await using var session = HeadlessUnitTestSession.StartNew(typeof(DashboardTestApplication));
        await session.Dispatch(() =>
        {
            foreach (var width in new[] {1280,390})
            foreach (var mode in Enum.GetValues<CuiAppearance>())
            {
                var preferences = new Preferences(); preferences.SetAsync("Professional",appearance:mode,accent:new CuiAccentPreference(CuiAccentOverrideMode.AllApps)).GetAwaiter().GetResult();
                var empty = new EmptyOwner(); using var settings = new BrowserAppearanceSettings(empty,empty,preferences,()=>{});
                using var theme = new CuiThemePreferenceContext(preferences);
                var registry = new CuiControlRegistry(); BrowserCanonicalIcons.Register(registry);
                using var loader = new CuiControlLoader(registry); loader.SetThemePreferenceContext(theme); loader.SetBindingContext(settings); loader.SetActionDispatcher(settings);
                var result = loader.TryLoad(document);
                Demand(result.Root is not null && !result.Diagnostics.Any(d=>d.Severity==CuiDiagnosticSeverity.Error),"Canonical Settings failed to lower: "+string.Join("; ",result.Diagnostics));
                var root = result.Root!; loader.WireBindings(root);
                using var availability = BrowserActionAvailability.Observe(root,loader,document,settings,settings);
                var window = new Window {Width=width,Height=1100,Content=root}; window.Show();
                try
                {
                    Dispatcher.UIThread.RunJobs(); using (window.CaptureRenderedFrame()) {}
                    var chooseNone = root.GetLogicalDescendants().OfType<Button>().Single(button=>Equals(button.Content,"None"));
                    chooseNone.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs(); using (window.CaptureRenderedFrame()) {}
                    var chooseAll = root.GetLogicalDescendants().OfType<Button>().Single(button=>Equals(button.Content,"All Apps"));
                    chooseAll.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs(); using (window.CaptureRenderedFrame()) {}
                    Demand(preferences.Current.EffectiveAccent.Mode==CuiAccentOverrideMode.AllApps,"Native All Apps control did not update owner");
                    var swatch = root.GetLogicalDescendants().OfType<Button>().SingleOrDefault(button=>Avalonia.Automation.AutomationProperties.GetName(button)=="Use Green accent");
                    if(swatch is null) throw new InvalidOperationException("Missing labelled Green choice. Buttons: "+string.Join(" | ",root.GetLogicalDescendants().OfType<Button>().Select(button=>$"{button.Content} / {Avalonia.Automation.AutomationProperties.GetName(button)}")));
                    swatch.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.UIThread.RunJobs(); using (window.CaptureRenderedFrame()) {}
                    Demand(preferences.Current.EffectiveAccent.AllAppsColour=="#1E9E58","Canonical swatch action lost its row parameter");
                    var colourButtons=root.GetLogicalDescendants().OfType<Button>().Where(button=>Avalonia.Automation.AutomationProperties.GetName(button)?.StartsWith("Use ",StringComparison.Ordinal)==true && Avalonia.Automation.AutomationProperties.GetName(button)!.EndsWith(" accent",StringComparison.Ordinal)).ToArray();
                    Demand(colourButtons.Length==13,"Canonical swatch choices were lost");
                    if(width==1280) Demand(colourButtons.Select(button=>button.TranslatePoint(default,root)!.Value.X).Distinct().Count()>1,"Accent choices did not wrap into usable columns");
                    foreach(var button in colourButtons)
                    {
                        var origin=button.TranslatePoint(default,root)!.Value;
                        Demand(origin.X>=-.5 && origin.X+button.Bounds.Width<=width+.5,$"Colour swatch exceeds {width}px viewport");
                    }
                    foreach (var button in root.GetLogicalDescendants().OfType<Button>().Where(button=>button.IsEffectivelyVisible && new[] {"All Apps","Individual Apps","None","Use colour","Green"}.Contains(button.Content as string)))
                    {
                        var origin=button.TranslatePoint(default,root)!.Value;
                        Demand(origin.X>=-.5 && origin.X+button.Bounds.Width<=width+.5,$"Accent control exceeds {width}px viewport");
                    }
                    var output=Environment.GetEnvironmentVariable("HOME_ACCENT_CAPTURES");
                    if (output is not null) {Directory.CreateDirectory(output);using var frame=window.CaptureRenderedFrame();frame!.Save(Path.Combine(output,$"appearance-{mode}-{width}.png"));}
                }
                finally {window.Close();}
            }
        },CancellationToken.None);
    }
    private static void Demand(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class EmptyOwner : ICuiBindingContext, ICuiActionDispatcher
    {
        public bool TryGetValue(string path, out object? value) { value = null; return false; }
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected account action");
    }
    private sealed class Preferences : IBrowserHomeThemePreferences
    {
        public CuiThemeDefinitionCatalog Catalog { get; } = new();
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 0, "controlled-observer-fixture", false, new());
        public string? Diagnostic { get; set; }
        internal bool AllowChanges = true;
        public bool CanChange => AllowChanges;
        public event EventHandler? Changed;
        internal int Writes;
        internal bool Disposed;
        internal TaskCompletionSource? Release;
        internal void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public async Task<HomeThemeOperationResult> SetAsync(string name, CuiAccessibilitySettings? accessibility = null, CancellationToken token = default, CuiAppearance? appearance = null, CuiAccentPreference? accent = null)
        {
            token.ThrowIfCancellationRequested();
            if (Release is not null) await Release.Task;
            Writes++;
            Current = Current with { Name = name, Revision = Current.Revision + 1, Appearance = appearance ?? Current.Appearance, Accent = accent ?? Current.Accent };
            Diagnostic = null;
            Notify();
            return new(true, "Succeeded"); // Controlled receipt for presentation-only regression; no storage claim.
        }
        public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
