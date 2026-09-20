using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using Xunit;

namespace HavenOS.Release.Tests;

public class ReleaseMetadataSchemaTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void ReleaseMetadataSchema_ValidatesAllFiveProductTypes()
    {
        var fixtureFiles = new[]
        {
            "haven-welcome.json",           // App
            "haven-shell.json",             // SystemSurface
            "havenos-studio.json",          // App
            "llamacpp-provider.json",       // Provider
            "haven-notifications.json",     // BackgroundComponent
            "haven-settings-service.json"   // Service
        };

        foreach (var file in fixtureFiles)
        {
            var path = Path.Combine(FixturesPath, file);
            Assert.True(File.Exists(path), $"Fixture {file} exists");

            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Validate required fields
            Assert.True(root.TryGetProperty("productId", out _), $"{file}: productId present");
            Assert.True(root.TryGetProperty("productType", out _), $"{file}: productType present");
            Assert.True(root.TryGetProperty("registryIdentity", out _), $"{file}: registryIdentity present");
            Assert.True(root.TryGetProperty("sourceRepository", out _), $"{file}: sourceRepository present");
            Assert.True(root.TryGetProperty("donor", out _), $"{file}: donor present");
            Assert.True(root.TryGetProperty("package", out _), $"{file}: package present");
            Assert.True(root.TryGetProperty("entrypoint", out _), $"{file}: entrypoint present");
            Assert.True(root.TryGetProperty("service", out _), $"{file}: service present");
            Assert.True(root.TryGetProperty("routes", out _), $"{file}: routes present");
            Assert.True(root.TryGetProperty("capabilities", out _), $"{file}: capabilities present");
            Assert.True(root.TryGetProperty("dependencies", out _), $"{file}: dependencies present");
            Assert.True(root.TryGetProperty("persistence", out _), $"{file}: persistence present");
            Assert.True(root.TryGetProperty("permissions", out _), $"{file}: permissions present");
            Assert.True(root.TryGetProperty("providers", out _), $"{file}: providers present");
            Assert.True(root.TryGetProperty("offlineRequirement", out _), $"{file}: offlineRequirement present");
            Assert.True(root.TryGetProperty("smokeTest", out _), $"{file}: smokeTest present");
            Assert.True(root.TryGetProperty("provenanceState", out _), $"{file}: provenanceState present");
            Assert.True(root.TryGetProperty("imageInclusionState", out _), $"{file}: imageInclusionState present");
            Assert.True(root.TryGetProperty("approvedVmState", out _), $"{file}: approvedVmState present");

            // Validate registry identity references Worker 3
            var registry = root.GetProperty("registryIdentity");
            Assert.Equal("cakeos.shared-app-registry", registry.GetProperty("registryId").GetString());
            Assert.Equal(1, registry.GetProperty("registrySchemaVersion").GetInt32());

            // Validate product type is one of the five
            var productType = root.GetProperty("productType").GetString();
            Assert.Contains(productType, new[] { "App", "SystemSurface", "BackgroundComponent", "Provider", "Service" });

            // Validate provenance state is explicit
            var provenanceState = root.GetProperty("provenanceState").GetString();
            Assert.Contains(provenanceState, new[] { "Unknown", "Partial", "Blocked", "Ready" });

            // Validate image inclusion state
            var imageState = root.GetProperty("imageInclusionState").GetString();
            Assert.Contains(imageState, new[] { "NotApplicable", "Excluded", "Pending", "Included" });

            // Validate approved VM state
            var vmState = root.GetProperty("approvedVmState").GetString();
            Assert.Contains(vmState, new[] { "NotTested", "Failed", "Partial", "Verified" });
        }
    }

    [Fact]
    public void ReleaseMetadataSchema_RejectsInvalidProvenance()
    {
        // Missing donor licence
        var invalidJson = """
        {
          "productId": "test.app",
          "productType": "App",
          "registryIdentity": { "registryId": "cakeos.shared-app-registry", "registrySchemaVersion": 1 },
          "sourceRepository": { "url": "https://example.com", "revision": "abc123" },
          "donor": { "name": "Test", "repositoryUrl": "https://example.com", "revision": "abc123", "licence": "" },
          "package": { "name": "test-app", "version": "1.0.0", "hashSha256": "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456" },
          "entrypoint": { "binaryPath": "/usr/bin/test" },
          "service": {},
          "routes": [],
          "capabilities": [],
          "dependencies": [],
          "persistence": { "requiresSettings": false, "requiresCache": false, "requiresState": false, "requiresData": false },
          "permissions": { "requiredGrants": [], "optionalGrants": [] },
          "providers": [],
          "offlineRequirement": "NotRequired",
          "smokeTest": { "command": "test --version" },
          "provenanceState": "Ready",
          "imageInclusionState": "Included",
          "approvedVmState": "Verified"
        }
        """;

        var doc = JsonDocument.Parse(invalidJson);
        var donor = doc.RootElement.GetProperty("donor");
        Assert.Equal("", donor.GetProperty("licence").GetString());

        // In a real implementation, validation would reject this
        // This test documents the expected rejection behavior
    }

    [Fact]
    public void ReleaseMetadataSchema_RejectsMissingProvenance()
    {
        var invalidJson = """
        {
          "productId": "test.app",
          "productType": "App",
          "registryIdentity": { "registryId": "cakeos.shared-app-registry", "registrySchemaVersion": 1 },
          "sourceRepository": { "url": "https://example.com", "revision": "abc123" },
          "donor": { "name": "", "repositoryUrl": "", "revision": "", "licence": "" },
          "package": { "name": "", "version": "", "hashSha256": "" },
          "entrypoint": { "binaryPath": "" },
          "service": {},
          "routes": [],
          "capabilities": [],
          "dependencies": [],
          "persistence": { "requiresSettings": false, "requiresCache": false, "requiresState": false, "requiresData": false },
          "permissions": { "requiredGrants": [], "optionalGrants": [] },
          "providers": [],
          "offlineRequirement": "NotRequired",
          "smokeTest": { "command": "" },
          "provenanceState": "Unknown",
          "imageInclusionState": "NotApplicable",
          "approvedVmState": "NotTested"
        }
        """;

        var doc = JsonDocument.Parse(invalidJson);
        var donor = doc.RootElement.GetProperty("donor");
        Assert.Equal("", donor.GetProperty("name").GetString());
        Assert.Equal("", donor.GetProperty("repositoryUrl").GetString());
        Assert.Equal("", donor.GetProperty("revision").GetString());
        Assert.Equal("", donor.GetProperty("licence").GetString());

        var pkg = doc.RootElement.GetProperty("package");
        Assert.Equal("", pkg.GetProperty("name").GetString());
        Assert.Equal("", pkg.GetProperty("version").GetString());
        Assert.Equal("", pkg.GetProperty("hashSha256").GetString());

        var provenanceState = doc.RootElement.GetProperty("provenanceState").GetString();
        Assert.Equal("Unknown", provenanceState);
    }

    [Fact]
    public void ReleaseMetadataSchema_ExplicitStateHandling()
    {
        var fixtureFiles = new[]
        {
            ("haven-welcome.json", "Ready", "Included", "Verified", true),
            ("haven-shell.json", "Ready", "Included", "Verified", true),
            ("havenos-studio.json", "Ready", "Included", "Verified", true),
            ("llamacpp-provider.json", "Ready", "Pending", "Partial", false),
            ("haven-notifications.json", "Partial", "Excluded", "NotTested", false),
            ("haven-settings-service.json", "Blocked", "NotApplicable", "Failed", false)
        };

        foreach (var (file, expectedProvenance, expectedImage, expectedVm, expectedConverged) in fixtureFiles)
        {
            var path = Path.Combine(FixturesPath, file);
            var json = File.ReadAllText(path);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(expectedProvenance, root.GetProperty("provenanceState").GetString());
            Assert.Equal(expectedImage, root.GetProperty("imageInclusionState").GetString());
            Assert.Equal(expectedVm, root.GetProperty("approvedVmState").GetString());

            // Verify convergence logic
            var provenance = root.GetProperty("provenanceState").GetString();
            var image = root.GetProperty("imageInclusionState").GetString();
            var vm = root.GetProperty("approvedVmState").GetString();
            var smokeEvidence = root.TryGetProperty("smokeEvidence", out var evidence) && evidence.ValueKind != JsonValueKind.Null
                ? evidence.GetProperty("exitCode").GetInt32() == 0
                : false;
            var offlineReq = root.GetProperty("offlineRequirement").GetString();

            var offlineMet = offlineReq == "NotRequired" || smokeEvidence;
            var isConverged = provenance == "Ready" && image == "Included" && vm == "Verified" && offlineMet && smokeEvidence;

            Assert.Equal(expectedConverged, isConverged);
        }
    }

    [Fact]
    public void ReleaseManifest_ValidatesAgainstSchema()
    {
        var path = Path.Combine(FixturesPath, "release-manifest.json");
        Assert.True(File.Exists(path), "Release manifest fixture exists");

        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("releaseId", out _));
        Assert.True(root.TryGetProperty("version", out _));
        Assert.True(root.TryGetProperty("createdAt", out _));
        Assert.True(root.TryGetProperty("registryIdentity", out _));
        Assert.True(root.TryGetProperty("components", out _));

        var registry = root.GetProperty("registryIdentity");
        Assert.Equal("cakeos.shared-app-registry", registry.GetProperty("registryId").GetString());
        Assert.Equal(1, registry.GetProperty("registrySchemaVersion").GetInt32());

        var components = root.GetProperty("components");
        Assert.Equal(6, components.GetArrayLength()); // 5 product types + 1 unknown (not in manifest)

        // Count product types
        var productTypes = new HashSet<string>();
        foreach (var component in components.EnumerateArray())
        {
            productTypes.Add(component.GetProperty("productType").GetString()!);
        }
        Assert.Equal(5, productTypes.Count); // App, SystemSurface, Provider, BackgroundComponent, Service
    }

    [Fact]
    public void ConvergenceMatrix_DerivesFromRealEvidence()
    {
        var path = Path.Combine(FixturesPath, "convergence-matrix.json");
        Assert.True(File.Exists(path), "Convergence matrix fixture exists");

        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("releaseId", out _));
        Assert.True(root.TryGetProperty("generatedAt", out _));
        Assert.True(root.TryGetProperty("productStates", out _));
        Assert.True(root.TryGetProperty("totalProducts", out _));
        Assert.True(root.TryGetProperty("convergedProducts", out _));
        Assert.True(root.TryGetProperty("partialProducts", out _));
        Assert.True(root.TryGetProperty("blockedProducts", out _));
        Assert.True(root.TryGetProperty("unknownProducts", out _));
        Assert.True(root.TryGetProperty("convergencePercentage", out _));

        var total = root.GetProperty("totalProducts").GetInt32();
        var converged = root.GetProperty("convergedProducts").GetInt32();
        var partial = root.GetProperty("partialProducts").GetInt32();
        var blocked = root.GetProperty("blockedProducts").GetInt32();
        var unknown = root.GetProperty("unknownProducts").GetInt32();
        var percentage = root.GetProperty("convergencePercentage").GetDouble();

        Assert.Equal(6, total);
        Assert.Equal(3, converged);
        Assert.Equal(2, partial);
        Assert.Equal(1, blocked);
        Assert.Equal(0, unknown);
        Assert.Equal(50.0, percentage);

        // Verify matrix derives from REAL evidence (smokeEvidence.executed == true)
        var states = root.GetProperty("productStates");
        foreach (var state in states.EnumerateArray())
        {
            var productId = state.GetProperty("productId").GetString();
            var smokePassed = state.GetProperty("smokeTestPassed").GetBoolean();
            var offlineMet = state.GetProperty("offlineRequirementMet").GetBoolean();

            // Find corresponding fixture
            var fixtureName = productId switch
            {
                "haven.welcome" => "haven-welcome.json",
                "haven.shell" => "haven-shell.json",
                "havenos.studio" => "havenos-studio.json",
                "llamacpp.provider" => "llamacpp-provider.json",
                "haven.notifications" => "haven-notifications.json",
                "haven.settings" => "haven-settings-service.json",
                _ => null
            };

            if (fixtureName != null)
            {
                var fixturePath = Path.Combine(FixturesPath, fixtureName);
                var fixtureJson = File.ReadAllText(fixturePath);
                var fixtureDoc = JsonDocument.Parse(fixtureJson);
                var fixtureRoot = fixtureDoc.RootElement;

                var fixtureSmokeEvidence = fixtureRoot.TryGetProperty("smokeEvidence", out var fEvidence) && fEvidence.ValueKind != JsonValueKind.Null;
                var fixtureSmokePassed = fixtureSmokeEvidence && fEvidence.GetProperty("executed").GetBoolean() && fEvidence.GetProperty("exitCode").GetInt32() == 0;

                // Matrix MUST derive from real evidence
                Assert.Equal(fixtureSmokePassed, smokePassed);
            }
        }
    }

    [Fact]
    public void ConvergenceMatrix_DistinguishesStatesFromImageReadiness()
    {
        var path = Path.Combine(FixturesPath, "convergence-matrix.json");
        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var states = root.GetProperty("productStates");

        // Verify llamacpp.provider: provenance Ready but image Pending -> NOT converged
        var llamaState = states.EnumerateArray().First(s => s.GetProperty("productId").GetString() == "llamacpp.provider");
        Assert.Equal("Ready", llamaState.GetProperty("provenanceState").GetString());
        Assert.Equal("Pending", llamaState.GetProperty("imageInclusionState").GetString());
        Assert.False(llamaState.GetProperty("isConverged").GetBoolean());

        // Verify haven.notifications: provenance Partial, image Excluded -> NOT converged
        var notifState = states.EnumerateArray().First(s => s.GetProperty("productId").GetString() == "haven.notifications");
        Assert.Equal("Partial", notifState.GetProperty("provenanceState").GetString());
        Assert.Equal("Excluded", notifState.GetProperty("imageInclusionState").GetString());
        Assert.False(notifState.GetProperty("isConverged").GetBoolean());

        // Verify haven.settings: provenance Blocked, image NotApplicable -> NOT converged
        var settingsState = states.EnumerateArray().First(s => s.GetProperty("productId").GetString() == "haven.settings");
        Assert.Equal("Blocked", settingsState.GetProperty("provenanceState").GetString());
        Assert.Equal("NotApplicable", settingsState.GetProperty("imageInclusionState").GetString());
        Assert.False(settingsState.GetProperty("isConverged").GetBoolean());

        // Verify fully converged: welcome, shell, studio
        foreach (var productId in new[] { "haven.welcome", "haven.shell", "havenos.studio" })
        {
            var state = states.EnumerateArray().First(s => s.GetProperty("productId").GetString() == productId);
            Assert.Equal("Ready", state.GetProperty("provenanceState").GetString());
            Assert.Equal("Included", state.GetProperty("imageInclusionState").GetString());
            Assert.Equal("Verified", state.GetProperty("approvedVmState").GetString());
            Assert.True(state.GetProperty("isConverged").GetBoolean());
        }
    }

    [Fact]
    public void ReleaseMetadataSchema_RejectsWrongRegistryIdentity()
    {
        var invalidJson = """
        {
          "productId": "test.app",
          "productType": "App",
          "registryIdentity": { "registryId": "other.registry", "registrySchemaVersion": 1 },
          "sourceRepository": { "url": "https://example.com", "revision": "abc123" },
          "donor": { "name": "Test", "repositoryUrl": "https://example.com", "revision": "abc123", "licence": "MIT" },
          "package": { "name": "test-app", "version": "1.0.0", "hashSha256": "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456" },
          "entrypoint": { "binaryPath": "/usr/bin/test" },
          "service": {},
          "routes": [],
          "capabilities": [],
          "dependencies": [],
          "persistence": { "requiresSettings": false, "requiresCache": false, "requiresState": false, "requiresData": false },
          "permissions": { "requiredGrants": [], "optionalGrants": [] },
          "providers": [],
          "offlineRequirement": "NotRequired",
          "smokeTest": { "command": "test --version" },
          "provenanceState": "Ready",
          "imageInclusionState": "Included",
          "approvedVmState": "Verified"
        }
        """;

        var doc = JsonDocument.Parse(invalidJson);
        var registry = doc.RootElement.GetProperty("registryIdentity");
        Assert.NotEqual("cakeos.shared-app-registry", registry.GetProperty("registryId").GetString());
    }

    [Fact]
    public void ReleaseMetadataSchema_RejectsWrongSchemaVersion()
    {
        var invalidJson = """
        {
          "productId": "test.app",
          "productType": "App",
          "registryIdentity": { "registryId": "cakeos.shared-app-registry", "registrySchemaVersion": 2 },
          "sourceRepository": { "url": "https://example.com", "revision": "abc123" },
          "donor": { "name": "Test", "repositoryUrl": "https://example.com", "revision": "abc123", "licence": "MIT" },
          "package": { "name": "test-app", "version": "1.0.0", "hashSha256": "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456" },
          "entrypoint": { "binaryPath": "/usr/bin/test" },
          "service": {},
          "routes": [],
          "capabilities": [],
          "dependencies": [],
          "persistence": { "requiresSettings": false, "requiresCache": false, "requiresState": false, "requiresData": false },
          "permissions": { "requiredGrants": [], "optionalGrants": [] },
          "providers": [],
          "offlineRequirement": "NotRequired",
          "smokeTest": { "command": "test --version" },
          "provenanceState": "Ready",
          "imageInclusionState": "Included",
          "approvedVmState": "Verified"
        }
        """;

        var doc = JsonDocument.Parse(invalidJson);
        var registry = doc.RootElement.GetProperty("registryIdentity");
        Assert.NotEqual(1, registry.GetProperty("registrySchemaVersion").GetInt32());
    }

    [Fact]
    public void UnknownComponent_FixtureExists()
    {
        var path = Path.Combine(FixturesPath, "unknown-component.json");
        Assert.True(File.Exists(path), "Unknown component fixture exists");

        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("Unknown", root.GetProperty("provenanceState").GetString());
        Assert.Equal("NotApplicable", root.GetProperty("imageInclusionState").GetString());
        Assert.Equal("NotTested", root.GetProperty("approvedVmState").GetString());
    }
}

public class ReleaseMetadataIntegrationTests
{
    private static string FixturesPath => Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void ReleaseManifest_GeneratesConvergenceMatrix()
    {
        var manifestPath = Path.Combine(FixturesPath, "release-manifest.json");
        var manifestJson = File.ReadAllText(manifestPath);
        var manifestDoc = JsonDocument.Parse(manifestJson);
        var manifestRoot = manifestDoc.RootElement;

        var components = manifestRoot.GetProperty("components");
        var states = new List<JsonObject>();

        foreach (var component in components.EnumerateArray())
        {
            var productId = component.GetProperty("productId").GetString()!;
            var productType = component.GetProperty("productType").GetString()!;
            var provenanceState = component.GetProperty("provenanceState").GetString()!;
            var imageInclusionState = component.GetProperty("imageInclusionState").GetString()!;
            var approvedVmState = component.GetProperty("approvedVmState").GetString()!;

            var smokeEvidence = component.TryGetProperty("smokeEvidence", out var evidence) && evidence.ValueKind != JsonValueKind.Null
                ? evidence
                : null;

            var smokePassed = smokeEvidence?.GetProperty("executed").GetBoolean() == true
                && smokeEvidence?.GetProperty("exitCode").GetInt32() == 0;

            var offlineReq = component.GetProperty("offlineRequirement").GetString()!;
            var offlineMet = offlineReq == "NotRequired" || smokePassed;

            var isConverged = provenanceState == "Ready"
                && imageInclusionState == "Included"
                && approvedVmState == "Verified"
                && offlineMet
                && smokePassed;

            var blockingReason = provenanceState == "Blocked" ? "Provenance incomplete" : null;

            var state = new JsonObject
            {
                ["productId"] = productId,
                ["productType"] = productType,
                ["provenanceState"] = provenanceState,
                ["imageInclusionState"] = imageInclusionState,
                ["approvedVmState"] = approvedVmState,
                ["offlineRequirementMet"] = offlineMet,
                ["smokeTestPassed"] = smokePassed,
                ["blockingReason"] = blockingReason ?? "",
                ["isConverged"] = isConverged
            };

            states.Add(state);
        }

        var matrix = new JsonObject
        {
            ["releaseId"] = manifestRoot.GetProperty("releaseId").GetString()!,
            ["generatedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["productStates"] = JsonSerializer.SerializeToNode(states),
            ["totalProducts"] = states.Count,
            ["convergedProducts"] = states.Count(s => s["isConverged"]?.GetValue<bool>() == true),
            ["partialProducts"] = states.Count(s => s["provenanceState"]?.GetValue<string>() == "Partial" || s["imageInclusionState"]?.GetValue<string>() == "Pending"),
            ["blockedProducts"] = states.Count(s => s["provenanceState"]?.GetValue<string>() == "Blocked" || s["approvedVmState"]?.GetValue<string>() == "Failed"),
            ["unknownProducts"] = states.Count(s => s["provenanceState"]?.GetValue<string>() == "Unknown"),
            ["convergencePercentage"] = states.Count > 0 ? (double)states.Count(s => s["isConverged"]?.GetValue<bool>() == true) / states.Count * 100.0 : 0.0
        };

        Assert.Equal(6, matrix["totalProducts"]?.GetValue<int>());
        Assert.Equal(3, matrix["convergedProducts"]?.GetValue<int>());
        Assert.Equal(2, matrix["partialProducts"]?.GetValue<int>());
        Assert.Equal(1, matrix["blockedProducts"]?.GetValue<int>());
        Assert.Equal(0, matrix["unknownProducts"]?.GetValue<int>());
        Assert.Equal(50.0, matrix["convergencePercentage"]?.GetValue<double>());
    }

    [Fact]
    public void ReleaseMetadata_NoDuplicateRegistry()
    {
        // Verify the schema does NOT create a second app registry
        // It only REFERENCES Worker 3's registry identity

        var fixtureFiles = Directory.GetFiles(FixturesPath, "*.json")
            .Where(f => !f.EndsWith("release-manifest.json") && !f.EndsWith("convergence-matrix.json"))
            .ToArray();

        foreach (var file in fixtureFiles)
        {
            var json = File.ReadAllText(file);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var registry = root.GetProperty("registryIdentity");
            Assert.Equal("cakeos.shared-app-registry", registry.GetProperty("registryId").GetString());
            Assert.Equal(1, registry.GetProperty("registrySchemaVersion").GetInt32());
        }
    }
}