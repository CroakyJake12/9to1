using Haven.Application;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>Narrow, typed classification of one terminal model-provider discovery
/// settlement. Only a settlement whose EVERY direct cause is a demonstrably expected
/// provider connectivity/status failure (see
/// <see cref="ModelProviderDiscoveryAvailability"/>) becomes a degraded availability
/// observation. Any unexpected, cancellation or cleanup sibling keeps the whole
/// settlement blocking with the COMPLETE set of exact direct causes, so a mixed
/// settlement can never erase a sibling behind a single expected transport cause.</summary>
internal static class ChatModelDiscoveryPolicy
{
    /// <param name="Degraded">True when the settlement is a non-blocking degraded
    /// availability observation; <paramref name="Cause"/> carries its exact cause.</param>
    /// <param name="Blocking">True when the settlement must keep refusal/retirement
    /// semantics; <paramref name="Causes"/> carries EVERY direct cause to retain.</param>
    internal readonly record struct Decision(
        bool Degraded, bool Blocking, Exception? Cause, IReadOnlyList<Exception> Causes);

    internal static Decision Classify(IReadOnlyList<Exception> terminalCauses)
    {
        ArgumentNullException.ThrowIfNull(terminalCauses);
        if (terminalCauses.Count == 0)
            throw new InvalidOperationException(
                "A failed discovery settlement must expose at least one direct cause.");

        foreach (var cause in terminalCauses)
            if (!ModelProviderDiscoveryAvailability.IsExpectedUnavailable(cause))
                return new Decision(Degraded: false, Blocking: true, Cause: null, Causes: terminalCauses.ToArray());

        // Every direct cause is an expected provider condition: an honest terminal
        // degraded observation. A single cause keeps its identity; several stay exact
        // inside one aggregate so nothing is dropped.
        var availabilityCause = terminalCauses.Count == 1
            ? terminalCauses[0]
            : new AggregateException("The configured model provider is unavailable.", terminalCauses);
        return new Decision(Degraded: true, Blocking: false, Cause: availabilityCause, Causes: []);
    }
}
