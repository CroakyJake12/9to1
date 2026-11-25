using System.Net.Sockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Dev.Autonomous;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed class BuildAutonomousWorkbenchTests
{
    [AvaloniaFact]
    public async Task Build_workbench_streams_live_reasoning_delegation_checkpoints_and_diff_from_Dulche_socket()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var temp = Directory.CreateTempSubdirectory("build-auto-ui-");
        var repoDir = Path.Combine(temp.FullName, "repo");
        Directory.CreateDirectory(repoDir);
        File.WriteAllText(Path.Combine(repoDir, "Program.cs"), "public static class Demo { public static int Value => 1; }\n");
        await DeveloperAutonomousWorkspaceTools.RunGitAsync(repoDir, ["init", "-b", "main"]);
        await DeveloperAutonomousWorkspaceTools.RunGitAsync(repoDir, ["add", "-A"]);
        await DeveloperAutonomousWorkspaceTools.RunGitAsync(repoDir,
            ["-c", "user.name=Test", "-c", "user.email=test@local", "-c", "commit.gpgsign=false", "commit", "-m", "init"]);

        var progDir = Path.Combine(temp.FullName, "programme");
        Directory.CreateDirectory(Path.Combine(progDir, "modules"));
        Directory.CreateDirectory(Path.Combine(progDir, "records"));
        File.WriteAllText(Path.Combine(progDir, "manifest.json"), """
        {
          "schema": "9to1-lossless-implementation-programme-v1",
          "excluded_pending_owner": [],
          "modules": {
            "Q21": { "owner": "flash", "title": "Demo Module", "priority": "current", "rank": 1, "count": 1, "file": "modules/Q21.json" }
          },
          "records": {
            "REQ-UI-1": { "ticket": "Q21", "kind": "requirement", "file": "records/REQ-UI-1.json" }
          }
        }
        """);
        File.WriteAllText(Path.Combine(progDir, "modules", "Q21.json"), """{"id":"Q21","title":"Demo Module","rows":["REQ-UI-1"]}""");
        File.WriteAllText(Path.Combine(progDir, "records", "REQ-UI-1.json"), """
        {
          "id": "REQ-UI-1",
          "kind": "requirement",
          "source_action": "Inspect Demo",
          "source_text": "Verify Demo.Value.",
          "original_register": { "requirement": "Verify Demo.Value", "acceptance": "Demo.Value stays valid." },
          "related_ids": []
        }
        """);

        var socketPath = Path.Combine(temp.FullName, "dulche.sock");
        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(socketPath));
        server.Listen(8);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        var serveTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                Socket peer;
                try { peer = await server.AcceptAsync(cts.Token); }
                catch (OperationCanceledException) { break; }
                using (peer)
                await using (var io = new NetworkStream(peer))
                using (var reader = new StreamReader(io, Encoding.ASCII, false, leaveOpen: true))
                {
                    var reqLine = await reader.ReadLineAsync(cts.Token);
                    if (reqLine is null) continue;
                    var contentLength = 0;
                    while (await reader.ReadLineAsync(cts.Token) is { Length: > 0 } h)
                    {
                        if (h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            contentLength = int.Parse(h[15..].Trim());
                    }
                    if (contentLength > 0)
                    {
                        var buf = new char[contentLength];
                        await reader.ReadBlockAsync(buf, cts.Token);
                    }

                    if (reqLine.Contains("/v1/chat/completions"))
                    {
                        var sse =
                            "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\nX-Dulche-Run-Id: ui-run-42\r\n\r\n" +
                            "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"Analyzing Demo.cs via Qwen3.8.\"}}]}\n\n" +
                            "data: {\"choices\":[{\"delta\":{\"content\":\"Supervisor guidance: Demo.Value is clean.\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":30,\"completion_tokens\":12},\"timings\":{\"predicted_per_second\":132.4}}\n\n" +
                            "data: [DONE]\n\n";
                        await io.WriteAsync(Encoding.UTF8.GetBytes(sse), cts.Token);
                        continue;
                    }

                    string json = reqLine.Contains("/v1/status")
                        ? """
                        {
                          "phase": "ready",
                          "stop_all": false,
                          "models": [
                            {"id":"qwen3.8-flash-next","name":"Qwen3.8 Flash Next","state":"loaded","gpu":"RTX 5080","context":524288,"slots":1},
                            {"id":"qwen3.8-27b","name":"Qwen3.8 27B","state":"loaded","gpu":"Tesla V100","context":131072,"slots":1}
                          ],
                          "resources": {
                            "host_available_mib": 24000,
                            "host_total_mib": 96000,
                            "disk_free_bytes": 160000000000,
                            "gpus": [{"name":"RTX 5080","used_mib":14000,"total_mib":16303,"free_mib":2303,"temperature_c":39,"utilization":8}]
                          },
                          "agents": []
                        }
                        """
                        : reqLine.Contains("/count-tokens")
                            ? """{"input_tokens":77}"""
                            : "{}";
                    var payload = Encoding.UTF8.GetBytes(json);
                    var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                    await io.WriteAsync(header, cts.Token);
                    await io.WriteAsync(payload, cts.Token);
                }
            }
        });

        DeveloperProjectWorkbenchPage? page = null;
        Window? window = null;
        var store = new FileDeveloperWorkspaceStore(Path.Combine(temp.FullName, "devstore"));
        var wsRoot = new DeveloperWorkspaceRoot(Guid.NewGuid(), repoDir);
        var project = new DeveloperProject(Guid.NewGuid(), "demo-repo", "DemoRepo", [wsRoot.RootId], "C#", null, "dotnet", ["linux"], [], [], [], null);
        var created = await store.CreateAsync(DeveloperWorkspace.Create([wsRoot]) with { Projects = [project] }, cts.Token);
        Assert.True(created.Succeeded, created.Error?.Message);
        var workspace = created.Value!;
        var reference = new DeveloperProjectReference(workspace.WorkspaceId, workspace.Revision, project.ProjectId, project.Revision, wsRoot.RootId);
        var resolved = new DeveloperResolvedProject(reference, workspace, project, wsRoot, null);

        var conversations = new InMemoryConversationRepository();
        var containers = new InMemoryContainerRepository();
        var canonical = new TaskExecutionCoordinator(new InMemoryTaskExecutionRepository(), new NullSink());
        var devService = new DeveloperTaskWorkspaceService(
            store,
            new DeveloperCanonicalWorkspaceBinding(conversations, containers),
            canonical,
            new StubToolActionOwner(),
            new WorkspaceToolRuntime(new NoopWorkspaceToolService()));

        try
        {
            var prefs = new UserPreferencesService(new TempPaths(temp.FullName));
            var client = new DulcheLocalClient(prefs, socketPath);
            var gateway = client.AsAutonomousGateway();
            var taskId = Guid.NewGuid();
            var execId = Guid.NewGuid();
            var ctxId = Guid.NewGuid();

            page = new DeveloperProjectWorkbenchPage(
                devService,
                canonical,
                resolved,
                taskId,
                execId,
                ctxId,
                new ReadyScene(),
                (_, _, _) => throw new NotSupportedException(),
                () => { },
                cts.Token,
                retainOriginalAcquisition: null,
                autonomousModelGateway: gateway);

            window = new Window { Width = 1200, Height = 850, Content = page };
            window.Show();
            await page.ActivateAsync(cts.Token);

            // 1) Refresh live Dulche models via Unix socket.
            await page.DispatchAsync("dev.mission-refresh-models", null, cts.Token);
            Assert.True(page.TryGetValue("MissionWorkerModel", out var workerVal));
            Assert.Equal("qwen3.8-flash-next", workerVal);
            Assert.True(page.TryGetValue("MissionSupervisorModel", out var supVal));
            Assert.Equal("qwen3.8-27b", supVal);
            Assert.True(page.TryGetValue("MissionModelsSummary", out var modelsSummary));
            Assert.Contains("Qwen3.8 Flash Next", modelsSummary?.ToString());

            // 2) Load specification programme manifest.
            Assert.True(page.TrySetValue("MissionProgrammePath", progDir));
            await page.DispatchAsync("dev.mission-load-programme", null, cts.Token);
            Assert.True(page.TryGetValue("MissionStatusSummary", out var statusAfterProg));
            Assert.Contains("1 approved requirements", statusAfterProg?.ToString());

            // 3) Delegate to supervisor model and verify live reasoning + output + delegation log!
            Assert.True(page.TrySetValue("MissionDelegatePrompt", "Review Demo.Value for invariants."));
            await page.DispatchAsync("dev.mission-delegate", null, cts.Token);

            Assert.True(page.TryGetValue("MissionLiveReasoning", out var reasoning));
            Assert.Contains("Analyzing Demo.cs via Qwen3.8.", reasoning?.ToString());
            Assert.True(page.TryGetValue("MissionLiveOutput", out var liveOutput));
            Assert.Contains("Supervisor guidance: Demo.Value is clean.", liveOutput?.ToString());
            Assert.True(page.TryGetValue("MissionDelegationLog", out var delLog));
            Assert.Contains("qwen3.8-27b", delLog?.ToString());

            // 4) Save durable checkpoint & compact context through CUI commands.
            await page.DispatchAsync("dev.mission-checkpoint", null, cts.Token);
            await page.DispatchAsync("dev.mission-compact", null, cts.Token);
            Assert.True(page.TryGetValue("MissionCheckpointsSummary", out var cps));
            Assert.Contains("manual_ui_checkpoint", cps?.ToString());
            Assert.True(page.TryGetValue("MissionContextSummary", out var ctxSummary));
            Assert.Contains("77", ctxSummary?.ToString());
        }
        finally
        {
            if (page is not null)
                await page.CloseAndDrainAsync();
            await devService.CloseAndDrainAsync();
            window?.Close();
            cts.Cancel();
            try { await serveTask; } catch { }
            try { temp.Delete(true); } catch { }
        }
    }

    private sealed class ReadyScene : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "Ready", "Ready"));
    }

    private sealed record TempPaths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }

    private sealed class StubToolActionOwner : ITaskRunToolActionOwner
    {
        public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string toolName) => false;
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission originalAttempt, TaskExecutionSnapshot currentSnapshot, Guid actionId, OllamaToolCall originalCall, ToolRuntimeKind runtime, PermissionMode permissionIntent, string? originalWorkspaceRoot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation originalPreparation, Func<CancellationToken, Task<WorkspaceToolResult>> originalBody, CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation originalPreparation, TaskExecutionSnapshot currentSnapshot, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation originalPreparation, TaskRunToolActionResult originalResult, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation originalPreparation, TaskExecutionSnapshot acknowledgedSnapshot, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class NoopWorkspaceToolService : IWorkspaceToolService
    {
        public string ResolveWorkspacePath(string workspaceRoot, string relativePath) => Path.Combine(workspaceRoot, relativePath);
        public Task<string> ReadTextAsync(string workspaceRoot, string relativePath, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task WriteTextAtomicAsync(string workspaceRoot, string relativePath, string content, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> SearchFilesAsync(string workspaceRoot, string searchPattern, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class InMemoryConversationRepository : IConversationRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ChatMessage>>([]);
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryContainerRepository : IContainerRepository
    {
        public Task<IReadOnlyList<ContainerDefinition>> GetByModeAsync(HavenMode mode, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ContainerDefinition>>([]);
        public Task UpsertAsync(ContainerDefinition item, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Lesson> CreateSubjectAsync(ContainerDefinition subject, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAndDetachConversationsAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<Lesson>> GetLessonsAsync(Guid subjectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Lesson>>([]);
        public Task UpsertLessonAsync(Lesson lesson, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteLessonAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class InMemoryTaskExecutionRepository : ITaskExecutionRepository
    {
        public Task UpsertAsync(TaskExecutionSnapshot proposed, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<TaskExecutionSnapshot?> GetAsync(Guid taskId, CancellationToken cancellationToken) => Task.FromResult<TaskExecutionSnapshot?>(null);
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid contextId, CancellationToken cancellationToken) => Task.FromResult<TaskExecutionSnapshot?>(null);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>([]);
    }

    private sealed class NullSink : IExecutionEventSink
    {
        public bool TryPublish(ExecutionEvent executionEvent) => true;
    }
}
