using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Keyboard ownership for this exact native Work tree, not the global shell.</summary>
internal sealed class AssistantWorkspaceKeyboard : IDisposable
{
    private readonly Control _work;
    private readonly AssistantsCuiBindings _bindings;
    private bool _disposed;
    private int _focusGeneration;

    internal AssistantWorkspaceKeyboard(Control work, AssistantsCuiBindings bindings)
    {
        _work = work; _bindings = bindings;
        _work.KeyDown += OnWorkKeyDown;
        _bindings.WorkspaceDisclosureChanged += OnDisclosureChanged;
    }

    private void OnWorkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None ||
            _bindings.VisibleWorkspaceIdentity is not { } identity ||
            e.Source is not Visual source) return;

        var pane = Find<Control>("assistant-workspace");
        var toggle = Find<Button>("assistant-workspace-toggle");
        if (pane is null || !pane.IsEffectivelyVisible || !ReferenceEquals(source, toggle) &&
            !ReferenceEquals(source, pane) && !source.GetVisualAncestors().Contains(pane)) return;

        // Exactly the same synchronous presentation-only owner used by the authored close button.
        _bindings.DispatchAsync("assistants.workspace.close", identity).GetAwaiter().GetResult();
        e.Handled = true;
    }

    private void OnDisclosureChanged(AssistantIdentity identity, bool opened)
    {
        var generation = ++_focusGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            // A route or identity can change between the click and layout. Never steal focus
            // from another Assistant, screen, or an earlier iteration of the disclosure.
            if (_disposed || generation != _focusGeneration || _bindings.WorkRouteIdentity != identity ||
                (_bindings.VisibleWorkspaceIdentity == identity) != opened) return;
            var control = Find<Button>(opened ? "assistant-workspace-close" : "assistant-workspace-toggle");
            if (control is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }) control.Focus();
        }, DispatcherPriority.Input);
    }

    private T? Find<T>(string id) where T : Control =>
        _work.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == id);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _work.KeyDown -= OnWorkKeyDown;
        _bindings.WorkspaceDisclosureChanged -= OnDisclosureChanged;
    }
}
