using System.Runtime.Versioning;
using Haven.Application;
using Haven.Core;

namespace NineToOne.Web.Spaces.Storage;

/// <summary>Source-issued current-profile CONTEXT observation. The canonical Task admission owner
/// still performs fresh authority; this receipt never grants Run/provider/tool or installed Home access.</summary>
[SupportedOSPlatform("browser")]
public sealed class BrowserTaskContextIssuer(IndexedDbConversationRepository sameConversations)
{
    private readonly object _issuer = new();
    public bool IsIssuedOriginalContext(BrowserOriginalTaskContext context)
    {
        try { sameConversations.DemandPrivateContextCurrent(); }
        catch (ObjectDisposedException) { return false; }
        return context is not null && context.IsIssuedBy(_issuer);
    }
    public async Task<BrowserOriginalTaskContext> OpenExistingOriginalAsync(Guid actualContextId, CancellationToken token)
    {
        sameConversations.DemandPrivateContextCurrent();
        var actual = await sameConversations.ReadOriginalConversationAsync(actualContextId, token);
        sameConversations.DemandPrivateContextCurrent();
        if (actual.Conversation is not { Mode: HavenMode.Tasks, IsArchived: false, IsTemporary: false } conversation)
            throw new BrowserTaskContextUnavailableException("The current verified profile has no active canonical Tasks conversation with this ID.");
        return new(_issuer, conversation, actual.Actor);
    }
    /// <summary>Explicit new-context action only. No Task, ExecutionId or provider loop is created here.</summary>
    public async Task<BrowserOriginalTaskContext> CreateOriginalAsync(string title, CancellationToken token)
    {
        sameConversations.DemandPrivateContextCurrent();
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task, title, null, null,
            false, false, now, now);
        var actual = await sameConversations.InsertOriginalTaskConversationAsync(conversation, token);
        try { sameConversations.DemandPrivateContextCurrent(); }
        catch (Exception error)
        {
            // A genuine committed insertion remains inspectable. This detached observation
            // is NOT an issued current context and never supports automatic replacement/retry.
            throw new BrowserTaskContextCommitObservedAfterRetirementException(actual.Conversation, actual.Actor, error);
        }
        return new(_issuer, actual.Conversation, actual.Actor); // Only actual durable insert + current signed actor ACK.
    }
    public async Task<BrowserOriginalTaskContext> RevalidateOriginalAsync(BrowserOriginalTaskContext original, CancellationToken token)
    {
        if (!IsIssuedOriginalContext(original)) throw new BrowserTaskContextUnavailableException("This context was not issued by the same configured owner.");
        var current = await OpenExistingOriginalAsync(original.Conversation.Id, token);
        sameConversations.DemandPrivateContextCurrent();
        if (current.Actor != original.Actor || current.Conversation.SpaceId != original.Conversation.SpaceId)
            throw new BrowserTaskContextUnavailableException("The original signed activation or actual context membership changed; current Task authority must inspect it.");
        return current; // Same ContextId, never replacement work or an actor reauthentication grant.
    }
}

public sealed class BrowserOriginalTaskContext
{
    private readonly object _issuer;
    private readonly BrowserOriginalTaskContext _self;
    internal BrowserOriginalTaskContext(object issuer, Conversation conversation, AuthenticatedResourceActor actor)
    { _issuer = issuer; _self = this; Conversation = conversation; Actor = actor; }
    internal bool IsIssuedBy(object issuer) => ReferenceEquals(_issuer, issuer) && ReferenceEquals(_self, this);
    public Conversation Conversation { get; }
    public AuthenticatedResourceActor Actor { get; } // Detached observation only; no interface caller can mint this source receipt.
}

/// <summary>The actual context insert returned its validated durable acknowledgement before
/// retirement. Preserve SAME record/actor observation for inspection; no current issuer receipt,
/// permission, no-effect proof, replay or new Task/Run is supplied.</summary>
public sealed class BrowserTaskContextCommitObservedAfterRetirementException(
    Conversation originalConversation, AuthenticatedResourceActor originalActor, Exception cause)
    : IOException("The actual context insert was acknowledged, but its private context retired before publication; inspect this original record before any retry.", cause)
{
    public Conversation OriginalConversation { get; } = originalConversation;
    public AuthenticatedResourceActor OriginalActor { get; } = originalActor;
}
