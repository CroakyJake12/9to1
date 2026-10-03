using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;

namespace HavenOS.Apps.Stacks.NativeUI;

public static class StackNativeScene
{
    public static CuiNativeScene Create(StackCuiController controller, ICuiSceneReadiness authenticatedHomeReadiness)
    {
        using var stream = typeof(StackNativeScene).Assembly.GetManifestResourceStream("HavenOS.Apps.Stacks.NativeUI.UI.Stacks.cui")
            ?? throw new InvalidDataException("The canonical Stacks CUI surface is missing.");
        using var reader = new StreamReader(stream);
        return new("9to1.Stacks", "9to1 Stacks", "Stacks", new CuiRichParser().Parse(reader.ReadToEnd()),
            controller, controller, authenticatedHomeReadiness);
    }
}
