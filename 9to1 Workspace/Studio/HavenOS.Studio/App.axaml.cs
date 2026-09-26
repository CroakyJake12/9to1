using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace HavenOS.AIStudio;

public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "9to1", "AIStudio");
            var api = new AIStudioApi(new JsonStudioProjectStore(dataPath), new UnavailableStudioRuntimeAdapter(),
                new UnavailableCanonicalAgentBuilderAdapter());
            desktop.MainWindow = new MainWindow(api);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
