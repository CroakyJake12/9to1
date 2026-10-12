// Test-only host boundaries. No domain/platform acceptance is provided here.
// Actual Render uses ShowStatus. Theme observation is deterministic fixture state,
// never evidence of Home/provider persistence or browser preference acceptance.
using CakeOS.Cui.Themes;
using HavenOS.Home.Core;
using NineToOne.Web.Accounts;
namespace NineToOne.Web;

internal static class Program
{
    internal static readonly List<(string Code, string Message)> Statuses = [];
    internal static void ShowStatus(string code, string message) => Statuses.Add((code, message));
    internal static void Attach(BrowserApplication application) => throw Unused();
    internal static void WriteFragment(string fragment, bool replace) => throw Unused();
    internal static string ReadFragment() => throw Unused();
    private static Exception Unused() => new InvalidOperationException("This native Render fixture must not use browser bootstrap shims.");
}

internal static class BrowserFeatureComposition
{
    internal static AccountSettingsFeature Register(BrowserSurfaceRegistry registry) => throw new InvalidOperationException("Browser composition is outside this native Render fixture.");
    internal static AccountSettingsFeature RegisterPrivateAccountSettings(BrowserSurfaceRegistry registry) => throw new InvalidOperationException("Account composition is outside this native Render fixture.");
    internal static Task<IBrowserHomeThemePreferences> CreateThemePreferencesAsync() => Task.FromResult<IBrowserHomeThemePreferences>(new RenderFixtureTheme());

    private sealed class RenderFixtureTheme : IBrowserHomeThemePreferences
    {
        public CuiThemeDefinitionCatalog Catalog { get; } = new();
        public CuiThemePreferenceSnapshot Current { get; } = new("Professional",0,"controlled-render-fixture",false,new());
        public string? Diagnostic => "Controlled native fixture theme; no provider or persistence acceptance.";
        public bool CanChange => false;
        public event EventHandler? Changed { add { } remove { } }
        public Task<HomeThemeOperationResult> SetAsync(string name,CuiAccessibilitySettings? accessibility=null,CancellationToken token=default,CuiAppearance? appearance=null)
            => Task.FromException<HomeThemeOperationResult>(new InvalidOperationException("Theme mutation is outside this navigation/lifetime fixture."));
        public Task RefreshAsync(CancellationToken token=default) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

// This fixture never enters Home's JS storage/bootstrap path. Unexpected use is
// an assertion failure, not an in-memory persistence substitute.
internal sealed class BrowserHomeLocalPreferences : IHomeLocalPreferences
{
    public string? Read() => throw new InvalidOperationException("Browser Home storage is outside this native render/navigation fixture.");
    public void Write(string json) => throw new InvalidOperationException("Browser Home storage is outside this native render/navigation fixture.");
}
