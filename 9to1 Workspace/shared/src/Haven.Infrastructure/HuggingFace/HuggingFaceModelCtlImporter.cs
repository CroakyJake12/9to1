using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Haven.Application;

namespace Haven.Infrastructure;

/// <summary>A fully downloaded, verified local file; no credential or signed URL crosses here.</summary>
public sealed record HuggingFaceModelImport(string LocalPath, string ModelId, string DisplayName,
    string License, string Source, string Sha256, long Size);
public interface IHuggingFaceModelImporter
{
    Task ImportAsync(HuggingFaceModelImport request, CancellationToken cancellationToken);
}

/// <summary>Uses the shipped canonical Python modelctl and its shared store/lease semantics.
/// No shell, inherited environment, network or provider credential is passed to the child.</summary>
public sealed class HuggingFaceModelCtlImporter : IHuggingFaceModelImporter
{
    private readonly string _script, _python, _modelRoot, _runtimeDirectory;
    public HuggingFaceModelCtlImporter(string? scriptPath = null, string? pythonPath = null,
        string? modelRoot = null, string? runtimeDirectory = null)
    {
        _script = Path.GetFullPath(scriptPath ?? LlamaCppRuntimeLayout.ReadExplicitEnvironment().ModelCtlPath);
        _python = Path.GetFullPath(pythonPath ?? (OperatingSystem.IsWindows()
            ? Path.Combine(AppContext.BaseDirectory, "python", "python.exe")
            : LlamaCppPreviewBootstrap.ModelCtlPythonPath));
        _modelRoot = Path.GetFullPath(modelRoot ?? LlamaCppPreviewBootstrap.DefaultModelRoot());
        _runtimeDirectory = Path.GetFullPath(runtimeDirectory ?? DefaultRuntimeDirectory());
    }

    private static string DefaultRuntimeDirectory()
    {
        var actual = Environment.GetEnvironmentVariable("HAVEN_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(actual)) return actual;
        actual = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(actual)) return Path.Combine(actual, "haven");
        if (OperatingSystem.IsLinux()) return Path.Combine(Path.GetTempPath(), "haven-" + GetUid());
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "haven", "runtime");
    }
    [DllImport("libc", EntryPoint = "getuid", ExactSpelling = true)] private static extern uint GetUid();

    public async Task ImportAsync(HuggingFaceModelImport request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!Regex.IsMatch(request.ModelId, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z") ||
                !Regex.IsMatch(request.Sha256, @"\A[0-9a-f]{64}\z") || request.Size < 8 || request.Size > HuggingFaceModelService.MaximumModelBytes ||
                !Uri.TryCreate(request.Source, UriKind.Absolute, out var source) || source.Scheme != "https" ||
                source.Host != "huggingface.co" || !source.IsDefaultPort || source.Query.Length != 0 ||
                source.Fragment.Length != 0 || source.UserInfo.Length != 0) Fail();
            var local = new FileInfo(request.LocalPath);
            if (!local.Exists || local.LinkTarget is not null || local.Length != request.Size) Fail();
            if (!File.Exists(_python) || !File.Exists(_script) || !File.Exists(Path.Combine(Path.GetDirectoryName(_script)!, "gguf.py")) ||
                !File.Exists(Path.Combine(Path.GetDirectoryName(_script)!, "model_lease.py"))) Fail();
            var start = new ProcessStartInfo(_python)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_script)!
            };
            // Isolated interpreter ignores user Python/environment configuration but deliberately
            // admits the shipped script directory, where its gguf/model_lease closure lives.
            start.Environment.Clear();
            if (OperatingSystem.IsWindows())
            {
                var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
                if (!string.IsNullOrEmpty(systemRoot)) start.Environment["SystemRoot"] = systemRoot;
            }
            foreach (var arg in new[] { "-B", "-E", "-s", "-S", "-X", "utf8", _script, "--model-root", _modelRoot,
                "--runtime-dir", _runtimeDirectory, "import", Path.GetFullPath(request.LocalPath), "--id", request.ModelId,
                "--name", request.DisplayName, "--license", request.License, "--origin", request.Source,
                // GGUF validity is a storage admission, not evidence of chat/embedding support.
                "--capability", "unverified", "--redistribution", "review-required" }) start.ArgumentList.Add(arg);
            using var process = new Process { StartInfo = start };
            if (!process.Start()) Fail();
            var stdout = DrainAsync(process.StandardOutput);
            var stderr = DrainAsync(process.StandardError);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } // The process may have just exited.
                catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
                await process.WaitForExitAsync(CancellationToken.None);
                try { await Task.WhenAll(stdout, stderr); }
                catch { } // Pipe cleanup cannot replace the caller's cancellation.
                // modelctl atomically publishes only after validating/adopting the complete blob.
                // Cancellation may race that commit. A genuine matching installed manifest and
                // rehashed blob establish installation even if the child was killed before stdout.
                if (await PublishedMatchesAsync(request, rehash: true)) return;
                throw;
            }
            catch
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                finally
                {
                    await process.WaitForExitAsync(CancellationToken.None);
                    await Task.WhenAll(stdout, stderr);
                }
                throw;
            }
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0 || output is null || error is null) Fail();
            using var json = JsonDocument.Parse(output!);
            var result = json.RootElement;
            if (result.GetProperty("status").GetString() != "imported") Fail();
            var model = result.GetProperty("model");
            if (!Matches(model, request)) Fail();
            if (!await PublishedMatchesAsync(request, rehash: false)) Fail();
        }
        catch (OperationCanceledException) { throw; }
        catch (HuggingFaceModelException) { throw; }
        catch { Fail(); }
    }

    private async Task<bool> PublishedMatchesAsync(HuggingFaceModelImport request, bool rehash)
    {
        try
        {
            // Inspect the actual canonical published manifest, not just a child's success string.
            var manifestPath = Path.Combine(_modelRoot, "manifests", request.ModelId + ".json");
            var manifestInfo = new FileInfo(manifestPath);
            if (!manifestInfo.Exists || manifestInfo.LinkTarget is not null || manifestInfo.Length > 65536) return false;
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, CancellationToken.None));
            if (!Matches(manifest.RootElement, request)) return false;
            var blob = Path.Combine(_modelRoot, "blobs", "sha256", request.Sha256[..2], request.Sha256 + ".gguf");
            var blobInfo = new FileInfo(blob);
            if (!blobInfo.Exists || blobInfo.LinkTarget is not null || blobInfo.Length != request.Size) return false;
            if (rehash)
            {
                await using var stream = new FileStream(blob, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, CancellationToken.None)) != request.Sha256) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static bool Matches(JsonElement model, HuggingFaceModelImport request) =>
        model.GetProperty("id").GetString() == request.ModelId && model.GetProperty("sha256").GetString() == request.Sha256 &&
        model.GetProperty("size").GetInt64() == request.Size && model.GetProperty("source").GetString() == request.Source &&
        model.GetProperty("capabilities").ValueKind == JsonValueKind.Array &&
        model.GetProperty("capabilities").GetArrayLength() == 1 && model.GetProperty("capabilities")[0].GetString() == "unverified" &&
        model.GetProperty("blob").GetString() == request.Sha256[..2] + "/" + request.Sha256 + ".gguf";

    private static async Task<string?> DrainAsync(StreamReader reader)
    {
        var result = new StringBuilder(); var buffer = new char[4096]; var exceeded = false;
        while (true)
        {
            var count = await reader.ReadAsync(buffer);
            if (count == 0) break;
            if (result.Length + count > 65536) exceeded = true;
            if (!exceeded) result.Append(buffer, 0, count);
        }
        return exceeded ? null : result.ToString();
    }
    private static void Fail() => throw HuggingFaceModelService.Error(HuggingFaceModelError.ImportFailed);
}
