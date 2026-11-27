using Avalonia.Controls;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel
{
    // An authored click owns these exact choices before Home admission can suspend it.
    // Later text edits remain drafts; they cannot change an already submitted command.
    private sealed record CommandIntent(string Command, Item? Model,
        IReadOnlyDictionary<string, string?> Inputs, IReadOnlyDictionary<string, object?> Choices,
        int LoadPolicyIndex, bool Projects, string? InspectedRevision, string? LoadedProfiles,
        string? AdapterObservationModel, string? RollbackOperationId, string? RollbackOperationModel,
        long PromptRevision, string TargetSection, bool ShowSwarm)
    {
        internal string? Text(string key) => Inputs.GetValueOrDefault(key);
        internal Item? Selected(string key) => Choices.GetValueOrDefault(key) as Item;
        internal string Need(string key) => !string.IsNullOrWhiteSpace(Text(key)) ? Text(key)!.Trim()
            : throw new InvalidOperationException("Enter " + key.Replace('-', ' ') + ".");
    }

    private bool _runStopUnconfirmed, _observedInitialModel;
    private string? _retainedModelChoice;
    private string? _retainedModelName;

    private CommandIntent CaptureCommand(string command) => new(command, Model,
        _controls.Where(pair => pair.Value is TextBox).ToDictionary(pair => pair.Key,
            pair => ((TextBox)pair.Value).Text, StringComparer.Ordinal),
        _controls.Where(pair => pair.Value is ComboBox).ToDictionary(pair => pair.Key,
            pair => ((ComboBox)pair.Value).SelectedItem, StringComparer.Ordinal),
        Select("load-policy").SelectedIndex, _projects, _inspectedRevision, _loadedProfiles,
        _adapterObservationModel, _rollbackOperationId, _rollbackOperationModel, _promptRevision,
        command == "manage" ? (_modelSection == "advanced" ? "chat" : "advanced") :
            command.StartsWith("view-", StringComparison.Ordinal) ? command[5..] : "chat", !_showSwarm);

    private static bool IsNavigationCommand(string command) => command is
        "view-chat" or "view-performance" or "view-adapters" or "manage" or "swarm" or "swarm-return";

    private static bool HasUnconfirmedStop(Exception error) => error is DulcheRunStopUnconfirmedException ||
        error is AggregateException aggregate && aggregate.InnerExceptions.Any(HasUnconfirmedStop);

    private void ReportCommandFailure(Exception error)
    {
        if (HasUnconfirmedStop(error)) _runStopUnconfirmed = true;
        if (_active && !_lifetime.IsCancellationRequested) Text("message", error.Message);
    }
}
