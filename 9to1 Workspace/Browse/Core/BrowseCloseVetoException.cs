namespace HavenOS.Apps.Browse;

/// <summary>An ordinary native-content close refusal. No original retirement has
/// been admitted, so the host remains usable and may attempt close again.</summary>
public sealed class BrowseCloseVetoException : InvalidOperationException
{
    private const string Generic = "The native app has unsaved or pending work. The host remains open.";

    /// <param name="detail">The refusal ledger's own named cause (which tab, which condition). It
    /// travels IN the message because the message is what the application log's retained-cause line
    /// and the shutdown recovery screen print: measured on 21 November, three deterministic vetoes
    /// of the owner's live copy were logged with only this generic sentence, while the real cause
    /// ("tab browse://home - the tab navigation generation moved...") lived solely in an
    /// in-memory ledger that was cleared before anyone could read it. This adds no close authority.</param>
    /// <param name="staleAcquisition">True only for an <c>acquire-not-current</c> veto: the tab's
    /// content moved (address/navigation generation) while its own preparation was in flight. That
    /// shape - a self-refreshing page such as the Browse home surface measured on 21 November
    /// stalling every close - is retryable, so the close paths retry acquisition a bounded number
    /// of times before reporting. Every other cause (a genuine one-shot refusal, a leaked
    /// preparation, an abort fault) is reported unchanged.</param>
    internal BrowseCloseVetoException(string? detail = null, bool staleAcquisition = false)
        : base(detail is { Length: > 0 } d ? Generic + " [" + d + "]" : Generic)
        => StaleAcquisition = staleAcquisition;

    /// <summary>Diagnostic-only marker read by the close paths' bounded settle-retry; it adds no
    /// close or retirement authority.</summary>
    internal bool StaleAcquisition { get; init; }
}
