using Avalonia;

namespace HavenOS.Apps.Assistants.Host;

/// <summary>Owning executable entry point over the maintained Desktop startup/lifetime.</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        global::Haven.Desktop.App.RunOriginalDesktop(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<global::Haven.Desktop.App>()
            .UsePlatformDetect()
            .LogToTrace();
}
