using System.Reflection;
using System.Text.Json;
using NuGet.Packaging;
using NuGet.Versioning;

if (args.Length != 1) throw new ArgumentException("Exactly one original archive inventory is required.");
var inputs = JsonSerializer.Deserialize<ArchiveInput[]>(File.ReadAllBytes(args[0]))
    ?? throw new InvalidDataException("Original archive inventory is required.");
var results = new List<object>();
foreach (var input in inputs)
{
    using var reader = new PackageArchiveReader(input.Archive);
    var identity = reader.GetIdentity();
    if (!StringComparer.OrdinalIgnoreCase.Equals(identity.Id, input.PackageId)
        || !identity.Version.Equals(NuGetVersion.Parse(input.PackageVersion)))
        throw new InvalidDataException("Actual original archive identity/version differs from restored source inventory.");
    // Maintained NuGet API computes the unsigned reconstructed content hash for signed packages.
    // This is a content identity check, never a signature trust or signer authorization grant.
    var contentHash = reader.GetContentHash(CancellationToken.None);
    if (string.IsNullOrEmpty(contentHash)) throw new InvalidDataException("Actual maintained content hash unavailable.");
    results.Add(new { input.Archive, PackageId = identity.Id,
        PackageVersion = identity.Version.ToNormalizedString(), ContentHash = contentHash });
}
Console.WriteLine(JsonSerializer.Serialize(new { Results = results,
    NuGetAssembly = typeof(PackageArchiveReader).Assembly.Location,
    NuGetVersion = typeof(PackageArchiveReader).Assembly.GetName().Version?.ToString(),
    ValidatorAssembly = Assembly.GetExecutingAssembly().Location,
    Qualification = "Same original archive identity and maintained computed content hash only; signer trust is not established." }));
internal sealed record ArchiveInput(string Archive, string PackageId, string PackageVersion);
