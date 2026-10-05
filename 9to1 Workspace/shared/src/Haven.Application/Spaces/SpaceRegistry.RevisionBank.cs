using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Application;

public sealed partial class SpaceRegistry
{
    private const int MaximumRevisionBankMembers = 5000;
    private const int MaximumRevisionBankCategories = 128;
    private const int MaximumRevisionBankOperations = 1000;
    private const int MaximumRevisionBankBytes = 1024 * 1024;

    /// <summary>Detached existing-store metadata only; reading this snapshot grants no source access.</summary>
    public async Task<RevisionBankSnapshot?> ReadRevisionBankAsync(Guid spaceId, CancellationToken token = default)
    {
        if (spaceId == Guid.Empty) throw new ArgumentException("A canonical Space ID is required.", nameof(spaceId));
        var space = await ReadExistingAsync(spaceId, token).ConfigureAwait(false);
        return space is null ? null : new(space.Id, space.Revision,
            CloneRevisionBank(space.RevisionBank) ?? EmptyRevisionBank());
    }

    /// <summary>Changes membership/categories only. The trusted owning composition must separately
    /// resolve actual source permissions before opening an artifact; cached membership grants nothing.</summary>
    public Task<RevisionBankMutationResult> MutateRevisionBankAsync(RevisionBankMutation request,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireGuardedDeletion(); // SAME guarded Spaces store/authority requirement, not a new registry.
        var captured = CaptureRevisionBankRequest(request);
        var fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(captured)))).ToLowerInvariant();
        return MutateAsync(state =>
        {
            var existing = FindRequired(state.Spaces, captured.SpaceId);
            var bank = CloneRevisionBank(existing.RevisionBank) ?? EmptyRevisionBank();
            if (bank.Operations.SingleOrDefault(item => item.OperationId == captured.OperationId) is { } replay)
            {
                if (!StringComparer.Ordinal.Equals(replay.RequestSha256, fingerprint))
                    throw new RevisionBankOperationConflictException(captured.OperationId);
                if (replay.Result.SpaceId != captured.SpaceId ||
                    replay.Result.OperationId != captured.OperationId ||
                    replay.Result.SpaceRevision != checked(captured.ExpectedRevision + 1) ||
                    replay.Result.ResourceId != captured.ResourceId ||
                    replay.Result.CategoryId != captured.CategoryId)
                    throw new InvalidDataException("The saved Revision Bank receipt does not match its exact original request.");
                return (state, replay.Result);
            }
            if (existing.IsArchived) throw new InvalidOperationException("Restore this Space before editing its Revision Bank.");
            if (existing.Revision != captured.ExpectedRevision)
                throw new SpaceRevisionConflictException(existing.Id, captured.ExpectedRevision, existing.Revision);
            if (bank.Operations.Count >= MaximumRevisionBankOperations)
                throw new InvalidOperationException("Revision Bank replay capacity is full; retained operation receipts were preserved.");
            var now = _clock();
            if (now < existing.UpdatedAt) now = existing.UpdatedAt;
            var changed = ApplyRevisionBankMutation(existing, bank, captured, now);
            var didChange = JsonSerializer.Serialize(bank) != JsonSerializer.Serialize(changed);
            var nextRevision = checked(existing.Revision + 1);
            var result = new RevisionBankMutationResult(existing.Id, captured.OperationId, nextRevision,
                captured.ResourceId, captured.CategoryId, didChange);
            changed = changed with { Operations = [.. changed.Operations,
                new RevisionBankOperationReceipt(captured.OperationId, fingerprint, result, now)] };
            ValidateRevisionBank(changed, existing.ContextReferences!, existing.Id, nextRevision);
            var updated = existing with { Revision = nextRevision, UpdatedAt = now, RevisionBank = changed };
            ValidateDefinition(updated, state.Spaces.Where(item => item.Id != existing.Id).ToArray());
            return (state with { Spaces = state.Spaces.Select(item => item.Id == existing.Id ? updated : item).ToArray() }, result);
        }, token);
    }

    private static RevisionBankMutation CaptureRevisionBankRequest(RevisionBankMutation request)
    {
        if (request.SpaceId == Guid.Empty || request.OperationId == Guid.Empty || request.ExpectedRevision < 1 ||
            !Enum.IsDefined(request.Kind)) throw new ArgumentException("Exact Space, operation, kind and revision are required.");
        var categories = request.CategoryIds?.ToArray() ?? [];
        if (categories.Length > MaximumRevisionBankCategories || categories.Any(id => id == Guid.Empty) ||
            categories.Distinct().Count() != categories.Length)
            throw new ArgumentException("Category IDs must be bounded, nonempty and unique.");
        var name = request.CategoryName?.Trim();
        if (name is not null && (name.Length is < 1 or > 80))
            throw new ArgumentException("A category name must contain between 1 and 80 characters.");
        if (request.ResourceId == Guid.Empty || request.CategoryId == Guid.Empty ||
            request.SubjectId == Guid.Empty || request.TopicId == Guid.Empty ||
            request.TopicId.HasValue && !request.SubjectId.HasValue)
            throw new ArgumentException("Optional canonical IDs must be nonempty; a Topic requires its owning Subject.");
        if (request.SubjectId.HasValue || request.TopicId.HasValue)
            throw new NotSupportedException("Study topic association requires the canonical Subject/Lesson owner route.");
        var resourceAction = request.Kind is RevisionBankMutationKind.Add or RevisionBankMutationKind.Remove or RevisionBankMutationKind.Classify;
        var categoryAction = request.Kind is RevisionBankMutationKind.CreateCategory or RevisionBankMutationKind.RenameCategory or RevisionBankMutationKind.RemoveCategory;
        if (resourceAction != request.ResourceId.HasValue || categoryAction != request.CategoryId.HasValue ||
            (request.Kind is RevisionBankMutationKind.CreateCategory or RevisionBankMutationKind.RenameCategory) != (name is not null) ||
            request.Kind != RevisionBankMutationKind.Add && (request.SubjectId.HasValue || request.TopicId.HasValue) ||
            request.Kind is not (RevisionBankMutationKind.Add or RevisionBankMutationKind.Classify) && categories.Length != 0 ||
            (request.Kind == RevisionBankMutationKind.SetAutomaticClassification) != request.AutomaticallyClassifyTypes.HasValue)
            throw new ArgumentException("The request contains missing or inapplicable operation fields.");
        return request with { CategoryName = name, CategoryIds = categories.Order().ToArray() };
    }

    private static RevisionBankData ApplyRevisionBankMutation(SpaceDefinition space, RevisionBankData bank,
        RevisionBankMutation request, DateTimeOffset now)
    {
        var categories = bank.Categories.ToList();
        var members = bank.Members.ToList();
        var selectedCategories = request.CategoryIds ?? [];
        foreach (var id in selectedCategories)
            if (!categories.Any(item => item.CategoryId == id)) throw new KeyNotFoundException("The Revision Bank category is unavailable.");
        switch (request.Kind)
        {
            case RevisionBankMutationKind.Add:
                // SAME canonical ContextId; neither an arbitrary artifact ID nor another Space's reference is admitted.
                if (!space.ContextReferences!.Any(item => item.ContextId == request.ResourceId))
                    throw new KeyNotFoundException("The current Space source reference is unavailable.");
                if (members.Any(item => item.ResourceId == request.ResourceId))
                    throw new InvalidOperationException("This canonical source is already enrolled; classify its existing membership.");
                var enrolCategories = selectedCategories.ToArray();
                if (bank.AutomaticallyClassifyTypes && enrolCategories.Length == 0 &&
                    AutomaticRevisionBankCategory(space.ContextReferences!.Single(item => item.ContextId == request.ResourceId)) is { } inferred)
                    enrolCategories = [inferred];
                members.Add(new(request.ResourceId!.Value, enrolCategories, request.SubjectId, request.TopicId, now));
                break;
            case RevisionBankMutationKind.Remove:
                if (members.RemoveAll(item => item.ResourceId == request.ResourceId) != 1)
                    throw new KeyNotFoundException("The Revision Bank membership is unavailable.");
                break;
            case RevisionBankMutationKind.Classify:
                var member = members.SingleOrDefault(item => item.ResourceId == request.ResourceId)
                    ?? throw new KeyNotFoundException("The Revision Bank membership is unavailable.");
                members[members.IndexOf(member)] = member with { CategoryIds = selectedCategories.ToArray() };
                break;
            case RevisionBankMutationKind.CreateCategory:
                if (categories.Any(item => item.CategoryId == request.CategoryId ||
                    item.Name.Equals(request.CategoryName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The category ID or name already exists.");
                categories.Add(new(request.CategoryId!.Value, request.CategoryName!, categories.Count, false));
                break;
            case RevisionBankMutationKind.RenameCategory:
                var rename = categories.SingleOrDefault(item => item.CategoryId == request.CategoryId)
                    ?? throw new KeyNotFoundException("The Revision Bank category is unavailable.");
                if (rename.IsBuiltIn) throw new InvalidOperationException("Default Revision Bank categories are protected.");
                if (categories.Any(item => item.CategoryId != rename.CategoryId &&
                    item.Name.Equals(request.CategoryName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The category name already exists.");
                categories[categories.IndexOf(rename)] = rename with { Name = request.CategoryName! };
                break;
            case RevisionBankMutationKind.RemoveCategory:
                var remove = categories.SingleOrDefault(item => item.CategoryId == request.CategoryId)
                    ?? throw new KeyNotFoundException("The Revision Bank category is unavailable.");
                if (remove.IsBuiltIn) throw new InvalidOperationException("Default Revision Bank categories are protected.");
                categories.Remove(remove);
                categories = categories.Select((item, index) => item with { Position = index }).ToList();
                members = members.Select(item => item with { CategoryIds = item.CategoryIds.Where(id => id != remove.CategoryId).ToArray() }).ToList();
                break;
            case RevisionBankMutationKind.SetAutomaticClassification:
                var enabled = request.AutomaticallyClassifyTypes!.Value;
                if (enabled)
                    members = members.Select(item =>
                    {
                        if (item.CategoryIds.Count != 0) return item;
                        var reference = space.ContextReferences!.Single(source => source.ContextId == item.ResourceId);
                        return AutomaticRevisionBankCategory(reference) is { } inferred
                            ? item with { CategoryIds = new[] { inferred } } : item;
                    }).ToList();
                return bank with { AutomaticallyClassifyTypes = enabled, Members = members.ToArray() };
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
        return bank with { Categories = categories.ToArray(), Members = members.ToArray() };
    }

    // Classification is metadata only. Unrecognised owner/type pairs remain unclassified;
    // this does not pretend that Cards or Forms are installed or that their artifacts are accessible.
    private static Guid? AutomaticRevisionBankCategory(SpaceContextReference reference) =>
        (reference.OwnerAppId.ToLowerInvariant(), reference.Kind) switch
        {
            ("write", SpaceContextReferenceKind.WriteArtifact) => RevisionBankCategories.Notes,
            ("notes", SpaceContextReferenceKind.ConnectedEntity) => RevisionBankCategories.Notes,
            ("cards", SpaceContextReferenceKind.ConnectedEntity) => RevisionBankCategories.Flashcards,
            ("forms", SpaceContextReferenceKind.ConnectedEntity) => RevisionBankCategories.Quizzes,
            _ => null
        };

    private static RevisionBankData EmptyRevisionBank() => new(1, RevisionBankCategories.Defaults(), [], []);

    private static RevisionBankData? CloneRevisionBank(RevisionBankData? bank)
    {
        if (bank is null) return null;
        if (bank.Categories is null || bank.Members is null || bank.Operations is null ||
            bank.Members.Any(item => item is null || item.CategoryIds is null))
            throw new InvalidDataException("Revision Bank collections are missing.");
        return bank with { Categories = bank.Categories.ToArray(),
            Members = bank.Members.Select(item => item with { CategoryIds = item.CategoryIds.ToArray() }).ToArray(),
            Operations = bank.Operations.ToArray() };
    }

    private static void ValidateRevisionBank(RevisionBankData? bank, IReadOnlyList<SpaceContextReference> references,
        Guid spaceId, long spaceRevision)
    {
        if (bank is null) return;
        if (bank.SchemaVersion != 1) throw new NotSupportedException("Unsupported Revision Bank schema; saved metadata was preserved.");
        if (bank.Categories is null || bank.Members is null || bank.Operations is null ||
            bank.Categories.Count is < 6 or > MaximumRevisionBankCategories ||
            bank.Members.Count > MaximumRevisionBankMembers || bank.Operations.Count > MaximumRevisionBankOperations)
            throw new InvalidDataException("Revision Bank collections exceed supported bounds.");
        var categoryIds = new HashSet<Guid>(); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in bank.Categories)
            if (category is null || category.CategoryId == Guid.Empty || !categoryIds.Add(category.CategoryId) ||
                category.Name is null || category.Name.Trim() != category.Name || category.Name.Length is < 1 or > 80 ||
                !names.Add(category.Name) || category.Position < 0)
                throw new InvalidDataException("Revision Bank categories have invalid or duplicate identities.");
        if (!bank.Categories.OrderBy(item => item.Position).Select(item => item.Position).SequenceEqual(Enumerable.Range(0, bank.Categories.Count)))
            throw new InvalidDataException("Revision Bank category ordering is invalid.");
        foreach (var original in RevisionBankCategories.Defaults())
            if (bank.Categories.SingleOrDefault(item => item.CategoryId == original.CategoryId) != original)
                throw new InvalidDataException("Default Revision Bank categories must be preserved.");
        if (bank.Categories.Any(item => item.IsBuiltIn && !RevisionBankCategories.Defaults().Any(original => original.CategoryId == item.CategoryId)))
            throw new InvalidDataException("A custom category cannot claim protected default identity.");
        var memberIds = new HashSet<Guid>();
        foreach (var member in bank.Members)
            if (member is null || member.ResourceId == Guid.Empty || !memberIds.Add(member.ResourceId) ||
                !references.Any(item => item.ContextId == member.ResourceId) || member.CategoryIds is null ||
                member.CategoryIds.Count > MaximumRevisionBankCategories ||
                member.CategoryIds.Distinct().Count() != member.CategoryIds.Count || member.CategoryIds.Any(id => !categoryIds.Contains(id)) ||
                member.SubjectId == Guid.Empty || member.TopicId == Guid.Empty || member.TopicId.HasValue && !member.SubjectId.HasValue ||
                member.AddedAt == default)
                throw new InvalidDataException("Revision Bank membership must bind current canonical source references.");
        var operationIds = new HashSet<Guid>();
        foreach (var operation in bank.Operations)
            if (operation is null || operation.OperationId == Guid.Empty || !operationIds.Add(operation.OperationId) ||
                operation.RequestSha256 is null || operation.RequestSha256.Length != 64 ||
                operation.RequestSha256.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) ||
                operation.Result is null || operation.Result.OperationId != operation.OperationId ||
                operation.Result.SpaceId != spaceId || operation.Result.SpaceRevision < 1 ||
                operation.Result.SpaceRevision > spaceRevision ||
                operation.Result.ResourceId == Guid.Empty || operation.Result.CategoryId == Guid.Empty ||
                operation.CompletedAt == default)
                throw new InvalidDataException("Revision Bank operation receipts have invalid identities.");
        if (JsonSerializer.SerializeToUtf8Bytes(bank).Length > MaximumRevisionBankBytes)
            throw new InvalidDataException("Revision Bank metadata exceeds its complete serialized byte bound.");
    }
}
