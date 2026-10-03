using System.Globalization;
using System.Text.Json;
using HavenOS.Home.Core;

// Hosted TEST TOOL only: typed FileHome profile replacement executed as the isolated
// target test user. It never enrolls a publisher, installs a package, starts Home or
// claims a controlled-launch receipt. Production packages must exclude this tool.
if (!OperatingSystem.IsLinux()) return 1;
if (args.Length > 0 && (args[0] == SyntheticInstalledHomePackageProducer.Command ||
    args[0] == SyntheticInstalledHomePackageProducer.OwnerCommand ||
    args[0] == SyntheticInstalledHomePackageProducer.HelperCommand))
    return await SyntheticInstalledHomePackageProducer.RunAsync(args);
if (args.Length > 0 && args[0] == "--observe-home-state-path")
{
    if (args.Length != 2 || !uint.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var observedUid) ||
        observedUid == 0 || observedUid.ToString(CultureInfo.InvariantCulture) != args[1]) return 1;
    var observedPrincipal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(default);
    if (observedPrincipal != "unix-euid:" + args[1]) return 1;
    // Exact maintained FileHome.CreateDefault directory API/suffix, observed at runtime
    // as the real target user. This mode creates no directory, state, profile or actor.
    var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    if (string.IsNullOrWhiteSpace(localData) || string.IsNullOrWhiteSpace(applicationData) ||
        !Path.IsPathFullyQualified(localData) || !Path.IsPathFullyQualified(applicationData)) return 1;
    Console.WriteLine(JsonSerializer.Serialize(new { code = "ActualTargetUserHomePathObserved", observedPrincipal,
        homeStatePath = Path.GetFullPath(Path.Combine(localData, "9to1", "Home", "home-core-state.json")),
        localData, applicationData, wroteHome = false, grantedActor = false, installedAccepted = false }));
    return 0;
}
if (args.Length != 4 || !Path.IsPathFullyQualified(args[0]) || Path.GetFullPath(args[0]) != args[0] ||
    !uint.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expectedUid) || expectedUid == 0 ||
    !Guid.TryParseExact(args[2], "D", out var oldId) || !Guid.TryParseExact(args[3], "D", out var newId) ||
    oldId == Guid.Empty || newId == Guid.Empty || oldId == newId)
    return 1;
var principals = new OperatingSystemPrincipalSource();
if (await principals.GetPrincipalAsync(default) != "unix-euid:" + expectedUid.ToString(CultureInfo.InvariantCulture)) return 1;
var store = new FileHomeCoreStateStore(args[0]); var read = await store.ReadAsync();
if (!read.IsSuccess) return 1;
var records = read.State!.Records.Where(r => r.RecordId == "home.local-profile").ToArray();
if (records.Length != 1) return 1;
var record = records[0]; var profile = record.Payload.Deserialize<HomeLocalProfile>();
if (profile is null || profile.ProfileId != oldId || record.RecordType != "home.local-profile" || record.SchemaVersion != 1 ||
    record.Scope != HomeDataScope.DeviceLocal || record.Authority != HomeRecordAuthority.LocalCanonical) return 1;
if (await principals.GetPrincipalAsync(default) != "unix-euid:" + expectedUid.ToString(CultureInfo.InvariantCulture)) return 1;
var written = await store.WriteAsync(record with { Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = newId }) }, record.Revision);
if (!written.IsSuccess) return 1;
var after = await store.ReadAsync();
return after.IsSuccess && after.State!.Records.Single(r => r.RecordId == record.RecordId)
    .Payload.Deserialize<HomeLocalProfile>()?.ProfileId == newId ? 0 : 1;
