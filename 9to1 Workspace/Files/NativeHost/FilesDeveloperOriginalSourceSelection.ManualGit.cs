using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalSourceSelection : IDeveloperProjectOriginalManualGitSource
{
    private sealed class ManualGit(Selection selection, IDeveloperProjectOriginalReadAdmission read,
        IDeveloperProjectOriginalExistingSourceCapture capture, RegisteredWitness witness, DeveloperProjectGitInspectionKind operation,
        DeveloperSourceControlBinding repository, DeveloperGitCommand command)
        : ManualExecutionBase(selection, read, capture, witness, command.DefinitionSha256, "dev-manual-git"), IDeveloperProjectOriginalManualGitBinding
    {
        public DeveloperProjectGitInspectionKind Operation => operation;
        public string RepositoryBindingId => repository.BindingId;
        public string CanonicalRepositoryId => repository.CanonicalRepositoryId;
        public override string Command => command.Command;
        public override int TimeoutSeconds => DeveloperGitCommand.TimeoutSeconds;
    }
    private ManualGit RequireManualGit(IDeveloperProjectOriginalManualGitBinding binding)
    {
        lock (_gate) return binding is ManualGit actual && ReferenceEquals(RequireManualBase(actual), actual)
            ? actual : throw new UnauthorizedAccessException("SAME live Files Git inspection issuance required.");
    }
    public bool IsIssuedOriginalManualGit(IDeveloperProjectOriginalManualGitBinding binding)
    {
        lock (_gate) return !_retiring && binding is ManualGit actual && _manualExecutions.Contains(actual) &&
            IsIssuedOriginal(actual.Capture) && IsIssuedOriginal(actual.Selection);
    }
    public Task<IDeveloperProjectOriginalManualGitBinding> CaptureOriginalManualGitAsync(IDeveloperProjectOriginalReadSelection value,
        IDeveloperProjectOriginalReadAdmission read, IDeveloperProjectOriginalExistingSourceCapture capture,
        DeveloperProjectGitInspectionKind operation, string definitionSha256, CancellationToken token)
        => Start<IDeveloperProjectOriginalManualGitBinding>(async sources =>
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        var selection = Require(value);
        if (selection.Registered is not { } descriptor || !IsIssuedOriginal(capture))
            throw new UnauthorizedAccessException("Git inspection requires the genuine registered saved-project capture.");
        RegisteredWitness witness;
        lock (_gate)
            if (!_registeredCaptures.TryGetValue(capture, out witness!) || !ReferenceEquals(witness.Selection, selection))
                throw new UnauthorizedAccessException("Git capture belongs to another selected project.");
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
            repository is not { ProviderId: "git" } || RegisteredSerialize(repository) != descriptor.ExactSelectedRepositoryJson)
            throw new UnauthorizedAccessException("The actual saved project/root/repository differs from this local Git request.");
        var prepared = DeveloperGitCommand.Prepare(new(reference, workspace, project, root, repository), operation);
        if (!prepared.Succeeded || prepared.Value is not { } command || command.DefinitionSha256 != definitionSha256)
            throw new UnauthorizedAccessException("The inspected Git operation differs from the actual saved repository metadata.");
        DemandRegisteredCaptureNative(selection, capture);
        var original = new ManualGit(selection, read, capture, witness, operation, repository, command);
        lock (_gate)
        {
            if (_retiring || _manualExecutions.Count >= 128) throw new InvalidOperationException("Manual process source custody is closed or full.");
            _manualExecutions.Add(original);
        }
        return original;
    });
    public IReadOnlyList<ResourceScope> GetOriginalManualGitScopes(IDeveloperProjectOriginalManualGitBinding binding) => [RequireManualGit(binding).Scope];
    public Task RevalidateOriginalManualGitAsync(IDeveloperProjectOriginalManualGitBinding binding, AuthenticatedResourceActor actor, CancellationToken token)
        => Start(async sources =>
    {
        var original = RequireManualGit(binding);
        if (actor != original.OriginalActor) throw new UnauthorizedAccessException("The original Git actor changed.");
        await RevalidateBody(original.Selection, actor, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => reads.ValidateOriginalAsync(original.Selection, original.Read, token)).ConfigureAwait(false);
        await RevalidateRegisteredCaptureAsync(original.Selection, original.Capture, sources, token).ConfigureAwait(false);
        DemandRegisteredCaptureNative(original.Selection, original.Capture); return true;
    });
    public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalManualGitPinAsync(IDeveloperProjectOriginalManualGitBinding binding, CancellationToken token)
        => AcquireManualExecutionPinCoreAsync(binding, token);
    public void DemandOriginalManualGitPin(IDeveloperProjectOriginalManualGitBinding binding, IDeveloperWorkspaceOriginalExecutionCommitPin pin)
        => DemandManualExecutionPinCore(binding, pin);
    public Task CloseOriginalManualGitPinAsync(IDeveloperProjectOriginalManualGitBinding binding, IDeveloperWorkspaceOriginalExecutionCommitPin pin)
        => CloseManualExecutionPinCoreAsync(binding, pin);
}
