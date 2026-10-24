using Haven.Core;

namespace Haven.Application;

/// <summary>Optional capability on the SAME authenticated memory source. It adds no global
/// recall path. The issued selection retains its owner, exact records and source lifetime.</summary>
public interface IChatOriginalUcmlContextSource : IChatOriginalPersistentMemorySource
{
    Task<IChatOriginalUcmlContext?> SelectOriginalUcmlContextWithinSourceAsync(
        IChatOriginalPersistentMemoryInput input, Conversation conversation, string query,
        Action<Action> scope, Action<Task> retain, CancellationToken token);
}

public interface IChatOriginalUcmlContext
{
    string Context { get; }
    Task ValidateOriginalWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token);
}

/// <summary>A live restriction retained only in process, never a serialized credential or grant.
/// The maintained provider router calls it against its ACTUAL selected provider before dispatch.</summary>
public interface IOriginalPrivateContextDispatchGuard
{
    Task ValidateAsync(bool actualProviderIsLocal, CancellationToken token);
}

/// <summary>Maintained model clients that enforce OriginalPrivateContext before every provider
/// invocation, including fallback. Other model clients receive no UCML context.</summary>
public interface IOriginalPrivateContextModelClient : IOllamaClient { }

/// <summary>A maintained provider whose actual transport proves local recipient custody.
/// A configurable endpoint or IsLocal flag alone does not establish this capability.</summary>
public interface IOriginalPrivateContextModelProvider : IModelProvider { }

public static class OriginalPrivateContextDispatch
{
    public static bool RequiresGuard(GenerationOptions? options) =>
        options?.RequireOriginalPrivateContext == true || options?.OriginalPrivateContext is not null;

    /// <summary>Reject an unverified route before even its catalogue is contacted. The live
    /// source is still revalidated separately immediately before actual provider dispatch.</summary>
    public static void ValidateProviderRoute(GenerationOptions? options, IModelProvider? actualProvider)
    {
        OriginalSpacePolicyDispatch.ValidateRoute(options);
        if (!RequiresGuard(options)) return;
        if (options?.RequestedRoutingConstraints?.AllowCloud != false ||
            actualProvider is not IOriginalPrivateContextModelProvider { IsLocal: true } ||
            options.OriginalPrivateContext is null)
            throw new UnauthorizedAccessException("Private UCML context requires its live source guard and an actual local-only provider route.");
    }

    public static async Task ValidateProviderAsync(GenerationOptions? options, IModelProvider? actualProvider, CancellationToken token)
    {
        await ValidatePrivateContextAsync(options, actualProvider is IOriginalPrivateContextModelProvider { IsLocal: true }, token).ConfigureAwait(false);
        await OriginalSpacePolicyDispatch.ValidateAsync(options, actualProvider, token).ConfigureAwait(false);
    }

    public static async Task ValidateAsync(GenerationOptions? options, bool actualProviderIsLocal, CancellationToken token)
    {
        await ValidatePrivateContextAsync(options, actualProviderIsLocal, token).ConfigureAwait(false);
        await OriginalSpacePolicyDispatch.ValidateAsync(options, null, token).ConfigureAwait(false);
    }

    private static async Task ValidatePrivateContextAsync(GenerationOptions? options, bool actualProviderIsLocal, CancellationToken token)
    {
        if (!RequiresGuard(options)) return;
        if (options?.RequestedRoutingConstraints?.AllowCloud != false || !actualProviderIsLocal ||
            options.OriginalPrivateContext is not { } guard)
            throw new UnauthorizedAccessException("Private UCML context requires its live source guard and an actual local-only provider route.");
        await guard.ValidateAsync(actualProviderIsLocal, token).ConfigureAwait(false);
    }
}
