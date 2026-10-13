using Haven.Application;

namespace NineToOne.Web.Spaces.Chat;

/// <summary>No browser OS/workspace broker is installed. These are explicit platform
/// refusals, never synthetic successful tools or a source of advertised capabilities.</summary>
internal sealed class BrowserUnavailableWorkspaceTools : IWorkspaceToolService
{
    private static PlatformNotSupportedException Missing() => new("The browser host has no authorized OS workspace provider.");
    public string ResolveWorkspacePath(string root, string path) => throw Missing();
    public Task<string> ReadTextAsync(string root, string path, CancellationToken token) => Task.FromException<string>(Missing());
    public Task WriteTextAtomicAsync(string root, string path, string content, CancellationToken token) => Task.FromException(Missing());
    public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) => Task.FromException<IReadOnlyList<string>>(Missing());
    public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) => Task.FromException<ProcessResult>(Missing());
}
internal sealed class BrowserUnavailableComputerTools : IComputerToolService
{
    public bool IsSupported => false;
    private static Task<string> Missing() => Task.FromException<string>(new PlatformNotSupportedException("The browser host has no authorized computer-control provider."));
    public Task<string> SnapshotAsync(CancellationToken token) => Missing();
    public Task<string> ListWindowsAsync(CancellationToken token) => Missing();
    public Task<string> LaunchAppAsync(string name, CancellationToken token) => Missing();
    public Task<string> FocusWindowAsync(string title, CancellationToken token) => Missing();
    public Task<string> InvokeAsync(string title, string name, string automationId, CancellationToken token) => Missing();
    public Task<string> ClickAsync(string title, int x, int y, string button, CancellationToken token) => Missing();
    public Task<string> TypeAsync(string title, string text, CancellationToken token) => Missing();
    public Task<string> PressAsync(string title, string keys, CancellationToken token) => Missing();
    public Task<string> CloseWindowAsync(string title, CancellationToken token) => Missing();
}
