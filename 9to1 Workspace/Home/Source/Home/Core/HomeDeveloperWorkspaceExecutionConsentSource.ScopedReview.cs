using System.Text.Json;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeDeveloperWorkspaceExecutionConsentSource
{
    private sealed partial class Consent
    {
        private bool _originalScopedReview;
        private DateTimeOffset _nextOriginalPendingValidation;
        private int _originalPendingValidations;

        private void StartOriginalPendingValidationWindow()
        {
            if (_originalScopedReview)
            {
                _nextOriginalPendingValidation = DateTimeOffset.UtcNow.AddSeconds(30);
                _originalPendingValidations = 0;
            }
        }

        private Task ValidateOriginalPendingSourcesAsync(CloudflareOriginalTaskLedger sources, CancellationToken token)
        {
            if (!_originalScopedReview) return ValidateSourcesAsync(sources, token);
            DemandLive();
            var now = DateTimeOffset.UtcNow;
            if (_originalPendingValidations >= 10 || now < _nextOriginalPendingValidation) return Task.CompletedTask;
            _originalPendingValidations++; _nextOriginalPendingValidation = now.AddSeconds(30);
            return ValidateSourcesAsync(sources, token);
        }

        private void ConfigureOriginalScopedReview(CloudflareOriginalTaskLedger sources)
        {
            if (_bindingSource is not IOriginalScopedCanonicalResourceAccessResolver resolver) return;
            if (!sources.Invoke(() => resolver.ResourceKind == ExecuteAction))
                throw new UnauthorizedAccessException("The genuine scoped execution binding owner has a different resource kind.");
            _originalScopedReview = true;
        }

        private Task<HomePreparedReviewObservation> AuthorizeOriginalReviewAsync(CloudflareOriginalTaskLedger sources, CancellationToken token)
            => _originalScopedReview
                ? Owner._broker.AuthorizePreparedReviewWithinOriginalSourceAsync(_review!,
                    body => RunOriginalScopedReviewSource(sources, body), actual => RetainOriginalScopedTask(sources, actual), token)
                : Owner._broker.AuthorizePreparedReviewAsync(_review!, token);

        private Task<HomePreparedReviewObservation> ObserveOriginalReviewAsync(CloudflareOriginalTaskLedger sources, CancellationToken token)
            => _originalScopedReview
                ? Owner._broker.ObservePreparedReviewWithinOriginalSourceAsync(_review!,
                    body => RunOriginalScopedReviewSource(sources, body), actual => RetainOriginalScopedTask(sources, actual), token)
                : Owner._broker.ObservePreparedReviewAsync(_review!, token);

        private Task<HomeResourceExecutionCapability?> BeginOriginalReviewedExecutionAsync(
            CloudflareOriginalTaskLedger sources, JsonElement args, CancellationToken token)
            => _originalScopedReview
                ? Owner._broker.BeginExecutionCapabilityWithinOriginalSourceAsync(_review!.RequestId, args,
                    body => RunOriginalScopedReviewSource(sources, body), actual => RetainOriginalScopedTask(sources, actual),
                    RunOriginalReviewCleanup, token)
                : Owner._broker.BeginExecutionCapabilityAsync(_review!.RequestId, args, token);

        private Task<HomeResourceClaimResult> ClaimOriginalReviewedExecutionAsync(
            CloudflareOriginalTaskLedger sources, JsonElement args, CancellationToken token)
            => _originalScopedReview
                ? Owner._broker.ClaimExecutionWithinOriginalSourceAsync(_capability!, "dev", ExecuteAction, _scopes, args,
                    body => RunOriginalScopedReviewSource(sources, body), actual => RetainOriginalScopedTask(sources, actual),
                    RunOriginalReviewCleanup, token)
                : Owner._broker.ClaimExecutionObservedAsync(_capability!, "dev", ExecuteAction, _scopes, args, token);

        // Rejection auditing is owed after productive admission has ended. It retains the
        // same owner's physical ancestry without re-entering the caller's productive gate.
        private void RunOriginalScopedReviewSource(CloudflareOriginalTaskLedger sources, Action body)
            => _ = CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                DemandLive();
                RunOriginalScopedSource(sources, () => { DemandLive(); body(); });
                DemandLive(); return true;
            });

        private void RunOriginalReviewCleanup(Action body)
            => _ = CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { body(); return true; });
    }
}
