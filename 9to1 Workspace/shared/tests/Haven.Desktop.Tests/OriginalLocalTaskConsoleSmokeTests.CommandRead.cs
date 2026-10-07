using System.Globalization;
using System.Text.Json;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_opted_in_console_keeps_manual_Home_commands_usable_after_missing_attachment_READ_and_retains_the_original_refusal_without_Task_or_grant(bool quit)
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await ConsoleOwnership.WaitAsync(token);
        var previousInput = Console.In; var previousOutput = Console.Out;
        var selector = NativePersonalTaskColdRecoveryConfiguration.OriginalEnvironmentSelector;
        var previousSelector = Environment.GetEnvironmentVariable(selector);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        StringReader? input = null;
        string? root = null; string? filesRoot = null;
        Task<int>? actualRun = null;
        Task<HomeStateReadResult>? actualHomeRead = null;
        SqliteConnection? database = null; SqliteCommand? query = null;
        Task? databaseOpen = null; Task<object?>? count = null; Task? databaseClose = null;
        var failures = new List<Exception>();
        try
        {
            root = Directory.CreateTempSubdirectory("haven-real-command-read-console-").FullName;
            filesRoot = Directory.CreateTempSubdirectory("haven-explicit-empty-files-").FullName;
            Environment.SetEnvironmentVariable(selector, "1");
            input = new StringReader("files-configure " + filesRoot + "\nread README.md\nhome-requests\nfiles-status\n" + (quit ? "quit\n" : ""));
            Console.SetIn(input); Console.SetOut(output);
            actualRun = OriginalLocalTaskConsole.RunAsync(["--data-directory", root], token);
            Assert.Equal(1, await actualRun); // The original admitted refusal remains unhealthy at external close.
            var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => { using var parsed = JsonDocument.Parse(line); return parsed.RootElement.Clone(); }).ToArray();
            var refusal = Assert.Single(lines.Where(line => line.ValueKind == JsonValueKind.Object &&
                line.TryGetProperty("commandFailed", out var failed) && failed.GetBoolean()));
            var expectedPrefix = "System.InvalidOperationException: Attach or reopen the SAME saved Task/Run/project first.";
            Assert.StartsWith(expectedPrefix, refusal.GetProperty("cause").GetString() ?? throw new InvalidOperationException("No actual command refusal text was published."));
            var homeSnapshot = Assert.Single(lines.Where(line => line.ValueKind == JsonValueKind.Object &&
                line.TryGetProperty("PendingRequests", out _) && line.TryGetProperty("Grants", out _)));
            Assert.True(Array.IndexOf(lines, homeSnapshot) > Array.IndexOf(lines, refusal));
            Assert.Empty(homeSnapshot.GetProperty("PendingRequests").EnumerateArray());
            Assert.Empty(homeSnapshot.GetProperty("Grants").EnumerateArray());
            var process = Assert.Single(lines.Where(line => line.ValueKind == JsonValueKind.Object &&
                line.TryGetProperty("processFailed", out var failed) && failed.GetBoolean()));
            Assert.StartsWith(expectedPrefix, Assert.Single(process.GetProperty("causes").EnumerateArray()).GetString() ?? throw new InvalidOperationException("No original process failure text was published."));
            Assert.Contains("Dev read runs as retained background work", output.ToString());
            Assert.Contains("\"projectResourcesConfigured\":true", output.ToString());
            Assert.Contains("\"localHomeStarted\":true", output.ToString());
            Assert.Contains("\"modelInitialized\":false", output.ToString());
            Assert.Contains("\"devExecutionTrust\":true", output.ToString());
            Assert.Contains("\"PendingRequests\":[]", output.ToString());
            Assert.Contains("\"Grants\":[]", output.ToString());
            Assert.Contains("\"commandFailed\":true", output.ToString());
            Assert.Contains("Attach or reopen the SAME saved Task/Run/project first.", output.ToString());
            Assert.DoesNotContain("documentReadRequested", output.ToString());
            Assert.DoesNotContain("initialObservationStarted", output.ToString());
            Assert.DoesNotContain("originalPreparedSetup", output.ToString());
            actualHomeRead = new FileHomeCoreStateStore(Path.Combine(root, "Home", "state.json")).ReadAsync(token);
            var observed = await actualHomeRead;
            Assert.True(observed.IsSuccess);
            var state = observed.State ?? throw new InvalidOperationException("The real Home store returned no state.");
            var record = Assert.Single(state.Records.Where(value => value.RecordType == "files.native-workspace"));
            var configuration = record.Payload.Deserialize<NativeFilesWorkspaceConfiguration>()
                ?? throw new InvalidOperationException("No actual persisted Files configuration exists.");
            Assert.Equal(filesRoot, configuration.RootDirectory);
            Assert.NotEqual(Guid.Empty, configuration.StoreId);
            Assert.NotEqual(Guid.Empty, configuration.AppFolders["write"].Value);
            Assert.True(Directory.Exists(Path.Combine(filesRoot, "Write")));
            Assert.Empty(state.Records.Where(value => value.RecordType.StartsWith("dev.project.setup", StringComparison.Ordinal)));
            database = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(root, "haven.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            databaseOpen = database.OpenAsync(token); await databaseOpen;
            query = database.CreateCommand(); query.CommandText = "SELECT COUNT(*) FROM task_execution_state;";
            count = query.ExecuteScalarAsync(token);
            Assert.Equal(0L, Convert.ToInt64(await count, CultureInfo.InvariantCulture));
        }
        catch (Exception primary) { Capture(failures, null, primary); }
        finally
        {
            foreach (var actual in new Task?[] { actualRun, actualHomeRead, databaseOpen, count })
                if (actual is not null)
                    try { await actual.ConfigureAwait(false); }
                    catch (Exception cause) { Capture(failures, actual, cause); }
            try { query?.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
            if (database is not null)
            {
                try { databaseClose = database.DisposeAsync().AsTask(); }
                catch (Exception cause) { Capture(failures, null, cause); }
                if (databaseClose is not null)
                    try { await databaseClose.ConfigureAwait(false); }
                    catch (Exception cause) { Capture(failures, databaseClose, cause); }
            }
            try { Environment.SetEnvironmentVariable(selector, previousSelector); }
            catch (Exception cause) { Capture(failures, null, cause); }
            try { Console.SetIn(previousInput); } catch (Exception cause) { Capture(failures, null, cause); }
            try { Console.SetOut(previousOutput); } catch (Exception cause) { Capture(failures, null, cause); }
            if (actualRun?.IsCompleted == true)
            {
                try { input?.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
                try { output.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
                // The public runner intentionally retains shared Home/provider custody on
                // the actual refused original. Preserve its diagnostic data; no clean-close
                // or borrowed-owner disposal is inferred from the terminal integer driver.
            }
            ConsoleOwnership.Release();
        }
        if (failures.Count != 0)
            throw new AggregateException($"Actual configured command-read console or independent cleanup failed; data: {root}; selected Files: {filesRoot}.", failures);
    }
}
