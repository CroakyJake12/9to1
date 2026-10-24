using System.Runtime.CompilerServices;
using Haven.Application;

namespace HavenOS.Files.NativeHost;

/// <summary>Owner-issued descriptive metadata availability. This is not a page, permission,
/// navigation capability, storage claim or native application readiness observation.</summary>
public sealed class FilesOriginalHomeLibraryAvailability
{
    internal FilesOriginalHomeLibraryAvailability(bool available, Guid? storeId, string code, string message)
    { IsAvailable = available; StoreId = storeId; Code = code; Message = message; }
    public bool IsAvailable { get; }
    public Guid? StoreId { get; }
    public string Code { get; }
    public string Message { get; }
}

public sealed partial class FilesNativeBrowserService
{
    private sealed record HomeLibraryAvailabilityOwner(AuthenticatedResourceActor Actor);
    private readonly ConditionalWeakTable<FilesOriginalHomeLibraryAvailability, HomeLibraryAvailabilityOwner> _homeLibraryAvailability = new();
    public bool IsIssuedOriginalHomeLibraryAvailability(FilesOriginalHomeLibraryAvailability value, AuthenticatedResourceActor actor) =>
        _homeLibraryAvailability.TryGetValue(value, out var original) && original.Actor == actor;

    public async Task<FilesOriginalHomeLibraryAvailability> ObserveOriginalHomeLibraryAvailabilityWithinSourceAsync(
        AuthenticatedResourceActor originalActor, Func<bool> originalLifetime,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
        FilesOriginalHomeLibraryAvailability Issue(bool available, Guid? store, string code, string message)
        {
            var value = new FilesOriginalHomeLibraryAvailability(available, store, code, message);
            _homeLibraryAvailability.Add(value, new(originalActor)); return value;
        }
        if (!source.Invoke(originalLifetime)) return Issue(false, null, "LibraryRetired", "This Files Library observation retired.");
        if (await source.Observe(() => actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != originalActor)
            return Issue(false, null, "LibraryProfileChanged", "Reopen Library for the current Home profile.");
        var workspace = await source.Observe(() => workspaces.GetOriginalCurrentAsync(null, source, token)).ConfigureAwait(false);
        if (workspace is null)
            return Issue(false, null, "FilesSetupRequired", "Set up Files in Home before browsing its folder references.");
        if (workspace.Actor != originalActor ||
            await source.Observe(() => actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false) != originalActor)
            return Issue(false, null, "LibraryProfileChanged", "The original Files profile changed.");
        var current = await source.Observe(() => workspaces.CaptureOriginalReadCheckAsync(
            workspace, originalLifetime, source, token).AsTask()).ConfigureAwait(false);
        if (!await source.Observe(() => current(token).AsTask()).ConfigureAwait(false) || !source.Invoke(originalLifetime))
            return Issue(false, null, "FilesOwnerChanged", "Refresh Library after the original Files owner is available.");
        return Issue(true, workspace.Configuration.StoreId, "FilesMetadataAvailable", "The original Files metadata owner is available.");
    }

    /// <summary>The SAME maintained page issuer and protected original read path. The owner
    /// checks each real row; this bridge adds no query engine, persistence or read authority.</summary>
    public Task<FilesNativeBrowserPage> ListOriginalHomeLibraryPageWithinSourceAsync(
        AuthenticatedResourceActor originalActor, Guid expectedStoreId, string search, FilesNativeBrowserCursor? cursor,
        Func<bool> originalLifetime, Action<Action> scope, Action<Task> retain, CancellationToken token) =>
        ListOriginalHomeLibraryFolderPageWithinSourceAsync(originalActor, expectedStoreId, null,
            search, cursor, originalLifetime, scope, retain, token);

    /// <summary>Same original issuer for an actual canonical folder; no guessed offsets or filesystem scan.</summary>
    public async Task<FilesNativeBrowserPage> ListOriginalHomeLibraryFolderPageWithinSourceAsync(
        AuthenticatedResourceActor originalActor, Guid expectedStoreId, Guid? originalParent,
        string search, FilesNativeBrowserCursor? cursor, Func<bool> originalLifetime,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
        source.Invoke(() => { Demand(); return true; });
        var page = await source.Observe(() => ListOriginalDeveloperAsync(originalActor, source,
            parentID: originalParent, search: search, cursor: cursor, token: token, expectedStoreId: expectedStoreId)).ConfigureAwait(false);
        await source.ObserveVoid(() => RevalidateOriginalHomeLibraryPageWithinSourceAsync(page, originalActor,
            originalLifetime, scope, retain, token)).ConfigureAwait(false);
        return page;
        void Demand()
        {
            if (expectedStoreId == Guid.Empty || originalParent == Guid.Empty || !originalLifetime())
                throw new UnauthorizedAccessException("The original Files Library observation retired.");
        }
    }
    public async Task RevalidateOriginalHomeLibraryPageWithinSourceAsync(FilesNativeBrowserPage page,
        AuthenticatedResourceActor originalActor, Func<bool> originalLifetime,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = FilesOriginalParentSourceCallbacks.Create(scope, retain);
        source.Invoke(() =>
        {
            if (!originalLifetime()) throw new UnauthorizedAccessException("The original Files Library observation retired.");
            if (!_originalPages.TryGetValue(page, out var issued) || issued.Actor != originalActor ||
                issued.Workspace.Configuration.StoreId != page.StoreID)
                throw new UnauthorizedAccessException("Retain the SAME privately issued original Files Library page.");
            return true;
        });
        await source.ObserveVoid(() => RevalidateOriginalDeveloperAsync(page, originalActor, source, token)).ConfigureAwait(false);
        var current = await source.Observe(() => CaptureOriginalDeveloperPageReadCheckAsync(page, originalActor,
            originalLifetime, source, token).AsTask()).ConfigureAwait(false);
        if (!await source.Observe(() => current(token).AsTask()).ConfigureAwait(false) || !source.Invoke(originalLifetime))
            throw new UnauthorizedAccessException("The original Files Library observation retired.");
    }
}
