using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;
using NineToOne.Web;

internal static class AppAccentSettingsChecks
{
    internal static async Task Run()
    {
        var owner = new Owner(); var preferences = new Preferences();
        using (var controller = new BrowserAppAccentSettings(owner, owner, preferences, "app.present"))
        {
            Check(Value<bool>(controller, "ShowAppAccentSettings") == false && controller.IsActionAvailable("AppAccentBlue") == false,
                "default None hides app settings and rejects writes");
            await controller.DispatchAsync("AppAccentBlue", null);
            Check(preferences.Writes == 0, "None cannot write a hidden app override");
            preferences.Change(CuiAccentPreference.Default.WithMode(CuiAccentOverrideMode.AllApps));
            Check(!Value<bool>(controller, "ShowAppAccentSettings"), "AllApps hides the individual app menu");
            preferences.Change(CuiAccentPreference.Default.WithMode(CuiAccentOverrideMode.IndividualApps).WithAppColour("app.write", "#123456"));
            controller.PropertyChanged += (_, _) => throw new InvalidOperationException("retired observer fixture");
            await controller.DispatchAsync("AppAccentBlue", null);
            Check(preferences.Current.EffectiveAccent.IndividualApps["app.present"] == "#2563EB"
                && preferences.Current.EffectiveAccent.IndividualApps["app.write"] == "#123456"
                && preferences.Current.Name == "Professional" && preferences.Current.Appearance == CuiAppearance.Dark,
                "acknowledged app colour preserves other apps, theme and brightness despite observer failure");
            Check(controller.IsActionAvailable("AppAccentPurple") == true && Value<string>(controller, "AppAccentStatus").StartsWith("App colour saved."),
                "observer failure cannot latch busy state or replace the original write receipt");
            await controller.DispatchAsync("AppAccentReset", null);
            Check(!preferences.Current.EffectiveAccent.IndividualApps.ContainsKey("app.present") && preferences.Current.EffectiveAccent.IndividualApps["app.write"] == "#123456",
                "reset removes only this app override");
            var writes = preferences.Writes; preferences.CanChange = false;
            await controller.DispatchAsync("AppAccentRed", null);
            Check(preferences.Writes == writes && !Value<bool>(controller, "AppAccentCanChange"), "provider write restrictions remain authoritative");
            await controller.DispatchAsync("AppAccentInvalid", null);
            Check(owner.Dispatches == 0, "unknown accent commands cannot escape into the app owner");
            using var view = new CancellationTokenSource(); using var caller = new CancellationTokenSource();
            await controller.DispatchWithLifetimeAsync("Original", null, new(view.Token, caller.Token));
            Check(owner.Dispatches == 1 && owner.LastLifetime.ViewCancellation == view.Token && owner.LastLifetime.CallerCancellation == caller.Token,
                "ordinary app actions retain the original lifetime and owner");
        }
        preferences = new Preferences { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        preferences.Change(CuiAccentPreference.Default.WithMode(CuiAccentOverrideMode.IndividualApps));
        var retiring = new BrowserAppAccentSettings(owner, owner, preferences, "app.present");
        using (var view = new CancellationTokenSource())
        {
            var pending = retiring.DispatchWithLifetimeAsync("AppAccentRed", null, new(view.Token, default)).AsTask();
            Check(preferences.Writes == 1 && !pending.IsCompleted, "controller retains the actual admitted preference write");
            retiring.Dispose(); view.Cancel();
            Check(!preferences.Disposed && !pending.IsCompleted, "view disposal neither disposes the borrowed owner nor fabricates write settlement");
            preferences.Hold.SetResult(); await pending;
            Check(preferences.Current.EffectiveAccent.IndividualApps["app.present"] == "#D13438", "admitted Home write settles after view retirement");
        }
        preferences = new Preferences(); preferences.Change(CuiAccentPreference.Default.WithMode(CuiAccentOverrideMode.IndividualApps));
        using (var stale = new BrowserAppAccentSettings(owner, owner, preferences, "app.present"))
        {
            var changed = false;
            stale.PropertyChanged += (_, _) => { if (!changed) { changed = true; preferences.Change(CuiAccentPreference.Default); } };
            await stale.DispatchAsync("AppAccentGreen", null);
            Check(preferences.Writes == 0 && preferences.Current.EffectiveAccent.Mode == CuiAccentOverrideMode.None,
                "reentrant mode change before admission rejects the stale individual-colour candidate");
        }
        preferences = new Preferences(); preferences.Change(CuiAccentPreference.Default.WithMode(CuiAccentOverrideMode.IndividualApps));
        using (var cancelled = new BrowserAppAccentSettings(owner, owner, preferences, "app.present"))
        using (var view = new CancellationTokenSource())
        {
            view.Cancel(); var observed = false;
            try { await cancelled.DispatchWithLifetimeAsync("AppAccentBlue", null, new(view.Token, default)); }
            catch (OperationCanceledException) { observed = true; }
            Check(observed && preferences.Writes == 0, "cancelled view callbacks cannot admit a preference write");
        }
        await NativeMenu();
    }

    private static async Task NativeMenu()
    {
        await using var native = HeadlessUnitTestSession.StartNew(typeof(DashboardTestApplication));
        await native.Dispatch<int>(async () =>
        {
            var owner = new Owner(); var preferences = new Preferences();
            using var settings = new BrowserAppAccentSettings(owner, owner, preferences, "app.present");
            using var context = new CuiThemePreferenceContext(preferences);
            var registry = new CuiControlRegistry(); BrowserCanonicalIcons.Register(registry);
            using var loader = new CuiControlLoader(registry);
            loader.SetThemePreferenceContext(context); loader.SetSurface("Present"); loader.SetBindingContext(settings); loader.SetActionDispatcher(settings);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Cui version="1"><Import Source="framework/CUI/Components/App.cui"/>
                  <Theme Name="user_preference"><Page><Container Type="Grid" RowSizes="Auto 1fr" Padding="16">
                    <Container Type="Grid" ColumnSizes="1fr Auto">
                      <Input Type="Text" AccessibleName="Document text" Value="{Binding Text, Mode=TwoWay}"/>
                      <AppAccentMenu Column="1"/>
                    </Container>
                    <Text Row="1" Text="Existing app content"/>
                  </Container></Page></Theme>
                </Cui>
                """);
            Check(root is not null && !diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error),
                "explicit shared app import loads through the real CUI pipeline: " + string.Join("; ", diagnostics));
            loader.WireBindings(root!);
            var window = new Window { Width = 600, Height = 800, Content = root }; window.Show();
            try
            {
                void Flush() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); }
                void Frame(string name)
                {
                    if (Environment.GetEnvironmentVariable("APP_ACCENT_EVIDENCE_DIR") is not { Length: > 0 } directory) return;
                    Directory.CreateDirectory(directory); Flush();
                    using var frame = window.CaptureRenderedFrame(); frame!.Save(Path.Combine(directory, name + ".png"));
                }
                IEnumerable<Control> Authored()
                {
                    var pending = new Stack<Control>(); pending.Push(root!); var seen = new HashSet<Control>(ReferenceEqualityComparer.Instance);
                    while (pending.TryPop(out var control))
                    {
                        if (!seen.Add(control)) continue; yield return control;
                        foreach (var child in control.GetLogicalChildren().OfType<Control>()) pending.Push(child);
                        if (control is CuiContainerControl container) foreach (var child in container.AuthoredChildren) pending.Push(child);
                        if (control is ItemsControl items) foreach (var child in items.Items.OfType<Control>()) pending.Push(child);
                    }
                }
                MenuItem Menu(string name) => Authored().OfType<MenuItem>().Single(item => Equals(item.Header, name));
                void Click(Control control)
                {
                    Flush(); var top = TopLevel.GetTopLevel(control)!;
                    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
                    var hit = top.InputHitTest(point) as Visual;
                    Check(control.IsEnabled && (ReferenceEquals(hit, control) || hit?.GetVisualAncestors().Contains(control) == true), "physical input reaches the actual accent control");
                    top.MouseDown(point, MouseButton.Left); top.MouseUp(point, MouseButton.Left); Flush();
                }
                Flush();
                Check(!root!.GetVisualDescendants().OfType<MenuItem>().Any(item => AutomationProperties.GetName(item) == "App accent colour"), "None has no active native accent control");
                preferences.Change(CuiAccentPreference.Default.WithMode(CuiAccentOverrideMode.IndividualApps)); Flush();
                var palette = Menu("App accent colour");
                Frame("accent-mode-enabled");
                Check(palette.IsEffectivelyVisible && palette.Icon is Avalonia.Media.IImage { Size.Width: > 0 }
                    && palette.GetVisualDescendants().OfType<Image>().Any(image => image.Source is not null),
                    $"IndividualApps reveals a real canonical palette icon: visible={palette.IsEffectivelyVisible}, icon={palette.Icon?.GetType().Name}, bounds={palette.Bounds}");
                var input = root!.GetVisualDescendants().OfType<TextBox>().Single(); Click(input); window.KeyTextInput("draft"); Flush();
                Click(palette); Check(palette.IsSubMenuOpen, "physical pointer opens the canonical app accent menu");
                Frame("accent-menu-open");
                var blue = Menu("Blue");
                Check(blue.Icon is Avalonia.Media.IImage { Size.Width: > 0 }
                    && blue.GetVisualDescendants().OfType<Image>().Any(image => image.Source is not null), "named accent option displays an actual colour swatch");
                Click(blue); await loader.WhenActionsIdleAsync(); Flush();
                Check(preferences.Current.EffectiveAccent.IndividualApps["app.present"] == "#2563EB" && !palette.IsSubMenuOpen,
                    "physical colour selection saves through Home and closes its native menu");
                Frame("accent-menu-closed");
                Check(input.IsFocused && owner.Text == "draft", "closing the accent menu restores original document input focus and content");
                window.KeyTextInput(" continued"); Flush();
                Check(owner.Text == "draft continued", "typing resumes in the same original app input after colour selection");
                var acknowledged = preferences.Writes;
                Click(palette); window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null); Flush();
                Frame("accent-menu-escape");
                Check(!palette.IsSubMenuOpen && input.IsFocused && preferences.Writes == acknowledged,
                    $"Escape closes app settings without a write and restores document focus: open={palette.IsSubMenuOpen}, inputFocus={input.IsFocused}, focused={window.FocusManager?.GetFocusedElement()?.GetType().Name}, writes={preferences.Writes}/{acknowledged}");
                Click(palette); Click(Menu("Reset app colour")); await loader.WhenActionsIdleAsync(); Flush();
                Check(!preferences.Current.EffectiveAccent.IndividualApps.ContainsKey("app.present"), "physical reset clears only the app's explicit colour");
                preferences.Change(CuiAccentPreference.Default.WithMode(CuiAccentOverrideMode.AllApps)); Flush();
                Check(!palette.IsEffectivelyVisible && settings.IsActionAvailable("AppAccentBlue") == false, "changing global mode retires the individual app menu and its actions");
                window.Width = 390; Flush();
                Check(input.Bounds.Width > 200 && owner.Text == "draft continued", "hidden accent controls preserve the original compact header input");
                Frame("accent-menu-hidden-compact");
            }
            finally { window.Close(); }
            return 0;
        }, CancellationToken.None);
    }
    private static T Value<T>(BrowserAppAccentSettings settings, string key)
    { Check(settings.TryGetValue(key, out var value), "accent binding exists: " + key); return (T)value!; }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); Console.WriteLine("APP_ACCENT_ASSERT " + message); }

    private sealed class Owner : ICuiWritableBindingContext, ICuiLifetimeAwareActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
    {
        public string Text { get; private set; } = string.Empty;
        public int Dispatches; public CuiActionDispatchLifetime LastLifetime;
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool TryGetValue(string path, out object? value) { value = Text; return path == "Text"; }
        public bool TrySetValue(string path, object? value)
        { if (path != "Text" || value is not string text) return false; Text = text; PropertyChanged?.Invoke(this, new(nameof(Text))); return true; }
        public bool? IsActionAvailable(string command) => command == "Original";
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken token = default) => DispatchWithLifetimeAsync(command, parameter, new(default, token));
        public ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
        { Dispatches++; LastLifetime = lifetime; return ValueTask.CompletedTask; }
    }
    private sealed class Preferences : IBrowserHomeThemePreferences
    {
        public CuiThemeDefinitionCatalog Catalog { get; } = new();
        public CuiThemePreferenceSnapshot Current { get; private set; } = new("Professional", 0, "device-local-fixture", false, new(), CuiAppearance.Dark);
        public string? Diagnostic => null;
        public bool CanChange { get; set; } = true;
        public int Writes; public bool Disposed; public TaskCompletionSource? Hold;
        public event EventHandler? Changed;
        internal void Change(CuiAccentPreference value) { Current = Current with { Revision = Current.Revision + 1, Accent = value }; Changed?.Invoke(this, EventArgs.Empty); }
        public async Task<HomeThemeOperationResult> SetAsync(string name, CuiAccessibilitySettings? accessibility = null, CancellationToken token = default, CuiAppearance? appearance = null, CuiAccentPreference? accent = null)
        {
            token.ThrowIfCancellationRequested(); Writes++;
            if (Hold is not null) await Hold.Task.WaitAsync(token);
            Current = Current with { Name = name, Revision = Current.Revision + 1, Accent = accent ?? Current.EffectiveAccent, Appearance = appearance ?? Current.Appearance };
            Changed?.Invoke(this, EventArgs.Empty); return new(true, "Succeeded");
        }
        public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
