#if !ANDROID
using System.Runtime.InteropServices;
using System.Text.Json;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Registered systemd service entry only. Does not install/register/elevate,
/// enroll, activate or recover packages, and never falls through to the GUI.</summary>
internal static class LinuxHomeRootServiceCommand
{
    internal const string Switch = "--home-root-service";
    internal static bool IsInvocation(string[] args) => args.Length != 0 && args[0] == Switch;
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (!IsInvocation(args) || !OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The explicit registered Linux Root service entry is required.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal); var selected = new List<string>();
            var allowed = new HashSet<string>(StringComparer.Ordinal) { "--machine-state", "--publisher-certificate", "--publisher-sha256",
                "--signed-catalogue", "--catalogue-sha256", "--issuer-key-id", "--abi", "--operation-id", "--activation-parent", "--stage-leaf", "--plan", "--control-directory" };
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length) throw new ArgumentException("Every registered Root option requires its exact value.");
                if (args[index] == "--select") { selected.Add(args[index + 1]); continue; }
                if (!allowed.Contains(args[index]) || !values.TryAdd(args[index], args[index + 1])) throw new ArgumentException("Root options must be known and unique; only --select may repeat.");
            }
            string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ArgumentException("Missing explicit Root option: " + key);
            var statePath = Required("--machine-state"); var parent = Required("--activation-parent"); var plan = Required("--plan"); var control = Required("--control-directory");
            var signing = new LinuxHomePublisherEnrollmentOwner.Request(Required("--publisher-certificate"), Required("--publisher-sha256"),
                Required("--signed-catalogue"), Required("--catalogue-sha256"), Required("--issuer-key-id"), Required("--abi"), selected, Guid.Parse(Required("--operation-id")));
            if (new[] { statePath, parent, plan, control, signing.PublicCertificatePath, signing.SignedCataloguePath }.Any(path => !Path.IsPathFullyQualified(path)))
                throw new ArgumentException("All registered Root configuration paths must be explicitly absolute.");
            var store = new FileHomeCoreStateStore(statePath); var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var publisher = new LinuxHomePublisherEnrollmentOwner(store, new HomePackageDatabase(store), profiles, statePath);
            var runtime = new LinuxHomeRootServiceRuntime(new(publisher));
            using var lifetime = new CancellationTokenSource();
            using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; lifetime.Cancel(); });
            using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context => { context.Cancel = true; lifetime.Cancel(); });
            try
            {
                var run = runtime.RunOriginalAsync(new(new(signing, parent, Required("--stage-leaf"), plan), control), lifetime.Token);
                var startup = runtime.OriginalStartup.GetAwaiter().GetResult();
                output.WriteLine(JsonSerializer.Serialize(new { code = startup.HasAcceptedOriginalHomeControl ? "LinuxRootOriginalHomeControlAccepted" : "LinuxRootPrerequisiteUnavailable",
                    startup.MissingPrerequisite, runtimeReady = "NOT_ESTABLISHED_BY_CONTROL_ACCEPTANCE" }));
                run.GetAwaiter().GetResult(); return startup.HasAcceptedOriginalHomeControl ? 0 : 3;
            }
            finally { runtime.CloseAndDrainAsync().GetAwaiter().GetResult(); }
        }
        catch (Exception cause)
        {
            error.WriteLine("Linux registered Root failed: " + cause.GetType().Name + ": " + cause.Message);
            error.WriteLine(JsonSerializer.Serialize(new { code = "LinuxRootServiceNotConfirmed", runtimeReady = false })); return 1;
        }
    }
}
#endif
