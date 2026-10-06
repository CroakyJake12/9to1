using System.Text.Json;

namespace HavenOS.Apps.Dev;

/// <summary>Optional original read custody on the SAME saved store. This issues no binding,
/// immutable history, permission or setup acknowledgement.</summary>
public interface IDeveloperOriginalWorkspaceSourceReadStore
{
    Task<DeveloperOperationResult<DeveloperWorkspace>> GetWithinOriginalSourceAsync(Guid workspaceId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken);
}

public sealed partial class FileDeveloperWorkspaceStore : IDeveloperOriginalWorkspaceSourceReadStore
{
    public async Task<DeveloperOperationResult<DeveloperWorkspace>> GetWithinOriginalSourceAsync(Guid workspaceId,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalSynchronousScope); ArgumentNullException.ThrowIfNull(retainOriginalTask);
        FileStream? stream = null; JsonDocument? document = null; Task<JsonDocument>? parse = null; Task? close = null;
        var callbackFailed = false; var errors = new List<Exception>(); DeveloperOperationResult<DeveloperWorkspace>? result = null;
        void Record(Exception error)
        {
            if (error is AggregateException group) foreach (var cause in group.InnerExceptions) Record(cause);
            else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error);
        }
        void Invoke(Action body)
        {
            var gate = new object(); var causes = new List<Exception>(); var phase = 1; var used = 0;
            var thread = Environment.CurrentManagedThreadId;
            void Capture(Exception error) { lock (gate) if (!causes.Any(value => ReferenceEquals(value, error))) causes.Add(error); }
            try
            {
                originalSynchronousScope(() =>
                {
                    if (Volatile.Read(ref phase) == 0 || Environment.CurrentManagedThreadId != thread ||
                        Interlocked.CompareExchange(ref used, 1, 0) != 0)
                    {
                        var refusal = new InvalidOperationException("Original workspace read callback is expired, repeated or foreign-thread.");
                        Capture(refusal); throw refusal;
                    }
                    try { body(); } catch (Exception error) { Capture(error); throw; }
                });
            }
            catch (Exception error) { Capture(error); }
            finally { Volatile.Write(ref phase, 0); }
            if (Volatile.Read(ref used) == 0) Capture(new InvalidOperationException("Original workspace read callback was not invoked."));
            Exception[] snapshot; lock (gate) snapshot = causes.ToArray();
            if (snapshot.Length != 0) { callbackFailed = true; throw new AggregateException("Original workspace read factory failed.", snapshot); }
        }
        try
        {
            Invoke(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workspaceId == Guid.Empty) throw new ArgumentException("Select the original workspace ID.");
                stream = new FileStream(GetWorkspacePath(workspaceId), FileMode.Open, FileAccess.Read, FileShare.Read,
                    16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                parse = JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                retainOriginalTask(parse);
            });
        }
        catch (Exception error) { Record(error); }
        if (parse is not null)
            try { document = await parse.ConfigureAwait(false); }
            catch (Exception error) { if (parse.IsFaulted) foreach (var cause in parse.Exception!.InnerExceptions) Record(cause); else Record(error); }
        try
        {
            if (errors.Count == 0 && document is not null)
                Invoke(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var version = ReadSchemaVersion(document.RootElement);
                    var envelope = version == CurrentSchemaVersion ? document.RootElement.Deserialize<WorkspaceDocument>(JsonOptions) : null;
                    result = version != CurrentSchemaVersion
                        ? DeveloperOperationResult<DeveloperWorkspace>.Failure(version < 0 ? DeveloperOperationErrorCode.InvalidStoredData : DeveloperOperationErrorCode.UnsupportedSchemaVersion,
                            "The current original workspace schema is unavailable.", workspaceId.ToString("D"))
                        : envelope?.Workspace is not { } workspace || workspace.WorkspaceId != workspaceId || workspace.Validate() is not null
                            ? DeveloperOperationResult<DeveloperWorkspace>.Failure(DeveloperOperationErrorCode.InvalidStoredData,
                                "The current original workspace identity/data is invalid.", workspaceId.ToString("D"))
                            : DeveloperOperationResult<DeveloperWorkspace>.Success(workspace);
                });
        }
        catch (Exception error) { Record(error); }
        finally
        {
            try { document?.Dispose(); } catch (Exception error) { Record(error); }
            if (stream is not null)
                try { Invoke(() => { close = stream.DisposeAsync().AsTask(); retainOriginalTask(close); }); }
                catch (Exception error) { Record(error); }
            if (stream is not null && close is null)
            {
                // A refusing caller cannot strand this privately acquired FileStream.
                // Owed close has no caller-supplied product/factory and grants no read.
                try { close = stream.DisposeAsync().AsTask(); retainOriginalTask(close); }
                catch (Exception error) { Record(error); }
            }
            if (close is not null)
                try { await close.ConfigureAwait(false); }
                catch (Exception error) { if (close.IsFaulted) foreach (var cause in close.Exception!.InnerExceptions) Record(cause); else Record(error); }
        }
        if (errors.Count != 0)
        {
            if (parse?.IsCanceled == true && !callbackFailed && (close is null || close.IsCompletedSuccessfully) && errors.All(value => value is OperationCanceledException))
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
            throw new AggregateException("Original workspace read/cleanup did not settle cleanly.", errors);
        }
        return result ?? throw new InvalidOperationException("No current original workspace was observed.");
    }
}
