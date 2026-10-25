using System.Globalization;
using Haven.Application.Automations;

namespace Haven.Application;

/// <summary>Per-request pure nodes in the existing Automation executor. Input is held by
/// this actual execution instance, never read from editable node configuration.</summary>
internal sealed class SpaceUserMessageGraphExecutor(string originalInput) : IAutomationGraphNodeExecutor
{
    public bool CanExecute(AutomationGraphNodeDefinition node) => node.Category == SpaceUserMessageGraph.Category &&
        node.ActionKey is SpaceUserMessageGraph.SourceType or SpaceUserMessageGraph.TruncateType or SpaceUserMessageGraph.OutputType;
    public Task<AutomationGraphNodeExecutionResult> ExecuteAsync(AutomationGraphNodeExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!CanExecute(context.Node)) throw new InvalidOperationException("No Space message executor owns this node.");
        if (context.Node.ActionKey == SpaceUserMessageGraph.SourceType) return Task.FromResult(Result(originalInput, "Read original user-message input."));
        if (context.TypedInputs is null || !context.TypedInputs.TryGetValue("in", out var inputs) || inputs.Count != 1 ||
            !inputs[0].TryGetString(out var text) || text is null)
            return Task.FromResult(new AutomationGraphNodeExecutionResult(false, "Exactly one present string input is required."));
        if (context.Node.ActionKey == SpaceUserMessageGraph.TruncateType)
        {
            var maximum = int.Parse(context.Node.Parameters["maximumTextElements"], CultureInfo.InvariantCulture);
            var offsets = StringInfo.ParseCombiningCharacters(text);
            text = offsets.Length <= maximum ? text : text[..offsets[maximum]];
            return Task.FromResult(Result(text, "Applied the configured Unicode text-element limit."));
        }
        return Task.FromResult(new AutomationGraphNodeExecutionResult(true, "Produced derived request text.", text));
    }
    private static AutomationGraphNodeExecutionResult Result(string text, string message) => new(true, message, text,
        TypedOutputs: new Dictionary<string, AutomationGraphValue> { ["out"] = AutomationGraphValue.FromString(text) });
}
