namespace NineToOne.Web.Write;

/// <summary>
/// Reads an image from the platform surface that hosts the browser route. The
/// picked file is staged on disk exactly as the host delivered it so the owning
/// attachment store imports those same bytes; the browser never assigns the
/// attachment identity or decides where the document keeps it.
/// </summary>
public interface IWriteBrowserImageSource
{
    /// <summary>Opens the platform's image chooser, or reports null when the writer cancels it.</summary>
    Task<WritePickedImage?> PickImageAsync(CancellationToken cancellationToken);
}

/// <summary>An image the writer picked, staged on disk with the media type the chooser declared.</summary>
public sealed class WritePickedImage(string Name, string MediaType, WriteStagedPackage Staged)
{
    /// <summary>Gets the file name the chooser reported.</summary>
    public string Name { get; } = Name;
    /// <summary>Gets the media type the chooser declared for the picked file.</summary>
    public string MediaType { get; } = string.IsNullOrWhiteSpace(MediaType) ? "image/png" : MediaType;
    /// <summary>Gets the disposable staged copy the owning attachment store imports from.</summary>
    public WriteStagedPackage Staged { get; } = Staged;
    /// <summary>Gets the staged file path, which is only ever a codec staging location.</summary>
    public string StagedPath => Staged.Path;
}
