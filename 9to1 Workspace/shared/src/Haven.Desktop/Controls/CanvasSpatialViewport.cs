using CakeOS.Cui.Runtime;
using HavenOS.Apps.Canvas;

namespace Haven.Desktop.Controls;

/// <summary>Desktop compatibility facade for the owning Canvas viewport and renderer.</summary>
public sealed class CanvasSpatialViewport(CanvasRnoteDocument document, ICuiSceneReadiness readiness)
    : CanvasNativeViewport(document, readiness)
{
}
