using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home.Apps;

/// <summary>Layout observed by the authenticated actual root endpoint after verifying its
/// existing enrolled publisher policy, exact installed receipt and complete activated payload.
/// Implementations and these detached paths/hashes are never installation authority.</summary>
public interface IHomePackageOriginalActivatedLayoutEvidence
{
    string OriginalInstallationRoot { get; }
    string OriginalSignedReceiptPath { get; }
    string OriginalCompletePayloadPath { get; }
    string OriginalAppEntrypointRelativePath { get; }
    IReadOnlyList<HomePackageActivatedFile> OriginalActivatedFiles { get; }
}
public sealed record HomePackageActivatedFile(string RelativePath, long Bytes, string Sha256);

/// <summary>Versioned internal observation in SAME HomePackageDatabase entry. It adds no
/// package database, key trust, installer model, installed peer or launch authority.</summary>
public sealed record HomePackageOriginalInstalledActivationRecord(int SchemaVersion, string PackageId,
    string AppId, string Version, string Platform, string Abi, string CatalogueRevision,
    string SignedDescriptorBase64, string DescriptorPayloadBase64, string InstallationRoot,
    string SignedReceiptPath, string CompletePayloadPath, string EntrypointRelativePath,
    IReadOnlyList<HomePackageActivatedFile> ActivatedFiles, string OriginalRootOperationId)
{
    public const string Field = "home.original-installed-activation.v1";
    internal static HomePackageDatabaseEntry CaptureFromOriginalRoot(HomePackageOriginalRootRequest request,
        HomePackageOriginalRootOutcome outcome, HomePackageDatabaseEntry package, HomePackageActionResult result)
    {
        if (result.State != HomePackageOperationState.Succeeded || package.InstallationState != HomePackageInstallState.Installed ||
            request.Action.Action is not (HomePackageAction.Install or HomePackageAction.Update or HomePackageAction.Repair or HomePackageAction.SelectVersion or HomePackageAction.Rollback))
            return package;
        if (outcome.OriginalRootOutcomeEvidence is not IHomePackageOriginalActivatedLayoutEvidence layout)
        {
            // Never retain an old activation record after a new install without current
            // source-owned layout. Missing evidence yields unavailable to real readers.
            var missing = package.UnknownFields is null ? null : new Dictionary<string, JsonElement>(package.UnknownFields, StringComparer.Ordinal);
            missing?.Remove(Field); return package with { UnknownFields = missing };
        }
        var descriptor = request.Artifact;
        return CloudflareOriginalExecutionGuard.InvokeOriginal(request, () =>
        {
            var files = layout.OriginalActivatedFiles.Take(4097).Select(value => value with { }).ToArray();
            if (files.Length is < 1 or > 4096 || files.Select(value => value.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length ||
                files.Any(value => !SafeRelative(value.RelativePath) || value.Bytes is < 0 or > 8L * 1024 * 1024 * 1024 || !HomePackageArtifactSelection.Sha256(value.Sha256)))
                throw new InvalidDataException("The actual activated complete file cohort is invalid or unbounded.");
            var root = Path.GetFullPath(layout.OriginalInstallationRoot);
            if (!SafeRelative(layout.OriginalAppEntrypointRelativePath) ||
                !files.Any(value => StringComparer.OrdinalIgnoreCase.Equals(value.RelativePath, layout.OriginalAppEntrypointRelativePath)))
                throw new InvalidDataException("The actual entrypoint must belong to the complete activated cohort.");
            var observed = new HomePackageOriginalInstalledActivationRecord(1, descriptor.PackageId, descriptor.AppId, descriptor.Version,
                descriptor.Platform, descriptor.Abi, request.CatalogueRevision, Convert.ToBase64String(request.CopySignedDescriptor().Span),
                Convert.ToBase64String(request.CopyDescriptorPayload().Span), root, Path.GetFullPath(layout.OriginalSignedReceiptPath),
                Path.GetFullPath(layout.OriginalCompletePayloadPath), layout.OriginalAppEntrypointRelativePath,
                Array.AsReadOnly(files), request.OperationId);
            var fields = package.UnknownFields is null ? new Dictionary<string, JsonElement>(StringComparer.Ordinal) : new(package.UnknownFields, StringComparer.Ordinal);
            fields[Field] = JsonSerializer.SerializeToElement(observed); return package with { UnknownFields = fields };
        });
    }
    public static bool SafeRelative(string value) => value is { Length: > 0 and <= 1024 } &&
        !Path.IsPathRooted(value) && !value.Contains(':') && !value.Contains('\\') &&
        value.Split('/').All(segment => segment.Length != 0 && segment is not "." and not ".." && !segment.EndsWith('.') && !segment.EndsWith(' '));
}

public sealed partial class HomePackageOriginalDeviceOwner
{
    // Reference composition only; these methods issue no root or installation authority.
    public bool HasOriginalActivationComposition(IHomeCoreStateStore store, IHomePackageOriginalArtifactProvider artifacts,
        IHomePackageOriginalRootMutationPort root) => ReferenceEquals(_store, store) && ReferenceEquals(_artifacts, artifacts) && ReferenceEquals(_root, root);
    public bool IsOriginalRetainedRootMutation(IHomePackageOriginalRootMutation actual)
    {
        lock (_gate) return !_closing && _operations.Values.Any(value => ReferenceEquals(value.Mutation, actual) &&
            value.OriginalOpen?.IsCompletedSuccessfully == true &&
            value.OriginalRootClose is null && ReferenceEquals(value.OriginalOpen.GetAwaiter().GetResult(), actual));
    }
    private readonly Dictionary<Task, HomeOwnershipOriginalSourceCallbacks> _activationReads = new(ReferenceEqualityComparer.Instance);
    public Task DemandOriginalActivationChannelWithinSourceAsync(IHomePackageOriginalRootMutation sameActual,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var publication = new List<Exception>();
        var callbacks = new HomeOwnershipOriginalSourceCallbacks(body => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
            () => { scope(body); return true; }), retain);
        Task actual; TaskCompletionSource start;
        lock (_gate)
        {
            // Exact healthy finite observation tasks are metadata, not unresolved
            // package/root effects. Never probe Close or drop failed/pending children.
            foreach (var settled in _activationReads.Where(value => value.Key.IsCompletedSuccessfully && value.Value.Errors.Length == 0).Select(value => value.Key).ToArray())
            { _activationReads.Remove(settled); _originalTasks.Remove(settled); }
            RequireAdmission();
            if (!IsOriginalRetainedRootMutation(sameActual)) throw new UnauthorizedAccessException("An actual currently retained root channel from SAME device owner is required.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DemandPublished(start.Task); _originalTasks.Add(actual); _activationReads.Add(actual, callbacks);
        }
        try { callbacks.Run(() => retain(actual)); }
        catch (Exception cause) { publication.Add(cause); }
        finally { start.SetResult(); }
        return actual;
        async Task DemandPublished(Task gate)
        {
            await gate.ConfigureAwait(false);
            using var original = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (publication.Count != 0) throw new AggregateException("Actual activation read publication failed.", publication);
            await callbacks.ReadAsync(DemandRaw).ConfigureAwait(false);
            if (!IsOriginalRetainedRootMutation(sameActual)) throw new UnauthorizedAccessException("The actual root channel retired during installed observation.");
        }
        async Task<bool> DemandRaw()
        {
            var raw = sameActual.DemandOriginalChannelCurrentAsync(token);
            callbacks.Retain(raw); await raw.ConfigureAwait(false); return true;
        }
    }
}
