using System.Runtime.CompilerServices;
using Avalonia.Controls;

namespace CakeOS.Cui.Runtime;

/// <summary>Stored CUI participation, visual and input gates remain independent.</summary>
public static class CuiLifecycleProperties
{
    private static readonly ConditionalWeakTable<Control, State> States = new();
    public static void Attach(Control control) => _ = States.GetValue(control, value => new(value));
    public static void Detach(Control control)
    { if (States.TryGetValue(control, out var state)) state.Dispose(); States.Remove(control); }
    /// <summary>Independent deny-only host availability. Releasing a denial restores the current authored gates.</summary>
    public static IDisposable RestrictInteraction(Control control) => States.GetValue(control, value => new(value)).Restrict();
    public static bool SetLiteral(Control control, string name, string value, CuiPropertyLevel level, string source)
    {
        if (name is not ("Active" or "Hidden" or "Interactive")) return false;
        Attach(control);
        return CuiPropertyPresentation.Set(control, name, bool.Parse(value), level, source);
    }
    private sealed class State
    {
        private const string ActiveGate = "CUI effective Active gate";
        private const string HiddenGate = "CUI Hidden visual gate";
        private const string InputGate = "CUI Interactive input gate";
        private const string CapabilityGate = "CUI owner capability restriction";
        private readonly Control _control;
        private int _denials;
        private bool _interactive = true, _disposed;
        public IDisposable Restrict()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(State));
            _denials++; ApplyInput(); return new Denial(this);
        }
        private void Release() { if (_disposed) return; _denials--; ApplyInput(); }
        public void Dispose() => _disposed = true;
        private void ApplyInput()
        {
            if (_disposed) return;
            var restricted = !_interactive || _denials > 0;
            if (restricted)
            {
                CuiPropertyPresentation.Set(_control, "IsHitTestVisible", false, CuiPropertyLevel.Overlay, InputGate, long.MaxValue);
                CuiPropertyPresentation.Set(_control, "Focusable", false, CuiPropertyLevel.Overlay, InputGate, long.MaxValue);
                CuiPropertyPresentation.Set(_control, "IsEnabled", false, CuiPropertyLevel.Overlay, InputGate, long.MaxValue);
            }
            else
            {
                CuiPropertyPresentation.Remove(_control, "IsHitTestVisible", CuiPropertyLevel.Overlay, InputGate);
                CuiPropertyPresentation.Remove(_control, "Focusable", CuiPropertyLevel.Overlay, InputGate);
                CuiPropertyPresentation.Remove(_control, "IsEnabled", CuiPropertyLevel.Overlay, InputGate);
            }
            if (_denials > 0) CuiPropertyPresentation.Set(_control, "IsEnabled", false, CuiPropertyLevel.Overlay, CapabilityGate, long.MaxValue);
            else CuiPropertyPresentation.Remove(_control, "IsEnabled", CuiPropertyLevel.Overlay, CapabilityGate);
        }
        private sealed class Denial(State state) : IDisposable
        { private bool _released; public void Dispose() { if (_released) return; _released = true; state.Release(); } }
        public State(Control control)
        {
            _control = control;
            CuiPropertyPresentation.Set(control, "IsEnabled", control.IsEnabled, CuiPropertyLevel.BuiltIn, "CUI built-in enabled state");
            CuiPropertyPresentation.Set(control, "IsVisible", control.IsVisible, CuiPropertyLevel.BuiltIn, "CUI built-in visibility");
            CuiPropertyPresentation.Set(control, "IsHitTestVisible", control.IsHitTestVisible, CuiPropertyLevel.BuiltIn, "CUI built-in input");
            CuiPropertyPresentation.Set(control, "Focusable", control.Focusable, CuiPropertyLevel.BuiltIn, "CUI built-in focus");
            CuiPropertyPresentation.Set(control, "Opacity", control.Opacity, CuiPropertyLevel.BuiltIn, "CUI built-in opacity");
            CuiPropertyPresentation.Register(control, "Active", typeof(bool), true, value =>
            {
                if (value is false) CuiPropertyPresentation.Set(control, "IsVisible", false, CuiPropertyLevel.Overlay, ActiveGate, long.MaxValue);
                else CuiPropertyPresentation.Remove(control, "IsVisible", CuiPropertyLevel.Overlay, ActiveGate);
            });
            CuiPropertyPresentation.Register(control, "Hidden", typeof(bool), false, value =>
            {
                if (value is true) CuiPropertyPresentation.Set(control, "Opacity", 0d, CuiPropertyLevel.Overlay, HiddenGate, long.MaxValue);
                else CuiPropertyPresentation.Remove(control, "Opacity", CuiPropertyLevel.Overlay, HiddenGate);
            });
            CuiPropertyPresentation.Register(control, "Interactive", typeof(bool), true, value =>
            {
                _interactive = value is true; ApplyInput();
            });
        }
    }
}
