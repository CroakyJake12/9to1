using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.Application;
using Haven.Infrastructure;
namespace Haven.Desktop.Views.Pages.Shelf;

/// <summary>Owns the actual loaded root and original library display for one native tab lifetime.</summary>
public sealed class ShelfNativeWorkspaceHost : ContentControl, IDisposable
{
    private readonly CuiControlLoader _loader;
    private readonly ShelfNativeWorkspacePage _page;
    private bool _disposed;
    private ShelfNativeWorkspaceHost(CuiControlLoader loader, ShelfNativeWorkspacePage page, Control root)
    { _loader = loader; _page = page; Content = root; }
    public IReadOnlyList<ShelfWorkspaceReview> Reviews => _page.Reviews;
    public string PresentationStatus => _page.PresentationStatus;
    public string PresentedRequestID => _page.PresentedRequestID;
    public Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    public static async Task<ShelfNativeWorkspaceHost> OpenAsync(HomeShelfLibraryOwner sameOwner,
        AuthenticatedResourceActor originalActor, Func<string, Task> actualHomeReview,
        Func<Action, Task> actualUiRender, CancellationToken token = default,
        Func<bool>? originalLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(originalActor);
        var workspace = await ShelfLibraryWorkspace.OpenAsync(sameOwner, originalActor, token, originalLifetime);
        return await MountOriginalWorkspaceAsync(workspace, actualHomeReview, actualUiRender, token);
    }
    public static async Task<ShelfNativeWorkspaceHost> OpenForOriginalDisplayAsync(HomeShelfLibraryOwner sameOwner,
        IShelfLibraryDisplay originalDisplay, Func<string, Task> actualHomeReview,
        Func<Action, Task> actualUiRender, CancellationToken token = default)
    {
        var workspace = await ShelfLibraryWorkspace.OpenForOriginalDisplayAsync(sameOwner, originalDisplay, token);
        return await MountOriginalWorkspaceAsync(workspace, actualHomeReview, actualUiRender, token);
    }
    private static async Task<ShelfNativeWorkspaceHost> MountOriginalWorkspaceAsync(ShelfLibraryWorkspace workspace,
        Func<string, Task> actualHomeReview, Func<Action, Task> actualUiRender, CancellationToken token)
    {
        var page = new ShelfNativeWorkspacePage(workspace, actualHomeReview, actualUiRender);
        var loader = new CuiControlLoader();
        try
        {
            const string name = "Haven.Desktop.Resources.Cui.ShelfWorkspace.cui";
            using var stream = typeof(ShelfNativeWorkspaceHost).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidDataException("Actual Shelf CUI resource unavailable.");
            using var reader = new StreamReader(stream);
            var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), name);
            if (parser.Diagnostics.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("Actual Shelf CUI source invalid.");
            loader.SetBindingContext(page); loader.SetActionDispatcher(page);
            var loaded = loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("Actual Shelf CUI could not mount.");
            loader.WireBindings(loaded.Root);
            // Recheck original owning selection after loader awaits/initial read; no new actor/root adoption.
            await workspace.ReloadAsync(token);
            token.ThrowIfCancellationRequested();
            return new(loader, page, loaded.Root);
        }
        catch { loader.Dispose(); page.Dispose(); throw; }
    }
    // Retained recovery is audit/navigation only; closing never grants a new owner mutation.
    public Task<Haven.Infrastructure.ShelfLibraryCommit> FinishRetainedAsync(string requestID, CancellationToken token = default)
        => _page.FinishRetainedAsync(requestID, token);
    public Task OpenRetainedHomeReviewAsync(string requestID) => _page.OpenRetainedHomeReviewAsync(requestID);
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        // Revoke before detaching. Accepted loader tasks remain observable through the same loader.
        _page.Dispose(); _loader.Dispose(); Content = null;
    }
}
