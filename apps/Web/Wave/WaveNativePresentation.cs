using Avalonia;
using Avalonia.Controls;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Wave;

/// <summary>Wave-only caption allocation through the maintained native CUI registry.</summary>
public static class WaveNativePresentation
{
    public const string CaptionComponentName = "WaveCaption";

    public static CuiControlRegistry CreateControlRegistry()
    {
        var registry = new CuiControlRegistry();
        // Recorded native font1 captions have conservative ink overhangs up to
        // 0.469 units beyond their advance-based allocation. Allocate one native
        // unit on each side inside the TextBlock's real clip; keep ClipToBounds
        // unchanged. Font1/font2 and the full native/browser checks still decide
        // whether this allocation is sufficient for every authored caption.
        registry.RegisterControlType(CaptionComponentName,
            _ => new TextBlock { Padding = new Thickness(1, 0) });
        return registry;
    }
}
