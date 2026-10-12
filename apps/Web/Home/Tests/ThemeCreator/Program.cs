using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;
using NineToOne.Web;

var assertions = 0;
void Check(bool condition, string detail) { assertions++; if (!condition) throw new InvalidOperationException(detail); }
object? Read(BrowserThemeCreator creator, string path) { Check(creator.TryGetValue("ThemeCreator" + path, out var value), "Missing creator binding " + path); return value; }
void Set(BrowserThemeCreator creator, string path, object value) => Check(creator.TrySetValue("ThemeCreator" + path, value), "Draft edit rejected " + path);
var sink = new EmptyOwner();
var preferences = new ControlledPreferences();
using (var creator = new BrowserThemeCreator(sink, sink, preferences, preferences.Save))
{
    var original = preferences.Current;
    creator.Open(); Check(Read(creator, "Open") is true, "Creator did not open");
    Check(!preferences.Catalog.Names.Contains(BrowserThemeCreator.PreviewThemeName), "Ephemeral preview leaked into saved theme choices");
    Check(preferences.Writes == 0 && preferences.Current == original, "Opening mutated account preferences");
    Set(creator, "Name", "A real custom theme");
    Set(creator, "FillKindIndex", 1); Set(creator, "GradientKindIndex", 1);
    await creator.DispatchAsync("ThemeCreatorAddStop", null);
    Set(creator, "StopPosition", "35"); Set(creator, "ColourHex", "#D05070");
    Set(creator, "Transparency", "28"); Set(creator, "BackgroundBlur", "9");
    Set(creator, "AccentIndex", 1); Set(creator, "FillKindIndex", 2); Set(creator, "PatternKindIndex", 2);
    Set(creator, "PatternSize", "12"); Set(creator, "PatternSpacing", "17"); Set(creator, "PatternAngle", "27");
    Set(creator, "PatternColourIndex", 1); Set(creator, "ColourHex", "#173545");
    Set(creator, "AccentIndex", 2); Set(creator, "ColourRed", 212d);
    for (var role = 0; role < 7; role++) { Set(creator, "FontRoleIndex", role); Set(creator, "FontSize", (15 + role).ToString()); }
    Set(creator, "Roundness", 82d);
    Check(Read(creator, "CanSave") is true, "Valid full custom theme cannot save");
    Set(creator, "FontSize", "NaN"); Check(Read(creator, "CanSave") is false, "NaN font admitted");
    Set(creator, "FontSize", "21");
    preferences.Reject = true; await creator.DispatchAsync("ThemeCreatorSave", null);
    Check(Read(creator, "Open") is true && (string?)Read(creator, "Name") == "A real custom theme", "CAS rejection lost draft");
    Check(preferences.Current == original, "Rejected save changed account preference");
    preferences.Reject = false; await creator.DispatchAsync("ThemeCreatorSave", null);
    var saved = preferences.Saved!;
    Check(saved.PrimaryAccent.Gradient is { Kind: CuiCustomGradientKind.Radial } gradient && gradient.Stops.Count == 3 && gradient.Stops[1].Offset == .35 && gradient.Stops[1].Colour.BackgroundBlur == 9, "Gradient builder lost ordered colour/material state");
    Check(saved.SecondaryAccent.Pattern is { Kind: CuiCustomPatternKind.Dots, Spacing: 17, Angle: 27 } pattern && pattern.Background.Hex == "#173545", "Pattern builder lost geometry/background colour");
    Check(saved.TertiaryAccent.Colour.Hex.StartsWith("#D4", StringComparison.Ordinal) && saved.Fonts.Main.Size == 15 && saved.Fonts.Code.Size == 21 && saved.ButtonRoundnessPercent == 82, "Role-specific values did not survive save");
    Check(Read(creator, "Open") is false && !preferences.Catalog.TryResolve(BrowserThemeCreator.PreviewThemeName, out _), "Saved creator retained its ephemeral preview");
    creator.Open(); Set(creator, "Name", "Professional"); Check(Read(creator, "CanSave") is false, "Built-in name shadow admitted");
    Set(creator, "Name", saved.Name); Check(Read(creator, "CanSave") is false, "Custom name collision admitted");
    Set(creator, "Name", "Cancelled theme"); await creator.DispatchAsync("ThemeCreatorCancel", null);
    Check(preferences.Writes == 2 && preferences.Saved == saved, "Cancel changed account or custom library");
    creator.Open(); Set(creator, "Name", "Pinned owner draft"); preferences.AdvanceRevision();
    Check(Read(creator, "CanSave") is false && Read(creator, "CanUseCurrentRevision") is true, "Same-account revision change silently rebased the draft");
    await creator.DispatchAsync("ThemeCreatorUseCurrentRevision", null);
    Check(Read(creator, "CanSave") is true && (string?)Read(creator, "Name") == "Pinned owner draft", "Explicit revision review lost draft or failed to re-enable save");
    preferences.AdvanceOwner();
    Check(Read(creator, "CanSave") is false, "Changed account/revision admitted old draft");
    await creator.DispatchAsync("ThemeCreatorSave", null); Check(preferences.Writes == 2, "Stale draft reached account delegate");
}

await using (var session = HeadlessUnitTestSession.StartNew(typeof(ThemeCreatorTestApplication)))
{
    await session.Dispatch(async () =>
    {
        var owner = new ControlledPreferences();
        using var creator = new BrowserThemeCreator(sink, sink, owner, owner.Save);
        creator.Open();
        using var theme = new CuiThemePreferenceContext(owner, owner.Catalog);
        using var loader = new CuiControlLoader(); loader.SetThemePreferenceContext(theme); loader.SetBindingContext(creator); loader.SetActionDispatcher(creator);
        var document = new CuiRichParser().Parse("""
            <Cui><Import Source="framework/CUI/Components/ThemeCreator.cui"/>
              <Page><Container Type="Overlay" VerticalScrolling="True" HorizontalScrolling="False"><ThemeCreatorPanel/></Container></Page>
            </Cui>
            """);
        var composed = CuiThemeCreatorComponents.Compose(document);
        Check(ReferenceEquals(composed, CuiThemeCreatorComponents.Compose(composed)), "Theme creator library composed twice");
        var loaded = loader.TryLoad(composed);
        Check(loaded.Root is not null && !loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error), "Actual creator CUI failed: " + string.Join("; ", loaded.Diagnostics));
        loader.WireBindings(loaded.Root!);
        var window = new Window { Content = loaded.Root, Width = 800, Height = 600 };
        void Pump() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        T Find<T>(string name) where T : Control => loaded.Root!.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetName(control) == name);
        void Click(Control control)
        {
            control.BringIntoView(); Pump();
            var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
        }
        try
        {
            window.Show(); Pump(); var input = Find<TextBox>("Theme name");
            Check(input.IsFocused && input.SelectedText == "My theme", "Opening did not select theme name");
            foreach (var character in "Native theme draft") { window.KeyTextInput(character.ToString()); Pump(); }
            Check(input.Text == "Native theme draft" && input.CaretIndex == input.Text.Length, "Per-character editing lost caret/value");
            window.KeyPress(Key.Left, RawInputModifiers.None); window.KeyRelease(Key.Left, RawInputModifiers.None);
            window.KeyTextInput("!"); Pump(); Check(input.Text == "Native theme draf!t", "Middle-caret insertion failed");
            var type = Find<ComboBox>("Accent type"); Click(type); window.KeyPress(Key.Down, RawInputModifiers.None); window.KeyRelease(Key.Down, RawInputModifiers.None); window.KeyPress(Key.Enter, RawInputModifiers.None); window.KeyRelease(Key.Enter, RawInputModifiers.None); Pump();
            Check(Read(creator, "IsGradient") is true, "Physical accent selector did not enter gradient builder");
            Click(loaded.Root!.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Add stop"));
            Check(((string[])Read(creator, "StopNames")!).Length == 3, "Physical Add stop failed");
            var colour = Find<TextBox>("Colour hexadecimal RGB"); Click(colour);
            window.KeyPress(Key.A, RawInputModifiers.Control); window.KeyRelease(Key.A, RawInputModifiers.Control);
            foreach (var character in "#CE3864") { window.KeyTextInput(character.ToString()); Pump(); }
            Check((string?)Read(creator, "ColourHex") == "#CE3864", "Physical colour picker edits did not reach selected gradient stop");
            var preview = Find<Button>("Preview primary accent and edit it");
            Check(preview.Background is Avalonia.Media.IGradientBrush brush && brush.GradientStops.Count == 3 && brush.GradientStops[1].Color == Avalonia.Media.Color.Parse("#CE3864"), "Actual nested preview did not render the shared custom gradient");
            foreach (var (width, height) in new[] { (1200, 900), (800, 600), (390, 844) })
            {
                window.Width = width; window.Height = height; Pump();
                var controls = loaded.Root.GetVisualDescendants().Where(control => control.IsVisible && control is TextBox or ComboBox or Button).ToArray();
                Check(controls.All(control => control.Bounds.Width <= width), "Creator control exceeds narrow viewport");
                var output = Environment.GetEnvironmentVariable("THEME_CREATOR_EVIDENCE");
                if (!string.IsNullOrEmpty(output)) { Directory.CreateDirectory(output); window.CaptureRenderedFrame()?.Save(Path.Combine(output, $"creator-{width}x{height}.png")); }
            }
            Click(Find<Button>("Save custom theme to account")); await loader.WhenActionsIdleAsync(); Pump();
            Check(owner.Writes == 1 && owner.Saved?.Name == "Native theme draf!t", "Actual CUI Save did not reach same account delegate");
            Check(Read(creator, "Open") is false, "Successful native save did not close draft");
            using var absentOwnerLoader = new CuiControlLoader();
            var absentOwner = absentOwnerLoader.TryLoad(composed);
            Check(absentOwner.Root is not null && !absentOwner.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error), "Native Settings without a creator owner cannot load the imported library");
            Check(!absentOwner.Root!.GetVisualDescendants().OfType<CuiContainerControl>().Any(control => control.Name == "theme-creator" && control.IsVisible), "Native Settings exposed creator without a borrowed owner");
        }
        finally { window.Close(); }
    }, CancellationToken.None);
}
Console.WriteLine(JsonSerializer.Serialize(new { assertions, scope = "Creator source/controller and actual native CUI interaction only; controlled account receipts are not real account sync or browser acceptance." }));

sealed class EmptyOwner : ICuiBindingContext, ICuiActionDispatcher
{
    public bool TryGetValue(string path, out object? value) { value = null; return false; }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected account action " + command);
}
sealed class ControlledPreferences : IBrowserHomeThemePreferences
{
    public CuiThemeDefinitionCatalog Catalog { get; } = new();
    public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 0, "controlled-account-owner", false, new(), CuiAppearance.Dark);
    public string? Diagnostic => null;
    public bool CanChange => true;
    public event EventHandler? Changed;
    public bool Reject;
    public int Writes;
    public CuiCustomTheme? Saved;
    public Task<HomeThemeOperationResult> Save(CuiCustomTheme theme, CuiThemePreferenceSnapshot opening, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (opening.ScopeId != Current.ScopeId || opening.Revision != Current.Revision) throw new InvalidOperationException("Opening owner fence was not preserved");
        Writes++;
        if (Reject) return Task.FromResult(new HomeThemeOperationResult(false, "RevisionConflict", Diagnostic: "Saved appearance changed; your draft was kept."));
        Saved = theme.Validate(); Catalog.UpsertCustomTheme(Saved);
        Current = Current with { Name = Saved.Name, Revision = Current.Revision + 1 }; Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new HomeThemeOperationResult(true, "Succeeded"));
    }
    public void AdvanceOwner() { Current = Current with { ScopeId = "different-account-owner", Revision = 0 }; Changed?.Invoke(this, EventArgs.Empty); }
    public void AdvanceRevision() { Current = Current with { Revision = Current.Revision + 1 }; Changed?.Invoke(this, EventArgs.Empty); }
    public Task<HomeThemeOperationResult> SetAsync(string name, CuiAccessibilitySettings? accessibility = null, CancellationToken token = default, CuiAppearance? appearance = null, CuiAccentPreference? accent = null) => throw new InvalidOperationException("Creator bypassed its original save callback");
    public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
public sealed class ThemeCreatorTestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<ThemeCreatorTestApplication>().UseSkia())
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
}
