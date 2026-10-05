namespace NineToOne.Web.Media;

/// <summary>Browser-owned device/persistence boundary, not a Wave model or hosted Files service.</summary>
public interface IWaveBrowserMedia
{
    Task<string> InvokeAsync(string action, string arguments, CancellationToken cancellationToken = default);
    void SetDirty(bool dirty);
    void Release();
}

public sealed class WaveBrowserException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
