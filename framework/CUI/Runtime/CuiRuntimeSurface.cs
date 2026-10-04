using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Applies CUI documents to a live content host. Candidate trees are fully built
/// before the current tree is replaced, so a failed load leaves the last good UI intact.
/// </summary>
public sealed class CuiRuntimeSurface
{
    private readonly ContentControl _host;
    private readonly CuiControlRegistry _registry;
    private readonly ICuiBindingContext? _bindingContext;
    private readonly ICuiActionDispatcher? _actionDispatcher;
    private CuiControlLoader? _activeLoader;

    public CuiRuntimeSurface(
        ContentControl host,
        CuiControlRegistry? registry = null,
        ICuiBindingContext? bindingContext = null,
        ICuiActionDispatcher? actionDispatcher = null)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _registry = registry ?? CuiControlRegistry.Default;
        _bindingContext = bindingContext;
        _actionDispatcher = actionDispatcher;
    }

    public Control? CurrentRoot => _host.Content as Control;

    public bool TryApply(CuiDocument document, out IReadOnlyList<CuiDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        var version = document.RootProperties.TryGetValue("version", out var versionValue)
            ? versionValue is CuiLiteralValue literal ? literal.Value : null
            : null;
        if (!CuiRuntimeCompatibility.IsLanguageVersionCompatible(version))
        {
            diagnostics =
            [
                new CuiDiagnostic(
                    "CUIR020", CuiDiagnosticSeverity.Error,
                    $"CUI document language version '{version}' is not supported by runtime version {CuiRuntimeCompatibility.LanguageVersion}.",
                    document.Span)
            ];
            return false;
        }

        var candidateLoader = new CuiControlLoader(_registry);
        if (_bindingContext is not null)
            candidateLoader.SetBindingContext(_bindingContext);
        if (_actionDispatcher is not null)
            candidateLoader.SetActionDispatcher(_actionDispatcher);

        var (candidate, loadDiagnostics) = candidateLoader.TryLoad(document);
        diagnostics = loadDiagnostics;
        if (candidate is null || loadDiagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
        {
            candidateLoader.Dispose();
            return false;
        }

        var previous = CurrentRoot;
        var states = CaptureStates(previous);
        RestoreStates(candidate, states);
        _host.Content = candidate;
        _activeLoader?.Dispose();
        _activeLoader = candidateLoader;
        RestoreFocus(candidate, states);
        return true;
    }

    private static Dictionary<string, ControlState> CaptureStates(Control? root)
    {
        var states = new Dictionary<string, ControlState>(StringComparer.Ordinal);
        if (root is null)
            return states;

        foreach (var control in EnumerateControls(root))
        {
            var id = CuiRuntimeIdentity.GetStableId(control);
            if (id is null || states.ContainsKey(id))
                continue;
            states.Add(id, ControlState.Capture(control));
        }
        return states;
    }

    private static void RestoreStates(Control? root, IReadOnlyDictionary<string, ControlState> states)
    {
        if (root is null)
            return;

        foreach (var control in EnumerateControls(root))
        {
            var id = CuiRuntimeIdentity.GetStableId(control);
            if (id is not null && states.TryGetValue(id, out var state))
                state.Apply(control);
        }
    }

    private static void RestoreFocus(Control? root, IReadOnlyDictionary<string, ControlState> states)
    {
        if (root is null)
            return;

        foreach (var control in EnumerateControls(root))
        {
            var id = CuiRuntimeIdentity.GetStableId(control);
            if (id is not null && states.TryGetValue(id, out var state) && state.WasFocused)
            {
                if (Dispatcher.UIThread.CheckAccess())
                    control.Focus();
                else
                    Dispatcher.UIThread.Post(() => control.Focus());
                return;
            }
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        yield return root;
        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children.OfType<Control>())
                foreach (var descendant in EnumerateControls(child))
                    yield return descendant;
                break;
            case Decorator { Child: Control child }:
                foreach (var descendant in EnumerateControls(child))
                    yield return descendant;
                break;
            case ContentControl { Content: Control child }:
                foreach (var descendant in EnumerateControls(child))
                    yield return descendant;
                break;
            case ItemsControl items:
                foreach (var child in items.Items.OfType<Control>())
                foreach (var descendant in EnumerateControls(child))
                    yield return descendant;
                break;
        }
    }

    private sealed record ControlState(
        Type ControlType,
        string? Text,
        int SelectionStart,
        int SelectionEnd,
        int CaretIndex,
        bool? IsChecked,
        double? SliderValue,
        decimal? NumericValue,
        Avalonia.Vector? ScrollOffset,
        bool WasFocused)
    {
        public static ControlState Capture(Control control) => new(
            control.GetType(),
            (control as TextBox)?.Text,
            (control as TextBox)?.SelectionStart ?? 0,
            (control as TextBox)?.SelectionEnd ?? 0,
            (control as TextBox)?.CaretIndex ?? 0,
            (control as CheckBox)?.IsChecked,
            (control as Slider)?.Value,
            (control as NumericUpDown)?.Value,
            (control as ScrollViewer)?.Offset,
            control.IsFocused);

        public void Apply(Control control)
        {
            if (control.GetType() != ControlType)
                return;

            if (control is TextBox textBox && Text is not null)
            {
                textBox.Text = Text;
                textBox.SelectionStart = Math.Clamp(SelectionStart, 0, textBox.Text?.Length ?? 0);
                textBox.SelectionEnd = Math.Clamp(SelectionEnd, textBox.SelectionStart, textBox.Text?.Length ?? 0);
                textBox.CaretIndex = Math.Clamp(CaretIndex, 0, textBox.Text?.Length ?? 0);
            }
            if (control is CheckBox checkBox && IsChecked is not null)
                checkBox.IsChecked = IsChecked;
            if (control is Slider slider && SliderValue is not null)
                slider.Value = SliderValue.Value;
            if (control is NumericUpDown numeric && NumericValue is not null)
                numeric.Value = NumericValue;
            if (control is ScrollViewer scroll && ScrollOffset is not null)
                scroll.Offset = ScrollOffset.Value;
        }
    }
}
