using Avalonia.Threading;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Write;

public sealed partial class WritePage
{
    private bool _embeddedReadOnly;

    /// <summary>Applies the embedded view mode to the real Write scene. Document selection,
    /// scrolling and copy remain available; editing surfaces and authored controls are disabled.</summary>
    internal void SetEmbeddedReadOnly(bool readOnly)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_embeddedReadOnly == readOnly) return;
        _embeddedReadOnly = readOnly;
        _route.DocumentSurface.SetReadOnly(readOnly);
        foreach (var element in Descendants(_route.Root))
            if (element is Button or Input or Select or Toggle)
                element.SetValue(HavenProperties.Enabled, !readOnly);
    }

    private static IEnumerable<HavenElement> Descendants(HavenElement element) =>
        new[] { element }.Concat(element.Children.SelectMany(Descendants));
}
