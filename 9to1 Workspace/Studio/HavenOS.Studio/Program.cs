using System.Text.Json;

var root = FindWorkspaceRoot(Environment.CurrentDirectory);
var lockPath = Path.Combine(root, "havenos.lock");
using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "status";

if (command is "status" or "source")
{
    var ubuntu = document.RootElement.GetProperty("ubuntu");
    Console.WriteLine($"HavenOS source workspace: {root}");
    Console.WriteLine($"Ubuntu base: {ubuntu.GetProperty("release").GetString()} ({ubuntu.GetProperty("codename").GetString()})");
    foreach (var component in document.RootElement.GetProperty("components").EnumerateObject())
    {
        Console.WriteLine($"{component.Name}: {component.Value.GetProperty("classification").GetString()}");
    }
    return;
}

if (command == "validate")
{
    var required = new[] { "platform/gnome-shell", "platform/mutter", "platform/ubuntu/image/live-build/auto/config", "image/build-live-iso.sh", "tests/verify-workspace.ps1", "tests/verify-iso-provenance.ps1" };
    var missing = required.Where(path => !File.Exists(Path.Combine(root, path)) && !Directory.Exists(Path.Combine(root, path))).ToArray();
    if (missing.Length > 0) throw new InvalidOperationException("Missing required workspace paths: " + string.Join(", ", missing));
    Console.WriteLine("HavenOS workspace contract is present. This does not assert that an ISO or VM exists.");
    return;
}

Console.Error.WriteLine("Usage: HavenOS.Studio [status|source|validate]");
Environment.ExitCode = 2;

static string FindWorkspaceRoot(string start)
{
    for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "havenos.lock"))) return directory.FullName;
    throw new InvalidOperationException("Run inside a HavenOS workspace containing havenos.lock.");
}
