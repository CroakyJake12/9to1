using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Terminal.NativeUI;

public static class TerminalNativeScene
{
    public static CuiNativeScene Create(TerminalCuiWorkspace workspace, ICuiSceneReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(readiness);
        using var stream = typeof(TerminalNativeScene).Assembly.GetManifestResourceStream("HavenOS.Terminal.NativeUI.UI.TerminalWorkspace.cui")
            ?? throw new InvalidDataException("The owning Terminal CUI resource is missing.");
        using var reader = new StreamReader(stream);
        var registry = new CuiControlRegistry();
        registry.RegisterObjectRenderer("terminal.viewport", _ => workspace.Viewport);
        return new("9to1.Terminal", "Terminal", "Terminal", new CuiRichParser().Parse(reader.ReadToEnd()),
            workspace.Bindings, workspace, readiness) { ControlRegistry = registry };
    }
}
