#if !ANDROID
using System.Text.Json;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Explicit initial-installer command. Never elevates, starts a service or
/// composes the ordinary Home GUI. Uses public signing inputs and existing enrollment.</summary>
internal static class LinuxHomeBootstrapActivationCommand
{
    internal const string Switch = "--home-activate-bootstrap";
    internal const string RecoverySwitch = "--home-recover-bootstrap";
    internal static bool IsInvocation(string[] args) => args.Length != 0 && args[0] is Switch or RecoverySwitch;
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        Guid? operationId = null;
        try
        {
            if (!IsInvocation(args)) throw new ArgumentException("The explicit Linux bootstrap activation command is required.");
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux bootstrap activation requires Linux.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal); var selected = new List<string>();
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "--machine-state", "--publisher-certificate", "--publisher-sha256",
                "--signed-catalogue", "--catalogue-sha256", "--issuer-key-id", "--abi", "--operation-id", "--activation-parent", "--stage-leaf", "--plan" };
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length) throw new ArgumentException("Every bootstrap option requires its exact value.");
                if (args[index] == "--select") { selected.Add(args[index + 1]); continue; }
                if (!allowed.Contains(args[index]) || !values.TryAdd(args[index], args[index + 1]))
                    throw new ArgumentException("Bootstrap options must be known and unique; only --select may repeat.");
            }
            string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Missing explicit bootstrap option: " + key);
            var statePath = Required("--machine-state"); var parent = Required("--activation-parent"); var plan = Required("--plan");
            var signing = new LinuxHomePublisherEnrollmentOwner.Request(Required("--publisher-certificate"), Required("--publisher-sha256"),
                Required("--signed-catalogue"), Required("--catalogue-sha256"), Required("--issuer-key-id"), Required("--abi"), selected,
                Guid.Parse(Required("--operation-id")));
            operationId = signing.OperationId;
            if (new[] { statePath, parent, plan, signing.PublicCertificatePath, signing.SignedCataloguePath }.Any(path => !Path.IsPathFullyQualified(path)))
                throw new ArgumentException("Every administrative input/state/code path must be explicitly absolute.");
            var store = new FileHomeCoreStateStore(statePath); var database = new HomePackageDatabase(store);
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var publisher = new LinuxHomePublisherEnrollmentOwner(store, database, profiles, statePath);
            var owner = new LinuxHomeBootstrapActivationOwner(publisher);
            var request = new LinuxHomeBootstrapActivationOwner.Request(signing, parent, Required("--stage-leaf"), plan);
            var recovery = args[0] == RecoverySwitch;
            var result = (recovery ? owner.RecoverAsync(request, CancellationToken.None) : owner.ActivateAsync(request, CancellationToken.None)).GetAwaiter().GetResult();
            output.WriteLine(JsonSerializer.Serialize(new { code = recovery ? "LinuxBootstrapRecoverySettledRuntimeUnavailable" : "LinuxBootstrapCodeActivatedRuntimeUnavailable", operationId = result.OperationId,
                result.ActivePath, result.RegistryRevision, result.PackageIds, result.AlreadySettled, installedPeerAdmission = "NOT_ESTABLISHED", rootService = "NOT_STARTED" }));
            return 0;
        }
        catch (LinuxHomeBootstrapActivationOwner.ActivationUnconfirmedException cause)
        {
            error.WriteLine(JsonSerializer.Serialize(new { code = "LinuxBootstrapActivationUnconfirmed", operationId = cause.OperationId,
                observedActivePath = cause.ObservedActivePath, recoveryRequired = true }));
            return 2;
        }
        catch (Exception cause)
        {
            error.WriteLine("Linux initial bootstrap activation failed: " + cause.GetType().Name + ": " + cause.Message);
            error.WriteLine(JsonSerializer.Serialize(new { code = "LinuxBootstrapActivationNotPerformed", operationId })); return 1;
        }
    }
}
#endif
