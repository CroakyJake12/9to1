namespace Haven.Application;

public sealed partial class TaskRunOriginalFrameOwner : ITaskRunOriginalFrameProcessJoinGuard
{
    /// <summary>Checks the existing execution marker without starting or publishing close work.</summary>
    public void DemandExternalOriginalProcessJoin()
    {
        lock (_sync)
        {
            if (_executing.Value is not null)
                throw new InvalidOperationException("A frame cannot join the owner that contains it.");
        }
    }
}
