using System.ComponentModel;
using System.Text.Json;
using System.Globalization;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Core.Forms;
using Haven.Core.Mathematics;
using Haven.Application.Mathematics;

namespace Haven.Desktop.Mathematics;

/// <summary>Local question/marking draft over the shared editors. Only an explicit Save
/// returns a candidate to the existing Forms authoring service; cancellation, parse errors
/// and retired controls do not alter the authored question or bypass publication CAS.</summary>
public sealed class FormMathematicsAuthoringControl : ContentControl, ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly FormField _original;
    private readonly CuiControlLoader _loader = new();
    private NativeMathInputObservation? _inputObservation;
    private readonly SharedMathEditorControl? _math;
    private readonly SharedGraphEditorControl? _graph;
    private readonly SharedGraphEditorControl? _expectedGraph;
    private string _expected = "0", _tolerance = "0", _units = "", _points = "1", _status = "Edit the question";
    private int _comparison;
    private bool _graded, _disposed;
    public FormField? Result { get; private set; }
    public event EventHandler? Completed;
    public new event PropertyChangedEventHandler? PropertyChanged;
    public Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();
    public FormMathematicsAuthoringControl(FormField original)
    {
        if (original.Kind is not (FormFieldKind.Mathematical or FormFieldKind.Graph))
            throw new ArgumentException("MathematicalFieldRequired", nameof(original));
        _original = JsonSerializer.Deserialize<FormField>(JsonSerializer.Serialize(original))!;
        original = _original;
        if (original.Assessment is { Mathematics: null, Graph: null })
            throw new NotSupportedException("This editor cannot edit the existing grading policy; the question and grading have been preserved.");
        _graded = original.Assessment?.Mathematics is not null || original.Assessment?.Graph is not null;
        _points = (original.Assessment?.MaximumPoints ?? 1).ToString(CultureInfo.InvariantCulture);
        if (original.Assessment?.Mathematics is { } numeric)
        {
            _expected = numeric.Expected.ToString(CultureInfo.InvariantCulture);
            _tolerance = numeric.Tolerance.ToString(CultureInfo.InvariantCulture);
            _units = numeric.Units ?? ""; _comparison = (int)numeric.Comparison;
        }
        else if (original.Assessment?.Graph is { } coordinates)
            _tolerance = coordinates.CoordinateTolerance.ToString(CultureInfo.InvariantCulture);
        try
        {
            Control question;
            if (original.Kind == FormFieldKind.Mathematical)
            {
                _math = new(original.Mathematics ?? throw new InvalidDataException("CanonicalMathQuestionMissing"));
                question = _math;
            }
            else
            {
                var graph = original.Graph ?? throw new InvalidDataException("CanonicalGraphQuestionMissing");
                _graph = new(graph); question = _graph;
                _expectedGraph = new(original.Assessment?.ExpectedGraph ?? graph with {
                    GraphID = Guid.NewGuid(), Revision = 1, Expressions = [], Primitives = [] });
            }
            _loader.RegisterObjectRenderer("forms.math-question-editor", _ => question);
            _loader.RegisterObjectRenderer("forms.math-expected-editor", _ => _expectedGraph ?? (Control)new TextBlock { Text = "Numeric answer policy" });
            _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
            var parser = new CuiRichParser(); var document = parser.Parse(CuiSource, "Forms mathematical question.cui");
            if (parser.Diagnostics.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("FormsMathAuthoringCuiInvalid");
            var loaded = _loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("FormsMathAuthoringCuiUnavailable");
            _loader.WireBindings(loaded.Root); Content = loaded.Root;
            _inputObservation = new(this, () => !_disposed);
            _inputObservation.Bind(loaded.Root, new Dictionary<string,string> { ["forms-math-expected-number"]="Expected", ["forms-math-required-units"]="Units", ["forms-math-points"]="Points", ["forms-math-tolerance"]="Tolerance" });
        }
        catch { Dispose(); throw; }
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Expected" => _expected, "Tolerance" => _tolerance, "Units" => _units,
            "Points" => _points, "Graded" => _graded, "Numeric" => _math is not null,
            "Comparison" => _comparison, "ComparisonNames" => new[] { "Exact", "Absolute tolerance", "Percentage tolerance" },
            "Status" => _status, _ => null };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || !IsEffectivelyEnabled) return false;
        if (path == "Graded" && value is bool graded) _graded = graded;
        else if (path == "Comparison" && value is int comparison && comparison is >= 0 and <= 2) _comparison = comparison;
        else if (value is string text)
        {
            if (path == "Expected") _expected = text;
            else if (path == "Tolerance") _tolerance = text;
            else if (path == "Units") _units = text;
            else if (path == "Points") _points = text;
            else return false;
        }
        else return false;
        PropertyChanged?.Invoke(this, new(path)); return true;
    }
    public bool? IsActionAvailable(string command) => !_disposed && IsEffectivelyEnabled && command is "Save" or "Cancel";
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (parameter is not null || IsActionAvailable(command) != true) throw new InvalidOperationException("MathAuthoringActionUnavailable");
        if (command == "Cancel") { Result = null; Completed?.Invoke(this, EventArgs.Empty); return ValueTask.CompletedTask; }
        try
        {
            var maximum = _graded ? MathNumericLiteral.Read(_points).Value : 0;
            if (maximum < 0) throw new InvalidDataException("InvalidMaximumPoints");
            var weight = _original.Assessment?.Weight ?? 1;
            var release = _original.Assessment?.Release ?? FormResultRelease.AfterSubmission;
            FormField candidate;
            if (_math is not null)
            {
                var state = _math.Snapshot;
                _math.TryGetValue("Draft", out var draft);
                if (state.Diagnostic is not null || !Equals(draft, state.DraftLaTeX)) throw new InvalidDataException("MathParseError");
                MathNumericRule? rule = null;
                if (_graded)
                {
                    rule = new(_original.Assessment?.Mathematics?.RuleID ?? Guid.NewGuid(),
                        MathNumericLiteral.Read(_expected).Value, (MathNumericComparison)_comparison,
                        MathNumericLiteral.Read(_tolerance).Value,
                        _original.Assessment?.Mathematics?.SignificantFigures,
                        _original.Assessment?.Mathematics?.DecimalPlaces,
                        string.IsNullOrEmpty(_units) ? null : _units,
                        _original.Assessment?.Mathematics?.IgnoreUnitsCase ?? false);
                    _ = MathMarking.Evaluate(new(Guid.NewGuid(), 1, new NumericMathAnswer("0")), rule);
                }
                candidate = _original with { Mathematics = state.LastValid,
                    Assessment = rule is null ? null : new(maximum, weight, [], release, Mathematics:rule) };
            }
            else
            {
                if (_graph!.HasUncommittedCoordinates || _expectedGraph!.HasUncommittedCoordinates)
                    throw new InvalidDataException("GraphResponseInvalid: apply the edited coordinates first.");
                var question = _graph.Snapshot;
                FormFieldAssessment? assessment = null;
                if (_graded)
                {
                    var privateSnapshot = _expectedGraph.Snapshot;
                    var expectedEditor = new GraphEditorSession(privateSnapshot);
                    var expected = expectedEditor.ReplaceDefinition(privateSnapshot.Revision,
                        privateSnapshot with { Axes = question.Axes, ResponseTools = question.ResponseTools });
                    var target = _expectedGraph.LastResponse?.Actions.SingleOrDefault()?.Primitive.PrimitiveID
                        ?? _original.Assessment?.Graph?.ExpectedPrimitiveID;
                    if (target is null) throw new InvalidDataException("Place the expected marking point.");
                    var rule = new GraphCoordinateMarkingRule(_original.Assessment?.Graph?.RuleID ?? Guid.NewGuid(),
                        target.Value, MathNumericLiteral.Read(_tolerance).Value,
                        AllowReversedLine:_original.Assessment?.Graph?.AllowReversedLine ?? true,
                        QuestionPrimitiveID:_original.Assessment?.Graph?.QuestionPrimitiveID);
                    GraphMarking.ValidatePolicy(question, expected, rule);
                    assessment = new(maximum, weight, [], release, Graph:rule, ExpectedGraph:expected);
                }
                candidate = _original with { Graph = question, Assessment = assessment };
            }
            Result = candidate; Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException)
        { _status = error.Message; PropertyChanged?.Invoke(this, new("Status")); }
        return ValueTask.CompletedTask;
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _inputObservation?.Dispose(); _loader.Dispose(); Content = null; _math?.Dispose(); _graph?.Dispose(); _expectedGraph?.Dispose();
        Completed = null; PropertyChanged = null; IsEnabled = false;
    }
    public const string CuiSource = """
        <Cui><StackPanel spacing="8">
          <TextBlock text="Question prompt" /><Object id="forms-math-prompt" type="forms.math-question-editor" />
          <CheckBox id="forms-math-graded" content="Grade this answer" is-checked="{Binding Graded, Mode=TwoWay}" />
          <TextBlock text="Maximum points" /><TextBox id="forms-math-points" text="{Binding Points, Mode=TwoWay}" />
          <TextBlock text="Marking answer" /><Object id="forms-math-expected" type="forms.math-expected-editor" />
          <If Condition="{Binding Numeric}"><StackPanel spacing="4">
            <TextBlock text="Expected number" /><TextBox id="forms-math-expected-number" text="{Binding Expected, Mode=TwoWay}" />
            <ComboBox id="forms-math-comparison" items-source="{Binding ComparisonNames}" selected-index="{Binding Comparison, Mode=TwoWay}" />
            <TextBlock text="Required units (optional)" /><TextBox id="forms-math-required-units" text="{Binding Units, Mode=TwoWay}" />
          </StackPanel></If>
          <TextBlock text="Tolerance" /><TextBox id="forms-math-tolerance" text="{Binding Tolerance, Mode=TwoWay}" />
          <Button id="forms-math-save" action="Save" content="Save question" />
          <Button id="forms-math-cancel" action="Cancel" content="Cancel" />
          <TextBlock id="forms-math-edit-status" text="{Binding Status}" text-wrapping="Wrap" />
        </StackPanel></Cui>
        """;
}
