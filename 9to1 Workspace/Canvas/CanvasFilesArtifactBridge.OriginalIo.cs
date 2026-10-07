using System.Runtime.ExceptionServices;

namespace HavenOS.Apps.Canvas;

public sealed partial class CanvasFilesArtifactBridge
{
    private sealed class OriginalIoScope
    { internal CanvasOriginalArtifactFile? Lease; internal CanvasOriginalArtifactFile? BindingLease; }
    private void RetainOriginalBindings(OriginalIoScope scope)
    {
        if (captureOriginalHomeFence is null) return;
        if (string.IsNullOrWhiteSpace(originalFilesRoot))
            throw new UnauthorizedAccessException("The verified original Files workspace root is unavailable.");
        scope.BindingLease = CanvasOriginalArtifactFile.OpenRead(originalFilesRoot,
            Path.Combine(".9to1-files", "bindings.json"));
    }
    private static async Task<T> RunOriginalIoAsync<T>(Func<OriginalIoScope, Task<T>> body)
    {
        var scope = new OriginalIoScope(); var errors = new List<Exception>();
        Task<T>? actual = null; T result = default!; var originalCanceled = false;
        try { actual = body(scope); result = await actual.ConfigureAwait(false); }
        catch (Exception error)
        { originalCanceled = actual?.IsCanceled == true; AddOriginal(errors, (Exception?)actual?.Exception ?? error); }
        foreach (var lease in new[] { scope.Lease, scope.BindingLease }.OfType<CanvasOriginalArtifactFile>())
        {
            Task? cleanup = null;
            try { cleanup = lease.DisposeAsync().AsTask(); await cleanup.ConfigureAwait(false); }
            catch (Exception error)
            { originalCanceled |= cleanup?.IsCanceled == true; AddOriginal(errors, (Exception?)cleanup?.Exception ?? error); }
        }
        if (errors.Count == 1 && (errors[0] is not OperationCanceledException || originalCanceled))
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count != 0) throw new AggregateException("Canvas original Files work and independent byte custody cleanup failed.", errors);
        return result;
    }
}
