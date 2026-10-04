namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// Immutable structural mutation batch crossing a future collaboration/sync boundary.
/// Attachment blobs and attachment metadata are deliberately excluded from this first boundary and
/// require a separate explicit content capability before they can leave the device.
/// </summary>
public sealed record HavenBoardCollaborationBatch(
    Guid MutationId,
    string BoardId,
    long BaseVersion,
    string ActorId,
    IReadOnlyList<HavenBoardCommand> Commands)
{
    public const int MaxCommandsPerBatch = 256;
    public const int MaxActorIdLength = 128;

    public static HavenBoardCollaborationBatch Create(
        string boardId,
        long baseVersion,
        string actorId,
        IEnumerable<HavenBoardCommand> commands,
        Guid? mutationId = null)
    {
        ValidateSafeId(boardId, 128, nameof(boardId));
        ValidateSafeId(actorId, MaxActorIdLength, nameof(actorId));
        if (baseVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(baseVersion), baseVersion, "Base version must be positive.");
        ArgumentNullException.ThrowIfNull(commands);

        var materialized = commands.ToArray();
        if (materialized.Length == 0)
            throw new ArgumentException("A collaboration batch must contain at least one command.", nameof(commands));
        if (materialized.Length > MaxCommandsPerBatch)
            throw new InvalidOperationException(
                $"A collaboration batch may contain at most {MaxCommandsPerBatch} commands.");

        foreach (var command in materialized)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (!IsStructuralSyncCommand(command))
            {
                throw new InvalidOperationException(
                    $"Command '{command.GetType().Name}' is not allowed through the structural collaboration boundary.");
            }
        }

        return new HavenBoardCollaborationBatch(
            mutationId ?? Guid.NewGuid(),
            boardId,
            baseVersion,
            actorId,
            Array.AsReadOnly(materialized));
    }

    public static bool IsStructuralSyncCommand(HavenBoardCommand command) => command switch
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

    public static void ValidateSafeId(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException($"{parameterName} must contain 1 to {maxLength} safe characters.", parameterName);

        if (value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
            throw new ArgumentException(
                $"{parameterName} may contain only ASCII letters, digits, '-' and '_'.",
                parameterName);
    }
}

public sealed record HavenBoardPermissionDecision(bool Allowed, string Reason)
{
    public static HavenBoardPermissionDecision Allow(string reason = "Allowed") => new(true, reason);
    public static HavenBoardPermissionDecision Deny(string reason = "Denied") => new(false, reason);
}

public interface IHavenBoardPermissionProvider
{
    ValueTask<HavenBoardPermissionDecision> AuthorizeAsync(
        HavenBoardSnapshot snapshot,
        string actorId,
        HavenBoardCommand command,
        CancellationToken cancellationToken = default);
}

/// <summary>Fail-closed default for any inbound collaborator mutation.</summary>
public sealed class DenyAllHavenBoardPermissionProvider : IHavenBoardPermissionProvider
{
    public ValueTask<HavenBoardPermissionDecision> AuthorizeAsync(
        HavenBoardSnapshot snapshot,
        string actorId,
        HavenBoardCommand command,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(HavenBoardPermissionDecision.Deny("No collaboration permission provider is configured."));
}

/// <summary>Small first policy: only one explicitly configured actor may mutate the board.</summary>
public sealed class OwnerOnlyHavenBoardPermissionProvider : IHavenBoardPermissionProvider
{
    private readonly string _ownerActorId;

    public OwnerOnlyHavenBoardPermissionProvider(string ownerActorId)
    {
        HavenBoardCollaborationBatch.ValidateSafeId(
            ownerActorId,
            HavenBoardCollaborationBatch.MaxActorIdLength,
            nameof(ownerActorId));
        _ownerActorId = ownerActorId;
    }

    public ValueTask<HavenBoardPermissionDecision> AuthorizeAsync(
        HavenBoardSnapshot snapshot,
        string actorId,
        HavenBoardCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(string.Equals(actorId, _ownerActorId, StringComparison.Ordinal)
            ? HavenBoardPermissionDecision.Allow("Actor matches the configured board owner.")
            : HavenBoardPermissionDecision.Deny("Actor is not the configured board owner."));
    }
}

public enum HavenBoardSyncPublishStatus
{
    Disabled,
    Accepted,
    Rejected
}

public sealed record HavenBoardSyncPublishResult(HavenBoardSyncPublishStatus Status, string Message);

/// <summary>
/// Future transport seam only. Implementations live outside the board domain and require their own
/// user consent/network capability. The default implementation below performs no network IO.
/// </summary>
public interface IHavenBoardSyncAdapter
{
    string AdapterId { get; }
    bool IsEnabled { get; }

    Task<HavenBoardSyncPublishResult> PublishAsync(
        HavenBoardCollaborationBatch batch,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HavenBoardCollaborationBatch>> PullAsync(
        string boardId,
        long afterVersion,
        CancellationToken cancellationToken = default);
}

public sealed class DisabledHavenBoardSyncAdapter : IHavenBoardSyncAdapter
{
    public string AdapterId => "disabled";
    public bool IsEnabled => false;

    public Task<HavenBoardSyncPublishResult> PublishAsync(
        HavenBoardCollaborationBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new HavenBoardSyncPublishResult(
            HavenBoardSyncPublishStatus.Disabled,
            "Board sync is disabled; no data left the device."));
    }

    public Task<IReadOnlyList<HavenBoardCollaborationBatch>> PullAsync(
        string boardId,
        long afterVersion,
        CancellationToken cancellationToken = default)
    {
        HavenBoardCollaborationBatch.ValidateSafeId(boardId, 128, nameof(boardId));
        if (afterVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(afterVersion));
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<HavenBoardCollaborationBatch>>([]);
    }
}
