namespace Haven.Application;

public sealed partial class ChatSessionService
{
    private static OllamaToolDefinition[] RestrictOriginalRequestToolDefinitions(
        IReadOnlyList<OllamaToolDefinition> sameFreshDefinitions, GenerationOptions? originalOptions)
    {
        var constraints = originalOptions?.RequestedToolSelectionConstraints;
        return constraints is null ? sameFreshDefinitions.ToArray() : sameFreshDefinitions
            .Where(definition => constraints.Allows(definition.Name)).ToArray();
    }
}
