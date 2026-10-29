using Haven.Application;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Resolves the Files-visible folder that Boards saves into, and makes sure it exists.
///
/// Owner direction, 29 October 2026: "everything is saved to that unless the user picks another
/// location", and the reported failure was a board that "says saved locally, gives no location" while
/// the Boards folder in Files was empty. The cause was that Boards and Files answered from different
/// places: Files used its registered workspace while Boards wrote private GUID-named drafts.
///
/// This type keeps them on the same answer: Boards asks Files where the boards folder is, and if Files
/// has never been set up, the default 9to1 workspace is created first (no connect step, no account,
/// no gate). A workspace that was registered and then disappeared is deliberately not re-created here,
/// because the owner must choose between re-locating and starting a new one.</summary>
public static class BoardsWorkspaceFolders
{
    /// <summary>The physical folder for an app inside the device 9to1 workspace, or null when it cannot
    /// be established. Callers keep working without it - a board always remains a recoverable draft -
    /// but they must say so rather than implying the board was placed in Files.</summary>
    public static async Task<string?> ResolveAppDirectoryAsync(IServiceProvider services, string appId,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var workspaces = services.GetService<NativeFilesWorkspaceService>();
        var authority = services.GetService<NativeFilesWorkspaceAuthority>();
        if (workspaces is null || authority is null) return null;

        // A development copy carries its own application-data root, so its fallback location follows the
        // running instance instead of whichever install first wrote the default.
        var paths = services.GetService<IAppPaths>();
        if (paths is not null) workspaces.ApplicationDataFallbackDirectory ??= paths.DataDirectory;

        var directory = await authority.ResolveAppDirectoryAsync(appId, token).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(directory)) return directory;

        var composition = services.GetService<HomeNativeWindowsComposition>();
        if (composition?.LocalStoreOwnership is not HomeLocalStoreOwnership ownership) return null;
        try
        {
            await workspaces.ConfigureDefaultWorkspaceAsync(ownership, token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or
            UnauthorizedAccessException or InvalidDataException)
        {
            // Already configured by another tab, or the workspace needs the owner's decision. Neither is
            // a reason to lose a board: the caller keeps the draft and names the problem.
            LastResolveFailure = error.Message.Split('\n', 2)[0];
            return null;
        }
        var created = await authority.ResolveAppDirectoryAsync(appId, token).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(created) ? null : created;
    }

    /// <summary>Set when the workspace folder could not be established, so the surface can name the real
    /// reason instead of showing an unexplained missing location.</summary>
    public static string? LastResolveFailure { get; private set; }
}
