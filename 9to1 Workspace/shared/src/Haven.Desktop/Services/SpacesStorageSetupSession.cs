using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Spaces supplies its actual canonical identity/evidence to the shared Home setup workflow.</summary>
internal sealed class SpacesStorageSetupSession(IResourceStoreIdentitySource identities,
    IAuthenticatedResourceActorSource actors, HomeLocalStoreOwnership ownership,
    SpacesLocalStoreEvidenceProvider evidence)
    : HomeLocalStoreSetupSession("spaces", identities, actors, ownership, evidence);
