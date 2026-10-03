using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using HavenOS.Home.NativeUI;

namespace Haven.Desktop.Controls;

internal sealed class SpacesStorageSetupCuiSurface(SpacesStorageSetupSession setup, ICuiSceneReadiness readiness,
    Func<string, CancellationToken, Task> reviewPermissions)
    : HomeLocalStoreSetupCuiSurface(setup, "Spaces", "Spaces", readiness, reviewPermissions);
