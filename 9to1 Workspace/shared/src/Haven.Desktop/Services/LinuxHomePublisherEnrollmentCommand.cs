#if !ANDROID
using System.Text.Json;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Explicit installer administration entry, before ordinary GUI startup.
/// Reads only a public certificate and externally signed catalogue; it never creates
/// a key, elevates, installs a package or claims native Home/Root readiness.</summary>
internal static class LinuxHomePublisherEnrollmentCommand
{
    internal const string Switch = "--home-enroll-publisher";
    internal static bool IsInvocation(string[] args) => args.Length != 0 && args[0] == Switch;
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        Guid? operationId = null;
        try
        {
            if (!IsInvocation(args)) throw new ArgumentException("The explicit Linux publisher enrollment command is required.");
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux administrative enrollment requires Linux.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var selected = new List<string>();
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "--machine-state", "--publisher-certificate", "--publisher-sha256",
                "--signed-catalogue", "--catalogue-sha256", "--issuer-key-id", "--abi", "--operation-id" };
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length) throw new ArgumentException("Every enrollment option requires its exact value.");
                if (args[index] == "--select") { selected.Add(args[index + 1]); continue; }
                if (!allowed.Contains(args[index]) || !values.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException("Enrollment options must be known and unique; only --select may repeat.");
            }
            string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Missing explicit enrollment option: " + key);
            var statePath = Required("--machine-state");
            if (!Path.IsPathFullyQualified(statePath)) throw new ArgumentException("The actual canonical machine-state file must be an absolute path.");
            var operation = Guid.Parse(Required("--operation-id"));
            operationId = operation;
            var request = new LinuxHomePublisherEnrollmentOwner.Request(Required("--publisher-certificate"), Required("--publisher-sha256"),
                Required("--signed-catalogue"), Required("--catalogue-sha256"), Required("--issuer-key-id"), Required("--abi"), selected, operation);
            if (!Path.IsPathFullyQualified(request.PublicCertificatePath) || !Path.IsPathFullyQualified(request.SignedCataloguePath))
                throw new ArgumentException("Public signer and signed catalogue inputs must have explicit absolute paths.");
            var store = new FileHomeCoreStateStore(statePath);
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var owner = new LinuxHomePublisherEnrollmentOwner(store, new HomePackageDatabase(store), profiles, statePath);
            var result = owner.EnrollAsync(request, CancellationToken.None).GetAwaiter().GetResult();
            output.WriteLine(JsonSerializer.Serialize(new { code = result.AlreadyEnrolled ? "PublisherEnrollmentAlreadyStored" : "PublisherEnrollmentStored",
                registryRevision = result.RegistryRevision, publisherCertificateSha256 = result.Enrollment.PublisherCertificateSha256,
                catalogueRevision = result.Enrollment.CatalogueRevision, catalogueSha256 = result.Enrollment.CatalogueSha256,
                operationId = result.Enrollment.OriginalEnrollmentOperationId, installedPeerAdmission = "NOT_ESTABLISHED", packageActivation = "NOT_PERFORMED" }));
            return 0;
        }
        catch (LinuxHomePublisherEnrollmentOwner.EnrollmentCommitUnconfirmedException cause)
        {
            error.WriteLine(JsonSerializer.Serialize(new { code = "PublisherEnrollmentCommitUnconfirmed",
                operationId = cause.KnownCommittedResult.Enrollment.OriginalEnrollmentOperationId,
                registryRevision = cause.KnownCommittedResult.RegistryRevision, recoveryRequired = true }));
            return 2;
        }
        catch (Exception cause)
        {
            // No generic Desktop bootstrap, default app paths or GUI error screen is
            // composed for a refused explicit administrative command.
            error.WriteLine("Linux publisher enrollment failed: " + cause.GetType().Name + ": " + cause.Message);
            error.WriteLine(JsonSerializer.Serialize(new { code = "PublisherEnrollmentNotConfirmed", operationId, recoveryRequired = operationId is not null }));
            return 1;
        }
    }
}
#endif
