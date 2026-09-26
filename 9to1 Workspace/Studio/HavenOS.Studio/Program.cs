using System.Text.Json;
using Avalonia;

namespace HavenOS.AIStudio;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant();
        if (command is "status" or "source" or "validate") return RunWorkspaceCommand(args, command);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    private static int RunWorkspaceCommand(string[] args, string command)
    {
        try
        {
            var root = FindWorkspaceRoot(Environment.CurrentDirectory);
            var lockPath = Path.Combine(root, "havenos.lock");
            using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
            if (command is "status" or "source")
            {
                var ubuntu = document.RootElement.GetProperty("ubuntu");
                Console.WriteLine($"HavenOS source workspace: {root}");
                Console.WriteLine($"Ubuntu base: {ubuntu.GetProperty("release").GetString()} ({ubuntu.GetProperty("codename").GetString()})");
                foreach (var component in document.RootElement.GetProperty("components").EnumerateObject())
                    Console.WriteLine($"{component.Name}: {component.Value.GetProperty("classification").GetString()}");
                return 0;
            }

            var required = new[] { "platform/gnome-shell", "platform/mutter", "platform/ubuntu/image/live-build/auto/config", "image/build-live-iso.sh", "tests/verify-workspace.ps1", "tests/verify-iso-provenance.ps1" };
            var missing = required.Where(path => !File.Exists(Path.Combine(root, path)) && !Directory.Exists(Path.Combine(root, path))).ToArray();
            if (missing.Length > 0)
            {
                Console.Error.WriteLine("Missing required workspace paths: " + string.Join(", ", missing));
                return 1;
            }
            Console.WriteLine("HavenOS workspace contract is present. This does not assert that an ISO or VM exists.");
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string FindWorkspaceRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "havenos.lock"))) return directory.FullName;
        throw new InvalidOperationException("Run inside a HavenOS workspace containing havenos.lock.");
    }
}
