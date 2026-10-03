using Haven.Core.Mathematics;

namespace Haven.Application.Mathematics;

/// <summary>Prepares bounded observational geometry from exact captured source.
/// Implementations must await their actual preparation task, preserve original
/// domain exclusions and report unsupported primitives. Prepared samples do not
/// establish symbolic grading or domain equivalence.</summary>
public interface IGraphPlotPreparer
{
    ValueTask<PreparedGraphPlot> PrepareAsync(GraphDefinition graph,
        GraphPlotPreparationPolicy policy, CancellationToken cancellationToken = default);
}
