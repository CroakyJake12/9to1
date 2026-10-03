// Test-only host boundaries. No domain/platform acceptance is provided here.
// Actual Render uses ShowStatus; every unused browser bootstrap method throws.
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
    internal static void Register(BrowserSurfaceRegistry registry) => throw new InvalidOperationException("Browser composition is outside this native Render fixture.");
    internal static void RegisterPrivateAccountSettings(BrowserSurfaceRegistry registry) => throw new InvalidOperationException("Account composition is outside this native Render fixture.");
}
