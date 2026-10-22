using System.Security.Cryptography;
using System.Text.Json;
using Dulche.Runtime;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>A34 owning K-quant MMQ DSO custody tests. These exercise the REAL signed-capture gate
/// (StrataDeveloperArtifactCapture.Capture) with genuine negative controls: candidate closures are
/// admitted only when the pinned Linux DSO matches name + length + digest, and null/missing/mismatched
/// members fail closed as InvalidDataException (never an NRE). The bundled-worker record contract test
/// is kept pure. No GPU/native execution is claimed, and the positive kernel-immutable protected-root
/// lease is NOTRUN until Root allocates a serial SDK + immutable-FS fixture.</summary>
public sealed class StrataKqDsoCustodyTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Bundled_worker_carries_the_held_KQ_DSO_fd_path_and_digest()
    {
        var worker = new StrataBundledWorker("/w", "aaaa", "/usr/bin/python3",
            EnginePath: "/p/e", EngineSha256: "bbbb", EngineFdPath: "/proc/1/fd/7",
            HelperPaths: ["/p/h1"], HelperSha256: ["cccc"], HelperFdPaths: ["/proc/1/fd/8"],
            HelperDirectoryFdPath: "/proc/1/fd/5",
            NativeLibraryFdPath: "/proc/1/fd/9", NativeLibrarySha256: "dddd");
        Assert.Equal("/proc/1/fd/9", worker.NativeLibraryFdPath);
        Assert.Equal("dddd", worker.NativeLibrarySha256);
        // Omission defaults null, preserving legacy V1 single-executable semantics unchanged.
        var legacy = new StrataBundledWorker("/w", "aaaa");
        Assert.Null(legacy.NativeLibraryFdPath);
        Assert.Null(legacy.NativeLibrarySha256);
    }

    [Fact]
    public void Candidate_native_library_with_mismatched_length_fails_closed()
    {
        using var key = RSA.Create(3072);
        // The signed inventory pins name "libstrata_kq.so", length 28, digest 999…; the selection
        // keeps the exact name and digest but reports a different byte length. That is a real closure
        // mismatch and must fail closed (name+length+SHA make the byte-for-byte match, not SHA alone).
        var mismatch = CandidateSignedSelection(key) with
        {
            NativeLibraries = [DefaultNativeLibraries[0] with { Length = 29 }]
        };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(mismatch, Json)));
    }

    [Fact]
    public void Candidate_native_library_null_member_is_rejected_as_invalid_not_NRE()
    {
        using var key = RSA.Create(3072);
        // A null member inside the cloned native closure must be rejected by the typed authority as an
        // InvalidDataException, never surface as a NullReferenceException.
        var withNullMember = CandidateSignedSelection(key) with
        {
            NativeLibraries = [default(StrataInstalledFile)!]
        };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(withNullMember, Json)));
    }

    [Fact]
    public void Candidate_native_library_missing_from_selection_fails_closed()
    {
        using var key = RSA.Create(3072);
        // The signed inventory carries the pinned DSO but the selection omits it -> refused.
        var missing = CandidateSignedSelection(key) with { NativeLibraries = null };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(missing, Json)));
    }

    [Fact]
    public void Legacy_signed_inventory_carrying_candidate_native_library_is_rejected_even_when_selection_omits_it()
    {
        using var key = RSA.Create(3072);
        // A schema-1 signed inventory that smuggles the candidate native-library field is refused even
        // when the selection itself omits it: the strict V1 gate holds for the signed inventory too.
        var selection = Selection();
        var inventory = new StrataDeveloperBuildInventory(1, selection.WorkerFile.Sha256,
            "strata/015b075079c51a7aec670ee24924f920f5e7bb2b/abi1/" + selection.WorkerFile.Sha256,
            ["fixture-independent-architecture"], ["fixture-family"], ["Safetensors"], ["F16"], ["Text", "Streaming"], ["Linux"], ["X64"], [], ["fixture-native-registration"], 7, 0, false,
            NativeLibraries: [new("libstrata_kq.so", 28, new string('9', 64))]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(inventory, Json);
        var signed = selection with
        {
            IndependentBuildInventory = new(key.ExportSubjectPublicKeyInfoPem(),
                Convert.ToBase64String(bytes), Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)))
        };
        Assert.Throws<InvalidDataException>(() => StrataDeveloperArtifactCapture.Capture(JsonSerializer.Serialize(signed, Json)));
    }

    private static StrataDeveloperArtifactSelection Selection() => new(new("strata", "fixture-model", "fixture-model-revision"),
        "/unread-worker-root", new("worker", 32, new string('a', 64)), "/unread-checkpoint-root",
        [new("config.json", 16, new string('b', 64)), new("model.safetensors", 32, new string('c', 64))],
        new(new string('d', 64), "fixture-request-architecture", "fixture-family", "F16", ["Text"], 0, 0, 32, "fixture-native-registration"), [0]);
    private static readonly StrataInstalledFile[] DefaultNativeLibraries =
        [new StrataInstalledFile("libstrata_kq.so", 28, new string('9', 64))];
    private static StrataDeveloperArtifactSelection CandidateSignedSelection(RSA key)
    {
        var selection = Selection() with { CheckpointFiles = [new("model.gguf", 32, new string('e', 64))] };
        var runtime = "strata/011c9e600a93bbc9fd451c563501b5831c32bbca/abi2/" + selection.WorkerFile.Sha256;
        var engine = new StrataInstalledFile("strata", 42, new string('f', 64));
        var helpers = new[]
        {
            new StrataInstalledFile("strata_tokenizer_helper.py", 24, new string('1', 64)),
            new StrataInstalledFile("strata_tokenizer.py", 25, new string('2', 64)),
            new StrataInstalledFile("gguf_reader.py", 26, new string('3', 64)),
            new StrataInstalledFile("strata-runtime-dependencies.json", 27, new string('4', 64)),
        };
        var inventory = new StrataDeveloperBuildInventory(2, selection.WorkerFile.Sha256, runtime,
            ["fixture-independent-architecture"], ["qwen35moe"], ["GGUF"], ["Q4_K_M"], ["Text", "Streaming"],
            ["Linux"], ["X64"], [], ["qwen35moe"], 0, 0, false,
            Profile: "candidate", AbiVersion: 2, SourceIdentity: StrataCandidateWorker.VerifiedCandidateSourceCommit,
            EngineFile: engine, HelperFiles: helpers, NativeLibraries: DefaultNativeLibraries);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(inventory, Json);
        return selection with
        {
            EngineRoot = "/unread-engine-root",
            EngineFile = engine,
            HelperFiles = helpers,
            NativeLibraries = DefaultNativeLibraries,
            IndependentBuildInventory = new(key.ExportSubjectPublicKeyInfoPem(),
                Convert.ToBase64String(bytes), Convert.ToBase64String(key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)))
        };
    }
}
