using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

if (args.Length != 2 || args[0] is not ("seed" or "verify") || !Directory.Exists(args[1]))
    throw new ArgumentException("Explicit seed|verify and an existing isolated folder required.");
var mode = args[0]; var folder = Path.GetFullPath(args[1]);
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var codec = new WriteNativeDocumentPackageStore();
var checks = 0;
void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); ++checks; Console.WriteLine("PASS " + message); }
const string part = "vendor/opaque-future.bin";
const string property = "futureVendorState";
byte[] opaque = [0, 255, 9, 10, 127, 64, 1, 2, 3];
var future = JsonNode.Parse("{\"nested\":[1,true,\"exact future value\"],\"format\":\"unknown\"}")!;
var original = Path.Combine(folder, "source.9to1w");
var broken = Path.Combine(folder, "tampered.9to1w");
var seedPath = Path.Combine(folder, "native-seed.json");
var state = "NOT_RUN"; string? error = null;
try
{
    if (mode == "seed")
    {
        Check(!File.Exists(original) && !File.Exists(seedPath), "Fresh native source fixture required");
        var document = NotesDocument.Create("Native browser roundtrip");
        document.Metadata["integrationFixture"] = "original-codec-browser-roundtrip";
        var editor = new WriteDocumentEditor(document);
        editor.InsertDocumentText("Alpha beta gamma");
        Check(document.Sections[0].Pages[0].Blocks[0].Runs.Select(r => r.Text).Aggregate(string.Empty, (a, b) => a + b) == "Alpha beta gamma", "Actual editor keeps original spaced source text");
        Check((await codec.SaveAsync(document, original)).IsSuccess, "Actual owner codec creates the source package");
        // Controlled compatible future fields, following the unchanged original owner's preservation contract.
        using (var archive = ZipFile.Open(original, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!; JsonObject manifest;
            using (var input = entry.Open()) manifest = JsonNode.Parse(input)!.AsObject();
            entry.Delete(); manifest[property] = future.DeepClone();
            manifest["parts"]!.AsObject()[part] = Convert.ToHexStringLower(SHA256.HashData(opaque));
            using (var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open())) writer.Write(manifest.ToJsonString());
            using (var output = archive.CreateEntry(part).Open()) output.Write(opaque);
        }
        var opened = await codec.OpenAsync(original);
        Check(opened.IsSuccess && opened.Value!.Id == document.Id && opened.Value.Version == document.Version, "Owner reopens source identity and revision");
        Check(JsonSerializer.Serialize(opened.Value!.Sections, json) == JsonSerializer.Serialize(document.Sections, json), "Owner preserves every source section page block and run");
        Check(opened.Value.Metadata.ContainsKey(WriteNativeDocumentPackageMetadata.PreservedPackageStateKey), "Owner retains future optional package data");
        File.Copy(original, broken, overwrite: false);
        using (var archive = ZipFile.Open(broken, ZipArchiveMode.Update))
        { archive.GetEntry(part)!.Delete(); using var output = archive.CreateEntry(part).Open(); output.WriteByte(17); }
        var denied = await codec.OpenAsync(broken);
        Check(!denied.IsSuccess && denied.Error!.Code == WriteNativeDocumentPackageErrorCode.IntegrityCheckFailed, "Owner identifies tampered optional part as integrity failure");
        File.WriteAllText(seedPath, JsonSerializer.Serialize(new { document = opened.Value, sourceSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(original))), tamperedSha256 = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(broken))) }, json));
    }
    else
    {
        using var seed = JsonDocument.Parse(File.ReadAllText(seedPath));
        var expected = JsonSerializer.Deserialize<NotesDocument>(File.ReadAllText(Path.Combine(folder, "expected-canonical.json")), json)!;
        var downloaded = await codec.OpenAsync(Path.Combine(folder, "downloaded.9to1w"));
        Check(downloaded.IsSuccess, "Actual owner codec opens the browser downloaded bytes");
        var actual = downloaded.Value!;
        Check(actual.Id == expected.Id && actual.Version == expected.Version && actual.Id != seed.RootElement.GetProperty("document").GetProperty("id").GetGuid(), "Downloaded independent artifact identity and acknowledged version match actual browser storage");
        Check(JsonSerializer.Serialize(actual.Sections, json) == JsonSerializer.Serialize(expected.Sections, json), "Whole downloaded structure and rich run identities match acknowledged browser canonical content");
        var runs = actual.Sections[0].Pages[0].Blocks[0].Runs;
        Check(runs.Count == 3 && runs[0].Text == "Alpha " && !runs[0].Bold && runs[1].Text == "beta" && runs[1].Bold && runs[2].Text == " gamma" && !runs[2].Bold, "Only physically selected beta is bold and all spaces remain");
        Check(actual.Metadata.ContainsKey(WriteNativeDocumentPackageMetadata.PreservedPackageStateKey), "Downloaded owner retains preserved package state");
        // Internal preservation JSON is a storage representation, verified independently below.
        actual.Metadata.Remove(WriteNativeDocumentPackageMetadata.PreservedPackageStateKey);
        expected.Metadata.Remove(WriteNativeDocumentPackageMetadata.PreservedPackageStateKey);
        Check(JsonSerializer.Serialize(actual, json) == JsonSerializer.Serialize(expected, json), "Every other typed canonical field including exact timestamps and revision survives export");
        using (var archive = ZipFile.OpenRead(Path.Combine(folder, "downloaded.9to1w")))
        {
            using var bytes = new MemoryStream(); using (var input = archive.GetEntry(part)!.Open()) input.CopyTo(bytes);
            Check(bytes.ToArray().SequenceEqual(opaque), "Future optional part preserved byte for byte through browser durable import edit export");
            using var inputManifest = archive.GetEntry("manifest.json")!.Open(); var manifest = JsonNode.Parse(inputManifest)!;
            Check(JsonNode.DeepEquals(manifest[property], future), "Future nested manifest property preserved structurally");
        }
        var sourceBytes = await File.ReadAllBytesAsync(original); var tamperedBytes = await File.ReadAllBytesAsync(broken);
        Check(Convert.ToHexStringLower(SHA256.HashData(sourceBytes)) == seed.RootElement.GetProperty("sourceSha256").GetString() && Convert.ToHexStringLower(SHA256.HashData(tamperedBytes)) == seed.RootElement.GetProperty("tamperedSha256").GetString(), "Both selected original packages remain byte identical after browser brokerage");
    }
    state = "PASS";
}
catch (Exception ex) { state = "FAIL"; error = ex.ToString(); Console.WriteLine(error); }
finally { File.WriteAllText(Path.Combine(folder, "native-" + mode + "-results.json"), JsonSerializer.Serialize(new { mode, state, checks, error }, json)); }
return state == "PASS" ? 0 : 1;
