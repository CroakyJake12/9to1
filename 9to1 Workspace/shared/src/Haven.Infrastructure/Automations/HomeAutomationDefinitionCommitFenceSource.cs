using Haven.Application;
using Haven.Application.Automations;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Trusted same-factory Home fence for disabled reusable Prepared journal mutation only.
/// The actual SQL owner still verifies UUID/revision/private admission/exact immutable full candidate.</summary>
public sealed class HomeAutomationDefinitionCommitFenceSource(SqliteDatabase originalDatabase,
    FileHomeCoreStateStore originalHome, HomeLocalProfileIdentity originalProfiles,
    HomeResourceStoreOwnershipAuthority originalOwnership, HomeResourceOperationBroker originalBroker)
{
    internal bool IsFor(AutomationLocalStoreAuthority actualAuthority, ISqliteConnectionFactory actualFactory) =>
        ReferenceEquals(originalDatabase, actualFactory) && actualAuthority.IsBoundToHome(originalBroker, originalOwnership);
    internal ValueTask<HomeClaimedResourceCommitFence?> CaptureAsync(AutomationLocalStoreAuthority actualAuthority,
        IAutomationDefinitionCommitAdmission admission, ISqliteConnectionFactory actualFactory,
        AutomationDefinitionCommitContext context, AuthenticatedResourceActor originalActor, CancellationToken ct)
    {
        if (!IsFor(actualAuthority, actualFactory) || context.EntityKind != AutomationOwnerEntityKind.ReusableTask ||
            context.ActionID != "automations.update" || context.ExpectedRevision < 1)
            return ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null);
        var capability = actualAuthority.CapturePreparedCapability(admission, actualFactory, context);
        return capability is null ? ValueTask.FromResult<HomeClaimedResourceCommitFence?>(null) :
            HomeClaimedResourceCommitFence.CaptureAutomationDefinitionAsync(originalBroker, originalHome, originalProfiles,
                originalOwnership, context.StoreIdentity.StoreId.ToString("D"), context.EntityID, context.ExpectedRevision,
                capability, originalActor,
                () => actualAuthority.CapturePreparedCapability(admission, actualFactory, context) is { } original &&
                    ReferenceEquals(original, capability), ct);
    }
}
