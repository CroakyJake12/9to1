using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using NineToOne.Web;

namespace Haven.Desktop.Services;

/// <summary>One native editor and preview catalogue per Settings view. Only the host's admitted
/// appearance action can publish its draft into the canonical Home preference.</summary>
internal sealed class NativeHomeThemeCreator : IDisposable
{
    private readonly CuiControlLoader _loader = new();
    private readonly CuiThemePreferenceContext _context;
    private readonly BrowserThemeCreator _editor;
    private readonly CuiViewModel _host;
    private readonly Func<bool> _canEdit;
    private Button? _trigger;
    private readonly ContentControl _mount = new UserControl();
    private bool _open, _disposed;
    private bool _showInCategory = true;
    private Task? _retirement;

    internal NativeHomeThemeCreator(NativeHomeThemePreferences source, CuiViewModel host,
        Func<bool> canEdit, Func<object, CancellationToken, Task> apply)
    {
        _host = host;
        _canEdit = canEdit;
        var catalog = new CuiThemeDefinitionCatalog();
        _context = new(source, catalog);
        var bindings = new CuiViewModel();
        _editor = new(bindings, bindings, source, catalog, canEdit,
            (theme, opening, token) => source.SaveThemeDraftAsync(theme, opening, apply, token));
        _loader.SetThemePreferenceContext(_context);
        _loader.SetBindingContext(_editor);
        _loader.SetActionDispatcher(_editor);
        _editor.PropertyChanged += SyncOpen;
        _host.Set("HomeThemeCreatorOpen", false);
        _host.Set("HomeThemeCreationAvailable", true);
        SetCategoryPresentation(true);
    }

    internal void Register(CuiControlLoader outer)
    {
        outer.BindAuthoredControl("home-create-theme", _ => _trigger = new Button());
        outer.BindAuthoredControl("home-theme-creator-host", _ => _mount);
    }

    internal void Open()
    {
        if (_disposed || !_canEdit()) return;
        if (_mount.Content is null)
        {
            const string markup = """
                <Cui>
                  <Import Source="framework/CUI/Components/ThemeCreator.cui" />
                  <Page AccessibleName="Create theme"><ThemeCreatorPanel /></Page>
                </Cui>
                """;
            var doc = CuiThemeCreatorComponents.Compose(new CuiRichParser().Parse(markup, "NativeThemeCreator.cui"));
            var result = _loader.TryLoad(doc);
            if (result.Root is null || result.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("Home theme editor could not open: " + string.Join("; ", result.Diagnostics));
            _loader.WireBindings(result.Root);
            result.Root.KeyDown += OnEditorKey;
            _mount.Content = result.Root;
        }
        _editor.Open();
    }

    private void OnEditorKey(object? sender, KeyEventArgs args)
    {
        if (!args.Handled && args.Key == Key.Escape && _editor.IsActionAvailable("ThemeCreatorCancel") == true)
        {
            args.Handled = true;
            _editor.DispatchAsync("ThemeCreatorCancel", null).GetAwaiter().GetResult();
        }
    }

    internal void SetCategoryPresentation(bool show)
    {
        _showInCategory = show;
        _host.Set("HomeThemeCreatorVisible", _open && show);
        _host.Set("HomeThemeGalleryVisible", !_open || !show);
    }

    private void SyncOpen(object? sender, PropertyChangedEventArgs args)
    {
        if (!_editor.TryGetValue("ThemeCreatorOpen", out var value) || _open == (value is true)) return;
        _open = value is true;
        _host.Set("HomeThemeCreatorOpen", _open);
        SetCategoryPresentation(_showInCategory);
        if (!_open && !_disposed)
            Dispatcher.UIThread.Post(() => { if (!_open && !_disposed && _trigger?.IsEffectivelyVisible == true) _trigger.Focus(NavigationMethod.Tab); },
                DispatcherPriority.Loaded);
    }

    internal Task WhenIdleAsync() => _loader.WhenActionsIdleAsync();
    internal Task CloseAndDrainAsync() => _retirement ??= RetireAsync();

    private async Task RetireAsync()
    {
        try { await WhenIdleAsync(); }
        finally
        {
            if (Dispatcher.UIThread.CheckAccess()) Dispose();
            else await Dispatcher.UIThread.InvokeAsync(Dispose);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _editor.PropertyChanged -= SyncOpen;
        if (_mount.Content is Control root) root.KeyDown -= OnEditorKey;
        _loader.Dispose();
        _editor.Dispose();
        _context.Dispose();
        _mount.Content = null;
    }
}
