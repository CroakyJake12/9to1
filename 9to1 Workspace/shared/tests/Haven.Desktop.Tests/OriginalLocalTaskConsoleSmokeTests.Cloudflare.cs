using System.Globalization;
using System.Text.Json;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    [Fact]
    public async Task Actual_saved_Cloudflare_observation_without_configuration_creates_no_Task_or_permission_grant()
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await ConsoleOwnership.WaitAsync(token);
        var previousInput = Console.In;
        var previousOutput = Console.Out;
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var input = new StringReader("cf-connections\ncf-setup\nhome-requests\nquit\n");
        string? root = null;
        Task<int>? actualRun = null;
        Task<string>? homeRead = null;
        SqliteConnection? database = null;
        SqliteCommand? query = null;
        Task? databaseOpen = null;
        Task<object?>? count = null;
        Task? databaseClose = null;
        var failures = new List<Exception>();
        var healthyRun = false;
        try
        {
            root = Directory.CreateTempSubdirectory("haven-real-cf-observation-").FullName;
            Console.SetIn(input);
            Console.SetOut(output);
            actualRun = OriginalLocalTaskConsole.RunAsync(["--data-directory", root], token);
            var result = await actualRun;
            Assert.True(result == 0, output.ToString());
            healthyRun = true;
            Assert.Contains("CF_SETUP_REQUIRED", output.ToString());
            Assert.DoesNotContain("\"commandFailed\":true", output.ToString());
            Assert.DoesNotContain("initialObservationStarted", output.ToString());
            Assert.DoesNotContain("discoveryRequested", output.ToString());
            Assert.Contains("\"PendingRequests\":[]", output.ToString());
            Assert.Contains("\"Grants\":[]", output.ToString());
            homeRead = File.ReadAllTextAsync(Path.Combine(root, "Home", "state.json"), token);
            var state = JsonSerializer.Deserialize<HomeCoreStoredState>(await homeRead,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(state);
            Assert.DoesNotContain(state.Records, record => record.RecordId == "home.cloudflare.connection");
            // Read only the real normal process's persisted table, after its complete close.
            // There is no second configured graph or Task/actor/permission issuer here.
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
            foreach (var actual in new Task?[] { actualRun, homeRead, databaseOpen, count })
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
            throw new AggregateException($"Actual Cloudflare observation or independent cleanup failed; source data: {root}.", failures);
    }
}
