using CakeOS.Cui.Runtime;
using NineToOne.Admin;

// Separate-process authenticated Home transport is required; no configuration flag can manufacture readiness.
var unavailable=new CuiViewModel();
return CuiNativeHost.Run(AdminNativeScene.Create(unavailable,unavailable,new HomeBridgeRequired()),args);

sealed class HomeBridgeRequired:ICuiSceneReadiness
{
    public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,"HomeBridgeUnavailable",
            "Connect the compatible Home service and sign in to CAKE to open Admin."));
    }
}
