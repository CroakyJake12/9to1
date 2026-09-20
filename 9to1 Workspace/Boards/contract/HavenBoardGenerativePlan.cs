namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// A renderer-independent preview of a proposed generative UI mutation.
/// The plan contains typed Haven commands only; it performs no model, network, storage, or UI work.
/// </summary>
public sealed record HavenBoardGenerativePlan(
    Guid Id,
    long BaseVersion,
    IReadOnlyList<HavenBoardCommand> Commands,
    HavenBoardSnapshot Preview);

public static class HavenBoardGenerativePlanner
{
    public const int MaxCommandsPerPlan = 64;
    public const int MaxGeneratedTitleLength = 512;
    public const int MaxGeneratedCardIdLength = 128;

    public static HavenBoardGenerativePlan CreatePlan(
        HavenBoardSnapshot snapshot,
        IEnumerable<HavenBoardCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(commands);
        HavenBoardReducer.Validate(snapshot);

        var materialized = commands.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("A generative board plan must contain at least one command.", nameof(commands));
        if (materialized.Length > MaxCommandsPerPlan)
            throw new InvalidOperationException(
                $"A generative board plan may contain at most {MaxCommandsPerPlan} commands.");

        var preview = snapshot;
        foreach (var command in materialized)
        {
            ArgumentNullException.ThrowIfNull(command);
            ValidateGenerativeCommand(command);
            preview = HavenBoardReducer.Apply(preview, command);
        }

        // Never expose the mutable array retained by a prepared plan. The coordinator returns the
        // plan to untrusted provider/UI code for review, so the registered command sequence must not
        // be replaceable after that review has happened.
        IReadOnlyList<HavenBoardCommand> frozenCommands = Array.AsReadOnly(materialized);

        return new HavenBoardGenerativePlan(
            Guid.NewGuid(),
            snapshot.Version,
            frozenCommands,
            preview);
    }

    public static bool IsAllowed(HavenBoardCommand command) => command switch
    {
        CreateCardCommand => true,
        RenameCardCommand => true,
        RemoveCardCommand => true,
        CreateGroupCommand => true,
        RemoveGroupCommand => true,
        RenameGroupCommand => true,
        MoveGroupCommand => true,
        MoveCardCommand => true,
        SetCardParentCommand => true,
        SetFreeformCardFrameCommand => true,
        RemoveFreeformCardFrameCommand => true,
        _ => false
    };

    private static void ValidateGenerativeCommand(HavenBoardCommand command)
    {
        if (!IsAllowed(command))
        {
            throw new InvalidOperationException(
                $"Command '{command.GetType().Name}' is not allowed through the generative UI boundary.");
        }

        switch (command)
        {
            case CreateCardCommand create:
                ValidateGeneratedCardId(create.CardId);
                ValidateGeneratedTitle(create.Title, nameof(create.Title));
                break;
            case RenameCardCommand renameCard:
                ValidateGeneratedTitle(renameCard.Title, nameof(renameCard.Title));
                break;
            case CreateGroupCommand createGroup:
                ValidateGeneratedGroupId(createGroup.GroupId);
                ValidateGeneratedTitle(createGroup.Title, nameof(createGroup.Title));
                break;
            case RenameGroupCommand rename:
                ValidateGeneratedTitle(rename.Title, nameof(rename.Title));
                break;
        }
    }

    private static void ValidateGeneratedGroupId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxGeneratedCardIdLength)
            throw new InvalidOperationException(
                $"Generated group IDs must contain 1 to {MaxGeneratedCardIdLength} characters.");

        if (value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidOperationException(
                "Generated group IDs may contain only ASCII letters, digits, '-' and '_'.");
        }
    }

    private static void ValidateGeneratedCardId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxGeneratedCardIdLength)
            throw new InvalidOperationException(
                $"Generated card IDs must contain 1 to {MaxGeneratedCardIdLength} characters.");

        if (value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidOperationException(
                "Generated card IDs may contain only ASCII letters, digits, '-' and '_'.");
        }
    }

    private static void ValidateGeneratedTitle(string? value, string name)
    {
        if (value is not null && value.Length > MaxGeneratedTitleLength)
            throw new InvalidOperationException(
                $"Generated {name} exceeds the {MaxGeneratedTitleLength}-character limit.");
    }
}
