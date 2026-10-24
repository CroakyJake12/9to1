using Haven.Application;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Views.Pages.Write;

internal sealed partial class WordWriteHavenScene
{
    internal Container DrawingHost { get; } = new() { Name = "Write.Document.DrawingHost", Layout = HavenLayout.Canvas };
    internal WriteDocumentEditor? SharedInkEditor => _editor;
    internal event EventHandler? DrawingRequested;
    internal event EventHandler? PresentationChanged;
    private bool _sharedInkConfigured;
    internal NativeHost DrawingToolsHost { get; } = CreateDrawingToolsHost();
    private static NativeHost CreateDrawingToolsHost()
    {
        var host = new NativeHost { Name = "Write.Drawing.Tools" };
        host.Accessibility.AccessibleName = "Shared drawing tools";
        host.SetValue(HavenProperties.Width, HavenLength.Px(800));
        host.SetValue(HavenProperties.Height, HavenLength.Px(104));
        host.SetValue(HavenProperties.Clip, true);
        return host;
    }

    internal void ConfigureSharedInk(bool configured)
    {
        _sharedInkConfigured = configured;
        RebuildRibbon();
    }

    internal void SelectSharedInk(Guid blockId)
    {
        if (_editor is null) return;
        _editor.SelectBlock(blockId);
        OnDocumentSelectionChangedCore(this, EventArgs.Empty);
    }

    internal bool InsertSharedInkDrawing()
    {
        if (_editor is null) return false;
        _editor.InsertSharedInk();
        RebuildAll();
        return true;
    }

    private void AddSharedInkInsertion()
    {
        var drawing = Btn("Write.Insert.Drawing", "Drawing");
        drawing.Accessibility.Description = _sharedInkConfigured
            ? "Insert a shared drawing when the native drawing engine is available."
            : "Shared drawing is unavailable because a compatible Home drawing engine is not connected.";
        drawing.SetValue(HavenProperties.Enabled, _sharedInkConfigured);
        drawing.Invoked += (_, _) => OwnOriginalCallback(() => DrawingRequested?.Invoke(this, EventArgs.Empty));
        RibbonContent.Add(drawing);
    }

    private void AddSharedInkControls()
    {
        if (_editor?.SelectedBlock?.SharedProductivityBundles is null) return;
        // Composition, tools, labels and selected state are owned by WriteSharedInk.cui.
        // This is only its existing native mounting boundary within the Write chrome.
        DrawingToolsHost.Parent?.Remove(DrawingToolsHost);
        RibbonContent.Add(DrawingToolsHost);
    }
}
