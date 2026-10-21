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
    StrataDeveloperSignedBuildInventory? IndependentBuildInventory = null,
    string? PythonInterpreterPath = null,
    string? EngineRoot = null,
    StrataInstalledFile? EngineFile = null,
    IReadOnlyList<StrataInstalledFile>? HelperFiles = null);
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
    int MinimumCudaComputeMajor, int MinimumCudaComputeMinor, bool NcclBuilt,
    string? Profile = null, int? AbiVersion = null, string? SourceIdentity = null,
    StrataInstalledFile? EngineFile = null,
    IReadOnlyList<StrataInstalledFile>? HelperFiles = null);

internal sealed record StrataDeveloperArtifactCapture(StrataDeveloperArtifactSelection Selection,
    JsonElement Arguments, string Digest, InferenceModelRequirements Requirements, InferenceEngineSupport? BuildSupport,
    StrataNativeProfile? Profile, string? PythonInterpreterPath)
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
        var verified = VerifyIndependentInventory(selected);
        var candidate = verified.Profile is not null;
        if (selected.Model is null || string.IsNullOrWhiteSpace(selected.Model.ProviderId) ||
            string.IsNullOrWhiteSpace(selected.Model.ModelId) || selected.Model.ArtifactRevision is { } revision && string.IsNullOrWhiteSpace(revision) ||
            selected.Model.ProviderId.Length > 128 || selected.Model.ModelId.Length > 512 || selected.Model.ArtifactRevision is { Length: > 1024 } ||
            !Root(selected.WorkerRoot) || !Root(selected.CheckpointRoot) || selected.WorkerRoot == selected.CheckpointRoot ||
            selected.WorkerFile is null || !File(selected.WorkerFile) ||
            selected.CheckpointFiles is null ||
            (selected.CheckpointFiles.Count < (candidate ? 1 : 2) || selected.CheckpointFiles.Count > 128) ||
            selected.CheckpointFiles.Any(file => file is null || !File(file)) ||
            selected.CheckpointFiles.Select(file => file.RelativeName).Distinct(StringComparer.Ordinal).Count() != selected.CheckpointFiles.Count ||
            (candidate
                // Candidate V2 is engine-native single-file GGUF; split/multi-GGUF closures are not
                // proven by this relay and are therefore precisely refused (never a false universal claim).
                ? (selected.CheckpointFiles.Count(file => file.RelativeName.EndsWith(".gguf", StringComparison.Ordinal)) != 1)
                : (!selected.CheckpointFiles.Any(file => file.RelativeName == "config.json") ||
                   !selected.CheckpointFiles.Any(file => file.RelativeName.EndsWith(".safetensors", StringComparison.Ordinal)))) ||
            selected.Requirements is null || !Sha(selected.Requirements.ArtifactFingerprint) ||
            string.IsNullOrWhiteSpace(selected.Requirements.Architecture) || string.IsNullOrWhiteSpace(selected.Requirements.Family) ||
            string.IsNullOrWhiteSpace(selected.Requirements.Quantization) || string.IsNullOrWhiteSpace(selected.Requirements.NativeRegistration) ||
            selected.Requirements.MinimumRamBytes < 0 || selected.Requirements.MinimumVramBytes < 0 || selected.Requirements.ContextTokens <= 0 ||
            !Strings(selected.Requirements.RequiredFeatures) || selected.CudaDeviceIndices is null || selected.CudaDeviceIndices.Count is < 1 or > 64 ||
            selected.CudaDeviceIndices.Any(device => device < 0) || selected.CudaDeviceIndices.Distinct().Count() != selected.CudaDeviceIndices.Count ||
            selected.PythonInterpreterPath is { } python && (!IsCanonicalPosixRoot(python) || python.Length > 4096))
            throw new InvalidDataException("The explicit complete developer artifact tuple is invalid.");
        // Candidate V2 admits ONLY the verified engine-native GGUF closure. Legacy/single-executable
        // V1 never carries candidate closure fields. A candidate without its complete signed closure
        // fails closed here, never by silently falling back to the legacy path.
        if (candidate) ValidateCandidateClosure(selected, verified.Inventory!);
        else if (selected.EngineRoot is not null || selected.EngineFile is not null || selected.HelperFiles is not null)
            throw new InvalidDataException("Legacy V1 selections must not carry candidate engine/helper closure fields.");
        _ = selected.CheckpointFiles.Aggregate(0L, (size, file) => checked(size + file.Length)) is <= 64L * 1024 * 1024 * 1024
            ? true : throw new InvalidDataException("The selected original checkpoint exceeds 64 GiB.");
        selected = selected with
        {
            WorkerFile = selected.WorkerFile with { }, CheckpointFiles = Array.AsReadOnly(selected.CheckpointFiles.Select(file => file with { }).ToArray()),
            CudaDeviceIndices = Array.AsReadOnly(selected.CudaDeviceIndices.ToArray()),
            Requirements = selected.Requirements with { RequiredFeatures = Array.AsReadOnly(selected.Requirements.RequiredFeatures.ToArray()) }
        };
        var req = selected.Requirements;
        // Candidate V2 is engine-native GGUF; legacy V1 remains Safetensors. The verified profile is
        // the only selector — never a caller string.
        var weightFormat = candidate ? "GGUF" : "Safetensors";
        var requirements = new InferenceModelRequirements(selected.Model, req.ArtifactFingerprint, req.Architecture,
            req.Family, weightFormat, req.Quantization, req.RequiredFeatures.ToFrozenSet(StringComparer.Ordinal),
            req.MinimumRamBytes, req.MinimumVramBytes, req.ContextTokens, NativeRegistration: req.NativeRegistration);
        var args = JsonSerializer.SerializeToElement(selected, Json);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(args.GetRawText())));
        // An approved interpreter path is honored only for a VERIFIED candidate install; legacy V1
        // (or any unverified/unknown profile) stays at its default and never carries a python path.
        var pythonPath = candidate ? selected.PythonInterpreterPath : null;
        return new(selected, args, digest, requirements, verified.Support, verified.Profile, pythonPath);
    }
    private sealed record VerifiedInventory(InferenceEngineSupport? Support, StrataNativeProfile? Profile,
        StrataDeveloperBuildInventory? Inventory);
    private static VerifiedInventory VerifyIndependentInventory(StrataDeveloperArtifactSelection selection)
    {
        if (selection.IndependentBuildInventory is not { } signed) return new(null, null, null); // Never synthesize capability from requirements.
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
        return inventory.SchemaVersion switch
        {
            1 => VerifyLegacyInventory(selection, inventory),
            2 => VerifyCandidateInventory(selection, inventory),
            _ => throw new InvalidDataException("The independently signed build inventory schema is unsupported.")
        };
    }
    private static VerifiedInventory VerifyLegacyInventory(
        StrataDeveloperArtifactSelection selection, StrataDeveloperBuildInventory inventory)
    {
        // Exact original native origin/ABI/worker digest, separately observed again by hardware hello.
        var expectedBuild = "strata/015b075079c51a7aec670ee24924f920f5e7bb2b/abi1/" + selection.WorkerFile.Sha256.ToLowerInvariant();
        if (!Sha(inventory.WorkerSha256) ||
            !inventory.WorkerSha256.Equals(selection.WorkerFile.Sha256, StringComparison.OrdinalIgnoreCase) || inventory.RuntimeBuild != expectedBuild ||
            !Strings(inventory.Architectures) || !Strings(inventory.Families) || !Strings(inventory.WeightFormats) ||
            !Strings(inventory.Quantizations) || !Strings(inventory.Features) || !Strings(inventory.OperatingSystems) ||
            !Strings(inventory.CpuArchitectures) || !Strings(inventory.RequiredCpuFeatures) || !Strings(inventory.NativeRegistrations) ||
            inventory.MinimumCudaComputeMajor < 0 || inventory.MinimumCudaComputeMinor < 0)
            throw new InvalidDataException("The independently signed original build inventory differs from the selected worker.");
        return new(new(InferenceEngine.Strata, inventory.RuntimeBuild, true, null, Set(inventory.Architectures), Set(inventory.Families),
            Set(inventory.WeightFormats), Set(inventory.Quantizations), Set(inventory.Features), Set(inventory.OperatingSystems),
            Set(inventory.CpuArchitectures), Set(inventory.RequiredCpuFeatures), inventory.MinimumCudaComputeMajor,
            inventory.MinimumCudaComputeMinor, RequiresCuda: true, NcclBuilt: inventory.NcclBuilt, NativeRegistrations: Set(inventory.NativeRegistrations)), null, inventory);
    }
    private static VerifiedInventory VerifyCandidateInventory(
        StrataDeveloperArtifactSelection selection, StrataDeveloperBuildInventory inventory)
    {
        // The candidate ABI2 profile is produced only from VERIFIED signed fields: the pinned v0.2.2
        // source identity, ABI 2, the engine-native GGUF qwen35moe family, and the matching worker
        // digest under the candidate runtime-build path. No caller string and no guess selects it.
        const string candidateOrigin = "011c9e600a93bbc9fd451c563501b5831c32bbca";
        var expectedBuild = "strata/" + candidateOrigin + "/abi2/" + selection.WorkerFile.Sha256.ToLowerInvariant();
        if (inventory.Profile != "candidate" || inventory.AbiVersion != 2 ||
            inventory.SourceIdentity != StrataCandidateWorker.VerifiedCandidateSourceCommit ||
            !Sha(inventory.WorkerSha256) ||
            !inventory.WorkerSha256.Equals(selection.WorkerFile.Sha256, StringComparison.OrdinalIgnoreCase) || inventory.RuntimeBuild != expectedBuild ||
            !Strings(inventory.Architectures) || !Strings(inventory.Families) || !Strings(inventory.WeightFormats) ||
            !Strings(inventory.Quantizations) || !Strings(inventory.Features) || !Strings(inventory.OperatingSystems) ||
            !Strings(inventory.CpuArchitectures) || !Strings(inventory.RequiredCpuFeatures) || !Strings(inventory.NativeRegistrations) ||
            inventory.MinimumCudaComputeMajor < 0 || inventory.MinimumCudaComputeMinor < 0 ||
            !inventory.WeightFormats.Contains("GGUF", StringComparer.Ordinal) ||
            !inventory.Families.Contains("qwen35moe", StringComparer.Ordinal) ||
            !inventory.NativeRegistrations.Contains("qwen35moe", StringComparer.Ordinal))
            throw new InvalidDataException("The independently signed candidate build inventory is not the verified candidate source.");
        // The candidate HARD REQUIRES the complete engine/helper closure: the pinned engine sibling
        // base, the four pinned helper text-path members, and positive bounded digests. Rejecting a
        // missing/mismatched/unknown/duplicate/traversing member fails closed BEFORE any use.
        ValidateSignedCandidateClosure(inventory);
        // Truthful CUDA posture comes from the signed inventory's declared compute minimums, never a
        // manufactured receipt. A no-CUDA candidate (min compute 0) therefore claims RequiresCuda=false;
        // the genuine hardware probe remains the runtime source of truth (ProviderUnavailable).
        var requiresCuda = inventory.MinimumCudaComputeMajor > 0 || inventory.MinimumCudaComputeMinor > 0;
        var support = new(InferenceEngine.Strata, inventory.RuntimeBuild, true, null,
            Set(inventory.Architectures), Set(inventory.Families), Set(inventory.WeightFormats), Set(inventory.Quantizations),
            Set(inventory.Features), Set(inventory.OperatingSystems), Set(inventory.CpuArchitectures), Set(inventory.RequiredCpuFeatures),
            inventory.MinimumCudaComputeMajor, inventory.MinimumCudaComputeMinor, RequiresCuda: requiresCuda,
            NcclBuilt: inventory.NcclBuilt, NativeRegistrations: Set(inventory.NativeRegistrations));
        return new(support, new StrataNativeProfile("candidate", 2, inventory.SourceIdentity!), inventory);
    }
    private static readonly string[] RequiredHelperClosureNames =
        ["strata_tokenizer_helper.py", "strata_tokenizer.py", "gguf_reader.py", "strata-runtime-dependencies.json"];
    private static void ValidateSignedCandidateClosure(StrataDeveloperBuildInventory inventory)
    {
        var engine = inventory.EngineFile ?? throw new InvalidDataException("The signed candidate inventory is missing its engine closure.");
        if (!File(engine) || engine.RelativeName != "strata")
            throw new InvalidDataException("The signed candidate engine must be the pinned strata base under the engine root.");
        var helpers = inventory.HelperFiles;
        if (helpers is not { Count: 4 } || !helpers.All(File) ||
            !helpers.Select(file => file.RelativeName).Order(StringComparer.Ordinal).SequenceEqual(RequiredHelperClosureNames.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("The signed candidate helper closure must contain exactly the four pinned text-path members.");
    }
    private static void ValidateCandidateClosure(StrataDeveloperArtifactSelection selection, StrataDeveloperBuildInventory inventory)
    {
        // The selection must nominate the engine sibling root (a DISTINCT canonical root) and the
        // installed files beside the worker; their digests must exactly match the signed inventory.
        if (selection.EngineRoot is not { } engineRoot || !Root(engineRoot) ||
            engineRoot == selection.WorkerRoot || engineRoot == selection.CheckpointRoot)
            throw new InvalidDataException("The candidate artifact tuple must nominate a distinct engine sibling root.");
        var engine = inventory.EngineFile!;
        if (selection.EngineFile is not { } selEngine || !File(selEngine) ||
            selEngine.RelativeName != engine.RelativeName ||
            !selEngine.Sha256.Equals(engine.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected engine does not match the signed candidate build inventory.");
        var helpers = inventory.HelperFiles!;
        var selHelpers = selection.HelperFiles;
        if (selHelpers is not { Count: 4 } || !selHelpers.All(File) ||
            !selHelpers.Select(file => file.RelativeName).Order(StringComparer.Ordinal).SequenceEqual(RequiredHelperClosureNames.Order(StringComparer.Ordinal)))
            throw new InvalidDataException("The selected helper closure must contain exactly the four pinned text-path members.");
        foreach (var name in RequiredHelperClosureNames)
        {
            var inv = helpers.Single(file => file.RelativeName == name);
            var sel = selHelpers.Single(file => file.RelativeName == name);
            if (!sel.Sha256.Equals(inv.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected helper '" + name + "' digest differs from the signed candidate inventory.");
        }
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
    private static bool Root(string? value) => value is { Length: > 1 and <= 4096 } && !value.Any(char.IsControl) &&
        (Path.IsPathFullyQualified(value) ? value == Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)) : IsCanonicalPosixRoot(value));
    // Developer artifact roots name the actual protected installation on the Linux runtime host; they
    // must not be re-canonicalised through the reviewing host platform, which would accept a foreign drive root.
    private static bool IsCanonicalPosixRoot(string value) => value[0] == '/' && !value.Contains('\\') && !value.EndsWith('/') &&
        value.Split('/').Skip(1).All(segment => segment is not ("" or "." or ".."));
    private static bool File(StrataInstalledFile file) => file.RelativeName is { Length: > 0 and <= 255 } && file.RelativeName is not ("." or "..") &&
        file.RelativeName.IndexOfAny(['/', '\\', '\0']) < 0 && file.Length is > 0 and <= 64L * 1024 * 1024 * 1024 && Sha(file.Sha256);
}


