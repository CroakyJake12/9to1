using System.ComponentModel;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core.Mathematics;

namespace Haven.Desktop.Mathematics;

public sealed class MathAnswerDraftChangedEventArgs(MathAnswer? answer, string? diagnostic) : EventArgs
{
    public MathAnswer? Answer { get; } = answer;
    public string? Diagnostic { get; } = diagnostic;
}

/// <summary>Reusable numeric answer input that preserves submitted literals and units.
/// Invalid drafts remain visible and are reported immediately, without replacing the last
/// valid canonical answer or quietly rounding unsupported precision.</summary>
public sealed class NumericMathAnswerEditorControl : ContentControl,
    ICuiWritableBindingContext, INotifyPropertyChanged, IDisposable
{
    private readonly CuiControlLoader _loader = new();
    private NativeMathInputObservation? _inputObservation;
    private readonly MathServiceLimits _limits;
    private readonly bool _allowEmpty;
    private readonly Guid _answerID;
    private MathAnswer? _lastValid;
    private string _literal, _units, _status = "Enter a number";
    private bool _disposed;
    public new event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<MathAnswerDraftChangedEventArgs>? DraftChanged;
    public string DraftLiteral => _literal;
    public string DraftUnits => _units;
    public MathAnswer? LastValid => _lastValid is null ? null : MathObjectCodec.Capture(_lastValid, _limits);
    public NumericMathAnswerEditorControl(MathAnswer? initial = null, MathServiceLimits? limits = null,
        string? draftLiteral = null, string? draftUnits = null, bool allowEmpty = false)
    {
        _limits = limits ?? new(); _allowEmpty = allowEmpty;
        if (initial is not null)
        {
            _lastValid = MathObjectCodec.Capture(initial, _limits);
            if (_lastValid.Value is not NumericMathAnswer) throw new NotSupportedException("NumericMathAnswerRequired");
        }
        _answerID = initial?.AnswerID ?? Guid.NewGuid();
        _literal = draftLiteral ?? (_lastValid?.Value as NumericMathAnswer)?.Literal ?? "";
        _units = draftUnits ?? (_lastValid?.Value as NumericMathAnswer)?.Units ?? "";
        try
        {
            _loader.SetBindingContext(this);
            var parser = new CuiRichParser(); var document = parser.Parse(CuiSource, "NumericMathAnswerEditor.cui");
            if (parser.Diagnostics.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("NumericMathAnswerCuiInvalid");
            var loaded = _loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("NumericMathAnswerCuiUnavailable");
            _loader.WireBindings(loaded.Root); Content = loaded.Root;
            _inputObservation = new(this, () => !_disposed);
            _inputObservation.Bind(loaded.Root, new Dictionary<string,string> { ["math-answer-number"]="Literal", ["math-answer-units"]="Units" });
        }
        catch { Dispose(); throw; }
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Literal" => _literal, "Units" => _units, "Status" => _status, _ => null };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || !IsEffectivelyEnabled || value is not string text) return false;
        if (path == "Literal") { if (_literal == text) return true; _literal = text; }
        else if (path == "Units") { if (_units == text) return true; _units = text; }
        else return false;
        try
        {
            if (_allowEmpty && _literal.Length == 0 && _units.Length == 0)
            {
                _status = "No answer";
                DraftChanged?.Invoke(this, new(null, null));
                PropertyChanged?.Invoke(this, new(path)); PropertyChanged?.Invoke(this, new("Status"));
                return true;
            }
            _ = MathNumericLiteral.Read(_literal);
            var candidate = new MathAnswer(_answerID, checked((_lastValid?.Revision ?? 0) + 1),
                new NumericMathAnswer(_literal, string.IsNullOrEmpty(_units) ? null : _units));
            _lastValid = MathObjectCodec.Capture(candidate, _limits);
            _status = "Answer ready";
            DraftChanged?.Invoke(this, new(LastValid, null));
        }
        catch (InvalidDataException error)
        {
            _status = "Check the number: " + error.Message;
            DraftChanged?.Invoke(this, new(null, "MathParseError"));
        }
        catch (OverflowException)
        {
            _status = "The answer revision limit was reached; the previous answer has been preserved.";
            DraftChanged?.Invoke(this, new(null, "MathAnswerRevisionLimit"));
        }
        PropertyChanged?.Invoke(this, new(path)); PropertyChanged?.Invoke(this, new("Status"));
        return true;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _inputObservation?.Dispose(); _loader.Dispose(); Content = null; IsEnabled = false;
        PropertyChanged = null; DraftChanged = null;
    }
    public const string CuiSource = """
        <Cui><StackPanel spacing="8">
          <TextBlock text="Number" /><TextBox id="math-answer-number" text="{Binding Literal, Mode=TwoWay}" />
          <TextBlock text="Units (if required)" /><TextBox id="math-answer-units" text="{Binding Units, Mode=TwoWay}" />
          <TextBlock id="math-answer-status" text="{Binding Status}" text-wrapping="Wrap" />
        </StackPanel></Cui>
        """;
}
