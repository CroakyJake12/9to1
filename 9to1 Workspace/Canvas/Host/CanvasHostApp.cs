namespace HavenOS.Apps.Canvas;

/// <summary>Thin executable adapter to the original Home bootstrap and CUI workspace.
/// Canvas borrows the same Home/Files services and native retirement graph as a Haven tab.</summary>
public static class CanvasHostApp
{
    public static void RunOriginal(string[] args) => Haven.Desktop.App.RunOriginalCanvasDesktop(args);
}
