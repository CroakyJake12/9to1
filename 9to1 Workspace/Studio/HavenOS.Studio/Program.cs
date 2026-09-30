using CakeOS.Cui.Runtime;

namespace HavenOS.AIStudio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The installed authenticated Home bridge must supply canonical authoring
        // services. A separate Studio process may not create private Home/storage.
        return CuiNativeHost.Run(StudioNativeScene.CreateUnavailable(),args);
    }
}
