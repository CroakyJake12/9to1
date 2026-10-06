using System.Text;
namespace Haven.Application;

/// <summary>Fixed maintained host command transport. This builds a request; it grants no permission.
/// Canonical execution still requires the original action, model/tool policy and physical root owner.</summary>
public static class WorkspaceToolProcessRequestFactory
{
    public static ProcessRequest CreateOriginal(string root, string command, int timeoutSeconds)
    {
        if (OperatingSystem.IsWindows()) return CreateWindowsPowerShell(root, command, timeoutSeconds);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
            return CreatePosixSh(root, command, timeoutSeconds);
        throw new PlatformNotSupportedException("No maintained workspace command transport exists on this platform.");
    }
    public static ProcessRequest CreateWindowsPowerShell(string root, string command, int timeoutSeconds)
    {
        DemandInput(root, command);
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
        return new("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded,
            root, TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 900)));
    }
    public static ProcessRequest CreatePosixSh(string root, string command, int timeoutSeconds)
    {
        DemandInput(root, command);
        // No shell quoting/escaping of the caller's command: the entire original text is one argv.
        return new("/bin/sh", "", root, TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 900)))
        { ArgumentList = Array.AsReadOnly(new[] { "-c", command }) };
    }
    private static void DemandInput(string root, string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root); ArgumentException.ThrowIfNullOrWhiteSpace(command);
        if (root.Contains('\0') || command.Contains('\0')) throw new ArgumentException("Native command fields cannot contain NUL.");
    }
}
