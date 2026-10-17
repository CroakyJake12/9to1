namespace HavenOS.Apps.Browse;

/// <summary>An ordinary native-content close refusal. No original retirement has
/// been admitted, so the host remains usable and may attempt close again.</summary>
public sealed class BrowseCloseVetoException : InvalidOperationException
{
    internal BrowseCloseVetoException()
        : base("The native app has unsaved or pending work. The host remains open.") { }
}
