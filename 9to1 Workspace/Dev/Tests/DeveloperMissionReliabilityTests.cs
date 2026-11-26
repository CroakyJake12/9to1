using System.Net;
using System.Text.Json.Nodes;
using HavenOS.Apps.Dev.Autonomous;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperMissionReliabilityTests
{
    [Fact]
    public async Task Read_reordering_and_memory_do_not_evade_duplicate_detection()
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway();
        gateway.Reply("""{"op":"read_file","path":"tests/Acceptance.cs","memory":{"finding":"first"}}""");
        gateway.Reply("""{"memory":{"finding":"second"},"path":"tests/Acceptance.cs","op":"read_file"}""");
        await using var engine = new DeveloperAutonomousMissionEngine(fixture.Options, gateway);
        var mission = await engine.InitializeOrResumeAsync();

        await engine.RunSingleStepAsync(); // Admit.
        await engine.RunSingleStepAsync(); // Read.
        await engine.RunSingleStepAsync(); // Duplicate.

        Assert.Contains("identical read-only action", fixture.Store.ReadTranscript(mission.MissionId)[^1].Content);
        Assert.Single(engine.CurrentMission!.ActiveTask!.VisitedActionDigests);
        Assert.Equal(1, engine.CurrentMission.ActiveTask.IdleTurns);
        Assert.Equal(2, engine.CurrentMission.Notes.Count);
    }

    [Fact]
    public async Task Exhausting_a_programme_with_a_deferred_record_reports_blocked()
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway();
        gateway.Reply("""{"op":"defer_task","reason":"Required input unavailable"}""");
        await using var engine = new DeveloperAutonomousMissionEngine(fixture.Options, gateway);
        var mission = await engine.InitializeOrResumeAsync();

        await engine.StartAsync();

        Assert.Equal(DeveloperAutonomousMissionState.Blocked, engine.CurrentMission!.State);
        Assert.Equal("IX-BUILD-001", Assert.Single(engine.CurrentMission.BlockedRecordIds));
        Assert.Empty(engine.CurrentMission.CompletedRecordIds);
        Assert.Contains(fixture.Store.ReadEvents(mission.MissionId), e => e.Kind == "mission_blocked");
        Assert.DoesNotContain(fixture.Store.ReadEvents(mission.MissionId), e => e.Kind == "mission_completed");
    }

    [Theory]
    [InlineData("unloaded-worker", "reviewer", false)]
    [InlineData("worker", "unloaded-reviewer", true)]
    public async Task An_unloaded_selected_model_is_never_dispatched_or_replaced(
        string workerId, string supervisorId, bool delegation)
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway();
        var options = fixture.Options with { WorkerModelId = workerId, SupervisorModelId = supervisorId };
        await using var engine = new DeveloperAutonomousMissionEngine(options, gateway);
        await engine.InitializeOrResumeAsync();
        await engine.RefreshModelsAsync();

        if (delegation)
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.DelegateToSupervisorAsync("Check build."));
            Assert.Contains(supervisorId, failure.Message);
        }
        else
        {
            await engine.RunSingleStepAsync(); // Admit.
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RunSingleStepAsync());
            Assert.Contains(workerId, failure.Message);
        }

        Assert.Equal(workerId, engine.CurrentMission!.WorkerModelId);
        Assert.Equal(supervisorId, engine.CurrentMission.SupervisorModelId);
        Assert.Empty(gateway.Requests);
    }

    [Theory]
    [InlineData(512, true)]
    [InlineData(513, false)]
    public async Task Review_uses_the_full_diff_and_reserves_the_actual_completion_budget(int promptTokens, bool fits)
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway
        {
            ReviewerContext = 640,
            Measure = (model, _) => model == "reviewer" ? promptTokens : 128
        };
        gateway.Reply("""{"op":"complete_task","summary":"Verified change"}""");
        gateway.Responses.Enqueue((request, _, _, _) =>
        {
            var payload = JsonNode.Parse(request.Messages[^1].Content)!.AsObject();
            var answer = $$"""{"approve":true,"sha256":"{{payload["binding_digest"]}}","reason":"Checked full patch."}""";
            return Task.FromResult(Completion(request, answer));
        });
        var initial = await fixture.SeedVerifiedTaskAsync(gateway);
        await using var engine = new DeveloperAutonomousMissionEngine(fixture.Options, gateway, fixture.Store);
        await engine.InitializeOrResumeAsync(initial.MissionId);

        await engine.RunSingleStepAsync();

        var measured = Assert.Single(gateway.Measurements, entry => entry.Model == "reviewer");
        var measuredDiff = JsonNode.Parse(measured.Messages[^1].Content)!["diff"]!.GetValue<string>();
        Assert.True(measuredDiff.Length > 16_000);
        Assert.Contains("TAIL-SENTINEL", measuredDiff);
        Assert.Equal(initial.ActiveTask!.DiffText, measuredDiff);
        if (fits)
        {
            var request = Assert.Single(gateway.Requests, request => request.ModelId == "reviewer");
            Assert.Equal(measured.Messages.ToArray(), request.Messages.ToArray());
            Assert.Equal(128, request.MaxTokens);
            Assert.True(engine.CurrentMission!.ActiveTask?.Review?.Approved is true);
        }
        else
        {
            Assert.DoesNotContain(gateway.Requests, request => request.ModelId == "reviewer");
            Assert.Null(engine.CurrentMission!.ActiveTask?.Review);
            Assert.Contains("513 measured prompt tokens plus 128 completion tokens",
                fixture.Store.ReadTranscript(initial.MissionId)[^1].Content);
        }
    }

    [Theory]
    [InlineData("src/Alternative.csproj", "tests/Acceptance.csproj", "Focused", false)]
    [InlineData("src/App.csproj", "tests/Alternative.csproj", "Focused", false)]
    [InlineData("src/App.csproj", "tests/Acceptance.csproj", "Different", false)]
    [InlineData("src/App.csproj", "tests/Acceptance.csproj", "Focused", true)]
    public async Task Verification_cache_requires_the_exact_diff_projects_and_filter(
        string buildProject, string testProject, string filter, bool changeDiff)
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway();
        gateway.Reply($$"""{"op":"complete_task","build_project":"{{buildProject}}","test_project":"{{testProject}}","test_filter":"{{filter}}"}""");
        var initial = await fixture.SeedVerifiedTaskAsync(gateway);
        if (changeDiff)
            File.AppendAllText(Path.Combine(initial.ActiveTask!.WorktreePath!, "tests", "Acceptance.cs"), "\n// drift\n");

        await using var engine = new DeveloperAutonomousMissionEngine(fixture.Options, gateway, fixture.Store);
        await engine.InitializeOrResumeAsync(initial.MissionId);
        using var cancellation = new CancellationTokenSource();
        var attempted = false;
        engine.EventRecorded += recorded =>
        {
            if (recorded.Kind != "verification_started")
                return;
            attempted = true;
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        };

        await engine.RunSingleStepAsync(cancellation.Token);

        Assert.True(attempted);
        Assert.Single(gateway.Requests);
        Assert.DoesNotContain(fixture.Store.ReadEvents(initial.MissionId), e => e.Kind == "review_started");
    }

    [Fact]
    public async Task Read_only_gateway_operations_retry_transient_failures_before_dispatch()
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway { StatusFailures = 2, TokenFailures = 2 };
        gateway.Reply("""{"op":"git_diff"}""");
        await using var engine = new DeveloperAutonomousMissionEngine(fixture.Options, gateway);
        await engine.InitializeOrResumeAsync();

        await engine.RunSingleStepAsync();
        await engine.RunSingleStepAsync();

        Assert.Equal(5, gateway.StatusCalls);
        Assert.Equal(3, gateway.Measurements.Count);
        Assert.Single(gateway.Requests);
        Assert.Equal(1, engine.CurrentMission!.ActiveTask!.TurnCount);
    }

    [Fact]
    public async Task Cancelling_while_waiting_to_retry_token_measurement_exits_without_another_attempt()
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway { TokenFailures = 3 };
        var context = new DeveloperMissionContextManager(fixture.Store, gateway);
        using var cancellation = new CancellationTokenSource();
        var measure = context.MeasurePromptTokensAsync("worker", [new("user", "Measure this.")], cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => measure);

        Assert.Single(gateway.Measurements);
    }

    [Fact]
    public async Task Compaction_rejects_the_minimum_projection_if_it_exceeds_the_reserved_context()
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway();
        await using var engine = new DeveloperAutonomousMissionEngine(fixture.Options, gateway);
        var mission = await engine.InitializeOrResumeAsync();
        var context = new DeveloperMissionContextManager(fixture.Store, gateway);
        gateway.Measure = (_, _) => 385;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.BuildWorkingContextAsync(
            mission, "System", "worker", 512, 512, forceCompact: true, completionTokens: 128));

        Assert.Contains("385 measured prompt tokens plus 128 completion tokens", failure.Message);
        Assert.Equal(2, gateway.Measurements.Count);
        Assert.Empty(gateway.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Stalled_stream_cancels_transport_and_requires_an_owned_stop_confirmation(bool acknowledgeStop)
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway();
        var streamToken = CancellationToken.None;
        gateway.Responses.Enqueue(async (_, _, started, token) =>
        {
            streamToken = token;
            started?.Invoke("owned-stalled-run");
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The fake stream must be cancelled.");
        });
        gateway.Stop = _ =>
        {
            Assert.True(streamToken.IsCancellationRequested);
            return acknowledgeStop ? Task.CompletedTask : Task.FromException(new IOException("Stop was not acknowledged."));
        };
        var options = fixture.Options with { StreamIdleTimeout = TimeSpan.FromMilliseconds(150) };
        await using var engine = new DeveloperAutonomousMissionEngine(options, gateway, fixture.Store);
        var mission = await engine.InitializeOrResumeAsync();
        await engine.RunSingleStepAsync();

        var failure = await Record.ExceptionAsync(() => engine.RunSingleStepAsync().WaitAsync(TimeSpan.FromSeconds(10)));

        if (acknowledgeStop)
            Assert.IsType<TimeoutException>(failure);
        else
            Assert.Contains("did not confirm stopping", Assert.IsType<IOException>(failure).Message);
        Assert.Equal("owned-stalled-run", Assert.Single(gateway.StoppedRuns));
        Assert.Single(gateway.Requests);
        Assert.DoesNotContain(fixture.Store.ReadTranscript(mission.MissionId), item => item.Role == "assistant");
        var saved = fixture.Store.LoadMission(mission.MissionId)!;
        Assert.Equal(!acknowledgeStop, saved.CompletionStopUnconfirmed);
        Assert.Equal(acknowledgeStop ? null : "owned-stalled-run", saved.ActiveDulcheRunId);
        if (!acknowledgeStop)
            Assert.Throws<InvalidOperationException>(() => { _ = engine.StartAsync(); });
    }

    [Fact]
    public async Task Ambiguous_disconnect_before_run_headers_blocks_redispatch_after_resume()
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway();
        gateway.Responses.Enqueue((_, _, _, _) =>
            Task.FromException<DeveloperCompletionResult>(new HttpRequestException("Connection interrupted.")));
        DeveloperAutonomousMission mission;
        await using (var engine = new DeveloperAutonomousMissionEngine(fixture.Options, gateway, fixture.Store))
        {
            mission = await engine.InitializeOrResumeAsync();
            await engine.RunSingleStepAsync();
            await Assert.ThrowsAsync<IOException>(() => engine.RunSingleStepAsync());
        }

        await using var resumed = new DeveloperAutonomousMissionEngine(fixture.Options, gateway, fixture.Store);
        var saved = await resumed.InitializeOrResumeAsync(mission.MissionId);
        Assert.True(saved.CompletionStopUnconfirmed);
        Assert.Null(saved.ActiveDulcheRunId);
        Assert.Throws<InvalidOperationException>(() => { _ = resumed.StartAsync(); });
        Assert.Single(gateway.Requests);
        Assert.Empty(gateway.StoppedRuns);
    }

    [Fact]
    public async Task Partial_edit_failure_allows_rereading_the_changed_source()
    {
        using var fixture = await MissionFixture.CreateAsync();
        var gateway = new ScriptedGateway();
        gateway.Reply("""{"op":"read_file","path":"tests/Acceptance.cs"}""");
        gateway.Reply("""{"op":"edit_file","edits":[{"path":"tests/Acceptance.cs","old":"// baseline","new":"// recovered"},{"path":"tests/Acceptance.cs","old":"missing old text","new":"unused"}]}""");
        gateway.Reply("""{"op":"read_file","path":"tests/Acceptance.cs"}""");
        await using var engine = new DeveloperAutonomousMissionEngine(fixture.Options, gateway, fixture.Store);
        var mission = await engine.InitializeOrResumeAsync();
        await engine.RunSingleStepAsync();
        await engine.RunSingleStepAsync();
        await engine.RunSingleStepAsync();
        await engine.RunSingleStepAsync();
        var receipt = fixture.Store.ReadTranscript(mission.MissionId).Last(message => message.Role == "user").Content;
        Assert.Contains("// recovered", receipt);
        Assert.DoesNotContain("Rejected: identical", receipt);
        Assert.Equal("// recovered\n", await File.ReadAllTextAsync(
            Path.Combine(engine.CurrentMission!.ActiveTask!.WorktreePath!, "tests", "Acceptance.cs")));
    }

    private static DeveloperCompletionResult Completion(DeveloperCompletionRequest request, string answer) =>
        new("finished", request.ModelId, answer, "", "stop", 0, 0, null, TimeSpan.Zero);

    private sealed class ScriptedGateway : IDeveloperModelGateway
    {
        public int StatusFailures { get; init; }
        public int StatusCalls { get; private set; }
        public int TokenFailures { get; init; }
        public int ReviewerContext { get; init; } = 32_768;
        public Func<string, IReadOnlyList<DeveloperChatMessage>, int> Measure { get; set; } = (_, _) => 64;
        public Func<string, Task> Stop { get; set; } = _ => Task.CompletedTask;
        public List<DeveloperCompletionRequest> Requests { get; } = [];
        public List<(string Model, IReadOnlyList<DeveloperChatMessage> Messages)> Measurements { get; } = [];
        public List<string> StoppedRuns { get; } = [];
        public Queue<Func<DeveloperCompletionRequest, Action<DeveloperStreamChunk>?, Action<string>?, CancellationToken,
            Task<DeveloperCompletionResult>>> Responses { get; } = [];

        public void Reply(string content) => Responses.Enqueue((request, _, _, _) => Task.FromResult(Completion(request, content)));

        public Task<DeveloperDulcheStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            if (++StatusCalls <= StatusFailures)
                return Task.FromException<DeveloperDulcheStatus>(
                    new HttpRequestException("Transient status failure.", null, HttpStatusCode.ServiceUnavailable));

            return Task.FromResult(new DeveloperDulcheStatus("ready", false,
                [
                    new("worker", "Worker", "loaded", null, 32_768, 1, null, null),
                    new("reviewer", "Reviewer", "loaded", null, ReviewerContext, 1, null, null),
                    new("unloaded-worker", "Unloaded worker", "unloaded", null, 32_768, 1, null, null),
                    new("unloaded-reviewer", "Unloaded reviewer", "unloaded", null, 32_768, 1, null, null)
                ],
                new(24_000, 64_000, 100L * 1024 * 1024 * 1024, null, []), 0));
        }

        public Task<int> CountTokensAsync(string modelId, IReadOnlyList<DeveloperChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            Measurements.Add((modelId, messages));
            if (Measurements.Count <= TokenFailures)
                return Task.FromException<int>(new IOException("Transient tokenizer failure."));
            return Task.FromResult(Measure(modelId, messages));
        }

        public Task<DeveloperCompletionResult> StreamCompletionAsync(DeveloperCompletionRequest request,
            Action<DeveloperStreamChunk>? onChunk = null, Action<string>? onRunStarted = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Responses.Dequeue()(request, onChunk, onRunStarted, cancellationToken);
        }

        public Task StopRunAsync(string dulcheRunId, CancellationToken cancellationToken = default)
        {
            StoppedRuns.Add(dulcheRunId);
            return Stop(dulcheRunId);
        }
    }

    private sealed class MissionFixture(DirectoryInfo root, DeveloperAutonomousMissionOptions options) : IDisposable
    {
        public DeveloperAutonomousMissionOptions Options { get; } = options;
        public DeveloperAutonomousMissionStore Store { get; } = new(options.StoreDirectory!);

        public static async Task<MissionFixture> CreateAsync()
        {
            var root = Directory.CreateTempSubdirectory("9to1-mission-reliability-");
            try
            {
                var repo = Path.Combine(root.FullName, "repo");
                var programme = Path.Combine(root.FullName, "programme");
                Directory.CreateDirectory(Path.Combine(repo, "src"));
                Directory.CreateDirectory(Path.Combine(repo, "tests"));
                Directory.CreateDirectory(programme);
                foreach (var path in new[] { "src/App.csproj", "src/Alternative.csproj", "tests/Acceptance.csproj", "tests/Alternative.csproj" })
                    File.WriteAllText(Path.Combine(repo, path), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
                File.WriteAllText(Path.Combine(repo, "tests", "Acceptance.cs"), "// baseline\n");
                File.WriteAllText(Path.Combine(programme, "build.md"), "## IX-BUILD-001 — Preserve verification evidence\n");
                await DeveloperAutonomousWorkspaceTools.RunGitAsync(repo, ["init", "-b", "main"]);
                await DeveloperAutonomousWorkspaceTools.RunGitAsync(repo, ["add", "-A"]);
                await DeveloperAutonomousWorkspaceTools.RunGitAsync(repo,
                    ["-c", "user.name=Mission Test", "-c", "user.email=test@local", "-c", "commit.gpgsign=false", "commit", "-m", "initial"]);

                return new MissionFixture(root, new DeveloperAutonomousMissionOptions
                {
                    RepositoryRoot = repo,
                    ProgrammeDirectory = programme,
                    StoreDirectory = Path.Combine(root.FullName, "store"),
                    SdkLockPath = Path.Combine(root.FullName, "test-sdk.lock"),
                    WorkerModelId = "worker",
                    SupervisorModelId = "reviewer",
                    GuardedIntegration = false,
                    MaxCompletionTokens = 128
                });
            }
            catch
            {
                root.Delete(true);
                throw;
            }
        }

        public async Task<DeveloperAutonomousMission> SeedVerifiedTaskAsync(ScriptedGateway gateway)
        {
            await using var engine = new DeveloperAutonomousMissionEngine(Options, gateway, Store);
            await engine.InitializeOrResumeAsync();
            await engine.RunSingleStepAsync();
            var mission = engine.CurrentMission!;
            var task = mission.ActiveTask!;
            const string buildProject = "src/App.csproj";
            const string testProject = "tests/Acceptance.csproj";
            const string filter = "Focused";
            var worktree = task.WorktreePath!;
            File.AppendAllText(Path.Combine(worktree, "tests", "Acceptance.cs"),
                "// " + new string('a', 17_000) + "\n// TAIL-SENTINEL\n");
            var (paths, diff, sha) = await DeveloperAutonomousWorkspaceTools.CaptureDiffAsync(worktree);
            var verification = new DeveloperBuildVerificationReport(true, true, 0, 0, 1, 0, 0,
                buildProject, testProject, filter, "", [], DateTimeOffset.UtcNow, sha, 0);
            var seeded = mission with
            {
                ActiveTask = task with
                {
                    BuildProject = buildProject,
                    TestProject = testProject,
                    TestFilter = filter,
                    ChangedPaths = paths,
                    DiffText = diff,
                    DiffSha256 = sha,
                    Verification = verification
                }
            };
            Store.SaveMission(seeded);
            return seeded;
        }

        public void Dispose() => root.Delete(true);
    }
}
