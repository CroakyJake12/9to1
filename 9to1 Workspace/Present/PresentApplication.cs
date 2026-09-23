using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace HavenOS.Apps.Present;

public sealed class PresentApplication : Application
{
    private PresentAppHost? _host;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _host = PresentAppHost.CreateDefault();

            var window = new Window
            {
                Title = "Present",
                Width = 1440,
                Height = 900,
                MinWidth = 1024,
                MinHeight = 640,
                Content = _host.Page
            };

            var closeAfterSave = false;
            var closeSavePending = false;
            window.Closing += async (_, args) =>
            {
                if (closeAfterSave || _host is null)
                {
                    return;
                }

                args.Cancel = true;
                if (closeSavePending)
                {
                    return;
                }

                closeSavePending = true;
                try
                {
                    if (await _host.TrySaveBeforeCloseAsync())
                    {
                        closeAfterSave = true;
                        window.Close();
                    }
                }
                finally
                {
                    closeSavePending = false;
                }
            };

            window.Closed += (_, _) =>
            {
                _host?.Dispose();
                _host = null;
            };

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
