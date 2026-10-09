using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace HavenOS.Apps.Write;

public sealed class WriteApplication : Application
{
    private WriteAppHost? _host;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        var theme = new Uri("avares://Haven/Styles/DefaultTheme.axaml");
        Styles.Add(new StyleInclude(theme) { Source = theme });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Window
            {
                Title = "Write", Width = 1440, Height = 900,
                MinWidth = 1024, MinHeight = 640
            };
            var closeAfterSave = false;
            var closePending = false;
            var startupPending = true;
            window.Opened += async (_, _) =>
            {
                try
                {
                    _host = await WriteAppHost.CreateDefaultAsync();
                    window.Content = _host.Page;
                    await _host.InitializeAsync();
                }
                catch (Exception error)
                {
                    window.Content = new TextBlock
                    {
                        Text = "Write could not open its local services: " + error.Message,
                        Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    };
                }
                finally { startupPending = false; }
            };
            window.Closing += async (_, args) =>
            {
                if (closeAfterSave) return;
                args.Cancel = true;
                if (startupPending || closePending) return;
                if (_host is null) { closeAfterSave = true; window.Close(); return; }
                closePending = true;
                try
                {
                    if (!await _host.TrySaveBeforeCloseAsync()) return;
                    await _host.DisposeAsync();
                    _host = null;
                    closeAfterSave = true;
                    window.Close();
                }
                catch (Exception error)
                {
                    // Retain the editor when its owner cannot complete close preparation.
                    System.Diagnostics.Trace.TraceError("Write close failed: {0}", error);
                    if (_host?.IsDisposed == true)
                    {
                        // Storage preparation succeeded and all cleanup owners
                        // were awaited. Preserve cleanup errors in the trace;
                        // this terminal host cannot be used as a live editor.
                        _host = null;
                        closeAfterSave = true;
                        window.Close();
                    }
                }
                finally { closePending = false; }
            };
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
