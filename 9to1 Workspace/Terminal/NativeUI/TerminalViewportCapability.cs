namespace HavenOS.Apps.Terminal.NativeUI;

public sealed record TerminalViewportAvailability(bool Available, string Code, string Message);

/// <summary>Checks the installed owning screen backend without starting a process,
/// creating an environment identity or granting access to any session.</summary>
public static class TerminalViewportCapability
{
    public static TerminalViewportAvailability Check()
    {
        if (!OperatingSystem.IsLinux())
            return new(false, "terminal.viewport.platform-unavailable", "The native Terminal viewport is unavailable on this platform.");
        try
        {
            using var screen = new TerminalScreen(1, 1);
            var snapshot = screen.Snapshot();
            if (snapshot.Rows != 1 || snapshot.Columns != 1 || snapshot.Cells.Count != 1)
                throw new InvalidOperationException("The installed Terminal screen returned an invalid initial frame.");
            return new(true, "terminal.viewport.available", "The installed native Terminal screen is available.");
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException
            or BadImageFormatException or PlatformNotSupportedException or InvalidOperationException)
        {
            return new(false, "terminal.viewport.backend-unavailable", "The installed native Terminal screen could not be initialized: " + error.Message);
        }
    }
}
