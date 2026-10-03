using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Core.Games;

namespace Haven.Infrastructure.Games;

/// <summary>Native modelling works on the canonical mesh buffer/ID, not an external user export/import
/// workflow. Only fixed trusted Python runs; no user .blend file, script or asset path is accepted.</summary>
public sealed class BlenderMeshModeller(string executablePath) : IGamesMeshModeller
{
    public async Task<GamesMeshModelingResult> SubdivideAsync(GamesMeshResource resource, int levels,
        CancellationToken cancellationToken = default)
    {
        var snapshot = resource.Capture();
        snapshot.Validate();
        if (levels is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(levels));
        var estimatedIndices = (long)(snapshot.Geometry?.TriangleIndices.Count ?? 36) * (1L << (levels * 2));
        if (estimatedIndices > 393216 || (levels > 0 && estimatedIndices > 65536))
            throw new InvalidDataException("Games subdivision would exceed the configured geometry limits.");
        var executable = Path.GetFullPath(executablePath);
        if (!File.Exists(executable)) throw new FileNotFoundException("Games modelling runtime is unavailable.");
        var hash = await HashAsync(executable, cancellationToken).ConfigureAwait(false);
        var directory = Directory.CreateTempSubdirectory("9to1-games-model-").FullName;
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            var input = Path.Combine(directory, "input.json");
            var output = Path.Combine(directory, "output.json");
            var script = Path.Combine(directory, "model.py");
            await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new { resource = snapshot, levels },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), cancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(script, ModellingScript, cancellationToken).ConfigureAwait(false);
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--background", "--factory-startup", "--disable-autoexec", "--python", script, "--", input, output })
                start.ArgumentList.Add(argument);
            start.Environment["LC_ALL"] = "C";
            foreach (var pair in new[] { ("XDG_CACHE_HOME", "cache"), ("XDG_CONFIG_HOME", "config"), ("XDG_DATA_HOME", "data"),
                ("BLENDER_USER_CONFIG", "blender-config"), ("BLENDER_USER_SCRIPTS", "blender-scripts"), ("BLENDER_USER_DATAFILES", "blender-data") })
                start.Environment[pair.Item1] = Directory.CreateDirectory(Path.Combine(directory, pair.Item2)).FullName;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using var process = Process.Start(start) ?? throw new IOException("Games modelling process did not start.");
            var stdout = DrainAsync(process.StandardOutput, deadline.Token);
            var stderr = DrainAsync(process.StandardError, deadline.Token);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            if (process.ExitCode != 0 || !File.Exists(output) || new FileInfo(output).Length > 8388608)
                throw new InvalidDataException("Games native modelling failed or exceeded the output limit.");
            var result = JsonSerializer.Deserialize<ModelResult>(await File.ReadAllTextAsync(output, cancellationToken).ConfigureAwait(false),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Games native modelling result is missing.");
            if (result.ResourceID != snapshot.ResourceID || string.IsNullOrWhiteSpace(result.ObservedRuntimeVersion)
                || result.Vertices is null || result.TriangleIndices is null)
                throw new InvalidDataException("Games modelling result changed canonical resource identity.");
            var modelled = snapshot with { Geometry = new(result.Vertices.ToArray(), result.TriangleIndices.ToArray()) };
            modelled.Validate();
            if (await HashAsync(executable, cancellationToken).ConfigureAwait(false) != hash)
                throw new InvalidDataException("Games modelling runtime changed during the operation.");
            return new(modelled, result.ObservedRuntimeVersion, hash);
        }
        finally
        {
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    private sealed record ModelResult(Guid ResourceID, string ObservedRuntimeVersion,
        IReadOnlyList<GamesVector3> Vertices, IReadOnlyList<int> TriangleIndices);
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));
    }
    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
            if (result.Length < 16384) result.Append(buffer, 0, Math.Min(read, 16384 - result.Length));
        return result.ToString();
    }
    private const string ModellingScript = """
        import bpy, json, sys
        arguments = sys.argv[sys.argv.index("--") + 1:]
        with open(arguments[0], encoding="utf-8") as stream:
            request = json.load(stream)
        source = request["resource"]
        bpy.ops.object.select_all(action="SELECT")
        bpy.ops.object.delete(use_global=False)
        geometry = source.get("geometry")
        if geometry is None:
            bpy.ops.mesh.primitive_cube_add(size=1.0)
            obj = bpy.context.object
            for vertex in obj.data.vertices:
                vertex.co.x *= source["size"]["x"]
                vertex.co.y *= source["size"]["y"]
                vertex.co.z *= source["size"]["z"]
        else:
            mesh = bpy.data.meshes.new("9to1 canonical mesh")
            indices = geometry["triangleIndices"]
            mesh.from_pydata([(v["x"],v["y"],v["z"]) for v in geometry["vertices"]], [],
                [indices[index:index+3] for index in range(0, len(indices), 3)])
            obj = bpy.data.objects.new("9to1 canonical mesh", mesh)
            bpy.context.collection.objects.link(obj)
        if request["levels"]:
            modifier = obj.modifiers.new("9to1 subdivision", "SUBSURF")
            modifier.subdivision_type = "SIMPLE"
            modifier.levels = request["levels"]
        evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = evaluated.to_mesh()
        try:
            mesh.calc_loop_triangles()
            vertices = [{"x":float(v.co.x),"y":float(v.co.y),"z":float(v.co.z)} for v in mesh.vertices]
            indices = [int(index) for triangle in mesh.loop_triangles for index in triangle.vertices]
            with open(arguments[1], "w", encoding="utf-8") as stream:
                json.dump({"resourceID":source["resourceID"],"observedRuntimeVersion":bpy.app.version_string,
                    "vertices":vertices,"triangleIndices":indices}, stream, allow_nan=False)
        finally:
            evaluated.to_mesh_clear()
        """;
}
