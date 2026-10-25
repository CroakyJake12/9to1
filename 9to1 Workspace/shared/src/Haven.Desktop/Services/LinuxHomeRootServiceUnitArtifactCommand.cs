#if !ANDROID
using System.Text.Json;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Explicit private deployment-artifact export only. Never registers,
/// enables or starts systemd or falls through to normal GUI bootstrap.</summary>
internal static class LinuxHomeRootServiceUnitArtifactCommand
{
    internal const string Switch = "--home-export-root-unit";
    internal static bool IsInvocation(string[] args) => args.Length != 0 && args[0] == Switch;
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (!IsInvocation(args) || !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The explicit Linux unit artifact export is required.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal); var selected = new List<string>();
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "--machine-state", "--publisher-certificate", "--publisher-sha256",
                "--signed-catalogue", "--catalogue-sha256", "--issuer-key-id", "--abi", "--operation-id", "--activation-parent", "--stage-leaf", "--plan",
                "--control-directory", "--artifact-output", "--artifact-id" };
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length) throw new ArgumentException("Every artifact export option requires its exact value.");
                if (args[index] == "--select") { selected.Add(args[index + 1]); continue; }
                if (!allowed.Contains(args[index]) || !values.TryAdd(args[index], args[index + 1])) throw new ArgumentException("Artifact options must be known and unique; only --select may repeat.");
            }
            string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Missing explicit artifact option: " + key);
            var statePath = Required("--machine-state");
            var signing = new LinuxHomePublisherEnrollmentOwner.Request(Required("--publisher-certificate"), Required("--publisher-sha256"),
                Required("--signed-catalogue"), Required("--catalogue-sha256"), Required("--issuer-key-id"), Required("--abi"), selected, Guid.Parse(Required("--operation-id")));
            var activation = new LinuxHomeBootstrapActivationOwner.Request(signing, Required("--activation-parent"), Required("--stage-leaf"), Required("--plan"));
            if (new[] { statePath, activation.ExistingActivationParent, activation.PlanPath, signing.PublicCertificatePath, signing.SignedCataloguePath,
                Required("--control-directory"), Required("--artifact-output") }.Any(path => !Path.IsPathFullyQualified(path)))
                throw new ArgumentException("Artifact export requires explicit absolute original paths.");
            var store = new FileHomeCoreStateStore(statePath); var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var publisher = new LinuxHomePublisherEnrollmentOwner(store, new HomePackageDatabase(store), profiles, statePath);
            var exporter = new LinuxHomeRootServiceUnitArtifactOwner(new(publisher));
            var result = exporter.ExportAsync(new(activation, Required("--control-directory"), Required("--artifact-output"), Guid.Parse(Required("--artifact-id"))),
                CancellationToken.None).GetAwaiter().GetResult();
            output.WriteLine(JsonSerializer.Serialize(new { code = "LinuxRootUnitArtifactExportedForReview", result.ArtifactId, result.BootstrapOperationId,
                result.ArtifactDirectory, result.UnitSha256, result.ProvenanceSha256, serviceInstalled = false, serviceStarted = false, runtimeReady = false }));
            return 0;
        }
        catch (LinuxHomeRootServiceUnitArtifactOwner.ExportUnconfirmedException cause)
        {
            error.WriteLine(JsonSerializer.Serialize(new { code = "LinuxRootUnitArtifactUnconfirmed", cause.ObservedArtifactDirectory,
                preserveForReview = true, serviceInstalled = false, serviceStarted = false, runtimeReady = false })); return 2;
        }
        catch (Exception cause)
        {
            error.WriteLine("Linux Root unit artifact export failed: " + cause.GetType().Name + ": " + cause.Message);
            error.WriteLine(JsonSerializer.Serialize(new { code = "LinuxRootUnitArtifactNotConfirmed", serviceInstalled = false, serviceStarted = false, runtimeReady = false })); return 1;
        }
    }
}
#endif
