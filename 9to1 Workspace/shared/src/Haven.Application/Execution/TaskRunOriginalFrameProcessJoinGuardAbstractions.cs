namespace Haven.Application;

/// <summary>Pure external join preflight for the same finite-original frame owner.
/// This starts no work and supplies no admission, settlement or effect evidence.</summary>
public interface ITaskRunOriginalFrameProcessJoinGuard
{
    void DemandExternalOriginalProcessJoin();
}
