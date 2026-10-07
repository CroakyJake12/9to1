using System.Buffers.Binary;
using Haven.Infrastructure.Native.Windows;
using Microsoft.Win32.SafeHandles;

namespace Haven.Infrastructure;

internal sealed partial class NativePersonalTaskRecoveryStore
{
    // These are bounded native binary SID encodings, never a username or CAKE actor.
    // Root policy explicitly trusts Windows SYSTEM and BUILTIN Administrators.
    private const string WindowsSystemSid = "010100000000000512000000"; // S-1-5-18
    private const string WindowsAdministratorsSid = "01020000000000052000000020020000"; // S-1-5-32-544

    // The create-new owner also uses this SAME actual-handle predicate. No path,
    // descriptor, default token or previously successful setup substitutes for it.
    internal static string DemandOriginalWindowsPrivacy(SafeFileHandle handle, string principal) =>
        DemandProtectedWindowsSecurity(handle, principal).Fingerprint;

    private static WindowsOriginalSecurityObservation DemandProtectedWindowsSecurity(SafeFileHandle handle, string principal)
    {
        var actual = ReadBoundedWindowsSecurity(handle, principal);
        if (!actual.DaclProtected || actual.Dacl.Any(ace => (ace.Flags & ~0x03) != 0))
            throw new UnauthorizedAccessException("The actual Windows root/database/key requires a protected, explicit private DACL.");
        WindowsOriginalFileCustody.DemandCurrentSid(principal);
        return actual;
    }

    private static WindowsOriginalSecurityObservation ReadBoundedWindowsSecurity(SafeFileHandle handle, string principal)
    {
        WindowsOriginalFileCustody.DemandCurrentSid(principal);
        WindowsOriginalFileCustody.DemandOwner(handle, principal);
        var actual = WindowsOriginalFileCustody.ReadSecurity(handle);
        if (actual.OwnerSid != principal || actual.Revision != 1 || !actual.DaclPresent || actual.DaclIsNull ||
            actual.AclRevision is not (2 or 4) || actual.Dacl.Count > 64 ||
            actual.DaclProtected != ((actual.Control & 0x1000) != 0) ||
            actual.DaclDefaulted != ((actual.Control & 0x08) != 0) ||
            actual.Fingerprint.Length != 64 || actual.Fingerprint.Any(value => !Uri.IsHexDigit(value)))
            throw new UnauthorizedAccessException("The actual Windows personal-store owner/DACL observation is unsupported or unbounded.");
        foreach (var ace in actual.Dacl)
        {
            // Basic native allow/deny, concrete file-right subset and counted binary
            // SID only. Generic/object/callback/conditional rights never qualify.
            if (ace.Type is not (0 or 1) || ace.AccessMask is not { } mask || (mask & ~0x001f01ffu) != 0 ||
                ace.Sid is null || ace.Sid != principal && ace.Sid != WindowsSystemSid && ace.Sid != WindowsAdministratorsSid)
                throw new UnauthorizedAccessException("The original Windows personal-store DACL names a broad/foreign trustee or unsupported ACE.");
            byte[] raw;
            try { raw = Convert.FromHexString(ace.RawHex); }
            catch (FormatException error) { throw new UnauthorizedAccessException("The actual Windows ACE has an invalid binary observation.", error); }
            if (raw.Length < 16 || raw.Length > 76 || raw[0] != ace.Type || raw[1] != ace.Flags ||
                BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2, 2)) != raw.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4, 4)) != mask || raw[8] != 1 || raw[9] > 15 ||
                raw.Length != 16 + 4 * raw[9] || Convert.ToHexString(raw.AsSpan(8)) != ace.Sid)
                throw new UnauthorizedAccessException("The actual Windows basic ACE does not match its counted native SID/mask/layout.");
        }
        WindowsOriginalFileCustody.DemandCurrentSid(principal);
        return actual;
    }

    private static string DemandWindowsCompanionPrivacy(SafeFileHandle handle, string principal,
        WindowsOriginalSecurityObservation sameProtectedParent)
    {
        var actual = ReadBoundedWindowsSecurity(handle, principal);
        if (actual.DaclProtected)
        {
            if (actual.Dacl.Any(ace => (ace.Flags & ~0x03) != 0))
                throw new UnauthorizedAccessException("The protected Windows companion has inherited or unsupported ACE flags.");
        }
        else
        {
            // Current effective protection only: Windows metadata cannot establish
            // historical parent-at-creation provenance. Every child ACE must be the
            // ordered exact OI projection of THIS admitted SAME held parent.
            var projected = sameProtectedParent.Dacl.Where(ace => (ace.Flags & 1) != 0).ToArray();
            if (!sameProtectedParent.DaclProtected || projected.Length == 0 || actual.Dacl.Count != projected.Length)
                throw new UnauthorizedAccessException("The Windows companion lacks the SAME protected parent's complete file inheritance projection.");
            for (var index = 0; index < projected.Length; index++)
            {
                var inherited = actual.Dacl[index]; var parent = projected[index];
                var raw = Convert.FromHexString(parent.RawHex); raw[1] = 0x10;
                if (inherited.Flags != 0x10 || inherited.Type != parent.Type || inherited.AccessMask != parent.AccessMask ||
                    inherited.Sid != parent.Sid || inherited.RawHex != Convert.ToHexString(raw))
                    throw new UnauthorizedAccessException("The Windows companion contains explicit/mixed/foreign or unsupported inherited policy.");
            }
        }
        WindowsOriginalFileCustody.DemandCurrentSid(principal);
        return actual.Fingerprint;
    }
}
