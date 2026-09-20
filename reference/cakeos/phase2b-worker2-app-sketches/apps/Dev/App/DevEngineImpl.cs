using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HavenOS.Apps.Dev;

public sealed class DevEngine : IDevEngine
{
    private readonly string _vscodePath;
    private Process? _vscodeProcess;
    private NamedPipeClientStream? _pipeClient;
    private DevWorkspace? _currentWorkspace;
    private Action<DevEngineEvent>? _eventCallback;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public DevEngine(string vscodePath = "codium")
    {
        _vscodePath = vscodePath;
    }

    public DevWorkspace? CurrentWorkspace => _currentWorkspace;

    public async Task<DevWorkspace?> OpenWorkspaceAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        await SendRequestAsync("workspace.open", new { path }, cancellationToken);
        _currentWorkspace = new DevWorkspace(path, System.IO.Path.GetFileName(path), []);
        _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.WorkspaceOpened, path));
        return _currentWorkspace;
    }

    public async Task<DevWorkspace> CreateWorkspaceAsync(string path, string name, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(path);
        await EnsureVscodeRunningAsync(cancellationToken);
        await SendRequestAsync("workspace.create", new { path, name }, cancellationToken);
        _currentWorkspace = new DevWorkspace(path, name, []);
        _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.WorkspaceOpened, path));
        return _currentWorkspace;
    }

    public async Task CloseWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        if (_currentWorkspace != null)
        {
            await SendRequestAsync("workspace.close", new { }, cancellationToken);
            _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.WorkspaceClosed, _currentWorkspace.Path));
            _currentWorkspace = null;
        }
    }

    public async Task<IReadOnlyList<DevFile>> GetFilesAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<FileListResponse>("files.list", new { path = folderPath }, cancellationToken);
        return result?.Files ?? [];
    }

    public async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<FileContentResponse>("files.read", new { path }, cancellationToken);
        return result?.Content ?? "";
    }

    public async Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        await SendRequestAsync("files.write", new { path, content }, cancellationToken);
        _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.FileChanged, path));
    }

    public async Task<IReadOnlyList<DevExtension>> GetExtensionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<ExtensionListResponse>("extensions.list", new { }, cancellationToken);
        return result?.Extensions ?? [];
    }

    public async Task InstallExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        await SendRequestAsync("extensions.install", new { extensionId }, cancellationToken);
        _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.ExtensionInstalled, extensionId));
    }

    public async Task UninstallExtensionAsync(string extensionId, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        await SendRequestAsync("extensions.uninstall", new { extensionId }, cancellationToken);
        _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.ExtensionUninstalled, extensionId));
    }

    public async Task<IReadOnlyList<DevCommand>> GetCommandsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<CommandListResponse>("commands.list", new { }, cancellationToken);
        return result?.Commands ?? [];
    }

    public async Task ExecuteCommandAsync(string commandId, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        await SendRequestAsync("commands.execute", new { commandId }, cancellationToken);
    }

    public async Task<DevTerminal> CreateTerminalAsync(string name, string shell, string cwd, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<TerminalCreateResponse>("terminal.create", new { name, shell, cwd }, cancellationToken);
        if (result != null)
        {
            var terminal = new DevTerminal(result.Id, name, result.Pid, shell, cwd);
            _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.TerminalCreated, result.Id));
            return terminal;
        }
        throw new InvalidOperationException("Failed to create terminal");
    }

    public async Task<IReadOnlyList<DevTerminal>> GetTerminalsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<TerminalListResponse>("terminal.list", new { }, cancellationToken);
        return result?.Terminals ?? [];
    }

    public async Task<IReadOnlyList<DevDebugSession>> GetDebugSessionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<DebugSessionListResponse>("debug.sessions", new { }, cancellationToken);
        return result?.Sessions ?? [];
    }

    public async Task<DevDebugSession> StartDebugAsync(string config, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<DebugSessionStartResponse>("debug.start", new { config }, cancellationToken);
        if (result != null)
        {
            var session = new DevDebugSession(result.Id, result.Type, config, "running");
            _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.DebugStarted, result.Id));
            return session;
        }
        throw new InvalidOperationException("Failed to start debug session");
    }

    public async Task StopDebugAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        await SendRequestAsync("debug.stop", new { sessionId }, cancellationToken);
        _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.DebugStopped, sessionId));
    }

    public async Task<IReadOnlyList<DevTask>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        var result = await SendRequestAsync<TaskListResponse>("tasks.list", new { }, cancellationToken);
        return result?.Tasks ?? [];
    }

    public async Task RunTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await EnsureVscodeRunningAsync(cancellationToken);
        await SendRequestAsync("tasks.run", new { taskId }, cancellationToken);
        _eventCallback?.Invoke(new DevEngineEvent(DevEngineEventType.TaskStarted, taskId));
    }

    public void SetEventCallback(Action<DevEngineEvent> callback) => _eventCallback = callback;

    public ValueTask DisposeAsync()
    {
        _pipeClient?.Dispose();
        _vscodeProcess?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task EnsureVscodeRunningAsync(CancellationToken cancellationToken)
    {
        if (_pipeClient != null && _pipeClient.IsConnected)
            return;

        // Start VSCode/Codium with IPC enabled
        _vscodeProcess = Process.Start(new ProcessStartInfo
        {
            FileName = _vscodePath,
            Arguments = "--enable-proposed-api --extension-development-path=/tmp/haven-dev-ext --ipc",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        });

        if (_vscodeProcess == null)
            throw new InvalidOperationException($"Failed to start {_vscodePath}");

        // Connect to IPC pipe
        var pipeName = $"vscode-ipc-{Environment.UserName}-{_vscodeProcess.Id}";
        _pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipeClient.ConnectAsync(5000, cancellationToken);
    }

    private async Task<T?> SendRequestAsync<T>(string method, object @params, CancellationToken cancellationToken)
    {
        var request = new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method, @params };
        var json = JsonSerializer.Serialize(request);
        var data = Encoding.UTF8.GetBytes(json + "\n");
        
        await _pipeClient!.WriteAsync(data, cancellationToken);
        await _pipeClient.FlushAsync(cancellationToken);

        // Read response
        var buffer = new byte[8192];
        var responseBuilder = new StringBuilder();
        while (true)
        {
            int read = await _pipeClient.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (responseBuilder.ToString().Contains('\n')) break;
        }

        var responseJson = responseBuilder.ToString().Trim();
        var response = JsonSerializer.Deserialize<JsonRpcResponse<T>>(responseJson, _jsonOptions);
        
        if (response?.Error != null)
            throw new InvalidOperationException($"VSCode error: {response.Error.Message}");

        return response?.Result;
    }

    private async Task SendRequestAsync(string method, object @params, CancellationToken cancellationToken)
    {
        await SendRequestAsync<object>(method, @params, cancellationToken);
    }

    private sealed record JsonRpcResponse<T>(string Jsonrpc, string Id, T? Result, JsonRpcError? Error);
    private sealed record JsonRpcError(int Code, string Message);
    private sealed record FileListResponse(IReadOnlyList<DevFile> Files);
    private sealed record FileContentResponse(string Content);
    private sealed record ExtensionListResponse(IReadOnlyList<DevExtension> Extensions);
    private sealed record CommandListResponse(IReadOnlyList<DevCommand> Commands);
    private sealed record TerminalCreateResponse(string Id, int Pid);
    private sealed record TerminalListResponse(IReadOnlyList<DevTerminal> Terminals);
    private sealed record DebugSessionListResponse(IReadOnlyList<DevDebugSession> Sessions);
    private sealed record DebugSessionStartResponse(string Id, string Type);
    private sealed record TaskListResponse(IReadOnlyList<DevTask> Tasks);
}