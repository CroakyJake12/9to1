using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalSourceSelection : IDeveloperProjectOriginalManualStageSource
{
    private sealed class ManualStage(Selection selection, IDeveloperProjectOriginalReadAdmission read,
        IDeveloperProjectOriginalExistingSourceCapture capture, RegisteredWitness witness, DeveloperSavedStageChoice choice)
        : ManualExecutionBase(selection, read, capture, witness, choice.DefinitionSha256, "dev-manual-stage"), IDeveloperProjectOriginalManualStageBinding
    {
        public string SavedTaskId => choice.Task.TaskId;
        public long SavedTaskRevision => choice.Task.Revision;
        public string StageId => choice.Stage.StageId;
        public string StageKind => choice.Stage.Kind.ToString();
        public bool RequiresWorkspaceTrust => choice.Task.RequiresWorkspaceTrust;
        public override string Command => choice.Stage.Command!;
        public override int TimeoutSeconds => choice.TimeoutSeconds;
    }
    private ManualStage RequireManualStage(IDeveloperProjectOriginalManualStageBinding binding)
    {
        lock (_gate) return binding is ManualStage actual && ReferenceEquals(RequireManualBase(actual), actual)
            ? actual : throw new UnauthorizedAccessException("SAME live Files Saved-stage execution issuance required.");
    }
    public bool IsIssuedOriginalManualStage(IDeveloperProjectOriginalManualStageBinding binding)
    {
        lock (_gate) return !_retiring && binding is ManualStage actual && _manualExecutions.Contains(actual) &&
            IsIssuedOriginal(actual.Capture) && IsIssuedOriginal(actual.Selection);
    }
    public Task<IDeveloperProjectOriginalManualStageBinding> CaptureOriginalManualStageAsync(IDeveloperProjectOriginalReadSelection value,
        IDeveloperProjectOriginalReadAdmission read, IDeveloperProjectOriginalExistingSourceCapture capture,
        string savedTaskId, long savedTaskRevision, string stageId, string definitionSha256, CancellationToken token)
        => Start<IDeveloperProjectOriginalManualStageBinding>(async sources =>
    {
        var selection = Require(value);
        if (selection.Registered is not { } descriptor || !IsIssuedOriginal(capture))
            throw new UnauthorizedAccessException("Saved-stage execution requires the genuine registered saved-project capture.");
        RegisteredWitness witness;
        lock (_gate)
            if (!_registeredCaptures.TryGetValue(capture, out witness!) || !ReferenceEquals(witness.Selection, selection))
                throw new UnauthorizedAccessException("Stage capture belongs to another selected project.");
        await RevalidateBody(selection, selection.Workspace.Actor, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => reads.ValidateOriginalAsync(selection, read, token)).ConfigureAwait(false);
        await RevalidateRegisteredCaptureAsync(selection, capture, sources, token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(witness.Read.OriginalWorkspaceDocument);
        var workspace = document.RootElement.GetProperty("workspace").Deserialize<DeveloperWorkspace>(RegisteredJson)
            ?? throw new InvalidDataException("The native saved workspace is missing.");
        var reference = JsonSerializer.Deserialize<DeveloperProjectReference>(descriptor.ExactProjectReferenceJson, RegisteredJson)
            ?? throw new InvalidDataException("The exact saved project reference is missing.");
        var project = workspace.Projects.Single(row => row.ProjectId == descriptor.ProjectId);
        var root = workspace.Roots.Single(row => row.RootId == descriptor.RootId);
        var repository = descriptor.RepositoryBindingId is { } id ? workspace.SourceControlBindings.Single(row => row.BindingId == id) : null;
        if (workspace.WorkspaceId != descriptor.WorkspaceId || workspace.Revision != descriptor.WorkspaceRevision || root.EnvironmentId != "local" ||
            RegisteredSerialize(project) != descriptor.ExactSelectedProjectJson || RegisteredSerialize(root) != descriptor.ExactSelectedRootJson ||
            (repository is null ? null : RegisteredSerialize(repository)) != descriptor.ExactSelectedRepositoryJson)
            throw new UnauthorizedAccessException("The actual saved project/root/repository differs from this local stage request.");
        var prepared = DeveloperSavedStageSelection.Select(new(reference, workspace, project, root, repository),
            savedTaskId, savedTaskRevision, stageId, definitionSha256);
        if (!prepared.Succeeded || prepared.Value is not { } choice)
            throw new UnauthorizedAccessException("The inspected stage definition differs from the actual saved task metadata.");
        DemandRegisteredCaptureNative(selection, capture);
        var original = new ManualStage(selection, read, capture, witness, choice);
        lock (_gate)
        {
            if (_retiring || _manualExecutions.Count >= 128) throw new InvalidOperationException("Manual process source custody is closed or full.");
            _manualExecutions.Add(original);
        }
        return original;
    });
    public IReadOnlyList<ResourceScope> GetOriginalManualStageScopes(IDeveloperProjectOriginalManualStageBinding binding) => [RequireManualStage(binding).Scope];
    public Task RevalidateOriginalManualStageAsync(IDeveloperProjectOriginalManualStageBinding binding, AuthenticatedResourceActor actor, CancellationToken token)
        => Start(async sources =>
    {
        var original = RequireManualStage(binding);
        if (actor != original.OriginalActor) throw new UnauthorizedAccessException("The original stage actor changed.");
        await RevalidateBody(original.Selection, actor, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => reads.ValidateOriginalAsync(original.Selection, original.Read, token)).ConfigureAwait(false);
        await RevalidateRegisteredCaptureAsync(original.Selection, original.Capture, sources, token).ConfigureAwait(false);
        DemandRegisteredCaptureNative(original.Selection, original.Capture); return true;
    });
    public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalManualStagePinAsync(IDeveloperProjectOriginalManualStageBinding binding, CancellationToken token)
        => AcquireManualExecutionPinCoreAsync(binding, token);
    public void DemandOriginalManualStagePin(IDeveloperProjectOriginalManualStageBinding binding, IDeveloperWorkspaceOriginalExecutionCommitPin pin)
        => DemandManualExecutionPinCore(binding, pin);
    public Task CloseOriginalManualStagePinAsync(IDeveloperProjectOriginalManualStageBinding binding, IDeveloperWorkspaceOriginalExecutionCommitPin pin)
        => CloseManualExecutionPinCoreAsync(binding, pin);
}
