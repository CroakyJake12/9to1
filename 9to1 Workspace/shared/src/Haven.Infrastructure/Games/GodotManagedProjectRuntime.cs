using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core.Games;

namespace Haven.Infrastructure.Games;

/// <summary>Invokes the installed, explicitly pinned first-party C# module using canonical project data only.
/// Installation paths/pins are trusted host configuration; callers cannot select executable/module paths.</summary>
public sealed class GodotManagedProjectRuntime(string executablePath, string moduleDirectory,
    string expectedExecutableSha256, string expectedModuleSha256) : IGamesManagedProjectRuntime
{
    private const string Marker = "ASTRA_GAMES_MANAGED_SCENE=";
    public async Task<GamesManagedProjectObservation> ObserveAsync(GamesProjectDocument project, Guid sceneID, CancellationToken cancellationToken = default)
    {
        var captured = project.Capture();
        var scene = captured.Scenes.SingleOrDefault(item => item.SceneID == sceneID) ?? throw new KeyNotFoundException("GamesSceneNotFound");
        if (expectedExecutableSha256.Length != 64 || expectedModuleSha256.Length != 64) throw new InvalidDataException("Managed Games runtime pins are required.");
        var executable = Path.GetFullPath(executablePath); var module = Path.GetFullPath(moduleDirectory);
        await VerifyAsync().ConfigureAwait(false);
        var temporary = Directory.CreateTempSubdirectory("9to1-games-managed-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var source = Path.Combine(temporary, "canonical.9to1g");
            await File.WriteAllBytesAsync(source, GamesProjectCodec.Encode(captured), cancellationToken).ConfigureAwait(false);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var value in new[] { "--headless", "--path", module, "--", source, sceneID.ToString("D") }) start.ArgumentList.Add(value);
            foreach (var item in new[] { ("XDG_CACHE_HOME", "cache"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data") })
                start.Environment[item.Item1] = Directory.CreateDirectory(Path.Combine(temporary, item.Item2)).FullName;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var process = Process.Start(start) ?? throw new IOException("Managed Games process failed to start.");
            var output = DrainAsync(process.StandardOutput, deadline.Token);
            var errors = DrainAsync(process.StandardError, deadline.Token);
            try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); await Task.WhenAll(output, errors).ConfigureAwait(false); }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            var text = await output.ConfigureAwait(false); var errorText = await errors.ConfigureAwait(false);
            var frames = text.Split('\n').Where(line => line.StartsWith(Marker, StringComparison.Ordinal)).ToArray();
            if (process.ExitCode != 0 || frames.Length != 1 || text.Contains("ERROR:", StringComparison.Ordinal) || errorText.Contains("ERROR:", StringComparison.Ordinal))
                throw new InvalidDataException("Managed Games scene did not produce one successful observation.");
            var observed = JsonSerializer.Deserialize<Observation>(frames[0][Marker.Length..], new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Managed Games observation is missing.");
            if (observed.ProjectID != captured.ProjectID || observed.ProjectRevision != captured.Revision || observed.SceneID != sceneID
                || observed.Revision != scene.Revision || observed.Nodes is null || observed.Nodes.Count != scene.Nodes.Count
                || observed.Nodes.Select(node => node.NodeID).Distinct().Count() != scene.Nodes.Count
                || !observed.ObservedEngineVersion.StartsWith("4.7.2", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(observed.ManagedRuntimeVersion))
                throw new InvalidDataException("Managed Games observation has different canonical identity or runtime version.");
            foreach (var node in observed.Nodes)
            {
                var original = scene.Nodes.SingleOrDefault(item => item.NodeID == node.NodeID);
                if (original is null || node.EntityID != original.EntityID || node.ComponentID != original.Spatial.ComponentID
                    || node.Position != original.Spatial.Position || node.MeshResourceID != original.MeshResourceID
                    || node.WorldPosition is null || !float.IsFinite(node.WorldPosition.X) || !float.IsFinite(node.WorldPosition.Y) || !float.IsFinite(node.WorldPosition.Z)
                    || node.MeshVertexCount != (original.MeshResourceID is { } id ? scene.Meshes.Single(mesh => mesh.ResourceID == id).Geometry?.TriangleIndices.Count ?? 36 : 0))
                    throw new InvalidDataException("Managed Games node differs from its canonical scene.");
            }
            await VerifyAsync().ConfigureAwait(false);
            return new(captured.ProjectID, captured.Revision, sceneID, scene.Revision, observed.ObservedEngineVersion,
                observed.ManagedRuntimeVersion, expectedExecutableSha256, expectedModuleSha256, observed.Nodes.ToArray());
        }
        finally { Directory.Delete(temporary, recursive: true); }

        async Task VerifyAsync()
        {
            if (!string.Equals(await FileHashAsync(executable, cancellationToken).ConfigureAwait(false), expectedExecutableSha256, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(await FingerprintModuleAsync(module, cancellationToken).ConfigureAwait(false), expectedModuleSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installed managed Games runtime differs from the trusted host pins.");
        }
    }

    /// <summary>Release tooling records this value after building/importing the reviewed fixed module. Runtime use requires that prior pin.</summary>
    public static async Task<string> FingerprintModuleAsync(string moduleDirectory, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(moduleDirectory);
        var bin = Path.Combine(root, ".godot", "mono", "temp", "bin", "Debug");
        if (!Directory.Exists(bin)) throw new FileNotFoundException("The managed Games module has not been built.");
        var paths = new List<string> { "project.godot", "Main.tscn", "CanonicalSceneDriver.cs" };
        var queue = new Queue<string>(); queue.Enqueue(bin); var directoryCount = 0;
        while (queue.Count > 0)
        {
            var directory = queue.Dequeue();
            if (++directoryCount > 256) throw new InvalidDataException("Managed module exceeds configured directory bounds.");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Managed module cannot redirect its installation.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Managed module cannot redirect its installation.");
                if (Directory.Exists(entry)) queue.Enqueue(entry); else paths.Add(Path.GetRelativePath(root, entry));
                if (paths.Count + queue.Count > 256) throw new InvalidDataException("Managed module exceeds configured file bounds.");
            }
        }
        if (!paths.Any(path => Path.GetFileName(path) == "HavenOS.Games.Runtime.dll")) throw new FileNotFoundException("The managed driver assembly is missing.");
        long bytes = 0; var manifest = new StringBuilder();
        foreach (var relative in paths.Order(StringComparer.Ordinal))
        {
            var file = Path.Combine(root, relative);
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || (bytes = checked(bytes + new FileInfo(file).Length)) > 64 * 1024 * 1024)
                throw new InvalidDataException("Managed module exceeds bounds or redirects a file.");
            manifest.Append(relative.Replace('\\', '/')).Append('\0').Append(await FileHashAsync(file, cancellationToken).ConfigureAwait(false)).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString())));
    }
    private static async Task<string> FileHashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }
    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
    {
        var builder = new StringBuilder(); var buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            if (builder.Length + count > 262144) throw new InvalidDataException("Managed Games output exceeds its configured limit.");
            builder.Append(buffer, 0, count);
        }
        return builder.ToString();
    }
    private sealed record Observation(Guid ProjectID, long ProjectRevision, Guid SceneID, long Revision,
        string ObservedEngineVersion, string ManagedRuntimeVersion, IReadOnlyList<GamesNativeNodeObservation> Nodes);
}
