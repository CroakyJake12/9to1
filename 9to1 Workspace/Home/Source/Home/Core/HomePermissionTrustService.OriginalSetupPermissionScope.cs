using HavenOS.Home.Core;
namespace HavenOS.Home.PermissionsTrustNotifications;

public sealed partial class HomePermissionTrustService
{
    internal Task<bool> IsSetupExecutionCurrentWithinOriginalSourceAsync(string requestId,
        Action<Action> scope, Action<Task> retain, CancellationToken token, Action<Exception>? retainUnexpectedCallback = null) =>
        RunOriginalImportPermissionAsync(scope, retain,
            source => IsExecutionCurrentOriginalSetupCoreAsync(requestId, token, source), retainUnexpectedCallback);
}
