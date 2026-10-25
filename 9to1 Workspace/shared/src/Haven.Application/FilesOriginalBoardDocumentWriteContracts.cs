namespace Haven.Application;

/// <summary>Private Files-owner destination, issued only from the actual selected
/// file/parent/registered materialization and separately claimed files.board.write
/// Home operation. An implementation or copied description grants no authority.</summary>
public interface IFilesOriginalBoardDocumentWriteDestination { }

/// <summary>Physical description only. ParentFolderId is the selected file's actual
/// canonical parent; it is never converted into a path. DirectoryPath comes only
/// from that SAME Files owner's existing registered anchor and is not a Home/UI API.
/// RelativeContentReference is .9to1-board-{NewRevisionId:N}.content.</summary>
public sealed record FilesOriginalBoardDocumentWriteDescription(string DirectoryPath,
    Guid FilesStoreId, Guid ParentFolderId, Guid FileId, Guid ExpectedRevisionId,
    Guid NewRevisionId, Guid DocumentId, string RelativeContentReference,
    long PayloadBytes, string PayloadSha256, AuthenticatedResourceActor OriginalActor);

public interface IFilesOriginalBoardDocumentWriteSource
{
    bool IsIssuedOriginalBoardDocumentWriteDestination(IFilesOriginalBoardDocumentWriteDestination originalDestination);
    FilesOriginalBoardDocumentWriteDescription GetOriginalBoardDocumentWriteDescription(
        IFilesOriginalBoardDocumentWriteDestination originalDestination);
    /// <summary>Raw current same Files/file/parent/revision and held Home claim check.
    /// Called before each physical effect and beneath metadata CAS: no ordinary
    /// Home/profile/store read or writer reentry is permitted in this callback.</summary>
    void DemandOriginalFilesBoardDocumentCommit(IFilesOriginalBoardDocumentWriteDestination originalDestination);
}

/// <summary>Detached receipt observations, never a permission, metadata commit or
/// replacement for the privately issued physical materialization instance.</summary>
public sealed record FilesOriginalBoardDocumentMaterializationRecord(Guid FilesStoreId,
    Guid ParentFolderId, Guid FileId, Guid ExpectedRevisionId, Guid NewRevisionId,
    Guid DocumentId, string RelativeContentReference, long SizeBytes, string Sha256,
    AuthenticatedResourceActor OriginalActor);

public interface IFilesOriginalBoardDocumentMaterialization : IAsyncDisposable
{
    IFilesOriginalBoardDocumentWriteDestination OriginalDestination { get; }
    FilesOriginalBoardDocumentMaterializationRecord OriginalRecord { get; }
    string RelativeContentReference { get; }
    long SizeBytes { get; }
    string Sha256 { get; }
    Task? OriginalClose { get; }
    void RequestRetirement();
    void DemandExternalOriginalRetirementJoin();
    Task CloseAndDrainAsync();
}

/// <summary>Immutable create-only bytes before the SAME Files expected-revision
/// metadata CAS. Old content is never overwritten/deleted. Rejected/unknown CAS
/// keeps prior metadata and retains new bytes for owning recovery; closing this
/// physical receipt never guesses whether a metadata write committed.</summary>
public interface IFilesOriginalBoardDocumentPhysicalOwner
{
    IFilesOriginalBoardDocumentWriteSource? OriginalBoardDocumentWriteSource { get; }
    void BindOriginalBoardDocumentWriteSource(IFilesOriginalBoardDocumentWriteSource sameSource);
    Task<IFilesOriginalBoardDocumentMaterialization> MaterializeOriginalBoardDocumentWithinSourceAsync(
        IFilesOriginalBoardDocumentWriteDestination originalDestination, ReadOnlyMemory<byte> originalValidatedPayload,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    bool IsIssuedOriginalBoardDocumentMaterialization(IFilesOriginalBoardDocumentMaterialization originalMaterialization);
    Task RevalidateOriginalBoardDocumentWithinSourceAsync(IFilesOriginalBoardDocumentMaterialization originalMaterialization,
        Action<Action> scope, Action<Task> retain, CancellationToken cancellationToken);
    /// <summary>Raw retained inode/path/source check for the original metadata CAS.
    /// Full payload validation/rehash occurs outside the Files writer via Revalidate.</summary>
    void DemandOriginalBoardDocumentCommit(IFilesOriginalBoardDocumentMaterialization originalMaterialization);
    Task? OriginalClose { get; }
    void RequestRetirement();
    void DemandExternalOriginalRetirementJoin();
    Task CloseAndDrainAsync();
}
