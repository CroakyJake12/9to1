using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Stacks.NativeUI;

public static class StackNativeSurface
{
    public static CuiNativeScene CreateScene(StackCuiWorkspace workspace, ICuiSceneReadiness originalHomeReadiness)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(originalHomeReadiness);
        return new("stacks", "Stacks", "Dev", StackCuiWorkspace.LoadDocument(), workspace, workspace, originalHomeReadiness)
        { IsPublicationCurrent = () => workspace.OriginalClose is null };
    }
    public static int RunSetupRequired(string[] arguments)
    {
        var bindings = new CuiViewModel();
        return CuiNativeHost.Run(new("stacks", "Stacks", "Dev", StackCuiWorkspace.LoadDocument(), bindings, bindings, new RequiredHome()), arguments);
    }
    private sealed class RequiredHome : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable, "StacksOriginalHomeProjectUnavailable",
                "Open Stacks from Home to connect your authorised source projects. Existing projects are preserved."));
        }
    }
}
