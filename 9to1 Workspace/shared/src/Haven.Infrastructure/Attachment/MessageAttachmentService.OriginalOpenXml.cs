using System.IO.Compression;
using System.Text;
using System.Xml;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed partial class MessageAttachmentService
{
    // These bounds apply only to the separately approved original-content path.
    // Ordinary attachment parsing retains its existing behavior.
    private const long OriginalOpenXmlEntryLimit = 8L * 1024 * 1024;
    private const long OriginalOpenXmlExpandedLimit = 32L * 1024 * 1024;
    private const int OriginalOpenXmlEntryCountLimit = 2048;

    private async Task<string> ExtractOriginalOpenXmlTextAsync(AttachmentOriginalSources source, Stream content,
        MessageAttachmentKind kind, int maxCharacters, CancellationToken token)
    {
        ZipArchive? archive = null;
        var builder = new StringBuilder();
        var budget = new OriginalOpenXmlExpansionBudget();
        try
        {
            await ValidateOriginalOpenXmlDirectoryAsync(source, content, token).ConfigureAwait(false);
            source.Run(() => archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true));
            ZipArchiveEntry[] entries = [];
            source.Run(() =>
            {
                if (archive!.Entries.Count > OriginalOpenXmlEntryCountLimit)
                    throw new InvalidDataException("This document contains too many archive entries for local attachment extraction.");
                var prefixes = OpenXmlEntryPrefixes(kind);
                entries = archive.Entries.Where(entry => prefixes.Any(prefix => entry.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(entry => entry.FullName, StringComparer.OrdinalIgnoreCase).ToArray();
                long expanded = 0;
                foreach (var entry in entries)
                {
                    if (entry.Length < 0 || entry.Length > OriginalOpenXmlEntryLimit ||
                        checked(expanded += entry.Length) > OriginalOpenXmlExpandedLimit)
                        throw new InvalidDataException("This document exceeds the bounded local attachment extraction size.");
                }
            });
            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                Stream? rawEntry = null;
                XmlReader? reader = null;
                var complete = false; var failed = false;
                try
                {
                    source.Run(() => rawEntry = entry.Open());
                    source.Run(() => reader = XmlReader.Create(new OriginalOpenXmlEntryStream(rawEntry!, source, budget), new XmlReaderSettings
                    {
                        Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                        IgnoreComments = true, IgnoreProcessingInstructions = true,
                        MaxCharactersInDocument = OriginalOpenXmlEntryLimit, CloseInput = false
                    }));
                    complete = await AppendOpenXmlEntryTextAsync(reader!, builder, maxCharacters, token,
                        () => source.Read(reader!.ReadAsync),
                        () => source.Read(reader!.ReadElementContentAsStringAsync)).ConfigureAwait(false);
                }
                catch (Exception cause) { failed = true; source.Remember(cause); }
                // The reader and original entry are independently retired even if
                // acquisition/postguard or another close failed. The approved
                // content stream remains borrowed from its actual Files lease.
                if (reader is not null)
                    try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { reader.Dispose(); return true; }); }
                    catch (Exception cause) { failed = true; source.Remember(cause); }
                if (rawEntry is not null)
                {
                    Task? close = null;
                    try
                    {
                        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
                        { close = rawEntry.DisposeAsync().AsTask(); source.Retain(close); return true; });
                    }
                    catch (Exception cause) { failed = true; source.Remember(cause); }
                    if (close is not null)
                        try { await close.ConfigureAwait(false); }
                        catch (Exception cause) { failed = true; source.Remember(close.Exception ?? cause); }
                }
                if (failed) await source.Join().ConfigureAwait(false);
                if (complete) return Truncate(builder.ToString(), maxCharacters);
            }
            return Truncate(builder.ToString().Trim(), maxCharacters);
        }
        catch (Exception cause) { source.Remember(cause); throw; }
        finally
        {
            if (archive is not null)
                try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { archive.Dispose(); return true; }); }
                catch (Exception cause) { source.Remember(cause); }
        }
    }

    // Bound directory allocation before ZipArchive materializes entry objects.
    // This validates the outer container only; the maintained parser still owns
    // ZIP decompression, entry selection and document text interpretation.
    private static async Task ValidateOriginalOpenXmlDirectoryAsync(AttachmentOriginalSources source,
        Stream content, CancellationToken token)
    {
        long length = 0;
        source.Run(() =>
        {
            if (!content.CanSeek) throw new InvalidDataException("The approved document stream must support bounded archive inspection.");
            length = content.Length;
        });
        if (length < 22 || length > 50L * 1024 * 1024)
            throw new InvalidDataException("The approved document archive size is invalid.");
        var tailLength = (int)Math.Min(length, 22 + ushort.MaxValue);
        var tail = await ReadOriginalOpenXmlDirectoryBytesAsync(source, content, length - tailLength, tailLength, token).ConfigureAwait(false);
        var end = -1;
        for (var index = tail.Length - 22; index >= 0; index--)
            if (ArchiveUInt32(tail, index) == 0x06054b50 && index + 22 + ArchiveUInt16(tail, index + 20) == tail.Length)
            { end = index; break; }
        if (end < 0) throw new InvalidDataException("The document archive directory is missing or malformed.");
        var count = ArchiveUInt16(tail, end + 10);
        var size = ArchiveUInt32(tail, end + 12);
        var offset = ArchiveUInt32(tail, end + 16);
        if (ArchiveUInt16(tail, end + 4) != 0 || ArchiveUInt16(tail, end + 6) != 0 ||
            ArchiveUInt16(tail, end + 8) != count || count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue ||
            (end >= 20 && ArchiveUInt32(tail, end - 20) == 0x07064b50))
            throw new NotSupportedException("ZIP64 and split document archives are not supported in this attachment flow.");
        if (count > OriginalOpenXmlEntryCountLimit || size > 2 * 1024 * 1024 ||
            (long)offset + size != length - tailLength + end)
            throw new InvalidDataException("The document archive directory exceeds the local attachment bounds or is inconsistent.");
        var directory = await ReadOriginalOpenXmlDirectoryBytesAsync(source, content, offset, (int)size, token).ConfigureAwait(false);
        var position = 0; var observed = 0;
        while (position < directory.Length)
        {
            if (++observed > OriginalOpenXmlEntryCountLimit || directory.Length - position < 46 || ArchiveUInt32(directory, position) != 0x02014b50)
                throw new InvalidDataException("The document archive directory entries are malformed or exceed the local limit.");
            var name = ArchiveUInt16(directory, position + 28);
            var extra = ArchiveUInt16(directory, position + 30);
            var comment = ArchiveUInt16(directory, position + 32);
            var next = position + 46 + name + extra + comment;
            if (next > directory.Length || ArchiveUInt16(directory, position + 34) != 0 ||
                ArchiveUInt32(directory, position + 20) == uint.MaxValue || ArchiveUInt32(directory, position + 24) == uint.MaxValue ||
                ArchiveUInt32(directory, position + 42) == uint.MaxValue)
                throw new InvalidDataException("The document archive uses unsupported or inconsistent entry metadata.");
            var extraPosition = position + 46 + name; var extraEnd = extraPosition + extra;
            while (extraPosition < extraEnd)
            {
                if (extraEnd - extraPosition < 4) throw new InvalidDataException("A document archive extra field is incomplete.");
                var id = ArchiveUInt16(directory, extraPosition);
                var extraSize = ArchiveUInt16(directory, extraPosition + 2);
                if (id == 1) throw new NotSupportedException("ZIP64 document entries are not supported in this attachment flow.");
                extraPosition += 4 + extraSize;
                if (extraPosition > extraEnd) throw new InvalidDataException("A document archive extra field exceeds its entry.");
            }
            position = next;
        }
        if (observed != count) throw new InvalidDataException("The document archive entry count is inconsistent.");
        source.Run(() => content.Seek(0, SeekOrigin.Begin));
    }
    private static ushort ArchiveUInt16(byte[] bytes, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private static uint ArchiveUInt32(byte[] bytes, int offset) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static async Task<byte[]> ReadOriginalOpenXmlDirectoryBytesAsync(AttachmentOriginalSources source,
        Stream content, long offset, int count, CancellationToken token)
    {
        var bytes = new byte[count];
        source.Run(() => content.Seek(offset, SeekOrigin.Begin));
        var read = 0;
        while (read < count)
        {
            var next = await source.Read(() => content.ReadAsync(bytes.AsMemory(read, count - read), token).AsTask()).ConfigureAwait(false);
            if (next <= 0 || next > count - read) throw new EndOfStreamException("The original document archive directory changed or ended early.");
            read += next;
        }
        return bytes;
    }

    private sealed class OriginalOpenXmlExpansionBudget
    {
        private long _used;
        internal void Take(int count)
        {
            if (Interlocked.Add(ref _used, count) > OriginalOpenXmlExpandedLimit)
                throw new InvalidDataException("Expanded document text exceeds the local attachment limit.");
        }
    }
    private sealed class OriginalOpenXmlEntryStream(Stream actual, AttachmentOriginalSources sources,
        OriginalOpenXmlExpansionBudget budget) : Stream
    {
        private long _used;
        private int Count(int count)
        {
            if (count < 0 || Interlocked.Add(ref _used, count) > OriginalOpenXmlEntryLimit)
                throw new InvalidDataException("An expanded document entry exceeds the local attachment limit.");
            budget.Take(count); return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Count(sources.Invoke(() => actual.Read(buffer, offset, count)));
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            Count(await sources.Read(() => actual.ReadAsync(buffer, offset, count, token)).ConfigureAwait(false));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            Count(await sources.Read(() => actual.ReadAsync(buffer, token).AsTask()).ConfigureAwait(false));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        // This wrapper never owns the original entry; its issuing call closes it.
    }
}
