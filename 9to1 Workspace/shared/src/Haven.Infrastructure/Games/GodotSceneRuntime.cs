using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core.Games;

namespace Haven.Infrastructure.Games;

/// <summary>Observed native scene semantics over generated data-only scenes. The fixed private donor
/// bridge is not a user scripting language or a substitute for the required managed Games runtime.</summary>
public sealed class GodotSceneRuntime(string executablePath) : IGamesSceneRuntime
{
    private const string Marker = "ASTRA_GAMES_SCENE_RESULT=";
    public async Task<GamesNativeSceneObservation> ObserveAsync(GamesSceneSnapshot scene, CancellationToken cancellationToken = default)
    {
        var snapshot = scene.Capture(); // Capture before validation/await; no arbitrary scripts, assets or user paths.
        var executable = Path.GetFullPath(executablePath);
        if (!File.Exists(executable)) throw new FileNotFoundException("Games native runtime is unavailable.");
        var executableHash = await HashAsync(executable, cancellationToken).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("9to1-games-native-scene-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "project.godot"),
                "config_version=5\n[application]\nconfig/name=\"9to1 internal scene bridge\"\n[rendering]\nrenderer/rendering_method=\"gl_compatibility\"\n", cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "scene.tscn"), GenerateScene(snapshot), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "geometry.json"), JsonSerializer.Serialize(
                snapshot.Meshes.Where(mesh => mesh.Geometry is not null), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(directory, "observe.gd"), Probe, cancellationToken).ConfigureAwait(false);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--headless", "--path", directory, "--script", "res://observe.gd" })
                start.ArgumentList.Add(argument);
            start.Environment["LC_ALL"] = "C";
            // Donor state is operation-scoped; HOME and canonical project storage are untouched.
            foreach (var pair in new[] { ("XDG_CACHE_HOME", "cache"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data") })
                start.Environment[pair.Item1] = Directory.CreateDirectory(Path.Combine(directory, pair.Item2)).FullName;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            using var process = Process.Start(start) ?? throw new IOException("Games native process did not start.");
            var output = DrainAsync(process.StandardOutput, deadline.Token, 262144);
            var errors = DrainAsync(process.StandardError, deadline.Token, 16384);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await Task.WhenAll(output, errors).ConfigureAwait(false);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            var text = await output.ConfigureAwait(false);
            var frames = text.Split('\n').Where(line => line.StartsWith(Marker, StringComparison.Ordinal)).ToArray();
            if (process.ExitCode != 0 || frames.Length != 1 || text.Length >= 262144
                || (await errors.ConfigureAwait(false)).Contains("ERROR:", StringComparison.Ordinal))
                throw new InvalidDataException("Games native scene could not be observed.");
            var observed = JsonSerializer.Deserialize<ProbeResult>(frames[0][Marker.Length..],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Games native observation is missing.");
            if (observed.ProjectID != snapshot.ProjectID || observed.SceneID != snapshot.SceneID || observed.Revision != snapshot.Revision
                || string.IsNullOrWhiteSpace(observed.ObservedEngineVersion) || observed.Nodes is null
                || observed.Nodes.Count != snapshot.Nodes.Count || observed.Nodes.Select(node => node.NodeID).Distinct().Count() != observed.Nodes.Count)
                throw new InvalidDataException("Games native observation does not match the canonical scene.");
            foreach (var node in observed.Nodes)
            {
                var original = snapshot.Nodes.SingleOrDefault(candidate => candidate.NodeID == node.NodeID);
                var expectedVertices = original?.MeshResourceID is { } resourceID
                    ? snapshot.Meshes.Single(mesh => mesh.ResourceID == resourceID).Geometry?.TriangleIndices.Count ?? 36 : 0;
                if (original is null || node.EntityID != original.EntityID || node.ComponentID != original.Spatial.ComponentID
                    || node.MeshResourceID != original.MeshResourceID || node.Position != original.Spatial.Position
                    || node.WorldPosition is null || !float.IsFinite(node.WorldPosition.X) || !float.IsFinite(node.WorldPosition.Y)
                    || !float.IsFinite(node.WorldPosition.Z) || node.MeshVertexCount != expectedVertices)
                    throw new InvalidDataException("Games native node semantics do not match the canonical scene.");
            }
            if (await HashAsync(executable, cancellationToken).ConfigureAwait(false) != executableHash)
                throw new InvalidDataException("Games runtime changed during observation.");
            return new(snapshot.ProjectID, snapshot.SceneID, snapshot.Revision, observed.ObservedEngineVersion, executableHash, observed.Nodes.ToArray());
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private static string GenerateScene(GamesSceneSnapshot scene)
    {
        var text = new StringBuilder($"[gd_scene load_steps={scene.Meshes.Count + 1} format=3]\n");
        foreach (var mesh in scene.Meshes)
        {
            text.AppendLine($"\n[sub_resource type=\"BoxMesh\" id=\"m{mesh.ResourceID:N}\"]");
            text.AppendLine($"size = {Vector(mesh.Size)}");
            text.AppendLine($"metadata/_9to1_resource_id = \"{mesh.ResourceID:D}\"");
        }
        var paths = new Dictionary<Guid, string>();
        var remaining = scene.Nodes.ToList();
        while (remaining.Count > 0)
        {
            var node = remaining.First(candidate => candidate.ParentNodeID is null || paths.ContainsKey(candidate.ParentNodeID.Value));
            var name = "n" + node.NodeID.ToString("N");
            var parent = node.ParentNodeID is { } parentID ? paths[parentID] : null;
            text.AppendLine($"\n[node name=\"{name}\" type=\"{(node.MeshResourceID is null ? "Node3D" : "MeshInstance3D")}\"{(parent is null ? "" : " parent=" + JsonSerializer.Serialize(parent))}]");
            text.AppendLine($"position = {Vector(node.Spatial.Position)}");
            if (node.MeshResourceID is { } mesh) text.AppendLine($"mesh = SubResource(\"m{mesh:N}\")");
            text.AppendLine($"metadata/_9to1_node_id = \"{node.NodeID:D}\"");
            text.AppendLine($"metadata/_9to1_entity_id = \"{node.EntityID:D}\"");
            text.AppendLine($"metadata/_9to1_component_id = \"{node.Spatial.ComponentID:D}\"");
            text.AppendLine("metadata/_9to1_display_name = " + JsonSerializer.Serialize(node.Name));
            if (parent is null)
            {
                text.AppendLine($"metadata/_9to1_project_id = \"{scene.ProjectID:D}\"");
                text.AppendLine($"metadata/_9to1_scene_id = \"{scene.SceneID:D}\"");
                text.AppendLine($"metadata/_9to1_revision = {scene.Revision}");
                paths[node.NodeID] = ".";
            }
            else paths[node.NodeID] = parent == "." ? name : parent + "/" + name;
            remaining.Remove(node);
        }
        return text.ToString();
    }
    private static string Vector(GamesVector3 value) => $"Vector3({value.X.ToString("R", CultureInfo.InvariantCulture)}, {value.Y.ToString("R", CultureInfo.InvariantCulture)}, {value.Z.ToString("R", CultureInfo.InvariantCulture)})";
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
    }
    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token, int limit)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            if (text.Length < limit) text.Append(buffer, 0, Math.Min(read, limit - text.Length));
        return text.ToString();
    }
    private sealed record ProbeResult(Guid ProjectID, Guid SceneID, long Revision, string ObservedEngineVersion,
        IReadOnlyList<GamesNativeNodeObservation> Nodes);
    private const string Probe = """
        extends SceneTree
        func _initialize():
            call_deferred("_observe_scene")
        func _observe_scene():
            var geometries = JSON.parse_string(FileAccess.get_file_as_string("res://geometry.json"))
            if geometries == null:
                quit(3)
                return
            var resources = {}
            for source in geometries:
                var positions = PackedVector3Array()
                for vector in source.geometry.vertices:
                    positions.append(Vector3(vector.x, vector.y, vector.z))
                var arrays = []
                arrays.resize(Mesh.ARRAY_MAX)
                arrays[Mesh.ARRAY_VERTEX] = positions
                arrays[Mesh.ARRAY_INDEX] = PackedInt32Array(source.geometry.triangleIndices)
                var mesh = ArrayMesh.new()
                mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
                mesh.set_meta("_9to1_resource_id", source.resourceID)
                resources[source.resourceID] = mesh
            var packed = load("res://scene.tscn")
            if packed == null:
                quit(2)
                return
            var scene = packed.instantiate()
            root.add_child(scene)
            var queue = [scene]
            var observations = []
            while not queue.is_empty():
                var node = queue.pop_front()
                var mesh_id = null
                var vertices = 0
                if node is MeshInstance3D:
                    mesh_id = node.mesh.get_meta("_9to1_resource_id")
                    if resources.has(mesh_id):
                        node.mesh = resources[mesh_id]
                    vertices = node.mesh.get_faces().size()
                observations.append({"nodeID": node.get_meta("_9to1_node_id"), "entityID": node.get_meta("_9to1_entity_id"),
                    "componentID": node.get_meta("_9to1_component_id"), "position": {"x":node.position.x,"y":node.position.y,"z":node.position.z},
                    "worldPosition": {"x":node.global_position.x,"y":node.global_position.y,"z":node.global_position.z},
                    "meshResourceID":mesh_id,"meshVertexCount":vertices})
                queue.append_array(node.get_children())
            print("ASTRA_GAMES_SCENE_RESULT=" + JSON.stringify({"projectID":scene.get_meta("_9to1_project_id"),
                "sceneID":scene.get_meta("_9to1_scene_id"),"revision":scene.get_meta("_9to1_revision"),
                "observedEngineVersion":Engine.get_version_info()["string"],"nodes":observations}))
            scene.free()
            quit(0)
        """;
}
