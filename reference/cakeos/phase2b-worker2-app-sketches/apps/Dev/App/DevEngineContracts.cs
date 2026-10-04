namespace HavenOS.Apps.Dev;

public sealed record DevWorkspace(string Path, string Name, IReadOnlyList<DevFolder> Folders);
public sealed record DevFolder(string Path, string Name);
public sealed record DevFile(string Path, string Name, long Size, DateTimeOffset Modified, string LanguageId);
public sealed record DevExtension(string Id, string Name, string Version, string Publisher, bool Enabled, string Description);
public sealed record DevCommand(string Id, string Title, string Category);
public sealed record DevTerminal(string Id, string Name, int Pid, string Shell, string Cwd);
public sealed record DevDebugSession(string Id, string Type, string Config, string Status);
public sealed record DevTask(string Id, string Label, string Type, string Command, string Group);

public interface IDevEngine : IAsyncDisposable
{
    Task<DevWorkspace?> OpenWorkspaceAsync(string path, CancellationToken cancellationToken = default);
    Task<DevWorkspace> CreateWorkspaceAsync(string path, string name, CancellationToken cancellationToken = default);
    Task CloseWorkspaceAsync(CancellationToken cancellationToken = default);
    DevWorkspace? CurrentWorkspace { get; }
    Task<IReadOnlyList<DevFile>> GetFilesAsync(string folderPath, CancellationToken cancellationToken = default);
    Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default);
    Task WriteFileAsync(string path, string content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DevExtension>> GetExtensionsAsync(CancellationToken cancellationToken = default);
    Task InstallExtensionAsync(string extensionId, CancellationToken cancellationToken = default);
    Task UninstallExtensionAsync(string extensionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DevCommand>> GetCommandsAsync(CancellationToken cancellationToken = default);
    Task ExecuteCommandAsync(string commandId, CancellationToken cancellationToken = default);
    Task<DevTerminal> CreateTerminalAsync(string name, string shell, string cwd, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DevTerminal>> GetTerminalsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DevDebugSession>> GetDebugSessionsAsync(CancellationToken cancellationToken = default);
    Task<DevDebugSession> StartDebugAsync(string config, CancellationToken cancellationToken = default);
    Task StopDebugAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DevTask>> GetTasksAsync(CancellationToken cancellationToken = default);
    Task RunTaskAsync(string taskId, CancellationToken cancellationToken = default);
    void SetEventCallback(Action<DevEngineEvent> callback);
}

public enum DevEngineEventType
{
    WorkspaceOpened = 0,
    WorkspaceClosed = 1,
    FileChanged = 2,
    FileCreated = 3,
    FileDeleted = 4,
    ExtensionInstalled = 5,
    ExtensionUninstalled = 6,
    TerminalCreated = 7,
    TerminalExited = 8,
    DebugStarted = 9,
    DebugStopped = 10,
    TaskStarted = 11,
    TaskCompleted = 12,
}

public sealed record DevEngineEvent(DevEngineEventType Type, string Payload);

public sealed class DevAppService(IDevEngine engine)
{
    public IDevEngine Engine { get; } = engine ?? throw new ArgumentNullException(nameof(engine));
}