namespace NineToOne.Web;

/// <summary>Retires mounted CUI input/focus handlers before their owning view lease.</summary>
internal sealed class AttachedSurfaceLifetime(IDisposable attachment, IDisposable? owner) : IDisposable
{
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { attachment.Dispose(); } catch (Exception error) { (failures ??= []).Add(error); }
        try { owner?.Dispose(); } catch (Exception error) { (failures ??= []).Add(error); }
        if (failures is not null) throw new AggregateException("The browser surface could not fully detach.", failures);
    }
}
