using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed class WriteNativeDocumentPackageStoreTests
{
    [Fact]
    public async Task Save_and_open_preserve_structured_document_identity_and_content()
    {
        using var directory = new TemporaryDirectory();
        var store = new WriteNativeDocumentPackageStore();
        var document = NotesDocument.Create("Coursework");
        var block = document.Sections[0].Pages[0].Blocks[0];
        block.PlainText = "A structured paragraph";
        block.Runs = [new NotesTextRun { Text = block.PlainText, Bold = true }];
        var path = Path.Combine(directory.Path, "coursework.9to1w");

        var saved = await store.SaveAsync(document, path);
        var opened = await store.OpenAsync(path);

        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        Assert.Equal(document.Id, opened.Value!.Id);
        Assert.Equal(document.Sections[0].Id, opened.Value.Sections[0].Id);
        Assert.Equal("A structured paragraph", opened.Value.Sections[0].Pages[0].Blocks[0].PlainText);
        Assert.True(opened.Value.Sections[0].Pages[0].Blocks[0].Runs[0].Bold);
    }

    [Fact]
    public async Task Open_rejects_changed_document_bytes_without_returning_a_partial_document()
    {
        using var directory = new TemporaryDirectory();
        var store = new WriteNativeDocumentPackageStore();
        var path = Path.Combine(directory.Path, "tampered.9to1w");
        Assert.True((await store.SaveAsync(NotesDocument.Create("Original"), path)).IsSuccess);
        RewriteEntry(path, "document.json", bytes => [.. bytes, (byte)' ']);

        var opened = await store.OpenAsync(path);

        Assert.False(opened.IsSuccess);
        Assert.Equal(WriteNativeDocumentPackageErrorCode.IntegrityCheckFailed, opened.Error!.Code);
        Assert.Null(opened.Value);
    }

    [Fact]
    public async Task Open_rejects_unsupported_manifest_version()
    {
        using var directory = new TemporaryDirectory();
        var store = new WriteNativeDocumentPackageStore();
        var path = Path.Combine(directory.Path, "future.9to1w");
        Assert.True((await store.SaveAsync(NotesDocument.Create(), path)).IsSuccess);
        RewriteEntry(path, "manifest.json", bytes =>
        {
            using var manifest = JsonDocument.Parse(bytes);
            var value = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(manifest.RootElement.GetRawText())!;
            value["version"] = JsonDocument.Parse("2").RootElement.Clone();
            return JsonSerializer.SerializeToUtf8Bytes(value);
        });

        var opened = await store.OpenAsync(path);

        Assert.False(opened.IsSuccess);
        Assert.Equal(WriteNativeDocumentPackageErrorCode.UnsupportedDocumentVersion, opened.Error!.Code);
    }

    [Fact]
    public async Task Failed_save_keeps_existing_destination_intact()
    {
        using var directory = new TemporaryDirectory();
        var store = new WriteNativeDocumentPackageStore();
        var path = Path.Combine(directory.Path, "existing.9to1w");
        var original = NotesDocument.Create("Original");
        Assert.True((await store.SaveAsync(original, path)).IsSuccess);
        var before = await File.ReadAllBytesAsync(path);
        original.Metadata[WriteNativeDocumentPackageMetadata.PreservedPackageStateKey] = "not-json";

        var saved = await store.SaveAsync(original, path);

        Assert.False(saved.IsSuccess);
        Assert.Equal(WriteNativeDocumentPackageErrorCode.InvalidPackage, saved.Error!.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Open_rejects_traversal_entries()
    {
        using var directory = new TemporaryDirectory();
        var store = new WriteNativeDocumentPackageStore();
        var path = Path.Combine(directory.Path, "unsafe.9to1w");
        var document = NotesDocument.Create();
        var documentBytes = JsonSerializer.SerializeToUtf8Bytes(document, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = WriteNativeDocumentPackageStore.FormatId,
            version = WriteNativeDocumentPackageStore.CurrentFormatVersion,
            documentId = document.Id,
            documentSha256 = Convert.ToHexString(SHA256.HashData(documentBytes)).ToLowerInvariant(),
            parts = new Dictionary<string, string>()
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            await AddEntryAsync(archive, "manifest.json", manifestBytes);
            await AddEntryAsync(archive, "document.json", documentBytes);
            await AddEntryAsync(archive, "../escape.txt", [1]);
        }

        var opened = await store.OpenAsync(path);

        Assert.False(opened.IsSuccess);
        Assert.Equal(WriteNativeDocumentPackageErrorCode.UnsafePackageEntry, opened.Error!.Code);
    }

    private static void RewriteEntry(string path, string name, Func<byte[], byte[]> rewrite)
    {
        var temporary = path + ".rewrite";
        using (var source = ZipFile.OpenRead(path))
        using (var destination = ZipFile.Open(temporary, ZipArchiveMode.Create))
        {
            foreach (var entry in source.Entries)
            {
                using var input = entry.Open();
                using var buffer = new MemoryStream();
                input.CopyTo(buffer);
                var bytes = entry.FullName == name ? rewrite(buffer.ToArray()) : buffer.ToArray();
                using var output = destination.CreateEntry(entry.FullName).Open();
                output.Write(bytes);
            }
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task AddEntryAsync(ZipArchive archive, string name, byte[] bytes)
    {
        await using var output = archive.CreateEntry(name).Open();
        await output.WriteAsync(bytes);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"write-package-tests-{Guid.NewGuid():N}");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
