using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDeveloperManualProjectExecutionAdmissionSource : IDeveloperProjectOriginalManualGitAdmissionSource
{
    public Task<IDeveloperProjectOriginalManualExecutionAdmission> AcquireOriginalManualGitAsync(IDeveloperProjectOriginalManualGitBinding binding, CancellationToken token)
        => AcquireManualProcessAsync(new GitBinding(binding), token);

    // The common private adapter preserves the two real public identity domains. A Git
    // operation never supplies or pretends to be a Console RunConfiguration.
    internal abstract class ProcessBinding
    {
        internal abstract object Original { get; }
        internal abstract string CanonicalRoot { get; }
        internal abstract AuthenticatedResourceActor OriginalActor { get; }
        internal abstract string Command { get; }
        internal abstract int TimeoutSeconds { get; }
        internal abstract string ReviewText { get; }
        internal virtual string ReceiptPrefix => "dev:manual-run:";
        internal virtual string SuccessCode => "DEV_MANUAL_RUN_EXIT_ZERO";
        internal virtual string FailureCode => "DEV_MANUAL_RUN_INSPECT_OR_FAILED";
        internal abstract JsonElement ReviewArguments(Guid operation, ResourceScope scope);
        internal abstract bool IsIssued(IDeveloperProjectOriginalManualExecutionSource files);
        internal abstract IReadOnlyList<ResourceScope> Scopes(IDeveloperProjectOriginalManualExecutionSource files);
        internal abstract Task RevalidateAsync(IDeveloperProjectOriginalManualExecutionSource files, AuthenticatedResourceActor actor, CancellationToken token);
        internal abstract Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquirePinAsync(IDeveloperProjectOriginalManualExecutionSource files, CancellationToken token);
        internal abstract void DemandPin(IDeveloperProjectOriginalManualExecutionSource files, IDeveloperWorkspaceOriginalExecutionCommitPin pin);
        internal abstract Task ClosePinAsync(IDeveloperProjectOriginalManualExecutionSource files, IDeveloperWorkspaceOriginalExecutionCommitPin pin);
    }
    private sealed class ConsoleBinding(IDeveloperProjectOriginalManualExecutionBinding value) : ProcessBinding
    {
        internal override object Original => value;
        internal override string CanonicalRoot => value.CanonicalRoot;
        internal override AuthenticatedResourceActor OriginalActor => value.OriginalActor;
        internal override string Command => value.Command;
        internal override int TimeoutSeconds => value.TimeoutSeconds;
        internal override string ReviewText => "Run this exact saved console command at the verified project root. It can change files and invoke programs with your local account. READ and source WRITE approvals do not authorize this process.";
        internal override JsonElement ReviewArguments(Guid operation, ResourceScope scope) => JsonSerializer.SerializeToElement(new
        { operationId = operation, value.ConfigurationId, value.ConfigurationRevision, value.DefinitionSha256, value.CanonicalRoot, value.Command, value.TimeoutSeconds,
            scope, limits = "one local declared console process; retained output/exit; no prelaunch, preview or remote execution" });
        internal override bool IsIssued(IDeveloperProjectOriginalManualExecutionSource files) => files.IsIssuedOriginalManualExecution(value);
        internal override IReadOnlyList<ResourceScope> Scopes(IDeveloperProjectOriginalManualExecutionSource files) => files.GetOriginalManualExecutionScopes(value);
        internal override Task RevalidateAsync(IDeveloperProjectOriginalManualExecutionSource files, AuthenticatedResourceActor actor, CancellationToken token) => files.RevalidateOriginalManualExecutionAsync(value, actor, token);
        internal override Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquirePinAsync(IDeveloperProjectOriginalManualExecutionSource files, CancellationToken token) => files.AcquireOriginalManualExecutionPinAsync(value, token);
        internal override void DemandPin(IDeveloperProjectOriginalManualExecutionSource files, IDeveloperWorkspaceOriginalExecutionCommitPin pin) => files.DemandOriginalManualExecutionPin(value, pin);
        internal override Task ClosePinAsync(IDeveloperProjectOriginalManualExecutionSource files, IDeveloperWorkspaceOriginalExecutionCommitPin pin) => files.CloseOriginalManualExecutionPinAsync(value, pin);
    }
    private sealed class GitBinding(IDeveloperProjectOriginalManualGitBinding value) : ProcessBinding
    {
        private static IDeveloperProjectOriginalManualGitSource GitFiles(IDeveloperProjectOriginalManualExecutionSource files) =>
            files as IDeveloperProjectOriginalManualGitSource ?? throw new NotSupportedException("The configured Files owner cannot issue Git inspection.");
        internal override object Original => value;
        internal override string CanonicalRoot => value.CanonicalRoot;
        internal override AuthenticatedResourceActor OriginalActor => value.OriginalActor;
        internal override string Command => value.Command;
        internal override int TimeoutSeconds => value.TimeoutSeconds;
        internal override string ReceiptPrefix => "dev:manual-git:";
        internal override string SuccessCode => "DEV_MANUAL_GIT_EXIT_ZERO";
        internal override string FailureCode => "DEV_MANUAL_GIT_INSPECT_OR_FAILED";
        internal override string ReviewText => "Run Git to inspect this saved repository. Git sees saved files; unsaved editor changes are kept separately. This starts a local program under your account and requires its own permission.";
        internal override JsonElement ReviewArguments(Guid operation, ResourceScope scope) => JsonSerializer.SerializeToElement(new
        { operationId = operation, purpose = "git-inspection", value.Operation, operationName = value.Operation.ToString(), value.RepositoryBindingId, value.CanonicalRepositoryId,
            value.DefinitionSha256, value.CanonicalRoot, value.Command, value.TimeoutSeconds, scope,
            limits = "one local Git inspection; no optional locks, external diff, textconv or fsmonitor; executor bounds stdout/stderr to 1,000,000 characters each" });
        internal override bool IsIssued(IDeveloperProjectOriginalManualExecutionSource files) => GitFiles(files).IsIssuedOriginalManualGit(value);
        internal override IReadOnlyList<ResourceScope> Scopes(IDeveloperProjectOriginalManualExecutionSource files) => GitFiles(files).GetOriginalManualGitScopes(value);
        internal override Task RevalidateAsync(IDeveloperProjectOriginalManualExecutionSource files, AuthenticatedResourceActor actor, CancellationToken token) => GitFiles(files).RevalidateOriginalManualGitAsync(value, actor, token);
        internal override Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquirePinAsync(IDeveloperProjectOriginalManualExecutionSource files, CancellationToken token) => GitFiles(files).AcquireOriginalManualGitPinAsync(value, token);
        internal override void DemandPin(IDeveloperProjectOriginalManualExecutionSource files, IDeveloperWorkspaceOriginalExecutionCommitPin pin) => GitFiles(files).DemandOriginalManualGitPin(value, pin);
        internal override Task ClosePinAsync(IDeveloperProjectOriginalManualExecutionSource files, IDeveloperWorkspaceOriginalExecutionCommitPin pin) => GitFiles(files).CloseOriginalManualGitPinAsync(value, pin);
    }
}
