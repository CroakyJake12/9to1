#if !ANDROID
using System.Reflection;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop;

public sealed partial class App
{
    // This is the owning executable's compiled product declaration. It chooses a
    // route only; Home startup, current actor/store and tab publication still
    // belong to their original owners and are checked by OpenFilesAsync.
    private Task OpenOriginalInitialProductRouteAsync(MainView shell, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var declarations = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(row => row.Key == "9to1.InitialApp").ToArray();
        if (declarations.Length == 0) return Task.CompletedTask;
        if (declarations.Length != 1 || declarations[0].Value is not ("files" or "sites" or "dev"))
            throw new InvalidDataException("The owning executable's initial App declaration is unsupported or duplicated.");
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The owning Windows product requires its original Windows host.");
        if (_actualWindowsHome is null)
            throw new UnauthorizedAccessException("The owning native product requires the original Windows Home composition.");
        // The caller acquires and awaits this SAME task inside actual App startup,
        // after Home start and shell initialization/session restore. No second
        // window, provider, approval or cached Ready observation is constructed.
        if (declarations[0].Value == "files") return shell.OpenFilesAsync(false, token);
        var provider = _services ?? throw new InvalidOperationException("The original App provider is unavailable.");
        var window = _actualSameProcessNativeWindow
            ?? throw new InvalidOperationException("The original native window is unavailable.");
        if (!ReferenceEquals(shell, _actualSameProcessNativeShell) || !ReferenceEquals(window.DataContext, shell))
            throw new UnauthorizedAccessException("Retain the SAME owning native shell/window.");
        if (declarations[0].Value == "dev") return shell.OpenOriginalInitialDevelopmentCatalogAsync(token);
        return shell.OpenOriginalSitesAsync(provider, _actualWindowsHome, token, window.AcquireOriginalWindowLifetime());
    }
}
#endif
