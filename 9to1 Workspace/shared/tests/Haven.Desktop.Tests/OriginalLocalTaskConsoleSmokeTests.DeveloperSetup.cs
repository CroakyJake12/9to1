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
    public async Task Actual_fresh_console_configures_the_selected_empty_Files_root_and_closes_setup_owners_without_Task_or_review(bool quit)
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
        var failures = new List<Exception>(); var healthyRun = false;
        try
        {
            root = Directory.CreateTempSubdirectory("haven-real-fresh-setup-console-").FullName;
            filesRoot = Directory.CreateTempSubdirectory("haven-explicit-empty-files-").FullName;
            Environment.SetEnvironmentVariable(selector, "0");
            input = new StringReader("files-configure " + filesRoot + "\nfiles-status\nhome-requests\n" + (quit ? "quit\n" : ""));
            Console.SetIn(input); Console.SetOut(output);
            actualRun = OriginalLocalTaskConsole.RunAsync(["--data-directory", root], token);
            Assert.True(await actualRun == 0, output.ToString()); healthyRun = true;
            Assert.Contains("project-register", output.ToString());
            Assert.Contains("container-create", output.ToString());
            Assert.Contains("\"localHomeStarted\":true", output.ToString());
            Assert.Contains("\"modelInitialized\":false", output.ToString());
            Assert.Contains("\"devExecutionTrust\":false", output.ToString());
            Assert.Contains("\"PendingRequests\":[]", output.ToString());
            Assert.Contains("\"Grants\":[]", output.ToString());
            Assert.DoesNotContain("\"commandFailed\":true", output.ToString());
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
            if (healthyRun && (database is null || databaseClose?.IsCompletedSuccessfully == true))
            {
                try { input?.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
                try { output.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
                if (filesRoot is not null)
                    try { Directory.Delete(filesRoot, true); } catch (Exception cause) { Capture(failures, null, cause); }
                if (root is not null)
                    try { Directory.Delete(root, true); } catch (Exception cause) { Capture(failures, null, cause); }
            }
            ConsoleOwnership.Release();
        }
        if (failures.Count != 0)
            throw new AggregateException($"Actual empty-root setup console or independent cleanup failed; data: {root}; selected Files: {filesRoot}.", failures);
    }
}
