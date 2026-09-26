namespace HavenOS.Apps.Stacks;

public enum StackDomainKind
{
    Main,
    Branch,
    Twig,
    Leaf,
}

public enum StackStorageMode
{
    Files,
    GitHub,
    FilesAndGitHub,
}

public enum StackAuthorityMode
{
    FilesPrimary,
    GitHubPrimary,
    Bidirectional,
}

public enum StackVisibility
{
    Private,
    Public,
}

public enum StackMutationKind
{
    Upsert,
    Delete,
    Rename,
}

public enum StackConflictResolutionAction
{
    KeepLocal,
    TakeIncoming,
    UseManualContent,
}

public enum StackConflictProposalState
{
    PendingReview,
    Rejected,
    AcceptedWithoutMerge,
    AcceptedAndMerged,
}

public enum StackFreezeState
{
    Active,
    Expired,
    Thawed,
}

public enum StackFailureCode
{
    ProjectNotFound,
    DomainNotFound,
    InvalidHierarchy,
    ReparentConflict,
    MaterialisationFailed,
    SourceCorrupt,
    ManagedMetadataMissing,
    SchemaVersionUnsupported,
    MigrationFailed,
    UpstreamConflict,
    ConflictUnresolved,
    RootLocked,
    RootClaimed,
    FreezeActive,
    ChangeRequestOutOfDate,
    RequiredCheckFailed,
    ApprovalRequired,
    SyncConflict,
    RemoteUnavailable,
    MirrorFailed,
    PrivateProjectionViolation,
    LfsObjectMissing,
    RestoreIncomplete,
    PurgeBlocked,
    RevisionConflict,
    PermissionDenied,
    CapabilityUnavailable,
    InvalidPath,
    DuplicateIdentity,
    ManagedRefProtected,
    RecoveryStateUncertain,
}

public enum StackCapability
{
    ViewSource,
    Contribute,
    CreateDomain,
    ManageRoots,
    ManageFreezes,
    ManageRemotes,
    AdvancedGitReconciliation,
    Prune,
    Purge,
}

public sealed record StackActor(string ActorId, IReadOnlySet<StackCapability> Capabilities, bool TwoFactorVerified = false)
{
    public static StackActor System { get; } = new("system", Enum.GetValues<StackCapability>().ToHashSet(), true);

    public bool Can(StackCapability capability) => Capabilities.Contains(capability);
}

public sealed record StackProjectConfiguration(
    string Name,
    StackStorageMode StorageMode,
    string FilesRoot,
    StackAuthorityMode AuthorityMode = StackAuthorityMode.FilesPrimary,
    bool RequireTwoFactorForPrune = true,
    bool RequireTwoFactorForPurge = true,
    TimeSpan? DeletedRetention = null)
{
    public StackProjectConfiguration Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(FilesRoot);
        if (!Path.IsPathRooted(FilesRoot))
        {
            throw new ArgumentException("The Files root must be an absolute path.", nameof(FilesRoot));
        }

        if (Name is "." or ".." || Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The project name cannot be used as a directory name.", nameof(Name));
        }

        if (StorageMode == StackStorageMode.Files && AuthorityMode != StackAuthorityMode.FilesPrimary)
        {
            throw new ArgumentException("Files-only projects must use Files as the source authority.", nameof(AuthorityMode));
        }

        if (StorageMode == StackStorageMode.GitHub && AuthorityMode == StackAuthorityMode.FilesPrimary)
        {
            throw new ArgumentException("GitHub-only projects must use GitHub as the source authority.", nameof(AuthorityMode));
        }

        if (DeletedRetention < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DeletedRetention));
        }

        return this;
    }
}

public sealed record StackResource(byte[] Content, StackVisibility Visibility = StackVisibility.Private, bool IsBinary = false)
{
    public StackResource Copy() => this with { Content = Content.ToArray() };
}

public sealed record StackMutation(
    StackMutationKind Kind,
    string Path,
    StackResource? Resource = null,
    string? RenameTo = null);

public sealed record StackRevision(
    Guid Id,
    long Sequence,
    Guid DomainId,
    Guid? ParentRevisionId,
    DateTimeOffset CreatedAt,
    string ActorId,
    string Message,
    IReadOnlyList<string> ChangedPaths,
    string? GitCommitId = null);

public sealed record StackConflict(
    Guid Id,
    Guid ProjectId,
    Guid DomainId,
    string Path,
    StackResource? Base,
    StackResource? Local,
    StackResource? Incoming,
    Guid BaseRevisionId,
    Guid IncomingRevisionId,
    DateTimeOffset CreatedAt,
    bool IsResolved = false,
    bool MergeCompleted = false);

public sealed record StackConflictProposal(
    Guid Id,
    Guid ConflictId,
    string Summary,
    StackResource? ProposedResource,
    StackConflictProposalState State,
    DateTimeOffset CreatedAt,
    string ProposedBy);

public sealed record StackRoot(
    Guid Id,
    Guid OwnerDomainId,
    IReadOnlyList<string> Paths,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    bool IsActive = true);

public sealed record StackSubroot(
    Guid Id,
    Guid OwnerDomainId,
    IReadOnlyList<Guid> RootIds,
    IReadOnlyList<StackMutation> ProposedChanges,
    HashSet<string> RequiredChecks,
    HashSet<string> CompletedChecks,
    int RequiredApprovals,
    HashSet<string> Approvals,
    bool HasUnresolvedConflicts,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    bool IsActive = true);

public sealed record StackFreeze(
    Guid Id,
    Guid TargetDomainId,
    string? TargetPath,
    HashSet<Guid> DescendantDomainIds,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    string Reason,
    DateTimeOffset? ExpiresAt,
    string? ExpiryCondition,
    StackFreezeState State);

public sealed record StackAuditEntry(
    Guid Id,
    DateTimeOffset Timestamp,
    string ActorId,
    string Action,
    Guid ProjectId,
    Guid? TargetId,
    Guid? BeforeRevisionId,
    Guid? AfterRevisionId,
    string Outcome,
    string? Detail = null);

public sealed record StackDomainSnapshot(
    Guid Id,
    Guid ProjectId,
    string Name,
    StackDomainKind Kind,
    Guid? ParentId,
    Guid BaseRevisionId,
    Guid? HeadRevisionId,
    bool IsActive,
    bool IsDeleted,
    IReadOnlyDictionary<string, StackResource?> BaseTree,
    IReadOnlyList<StackMutation> LocalChanges);

public sealed record StackDomainChanges(int Added, int Deleted, int Modified, int Renamed, IReadOnlyList<StackMutation> Changes);

public sealed record StackEffectiveTreeSnapshot(Guid DomainId, Guid RevisionId, IReadOnlyDictionary<string, StackResource> Files);

public sealed class StackFailureException : Exception
{
    public StackFailureException(StackFailureCode code, string message, string target, bool recoverable = false, bool retryable = false, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Target = target;
        Recoverable = recoverable;
        Retryable = retryable;
    }

    public StackFailureCode Code { get; }
    public string Target { get; }
    public bool Recoverable { get; }
    public bool Retryable { get; }
}

public static class StackPath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal))
        {
            throw new StackFailureException(StackFailureCode.InvalidPath, "Stack paths must be relative to the project source root.", path);
        }

        string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Any(static part => part is "." or ".." || part.StartsWith(".stack", StringComparison.OrdinalIgnoreCase))
            || new[] { ".git", ".branches", ".source", ".roots" }.Contains(parts[0], StringComparer.OrdinalIgnoreCase))
        {
            throw new StackFailureException(StackFailureCode.InvalidPath, "The path is empty, traverses outside the project, or targets reserved Stack metadata.", path);
        }

        return string.Join('/', parts);
    }

    public static bool IsWithin(string path, string root)
    {
        string normalizedPath = Normalize(path);
        string normalizedRoot = Normalize(root).TrimEnd('/');
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase);
    }
}
