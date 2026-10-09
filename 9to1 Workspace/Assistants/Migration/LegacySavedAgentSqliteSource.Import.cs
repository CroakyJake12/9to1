using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Migration;

public sealed partial class LegacySavedAgentSqliteSource : IHomeOriginalScopedResourceStoreIdentitySource
{
    public ValueTask<ResourceStoreIdentity> GetStoreIdentityWithinOriginalSourceAsync(
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token) =>
        new(GetIdentityAsync(new(originalSynchronousScope, retainOriginalTask), token));

    // Pure composition observation: no path, identifier or detached ownership DTO
    // may substitute for this source's actual actor, source and receipt authority.
    public bool IsOriginalImportSession(HomeOriginalLocalStoreImportSession actual,
        IResourceStoreOwnershipReceiptAuthority actualReadAuthority) =>
        actual.IsOriginalSource(this, this, _profiles, actualReadAuthority);
}
