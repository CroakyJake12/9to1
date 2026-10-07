using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Haven.Infrastructure.Native.Windows;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

internal sealed partial class NativePersonalTaskRecoveryStore
{
    private readonly WindowsPersonalStore? _windows;
    private NativePersonalTaskRecoveryStore(WindowsPersonalStore original)
    {
        _windows = original;
        _directory = null!; _database = null!;
        _directoryPath = original.DirectoryPath; _databasePath = original.DatabasePath;
        _expectedDirectory = default; _expectedDatabase = default; _uid = 0;
    }
    private static NativePersonalTaskRecoveryStore AcquireOriginalWindows(string directory, string database) =>
        new(new WindowsPersonalStore(directory, database));

    private sealed class WindowsPersonalStore : IDisposable
    {
        private const string KeyLeaf = ".task-recovery-auth.v1";
        private readonly object _gate = new();
        private readonly WindowsOriginalRoot _root;
        private readonly SafeFileHandle _directory;
        private readonly SafeFileHandle _database;
        private readonly WindowsOriginalFileIdentity _expectedDirectory, _expectedDatabase;
        private readonly string _directorySecurity, _databaseSecurity;
        private readonly string _principal;
        private bool _closed;
        // Private owning-test negative only; production acquisition/DI never configures
        // this callback. It cannot replace a native probe/result or confer authority.
        private Action<byte[]>? _beforeOriginalStageName = null;
        internal string DirectoryPath { get; }
        internal string DatabasePath { get; }

        internal WindowsPersonalStore(string directory, string database)
        {
            WindowsOriginalFileCustody.RequireCapabilities();
            DirectoryPath = WindowsOriginalFileCustody.NormalizeLocalPath(directory);
            DatabasePath = WindowsOriginalFileCustody.NormalizeLocalPath(database);
            if (!string.Equals(Path.GetDirectoryName(DatabasePath), DirectoryPath, StringComparison.OrdinalIgnoreCase) ||
                !WindowsOriginalFileCustody.IsSafeLeaf(Path.GetFileName(DatabasePath)))
                throw new UnauthorizedAccessException("The Windows database must be a direct child of the SAME configured personal store.");
            _principal = WindowsOriginalFileCustody.CurrentSid();
            WindowsOriginalRoot? root = null; SafeFileHandle? ownedDirectory = null, ownedDatabase = null;
            var failures = new List<Exception>();
            try
            {
                root = WindowsOriginalFileCustody.RetainRoot(DirectoryPath);
                if (root.Principal != _principal) throw new UnauthorizedAccessException("The actual Windows store principal changed during acquisition.");
                ownedDirectory = root.OpenDirectory(DirectoryPath, mutable: true);
                ownedDatabase = WindowsOriginalFileCustody.OpenRelative(ownedDirectory, Path.GetFileName(DatabasePath),
                    WindowsOriginalFileCustody.FileRead, 3, WindowsOriginalCreateDisposition.OpenExisting, WindowsOriginalFileKind.File);
                _expectedDirectory = DemandKind(ownedDirectory, true);
                _expectedDatabase = DemandKind(ownedDatabase, false);
                if (_expectedDatabase.Volume != _expectedDirectory.Volume)
                    throw new UnauthorizedAccessException("The actual Windows database is not in its SAME local store volume.");
                WindowsOriginalFileCustody.DemandPath(ownedDirectory, DirectoryPath, true);
                WindowsOriginalFileCustody.DemandPath(ownedDatabase, DatabasePath, false);
                _directorySecurity = DemandOriginalWindowsPrivacy(ownedDirectory, _principal);
                _databaseSecurity = DemandOriginalWindowsPrivacy(ownedDatabase, _principal);
                _root = root; _directory = ownedDirectory; _database = ownedDatabase;
                Validate();
                return;
            }
            catch (Exception error) { Add(failures, error); }
            // Constructor failure retains every actual native cleanup sibling. No usable
            // store is returned and no descriptor or path is adopted/repaired.
            foreach (var actual in new IDisposable?[] { ownedDatabase, ownedDirectory, root })
                if (actual is not null) try { actual.Dispose(); } catch (Exception error) { Add(failures, error); }
            Throw(failures); throw new IOException("No original Windows store was acquired.");
        }

        internal void Validate()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                _root.DemandCurrent(); WindowsOriginalFileCustody.DemandCurrentSid(_principal);
                DemandPinned(_directory, DirectoryPath, true, _expectedDirectory, _directorySecurity);
                DemandPinned(_database, DatabasePath, false, _expectedDatabase, _databaseSecurity);
                // Namespace reopens are parent-relative. Only the facade's exact original
                // missing status denotes absence; Win32/general IO failures remain faults.
                using (var current = _root.OpenDirectory(DirectoryPath, mutable: true))
                    DemandPinned(current, DirectoryPath, true, _expectedDirectory, _directorySecurity);
                using (var current = WindowsOriginalFileCustody.OpenRelative(_directory, Path.GetFileName(DatabasePath),
                    WindowsOriginalFileCustody.FileRead, 3, WindowsOriginalCreateDisposition.OpenExisting, WindowsOriginalFileKind.File))
                    DemandPinned(current, DatabasePath, false, _expectedDatabase, _databaseSecurity);
                foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
                    ValidateCompanion(suffix);
                _root.DemandCurrent(); WindowsOriginalFileCustody.DemandCurrentSid(_principal);
            }
        }

        private void ValidateCompanion(string suffix)
        {
            DemandPinned(_directory, DirectoryPath, true, _expectedDirectory, _directorySecurity);
            var parent = DemandProtectedWindowsSecurity(_directory, _principal);
            if (parent.Fingerprint != _directorySecurity)
                throw new UnauthorizedAccessException("The SAME Windows companion parent policy changed before open.");
            SafeFileHandle? companion = null; var failures = new List<Exception>();
            try
            {
                try { companion = WindowsOriginalFileCustody.OpenRelative(_directory, Path.GetFileName(DatabasePath) + suffix,
                    WindowsOriginalFileCustody.FileRead, 3, WindowsOriginalCreateDisposition.OpenExisting, WindowsOriginalFileKind.File); }
                catch (Exception error) when (WindowsOriginalFileCustody.IsOriginalMissing(error)) { }
                if (companion is not null)
                {
                    var identity = DemandKind(companion, false);
                    if (identity.Volume != _expectedDirectory.Volume)
                        throw new UnauthorizedAccessException("The actual Windows companion crossed its SAME parent's volume.");
                    WindowsOriginalFileCustody.DemandPath(companion, DatabasePath + suffix, false);
                    var security = DemandWindowsCompanionPrivacy(companion, _principal, parent);
                    WindowsOriginalFileCustody.DemandPath(companion, DatabasePath + suffix, false);
                    if (!DemandKind(companion, false).SameFile(identity) ||
                        DemandWindowsCompanionPrivacy(companion, _principal, parent) != security)
                        throw new UnauthorizedAccessException("The held Windows companion identity or private policy changed during observation.");
                }
            }
            catch (Exception error) { Add(failures, error); }
            if (companion is not null) try { companion.Dispose(); } catch (Exception error) { Add(failures, error); }
            // Parent identity/security/SID is checked even after a missing child,
            // child refusal or close fault. Original siblings remain inspectable.
            try
            {
                DemandPinned(_directory, DirectoryPath, true, _expectedDirectory, _directorySecurity);
                _root.DemandCurrent(); WindowsOriginalFileCustody.DemandCurrentSid(_principal);
            }
            catch (Exception error) { Add(failures, error); }
            Throw(failures);
        }

        internal byte[] ReadOrCreateAuthenticationKey(bool create)
        {
            lock (_gate)
            {
                Validate();
                SafeFileHandle? key = null;
                var failures = new List<Exception>(); byte[]? actual = null;
                try
                {
                    try { key = OpenKey(); }
                    catch (Exception error) when (WindowsOriginalFileCustody.IsOriginalMissing(error))
                    {
                        if (!create) throw new UnauthorizedAccessException("The original Windows authentication key is missing; no capsule provenance is reconstructed.", error);
                        CreateKey(); Validate(); key = OpenKey();
                    }
                    var path = Path.Combine(DirectoryPath, KeyLeaf);
                    var first = DemandKind(key, false); var security = DemandOriginalWindowsPrivacy(key, _principal);
                    if (first.Size != 32) throw new UnauthorizedAccessException("The actual Windows authentication key has an invalid size.");
                    WindowsOriginalFileCustody.DemandPath(key, path, false);
                    // This is an actual namespace/storage synchronization operation. Its
                    // platform/access failure is explicit; file-only flush cannot substitute.
                    WindowsOriginalFileCustody.Flush(_directory);
                    actual = new byte[32];
                    if (RandomAccess.Read(key, actual, 0) != actual.Length ||
                        !WindowsOriginalFileCustody.ReadIdentity(key).SameReadVersion(first) ||
                        DemandOriginalWindowsPrivacy(key, _principal) != security)
                        throw new UnauthorizedAccessException("The held original Windows authentication key changed while read.");
                    using (var current = OpenKey())
                        if (!DemandKind(current, false).SameReadVersion(first) ||
                            DemandOriginalWindowsPrivacy(current, _principal) != security)
                            throw new UnauthorizedAccessException("The original Windows authentication key namespace/security changed while read.");
                    WindowsOriginalFileCustody.DemandPath(key, path, false); Validate();
                }
                catch (Exception error) { Add(failures, error); }
                finally
                {
                    if (key is not null) try { key.Dispose(); } catch (Exception error) { Add(failures, error); }
                    if (failures.Count != 0 && actual is not null) CryptographicOperations.ZeroMemory(actual);
                }
                Throw(failures);
                return actual ?? throw new IOException("The original Windows key read returned no bytes.");
            }
        }
        private SafeFileHandle OpenKey() => WindowsOriginalFileCustody.OpenRelative(_directory, KeyLeaf,
            WindowsOriginalFileCustody.FileRead, 1, WindowsOriginalCreateDisposition.OpenExisting, WindowsOriginalFileKind.File);

        private void CreateKey()
        {
            // Probe the actual SAME retained parent before CSPRNG/name initialization or
            // namespace effects. An unsupported/refused Flags0 sync creates nothing.
            Validate();
            WindowsOriginalFileCustody.Flush(_directory);
            Validate();
            var bytes = RandomNumberGenerator.GetBytes(32);
            try { CreateKeyWithOriginalBuffer(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        private void CreateKeyWithOriginalBuffer(byte[] bytes)
        {
            // All callback/name/path/list/resource initialization is already inside the
            // generated original buffer's owning finally. No early allocation can skip it.
            _beforeOriginalStageName?.Invoke(bytes);
            var stageLeaf = ".task-recovery-auth-stage-" + Guid.NewGuid().ToString("N");
            var stagePath = Path.Combine(DirectoryPath, stageLeaf);
            SafeFileHandle? stage = null; WindowsOriginalFileIdentity initial = default;
            var published = false; var stageCreateAttempted = false; var failures = new List<Exception>();
            try
            {
                Validate();
                using var descriptor = WindowsOriginalFileCustody.CreatePrivateDescriptor(_principal);
                stageCreateAttempted = true;
                stage = WindowsOriginalFileCustody.OpenRelative(_directory, stageLeaf,
                    WindowsOriginalFileCustody.ExclusiveStage, 0, WindowsOriginalCreateDisposition.CreateNew,
                    WindowsOriginalFileKind.File, descriptor);
                initial = DemandKind(stage, false);
                // Actual stage-create namespace effect: synchronize THIS parent, not
                // merely its new file data. Later faults retain the partial outcome.
                WindowsOriginalFileCustody.Flush(_directory);
                WindowsOriginalFileCustody.DemandPath(stage, stagePath, false);
                WindowsOriginalFileCustody.DemandPrivateStage(stage, _principal);
                WindowsOriginalFileCustody.RetainStageTimestamps(stage);
                RandomAccess.Write(stage, bytes, 0);
                if (DemandKind(stage, false).Size != 32) throw new IOException("The actual Windows stage write did not produce exactly32 bytes.");
                WindowsOriginalFileCustody.Flush(stage);
                WindowsOriginalFileCustody.DemandPrivateStage(stage, _principal); Validate();
                // Probe again immediately before the distinct rename/publication effect.
                WindowsOriginalFileCustody.Flush(_directory);
                try { WindowsOriginalFileCustody.PublishCreateOnly(stage, _directory, KeyLeaf); published = true; }
                catch (Exception error) when (WindowsOriginalFileCustody.IsOriginalNameCollision(error))
                {
                    // Only this actual create-only target collision can denote another
                    // creator. Its key still needs the independent protected read below.
                }
                if (published) WindowsOriginalFileCustody.Flush(_directory);
            }
            catch (Exception error) { Add(failures, error); }
            finally
            {
                if (stage is not null)
                {
                    if (!published)
                        try
                        {
                            var current = DemandKind(stage, false);
                            if (!current.SameFile(initial)) throw new UnauthorizedAccessException("The actual owned Windows stage identity changed before cleanup.");
                            WindowsOriginalFileCustody.DeleteOwnedStage(stage, stagePath, current);
                            WindowsOriginalFileCustody.Flush(_directory);
                        }
                        catch (Exception error) { Add(failures, error); }
                    try { stage.Dispose(); } catch (Exception error) { Add(failures, error); }
                    // Stage-handle closure and parent namespace/storage sync are separate
                    // real operations. No pathname deletion can remove a foreign winner.
                    try { WindowsOriginalFileCustody.Flush(_directory); } catch (Exception error) { Add(failures, error); }
                }
                else if (stageCreateAttempted)
                {
                    // A failing actual create may not return its native handle. Preserve
                    // that original unknown result and synchronize the affected parent;
                    // never delete a pathname without SAME owned-stage handle custody.
                    try { WindowsOriginalFileCustody.Flush(_directory); } catch (Exception error) { Add(failures, error); }
                }
            }
            Throw(failures);
        }
        private static WindowsOriginalFileIdentity DemandKind(SafeFileHandle handle, bool directory)
        {
            var actual = WindowsOriginalFileCustody.ReadIdentity(handle);
            if ((actual.Attributes & 0x400) != 0 || actual.Links == 0 ||
                (directory ? !actual.IsDirectory : !actual.IsRegular || actual.Links != 1))
                throw new UnauthorizedAccessException("The Windows personal-store object is redirected, wrong-kind or not single-link.");
            return actual;
        }
        private void DemandPinned(SafeFileHandle handle, string path, bool directory,
            WindowsOriginalFileIdentity expected, string security)
        {
            if (!DemandKind(handle, directory).SameFile(expected))
                throw new UnauthorizedAccessException("The original Windows personal-store physical identity changed.");
            WindowsOriginalFileCustody.DemandPath(handle, path, directory);
            if (DemandOriginalWindowsPrivacy(handle, _principal) != security || !DemandKind(handle, directory).SameFile(expected))
                throw new UnauthorizedAccessException("The original Windows personal-store privacy or physical identity changed.");
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_closed) return; _closed = true;
                var failures = new List<Exception>();
                foreach (var actual in new IDisposable[] { _database, _directory, _root })
                    try { actual.Dispose(); } catch (Exception error) { Add(failures, error); }
                Throw(failures);
            }
        }
        private static void Add(List<Exception> failures, Exception error)
        { if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error); }
        private static void Throw(List<Exception> failures)
        {
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Actual Windows personal-store acquisition or cleanup failed.", failures);
        }
    }
}
