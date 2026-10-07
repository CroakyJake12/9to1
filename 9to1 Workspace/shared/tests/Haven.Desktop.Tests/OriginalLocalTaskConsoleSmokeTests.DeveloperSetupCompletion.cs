using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalLocalTaskConsoleSmokeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_reviewed_existing_setup_joins_private_completion_before_dependencies_and_refuses_changed_source(bool changeSource)
    {
        if (!OperatingSystem.IsLinux()) return;
        var token = TestContext.Current.CancellationToken;
        await ConsoleOwnership.WaitAsync(token);
        var previousInput = Console.In; var previousOutput = Console.Out;
        var selector = NativePersonalTaskColdRecoveryConfiguration.OriginalEnvironmentSelector;
        var previousSelector = Environment.GetEnvironmentVariable(selector);
        var input = new ReviewedSetupInput(); var output = new ReviewedSetupOutput();
        var observations = new List<Task>(); var failures = new List<Exception>();
        string? root = null; string? filesRoot = null;
        Task<Task<int>>? actualLaunch = null; Task<int>? actualRun = null; Task<int>? wholeDriver = null;
        var quitRequested = false;
        try
        {
            root = Directory.CreateTempSubdirectory("haven-actual-reviewed-setup-close-").FullName;
            PrepareOriginalPrivateHomeDirectory(root);
            filesRoot = Directory.CreateTempSubdirectory("haven-actual-reviewed-setup-files-").FullName;
            Environment.SetEnvironmentVariable(selector, "1");
            Console.SetIn(input); Console.SetOut(output);
            var actualDataRoot = root;
            // Console.SetIn may synchronize an async read through ReadLine. Keep its
            // synchronous prefix off the controller, and retain BOTH the factory and
            // exact public RunAsync task; Unwrap is only the fixture's encompassing wait.
            actualLaunch = Task.Factory.StartNew(() => OriginalLocalTaskConsole.RunAsync(
                ["--data-directory", actualDataRoot], token), CancellationToken.None,
                TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
            wholeDriver = actualLaunch.Unwrap();
            await output.NextAsync(value => value.TryGetProperty("localHomeStarted", out _), token);
            input.Supply("files-configure " + filesRoot);
            var configured = await output.NextAsync(value => value.TryGetProperty("Configuration", out _), token);
            Assert.Equal(filesRoot, configured.GetProperty("Configuration").GetProperty("RootDirectory").GetString());

            // Only these new private fixture bytes are selected; the production kernel,
            // actor, source READ, manifest review, eight effect owners and journal are real.
            var projectRoot = Directory.CreateDirectory(Path.Combine(filesRoot, "Write", "ReviewedProbe")).FullName;
            var sourcePath = Path.Combine(projectRoot, "Probe.cs");
            var projectPath = Path.Combine(projectRoot, "ReviewedProbe.csproj");
            var sourceBytes = Encoding.UTF8.GetBytes("public static class Probe { public static int Value => 7; }\n");
            var projectBytes = Encoding.UTF8.GetBytes("<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n");
            var sourceWrite = File.WriteAllBytesAsync(sourcePath, sourceBytes, token); observations.Add(sourceWrite); await sourceWrite;
            var projectWrite = File.WriteAllBytesAsync(projectPath, projectBytes, token); observations.Add(projectWrite); await projectWrite;
            input.Supply("project-register write " + projectRoot);
            var requested = await output.NextAsync(value => value.TryGetProperty("projectRegistrationRequested", out _), token);
            var setupId = requested.GetProperty("setupId").GetGuid();
            var read = await ReviewActualSetupRequestAsync(input, output, HomeDeveloperProjectReadAdmissionSource.ReadAction, token);
            var readScope = read.GetProperty("Impact").GetProperty("ResourceBinding").GetProperty("Scopes")[0];
            var readArguments = JsonSerializer.SerializeToUtf8Bytes(new
            {
                sourceSelection = readScope.GetProperty("Id").GetString(),
                sourceRevision = readScope.GetProperty("Revision").GetString(),
                limits = "64 files/64 folders; 4 MiB per file; 16 MiB total; no copy or mutation"
            });
            Assert.Equal(Convert.ToHexString(SHA256.HashData(readArguments)), read.GetProperty("Impact").GetProperty("ArgumentsDigest").GetString());
            input.Supply("home-accept " + read.GetProperty("RequestId").GetString());

            var prepared = await output.NextAsync(value => value.TryGetProperty("originalPreparedSetup", out _), token);
            var intent = prepared.GetProperty("originalPreparedSetup").Deserialize<DeveloperProjectSetupIntent>()
                ?? throw new InvalidDataException("No actual reviewed setup intent was returned.");
            Assert.Equal(setupId, intent.SetupId); Assert.Equal(projectRoot, intent.OriginalExistingProjectRoot);
            Assert.Empty(intent.Folders); Assert.Equal(2, intent.Files.Length); Assert.Equal(8, intent.Steps.Length);
            Assert.Equal(8, intent.Steps.Select(step => step.StepId).Distinct().Count());
            var observedSource = Assert.Single(intent.Files, file => file.RelativePath == "Probe.cs");
            var observedProject = Assert.Single(intent.Files, file => file.RelativePath == "ReviewedProbe.csproj");
            Assert.Equal(sourceBytes.LongLength, observedSource.SizeBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant(), observedSource.ContentSha256);
            Assert.Equal(projectBytes.LongLength, observedProject.SizeBytes);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(projectBytes)).ToLowerInvariant(), observedProject.ContentSha256);
            var review = await ReviewActualSetupRequestAsync(input, output, HomeDeveloperProjectSetupPermissionSource.SetupAction, token);
            Assert.NotEqual(read.GetProperty("RequestId").GetString(), review.GetProperty("RequestId").GetString());
            Assert.Equal(read.GetProperty("Impact").GetProperty("ResourceBinding").GetProperty("OriginalActor").GetRawText(),
                review.GetProperty("Impact").GetProperty("ResourceBinding").GetProperty("OriginalActor").GetRawText());
            var arguments = JsonSerializer.SerializeToUtf8Bytes(new
            {
                intent, digest = intent.Digest(),
                originalEffectLimits = "512 distinct once-issued steps; no command trust, model permission, copy replay or source READ grant"
            });
            Assert.Equal(Convert.ToHexString(SHA256.HashData(arguments)), review.GetProperty("Impact").GetProperty("ArgumentsDigest").GetString());
            input.Supply("home-accept " + review.GetProperty("RequestId").GetString());

            var completed = await output.NextAsync(value => value.TryGetProperty("originalSetupAcknowledged", out _), token);
            var checkpoint = completed.GetProperty("originalSetupAcknowledged").Deserialize<DeveloperProjectSetupCheckpoint>()
                ?? throw new InvalidDataException("No actual final journal ACK was returned.");
            Assert.Equal(setupId, checkpoint.Intent.SetupId); Assert.Equal(17, checkpoint.Revision);
            Assert.Equal(intent.Steps.Select(step => step.StepId), checkpoint.Observations.Select(row => row.StepId));
            Assert.All(checkpoint.Observations, row =>
            {
                Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, row.State);
                Assert.False(string.IsNullOrWhiteSpace(row.OriginalReceiptReference));
                Assert.False(string.IsNullOrWhiteSpace(row.OriginalOutcomeDigest)); Assert.Null(row.ErrorCode);
            });
            var stepEvents = output.Events.Where(value => value.TryGetProperty("OriginalAdmission", out _)).ToArray();
            Assert.Equal(8, stepEvents.Length);
            Assert.Equal(new long[] { 3, 5, 7, 9, 11, 13, 15, 17 },
                stepEvents.Select(value => value.GetProperty("AcknowledgedCheckpoint").GetProperty("Revision").GetInt64()));
            if (changeSource)
            {
                // A real physical mutation after all ACKs must defeat final current-source
                // validation. Stored complete rows never become a substitute authority.
                var mutation = File.WriteAllTextAsync(sourcePath, "// changed after actual final ACK\n" + Encoding.UTF8.GetString(sourceBytes), token);
                observations.Add(mutation); await mutation;
            }
            input.Supply("quit"); quitRequested = true;
            var exit = await wholeDriver;
            actualRun = await actualLaunch;
            Assert.True(exit == (changeSource ? 1 : 0), output.ToString());
            var recoveryRead = File.ReadAllTextAsync(Path.Combine(root, "startup-recovery.json"), token);
            observations.Add(recoveryRead);
            using var recovery = JsonDocument.Parse(await recoveryRead);
            Assert.Equal(!changeSource, recovery.RootElement.GetProperty("currentRun").GetProperty("cleanShutdown").GetBoolean());
            var stateRead = new FileHomeCoreStateStore(Path.Combine(root, "Home", "state.json")).ReadAsync(token);
            observations.Add(stateRead); var state = await stateRead; Assert.True(state.IsSuccess);
            var storedState = state.State ?? throw new InvalidDataException("The actual Home store returned no state.");
            var stored = Assert.Single(storedState.Records, row => row.RecordType == "home.dev-project-setup");
            var durable = stored.Payload.Deserialize<DeveloperProjectSetupCheckpoint>()!;
            Assert.Equal(17, durable.Revision); Assert.Equal(setupId, durable.Intent.SetupId);
            Assert.All(durable.Observations, row => Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, row.State));
            var permission = Assert.Single(storedState.Records, row => row.RecordType == "home.permissions-trust").Payload;
            Assert.Empty(permission.GetProperty("Grants").EnumerateArray());
            var audit = permission.GetProperty("Audit").EnumerateArray().ToArray();
            if (changeSource)
            {
                Assert.Contains("\"processFailed\":true", output.ToString());
                Assert.Contains("DEV_SETUP_FINAL_JOURNAL_ACK_REQUIRED", output.ToString());
                Assert.DoesNotContain(audit, row => row.GetProperty("ResultCode").GetString() == "DEV_SETUP_ORIGINAL_ACKNOWLEDGED");
                Assert.Contains(audit, row => row.GetProperty("RequestId").GetString() == review.GetProperty("RequestId").GetString()
                    && row.GetProperty("ResultCode").GetString() == "DEV_SETUP_ORIGINAL_INCOMPLETE"
                    && row.GetProperty("RequestState").GetInt32() == (int)HomePermissionRequestState.PartiallyCompleted);
            }
            else
            {
                Assert.DoesNotContain("\"processFailed\":true", output.ToString());
                Assert.Contains(audit, row => row.GetProperty("RequestId").GetString() == review.GetProperty("RequestId").GetString()
                    && row.GetProperty("ResultCode").GetString() == "DEV_SETUP_ORIGINAL_ACKNOWLEDGED");
            }
            Assert.DoesNotContain("initialObservationStarted", output.ToString());
        }
        catch (Exception cause) { Capture(failures, null, cause); }
        finally
        {
            // A timed-out observation never drops the actual application driver. The same
            // supported quit drains it; all original tasks and observer reads are joined.
            if (actualLaunch is not null && !quitRequested)
                try { input.Supply("quit"); } catch (Exception cause) { Capture(failures, null, cause); }
            if (actualLaunch is not null)
                try { actualRun = await actualLaunch; } catch (Exception cause) { Capture(failures, actualLaunch, cause); }
            if (actualRun is not null)
                try { await actualRun; } catch (Exception cause) { Capture(failures, actualRun, cause); }
            if (wholeDriver is not null)
                try { await wholeDriver; } catch (Exception cause) { Capture(failures, wholeDriver, cause); }
            foreach (var original in observations)
                try { await original; } catch (Exception cause) { Capture(failures, original, cause); }
            foreach (var original in output.OriginalReads)
                try { await original; } catch (Exception cause) { Capture(failures, original, cause); }
            foreach (var original in input.OriginalReads)
                try { await original; } catch (Exception cause) { Capture(failures, original, cause); }
            try { Environment.SetEnvironmentVariable(selector, previousSelector); } catch (Exception cause) { Capture(failures, null, cause); }
            try { Console.SetIn(previousInput); } catch (Exception cause) { Capture(failures, null, cause); }
            try { Console.SetOut(previousOutput); } catch (Exception cause) { Capture(failures, null, cause); }
            if (root is not null)
                try { await File.WriteAllTextAsync(Path.Combine(root, "actual-reviewed-setup-console.log"), output.ToString(), CancellationToken.None); }
                catch (Exception cause) { Capture(failures, null, cause); }
            try { input.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
            try { output.Dispose(); } catch (Exception cause) { Capture(failures, null, cause); }
            ConsoleOwnership.Release(); // Private evidence/keys/source bytes remain retained.
        }
        if (failures.Count != 0)
            throw new AggregateException($"Actual reviewed setup/permission/dependency close failed; retained private data: {root}; Files: {filesRoot}.", failures);
    }

    private static async Task<JsonElement> ReviewActualSetupRequestAsync(ReviewedSetupInput input,
        ReviewedSetupOutput output, string action, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (true)
        {
            input.Supply("home-requests");
            var snapshot = await output.NextAsync(value => value.TryGetProperty("PendingRequests", out _), token);
            var pending = snapshot.GetProperty("PendingRequests").EnumerateArray()
                .Where(row => row.GetProperty("Scope").GetProperty("ActionName").GetString() == action).ToArray();
            if (pending.Length == 0)
            {
                if (DateTimeOffset.UtcNow >= deadline) throw new TimeoutException("The actual Home request was not published: " + action);
                await Task.Delay(25, token); continue;
            }
            var request = Assert.Single(pending); var id = request.GetProperty("RequestId").GetString();
            input.Supply("home-display " + id);
            var displayed = await output.NextAsync(value => value.TryGetProperty("RequestId", out var current) && current.GetString() == id, token);
            Assert.Equal(request.GetRawText(), displayed.GetRawText());
            Assert.Equal("dev", displayed.GetProperty("Scope").GetProperty("TargetAppId").GetString());
            Assert.False(displayed.GetProperty("Scope").GetProperty("IncludesAllObjects").GetBoolean());
            Assert.True(displayed.GetProperty("Caller").GetProperty("IsVerified").GetBoolean());
            Assert.True(displayed.GetProperty("Policy").GetProperty("RequiresPerActionApproval").GetBoolean());
            Assert.False(displayed.GetProperty("Policy").GetProperty("HasExternalSideEffects").GetBoolean());
            Assert.False(displayed.GetProperty("AlwaysTrustWarningShown").GetBoolean());
            Assert.Equal(JsonValueKind.Null, displayed.GetProperty("AppliedGrantId").ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(displayed.GetProperty("Impact").GetProperty("ChangePreview").GetString()));
            return displayed; // The caller explicitly reviews its exact arguments before ONE Accept.
        }
    }

    private sealed class ReviewedSetupInput : TextReader
    {
        private readonly Channel<string?> _commands = Channel.CreateUnbounded<string?>(new() { SingleReader = true });
        private readonly object _gate = new(); private readonly List<Task> _reads = [];
        internal IReadOnlyList<Task> OriginalReads { get { lock (_gate) return _reads.ToArray(); } }
        internal void Supply(string command)
        { if (!_commands.Writer.TryWrite(command)) throw new InvalidOperationException("The actual console input was closed."); }
        private Task<string?> AcquireRead(CancellationToken token)
        {
            var actual = _commands.Reader.ReadAsync(token).AsTask();
            lock (_gate) _reads.Add(actual); return actual;
        }
        public override string? ReadLine() => AcquireRead(CancellationToken.None).GetAwaiter().GetResult();
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken) => new(AcquireRead(cancellationToken));
        protected override void Dispose(bool disposing) { if (disposing) _commands.Writer.TryComplete(); base.Dispose(disposing); }
    }

    private sealed class ReviewedSetupOutput : TextWriter
    {
        private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new() { SingleReader = true });
        private readonly StringBuilder _text = new(); private readonly object _gate = new();
        private readonly List<JsonElement> _events = []; private readonly List<Task> _reads = [];
        public override Encoding Encoding => System.Text.Encoding.UTF8;
        internal IReadOnlyList<JsonElement> Events => _events.ToArray();
        internal IReadOnlyList<Task> OriginalReads => _reads.ToArray();
        public override void WriteLine(string? value)
        {
            lock (_gate) _text.AppendLine(value);
            if (!_lines.Writer.TryWrite(value ?? "")) throw new InvalidOperationException("The actual output observer was closed.");
        }
        public override string ToString() { lock (_gate) return _text.ToString(); }
        internal async Task<JsonElement> NextAsync(Func<JsonElement, bool> predicate, CancellationToken token)
        {
            using var observation = CancellationTokenSource.CreateLinkedTokenSource(token); observation.CancelAfter(TimeSpan.FromSeconds(30));
            while (true)
            {
                var actual = _lines.Reader.ReadAsync(observation.Token).AsTask(); _reads.Add(actual);
                using var document = JsonDocument.Parse(await actual);
                var value = document.RootElement.Clone();
                if (value.ValueKind != JsonValueKind.Object) continue;
                _events.Add(value);
                if (value.TryGetProperty("commandFailed", out _) || value.TryGetProperty("processFailed", out _))
                    throw new InvalidOperationException(value.GetRawText());
                if (predicate(value)) return value;
            }
        }
    }
}
