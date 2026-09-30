using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Controls;

/// <summary>The canonical retained scene projects the owning route's shared AI session.</summary>
public sealed class ContextualAiCuiSurface : UserControl, IDisposable
{
    private readonly FloatingAiBarState _state;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiViewModel _model = new();
    private readonly CuiSceneHost _host = new();
    private readonly CancellationTokenSource _lifetime = new();
    private TextBox? _editor;
    private string? _appId;
    private string? _surface;
    private bool _refreshing;
    private bool _disposed;

    public ContextualAiCuiSurface(FloatingAiBarState state, ICuiSceneReadiness readiness)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _state.Changed += StateChanged;
        _model.PropertyChanged += BindingChanged;
        Content = _host;
        Refresh();
    }

    public async Task<CuiSceneAvailability> InitializeAsync(string appId, string surface,
        CancellationToken cancellationToken = default)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        _appId = appId;
        _surface = surface;
        if (_editor is not null) _editor.KeyDown -= EditorKeyDown;
        _editor = null;
        var document = new CuiRichParser().Parse(FloatingAiBarScene.ReadSource());
        var available = await _host.ShowAsync(new(appId, "AI assistant", surface, document,
            _model, new Actions(this), _readiness), cancellationToken);
        if (available.State == CuiSceneAvailabilityState.Ready)
        {
            _editor = this.GetLogicalDescendants().OfType<TextBox>().Single();
            _editor.KeyDown += EditorKeyDown;
            await _state.RefreshModelsAsync(cancellationToken);
            Refresh();
        }
        return available;
    }

    private void EditorKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || args.KeyModifiers != KeyModifiers.None || _disposed) return;
        args.Handled = true;
        _ = SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        try
        {
            if (!await RequireCurrentReadinessAsync(_lifetime.Token)) return;
            if (_editor is not null) _state.Prompt = _editor.Text ?? string.Empty;
            await _state.SubmitAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async ValueTask<bool> RequireCurrentReadinessAsync(CancellationToken cancellationToken)
    {
        var availability = await _readiness.CheckAsync(cancellationToken);
        if (availability.State == CuiSceneAvailabilityState.Ready) return true;
        _state.Cancel();
        if (_editor is not null) _editor.KeyDown -= EditorKeyDown;
        _editor = null;
        // Replace previously authorised content with the actual failed handshake result.
        await _host.ShowAsync(new(_appId!, "AI assistant", _surface!,
            new CuiRichParser().Parse(FloatingAiBarScene.ReadSource()), _model, new Actions(this),
            new DeniedReadiness(availability)), cancellationToken);
        return false;
    }

    private sealed class DeniedReadiness(CuiSceneAvailability actualFailure) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(actualFailure with { State = CuiSceneAvailabilityState.Unavailable });
    }

    private void BindingChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_refreshing && !_disposed && args.PropertyName == "Prompt")
            _state.Prompt = _model.Get("Prompt") as string ?? string.Empty;
    }

    private void StateChanged(object? sender, EventArgs args)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        if (_disposed) return;
        _refreshing = true;
        try
        {
            _model.Set("AccessModeLabel", _state.AccessModeLabel);
            _model.Set("ModelPickerLabel", _state.ModelPickerLabel);
            _model.Set("ModelPickerAvailable", _state.ModelPickerAvailable);
            _model.Set("IsCollapsed", _state.Mode == FloatingAiBarMode.Collapsed);
            _model.Set("IsExpanded", _state.Mode != FloatingAiBarMode.Collapsed);
            _model.Set("IsStreaming", _state.Mode == FloatingAiBarMode.Streaming);
            _model.Set("HasError", !string.IsNullOrWhiteSpace(_state.Error));
            _model.Set("ContextLabel", _state.ContextLabel ?? string.Empty);
            _model.Set("RequestStateLabel", _state.RequestStateLabel);
            _model.Set("Prompt", _state.Prompt);
            _model.Set("Response", _state.Response);
            _model.Set("Error", _state.Error ?? string.Empty);
        }
        finally { _refreshing = false; }
    }

    private sealed class Actions(ContextualAiCuiSurface owner) : ICuiActionDispatcher
    {
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(owner._disposed, owner);
            cancellationToken.ThrowIfCancellationRequested();
            switch (command)
            {
                case "SetReadOnly": owner._state.SetReadOnly(); break;
                case "SetWriteMode": owner._state.SetWriteMode(); break;
                case "Expand": owner._state.Expand(); break;
                case "Collapse": owner._state.Collapse(); break;
                case "Cancel": owner._state.Cancel(); break;
                case "SelectNextModel": await owner._state.SelectNextModelAsync(cancellationToken); break;
                case "Submit":
                    if (!await owner.RequireCurrentReadinessAsync(cancellationToken)) break;
                    if (owner._editor is not null) owner._state.Prompt = owner._editor.Text ?? string.Empty;
                    await owner._state.SubmitAsync(cancellationToken);
                    break;
                default: throw new InvalidOperationException("The shared AI scene requested an undeclared UI command.");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _state.Changed -= StateChanged;
        _model.PropertyChanged -= BindingChanged;
        if (_editor is not null) _editor.KeyDown -= EditorKeyDown;
        _host.Dispose();
        _lifetime.Dispose();
    }
}
