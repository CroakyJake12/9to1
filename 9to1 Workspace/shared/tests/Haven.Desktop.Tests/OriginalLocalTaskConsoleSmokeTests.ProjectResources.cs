using System.Globalization;
using System.Text.Json;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    [Fact]
    public async Task Actual_opted_in_project_console_configures_and_drains_the_same_sources_without_a_Task_or_READ_grant()
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await ConsoleOwnership.WaitAsync(token);
        var previousInput = Console.In;
        var previousOutput = Console.Out;
        var previousSelector = Environment.GetEnvironmentVariable(NativePersonalTaskColdRecoveryConfiguration.OriginalEnvironmentSelector);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var input = new StringReader("home-requests\nquit\n");
        string? root = null;
        Task<int>? actualRun = null;
        Task<string>? recoveryRead = null;
        SqliteConnection? database = null;
        SqliteCommand? query = null;
        Task? databaseOpen = null;
        Task<object?>? count = null;
        Task? databaseClose = null;
        var failures = new List<Exception>();
        var healthyRun = false;
        try
        {
            root = Directory.CreateTempSubdirectory("haven-real-project-console-").FullName;
            Environment.SetEnvironmentVariable(NativePersonalTaskColdRecoveryConfiguration.OriginalEnvironmentSelector, "1");
            Console.SetIn(input);
            Console.SetOut(output);
            actualRun = OriginalLocalTaskConsole.RunAsync(["--data-directory", root], token);
            Assert.True(await actualRun == 0, output.ToString());
            healthyRun = true;
            Assert.Contains("\"projectResourcesConfigured\":true", output.ToString());
            Assert.Contains("\"localHomeStarted\":true", output.ToString());
            Assert.Contains("\"modelInitialized\":false", output.ToString());
            Assert.DoesNotContain("\"commandFailed\":true", output.ToString());
            Assert.DoesNotContain("initialObservationStarted", output.ToString());
            Assert.Contains("\"PendingRequests\":[]", output.ToString());
            Assert.Contains("\"Grants\":[]", output.ToString());
            recoveryRead = File.ReadAllTextAsync(Path.Combine(root, "startup-recovery.json"), token);
            using var recovery = JsonDocument.Parse(await recoveryRead);
            Assert.True(recovery.RootElement.GetProperty("currentRun").GetProperty("cleanShutdown").GetBoolean());
            database = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path.Combine(root, "haven.db"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            databaseOpen = database.OpenAsync(token);
            await databaseOpen;
            query = database.CreateCommand();
            query.CommandText = "SELECT COUNT(*) FROM task_execution_state;";
            count = query.ExecuteScalarAsync(token);
            Assert.Equal(0L, Convert.ToInt64(await count, CultureInfo.InvariantCulture));
        }
        catch (Exception primary) { Capture(failures, null, primary); }
        finally
        {
            foreach (var actual in new Task?[] { actualRun, recoveryRead, databaseOpen, count })
                if (actual is not null)
                    try { await actual.ConfigureAwait(false); }
                    catch (Exception cause) { Capture(failures, actual, cause); }
            try { query?.Dispose(); }
            catch (Exception cause) { Capture(failures, null, cause); }
            if (database is not null)
            {
                try { databaseClose = database.DisposeAsync().AsTask(); }
                catch (Exception cause) { Capture(failures, null, cause); }
                if (databaseClose is not null)
                    try { await databaseClose.ConfigureAwait(false); }
                    catch (Exception cause) { Capture(failures, databaseClose, cause); }
            }
            try { Environment.SetEnvironmentVariable(NativePersonalTaskColdRecoveryConfiguration.OriginalEnvironmentSelector, previousSelector); }
            catch (Exception cause) { Capture(failures, null, cause); }
            try { Console.SetIn(previousInput); }
            catch (Exception cause) { Capture(failures, null, cause); }
            try { Console.SetOut(previousOutput); }
            catch (Exception cause) { Capture(failures, null, cause); }
            if (healthyRun && (database is null || databaseClose?.IsCompletedSuccessfully == true))
            {
                try { input.Dispose(); }
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
            throw new AggregateException($"Actual project console configuration or independent cleanup failed; source data: {root}.", failures);
    }
}
