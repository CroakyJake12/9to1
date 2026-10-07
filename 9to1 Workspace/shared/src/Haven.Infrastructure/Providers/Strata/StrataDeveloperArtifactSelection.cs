using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dulche.Runtime;

namespace Haven.Infrastructure;

/// <summary>Untrusted nonsecret configuration only. Selection grants no read, model use,
/// publisher trust, installation status or native readiness. Home must review this FULL tuple.</summary>
public sealed record StrataDeveloperArtifactSelection(ModelIdentity Model, string WorkerRoot,
    StrataInstalledFile WorkerFile, string CheckpointRoot, IReadOnlyList<StrataInstalledFile> CheckpointFiles,
    StrataDeveloperRequirements Requirements, IReadOnlyList<int> CudaDeviceIndices,
    StrataDeveloperSignedBuildInventory? IndependentBuildInventory = null);
public sealed record StrataDeveloperRequirements(string ArtifactFingerprint, string Architecture,
    string Family, string Quantization, IReadOnlyList<string> RequiredFeatures, long MinimumRamBytes,
    long MinimumVramBytes, int ContextTokens, string NativeRegistration);
/// <summary>An independent developer-signed inventory, not a publisher enrolment or a model
/// requirement. The exact public key, payload and signature are included in the manual review.</summary>
public sealed record StrataDeveloperSignedBuildInventory(string DeveloperPublicKeyPem,
    string PayloadBase64, string SignatureBase64);
public sealed record StrataDeveloperBuildInventory(int SchemaVersion, string WorkerSha256,
    string RuntimeBuild, IReadOnlyList<string> Architectures, IReadOnlyList<string> Families,
    IReadOnlyList<string> WeightFormats, IReadOnlyList<string> Quantizations, IReadOnlyList<string> Features,
    IReadOnlyList<string> OperatingSystems, IReadOnlyList<string> CpuArchitectures,
    IReadOnlyList<string> RequiredCpuFeatures, IReadOnlyList<string> NativeRegistrations,
    int MinimumCudaComputeMajor, int MinimumCudaComputeMinor, bool NcclBuilt);

internal sealed record StrataDeveloperArtifactCapture(StrataDeveloperArtifactSelection Selection,
    JsonElement Arguments, string Digest, InferenceModelRequirements Requirements, InferenceEngineSupport? BuildSupport)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    internal static StrataDeveloperArtifactCapture Capture(string configurationJson)
    {
        if (Encoding.UTF8.GetByteCount(configurationJson) is < 1 or > 64 * 1024)
            throw new InvalidDataException("The explicit developer artifact tuple must fit 64 KiB.");
        var bytes = Encoding.UTF8.GetBytes(configurationJson);
        using (var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 })) DemandDistinctProperties(document.RootElement);
        var selected = JsonSerializer.Deserialize<StrataDeveloperArtifactSelection>(bytes, Json)
            ?? throw new InvalidDataException("No explicit developer artifact selection was supplied.");
        if (selected.Model is null || string.IsNullOrWhiteSpace(selected.Model.ProviderId) ||
            string.IsNullOrWhiteSpace(selected.Model.ModelId) || selected.Model.ArtifactRevision is { } revision && string.IsNullOrWhiteSpace(revision) ||
            selected.Model.ProviderId.Length > 128 || selected.Model.ModelId.Length > 512 || selected.Model.ArtifactRevision is { Length: > 1024 } ||
            !Root(selected.WorkerRoot) || !Root(selected.CheckpointRoot) || selected.WorkerRoot == selected.CheckpointRoot ||
            selected.WorkerFile is null || !File(selected.WorkerFile) ||
            selected.CheckpointFiles is null || selected.CheckpointFiles.Count is < 2 or > 128 ||
            selected.CheckpointFiles.Any(file => file is null || !File(file)) ||
            selected.CheckpointFiles.Select(file => file.RelativeName).Distinct(StringComparer.Ordinal).Count() != selected.CheckpointFiles.Count ||
            !selected.CheckpointFiles.Any(file => file.RelativeName == "config.json") ||
            !selected.CheckpointFiles.Any(file => file.RelativeName.EndsWith(".safetensors", StringComparison.Ordinal)) ||
            selected.Requirements is null || !Sha(selected.Requirements.ArtifactFingerprint) ||
            string.IsNullOrWhiteSpace(selected.Requirements.Architecture) || string.IsNullOrWhiteSpace(selected.Requirements.Family) ||
            string.IsNullOrWhiteSpace(selected.Requirements.Quantization) || string.IsNullOrWhiteSpace(selected.Requirements.NativeRegistration) ||
            selected.Requirements.MinimumRamBytes < 0 || selected.Requirements.MinimumVramBytes < 0 || selected.Requirements.ContextTokens <= 0 ||
            !Strings(selected.Requirements.RequiredFeatures) || selected.CudaDeviceIndices is null || selected.CudaDeviceIndices.Count is < 1 or > 64 ||
            selected.CudaDeviceIndices.Any(device => device < 0) || selected.CudaDeviceIndices.Distinct().Count() != selected.CudaDeviceIndices.Count)
            throw new InvalidDataException("The explicit complete developer artifact tuple is invalid.");
        _ = selected.CheckpointFiles.Aggregate(0L, (size, file) => checked(size + file.Length)) is <= 64L * 1024 * 1024 * 1024
            ? true : throw new InvalidDataException("The selected original checkpoint exceeds 64 GiB.");
        selected = selected with
        {
            WorkerFile = selected.WorkerFile with { }, CheckpointFiles = Array.AsReadOnly(selected.CheckpointFiles.Select(file => file with { }).ToArray()),
            CudaDeviceIndices = Array.AsReadOnly(selected.CudaDeviceIndices.ToArray()),
            Requirements = selected.Requirements with { RequiredFeatures = Array.AsReadOnly(selected.Requirements.RequiredFeatures.ToArray()) }
        };
        var req = selected.Requirements;
        var requirements = new InferenceModelRequirements(selected.Model, req.ArtifactFingerprint, req.Architecture,
            req.Family, "Safetensors", req.Quantization, req.RequiredFeatures.ToFrozenSet(StringComparer.Ordinal),
            req.MinimumRamBytes, req.MinimumVramBytes, req.ContextTokens, NativeRegistration: req.NativeRegistration);
        var args = JsonSerializer.SerializeToElement(selected, Json);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(args.GetRawText())));
        return new(selected, args, digest, requirements, VerifyIndependentInventory(selected));
    }
    private static InferenceEngineSupport? VerifyIndependentInventory(StrataDeveloperArtifactSelection selection)
    {
        if (selection.IndependentBuildInventory is not { } signed) return null; // Never synthesize capability from requirements.
        if (signed.DeveloperPublicKeyPem is not { Length: > 0 and <= 8192 } ||
            signed.PayloadBase64 is not { Length: > 0 and <= 32768 } || signed.SignatureBase64 is not { Length: > 0 and <= 2048 })
            throw new InvalidDataException("The independent developer inventory envelope exceeds its bound.");
        if (!signed.DeveloperPublicKeyPem.StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) ||
            signed.DeveloperPublicKeyPem.Contains("PRIVATE KEY", StringComparison.Ordinal))
            throw new InvalidDataException("Supply only the explicit developer PUBLIC key; no private key is accepted or reviewed.");
        var payload = Convert.FromBase64String(signed.PayloadBase64); var signature = Convert.FromBase64String(signed.SignatureBase64);
        using (var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 16 })) DemandDistinctProperties(document.RootElement);
        using var publicKey = RSA.Create(); publicKey.ImportFromPem(signed.DeveloperPublicKeyPem);
        if (publicKey.KeySize < 3072 || !publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new UnauthorizedAccessException("The exact independent developer inventory signature is invalid.");
        var inventory = JsonSerializer.Deserialize<StrataDeveloperBuildInventory>(payload, Json)
            ?? throw new InvalidDataException("The independently signed developer inventory is empty.");
        // Exact original native origin/ABI/worker digest, separately observed again by hardware hello.
        var expectedBuild = "strata/015b075079c51a7aec670ee24924f920f5e7bb2b/abi1/" + selection.WorkerFile.Sha256.ToLowerInvariant();
        if (inventory.SchemaVersion != 1 || !Sha(inventory.WorkerSha256) ||
            !inventory.WorkerSha256.Equals(selection.WorkerFile.Sha256, StringComparison.OrdinalIgnoreCase) || inventory.RuntimeBuild != expectedBuild ||
            !Strings(inventory.Architectures) || !Strings(inventory.Families) || !Strings(inventory.WeightFormats) ||
            !Strings(inventory.Quantizations) || !Strings(inventory.Features) || !Strings(inventory.OperatingSystems) ||
            !Strings(inventory.CpuArchitectures) || !Strings(inventory.RequiredCpuFeatures) || !Strings(inventory.NativeRegistrations) ||
            inventory.MinimumCudaComputeMajor < 0 || inventory.MinimumCudaComputeMinor < 0)
            throw new InvalidDataException("The independently signed original build inventory differs from the selected worker.");
        return new(InferenceEngine.Strata, inventory.RuntimeBuild, true, null, Set(inventory.Architectures), Set(inventory.Families),
            Set(inventory.WeightFormats), Set(inventory.Quantizations), Set(inventory.Features), Set(inventory.OperatingSystems),
            Set(inventory.CpuArchitectures), Set(inventory.RequiredCpuFeatures), inventory.MinimumCudaComputeMajor,
            inventory.MinimumCudaComputeMinor, RequiresCuda: true, NcclBuilt: inventory.NcclBuilt, NativeRegistrations: Set(inventory.NativeRegistrations));
    }
    private static void DemandDistinctProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = value.EnumerateObject().ToArray();
            if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length)
                throw new InvalidDataException("Duplicate artifact/build-inventory fields are not a reviewable tuple.");
            foreach (var property in properties) DemandDistinctProperties(property.Value);
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) DemandDistinctProperties(item);
    }
    private static FrozenSet<string> Set(IReadOnlyList<string> values) => values.ToFrozenSet(StringComparer.Ordinal);
    private static bool Strings(IReadOnlyList<string>? values) => values is { Count: <= 128 } &&
        values.All(value => value is { Length: > 0 and <= 256 } && !value.Any(char.IsControl)) && values.Distinct(StringComparer.Ordinal).Count() == values.Count;
    private static bool Sha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private static bool Root(string? value) => value is { Length: > 1 and <= 4096 } && Path.IsPathFullyQualified(value) &&
        value == Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)) && !value.Any(char.IsControl);
    private static bool File(StrataInstalledFile file) => file.RelativeName is { Length: > 0 and <= 255 } && file.RelativeName is not ("." or "..") &&
        file.RelativeName.IndexOfAny(['/', '\\', '\0']) < 0 && file.Length is > 0 and <= 64L * 1024 * 1024 * 1024 && Sha(file.Sha256);
}
