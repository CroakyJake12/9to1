using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;

namespace HavenOS.AIStudio;

public sealed partial class App : Application
{
    public override void Initialize() { } // Presentation resources are owned by the CUI scene.

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
