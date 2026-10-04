using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

if (args.Length != 1 || !Directory.Exists(args[0])) throw new ArgumentException("Supply a fresh existing isolated fixture directory.");
var directory = Path.GetFullPath(args[0]);
var original = Path.Combine(directory, "original.9to1w");
if (File.Exists(original)) throw new ArgumentException("Fixture must be fresh.");
var assertions = 0;
void Check(bool passed, string name)
{
    if (!passed) throw new InvalidOperationException(name);
    ++assertions; Console.WriteLine("ASSERT " + name);
}
var codec = new WriteNativeDocumentPackageStore();
var document = NotesDocument.Create("Native format owner fixture");
var editor = new WriteDocumentEditor(document);
editor.InsertDocumentText("Alpha beta gamma");
var block = editor.Blocks().First();
editor.SelectBlock(block.Id, 10, 6, 10);
editor.ToggleSelectionCharacter(WriteCharacterFormat.Bold);
Check((await codec.SaveAsync(document, original, default)).IsSuccess, "unchanged actual native codec writes real owning document package");
var opaque = new byte[] { 0, 255, 9, 10, 127, 64, 1, 2, 3 };
const string partName = "vendor/opaque-future-part.bin";
const string propertyName = "futureVendorState";
var expectedProperty = JsonNode.Parse("{\"nested\":[1,true,\"exact future value\"],\"format\":\"unknown\"}")!;
using (var archive = ZipFile.Open(original, ZipArchiveMode.Update))
{
    var manifestEntry = archive.GetEntry("manifest.json")!;
    JsonObject manifest;
    using (var input = manifestEntry.Open()) manifest = JsonNode.Parse(input)!.AsObject();
    manifestEntry.Delete();
    manifest[propertyName] = expectedProperty.DeepClone();
    (manifest["parts"] ??= new JsonObject()).AsObject()[partName] = Convert.ToHexString(SHA256.HashData(opaque)).ToLowerInvariant();
    using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write(manifest.ToJsonString());
    using (var output = archive.CreateEntry(partName).Open()) output.Write(opaque);
}
var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(original));
var opened = await codec.OpenAsync(original, default);
Check(opened.IsSuccess && opened.Value!.Id == document.Id && opened.Value.Version == document.Version,
    "actual native open preserves owner document ID and existing version; package is not a repository commit");
Check(JsonSerializer.Serialize(opened.Value!.Sections) == JsonSerializer.Serialize(document.Sections),
    "actual native open preserves whole section/page/block/run identities and beta-only formatting");
Check(opened.Value.Metadata.ContainsKey(WriteNativeDocumentPackageMetadata.PreservedPackageStateKey),
    "actual native owner retains unknown optional data using its existing preservation metadata");
var roundtrip = Path.Combine(directory, "roundtrip.9to1w");
Check((await codec.SaveAsync(opened.Value, roundtrip, default)).IsSuccess, "actual native export re-emits structured owner document and preserved package parts");
using (var archive = ZipFile.OpenRead(roundtrip))
{
    using var input = archive.GetEntry(partName)!.Open();
    using var copy = new MemoryStream(); input.CopyTo(copy);
    Check(copy.ToArray().SequenceEqual(opaque), "unknown optional part survives exact byte-for-byte format roundtrip");
    using var manifest = archive.GetEntry("manifest.json")!.Open();
    var root = JsonNode.Parse(manifest)!;
    Check(JsonNode.DeepEquals(root[propertyName], expectedProperty), "unknown nested manifest property survives structurally exact format roundtrip");
}
var sourceAfter = SHA256.HashData(await File.ReadAllBytesAsync(original));
Check(sourceAfter.SequenceEqual(originalHash), "opening and re-exporting leaves original selected package bytes unchanged");
var reopened = await codec.OpenAsync(roundtrip, default);
Check(reopened.IsSuccess && reopened.Value!.Id == document.Id &&
    JsonSerializer.Serialize(reopened.Value.Sections) == JsonSerializer.Serialize(document.Sections),
    "reopened native roundtrip retains full owning content and stable IDs");
var tampered = Path.Combine(directory, "tampered-optional.9to1w");
File.Copy(roundtrip, tampered);
using (var archive = ZipFile.Open(tampered, ZipArchiveMode.Update))
{
    archive.GetEntry(partName)!.Delete();
    using var output = archive.CreateEntry(partName).Open(); output.WriteByte(17);
}
var denied = await codec.OpenAsync(tampered, default);
Check(!denied.IsSuccess && denied.Error!.Code == WriteNativeDocumentPackageErrorCode.IntegrityCheckFailed,
    "actual native codec rejects tampered optional-part integrity without changing original");
Console.WriteLine(JsonSerializer.Serialize(new { assertions,
    scope = "actual unchanged native codec/structured model format only; not browser chooser, canonical repository or donor acceptance" }));
