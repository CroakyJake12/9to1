using HavenOS.Files;
namespace HavenOS.Apps.Canvas;
/// <summary>Owning composition retains its final authority through the actual Files metadata transaction.</summary>
public sealed class CanvasFilesFinalAuthority : IAsyncDisposable
{
    public FilesCommitAuthorityGuard Guard {get;}
    private readonly IAsyncDisposable _lifetime;
    private readonly Func<CancellationToken,ValueTask<bool>>? _originalPredicate;
    public CanvasFilesFinalAuthority(FilesCommitAuthorityGuard guard,IAsyncDisposable lifetime)
    { Guard=guard??throw new ArgumentNullException(nameof(guard));_lifetime=lifetime??throw new ArgumentNullException(nameof(lifetime)); }
    public CanvasFilesFinalAuthority(string actorId,Func<CancellationToken,ValueTask<bool>> originalPredicate,IAsyncDisposable lifetime)
    {
        ArgumentNullException.ThrowIfNull(originalPredicate);
        _originalPredicate=originalPredicate;Guard=new(actorId,originalPredicate);
        _lifetime=lifetime??throw new ArgumentNullException(nameof(lifetime));
    }
    internal ValueTask<bool> ValidateOriginalAsync(CancellationToken ct)
        =>_originalPredicate is null?ValueTask.FromException<bool>(new NotSupportedException("Original final predicate composition unavailable.")):_originalPredicate(ct);
    public ValueTask DisposeAsync()=>_lifetime.DisposeAsync();
}
