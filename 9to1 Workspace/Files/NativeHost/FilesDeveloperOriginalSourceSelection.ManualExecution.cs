using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalSourceSelection : IDeveloperProjectOriginalManualExecutionSource
{
    private readonly HashSet<ManualExecution> _manualExecutions = [];
    private sealed class ManualExecution(Selection selection, IDeveloperProjectOriginalReadAdmission read,
        IDeveloperProjectOriginalExistingSourceCapture capture, RegisteredWitness witness, DeveloperRunTargetChoice choice)
        : IDeveloperProjectOriginalManualExecutionBinding
    {
        internal Selection Selection => selection;
        internal IDeveloperProjectOriginalReadAdmission Read => read;
        internal IDeveloperProjectOriginalExistingSourceCapture Capture => capture;
        internal RegisteredWitness Witness => witness;
        internal ResourceScope Scope { get; } = new("dev.project.console", "dev-manual-run:" + Guid.NewGuid().ToString("D"),
            witness.Read.OriginalWorkspaceDocumentSha256 + ":" + witness.Read.OriginalRegisteredRootFingerprint + ":" + choice.DefinitionSha256, ResourceAccess.Execute);
        internal readonly Dictionary<IDeveloperWorkspaceOriginalExecutionCommitPin, Task> Pins = new(ReferenceEqualityComparer.Instance);
        public string CanonicalRoot => selection.Root.OriginalProjectRoot;
        public AuthenticatedResourceActor OriginalActor => selection.Workspace.Actor;
        public string ConfigurationId => choice.Configuration.Id;
        public long ConfigurationRevision => choice.Configuration.Revision;
        public string DefinitionSha256 => choice.DefinitionSha256;
        public string Command => choice.Configuration.Command!;
        public int TimeoutSeconds => choice.Configuration.TimeoutSeconds ?? 120;
    }
    private ManualExecution RequireManual(IDeveloperProjectOriginalManualExecutionBinding binding)
    {
        lock (_gate) return !_retiring && binding is ManualExecution original && _manualExecutions.Contains(original) &&
            IsIssuedOriginal(original.Capture) && IsIssuedOriginal(original.Selection)
            ? original : throw new UnauthorizedAccessException("SAME live Files manual execution issuance required.");
    }
    public bool IsIssuedOriginalManualExecution(IDeveloperProjectOriginalManualExecutionBinding binding)
    {
        lock (_gate) return !_retiring && binding is ManualExecution original && _manualExecutions.Contains(original) &&
            IsIssuedOriginal(original.Capture) && IsIssuedOriginal(original.Selection);
    }
    public Task<IDeveloperProjectOriginalManualExecutionBinding> CaptureOriginalManualExecutionAsync(
        IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalReadAdmission read,
        IDeveloperProjectOriginalExistingSourceCapture capture, string configurationId, long configurationRevision,
        string definitionSha256, CancellationToken token) => Start<IDeveloperProjectOriginalManualExecutionBinding>(async sources =>
    {
        var selection = Require(value);
        if (selection.Registered is not { } descriptor || !IsIssuedOriginal(capture))
            throw new UnauthorizedAccessException("Manual Run requires the genuine registered saved-project capture.");
        RegisteredWitness witness;
        lock (_gate)
            if (!_registeredCaptures.TryGetValue(capture, out witness!) || !ReferenceEquals(witness.Selection, selection))
                throw new UnauthorizedAccessException("Manual Run capture belongs to another selected project.");
        await RevalidateBody(selection, selection.Workspace.Actor, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => reads.ValidateOriginalAsync(selection, read, token)).ConfigureAwait(false);
        await RevalidateRegisteredCaptureAsync(selection, capture, sources, token).ConfigureAwait(false);
        // The declaration comes from the actual READ-authorized native document, never caller DTOs.
        using var document = JsonDocument.Parse(witness.Read.OriginalWorkspaceDocument);
        var workspace = document.RootElement.GetProperty("workspace").Deserialize<DeveloperWorkspace>(RegisteredJson)
            ?? throw new InvalidDataException("The saved workspace document has no workspace.");
        var reference = JsonSerializer.Deserialize<DeveloperProjectReference>(descriptor.ExactProjectReferenceJson, RegisteredJson)
            ?? throw new InvalidDataException("The saved selection reference is missing.");
        var project = workspace.Projects.Single(x => x.ProjectId == descriptor.ProjectId);
        var root = workspace.Roots.Single(x => x.RootId == descriptor.RootId);
        var repository = descriptor.RepositoryBindingId is { } id ? workspace.SourceControlBindings.Single(x => x.BindingId == id) : null;
        if (workspace.WorkspaceId != descriptor.WorkspaceId || workspace.Revision != descriptor.WorkspaceRevision ||
            RegisteredSerialize(project) != descriptor.ExactSelectedProjectJson || RegisteredSerialize(root) != descriptor.ExactSelectedRootJson ||
            (repository is null ? null : RegisteredSerialize(repository)) != descriptor.ExactSelectedRepositoryJson)
            throw new UnauthorizedAccessException("The native saved project no longer matches the exact selected records.");
        var observed = DeveloperRunConfigurationSelection.Observe(new(reference, workspace, project, root, repository));
        var choice = observed.Value?.SingleOrDefault(x => x.Configuration.Id == configurationId &&
            x.Configuration.Revision == configurationRevision && x.DefinitionSha256 == definitionSha256)
            ?? throw new UnauthorizedAccessException("The reviewed Run declaration differs from the actual saved metadata.");
        if (choice.UnavailableReason is { } reason) throw new NotSupportedException(reason);
        DemandRegisteredCaptureNative(selection, capture);
        var original = new ManualExecution(selection, read, capture, witness, choice);
        lock (_gate)
        {
            if (_retiring || _manualExecutions.Count >= 128) throw new InvalidOperationException("Manual Run source custody is closed or full.");
            _manualExecutions.Add(original);
        }
        return original;
    });
    public IReadOnlyList<ResourceScope> GetOriginalManualExecutionScopes(IDeveloperProjectOriginalManualExecutionBinding binding)
        => [RequireManual(binding).Scope];
    public Task RevalidateOriginalManualExecutionAsync(IDeveloperProjectOriginalManualExecutionBinding binding,
        AuthenticatedResourceActor actor, CancellationToken token) => Start(async sources =>
    {
        var original = RequireManual(binding);
        if (actor != original.OriginalActor) throw new UnauthorizedAccessException("The manual Run actor changed.");
        await RevalidateBody(original.Selection, actor, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => reads.ValidateOriginalAsync(original.Selection, original.Read, token)).ConfigureAwait(false);
        await RevalidateRegisteredCaptureAsync(original.Selection, original.Capture, sources, token).ConfigureAwait(false);
        DemandRegisteredCaptureNative(original.Selection, original.Capture);
        return true;
    });
    public Task<IDeveloperWorkspaceOriginalExecutionCommitPin> AcquireOriginalManualExecutionPinAsync(
        IDeveloperProjectOriginalManualExecutionBinding binding, CancellationToken token)
        => Start<IDeveloperWorkspaceOriginalExecutionCommitPin>(async sources =>
    {
        var original = RequireManual(binding);
        await RevalidateBody(original.Selection, original.OriginalActor, sources, token).ConfigureAwait(false);
        await sources.ObserveVoid(() => reads.ValidateOriginalAsync(original.Selection, original.Read, token)).ConfigureAwait(false);
        var native = original.Witness.Native;
        Task<IDeveloperOriginalCurrentProjectNativeRead>? acquisition = null;
        IDeveloperOriginalCurrentProjectNativeRead? retained = null;
        try
        {
            var pin = await sources.ObserveProduct(() => acquisition = native.CaptureRegisteredOriginalWithinSourceAsync(original.Selection,
                original.Read, sources.OriginalSynchronousScope, sources.RetainOriginalTask, token), actual =>
            {
                if (acquisition is null || !native.IsOwnedRegisteredOriginalRead(original.Selection, acquisition, actual))
                    throw new UnauthorizedAccessException("The original native execution pin has no private historical custody.");
                retained = actual;
                lock (_gate) original.Pins.Add(actual, acquisition);
            }).ConfigureAwait(false);
            if (acquisition is null || !native.IsIssuedRegisteredOriginalRead(original.Selection, acquisition, pin))
                throw new UnauthorizedAccessException("The original native execution pin was not privately issued.");
            if (pin.OriginalWorkspaceDocumentSha256 != original.Witness.Read.OriginalWorkspaceDocumentSha256 ||
                pin.OriginalRegisteredRootFingerprint != original.Witness.Read.OriginalRegisteredRootFingerprint)
                throw new UnauthorizedAccessException("The saved declaration, registration or root changed during Run review.");
            DemandOriginalManualExecutionPin(original, pin); return pin;
        }
        catch (Exception error)
        {
            // A callback can refuse after the native acquisition actually succeeded. Close the
            // SAME late product before this operation settles; the global native issuer is borrowed.
            if (retained is not null)
                try { await sources.ObserveVoid(() => CloseOriginalManualExecutionPinAsync(original, retained)).ConfigureAwait(false); }
                catch (Exception close) { throw new AggregateException("Manual native pin acquisition and independent late cleanup failed.", error, close); }
            throw;
        }
    });
    public void DemandOriginalManualExecutionPin(IDeveloperProjectOriginalManualExecutionBinding binding,
        IDeveloperWorkspaceOriginalExecutionCommitPin pin)
    {
        var original = RequireManual(binding); Task acquisition;
        lock (_gate)
            if (!original.Pins.TryGetValue(pin, out acquisition!)) throw new UnauthorizedAccessException("Foreign manual execution pin.");
        if (pin is not IDeveloperOriginalCurrentProjectNativeRead read ||
            !original.Witness.Native.IsIssuedRegisteredOriginalRead(original.Selection, acquisition, read))
            throw new UnauthorizedAccessException("The original manual execution pin retired.");
        read.DemandOriginalExecutionBinding();
    }
    public Task CloseOriginalManualExecutionPinAsync(IDeveloperProjectOriginalManualExecutionBinding binding,
        IDeveloperWorkspaceOriginalExecutionCommitPin pin) => Start(async sources =>
    {
        // Historical private ownership permits cleanup after issuer retirement; eligibility does not.
        ManualExecution original; Task acquisition;
        lock (_gate)
        {
            original = binding is ManualExecution owned && _manualExecutions.Contains(owned) ? owned
                : throw new UnauthorizedAccessException("Foreign manual execution binding close.");
            if (!original.Pins.TryGetValue(pin, out acquisition!)) throw new UnauthorizedAccessException("Foreign manual execution pin close.");
        }
        if (pin is not IDeveloperOriginalCurrentProjectNativeRead read ||
            !original.Witness.Native.IsOwnedRegisteredOriginalRead(original.Selection, acquisition, read))
            throw new UnauthorizedAccessException("No private native pin cleanup custody exists.");
        Task? close = null;
        await sources.ObserveVoid(() => close = read.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (close is null || !original.Witness.Native.IsClosedRegisteredOriginalRead(original.Selection, acquisition, read, close))
            throw new InvalidOperationException("The genuine manual execution native pin close is unverified.");
        return true;
    });
}
