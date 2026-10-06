using System.Globalization;
using System.Text.Json;
using Haven.Desktop.Services;
using Haven.Core;
using Microsoft.Data.Sqlite;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    [Fact]
    public async Task Explicit_local_configuration_persists_only_the_actual_provider_metadata_without_a_model_Task_or_permission_grant()
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await ConsoleOwnership.WaitAsync(token);
        var previousInput = Console.In;
        var previousOutput = Console.Out;
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var input = new StringReader("local-configure http://127.0.0.1/\nhome-requests\nquit\n");
        string? root = null;
        Task<int>? actualRun = null;
        Task<string>? configurationRead = null;
        SqliteConnection? database = null;
        SqliteCommand? query = null;
        Task? databaseOpen = null;
        Task<object?>? count = null;
        Task? databaseClose = null;
        var failures = new List<Exception>();
        var healthyRun = false;
        try
        {
            root = Directory.CreateTempSubdirectory("haven-real-local-config-").FullName;
            Console.SetIn(input);
            Console.SetOut(output);
            actualRun = OriginalLocalTaskConsole.RunAsync(["--data-directory", root], token);
            Assert.True(await actualRun == 0, output.ToString());
            healthyRun = true;
            Assert.Contains("\"providerMetadataConfigured\":true", output.ToString());
            Assert.Contains("\"localHomeStarted\":true", output.ToString());
            Assert.Contains("\"modelInitialized\":false", output.ToString());
            Assert.DoesNotContain("\"commandFailed\":true", output.ToString());
            Assert.DoesNotContain("initialObservationStarted", output.ToString());
            Assert.Contains("\"PendingRequests\":[]", output.ToString());
            Assert.Contains("\"Grants\":[]", output.ToString());
            configurationRead = File.ReadAllTextAsync(Path.Combine(root, "model-providers.json"), token);
            var rows = JsonSerializer.Deserialize<List<ProviderConfiguration>>(await configurationRead,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("No actual persisted provider metadata was returned.");
            var row = Assert.Single(rows);
            Assert.Equal("llama-cpp", row.Id);
            Assert.Equal(ModelProviderKind.OpenAICompatible, row.Kind);
            Assert.Equal("http://127.0.0.1/", row.Endpoint);
            Assert.True(row.IsEnabled); Assert.True(row.IsLocal); Assert.False(row.AllowCloudFallback);
            Assert.Empty(row.Metadata);
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
            foreach (var actual in new Task?[] { actualRun, configurationRead, databaseOpen, count })
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
            throw new AggregateException($"Actual local provider configuration or independent cleanup failed; source data: {root}.", failures);
    }
}
