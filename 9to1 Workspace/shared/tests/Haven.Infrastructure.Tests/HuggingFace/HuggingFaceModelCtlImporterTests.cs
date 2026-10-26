using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class HuggingFaceModelCtlImporterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_canonical_Python_import_publishes_verified_manifest_even_when_cancellation_races_commit(bool cancelAfterCommit)
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("haven-hf-modelctl-").FullName;
        try
        {
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var canonical = FindCanonicalDirectory();
            File.Copy(Path.Combine(canonical, "modelctl.py"), Path.Combine(root, "canonical_modelctl.py"));
            foreach (var name in new[] { "gguf.py", "model_lease.py" }) File.Copy(Path.Combine(canonical, name), Path.Combine(root, name));
            var wrapper = Path.Combine(root, "modelctl.py");
            await File.WriteAllTextAsync(wrapper, """
                import json, os, pathlib, sys, time
                pathlib.Path(__file__).with_name('invocation.json').write_text(json.dumps({'argv': sys.argv, 'env': dict(os.environ)}))
                from canonical_modelctl import main
                result = main()
                if result == 0 and pathlib.Path(__file__).with_name('pause-after-commit').exists():
                    pathlib.Path(__file__).with_name('committed.ready').write_text(str(os.getpid()))
                    time.sleep(120)
                raise SystemExit(result)
                """
            );
            byte[] bytes = [71, 71, 85, 70, 3, 0, 0, 0, 1, 2, 3];
            var local = Path.Combine(root, "selected model.gguf"); await File.WriteAllBytesAsync(local, bytes);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var store = Path.Combine(root, "store"); var runtime = Path.Combine(root, "runtime");
            var request = new HuggingFaceModelImport(local, "hf-fixture", "Fixture model", "apache-2.0",
                "https://huggingface.co/owner/model/blob/0123456789abcdef0123456789abcdef01234567/model.gguf", hash, bytes.Length);
            var importer = new HuggingFaceModelCtlImporter(wrapper, "/usr/bin/python3", store, runtime);
            if (cancelAfterCommit) await File.WriteAllTextAsync(Path.Combine(root, "pause-after-commit"), "fixture");
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var import = importer.ImportAsync(request, cancellation.Token);
            if (cancelAfterCommit)
            {
                while (!File.Exists(Path.Combine(root, "committed.ready")) && !import.IsCompleted)
                    await Task.Delay(20, cancellation.Token);
                Assert.True(File.Exists(Path.Combine(root, "committed.ready")));
                cancellation.Cancel();
            }
            await import; // A genuine installed receipt wins over cancellation after the atomic commit.
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(store, "manifests", "hf-fixture.json")));
            Assert.Equal(request.Source, manifest.RootElement.GetProperty("source").GetString());
            Assert.Equal(hash, manifest.RootElement.GetProperty("sha256").GetString());
            Assert.Equal("unverified", Assert.Single(manifest.RootElement.GetProperty("capabilities").EnumerateArray()).GetString());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(store, "blobs", "sha256", hash[..2], hash + ".gguf")));
            using var invocation = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "invocation.json")));
            var argv = invocation.RootElement.GetProperty("argv").EnumerateArray().Select(v => v.GetString()).ToArray();
            Assert.Contains(local, argv); Assert.Contains(request.Source, argv);
            foreach (var entry in invocation.RootElement.GetProperty("env").EnumerateObject())
                Assert.Equal("LC_CTYPE", entry.Name); // Python may add locale coercion; no inherited provider/user environment.
            // Duplicate IDs are rejected by modelctl, rather than silently overwriting a model.
            var error = await Assert.ThrowsAsync<HuggingFaceModelException>(() => importer.ImportAsync(request, CancellationToken.None));
            Assert.Equal(HuggingFaceModelError.ImportFailed, error.Code);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Cancellation_kills_and_reaps_the_owned_Python_child()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("haven-hf-modelctl-cancel-").FullName;
        try
        {
            var wrapper = Path.Combine(root, "modelctl.py");
            await File.WriteAllTextAsync(wrapper, "import os,pathlib,time\npathlib.Path(__file__).with_name('child.pid').write_text(str(os.getpid()))\ntime.sleep(120)\n");
            await File.WriteAllTextAsync(Path.Combine(root, "gguf.py"), "");
            await File.WriteAllTextAsync(Path.Combine(root, "model_lease.py"), "");
            var local = Path.Combine(root, "fixture.gguf"); await File.WriteAllBytesAsync(local, new byte[8]);
            var importer = new HuggingFaceModelCtlImporter(wrapper, "/usr/bin/python3", Path.Combine(root, "store"), Path.Combine(root, "runtime"));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var request = new HuggingFaceModelImport(local, "hf-fixture", "Fixture", "review-required",
                "https://huggingface.co/owner/model/blob/0123456789abcdef0123456789abcdef01234567/model.gguf", new string('a', 64), 8);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportAsync(request, cancellation.Token));
            var pid = int.Parse(await File.ReadAllTextAsync(Path.Combine(root, "child.pid")), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string FindCanonicalDirectory([CallerFilePath] string source = "")
    {
        for (var parent = new DirectoryInfo(Path.GetDirectoryName(source)!); parent is not null; parent = parent.Parent)
        {
            var candidate = Path.Combine(parent.FullName, "9to1 Workspace", "Home", "Source", "Dulche", "llamacpp");
            if (File.Exists(Path.Combine(candidate, "modelctl.py"))) return candidate;
        }
        throw new InvalidOperationException("The canonical modelctl source closure must be present for this real import test.");
    }
}
