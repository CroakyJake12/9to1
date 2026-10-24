namespace Haven.Application;

/// <summary>Marker for the SAME actually committed value. Instantiation, serialization,
/// equality or implementation of this interface is never provenance or permission.</summary>
public interface IDeveloperProjectOriginalMetadataCommitValue { }

/// <summary>Original canonical metadata-write provenance, not a native/currentness/actor/
/// Task/READ/WRITE/execution grant. The actual configured SAME store must recognise this
/// exact receipt and committed-value reference before its observations can be used.
/// Consumers must freshly read the after document/root, compare both document identities
/// and unchanged complete authority projection, then use their source-owned rebind protocol.
/// Old execution grants/pins/cold capsules are never silently promoted by these fields.</summary>
public interface IDeveloperProjectOriginalMetadataTransition
{
    IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore { get; }
    IDeveloperProjectOriginalMetadataCommitValue OriginalCommittedValue { get; }
    Guid WorkspaceId { get; }
    string BeforeDocumentSha256 { get; }
    string AfterDocumentSha256 { get; }
    string WorkspaceAuthoritySha256 { get; }
    Task OriginalAtomicWriteTask { get; }
}

/// <summary>Additional provenance port on the existing original metadata store. No alternate
/// provider/store is created. A copied marker/receipt, foreign store, failed or cancelled writer
/// cannot qualify. Issuance is historical provenance; it is not proof the document is current.</summary>
public interface IDeveloperProjectOriginalMetadataTransitionStore : IDeveloperProjectOriginalWorkspaceMetadataStore
{
    IDeveloperProjectOriginalMetadataTransition? GetOriginalMetadataTransition(
        IDeveloperProjectOriginalMetadataCommitValue sameActuallyCommittedValue);
    bool IsIssuedOriginalMetadataTransition(IDeveloperProjectOriginalMetadataTransition sameTransition,
        IDeveloperProjectOriginalMetadataCommitValue sameActuallyCommittedValue);

    /// <summary>Pure schema validation of the actual before/after native observations against
    /// SAME private receipt. Strings alone grant nothing; Home/Files must independently prove
    /// their actual source-issued READs, healthy closes and current actor/root/permission.</summary>
    bool VerifyOriginalMetadataTransitionDocuments(IDeveloperProjectOriginalMetadataTransition sameTransition,
        IDeveloperProjectOriginalMetadataCommitValue sameActuallyCommittedValue,
        string originalNativeBeforeDocument, string actualNativeAfterDocument) => false;
}
