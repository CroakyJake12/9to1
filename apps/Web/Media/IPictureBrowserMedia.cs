namespace NineToOne.Web.Media;

/// <summary>Browser device I/O only; PictureDocument and PictureCropService own editable state and pixels.</summary>
public interface IPictureBrowserMedia
{
    Task<string> InvokeAsync(string action, string arguments, CancellationToken cancellationToken = default);
    void SetDirty(bool dirty);
    void Release();
}
