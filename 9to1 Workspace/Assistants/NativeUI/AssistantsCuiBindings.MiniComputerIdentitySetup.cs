using HavenOS.Apps.Assistants.MiniComputer;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsMiniComputerCuiBindings
{
    internal AssistantMiniComputerIdentitySetupPreview? OriginalIdentitySetup { get; private set; }
    private Task<AssistantMiniComputerIdentitySetupResult>? _identityTask;
    private AssistantMiniComputerIdentitySetupPreview? _identityAcceptedPreview;
    private AssistantMiniComputerIdentitySetupResult? _identityResult;
    private bool _identityPending, _identityUnconfirmed;
    private string _identityStatus = "", _identityRequest = "";
    private bool HasIdentitySetupChanges => _identityPending || _identityUnconfirmed ||
        OriginalIdentitySetup?.CanRequest == true && _identityResult is null;
    private bool CanRequestIdentitySetup => OriginalIdentitySetup?.CanRequest == true && _identityResult is null &&
        !_busy && !_pending && !_unconfirmed && !_identityPending && !_identityUnconfirmed;
    private bool CanDiscardIdentitySetup => OriginalIdentitySetup is not null &&
        !_busy && !_identityPending && !_identityUnconfirmed;
    internal void SetIdentitySetupPreview(AssistantMiniComputerIdentitySetupPreview preview)
    {
        if (HasUnconfirmedChanges) throw new InvalidOperationException("Settle the existing Mini Computer review first.");
        ClearPreview(); OriginalIdentitySetup = preview; _identityResult = null; _identityTask = null;
        _identityAcceptedPreview = null; _identityRequest = ""; _identityStatus = preview.Reason; Refresh();
    }
    internal void BeginIdentitySetup() { _identityPending = true; _identityStatus = "Waiting for the separate Home catalogue identity setup decision."; Refresh(); }
    internal void RetainOriginalIdentitySetup(AssistantMiniComputerIdentitySetupPreview preview, Task<AssistantMiniComputerIdentitySetupResult> actual)
    {
        if (!ReferenceEquals(preview, OriginalIdentitySetup) || _identityTask is not null && !ReferenceEquals(_identityTask, actual))
            throw new InvalidOperationException("Retain the SAME accepted catalogue setup preview and actual source Task.");
        _identityAcceptedPreview = preview; _identityTask = actual;
    }
    internal bool ObserveOriginalIdentitySettlement(AssistantMiniComputerIdentitySetupPreview preview, Task<AssistantMiniComputerIdentitySetupResult> actual)
    {
        if (!ReferenceEquals(preview, OriginalIdentitySetup) || !ReferenceEquals(preview, _identityAcceptedPreview) ||
            !ReferenceEquals(actual, _identityTask) || !actual.IsCompletedSuccessfully) return false;
        // This is private terminal custody, including after view revocation. It does
        // not publish UI, grant import, or waive any independent callback failure.
        _identityResult = actual.Result; _identityPending = false; return true;
    }
    internal void ObserveIdentitySetup(AssistantMiniComputerPendingObservation value)
    { _identityRequest = value.RequestId ?? ""; _identityStatus = value.Reason; Refresh(); }
    internal void PublishIdentitySetupResult(AssistantMiniComputerIdentitySetupResult result)
    { _identityResult = result; _identityPending = false; _identityStatus = result.Reason; Refresh(); }
    internal void MarkIdentitySetupUnconfirmed(string reason)
    { _identityUnconfirmed = true; _identityStatus = reason; Refresh(); }
    internal void ClearIdentitySetup()
    {
        if (_identityPending || _identityUnconfirmed) throw new InvalidOperationException("The accepted original catalogue setup must settle first.");
        OriginalIdentitySetup = null; _identityResult = null; _identityTask = null; _identityAcceptedPreview = null;
        _identityRequest = ""; _identityStatus = ""; Refresh();
    }
    private void RefreshIdentitySetup()
    {
        Set("HasMiniIdentityPreview", OriginalIdentitySetup is not null);
        Set("MiniIdentityStatus", _identityStatus); Set("MiniIdentityCatalogue", OriginalIdentitySetup?.CatalogueName ?? "");
        Set("MiniIdentityRequest", _identityRequest); Set("HasMiniIdentityRequest", _identityRequest.Length != 0);
        Set("CanMiniIdentityPrepare", IsActionAvailable("assistants.mini.identity.prepare") == true);
        Set("CanMiniIdentityRequest", IsActionAvailable("assistants.mini.identity.request") == true);
        Set("CanMiniIdentityDiscard", IsActionAvailable("assistants.mini.identity.discard") == true);
    }
}
