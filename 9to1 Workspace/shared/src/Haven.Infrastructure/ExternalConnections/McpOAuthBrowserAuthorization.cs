using System.Diagnostics;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using ModelContextProtocol.Authentication;

namespace Haven.Infrastructure;

internal static class McpOAuthBrowserAuthorization
{
    public static async Task<AuthorizationResult?> AuthorizeAsync(AuthorizationCallbackContext context, CancellationToken cancellationToken)
    {
        if (!context.RedirectUri.IsLoopback || context.RedirectUri.Scheme != Uri.UriSchemeHttp)
            throw new InvalidOperationException("9to1 MCP OAuth requires an HTTP loopback redirect URI.");
        using var listener = new HttpListener();
        listener.Prefixes.Add(Prefix(context.RedirectUri));
        listener.Start();
        Process.Start(new ProcessStartInfo(context.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var callback = await WaitForCallbackAsync(listener, context.RedirectUri, timeout.Token).ConfigureAwait(false);
        var query = callback.Request.QueryString;
        return await CompleteAuthorizationResponseAsync(query["error"], query["code"], query["state"], query["iss"], timeout.Token,
            success => RespondAsync(callback.Response, success)).ConfigureAwait(false);
    }

    internal static string Prefix(Uri redirectUri)
    {
        var builder = new UriBuilder(redirectUri) { Query = string.Empty, Fragment = string.Empty };
        if (!redirectUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal)) builder.Path = "/";
        return builder.Uri.AbsoluteUri;
    }

    internal static bool MatchesRedirectPath(Uri requestUri, Uri redirectUri) =>
        requestUri.Scheme.Equals(redirectUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
        requestUri.Host.Equals(redirectUri.Host, StringComparison.OrdinalIgnoreCase) &&
        requestUri.Port == redirectUri.Port &&
        string.Equals(requestUri.AbsolutePath, redirectUri.AbsolutePath, StringComparison.Ordinal);

    internal static bool HasCompleteAuthorizationResponse(string? code, string? state) =>
        !string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(state);

    internal static bool IsSuccessfulResponse(string? error, string? code, string? state, bool cancelled) =>
        string.IsNullOrWhiteSpace(error) && HasCompleteAuthorizationResponse(code, state) && !cancelled;

    // The SDK still validates state/issuer and exchanges the code after this
    // callback returns; the browser must not imply the account is connected yet.
    internal static string ResponseText(bool success) => success
        ? "Authorization response received. You can return to the app."
        : "Connection not completed. Return to 9to1 to review the error.";

    internal static async Task<AuthorizationResult> CompleteAuthorizationResponseAsync(string? error, string? code, string? state,
        string? issuer, CancellationToken cancellationToken, Func<bool, Task> respondAsync)
    {
        ArgumentNullException.ThrowIfNull(respondAsync);
        Exception? primaryFailure = cancellationToken.IsCancellationRequested
            ? new OperationCanceledException(cancellationToken)
            : !string.IsNullOrWhiteSpace(error)
                ? new InvalidOperationException("MCP authorization was declined by the provider.")
                : !HasCompleteAuthorizationResponse(code, state)
                    ? new InvalidOperationException("MCP authorization callback did not include a complete response.")
                    : null;

        try
        {
            // The response callback owns its own short deadline; never pass the
            // caller token here because it may already be cancelled.
            await respondAsync(primaryFailure is null).ConfigureAwait(false);
        }
        catch (Exception responseOrCleanupFailure)
        {
            if (primaryFailure is null && cancellationToken.IsCancellationRequested)
                primaryFailure = new OperationCanceledException(cancellationToken);
            if (primaryFailure is not null)
                throw new AggregateException("MCP OAuth callback and browser response both failed.", primaryFailure, responseOrCleanupFailure);
            throw;
        }

        if (primaryFailure is null && cancellationToken.IsCancellationRequested)
            primaryFailure = new OperationCanceledException(cancellationToken);
        if (primaryFailure is not null) ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        return new AuthorizationResult { Code = code, State = state, Iss = issuer };
    }

    private static async Task<HttpListenerContext> WaitForCallbackAsync(HttpListener listener, Uri redirectUri, CancellationToken cancellationToken)
    {
        while (true)
        {
            var callback = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (callback.Request.Url is { } requestUri && MatchesRedirectPath(requestUri, redirectUri)) return callback;
            await RespondAsync(callback.Response, success: false).ConfigureAwait(false);
        }
    }

    internal static async Task CompleteResponseAsync(Func<CancellationToken, Task> write, Action close, Action abort, TimeSpan deadline,
        Action<Task>? lateFaultObservation = null)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(close);
        ArgumentNullException.ThrowIfNull(abort);
        if (deadline <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(deadline));

        var failures = new List<Exception>();
        Task? pendingWrite = null;
        var writeFailed = false;
        var writeTimedOut = false;
        using var responseDeadline = new CancellationTokenSource(deadline);
        try
        {
            pendingWrite = write(responseDeadline.Token);
            await pendingWrite.WaitAsync(responseDeadline.Token).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            writeFailed = true;
            writeTimedOut = failure is OperationCanceledException && responseDeadline.IsCancellationRequested;
            failures.Add(failure);
        }
        finally
        {
            var closeFailed = false;
            try { close(); }
            catch (Exception failure) { closeFailed = true; failures.Add(failure); }

            if (writeFailed || closeFailed)
            {
                try { abort(); }
                catch (Exception failure) { failures.Add(failure); }
            }

            if (writeFailed && pendingWrite is not null)
            {
                // A timeout can win WaitAsync even though close/abort subsequently faults the write.
                if (writeTimedOut && pendingWrite.IsFaulted && pendingWrite.Exception is { } lateFailure)
                    failures.AddRange(lateFailure.Flatten().InnerExceptions);
                // Close/abort can fault a timed-out write before this observer is attached.
                var observation = ObserveLateWriteFault(pendingWrite);
                try { lateFaultObservation?.Invoke(observation); }
                catch (Exception failure) { failures.Add(failure); }
                _ = observation;
            }
        }

        if (failures.Count > 0) throw new AggregateException("MCP OAuth browser response or cleanup failed.", failures);
    }

    internal static Task ObserveLateWriteFault(Task pendingWrite)
    {
        ArgumentNullException.ThrowIfNull(pendingWrite);
        return pendingWrite.ContinueWith(static task => _ = task.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static Task RespondAsync(HttpListenerResponse response, bool success)
    {
        var bytes = Encoding.UTF8.GetBytes(ResponseText(success));
        return CompleteResponseAsync(async deadlineToken =>
        {
            response.StatusCode = 200;
            response.ContentType = "text/plain; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, deadlineToken).ConfigureAwait(false);
        }, response.Close, response.Abort, TimeSpan.FromSeconds(2));
    }
}
