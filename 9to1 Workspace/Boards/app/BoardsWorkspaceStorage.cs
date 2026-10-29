using Haven.Application;

namespace CakeOS.Apps.Boards.App;

/// <summary>Where standalone Boards (running outside Haven, with no Files workspace service of its own)
/// puts boards on the device. It follows the same shared root rule as Files rather than inventing a
/// second answer: a 9to1 folder under Documents, inspected and completed, never destroyed. A workspace
/// that has already been registered by Files is found through Files when Boards runs inside Haven;
/// standalone Boards uses the default root because it has a separate application-data store and cannot
/// see another installation's registration. This is recorded as a known gap, not papered over.</summary>
public static class BoardsWorkspaceStorage
{
    /// <summary>The boards folder inside the device workspace, or null when it could not be
    /// established - in which case boards stay recoverable drafts and the surface says so.</summary>
    public static string? DefaultBoardsDirectory(string applicationDataFallback)
    {
        try
        {
            var planned = LocalWorkspaceFolders.Provision(
                LocalWorkspaceFolders.ResolveDefaultRoot(applicationDataFallback).RootDirectory);
            return Path.Combine(planned.AppFoldersDirectory, "Boards");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            or InvalidDataException or ArgumentException)
        { return null; }
    }
}
