using Haven.Application;
using Haven.Infrastructure;
using Xunit;
namespace Haven.Infrastructure.Tests;
public sealed class LinuxProviderSecretStoreCancellationTests
{
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
    [Fact]
    public async Task Genuine_canceled_input_has_actual_canceled_task_and_does_not_publish_a_record()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("haven-credential-cancel-").FullName;
        LinuxProviderSecretStore? source = null; Exception? primary = null; Task? actual = null;
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            source = new(new Paths(root)); using var stop = new CancellationTokenSource(); stop.Cancel();
            actual = source.SetAsync("controlled", "name", "not-a-real-token", stop.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
            Assert.True(actual.IsCanceled); Assert.False(actual.IsFaulted);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "Credentials"), "*.credential"));
        }
        catch (Exception cause) { primary = cause; }
        finally
        {
            var errors = new List<Exception>(); if (primary is not null) errors.Add(primary);
            // A genuine already-observed cancellation is expected; faults remain real causes.
            if (actual is not null) try { await actual; } catch (Exception cause) { if (!actual.IsCanceled) errors.Add(actual.Exception ?? cause); }
            try { source?.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            try { Directory.Delete(root, true); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count != 0) throw new AggregateException("Controlled local credential originals failed.", errors);
        }
    }
}
