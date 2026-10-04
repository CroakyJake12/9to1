namespace HavenOS.Home.PermissionsTrustNotifications;

/// <summary>Stable Home feature-state failure for UI/API adapters to report without exposing storage internals.</summary>
public sealed class HomeFeatureStoreException(string code, string message, Exception? innerException = null)
    : IOException(message, innerException)
{
    public string Code { get; } = code;
}
