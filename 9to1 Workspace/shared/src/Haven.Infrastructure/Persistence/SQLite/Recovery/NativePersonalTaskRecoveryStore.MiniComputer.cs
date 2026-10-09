using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Infrastructure.Native.Windows;

namespace Haven.Infrastructure;

internal sealed partial class NativePersonalTaskRecoveryStore
{
    /// <summary>Physical file identity only. A separately opened reader must belong
    /// to the SAME held native file, not merely report the same path string.</summary>
    internal void ValidateOriginalMiniComputerReadHandle(SafeFileHandle actualReader)
    {
        ArgumentNullException.ThrowIfNull(actualReader);
        Validate();
        if (_windows is { } windows) windows.ValidateOriginalMiniComputerReadHandle(actualReader);
        else if (!Observe(actualReader, directory: false).SameObject(_expectedDatabase))
            throw new UnauthorizedAccessException("The actual catalogue reader belongs to a different native file.");
        Validate();
    }

    // An opaque observation issued for the SAME configured Mini catalogue owner.
    // It is not a file/path grant and contains no authentication key material.
    internal sealed class MiniComputerIdentityStamp
    {
        private readonly object _catalogueOwner;
        private readonly string _directoryPath, _filePath;
        private readonly Identity _directory, _file;
        private readonly uint _principal;
        private readonly object? _windows;
        internal string Fingerprint { get; }
        internal MiniComputerIdentityStamp(NativePersonalTaskRecoveryStore original, object catalogueOwner, SafeFileHandle? staged = null)
        {
            _catalogueOwner = catalogueOwner; _directoryPath = original._directoryPath; _filePath = original._databasePath;
            _directory = original._expectedDirectory; _file = staged is null ? original._expectedDatabase : original.Observe(staged, false); _principal = original._uid;
            if (original._windows is { } windows)
            { var proof = windows.CaptureOriginalMiniComputerStamp(catalogueOwner); _windows = proof.Stamp; Fingerprint = proof.Fingerprint; }
            else Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            { Schema = 1, Platform = "linux", Directory = _directoryPath, File = _filePath,
                DirectoryIdentity = _directory, FileIdentity = _file, Principal = _principal })));
        }
        internal bool Matches(NativePersonalTaskRecoveryStore actual, object catalogueOwner)
        {
            if (!ReferenceEquals(_catalogueOwner, catalogueOwner) || _directoryPath != actual._directoryPath || _filePath != actual._databasePath)
                return false;
            if (actual._windows is { } windows) return _windows is not null && windows.IsOriginalMiniComputerStamp(_windows, catalogueOwner);
            return _windows is null && _principal == actual._uid &&
                actual.Observe(actual._directory, true).SameObject(_directory) && actual.Observe(actual._database, false) == _file;
        }
    }
    internal MiniComputerIdentityStamp CaptureOriginalMiniComputerIdentityStamp(object sameCatalogueOwner)
    {
        ArgumentNullException.ThrowIfNull(sameCatalogueOwner); Validate();
        var stamp = new MiniComputerIdentityStamp(this, sameCatalogueOwner); Validate(); return stamp;
    }
    internal void DemandOriginalMiniComputerIdentityStamp(MiniComputerIdentityStamp sameStamp, object sameCatalogueOwner)
    {
        ArgumentNullException.ThrowIfNull(sameStamp); Validate();
        if (!sameStamp.Matches(this, sameCatalogueOwner))
            throw new UnauthorizedAccessException("The exact original Mini Computer file, parent, principal or private security changed.");
        Validate();
    }

    internal SafeFileHandle CreateOriginalMiniComputerStage(string leaf, MiniComputerIdentityStamp expected, object sameCatalogueOwner)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Original catalogue replacement needs this platform's actual native writer.");
        if (string.IsNullOrWhiteSpace(leaf) || Path.GetFileName(leaf) != leaf || !leaf.StartsWith(".mini-identity-", StringComparison.Ordinal))
            throw new ArgumentException("The actual source must issue a finite private stage name.");
        DemandOriginalMiniComputerIdentityStamp(expected, sameCatalogueOwner);
        var fd = OpenAt(_directory.DangerousGetHandle().ToInt32(), leaf,
            1 | 0x40 | 0x80 | NoFollow | CloseOnExec, (uint)(_expectedDatabase.Mode & 0x1ff));
        if (fd < 0) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error(),
            "The original catalogue identity stage could not be created.");
        // The caller captures this handle before any subsequent validation/flush.
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }
    internal MiniComputerIdentityStamp ObserveOriginalMiniComputerStage(SafeFileHandle stage, string leaf, object sameCatalogueOwner)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        Validate();
        using var actual = OpenMiniComputerChild(leaf);
        if (!Observe(actual, false).SameObject(Observe(stage, false)) || Observe(stage, false).Mode != _expectedDatabase.Mode)
            throw new UnauthorizedAccessException("The actual identity stage is a different native file.");
        return new MiniComputerIdentityStamp(this, sameCatalogueOwner, stage);
    }
    internal void PublishOriginalMiniComputerIdentityStage(SafeFileHandle stage, string leaf,
        MiniComputerIdentityStamp original, object sameCatalogueOwner)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        DemandOriginalMiniComputerIdentityStamp(original, sameCatalogueOwner);
        _ = ObserveOriginalMiniComputerStage(stage, leaf, sameCatalogueOwner);
        RandomAccess.FlushToDisk(stage); RandomAccess.FlushToDisk(_directory);
        DemandOriginalMiniComputerIdentityStamp(original, sameCatalogueOwner);
        // Kernel-held directory anchor, not the configured parent's mutable path.
        // The SAME canonical catalogue writer reservation serializes product writers.
        var anchor = "/proc/self/fd/" + _directory.DangerousGetHandle().ToInt32().ToString(System.Globalization.CultureInfo.InvariantCulture);
        File.Move(Path.Combine(anchor, leaf), Path.Combine(anchor, Path.GetFileName(_databasePath)), overwrite: true);
        RandomAccess.FlushToDisk(_directory);
        if (GetEffectiveUserId() != _uid || !Observe(_directory, true).SameObject(_expectedDirectory) ||
            !ObservePath(_directoryPath, true).SameObject(_expectedDirectory))
            throw new UnauthorizedAccessException("The original catalogue parent changed during publication; retain the exact result for recovery.");
    }
    private SafeFileHandle OpenMiniComputerChild(string leaf)
    {
        if (Path.GetFileName(leaf) != leaf) throw new UnauthorizedAccessException("An exact original child name is required.");
        var fd = OpenAt(_directory.DangerousGetHandle().ToInt32(), leaf, NoFollow | CloseOnExec, 0);
        if (fd < 0) throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
        return new SafeFileHandle((IntPtr)fd, ownsHandle: true);
    }

    private sealed partial class WindowsPersonalStore
    {
        private sealed record MiniComputerStamp(object CatalogueOwner, string Directory, string File, string Principal,
            WindowsOriginalFileIdentity DirectoryIdentity, WindowsOriginalFileIdentity FileIdentity,
            string DirectorySecurity, string FileSecurity);
        internal (object Stamp, string Fingerprint) CaptureOriginalMiniComputerStamp(object catalogueOwner)
        {
            lock (_gate)
            {
                Validate();
                var stamp = new MiniComputerStamp(catalogueOwner, DirectoryPath, DatabasePath, _principal,
                    DemandKind(_directory, true), DemandKind(_database, false), _directorySecurity, _databaseSecurity);
                var digest = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
                { Schema = 1, Platform = "windows", stamp.Directory, stamp.File, stamp.Principal,
                    stamp.DirectoryIdentity, stamp.FileIdentity, stamp.DirectorySecurity, stamp.FileSecurity })));
                Validate(); return (stamp, digest);
            }
        }
        internal bool IsOriginalMiniComputerStamp(object stamp, object catalogueOwner)
        {
            lock (_gate)
            {
                Validate();
                return stamp is MiniComputerStamp original && ReferenceEquals(original.CatalogueOwner, catalogueOwner) &&
                    original.Directory == DirectoryPath && original.File == DatabasePath && original.Principal == _principal &&
                    DemandKind(_directory, true).SameFile(original.DirectoryIdentity) &&
                    DemandKind(_database, false).SameReadVersion(original.FileIdentity) &&
                    original.DirectorySecurity == _directorySecurity && original.FileSecurity == _databaseSecurity;
            }
        }

        internal void ValidateOriginalMiniComputerReadHandle(SafeFileHandle actualReader)
        {
            lock (_gate)
            {
                Validate();
                DemandPinned(actualReader, DatabasePath, false, _expectedDatabase, _databaseSecurity);
                Validate();
            }
        }
    }
}
