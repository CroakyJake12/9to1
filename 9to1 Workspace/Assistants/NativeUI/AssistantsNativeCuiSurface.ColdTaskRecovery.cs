namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private async Task ReviewOriginalColdTaskRecoveryAsync(CancellationToken token)
    {
        var binding = _binding ?? throw new InvalidOperationException("Open the saved Task conversation first.");
        var generation = PresentationGeneration;
        PublishSynchronous(() => Bindings.SetColdTaskRecoveryBusy(true));
        try
        {
            var actual = await SourceAsync(() => _controller.ReadOriginalColdTaskRecoveryAsync(token));
            if (!IsPresentationCurrent(binding, generation)) return;
            // Publish against the actual current Task revision, not an older view snapshot.
            await SourceAsync(() => _controller.RefreshWorkAsync(token));
            if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() =>
            {
                if (!IsPresentationCurrent(binding, generation)) return;
                var current = _controller.Snapshot;
                if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
                Bindings.PublishOriginalColdTaskPreview(binding, actual);
            });
        }
        finally
        {
            if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() =>
            { if (IsPresentationCurrent(binding, generation)) Bindings.SetColdTaskRecoveryBusy(false); });
        }
    }
    private async Task ResumeOriginalColdTaskAsync(CancellationToken token)
    {
        var binding = _binding ?? throw new InvalidOperationException("Open the saved Task conversation first.");
        var generation = PresentationGeneration;
        var preview = Bindings.CaptureOriginalColdTaskPreview();
        PublishSynchronous(() => Bindings.SetColdTaskRecoveryBusy(true));
        try
        {
            var actual = await SourceAsync(() => _controller.StartObservedOriginalColdTaskResumeAsync(preview, token));
            ReleasePreparationBlock();
            if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() =>
            { if (IsPresentationCurrent(binding, generation)) Bindings.ConsumeOriginalColdTaskPreview(preview); });
            try { await SourceAsync(actual.WaitAsync); }
            finally { await SourceAsync(() => _controller.CloseOriginalObservationAsync(actual)); }
            if (IsPresentationCurrent(binding, generation))
            {
                await SourceAsync(() => _controller.RefreshWorkAsync(token));
                if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() =>
                {
                    if (!IsPresentationCurrent(binding, generation)) return;
                    var current = _controller.Snapshot;
                    if (current.Revision >= _appliedSnapshot) ApplySnapshot(current);
                });
            }
        }
        finally
        {
            if (IsPresentationCurrent(binding, generation)) PublishSynchronous(() =>
            { if (IsPresentationCurrent(binding, generation)) Bindings.SetColdTaskRecoveryBusy(false); });
        }
    }
}
