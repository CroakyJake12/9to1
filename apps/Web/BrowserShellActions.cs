using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web;

/// <summary>Shell navigation only; every editor operation stays with the original owner and lifetime.</summary>
internal sealed class BrowserShellActions(ICuiActionDispatcher owner, Action goHome) : ICuiLifetimeAwareActionDispatcher, ICuiActionAvailability
{
    public bool? IsActionAvailable(string command) => command == "ShellHome" || (owner as ICuiActionAvailability)?.IsActionAvailable(command) == true;
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command == "ShellHome") { goHome(); return ValueTask.CompletedTask; }
        return owner.DispatchAsync(command, parameter, cancellationToken);
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
