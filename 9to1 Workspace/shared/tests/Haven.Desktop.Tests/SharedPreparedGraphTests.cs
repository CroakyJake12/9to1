using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Application.Mathematics;
using Haven.Core.Mathematics;
using Haven.Desktop.Mathematics;
using ScottPlot.Avalonia;

namespace Haven.Desktop.Tests;

public sealed class SharedPreparedGraphTests
{
    [Theory]
    [InlineData(@"\frac{1}{x}", "0/1", 0d)]
    [InlineData(@"\frac{x}{x}", "0/1", 0d)]
    [InlineData(@"\frac{0}{x}", "0/1", 0d)]
    [InlineData(@"\frac{1}{\frac{1}{x}}", "0/1", 0d)]
    [InlineData(@"x^{0}", "0/1", 0d)]
    [InlineData(@"\frac{1}{x-0.125}", "1/8", 0.125d)]
    public async Task Original_rational_holes_split_real_prepared_paths_before_display_sampling(
        string source, string exactRoot, double displayRoot)
    {
        var graph = Graph(source); var bytes = MathObjectCodec.Encode(graph);
        var prepared = await new RationalGraphPlotPreparer().PrepareAsync(graph,
            new(SamplesPerInterval: 16), TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Refusals);
        var excluded = Assert.Single(prepared.OriginalExclusions);
        Assert.Equal(exactRoot, excluded.ExactAbscissa); Assert.Equal(displayRoot, excluded.DisplayAbscissa);
        Assert.Equal(graph.GraphID, prepared.GraphID); Assert.Equal(graph.Revision, prepared.GraphRevision);
        Assert.Equal(PreparedGraphPlot.Digest(graph), prepared.GraphBodyDigest);
        Assert.True(prepared.Segments.Count >= 2);
        Assert.All(prepared.Segments, segment =>
        {
            Assert.Equal(graph.Primitives[0].PrimitiveID, segment.PrimitiveID);
            Assert.Equal(((GraphFunction)graph.Primitives[0]).Expression, segment.Expression);
            Assert.True(segment.Points.All(point => point.X < displayRoot) || segment.Points.All(point => point.X > displayRoot));
            Assert.All(segment.Points, point => Assert.True(double.IsFinite(point.X) && double.IsFinite(point.Y)));
        });
        Assert.Equal(bytes, MathObjectCodec.Encode(graph));
        Assert.Equal(bytes, MathObjectCodec.Encode(prepared.CaptureSource()));
    }

    [Fact]
    public async Task Original_product_divisor_roots_split_three_paths_inside_the_declared_domain()
    {
        var graph = Graph(@"\frac{1}{(x-0.5)(x+0.5)}");
        graph = graph with { Primitives = [((GraphFunction)graph.Primitives[0]) with { Domain = new(-1, 1) }] };
        var prepared = await new RationalGraphPlotPreparer().PrepareAsync(graph,
            new(SamplesPerInterval: 32), TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Refusals);
        Assert.Equal(new[] { "-1/2", "1/2" }, prepared.OriginalExclusions.Select(item => item.ExactAbscissa));
        Assert.True(prepared.Segments.Count >= 3);
        Assert.All(prepared.Segments, segment =>
        {
            Assert.All(segment.Points, point => Assert.InRange(point.X, -1d, 1d));
            Assert.True(segment.Points.All(point => point.X < -0.5) ||
                segment.Points.All(point => point.X > -0.5 && point.X < 0.5) || segment.Points.All(point => point.X > 0.5));
        });
    }

    [Theory]
    [InlineData("e")]
    [InlineData("i")]
    public async Task Declared_graph_variable_remains_real_input_without_parser_constant_substitution(string variable)
    {
        var graph = Graph(variable + "^{2}");
        graph = graph with { Primitives = [((GraphFunction)graph.Primitives[0]) with { Variable = variable }] };
        var prepared = await new RationalGraphPlotPreparer().PrepareAsync(graph,
            new(SamplesPerInterval: 16), TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Refusals); Assert.Empty(prepared.OriginalExclusions);
        var segment = Assert.Single(prepared.Segments);
        Assert.Contains(segment.Points, point => point.X == 0 && point.Y == 0);
        Assert.Equal(4d, segment.Points[0].Y); Assert.Equal(4d, segment.Points[^1].Y);
        Assert.Equal(variable, Assert.IsType<GraphFunction>(prepared.CaptureSource().Primitives[0]).Variable);
    }

    [Fact]
    public async Task Exact_nonzero_decimal_denominator_keeps_values_and_multiple_series_bind_exact_versions()
    {
        var first = new MathExpression(Guid.NewGuid(), 3, @"\frac{x}{0.00000000000000000001}");
        var second = new MathExpression(Guid.NewGuid(), 9, "x^{2}");
        var a = new GraphFunction(Guid.NewGuid(), new(first.ExpressionID, first.Revision));
        var b = new GraphFunction(Guid.NewGuid(), new(second.ExpressionID, second.Revision), Domain: new(-1, 1));
        var graph = new GraphDefinition(Guid.NewGuid(), 4, new(-2, 2, -5, 5), [first, second], [a, b], []);
        var prepared = await new RationalGraphPlotPreparer().PrepareAsync(graph,
            new(SamplesPerInterval: 16, ExpressionLimits: new(MaxLiteralDigits: 24)), TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Refusals); Assert.Empty(prepared.OriginalExclusions);
        // The first series has only one displayable sample and cannot create a
        // connecting line. That clipping does not invent a denominator hole.
        Assert.DoesNotContain(prepared.Segments, segment => segment.PrimitiveID == a.PrimitiveID);
        var quadratic = Assert.Single(prepared.Segments, segment => segment.PrimitiveID == b.PrimitiveID);
        Assert.Equal(b.Expression, quadratic.Expression);
        Assert.Equal(-1d, quadratic.Points[0].X); Assert.Equal(1d, quadratic.Points[^1].X);
        Assert.Contains(quadratic.Points, point => point.X == 0 && point.Y == 0);
        Assert.Equal(PreparedGraphPlot.Digest(graph), prepared.GraphBodyDigest);
    }

    [Theory]
    [InlineData(@"\frac{1}{x^{2}+1}", "GraphOriginalZeroSetUnclassified")]
    [InlineData(@"\frac{1}{0}", "MathOriginalDomainEmptyOrIndeterminate")]
    [InlineData(@"0^{0}", "MathOriginalDomainEmptyOrIndeterminate")]
    [InlineData(@"\sin{x}", "RationalSyntaxUnsupported")]
    public async Task Unsupported_original_domain_or_function_scope_refuses_without_partial_paths(string source, string diagnostic)
    {
        var graph = Graph(source); var original = MathObjectCodec.Encode(graph);
        var prepared = await new RationalGraphPlotPreparer().PrepareAsync(graph, new(), TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Segments); Assert.Empty(prepared.OriginalExclusions);
        Assert.Equal(diagnostic, Assert.Single(prepared.Refusals).Diagnostic);
        Assert.Equal(original, MathObjectCodec.Encode(prepared.CaptureSource()));
    }

    [Fact]
    public async Task Explicit_equation_and_inequality_keep_boundary_semantics_and_classified_shading()
    {
        var equation = new MathExpression(Guid.NewGuid(), 2, "y=x^{2}");
        var strict = new MathExpression(Guid.NewGuid(), 5, "y>0");
        var inclusive = new MathExpression(Guid.NewGuid(), 6, @"y\leq 0");
        var a = new GraphEquation(Guid.NewGuid(), new(equation.ExpressionID, equation.Revision));
        var b = new GraphInequality(Guid.NewGuid(), new(strict.ExpressionID, strict.Revision), IncludeBoundary: false);
        var c = new GraphInequality(Guid.NewGuid(), new(inclusive.ExpressionID, inclusive.Revision));
        var graph = new GraphDefinition(Guid.NewGuid(), 8, new(-2, 2, -5, 5),
            [equation, strict, inclusive], [a, b, c], []);
        var original = MathObjectCodec.Encode(graph);
        var prepared = await new RationalGraphPlotPreparer().PrepareAsync(graph,
            new(SamplesPerInterval: 16), TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Refusals); Assert.Empty(prepared.OriginalExclusions);
        Assert.True(Assert.Single(prepared.Segments, segment => segment.PrimitiveID == a.PrimitiveID).IncludeBoundary);
        Assert.False(Assert.Single(prepared.Segments, segment => segment.PrimitiveID == b.PrimitiveID).IncludeBoundary);
        Assert.True(Assert.Single(prepared.Segments, segment => segment.PrimitiveID == c.PrimitiveID).IncludeBoundary);
        var above = Assert.Single(prepared.ShadedRegions, region => region.PrimitiveID == b.PrimitiveID);
        var below = Assert.Single(prepared.ShadedRegions, region => region.PrimitiveID == c.PrimitiveID);
        Assert.False(above.IncludeBoundary); Assert.True(below.IncludeBoundary);
        Assert.All(above.Boundary, point => Assert.InRange(point.Y, 0d, 5d));
        Assert.All(below.Boundary, point => Assert.InRange(point.Y, -5d, 0d));
        Assert.Equal(original, MathObjectCodec.Encode(prepared.CaptureSource()));
        Assert.Equal(original, MathObjectCodec.Encode(graph));
    }

    [Theory]
    [InlineData("y<0", true, "GraphInequalityBoundaryMismatch")]
    [InlineData(@"y\leq 0", false, "GraphInequalityBoundaryMismatch")]
    [InlineData("y<=0", true, "GraphExplicitYRelationUnsupported")]
    [InlineData("y≤0", true, "GraphExplicitYRelationUnsupported")]
    [InlineData("x+y<0", false, "GraphExplicitYRelationUnsupported")]
    [InlineData("y<x<1", false, "GraphExplicitYRelationUnsupported")]
    public async Task Contradictory_boundary_and_noncanonical_relation_atoms_refuse_without_guessing(
        string source, bool includeBoundary, string diagnostic)
    {
        var expression = new MathExpression(Guid.NewGuid(), 1, source);
        var graph = new GraphDefinition(Guid.NewGuid(), 1, new(-2, 2, -5, 5), [expression],
            [new GraphInequality(Guid.NewGuid(), new(expression.ExpressionID, expression.Revision), includeBoundary)], []);
        var prepared = await new RationalGraphPlotPreparer().PrepareAsync(graph, new(), TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Segments); Assert.Empty(prepared.ShadedRegions);
        Assert.Equal(diagnostic, Assert.Single(prepared.Refusals).Diagnostic);
        Assert.Equal(MathObjectCodec.Encode(graph), MathObjectCodec.Encode(prepared.CaptureSource()));
    }

    [Fact]
    public async Task Shading_retains_original_denominator_holes_even_when_between_regular_sample_positions()
    {
        var expression = new MathExpression(Guid.NewGuid(), 1, @"y\geq\frac{x-0.125}{x-0.125}");
        var graph = new GraphDefinition(Guid.NewGuid(), 1, new(-2, 2, -5, 5), [expression],
            [new GraphInequality(Guid.NewGuid(), new(expression.ExpressionID, expression.Revision))], []);
        var prepared = await new RationalGraphPlotPreparer().PrepareAsync(graph,
            new(SamplesPerInterval: 16), TestContext.Current.CancellationToken);
        Assert.Empty(prepared.Refusals); Assert.Equal("1/8", Assert.Single(prepared.OriginalExclusions).ExactAbscissa);
        Assert.Equal(2, prepared.ShadedRegions.Count);
        Assert.All(prepared.ShadedRegions, region =>
            Assert.True(region.Boundary.All(point => point.X < 0.125) || region.Boundary.All(point => point.X > 0.125)));
        Assert.All(prepared.Segments, segment => Assert.True(segment.IncludeBoundary));
    }

    [Fact]
    public async Task Preparation_enforces_actual_operation_budget_and_original_cancellation()
    {
        var graph = Graph("x^{2}");
        var result = await new RationalGraphPlotPreparer().PrepareAsync(graph,
            new(MaxOperations: 1), TestContext.Current.CancellationToken);
        Assert.Empty(result.Segments); Assert.Equal("GraphOperationBudgetExceeded", Assert.Single(result.Refusals).Diagnostic);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new RationalGraphPlotPreparer().PrepareAsync(graph, new(), canceled.Token));
        Assert.Equal(canceled.Token, error.CancellationToken);
    }

    [Fact]
    public async Task Authored_native_view_renders_actual_prepared_paths_and_retains_canonical_bytes()
    {
        await WithOriginalNativeSession(async () =>
        {
            var graph = Graph(@"\frac{1}{x-0.125}"); var bytes = MathObjectCodec.Encode(graph);
            var view = new SharedPreparedGraphViewControl(graph, new RationalGraphPlotPreparer(), new(SamplesPerInterval: 32));
            var window = new Window { Content = view, Width = 720, Height = 500 };
            Exception? primary = null; var failures = new List<Exception>(); Task? originalTask = null;
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                using var before = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
                var beforeBytes = Pixels(before);
                originalTask = view.SetSourceAsync(graph, TestContext.Current.CancellationToken);
                Assert.Same(originalTask, view.OriginalPreparationTask); await originalTask;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var prepared = Assert.IsType<PreparedGraphPlot>(view.Prepared);
                Assert.Equal("1/8", Assert.Single(prepared.OriginalExclusions).ExactAbscissa);
                var native = Assert.Single(view.GetVisualDescendants().OfType<AvaPlot>());
                Assert.True(native.Plot.LastRender.DataRect.HasArea);
                Assert.Equal(prepared.Segments.Count, native.Plot.GetPlottables().OfType<ScottPlot.Plottables.Scatter>().Count());
                using var after = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
                var afterBytes = Pixels(after);
                Assert.NotEqual(beforeBytes, afterBytes);
                Assert.True(afterBytes.Where((_, index) => index % 4 != 3).Distinct().Count() >= 16);
                var status = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), control => control.Name == "math-prepared-status");
                Assert.Equal("Prepared mathematical graph", status.Text);
                var textual = Assert.Single(view.GetVisualDescendants().OfType<TextBlock>(), control => control.Name == "math-prepared-expressions");
                Assert.Equal(graph.Expressions[0].LaTeX, textual.Text);
                Assert.Equal(bytes, MathObjectCodec.Encode(view.CaptureSource()));
                Assert.Equal(bytes, MathObjectCodec.Encode(graph));
            }
            catch (Exception error) { primary = error; }
            finally
            {
                await CollectOriginalTaskFailure(originalTask, failures);
                try { await view.DisposeAsync(); } catch (Exception error) { Retain(failures, error); }
                try { window.Close(); } catch (Exception error) { Retain(failures, error); }
            }
            ThrowPreserved(primary, failures);
        });
    }

    [Fact]
    public async Task Native_inequality_renders_owned_fill_with_dashed_strict_boundary()
    {
        await WithOriginalNativeSession(async () =>
        {
            var expression = new MathExpression(Guid.NewGuid(), 2, "y>x");
            var graph = new GraphDefinition(Guid.NewGuid(), 9, new(-2, 2, -5, 5), [expression],
                [new GraphInequality(Guid.NewGuid(), new(expression.ExpressionID, expression.Revision), IncludeBoundary: false)], []);
            var view = new SharedPreparedGraphViewControl(graph, new RationalGraphPlotPreparer());
            var window = new Window { Content = view, Width = 720, Height = 500 };
            Exception? primary = null; var failures = new List<Exception>(); Task? originalTask = null;
            try
            {
                window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                originalTask = view.SetSourceAsync(graph, TestContext.Current.CancellationToken); await originalTask;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var prepared = Assert.IsType<PreparedGraphPlot>(view.Prepared);
                var native = Assert.Single(view.GetVisualDescendants().OfType<AvaPlot>());
                var scatter = Assert.Single(native.Plot.GetPlottables().OfType<ScottPlot.Plottables.Scatter>());
                Assert.Equal(ScottPlot.LinePattern.Dashed, scatter.LinePattern);
                var polygon = Assert.Single(native.Plot.GetPlottables().OfType<ScottPlot.Plottables.Polygon>());
                Assert.Equal(0f, polygon.LineWidth); Assert.False(Assert.Single(prepared.ShadedRegions).IncludeBoundary);
                using var frame = Assert.IsAssignableFrom<Bitmap>(window.CaptureRenderedFrame());
                Assert.True(Pixels(frame).Where((_, index) => index % 4 != 3).Distinct().Count() >= 16);
                Assert.Equal(MathObjectCodec.Encode(graph), MathObjectCodec.Encode(view.CaptureSource()));
            }
            catch (Exception error) { primary = error; }
            finally
            {
                await CollectOriginalTaskFailure(originalTask, failures);
                try { await view.DisposeAsync(); } catch (Exception error) { Retain(failures, error); }
                try { window.Close(); } catch (Exception error) { Retain(failures, error); }
            }
            ThrowPreserved(primary, failures);
        });
    }

    [Fact]
    public async Task Native_replacement_waits_the_original_task_and_refuses_same_identity_different_body_result()
    {
        await WithOriginalNativeSession(async () =>
        {
            var graph = Graph("x");
            var changed = graph with { Expressions = [graph.Expressions[0] with { LaTeX = "x^{2}" }] };
            var controlled = new HeldPreparer();
            var view = new SharedPreparedGraphViewControl(graph, controlled);
            Task? original = null, replacement = null, disposal = null;
            Exception? primary = null, originalCancellation = null, mismatchFailure = null, disposalFailure = null;
            var failures = new List<Exception>(); var verifiedExpectedFailures = false;
            try
            {
                original = view.SetSourceAsync(graph, TestContext.Current.CancellationToken);
                await controlled.FirstEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                replacement = view.SetSourceAsync(changed, TestContext.Current.CancellationToken);
                Assert.False(original.IsCompleted); Assert.False(controlled.SecondEntered.Task.IsCompleted);
                controlled.First.TrySetResult(EmptyPrepared(graph));
                originalCancellation = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
                await controlled.SecondEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                // This result has the requested ID/revision but the old full body.
                controlled.Second.TrySetResult(EmptyPrepared(graph));
                var mismatch = await Assert.ThrowsAsync<InvalidDataException>(() => replacement); mismatchFailure = mismatch;
                Assert.Equal("StaleOrForeignPreparedGraph", mismatch.Message); Assert.Null(view.Prepared);
                Assert.True(view.TryGetValue("Status", out var status)); Assert.Equal("Mathematical graph preparation failed", status);
                Assert.Equal(MathObjectCodec.Encode(changed), MathObjectCodec.Encode(view.CaptureSource()));
                disposal = view.DisposeAsync().AsTask();
                var cleanup = await Assert.ThrowsAsync<AggregateException>(() => disposal); disposalFailure = cleanup;
                Assert.Contains(cleanup.InnerExceptions, error => ReferenceEquals(error, mismatch));
                Assert.All(cleanup.InnerExceptions, error => Assert.Same(mismatch, error));
                Assert.Same(disposal, view.OriginalDisposalTask); verifiedExpectedFailures = true;
            }
            catch (Exception error) { primary = error; }
            finally
            {
                controlled.First.TrySetResult(EmptyPrepared(graph)); controlled.Second.TrySetResult(EmptyPrepared(graph));
                await CollectOriginalTaskFailure(original, failures, verifiedExpectedFailures ? originalCancellation : null);
                await CollectOriginalTaskFailure(replacement, failures, verifiedExpectedFailures ? mismatchFailure : null);
                try { disposal ??= view.DisposeAsync().AsTask(); } catch (Exception error) { Retain(failures, error); }
                await CollectOriginalTaskFailure(disposal, failures, verifiedExpectedFailures ? disposalFailure : null);
            }
            ThrowPreserved(primary, failures);
        });
    }

    [Fact]
    public async Task Native_disposal_waits_pending_original_preparation_and_preserves_unrelated_cancellation()
    {
        await WithOriginalNativeSession(async () =>
        {
            var graph = Graph("x"); var held = new HeldPreparer();
            var view = new SharedPreparedGraphViewControl(graph, held);
            Task? task = null, drain = null; Exception? primary = null, originalFailure = null, disposalFailure = null;
            var failures = new List<Exception>(); var verifiedExpectedFailures = false;
            using var unrelated = new CancellationTokenSource(); unrelated.Cancel();
            var failure = new OperationCanceledException("UnrelatedOriginalCancellation", unrelated.Token);
            try
            {
                task = view.SetSourceAsync(graph, TestContext.Current.CancellationToken);
                await held.FirstEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                drain = view.DisposeAsync().AsTask();
                Assert.False(drain.IsCompleted); Assert.False(task.IsCompleted);
                held.First.TrySetException(failure);
                var original = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task); originalFailure = original;
                Assert.Same(failure, original);
                var cleanup = await Assert.ThrowsAsync<AggregateException>(() => drain); disposalFailure = cleanup;
                Assert.Contains(cleanup.InnerExceptions, error => ReferenceEquals(error, failure));
                Assert.All(cleanup.InnerExceptions, error => Assert.Same(failure, error));
                Assert.Same(drain, view.OriginalDisposalTask); Assert.Null(view.Content); Assert.Null(view.Prepared);
                Assert.Throws<ObjectDisposedException>(() => { view.SetSourceAsync(graph, TestContext.Current.CancellationToken); });
                verifiedExpectedFailures = true;
            }
            catch (Exception error) { primary = error; }
            finally
            {
                held.First.TrySetResult(EmptyPrepared(graph));
                await CollectOriginalTaskFailure(task, failures, verifiedExpectedFailures ? originalFailure : null);
                try { drain ??= view.DisposeAsync().AsTask(); } catch (Exception error) { Retain(failures, error); }
                await CollectOriginalTaskFailure(drain, failures, verifiedExpectedFailures ? disposalFailure : null);
            }
            ThrowPreserved(primary, failures);
        });
    }

    [Fact]
    public async Task Throwing_property_callback_retains_the_new_original_task_and_drains_the_previous_preparation()
    {
        await WithOriginalNativeSession(async () =>
        {
            var graph = Graph("x"); var changed = graph with { Revision = graph.Revision + 1 };
            var held = new HeldPreparer(); var view = new SharedPreparedGraphViewControl(graph, held);
            var callbackFailure = new InvalidOperationException("OriginalPropertyCallbackFailure");
            Task? first = null, second = null, disposal = null; Exception? primary = null, firstFailure = null, secondFailure = null, disposalFailure = null;
            var failures = new List<Exception>(); var verified = false;
            try
            {
                first = view.SetSourceAsync(graph, TestContext.Current.CancellationToken);
                await held.FirstEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                view.PropertyChanged += (_, args) => { if (args.PropertyName == "Description") throw callbackFailure; };
                second = view.SetSourceAsync(changed, TestContext.Current.CancellationToken);
                Assert.Same(second, view.OriginalPreparationTask); Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
                Assert.Equal(MathObjectCodec.Encode(changed), MathObjectCodec.Encode(view.CaptureSource()));
                held.First.TrySetResult(EmptyPrepared(graph));
                firstFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
                secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => second);
                Assert.Same(callbackFailure, secondFailure); Assert.False(held.SecondEntered.Task.IsCompleted);
                Assert.Null(view.Prepared); Assert.True(view.TryGetValue("Status", out var status));
                Assert.Equal("Mathematical graph preparation failed", status);
                disposal = view.DisposeAsync().AsTask();
                var error = await Assert.ThrowsAsync<AggregateException>(() => disposal); disposalFailure = error;
                Assert.All(error.InnerExceptions, item => Assert.Same(callbackFailure, item));
                Assert.Contains(error.InnerExceptions, item => ReferenceEquals(item, callbackFailure));
                verified = true;
            }
            catch (Exception error) { primary = error; }
            finally
            {
                held.First.TrySetResult(EmptyPrepared(graph)); held.Second.TrySetResult(EmptyPrepared(changed));
                await CollectOriginalTaskFailure(first, failures, verified ? firstFailure : null);
                await CollectOriginalTaskFailure(second, failures, verified ? secondFailure : null);
                try { disposal ??= view.DisposeAsync().AsTask(); } catch (Exception error) { Retain(failures, error); }
                await CollectOriginalTaskFailure(disposal, failures, verified ? disposalFailure : null);
            }
            ThrowPreserved(primary, failures);
        });
    }

    [Fact]
    public async Task Reentrant_property_retirement_observes_and_drains_the_same_already_admitted_preparation()
    {
        await WithOriginalNativeSession(async () =>
        {
            var graph = Graph("x"); var bytes = MathObjectCodec.Encode(graph); var held = new HeldPreparer();
            var view = new SharedPreparedGraphViewControl(graph, held);
            Task? original = null, retirement = null, callbackPreparation = null;
            Exception? primary = null, originalFailure = null; var failures = new List<Exception>(); var verified = false;
            var observedSameRetirement = false; var observedPending = false;
            view.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != "Description") return;
                callbackPreparation = view.OriginalPreparationTask;
                retirement = view.DisposeAsync().AsTask();
                observedSameRetirement = ReferenceEquals(retirement, view.OriginalDisposalTask);
                observedPending = !retirement.IsCompleted;
            };
            try
            {
                original = view.SetSourceAsync(graph, TestContext.Current.CancellationToken);
                Assert.Same(original, callbackPreparation); Assert.True(observedSameRetirement); Assert.True(observedPending);
                Assert.Same(retirement, view.DisposeAsync().AsTask());
                originalFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
                await Assert.IsAssignableFrom<Task>(retirement);
                Assert.False(held.FirstEntered.Task.IsCompleted); Assert.Null(view.Content); Assert.Null(view.Prepared);
                Assert.Equal(bytes, MathObjectCodec.Encode(view.CaptureSource())); verified = true;
            }
            catch (Exception error) { primary = error; }
            finally
            {
                held.First.TrySetResult(EmptyPrepared(graph));
                await CollectOriginalTaskFailure(original, failures, verified ? originalFailure : null);
                try { retirement ??= view.DisposeAsync().AsTask(); } catch (Exception error) { Retain(failures, error); }
                await CollectOriginalTaskFailure(retirement, failures);
            }
            ThrowPreserved(primary, failures);
        });
    }

    [Fact]
    public async Task Throwing_cancellation_callback_reuses_the_same_original_retirement_and_preserves_its_failure()
    {
        await WithOriginalNativeSession(async () =>
        {
            var graph = Graph("x"); var held = new HeldPreparer(); var view = new SharedPreparedGraphViewControl(graph, held);
            var callbackFailure = new InvalidOperationException("OriginalCancellationCallbackFailure");
            Task? original = null, retirement = null, reentered = null;
            Exception? primary = null, originalFailure = null, retirementFailure = null;
            CancellationTokenRegistration registration = default; var failures = new List<Exception>(); var verified = false;
            var observedPending = false; var callbackOnUi = false;
            try
            {
                original = view.SetSourceAsync(graph, TestContext.Current.CancellationToken);
                await held.FirstEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
                registration = held.FirstToken.Register(() =>
                {
                    callbackOnUi = Dispatcher.UIThread.CheckAccess();
                    reentered = view.DisposeAsync().AsTask(); observedPending = !reentered.IsCompleted;
                    throw callbackFailure;
                });
                retirement = view.DisposeAsync().AsTask();
                Assert.True(callbackOnUi); Assert.True(observedPending); Assert.Same(retirement, reentered);
                Assert.Same(retirement, view.OriginalDisposalTask); Assert.False(original.IsCompleted); Assert.False(retirement.IsCompleted);
                held.First.TrySetResult(EmptyPrepared(graph));
                originalFailure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original);
                var failure = await Assert.ThrowsAsync<AggregateException>(() => retirement); retirementFailure = failure;
                var actual = failure.Flatten().InnerExceptions;
                Assert.Contains(actual, item => ReferenceEquals(item, callbackFailure));
                Assert.All(actual, item => Assert.Same(callbackFailure, item));
                Assert.Null(view.Content); Assert.Null(view.Prepared); verified = true;
            }
            catch (Exception error) { primary = error; }
            finally
            {
                held.First.TrySetResult(EmptyPrepared(graph));
                await CollectOriginalTaskFailure(original, failures, verified ? originalFailure : null);
                try { retirement ??= view.DisposeAsync().AsTask(); } catch (Exception error) { Retain(failures, error); }
                await CollectOriginalTaskFailure(retirement, failures, verified ? retirementFailure : null);
                try { registration.Dispose(); } catch (Exception error) { Retain(failures, error); }
            }
            ThrowPreserved(primary, failures);
        });
    }

    private static async Task WithOriginalNativeSession(Func<Task> callback)
    {
        var session = HeadlessUnitTestSession.StartNew(typeof(HomeProductivityCuiSurfaceTests.PixelAppBuilder));
        Task? originalCallback = null; Task<bool>? originalDispatch = null;
        Exception? primary = null; var failures = new List<Exception>();
        try
        {
            // The maintained API has no Func<Task> overload. This exact typed
            // dispatch awaits the SAME callback task inside the actual UI frame.
            originalDispatch = session.Dispatch<bool>(async () =>
            {
                originalCallback = callback();
                await originalCallback;
                return true;
            }, CancellationToken.None);
            await originalDispatch;
        }
        catch (Exception error) { primary = error; }
        finally
        {
            await CollectOriginalTaskFailure(originalCallback, failures);
            await CollectOriginalTaskFailure(originalDispatch, failures);
            try { await session.DisposeAsync(); }
            catch (Exception error) { Retain(failures, error); }
        }
        ThrowPreserved(primary, failures);
    }

    private static async Task CollectOriginalTaskFailure(Task? original, List<Exception> failures, Exception? verifiedExpected = null)
    {
        if (original is null) return;
        try { await original; }
        catch (Exception error) { if (!ReferenceEquals(error, verifiedExpected)) Retain(failures, error); }
    }
    private static void Retain(List<Exception> failures, Exception error)
    {
        if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error);
    }
    private static void ThrowPreserved(Exception? primary, List<Exception> failures)
    {
        if (primary is not null && !failures.Any(item => ReferenceEquals(item, primary))) failures.Insert(0, primary);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }

    private static GraphDefinition Graph(string source)
    {
        var expression = new MathExpression(Guid.NewGuid(), 7, source);
        return new(Guid.NewGuid(), 3, new(-2, 2, -5, 5), [expression],
            [new GraphFunction(Guid.NewGuid(), new(expression.ExpressionID, expression.Revision))], []);
    }
    private static PreparedGraphPlot EmptyPrepared(GraphDefinition graph) => new(graph, new(), [], [], [], "ControlledNativePublicationFixture");
    // Controlled provider only exercises native original-task/whole-body fences.
    // Actual maintained parser/point preparation is exercised by the cases above.
    private sealed class HeldPreparer : IGraphPlotPreparer
    {
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PreparedGraphPlot> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<PreparedGraphPlot> Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public CancellationToken FirstToken { get; private set; }
        public ValueTask<PreparedGraphPlot> PrepareAsync(GraphDefinition graph, GraphPlotPreparationPolicy policy, CancellationToken token = default)
        {
            if (Interlocked.Increment(ref _calls) == 1) { FirstToken = token; FirstEntered.TrySetResult(); return new(First.Task); }
            SecondEntered.TrySetResult(); return new(Second.Task);
        }
    }
    private static byte[] Pixels(Bitmap bitmap)
    {
        var bytes = new byte[checked(bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4)];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), pinned.AddrOfPinnedObject(), bytes.Length, bitmap.PixelSize.Width * 4); }
        finally { pinned.Free(); }
        return bytes;
    }
}
