using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDeveloperManualProjectExecutionAdmissionSource : IDeveloperProjectOriginalManualStageAdmissionSource
{
    public Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireOriginalManualStageAsync(IDeveloperProjectOriginalManualStageBinding binding, CancellationToken token)
        => AcquireManualProcessAsync(new StageBinding(binding), token);

    private sealed class StageBinding(IDeveloperProjectOriginalManualStageBinding value) : ProcessBinding
    {
        private static IDeveloperProjectOriginalManualStageSource StageFiles(IDeveloperProjectOriginalManualExecutionSource files) =>
            files as IDeveloperProjectOriginalManualStageSource ?? throw new NotSupportedException("The configured Files owner cannot issue saved-stage execution.");
        internal override object Original => value;
        internal override string CanonicalRoot => value.CanonicalRoot;
        internal override AuthenticatedResourceActor OriginalActor => value.OriginalActor;
        internal override string Command => value.Command;
        internal override int TimeoutSeconds => value.TimeoutSeconds;
        internal override string ReceiptPrefix => "dev:manual-stage:";
        internal override string SuccessCode => "DEV_MANUAL_STAGE_EXIT_ZERO";
        internal override string FailureCode => "DEV_MANUAL_STAGE_INSPECT_OR_FAILED";
        internal override string ReviewText => "Run this exact saved development stage. It can change files and invoke programs under your local account. This approval covers this stage only; opening the project or approving READ does not grant execution permission.";
        internal override JsonElement ReviewArguments(Guid operation, ResourceScope scope) => JsonSerializer.SerializeToElement(new
        { operationId = operation, purpose = "saved-development-stage", value.SavedTaskId, value.SavedTaskRevision, value.StageId, value.StageKind,
            value.RequiresWorkspaceTrust, value.DefinitionSha256, value.CanonicalRoot, value.Command, value.TimeoutSeconds, scope,
            limits = "one local independent saved stage; no skipped prerequisites, argument-vector reinterpretation or remote execution; executor bounds stdout/stderr to 1,000,000 characters each" });
        internal override bool IsIssued(IDeveloperProjectOriginalManualExecutionSource files) => StageFiles(files).IsIssuedOriginalManualStage(value);
        internal override IReadOnlyList<ResourceScope> Scopes(IDeveloperProjectOriginalManualExecutionSource files) => StageFiles(files).GetOriginalManualStageScopes(value);
        internal override Task RevalidateAsync(IDeveloperProjectOriginalManualExecutionSource files, AuthenticatedResourceActor actor, CancellationToken token) => StageFiles(files).RevalidateOriginalManualStageAsync(value, actor, token);
        internal override Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquirePinAsync(IDeveloperProjectOriginalManualExecutionSource files, CancellationToken token) => StageFiles(files).AcquireOriginalManualStagePinAsync(value, token);
        internal override void DemandPin(IDeveloperProjectOriginalManualExecutionSource files, IDeveloperWorkspaceOriginalExecutionCommitPin pin) => StageFiles(files).DemandOriginalManualStagePin(value, pin);
        internal override Task ClosePinAsync(IDeveloperProjectOriginalManualExecutionSource files, IDeveloperWorkspaceOriginalExecutionCommitPin pin) => StageFiles(files).CloseOriginalManualStagePinAsync(value, pin);
    }
}
