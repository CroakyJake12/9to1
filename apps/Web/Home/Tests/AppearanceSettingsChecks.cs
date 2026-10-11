using CakeOS.Cui;
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;
using NineToOne.Web;

internal static class AppearanceSettingsChecks
{
    internal static async Task Run()
    {
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
        public bool CanChange => true;
        public event EventHandler? Changed;
        internal int Writes;
        internal bool Disposed;
        internal TaskCompletionSource? Release;
        internal void Notify() => Changed?.Invoke(this, EventArgs.Empty);
        public async Task<HomeThemeOperationResult> SetAsync(string name, CuiAccessibilitySettings? accessibility = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (Release is not null) await Release.Task;
            Writes++;
            Current = Current with { Name = name, Revision = Current.Revision + 1 };
            Diagnostic = null;
            Notify();
            return new(true, "Succeeded"); // Controlled receipt for presentation-only regression; no storage claim.
        }
        public Task RefreshAsync(CancellationToken token = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
