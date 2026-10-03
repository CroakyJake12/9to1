using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application.Mathematics;
using Haven.Core.Mathematics;
using ScottPlot.Avalonia;

namespace Haven.Desktop.Mathematics;

/// <summary>Reusable authored CUI graph editor over the shared versioned graph session.
/// Native clicks and coordinate entry emit the same typed response. The host owns identity,
/// approval and persistence; this component provides no independent account/data service.</summary>
public sealed class SharedGraphEditorControl : ContentControl, ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly CuiControlLoader _loader;
    private NativeMathInputObservation? _inputObservation;
    private readonly GraphEditorSession _session;
    private readonly GraphResponseEditorSession? _responseSession;
    private bool _coordinateDraft;
    private readonly MathServiceLimits _limits;
    private readonly AvaPlot _plot;
    private string _x = "0", _y = "0", _status = "Ready";
    private GraphProjection _projection = new(Guid.Empty, 0, [], [], ScottPlotGraphAdapter.Implementation);
    private GraphResponse? _lastResponse;
    private bool _disposed;
    public new event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ResponseDraftChanged;
    public bool HasUncommittedCoordinates => _coordinateDraft;
    public (string X, string Y) CoordinateDraft => (_x, _y);
    public GraphDefinition Snapshot => _responseSession?.Projection() ?? _session.Snapshot();
    public GraphResponse? LastResponse => _lastResponse is null ? null : MathObjectCodec.Capture(_lastResponse, _limits);
    public GraphProjection Projection => _projection with {
        RenderedPrimitiveIDs = (Guid[])_projection.RenderedPrimitiveIDs.Clone(),
        UnavailableSymbolicPrimitiveIDs = (Guid[])_projection.UnavailableSymbolicPrimitiveIDs.Clone() };
    public Task WhenActionsIdleAsync() => _loader.WhenActionsIdleAsync();

    public SharedGraphEditorControl(GraphDefinition initial, MathServiceLimits? limits = null)
        : this(initial, limits, null, false) { }
    public static SharedGraphEditorControl ForResponse(GraphDefinition question, GraphResponse? initial = null,
        MathServiceLimits? limits = null) => new(question, limits, initial, true);
    private SharedGraphEditorControl(GraphDefinition initial, MathServiceLimits? limits,
        GraphResponse? response, bool responseMode)
    {
        _limits = limits ?? new(); _session = new(initial, _limits);
        if (responseMode)
        {
            _responseSession = new(initial, response, _limits);
            _lastResponse = response is null ? null : MathObjectCodec.Capture(response, _limits);
            if (_lastResponse?.Actions is { Length: 1 } actions && actions[0].Primitive is GraphPoint point)
            { _x = point.Position.X.ToString(CultureInfo.InvariantCulture); _y = point.Position.Y.ToString(CultureInfo.InvariantCulture); }
        }
        _loader = new CuiControlLoader();
        _plot = new() { Name = "math-native-plot", Height = 360 };
        try
        {
            _projection = ScottPlotGraphAdapter.Render(_plot, Snapshot, _limits);
            _plot.AddHandler(InputElement.PointerReleasedEvent, OnGraphPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
            _loader.RegisterObjectRenderer("math.shared-graph", component =>
            {
                if (component.Name != "math-graph") throw new InvalidDataException("UnexpectedMathGraphControl");
                return _plot;
            });
            _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
            var parser = new CuiRichParser(); var document = parser.Parse(CuiSource, "SharedGraphEditor.cui");
            if (parser.Diagnostics.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("SharedGraphEditorCuiInvalid");
            var loaded = _loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(x => x.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("SharedGraphEditorCuiUnavailable");
            _loader.WireBindings(loaded.Root); Content = loaded.Root;
            _inputObservation = new(this, () => !_disposed);
            _inputObservation.Bind(loaded.Root, new Dictionary<string,string> { ["math-graph-x"]="X", ["math-graph-y"]="Y" }); DescribeProjection();
        }
        catch { Dispose(); throw; }
    }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "X" => _x, "Y" => _y, "Status" => _status,
            "Coordinates" => string.Join(Environment.NewLine, Snapshot.Primitives.OfType<GraphPoint>()
                .Select(p => string.Create(CultureInfo.InvariantCulture, $"({p.Position.X}, {p.Position.Y})"))), _ => null };
        return value is not null;
    }
    public bool TrySetValue(string path, object? value)
    {
        if (_disposed || !IsEffectivelyEnabled || value is not string text) return false;
        if (path == "X") { if (_x == text) return true; _x = text; }
        else if (path == "Y") { if (_y == text) return true; _y = text; }
        else return false;
        _coordinateDraft = true; Changed(path); ResponseDraftChanged?.Invoke(this, EventArgs.Empty); return true;
    }
    public bool? IsActionAvailable(string command) => !_disposed && IsEffectivelyEnabled && command == "PlacePoint" &&
        Snapshot.ResponseTools.Contains(GraphResponseTool.PlacePoint)
        && (_responseSession is null || _responseSession.Snapshot().Actions.Length <= 1);
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("GraphActionUnavailable");
        decimal x, y;
        try { x = MathNumericLiteral.Read(_x).Value; y = MathNumericLiteral.Read(_y).Value; }
        catch (InvalidDataException)
        { _status = "Enter exact supported decimal coordinates"; Changed("Status"); return ValueTask.CompletedTask; }
        try { PlacePoint(new(x, y)); }
        catch (OverflowException)
        { _status = "The graph revision limit was reached; the previous response and coordinate draft have been preserved."; Changed("Status"); }
        return ValueTask.CompletedTask;
    }
    private void OnGraphPointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (_disposed || !IsEffectivelyEnabled || args.InitialPressMouseButton != MouseButton.Left || IsActionAvailable("PlacePoint") != true) return;
        var position = args.GetPosition(_plot);
        var pixel = new ScottPlot.Pixel((float)position.X, (float)position.Y);
        var rectangle = _plot.Plot.LastRender.DataRect;
        if (!rectangle.HasArea || !rectangle.Contains(pixel)) return;
        var coordinate = _plot.Plot.GetCoordinates(pixel);
        if (!double.IsFinite(coordinate.X) || !double.IsFinite(coordinate.Y)) return;
        try { PlacePoint(new(checked((decimal)coordinate.X), checked((decimal)coordinate.Y))); }
        catch (Exception error) when (error is OverflowException or InvalidDataException)
        { _status = "Point unavailable: " + error.Message; Changed("Status"); }
        args.Handled = true;
    }
    private void PlacePoint(GraphCoordinate point)
    {
        var original = Snapshot;
        if (point.X < original.Axes.XMinimum || point.X > original.Axes.XMaximum ||
            point.Y < original.Axes.YMinimum || point.Y > original.Axes.YMaximum)
        { _status = "Point is outside the configured graph range"; Changed("Status"); return; }
        var response = new GraphResponse(Guid.NewGuid(), 1, original.GraphID, original.Revision, [],
            [new(GraphResponseTool.PlacePoint, new GraphPoint(Guid.NewGuid(), point))]);
        GraphDefinition next;
        if (_responseSession is not null)
        {
            response = _responseSession.PlacePoint(_responseSession.Snapshot().Revision, point);
            next = _responseSession.Projection();
        }
        else next = _session.Apply(response);
        _lastResponse = MathObjectCodec.Capture(response, _limits);
        _coordinateDraft = false;
        _projection = ScottPlotGraphAdapter.Render(_plot, next, _limits);
        _x = point.X.ToString(CultureInfo.InvariantCulture); _y = point.Y.ToString(CultureInfo.InvariantCulture);
        DescribeProjection(); Changed("X"); Changed("Y"); Changed("Coordinates"); Changed("Status");
        ResponseDraftChanged?.Invoke(this, EventArgs.Empty);
    }
    private void DescribeProjection() => _status = _projection.UnavailableSymbolicPrimitiveIDs.Length == 0
        ? "Click the graph or enter coordinates to place a point"
        : "Coordinate graph shown; symbolic plotting requires a configured mathematical evaluator";
    private void Changed(string path) => PropertyChanged?.Invoke(this, new(path));
    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        _plot.RemoveHandler(InputElement.PointerReleasedEvent, OnGraphPointerReleased);
        _inputObservation?.Dispose(); _loader.Dispose(); Content = null; _plot.Plot.Dispose(); PropertyChanged = null; ResponseDraftChanged = null;
    }
    public const string CuiSource = """
        <Cui>
          <StackPanel spacing="8" margin="12">
            <Object Type="math.shared-graph" id="math-graph" />
            <TextBlock text="Point coordinates" />
            <StackPanel orientation="Horizontal" spacing="8">
              <TextBlock text="x" /><TextBox id="math-graph-x" text="{Binding X, Mode=TwoWay}" />
              <TextBlock text="y" /><TextBox id="math-graph-y" text="{Binding Y, Mode=TwoWay}" />
              <Button id="math-place-point" action="PlacePoint" content="Place point" />
            </StackPanel>
            <TextBlock id="math-graph-coordinates" text="{Binding Coordinates}" />
            <TextBlock id="math-graph-status" text="{Binding Status}" text-wrapping="Wrap" />
          </StackPanel>
        </Cui>
        """;
}
