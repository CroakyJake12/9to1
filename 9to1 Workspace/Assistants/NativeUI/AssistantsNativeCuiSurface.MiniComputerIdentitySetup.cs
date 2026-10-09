using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.MiniComputer;

namespace HavenOS.Apps.Assistants.NativeUI;

public sealed partial class AssistantsNativeCuiSurface
{
    private long _miniIdentityRequest;
    private async Task DispatchOriginalMiniComputerIdentitySetupAsync(string command,
        AssistantConversationBinding binding, long generation, CancellationToken token)
    {
        if (_miniComputerManagement is not IAssistantMiniComputerIdentitySetupController owner)
        { PublishSynchronous(() => MiniComputerBindings.SetError("Catalogue identity setup is unavailable in this host.")); return; }
        if (command == "assistants.mini.identity.discard")
        { PublishSynchronous(MiniComputerBindings.ClearIdentitySetup); return; }
        var request = Interlocked.Increment(ref _miniIdentityRequest);
        PublishSynchronous(() => MiniComputerBindings.SetBusy(true));
        try
        {
            if (command == "assistants.mini.identity.prepare")
            {
                var preview = await SourceAsync(() => owner.PrepareIdentitySetupAsync(binding, Guid.NewGuid(), token));
                if (Current()) PublishSynchronous(() => { if (Current()) MiniComputerBindings.SetIdentitySetupPreview(preview); });
                return;
            }
            if (command != "assistants.mini.identity.request") throw new InvalidOperationException("No catalogue setup action was selected.");
            var original = MiniComputerBindings.OriginalIdentitySetup ?? throw new InvalidOperationException("Review this exact catalogue setup first.");
            PublishSynchronous(MiniComputerBindings.BeginIdentitySetup);
            Task<AssistantMiniComputerIdentitySetupResult>? raw = null;
            var delivery = SourceAsync(() =>
            {
                raw = owner.ExecuteIdentitySetupAsync(original, token);
                lock (_gate) _originals.Add(raw);
                MiniComputerBindings.RetainOriginalIdentitySetup(original, raw); return raw;
            });
            Exception? publication = null;
            try
            {
                while (!delivery.IsCompleted && Current())
                {
                    PublishSynchronous(() => MiniComputerBindings.ObserveIdentitySetup(owner.ObserveOriginalIdentitySetup(original)));
                    await SourceAsync(() => Task.Delay(250));
                }
            }
            catch (Exception cause) { publication = cause; Add(cause); }
            var result = await delivery;
            if (raw is not null) MiniComputerBindings.ObserveOriginalIdentitySettlement(original, raw);
            if (publication is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(publication).Throw();
            if (Current() && ReferenceEquals(MiniComputerBindings.OriginalIdentitySetup, original))
            {
                PublishSynchronous(() => MiniComputerBindings.PublishIdentitySetupResult(result));
                if (result.Applied) await ReadOriginalMiniComputerImportAsync(binding, generation, token);
            }
        }
        catch (Exception cause)
        {
            if (Current()) PublishSynchronous(() =>
            { if (command == "assistants.mini.identity.request") MiniComputerBindings.MarkIdentitySetupUnconfirmed(cause.Message); else MiniComputerBindings.SetError(cause.Message); });
            throw;
        }
        finally { if (Current()) PublishSynchronous(() => { if (Current()) MiniComputerBindings.SetBusy(false); }); }
        bool Current() => IsMiniComputerCurrent(binding, generation) && request == Volatile.Read(ref _miniIdentityRequest);
    }
}
