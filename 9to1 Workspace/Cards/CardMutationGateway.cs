using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Apps.Cards;

/// <summary>A mutation request from an app or automation caller. Actor identity is deliberately
/// absent: the host must resolve the current authenticated principal independently.</summary>
public abstract record CardMutation(string OperationId, Guid SetId, long ExpectedSetRevision,
    CardInteractionMode Mode)
{
    public sealed record Rename(string OperationId, Guid SetId, long ExpectedSetRevision,
        CardInteractionMode Mode, string Title)
        : CardMutation(OperationId, SetId, ExpectedSetRevision, Mode);

    public sealed record BulkEdit(string OperationId, Guid SetId, long ExpectedSetRevision,
        CardInteractionMode Mode, IReadOnlyList<CardSideEdit> Edits)
        : CardMutation(OperationId, SetId, ExpectedSetRevision, Mode);

    public sealed record AddCards(string OperationId, Guid SetId, long ExpectedSetRevision,
        CardInteractionMode Mode, IReadOnlyList<CardDraft> Drafts)
        : CardMutation(OperationId, SetId, ExpectedSetRevision, Mode);

    public sealed record Duplicate(string OperationId, Guid SetId, long ExpectedSetRevision,
        CardInteractionMode Mode, Guid CardId)
        : CardMutation(OperationId, SetId, ExpectedSetRevision, Mode);

    public sealed record Move(string OperationId, Guid SetId, long ExpectedSetRevision,
        CardInteractionMode Mode, Guid CardId, int VisibleDestination)
        : CardMutation(OperationId, SetId, ExpectedSetRevision, Mode);

    public sealed record Delete(string OperationId, Guid SetId, long ExpectedSetRevision,
        CardInteractionMode Mode, IReadOnlyList<Guid> CardIds)
        : CardMutation(OperationId, SetId, ExpectedSetRevision, Mode);

    public sealed record Restore(string OperationId, Guid SetId, long ExpectedSetRevision,
        CardInteractionMode Mode, Guid CardId)
        : CardMutation(OperationId, SetId, ExpectedSetRevision, Mode);
}

/// <summary>Provenance from a trusted Home/host identity service, NOT supplied by a card page.</summary>
public sealed record CardTrustedCaller(string PrincipalId, string SessionEvidenceId);

public sealed record CardAdmissionDecision(bool Allowed, string Code);

/// <summary>
/// Mandatory external authorisation. Production must use the canonical Home policy
/// with fresh caller, source, scope, permission and revision information. A refusal
/// must not be replaced with an optimistic local result.
/// </summary>
public interface ICardMutationAdmission
{
    ValueTask<CardAdmissionDecision> CheckAsync(CardTrustedCaller caller,
        CardMutation request, CardSet current, CancellationToken cancellationToken);
}

public interface ICardTrustedCallerSource
{
    ValueTask<CardTrustedCaller?> ResolveAsync(CancellationToken cancellationToken);
}

public enum CardCommitStatus { Committed, AlreadyCommitted, RevisionConflict, AdmissionRevoked, Unavailable }

/// <summary>Actual storage-owner acknowledgement, never inferred from an attempted write.</summary>
public sealed record CardCommitReceipt(CardCommitStatus Status, Guid SetId,
    string OperationId, long? CommittedRevision, string Code);

/// <summary>
/// Provider implementation must atomically bind (PrincipalId, OperationId)
/// to one request payload and one actual commit result, reject conflicting reuse,
/// compare expected/current revision, and recheck permission at the COMMIT boundary.
/// This interface does not implement a second persistence backend.
/// </summary>
public interface ICardCanonicalMutationStore
{
    Task<CardSet?> ReadForPrincipalAsync(CardTrustedCaller caller, Guid setId,
        CancellationToken cancellationToken);

    // The read is bound to the actual principal, operation ID and exact request
    // fingerprint, and must fail closed if an ID was reused for another payload.
    Task<CardCommitReceipt?> FindReceiptAsync(CardTrustedCaller caller, Guid setId,
        string operationId, string requestFingerprint, CancellationToken cancellationToken);

    Task<CardCommitReceipt> CommitAsync(CardTrustedCaller caller, CardMutation request,
        string requestFingerprint, CardSet successor,
        Func<CancellationToken, ValueTask<CardAdmissionDecision>> recheckAdmission,
        CancellationToken cancellationToken);
}

public sealed record CardMutationOutcome(bool Succeeded, string Code,
    Guid SetId, string OperationId, long? CommittedRevision, bool Replayed = false);

/// <summary>
/// Typed command entry shared by CUI and automation consumers. No direct disk access,
/// identity spoofing, local permission grants, or claimed persistence without a receipt.
/// </summary>
public sealed class CardMutationGateway(
    ICardCanonicalMutationStore store,
    ICardTrustedCallerSource callerSource,
    ICardMutationAdmission admission)
{
    public async Task<CardMutationOutcome> ExecuteAsync(CardMutation command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.SetId == Guid.Empty || string.IsNullOrWhiteSpace(command.OperationId)
            || command.OperationId.Length > 128)
            throw new CardOperationException(CardFailureCode.InvalidState,
                "A stable target and bounded operation ID are mandatory.");

        CardTrustedCaller caller = await callerSource.ResolveAsync(cancellationToken)
            ?? throw new CardOperationException(CardFailureCode.InvalidState,
                "An authenticated current caller is required.");
        if (string.IsNullOrWhiteSpace(caller.PrincipalId)
            || string.IsNullOrWhiteSpace(caller.SessionEvidenceId))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "The host returned incomplete current caller evidence.");

        // Load is a principal-authorised operation provided by the canonical owner.
        CardSet current = await store.ReadForPrincipalAsync(caller,
                command.SetId, cancellationToken)
            ?? throw new CardOperationException(CardFailureCode.CardNotFound,
                "The authorised set cannot be found.");
        CardSetOperations.Validate(current);
        if (current.SetId != command.SetId)
            throw new CardOperationException(CardFailureCode.InvalidState,
                "The store returned a different canonical set.");

        CardAdmissionDecision permit = await admission.CheckAsync(caller, command,
            current, cancellationToken);
        if (!permit.Allowed)
            return new(false, permit.Code, command.SetId, command.OperationId, null);

        // Only the canonical owner can recognise a previously acknowledged
        // mutation. A different payload with the same operation ID must refuse.
        string fingerprint = Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(command, command.GetType())));
        CardCommitReceipt? prior = await store.FindReceiptAsync(caller,
            command.SetId, command.OperationId, fingerprint, cancellationToken);
        if (prior is not null)
            return FromReceipt(command, prior, expectedReplay: true);

        // A replay is not presumed from the cached source. Only the owning
        // transaction service can recognise and return an acknowledged replay.
        CardSet successor = command switch
        {
            CardMutation.Rename rename => CardSetOperations.RenameSet(
                current, rename.Title, rename.ExpectedSetRevision, rename.Mode),
            CardMutation.BulkEdit edit => CardSetOperations.BulkEditSides(
                current, edit.Edits, edit.ExpectedSetRevision, edit.Mode),
            CardMutation.AddCards add => CardSetOperations.AddCards(current,
                add.Drafts.Select(draft => (draft.Front, draft.Back)),
                add.ExpectedSetRevision, add.Mode),
            CardMutation.Duplicate duplicate => CardSetOperations.Duplicate(current,
                duplicate.CardId, duplicate.ExpectedSetRevision, duplicate.Mode),
            CardMutation.Move move => CardSetOperations.MoveVisible(current,
                move.CardId, move.VisibleDestination, move.ExpectedSetRevision, move.Mode),
            CardMutation.Delete delete => DeleteCards(current, delete),
            CardMutation.Restore restore => CardSetOperations.Restore(
                current, restore.CardId, restore.ExpectedSetRevision, restore.Mode),
            _ => throw new CardOperationException(CardFailureCode.InvalidState,
                "Unsupported Cards command."),
        };
        // Require a commit-boundary recheck with the SAME resolved principal and
        // request, not a caller-provided privilege flag.
        CardCommitReceipt receipt = await store.CommitAsync(caller, command, fingerprint, successor,
            token => admission.CheckAsync(caller, command, current, token),
            cancellationToken);

        return FromReceipt(command, receipt, expectedReplay: false);
    }

    private static CardSet DeleteCards(CardSet set, CardMutation.Delete delete)
    {
        if (delete.ExpectedSetRevision != set.Revision)
            throw new CardOperationException(CardFailureCode.RevisionConflict,
                "Delete was requested against a stale set revision.");
        CardDeletePreview preview = CardSetOperations.PreviewDelete(set, delete.CardIds);
        return CardSetOperations.SoftDelete(set, preview, delete.Mode);
    }

    private static CardMutationOutcome FromReceipt(
        CardMutation command, CardCommitReceipt receipt, bool expectedReplay)
    {
        if (receipt.SetId != command.SetId || receipt.OperationId != command.OperationId
            || (receipt.Status is CardCommitStatus.Committed or CardCommitStatus.AlreadyCommitted
                && receipt.CommittedRevision is null))
            throw new CardOperationException(CardFailureCode.InvalidState,
                "The storage owner returned a mismatched or incomplete commit receipt.");
        if (expectedReplay && receipt.Status != CardCommitStatus.AlreadyCommitted)
            throw new CardOperationException(CardFailureCode.InvalidState,
                "A replay lookup must return an acknowledged prior operation, not a new commit.");

        return receipt.Status switch
        {
            CardCommitStatus.Committed => new(true, receipt.Code, command.SetId,
                command.OperationId, receipt.CommittedRevision),
            CardCommitStatus.AlreadyCommitted => new(true, receipt.Code, command.SetId,
                command.OperationId, receipt.CommittedRevision, true),
            _ => new(false, receipt.Code, command.SetId, command.OperationId,
                receipt.CommittedRevision),
        };
    }
}
