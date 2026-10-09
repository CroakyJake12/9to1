using System.Collections.Immutable;

namespace Haven.Application;

/// <summary>Metadata on a genuine retained source capture. This interface and its strings are
/// observations only; the configured physical issuer must recognize the SAME original object.</summary>
public interface IDeveloperProjectOriginalSourceCapture
{
    string OriginalCaptureReference { get; }
    string OriginalCaptureDigest { get; }
    ImmutableArray<string> OriginalFolderPaths { get; }
    ImmutableArray<DeveloperProjectCapturedSourceFile> OriginalFiles { get; }
}
public sealed record DeveloperProjectCapturedSourceFile(string RelativePath, long SizeBytes,
    string ContentSha256, string OriginalSourceReference);

/// <summary>Required trusted composition. An absent issuer refuses preparation. A public path,
/// copied descriptor, CAKE account or deserialized journal cannot recreate a source capture.
/// Implementations retain genuine source handles/read Tasks and their actor/root permission;
/// this port supplies no filesystem authority and is not an implementation of that producer.</summary>
public interface IDeveloperProjectOriginalCaptureAuthority
{
    bool IsIssuedOriginal(IDeveloperProjectOriginalSourceCapture original);
    Task RevalidateOriginalAsync(IDeveloperProjectOriginalSourceCapture original,
        AuthenticatedResourceActor originalActor, CancellationToken cancellationToken);
    void DemandExternalOriginalCaptureJoin();
}

/// <summary>Observation on a genuine source capture for a selected existing project already
/// beneath the SAME configured Files root. The public interface/path is not provenance; the
/// configured issuer still recognizes the SAME object and revalidates actual retained handles.</summary>
public interface IDeveloperProjectOriginalExistingSourceCapture : IDeveloperProjectOriginalSourceCapture
{
    string OriginalExistingProjectRoot { get; }
}
