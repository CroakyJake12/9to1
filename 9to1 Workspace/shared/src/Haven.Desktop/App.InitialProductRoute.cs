#if !ANDROID
using System.Reflection;
using Haven.Desktop.Views.Shell;

namespace Haven.Desktop;

public sealed partial class App
{
    /// <summary>Starts the SAME original Desktop bootstrap and platform lifetime.</summary>
    public static void RunOriginalDesktop(string[] args) => Program.Main(args);

    internal enum OriginalUnifiedProduct { None, Canvas, Boards }

    // Both thin executables share one process-lifetime selection. Keeping this tiny
    // bootstrap owner separate lets tests exercise the production CAS without
    // resetting the actual process, constructing Home or launching a native app.
    internal sealed class OriginalUnifiedProductEntry
    {
        private int _product;
        internal OriginalUnifiedProduct Product => (OriginalUnifiedProduct)Volatile.Read(ref _product);

        internal void Capture(OriginalUnifiedProduct product,
            IEnumerable<AssemblyMetadataAttribute> metadata, bool applicationStarted)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            if (product is not (OriginalUnifiedProduct.Canvas or OriginalUnifiedProduct.Boards))
                throw new ArgumentOutOfRangeException(nameof(product));
            if (applicationStarted || Interlocked.CompareExchange(ref _product, (int)product, 0) != 0)
                throw new InvalidOperationException("Capture the native product entry once, before the original application starts.");
            // A refused declaration or failed bootstrap consumes the same selector;
            // another app cannot reuse a partially started or retired Home graph.
            _ = ResolveOriginalInitialProductDeclaration(metadata, product);
        }
    }

    private static readonly OriginalUnifiedProductEntry _originalUnifiedProductEntry = new();

    /// <summary>The compiled Canvas executable selects a route in the SAME Home process.
    /// This process-lifetime selector creates no Home, Files store, approval or app grant.</summary>
    public static void RunOriginalCanvasDesktop(string[] args) => RunOriginalUnifiedProductDesktop(args, OriginalUnifiedProduct.Canvas);

    /// <summary>Entry-convergence host over the SAME original Home/Notes/CUI owners.
    /// Existing .9to1board data and standalone capabilities require migration/parity
    /// acceptance before this entry can replace the current standalone package.</summary>
    public static void RunOriginalBoardsDesktop(string[] args) => RunOriginalUnifiedProductDesktop(args, OriginalUnifiedProduct.Boards);

    private static void RunOriginalUnifiedProductDesktop(string[] args, OriginalUnifiedProduct product)
    {
        ArgumentNullException.ThrowIfNull(args);
        _originalUnifiedProductEntry.Capture(product,
            typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), Avalonia.Application.Current is not null);
        if (product == OriginalUnifiedProduct.Boards) DemandSupportedOriginalBoardsArguments(args);
        // Preserve the original argument parser and Canvas caller contract unchanged.
        Program.Main(args);
    }

    internal static void DemandSupportedOriginalBoardsArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        // This new entry is not yet a file-association/package replacement. Refuse
        // an explicit previous-standalone file request instead of showing an empty
        // library as if that file opened. Home locator values remain the original
        // parser's responsibility, including directories with this suffix.
        if (args.Length != 0 && !args[0].StartsWith("--", StringComparison.Ordinal) &&
            args[^1].EndsWith(".9to1board", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("This shared Boards entry does not yet import .9to1board files. Preserve the file and use the current standalone app until migration is available.");
    }

    internal static string? ValidateOriginalInitialProductDeclaration(IEnumerable<AssemblyMetadataAttribute> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var declarations = metadata.Where(row => row.Key == "9to1.InitialApp").Take(2).ToArray();
        if (declarations.Length == 0) return null;
        if (declarations.Length != 1 || declarations[0].Value is not ("files" or "sites" or "dev" or "write" or "present" or "assistants" or "canvas" or "boards"))
            throw new InvalidDataException("The owning executable's initial App declaration is unsupported or duplicated.");
        return declarations[0].Value;
    }

    // Compatibility overload for the existing compiled Canvas boundary controls.
    internal static string? ResolveOriginalInitialProductDeclaration(IEnumerable<AssemblyMetadataAttribute> metadata, bool canvasEntry)
        => ResolveOriginalInitialProductDeclaration(metadata, canvasEntry ? OriginalUnifiedProduct.Canvas : OriginalUnifiedProduct.None);

    internal static string? ResolveOriginalInitialProductDeclaration(IEnumerable<AssemblyMetadataAttribute> metadata, OriginalUnifiedProduct entry)
    {
        var declared = ValidateOriginalInitialProductDeclaration(metadata);
        if (entry == OriginalUnifiedProduct.None) return declared;
        var selected = entry switch
        {
            OriginalUnifiedProduct.Canvas => "canvas",
            OriginalUnifiedProduct.Boards => "boards",
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
        if (declared is not null && declared != selected)
            throw new InvalidDataException("The native entry conflicts with the owning executable's compiled product declaration.");
        return selected;
    }

    internal static string OriginalUnifiedProductRoute(OriginalUnifiedProduct product) => product switch
    {
        OriginalUnifiedProduct.Canvas => "9to1://canvas",
        OriginalUnifiedProduct.Boards => "9to1://boards",
        _ => throw new ArgumentOutOfRangeException(nameof(product))
    };

    private static string? OriginalInitialProduct => ResolveOriginalInitialProductDeclaration(
        typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>(), _originalUnifiedProductEntry.Product);

    private static Task? OpenOriginalInitialUnifiedProductRouteAsync(UnifiedWorkspaceHost workspace, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var product = OriginalInitialProduct;
        if (product is null) return Task.CompletedTask;
        var selected = product switch
        {
            "canvas" => OriginalUnifiedProduct.Canvas,
            "boards" => OriginalUnifiedProduct.Boards,
            _ => throw new InvalidOperationException("This compiled product requires its original shell entry.")
        };
        // Null is the unified owner's explicit accepted-startup-retirement result.
        // The actual source, registration and native child still belong to that owner.
        return workspace.TryAcquireInitialUnifiedProductRoute(selected, token);
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
