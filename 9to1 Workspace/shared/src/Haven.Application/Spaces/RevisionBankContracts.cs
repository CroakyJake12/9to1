namespace Haven.Application;

// Membership is metadata in the canonical Spaces registry. These records never contain
// artifact payloads and never authorize reading or editing an owning application's source.
public sealed record RevisionBankCategory(Guid CategoryId, string Name, int Position, bool IsBuiltIn);
public sealed record RevisionBankMember(Guid ResourceId, IReadOnlyList<Guid> CategoryIds,
    Guid? SubjectId, Guid? TopicId, DateTimeOffset AddedAt);
public sealed record RevisionBankMutationResult(Guid SpaceId, Guid OperationId, long SpaceRevision,
    Guid? ResourceId, Guid? CategoryId, bool Changed);
public sealed record RevisionBankOperationReceipt(Guid OperationId, string RequestSha256,
    RevisionBankMutationResult Result, DateTimeOffset CompletedAt);
public sealed record RevisionBankData(int SchemaVersion, IReadOnlyList<RevisionBankCategory> Categories,
    IReadOnlyList<RevisionBankMember> Members, IReadOnlyList<RevisionBankOperationReceipt> Operations,
    bool AutomaticallyClassifyTypes = false);
public sealed record RevisionBankSnapshot(Guid SpaceId, long SpaceRevision, RevisionBankData Data);

public enum RevisionBankMutationKind
{
    Add = 0, Remove = 1, Classify = 2, CreateCategory = 3,
    RenameCategory = 4, RemoveCategory = 5, SetAutomaticClassification = 6
}

public sealed record RevisionBankMutation(Guid SpaceId, long ExpectedRevision, Guid OperationId,
    RevisionBankMutationKind Kind, Guid? ResourceId = null, Guid? CategoryId = null,
    string? CategoryName = null, IReadOnlyList<Guid>? CategoryIds = null,
    Guid? SubjectId = null, Guid? TopicId = null, bool? AutomaticallyClassifyTypes = null);

public sealed class RevisionBankOperationConflictException(Guid operationId)
    : InvalidOperationException("This Revision Bank operation ID belongs to a different request.")
{
    public Guid OperationId { get; } = operationId;
}

public static class RevisionBankCategories
{
    public static Guid Recommended { get; } = Guid.Parse("b2000000-0000-0000-0000-000000000001");
    public static Guid All { get; } = Guid.Parse("b2000000-0000-0000-0000-000000000002");
    public static Guid Flashcards { get; } = Guid.Parse("b2000000-0000-0000-0000-000000000003");
    public static Guid Notes { get; } = Guid.Parse("b2000000-0000-0000-0000-000000000004");
    public static Guid Papers { get; } = Guid.Parse("b2000000-0000-0000-0000-000000000005");
    public static Guid Quizzes { get; } = Guid.Parse("b2000000-0000-0000-0000-000000000006");

    internal static RevisionBankCategory[] Defaults() =>
    [
        new(Recommended, "Recommended", 0, true), new(All, "All", 1, true),
        new(Flashcards, "Flashcards", 2, true), new(Notes, "Notes", 3, true),
        new(Papers, "Papers", 4, true), new(Quizzes, "Quizzes", 5, true)
    ];
}
