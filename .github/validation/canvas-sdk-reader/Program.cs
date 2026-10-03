using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
if (args.Length != 2) throw new ArgumentException("exact selected SDK directory and provider path required");
var sdk = Path.GetFullPath(args[0]);
var provider = Path.GetFullPath(args[1]);
if (!Directory.Exists(sdk) || !File.Exists(provider) ||
    !provider.StartsWith(sdk + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
    Path.GetFileName(provider) != "Microsoft.TestPlatform.TestHostRuntimeProvider.dll" ||
    new FileInfo(provider).LinkTarget is not null)
    throw new InvalidDataException("provider must be a regular exact SDK-bundle member");
byte[] Hash() { using var input = File.OpenRead(provider); return SHA256.HashData(input); }
var before = Hash();
var info = FileVersionInfo.GetVersionInfo(provider);
var after = Hash();
if (!CryptographicOperations.FixedTimeEquals(before, after)) throw new InvalidDataException("provider changed during metadata read");
Console.WriteLine(JsonSerializer.Serialize(new {
    providerPath = provider,
    sha256 = Convert.ToHexString(before).ToLowerInvariant(),
    productVersion = info.ProductVersion,
    fileVersion = info.FileVersion,
    assemblyExecuted = false,
    sourceLineageAccepted = false
}));
