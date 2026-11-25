namespace HavenOS.Apps.Stacks;

/// <summary>Classification of a store/manifest load failure. Infrastructure failures are never reported as ordinary source deletion.</summary>
public enum StackLoadFailureKind { OrdinaryEmpty, InfrastructureMissing, Corrupt }

/// <summary>A violation preventing public publication: a private or secret-marked resource reachable in the projection's history.</summary>
public sealed record StackProjectionViolation(string Path, string Reason);

/// <summary>
/// Pure public-projection verifier (ED-STACKS-005). A public projection must exclude private resources and secrets from the entire reachable
/// object/history set — inherited ancestor state included — rather than merely hiding them in a browser. It also distinguishes an infrastructure/
/// manifest failure from a genuinely empty source so corruption or a missing store is never mistaken for ordinary deletion. No I/O occurs here.
/// </summary>
public static class StackPublicProjectionModel
{
    /// <summary>Collect every path reachable through the domain ancestry's effective state (inherited ancestors included), keyed to its resource visibility.</summary>
    public static IReadOnlyDictionary<string, StackResource?> ComputeReachable(IEnumerable<StackDomainRecord> domainAncestry)
    {
        ArgumentNullException.ThrowIfNull(domainAncestry);
        var reachable = new Dictionary<string, StackResource?>(StringComparer.OrdinalIgnoreCase);
        // Later (descendant) entries override ancestors for the same path, matching effective-tree combination.
        foreach (var domain in domainAncestry)
            foreach (var entry in domain.BaseTree)
                reachable[entry.Key] = entry.Value;
        return reachable;
    }

    /// <summary>Whether a resource is secret-marked. A resource whose content contains the sentinel marker is treated as a secret.</summary>
    private static bool IsSecret(StackResource? resource)
    {
        if (resource is null) return false;
        // Cheap containment check over UTF-8 bytes for the agreed secret marker.
        var marker = System.Text.Encoding.UTF8.GetBytes("9to1:secret");
        return ContainsSequence(resource.Content, marker);
    }

    /// <summary>Return violations for any Private-visibility or secret-marked resource present in the reachable set. Empty means safe to publish.</summary>
    public static IReadOnlyList<StackProjectionViolation> FindViolations(IReadOnlyDictionary<string, StackResource?> reachable)
    {
        ArgumentNullException.ThrowIfNull(reachable);
        var violations = new List<StackProjectionViolation>();
        foreach (var entry in reachable)
        {
            if (entry.Value is not { } resource) continue;
            if (resource.Visibility == StackVisibility.Private) violations.Add(new StackProjectionViolation(entry.Key, "Private resource is reachable in the public projection."));
            else if (IsSecret(resource)) violations.Add(new StackProjectionViolation(entry.Key, "Secret-marked content is reachable in the public projection."));
        }
        return violations;
    }

    /// <summary>A projection is safe to publish only when no private/secret resource is reachable anywhere in its history.</summary>
    public static bool IsSafeToPublish(IReadOnlyDictionary<string, StackResource?> reachable) => FindViolations(reachable).Count == 0;

    /// <summary>Classify a load failure. A missing file/store or an unreadable/corrupt manifest is infrastructure, not ordinary empty source.</summary>
    public static StackLoadFailureKind ClassifyLoadFailure(bool fileMissing, bool manifestCorrupt) =>
        fileMissing ? StackLoadFailureKind.InfrastructureMissing : manifestCorrupt ? StackLoadFailureKind.Corrupt : StackLoadFailureKind.OrdinaryEmpty;

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return false;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++) if (haystack[i + j] != needle[j]) { match = false; break; }
            if (match) return true;
        }
        return false;
    }
}
