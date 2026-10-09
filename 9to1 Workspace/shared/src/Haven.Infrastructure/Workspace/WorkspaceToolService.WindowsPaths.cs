using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    /// <summary>Retained Windows ancestors protect the SAME physical workspace against path
    /// component rename/reparse substitution while the original runs. This grants no access;
    /// actual task/permission fences are still required. Ordinary legacy APIs do not use this port.</summary>
    private sealed class OriginalWindowsPathLease : IDisposable
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, SafeFileHandle> _directories = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _root;
        private bool _closed;
        public OriginalWindowsPathLease(string root)
        {
            _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            try { EnsureDirectory(_root, null); }
            catch (Exception original)
            {
                var errors = new List<Exception> { original };
                try { Dispose(); } catch (Exception cleanup) { AddOriginalErrors(errors, null, cleanup); }
                ThrowOriginalErrors(errors);
                throw;
            }
        }

        public void EnsureDirectory(string directory, Action<string>? createOriginal)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (!IsWithinRoot(_root, full, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The original directory escapes its exact physical root.");
            lock (_gate)
            {
                if (_closed) throw new ObjectDisposedException(nameof(OriginalWindowsPathLease));
                var volume = Path.GetPathRoot(full) ?? throw new InvalidDataException("The original volume is unavailable.");
                var paths = new List<string> { volume };
                var current = volume;
                foreach (var component in full[volume.Length..].Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries))
                {
                    current = Path.Combine(current, component);
                    paths.Add(current);
                }
                foreach (var path in paths)
                {
                    var key = Path.TrimEndingDirectorySeparator(path);
                    if (_directories.ContainsKey(key)) continue;
                    if (!Directory.Exists(path) && createOriginal is not null)
                    {
                        // Every preceding ancestor is already held. Create exactly ONE child,
                        // never follow an unchecked newly-created intermediate component.
                        if (!IsWithinRoot(_root, path, StringComparison.OrdinalIgnoreCase))
                            throw new UnauthorizedAccessException("An original parent is outside its physical root.");
                        createOriginal(path);
                    }
                    var handle = CreateFileW(path, 0x80U, 1U, IntPtr.Zero, 3, 0x02000000U | 0x00200000U, IntPtr.Zero);
                    if (handle.IsInvalid)
                    {
                        var error = Marshal.GetLastPInvokeError();
                        handle.Dispose();
                        if (error is 2 or 3) throw new DirectoryNotFoundException("The original physical ancestor is absent.");
                        throw new Win32Exception(error, "The original physical ancestor could not be retained.");
                    }
                    try { DemandExactHandlePath(handle, path, requireDirectory: true); }
                    catch
                    {
                        handle.Dispose();
                        throw;
                    }
                    _directories.Add(key, handle);
                }
            }
        }

        public SafeFileHandle OpenOriginalRead(string path)
        {
            var full = Path.GetFullPath(path);
            if (!IsWithinRoot(_root, full, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The original read escapes its physical root.");
            EnsureDirectory(Path.GetDirectoryName(full)!, null);
            lock (_gate)
            {
                if (_closed) throw new ObjectDisposedException(nameof(OriginalWindowsPathLease));
                var handle = CreateFileW(full, 0x80000000U, 1U, IntPtr.Zero, 3, 0x40000000U | 0x00200000U, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var error = Marshal.GetLastPInvokeError();
                    handle.Dispose();
                    if (error == 2) throw new FileNotFoundException("The original read file is absent.", full);
                    if (error == 3) throw new DirectoryNotFoundException("The original read parent is absent.");
                    throw new Win32Exception(error, "The original read handle could not be retained.");
                }
                try { DemandExactHandlePath(handle, full, requireDirectory: false); }
                catch
                {
                    handle.Dispose();
                    throw;
                }
                return handle;
            }
        }

        public void DemandCurrent()
        {
            lock (_gate)
            {
                if (_closed) throw new ObjectDisposedException(nameof(OriginalWindowsPathLease));
                foreach (var original in _directories)
                    DemandExactHandlePath(original.Value, original.Key, requireDirectory: true);
            }
        }
        public SafeFileHandle OpenTraversalDirectory(string path)
        {
            EnsureDirectory(path, null);
            return OpenTraversalHandle(path, 0x81U, requireDirectory: true);
        }
        public SafeFileHandle OpenTraversalEntry(string path)
        {
            EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!, null);
            return OpenTraversalHandle(path, 0x80U, requireDirectory: null);
        }
        private SafeFileHandle OpenTraversalHandle(string path, uint access, bool? requireDirectory)
        {
            var full = Path.GetFullPath(path);
            if (!IsWithinRoot(_root, full, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The original traversal escapes its physical root.");
            lock (_gate)
            {
                if (_closed) throw new ObjectDisposedException(nameof(OriginalWindowsPathLease));
                var handle = CreateFileW(full, access, 1U, IntPtr.Zero, 3, 0x02000000U | 0x00200000U, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    var code = Marshal.GetLastPInvokeError(); handle.Dispose();
                    throw new Win32Exception(code, "The original traversal handle could not be retained.");
                }
                try
                {
                    var identity = ReadTraversalWindowsIdentity(handle);
                    DemandExactHandlePath(handle, full, requireDirectory ?? identity.IsDirectory);
                    if (requireDirectory is { } directory && identity.IsDirectory != directory ||
                        !identity.IsDirectory && identity.Links != 1)
                        throw new UnauthorizedAccessException("Original traversal refuses changed kinds and hard-link aliases.");
                    return handle;
                }
                catch (Exception error)
                {
                    var errors = new List<Exception> { error };
                    try { handle.Dispose(); } catch (Exception close) { AddOriginalErrors(errors, null, close); }
                    ThrowOriginalErrors(errors); throw;
                }
            }
        }

        public void Dispose()
        {
            SafeFileHandle[] originals;
            lock (_gate)
            {
                if (_closed) return;
                _closed = true;
                originals = _directories.Values.Reverse().ToArray();
                _directories.Clear();
            }
            var errors = new List<Exception>();
            foreach (var original in originals)
                try { original.Dispose(); } catch (Exception error) { AddOriginalErrors(errors, null, error); }
            ThrowOriginalErrors(errors);
        }
    }

    private static void DemandExactHandlePath(SafeFileHandle original, string expected, bool requireDirectory)
    {
        var attributes = new byte[8]; // FILE_ATTRIBUTE_TAG_INFO: DWORD attributes, DWORD tag.
        if (!GetFileInformationByHandleEx(original, 9, attributes, (uint)attributes.Length))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original physical attributes could not be observed.");
        var flags = (FileAttributes)BitConverter.ToUInt32(attributes, 0);
        if ((flags & FileAttributes.ReparsePoint) != 0 || requireDirectory != ((flags & FileAttributes.Directory) != 0))
            throw new UnauthorizedAccessException("The original physical component is redirected or has another kind.");
        var buffer = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(original, buffer, (uint)buffer.Capacity, 0);
        if (length == 0) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The original final physical path is unavailable.");
        if (length >= buffer.Capacity) throw new InvalidDataException("The original final physical path exceeds the bounded native observation.");
        var final = buffer.ToString();
        if (final.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)) final = "\\\\" + final[8..];
        else if (final.StartsWith("\\\\?\\", StringComparison.Ordinal)) final = final[4..];
        if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(final)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected)), StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The retained original handle resolves to another physical path.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass,
        [Out] byte[] information, uint bufferSize);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder filePath, uint characters, uint flags);
}
