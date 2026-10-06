using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace Haven.Application;

/// <summary>Once-created import metadata, not a resource permission or an owning source receipt.
/// The genuine preparation persists this whole intent before resource effects. An uncertain
/// preparation/step must recover the same IDs, never create another apparently fresh import.</summary>
public sealed record DeveloperProjectSetupIntent(
    Guid SetupId, AuthenticatedResourceActor OriginalActor,
    Guid OriginalFilesStoreId, string OriginalFilesConfigurationDigest,
    Guid OriginalDestinationFolderId, string OriginalDestinationFolderRevision,
    string OriginalSourceCaptureReference, string OriginalSourceDigest,
    Guid WorkspaceId, long ExpectedWorkspaceRevision, Guid ProjectId, Guid RootId,
    Guid ProjectFolderId, string ProjectName,
    ImmutableArray<DeveloperProjectSetupFolder> Folders,
    ImmutableArray<DeveloperProjectSetupFile> Files,
    ImmutableArray<DeveloperProjectSetupStep> Steps)
{
    public DeveloperProjectSetupMode Mode { get; init; }
    // Metadata only. The SAME private source issuer must prove actual configured-root confinement.
    public string? OriginalExistingProjectRoot { get; init; }
    public const int MaximumFiles = 64;
    public const int MaximumFolders = 64;
    public const int MaximumSteps = 512;
    public const long MaximumFileBytes = 4 * 1024 * 1024;
    public const long MaximumTotalBytes = 16 * 1024 * 1024;
    public string Digest() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant();
    public string? Validate()
    {
        if (SetupId == Guid.Empty || WorkspaceId == Guid.Empty || ProjectId == Guid.Empty || RootId == Guid.Empty || ProjectFolderId == Guid.Empty ||
            OriginalFilesStoreId == Guid.Empty || OriginalDestinationFolderId == Guid.Empty || ExpectedWorkspaceRevision < 0)
            return "Retain the once-created setup/project identities and original existing Files destination.";
        if (OriginalActor is null || OriginalActor.AccountId is not null || OriginalActor.OrganisationId is not null ||
            string.IsNullOrWhiteSpace(OriginalActor.ActorId) || !Guid.TryParse(OriginalActor.ProfileId, out var profile) || profile == Guid.Empty)
            return "Local project registration requires the actual authenticated personal OS profile.";
        if (!Guid.TryParse(OriginalDestinationFolderRevision, out var revision) || revision == Guid.Empty ||
            !IsHash(OriginalFilesConfigurationDigest) || !IsHash(OriginalSourceDigest) || string.IsNullOrWhiteSpace(OriginalSourceCaptureReference))
            return "Original source capture, Files configuration and destination revision are required.";
        if (!SafeName(ProjectName) || Files.IsDefaultOrEmpty || Files.Length > MaximumFiles || Folders.IsDefault || Folders.Length > MaximumFolders ||
            Steps.IsDefaultOrEmpty || Steps.Length > MaximumSteps)
            return "This bounded registration requires a safe project name and a finite original plan; unsupported size is refused, not truncated.";
        if (!Enum.IsDefined(Mode) || Mode == DeveloperProjectSetupMode.RegisterExisting &&
            (string.IsNullOrWhiteSpace(OriginalExistingProjectRoot) || !Path.IsPathFullyQualified(OriginalExistingProjectRoot)) ||
            Mode == DeveloperProjectSetupMode.CopyIntoFiles && OriginalExistingProjectRoot is not null)
            return "Retain the explicit original setup mode and genuine existing project location; no path is an authority grant.";
        var folders = new Dictionary<Guid, DeveloperProjectSetupFolder>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders)
        {
            if (folder is null || folder.FolderId == Guid.Empty || folder.ParentFolderId == Guid.Empty || folder.FolderId == ProjectFolderId ||
                !SafeRelative(folder.RelativePath) || !folders.TryAdd(folder.FolderId, folder) || !paths.Add(folder.RelativePath))
                return "Project folder identities and relative locations must be distinct and safe.";
        }
        var fileIds = new HashSet<Guid>(); var revisionIds = new HashSet<Guid>(); long total = 0;
        foreach (var file in Files)
        {
            if (file is null || file.FileId == Guid.Empty || file.RevisionId == Guid.Empty || !fileIds.Add(file.FileId) || !revisionIds.Add(file.RevisionId) ||
                folders.ContainsKey(file.FileId) || file.FileId == ProjectFolderId || !SafeRelative(file.RelativePath) || !paths.Add(file.RelativePath) ||
                !IsHash(file.ContentSha256) || file.SizeBytes < 0 || file.SizeBytes > MaximumFileBytes || string.IsNullOrWhiteSpace(file.OriginalSourceReference) ||
                file.ParentFolderId != ProjectFolderId && !folders.ContainsKey(file.ParentFolderId))
                return "Each source file requires its once-created canonical parent/item/revision and exact captured bytes.";
            total = checked(total + file.SizeBytes);
        }
        if (total > MaximumTotalBytes) return "The captured source exceeds this adapter's documented finite import bound.";
        foreach (var folder in Folders)
        {
            var seen = new HashSet<Guid>(); var current = folder;
            for (var depth = 0; ; depth++)
            {
                if (depth >= 10 || !seen.Add(current.FolderId)) return "The source folder ancestry is cyclic or exceeds this adapter's depth bound.";
                if (current.ParentFolderId == ProjectFolderId) break;
                if (!folders.TryGetValue(current.ParentFolderId, out current!)) return "A source folder is outside the once-created project root.";
            }
        }
        string ParentPath(Guid parent) => parent == ProjectFolderId ? "" : folders[parent].RelativePath;
        static string DirectoryPart(string path) => path.LastIndexOf('/') is var slash && slash >= 0 ? path[..slash] : "";
        foreach (var folder in Folders)
            if (!StringComparer.Ordinal.Equals(DirectoryPart(folder.RelativePath), ParentPath(folder.ParentFolderId)))
                return "Folder path and once-created canonical parent must describe the SAME hierarchy.";
        foreach (var file in Files)
            if (!StringComparer.Ordinal.Equals(DirectoryPart(file.RelativePath), ParentPath(file.ParentFolderId)))
                return "File path and once-created canonical parent must describe the SAME hierarchy.";
        if (Steps.Any(value => value is null || value.StepId == Guid.Empty || !Enum.IsDefined(value.Kind)) ||
            Steps.Select(value => value.StepId).Distinct().Count() != Steps.Length)
            return "Every actual effect step requires one distinct retained identity.";
        // A public journal cannot omit an effect, add an unrelated resource, or substitute
        // another file's ID. The private approval still has to issue the exact SAME plan.
        var required = new List<(DeveloperProjectSetupStepKind Kind, Guid? FileId, Guid? FolderId)>
            { (DeveloperProjectSetupStepKind.CreateProjectFolder, null, ProjectFolderId),
              (Mode == DeveloperProjectSetupMode.RegisterExisting ? DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory : DeveloperProjectSetupStepKind.CreateProjectDirectory, null, ProjectFolderId) };
        foreach (var folder in Folders.OrderBy(value => value.RelativePath.Count(c => c == '/')).ThenBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            required.Add((DeveloperProjectSetupStepKind.CreateChildFolder, null, folder.FolderId));
            required.Add((Mode == DeveloperProjectSetupMode.RegisterExisting ? DeveloperProjectSetupStepKind.ObserveExistingChildDirectory : DeveloperProjectSetupStepKind.CreateChildDirectory, null, folder.FolderId));
        }
        required.Add((DeveloperProjectSetupStepKind.RegisterProjectFolder, null, ProjectFolderId));
        foreach (var file in Files.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            if (Mode == DeveloperProjectSetupMode.RegisterExisting)
                required.Add((DeveloperProjectSetupStepKind.RegisterExistingFileMetadata, file.FileId, file.ParentFolderId));
            else
            {
                required.Add((DeveloperProjectSetupStepKind.PublishImmutableSource, file.FileId, file.ParentFolderId));
                required.Add((DeveloperProjectSetupStepKind.PublishMaterializedFile, file.FileId, file.ParentFolderId));
                required.Add((DeveloperProjectSetupStepKind.PublishFileRevision, file.FileId, file.ParentFolderId));
            }
            required.Add((DeveloperProjectSetupStepKind.RegisterMaterialization, file.FileId, file.ParentFolderId));
        }
        required.Add((DeveloperProjectSetupStepKind.SaveDevWorkspace, null, ProjectFolderId));
        if (Steps.Length != required.Count || !Steps.Select(value => (value.Kind, value.FileId, value.FolderId)).SequenceEqual(required))
            return "The once-created step plan must cover the exact resources in deterministic parent-first order.";
        return null;
    }
    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool SafeName(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 120 && value == value.Trim() &&
        value is not ("." or "..") && !value.Any(character => character < 32 || character is '/' or '\\' or ':' or '\0') && !value.EndsWith('.');
    private static bool SafeRelative(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048 && !Path.IsPathFullyQualified(value) &&
        !value.Contains('\\') && value.Split('/').All(SafeName);
}

public enum DeveloperProjectSetupMode { CopyIntoFiles = 0, RegisterExisting = 1 }

public sealed record DeveloperProjectSetupFolder(Guid FolderId, Guid ParentFolderId, string RelativePath);
public sealed record DeveloperProjectSetupFile(Guid FileId, Guid ParentFolderId, Guid RevisionId,
    string RelativePath, long SizeBytes, string ContentSha256, string OriginalSourceReference);

/// <summary>Canonical resource effects only. Persisting the intent/checkpoint is preparation and
/// outcome custody; a journal state never becomes Home approval or proof of a resource effect.</summary>
public enum DeveloperProjectSetupStepKind
{
    CreateProjectFolder, CreateChildFolder, RegisterProjectFolder,
    PublishImmutableSource, PublishFileRevision, RegisterMaterialization, SaveDevWorkspace,
    CreateProjectDirectory, CreateChildDirectory, PublishMaterializedFile,
    ObserveExistingProjectDirectory, ObserveExistingChildDirectory, RegisterExistingFileMetadata
}
public sealed record DeveloperProjectSetupStep(Guid StepId, DeveloperProjectSetupStepKind Kind, Guid? FileId = null, Guid? FolderId = null);

public enum DeveloperProjectSetupStepState { NotStarted, Admitted, Acknowledged, Unresolved }
/// <summary>Persisted recovery observation only. Original private producer receipts remain owning
/// evidence; deserializing this journal can never recreate a commit pin or authorize replay.</summary>
public sealed record DeveloperProjectSetupCheckpoint(
    DeveloperProjectSetupIntent Intent, long Revision, ImmutableArray<DeveloperProjectSetupStepObservation> Observations);
public sealed record DeveloperProjectSetupStepObservation(Guid StepId, DeveloperProjectSetupStepState State,
    string? OriginalReceiptReference, string? OriginalOutcomeDigest, string? ErrorCode);
