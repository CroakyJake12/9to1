using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.MiniComputer;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private readonly IAssistantMiniComputerController? _miniComputerManagement;
    private readonly string _miniComputerUnavailableReason;
    private AssistantsMiniComputerCuiBindings? _miniComputerBindings;
    private AssistantConversationBinding? _miniComputerTarget;
    private long _miniComputerGeneration, _miniComputerReadRequest;
    public IAssistantMiniComputerController? OriginalMiniComputerController => _miniComputerManagement;
    public AssistantsMiniComputerCuiBindings MiniComputerBindings => _miniComputerBindings ??
        throw new InvalidOperationException("The original Mini Computer presentation was not acquired.");
    private bool IsMiniComputerCurrent(AssistantConversationBinding binding, long generation) =>
        Bindings.IsMiniComputerVisible && ReferenceEquals(_miniComputerTarget, binding) && IsPresentationCurrent(binding, generation);
    private async Task OpenOriginalMiniComputerAsync(CancellationToken token)
    {
        var binding = _binding ?? throw new InvalidOperationException("Open this Assistant’s actual conversation before its Mini Computer.");
        _miniComputerTarget = binding; _miniComputerGeneration = PresentationGeneration;
        PublishSynchronous(Bindings.ShowMiniComputer);
        if (_miniComputerManagement is null)
        { PublishSynchronous(() => MiniComputerBindings.SetUnavailable(_miniComputerUnavailableReason)); return; }
        await ReadOriginalMiniComputerAsync(binding, _miniComputerGeneration, token);
        await ReadOriginalMiniComputerImportAsync(binding, _miniComputerGeneration, token);
    }
    private async Task ReadOriginalMiniComputerAsync(AssistantConversationBinding binding, long generation, CancellationToken token)
    {
        var request = Interlocked.Increment(ref _miniComputerReadRequest);
        PublishSynchronous(() => MiniComputerBindings.SetBusy(true));
        try
        {
            var actual = await SourceAsync(() => _miniComputerManagement!.ReadAsync(binding, token));
            if (IsMiniComputerCurrent(binding, generation) && request == Volatile.Read(ref _miniComputerReadRequest))
                PublishSynchronous(() =>
                { if (IsMiniComputerCurrent(binding, generation) && request == Volatile.Read(ref _miniComputerReadRequest)) MiniComputerBindings.SetView(actual); });
        }
        finally
        {
            if (IsMiniComputerCurrent(binding, generation) && request == Volatile.Read(ref _miniComputerReadRequest))
                PublishSynchronous(() => MiniComputerBindings.SetBusy(false));
        }
    }
    private ValueTask DispatchMiniComputerAsync(string command, object? parameter, CancellationToken token) => new(RunAsync(async () =>
    {
        PublishSynchronous(() =>
        {
            if (!Bindings.IsMiniComputerVisible || MiniComputerBindings.IsActionAvailable(command) != true)
                throw new InvalidOperationException("The original VM view no longer accepts this action.");
        });
        if (command == "assistants.mini.back") { PublishSynchronous(Bindings.ShowWork); return; }
        if (command == "assistants.mini.discard") { PublishSynchronous(MiniComputerBindings.ClearPreview); return; }
        var binding = _miniComputerTarget ?? throw new InvalidOperationException("The original Assistant conversation is unavailable.");
        var generation = _miniComputerGeneration;
        if (!IsMiniComputerCurrent(binding, generation)) throw new InvalidOperationException("Reopen the current Assistant’s Mini Computer view.");
        var owner = _miniComputerManagement ?? throw new InvalidOperationException(_miniComputerUnavailableReason);
        if (command.StartsWith("assistants.mini.identity.", StringComparison.Ordinal))
        { await DispatchOriginalMiniComputerIdentitySetupAsync(command, binding, generation, token); return; }
        if (command.StartsWith("assistants.mini.import.", StringComparison.Ordinal))
        { await DispatchOriginalMiniComputerImportAsync(command, binding, generation, token); return; }
        if (command == "assistants.mini.refresh") { await ReadOriginalMiniComputerAsync(binding, generation, token); return; }
        PublishSynchronous(() => MiniComputerBindings.SetBusy(true));
        try
        {
            var view = MiniComputerBindings.OriginalView ?? throw new InvalidOperationException("Read the actual permitted VM catalogue first.");
            if (command == "assistants.mini.choose")
            {
                if (parameter is not AssistantsMiniComputerCuiBindings.VirtualMachineRow row || !MiniComputerBindings.IsOriginalRow(row))
                    throw new InvalidOperationException("Choose the same current displayed VM row.");
                var selection = await SourceAsync(() => owner.SelectAsync(view, row.Original, token));
                if (!selection.Saved)
                { if (IsMiniComputerCurrent(binding, generation)) PublishSynchronous(() => MiniComputerBindings.SetError(selection.Reason)); return; }
                // ConfigureAsync publishes a genuine refreshed membership. Re-enter
                // with that SAME new binding; do not keep the obsolete definition revision.
                if (!IsRetiring && Bindings.IsMiniComputerVisible && _controller.Snapshot.ConversationBinding is { } fresh &&
                    fresh.Conversation.Id == binding.Conversation.Id && fresh.Definition.Identity == binding.Definition.Identity)
                { PublishSynchronous(() => ApplySnapshot(_controller.Snapshot)); await OpenOriginalMiniComputerAsync(token); }
                return;
            }
            if (command == "assistants.mini.confirm")
            {
                var preview = MiniComputerBindings.OriginalPreview ?? throw new InvalidOperationException("Review this exact VM action first.");
                PublishSynchronous(MiniComputerBindings.BeginOperation);
                Task<AssistantMiniComputerOperationResult>? original = null;
                var actual = SourceAsync(() =>
                {
                    original = owner.ExecuteAsync(preview, token);
                    // Capture the actual issuer Task before a subsequent binding or
                    // source publication can fail. The enclosing async wrapper is
                    // retained independently, never used as issuer identity.
                    lock (_gate) _originals.Add(original);
                    MiniComputerBindings.RetainOriginalOperation(preview, original);
                    return original;
                });
                Exception? publication = null;
                try
                {
                    while (!actual.IsCompleted && IsMiniComputerCurrent(binding, generation))
                    {
                        PublishSynchronous(() => MiniComputerBindings.Observe(owner.ObserveOriginalOperation(preview)));
                        await SourceAsync(() => Task.Delay(250));
                    }
                }
                catch (Exception cause) { publication = cause; Add(cause); }
                var result = await actual; // Independently join even if UI publication failed.
                if (original is null || !MiniComputerBindings.ObserveOriginalSettlement(preview, original))
                    throw new InvalidOperationException("The original VM settlement did not match its accepted preview and Task.");
                if (publication is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(publication).Throw();
                if (IsMiniComputerCurrent(binding, generation) && ReferenceEquals(MiniComputerBindings.OriginalPreview, preview))
                    PublishSynchronous(() => MiniComputerBindings.SetResult(result));
                return;
            }
            var action = command switch
            {
                "assistants.mini.inspect" => CanonicalMiniComputerAction.Inspect,
                "assistants.mini.start" => CanonicalMiniComputerAction.Start,
                "assistants.mini.pause" => CanonicalMiniComputerAction.Pause,
                "assistants.mini.resume" => CanonicalMiniComputerAction.Resume,
                "assistants.mini.save" => CanonicalMiniComputerAction.SaveState,
                "assistants.mini.shutdown" => CanonicalMiniComputerAction.Shutdown,
                _ => throw new InvalidOperationException("No original VM action was selected.")
            };
            var prepared = await SourceAsync(() => owner.PrepareAsync(view, action, Guid.NewGuid(), token));
            if (IsMiniComputerCurrent(binding, generation) && ReferenceEquals(MiniComputerBindings.OriginalView, view))
                PublishSynchronous(() => MiniComputerBindings.SetPreview(prepared));
        }
        catch (Exception cause)
        {
            if (IsMiniComputerCurrent(binding, generation)) PublishSynchronous(() =>
            { if (command == "assistants.mini.confirm") MiniComputerBindings.MarkUnconfirmed(cause.Message); else MiniComputerBindings.SetError(cause.Message); });
            throw;
        }
        finally { if (IsMiniComputerCurrent(binding, generation)) PublishSynchronous(() => MiniComputerBindings.SetBusy(false)); }
    }));
}
