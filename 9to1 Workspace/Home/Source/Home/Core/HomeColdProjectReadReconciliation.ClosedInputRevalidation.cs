using System.Runtime.ExceptionServices;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeColdProjectReadReconciliation
{
    private async Task ValidateClosedInputThroughFreshOriginalReadAsync(Work original, Context sources,
        CancellationToken token)
    {
        // Retain the newly reserved SAME issuer Work before its first source start.
        // Its existing StartInput performs the real Home/Files/native current READ,
        // manual permission and short borrower protocol; metadata issues no grant.
        Work? fresh = null; Task? preparation = null, actualClose = null;
        Exception? primary = null; var cleanup = new List<Exception>();
        try
        {
            _ = sources.Invoke(() => { fresh = ReserveAdmitted(null); return fresh; });
            var actualFresh = fresh ?? throw new InvalidOperationException("The actual fresh READ owner was not captured.");
            _ = sources.Invoke(() =>
            {
                preparation = actualFresh.StartInput(original.Conversation, original.Container,
                    original.Identity.OriginalProjectContextJson, sources.Scope, sources.Retain, token,
                    original.Identity.SavedWorkspaceDocumentSha256, original.Identity.RegisteredRootFingerprint);
                sources.Retain(preparation); return preparation;
            });
            await sources.Await(preparation ?? throw new InvalidOperationException("The actual fresh READ was not acquired.")).ConfigureAwait(false);
            DemandSameResourceIdentity(original.Identity, actualFresh.Identity);
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            // A raw preparation can be accepted before a caller's post-callback
            // refusal. Join it independently, then acquire and join SAME fresh close.
            if (preparation is not null)
                try { await sources.Await(preparation).ConfigureAwait(false); }
                catch (Exception cause) { cleanup.Add(cause); }
            if (fresh is not null)
            {
                var closes = new List<Task>();
                sources.AcquireClose(() => { actualClose = fresh.CloseOwned(); return actualClose; }, closes);
                if (actualClose is null) cleanup.Add(new InvalidOperationException("The actual fresh READ close was not acquired."));
                else
                    try { await sources.Await(actualClose).ConfigureAwait(false); }
                    catch (Exception cause) { cleanup.Add(cause); }
                if (!fresh.CanPruneSuccessfulClose)
                    cleanup.Add(new InvalidOperationException("The actual fresh READ source/borrower cohort did not close successfully."));
            }
        }
        if (cleanup.Count != 0)
            throw new AggregateException("Fresh closed-input project revalidation and actual cleanup failed.",
                (primary is null ? Enumerable.Empty<Exception>() : new[] { primary }).Concat(cleanup));
        if (primary is not null) ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private sealed partial class Work
    {
        internal void DemandExactHealthyClosedInput(TaskRunColdChatInput exact)
        {
            if (ObserveSuccessfullySettledOriginalInputClose() is null)
                throw new UnauthorizedAccessException("SAME privately issued healthy closed project input required.");
            DemandExactOriginalInputValues(exact);
            if (ObserveSuccessfullySettledOriginalInputClose() is null)
                throw new UnauthorizedAccessException("The actual closed project input cohort changed.");
        }
    }
}
