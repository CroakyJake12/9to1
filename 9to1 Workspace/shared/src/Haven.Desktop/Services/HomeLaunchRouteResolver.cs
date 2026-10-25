using Haven.Application;
using Haven.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// Resolves a validated Home app catalogue identity to its canonical native content address BEFORE
/// strict native admission. The real built-in Chat mode is not a standalone native surface: it is
/// owned by the canonical Spaces Chat space (<see cref="SpaceRegistry.ChatSpaceId"/>), so launching
/// a Chat card navigates to <c>9to1://spaces/selected/{ChatSpaceId}</c>, which the registered Spaces
/// owner admits. All other apps keep their plain <c>9to1://key</c> route (unchanged admission/refusal).
/// This helper is used by both the Home page and the routing regression; the caller is responsible for
/// validating the key against the real catalogue before calling it.
/// </summary>
internal static class HomeLaunchRouteResolver
{
    public static AppContentAddress Resolve(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var normalized = key.Trim().ToLowerInvariant();
        if (normalized == "boards")
            return AppContentAddress.Parse("browse://home/library/boards");
        if (normalized == "chat")
            return AppContentAddress.Parse($"9to1://spaces/selected/{SpaceRegistry.ChatSpaceId:D}");
        return AppContentAddress.Parse($"9to1://{normalized}");
    }
}
