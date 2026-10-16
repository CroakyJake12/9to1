using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Memory;
using NineToOne.Dulche.Context;

return await ProtectTool.RunAsync(args);

internal static class ProtectTool
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "help")
            {
                Console.WriteLine("""
                    9to1 UCML trusted offline encryption utility (Linux)
                    init-key --store PRIVATE_DIRECTORY --owner OWNER --output KEY_REFERENCE_JSON
                    seal --input UCML --store PRIVATE_DIRECTORY --key-ref KEY_REFERENCE_JSON --output ENVELOPE
                    unseal --input ENVELOPE --store PRIVATE_DIRECTORY --owner OWNER --output UCML

                    Run directly as the file owner, never as an AI model tool or remote service.
                    The key reference contains only a non-secret identifier. Raw keys stay in the
                    existing LinuxProviderSecretStore. Keep the private store for future recovery.
                    Existing outputs are never overwritten. No networking or live memory mutation.
                    """);
                return 0;
            }
            if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This host uses the existing Linux credential owner.");
            if (args[0] is not ("init-key" or "seal" or "unseal")) throw new ArgumentException("Unknown command.");
            var options = ParseOptions(args.Skip(1).ToArray());
            var allowed = args[0] switch
            {
                "init-key" => new[] { "store", "owner", "output" },
                "seal" => ["input", "store", "key-ref", "output"],
                _ => ["input", "store", "owner", "output"]
            };
            if (options.Count != allowed.Length || options.Keys.Except(allowed, StringComparer.Ordinal).Any())
                throw new ArgumentException("Supply the exact documented options.");
            var output = Path.GetFullPath(Required(options, "output"));
            // Reject obvious collisions before any explicit provisioning operation.
            if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output already exists.");
            var storePath = Path.GetFullPath(Required(options, "store"));
            if (args[0] == "init-key")
            {
                Directory.CreateDirectory(storePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            else if (!Directory.Exists(storePath)) throw new DirectoryNotFoundException("The retained credential store is required.");
            using var secretStore = new LinuxProviderSecretStore(new LocalPaths(storePath));
            var custody = new ProviderSecretUcmlKeyCustody(secretStore);
            var protection = new UcmlProtection(custody);
            switch (args[0])
            {
                case "init-key":
                    var created = await custody.CreateKeyAsync(Required(options, "owner"));
                    WriteNew(output, JsonSerializer.Serialize(created, Json));
                    Console.WriteLine("Created a retained private key and saved its non-secret reference.");
                    break;
                case "seal":
                    var key = JsonSerializer.Deserialize<UcmlKeyReference>(ReadBounded(Required(options, "key-ref"), 4096), Json)
                        ?? throw new FormatException("Missing key reference.");
                    var doc = UcmlCodec.Parse(ReadBounded(Required(options, "input"), UcmlCodec.MaximumDocumentBytes));
                    var encrypted = await protection.SealAsync(doc, key.OwnerId, key, DateTimeOffset.UtcNow);
                    WriteNew(output, encrypted);
                    Console.WriteLine("Sealed UCML; plaintext input retained.");
                    break;
                case "unseal":
                    var plain = await protection.UnsealAsync(ReadBounded(Required(options, "input"), UcmlProtection.MaximumEnvelopeBytes), Required(options, "owner"));
                    WriteNew(output, UcmlCodec.Serialize(plain));
                    Console.WriteLine("Verified and unsealed UCML into a new private file.");
                    break;
            }
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or
            FormatException or JsonException or CryptographicException or AggregateException or NotSupportedException)
        {
            // Credential-owner exceptions can include underlying details; never print them.
            Console.Error.WriteLine($"UCML protection refused ({error.GetType().Name}). Check options, file ownership, and retained key store. No key material is logged.");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) || !result.TryAdd(args[i][2..], args[i + 1]))
                throw new ArgumentException("Options must be unique pairs.");
        return result;
    }
    private static string Required(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Missing option.");
    private static string ReadBounded(string path, int maximum)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > maximum) throw new FormatException("Input exceeds its limit.");
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            if (bytes.Length + count > maximum) throw new FormatException("Input exceeds its limit.");
            bytes.Write(buffer, 0, count);
        }
        return Utf8.GetString(bytes.ToArray()).TrimStart('\uFEFF');
    }
    private static void WriteNew(string path, string text)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite };
        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream, Utf8);
        writer.Write(text);
    }
    private sealed record LocalPaths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "unused-browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "unused-attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "unused-logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "unused-state");
    }
}
