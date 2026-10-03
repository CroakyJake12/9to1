using System.ComponentModel;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application.Mathematics;
using Haven.Core;
using Haven.Core.Mathematics;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;

namespace Haven.Desktop.Mathematics;

/// <summary>Reusable authored CUI editor over the canonical shared expression/session.
/// Preview is a transient projection through the existing Home renderer, never a second
/// persisted Notes/math object. The owner is responsible for authorized atomic persistence.</summary>
public sealed class SharedMathEditorControl : ContentControl, ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly CuiControlLoader _loader;
    private NativeMathInputObservation? _inputObservation;
    private readonly MathEditorSession _session;
    private readonly MathServiceLimits _limits;
    private readonly Border _previewHost = new();
    private HomeProductivityCuiSurface? _preview;
    private string _draft;
    private string _symbol = "x";
    private string _status = "Ready";
    private bool _showSource;
    private bool _disposed;
    public new event PropertyChangedEventHandler? PropertyChanged;
    public MathEditorState Snapshot => _session.Snapshot();
    public Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();

    public SharedMathEditorControl(MathExpression initial, MathServiceLimits? limits = null)
    {
        _limits = limits ?? new();
        _session = new(initial, new CSharpMathSyntaxAdapter(), _limits);
        _draft = initial.LaTeX;
        _loader = new CuiControlLoader();
        try
        {
            _loader.RegisterObjectRenderer("math.shared-preview", component =>
            {
                if (component.Name != "math-preview") throw new InvalidDataException("UnexpectedMathPreviewControl");
                return _previewHost;
            });
            _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
            var parser = new CuiRichParser(); var document = parser.Parse(CuiSource, "SharedMathEditor.cui");
            if (parser.Diagnostics.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("SharedMathEditorCuiInvalid");
            var loaded = _loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("SharedMathEditorCuiUnavailable");
            _loader.WireBindings(loaded.Root); Content = loaded.Root;
            _inputObservation = new(this, () => !_disposed);
            _inputObservation.Bind(loaded.Root, new Dictionary<string,string> { ["math-symbol"]="Symbol", ["math-source"]="Draft" });
            RefreshPreview();
        }
        catch { Dispose(); throw; }
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Draft" => _draft, "Symbol" => _symbol, "Status" => _status,
            "ShowSource" => _showSource, "CanonicalSource" => Snapshot.LastValid.LaTeX, _ => null };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || !IsEffectivelyEnabled || value is not string text) return false;
        if (path == "Draft") { if (_draft == text) return true; _draft = text; }
        else if (path == "Symbol") { if (_symbol == text) return true; _symbol = text; }
        else return false;
        Changed(path);
        return true;
    }
    public bool? IsActionAvailable(string command)
    {
        if (_disposed || !IsEffectivelyEnabled) return false;
        if (command is "Source" or "Restore" or "ToggleSource") return true;
        return (command == "InsertSymbol" || Enum.TryParse<MathVisualOperation>(command, out var operation) &&
            Enum.IsDefined(operation) && operation.ToString() == command) && Snapshot.Diagnostic is null && _draft == Snapshot.DraftLaTeX;
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("MathActionUnavailable");
        if (command == "ToggleSource")
        {
            _showSource = !_showSource; Changed("ShowSource");
            if (Content is Control root) _inputObservation?.Bind(root, new Dictionary<string,string> { ["math-symbol"]="Symbol", ["math-source"]="Draft" });
            return ValueTask.CompletedTask;
        }
        var original = Snapshot;
        MathEditorState result;
        try
        {
            if (command == "Source") result = _session.EditLaTeX(original.LastValid.Revision, _draft);
            else if (command == "Restore") result = _session.RestoreLastValid(original.LastValid.Revision);
            else
            {
                var operation = Enum.Parse<MathVisualOperation>(command);
                result = _session.EditVisual(original.LastValid.Revision, operation == MathVisualOperation.InsertSymbol
                    ? new(operation, original.DraftLaTeX.Length, 0, _symbol)
                    : new(operation, 0, original.DraftLaTeX.Length));
            }
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException)
        { _status = error.Message; Changed("Status"); return ValueTask.CompletedTask; }
        _draft = result.DraftLaTeX; _status = result.Diagnostic ?? "Expression updated";
        if (result.LastValid != original.LastValid) RefreshPreview();
        Changed("Draft"); Changed("CanonicalSource"); Changed("Status");
        return ValueTask.CompletedTask;
    }
    private void RefreshPreview()
    {
        var expression = Snapshot.LastValid;
        var projection = new NotesBlock { Id = expression.ExpressionID, Kind = NotesBlockKind.Equation, StyleId = "",
            Equation = new() { Source = expression.LaTeX, ViewMode = NotesEquationViewMode.Visual,
                AccessibleAlternative = expression.AccessibleDescription } };
        var old = _preview;
        try
        {
            var handler = new HomeEquationObjectHandler();
            var next = new HomeProductivityCuiSurface(handler.Render(HomeEquationObjectHandler.Project(projection)));
            _previewHost.Child = next; _preview = next;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException)
        {
            _status = "Equation preview unavailable: " + error.Message;
            _previewHost.Child = new TextBlock { Text = _status }; _preview = null;
        }
        old?.Dispose();
    }
    private void Changed(string path) => PropertyChanged?.Invoke(this, new(path));
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _inputObservation?.Dispose(); _loader.Dispose(); Content = null; _previewHost.Child = null; _preview?.Dispose(); _preview = null;
        PropertyChanged = null;
    }

    // The component is reusable from product CUI hosts. Buttons insert inspectable starter
    // notation; unsupported parser notation yields a recoverable draft error.
    public const string CuiSource = """
        <Cui>
          <StackPanel spacing="8" margin="12">
            <Object Type="math.shared-preview" id="math-preview" />
            <TextBlock text="Symbol or number" />
            <TextBox id="math-symbol" text="{Binding Symbol, Mode=TwoWay}" />
            <Button id="math-insert" action="InsertSymbol" content="Insert symbol" />
            <StackPanel orientation="Horizontal" spacing="4">
              <Button id="math-fraction" action="Fraction" content="Fraction" />
              <Button id="math-power" action="Power" content="Power" />
              <Button id="math-subscript" action="Subscript" content="Subscript" />
              <Button id="math-root" action="SquareRoot" content="Root" />
            </StackPanel>
            <StackPanel orientation="Horizontal" spacing="4">
              <Button id="math-log" action="Logarithm" content="Log" />
              <Button id="math-sine" action="Sine" content="Sin" />
              <Button id="math-cosine" action="Cosine" content="Cos" />
              <Button id="math-tangent" action="Tangent" content="Tan" />
            </StackPanel>
            <StackPanel orientation="Horizontal" spacing="4">
              <Button id="math-integral" action="Integral" content="Integral" />
              <Button id="math-sum" action="Sum" content="Sum" />
              <Button id="math-product" action="Product" content="Product" />
              <Button id="math-vector" action="Vector" content="Vector" />
            </StackPanel>
            <StackPanel orientation="Horizontal" spacing="4">
              <Button id="math-matrix" action="Matrix" content="Matrix" />
              <Button id="math-set" action="Set" content="Set" />
              <Button id="math-piecewise" action="Piecewise" content="Piecewise" />
              <Button id="math-probability" action="Probability" content="Probability" />
              <Button id="math-aligned" action="Aligned" content="Aligned" />
            </StackPanel>
            <Button id="math-mode" action="ToggleSource" content="Show or hide LaTeX" />
            <If Condition="{Binding ShowSource}">
              <StackPanel>
                <TextBox id="math-source" text="{Binding Draft, Mode=TwoWay}" />
                <Button id="math-apply-source" action="Source" content="Apply LaTeX" />
              </StackPanel>
            </If>
            <Button id="math-restore" action="Restore" content="Restore last valid expression" />
            <TextBlock id="math-status" text="{Binding Status}" text-wrapping="Wrap" />
          </StackPanel>
        </Cui>
        """;
}
