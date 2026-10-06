using System.Globalization;
using System.Text.Json;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

[CollectionDefinition("OriginalLocalTaskConsole", DisableParallelization = true)]
public sealed class OriginalLocalTaskConsoleCollection;

/// <summary>Real Linux Home/normal business graph and the public CLI driver, with
/// controlled console input only. No model, native installed session, Task admission,
/// project receipt or permission approval is fabricated or exercised here.</summary>
[Collection("OriginalLocalTaskConsole")]
public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    private static readonly SemaphoreSlim ConsoleOwnership = new(1, 1);

    [Theory]
    [InlineData("")]
    [InlineData("help\nquit\n")]
    public async Task Actual_linux_console_startup_and_EOF_or_quit_join_the_real_graph_before_final_clean(string input)
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await ConsoleOwnership.WaitAsync(token);
        var previousInput = Console.In;
        var previousOutput = Console.Out;
        var output = new StringWriter(CultureInfo.InvariantCulture);
        StringReader? suppliedInput = null;
        string? root = null;
        Task<int>? actualRun = null;
        Task<string>? actualRead = null;
        var failures = new List<Exception>();
        var healthyRun = false;
        try
        {
            root = Directory.CreateTempSubdirectory("haven-real-console-smoke-").FullName;
            suppliedInput = new(input);
            Console.SetIn(suppliedInput);
            Console.SetOut(output);
            actualRun = OriginalLocalTaskConsole.RunAsync(["--data-directory", root], token);
            var result = await actualRun;
            Assert.True(result == 0, output.ToString());
            healthyRun = true; // Only the real public encompassing driver reported healthy close.
            Assert.Contains("\"localHomeStarted\":true", output.ToString());
            Assert.Contains("\"installedSession\":false", output.ToString());
            Assert.Contains("\"modelInitialized\":false", output.ToString());
            actualRead = File.ReadAllTextAsync(Path.Combine(root, "startup-recovery.json"), token);
            using var persisted = JsonDocument.Parse(await actualRead);
            var run = persisted.RootElement.GetProperty("currentRun");
            Assert.True(run.GetProperty("startupCompleted").GetBoolean());
            Assert.True(run.GetProperty("cleanShutdown").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(run.GetProperty("id").GetString()));
            Assert.True(File.Exists(Path.Combine(root, "Home", "state.json")));
        }
        catch (Exception primary) { Capture(failures, null, primary); }
        finally
        {
            // Independent joins retain genuine originals even if an assertion or read failed.
            if (actualRun is not null)
                try { await actualRun.ConfigureAwait(false); }
                catch (Exception cause) { Capture(failures, actualRun, cause); }
            if (actualRead is not null)
                try { await actualRead.ConfigureAwait(false); }
                catch (Exception cause) { Capture(failures, actualRead, cause); }
            try { Console.SetIn(previousInput); }
            catch (Exception cause) { Capture(failures, null, cause); }
            try { Console.SetOut(previousOutput); }
            catch (Exception cause) { Capture(failures, null, cause); }
            // Unknown/failed process close preserves its actual data and output for inspection;
            // it does not authorize deleting storage or disposing a possibly borrowed writer.
            if (healthyRun)
            {
                try { suppliedInput?.Dispose(); }
                catch (Exception cause) { Capture(failures, null, cause); }
                try { output.Dispose(); }
                catch (Exception cause) { Capture(failures, null, cause); }
                if (root is not null)
                    try { Directory.Delete(root, true); }
                    catch (Exception cause) { Capture(failures, null, cause); }
            }
            ConsoleOwnership.Release();
        }
        if (failures.Count != 0)
            throw new AggregateException($"Actual console smoke or independent cleanup failed; source data: {root}.", failures);
    }

    private static void Capture(List<Exception> failures, Task? original, Exception caught)
    {
        var fullOriginal = (Exception?)original?.Exception ?? caught;
        if (!failures.Any(prior => ReferenceEquals(prior, fullOriginal))) failures.Add(fullOriginal);
    }
}
