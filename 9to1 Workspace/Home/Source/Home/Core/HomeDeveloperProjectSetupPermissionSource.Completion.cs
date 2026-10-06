using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDeveloperProjectSetupPermissionSource
{
    private sealed partial class Permission
    {
        private IDeveloperProjectOriginalSetupCompletionSource? _completionSource;
        private readonly CloudflareOriginalTaskLedger _completionStages = new();
        private Task<bool>? _finalCompletion;

        private bool EveryOriginalStepClosedSuccessfully()
        {
            KeyValuePair<DeveloperProjectSetupStep, Step>[] steps;
            lock (_gate) steps = _steps.ToArray();
            return steps.Length == intent.Steps.Length && intent.Steps.All(sameStep =>
                steps.Any(pair => ReferenceEquals(pair.Key, sameStep) && pair.Value.HasSuccessfulOriginalCompletion(sameStep)));
        }

        private Task<bool> StartOriginalFinalCompletion()
        {
            Task actual; TaskCompletionSource begin;
            lock (_gate)
            {
                if (_finalCompletion is not null) return _finalCompletion;
                begin = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _completionStages.BindOriginalOwner(this);
                _finalCompletion = ReadFinalCompletionPublishedAsync(begin.Task);
                actual = _finalCompletion;
            }
            begin.SetResult(); return (Task<bool>)actual;
        }

        private async Task<bool> ReadFinalCompletionPublishedAsync(Task begin)
        {
            await begin.ConfigureAwait(false);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            return await _completionStages.RunToOriginalSettlementAsync(async () =>
            {
                if (_completionSource is null || owner._completions is null || !EveryOriginalStepClosedSuccessfully()) return false;
                if (!_completionStages.Invoke(() => ReferenceEquals(owner._completions(), _completionSource)))
                    throw new UnauthorizedAccessException("The same configured private final journal issuer is required.");
                var completion = await _completionStages.AwaitAsync(_completionStages.Invoke(() =>
                    _completionSource.GetOriginalCompletionAsync(intent, capture, CancellationToken.None))).ConfigureAwait(false);
                if (completion is null) return false;
                if (!_completionStages.Invoke(() => _completionSource.IsIssuedOriginalCompletion(intent, capture, completion)))
                    throw new UnauthorizedAccessException("No same private original final journal completion was issued.");
                await _completionStages.AwaitAsync(_completionStages.Invoke(() =>
                    _completionSource.ValidateOriginalCompletionAsync(intent, capture, completion, CancellationToken.None))).ConfigureAwait(false);
                if (!_completionStages.Invoke(() => ReferenceEquals(owner._completions(), _completionSource)
                    && _completionSource.IsIssuedOriginalCompletion(intent, capture, completion)) || !EveryOriginalStepClosedSuccessfully())
                    throw new UnauthorizedAccessException("Original setup/journal completion retired during final reads.");
                return true;
            }).ConfigureAwait(false);
        }
    }
}
