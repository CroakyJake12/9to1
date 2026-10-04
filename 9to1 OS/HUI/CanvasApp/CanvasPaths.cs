namespace CakeOS.Canvas.App;

/// <summary>Shared document locations. Hosts pass platform roots explicitly.</summary>
public static class CanvasPaths
{
    public static string DefaultDocumentsDir() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CakeOS", "Canvas");
}
