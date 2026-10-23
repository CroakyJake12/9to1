using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web;

/// <summary>Shell navigation only; every editor operation stays with the original owner and lifetime.</summary>
internal sealed class BrowserShellActions(ICuiActionDispatcher owner, Action goHome) : ICuiLifetimeAwareActionDispatcher, ICuiResultReportingDispatcher, ICuiActionAvailability
{
    public bool? IsActionAvailable(string command) => command == "ShellHome" || (owner as ICuiActionAvailability)?.IsActionAvailable(command) == true;
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command == "ShellHome") { goHome(); return ValueTask.CompletedTask; }
        return owner.DispatchAsync(command, parameter, cancellationToken);
    }
    public ValueTask<CuiCommandOutcome> DispatchWithOutcomeAsync(string command, object? parameter,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command == "ShellHome") { goHome(); return new(new CuiCommandOutcome(true, false, "Succeeded", null, null)); }
        // Preserve the exact original typed operation/value. An async forwarding adapter
        // could turn a faulted Task carrying OCE into a canceled replacement Task.
        if (owner is ICuiResultReportingDispatcher reporting)
            return reporting.DispatchWithOutcomeAsync(command, parameter, cancellationToken);
        // CUI's reporting entry supplies its VIEW token. Legacy lifetime-aware owners
        // must still distinguish it from explicit caller cancellation after admission.
        return ObserveImplicitOutcomeAsync(DispatchWithLifetimeAsync(command, parameter, new(cancellationToken, CancellationToken.None)));
    }
    private static async ValueTask<CuiCommandOutcome> ObserveImplicitOutcomeAsync(ValueTask operation)
    {
        var actual = operation.AsTask();
        try { await actual.ConfigureAwait(false); }
        catch (Exception) when (actual.IsFaulted) { throw new AggregateException(actual.Exception!.InnerExceptions); }
        return new(true, false, "Succeeded", null, null); // Same prior CUI fallback for an owner without typed results.
    }

    public async ValueTask DispatchWithLifetimeAsync(string command, object? parameter, CuiActionDispatchLifetime lifetime)
    {
        if (command == "ShellHome")
        {
            lifetime.ViewCancellation.ThrowIfCancellationRequested();
            await DispatchAsync(command, parameter, lifetime.CallerCancellation);
            return;
        }
        if (owner is ICuiLifetimeAwareActionDispatcher tracked)
        {
            await tracked.DispatchWithLifetimeAsync(command, parameter, lifetime);
            return;
        }

        // Older owners receive one token. Keep both cancellation sources connected until
        // the original operation settles, just as direct CUI dispatch did before this wrapper.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.CallerCancellation, lifetime.ViewCancellation);
        await owner.DispatchAsync(command, parameter, linked.Token);
    }
}
