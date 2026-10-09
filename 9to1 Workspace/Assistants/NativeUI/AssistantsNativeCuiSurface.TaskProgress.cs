using Haven.Core;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private async Task SubmitOriginalFollowUpAsync(TaskFollowUpMode mode, CancellationToken token)
    {
        var binding = _binding ?? throw new InvalidOperationException("Open the Task conversation first.");
        var generation = PresentationGeneration;
        var expected = DemandTask(); var instruction = Bindings.Prompt;
        PublishSynchronous(() => Bindings.SetTaskFollowUpBusy(true));
        try
        {
            var actual = await SourceAsync(() => _controller.SubmitFollowUpAsync(expected, instruction, mode, token));
            if (actual.Mode != mode || actual.Snapshot.TaskId != expected.TaskId ||
                actual.Snapshot.ContextId != expected.ContextId || actual.Snapshot.ExecutionId != expected.ExecutionId ||
                actual.Snapshot.PersistenceRevision < expected.PersistenceRevision)
                throw new UnauthorizedAccessException("The follow-up result does not belong to this actual Task.");
            if (!IsPresentationCurrent(binding, generation)) return;
            await SourceAsync(() => _controller.RefreshWorkAsync(token));
            if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() =>
            {
                if (!IsPresentationCurrent(binding, generation)) return;
                var current = _controller.Snapshot;
                if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
                Bindings.PublishOriginalFollowUpDecision(binding, actual);
            });
        }
        finally
        {
            if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() =>
            {
                if (IsPresentationCurrent(binding, generation)) Bindings.SetTaskFollowUpBusy(false);
            });
        }
    }
}
