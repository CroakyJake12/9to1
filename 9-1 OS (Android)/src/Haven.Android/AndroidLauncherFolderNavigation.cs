using NineToOne.Launcher;

namespace Haven.Android;

public sealed class AndroidLauncherFolderNavigation(HomeLauncherSession sessions)
{
    public async Task<LauncherFolder> ReadAsync(LauncherSessionSnapshot original, Guid folderId,
        Func<bool> originalHostCurrent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(originalHostCurrent);
        ct.ThrowIfCancellationRequested();
        if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original folder view changed.");
        var folder = original.Layout.Current.Folders.SingleOrDefault(item => item.Id == folderId)
            ?? throw new InvalidOperationException("This folder was not in the displayed launcher layout.");
        if (!await sessions.IsCurrentAsync(original, ct) || !originalHostCurrent())
            throw new UnauthorizedAccessException("The original folder or launcher session changed. Reopen it.");
        ct.ThrowIfCancellationRequested();
        return folder;
    }
}
