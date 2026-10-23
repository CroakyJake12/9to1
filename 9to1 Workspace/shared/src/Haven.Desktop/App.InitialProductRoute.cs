#if !ANDROID
using System.Reflection;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop;

public sealed partial class App
{
    /// <summary>Starts the SAME original Desktop bootstrap and platform lifetime.</summary>
    public static void RunOriginalDesktop(string[] args) => Program.Main(args);

    private static int _originalCanvasEntry;

    /// <summary>The compiled Canvas executable selects a route in the SAME Home process.
    /// This process-lifetime selector creates no Home, Files store, approval or app grant.</summary>
    public static void RunOriginalCanvasDesktop(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (Avalonia.Application.Current is not null || Interlocked.CompareExchange(ref _originalCanvasEntry, 1, 0) != 0)
            throw new InvalidOperationException("Capture the Canvas entry once, before the original native application starts.");
        // Keep this selection immutable even after a failed bootstrap. A failed process
        // cannot re-enter as another product using a partially retired Home graph.
        _ = ResolveOriginalInitialProductDeclaration(typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), true);
        Program.Main(args);
    }

    internal static string? ValidateOriginalInitialProductDeclaration(IEnumerable<AssemblyMetadataAttribute> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var declarations = metadata.Where(row => row.Key == "9to1.InitialApp").Take(2).ToArray();
        if (declarations.Length == 0) return null;
        if (declarations.Length != 1 || declarations[0].Value is not ("files" or "sites" or "dev" or "write" or "present" or "assistants" or "canvas"))
            throw new InvalidDataException("The owning executable's initial App declaration is unsupported or duplicated.");
        return declarations[0].Value;
    }

    internal static string? ResolveOriginalInitialProductDeclaration(IEnumerable<AssemblyMetadataAttribute> metadata, bool canvasEntry)
    {
        var declared = ValidateOriginalInitialProductDeclaration(metadata);
        if (!canvasEntry) return declared;
        if (declared is not (null or "canvas"))
            throw new InvalidDataException("The Canvas entry conflicts with the owning executable's compiled product declaration.");
        return "canvas";
    }
    private static string? OriginalInitialProduct => ResolveOriginalInitialProductDeclaration(
        typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), Volatile.Read(ref _originalCanvasEntry) != 0);

    private static Task? OpenOriginalInitialUnifiedProductRouteAsync(UnifiedWorkspaceHost workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (OriginalInitialProduct is null) return Task.CompletedTask;
        if (OriginalInitialProduct != "canvas")
            throw new InvalidOperationException("This compiled product requires its original shell entry.");
        // Null is the unified owner's explicit accepted-startup-retirement result.
        // The actual source, registration and native child still belong to that owner.
        return workspace.TryAcquireInitialCanvasRoute(token);
    }

    // This is the owning executable's compiled product declaration. It chooses a
    // route only; Home startup, current actor/store and tab publication still
    // belong to their original owners and are checked by OpenFilesAsync.
    private Task OpenOriginalInitialProductRouteAsync(MainView shell, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var product = OriginalInitialProduct;
        if (product is null) return Task.CompletedTask;
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The owning Windows product requires its original Windows host.");
        if (_actualWindowsHome is null)
            throw new UnauthorizedAccessException("The owning native product requires the original Windows Home composition.");
        // The caller acquires and awaits this SAME task inside actual App startup,
        // after Home start and shell initialization/session restore. No second
        // window, provider, approval or cached Ready observation is constructed.
        if (product == "files") return shell.OpenFilesAsync(false, token);
        var provider = _services ?? throw new InvalidOperationException("The original App provider is unavailable.");
        var window = _actualSameProcessNativeWindow
            ?? throw new InvalidOperationException("The original native window is unavailable.");
        if (!ReferenceEquals(shell, _actualSameProcessNativeShell) || !ReferenceEquals(window.DataContext, shell))
            throw new UnauthorizedAccessException("Retain the SAME owning native shell/window.");
        if (product == "dev") return shell.OpenOriginalInitialDevelopmentCatalogAsync(token);
        if (product == "assistants") return OpenOriginalAssistantsForShellAsync(shell, token);
        if (product is "write" or "present")
            return shell.OpenOriginalInitialDocumentProductAsync(product, provider,
                _actualWindowsHome, token, window.AcquireOriginalWindowLifetime());
        return shell.OpenOriginalSitesAsync(provider, _actualWindowsHome, token, window.AcquireOriginalWindowLifetime());
    }
}
#endif
