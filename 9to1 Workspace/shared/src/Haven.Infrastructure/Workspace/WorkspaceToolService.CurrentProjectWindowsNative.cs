using System.Text.Json;
using Haven.Application;

namespace Haven.Infrastructure;

public sealed partial class WorkspaceToolService
{
    private sealed partial class CurrentProjectNativeSource
    {
        private void CaptureWindowsDescriptors(Read read)
        {
            RequireDeveloperWindowsExports(); var descriptor = read.Descriptor;
            var ancestor = NormalizeDeveloperWindowsPath(store.OriginalWorkspaceMetadataAncestor);
            var directory = NormalizeDeveloperWindowsPath(store.OriginalWorkspaceMetadataDirectory);
            if (!IsWithinRoot(ancestor, directory, StringComparison.OrdinalIgnoreCase) || string.Equals(ancestor, directory, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The actual configured Windows metadata ancestor is unavailable.");
            var components = Path.GetRelativePath(ancestor, directory).Split(Path.DirectorySeparatorChar);
            if (components.Length is < 1 or > 4 || components.Any(value => !SafeDeveloperWindowsLeaf(value)))
                throw new UnauthorizedAccessException("The actual Windows metadata directory exceeds its bounded configured ancestry.");
            read.WindowsSid = CurrentDeveloperWindowsSid();
            read.RegistrationPath = descriptor.OriginalRegistrationStatePath; read.WorkingPath = descriptor.RegisteredProjectRoot;
            read.WindowsMetadataRoot = new DeveloperWindowsRootLease(ancestor);
            using (var parent = read.WindowsMetadataRoot.OpenDirectory(directory)) DemandDeveloperWindowsOwner(parent, read.WindowsSid);
            read.MetadataPath = Path.Combine(directory, descriptor.WorkspaceId.ToString("N") + ".json");
            read.MetadataHandle = read.WindowsMetadataRoot.OpenRead(read.MetadataPath); DemandDeveloperWindowsOwner(read.MetadataHandle, read.WindowsSid);
            read.WindowsMetadataIdentity = ReadDeveloperWindowsIdentity(read.MetadataHandle);
            read.WindowsFilesRoot = new DeveloperWindowsRootLease(descriptor.ConfiguredFilesRoot);
            using (var parent = read.WindowsFilesRoot.OpenDirectory(descriptor.ConfiguredFilesRoot)) DemandDeveloperWindowsOwner(parent, read.WindowsSid);
            read.RegistrationHandle = read.WindowsFilesRoot.OpenRead(read.RegistrationPath); DemandDeveloperWindowsOwner(read.RegistrationHandle, read.WindowsSid);
            read.WindowsRegistrationIdentity = ReadDeveloperWindowsIdentity(read.RegistrationHandle);
            read.WorkingRoot = read.WindowsFilesRoot.OpenDirectory(read.WorkingPath); DemandDeveloperWindowsOwner(read.WorkingRoot, read.WindowsSid);
            read.WindowsWorkingIdentity = ReadDeveloperWindowsIdentity(read.WorkingRoot);
        }
        private static string WindowsFingerprint(Read read, JsonElement registration)
        {
            if (read.WindowsWorkingIdentity is not { } root || read.WindowsSid is null) throw new UnauthorizedAccessException("No original Windows project identity exists.");
            return Digest(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Platform = "Windows", Registration = registration,
                Root = new { root.Volume, root.IdLow, root.IdHigh }, read.Descriptor.ConfiguredFilesRoot,
                read.Descriptor.RegisteredProjectRoot, OriginalSid = read.WindowsSid
            }));
        }
        private static void DemandWindowsNative(Read read)
        {
            if (read.NativeClose is not null) throw new ObjectDisposedException("current Windows project native descriptors");
            DemandDeveloperWindowsSid(read.WindowsSid!); read.WindowsMetadataRoot!.DemandCurrent(); read.WindowsFilesRoot!.DemandCurrent();
            DemandDeveloperWindowsPath(read.MetadataHandle!, read.MetadataPath, false);
            DemandDeveloperWindowsPath(read.RegistrationHandle!, read.RegistrationPath, false);
            DemandDeveloperWindowsPath(read.WorkingRoot!, read.WorkingPath, true); DemandDeveloperWindowsCase(read.WorkingRoot!);
            DemandDeveloperWindowsOwner(read.MetadataHandle!, read.WindowsSid!); DemandDeveloperWindowsOwner(read.RegistrationHandle!, read.WindowsSid!);
            DemandDeveloperWindowsOwner(read.WorkingRoot!, read.WindowsSid!);
            var metadata = ReadDeveloperWindowsIdentity(read.MetadataHandle!); var registration = ReadDeveloperWindowsIdentity(read.RegistrationHandle!);
            var root = ReadDeveloperWindowsIdentity(read.WorkingRoot!);
            if (read.WindowsMetadataIdentity is not { } expectedMetadata || read.WindowsRegistrationIdentity is not { } expectedRegistration ||
                read.WindowsWorkingIdentity is not { } expectedRoot || !metadata.IsRegular || metadata.Links != 1 || !metadata.SameReadVersion(expectedMetadata) ||
                !registration.IsRegular || registration.Links != 1 || !registration.SameReadVersion(expectedRegistration) || !root.IsDirectory || !root.SameFile(expectedRoot))
                throw new IOException("The actual Windows saved document/registration/root descriptor pin changed.");
        }
    }
    private sealed partial class DeveloperCaptureSource
    {
        private static void CaptureWindowsSavedRoot(SavedRootPin pin)
        {
            var physical = pin.Preparation.Capture.Physical;
            pin.WindowsSid = pin.Preparation.WindowsSid ?? throw new UnauthorizedAccessException("No genuine original Windows metadata principal exists.");
            DemandDeveloperWindowsSid(pin.WindowsSid);
            pin.WindowsMetadataRoot = new DeveloperWindowsRootLease(pin.Preparation.Ancestor);
            pin.WindowsFilesRoot = new DeveloperWindowsRootLease(pin.Evidence.OriginalFilesRoot);
            pin.MetadataHandle = pin.WindowsMetadataRoot.OpenRead(SavedMetadataPath(pin)); DemandDeveloperWindowsOwner(pin.MetadataHandle, pin.WindowsSid);
            pin.WindowsMetadataIdentity = ReadDeveloperWindowsIdentity(pin.MetadataHandle);
            if (((WorkspaceMetadataObservation)pin.Evidence.OriginalMetadataObservation).WindowsIdentity is not { } committed ||
                !pin.WindowsMetadataIdentity.Value.SameReadVersion(committed)) throw new IOException("The current Windows workspace file/version differs from its original native acknowledgment.");
            pin.RegistrationHandle = pin.WindowsFilesRoot.OpenRead(pin.Evidence.OriginalRegistrationStatePath); DemandDeveloperWindowsOwner(pin.RegistrationHandle, pin.WindowsSid);
            pin.WindowsRegistrationIdentity = ReadDeveloperWindowsIdentity(pin.RegistrationHandle);
            pin.WorkingRoot = pin.WindowsFilesRoot.OpenDirectory(pin.Binding.CanonicalRoot); DemandDeveloperWindowsOwner(pin.WorkingRoot, pin.WindowsSid);
            pin.WindowsWorkingIdentity = ReadDeveloperWindowsIdentity(pin.WorkingRoot);
            if (physical.WindowsProjectIdentity is not { } original || !pin.WindowsWorkingIdentity.Value.IsDirectory ||
                !pin.WindowsWorkingIdentity.Value.SameFile(original)) throw new UnauthorizedAccessException("The actual registered Windows working root was replaced.");
        }
        private static void DemandWindowsSavedRoot(SavedRootPin pin)
        {
            DemandDeveloperWindowsSid(pin.WindowsSid!); pin.WindowsMetadataRoot!.DemandCurrent(); pin.WindowsFilesRoot!.DemandCurrent();
            DemandDeveloperWindowsPath(pin.MetadataHandle!, SavedMetadataPath(pin), false);
            DemandDeveloperWindowsPath(pin.RegistrationHandle!, pin.Evidence.OriginalRegistrationStatePath, false);
            DemandDeveloperWindowsPath(pin.WorkingRoot!, pin.Binding.CanonicalRoot, true); DemandDeveloperWindowsCase(pin.WorkingRoot!);
            DemandDeveloperWindowsOwner(pin.MetadataHandle!, pin.WindowsSid!); DemandDeveloperWindowsOwner(pin.RegistrationHandle!, pin.WindowsSid!);
            DemandDeveloperWindowsOwner(pin.WorkingRoot!, pin.WindowsSid!);
            var metadata = ReadDeveloperWindowsIdentity(pin.MetadataHandle!); var registration = ReadDeveloperWindowsIdentity(pin.RegistrationHandle!);
            var working = ReadDeveloperWindowsIdentity(pin.WorkingRoot!);
            if (pin.WindowsMetadataIdentity is not { } expectedMetadata || pin.WindowsRegistrationIdentity is not { } expectedRegistration ||
                pin.WindowsWorkingIdentity is not { } expectedWorking || !metadata.IsRegular || metadata.Links != 1 || !metadata.SameReadVersion(expectedMetadata) ||
                !registration.IsRegular || registration.Links != 1 || !registration.SameReadVersion(expectedRegistration) || !working.IsDirectory || !working.SameFile(expectedWorking))
                throw new IOException("The actual Windows saved document/registration version or working-root identity changed before native Start.");
        }
    }
}
