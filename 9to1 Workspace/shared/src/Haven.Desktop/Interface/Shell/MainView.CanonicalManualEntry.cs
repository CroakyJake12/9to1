using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Views.Shell;

public sealed partial class MainView
{
    private IDeveloperWorkspaceStore? _manualWorkspaces;
    private DeveloperTaskWorkspaceService? _manualDevelopment;
    private DeveloperProjectWorkbenchPageFactory? _manualWorkbenchFactory;
    private ICuiSceneReadiness? _manualHomeReadiness;
    private Func<string, CancellationToken, Task<DeveloperProjectReference>>? _createManualEntry;
    private Func<DeveloperProjectReference, Task>? _openExistingManualEntry;

    /// <summary>
    /// Retains the genuine manual (no-AI) saved-project providers for the canonical shell landing.
    /// All providers must be supplied together by the App wiring (the real configured Files workspace
    /// store, the SAME Dev/readiness owners and the reviewed register-existing create/open agents).
    /// This registers no actor, grant, provider or business owner; the page uses these only to
    /// enumerate/register/open same manual no-AI projects through the same reviewed owner chain as the
    /// 9to1://dev session. Opening a saved manual project resolves the SAME identity and presents the
    /// SAME compiled-CUI manual workbench in a real shell tab.
    /// </summary>
    internal void ConfigureOriginalManualDevelopmentEntry(
        IDeveloperWorkspaceStore workspaces,
        DeveloperTaskWorkspaceService development,
        DeveloperProjectWorkbenchPageFactory workbenchFactory,
        ICuiSceneReadiness homeReadiness,
        Func<string, CancellationToken, Task<DeveloperProjectReference>> createManual,
        Func<DeveloperProjectReference, Task> openExistingManual)
    {
        Dispatcher.UIThread.VerifyAccess();
        _originalShellWork.DemandAdmission();
        ArgumentNullException.ThrowIfNull(workspaces);
        ArgumentNullException.ThrowIfNull(development);
        ArgumentNullException.ThrowIfNull(workbenchFactory);
        ArgumentNullException.ThrowIfNull(homeReadiness);
        ArgumentNullException.ThrowIfNull(createManual);
        ArgumentNullException.ThrowIfNull(openExistingManual);
        if (_manualWorkspaces is not null || _createManualEntry is not null || _openExistingManualEntry is not null)
            throw new InvalidOperationException("The original manual development entry is already bound.");
        _manualWorkspaces = workspaces;
        _manualDevelopment = development;
        _manualWorkbenchFactory = workbenchFactory;
        _manualHomeReadiness = homeReadiness;
        _createManualEntry = createManual;
        _openExistingManualEntry = openExistingManual;
    }
}
