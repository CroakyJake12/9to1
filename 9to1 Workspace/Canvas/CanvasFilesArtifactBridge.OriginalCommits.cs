using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Canvas;

/// <summary>Read-only actual Files acknowledgement; never Home completion, permission or replay authority.</summary>
public sealed record CanvasOriginalFilesCommit(HostedItemId FileId, CanvasArtifact Artifact, FilesRevision Revision);

public sealed partial class CanvasFilesArtifactBridge
{
    private sealed class OriginalCommit
    {
        internal readonly object Gate = new();
        internal CanvasOriginalFilesCommit? Acknowledged;
        internal Task? ActualProvider;
    }
    private readonly ConditionalWeakTable<HomeResourceExecutionCapability, OriginalCommit> _originalCommits = new();

    public bool TryGetOriginalCommit(HomeResourceExecutionCapability sameCapability, out CanvasOriginalFilesCommit? observation)
    {
        observation = null;
        if (!_originalCommits.TryGetValue(sameCapability, out var original)) return false;
        lock (original.Gate)
        {
            if (original.Acknowledged is { } held)
                observation = new(held.FileId, CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(held.Artifact)), held.Revision);
        }
        return observation is not null;
    }
    public Task<(HostedItemId FileId, FilesRevision Revision)> CreateOriginalAsync(CanvasArtifact artifact,
        CanvasCreationTarget target, AuthenticatedResourceActor claimedActor,
        HomeResourceExecutionCapability sameCapability, CancellationToken token = default)
        => CreateCoreAsync(artifact, target, claimedActor, token, sameCapability);
    public Task<FilesRevision> SaveOriginalAsync(HostedItemId fileId, CanvasArtifact artifact,
        FilesRevisionId? expectedRevision, AuthenticatedResourceActor claimedActor,
        HomeResourceExecutionCapability sameCapability, CancellationToken token = default)
        => SaveCoreAsync(fileId, artifact, expectedRevision, claimedActor, null, token, sameCapability);
    public Task<FilesRevision> SaveOriginalAsync(HostedItemId fileId, CanvasArtifact artifact,
        FilesRevisionId? expectedRevision, Guid originalStoreId, AuthenticatedResourceActor claimedActor,
        HomeResourceExecutionCapability sameCapability, CancellationToken token = default)
        => SaveCoreAsync(fileId, artifact, expectedRevision, claimedActor, originalStoreId, token, sameCapability);

    private void RequireOriginalCommitSource(HomeResourceExecutionCapability? sameCapability)
    {
        if (captureOriginalHomeFence is not null && sameCapability is null)
            throw new UnauthorizedAccessException("This genuine Canvas owner requires its actual privately claimed Home capability.");
        if (sameCapability is not null && _originalCommits.TryGetValue(sameCapability, out _))
            throw new InvalidOperationException("The original Canvas operation already reached Files; inspect its retained result instead of replaying it.");
    }
    private async Task<FilesResult<FilesRevision>> CommitOriginalFilesAsync(HostedItemId fileId, CanvasArtifact artifact,
        AuthenticatedResourceActor actor, DurableDriveProvider provider, HomeResourceExecutionCapability? capability,
        Func<FilesCommitAuthorityGuard, Task<FilesResult<FilesRevision>>> actualCommit, CancellationToken token)
    {
        HomeClaimedResourceCommitFence? fence = null; Task? source = null;
        FilesResult<FilesRevision>? result = null; var errors = new List<Exception>(); var originalCanceled = false;
        OriginalCommit? original = null;
        try
        {
            var captured = CanvasArtifactCodec.Deserialize(CanvasArtifactCodec.Serialize(artifact));
            FilesCommitAuthorityGuard guard;
            if (captureOriginalHomeFence is not null)
            {
                if (capability is null) throw new UnauthorizedAccessException("The original Home claim is unavailable.");
                var capture = captureOriginalHomeFence(actor, provider, capability, token).AsTask(); source = capture;
                fence = await capture.ConfigureAwait(false);
                guard = new(actor.ActorId, fence.ValidateAsync);
            }
            else guard = captureCommitAuthority is null
                ? new(actor.ActorId, async cancel => await actors.GetCurrentAsync(cancel).ConfigureAwait(false) == actor && hostAllowsWrites())
                : await captureCommitAuthority(actor, provider, token).ConfigureAwait(false);
            if (capability is not null)
            {
                original = new();
                try { _originalCommits.Add(capability, original); }
                catch (ArgumentException) { throw new InvalidOperationException("The SAME Canvas capability cannot dispatch another Files commit."); }
            }
            var actual = actualCommit(guard); source = actual;
            if (original is not null) lock (original.Gate) original.ActualProvider = actual;
            try { result = await actual.ConfigureAwait(false); }
            catch when (actual.IsFaulted) { throw actual.Exception!; }
            if (result.IsSuccess && original is not null)
            {
                // The provider returned its actual ACK. Preserve it BEFORE cleanup/audit/reopen.
                lock (original.Gate) original.Acknowledged = new(fileId, captured, result.Value!);
            }
        }
        catch (Exception error) { originalCanceled = source?.IsCanceled == true; AddOriginal(errors, (Exception?)source?.Exception ?? error); }
        finally
        {
            Task? close = null;
            try { if (fence is not null) { close = fence.DisposeAsync().AsTask(); await close.ConfigureAwait(false); } }
            catch (Exception error) { originalCanceled |= close?.IsCanceled == true; AddOriginal(errors, (Exception?)close?.Exception ?? error); }
        }
        if (errors.Count == 1 && (errors[0] is not OperationCanceledException || originalCanceled)) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 0) throw new AggregateException("Original Canvas Files dispatch and independent Home-fence cleanup failed.", errors);
        return result ?? throw new InvalidDataException("Files supplied no original Canvas commit result.");
    }
    private static void AddOriginal(List<Exception> errors, Exception error)
    {
        if (error is AggregateException group) foreach (var child in group.InnerExceptions) AddOriginal(errors, child);
        else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
    }
}
