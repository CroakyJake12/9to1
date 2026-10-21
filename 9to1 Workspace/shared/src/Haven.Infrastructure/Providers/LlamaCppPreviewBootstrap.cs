using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>
/// Result of discovering/validating the staged Linux ARM64 Dulche preview assets against the
/// canonical bundle manifest/receipt and the canonical per-user modelctl store. A field is only
/// reported when actually read and verified; every missing/mismatched prerequisite is surfaced as an
/// actionable deficiency, never hidden.
/// </summary>
public sealed record LlamaCppPreviewDiscovery(
    bool Available,
    string? ExecutablePath,
    string? ExpectedExecutableSha256,
    string? ModelPath,
    string? ExpectedModelSha256,
    string? ModelAlias,
    string? License,
    int? ElfClass,
    int? ElfMachine,
    IReadOnlyList<string> Deficiencies);

/// <summary>Status of a fresh-user bundled default-model adoption.</summary>
public enum SeedImportStatus
{
    Imported,
    SkippedExistingModel,
    MissingSeed,
    InvalidSeed,
    Unavailable
}

/// <summary>Outcome of <see cref="LlamaCppPreviewBootstrap.EnsureDefaultModelImportedAsync"/>.</summary>
public sealed record SeedImportOutcome(SeedImportStatus Status, string? ModelId, string? Detail)
{
    public bool IsImported => Status == SeedImportStatus.Imported;
}

/// <summary>Canonical modelctl import request (typed argv). Invoked under the actual user via the
/// packaged Python <c>modelctl.py</c>, never a recoded C# copy algorithm.</summary>
public sealed record ModelCtlRequest(
    string PythonPath,
    string ModelCtlPath,
    string ModelRoot,
    string RuntimeDir,
    string SeedBlobPath,
    string Id,
    string Name,
    string License,
    string Origin,
    string Capability,
    string Redistribution);

/// <summary>Outcome of a modelctl subprocess invocation: settled exit code plus captured stdout/stderr.</summary>
public sealed record ModelCtlResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}

/// <summary>Runs the canonical modelctl import subprocess. The default implementation spawns
/// <c>/usr/bin/python3 -B modelctl.py ...</c>; tests may substitute a deterministic fake.</summary>
public delegate Task<ModelCtlResult> ModelCtlRunner(ModelCtlRequest request, CancellationToken token);

/// <summary>
/// App/Home-owned bootstrap for the local chat preview on the owner-confirmed Linux ARM/aarch64
/// target. It reconciles the EXISTING canonical contracts:
/// <list type="bullet">
/// <item><description>Runtime bundle manifest/receipt at <c>/usr/lib/haven/inference/bundle</c>
/// (existing schema; asset fields installPath, expectedSha256/sha256, expectedSize/size, platform,
/// format.machine, license, redistribution, admitted/state, required) — NOT an invented
/// runtime.lock.json/flat model-manifest.</description></item>
/// <item><description>Canonical per-user model store <c>$XDG_DATA_HOME/haven/models</c> with
/// <c>manifests/&lt;id&gt;.json</c> content-addressed blobs (existing modelctl contract) — NOT a
/// flat <c>model-manifest.json</c> or CI test-model alias.</description></item>
/// </list>
/// It performs real existence/size/SHA-256 and ELF-header probes and never fabricates a staged path,
/// a Ready state or a supported platform. The verified-peer path is Linux AF_UNIX on the RAW WORKER
/// socket (<c>$XDG_RUNTIME_DIR/haven/llamacpp-worker.sock</c>); the broker control socket
/// (<c>inference.sock</c>) is never used as a raw server transport. On a fresh user (empty model
/// store) the bundled default model is adopted through the canonical modelctl-style import contract
/// (verified seed → content-addressed blob → atomic manifest), as the actual user.
/// </summary>
public sealed class LlamaCppPreviewBootstrap
{
    public const string BundleDir = "/usr/lib/haven/inference/bundle";
    public const string BundleManifestFileName = "bundle-manifest.json";
    public const string BundleReceiptFileName = "bundle-receipt.json";
    public const string RuntimeRoot = "/usr/lib/haven";
    public const string LlamaServerRelativeInstallPath = "llama.cpp/llama-server";
    public const string LlamaServerAbsolutePath = RuntimeRoot + "/" + LlamaServerRelativeInstallPath;
    public const int ArmAarch64ElfMachine = 183;
    public const int ElfClass64 = 2;

    public const string ModelRootRelative = "haven/models";
    public const string WorkerSocketRelative = "haven/llamacpp-worker.sock";
    public const string BrokerSocketRelative = "haven/inference.sock";
    public const string BundleClosureRelativeDir = "llama.cpp";

    // Bundled default model (owner-pinned Qwen3 0.6B Q6_K) used to seed a fresh user store.
    public const string DefaultSeedDir = "/usr/share/haven/default-models";
    public const string DefaultSeedModelId = "qwen3-06b-q6k";
    public const string DefaultSeedModelSha256 = "1c15a88244c9e852516e5ba0e394fc97f2182f1f2757413fb99fe6a9214c033b";
    public const long DefaultSeedModelSize = 495107776;
    public const string SeedNoticesRelativeDir = "notices";
    public const string SeedLicenseNoticeFileName = "Qwen3-Apache-2.0-LICENSE.txt";
    public const string LlamaCppModelProviderId = "llama-cpp";
    public const string ModelCtlPythonPath = "/usr/bin/python3";
    public const string ModelCtlScriptPath = "/usr/lib/haven/inference/modelctl.py";

    private readonly string _bundleDir;
    private readonly string _runtimeRoot;
    private readonly string _modelRoot;
    private readonly string? _preferredModelId;
    private readonly string _seedDir;
    private readonly string _defaultSeedId;
    private readonly string _defaultSeedSha256;
    private readonly long _defaultSeedSize;
    private readonly ModelCtlRunner? _modelCtlRunner;

    public LlamaCppPreviewBootstrap(
        string? bundleDir = null,
        string? runtimeRoot = null,
        string? modelRoot = null,
        string? preferredModelId = null,
        string? seedDir = null,
        string? defaultSeedId = null,
        string? defaultSeedSha256 = null,
        long? defaultSeedSize = null,
        ModelCtlRunner? modelCtlRunner = null)
    {
        _bundleDir = bundleDir ?? BundleDir;
        _runtimeRoot = runtimeRoot ?? RuntimeRoot;
        _modelRoot = modelRoot ?? DefaultModelRoot();
        _preferredModelId = preferredModelId;
        _seedDir = seedDir ?? DefaultSeedDir;
        _defaultSeedId = defaultSeedId ?? DefaultSeedModelId;
        _defaultSeedSha256 = defaultSeedSha256 ?? DefaultSeedModelSha256;
        _defaultSeedSize = defaultSeedSize ?? DefaultSeedModelSize;
        _modelCtlRunner = modelCtlRunner;
    }

    public bool IsLinux => OperatingSystem.IsLinux();
    public string ModelRoot => _modelRoot;
    public string BundleDirValue => _bundleDir;
    public string RuntimeRootValue => _runtimeRoot;

    /// <summary>Raw owned worker transport socket (NOT the broker control socket).</summary>
    public string DefaultWorkerSocketPath
    {
        get
        {
            var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (!string.IsNullOrWhiteSpace(runtimeDir))
            {
                var candidate = Path.Combine(runtimeDir, WorkerSocketRelative);
                if (Path.IsPathFullyQualified(candidate)) return candidate;
            }
            return Path.Combine(Path.GetTempPath(), "haven-llamacpp-worker.sock");
        }
    }

    /// <summary>Discovers and verifies the full staged bundle closure and model store. Real file probes only.</summary>
    public async Task<LlamaCppPreviewDiscovery> DiscoverAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deficiencies = new List<string>();

        if (!IsLinux)
        {
            deficiencies.Add("The verified local peer path is Linux AF_UNIX only; the confirmed target is Linux arm64/aarch64.");
            return new LlamaCppPreviewDiscovery(false, null, null, null, null, null, null, null, null, deficiencies);
        }

        string? executablePath = null;
        string? executableSha = null;
        string? license = null;
        int? elfClass = null;
        int? elfMachine = null;

        var bundleManifestPath = Path.Combine(_bundleDir, BundleManifestFileName);
        if (!File.Exists(bundleManifestPath))
        {
            deficiencies.Add($"The canonical bundle manifest is missing at '{bundleManifestPath}'.");
        }
        else
        {
            using var manifestDoc = await ReadObjectAsync(bundleManifestPath, cancellationToken).ConfigureAwait(false);
            if (manifestDoc is null)
            {
                deficiencies.Add("The canonical bundle manifest is not valid JSON.");
            }
            else
            {
                var manifest = manifestDoc.RootElement;
                var host = manifest.TryGetProperty("host", out var hostProp) ? hostProp : default;
                var hostArch = host.TryGetProperty("arch", out var hostArchProp) ? hostArchProp.GetString() : null;
                var hostOs = host.TryGetProperty("os", out var hostOsProp) ? hostOsProp.GetString() : null;
                if (!string.Equals(hostOs, "linux", StringComparison.Ordinal)
                    || !string.Equals(hostArch, "aarch64", StringComparison.Ordinal))
                    deficiencies.Add("The canonical bundle host is not the confirmed linux/aarch64 target.");

                // Canonical script state: only an admitted, fully-present bundle (no missing required
                // assets) is usable. The installed manifest carries state "ready"/"degraded"/"invalid".
                var state = manifest.TryGetProperty("state", out var stateProp) ? stateProp.GetString() : null;
                if (!string.Equals(state, "ready", StringComparison.Ordinal))
                    deficiencies.Add($"The canonical bundle state '{state}' is not ready (missing required assets or invalid).");

                // Full bounded runtime closure: every declared asset must be present, size/SHA/ELF
                // verified, bounded under the runtime root, with no extra/escaping files. The receipt
                // is reconciled (presence + llama-server hash parity).
                var closure = await ValidateRuntimeClosureAsync(manifest, deficiencies, cancellationToken).ConfigureAwait(false);
                executablePath = closure.ExecutablePath;
                executableSha = closure.ExecutableSha256;
                elfClass = closure.ElfClass;
                elfMachine = closure.ElfMachine;
                license = closure.License;
            }
        }

        // ---- Canonical per-user modelctl store ----
        string? modelPath = null;
        string? modelSha = null;
        string? modelAlias = null;
        var (mPath, mSha, mAlias) = await FindEligibleModelAsync(_modelRoot, _preferredModelId, cancellationToken).ConfigureAwait(false);
        modelPath = mPath; modelSha = mSha; modelAlias = mAlias;
        if (modelPath is null)
            deficiencies.Add("No verified eligible model exists in the canonical model store (manifests/<id>.json + content-addressed blob).");

        var available = deficiencies.Count == 0
            && executablePath is not null
            && !string.IsNullOrWhiteSpace(executableSha)
            && modelPath is not null
            && !string.IsNullOrWhiteSpace(modelSha)
            && !string.IsNullOrWhiteSpace(modelAlias)
            && elfClass == ElfClass64
            && elfMachine == ArmAarch64ElfMachine;

        return new LlamaCppPreviewDiscovery(available, executablePath, executableSha, modelPath, modelSha,
            modelAlias, license, elfClass, elfMachine, deficiencies);
    }

    /// <summary>Endpoint options for the AF_UNIX verified peer (raw worker socket). Disabled unless
    /// the discovery is fully verified; unavailable assets stay actionable degraded.</summary>
    public LlamaCppLocalEndpointOptions BuildEndpointOptions(LlamaCppPreviewDiscovery discovery)
    {
        if (!IsLinux || !discovery.Available)
            return new LlamaCppLocalEndpointOptions();
        return new LlamaCppLocalEndpointOptions(
            Enabled: true,
            SocketPath: DefaultWorkerSocketPath,
            ExpectedExecutableSha256: discovery.ExpectedExecutableSha256,
            ModelPath: discovery.ModelPath,
            ExpectedModelSha256: discovery.ExpectedModelSha256,
            ModelAlias: discovery.ModelAlias);
    }

    /// <summary>Runtime options for the owned child. The child binds the raw worker socket (never
    /// the broker control socket) and loads the verified model; only genuinely verified assets enable it.
    /// The compatible non-thinking template default (--jinja --reasoning off) is selected ONLY for the
    /// genuine bundled-preview Qwen profile: BOTH the canonical bundled model id qwen3-06b-q6k AND the
    /// actual verified exact pinned GGUF SHA-256 must match. A different model under the same alias or
    /// the same hash under another alias keeps the default Unset/`Jinja null so arbitrary models and
    /// explicit callers are never forced into the bundled profile.</summary>
    public LlamaCppRuntimeOptions BuildRuntimeOptions(LlamaCppPreviewDiscovery discovery)
    {
        if (!IsLinux || !discovery.Available)
            return new LlamaCppRuntimeOptions();
        var bundledQwen =
            string.Equals(discovery.ModelAlias, DefaultSeedModelId, StringComparison.Ordinal)
            && string.Equals(discovery.ExpectedModelSha256, DefaultSeedModelSha256, StringComparison.OrdinalIgnoreCase);
        return new LlamaCppRuntimeOptions(
            Enabled: true,
            ExecutablePath: discovery.ExecutablePath,
            ModelPath: discovery.ModelPath,
            Port: 18080,
            ContextSize: 32768,
            ParallelRequests: 1,
            AlwaysLoaded: true,
            ModelAlias: discovery.ModelAlias,
            SocketPath: DefaultWorkerSocketPath,
            UseJinja: bundledQwen ? true : null,
            Reasoning: bundledQwen ? LlamaCppReasoningMode.Off : LlamaCppReasoningMode.Unset);
    }

    public static string DefaultModelRoot()
    {
        // Canonical modelctl semantics (modelctl.py): HAVEN_MODEL_ROOT override, else user data home.
        var overrideRoot = Environment.GetEnvironmentVariable("HAVEN_MODEL_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot)) return Path.GetFullPath(overrideRoot);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDir = string.IsNullOrWhiteSpace(dataHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
            : dataHome;
        return Path.Combine(baseDir, ModelRootRelative);
    }

    /// <summary>
    /// Fresh-user adoption of the bundled default model under the canonical modelctl-style import
    /// contract (verify seed → content-addressed blob → atomic manifest), executed as the actual user.
    /// If an eligible model already exists in the user store, the user's selection wins and nothing is
    /// written (no overwrite, never /root HOME, no guessed-user import).
    /// </summary>
    public async Task<SeedImportOutcome> EnsureDefaultModelImportedAsync(CancellationToken cancellationToken)
    {
        if (!IsLinux) return new(SeedImportStatus.Unavailable, null, "The canonical model store seed import is Linux-only.");

        // Preserve an existing selection: never mutate a store that already has a verified model.
        var (p, _, a) = await FindEligibleModelAsync(_modelRoot, _preferredModelId, cancellationToken).ConfigureAwait(false);
        if (p is not null) return new(SeedImportStatus.SkippedExistingModel, a, "An eligible model is already present; existing selection preserved.");

        var seedManifestPath = Path.Combine(_seedDir, _defaultSeedId + ".json");
        if (!File.Exists(seedManifestPath))
            return new(SeedImportStatus.MissingSeed, null, $"Package seed manifest is missing at '{seedManifestPath}'.");

        using var doc = await ReadObjectAsync(seedManifestPath, cancellationToken).ConfigureAwait(false);
        if (doc is null) return new(SeedImportStatus.InvalidSeed, null, "Package seed manifest is not valid JSON.");

        var m = doc.RootElement;
        var id = m.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
        var sha = m.TryGetProperty("sha256", out var shaProp) ? shaProp.GetString() : null;
        var size = m.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var sz) ? sz : (long?)null;
        var license = m.TryGetProperty("license", out var licProp) ? licProp.GetString() : null;

        if (!string.Equals(id, _defaultSeedId, StringComparison.Ordinal))
            return new(SeedImportStatus.InvalidSeed, id, $"Seed manifest id '{id}' != expected '{_defaultSeedId}'.");
        if (!string.Equals(sha, _defaultSeedSha256, StringComparison.Ordinal))
            return new(SeedImportStatus.InvalidSeed, id, "Seed manifest SHA-256 does not match the pinned bundled default.");
        if (size != _defaultSeedSize)
            return new(SeedImportStatus.InvalidSeed, id, "Seed manifest size does not match the pinned bundled default.");
        if (!IsValidSha256Hex(sha) || size is null or <= 0)
            return new(SeedImportStatus.InvalidSeed, id, "Seed manifest lacks a valid size/SHA-256.");
        if (string.IsNullOrWhiteSpace(license))
            return new(SeedImportStatus.InvalidSeed, id, "Seed manifest lacks licence metadata.");

        // Notices must ship with the package seed (Apache-2.0 provenance) before import.
        var noticePath = Path.Combine(_seedDir, SeedNoticesRelativeDir, SeedLicenseNoticeFileName);
        if (!File.Exists(noticePath))
            return new(SeedImportStatus.InvalidSeed, id, $"Package seed licence notice is missing at '{noticePath}'.");

        var seedBlobPath = Path.Combine(_seedDir, _defaultSeedSha256 + ".gguf");
        var seedInfo = new FileInfo(seedBlobPath);
        if (!seedInfo.Exists || (seedInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            return new(SeedImportStatus.InvalidSeed, id, "Seed GGUF is absent or is a symlink (symlink sources are rejected).");
        if (seedInfo.Length != size)
            return new(SeedImportStatus.InvalidSeed, id, $"Seed GGUF size {seedInfo.Length} != declared {size}.");
        var actualSeedSha = await ComputeSha256Async(seedBlobPath, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(actualSeedSha, sha, StringComparison.Ordinal))
            return new(SeedImportStatus.InvalidSeed, id, "Seed GGUF SHA-256 does not match the manifest.");

        var displayName = m.TryGetProperty("displayName", out var dnProp) ? dnProp.GetString() : null;
        var origin = m.TryGetProperty("source", out var srcProp) ? srcProp.GetString() : null;
        var redistribution = m.TryGetProperty("redistribution", out var rdProp) ? rdProp.GetString() : null;
        var capability = m.TryGetProperty("capabilities", out var capProp) && capProp.ValueKind == JsonValueKind.Array
            && capProp.GetArrayLength() > 0 ? capProp[0].GetString() : null;

        // Invoke the canonical modelctl import under the actual user. The canonical tool owns store
        // locking (runtimeDir/model-store.lease + model-ID.lease via flock), staging, content
        // addressing and atomic manifest publish. Settle stdout/stderr/task/failure/cancel and join
        // the process exit before returning; never fire-and-forget, shell, or recode the algorithm.
        var request = new ModelCtlRequest(
            ModelCtlPythonPath,
            ModelCtlScriptPath,
            _modelRoot,
            DefaultRuntimeDir(),
            seedBlobPath,
            id!, displayName ?? _defaultSeedId,
            license!, origin ?? _defaultSeedId,
            capability ?? "chat",
            redistribution ?? "allowed");

        ModelCtlResult result;
        try
        {
            var runner = _modelCtlRunner;
            result = runner is not null
                ? await runner(request, cancellationToken).ConfigureAwait(false)
                : await RunModelCtlAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(SeedImportStatus.InvalidSeed, id, "Seed import failed: " + ex.Message);
        }

        if (result.Success)
            return new(SeedImportStatus.Imported, id, "Bundled default model adopted via canonical modelctl import.");

        return new(SeedImportStatus.InvalidSeed, id,
            $"modelctl import failed (exit {result.ExitCode}): {Quote(result.StdErr ?? result.StdOut)}");
    }

    /// <summary>
    /// Seeds the legitimate provider admission for the bundled-local provider under the EXISTING
    /// configuration ownership. Only the local <c>llama-cpp</c> provider is touched; other providers
    /// are never enabled and no endpoint is invented (transport is the raw worker socket; the stored
    /// endpoint is the observational loopback representative required by the OpenAI-compatible schema).
    /// An existing user configuration — including a disabled one — is respected and left unchanged.
    /// </summary>
    public async Task EnsureProviderAdmissionAsync(
        IProviderConfigurationStore store, LlamaCppLocalEndpointOptions endpoint, CancellationToken cancellationToken)
    {
        if (store is null) return;
        var existing = await store.GetAsync(LlamaCppModelProviderId, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return;

        var representative = $"http://127.0.0.1:{18080}/v1/";
        var configuration = new ProviderConfiguration(
            LlamaCppModelProviderId,
            ModelProviderKind.OpenAICompatible,
            "Local llama.cpp",
            representative,
            IsEnabled: true,
            IsLocal: true,
            AllowCloudFallback: false,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DateTimeOffset.UtcNow);
        await store.UpsertAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Finds the first verified eligible model in the canonical modelctl store. Mirrors the
    /// canonical HomeDulchePackagedModelEvidenceSource contract: manifest &lt;id&gt;.json + canonical
    /// content-address blob &lt;first2&gt;/&lt;sha&gt;.gguf, size and SHA re-verified.</summary>
    private static async Task<(string? PathValue, string? ShaValue, string? AliasValue)> FindEligibleModelAsync(
        string modelRoot, string? preferredId, CancellationToken cancellationToken)
    {
        var manifestsDir = Path.Combine(modelRoot, "manifests");
        if (!Directory.Exists(manifestsDir)) return (null, null, null);
        foreach (var manifestPath in Directory.EnumerateFiles(manifestsDir, "*.json").Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var manifestDoc = await ReadObjectAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            if (manifestDoc is null) continue;
            var manifest = manifestDoc.RootElement;
            var id = manifest.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (preferredId is not null && !string.Equals(id, preferredId, StringComparison.Ordinal)) continue;
            var sha = manifest.TryGetProperty("sha256", out var shaProp) ? shaProp.GetString() : null;
            var size = manifest.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var s) ? s : (long?)null;
            var blob = manifest.TryGetProperty("blob", out var blobProp) ? blobProp.GetString() : null;
            var license = manifest.TryGetProperty("license", out var licProp) ? licProp.GetString() : null;
            if (!IsValidSha256Hex(sha) || size is null or < 8 || string.IsNullOrWhiteSpace(blob)) continue;
            if (string.IsNullOrWhiteSpace(license)) continue;
            var blobPath = CanonicalBlobPath(modelRoot, sha!, blob!);
            if (blobPath is null) continue;
            var info = new FileInfo(blobPath);
            if (!info.Exists || info.Length != size) continue;
            var actual = await ComputeSha256Async(blobPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, sha, StringComparison.Ordinal)) continue;
            return (blobPath, sha, id);
        }
        return (null, null, null);
    }

    private sealed record DeclaredAsset(string Logical, bool IsBinary, bool IsServer, long? Size, string? Sha, bool Required);

    private sealed record ClosureResult(string? ExecutablePath, string? ExecutableSha256, int? ElfClass, int? ElfMachine, string? License);

    /// <summary>Validates the FULL bounded runtime closure from the manifest: every declared asset must
    /// be present, size/SHA-256/ELF(183) verified, bounded under the runtime root, with no extra or
    /// escaping files. Also reconciles the canonical receipt.</summary>
    private async Task<ClosureResult> ValidateRuntimeClosureAsync(
        JsonElement manifest, List<string> deficiencies, CancellationToken cancellationToken)
    {
        string? executablePath = null;
        string? executableSha = null;
        int? elfClass = null;
        int? elfMachine = null;
        string? license = null;

        var runtimePrefix = Path.GetFullPath(_runtimeRoot) + Path.DirectorySeparatorChar;
        var closureRoot = Path.GetFullPath(Path.Combine(_runtimeRoot, BundleClosureRelativeDir));
        var closurePrefix = closureRoot + Path.DirectorySeparatorChar;

        if (!manifest.TryGetProperty("assets", out var assetsProp) || assetsProp.ValueKind != JsonValueKind.Array)
        {
            deficiencies.Add("The canonical bundle manifest declares no assets array.");
            return new(null, null, null, null, null);
        }

        var declared = new Dictionary<string, DeclaredAsset>(StringComparer.Ordinal);
        var closureRelativeDeclared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assetsProp.EnumerateArray())
        {
            var engine = asset.TryGetProperty("engine", out var engineProp) ? engineProp.GetString() : null;
            if (!string.Equals(engine, "llamacpp", StringComparison.Ordinal)) continue;
            var logical = asset.TryGetProperty("logical", out var logicalProp) ? logicalProp.GetString() : null;
            var installPath = asset.TryGetProperty("installPath", out var installProp) ? installProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(installPath)) continue;

            var kind = asset.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() : null;
            var isBinary = string.Equals(kind, "runtime.binary", StringComparison.Ordinal);
            var isServer = isBinary && installPath.EndsWith("llama-server", StringComparison.Ordinal);
            var normalized = installPath.Replace('\\', '/');
            if (!normalized.StartsWith(BundleClosureRelativeDir + "/", StringComparison.Ordinal))
            {
                deficiencies.Add($"Bundle asset '{logical ?? installPath}' installPath escapes the bounded '{BundleClosureRelativeDir}/' closure directory.");
                continue;
            }
            if (isServer)
                license = asset.TryGetProperty("license", out var licProp) ? licProp.GetString() : null;

            declared[normalized] = new DeclaredAsset(logical ?? installPath, isBinary, isServer,
                ReadAssetSize(asset), ReadAssetHex(asset),
                !asset.TryGetProperty("required", out var reqProp) || reqProp.GetBoolean());
            closureRelativeDeclared.Add(normalized.Substring(BundleClosureRelativeDir.Length + 1));
        }

        if (declared.Count == 0)
            deficiencies.Add("The canonical bundle manifest declares no engine assets.");

        foreach (var kv in declared)
        {
            var rel = kv.Key;
            var da = kv.Value;
            cancellationToken.ThrowIfCancellationRequested();
            if (!da.Required) continue;

            var absolute = Path.GetFullPath(Path.Combine(_runtimeRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!absolute.StartsWith(runtimePrefix, StringComparison.Ordinal)
                || !absolute.StartsWith(closurePrefix, StringComparison.Ordinal))
            {
                deficiencies.Add($"Declared closure asset resolves outside the bounded runtime root: '{rel}'.");
                continue;
            }

            var final = absolute;
            try
            {
                var resolved = File.ResolveLinkTarget(absolute, returnFinalTarget: true);
                if (resolved is not null) final = resolved.FullName;
            }
            catch (IOException) { }

            if (!final.StartsWith(runtimePrefix, StringComparison.Ordinal))
            {
                deficiencies.Add($"Declared closure asset '{rel}' symlink-target escapes the runtime root.");
                continue;
            }

            var info = new FileInfo(final);
            if (!info.Exists)
            {
                deficiencies.Add($"Declared closure asset is absent: '{rel}'.");
                continue;
            }

            if (da.Size is not null && info.Length != da.Size)
                deficiencies.Add($"Declared closure asset '{rel}' size {info.Length} != declared {da.Size}.");

            var actualSha = await ComputeSha256Async(final, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(da.Sha) && !string.Equals(actualSha, da.Sha, StringComparison.Ordinal))
                deficiencies.Add($"Declared closure asset '{rel}' SHA-256 does not match the bundle pin.");

            if (da.IsBinary)
            {
                var elf = TryReadElfMachine(final);
                if (elf is null) deficiencies.Add($"Declared runtime asset '{rel}' is not a valid ELF header.");
                else
                {
                    var (k, m) = elf.Value;
                    if (k != ElfClass64) deficiencies.Add($"Declared runtime asset '{rel}' ELF class {k} is not 64-bit.");
                    if (m != ArmAarch64ElfMachine)
                        deficiencies.Add($"Declared runtime asset '{rel}' ELF e_machine {m} is not ARM AArch64 (183); an x86_64 binary is a mismatch even if renamed.");
                    else if (da.IsServer)
                    {
                        executablePath = absolute;
                        executableSha = actualSha;
                        elfClass = k;
                        elfMachine = m;
                    }
                }
            }
        }

        // Reject extra/undeclared closure files, while accounting for symlink backing files of declared paths.
        if (Directory.Exists(closureRoot))
        {
            var accountedResolved = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in declared)
            {
                var absolute = Path.GetFullPath(Path.Combine(_runtimeRoot, kv.Key.Replace('/', Path.DirectorySeparatorChar)));
                try
                {
                    var resolved = File.ResolveLinkTarget(absolute, returnFinalTarget: true);
                    if (resolved is not null && resolved.FullName.StartsWith(closurePrefix, StringComparison.Ordinal))
                    {
                        var rrel = Path.GetRelativePath(closureRoot, resolved.FullName).Replace(Path.DirectorySeparatorChar, '/');
                        accountedResolved.Add(rrel);
                    }
                }
                catch (IOException) { }
            }
            foreach (var file in Directory.EnumerateFiles(closureRoot, "*", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(file);
                var rel = Path.GetRelativePath(closureRoot, full).Replace(Path.DirectorySeparatorChar, '/');
                if (!closureRelativeDeclared.Contains(rel) && !accountedResolved.Contains(rel))
                    deficiencies.Add($"Undeclared closure file present: 'llama.cpp/{rel}'.");
            }
        }
        else
        {
            deficiencies.Add($"The bounded runtime closure directory is missing: '{closureRoot}'.");
        }

        await ReconcileReceiptAsync(ReadBundleId(manifest), executableSha, deficiencies, cancellationToken).ConfigureAwait(false);

        return new(executablePath, executableSha, elfClass, elfMachine, license);
    }

    private async Task ReconcileReceiptAsync(
        string? bundleId, string? serverSha, List<string> deficiencies, CancellationToken cancellationToken)
    {
        var receiptPath = Path.Combine(_bundleDir, BundleReceiptFileName);
        if (!File.Exists(receiptPath))
        {
            deficiencies.Add($"The canonical bundle receipt is missing at '{receiptPath}'.");
            return;
        }
        using var doc = await ReadObjectAsync(receiptPath, cancellationToken).ConfigureAwait(false);
        if (doc is null) { deficiencies.Add("The canonical bundle receipt is not valid JSON."); return; }

        var r = doc.RootElement;
        if (bundleId is not null && r.TryGetProperty("bundleId", out var idProp) && idProp.GetString() is { } rid
            && !string.Equals(rid, bundleId, StringComparison.Ordinal))
            deficiencies.Add($"The canonical bundle receipt bundleId '{rid}' does not match the manifest '{bundleId}'.");

        if (r.TryGetProperty("admittedAssets", out var assets) && assets.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in assets.EnumerateObject())
            {
                if (pair.Value.ValueKind != JsonValueKind.Object) continue;
                var sha = pair.Value.TryGetProperty("sha256", out var sp) ? sp.GetString() : null;
                if (!IsValidSha256Hex(sha)) continue;
                if (pair.Name.EndsWith("llama-server", StringComparison.Ordinal) && serverSha is not null
                    && !string.Equals(sha, serverSha, StringComparison.Ordinal))
                    deficiencies.Add("The canonical bundle receipt llama-server hash does not match the verified installed binary.");
            }
        }
    }

    private static string? ReadBundleId(JsonElement manifest) =>
        manifest.TryGetProperty("bundleId", out var p) ? p.GetString() : null;

    private static string? ReadAssetHex(JsonElement asset) =>
        asset.TryGetProperty("expectedSha256", out var e1) ? e1.GetString()
        : asset.TryGetProperty("sha256", out var e2) ? e2.GetString()
        : null;

    private static long? ReadAssetSize(JsonElement asset)
    {
        if (asset.TryGetProperty("expectedSize", out var s1) && s1.TryGetInt64(out var n1)) return n1;
        if (asset.TryGetProperty("size", out var s2) && s2.TryGetInt64(out var n2)) return n2;
        return null;
    }

    internal static bool IsValidSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string? CanonicalBlobPath(string modelRoot, string sha256, string blob)
    {
        var blobRoot = Path.GetFullPath(Path.Combine(modelRoot, "blobs", "sha256"));
        var expected = Path.Combine(sha256[..2], sha256 + ".gguf").Replace(Path.DirectorySeparatorChar, '/');
        if (!string.Equals(blob, expected, StringComparison.Ordinal)) return null;
        var candidate = Path.GetFullPath(Path.Combine(blobRoot, blob.Replace('/', Path.DirectorySeparatorChar)));
        var rootPrefix = blobRoot + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, StringComparison.Ordinal) ? candidate : null;
    }

    /// <summary>Reads the ELF e_ident class and e_machine from the installed binary. Returns null for
    /// a non-ELF/magically-invalid file so an x86_64 relabel is never mistaken for ARM.</summary>
    internal static (int ElClass, int Machine)? TryReadElfMachine(string path)
    {
        try
        {
            var header = new byte[20];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                       64, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                fs.ReadExactly(header);
            }
            if (header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7f, (byte)'E', (byte)'L', (byte)'F' }) == false)
                return null;
            var elfClass = header[4];
            var data = header[5];
            if (elfClass is not (1 or 2) || data is not (1 or 2)) return null;
            var machine = data == 2
                ? BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(18))
                : BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(18));
            return (elfClass, machine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Parses a JSON file and returns its <see cref="JsonDocument"/> (caller owns disposal)
    /// or null when unreadable. Type-correct counterpart of the prior (invalid) helper.</summary>
    internal static async Task<JsonDocument?> ReadObjectAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    /// <summary>Canonical actual-user runtime directory for modelctl leases. Matches modelctl.py:
    /// HAVEN_RUNTIME_DIR override, else $XDG_RUNTIME_DIR/haven, else /tmp/haven-{actual uid}. Never
    /// a guessed /root or username path.</summary>
    private static string DefaultRuntimeDir()
    {
        var overrideRuntime = Environment.GetEnvironmentVariable("HAVEN_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(overrideRuntime)) return Path.GetFullPath(overrideRuntime);
        var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntime)) return Path.Combine(xdgRuntime, "haven");
        return Path.Combine(Path.GetTempPath(), "haven-" + GetUnixUid());
    }

    /// <summary>Real user id via libc getuid (matches canonical modelctl.py os.getuid()). Returns 0
    /// when the native call is unavailable so the /tmp/haven-{uid} lease fallback never hard-fails.</summary>
    private static uint GetUnixUid()
    {
        try { return NativeGetUid(); }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException or BadImageFormatException) { return 0; }
    }

    [DllImport("libc", EntryPoint = "getuid", ExactSpelling = true)]
    private static extern uint NativeGetUid();

    /// <summary>Spawns the canonical modelctl import subprocess and settles its stdout/stderr/exit.
    /// Typed ArgumentList (no shell). On EVERY post-start success/failure/cancel this retains
    /// ownership: it kills a cancelled child, waits for the actual process exit, and drains both
    /// redirected streams before returning or rethrowing. Never fire-and-forget.</summary>
    internal static async Task<ModelCtlResult> RunModelCtlAsync(ModelCtlRequest request, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.PythonPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-B");
        psi.ArgumentList.Add(request.ModelCtlPath);
        psi.ArgumentList.Add("--model-root");
        psi.ArgumentList.Add(request.ModelRoot);
        psi.ArgumentList.Add("--runtime-dir");
        psi.ArgumentList.Add(request.RuntimeDir);
        psi.ArgumentList.Add("import");
        psi.ArgumentList.Add(request.SeedBlobPath);
        psi.ArgumentList.Add("--id");
        psi.ArgumentList.Add(request.Id);
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add(request.Name);
        psi.ArgumentList.Add("--license");
        psi.ArgumentList.Add(request.License);
        psi.ArgumentList.Add("--origin");
        psi.ArgumentList.Add(request.Origin);
        psi.ArgumentList.Add("--capability");
        psi.ArgumentList.Add(request.Capability);
        psi.ArgumentList.Add("--redistribution");
        psi.ArgumentList.Add(request.Redistribution);

        using var process = new Process { StartInfo = psi };
        Task<string>? stdOutTask = null;
        Task<string>? stdErrTask = null;
        try
        {
            if (!process.Start()) return new(-1, "", "modelctl could not be started.");

            // Read streams independently of the caller token so cancellation still lets us reap the
            // child and both pipes before rethrowing (full owned settlement).
            stdOutTask = process.StandardOutput.ReadToEndAsync();
            stdErrTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                await SettleChildAsync(process, stdOutTask, stdErrTask).ConfigureAwait(false);
                throw;
            }

            await SettleChildAsync(process, stdOutTask, stdErrTask).ConfigureAwait(false);
            return new(process.ExitCode,
                await stdOutTask.ConfigureAwait(false),
                await stdErrTask.ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            TryKill(process);
            if (stdOutTask is not null && stdErrTask is not null)
                await SettleChildAsync(process, stdOutTask, stdErrTask).ConfigureAwait(false);
            return new(-1, "", "modelctl could not start: " + ex.Message);
        }
    }

    /// <summary>Joins the child process exit and drains both redirected stream tasks. Waits for the
    /// real process exit (reaping the child), then awaits both pipe drains, so no redirected pipe
    /// remains open after a success/failure/cancel.</summary>
    private static async Task SettleChildAsync(Process process, Task<string> stdOutTask, Task<string> stdErrTask)
    {
        if (!process.HasExited) await process.WaitForExitAsync().ConfigureAwait(false);
        await Task.WhenAll(stdOutTask, stdErrTask).ConfigureAwait(false);
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: false); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static string Quote(string? value) => value is null ? "(no output)" : value.Trim();
}
