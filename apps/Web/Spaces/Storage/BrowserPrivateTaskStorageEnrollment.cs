using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Weak actual-registry identity discovers only its SAME issued private storage owner.
/// Membership is not signed identity, canonical Task admission, a Home grant or an Available route.</summary>
[SupportedOSPlatform("browser")]
internal static class BrowserPrivateTaskStorageEnrollment
{
    private static readonly ConditionalWeakTable<BrowserSurfaceRegistry, BrowserPrivateTaskStorage> Owners = new();

    internal static BrowserPrivateTaskStorage GetCurrent(BrowserSurfaceRegistry registry)
    {
        if (!Owners.TryGetValue(registry, out var actual))
            throw new InvalidOperationException("No same-registry private Task storage lifetime is enrolled.");
        actual.DemandPrivateContextCurrent();
        return actual;
    }

    internal static void Register(BrowserSurfaceRegistry registry)
    {
        if (Owners.TryGetValue(registry, out var previous))
        {
            try
            {
                previous.DemandPrivateContextCurrent();
                throw new InvalidOperationException("The same registry still has a live private Task storage owner.");
            }
            catch (ObjectDisposedException) { Owners.Remove(registry); }
        }
        var actual = new BrowserPrivateTaskStorage(); // No external source or resource acquisition.
        var enrolled = registry.RegisterPrivateLifetime(actual);
        if (!enrolled.Succeeded) throw new InvalidOperationException(enrolled.Message);
        Owners.Add(registry, actual); // Registry owns revocation/drain before a later account factory can fail.
    }
}
