using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;

namespace CakeOS.Cui.Runtime;

/// <summary>Shared primitive styling uses the nearest CUI theme scope, including
/// explicit non-default themes. Apps supply content and layout, not a second control palette.</summary>
public sealed class CuiComponentStyles : Styles
{
    public CuiComponentStyles()
    {
        // The backend finds templates here; every ordinary presentation value
        // is supplied by CuiPropertyPresentation through the CUI cascade.
        CuiControlTemplates.Populate(Resources);
    }
}
