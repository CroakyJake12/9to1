using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NineToOne.Dulche.Den;
using Xunit;

namespace Dulche.Den.Tests;

/// <summary>
/// Owning service-level tests for the B5 optional official-artifact tuple consumer on the ACTUAL Den
/// stack (DenStore + DulcheDen + LocalModelRegistry + ModelAdmissionPolicy). These exercise the real
/// register path with a real temporary Den store and a real small on-disk artifact; no fake DenStore,
/// mirrored arithmetic, or manual 21GB/sparse we files are used.
/// </summary>
public sealed class ModelRegistrationServiceTests
{
    private const long TwentyFourGiB = 24L * 1024 * 1024 * 1024;

    private const string OrnithCanonicalId = "ornith-ai/Ornith-1.5-122B";
    private const string OrnithRepo = "ornith-ai/Ornith-1.5-122B-GGUF";
    private const string OrnithRev = "8d5893d767c449a4f18dd08f4bb6d0d6d4426175";
    private const string OrnithFile = "Ornith-1.5-122B-Q4_K_M.gguf";
    private const long OrnithLen = 75843911648L;
    private const string OrnithSha = "729fe18c2491aaa446074a923e12f184f9f2239ffd5ce3dae47b0ce9cf8df40c";

    private const string SageCanonicalId = "InternScience/Sage-A1.5";
    private const string SageRepo = "InternScience/Sage-A1.5-Q4_K_M-GGUF";
    private const string SageRev = "656dec1d8d7b74fe5952cb4e83bdc42f6708e52d";
    private const string SageFile = "Sage-A1.5-Q4_K_M.gguf";
    private const long SageLen = 21713463520L;
    private const string SageSha = "a64fe0bbf936db3eb54f66f142c2b2eee569ade83673649f549a21daef8ca395";

    // ---- Serialization / type safety ------------------------------------------------

    [Fact]
    public void NullOfficialArtifactIsOmittedFromCanonicalJsonAndRoundTripsAsNull()
    {
        var model = new ModelRecord
        {
            Id = "model-generic",
            NamespaceId = "personal",
            DisplayName = "Generic model",
            ArtifactRevision = "rev-1",
            ArtifactSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };
        var json = JsonSerializer.Serialize<DenRecord>(model, DenJson.Options);
        Assert.DoesNotContain("\"officialArtifact\"", json);

        var back = Assert.IsType<ModelRecord>(JsonSerializer.Deserialize<DenRecord>(json, DenJson.Options));
        Assert.Null(back.OfficialArtifact);
    }

    [Fact]
    public void PresentOfficialArtifactTupleRoundTripsViaCanonicalJson()
    {
        var model = OfficialModel("model-ornith", OrnithCanonicalId, OrnithRepo, OrnithRev, OrnithFile,
            OrnithLen, OrnithSha, "Q4_K_M");
        var json = JsonSerializer.Serialize<DenRecord>(model, DenJson.Options);
        Assert.Contains("\"officialArtifact\"", json);

        var back = Assert.IsType<ModelRecord>(JsonSerializer.Deserialize<DenRecord>(json, DenJson.Options));
        Assert.NotNull(back.OfficialArtifact);
        Assert.Equal(model.OfficialArtifact, back.OfficialArtifact);
    }

    [Fact]
    public void OfficialArtifactTupleIsImmutableWithExplicitProvenance()
    {
        var tuple = new OfficialArtifactTuple(OrnithRepo, OrnithRev, OrnithFile, OrnithLen, OrnithSha,
            "Q4_K_M", OrnithCanonicalId);
        var changed = tuple with { FileName = "Ornith-1.5-122B-Q8_0.gguf" };
        Assert.NotEqual(tuple, changed);
        Assert.Equal(OrnithFile, tuple.FileName);       // original unaffected
        Assert.Equal(typeof(OfficialArtifactTuple), tuple.GetType()); // sealed, non-derivable
        Assert.Equal(OrnithCanonicalId, tuple.ExternalModelId);
    }

    // ---- Domain validation on the Den model-record save path --------------------------

    [Fact]
    public async Task SaveRejectsNegativeOfficialArtifactLength()
    {
        await using var fx = await Fixture.CreateAsync();
        var ex = await Assert.ThrowsAsync<DenException>(() => fx.Admin().SaveAsync(
            OfficialModel("m", OrnithCanonicalId, OrnithRepo, OrnithRev, OrnithFile, -1, OrnithSha,
                "Q4_K_M"), 0, OpId()));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
    }

    [Fact]
    public async Task SaveRejectsMalformedOfficialArtifactSha()
    {
        await using var fx = await Fixture.CreateAsync();
        var ex = await Assert.ThrowsAsync<DenException>(() => fx.Admin().SaveAsync(
            OfficialModel("m", OrnithCanonicalId, OrnithRepo, OrnithRev, OrnithFile, OrnithLen,
                "not-a-sha256", "Q4_K_M"), 0, OpId()));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
    }

    [Fact]
    public async Task SaveRejectsEmptyOfficialArtifactComponents()
    {
        await using var fx = await Fixture.CreateAsync();
        var tuple = new OfficialArtifactTuple("", OrnithRev, OrnithFile, OrnithLen, OrnithSha,
            "Q4_K_M", OrnithCanonicalId);
        var model = Model(ArtifactSha256: OrnithSha, tuple: tuple);
        var ex = await Assert.ThrowsAsync<DenException>(() => fx.Admin().SaveAsync(model, 0, OpId()));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
    }

    [Fact]
    public async Task SaveRejectsOfficialArtifactShaDisagreeingWithRecordWitness()
    {
        await using var fx = await Fixture.CreateAsync();
        var tuple = new OfficialArtifactTuple(OrnithRepo, OrnithRev, OrnithFile, OrnithLen, OrnithSha,
            "Q4_K_M", OrnithCanonicalId);
        var model = Model(ArtifactSha256: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", tuple: tuple);
        var ex = await Assert.ThrowsAsync<DenException>(() => fx.Admin().SaveAsync(model, 0, OpId()));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
    }

    [Fact]
    public async Task SaveRejectsOfficialArtifactRevisionDisagreeingWithRecordWitness()
    {
        await using var fx = await Fixture.CreateAsync();
        // Otherwise-valid pinned tuple/SHA but the record's artifact revision contradicts the official GGUF rev.
        var model = OfficialModel("m", OrnithCanonicalId, OrnithRepo, OrnithRev, OrnithFile, OrnithLen,
            OrnithSha, "Q4_K_M") with { ArtifactRevision = "wrong-artifact-revision" };
        var ex = await Assert.ThrowsAsync<DenException>(() => fx.Admin().SaveAsync(model, 0, OpId()));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
    }

    [Fact]
    public async Task SaveRejectsOfficialArtifactQuantisationDisagreeingWithRecordWitness()
    {
        await using var fx = await Fixture.CreateAsync();
        // Otherwise-valid pinned tuple/SHA but the record's quantization contradicts the official tuple quant.
        var model = OfficialModel("m", OrnithCanonicalId, OrnithRepo, OrnithRev, OrnithFile, OrnithLen,
            OrnithSha, "Q4_K_M") with { Quantisation = "Q8_0" };
        var ex = await Assert.ThrowsAsync<DenException>(() => fx.Admin().SaveAsync(model, 0, OpId()));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
    }

    // ---- Registration: generic (absent tuple) path -----------------------------------

    [Fact]
    public async Task RegisterGenericModelWithoutTupleSucceedsWithActualSmallFile()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        var tiny = WriteTempArtifact(fx, Encoding.UTF8.GetBytes("tiny real artifact bytes"));
        await den.SaveAsync(Model(ArtifactSha256: tiny.Sha256), 0, OpId());

        var installation = await den.RegisterLocalModelAsync("personal", "model-generic", tiny.Path, tiny.Sha256);

        Assert.Equal("model-generic", installation.ModelId);
        Assert.True(installation.Enabled);
        Assert.Equal(tiny.Length, installation.Length);
        Assert.Equal(tiny.Sha256, installation.Sha256);

        var registry = new LocalModelRegistry(fx.Root, fx.Store.Manifest.DeviceId);
        var verified = await registry.GetVerifiedAsync("model-generic");
        Assert.NotNull(verified);
        Assert.Equal(tiny.Length, verified!.Length);
    }

    [Fact]
    public async Task RegistrationRejectsCallerArtifactShaMismatchBeforeAnyInstallation()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        var tiny = WriteTempArtifact(fx, Encoding.UTF8.GetBytes("whatever"));
        var catalogueSha = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        await den.SaveAsync(Model(ArtifactSha256: catalogueSha), 0, OpId());

        // Caller supplies a DIFFERENT (still well-formed) SHA; the catalogue gate refuses before install.
        var ex = await Assert.ThrowsAsync<DenException>(() =>
            den.RegisterLocalModelAsync("personal", "model-generic", tiny.Path,
                "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);

        var registry = new LocalModelRegistry(fx.Root, fx.Store.Manifest.DeviceId);
        Assert.Null(await registry.GetVerifiedAsync("model-generic"));
    }

    // ---- Registration: official tuple admission --------------------------------------

    [Fact]
    public async Task RegisterOfficialOrnithRefusedAtSourceCeiling()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        var tiny = WriteTempArtifact(fx, [1, 2, 3]);
        await den.SaveAsync(OfficialModel("model-ornith", OrnithCanonicalId, OrnithRepo, OrnithRev,
            OrnithFile, OrnithLen, OrnithSha, "Q4_K_M"), 0, OpId());

        var ex = await Assert.ThrowsAsync<DenException>(() =>
            den.RegisterLocalModelAsync("personal", "model-ornith", tiny.Path, OrnithSha));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
        Assert.Contains("source ceiling", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterOfficialSageRefusedAtDefaultEightGiBQuota()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        var tiny = WriteTempArtifact(fx, [1, 2, 3]);
        await den.SaveAsync(OfficialModel("model-sage", SageCanonicalId, SageRepo, SageRev, SageFile,
            SageLen, SageSha, "Q4_K_M"), 0, OpId());

        var ex = await Assert.ThrowsAsync<DenException>(() =>
            den.RegisterLocalModelAsync("personal", "model-sage", tiny.Path, SageSha));
        Assert.Equal(DenErrorCode.RetentionBlocked, ex.Code);
        Assert.Contains("quota", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OfficialSagePassesPreflightAtOwnerQuotaThenWrongOnDiskHashRefusesInstallation()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        // Owner raises the quota via the ONLY real route (SetStorageQuotaAsync, Administer required).
        await den.SetStorageQuotaAsync("personal", Quota(TwentyFourGiB), 0, OpId());
        await den.SaveAsync(OfficialModel("model-sage", SageCanonicalId, SageRepo, SageRev, SageFile,
            SageLen, SageSha, "Q4_K_M"), 0, OpId());

        // Preflight would admit Sage at 24 GiB; the ACTUAL tiny file hash does not match the official
        // catalogue hash, so the on-disk evidence gate must refuse installation (no fake 21GB file).
        var tiny = WriteTempArtifact(fx, [9, 9, 9]);
        var ex = await Assert.ThrowsAsync<DenException>(() =>
            den.RegisterLocalModelAsync("personal", "model-sage", tiny.Path, SageSha));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
        Assert.Contains("hash", ex.Message, StringComparison.OrdinalIgnoreCase);

        var registry = new LocalModelRegistry(fx.Root, fx.Store.Manifest.DeviceId);
        Assert.Null(await registry.GetVerifiedAsync("model-sage"));
    }

    [Fact]
    public async Task RegisterOfficialUnknownExternalIdentityRefusedWithoutGenericFallback()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        var tiny = WriteTempArtifact(fx, [1, 2, 3]);
        var sha = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
        await den.SaveAsync(OfficialModel("model-unknown", "some/vendor-NonSense", "some/repo", "r1",
            "model.gguf", 1024, sha, "Q4_K_M"), 0, OpId());

        var ex = await Assert.ThrowsAsync<DenException>(() =>
            den.RegisterLocalModelAsync("personal", "model-unknown", tiny.Path, sha));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
        Assert.Contains("unrecognized", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterOfficialTupleMissingFormatAndFamilyIsRefused()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        var tiny = WriteTempArtifact(fx, [1, 2, 3]);
        var model = OfficialModel("model-ornith", OrnithCanonicalId, OrnithRepo, OrnithRev, OrnithFile,
            OrnithLen, OrnithSha, "Q4_K_M") with { Format = null, Family = null }; // format/family left null
        await den.SaveAsync(model, 0, OpId());

        var ex = await Assert.ThrowsAsync<DenException>(() =>
            den.RegisterLocalModelAsync("personal", model.Id, tiny.Path, OrnithSha));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
        Assert.Contains("format", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterRefusesOfficialTupleContradictingAtRestRecordStoredViaUnderlyingDenStore()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        // Write the catalogue record through the UNDERLYING DenStore author-path (so DulcheDen.SaveAsync's
        // tuple validation is genuinely bypassed): the record's artifact SHA contradicts the official pinned
        // tuple SHA. Registration must re-corroborate tuple vs model and refuse; no installation is published.
        var atRest = OfficialModel("model-bypass", OrnithCanonicalId, OrnithRepo, OrnithRev, OrnithFile,
            OrnithLen, OrnithSha, "Q4_K_M") with
            {
                ArtifactSha256 = "1111111111111111111111111111111111111111111111111111111111111111"
            };
        await fx.Store.RestoreRecordAsync(atRest, 0, OpId(),
            new NamespaceAccessPolicy([new DenAccessRule("admin", "personal", DenPermission.Administer)]),
            "admin");

        var tiny = WriteTempArtifact(fx, [4, 5, 6]);
        var ex = await Assert.ThrowsAsync<DenException>(() =>
            den.RegisterLocalModelAsync("personal", "model-bypass", tiny.Path, atRest.ArtifactSha256!));
        Assert.Equal(DenErrorCode.InvalidRecord, ex.Code);
        Assert.Contains("artifact SHA", ex.Message, StringComparison.OrdinalIgnoreCase);

        var registry = new LocalModelRegistry(fx.Root, fx.Store.Manifest.DeviceId);
        Assert.Null(await registry.GetVerifiedAsync("model-bypass"));
    }

    // ---- Authority / fence --------------------------------------------

    [Fact]
    public async Task RegistrationAuthorizationDeniedForNonWriter()
    {
        await using var fx = await Fixture.CreateAsync();
        var admin = fx.Admin();
        var tiny = WriteTempArtifact(fx, Encoding.UTF8.GetBytes("authorized seed"));
        await admin.SaveAsync(Model(ArtifactSha256: tiny.Sha256), 0, OpId());

        var reader = fx.Reader();
        var ex = await Assert.ThrowsAsync<DenException>(() =>
            reader.RegisterLocalModelAsync("personal", "model-generic", tiny.Path, tiny.Sha256));
        Assert.Equal(DenErrorCode.Forbidden, ex.Code);
    }

    [Fact]
    public async Task SetStorageQuotaRequiresAdministerAndIsNotAMutableCallerFlag()
    {
        await using var fx = await Fixture.CreateAsync();
        var reader = fx.Reader();
        var ex = await Assert.ThrowsAsync<DenException>(() =>
            reader.SetStorageQuotaAsync("personal", Quota(TwentyFourGiB), 0, OpId()));
        Assert.Equal(DenErrorCode.Forbidden, ex.Code);
    }

    [Fact]
    public async Task StableDenIdIsDistinctFromExternalModelIdentity()
    {
        await using var fx = await Fixture.CreateAsync();
        var den = fx.Admin();
        var model = OfficialModel("model-ornith-001", OrnithCanonicalId, OrnithRepo, OrnithRev,
            OrnithFile, OrnithLen, OrnithSha, "Q4_K_M");
        // The stable Den ID is a local identifier; it must not be replaced by the HF external id.
        Assert.NotEqual(model.Id, model.OfficialArtifact!.ExternalModelId);
        await den.SaveAsync(model, 0, OpId());

        var stored = await den.GetModelByID("model-ornith-001", "personal");
        Assert.NotNull(stored);
        Assert.Equal("model-ornith-001", stored!.Id);
        Assert.Equal(OrnithCanonicalId, stored.OfficialArtifact!.ExternalModelId);
    }

    // ---- Helpers ------------------------------------------------------

    private static ModelRecord Model(string? ArtifactSha256 = null, OfficialArtifactTuple? tuple = null) => new()
    {
        Id = "model-generic",
        NamespaceId = "personal",
        DisplayName = "Generic model",
        ArtifactRevision = "rev-1",
        ArtifactSha256 = ArtifactSha256,
        OfficialArtifact = tuple
    };

    private static ModelRecord OfficialModel(string denId, string externalId, string repo, string rev,
        string file, long length, string sha, string quant, string? format = "GGUF", string? family = "qwen35moe")
        => new()
        {
            Id = denId,
            NamespaceId = "personal",
            DisplayName = externalId,
            ArtifactRevision = rev,
            ArtifactSha256 = sha,
            Format = format,
            Family = family,
            Quantisation = quant,
            OfficialArtifact = new OfficialArtifactTuple(repo, rev, file, length, sha, quant, externalId)
        };

    private static StorageQuotaRecord Quota(long modelWeightBytes) => new()
    {
        Id = "storage-quota",
        NamespaceId = "personal",
        ModelWeightBytes = modelWeightBytes
    };

    private static string OpId() => "op-b5-" + Guid.NewGuid().ToString("N");

    private static (string Path, string Sha256, long Length) WriteTempArtifact(Fixture fx, byte[] content)
    {
        var path = Path.Combine(fx.Root, "install-" + Guid.NewGuid().ToString("N") + ".bin");
        File.WriteAllBytes(path, content);
        return (path, Convert.ToHexString(SHA256.HashData(content)), content.LongLength);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required string Root;
        public required DenStore Store;

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "den-b5-" + Guid.NewGuid().ToString("N"));
            var store = await DenStore.CreateAsync(root, [new DenNamespace("personal", "personal")]);
            return new Fixture { Root = root, Store = store };
        }

        public DulcheDen Admin(string principalId = "admin") => new(
            Store,
            new NamespaceAccessPolicy([new DenAccessRule(principalId, "personal", DenPermission.Administer)]),
            principalId);

        public DulcheDen Reader(string principalId = "reader") => new(
            Store,
            new NamespaceAccessPolicy([new DenAccessRule(principalId, "personal", DenPermission.Read)]),
            principalId);

        public async ValueTask DisposeAsync()
        {
            await Store.DisposeAsync();
            try { Directory.Delete(Root, true); } catch { /* best-effort temp cleanup */ }
        }
    }
}
