using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace HavenOS.Apps.Data.Native;

public sealed class DataApplication : Application
{
    private DataAppHost? _host;
    private Task? _startup;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var originalTheme = new Uri("avares://Haven/Styles/DefaultTheme.axaml");
        Styles.Add(new StyleInclude(originalTheme) { Source = originalTheme });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Window { Title = "Data", Width = 1440, Height = 900,
                MinWidth = 1024, MinHeight = 640 };
            var allowClose = false; var closing = false;
            window.Opened += async (_, _) =>
            {
                if (_startup is not null) return;
                // Publish the actual start task before its page/render callbacks.
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _startup = StartAsync(start.Task); start.SetResult();
                try { await _startup; }
                catch (Exception error)
                {
                    System.Diagnostics.Trace.TraceError("Data startup failed: {0}", error);
                    window.Content = new TextBlock { Text = "Data could not open its local workbooks: " + error.Message,
                        Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                }
            };
            async Task StartAsync(Task start)
            {
                await start;
                _host = DataAppHost.CreateDefault();
                await _host.InitializeAsync();
                window.Content = _host.Page;
            }
            window.Closing += async (_, args) =>
            {
                if (allowClose) return;
                args.Cancel = true;
                if (closing) return;
                closing = true;
                try
                {
                    var startupFailed = false;
                    if (_startup is not null)
                        try { await _startup; } catch { startupFailed = true; } // Original error remains in its task and trace.
                    if (_host is not null)
                    {
                        if (!startupFailed && !await _host.TrySaveBeforeCloseAsync()) return;
                        await _host.DisposeAsync();
                        _host = null;
                    }
                    allowClose = true; window.Close();
                }
                catch (Exception error)
                {
                    System.Diagnostics.Trace.TraceError("Data close retained its local owner: {0}", error);
                    if (_host?.IsDisposed == true)
                    {
                        _host = null; allowClose = true; window.Close();
                    }
                }
                finally { closing = false; }
            };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
