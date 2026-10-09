using HavenOS.Apps.Assistants.Contracts;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Assistants;

/// <summary>
/// A trusted host supplies this private-context owner after binding the actual Home Den,
/// conversation/Chat/Task owners and resource/model/Dev ports. Implementing the interface,
/// observing CAKE identity or possessing a Task read partition grants none of that authority.
/// The bridge revalidates current owner-issued bindings before every canonical operation.
/// </summary>
public interface IBrowserAssistantsCanonicalOwner
{
    /// <summary>Actual Home/service/actor readiness checks. A constant Ready observation is not a host binding.</summary>
    ICuiSceneReadiness OriginalReadiness { get; }
    /// <summary>Creates a fresh presentation bridge over the existing canonical owners.
    /// The factory retains its actual work and any partially created bridge until external drain.</summary>
    Task<IAssistantCanonicalBridge> OpenOriginalBridgeAsync(CancellationToken cancellationToken);

    /// <summary>Immediately revoke actual account/resource authority. No save or refusal may veto revocation.</summary>
    void RevokePrivateContext();

    /// <summary>Reject calls made from this owner's live factory, source or publication callback.</summary>
    void DemandExternalOriginalRetirementJoin();

    /// <summary>The SAME retained factory/private-context close. Joins issued work without retiring borrowed business services.</summary>
    Task CloseAndDrainAsync();
}
