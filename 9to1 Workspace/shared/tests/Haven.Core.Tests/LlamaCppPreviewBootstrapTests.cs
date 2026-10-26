using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Infrastructure;

namespace Haven.Core.Tests;

public sealed class LlamaCppPreviewBootstrapTests
{
    private const int ArmAarch64ElfMachine = 183;
    private const int X86_64ElfMachine = 62;

    [LinuxPreviewTheory]
    [InlineData("")]
    [InlineData("mixed-elf")]
    [InlineData("mixed-relabelled-kind")]
    [InlineData("host")]
    [InlineData("platform")]
    [InlineData("hash")]
    [InlineData("missing-hash")]
    [InlineData("size")]
    [InlineData("missing-size")]
    [InlineData("receipt-missing")]
    [InlineData("receipt-empty")]
    [InlineData("receipt-hash")]
    [InlineData("receipt-asset-missing")]
    [InlineData("receipt-bundle")]
    [InlineData("receipt-state")]
    public async Task Explicit_x64_discovery_checks_entire_targeted_closure_and_receipt_without_weakening_ARM(string defect)
    {
        var root = Directory.CreateTempSubdirectory("haven-preview-explicit-x64-").FullName;
        try
        {
            var runtime = Path.Combine(root, "runtime");
            var bundle = Path.Combine(runtime, "inference", "bundle");
            var models = Path.Combine(root, "models");
            var (assets, serverPath, serverSha) = await ScaffoldClosure(runtime, X86_64ElfMachine);
            var manifest = JsonSerializer.SerializeToNode(CanonicalManifest(assets, "x86_64"))!;
            var receipt = JsonSerializer.SerializeToNode(CanonicalReceipt(assets))!;
            var dependency = manifest["assets"]![1]!;
            switch (defect)
            {
                case "mixed-elf":
                case "mixed-relabelled-kind":
                    var path = Path.Combine(runtime, "llama.cpp", "libggml-base.so.0");
                    WriteElf(path, ArmAarch64ElfMachine);
                    dependency["expectedSha256"] = ComputeSha256(path);
                    if (defect == "mixed-relabelled-kind") dependency["kind"] = "runtime.bundle";
                    receipt["admittedAssets"]![dependency["logical"]!.GetValue<string>()]!["sha256"] = ComputeSha256(path);
                    break;
                case "host": manifest["host"]!["arch"] = "aarch64"; break;
                case "platform": dependency["platform"]!["arch"] = "aarch64"; break;
                case "hash": dependency["expectedSha256"] = new string('f', 64); break;
                case "missing-hash": dependency.AsObject().Remove("expectedSha256"); break;
                case "size": dependency["expectedSize"] = 1; break;
                case "missing-size": dependency.AsObject().Remove("expectedSize"); break;
                case "receipt-empty": receipt["admittedAssets"] = new JsonObject(); break;
                case "receipt-hash": receipt["admittedAssets"]![manifest["assets"]![0]!["logical"]!.GetValue<string>()]!["sha256"] = new string('f', 64); break;
                case "receipt-asset-missing": receipt["admittedAssets"]!.AsObject().Remove(dependency["logical"]!.GetValue<string>()); break;
                case "receipt-bundle": receipt["bundleId"] = "different-bundle"; break;
                case "receipt-state": receipt["state"] = "invalid"; break;
            }
            await JsonFileAsync(Path.Combine(bundle, "bundle-manifest.json"), manifest);
            if (defect != "receipt-missing") await JsonFileAsync(Path.Combine(bundle, "bundle-receipt.json"), receipt);
            byte[] bytes = [71, 71, 85, 70, 3, 0, 0, 0, 1, 2, 3];
            var modelSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await ScaffoldModel(models, "selected", modelSha, bytes);
            await JsonFileAsync(Path.Combine(models, "manifests", "selected.json"), ModelManifest("selected", modelSha, bytes.Length));
            var x64 = new LlamaCppPreviewBootstrap(bundle, runtime, models, target: LlamaCppPreviewTarget.LinuxX64);
            var discovery = await x64.DiscoverAsync(CancellationToken.None);
            if (defect.Length == 0)
            {
                Assert.True(discovery.Available, string.Join("; ", discovery.Deficiencies));
                Assert.Equal(serverPath, discovery.ExecutablePath);
                Assert.Equal(serverSha, discovery.ExpectedExecutableSha256);
                Assert.Equal(X86_64ElfMachine, discovery.ElfMachine);
                Assert.True(x64.BuildEndpointOptions(discovery).Enabled);
                Assert.True(x64.BuildRuntimeOptions(discovery).Enabled);
                // Verification is not inference readiness: no child process is launched by discovery.
            }
            else
            {
                Assert.False(discovery.Available);
                Assert.NotEmpty(discovery.Deficiencies);
                Assert.False(x64.BuildEndpointOptions(discovery).Enabled);
                Assert.False(x64.BuildRuntimeOptions(discovery).Enabled);
            }
            // Default ARM verification cannot accept even a fully matching, genuinely x64 closure.
            var arm = new LlamaCppPreviewBootstrap(bundle, runtime, models);
            Assert.False((await arm.DiscoverAsync(CancellationToken.None)).Available);
            Assert.False(arm.BuildEndpointOptions(discovery).Enabled);
            Assert.False(arm.BuildRuntimeOptions(discovery).Enabled);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Explicit_bundle_layout_resolves_relative_to_app_not_working_directory_and_keeps_installed_defaults()
    {
        var app = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "haven-layout-app"));
        var relative = LlamaCppRuntimeLayout.ReadExplicitEnvironment(_ => "private-assets", app);
        var root = Path.Combine(app, "private-assets");
        Assert.Equal(root, relative.RuntimeRoot);
        Assert.Equal(Path.Combine(root, "inference", "bundle"), relative.BundleDirectory);
        Assert.Equal(Path.Combine(root, "inference", "modelctl.py"), relative.ModelCtlPath);
        Assert.Equal(Path.Combine(root, "default-models"), relative.SeedDirectory);
        Assert.Equal(relative, LlamaCppRuntimeLayout.ReadExplicitEnvironment(_ => root, app));
        var installed = LlamaCppRuntimeLayout.ReadExplicitEnvironment(_ => null, app);
        Assert.Equal(LlamaCppPreviewBootstrap.RuntimeRoot, installed.RuntimeRoot);
        Assert.Equal(LlamaCppPreviewBootstrap.BundleDir, installed.BundleDirectory);
        Assert.Equal(LlamaCppPreviewBootstrap.DefaultSeedDir, installed.SeedDirectory);
        if (OperatingSystem.IsLinux()) Assert.Equal(LlamaCppPreviewBootstrap.ModelCtlScriptPath, installed.ModelCtlPath);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppPreviewBootstrap(target: (LlamaCppPreviewTarget)999));
    }

    public sealed class LinuxPreviewTheoryAttribute : TheoryAttribute
    { public LinuxPreviewTheoryAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires Linux AF_UNIX target discovery; no runtime or model inference is performed."; } }

    [LinuxPreviewTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Default_ARM_discovery_keeps_ARM_closure_and_rejects_a_pinned_x64_dependency(bool mixed)
    {
        var root = Directory.CreateTempSubdirectory("haven-preview-arm-closure-").FullName;
        try
        {
            var runtime = Path.Combine(root, "runtime");
            var bundle = Path.Combine(runtime, "inference", "bundle");
            var models = Path.Combine(root, "models");
            var (assets, _, _) = await ScaffoldClosure(runtime, ArmAarch64ElfMachine);
            var manifest = JsonSerializer.SerializeToNode(CanonicalManifest(assets))!;
            var receipt = JsonSerializer.SerializeToNode(CanonicalReceipt(assets))!;
            if (mixed)
            {
                var path = Path.Combine(runtime, "llama.cpp", "libggml-base.so.0");
                WriteElf(path, X86_64ElfMachine);
                var asset = manifest["assets"]![1]!;
                asset["expectedSha256"] = ComputeSha256(path);
                receipt["admittedAssets"]![asset["logical"]!.GetValue<string>()]!["sha256"] = ComputeSha256(path);
            }
            await JsonFileAsync(Path.Combine(bundle, "bundle-manifest.json"), manifest);
            await JsonFileAsync(Path.Combine(bundle, "bundle-receipt.json"), receipt);
            var bytes = new byte[64];
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await ScaffoldModel(models, "arm-model", hash, bytes);
            await JsonFileAsync(Path.Combine(models, "manifests", "arm-model.json"), ModelManifest("arm-model", hash, bytes.Length));
            var arm = new LlamaCppPreviewBootstrap(bundle, runtime, models);
            var discovery = await arm.DiscoverAsync(CancellationToken.None);
            Assert.Equal(!mixed, discovery.Available);
            if (mixed) Assert.Contains(discovery.Deficiencies, item => item.Contains("e_machine 62", StringComparison.Ordinal));
            var x64 = new LlamaCppPreviewBootstrap(bundle, runtime, models, target: LlamaCppPreviewTarget.LinuxX64);
            Assert.False((await x64.DiscoverAsync(CancellationToken.None)).Available);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Discovery_reports_actionable_degraded_when_no_canonical_bundle_or_model_exists()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bootstrap = NewBootstrap(root);
            var discovery = await bootstrap.DiscoverAsync(CancellationToken.None);

            Assert.False(discovery.Available);
            Assert.Contains(discovery.Deficiencies, d => d.Contains("bundle-manifest.json", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(discovery.Deficiencies, d => d.Contains("model store", StringComparison.OrdinalIgnoreCase));

            var endpoint = bootstrap.BuildEndpointOptions(discovery);
            Assert.False(endpoint.Enabled);
            var runtime = bootstrap.BuildRuntimeOptions(discovery);
            Assert.False(runtime.Enabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Discovery_verifies_canonical_bundle_closure_and_model_store_and_builds_enabled_options()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-assets-" + Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(root, "usr/lib/haven");
        var bundleDir = Path.Combine(runtimeRoot, "inference", "bundle");
        var modelRoot = Path.Combine(root, "haven/models");
        Directory.CreateDirectory(bundleDir);
        try
        {
            // --- Full staged closure: llama-server + 2 native deps + LICENSE, all ARM64 ELF/notice ---
            var (manifestAssets, serverPath, serverSha) = await ScaffoldClosure(runtimeRoot, ArmAarch64ElfMachine);
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-manifest.json"),
                CanonicalManifest(manifestAssets));
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-receipt.json"),
                CanonicalReceipt(manifestAssets));

            // --- Canonical per-user model store (content-addressed) ---
            var blobBytes = new byte[256];
            new Random(42).NextBytes(blobBytes);
            var modelSha = Convert.ToHexString(SHA256.HashData(blobBytes)).ToLowerInvariant();
            var blobPath = Path.Combine(modelRoot, "blobs", "sha256", modelSha[..2], modelSha + ".gguf");
            Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);
            await File.WriteAllBytesAsync(blobPath, blobBytes);
            await JsonFileAsync(Path.Combine(modelRoot, "manifests", "qwen3-0.6b-q6-k.json"),
                ModelManifest("qwen3-0.6b-q6-k", modelSha, blobBytes.Length));

            var bootstrap = NewBootstrap(root, runtimeRoot: runtimeRoot, bundleDir: bundleDir, modelRoot: modelRoot);
            var discovery = await bootstrap.DiscoverAsync(CancellationToken.None);
            Assert.True(discovery.Available, string.Join("; ", discovery.Deficiencies));
            Assert.Equal(serverPath, discovery.ExecutablePath);
            Assert.Equal(serverSha, discovery.ExpectedExecutableSha256);
            Assert.Equal(blobPath, discovery.ModelPath);
            Assert.Equal(modelSha, discovery.ExpectedModelSha256);
            Assert.Equal("qwen3-0.6b-q6-k", discovery.ModelAlias);
            Assert.Equal("MIT", discovery.License);
            Assert.Equal(2, discovery.ElfClass);
            Assert.Equal(ArmAarch64ElfMachine, discovery.ElfMachine);

            var endpoint = bootstrap.BuildEndpointOptions(discovery);
            Assert.True(endpoint.Enabled);
            Assert.EndsWith("llamacpp-worker.sock", endpoint.SocketPath, StringComparison.Ordinal);
            Assert.DoesNotContain("inference.sock", endpoint.SocketPath, StringComparison.Ordinal);

            var runtime = bootstrap.BuildRuntimeOptions(discovery);
            Assert.True(runtime.Enabled);
            Assert.Equal(endpoint.SocketPath, runtime.SocketPath);
            Assert.True(runtime.AlwaysLoaded);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Discovery_refuses_an_x86_64_relabel_even_when_hashes_match()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-x86-" + Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(root, "usr/lib/haven");
        var bundleDir = Path.Combine(runtimeRoot, "inference", "bundle");
        var modelRoot = Path.Combine(root, "haven/models");
        Directory.CreateDirectory(bundleDir);
        try
        {
            // The entire x86_64 closure is relabelled as an ARM bundle; real ELF probes must refuse it.
            var (manifestAssets, _, serverSha) = await ScaffoldClosure(runtimeRoot, X86_64ElfMachine);
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-manifest.json"), CanonicalManifest(manifestAssets));
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-receipt.json"), CanonicalReceipt(manifestAssets));

            var blobBytes = new byte[64];
            new Random(7).NextBytes(blobBytes);
            var modelSha = Convert.ToHexString(SHA256.HashData(blobBytes)).ToLowerInvariant();
            await ScaffoldModel(modelRoot, "m", modelSha, blobBytes);
            await JsonFileAsync(Path.Combine(modelRoot, "manifests", "m.json"), ModelManifest("m", modelSha, blobBytes.Length));

            var bootstrap = NewBootstrap(root, runtimeRoot: runtimeRoot, bundleDir: bundleDir, modelRoot: modelRoot);
            var discovery = await bootstrap.DiscoverAsync(CancellationToken.None);
            Assert.False(discovery.Available);
            Assert.Contains(discovery.Deficiencies, d =>
                d.Contains("e_machine", StringComparison.OrdinalIgnoreCase)
                && d.Contains("183", StringComparison.Ordinal));
            Assert.False(bootstrap.BuildEndpointOptions(discovery).Enabled);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Discovery_rejects_invalid_receipt()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-badreceipt-" + Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(root, "usr/lib/haven");
        var bundleDir = Path.Combine(runtimeRoot, "inference", "bundle");
        var modelRoot = Path.Combine(root, "haven/models");
        Directory.CreateDirectory(bundleDir);
        try
        {
            var (manifestAssets, _, _) = await ScaffoldClosure(runtimeRoot, ArmAarch64ElfMachine);
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-manifest.json"), CanonicalManifest(manifestAssets));
            await File.WriteAllTextAsync(Path.Combine(bundleDir, "bundle-receipt.json"), "{ not valid json ");

            var blobBytes = new byte[64];
            new Random(3).NextBytes(blobBytes);
            var modelSha = Convert.ToHexString(SHA256.HashData(blobBytes)).ToLowerInvariant();
            await ScaffoldModel(modelRoot, "m", modelSha, blobBytes);
            await JsonFileAsync(Path.Combine(modelRoot, "manifests", "m.json"), ModelManifest("m", modelSha, blobBytes.Length));

            var bootstrap = NewBootstrap(root, runtimeRoot: runtimeRoot, bundleDir: bundleDir, modelRoot: modelRoot);
            var discovery = await bootstrap.DiscoverAsync(CancellationToken.None);
            Assert.False(discovery.Available);
            Assert.Contains(discovery.Deficiencies, d => d.Contains("receipt", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Discovery_rejects_missing_declared_dependency()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-missingdep-" + Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(root, "usr/lib/haven");
        var bundleDir = Path.Combine(runtimeRoot, "inference", "bundle");
        var modelRoot = Path.Combine(root, "haven/models");
        Directory.CreateDirectory(bundleDir);
        try
        {
            var (manifestAssets, _, serverSha) = await ScaffoldClosure(runtimeRoot, ArmAarch64ElfMachine);
            var extra = new
            {
                logical = "arm64-libggml-cpu-armv9.2_1.so",
                kind = "runtime.binary",
                engine = "llamacpp",
                platform = new { os = "linux", arch = "aarch64" },
                runtimeFormat = "elf",
                license = "MIT",
                redistribution = "allowed",
                installPath = "llama.cpp/libggml-cpu-armv9.2_1.so",
                expectedSha256 = new string('0', 64),
                expectedSize = 1L,
                capabilities = new[] { "runtime-dependency" },
                required = true
            };
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-manifest.json"),
                CanonicalManifest(manifestAssets.Append(extra).ToArray()));
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-receipt.json"), CanonicalReceipt(manifestAssets));

            var blobBytes = new byte[64];
            new Random(4).NextBytes(blobBytes);
            var modelSha = Convert.ToHexString(SHA256.HashData(blobBytes)).ToLowerInvariant();
            await ScaffoldModel(modelRoot, "m", modelSha, blobBytes);
            await JsonFileAsync(Path.Combine(modelRoot, "manifests", "m.json"), ModelManifest("m", modelSha, blobBytes.Length));

            var bootstrap = NewBootstrap(root, runtimeRoot: runtimeRoot, bundleDir: bundleDir, modelRoot: modelRoot);
            var discovery = await bootstrap.DiscoverAsync(CancellationToken.None);
            Assert.False(discovery.Available);
            Assert.Contains(discovery.Deficiencies, d =>
                d.Contains("absent", StringComparison.OrdinalIgnoreCase)
                && d.Contains("libggml-cpu-armv9.2_1.so", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Fresh_seed_import_adopts_bundled_default_model()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-seed-" + Guid.NewGuid().ToString("N"));
        var modelRoot = Path.Combine(root, "haven/models");
        var seedDir = Path.Combine(root, "seed");
        Directory.CreateDirectory(seedDir);
        try
        {
            var seedBytes = new byte[200];
            new Random(11).NextBytes(seedBytes);
            var seedSha = Convert.ToHexString(SHA256.HashData(seedBytes)).ToLowerInvariant();
            await File.WriteAllBytesAsync(Path.Combine(seedDir, seedSha + ".gguf"), seedBytes);
            var noticesDir = Path.Combine(seedDir, "notices");
            Directory.CreateDirectory(noticesDir);
            await File.WriteAllTextAsync(Path.Combine(noticesDir, "Qwen3-Apache-2.0-LICENSE.txt"), "Apache License 2.0 notice");
            await JsonFileAsync(Path.Combine(seedDir, "qwen3-06b-q6k.json"),
                SeedManifest("qwen3-06b-q6k", seedSha, seedBytes.Length));

            var runner = FakeImportRunner(modelRoot, seedSha, seedBytes.Length, out var invocations);
            var bootstrap = NewBootstrap(root, modelRoot: modelRoot,
                seedDir: seedDir, defaultSeedSha256: seedSha, defaultSeedSize: seedBytes.Length, runner: runner);

            var outcome = await bootstrap.EnsureDefaultModelImportedAsync(CancellationToken.None);
            Assert.True(outcome.IsImported, outcome.Detail);
            Assert.Equal("qwen3-06b-q6k", outcome.ModelId);
            Assert.True(File.Exists(Path.Combine(modelRoot, "manifests", "qwen3-06b-q6k.json")));
            Assert.True(File.Exists(Path.Combine(modelRoot, "blobs", "sha256", seedSha[..2], seedSha + ".gguf")));

            // The canonical modelctl import was invoked with typed, canonical argv.
            Assert.Single(invocations);
            var req = invocations[0];
            Assert.Equal("/usr/bin/python3", req.PythonPath);
            Assert.Equal("/usr/lib/haven/inference/modelctl.py", req.ModelCtlPath);
            Assert.Equal(modelRoot, req.ModelRoot);
            Assert.Contains("haven", req.RuntimeDir, StringComparison.Ordinal);
            Assert.Equal(Path.Combine(seedDir, seedSha + ".gguf"), req.SeedBlobPath);
            Assert.Equal("qwen3-06b-q6k", req.Id);
            Assert.Equal("Qwen3 0.6B Q6_K", req.Name);
            Assert.Equal("Apache-2.0", req.License);
            Assert.Equal("https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/50968a4468ef4233ed78cd7c3de230dd1d61a56b/Qwen3-0.6B-Q6_K.gguf", req.Origin);
            Assert.Equal("chat", req.Capability);
            Assert.Equal("allowed", req.Redistribution);

            // Idempotent + preservation: a second attempt must not invoke modelctl again.
            var second = await bootstrap.EnsureDefaultModelImportedAsync(CancellationToken.None);
            Assert.Equal(SeedImportStatus.SkippedExistingModel, second.Status);
            Assert.Single(invocations);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Fresh_seed_import_preserves_existing_selection()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-seedkeep-" + Guid.NewGuid().ToString("N"));
        var modelRoot = Path.Combine(root, "haven/models");
        var seedDir = Path.Combine(root, "seed");
        Directory.CreateDirectory(seedDir);
        try
        {
            // The user already has a verified model; import must be skipped and untouched.
            var existingBytes = new byte[128];
            new Random(5).NextBytes(existingBytes);
            var existingSha = Convert.ToHexString(SHA256.HashData(existingBytes)).ToLowerInvariant();
            await ScaffoldModel(modelRoot, "user-selected", existingSha, existingBytes);
            await JsonFileAsync(Path.Combine(modelRoot, "manifests", "user-selected.json"),
                ModelManifest("user-selected", existingSha, existingBytes.Length));

            var seedBytes = new byte[200];
            new Random(11).NextBytes(seedBytes);
            var seedSha = Convert.ToHexString(SHA256.HashData(seedBytes)).ToLowerInvariant();
            await File.WriteAllBytesAsync(Path.Combine(seedDir, seedSha + ".gguf"), seedBytes);
            var noticesDir = Path.Combine(seedDir, "notices");
            Directory.CreateDirectory(noticesDir);
            await File.WriteAllTextAsync(Path.Combine(noticesDir, "Qwen3-Apache-2.0-LICENSE.txt"), "notice");
            await JsonFileAsync(Path.Combine(seedDir, "qwen3-06b-q6k.json"),
                SeedManifest("qwen3-06b-q6k", seedSha, seedBytes.Length));

            var runner = RecordingRunner(out var invocations);
            var bootstrap = NewBootstrap(root, modelRoot: modelRoot,
                seedDir: seedDir, defaultSeedSha256: seedSha, defaultSeedSize: seedBytes.Length, runner: runner);

            var outcome = await bootstrap.EnsureDefaultModelImportedAsync(CancellationToken.None);
            Assert.Equal(SeedImportStatus.SkippedExistingModel, outcome.Status);
            Assert.Equal("user-selected", outcome.ModelId);
            Assert.Empty(invocations);
            Assert.False(File.Exists(Path.Combine(modelRoot, "manifests", "qwen3-06b-q6k.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Fresh_seed_import_rejects_invalid_seed()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-seedbad-" + Guid.NewGuid().ToString("N"));
        var modelRoot = Path.Combine(root, "haven/models");
        var seedDir = Path.Combine(root, "seed");
        Directory.CreateDirectory(seedDir);
        try
        {
            var seedBytes = new byte[200];
            new Random(11).NextBytes(seedBytes);
            var seedSha = Convert.ToHexString(SHA256.HashData(seedBytes)).ToLowerInvariant();
            await File.WriteAllBytesAsync(Path.Combine(seedDir, seedSha + ".gguf"), seedBytes);
            var noticesDir = Path.Combine(seedDir, "notices");
            Directory.CreateDirectory(noticesDir);
            await File.WriteAllTextAsync(Path.Combine(noticesDir, "Qwen3-Apache-2.0-LICENSE.txt"), "notice");
            // Declared hash does NOT match the actual seed bytes => must be rejected before modelctl.
            await JsonFileAsync(Path.Combine(seedDir, "qwen3-06b-q6k.json"),
                SeedManifest("qwen3-06b-q6k", new string('f', 64), seedBytes.Length));

            var runner = RecordingRunner(out var invocations);
            var bootstrap = NewBootstrap(root, modelRoot: modelRoot,
                seedDir: seedDir, defaultSeedSha256: seedSha, defaultSeedSize: seedBytes.Length, runner: runner);

            var outcome = await bootstrap.EnsureDefaultModelImportedAsync(CancellationToken.None);
            Assert.Equal(SeedImportStatus.InvalidSeed, outcome.Status);
            Assert.Empty(invocations);
            Assert.False(File.Exists(Path.Combine(modelRoot, "manifests", "qwen3-06b-q6k.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Lifecycle_start_populates_state_and_stays_degraded_without_assets()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bootstrap = NewBootstrap(root);
            var state = new LlamaCppPreviewBootstrapState();
            await using var lifecycle = new LlamaCppPreviewLifecycle(
                bootstrap, state, new FakeProviderConfigurationStore(), new StubServiceProvider());

            Assert.Null(lifecycle.StartStatus);
            await lifecycle.StartIfReadyAsync(CancellationToken.None);

            Assert.False(state.Discovery.Available);
            Assert.False(state.Endpoint.Enabled);
            Assert.False(state.Runtime.Enabled);
            Assert.False(lifecycle.Ready);
            Assert.Null(lifecycle.StartStatus);
            Assert.Null(lifecycle.Failure);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Lifecycle_propagates_cancellation_and_drains()
    {
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-cancel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bootstrap = NewBootstrap(root);
            var state = new LlamaCppPreviewBootstrapState();
            await using var lifecycle = new LlamaCppPreviewLifecycle(
                bootstrap, state, new FakeProviderConfigurationStore(), new StubServiceProvider());

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lifecycle.StartIfReadyAsync(cts.Token));
            Assert.NotNull(lifecycle.Failure);
            Assert.False(lifecycle.Ready);
            // Drain must complete cleanly after a cancelled/failed start.
            await lifecycle.DisposeAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Lifecycle_applies_enabled_verified_options_before_runtime_failure()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-early-" + Guid.NewGuid().ToString("N"));
        var runtimeRoot = Path.Combine(root, "usr/lib/haven");
        var bundleDir = Path.Combine(runtimeRoot, "inference", "bundle");
        var modelRoot = Path.Combine(root, "haven/models");
        Directory.CreateDirectory(bundleDir);
        try
        {
            var (manifestAssets, _, serverSha) = await ScaffoldClosure(runtimeRoot, ArmAarch64ElfMachine);
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-manifest.json"), CanonicalManifest(manifestAssets));
            await JsonFileAsync(Path.Combine(bundleDir, "bundle-receipt.json"), CanonicalReceipt(manifestAssets));

            var blobBytes = new byte[128];
            new Random(9).NextBytes(blobBytes);
            var modelSha = Convert.ToHexString(SHA256.HashData(blobBytes)).ToLowerInvariant();
            await ScaffoldModel(modelRoot, "qwen-selected", modelSha, blobBytes);
            await JsonFileAsync(Path.Combine(modelRoot, "manifests", "qwen-selected.json"),
                ModelManifest("qwen-selected", modelSha, blobBytes.Length));

            var bootstrap = NewBootstrap(root, runtimeRoot: runtimeRoot, bundleDir: bundleDir, modelRoot: modelRoot);
            var state = new LlamaCppPreviewBootstrapState();
            await using var lifecycle = new LlamaCppPreviewLifecycle(
                bootstrap, state, new FakeProviderConfigurationStore(), new StubServiceProvider());

            // The owned child is a fake non-executable ELF, so runtime start will fail/produce a
            // not-running status. This is fine: the invariant under test is that the verified options
            // are applied to state BEFORE runtime outcome, independent of Ready.
            try { await lifecycle.StartIfReadyAsync(CancellationToken.None); }
            catch { /* runtime start failure is expected for the fake executable */ }

            // This is the exact invariant the App ordering fix relies on: the verified endpoint/runtime
            // options are configured on the shared state BEFORE any provider resolution or runtime
            // success, so a provider constructed afterward observes ready configured options (not the
            // initial disabled default). No synthetic ready flag is set because the owned child did not
            // genuinely run (fake non-executable ELF).
            Assert.True(state.Endpoint.Enabled);
            Assert.True(state.Runtime.Enabled);
            Assert.False(lifecycle.Ready);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Same_early_provider_binds_verified_preview_options_and_admission()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-admit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bootstrap = NewBootstrap(root);
            var state = new LlamaCppPreviewBootstrapState();
            var configurations = new FakeProviderConfigurationStore();

            // Resolve the SAME provider BEFORE any verified state is applied, exactly as the registry/
            // authority/retirement infrastructure does during ConfigureServices when it enumerates the
            // provider catalogue and snapshots the initial (disabled) endpoint.
            var provider = new LlamaCppModelProvider(state.Endpoint, configurations, state);
            Assert.False(state.Endpoint.Enabled);

            var verified = new LlamaCppLocalEndpointOptions(
                Enabled: true,
                SocketPath: "/tmp/haven-preview-nonexistent.sock",
                ExpectedExecutableSha256: new string('0', 64),
                ModelPath: "/tmp/haven-preview-nonexistent.gguf",
                ExpectedModelSha256: new string('0', 64),
                ModelAlias: "qwen3-06b-q6k");
            state.Apply(
                new LlamaCppPreviewDiscovery(true, null, null, null, null, null, null, null, null, []),
                verified,
                new LlamaCppRuntimeOptions());

            // Legitimate canonical provider admission under existing ownership (the real bootstrap step
            // that precedes the provider's first actual use). Without this the provider correctly
            // refuses with "disabled in the configuration store" before it even attempts the socket.
            await bootstrap.EnsureProviderAdmissionAsync(configurations, verified, CancellationToken.None);

            // The SAME early provider now consumes verified enabled options AND legitimate admission: a
            // health check proceeds past the disabled branch and attempts the real (absent) socket - a
            // genuine failure distinction - rather than reporting the silent-disabled empty state. This
            // is the exact invariant the App ordering + one-time binding fix relies on for provider
            // resolution that precedes the awaited bootstrap.
            var health = await provider.CheckHealthAsync(CancellationToken.None);
            Assert.False(health.IsHealthy);
            Assert.DoesNotContain("disabled", health.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Same_early_provider_getmodels_deferred_until_verified_enabled_state()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "haven-preview-defer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bootstrap = NewBootstrap(root);
            var state = new LlamaCppPreviewBootstrapState();
            var configurations = new FakeProviderConfigurationStore();
            var provider = new LlamaCppModelProvider(state.Endpoint, configurations, state);

            // Registry/provider enumeration BEFORE the async bootstrap must stay honest: empty, degraded,
            // and crucially NOT pinned to the initial disabled endpoint.
            var preBootstrap = await provider.GetModelsAsync(CancellationToken.None);
            Assert.Empty(preBootstrap);

            var verified = new LlamaCppLocalEndpointOptions(
                Enabled: true,
                SocketPath: "/tmp/haven-preview-nonexistent.sock",
                ExpectedExecutableSha256: new string('0', 64),
                ModelPath: "/tmp/haven-preview-nonexistent.gguf",
                ExpectedModelSha256: new string('0', 64),
                ModelAlias: "qwen3-06b-q6k");
            state.Apply(
                new LlamaCppPreviewDiscovery(true, null, null, null, null, null, null, null, null, []),
                verified,
                new LlamaCppRuntimeOptions());
            await bootstrap.EnsureProviderAdmissionAsync(configurations, verified, CancellationToken.None);

            // The SAME provider now binds once and reaches the real socket identity distinction. With no
            // server listening that is an unavailable catalogue (real connect failure), NOT a silent
            // disabled/empty result.
            await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetModelsAsync(CancellationToken.None));

            // No rebind after a verified binding: even a later disabled state snapshot must not downgrade
            // the already-bound verified provider (rebinding mid-life would mix peer/cache identities).
            state.Apply(
                new LlamaCppPreviewDiscovery(false, null, null, null, null, null, null, null, null, []),
                new LlamaCppLocalEndpointOptions(),
                new LlamaCppRuntimeOptions());
            await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetModelsAsync(CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Explicit_option_provider_is_unaffected_by_preview_state()
    {
        if (!OperatingSystem.IsLinux()) return;
        var bootstrap = new LlamaCppPreviewBootstrap();
        var state = new LlamaCppPreviewBootstrapState();
        var configurations = new FakeProviderConfigurationStore();

        var explicitEnabled = new LlamaCppLocalEndpointOptions(
            Enabled: true,
            SocketPath: "/tmp/haven-explicit-nonexistent.sock",
            ExpectedExecutableSha256: new string('0', 64),
            ModelPath: "/tmp/haven-explicit-nonexistent.gguf",
            ExpectedModelSha256: new string('0', 64),
            ModelAlias: "explicit-alias");
        await bootstrap.EnsureProviderAdmissionAsync(configurations, explicitEnabled, CancellationToken.None);

        // Explicit-option caller passes NO preview state; the shared BootstrapState must not influence it.
        var provider = new LlamaCppModelProvider(explicitEnabled, configurations);

        // A disabled preview state applied later must not disable or rebind the explicit provider.
        state.Apply(
            new LlamaCppPreviewDiscovery(false, null, null, null, null, null, null, null, null, []),
            new LlamaCppLocalEndpointOptions(),
            new LlamaCppRuntimeOptions());

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetModelsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Explicit_user_disabled_provider_is_not_overridden_by_admission()
    {
        if (!OperatingSystem.IsLinux()) return;
        var bootstrap = new LlamaCppPreviewBootstrap();
        var state = new LlamaCppPreviewBootstrapState();
        var configurations = new FakeProviderConfigurationStore();

        // The user explicitly disabled the canonical local provider; admission must respect it.
        await configurations.UpsertAsync(new ProviderConfiguration(
            "llama-cpp", ModelProviderKind.OpenAICompatible, "Local llama.cpp",
            "http://127.0.0.1:18080/v1/", IsEnabled: false, IsLocal: true,
            AllowCloudFallback: false, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow), CancellationToken.None);

        var verified = new LlamaCppLocalEndpointOptions(
            Enabled: true,
            SocketPath: "/tmp/haven-preview-nonexistent.sock",
            ExpectedExecutableSha256: new string('0', 64),
            ModelPath: "/tmp/haven-preview-nonexistent.gguf",
            ExpectedModelSha256: new string('0', 64),
            ModelAlias: "qwen3-06b-q6k");
        state.Apply(
            new LlamaCppPreviewDiscovery(true, null, null, null, null, null, null, null, null, []),
            verified,
            new LlamaCppRuntimeOptions());

        // Admission never overrides an existing user configuration (including a disabled one).
        await bootstrap.EnsureProviderAdmissionAsync(configurations, verified, CancellationToken.None);
        var stored = await configurations.GetAsync("llama-cpp", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.False(stored!.IsEnabled);

        var provider = new LlamaCppModelProvider(state.Endpoint, configurations, state);
        var health = await provider.CheckHealthAsync(CancellationToken.None);
        Assert.False(health.IsHealthy);
        Assert.Contains("disabled", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Same_early_provider_stays_disabled_when_no_verified_state()
    {
        if (!OperatingSystem.IsLinux()) return;
        var state = new LlamaCppPreviewBootstrapState();
        var provider = new LlamaCppModelProvider(state.Endpoint, new FakeProviderConfigurationStore(), state);
        Assert.False(state.Endpoint.Enabled);
        var health = await provider.CheckHealthAsync(CancellationToken.None);
        Assert.False(health.IsHealthy);
        Assert.Contains("disabled", health.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bundled_preview_qwen_profile_selects_nonthinking_template_default()
    {
        if (!OperatingSystem.IsLinux()) return;
        var bootstrap = NewBootstrap(Path.Combine(Path.GetTempPath(), "haven-preview-profile-" + Guid.NewGuid().ToString("N")));

        // The genuine bundled-preview Qwen model: BOTH canonical id and exact verified pinned GGUF
        // SHA-256 must match for the non-thinking default (--jinja, --reasoning off).
        var qwenDiscovery = new LlamaCppPreviewDiscovery(
            true, "/usr/lib/haven/llama.cpp/llama-server", new string('0', 64),
            "/home/user/haven/models/qwen3-06b-q6k.gguf",
            LlamaCppPreviewBootstrap.DefaultSeedModelSha256,
            "qwen3-06b-q6k", "Apache-2.0", 2, 183, []);
        var qwenOptions = bootstrap.BuildRuntimeOptions(qwenDiscovery);
        Assert.True(qwenOptions.Enabled);
        Assert.True(qwenOptions.UseJinja);
        Assert.Equal(LlamaCppReasoningMode.Off, qwenOptions.Reasoning);

        // Same canonical ID with a DIFFERENT actual model hash must NOT gain the bundled profile.
        var otherBytesDiscovery = new LlamaCppPreviewDiscovery(
            true, "/usr/lib/haven/llama.cpp/llama-server", new string('0', 64),
            "/home/user/haven/models/qwen3-06b-q6k.gguf", new string('1', 64),
            "qwen3-06b-q6k", "Apache-2.0", 2, 183, []);
        var otherBytesOptions = bootstrap.BuildRuntimeOptions(otherBytesDiscovery);
        Assert.True(otherBytesOptions.Enabled);
        Assert.Null(otherBytesOptions.UseJinja);
        Assert.Equal(LlamaCppReasoningMode.Unset, otherBytesOptions.Reasoning);

        // Same exact pinned hash under a DIFFERENT alias must also NOT gain the bundled profile.
        var otherAliasDiscovery = new LlamaCppPreviewDiscovery(
            true, "/usr/lib/haven/llama.cpp/llama-server", new string('0', 64),
            "/home/user/haven/models/renamed.gguf",
            LlamaCppPreviewBootstrap.DefaultSeedModelSha256,
            "renamed-model", "Apache-2.0", 2, 183, []);
        var otherAliasOptions = bootstrap.BuildRuntimeOptions(otherAliasDiscovery);
        Assert.True(otherAliasOptions.Enabled);
        Assert.Null(otherAliasOptions.UseJinja);
        Assert.Equal(LlamaCppReasoningMode.Unset, otherAliasOptions.Reasoning);
    }

    private static LlamaCppPreviewBootstrap NewBootstrap(string root,
        string? runtimeRoot = null, string? bundleDir = null, string? modelRoot = null,
        string? seedDir = null, string? defaultSeedSha256 = null, long? defaultSeedSize = null,
        ModelCtlRunner? runner = null) =>
        new(bundleDir ?? Path.Combine(root, "bundle"),
            runtimeRoot ?? Path.Combine(root, "runtime"),
            modelRoot ?? Path.Combine(root, "models"),
            null, seedDir, null, defaultSeedSha256, defaultSeedSize, runner);

    private static object CanonicalManifest(object[] assets, string architecture = "aarch64") => new
    {
        manifestSchemaVersion = 1,
        bundleId = "dulche-llamacpp-runtime",
        bundleVersion = "0.4.0-b10809-linux-arm64",
        purpose = "bundled-dulche-runtime-model-assets",
        host = new { os = "linux", arch = architecture },
        state = "ready",
        admission = new { policy = "explicit-supplied-files", zeroHashAdmitted = false, inventedPins = 0, networkUsed = false },
        assets,
        missingAssets = Array.Empty<object>(),
        engineState = new { llamacpp = new { present = true, required = false } }
    };

    private static object CanonicalReceipt(object[] declared) => new
    {
        receiptSchemaVersion = 1,
        packer = "haven-dulche-bundle",
        packerVersion = "b10809",
        bundleId = "dulche-llamacpp-runtime",
        bundleVersion = "0.4.0-b10809-linux-arm64",
        manifestSha256 = new string('a', 64),
        state = "ready",
        assetCount = declared.Length,
        admittedAssets = declared.Select(asset => JsonSerializer.SerializeToElement(asset)).ToDictionary(
            asset => asset.GetProperty("logical").GetString()!,
            asset => new { sha256 = asset.GetProperty("expectedSha256").GetString(), size = asset.GetProperty("expectedSize").GetInt64() }),
        missingRequiredCount = 0,
        engineState = new { llamacpp = new { present = true, required = false } },
        deterministic = true
    };

    private static object ModelManifest(string id, string sha, int size) => new
    {
        id,
        displayName = id,
        sha256 = sha,
        size,
        blob = $"{sha[..2]}/{sha}.gguf",
        license = "Apache-2.0",
        source = "https://huggingface.co/unsloth/Qwen3-0.6B-GGUF",
        ggufVersions = new[] { 2, 3 },
        capabilities = new[] { "chat" },
        redistribution = "allowed"
    };

    private static object SeedManifest(string id, string sha, int size) => new
    {
        schemaVersion = 1,
        id,
        displayName = "Qwen3 0.6B Q6_K",
        sha256 = sha,
        size,
        blob = $"{sha[..2]}/{sha}.gguf",
        license = "Apache-2.0",
        source = "https://huggingface.co/unsloth/Qwen3-0.6B-GGUF/resolve/50968a4468ef4233ed78cd7c3de230dd1d61a56b/Qwen3-0.6B-Q6_K.gguf",
        ggufVersions = new[] { 3 },
        capabilities = new[] { "chat" },
        redistribution = "allowed"
    };

    private static ModelCtlRunner FakeImportRunner(string modelRoot, string seedSha, int size, out List<ModelCtlRequest> invocations)
    {
        var calls = new List<ModelCtlRequest>();
        invocations = calls;
        return (request, token) =>
        {
            calls.Add(request);
            // Mirror the canonical modelctl effect so store post-conditions can be asserted.
            var blobDir = Path.Combine(modelRoot, "blobs", "sha256", seedSha[..2]);
            Directory.CreateDirectory(blobDir);
            File.Copy(request.SeedBlobPath, Path.Combine(blobDir, seedSha + ".gguf"), overwrite: false);
            var manifestsDir = Path.Combine(modelRoot, "manifests");
            Directory.CreateDirectory(manifestsDir);
            var manifest = new
            {
                id = request.Id,
                displayName = request.Name,
                sha256 = seedSha,
                size,
                blob = $"{seedSha[..2]}/{seedSha}.gguf",
                license = request.License,
                source = request.Origin,
                ggufVersions = new[] { 2, 3 },
                capabilities = new[] { request.Capability },
                redistribution = request.Redistribution
            };
            File.WriteAllText(Path.Combine(manifestsDir, request.Id + ".json"), JsonSerializer.Serialize(manifest));
            return Task.FromResult(new ModelCtlResult(0, "imported", ""));
        };
    }

    private static ModelCtlRunner RecordingRunner(out List<ModelCtlRequest> invocations)
    {
        var calls = new List<ModelCtlRequest>();
        invocations = calls;
        return (request, token) =>
        {
            calls.Add(request);
            return Task.FromResult(new ModelCtlResult(0, "", ""));
        };
    }

    private static async Task<(object[] Assets, string ServerPath, string ServerSha)> ScaffoldClosure(string runtimeRoot, int machine)
    {
        var closureDir = Path.Combine(runtimeRoot, "llama.cpp");
        Directory.CreateDirectory(closureDir);

        var serverPath = Path.Combine(closureDir, "llama-server");
        var dep1Path = Path.Combine(closureDir, "libggml-base.so.0");
        var dep2Path = Path.Combine(closureDir, "libggml-cpu-armv8.0_1.so");
        var licensePath = Path.Combine(closureDir, "LICENSE");

        WriteElf(serverPath, machine);
        WriteElf(dep1Path, machine);
        WriteElf(dep2Path, machine);
        await File.WriteAllTextAsync(licensePath, "MIT license notice");
        var serverSha = ComputeSha256(serverPath);

        object[] assets =
        [
            BinaryAsset((machine == X86_64ElfMachine ? "x64-" : "arm64-") + "llama-server", "llama.cpp/llama-server", serverPath, machine, ["runtime"]),
            BinaryAsset((machine == X86_64ElfMachine ? "x64-" : "arm64-") + "libggml-base.so", "llama.cpp/libggml-base.so.0", dep1Path, machine, ["runtime-dependency"]),
            BinaryAsset((machine == X86_64ElfMachine ? "x64-" : "arm64-") + "libggml-cpu", "llama.cpp/libggml-cpu-armv8.0_1.so", dep2Path, machine, ["runtime-dependency"]),
            new
            {
                logical = "llamacpp-MIT-notice",
                kind = "runtime.bundle",
                engine = "llamacpp",
                platform = new { os = "linux", arch = machine == X86_64ElfMachine ? "x86_64" : "aarch64" },
                runtimeFormat = "opaque",
                source = "https://raw.githubusercontent.com/ggml-org/llama.cpp/5266f24da75dc449bd56cbed7addb9c8e4a6a73e/LICENSE",
                license = "MIT",
                path = "LICENSE",
                installPath = "llama.cpp/LICENSE",
                expectedSha256 = ComputeSha256(licensePath),
                expectedSize = new FileInfo(licensePath).Length,
                capabilities = new[] { "license-notice" },
                redistribution = "allowed",
                required = true
            }
        ];
        return (assets, serverPath, serverSha);
    }

    private static object BinaryAsset(string logical, string installPath, string filePath, int machine, string[] capabilities)
    {
        return new
        {
            logical,
            kind = "runtime.binary",
            engine = "llamacpp",
            platform = new { os = "linux", arch = machine == X86_64ElfMachine ? "x86_64" : "aarch64" },
            runtimeFormat = "elf",
            source = "https://github.com/ggml-org/llama.cpp/releases/tag/b10809",
            license = "MIT",
            path = Path.GetFileName(filePath),
            installPath,
            expectedSha256 = ComputeSha256(filePath),
            expectedSize = new FileInfo(filePath).Length,
            capabilities,
            format = new { format = "elf", machine, osabi = "linux", structurallyComplete = true },
            redistribution = "allowed",
            admitted = true,
            state = "present",
            required = true
        };
    }

    private static async Task ScaffoldModel(string modelRoot, string id, string sha, byte[] bytes)
    {
        var blobPath = Path.Combine(modelRoot, "blobs", "sha256", sha[..2], sha + ".gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(blobPath)!);
        await File.WriteAllBytesAsync(blobPath, bytes);
    }

    private static async Task JsonFileAsync(string path, object value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value);
    }

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>Writes a minimal valid ELF64 little-endian header with the requested e_machine.</summary>
    private static void WriteElf(string path, int machine)
    {
        var header = new byte[64];
        header[0] = 0x7f; header[1] = (byte)'E'; header[2] = (byte)'L'; header[3] = (byte)'F';
        header[4] = 2; // ELFCLASS64
        header[5] = 1; // little-endian
        header[6] = 1; // EV_CURRENT
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(16), 2); // ET_EXEC
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(18), (ushort)machine); // e_machine
        File.WriteAllBytes(path, header);
    }

    private sealed class FakeProviderConfigurationStore : IProviderConfigurationStore
    {
        private readonly Dictionary<string, ProviderConfiguration> _store = new(StringComparer.OrdinalIgnoreCase);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<ProviderConfiguration>)_store.Values.ToArray());
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken ct) =>
            Task.FromResult(_store.TryGetValue(providerId, out var cfg) ? cfg : null);
        public Task UpsertAsync(ProviderConfiguration configuration, CancellationToken ct)
        {
            _store[configuration.Id] = configuration;
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string providerId, CancellationToken ct)
        {
            _store.Remove(providerId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
