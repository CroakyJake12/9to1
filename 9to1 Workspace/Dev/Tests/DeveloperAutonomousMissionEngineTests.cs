using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HavenOS.Apps.Dev.Autonomous;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperAutonomousMissionEngineTests
{
    [Fact]
    public void Programme_catalog_loads_manifest_and_strictly_excludes_unapproved_proposals()
    {
        var temp = Directory.CreateTempSubdirectory("9to1-prog-cat-");
        try
        {
            WriteSampleProgramme(temp.FullName);
            var catalog = DeveloperProgrammeCatalog.Load(temp.FullName);

            Assert.True(catalog.IsStructuredManifest);
            Assert.Equal(2, catalog.TotalApprovedRecords);
            Assert.Equal(1, catalog.ExcludedProposalCount);
            Assert.True(catalog.IsExcludedProposal("P1"));

            var records = catalog.GetModuleRecords("Q21");
            Assert.Equal(["REQ-001", "REQ-002"], records.Select(r => r.RecordId).ToArray());

            var first = catalog.SelectNextUncoveredRequirement(new HashSet<string>(), new HashSet<string>());
            Assert.NotNull(first);
            Assert.Equal("REQ-001", first!.Value.Record.RecordId);

            var second = catalog.SelectNextUncoveredRequirement(new HashSet<string> { "REQ-001" }, new HashSet<string>());
            Assert.NotNull(second);
            Assert.Equal("REQ-002", second!.Value.Record.RecordId);

            var none = catalog.SelectNextUncoveredRequirement(new HashSet<string> { "REQ-001", "REQ-002" }, new HashSet<string>());
            Assert.Null(none);
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void Programme_catalog_loads_live_implementation_programme_v1_when_present()
    {
        var livePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "9to1-swarm-harness-20261124", "owner-queue-20261125", "implementation-programme-v1");
        if (!File.Exists(Path.Combine(livePath, "manifest.json")))
            return;

        var catalog = DeveloperProgrammeCatalog.Load(livePath);
        Assert.True(catalog.IsStructuredManifest);
        Assert.Equal(6845, catalog.TotalApprovedRecords);
        Assert.Equal(7, catalog.ExcludedProposalCount);
        foreach (var p in new[] { "review:P1", "review:P2", "review:P3", "review:P4", "review:P5", "review:P6", "review:P7" })
            Assert.True(catalog.IsExcludedProposal(p));

        var next = catalog.SelectNextUncoveredRequirement(new HashSet<string>(), new HashSet<string>());
        Assert.NotNull(next);
        Assert.False(next!.Value.Record.IsExcludedProposal);
    }

    [Fact]
    public async Task Dulche_gateway_streams_live_content_and_reasoning_and_enforces_hardware_safeguards()
    {
        var handler = new StubHttpHandler();
        using var gateway = new DulcheUnixSocketGateway(handler);

        var status = await gateway.GetStatusAsync();
        Assert.Equal("ready", status.Phase);
        Assert.Equal(2, status.Models.Count);
        Assert.Equal(524288, status.Models[0].ContextLength);
        Assert.Equal(131072, status.Models[1].ContextLength);

        // Hardware safeguard passes when above floor and fails closed below floor.
        DulcheUnixSocketGateway.DemandHardwareSafeguards(status, memoryFloorMiB: 1100, diskFloorGiB: 10);
        Assert.Throws<InvalidOperationException>(() =>
            DulcheUnixSocketGateway.DemandHardwareSafeguards(status, memoryFloorMiB: 999_999, diskFloorGiB: 10));

        var tokens = await gateway.CountTokensAsync("qwen-flash", [new("user", "hello")]);
        Assert.Equal(42, tokens);

        var reasoningChunks = new List<string>();
        var contentChunks = new List<string>();
        string? observedRun = null;

        var completion = await gateway.StreamCompletionAsync(
            new DeveloperCompletionRequest("qwen-flash", [new("user", "test")], EnableReasoning: true),
            onChunk: chunk =>
            {
                if (!string.IsNullOrEmpty(chunk.ReasoningDelta)) reasoningChunks.Add(chunk.ReasoningDelta);
                if (!string.IsNullOrEmpty(chunk.ContentDelta)) contentChunks.Add(chunk.ContentDelta);
            },
            onRunStarted: id => observedRun = id);

        Assert.Equal("run-sse-1", observedRun);
        Assert.Equal("think step 1 -> step 2", completion.ReasoningContent);
        Assert.Equal("{\"op\":\"git_diff\"}", completion.Content);
        Assert.Equal(2, reasoningChunks.Count);
        Assert.Equal(2, contentChunks.Count);
        Assert.Equal(148.5, completion.TokensPerSecond);
    }

    [Fact]
    public void Store_persists_hash_chained_transcript_and_detects_tampering()
    {
        var temp = Directory.CreateTempSubdirectory("9to1-store-");
        try
        {
            var store = new DeveloperAutonomousMissionStore(temp.FullName);
            var m0 = store.AppendTranscriptMessage("m-test-1", "user", "hello", null, "init");
            var m1 = store.AppendTranscriptMessage("m-test-1", "assistant", "{\"op\":\"list_files\"}", "checking files", "list_files", ".");
            Assert.Equal(0, m0.Index);
            Assert.Equal(1, m1.Index);
            Assert.True(store.VerifyTranscriptIntegrity("m-test-1", out var err));
            Assert.Null(err);

            // Tamper with transcript line 0 and verify hash-chain failure.
            var path = Path.Combine(store.GetMissionDirectory("m-test-1"), "transcript.jsonl");
            var text = File.ReadAllText(path).Replace("hello", "tampered", StringComparison.Ordinal);
            File.WriteAllText(path, text);
            Assert.False(store.VerifyTranscriptIntegrity("m-test-1", out var tamperErr));
            Assert.NotNull(tamperErr);
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public void Workspace_tools_block_path_traversal_and_prohibited_suppressions()
    {
        var temp = Directory.CreateTempSubdirectory("9to1-ws-sec-");
        try
        {
            var file = Path.Combine(temp.FullName, "Sample.cs");
            File.WriteAllText(file, "public class Sample { public int X() => 1; }\n");

            Assert.Throws<UnauthorizedAccessException>(() =>
                DeveloperAutonomousWorkspaceTools.NormalizeAndValidateRelativePath(temp.FullName, "../outside.cs"));
            Assert.Throws<UnauthorizedAccessException>(() =>
                DeveloperAutonomousWorkspaceTools.NormalizeAndValidateRelativePath(temp.FullName, "/etc/passwd"));
            Assert.Throws<UnauthorizedAccessException>(() =>
                DeveloperAutonomousWorkspaceTools.NormalizeAndValidateRelativePath(temp.FullName, ".git/config"));
            Assert.Throws<UnauthorizedAccessException>(() =>
                DeveloperAutonomousWorkspaceTools.EditFileExact(temp.FullName, "Sample.cs", "public int X() => 1;",
                    "#pragma warning disable CS0168\npublic int X() => 1;"));
        }
        finally { temp.Delete(true); }
    }

    [Fact]
    public async Task Autonomous_mission_progresses_across_two_requirements_with_diagnostics_delegation_review_compaction_and_guarded_integration()
    {
        var root = Directory.CreateTempSubdirectory("9to1-e2e-auto-");
        try
        {
            var repoDir = Path.Combine(root.FullName, "repo");
            var progDir = Path.Combine(root.FullName, "programme");
            var storeDir = Path.Combine(root.FullName, "store");
            await CreateSampleGitRepoAsync(repoDir);
            WriteSampleProgramme(progDir);

            var mainBefore = await DeveloperAutonomousWorkspaceTools.ResolveHeadRevisionAsync(repoDir);
            var gateway = new ScriptedDualModelGateway();

            var options = new DeveloperAutonomousMissionOptions
            {
                RepositoryRoot = repoDir,
                ProgrammeDirectory = progDir,
                StoreDirectory = storeDir,
                MissionName = "E2E Independence Proof",
                AutoAdvance = true,
                GuardedIntegration = true,
                RequireSupervisorReview = true,
                RequireTestFileChange = true,
                EnableReasoning = true,
                MemoryFloorMiB = 256,
                DiskFloorGiB = 1,
                CommandTimeoutSeconds = 180
            };

            var observedReasoning = new List<string>();
            var observedTerminal = new List<string>();
            await using var engine = new DeveloperAutonomousMissionEngine(options, gateway);
            engine.StreamUpdated += snap =>
            {
                if (!string.IsNullOrEmpty(snap.ReasoningText))
                    observedReasoning.Add(snap.ReasoningText);
            };
            engine.TerminalOutputAppended += line => observedTerminal.Add(line);

            var initial = await engine.InitializeOrResumeAsync();
            Assert.Equal("qwen3.8-flash-next", initial.WorkerModelId);
            Assert.Equal("qwen3.8-27b", initial.SupervisorModelId);

            await engine.StartAsync();

            var final = engine.CurrentMission!;
            var allTranscript = new DeveloperAutonomousMissionStore(storeDir).ReadTranscript(final.MissionId).Select(m => $"[{m.Index}:{m.Role}:{m.Operation}] {m.Content[..Math.Min(160, m.Content.Length)].Replace('\n', ' ')}");
            var diagMsg = $"State={final.State}, Completed={final.CompletedTasks.Count}, Blocked={final.BlockedTasks.Count} [{string.Join("; ", final.BlockedTasks.Select(b => b.TaskId + ":" + b.BlockerReason))}], LastError={final.LastError}\nTranscript:\n{string.Join("\n", allTranscript)}";
            Assert.True(final.State == DeveloperAutonomousMissionState.Completed, diagMsg);
            Assert.Null(final.ActiveTask);
            Assert.True(final.CompletedTasks.Count == 2, diagMsg);
            Assert.Equal(["REQ-001", "REQ-002"], final.CompletedRecordIds.ToArray());
            Assert.DoesNotContain("P1", final.CompletedRecordIds);
            Assert.Empty(final.BlockedTasks);

            // Verify live reasoning & terminal output were streamed.
            Assert.NotEmpty(observedReasoning);
            Assert.Contains(observedTerminal, line => line.Contains("Guarded verification", StringComparison.Ordinal));

            // Verify delegation + supervisor rework + approval receipts were recorded.
            Assert.Contains(final.DelegatedRuns, r => r.Role == DeveloperAgentRole.DelegatedSubagent && r.ModelId == "qwen3.8-27b");
            Assert.Contains(final.DelegatedRuns, r => r.Role == DeveloperAgentRole.Supervisor && r.Status == "rejected");
            Assert.Contains(final.DelegatedRuns, r => r.Role == DeveloperAgentRole.Supervisor && r.Status == "approved");

            // Verify durable checkpoints and context compaction epoch were created.
            Assert.NotEmpty(final.Checkpoints);
            Assert.NotNull(final.ActiveEpochId);
            Assert.NotEmpty(final.Notes);

            // Verify main branch was NEVER mutated while mission branch advanced by 2 verified commits!
            var mainAfter = await DeveloperAutonomousWorkspaceTools.ResolveHeadRevisionAsync(repoDir);
            Assert.Equal(mainBefore, mainAfter);

            var branchLog = await DeveloperAutonomousWorkspaceTools.RunGitAsync(
                repoDir, ["rev-list", "--count", $"{mainBefore}..refs/heads/{final.IntegrationBranch}"]);
            Assert.Equal("2", branchLog.Trim());

            // Verify restart recovery from durable store preserves full state and valid hash chain.
            var store = new DeveloperAutonomousMissionStore(storeDir);
            Assert.True(store.VerifyTranscriptIntegrity(final.MissionId, out var chainErr));
            Assert.Null(chainErr);

            await using var reloadedEngine = new DeveloperAutonomousMissionEngine(options, gateway, store);
            var resumed = await reloadedEngine.InitializeOrResumeAsync(final.MissionId);
            Assert.Equal(final.MissionId, resumed.MissionId);
            Assert.Equal(final.CurrentRevision, resumed.CurrentRevision);
            Assert.Equal(2, resumed.CompletedTasks.Count);
        }
        finally
        {
            try { root.Delete(true); } catch { }
        }
    }

    [Fact]
    public async Task Live_Qwen_develops_two_requirements_autonomously_through_Dulche()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NINETOONE_LIVE_QWEN_TEST"), "1", StringComparison.Ordinal))
            return;

        var root = Directory.CreateTempSubdirectory("9to1-live-qwen-e2e-");
        try
        {
            var repoDir = Path.Combine(root.FullName, "repo");
            var progDir = Path.Combine(root.FullName, "programme");
            var storeDir = Path.Combine(root.FullName, "store");
            await CreateSampleGitRepoAsync(repoDir);
            WriteSampleProgramme(progDir);

            var mainBefore = await DeveloperAutonomousWorkspaceTools.ResolveHeadRevisionAsync(repoDir);
            using var gateway = new DulcheUnixSocketGateway();
            var liveStatus = await gateway.GetStatusAsync();
            Assert.Equal("ready", liveStatus.Phase);
            Assert.True(liveStatus.Models.Any(m => m.IsLoaded), "Expected at least one loaded local model in Dulche.");

            var options = new DeveloperAutonomousMissionOptions
            {
                RepositoryRoot = repoDir,
                ProgrammeDirectory = progDir,
                StoreDirectory = storeDir,
                MissionName = "Live Qwen Independence Proof",
                AutoAdvance = true,
                GuardedIntegration = true,
                RequireSupervisorReview = true,
                RequireTestFileChange = true,
                EnableReasoning = true,
                MaxTurnsPerTask = 16,
                MaxIdleTurns = 5,
                MemoryFloorMiB = 1100,
                DiskFloorGiB = 10,
                CommandTimeoutSeconds = 180
            };

            var observedReasoning = 0;
            var observedContent = 0;
            var eventLog = new List<string>();
            await using var engine = new DeveloperAutonomousMissionEngine(options, gateway);
            engine.StreamUpdated += snap =>
            {
                if (!string.IsNullOrEmpty(snap.ReasoningText)) observedReasoning++;
                if (!string.IsNullOrEmpty(snap.ContentText)) observedContent++;
            };
            engine.EventRecorded += ev => eventLog.Add($"[{ev.Sequence}] {ev.Kind}: {ev.Message}");

            var initial = await engine.InitializeOrResumeAsync();
            Assert.False(string.IsNullOrWhiteSpace(initial.WorkerModelId));
            Assert.False(string.IsNullOrWhiteSpace(initial.SupervisorModelId));

            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
            await engine.StartAsync(timeout.Token);

            var final = engine.CurrentMission!;
            var summary = $"State={final.State}, Worker={final.WorkerModelId}, Supervisor={final.SupervisorModelId}, " +
                $"Completed={final.CompletedTasks.Count} [{string.Join(",", final.CompletedRecordIds)}], " +
                $"Blocked={final.BlockedTasks.Count} [{string.Join("; ", final.BlockedTasks.Select(b => b.TaskId + ":" + b.BlockerReason))}], " +
                $"ReasoningEvents={observedReasoning}, ContentEvents={observedContent}, LastError={final.LastError}\n" +
                $"Events:\n{string.Join("\n", eventLog.TakeLast(25))}";

            Assert.True(final.State == DeveloperAutonomousMissionState.Completed, summary);
            Assert.True(final.CompletedTasks.Count == 2, summary);
            Assert.Equal(["REQ-001", "REQ-002"], final.CompletedRecordIds.ToArray());
            Assert.DoesNotContain("P1", final.CompletedRecordIds);
            Assert.True(observedContent > 0, summary);

            // Verify primary branch was never mutated and isolated mission branch has 2 real commits from Qwen!
            var mainAfter = await DeveloperAutonomousWorkspaceTools.ResolveHeadRevisionAsync(repoDir);
            Assert.Equal(mainBefore, mainAfter);
            var commitCount = (await DeveloperAutonomousWorkspaceTools.RunGitAsync(
                repoDir, ["rev-list", "--count", $"{mainBefore}..refs/heads/{final.IntegrationBranch}"])).Trim();
            Assert.Equal("2", commitCount);

            // Verify durable transcript integrity and checkpoints.
            var store = new DeveloperAutonomousMissionStore(storeDir);
            Assert.True(store.VerifyTranscriptIntegrity(final.MissionId, out var chainErr), chainErr ?? "");
            Assert.NotEmpty(final.Checkpoints);
        }
        finally
        {
            try { root.Delete(true); } catch { }
        }
    }

    private static async Task CreateSampleGitRepoAsync(string repoDir)
    {
        Directory.CreateDirectory(Path.Combine(repoDir, "src"));
        Directory.CreateDirectory(Path.Combine(repoDir, "tests"));

        File.WriteAllText(Path.Combine(repoDir, "src", "MiniCalc.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(repoDir, "src", "Calculator.cs"),
            """
            namespace MiniCalc;

            public static class Calculator
            {
                public static int Add(int a, int b) => a + b;
            }
            """);

        File.WriteAllText(Path.Combine(repoDir, "tests", "MiniCalc.Tests.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <IsPackable>false</IsPackable>
                <IsTestProject>true</IsTestProject>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="..\src\MiniCalc.csproj" />
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit" Version="2.9.3" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(repoDir, "tests", "CalculatorTests.cs"),
            """
            using MiniCalc;
            using Xunit;

            namespace MiniCalc.Tests;

            public sealed class CalculatorTests
            {
                [Fact]
                public void Add_sums_two_numbers() => Assert.Equal(5, Calculator.Add(2, 3));
            }
            """);

        await DeveloperAutonomousWorkspaceTools.RunGitAsync(repoDir, ["init", "-b", "main"]);
        await DeveloperAutonomousWorkspaceTools.RunGitAsync(repoDir, ["add", "-A"]);
        await DeveloperAutonomousWorkspaceTools.RunGitAsync(repoDir,
            ["-c", "user.name=Test", "-c", "user.email=test@local", "-c", "commit.gpgsign=false", "commit", "-m", "initial"]);
    }

    private static void WriteSampleProgramme(string dir)
    {
        Directory.CreateDirectory(Path.Combine(dir, "modules"));
        Directory.CreateDirectory(Path.Combine(dir, "records"));

        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            """
            {
              "schema": "9to1-lossless-implementation-programme-v1",
              "excluded_pending_owner": ["P1"],
              "modules": {
                "Q21": {
                  "owner": "flash",
                  "title": "MiniCalc Build Module",
                  "priority": "current",
                  "rank": 1,
                  "count": 2,
                  "file": "modules/Q21.json",
                  "source_candidates": ["src/Calculator.cs"]
                }
              },
              "records": {
                "REQ-001": { "ticket": "Q21", "kind": "requirement", "file": "records/REQ-001.json" },
                "REQ-002": { "ticket": "Q21", "kind": "requirement", "file": "records/REQ-002.json" },
                "P1": { "ticket": "Q21", "kind": "proposal", "file": "records/P1.json" }
              }
            }
            """);

        File.WriteAllText(Path.Combine(dir, "modules", "Q21.json"),
            """
            {
              "id": "Q21",
              "title": "MiniCalc Build Module",
              "rows": ["REQ-001", "REQ-002", "P1"]
            }
            """);

        File.WriteAllText(Path.Combine(dir, "records", "REQ-001.json"),
            """
            {
              "id": "REQ-001",
              "kind": "requirement",
              "source_action": "Implement Subtract",
              "source_text": "Calculator.Subtract(int a, int b) must return a - b.",
              "original_register": {
                "requirement": "Add Calculator.Subtract(int a, int b)",
                "acceptance": "Calculator.Subtract(10, 4) returns 6 with a focused xUnit test."
              },
              "related_ids": []
            }
            """);

        File.WriteAllText(Path.Combine(dir, "records", "REQ-002.json"),
            """
            {
              "id": "REQ-002",
              "kind": "requirement",
              "source_action": "Implement Multiply",
              "source_text": "Calculator.Multiply(int a, int b) must return a * b.",
              "original_register": {
                "requirement": "Add Calculator.Multiply(int a, int b)",
                "acceptance": "Calculator.Multiply(6, 7) returns 42 with a focused xUnit test."
              },
              "related_ids": ["REQ-001"]
            }
            """);

        File.WriteAllText(Path.Combine(dir, "records", "P1.json"),
            """
            {
              "id": "P1",
              "kind": "proposal",
              "source_action": "Unapproved proposal",
              "source_text": "Must never be selected.",
              "original_register": { "requirement": "Excluded P1" },
              "related_ids": []
            }
            """);
    }

    private sealed class StubHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/v1/status")
            {
                return Task.FromResult(Json("""
                {
                  "phase": "ready",
                  "stop_all": false,
                  "models": [
                    {"id":"qwen-flash","name":"Qwen3.8 Flash","state":"loaded","gpu":"RTX 5080","context":524288,"slots":1},
                    {"id":"qwen-27b","name":"Qwen3.8 27B","state":"loaded","gpu":"Tesla V100","context":131072,"slots":1}
                  ],
                  "resources": {
                    "host_available_mib": 24000,
                    "host_total_mib": 96000,
                    "disk_free_bytes": 150000000000,
                    "gpus": [{"name":"RTX 5080","used_mib":14000,"total_mib":16303,"free_mib":2303,"temperature_c":40,"utilization":12}]
                  },
                  "agents": []
                }
                """));
            }
            if (path.EndsWith("/count-tokens", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"input_tokens\":42}"));

            if (path == "/v1/chat/completions")
            {
                var sse =
                    "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"think step 1 \"}}]}\n\n" +
                    "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"-> step 2\"}}]}\n\n" +
                    "data: {\"choices\":[{\"delta\":{\"content\":\"{\\\"op\\\":\"}}]}\n\n" +
                    "data: {\"choices\":[{\"delta\":{\"content\":\"\\\"git_diff\\\"}\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":42,\"completion_tokens\":8},\"timings\":{\"predicted_per_second\":148.5}}\n\n" +
                    "data: [DONE]\n\n";
                var resp = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
                };
                resp.Headers.Add("X-Dulche-Run-Id", "run-sse-1");
                return Task.FromResult(resp);
            }
            return Task.FromResult(Json("{}"));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class ScriptedDualModelGateway : IDeveloperModelGateway
    {
        private int _workerStep;
        private int _supervisorReviews;

        public Task<DeveloperDulcheStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new DeveloperDulcheStatus(
                Phase: "ready",
                StopAll: false,
                Models:
                [
                    new("qwen3.8-flash-next", "Qwen3.8 Flash Next", "loaded", "RTX 5080", 524288, 1, "Thunder / Strata", "dulche"),
                    new("qwen3.8-27b", "Qwen3.8 27B", "loaded", "Tesla V100", 131072, 1, "Lightning / llama.cpp RPC", "dulche-v100-rpc")
                ],
                Resources: new DeveloperResourceSnapshot(28000, 96000, 180L * 1024 * 1024 * 1024, 2.0,
                    [new("RTX 5080", 15000, 16303, 1303, 39, 10)]),
                ActiveAgentsCount: 0));

        public Task<int> CountTokensAsync(string modelId, IReadOnlyList<DeveloperChatMessage> messages, CancellationToken cancellationToken = default) =>
            Task.FromResult(Math.Max(64, messages.Sum(m => m.Content.Length) / 4));

        public Task<DeveloperCompletionResult> StreamCompletionAsync(
            DeveloperCompletionRequest request,
            Action<DeveloperStreamChunk>? onChunk = null,
            Action<string>? onRunStarted = null,
            CancellationToken cancellationToken = default)
        {
            var runId = "sim-" + Guid.NewGuid().ToString("N")[..8];
            onRunStarted?.Invoke(runId);

            string reasoning;
            string content;

            if (request.ModelId == "qwen3.8-27b" && request.Messages[0].Content.Contains("independent supervisor reviewer", StringComparison.OrdinalIgnoreCase))
            {
                var body = JsonNode.Parse(request.Messages[^1].Content)!.AsObject();
                var digest = body["binding_digest"]!.ToString();
                _supervisorReviews++;
                if (_supervisorReviews == 1)
                {
                    reasoning = "Checking diff for edge-case negative input test coverage.";
                    content = JsonSerializer.Serialize(new
                    {
                        approve = false,
                        sha256 = digest,
                        reason = "Please also add an assertion for negative inputs in Subtract test."
                    });
                }
                else
                {
                    reasoning = "Verified diff hash, implementation accuracy, and passing focused xUnit tests.";
                    content = JsonSerializer.Serialize(new
                    {
                        approve = true,
                        sha256 = digest,
                        reason = "Implementation and tests satisfy acceptance criteria."
                    });
                }
            }
            else if (request.ModelId == "qwen3.8-27b")
            {
                reasoning = "Analyzing delegated request from worker.";
                content = "Add Calculator.Subtract returning a - b and verify both positive and negative operands in CalculatorTests.";
            }
            else
            {
                _workerStep++;
                (reasoning, content) = _workerStep switch
                {
                    // Task 1: read file, introduce compiler typo, run_tests (observe real CS error!), delegate, fix, add test, compact, recall, complete (rejected once), add negative test, complete (approved -> integrated!)
                    1 => ("Inspect Calculator.cs first.", "{\"op\":\"read_file\",\"path\":\"src/Calculator.cs\",\"memory\":{\"finding\":\"Calculator only has Add\",\"next\":\"Add Subtract\",\"references\":[\"src/Calculator.cs\"]}}"),
                    2 => ("Add Subtract with an intentional missing semicolon to test real MSBuild diagnostic recovery.",
                        JsonSerializer.Serialize(new
                        {
                            op = "edit_file",
                            path = "src/Calculator.cs",
                            old = "public static int Add(int a, int b) => a + b;",
                            @new = "public static int Add(int a, int b) => a + b;\n    public static int Subtract(int a, int b) => a - b"
                        })),
                    3 => ("Run build and tests to observe real diagnostics.", "{\"op\":\"run_tests\",\"project\":\"src/MiniCalc.csproj\",\"test_project\":\"tests/MiniCalc.Tests.csproj\"}"),
                    4 => ("Delegate edge-case check to supervisor while preparing fix.", "{\"op\":\"delegate\",\"objective\":\"Confirm edge cases for Calculator.Subtract.\"}"),
                    5 => ("Fix missing semicolon in Calculator.cs.",
                        JsonSerializer.Serialize(new
                        {
                            op = "edit_file",
                            path = "src/Calculator.cs",
                            old = "public static int Subtract(int a, int b) => a - b",
                            @new = "public static int Subtract(int a, int b) => a - b;"
                        })),
                    6 => ("Add Subtract xUnit test.",
                        JsonSerializer.Serialize(new
                        {
                            op = "edit_file",
                            path = "tests/CalculatorTests.cs",
                            old = "public void Add_sums_two_numbers() => Assert.Equal(5, Calculator.Add(2, 3));",
                            @new = "public void Add_sums_two_numbers() => Assert.Equal(5, Calculator.Add(2, 3));\n    [Fact]\n    public void Subtract_returns_difference() => Assert.Equal(6, Calculator.Subtract(10, 4));"
                        })),
                    7 => ("Compact working context cleanly before submitting.", "{\"op\":\"compact\",\"reason\":\"pre-submission checkpoint\"}"),
                    8 => ("Recall earlier note from archive to prove lossless history.", "{\"op\":\"recall\",\"query\":\"MISSION INITIALIZED\"}"),
                    9 => ("Submit REQ-001 for verification and supervisor review.", "{\"op\":\"complete_task\",\"summary\":\"Added Calculator.Subtract and focused test.\",\"build_project\":\"src/MiniCalc.csproj\",\"test_project\":\"tests/MiniCalc.Tests.csproj\"}"),
                    10 => ("Address supervisor rework feedback by asserting negative operand handling.",
                        JsonSerializer.Serialize(new
                        {
                            op = "edit_file",
                            path = "tests/CalculatorTests.cs",
                            old = "public void Subtract_returns_difference() => Assert.Equal(6, Calculator.Subtract(10, 4));",
                            @new = "public void Subtract_returns_difference() { Assert.Equal(6, Calculator.Subtract(10, 4)); Assert.Equal(-3, Calculator.Subtract(2, 5)); }"
                        })),
                    11 => ("Resubmit REQ-001 after adding negative operand assertion.", "{\"op\":\"complete_task\",\"summary\":\"Added Subtract and positive/negative operand tests.\",\"build_project\":\"src/MiniCalc.csproj\",\"test_project\":\"tests/MiniCalc.Tests.csproj\"}"),
                    // Task 2 (auto-selected without external direction!): read, edit source, edit test, complete!
                    12 => ("Autonomously inspecting Calculator.cs for REQ-002 (Multiply).", "{\"op\":\"read_file\",\"path\":\"src/Calculator.cs\"}"),
                    13 => ("Implementing Calculator.Multiply on top of REQ-001.",
                        JsonSerializer.Serialize(new
                        {
                            op = "edit_file",
                            path = "src/Calculator.cs",
                            old = "public static int Subtract(int a, int b) => a - b;",
                            @new = "public static int Subtract(int a, int b) => a - b;\n    public static int Multiply(int a, int b) => a * b;"
                        })),
                    14 => ("Adding focused Multiply xUnit test.",
                        JsonSerializer.Serialize(new
                        {
                            op = "edit_file",
                            path = "tests/CalculatorTests.cs",
                            old = "Assert.Equal(-3, Calculator.Subtract(2, 5)); }",
                            @new = "Assert.Equal(-3, Calculator.Subtract(2, 5)); }\n    [Fact]\n    public void Multiply_returns_product() => Assert.Equal(42, Calculator.Multiply(6, 7));"
                        })),
                    _ => ("Submitting REQ-002 for guarded verification, review, and integration.", "{\"op\":\"complete_task\",\"summary\":\"Implemented Calculator.Multiply with focused unit test.\",\"build_project\":\"src/MiniCalc.csproj\",\"test_project\":\"tests/MiniCalc.Tests.csproj\"}")
                };
            }

            onChunk?.Invoke(new DeveloperStreamChunk(null, reasoning, null, null, null, null, runId));
            onChunk?.Invoke(new DeveloperStreamChunk(content, null, "stop", 256, 48, 120.0, runId));
            return Task.FromResult(new DeveloperCompletionResult(runId, request.ModelId, content, reasoning, "stop", 256, 48, 120.0, TimeSpan.FromMilliseconds(25)));
        }

        public Task StopRunAsync(string dulcheRunId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
