using Haven.Application;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

/// <summary>Supplied only by the original native host. An available handler is an
/// affordance, not read permission; preparation must admit the exact issued row.</summary>
public interface IFilesNativeBoardOpenHost
{
    bool IsAvailable { get; }
    Task<FilesNativeBoardOpenPreparation> PrepareAsync(FilesNativeBrowserPage page,
        HostedItemMetadata row, AuthenticatedResourceActor actor,
        Action<Action> originalScope, Action<Task> retainOriginalTask, CancellationToken token);
}

/// <summary>A producer-classified read refusal can be displayed without inventing
/// a document or treating another failure as a successful read.</summary>
public sealed record FilesNativeBoardOpenPreparation(IFilesNativePreparedBoardOpen? Prepared, string? Refusal);

/// <summary>The exact prepared revision is private to its original host. A successful
/// queue transfers cleanup to that host; it waits outside the sending Files action.</summary>
public interface IFilesNativePreparedBoardOpen : IAsyncDisposable
{
    bool QueueAfter(Task originalSourceSettlement);
}
