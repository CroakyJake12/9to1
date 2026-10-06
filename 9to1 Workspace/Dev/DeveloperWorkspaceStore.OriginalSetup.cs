using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Dev;

/// <summary>The SAME configured store's additional create-only operation. A native private
/// preparation and actual entry are mandatory; legacy Create/Get/Save do not mint strict ACKs.</summary>
public interface IDeveloperOriginalWorkspaceSetupStore : IDeveloperWorkspaceStore,
    IDeveloperProjectOriginalWorkspaceMetadataStore
{
    Task<DeveloperOriginalWorkspaceSetupResult> CreateOriginalSetupAsync(DeveloperWorkspace workspace,
        IDeveloperProjectOriginalWorkspaceMetadataSource sameNativeSource,
        IDeveloperProjectOriginalWorkspaceMetadataPreparation samePreparation,
        IDeveloperProjectOriginalSetupStepEntry sameEntry, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}

/// <summary>Observations only. The configured issuer must recognise the SAME native
/// Task/result/preparation and its cleanup; no public record issues a setup acknowledgement.</summary>
public sealed record DeveloperOriginalWorkspaceSetupResult(DeveloperWorkspace Workspace,
    IDeveloperProjectOriginalWorkspaceMetadataPreparation OriginalPreparation,
    Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> OriginalNativeWriteTask,
    IDeveloperProjectOriginalWorkspaceMetadataObservation OriginalObservation);

public sealed partial class FileDeveloperWorkspaceStore
{
    public string OriginalWorkspaceMetadataDirectory => _workspaceDirectory;
    public string OriginalWorkspaceMetadataAncestor => Path.GetDirectoryName(
        Path.GetDirectoryName(_workspaceDirectory) ?? throw new InvalidOperationException("No actual Dev metadata parent exists."))
        ?? throw new InvalidOperationException("No bounded actual Dev metadata ancestor exists.");

    public async Task<DeveloperOriginalWorkspaceSetupResult> CreateOriginalSetupAsync(DeveloperWorkspace workspace,
        IDeveloperProjectOriginalWorkspaceMetadataSource sameNativeSource,
        IDeveloperProjectOriginalWorkspaceMetadataPreparation samePreparation,
        IDeveloperProjectOriginalSetupStepEntry sameEntry, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace); ArgumentNullException.ThrowIfNull(sameNativeSource);
        ArgumentNullException.ThrowIfNull(samePreparation); ArgumentNullException.ThrowIfNull(sameEntry);
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        var sources = new OriginalWorkspaceSetupSources(originalSynchronousScope, retainOriginalTask);
        SemaphoreSlim? semaphore = null; bool acquired = false; byte[]? bytes = null;
        Task<IDeveloperProjectOriginalWorkspaceMetadataObservation>? originalWrite = null;
        IDeveloperProjectOriginalWorkspaceMetadataObservation? observation = null;
        var errors = new List<Exception>();
        try
        {
            sources.Invoke(() =>
            {
                if (workspace.Revision != 1 || workspace.ModifiedAt != workspace.CreatedAt || workspace.Validate() is not null ||
                    !sameNativeSource.IsIssuedOriginalWorkspaceMetadataPreparation(samePreparation, this, workspace.WorkspaceId) ||
                    !samePreparation.IsBoundToOriginalStore(this, workspace.WorkspaceId))
                    throw new UnauthorizedAccessException("Retain the actual configured store and source-issued once-created workspace preparation.");
                bytes = JsonSerializer.SerializeToUtf8Bytes(new WorkspaceDocument(CurrentSchemaVersion, workspace), JsonOptions);
                semaphore = _locks.GetOrAdd(workspace.WorkspaceId, static _ => new SemaphoreSlim(1, 1));
            });
            await sources.ObserveVoid(() => semaphore!.WaitAsync(cancellationToken), () => acquired = true).ConfigureAwait(false);
            observation = await sources.Observe(() =>
            {
                originalWrite = samePreparation.CreateOriginalMetadataAsync(bytes!, sameEntry,
                    originalSynchronousScope, retainOriginalTask, cancellationToken);
                return originalWrite;
            }).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                if (!sameNativeSource.IsIssuedOriginalWorkspaceMetadataOutcome(samePreparation, originalWrite!, observation!) ||
                    !System.Text.Encoding.UTF8.GetBytes(observation!.OriginalCommittedDocument).AsSpan().SequenceEqual(bytes))
                    throw new InvalidDataException("The complete original native workspace document/readback does not match this actual store operation.");
            });
        }
        catch (Exception error) { OriginalWorkspaceSetupSources.Add(errors, error); }
        finally
        {
            // Native child close is independent of the write's success. Its actual task is
            // retained before scope exit and joined before a store result can be returned.
            try { await sources.ObserveVoid(samePreparation.CloseAndDrainAsync).ConfigureAwait(false); }
            catch (Exception error) { OriginalWorkspaceSetupSources.Add(errors, error); }
            if (acquired)
                try { sources.Invoke(() => semaphore!.Release()); }
                catch (Exception error) { OriginalWorkspaceSetupSources.Add(errors, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original strict workspace stage/readback/cleanup is failed or unknown; no ACK or automatic repeat.", errors);
        return new DeveloperOriginalWorkspaceSetupResult(workspace, samePreparation,
            originalWrite ?? throw new InvalidOperationException("No actual native write Task was captured."),
            observation ?? throw new InvalidOperationException("No actual native metadata observation exists."));
    }

    private sealed class OriginalWorkspaceSetupSources(Action<Action> scope, Action<Task> retain)
    {
        internal void Invoke(Action callback)
        {
            var thread = Environment.CurrentManagedThreadId; int phase = 1, used = 0;
            try
            {
                scope(() =>
                {
                    if (Volatile.Read(ref phase) == 0 || Environment.CurrentManagedThreadId != thread ||
                        Interlocked.Exchange(ref used, 1) != 0)
                        throw new InvalidOperationException("Original workspace source scope is finite, same-thread and single-use.");
                    callback();
                });
                if (Volatile.Read(ref used) == 0) throw new InvalidOperationException("Original workspace callback was not invoked synchronously.");
            }
            catch (OperationCanceledException original) { throw new AggregateException("Direct original workspace callback failed without a canceled returned Task.", original); }
            finally { Volatile.Write(ref phase, 0); }
        }
        internal async Task<T> Observe<T>(Func<Task<T>> factory)
        {
            Task<T>? actual = null; T result = default!; var errors = new List<Exception>();
            try { Invoke(() => { actual = factory() ?? throw new InvalidOperationException("Original workspace source returned no Task."); retain(actual); }); }
            catch (Exception error) { Add(errors, error); }
            if (actual is not null)
                try { result = await actual.ConfigureAwait(false); }
                catch (Exception error)
                {
                    if (actual.IsCanceled && errors.Count == 0) throw;
                    foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) Add(errors, cause);
                }
            if (errors.Count != 0) throw new AggregateException("Original workspace raw source/enrollment failed; actual returned Task was independently joined.", errors);
            return result;
        }
        internal async Task ObserveVoid(Func<Task> factory, Action? captureSuccess = null)
        {
            Task? actual = null; var errors = new List<Exception>();
            try { Invoke(() => { actual = factory() ?? throw new InvalidOperationException("Original workspace source returned no Task."); retain(actual); }); }
            catch (Exception error) { Add(errors, error); }
            if (actual is not null)
                try { await actual.ConfigureAwait(false); captureSuccess?.Invoke(); }
                catch (Exception error)
                {
                    if (actual.IsCanceled && errors.Count == 0) throw;
                    foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) Add(errors, cause);
                }
            if (errors.Count != 0) throw new AggregateException("Original workspace raw source/enrollment failed; actual returned Task was independently joined.", errors);
        }
        internal static void Add(List<Exception> errors, Exception error)
        { if (!errors.Any(actual => ReferenceEquals(actual, error))) errors.Add(error); }
    }
}
