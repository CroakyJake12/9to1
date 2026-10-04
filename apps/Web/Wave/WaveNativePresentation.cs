using Avalonia;
using Avalonia.Controls;
using CakeOS.Cui.Runtime;

namespace NineToOne.Web.Wave;

/// <summary>Wave-only text allocation through the maintained native CUI registry.</summary>
public static class WaveNativePresentation
{
    public const string CaptionComponentName = "WaveCaption";
    public const string TextComponentName = "WaveText";

    public static CuiControlRegistry CreateControlRegistry()
    {
        var registry = new CuiControlRegistry();
        // Recorded native font1/2 ink exceeded advance-based own allocations by
        // up to 0.469 horizontal and 0.563 vertical units. One native unit on
        // each side allocates inside the real TextBlock clip; keep clipping and
        // shaping unchanged. The full native/browser checks still decide whether
        // this is sufficient. These factories are scoped to this Wave loader.
        registry.RegisterControlType(CaptionComponentName,
            _ => new TextBlock { Padding = new Thickness(1, 1) });
        // TextBlock is a reserved CUI type and cannot be overridden. WaveText
        // preserves the actual native type and all authored label properties;
        // only its vertical allocation changes, leaving wrap width unchanged.
        registry.RegisterControlType(TextComponentName,
            _ => new TextBlock { Padding = new Thickness(0, 1) });
        return registry;
    }
}
