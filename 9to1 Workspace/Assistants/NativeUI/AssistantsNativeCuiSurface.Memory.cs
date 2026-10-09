using Haven.Core;
using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private readonly IAssistantMemoryManagementController? _memoryManagement;
    private readonly string _memoryUnavailableReason;
    private AssistantsMemoryCuiBindings? _memoryBindings;
    private AssistantConversationBinding? _memoryTarget;
    private long _memoryPresentationGeneration;
    public IAssistantMemoryManagementController? OriginalMemoryManagementController => _memoryManagement;
    public AssistantsMemoryCuiBindings MemoryBindings => _memoryBindings ??
        throw new InvalidOperationException("The original memory presentation was not acquired.");
    private bool IsMemoryCurrent(AssistantConversationBinding actual, long generation) =>
        Bindings.IsMemoryVisible && ReferenceEquals(_memoryTarget, actual) && IsPresentationCurrent(actual, generation);

    private async Task OpenOriginalMemoryAsync(CancellationToken token)
    {
        var target = _binding ?? throw new InvalidOperationException("Open this Assistant's actual conversation before its memory.");
        var generation = PresentationGeneration; _memoryTarget = target; _memoryPresentationGeneration = generation;
        PublishSynchronous(() => { Bindings.ShowMemory(); MemoryBindings.SetRevisionSupport(_memoryManagement is IAssistantMemoryRevisionManagementController); MemoryBindings.SetPagingSupport(_memoryManagement is IAssistantMemoryManagementPagingController); });
        if (_memoryManagement is null)
        { PublishSynchronous(() => MemoryBindings.SetUnavailable(_memoryUnavailableReason)); return; }
        await ReadOriginalMemoryAsync(target, generation, token);
    }

    private ValueTask DispatchMemoryAsync(string command, object? parameter, CancellationToken token) => new(RunAsync(async () =>
    {
        PublishSynchronous(() =>
        {
            if (!Bindings.IsMemoryVisible || MemoryBindings.IsActionAvailable(command) != true)
                throw new InvalidOperationException("This original memory review no longer accepts that action.");
        });
        if (command == "assistants.memory.back") { PublishSynchronous(Bindings.ShowWork); return; }
        if (command == "assistants.memory.discard") { PublishSynchronous(MemoryBindings.DiscardDraft); return; }
        if (command == "assistants.memory.revise") { PublishSynchronous(MemoryBindings.ReviseDraft); return; }
        if (command is "assistants.memory.correct" or "assistants.memory.reject")
        {
            var row = parameter as AssistantsMemoryCuiBindings.MemoryRow ?? throw new InvalidOperationException("Select the actual memory row.");
            PublishSynchronous(() => MemoryBindings.StartRevision(row, command == "assistants.memory.correct"
                ? CanonicalAssistantMemoryMutationKind.Correct : CanonicalAssistantMemoryMutationKind.Reject)); return;
        }
        var target = _memoryTarget ?? throw new InvalidOperationException("The original memory conversation is unavailable.");
        var generation = _memoryPresentationGeneration;
        if (!IsMemoryCurrent(target, generation)) throw new InvalidOperationException("The original Assistant conversation changed; reopen its memory.");
        var owner = _memoryManagement ?? throw new InvalidOperationException(_memoryUnavailableReason);
        if (command.StartsWith("assistants.memory.import.", StringComparison.Ordinal))
        { await DispatchOriginalMemoryImportAsync(command, target, generation, token); return; }
        if (command == "assistants.memory.status") { PublishOriginalMemoryObservation(owner, target, generation); return; }
        if (command is "assistants.memory.refresh" or "assistants.memory.search" or "assistants.memory.older")
        { await ReadOriginalMemoryAsync(target, generation, token, command == "assistants.memory.older" ? MemoryBindings.OriginalView?.NextContinuation : null); return; }
        PublishSynchronous(() => MemoryBindings.SetBusy(true));
        try
        {
            if (command == "assistants.memory.review")
            {
                var view = MemoryBindings.OriginalView ?? throw new InvalidOperationException("Read the actual permitted memory first.");
                var operation = MemoryBindings.OriginalOperationId;
                AssistantMemoryWritePreview? preview;
                if (MemoryBindings.OriginalSelectedRecord is { } selected)
                {
                    var revisions = owner as IAssistantMemoryRevisionManagementController ?? throw new InvalidOperationException("The actual memory revision owner is unavailable.");
                    var prepared = await SourceAsync(() => revisions.PrepareRevisionAsync(view, selected, MemoryBindings.OriginalMutationKind,
                        MemoryBindings.OriginalTitle, MemoryBindings.OriginalSummary, operation, token));
                    preview = prepared.Preview;
                    if (preview is null)
                    {
                        if (IsMemoryCurrent(target, generation) && ReferenceEquals(MemoryBindings.OriginalView, view))
                            PublishSynchronous(() => MemoryBindings.SetError(prepared.Reason));
                        return;
                    }
                }
                else preview = await SourceAsync(() => owner.PrepareAsync(view, MemoryBindings.OriginalTitle,
                    MemoryBindings.OriginalSummary, operation, token));
                if (IsMemoryCurrent(target, generation) && ReferenceEquals(MemoryBindings.OriginalView, view))
                    PublishSynchronous(() => MemoryBindings.SetPreview(preview));
            }
            else if (command == "assistants.memory.confirm")
            {
                var preview = MemoryBindings.OriginalPreview ?? throw new InvalidOperationException("Review this exact draft before confirming it.");
                PublishSynchronous(MemoryBindings.BeginCommit);
                // The SAME actual commit is retained before observing status. Status
                // refresh never issues another commit or treats a request ID as consent.
                var actual = SourceAsync(() => owner.CommitAsync(preview, token));
                Exception? publicationFailure = null;
                try
                {
                    while (!actual.IsCompleted && IsMemoryCurrent(target, generation))
                    {
                        PublishOriginalMemoryObservation(owner, target, generation);
                        // A bounded display tick is also an owned raw Task. It carries
                        // no cancellation/permission waiver and no source query.
                        await SourceAsync(() => Task.Delay(250));
                    }
                }
                catch (Exception failure) { publicationFailure = failure; Add(failure); }
                var result = await actual; // Always independently join even if status publication failed.
                if (publicationFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(publicationFailure).Throw();
                if (IsMemoryCurrent(target, generation) && ReferenceEquals(MemoryBindings.OriginalPreview, preview))
                    PublishSynchronous(() => MemoryBindings.Acknowledge(preview, result));
                if (result.Saved && IsMemoryCurrent(target, generation))
                {
                    await ReadOriginalMemoryAsync(target, generation, token);
                }
            }
        }
        catch (Exception failure)
        {
            if (IsMemoryCurrent(target, generation)) PublishSynchronous(() =>
            {
                if (command == "assistants.memory.confirm") MemoryBindings.MarkUnconfirmed(failure.Message);
                else MemoryBindings.SetError(failure.Message);
            });
            throw; // Unknown/mixed original faults remain in native close custody.
        }
        finally { if (IsMemoryCurrent(target, generation)) PublishSynchronous(() => MemoryBindings.SetBusy(false)); }
    }));

    private void PublishOriginalMemoryObservation(IAssistantMemoryManagementController owner,
        AssistantConversationBinding target, long generation)
    {
        if (owner is not IAssistantMemoryWriteObservationSource observer || !IsMemoryCurrent(target, generation)) return;
        PublishSynchronous(() =>
        {
            if (IsMemoryCurrent(target, generation) && MemoryBindings.OriginalPreview is { } samePreview)
                MemoryBindings.Observe(observer.ObserveOriginalWrite(samePreview));
        });
    }
}
