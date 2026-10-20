using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;

namespace CakeOS.Cui.Runtime;

/// <summary>
/// Applies CUI documents to a live content host. Candidate trees are fully built
/// before the current tree is replaced, so a failed load leaves the last good UI intact.
/// </summary>
public sealed class CuiRuntimeSurface : IDisposable
{
    private readonly ContentControl _host;
    private readonly CuiControlRegistry _registry;
    private readonly ICuiBindingContext? _bindingContext;
    private readonly ICuiActionDispatcher? _actionDispatcher;
    private CuiControlLoader? _activeLoader;
    private CuiControlLoader? _retiringLoader;
    private Control? _activeRoot;
    private bool _applying;
    private bool _disposed;

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
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_applying)
            throw new InvalidOperationException("A CUI surface application is already in progress.");
        _applying = true;
        try
        {
            return TryApplyCore(document, out diagnostics);
        }
        finally
        {
            _applying = false;
        }
    }

    private bool TryApplyCore(CuiDocument document, out IReadOnlyList<CuiDiagnostic> diagnostics)
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

        CuiControlLoader? candidateLoader = new(_registry);
        try
        {
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

            var states = CaptureStates(CurrentRoot);
            RestoreStates(candidate, states);
            candidateLoader.WireBindings(candidate);
            ObjectDisposedException.ThrowIf(_disposed, this);

            _retiringLoader = _activeLoader;
            // Native Content notifications can synchronously dispose the surface or throw
            // after publishing the new tree. Establish its owner before entering callbacks.
            _activeLoader = candidateLoader;
            _activeRoot = candidate;
            candidateLoader = null;
            try
            {
                _host.Content = candidate;
                ObjectDisposedException.ThrowIf(_disposed, this);
                RestoreFocus(candidate, states);
            }
            catch (Exception failure)
            {
                CleanupAfterFailure(RetirePreviousLoader, failure);
                throw;
            }
            RetirePreviousLoader();
            return true;
        }
        catch (Exception failure)
        {
            if (candidateLoader is not null) CleanupAfterFailure(candidateLoader.Dispose, failure);
            throw;
        }
    }

    /// <summary>Revoke actions and subscriptions owned by this surface. Admitted asynchronous
    /// actions receive cancellation; their owning dispatcher still observes their completion.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var loader = _activeLoader;
        var retiringLoader = _retiringLoader;
        var root = _activeRoot;
        _activeLoader = null;
        _retiringLoader = null;
        _activeRoot = null;

        void DisposeOwnedLoaders()
        {
            try
            {
                loader?.Dispose();
            }
            catch (Exception failure)
            {
                if (retiringLoader is not null) CleanupAfterFailure(retiringLoader.Dispose, failure);
                throw;
            }
            retiringLoader?.Dispose();
        }

        void ClearOwnedContent()
        {
            if (root is not null && ReferenceEquals(_host.Content, root))
                _host.Content = null;
        }

        try
        {
            DisposeOwnedLoaders();
        }
        catch (Exception failure)
        {
            CleanupAfterFailure(ClearOwnedContent, failure);
            throw;
        }
        ClearOwnedContent();
    }

    private void RetirePreviousLoader()
    {
        var previousLoader = _retiringLoader;
        _retiringLoader = null;
        previousLoader?.Dispose();
    }

    private static void CleanupAfterFailure(Action cleanup, Exception failure)
    {
        try
        {
            cleanup();
        }
        catch (Exception cleanupFailure) when (!ReferenceEquals(failure, cleanupFailure))
        {
            throw new AggregateException("CUI surface preparation or publication and cleanup both failed.", failure, cleanupFailure);
        }
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
