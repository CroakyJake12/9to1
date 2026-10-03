#if ASTRA_HOME_NATIVE_PROBE || ASTRA_FORMS_NATIVE_PROBE
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haven.Desktop.Validation;

/// <summary>Validation-only fresh data isolation witness. This does not authenticate an account,
/// grant Home authority, observe a UI or prove process/callback/native cleanup.</summary>
internal sealed class NativeProbeProfileWitness
{
    private readonly string _root;
    private readonly string _nonce;
    private readonly string _witnessSha256;
    private readonly int _producerPid;
    private readonly string _producerStartTicks;

    private NativeProbeProfileWitness(string root, string nonce, string witnessSha256, int producerPid, string producerStartTicks)
        => (_root, _nonce, _witnessSha256, _producerPid, _producerStartTicks) = (root, nonce, witnessSha256, producerPid, producerStartTicks);

    internal static NativeProbeProfileWitness RequireFreshOriginalProducerProfile()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original native profile producer requires Linux.");
        var root = RequiredEnvironment("ASTRA_NATIVE_PROBE_PROFILE_ROOT");
        if (root != Path.GetFullPath(root) || !root.StartsWith("/tmp/astra-native-probe-", StringComparison.Ordinal)
            || Path.GetDirectoryName(root) != "/tmp")
            throw new InvalidDataException("The original producer must supply one fresh direct temporary profile.");
        var nonce = RequiredEnvironment("ASTRA_NATIVE_PROBE_PROFILE_NONCE");
        var witnessSha256 = RequiredEnvironment("ASTRA_NATIVE_PROBE_PROFILE_WITNESS_SHA256");
        RequireHex(nonce); RequireHex(witnessSha256);
        var path = Path.Combine(root, "producer-witness.json");
        RequirePrivateDirectory(root);
        RequirePrivateFile(path);
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length > 4096 || Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != witnessSha256)
            throw new InvalidDataException("The original producer witness differs from its exact launch environment.");
        var witness = JsonSerializer.Deserialize<ProducerWitness>(bytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        }) ?? throw new InvalidDataException("The original producer witness is absent.");
        if (witness.SchemaVersion != 1 || witness.Root != root || witness.Nonce != nonce || witness.ProducerPid <= 1
            || string.IsNullOrEmpty(witness.ProducerStartTicks) || !witness.ProducerStartTicks.All(char.IsAsciiDigit))
            throw new InvalidDataException("The original profile witness identity is invalid.");
        var original = new NativeProbeProfileWitness(root, nonce, witnessSha256, witness.ProducerPid, witness.ProducerStartTicks);
        original.RequireCurrentOriginalProducerProfile();
        var expected = new[] { "cache", "config", "data", "haven", "producer-witness.json" };
        if (!Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal).SequenceEqual(expected))
            throw new InvalidDataException("The original profile contains unexpected pre-existing entries.");
        foreach (var child in expected.Take(4))
            if (Directory.EnumerateFileSystemEntries(Path.Combine(root, child)).Any())
                throw new InvalidDataException("A fresh empty original data directory is required before App initialization.");
        return original;
    }

    internal void RequireInitializedAppDataDirectory(string actualDirectory)
    {
        RequireCurrentOriginalProducerProfile();
        if (actualDirectory != Path.Combine(_root, "haven"))
            throw new InvalidDataException("The actual normal AppPaths do not use the original isolated profile.");
    }

    internal void RequireCurrentOriginalProducerProfile()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The original native profile producer requires Linux.");
        if (RequiredEnvironment("ASTRA_NATIVE_PROBE_PROFILE_ROOT") != _root
            || RequiredEnvironment("ASTRA_NATIVE_PROBE_PROFILE_NONCE") != _nonce
            || RequiredEnvironment("ASTRA_NATIVE_PROBE_PROFILE_WITNESS_SHA256") != _witnessSha256)
            throw new InvalidDataException("The original profile launch environment changed.");
        RequirePrivateDirectory(_root);
        foreach (var (variable, child) in new[] { ("XDG_DATA_HOME", "data"), ("XDG_CONFIG_HOME", "config"),
                     ("XDG_CACHE_HOME", "cache"), ("HAVEN_DATA_DIR", "haven") })
        {
            var expected = Path.Combine(_root, child);
            if (RequiredEnvironment(variable) != expected)
                throw new InvalidDataException("The actual data environment differs from the original producer profile.");
            RequirePrivateDirectory(expected);
        }
        if (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) != Path.Combine(_root, "data")
            || Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) != Path.Combine(_root, "config"))
            throw new InvalidDataException("The actual operating-system application data paths are not isolated.");
        var witness = Path.Combine(_root, "producer-witness.json");
        RequirePrivateFile(witness);
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(witness))).ToLowerInvariant() != _witnessSha256)
            throw new InvalidDataException("The original producer witness changed.");
        var selfFields = ProcessFields("/proc/self/stat");
        if (int.Parse(selfFields[1], CultureInfo.InvariantCulture) != _producerPid
            || ProcessFields("/proc/" + _producerPid.ToString(CultureInfo.InvariantCulture) + "/stat")[19] != _producerStartTicks)
            throw new InvalidDataException("The actual original direct producer PID/birth no longer matches.");
    }

    private static string RequiredEnvironment(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
            : throw new InvalidDataException("The original profile environment is missing: " + name);

    private static void RequireHex(string value)
    {
        if (value.Length != 64 || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvalidDataException("The original profile witness requires exact lowercase SHA/nonce values.");
    }

    private static string[] ProcessFields(string path)
    {
        var value = File.ReadAllText(path); var end = value.LastIndexOf(')');
        if (end < 0) throw new InvalidDataException("The actual original process birth record is invalid.");
        var fields = value[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 20) throw new InvalidDataException("The actual original process birth record is truncated.");
        return fields;
    }

    private static void RequirePrivateDirectory(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || directory.LinkTarget is not null || (directory.Attributes & FileAttributes.ReparsePoint) != 0
            || File.GetUnixFileMode(path) != (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute))
            throw new InvalidDataException("The original private profile directory is absent, linked or not mode0700.");
    }

    private static void RequirePrivateFile(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var file = new FileInfo(path);
        if (!file.Exists || file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || File.GetUnixFileMode(path) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            throw new InvalidDataException("The original private producer witness is absent, linked or not mode0600.");
    }

    private sealed record ProducerWitness(int SchemaVersion, string Root, string Nonce, int ProducerPid, string ProducerStartTicks);
}
#endif
