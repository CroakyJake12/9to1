using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Forms;

namespace Haven.Desktop.Views.Pages.Forms;

/// <summary>Owns one actual CUI loader and awaits each child window until its native lifetime ends.
/// The supplied origin check is a host fence; canonical services still own final operation admission.</summary>
public sealed class FormsNativeWorkspaceHost : ContentControl, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<CancellationToken, Task> _requireOriginal;
    private readonly Func<Window?> _owner;
    private readonly Action _revokeOrigin;
    private readonly HashSet<Window> _children = [];
    private CuiControlLoader? _loader;
    private bool _disposed;

    public FormsNativeWorkspaceHost(Func<CancellationToken, Task> requireOriginal, Func<Window?> owner, Action revokeOrigin)
    {
        _requireOriginal = requireOriginal;
        _owner = owner;
        _revokeOrigin = revokeOrigin;
    }

    public async Task RequireCurrentAsync(CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _requireOriginal(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
    }

    public async Task MountAsync(FormsCuiWorkspace workspace, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _requireOriginal(linked.Token);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loader is not null) throw new InvalidOperationException("The Forms workspace is already mounted.");
        var candidate = new CuiControlLoader();
        try
        {
            candidate.SetBindingContext(workspace);
            candidate.SetActionDispatcher(new OriginDispatcher(this, workspace));
            var loaded = candidate.TryLoad(FormsCuiWorkspace.LoadDocument());
            RequireRoot(loaded.Root, loaded.Diagnostics);
            await _requireOriginal(linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            Content = loaded.Root;
            _loader = candidate;
        }
        catch { candidate.Dispose(); throw; }
    }

    public Task ShowPreviewAsync(FormNativePreview preview, CancellationToken token) =>
        ShowAsync("Form preview", preview, preview, preview.Register, preview.CreateDocument(), token);

    public Task ShowResponseAsync(FormNativeResponseSurface response, CancellationToken token) =>
        ShowAsync("Form response", response, response, response.Register, response.CreateDocument(), token);

    private async Task ShowAsync(string title, ICuiBindingContext bindings, ICuiActionDispatcher actions,
        Action<CuiControlRegistry> register, CuiDocument document, CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _requireOriginal(linked.Token);
        var owner = _owner() ?? throw new InvalidOperationException("Forms requires a live native owner window.");
        var registry = new CuiControlRegistry();
        register(registry);
        using var loader = new CuiControlLoader(registry);
        loader.SetBindingContext(bindings);
        loader.SetActionDispatcher(new OriginDispatcher(this, actions));
        var loaded = loader.TryLoad(document);
        RequireRoot(loaded.Root, loaded.Diagnostics);
        await _requireOriginal(linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        var window = new Window { Title = title, Width = 760, Height = 680,
            Content = new ScrollViewer { Content = loaded.Root } };
        _children.Add(window);
        using var cancellation = linked.Token.Register(() => Dispatcher.UIThread.Post(window.Close));
        try
        {
            // The workspace owns the response surface until this task ends. Returning at Show()
            // would dispose its controls while the respondent is still interacting with them.
            await window.ShowDialog(owner);
            linked.Token.ThrowIfCancellationRequested();
            await _requireOriginal(linked.Token);
        }
        finally { _children.Remove(window); window.Close(); window.Content = null; }
    }

    private sealed class OriginDispatcher(FormsNativeWorkspaceHost host, ICuiActionDispatcher inner)
        : ICuiActionDispatcher, ICuiActionAvailability
    {
        public bool? IsActionAvailable(string command) => !host._disposed
            && (inner is not ICuiActionAvailability availability || availability.IsActionAvailable(command) == true);
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(host._disposed, host);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, host._lifetime.Token);
            await host.RequireCurrentAsync(linked.Token);
            await inner.DispatchAsync(command, parameter, linked.Token);
            // This is display freshness, not an assertion that a completed owner mutation rolled back.
            await host.RequireCurrentAsync(linked.Token);
        }
    }

    private static void RequireRoot(Control? root, IReadOnlyList<CuiDiagnostic> diagnostics)
    {
        if (root is null || diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The actual Forms CUI could not be mounted.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _revokeOrigin(); // The owner actor adapter denies final admission even for already in-flight calls.
        _lifetime.Cancel();
        foreach (var child in _children.ToArray()) child.Close();
        Content = null;
        _loader?.Dispose();
        _loader = null;
        _lifetime.Dispose();
    }
}
