using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application.Mathematics;
using Haven.Core.Mathematics;
using ScottPlot.Avalonia;

namespace Haven.Desktop.Mathematics;

/// <summary>Authored native view of an observational prepared plot. The owning
/// caller supplies the canonical graph and evaluator; this view cannot persist,
/// approve, mark or alter a question. Every original preparation task settles
/// before replacement or disposal, and whole source bytes fence publication.</summary>
public sealed class SharedPreparedGraphViewControl : ContentControl,
    ICuiBindingContext, INotifyPropertyChanged, IAsyncDisposable
{
    private readonly IGraphPlotPreparer _preparer;
    private readonly GraphPlotPreparationPolicy _policy;
    private readonly CuiControlLoader _loader = new();
    private readonly AvaPlot _plot = new() { Name = "math-prepared-native-plot", Height = 360 };
    private readonly List<CancellationTokenSource> _requests = [];
    private GraphDefinition _source;
    private PreparedGraphPlot? _prepared;
    private Task _preparation = Task.CompletedTask;
    private CancellationTokenSource? _currentCancellation;
    private Task? _disposal;
    private long _generation;
    private bool _disposed;
    private string _status = "Ready for preparation";
    public new event PropertyChangedEventHandler? PropertyChanged;
    public GraphDefinition CaptureSource() => MathObjectCodec.Capture(_source);
    public PreparedGraphPlot? Prepared => _prepared;
    public Task OriginalPreparationTask => _preparation;
    public Task OriginalDisposalTask => _disposal ?? Task.CompletedTask;

    public SharedPreparedGraphViewControl(GraphDefinition source, IGraphPlotPreparer preparer,
        GraphPlotPreparationPolicy? policy = null)
    {
        RequireUiThread(); _preparer = preparer ?? throw new ArgumentNullException(nameof(preparer));
        _policy = policy ?? new(); _policy.Validate(); _source = MathObjectCodec.Capture(source);
        try
        {
            RequireNativeRange(_source);
            lock (_plot.Plot.Sync) ScottPlotGraphAdapter.Render(_plot, _source);
            _loader.RegisterObjectRenderer("math.prepared-graph", component =>
            {
                if (component.Name != "math-prepared-graph") throw new InvalidDataException("UnexpectedPreparedGraphControl");
                return _plot;
            });
            _loader.SetBindingContext(this);
            var parser = new CuiRichParser(); var document = parser.Parse(CuiSource, "SharedPreparedGraphView.cui");
            if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("PreparedGraphViewCuiInvalid");
            var loaded = _loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
                throw new InvalidDataException("PreparedGraphViewCuiUnavailable");
            _loader.WireBindings(loaded.Root); Content = loaded.Root;
            DetachedFromVisualTree += OnDetached;
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            try { _loader.Dispose(); } catch (Exception error) { failures.Add(error); }
            try { Content = null; } catch (Exception error) { failures.Add(error); }
            try { lock (_plot.Plot.Sync) _plot.Plot.Dispose(); } catch (Exception error) { failures.Add(error); }
            if (failures.Count > 1) throw new AggregateException("PreparedGraphConstructionFailed", failures);
            throw;
        }
    }

    public Task SetSourceAsync(GraphDefinition source, CancellationToken cancellationToken = default)
    {
        RequireUiThread(); ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        var captured = MathObjectCodec.Capture(source); RequireNativeRange(captured);
        var previous = _preparation; var previousCancellation = _currentCancellation;
        var previousToken = previousCancellation?.Token ?? CancellationToken.None;
        var generation = checked(_generation + 1);
        var current = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var admission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admissionFailures = new List<Exception>();
        // The SAME admitted task is retained before any owned Cancel/property callback.
        // A reentrant replacement drains this task, and retirement cannot overlook it.
        var original = PrepareAndPublishAsync(admission.Task, admissionFailures, previous,
            previousCancellation, previousToken, captured, generation, current.Token);
        _requests.Add(current); _currentCancellation = current;
        _generation = generation; _source = captured; _preparation = original;
        try
        {
            try { previousCancellation?.Cancel(); } catch (Exception error) { RetainFailure(admissionFailures, error); }
            if (!_disposed && generation == _generation)
                try { PropertyChanged?.Invoke(this, new("Description")); }
                catch (Exception error) { RetainFailure(admissionFailures, error); }
            if (!_disposed && generation == _generation)
                try { PropertyChanged?.Invoke(this, new("Expressions")); }
                catch (Exception error) { RetainFailure(admissionFailures, error); }
            if (!_disposed && generation == _generation)
                try { SetStatus("Preparing mathematical graph"); }
                catch (Exception error) { RetainFailure(admissionFailures, error); }
        }
        finally { admission.SetResult(); }
        // Return this admitted task even if a callback has already installed a successor.
        return original;
    }

    private async Task PrepareAndPublishAsync(Task admission, IReadOnlyList<Exception> admissionFailures,
        Task previous, CancellationTokenSource? previousCancellation,
        CancellationToken previousToken, GraphDefinition captured, long generation, CancellationToken token)
    {
        await admission.ConfigureAwait(false);
        try
        {
            await PrepareAndPublishCoreAsync(admissionFailures, previous, previousCancellation, previousToken,
                captured, generation, token).ConfigureAwait(false);
        }
        catch (Exception original)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!_disposed && generation == _generation)
                        SetStatus(original is OperationCanceledException error && token.IsCancellationRequested && error.CancellationToken == token
                            ? "Mathematical graph preparation canceled" : "Mathematical graph preparation failed");
                });
            }
            catch (Exception publicationFailure)
            { throw new AggregateException("PreparedGraphFailurePublicationFailed", original, publicationFailure); }
            throw;
        }
    }
    private async Task PrepareAndPublishCoreAsync(IReadOnlyList<Exception> admissionFailures,
        Task previous, CancellationTokenSource? previousCancellation,
        CancellationToken previousToken, GraphDefinition captured, long generation, CancellationToken token)
    {
        var failures = new List<Exception>(admissionFailures);
        try { await previous.ConfigureAwait(false); }
        catch (OperationCanceledException error) when (previousCancellation?.IsCancellationRequested == true &&
            error.CancellationToken == previousToken)
        { /* Only cancellation of that exact original replaced request is expected. */ }
        catch (Exception error) { RetainFailure(failures, error); }
        ThrowOriginalFailures("PreparedGraphAdmissionAndOriginalDrainFailed", failures);
        token.ThrowIfCancellationRequested();
        var prepared = await _preparer.PrepareAsync(captured, _policy, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || generation != _generation || token.IsCancellationRequested ||
                PreparedGraphPlot.Digest(_source) != PreparedGraphPlot.Digest(captured)) return;
            if (prepared.GraphBodyDigest != PreparedGraphPlot.Digest(captured) ||
                prepared.GraphID != captured.GraphID || prepared.GraphRevision != captured.Revision ||
                prepared.Policy != _policy)
                throw new InvalidDataException("StaleOrForeignPreparedGraph");
            // Maintained Scatter sources retain arrays. Each path receives its own
            // owned array, never mutated after handoff, and never a function callback.
            var paths = prepared.Segments.Select(segment => (segment.IncludeBoundary,
                Coordinates: segment.Points.Select(point => new ScottPlot.Coordinates(point.X, point.Y)).ToArray())).ToArray();
            var regions = prepared.ShadedRegions.Select(region => region.Boundary.Select(point =>
                new ScottPlot.Coordinates(point.X, point.Y)).ToArray()).ToArray();
            lock (_plot.Plot.Sync)
            {
                ScottPlotGraphAdapter.Render(_plot, captured);
                foreach (var region in regions)
                {
                    var polygon = _plot.Plot.Add.Polygon(region); polygon.LineWidth = 0;
                    polygon.FillColor = polygon.FillColor.WithAlpha(.2);
                }
                foreach (var path in paths)
                {
                    var scatter = _plot.Plot.Add.ScatterLine(path.Coordinates);
                    scatter.LinePattern = path.IncludeBoundary ? ScottPlot.LinePattern.Solid : ScottPlot.LinePattern.Dashed;
                }
            }
            _prepared = prepared; _plot.Refresh();
            SetStatus(prepared.Refusals.Count == 0 ? "Prepared mathematical graph" :
                "Some expressions could not be plotted. " +
                string.Join(" ", prepared.Refusals.Select(item => DescribeRefusal(item.Diagnostic)).Distinct(StringComparer.Ordinal)));
        });
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Status" => _status, "Description" => _source.AccessibleDescription,
            "Expressions" => string.Join(Environment.NewLine, _source.Expressions.Select(expression => expression.LaTeX)),
            _ => null
        };
        return value is not null;
    }
    private static string DescribeRefusal(string diagnostic) => diagnostic switch
    {
        "MathParseError" => "Check the formula's notation.",
        "GraphExplicitYRelationUnsupported" or "GraphEquationRelationUnsupported" or "GraphInequalityRelationUnsupported" =>
            "Write an equation as y = an expression, or use y with a single inequality sign.",
        "GraphInequalityBoundaryMismatch" => "The inequality sign must agree with whether its boundary is included.",
        "GraphOriginalZeroSetUnclassified" or "GraphRationalEntityUnsupported" =>
            "This formula's domain is not supported by the current plotter.",
        "MathOriginalDomainEmptyOrIndeterminate" => "The formula is undefined throughout the selected domain.",
        "GraphPlotVisibleDomainEmpty" => "The formula's domain does not intersect the horizontal range.",
        "GraphRangeBelowDisplayPrecision" or "GraphSampleRangeBelowDisplayPrecision" => "Choose a wider horizontal range.",
        _ => "Use a simpler formula or a smaller range within the current plotting limits."
    };
    private void SetStatus(string value)
    { _status = value; PropertyChanged?.Invoke(this, new("Status")); }
    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs args) =>
        BeginOriginalRetirement();
    public ValueTask DisposeAsync()
    {
        RequireUiThread(); return new(BeginOriginalRetirement());
    }
    private Task BeginOriginalRetirement()
    {
        if (_disposal is { } retained) return retained;
        var preparation = _preparation; var current = _currentCancellation;
        var token = current?.Token ?? CancellationToken.None;
        var admission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        var original = RetireAsync(admission.Task, failures, preparation, current, token);
        // Publish/coalesce before the synchronous cancellation callback can reenter.
        // Keep cancellation callbacks on the original UI thread and drain that task.
        _disposal = original; _disposed = true;
        try
        {
            try { DetachedFromVisualTree -= OnDetached; } catch (Exception error) { RetainFailure(failures, error); }
            try { current?.Cancel(); } catch (Exception error) { RetainFailure(failures, error); }
        }
        finally { admission.SetResult(); }
        return original;
    }
    private async Task RetireAsync(Task admission, IReadOnlyList<Exception> admissionFailures,
        Task original, CancellationTokenSource? current, CancellationToken token)
    {
        await admission.ConfigureAwait(false);
        var failures = new List<Exception>(admissionFailures);
        try { await original.ConfigureAwait(false); }
        catch (OperationCanceledException error) when (current?.IsCancellationRequested == true && error.CancellationToken == token)
        { /* Exact current-request cancellation during this original drain only. */ }
        catch (Exception error) { RetainFailure(failures, error); }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try { _loader.Dispose(); } catch (Exception error) { RetainFailure(failures, error); }
                try { Content = null; } catch (Exception error) { RetainFailure(failures, error); }
                try { lock (_plot.Plot.Sync) _plot.Plot.Dispose(); } catch (Exception error) { RetainFailure(failures, error); }
                _prepared = null; PropertyChanged = null;
                foreach (var request in _requests)
                { try { request.Dispose(); } catch (Exception error) { RetainFailure(failures, error); } }
                _requests.Clear();
            });
        }
        catch (Exception error) { RetainFailure(failures, error); }
        if (failures.Count != 0) throw new AggregateException("PreparedGraphOriginalDrainFailed", failures);
    }
    private static void RetainFailure(List<Exception> failures, Exception error)
    {
        if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error);
    }
    private static void ThrowOriginalFailures(string message, IReadOnlyList<Exception> failures)
    {
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(message, failures);
    }
    private void RequireNativeRange(GraphDefinition graph)
    {
        var axes = graph.Axes;
        if (new[] { axes.XMinimum, axes.XMaximum, axes.YMinimum, axes.YMaximum }
            .Any(value => Math.Abs((double)value) > _policy.MaxDisplayMagnitude))
            throw new NotSupportedException("GraphRangeBeyondDisplayPolicy");
        foreach (var primitive in graph.Primitives)
        {
            GraphCoordinate[] points = primitive switch
            {
                GraphPoint point => [point.Position], GraphLine line => [line.Start, line.End],
                GraphCurve curve => curve.Points, GraphCoordinateTable table => table.Points,
                GraphRegion region => region.Boundary, _ => []
            };
            if (points.Any(point => Math.Abs((double)point.X) > _policy.MaxDisplayMagnitude ||
                Math.Abs((double)point.Y) > _policy.MaxDisplayMagnitude))
                throw new NotSupportedException("GraphCoordinateBeyondDisplayPolicy");
        }
    }
    private static void RequireUiThread()
    {
        if (!Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("PreparedGraphNativeThreadRequired");
    }
    public const string CuiSource = """
        <Cui>
          <StackPanel spacing="8" margin="12">
            <Object Type="math.prepared-graph" id="math-prepared-graph" />
            <TextBlock id="math-prepared-description" text="{Binding Description}" text-wrapping="Wrap" />
            <TextBlock id="math-prepared-expressions" text="{Binding Expressions}" text-wrapping="Wrap" />
            <TextBlock id="math-prepared-status" text="{Binding Status}" text-wrapping="Wrap" />
          </StackPanel>
        </Cui>
        """;
}
