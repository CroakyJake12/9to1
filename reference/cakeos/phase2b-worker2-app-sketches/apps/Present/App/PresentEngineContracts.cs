namespace HavenOS.Apps.Present;

public sealed record PresentSlideInfo(int Index, string Name, string Hash);
public sealed record PresentSlideExtent(long WidthTwips, long HeightTwips);
public sealed record PresentElementSnapshot(int SlideIndex, int ObjectIndex, IReadOnlyList<string> Text);
public sealed record PresentTileRequest(int SlideIndex, int PixelWidth, int PixelHeight, int TileXTwips, int TileYTwips, int TileWidthTwips, int TileHeightTwips);
public sealed record PresentRenderedTile(int PixelWidth, int PixelHeight, PixelFormat PixelFormat, IReadOnlyList<byte> Pixels);
public sealed record PresentEngineEvent(int UpstreamType, string Payload);

public enum PixelFormat
{
    Rgba = 0,
    Bgra = 1
}

public interface IPresentEngine : IAsyncDisposable
{
    void Open(string documentPathOrUrl);
    void Close();
    bool IsOpen();
    IReadOnlyList<PresentSlideInfo> GetSlides();
    int CurrentSlide();
    void SetCurrentSlide(int slideIndex);
    PresentSlideExtent GetSlideExtent(int slideIndex);
    bool SupportsElementSnapshots();
    IReadOnlyList<PresentElementSnapshot> GetElementSnapshot(string documentPathOrUrl, int slideIndex);
    void SelectElement(string snapshotPathOrUrl, int slideIndex, int objectIndex);
    void ClearElementSelection(int slideIndex, int objectIndex);
    bool ReplaceElementText(string snapshotPathOrUrl, int slideIndex, int objectIndex, string text);
    void AddSlideAfter(int slideIndex);
    void DuplicateSlide(int slideIndex);
    void DeleteSlide(int slideIndex);
    void MoveSlide(int fromIndex, int toIndex);
    void Undo();
    void Redo();
    PresentRenderedTile RenderTile(PresentTileRequest request);
    void PostKeyEvent(KeyEventType type, int charCode, int keyCode);
    void PostMouseEvent(MouseEventType type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers);
    void PostUnoCommand(string command, string jsonArguments = "{}", bool notifyWhenFinished = false);
    void SaveAs(string destinationPathOrUrl, string format = "", string filterOptions = "");
    void SetEventCallback(Action<PresentEngineEvent> callback);
    static string PathToFileUrl(string pathOrUrl);
}

public enum KeyEventType
{
    Input = 0,
    Up = 1
}

public enum MouseEventType
{
    ButtonDown = 0,
    ButtonUp = 1,
    Move = 2
}

public sealed class PresentAppService(IPresentEngine engine)
{
    public IPresentEngine Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
}