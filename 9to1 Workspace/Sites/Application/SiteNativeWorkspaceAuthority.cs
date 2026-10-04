namespace HavenOS.Apps.Sites.Application;

/// <summary>Trusted Files/Home projection. ProfileID is a verified local profile, never a substitute CAKE AccountID.</summary>
public sealed record SiteNativeWorkspaceBinding(string ProfileId,string ActorId,string AuthenticationRevision,
    Guid FilesFolderId,string FolderRevision,string RootDirectory);

/// <summary>
/// Implemented by the designated native host using current Home ownership, OS profile,
/// canonical Files metadata and explicit directory binding. No private fallback root.
/// Apps recheck this binding and Home resource/action authority on every mutation;
/// possession of a binding does not authorise an action or bypass Sites revision checks.
/// </summary>
public interface ISiteNativeWorkspaceAuthority
{
    Task<SiteNativeWorkspaceBinding?> GetCurrentAsync(CancellationToken cancellationToken=default);
}
