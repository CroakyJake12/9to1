using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private async Task ChangeOriginalIdentityAvailabilityAsync(string command, CancellationToken token)
    {
        if (Bindings.IsActionAvailable(command) != true) return;
        var selected = DemandDefinition();
        var generation = PresentationGeneration;
        if (!await PrepareNavigationAsync(token) || IsRetiring) return;
        PublishSynchronous(() =>
        {
            var actual = _controller.Snapshot;
            if (actual.Revision >= _appliedSnapshot) ApplySnapshot(actual);
        });
        if (IsRetiring || generation != PresentationGeneration ||
            _controller.Snapshot.SelectedAssistant is not { } current || current.Identity != selected.Identity ||
            current.Revision != selected.Revision || Bindings.IsActionAvailable(command) != true) return;
        var configuration = command switch
        {
            "assistants.disable" => selected.Configuration with { Enabled = false },
            "assistants.enable" => selected.Configuration with { Enabled = true },
            "assistants.archive" => selected.Configuration with { Archived = true },
            "assistants.restore" => selected.Configuration with { Archived = false },
            _ => throw new AssistantCommandRefusedException("This Assistant availability action is unavailable.")
        };
        // The existing canonical CAS owns the saved identity. Restore preserves
        // the independent Enabled preference and never resumes/cancels a Task.
        var saved = await SourceAsync(() => _controller.ConfigureAsync(selected.Identity, selected.Revision,
            configuration, Guid.NewGuid(), token));
        if (IsRetiring || _controller.Snapshot.SelectedAssistant?.Identity != selected.Identity) return;
        PublishSynchronous(() =>
        {
            var latest = _controller.Snapshot;
            var publication = latest.Revision >= saved.Revision ? latest : saved;
            if (publication.Revision >= _appliedSnapshot) ApplySnapshot(publication);
        });
        if (configuration.Enabled && !configuration.Archived && _binding is not null)
            await RefreshOriginalConversationModelsAsync(token);
    }
}
