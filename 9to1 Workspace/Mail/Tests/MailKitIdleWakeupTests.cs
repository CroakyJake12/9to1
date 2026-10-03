using HavenOS.Mail.Providers;
using Xunit;

namespace HavenOS.Mail.Tests;

/// <summary>Real maintained loopback SMTP/IMAP only; no production account or Send grant.</summary>
[Collection("Original maintained Mail IDLE isolation")]
public sealed class MailKitIdleWakeupTests
{
    [Fact]
    public async Task Actual_TLS_IDLE_original_folder_arrival_completes_before_timer_and_pending_wait_is_drained()
    {
        var host = Required("HAVEN_MAIL_FIXTURE_HOST");
        Assert.True(host is "localhost" or "127.0.0.1" or "::1");
        var account = MailKitTransportFixture.Account(host,
            int.Parse(Required("HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Required("HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture),
            Required("HAVEN_MAIL_FIXTURE_ADDRESS"));
        var credentials = new OriginalFixtureCredentials(Required("HAVEN_MAIL_FIXTURE_PASSWORD"));
        var provider = new MailKitImapSmtpProvider(credentials);
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var waitLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var originalWait = provider.WaitForChangesAsync(account, waitLifetime.Token);
        Exception? primary = null;
        try
        {
            // This witness comes from the real TLS IMAP connection asking for
            // its credential. It does not invent authentication/IDLE readiness.
            await credentials.OriginalCredentialRead.Task.WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
            var subject = "Original IDLE arrival " + Guid.NewGuid().ToString("N");
            // An arrival before SELECT/IDLE may be part of initial membership.
            // Repeat bounded genuine arrivals until the original await observes
            // a response. Passing never depends on a fixed readiness sleep.
            var arrivals = 0;
            while (!originalWait.IsCompleted && arrivals < 16)
            {
                var sent = await provider.SendAsync(account,
                    MailKitTransportFixture.Draft(account) with { Subject = subject + " " + arrivals }, lifetime.Token);
                Assert.False(string.IsNullOrWhiteSpace(sent.ProviderMessageId));
                arrivals++;
                await Task.WhenAny(originalWait, Task.Delay(TimeSpan.FromMilliseconds(250), lifetime.Token));
            }
            Assert.InRange(arrivals, 1, 16);
            // The unchanged timer is 25 minutes. Only normal completion of the
            // original real IDLE await within this bound proves arrival wakeup.
            await originalWait.WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
            Assert.False(waitLifetime.IsCancellationRequested);
            var observed = await provider.SynchronizeAsync(account, null, lifetime.Token);
            Assert.Contains(observed.Messages, message => message.Subject.StartsWith(subject, StringComparison.Ordinal));
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            // Keep the original task and await its own disconnect/finally. A
            // cleanup exception must survive alongside the original assertion.
            var cleanupFailures = new List<Exception>();
            // Cancellation callbacks may throw. Still await the same original
            // task so its disconnect/finally cannot be skipped by that error.
            try { waitLifetime.Cancel(); }
            catch (Exception cleanup) { CollectCleanupFailure(cleanupFailures, primary, cleanup); }
            try { await originalWait; }
            catch (OperationCanceledException) when (waitLifetime.IsCancellationRequested) { }
            catch (Exception cleanup) { CollectCleanupFailure(cleanupFailures, primary, cleanup); }
            if (cleanupFailures.Count > 0)
            {
                if (primary is not null) cleanupFailures.Insert(0, primary);
                throw new AggregateException("Original IDLE witness and/or cleanup failed.", cleanupFailures);
            }
        }
    }

    private static void CollectCleanupFailure(List<Exception> failures, Exception? primary, Exception cleanup)
    {
        if (!object.ReferenceEquals(cleanup, primary) && !failures.Any(existing => object.ReferenceEquals(existing, cleanup)))
            failures.Add(cleanup);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException("The required real transport fixture is not configured: " + name);

    private sealed class OriginalFixtureCredentials(string secret) : IMailCredentialResolver
    {
        public TaskCompletionSource OriginalCredentialRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<MailProviderCredential> ResolveAsync(string reference, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (reference != "fixture-local-mail") throw new InvalidOperationException("Unexpected original credential reference.");
            OriginalCredentialRead.TrySetResult();
            return ValueTask.FromResult(new MailProviderCredential(secret, false));
        }
    }
}

[CollectionDefinition("Original maintained Mail IDLE isolation", DisableParallelization = true)]
public sealed class MailKitIdleIsolationCollection { }
