namespace HavenOS.Apps.Stacks;

/// <summary>Decision from revalidating a freeze at its expiry. ReconcileRequired means in-flight work or queued cascades must be handled before the freeze lifts — never applied silently.</summary>
public enum StackFreezeExpiryDecision { Expire, ReconcileRequired }

/// <summary>
/// Pure scoped-freeze evaluator (ED-STACKS-003). A Freeze is a temporary exclusion over an explicit selected-descendant set, optionally narrowed
/// to a target path. It blocks edits only for domains the freeze explicitly named (not merely any descendant), respects the path scope, requires
/// management permission for early thaw, and on expiry revalidates in-flight/queued work rather than silently applying. No engine mutation here.
/// </summary>
public static class StackFreezeScope
{
    /// <summary>Whether an edit to (domainId, path) is blocked by this freeze. True only when the freeze is Active, the domain is in the explicitly
/// selected descendant set, and (if a target path scope is set) the path lies within it.</summary>
    public static bool IsEditBlocked(StackFreeze freeze, Guid domainId, string path)
    {
        ArgumentNullException.ThrowIfNull(freeze);
        if (freeze.State != StackFreezeState.Active) return false;
        if (!freeze.DescendantDomainIds.Contains(domainId)) return false;
        // A null target path scopes the freeze to the whole selected descendant subtree; a set path narrows it by prefix.
        if (freeze.TargetPath is { Length: > 0 } scope && !IsWithin(path, scope)) return false;
        return true;
    }

    /// <summary>A newly created descendant inherits the freeze only if its recorded ancestry includes an explicitly selected frozen domain.</summary>
    public static bool InheritsOnNewDescendant(StackFreeze freeze, IEnumerable<Guid> newDomainAncestry)
    {
        ArgumentNullException.ThrowIfNull(freeze); ArgumentNullException.ThrowIfNull(newDomainAncestry);
        if (freeze.State != StackFreezeState.Active) return false;
        return newDomainAncestry.Any(ancestor => freeze.DescendantDomainIds.Contains(ancestor));
    }

    /// <summary>Early thaw before a configured expiry requires management permission.</summary>
    public static bool RequiresManagementForEarlyThaw(StackFreeze freeze, DateTimeOffset now)
    {
        _ = ArgumentNullException.ThrowIfNull(freeze);
        return freeze.ExpiresAt is null || now < freeze.ExpiresAt.Value;
    }

    /// <summary>On reaching expiry, revalidate in-flight work and queued cascades. If any exist, the freeze cannot lift yet — report reconcile-required.
/// rather than silently applying pending changes.</summary>
    public static StackFreezeExpiryDecision RevalidateOnExpiry(StackFreeze freeze, bool hasInProgressOrQueued)
    {
        ArgumentNullException.ThrowIfNull(freeze);
        return hasInProgressOrQueued ? StackFreezeExpiryDecision.ReconcileRequired : StackFreezeExpiryDecision.Expire;
    }

    private static bool IsWithin(string path, string scope)
    {
        var normalizedPath = path.Replace('\\', '/');
        var normalizedScope = scope.Replace('\\', '/').TrimEnd('/');
        return string.Equals(normalizedPath, normalizedScope, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedScope + "/", StringComparison.OrdinalIgnoreCase);
    }
}
