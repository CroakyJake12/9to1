namespace Haven.Application.Automations;

/// <summary>Runs Automation graphs in a graph-local simulation that cannot invoke physical-device side effects.</summary>
public static class AutomationGraphTestRunner
{
    public static Task<AutomationGraphRunResult> RunAsync(AutomationGraphDefinition graph, CancellationToken cancellationToken)
        => RunAsync(graph, cancellationToken, null);

    public static Task<AutomationGraphRunResult> RunAsync(AutomationGraphDefinition graph,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, AutomationGraphValue>>? triggerOutputs)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return new AutomationGraphRunner([new SimulatedDeviceNodeExecutor(), new BuiltInAutomationActionNodeExecutor()])
            .RunAsync(graph, AutomationGraphRunMode.Test, cancellationToken, triggerOutputs);
    }

    private sealed class SimulatedDeviceNodeExecutor : IAutomationGraphNodeExecutor
    {
        public bool CanExecute(AutomationGraphNodeDefinition node) =>
            string.Equals(node.Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase) && node.ToDevice() is not null;

        public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = context.Node.ToDevice();
            if (node is null) return Task.FromResult(new AutomationGraphNodeExecutionResult(false, "The DEVICE node is missing a target or action."));
            return Task.FromResult(new AutomationGraphNodeExecutionResult(false,
                $"Test cannot safely simulate device action '{node.ActionKey}' on {node.Target.DisplayName}; no device preview is available. Confirm an explicit real Run to perform this side effect."));
        }
    }
}
