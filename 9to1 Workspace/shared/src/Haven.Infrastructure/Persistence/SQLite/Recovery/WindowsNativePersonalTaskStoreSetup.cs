using System.Runtime.ExceptionServices;
using Haven.Infrastructure.Native.Windows;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

/// <summary>Explicit Windows64 create-new setup for the CURRENT configured personal
/// data path. This creates no key, schema, permissions, Task or readiness claim.</summary>
public static class WindowsNativePersonalTaskStoreSetup
{
    // Private negative-owning seams only. Production never installs callbacks;
    // they cannot supply a SID, descriptor, native status, path or positive result.
    private static Action<object>? _beforeOriginalParentProbe = null;
    private static Action<object>? _beforeOriginalDatabaseCreate = null;
    private static Action<object>? _afterOriginalDatabaseCreate = null;
    private static Action? _beforeOriginalDatabaseClose = null;
    private static Action? _beforeOriginalDirectoryClose = null;

    /// <summary>Creates only a NEW configured root and direct zero-length haven.db,
    /// before AppPaths/provider/SQLite. Returns a nonsecret normalized path observation
    /// after actual native work and owning cleanup settle; it grants no authority.</summary>
    public static string CreateNewConfiguredPersonalStore()
    {
        WindowsOriginalFileCustody.RequireCapabilities();
        return new OriginalCreateNewOperation(ConfiguredPath()).Create();
    }

    private static string ConfiguredPath()
    {
        // Exact existing AppPaths mapping, without constructing its effectful owner.
        var custom = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var configured = string.IsNullOrWhiteSpace(custom) ? Path.Combine(appData, "Haven") : Path.GetFullPath(custom);
        return WindowsOriginalFileCustody.NormalizeLocalPath(configured);
    }

    private sealed class OriginalCreateNewOperation
    {
        private readonly string _path, _parentPath, _leaf, _databasePath;
        private WindowsOriginalRoot? _ancestors;
        private SafeFileHandle? _parent, _directory, _database;
        private WindowsOriginalPrivateDescriptor? _descriptor;
        private string _principal = "", _parentSecurity = "", _directorySecurity = "", _databaseSecurity = "";
        private WindowsOriginalFileIdentity _parentIdentity, _directoryIdentity, _databaseIdentity;
        private bool _directoryMayExist, _databaseMayExist;

        internal OriginalCreateNewOperation(string path)
        {
            _path = path;
            _parentPath = Path.GetDirectoryName(path) ?? throw new UnauthorizedAccessException("A NEW bounded store requires an existing qualified parent.");
            _leaf = Path.GetFileName(path);
            if (!WindowsOriginalFileCustody.IsSafeLeaf(_leaf) || _parentPath.Length <= 3)
                throw new UnauthorizedAccessException("A NEW store requires a safe direct child of an existing bounded local parent.");
            _databasePath = Path.Combine(path, "haven.db");
        }

        internal string Create()
        {
            var failures = new List<Exception>();
            try
            {
                _principal = WindowsOriginalFileCustody.CurrentSid();
                _ancestors = WindowsOriginalFileCustody.RetainRoot(_parentPath);
                if (_ancestors.Principal != _principal) throw new UnauthorizedAccessException("The genuine Windows setup principal changed.");
                _parent = _ancestors.OpenDirectory(_parentPath, mutable: true);
                _parentIdentity = Kind(_parent, true);
                WindowsOriginalFileCustody.DemandPath(_parent, _parentPath, true);
                _parentSecurity = WindowsOriginalFileCustody.ReadSecurity(_parent).Fingerprint;
                _beforeOriginalParentProbe?.Invoke(this);
                DemandConfiguration(); DemandParent();
                // Actual SAME existing parent's Flags0 probe MUST precede all create
                // effects. No file-only flush or default descriptor substitutes.
                WindowsOriginalFileCustody.Flush(_parent);
                DemandParent(); DemandConfiguration();
                _descriptor = WindowsOriginalFileCustody.CreatePrivateDescriptor(_principal);
                _directoryMayExist = true;
                try
                {
                    _directory = WindowsOriginalFileCustody.OpenRelative(_parent, _leaf,
                        WindowsOriginalFileCustody.DirectoryMutable, 3, WindowsOriginalCreateDisposition.CreateNew,
                        WindowsOriginalFileKind.Directory, _descriptor);
                }
                catch (Exception error) when (WindowsOriginalFileCustody.IsOriginalNameCollision(error))
                { _directoryMayExist = false; throw; }
                _directoryIdentity = Kind(_directory, true);
                if (_directoryIdentity.Volume != _parentIdentity.Volume) throw new UnauthorizedAccessException("The NEW store crossed its retained parent's volume.");
                WindowsOriginalFileCustody.DemandPath(_directory, _path, true);
                _directorySecurity = NativePersonalTaskRecoveryStore.DemandOriginalWindowsPrivacy(_directory, _principal);
                DemandDirectory(); DemandParent(); WindowsOriginalFileCustody.Flush(_parent); DemandParent();
                // Probe the SAME NEW root before any database effects.
                WindowsOriginalFileCustody.Flush(_directory); DemandDirectory(); DemandConfiguration();
                _beforeOriginalDatabaseCreate?.Invoke(this);
                DemandDirectory(); DemandParent(); DemandConfiguration();
                _databaseMayExist = true;
                try
                {
                    _database = WindowsOriginalFileCustody.OpenRelative(_directory, "haven.db",
                        WindowsOriginalFileCustody.ExclusiveStage, 0, WindowsOriginalCreateDisposition.CreateNew,
                        WindowsOriginalFileKind.File, _descriptor);
                }
                catch (Exception error) when (WindowsOriginalFileCustody.IsOriginalNameCollision(error))
                { _databaseMayExist = false; throw; }
                _databaseIdentity = Kind(_database, false);
                _databaseSecurity = NativePersonalTaskRecoveryStore.DemandOriginalWindowsPrivacy(_database, _principal);
                DemandDatabase();
                _afterOriginalDatabaseCreate?.Invoke(this);
                WindowsOriginalFileCustody.Flush(_database); DemandDatabase();
                WindowsOriginalFileCustody.Flush(_directory); DemandDirectory(); DemandParent(); DemandConfiguration();
            }
            catch (Exception error) { Add(failures, error); }

            // Every original close/sync/validation sibling is attempted separately.
            // Failed/unknown post-effect work preserves observed partial state; no
            // pathname adoption, recursive deletion, reset, retry or rollback claim.
            if (_database is not null)
            {
                Attempt(failures, DemandDatabase);
                Attempt(failures, () => _beforeOriginalDatabaseClose?.Invoke());
                Attempt(failures, _database.Dispose);
            }
            if (_directory is not null)
            {
                Attempt(failures, DemandDirectory);
                Attempt(failures, () => WindowsOriginalFileCustody.Flush(_directory));
                Attempt(failures, DemandDirectory);
                Attempt(failures, () => _beforeOriginalDirectoryClose?.Invoke());
                Attempt(failures, _directory.Dispose);
            }
            if (_parent is not null && _directoryMayExist)
            {
                Attempt(failures, DemandParent);
                // Separately synchronize SAME original parent AFTER owning root close,
                // including attempted create whose original outcome is unknown.
                Attempt(failures, () => WindowsOriginalFileCustody.Flush(_parent));
                Attempt(failures, DemandParent);
            }
            if (_descriptor is not null) Attempt(failures, _descriptor.Dispose);
            if (_parent is not null) Attempt(failures, _parent.Dispose);
            if (_ancestors is not null) Attempt(failures, _ancestors.Dispose);
            Attempt(failures, DemandConfiguration);
            if (_principal.Length != 0) Attempt(failures, () => WindowsOriginalFileCustody.DemandCurrentSid(_principal));
            if (failures.Count != 0)
            {
                if (_directoryMayExist || _databaseMayExist)
                    throw new IOException("NEW Windows private-store setup failed or is unknown after create effects. Observed partial state is retained; no rollback or readiness is claimed.",
                        failures.Count == 1 ? failures[0] : new AggregateException("Original setup, close, validation and native synchronization siblings.", failures));
                if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
                throw new AggregateException("NEW Windows private-store setup refused before create effects.", failures);
            }
            return _path;
        }

        private void DemandConfiguration()
        {
            if (!string.Equals(ConfiguredPath(), _path, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The CURRENT configured data path changed during NEW Windows store setup.");
        }
        private void DemandParent()
        {
            _ancestors!.DemandCurrent(); WindowsOriginalFileCustody.DemandCurrentSid(_principal);
            WindowsOriginalFileCustody.DemandPath(_parent!, _parentPath, true);
            if (!Kind(_parent!, true).SameFile(_parentIdentity) ||
                WindowsOriginalFileCustody.ReadSecurity(_parent!).Fingerprint != _parentSecurity ||
                !Kind(_parent!, true).SameFile(_parentIdentity))
                throw new UnauthorizedAccessException("The SAME retained setup parent identity/owner/security changed.");
            WindowsOriginalFileCustody.DemandCurrentSid(_principal);
        }
        private void DemandDirectory()
        {
            DemandParent(); WindowsOriginalFileCustody.DemandPath(_directory!, _path, true);
            if (!Kind(_directory!, true).SameFile(_directoryIdentity) ||
                NativePersonalTaskRecoveryStore.DemandOriginalWindowsPrivacy(_directory!, _principal) != _directorySecurity ||
                !Kind(_directory!, true).SameFile(_directoryIdentity))
                throw new UnauthorizedAccessException("The actual NEW private root identity/owner/security changed.");
        }
        private void DemandDatabase()
        {
            DemandDirectory(); WindowsOriginalFileCustody.DemandPath(_database!, _databasePath, false);
            var actual = Kind(_database!, false);
            if (!actual.SameFile(_databaseIdentity) || actual.Volume != _directoryIdentity.Volume || actual.Size != 0 ||
                NativePersonalTaskRecoveryStore.DemandOriginalWindowsPrivacy(_database!, _principal) != _databaseSecurity ||
                !Kind(_database!, false).SameReadVersion(actual))
                throw new UnauthorizedAccessException("The NEW explicit database identity/zero-length input/owner/security changed.");
        }
        private static WindowsOriginalFileIdentity Kind(SafeFileHandle handle, bool directory)
        {
            var actual = WindowsOriginalFileCustody.ReadIdentity(handle);
            if ((actual.Attributes & 0x400) != 0 || actual.Links == 0 ||
                (directory ? !actual.IsDirectory : !actual.IsRegular || actual.Links != 1))
                throw new UnauthorizedAccessException("The actual setup object has an unsupported kind/link count.");
            return actual;
        }
        private static void Attempt(List<Exception> failures, Action action)
        { try { action(); } catch (Exception error) { Add(failures, error); } }
        private static void Add(List<Exception> failures, Exception error)
        { if (!failures.Any(original => ReferenceEquals(original, error))) failures.Add(error); }
    }
}
