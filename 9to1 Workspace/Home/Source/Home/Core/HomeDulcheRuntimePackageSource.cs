using System.Security.Cryptography;
using System.Text;

namespace HavenOS.Home.Core;

/// <summary>Resolves the packaged Dulche runtime on the current platform. It performs real package
/// existence probes and, when a pinned executable SHA-256 is supplied, actually re-hashes the
/// backend executable to verify it. Without a genuine pin the runtime is Installed but NOT Verified:
/// missing artifact metadata is a truthful blocked axis, never Ready.</summary>
public sealed class HomeDulcheLinuxRuntimePackageSource : IHomeDulcheRuntimePackageSource
{
    public const string PlatformId = "linux";
    public const string LlamaServerPath = "/usr/lib/haven/llama.cpp/llama-server";
    public const string BrokerPath = "/usr/lib/haven/inference/broker.py";
    public const string ModelCtlPath = "/usr/bin/haven-modelctl";

    private readonly string? _expectedExecutableSha256;

    public HomeDulcheLinuxRuntimePackageSource(string? expectedExecutableSha256 = null)
    {
        if (expectedExecutableSha256 is not null && !HomeDulcheHash.IsValidSha256Hex(expectedExecutableSha256))
            throw new ArgumentException("The pinned runtime executable SHA-256 must be 64 lowercase hex digits.", nameof(expectedExecutableSha256));
        _expectedExecutableSha256 = expectedExecutableSha256;
    }

    public async ValueTask<HomeDulchePackageResult> ProbeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var deficiencies = new List<string>();

        var server = new FileInfo(LlamaServerPath);
        var broker = new FileInfo(BrokerPath);
        var modelCtl = new FileInfo(ModelCtlPath);
        var installed = server.Exists && server.Length > 0
            && broker.Exists && broker.Length > 0
            && modelCtl.Exists && modelCtl.Length > 0;
        if (!installed)
        {
            if (!server.Exists || server.Length == 0)
                deficiencies.Add($"The packaged llama.cpp runtime executable is not installed at '{LlamaServerPath}'.");
            if (!broker.Exists)
                deficiencies.Add($"The packaged inference broker is not installed at '{BrokerPath}'.");
            if (!modelCtl.Exists)
                deficiencies.Add($"The packaged model manager is not installed at '{ModelCtlPath}'.");
            return new(false, false, null, null, deficiencies);
        }

        var actual = await ComputeSha256Async(LlamaServerPath, cancellationToken).ConfigureAwait(false);
        var verified = _expectedExecutableSha256 is not null
            && string.Equals(actual, _expectedExecutableSha256, StringComparison.Ordinal);
        if (!verified)
            deficiencies.Add(_expectedExecutableSha256 is null
                ? "The packaged runtime executable has no verified SHA-256 pin; runtime verification is blocked."
                : "The packaged runtime executable hash does not match its pinned SHA-256.");

        return new(true, verified, LlamaServerPath, actual, deficiencies);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }
}

/// <summary>Explicit unsupported-platform runtime package source. An absent platform adapter stays
/// unsupported; it never fakes a package or a Ready state.</summary>
public sealed class HomeDulcheUnsupportedRuntimePackageSource(string platform) : IHomeDulcheRuntimePackageSource
{
    public string Platform { get; } = platform;
    public ValueTask<HomeDulchePackageResult> ProbeAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new HomeDulchePackageResult(false, false, null, null,
            [$"No packaged Dulche runtime adapter exists for platform '{Platform}'."]));
}
