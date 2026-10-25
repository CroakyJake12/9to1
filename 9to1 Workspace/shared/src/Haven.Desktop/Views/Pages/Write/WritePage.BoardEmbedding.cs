using Avalonia.Threading;
using Haven.UI.Components;
using Haven.UI;

namespace Haven.Desktop.Views.Pages.Write;

public sealed partial class WritePage
{
    private bool? _embeddedReadOnly;

    /// <summary>Applies the embedded view mode to the real Write scene. Document selection,
    /// scrolling and copy remain available; editing surfaces and authored controls are disabled.</summary>
    internal void SetEmbeddedReadOnly(bool readOnly)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_embeddedReadOnly == readOnly) return;
        _embeddedReadOnly = readOnly;
        _route.DocumentSurface.SetReadOnly(readOnly);
        _route.NewButton.SetValue(HavenProperties.Enabled, false);
        _route.ImportButton.SetValue(HavenProperties.Enabled, false);
        _route.ExportButton.SetValue(HavenProperties.Enabled, false);
        _route.PreviousButton.SetValue(HavenProperties.Enabled, false);
        _route.NextButton.SetValue(HavenProperties.Enabled, false);
        foreach (var element in Descendants(_route.Root))
            if (element is Button or Input or Select or Toggle)
            {
                var enabled = !readOnly;
                if (element is Button button && (button == _route.NewButton || button == _route.ImportButton ||
                    button == _route.ExportButton || button == _route.PreviousButton || button == _route.NextButton))
                    enabled = false;
                element.SetValue(HavenProperties.Enabled, enabled);
            }
    }

    private static IEnumerable<HavenElement> Descendants(HavenElement element) =>
        new[] { element }.Concat(element.Children.SelectMany(Descendants));
}
