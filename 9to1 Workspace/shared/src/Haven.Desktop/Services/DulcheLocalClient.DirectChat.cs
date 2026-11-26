using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Haven.Desktop.Services;

public sealed partial class DulcheLocalClient
{
    internal static readonly TimeSpan DirectChatAdmissionTimeout = TimeSpan.FromMinutes(8);
    internal static readonly TimeSpan DirectChatFirstProgressTimeout = TimeSpan.FromMinutes(8);
    internal static readonly TimeSpan DirectChatIdleTimeout = TimeSpan.FromMinutes(2);

    // Instance-only test seam: production callers always use the named limits.
    internal TimeSpan DirectAdmissionLimit { get; init; } = DirectChatAdmissionTimeout;
    internal TimeSpan DirectFirstProgressLimit { get; init; } = DirectChatFirstProgressTimeout;
    internal TimeSpan DirectIdleLimit { get; init; } = DirectChatIdleTimeout;

    private async IAsyncEnumerable<string> StreamDirectChatAsync(object payload, Action<string> runStarted,
        [EnumeratorCancellation] CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(DirectAdmissionLimit);
        var admitted = false;
        var progressed = false;
        void Started(string id)
        {
            admitted = true;
            bounded.CancelAfter(DirectFirstProgressLimit);
            runStarted(id);
        }
        void Observe(JsonNode frame)
        {
            // Headers, heartbeats and role-only frames are not model progress.
            // Reasoning is genuine progress even before the first visible token.
            var delta = frame["choices"]?.AsArray().FirstOrDefault()?["delta"];
            if (!string.IsNullOrEmpty(delta?["content"]?.ToString()) ||
                !string.IsNullOrEmpty(delta?["reasoning_content"]?.ToString()) ||
                !string.IsNullOrEmpty(delta?["reasoning"]?.ToString()))
            {
                progressed = true;
                bounded.CancelAfter(DirectIdleLimit);
            }
        }
        await using var source = StreamChatAsync(payload, Started, ownsIncompleteRun: true,
            bounded.Token, Observe).GetAsyncEnumerator(bounded.Token);
        while (true)
        {
            bool next;
            try { next = await source.MoveNextAsync().ConfigureAwait(false); }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested && bounded.IsCancellationRequested)
            {
                // StreamChatAsync has already awaited the exact run's bounded
                // stop settlement before this cancellation reaches the UI.
                throw new TimeoutException(progressed
                    ? "The response stalled. Its run was stopped; partial output and your draft were retained."
                    : admitted
                        ? "The model did not begin responding within the startup limit. Its run was stopped; your draft was retained."
                        : "Dulche did not acknowledge the response within the admission limit.", error);
            }
            catch (DulcheRunStopUnconfirmedException error) when (!token.IsCancellationRequested && bounded.IsCancellationRequested)
            {
                throw new DulcheRunStopUnconfirmedException(error.RunId,
                    "The response timed out and its remote stop is unconfirmed. Partial output and your draft were retained; inspect the saved run before retrying.", error);
            }
            if (!next) yield break;
            yield return source.Current;
        }
    }
}
