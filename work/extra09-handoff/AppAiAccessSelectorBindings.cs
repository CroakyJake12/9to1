using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Cui.AI;

namespace Haven.Desktop.Views.Pages.Chat;

/// <summary>
/// PROPOSED production wiring (host layer) — not part of framework/CUI/AI.
///
/// Bridges the reusable <see cref="AppAiAccessSelector"/> (typed adapter over the existing
/// <see cref="FloatingAiBarState"/>) into an authored CUI scene, so <c>AiAccessSelector.cui</c>
/// can be mounted by a host (the shared floating bar and/or the shared composer) with the SAME
/// live AI access-mode state.
///
/// This file belongs in the host project that already references CakeOS.Cui.Runtime (for example
/// Haven.Desktop). The AI framework project (NineToOne.Cui.AI) intentionally does NOT depend on
/// the CUI runtime, so this thin bridge keeps that boundary clean.
///
/// All reads reflect live state; all selections delegate to the real authority; there is no
/// second access-mode cache and no invented approval-policy semantics (DEC-001 is unresolved).
/// </summary>
public sealed class AppAiAccessSelectorBindings :
    ICuiWritableBindingContext,
    ICuiActionDispatcher,
    ICuiActionAvailability,
    IDisposable
{
    private readonly AppAiAccessSelector _selector;

    public AppAiAccessSelectorBindings(AppAiAccessSelector selector)
    {
        _selector = selector ?? throw new ArgumentNullException(nameof(selector));
    }

    public AppAiAccessSelector Selector => _selector;

    public bool TryGetValue(string path, out object? value)
    {
        value = null;
        switch (path)
        {
            case "Access.CurrentModeLabel":
                value = _selector.CurrentModeLabel;
                return true;
            case "Access.IsReadOnly":
                value = _selector.IsReadOnly;
                return true;
            case "Access.IsWriteMode":
                value = _selector.IsWriteMode;
                return true;
            case "Access.CanChangeMode":
                value = _selector.CanChangeMode;
                return true;
            case "Access.HasStatus":
                value = _selector.HasStatus;
                return true;
            case "Access.StatusMessage":
                value = _selector.Status?.Message ?? string.Empty;
                return true;
            default:
                return false;
        }
    }

    public bool TrySetValue(string path, object? value) => false;

    public bool? IsActionAvailable(string command) => command switch
    {
        "access.select-readonly" or "access.select-write" => _selector.CanChangeMode,
        _ => null
    };

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (command)
        {
            case "access.select-readonly":
                _selector.SelectReadOnly();
                return ValueTask.CompletedTask;
            case "access.select-write":
                _selector.SelectWrite();
                return ValueTask.CompletedTask;
            default:
                return ValueTask.FromException(new InvalidOperationException(
                    $"AI access selector CUI command '{command}' is not owned by the shared selector."));
        }
    }

    public void Dispose() => _selector.Dispose();
}
