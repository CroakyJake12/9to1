using System.Text.Json.Serialization;

namespace CakeOS.Apps.Boards.Contract;

public sealed record HavenBoardSnapshot(
    string Id,
    string Title,
    long Version,
    IReadOnlyList<HavenBoardGroup> Groups,
    HavenBoardFreeformLayout? Freeform = null)
{
    public static HavenBoardSnapshot CreateDefault() => new(
        Id: "board-main",
        Title: "Haven Boards",
        Version: 1,
        Groups:
        [
            new HavenBoardGroup("todo", "To do", [new HavenBoardCard("card-1", "First task")]),
            new HavenBoardGroup("doing", "Doing", [new HavenBoardCard("card-2", "Try AppFlowy Board")]),
            new HavenBoardGroup("done", "Done", [new HavenBoardCard("card-3", "Persist locally")])
        ]);
}

public sealed record HavenBoardGroup(
    string Id,
    string Title,
    IReadOnlyList<HavenBoardCard> Cards);

public sealed record HavenBoardCard(
    string Id,
    string Title,
    string? ParentCardId = null,
    IReadOnlyList<HavenBoardAttachment>? Attachments = null);

public sealed record HavenBoardAttachment(
    string Id,
    string DisplayName,
    string LocalReference,
    HavenBoardAttachmentAvailability Availability = HavenBoardAttachmentAvailability.Available);

public sealed record HavenBoardFreeformLayout(IReadOnlyList<HavenBoardFreeformItem> Items);

public sealed record HavenBoardFreeformItem(
    string CardId,
    double X,
    double Y,
    double Width = 280,
    double Height = 160,
    int ZIndex = 0);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HavenBoardAttachmentAvailability
{
    Available,
    Missing,
    RemoteOnly
}

public abstract record HavenBoardCommand;

public sealed record CreateCardCommand(string GroupId, string CardId, string Title) : HavenBoardCommand;
public sealed record RenameCardCommand(string CardId, string Title) : HavenBoardCommand;
public sealed record RemoveCardCommand(string CardId) : HavenBoardCommand;
public sealed record CreateGroupCommand(string GroupId, string Title, int? ToIndex = null) : HavenBoardCommand;
public sealed record RemoveGroupCommand(string GroupId) : HavenBoardCommand;
public sealed record RenameGroupCommand(string GroupId, string Title) : HavenBoardCommand;
public sealed record MoveGroupCommand(int FromIndex, int ToIndex) : HavenBoardCommand;
public sealed record MoveCardCommand(string FromGroupId, int FromIndex, string ToGroupId, int ToIndex) : HavenBoardCommand;
public sealed record SetCardParentCommand(string CardId, string? ParentCardId) : HavenBoardCommand;
public sealed record SetFreeformCardFrameCommand(
    string CardId,
    double X,
    double Y,
    double Width = 280,
    double Height = 160,
    int ZIndex = 0) : HavenBoardCommand;
public sealed record RemoveFreeformCardFrameCommand(string CardId) : HavenBoardCommand;
public sealed record AddAttachmentCommand(string CardId, HavenBoardAttachment Attachment) : HavenBoardCommand;
public sealed record RemoveAttachmentCommand(string CardId, string AttachmentId) : HavenBoardCommand;

public static class HavenBoardReducer
{
    public const double FreeformCoordinateLimit = 1_000_000;
    public const double FreeformMinWidth = 120;
    public const double FreeformMinHeight = 80;
    public const double FreeformMaxDimension = 4_000;
    public const int FreeformZIndexLimit = 1_000_000;

    public static HavenBoardSnapshot Apply(HavenBoardSnapshot snapshot, HavenBoardCommand command)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command);
        Validate(snapshot);

        var groups = snapshot.Groups
            .Select(group => new MutableGroup(group.Id, group.Title, group.Cards.ToList()))
            .ToList();
        var freeformItems = (snapshot.Freeform?.Items ?? []).ToList();

        switch (command)
        {
            case CreateCardCommand create:
            {
                var target = FindGroup(groups, create.GroupId);
                ValidateNewCardId(groups, create.CardId);
                target.Cards.Add(new HavenBoardCard(create.CardId, NormaliseTitle(create.Title, "Untitled card")));
                break;
            }
            case RenameCardCommand renameCard:
            {
                var located = FindCard(groups, renameCard.CardId);
                located.Group.Cards[located.Index] = located.Card with
                {
                    Title = NormaliseTitle(renameCard.Title, "Untitled card")
                };
                break;
            }
            case RemoveCardCommand removeCard:
            {
                var located = FindCard(groups, removeCard.CardId);
                located.Group.Cards.RemoveAt(located.Index);
                freeformItems.RemoveAll(item =>
                    string.Equals(item.CardId, removeCard.CardId, StringComparison.Ordinal));
                OrphanChildren(groups, removeCard.CardId);
                break;
            }
            case CreateGroupCommand createGroup:
            {
                ValidateNewGroupId(groups, createGroup.GroupId);
                var insertAt = createGroup.ToIndex ?? groups.Count;
                insertAt = Math.Clamp(insertAt, 0, groups.Count);
                groups.Insert(insertAt, new MutableGroup(
                    createGroup.GroupId,
                    NormaliseTitle(createGroup.Title, "Untitled group"),
                    []));
                break;
            }
            case RemoveGroupCommand removeGroup:
            {
                var index = groups.FindIndex(group =>
                    string.Equals(group.Id, removeGroup.GroupId, StringComparison.Ordinal));
                if (index < 0)
                    throw new InvalidOperationException($"Board group '{removeGroup.GroupId}' does not exist.");

                var removedCardIds = groups[index].Cards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
                groups.RemoveAt(index);
                freeformItems.RemoveAll(item => removedCardIds.Contains(item.CardId));
                foreach (var removedCardId in removedCardIds)
                    OrphanChildren(groups, removedCardId);
                break;
            }
            case RenameGroupCommand rename:
            {
                var target = FindGroup(groups, rename.GroupId);
                target.Title = NormaliseTitle(rename.Title, "Untitled group");
                break;
            }
            case MoveGroupCommand moveGroup:
            {
                RequireIndex(moveGroup.FromIndex, groups.Count, nameof(moveGroup.FromIndex));
                RequireInsertIndex(moveGroup.ToIndex, groups.Count, nameof(moveGroup.ToIndex));
                if (moveGroup.FromIndex != moveGroup.ToIndex)
                {
                    var moved = groups[moveGroup.FromIndex];
                    groups.RemoveAt(moveGroup.FromIndex);
                    groups.Insert(Math.Clamp(moveGroup.ToIndex, 0, groups.Count), moved);
                }
                break;
            }
            case MoveCardCommand moveCard:
            {
                var source = FindGroup(groups, moveCard.FromGroupId);
                var target = FindGroup(groups, moveCard.ToGroupId);
                RequireIndex(moveCard.FromIndex, source.Cards.Count, nameof(moveCard.FromIndex));

                var card = source.Cards[moveCard.FromIndex];
                source.Cards.RemoveAt(moveCard.FromIndex);
                target.Cards.Insert(Math.Clamp(moveCard.ToIndex, 0, target.Cards.Count), card);
                break;
            }
            case SetCardParentCommand setParent:
            {
                var located = FindCard(groups, setParent.CardId);
                if (setParent.ParentCardId is not null)
                    EnsureValidParentAssignment(groups, setParent.CardId, setParent.ParentCardId);

                located.Group.Cards[located.Index] = located.Card with { ParentCardId = setParent.ParentCardId };
                break;
            }
            case SetFreeformCardFrameCommand setFrame:
            {
                _ = FindCard(groups, setFrame.CardId);
                var item = new HavenBoardFreeformItem(
                    setFrame.CardId,
                    setFrame.X,
                    setFrame.Y,
                    setFrame.Width,
                    setFrame.Height,
                    setFrame.ZIndex);
                ValidateFreeformItem(item);

                var existingIndex = freeformItems.FindIndex(existing =>
                    string.Equals(existing.CardId, setFrame.CardId, StringComparison.Ordinal));
                if (existingIndex >= 0)
                    freeformItems[existingIndex] = item;
                else
                    freeformItems.Add(item);
                break;
            }
            case RemoveFreeformCardFrameCommand removeFrame:
            {
                _ = FindCard(groups, removeFrame.CardId);
                var removed = freeformItems.RemoveAll(item =>
                    string.Equals(item.CardId, removeFrame.CardId, StringComparison.Ordinal));
                if (removed == 0)
                    throw new InvalidOperationException(
                        $"Card '{removeFrame.CardId}' does not have a freeform frame.");
                break;
            }
            case AddAttachmentCommand addAttachment:
            {
                ArgumentNullException.ThrowIfNull(addAttachment.Attachment);
                var located = FindCard(groups, addAttachment.CardId);
                var attachments = (located.Card.Attachments ?? []).ToList();
                if (attachments.Any(attachment => string.Equals(
                        attachment.Id,
                        addAttachment.Attachment.Id,
                        StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        $"Attachment '{addAttachment.Attachment.Id}' already exists on card '{addAttachment.CardId}'.");

                attachments.Add(addAttachment.Attachment);
                located.Group.Cards[located.Index] = located.Card with { Attachments = attachments.ToArray() };
                break;
            }
            case RemoveAttachmentCommand removeAttachment:
            {
                var located = FindCard(groups, removeAttachment.CardId);
                var attachments = (located.Card.Attachments ?? []).ToList();
                var removed = attachments.RemoveAll(attachment => string.Equals(
                    attachment.Id,
                    removeAttachment.AttachmentId,
                    StringComparison.Ordinal));
                if (removed == 0)
                    throw new InvalidOperationException(
                        $"Attachment '{removeAttachment.AttachmentId}' does not exist on card '{removeAttachment.CardId}'.");

                located.Group.Cards[located.Index] = located.Card with { Attachments = attachments.ToArray() };
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported board command '{command.GetType().Name}'.");
        }

        var updated = snapshot with
        {
            Version = checked(snapshot.Version + 1),
            Groups = groups.Select(group => new HavenBoardGroup(group.Id, group.Title, group.Cards.ToArray())).ToArray(),
            Freeform = snapshot.Freeform is null && freeformItems.Count == 0
                ? null
                : new HavenBoardFreeformLayout(freeformItems.ToArray())
        };
        Validate(updated);
        return updated;
    }

    public static void Validate(HavenBoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var cardsById = new Dictionary<string, HavenBoardCard>(StringComparer.Ordinal);
        foreach (var card in snapshot.Groups.SelectMany(group => group.Cards))
        {
            if (string.IsNullOrWhiteSpace(card.Id))
                throw new InvalidOperationException("Board cards must have non-empty IDs.");
            if (!cardsById.TryAdd(card.Id, card))
                throw new InvalidOperationException($"Board card ID '{card.Id}' is duplicated.");
        }

        foreach (var card in cardsById.Values)
        {
            if (card.ParentCardId is null)
                continue;
            ValidateParentChain(cardsById, card.Id, card.ParentCardId);
        }

        if (snapshot.Freeform is not null)
        {
            var positionedCards = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in snapshot.Freeform.Items)
            {
                ValidateFreeformItem(item);
                if (!cardsById.ContainsKey(item.CardId))
                    throw new InvalidOperationException(
                        $"Freeform layout references missing card '{item.CardId}'.");
                if (!positionedCards.Add(item.CardId))
                    throw new InvalidOperationException(
                        $"Freeform layout contains duplicate frame for card '{item.CardId}'.");
            }
        }
    }

    private static MutableGroup FindGroup(IReadOnlyList<MutableGroup> groups, string id) =>
        groups.FirstOrDefault(group => string.Equals(group.Id, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Board group '{id}' does not exist.");

    private static void ValidateNewCardId(IReadOnlyList<MutableGroup> groups, string cardId)
    {
        ValidateStableId(cardId, "Card", 128);
        if (groups.SelectMany(group => group.Cards).Any(card => string.Equals(card.Id, cardId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Card '{cardId}' already exists.");
    }

    private static void ValidateNewGroupId(IReadOnlyList<MutableGroup> groups, string groupId)
    {
        ValidateStableId(groupId, "Group", 128);
        if (groups.Any(group => string.Equals(group.Id, groupId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Board group '{groupId}' already exists.");
    }

    /// <summary>
    /// Donor group/card IDs are opaque stable strings (the donor example even uses
    /// display-style IDs such as "To Do"), so the neutral contract requires only
    /// non-empty, bounded IDs. Stricter charsets live at narrower boundaries
    /// (generative plans, board storage paths).
    /// </summary>
    private static void ValidateStableId(string value, string label, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new InvalidOperationException($"{label} ID must contain 1 to {maxLength} characters.");
    }

    private static void OrphanChildren(IReadOnlyList<MutableGroup> groups, string removedCardId)
    {
        foreach (var group in groups)
        {
            for (var index = 0; index < group.Cards.Count; index++)
            {
                if (string.Equals(group.Cards[index].ParentCardId, removedCardId, StringComparison.Ordinal))
                    group.Cards[index] = group.Cards[index] with { ParentCardId = null };
            }
        }
    }

    private static LocatedCard FindCard(IReadOnlyList<MutableGroup> groups, string cardId)
    {
        foreach (var group in groups)
        {
            var index = group.Cards.FindIndex(card => string.Equals(card.Id, cardId, StringComparison.Ordinal));
            if (index >= 0)
                return new LocatedCard(group, index, group.Cards[index]);
        }

        throw new InvalidOperationException($"Board card '{cardId}' does not exist.");
    }

    private static void EnsureValidParentAssignment(
        IReadOnlyList<MutableGroup> groups,
        string cardId,
        string proposedParentId)
    {
        if (string.Equals(cardId, proposedParentId, StringComparison.Ordinal))
            throw new InvalidOperationException("A card cannot be its own parent.");

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? currentId = proposedParentId;
        while (currentId is not null)
        {
            if (string.Equals(currentId, cardId, StringComparison.Ordinal))
                throw new InvalidOperationException("Card hierarchy cannot contain cycles.");
            if (!visited.Add(currentId))
                throw new InvalidOperationException("The existing card hierarchy contains a cycle.");

            currentId = FindCard(groups, currentId).Card.ParentCardId;
        }
    }

    private static void ValidateParentChain(
        IReadOnlyDictionary<string, HavenBoardCard> cardsById,
        string cardId,
        string proposedParentId)
    {
        if (string.Equals(cardId, proposedParentId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Board card '{cardId}' cannot be its own parent.");

        var visited = new HashSet<string>(StringComparer.Ordinal);
        string? currentId = proposedParentId;
        while (currentId is not null)
        {
            if (string.Equals(currentId, cardId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Board card hierarchy contains a cycle involving '{cardId}'.");
            if (!visited.Add(currentId))
                throw new InvalidOperationException("Board card hierarchy contains a cycle.");
            if (!cardsById.TryGetValue(currentId, out var current))
                throw new InvalidOperationException(
                    $"Board card '{cardId}' references missing parent '{currentId}'.");

            currentId = current.ParentCardId;
        }
    }

    private static void ValidateFreeformItem(HavenBoardFreeformItem item)
    {
        if (string.IsNullOrWhiteSpace(item.CardId))
            throw new InvalidOperationException("Freeform frames must reference a card ID.");
        if (!double.IsFinite(item.X) || !double.IsFinite(item.Y) ||
            Math.Abs(item.X) > FreeformCoordinateLimit || Math.Abs(item.Y) > FreeformCoordinateLimit)
            throw new InvalidOperationException("Freeform card coordinates are outside the supported finite range.");
        if (!double.IsFinite(item.Width) || !double.IsFinite(item.Height) ||
            item.Width < FreeformMinWidth || item.Height < FreeformMinHeight ||
            item.Width > FreeformMaxDimension || item.Height > FreeformMaxDimension)
            throw new InvalidOperationException("Freeform card dimensions are outside the supported range.");
        if (Math.Abs((long)item.ZIndex) > FreeformZIndexLimit)
            throw new InvalidOperationException("Freeform card z-index is outside the supported range.");
    }

    private static void RequireIndex(int index, int count, string name)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(name, index, $"Expected an index from 0 to {Math.Max(0, count - 1)}.");
    }

    private static void RequireInsertIndex(int index, int count, string name)
    {
        if (index < 0 || index > count)
            throw new ArgumentOutOfRangeException(name, index, $"Expected an insertion index from 0 to {count}.");
    }

    private static string NormaliseTitle(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed record LocatedCard(MutableGroup Group, int Index, HavenBoardCard Card);

    private sealed class MutableGroup(string id, string title, List<HavenBoardCard> cards)
    {
        public string Id { get; } = id;
        public string Title { get; set; } = title;
        public List<HavenBoardCard> Cards { get; } = cards;
    }
}
