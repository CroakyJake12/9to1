using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace HavenOS.Apps.Canvas;

/// <summary>Local Windows byte custody only. Home and Files still supply all business
/// authority. Ancestors and the exact regular, single-link leaf stay held through the
/// caller's final actor/configuration checks and canonical Files publication.</summary>
internal sealed class CanvasOriginalArtifactFile : IAsyncDisposable
{
    private readonly List<SafeFileHandle> _parents = [];
    private string _path = string.Empty;
    private FileStream? _stream;
    private SafeFileHandle? _leaf;
    private Task? _close;
    private FileIdentity _identity;
    private CanvasOriginalArtifactFile() { }
    private void AcquireInto(string root, string relative, bool create)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The genuine Canvas artifact owner requires Windows retained handles.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var volume = Path.GetPathRoot(fullRoot) ?? throw new InvalidDataException("Canvas volume is unavailable.");
        if (volume.Length != 3 || volume[1] != ':' || Path.IsPathRooted(relative) ||
            relative.Split(['/', '\\']).Any(part => string.IsNullOrEmpty(part) || part is "." or ".." ||
                part.Contains(':') || part.EndsWith('.') || part.EndsWith(' ')))
            throw new InvalidDataException("Canvas requires an exact local Files-relative artifact path.");
        _path = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!_path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Canvas artifact escapes its original root.");
        var parent = Path.GetDirectoryName(_path)!;
        var paths = new List<string> { volume }; var current = volume;
        foreach (var part in parent[volume.Length..].Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
        { current = Path.Combine(current, part); paths.Add(current); }
        if (paths.Count > 128) throw new InvalidDataException("Canvas original ancestor custody exceeds its finite handle bound.");
        foreach (var path in paths)
        {
            if (!Directory.Exists(path) && create && path.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                Directory.CreateDirectory(path); // Exactly one child; every preceding parent is already retained.
            var handle = Open(path, 0x80U, 1U, 3U, 0x02000000U | 0x00200000U);
            try { DemandPath(handle, path, true); }
            catch { handle.Dispose(); throw; }
            _parents.Add(handle);
        }
        _leaf = Open(_path, create ? 0x40000000U | 0x80U : 0x80000000U, create ? 0U : 1U,
            create ? 1U : 3U, 0x40000000U | 0x00200000U | (create ? 0x80000000U : 0U));
        DemandPath(_leaf, _path, false); _identity = ReadIdentity(_leaf);
        _stream = new(_leaf, create ? FileAccess.Write : FileAccess.Read, 4096, isAsync: true);
    }
    public static async Task<CanvasOriginalArtifactFile> WriteAsync(string root, string relative,
        ReadOnlyMemory<byte> bytes, CancellationToken token)
    {
        CanvasOriginalArtifactFile? original = null; Task? source = null;
        try
        {
            token.ThrowIfCancellationRequested(); original = Acquire(root, relative, true);
            source = original._stream!.WriteAsync(bytes, token).AsTask(); await Observe(source).ConfigureAwait(false);
            source = original._stream!.FlushAsync(token); await Observe(source).ConfigureAwait(false);
            original._stream!.Flush(flushToDisk: true); original.DemandUnchanged(bytes.Length);
            return original;
        }
        catch (Exception error)
        {
            await ThrowAfterCloseAsync(original, source, error).ConfigureAwait(false); throw;
        }
    }
    public static CanvasOriginalArtifactFile OpenRead(string root, string relative)
        => Acquire(root, relative, false);
    private static CanvasOriginalArtifactFile Acquire(string root, string relative, bool create)
    {
        // Construct in two phases so partially acquired handles remain independently disposable.
        var owner = new CanvasOriginalArtifactFile();
        try { owner.AcquireInto(root, relative, create); return owner; }
        catch (Exception error)
        {
            var errors = new List<Exception> { error };
            foreach (var parent in owner._parents.AsEnumerable().Reverse())
                try { parent.Dispose(); } catch (Exception close) { Add(errors, close); }
            try { owner._leaf?.Dispose(); } catch (Exception close) { Add(errors, close); }
            Throw(errors); throw;
        }
    }
    public async Task<byte[]> ReadAsync(long size, CancellationToken token)
    {
        if (size <= 0 || size > 512L * 1024 * 1024 || _stream is null)
            throw new InvalidDataException("Canvas artifact size is outside its bounded original read.");
        DemandUnchanged(size);
        var bytes = new byte[checked((int)size)];
        var source = _stream.ReadExactlyAsync(bytes.AsMemory(), token).AsTask();
        await Observe(source).ConfigureAwait(false); DemandUnchanged(size); return bytes;
    }
    public void DemandUnchanged(long size)
    {
        var handle = _leaf ?? throw new ObjectDisposedException(nameof(CanvasOriginalArtifactFile));
        DemandPath(handle, _path, false);
        var actual = ReadIdentity(handle);
        if (actual.Volume != _identity.Volume || actual.Index != _identity.Index || actual.Size != size)
            throw new InvalidDataException("The retained Canvas artifact identity or byte count changed.");
    }
    private static FileIdentity ReadIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var value))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Canvas cannot observe its original file identity.");
        if (value.NumberOfLinks != 1 || (value.Attributes & (0x400U | 0x10U | 0x40U)) != 0)
            throw new UnauthorizedAccessException("Canvas requires a regular, single-link original artifact.");
        return new(value.VolumeSerialNumber, ((ulong)value.FileIndexHigh << 32) | value.FileIndexLow,
            checked((long)(((ulong)value.FileSizeHigh << 32) | value.FileSizeLow)));
    }
    private static void DemandPath(SafeFileHandle handle, string path, bool directory)
    {
        if (GetFileType(handle) != 1) throw new UnauthorizedAccessException("Canvas original handle is not a disk file.");
        var tags = new byte[8];
        if (!GetFileInformationByHandleEx(handle, 9, tags, 8)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var attributes = (FileAttributes)BitConverter.ToUInt32(tags);
        if ((attributes & FileAttributes.ReparsePoint) != 0 || directory != ((attributes & FileAttributes.Directory) != 0))
            throw new UnauthorizedAccessException("Canvas original component is redirected or has another kind.");
        var buffer = new StringBuilder(32768); var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length == 0 || length >= buffer.Capacity) throw new Win32Exception(Marshal.GetLastPInvokeError(), "Canvas original final path is unavailable.");
        var final = buffer.ToString(); if (final.StartsWith("\\\\?\\", StringComparison.Ordinal)) final = final[4..];
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(final)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Canvas retained handle resolves to another path.");
    }
    private static SafeFileHandle Open(string path, uint access, uint share, uint mode, uint flags)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, mode, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastPInvokeError(); handle.Dispose(); throw new Win32Exception(error, "Canvas original handle acquisition failed.");
    }
    private static async Task Observe(Task actual)
    { try { await actual.ConfigureAwait(false); } catch when (actual.IsFaulted) { throw actual.Exception!; } }
    public ValueTask DisposeAsync() => new(_close ??= CloseAsync());
    private async Task CloseAsync()
    {
        var errors = new List<Exception>(); Task? source = null;
        try { if (_stream is not null) { source = _stream.DisposeAsync().AsTask(); await Observe(source).ConfigureAwait(false); } }
        catch (Exception error) { Add(errors, (Exception?)source?.Exception ?? error); }
        try { _leaf?.Dispose(); } catch (Exception error) { Add(errors, error); }
        foreach (var parent in _parents.AsEnumerable().Reverse()) try { parent.Dispose(); } catch (Exception error) { Add(errors, error); }
        _stream = null; _leaf = null; _parents.Clear(); Throw(errors);
    }
    private static async Task ThrowAfterCloseAsync(CanvasOriginalArtifactFile? owner, Task? source, Exception error)
    {
        var errors = new List<Exception>(); Add(errors, (Exception?)source?.Exception ?? error);
        Task? close = null;
        try { if (owner is not null) { close = owner.DisposeAsync().AsTask(); await close.ConfigureAwait(false); } }
        catch (Exception cleanup) { Add(errors, (Exception?)close?.Exception ?? cleanup); }
        if (errors.Count == 1 && errors[0] is OperationCanceledException && source?.IsCanceled == true)
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
        Throw(errors);
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (error is AggregateException group) foreach (var child in group.InnerExceptions) Add(errors, child); else if (!errors.Any(value => ReferenceEquals(value, error))) errors.Add(error); }
    private static void Throw(List<Exception> errors)
    { if (errors.Count == 1 && errors[0] is not OperationCanceledException) ExceptionDispatchInfo.Capture(errors[0]).Throw(); if (errors.Count != 0) throw new AggregateException("Original Canvas byte custody or independent cleanup failed.", errors); }
    private readonly record struct FileIdentity(uint Volume, ulong Index, long Size);
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    { public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write; public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, [Out] byte[] information, uint bytes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint characters, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint GetFileType(SafeFileHandle handle);
}
