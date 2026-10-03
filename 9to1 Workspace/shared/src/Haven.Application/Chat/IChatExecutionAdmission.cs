using System.Collections.Frozen;
using Haven.Core;

namespace Haven.Application;

/// <summary>The registered Home issuer validates its privately retained original token. Public
/// objects, identifiers, capability strings and cancellation tokens never grant authority.</summary>
public interface IChatExecutionAdmission
{
    ValueTask DemandCurrentAsync(object originalStepAuthority, Guid conversationId,
        string modelIdentity, OllamaToolCall? originalCall, CancellationToken cancellationToken = default);

    /// <summary>Returns only the SAME issuer-held detached immutable call captured before
    /// owning policy/review. This default denies; public copies never supply dispatch authority.</summary>
    ValueTask<OllamaToolCall> GetOriginalDispatchCallAsync(object originalStepAuthority, Guid conversationId,
        string modelIdentity, OllamaToolCall originalCall, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<OllamaToolCall>(new UnauthorizedAccessException(
            "No original immutable Home Agent dispatch call is registered."));

    /// <summary>Settles only the SAME admitted callback and actual returned result or original
    /// failure. Missing owning receipt verification refuses continuation; result text is no receipt.
    /// Settlement is attempted independently after the admitted callback even if its execution
    /// lifetime retires; this method neither renews permission nor authorizes another call.</summary>
    ValueTask CompleteOriginalCallAsync(object originalStepAuthority, Guid conversationId,
        string modelIdentity, OllamaToolCall originalCall, OllamaToolCall originalDispatchCall, WorkspaceToolResult? originalResult,
        Exception? originalFailure, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new UnauthorizedAccessException(
            "No original owning tool outcome verifier is registered."));

    /// <summary>Returns only the private issuer-held commitment for the SAME admitted
    /// immutable dispatch. Matching DTOs/IDs and caller-defined callbacks never grant access.</summary>
    ValueTask<object> GetOriginalCommitAdmissionAsync(object originalStepAuthority, Guid conversationId,
        string modelIdentity, OllamaToolCall originalDispatchCall, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<object>(new UnauthorizedAccessException("No original owning commit admission is registered."));

    /// <summary>Fresh actual original actor/run/step/request checks at the owner mutation boundary.
    /// This must not reenter the owning resource scope resolver while its commit lease is held.</summary>
    ValueTask<AuthenticatedResourceActor> DemandOriginalCommitCurrentAsync(object originalCommitAdmission,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<AuthenticatedResourceActor>(new UnauthorizedAccessException("The original owning commit is unavailable."));

    /// <summary>Returns only the original issuer-owned lifetime after fresh reference/currentness
    /// checks. Consumers require a cancellable current lifetime and link it through actual I/O.</summary>
    ValueTask<CancellationToken> GetOriginalLifetimeAsync(object originalStepAuthority,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromException<CancellationToken>(new UnauthorizedAccessException(
            "No original Home Agent execution lifetime is registered."));
}

/// <summary>Consumer boundary only; this object issues no execution authority. It forwards the
/// same original opaque token to the registered issuer at every model/tool dispatch and publication.</summary>
public sealed class ChatOriginalExecutionBoundary : IDisposable
{
    private readonly IChatExecutionAdmission _issuer;
    private readonly object _authority;
    private readonly Guid _conversationId;
    private readonly CancellationTokenSource _linked;
    private ChatOriginalExecutionBoundary(IChatExecutionAdmission issuer, object authority,
        Guid conversationId, CancellationTokenSource linked)
    { _issuer = issuer; _authority = authority; _conversationId = conversationId; _linked = linked; }
    public CancellationToken Token => _linked.Token;

    public static async ValueTask<ChatOriginalExecutionBoundary> OpenAsync(IChatExecutionAdmission? issuer,
        object originalStepAuthority, Guid conversationId, string modelIdentity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalStepAuthority);
        if (issuer is null) throw new UnauthorizedAccessException("The original Home Agent execution issuer is unavailable.");
        await issuer.DemandCurrentAsync(originalStepAuthority, conversationId, modelIdentity, null, cancellationToken).ConfigureAwait(false);
        var lifetime = await issuer.GetOriginalLifetimeAsync(originalStepAuthority, cancellationToken).ConfigureAwait(false);
        if (!lifetime.CanBeCanceled || lifetime.IsCancellationRequested)
            throw new UnauthorizedAccessException("The original Home Agent execution lifetime is unavailable or retired.");
        var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime, cancellationToken);
        var boundary = new ChatOriginalExecutionBoundary(issuer, originalStepAuthority, conversationId, linked);
        try
        {
            await boundary.DemandAsync(modelIdentity, null, linked.Token).ConfigureAwait(false);
            return boundary;
        }
        catch { linked.Dispose(); throw; }
    }

    public async ValueTask DemandAsync(string modelIdentity, OllamaToolCall? originalCall,
        CancellationToken cancellationToken = default)
    {
        Token.ThrowIfCancellationRequested(); cancellationToken.ThrowIfCancellationRequested();
        using var linked = LinkOriginalLifetime(cancellationToken);
        await _issuer.DemandCurrentAsync(_authority, _conversationId, modelIdentity, originalCall, linked.Token).ConfigureAwait(false);
        Token.ThrowIfCancellationRequested(); cancellationToken.ThrowIfCancellationRequested();
    }
    public async ValueTask<OllamaToolCall> GetDispatchCallAsync(string modelIdentity, OllamaToolCall originalCall,
        CancellationToken cancellationToken = default)
    {
        using var linked = LinkOriginalLifetime(cancellationToken);
        var dispatch = await _issuer.GetOriginalDispatchCallAsync(_authority, _conversationId, modelIdentity,
            originalCall, linked.Token).ConfigureAwait(false);
        Token.ThrowIfCancellationRequested(); cancellationToken.ThrowIfCancellationRequested();
        if (ReferenceEquals(dispatch, originalCall) || dispatch.Arguments is not FrozenDictionary<string, System.Text.Json.JsonElement>)
            throw new UnauthorizedAccessException("The original immutable dispatch body is unavailable.");
        return dispatch;
    }
    public async ValueTask CompleteCallAsync(string modelIdentity, OllamaToolCall originalCall,
        OllamaToolCall originalDispatchCall,
        WorkspaceToolResult? originalResult, Exception? originalFailure, CancellationToken cancellationToken = default)
    {
        // The original callback has already settled. Forward that exact outcome even when
        // new-execution permission/lifetime is retired; the owning issuer verifies its receipt.
        await _issuer.CompleteOriginalCallAsync(_authority, _conversationId, modelIdentity, originalCall,
            originalDispatchCall, originalResult, originalFailure, cancellationToken).ConfigureAwait(false);
    }
    public CancellationTokenSource LinkOriginalLifetime(CancellationToken cancellationToken)
    {
        Token.ThrowIfCancellationRequested();
        return CancellationTokenSource.CreateLinkedTokenSource(Token, cancellationToken);
    }
    public void Dispose() => _linked.Dispose();
}

