using NineToOne.Launcher;

namespace Haven.Android;

/// <summary>Page changes on the same privately issued Home snapshot, never an ambient replacement.</summary>
public sealed class AndroidLauncherPageNavigation(HomeLauncherSession sessions)
{
    public async Task<LauncherSessionSnapshot> SelectAsync(LauncherSessionSnapshot original, Guid pageId,
        Func<bool> originalHostCurrent, CancellationToken ct = default,
        Action<LauncherSessionSnapshot, LauncherStoredLayout>? knownCommitted = null)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(originalHostCurrent);
        void RequireHost()
        {
            ct.ThrowIfCancellationRequested();
            if (!originalHostCurrent()) throw new UnauthorizedAccessException("The original launcher page view closed or changed.");
        }
        RequireHost();
        if (!original.Layout.Current.Pages.Any(page => page.Id == pageId))
            throw new InvalidOperationException("The selected page is not part of the displayed launcher layout.");
        if (!await sessions.IsCurrentAsync(original, ct)) throw new UnauthorizedAccessException("The original launcher session changed.");
        RequireHost();
        // The denial-only predicate must read managed host fields only: Home evaluates it under its real writer lease.
        var saved = await sessions.EditForOriginalHostAsync(original, layout => LauncherLayoutEdits.SelectPage(layout, pageId), originalHostCurrent, ct);
        // Preserve actual returned acknowledgement before refusing stale native presentation; never adopt a replacement view.
        knownCommitted?.Invoke(original, saved);
        RequireHost();
        var renewed = await sessions.ReadAfterEditAsync(original, saved, ct);
        RequireHost();
        if (renewed is null || renewed.Layout.Current.ActivePageId != pageId ||
            renewed.Layout.AuthorityId != saved.AuthorityId || renewed.Layout.Revision != saved.Revision)
            throw new IOException("The committed page changed before presentation. Reopen the launcher.");
        return renewed;
    }
}
