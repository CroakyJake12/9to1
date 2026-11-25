using System.Text;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev.Autonomous;

namespace Haven.Desktop.Views.Pages.Development;

public sealed partial class DeveloperProjectWorkbenchPage
{
    private sealed record MissionTaskItem(string SelectionKey, string Label, string AccessibleName, bool CanSelect)
        : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                nameof(SelectionKey) => SelectionKey,
                nameof(Label) => Label,
                nameof(AccessibleName) => AccessibleName,
                nameof(CanSelect) => CanSelect,
                _ => null
            };
            return value is not null;
        }
    }

    private sealed record MissionChangedFileItem(string SelectionKey, string Label, string AccessibleName, bool CanOpen)
        : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path switch
            {
                nameof(SelectionKey) => SelectionKey,
                nameof(Label) => Label,
                nameof(AccessibleName) => AccessibleName,
                nameof(CanOpen) => CanOpen,
                _ => null
            };
            return value is not null;
        }
    }

    private IDeveloperModelGateway? _injectedAutonomousGateway;
    private DulcheUnixSocketGateway? _ownedAutonomousGateway;
    private DeveloperAutonomousMissionEngine? _autonomousEngine;
    private DeveloperAutonomousMission? _autonomousMission;
    private DeveloperDulcheStatus? _autonomousDulcheStatus;
    private DeveloperProgrammeCatalog? _autonomousProgramme;
    private DeveloperLiveStreamSnapshot _autonomousStream =
        new(DeveloperAgentRole.Worker, "", null, "", "", null, false);
    private readonly List<DeveloperMissionEvent> _autonomousEvents = [];
    private readonly StringBuilder _autonomousTerminalLog = new();
    private string? _selectedMissionTaskId;
    private string _missionObjective = "Autonomously implement and verify approved 9to1 specification requirements.";
    private string _missionProgrammePath = "";
    private string _missionWorkerModel = "";
    private string _missionSupervisorModel = "";
    private string _missionDelegatePrompt = "";
    private bool _missionAutoAdvance = true;
    private bool _missionGuardedIntegration = true;
    private bool _missionEnableReasoning = true;
    private bool _missionBusy;
    private long _lastStreamPublishTick;

    private bool IsAutonomousMissionActive
    {
        get
        {
            lock (_gate)
                return _autonomousEngine is { IsRunning: true } || _missionBusy;
        }
    }

    internal DeveloperAutonomousMissionEngine? AutonomousEngine
    {
        get { lock (_gate) return _autonomousEngine; }
    }

    private bool TryGetAutonomousValue(string path, out object? value)
    {
        switch (path)
        {
            case "MissionStatusSummary":
                value = FormatMissionStatusSummary();
                return true;
            case "MissionModelsSummary":
                value = FormatMissionModelsSummary();
                return true;
            case "MissionContextSummary":
                value = FormatMissionContextSummary();
                return true;
            case "MissionObjective":
                value = _missionObjective;
                return true;
            case "MissionProgrammePath":
                value = _missionProgrammePath;
                return true;
            case "MissionWorkerModel":
                value = _missionWorkerModel;
                return true;
            case "MissionSupervisorModel":
                value = _missionSupervisorModel;
                return true;
            case "MissionAutoAdvance":
                value = _missionAutoAdvance;
                return true;
            case "MissionGuardedIntegration":
                value = _missionGuardedIntegration;
                return true;
            case "MissionEnableReasoning":
                value = _missionEnableReasoning;
                return true;
            case "MissionDelegatePrompt":
                value = _missionDelegatePrompt;
                return true;
            case "MissionActiveTaskSummary":
                value = FormatSelectedOrActiveTaskSummary();
                return true;
            case "MissionTasks":
                value = BuildMissionTaskItems();
                return true;
            case "MissionLiveReasoning":
                value = string.IsNullOrEmpty(_autonomousStream.ReasoningText)
                    ? "No live reasoning tokens received yet. Enable 'Stream reasoning' and start a mission to observe model reasoning."
                    : _autonomousStream.ReasoningText;
                return true;
            case "MissionLiveOutput":
                value = string.IsNullOrEmpty(_autonomousStream.ContentText)
                    ? "No live generation output yet."
                    : _autonomousStream.ContentText;
                return true;
            case "MissionChangedFiles":
                value = BuildMissionChangedFileItems();
                return true;
            case "MissionDiffReview":
                value = FormatSelectedOrActiveDiff();
                return true;
            case "MissionBuildOutput":
                value = _autonomousTerminalLog.Length == 0
                    ? "No guarded build or test verification output recorded yet."
                    : _autonomousTerminalLog.ToString();
                return true;
            case "MissionDelegationLog":
                value = FormatDelegationLog();
                return true;
            case "MissionCheckpointsSummary":
                value = FormatCheckpointsSummary();
                return true;
            case "MissionEventLog":
                value = _autonomousEvents.Count == 0
                    ? "No autonomous mission events recorded yet."
                    : string.Join('\n', _autonomousEvents.TakeLast(60)
                        .Select(e => $"[{e.Timestamp:HH:mm:ss}] #{e.Sequence} {e.Kind}: {e.Message}"));
                return true;
            case "CanRefreshMissionModels":
                value = IsAutonomousActionAvailable("dev.mission-refresh-models");
                return true;
            case "CanLoadMissionProgramme":
                value = IsAutonomousActionAvailable("dev.mission-load-programme");
                return true;
            case "CanStartMission":
                value = IsAutonomousActionAvailable("dev.mission-start");
                return true;
            case "CanStepMission":
                value = IsAutonomousActionAvailable("dev.mission-step");
                return true;
            case "CanPauseMission":
                value = IsAutonomousActionAvailable("dev.mission-pause");
                return true;
            case "CanResumeMission":
                value = IsAutonomousActionAvailable("dev.mission-resume");
                return true;
            case "CanStopMission":
                value = IsAutonomousActionAvailable("dev.mission-stop");
                return true;
            case "CanCheckpointMission":
                value = IsAutonomousActionAvailable("dev.mission-checkpoint");
                return true;
            case "CanCompactMission":
                value = IsAutonomousActionAvailable("dev.mission-compact");
                return true;
            case "CanDelegateMission":
                value = IsAutonomousActionAvailable("dev.mission-delegate");
                return true;
            case "CanApproveMissionIntegration":
                value = IsAutonomousActionAvailable("dev.mission-approve-integration");
                return true;
            case "CanRejectMissionIntegration":
                value = IsAutonomousActionAvailable("dev.mission-reject-integration");
                return true;
            default:
                value = null;
                return false;
        }
    }

    private bool TrySetAutonomousValue(string path, object? value)
    {
        if (path is "MissionAutoAdvance" or "MissionGuardedIntegration" or "MissionEnableReasoning")
        {
            if (value is not bool flag) return false;
            long gen; lock (_gate) gen = _generation;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(() => IsCurrent(gen));
                original.DemandPublication();
                lock (_gate)
                {
                    if (path == "MissionAutoAdvance") _missionAutoAdvance = flag;
                    else if (path == "MissionGuardedIntegration") _missionGuardedIntegration = flag;
                    else _missionEnableReasoning = flag;
                    _autonomousEngine?.UpdateConfiguration(
                        autoAdvance: _missionAutoAdvance,
                        guardedIntegration: _missionGuardedIntegration,
                        enableReasoning: _missionEnableReasoning);
                }
                NotifyOriginal(original);
            });
            return true;
        }

        if (path is not ("MissionObjective" or "MissionProgrammePath" or "MissionWorkerModel" or "MissionSupervisorModel" or "MissionDelegatePrompt"))
            return false;
        if (value is not string text) return false;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            original.DemandPublication();
            lock (_gate)
            {
                switch (path)
                {
                    case "MissionObjective": _missionObjective = text; break;
                    case "MissionProgrammePath": _missionProgrammePath = text; break;
                    case "MissionWorkerModel": _missionWorkerModel = text; break;
                    case "MissionSupervisorModel": _missionSupervisorModel = text; break;
                    case "MissionDelegatePrompt": _missionDelegatePrompt = text; break;
                }
            }
            NotifyOriginal(original);
        });
        return true;
    }

    private bool? IsAutonomousActionAvailable(string command)
    {
        var running = _autonomousEngine is { IsRunning: true };
        var hasMission = _autonomousMission is not null;
        var activeTask = _autonomousMission?.ActiveTask;
        return command switch
        {
            "dev.mission-refresh-models" => !_missionBusy,
            "dev.mission-load-programme" => !_missionBusy && !running,
            "dev.mission-start" => !_missionBusy && !running,
            "dev.mission-step" => !_missionBusy && !running,
            "dev.mission-pause" => running,
            "dev.mission-resume" => !_missionBusy && !running && hasMission && _autonomousMission!.State is DeveloperAutonomousMissionState.Paused or DeveloperAutonomousMissionState.Idle or DeveloperAutonomousMissionState.Failed,
            "dev.mission-stop" => running || _missionBusy,
            "dev.mission-checkpoint" => !_missionBusy && hasMission,
            "dev.mission-compact" => !_missionBusy && !running && hasMission,
            "dev.mission-select-task" => hasMission && _autonomousMission!.AllTasks.Count > 0,
            "dev.mission-open-changed-file" => SelectedOrActiveMissionTask()?.ChangedPaths.Count > 0,
            "dev.mission-delegate" => !_missionBusy && !running && !string.IsNullOrWhiteSpace(_missionDelegatePrompt),
            "dev.mission-approve-integration" => !_missionBusy && !running && activeTask is { Verification: { BuildSucceeded: true, TestsSucceeded: true } },
            "dev.mission-reject-integration" => !_missionBusy && !running && activeTask is not null,
            _ => null
        };
    }

    private bool IsAutonomousCommand(string command) =>
        command.StartsWith("dev.mission-", StringComparison.Ordinal);

    private ValueTask DispatchAutonomousAsync(string command, object? parameter, CancellationToken cancellationToken)
    {
        long generation;
        lock (_gate)
        {
            if (!_active || _work.IsRetiring)
                throw new InvalidOperationException("The Build workbench is inactive.");
            if (IsAutonomousActionAvailable(command) != true)
                throw new InvalidOperationException($"Autonomous action '{command}' is currently unavailable.");
            generation = _generation;
        }

        if (command == "dev.mission-pause")
        {
            _autonomousEngine?.Pause();
            ScheduleAutonomousUiRefresh(generation, "Pause requested at the next safe autonomous step boundary.");
            return ValueTask.CompletedTask;
        }

        if (command == "dev.mission-select-task")
        {
            if (parameter is not string key || string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Select a valid autonomous mission task.");
            lock (_gate) _selectedMissionTaskId = key;
            ScheduleAutonomousUiRefresh(generation, $"Selected autonomous task {key}.");
            return ValueTask.CompletedTask;
        }

        if (command == "dev.mission-open-changed-file")
        {
            if (parameter is not string relPath || string.IsNullOrWhiteSpace(relPath))
                throw new InvalidOperationException("Select a changed file to open.");
            lock (_gate) _sourcePath = relPath;
            if (IsActionAvailable("dev.open-source") == true)
                return DispatchAsync("dev.open-source", null, cancellationToken);
            ScheduleAutonomousUiRefresh(generation, $"Selected changed file {relPath} in source path.");
            return ValueTask.CompletedTask;
        }

        return new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            var setBusy = command is not ("dev.mission-start" or "dev.mission-resume" or "dev.mission-stop");
            if (setBusy) lock (_gate) _missionBusy = true;
            try
            {
                switch (command)
                {
                    case "dev.mission-refresh-models":
                    {
                        var gateway = ResolveAutonomousGateway();
                        var status = await original.AwaitAsync(gateway.GetStatusAsync(cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () =>
                        {
                            _autonomousDulcheStatus = status;
                            var loaded = status.Models.Where(m => m.IsLoaded).ToArray();
                            if (string.IsNullOrWhiteSpace(_missionWorkerModel) && loaded.Length > 0)
                            {
                                var worker = loaded.OrderByDescending(m => m.Id.Contains("flash", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                                    .ThenByDescending(m => m.ContextLength).First();
                                _missionWorkerModel = worker.Id;
                            }
                            if (string.IsNullOrWhiteSpace(_missionSupervisorModel) && loaded.Length > 0)
                            {
                                var sup = loaded.FirstOrDefault(m => m.Id != _missionWorkerModel) ?? loaded[0];
                                _missionSupervisorModel = sup.Id;
                            }
                            _status = $"Connected to Dulche: {loaded.Length} loaded model(s), {status.Resources.HostAvailableMiB} MiB RAM available.";
                        }).ConfigureAwait(false);
                        break;
                    }
                    case "dev.mission-load-programme":
                    {
                        var path = ResolveProgrammePath();
                        var catalog = await original.AwaitAsync(Task.Run(() => DeveloperProgrammeCatalog.Load(path), cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () =>
                        {
                            _autonomousProgramme = catalog;
                            _missionProgrammePath = catalog.RootDirectory;
                            _status = $"Loaded specification programme: {catalog.TotalApprovedRecords:N0} approved requirements across {catalog.Modules.Count} modules ({catalog.ExcludedProposalCount} excluded proposals guarded).";
                        }).ConfigureAwait(false);
                        break;
                    }
                    case "dev.mission-start":
                    case "dev.mission-resume":
                    {
                        var engine = await EnsureAutonomousEngineInitializedAsync(original, cancellationToken).ConfigureAwait(false);
                        var runTask = engine.StartAsync(_businessToken);
                        await PublishAsync(original, () => _status = "Autonomous development mission running via Dulche.").ConfigureAwait(false);
                        _ = runTask.ContinueWith(
                            t => ScheduleAutonomousUiRefresh(generation,
                                t.IsFaulted
                                    ? "Autonomous mission failed: " + t.Exception?.GetBaseException().Message
                                    : "Autonomous mission settled in state: " + (engine.CurrentMission?.State.ToString() ?? "unknown")),
                            TaskScheduler.Default);
                        break;
                    }
                    case "dev.mission-step":
                    {
                        var engine = await EnsureAutonomousEngineInitializedAsync(original, cancellationToken).ConfigureAwait(false);
                        await original.AwaitAsync(engine.RunSingleStepAsync(cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () => _status = "Completed one autonomous mission step.").ConfigureAwait(false);
                        break;
                    }
                    case "dev.mission-stop":
                    {
                        if (_autonomousEngine is { } engine)
                            await original.AwaitAsync(engine.StopActiveRunAsync(cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () => _status = "Stopped active autonomous run. Checkpoints and isolated worktree preserved.").ConfigureAwait(false);
                        break;
                    }
                    case "dev.mission-checkpoint":
                    {
                        var engine = _autonomousEngine ?? throw new InvalidOperationException("Start or initialize a mission first.");
                        var cp = await original.AwaitAsync(engine.SaveCheckpointAsync("manual_ui_checkpoint", null, cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () => _status = $"Saved durable checkpoint {cp.CheckpointId}.").ConfigureAwait(false);
                        break;
                    }
                    case "dev.mission-compact":
                    {
                        var engine = _autonomousEngine ?? throw new InvalidOperationException("Start or initialize a mission first.");
                        var ep = await original.AwaitAsync(engine.CompactContextAsync("manual_ui_compaction", cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () => _status = $"Context compacted into epoch {ep.EpochId} ({ep.ProjectedPromptTokens} prompt tokens).").ConfigureAwait(false);
                        break;
                    }
                    case "dev.mission-delegate":
                    {
                        var engine = await EnsureAutonomousEngineInitializedAsync(original, cancellationToken).ConfigureAwait(false);
                        string prompt; lock (_gate) prompt = _missionDelegatePrompt.Trim();
                        var del = await original.AwaitAsync(engine.DelegateToSupervisorAsync(prompt, cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () =>
                        {
                            _missionDelegatePrompt = "";
                            _status = $"Supervisor delegation {del.RunId} completed via {del.ModelId}.";
                        }).ConfigureAwait(false);
                        break;
                    }
                    case "dev.mission-approve-integration":
                    {
                        var engine = _autonomousEngine ?? throw new InvalidOperationException("No active mission.");
                        var receipt = await original.AwaitAsync(engine.ApproveAndIntegrateActiveTaskAsync(cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () => _status = $"Guarded integration committed {receipt.CommitRevision[..Math.Min(10, receipt.CommitRevision.Length)]} onto {receipt.BranchName}.").ConfigureAwait(false);
                        break;
                    }
                    case "dev.mission-reject-integration":
                    {
                        var engine = _autonomousEngine ?? throw new InvalidOperationException("No active mission.");
                        string reason;
                        lock (_gate) reason = string.IsNullOrWhiteSpace(_missionDelegatePrompt)
                            ? "Owner requested additional refinement and verification before integration."
                            : _missionDelegatePrompt.Trim();
                        await original.AwaitAsync(engine.RejectActiveTaskForReworkAsync(reason, cancellationToken)).ConfigureAwait(false);
                        await PublishAsync(original, () => _status = "Marked active task for rework with feedback.").ConfigureAwait(false);
                        break;
                    }
                }
            }
            catch (Exception error)
            {
                await PublishAsync(original, () => _status = "Autonomous Build error: " + error.Message).ConfigureAwait(false);
                original.Retain(error);
            }
            finally
            {
                if (setBusy) lock (_gate) _missionBusy = false;
                if (IsCurrent(generation))
                {
                    try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                    catch (Exception error) { original.Retain(error); }
                }
                original.ThrowRetained();
            }
        }));
    }

    private async Task<DeveloperAutonomousMissionEngine> EnsureAutonomousEngineInitializedAsync(
        DesktopOriginalWorkLifetime.Original original,
        CancellationToken cancellationToken)
    {
        if (_autonomousEngine is { CurrentMission: not null } existing)
        {
            existing.UpdateConfiguration(
                workerModelId: _missionWorkerModel,
                supervisorModelId: _missionSupervisorModel,
                programmeDirectory: string.IsNullOrWhiteSpace(_missionProgrammePath) ? null : _missionProgrammePath,
                autoAdvance: _missionAutoAdvance,
                guardedIntegration: _missionGuardedIntegration,
                enableReasoning: _missionEnableReasoning);
            return existing;
        }

        var gateway = ResolveAutonomousGateway();
        var repoRoot = _project.Root.Location;
        var programmeDir = ResolveProgrammePathOrNull();
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var defaultLock = Path.Combine(homeDir, ".local", "state", "9to1-sdk.lock");

        var options = new DeveloperAutonomousMissionOptions
        {
            RepositoryRoot = repoRoot,
            ProgrammeDirectory = programmeDir,
            MissionName = $"Build: {_project.Project.Name}",
            Objective = _missionObjective,
            WorkerModelId = string.IsNullOrWhiteSpace(_missionWorkerModel) ? null : _missionWorkerModel.Trim(),
            SupervisorModelId = string.IsNullOrWhiteSpace(_missionSupervisorModel) ? null : _missionSupervisorModel.Trim(),
            AutoAdvance = _missionAutoAdvance,
            GuardedIntegration = _missionGuardedIntegration,
            EnableReasoning = _missionEnableReasoning,
            MemoryFloorMiB = 12_000,
            DiskFloorGiB = 70,
            SdkLockPath = defaultLock
        };

        var engine = new DeveloperAutonomousMissionEngine(options, gateway);
        long gen; lock (_gate) gen = _generation;

        engine.MissionChanged += mission =>
        {
            lock (_gate)
            {
                _autonomousMission = mission;
                if (!string.IsNullOrWhiteSpace(mission.WorkerModelId)) _missionWorkerModel = mission.WorkerModelId;
                if (!string.IsNullOrWhiteSpace(mission.SupervisorModelId)) _missionSupervisorModel = mission.SupervisorModelId;
                if (mission.ActiveTask is not null) _selectedMissionTaskId = mission.ActiveTask.TaskId;
            }
            ScheduleAutonomousUiRefresh(gen);
        };
        engine.StreamUpdated += stream =>
        {
            lock (_gate) _autonomousStream = stream;
            var now = Environment.TickCount64;
            if (!stream.IsStreaming || now - Interlocked.Read(ref _lastStreamPublishTick) >= 75)
            {
                Interlocked.Exchange(ref _lastStreamPublishTick, now);
                ScheduleAutonomousUiRefresh(gen);
            }
        };
        engine.TerminalOutputAppended += line =>
        {
            lock (_gate)
            {
                if (_autonomousTerminalLog.Length > 48_000)
                    _autonomousTerminalLog.Remove(0, _autonomousTerminalLog.Length - 32_000);
                _autonomousTerminalLog.AppendLine(line);
            }
            ScheduleAutonomousUiRefresh(gen);
        };
        engine.EventRecorded += ev =>
        {
            lock (_gate)
            {
                _autonomousEvents.Add(ev);
                if (_autonomousEvents.Count > 120)
                    _autonomousEvents.RemoveRange(0, _autonomousEvents.Count - 100);
            }
            ScheduleAutonomousUiRefresh(gen);
        };

        var mission = await original.AwaitAsync(engine.InitializeOrResumeAsync(cancellationToken: cancellationToken)).ConfigureAwait(false);
        lock (_gate)
        {
            _autonomousEngine = engine;
            _autonomousMission = mission;
            _autonomousProgramme = engine.ProgrammeCatalog ?? _autonomousProgramme;
        }
        return engine;
    }

    private IDeveloperModelGateway ResolveAutonomousGateway()
    {
        lock (_gate)
        {
            if (_injectedAutonomousGateway is not null) return _injectedAutonomousGateway;
            _ownedAutonomousGateway ??= new DulcheUnixSocketGateway();
            return _ownedAutonomousGateway;
        }
    }

    private string ResolveProgrammePath()
    {
        var resolved = ResolveProgrammePathOrNull();
        if (resolved is null)
            throw new DirectoryNotFoundException("Enter a programme manifest directory or open a repository containing docs/specification/.");
        return resolved;
    }

    private string? ResolveProgrammePathOrNull()
    {
        if (!string.IsNullOrWhiteSpace(_missionProgrammePath))
            return _missionProgrammePath.Trim();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var activeProgramme = Path.Combine(home, "9to1-swarm-harness-20261124", "owner-queue-20261125", "implementation-programme-v1");
        if (File.Exists(Path.Combine(activeProgramme, "manifest.json")))
            return activeProgramme;
        var repoSpec = Path.Combine(_project.Root.Location, "docs", "specification");
        if (Directory.Exists(repoSpec))
            return repoSpec;
        return null;
    }

    private void ScheduleAutonomousUiRefresh(long generation, string? statusOverride = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsCurrent(generation)) return;
            lock (_gate)
            {
                if (statusOverride is not null)
                    _status = statusOverride;
            }
            PropertyChanged?.Invoke(this, new(null));
        });
    }

    private DeveloperMissionTask? SelectedOrActiveMissionTask()
    {
        var mission = _autonomousMission;
        if (mission is null) return null;
        if (!string.IsNullOrWhiteSpace(_selectedMissionTaskId))
        {
            var found = mission.AllTasks.FirstOrDefault(t => t.TaskId == _selectedMissionTaskId);
            if (found is not null) return found;
        }
        return mission.ActiveTask ?? mission.CompletedTasks.LastOrDefault() ?? mission.BlockedTasks.LastOrDefault();
    }

    private MissionTaskItem[] BuildMissionTaskItems()
    {
        if (_autonomousMission is null) return [];
        return _autonomousMission.AllTasks.Select(t =>
        {
            var prefix = t.TaskId == _autonomousMission.ActiveTask?.TaskId ? "▶ " : "";
            var label = $"{prefix}{t.TaskId} [{t.State}] {string.Join(", ", t.RecordIds)} — {t.Title}";
            return new MissionTaskItem(t.TaskId, label, $"Inspect autonomous task {t.TaskId}", true);
        }).ToArray();
    }

    private MissionChangedFileItem[] BuildMissionChangedFileItems()
    {
        var task = SelectedOrActiveMissionTask();
        if (task is null || task.ChangedPaths.Count == 0) return [];
        return task.ChangedPaths.Select(p =>
            new MissionChangedFileItem(p, p, $"Open changed file {p} in Build editor", !_pending)).ToArray();
    }

    private string FormatMissionStatusSummary()
    {
        if (_autonomousMission is not { } m)
        {
            var progInfo = _autonomousProgramme is null
                ? "No mission started yet. Refresh Dulche models or start an autonomous mission."
                : $"Programme ready: {_autonomousProgramme.TotalApprovedRecords:N0} approved requirements across {_autonomousProgramme.Modules.Count} modules. Ready to start autonomous mission.";
            return progInfo;
        }
        var rev = m.CurrentRevision.Length > 10 ? m.CurrentRevision[..10] : m.CurrentRevision;
        var streamBadge = _autonomousStream.IsStreaming
            ? $" · STREAMING ({_autonomousStream.Role}" + (_autonomousStream.TokensPerSecond is > 0 ? $" {_autonomousStream.TokensPerSecond:0.0} t/s" : "") + ")"
            : "";
        return $"Mission {m.MissionId} · State: {m.State}{streamBadge} · Branch: {m.IntegrationBranch} @ {rev} · Completed: {m.CompletedTasks.Count} · Blocked: {m.BlockedTasks.Count}"
            + (string.IsNullOrWhiteSpace(m.LastError) ? "" : $"\nLast error: {m.LastError}");
    }

    private string FormatMissionModelsSummary()
    {
        if (_autonomousDulcheStatus is null)
            return $"Worker: {(string.IsNullOrWhiteSpace(_missionWorkerModel) ? "(auto from Dulche)" : _missionWorkerModel)} · Supervisor: {(string.IsNullOrWhiteSpace(_missionSupervisorModel) ? "(auto opposite model)" : _missionSupervisorModel)}";
        var loaded = _autonomousDulcheStatus.Models.Where(m => m.IsLoaded)
            .Select(m => $"{m.Name} ({m.Gpu ?? "local"}, {m.ContextLength / 1024}K ctx)")
            .ToArray();
        var gpus = string.Join(" | ", _autonomousDulcheStatus.Resources.Gpus.Select(g =>
            $"{g.Name}: {g.UsedMiB}/{g.TotalMiB} MiB" + (g.TemperatureC is { } t ? $" {t:0}°C" : "")));
        return $"Dulche ({_autonomousDulcheStatus.Phase}): "
            + (loaded.Length == 0 ? "No loaded models" : string.Join("; ", loaded))
            + $"\nRAM free: {_autonomousDulcheStatus.Resources.HostAvailableMiB} MiB"
            + (string.IsNullOrEmpty(gpus) ? "" : $" · {gpus}");
    }

    private string FormatMissionContextSummary()
    {
        if (_autonomousMission is not { } m)
            return "Context budget: auto-detected from active Dulche model. Durable checkpoints persist automatically.";
        var pct = m.ContextBudgetTokens > 0 ? (double)m.LastPromptTokens * 100.0 / m.ContextBudgetTokens : 0;
        return $"Context: {m.LastPromptTokens:N0} / {m.ContextBudgetTokens:N0} tokens ({pct:0.0}%) · Compaction threshold: {m.CompactThresholdTokens:N0} · Active epoch: {m.ActiveEpochId ?? "initial"} · Transcript: {m.TranscriptCount} messages · Checkpoints: {m.Checkpoints.Count}";
    }

    private string FormatSelectedOrActiveTaskSummary()
    {
        var task = SelectedOrActiveMissionTask();
        if (task is null) return "No autonomous specification task admitted yet.";
        var sb = new StringBuilder();
        sb.AppendLine($"{task.TaskId} [{task.State}] · Module {task.ModuleId} · Records: {string.Join(", ", task.RecordIds)} · Turns: {task.TurnCount}");
        sb.AppendLine(task.Title);
        sb.AppendLine("Acceptance: " + task.AcceptanceCriteria);
        if (task.Verification is { } v)
            sb.AppendLine($"Verification: build={(v.BuildSucceeded ? "PASS" : "FAIL")} tests={(v.TestsSucceeded ? "PASS" : "FAIL")} ({v.TestsPassed} passed, {v.TestsFailed} failed, {v.Diagnostics.Count} diagnostics)");
        if (task.Review is { } r)
            sb.AppendLine($"Supervisor review ({r.ReviewerModelId}): {(r.Approved ? "APPROVED" : "REWORK")} — {r.Reason}");
        if (task.Integration is { } i)
            sb.AppendLine($"Integrated: {i.BranchName} @ {i.CommitRevision[..Math.Min(10, i.CommitRevision.Length)]} ({i.ChangedPaths.Count} files)");
        if (!string.IsNullOrWhiteSpace(task.BlockerReason))
            sb.AppendLine($"Blocker: {task.BlockerReason}");
        return sb.ToString().TrimEnd();
    }

    private string FormatSelectedOrActiveDiff()
    {
        var task = SelectedOrActiveMissionTask();
        if (task is null || string.IsNullOrEmpty(task.DiffText))
            return "No diff staged in isolated task worktree yet.";
        return $"Diff SHA-256: {task.DiffSha256}\nChanged ({task.ChangedPaths.Count}): {string.Join(", ", task.ChangedPaths)}\n\n{task.DiffText}";
    }

    private string FormatDelegationLog()
    {
        if (_autonomousMission is null || _autonomousMission.DelegatedRuns.Count == 0)
            return "No supervisor reviews or delegated subagent runs recorded yet.";
        return string.Join("\n\n", _autonomousMission.DelegatedRuns.TakeLast(10).Select(r =>
            $"[{r.Role}] {r.RunId} · {r.ModelId} · {r.Status} · {r.Objective}\n{r.ResultSummary ?? r.LiveContent}"));
    }

    private string FormatCheckpointsSummary()
    {
        if (_autonomousMission is null || _autonomousMission.Checkpoints.Count == 0)
            return "No durable checkpoints saved yet.";
        return string.Join('\n', _autonomousMission.Checkpoints.TakeLast(20).Select(c =>
            $"[{c.CreatedAt:HH:mm:ss}] {c.CheckpointId} · {c.Reason} · rev={c.CurrentRevision[..Math.Min(10, c.CurrentRevision.Length)]} · files={c.ChangedPaths.Count} · msgs={c.HistoryMessageCount} · tokens={c.LastPromptTokens} · {c.Summary}"));
    }

    private async Task CloseAutonomousEngineAsync()
    {
        DeveloperAutonomousMissionEngine? engine;
        DulcheUnixSocketGateway? owned;
        lock (_gate)
        {
            engine = _autonomousEngine;
            _autonomousEngine = null;
            owned = _ownedAutonomousGateway;
            _ownedAutonomousGateway = null;
        }
        if (engine is not null)
            await engine.DisposeAsync().ConfigureAwait(false);
        owned?.Dispose();
    }
}
