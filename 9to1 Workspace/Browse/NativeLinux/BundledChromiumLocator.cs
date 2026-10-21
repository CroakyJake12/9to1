using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Apps.Browse;

/// <summary>
/// A locator for the *bundled* Chromium runtime staged under the private engine root
/// (e.g. /opt/9to1-preview/browser) by the native ARM64 acquisition/staging pipeline.
/// This is deliberately SEPARATE from the default installed locator candidates
/// (<see cref="ChromiumLocator.Candidates"/>): it only ever returns the staged
/// launcher, and only after the stage manifest verifies. Missing, mismatched,
/// malformed or unverified bundles fail closed (return null). Detection is
/// presence/static-verification only — it never claims a validated runtime (native
/// acceptance remains NOTRUN until the owner's own ARM/GNOME validation).
/// </summary>
public class BundledChromiumLocator
{
    public static readonly BundledChromiumLocator Instance = new();

    public const string DefaultBrowserRoot = "/opt/9to1-preview/browser";

    private const string LauncherRel = "launcher";
    private const string ManifestRel = "manifest.json";
    private const string SandboxRel = "chromium/chrome-sandbox";
    private const string BinaryRel = "chromium/chromium";

    // Fixed minimum bundle runtime entries that must be declared in the manifest with
    // their exact type (regular vs symlink): launcher, main binary, sandbox, and the
    // private JPEG real ELF + its SONAME symlink. A manifest that only hashes whatever
    // rows are supplied (and thus stays non-empty after both JPEG entries are removed)
    // is not an acceptable stage: the runtime would be missing essential payloads.
    private static readonly (string Rel, bool IsSymlink)[] RequiredRuntimeEntries =
    {
        (LauncherRel, false),
        (BinaryRel, false),                         // chromium/chromium (AArch64 ELF)
        (SandboxRel, false),                        // chromium/chrome-sandbox
        ("chromium/lib/libjpeg.so.62.3.0", false),  // private JPEG real ELF
        ("chromium/lib/libjpeg.so.62", true),       // private JPEG SONAME symlink
    };

    // Vendor-normal setuid sandbox mode: 4755 octal == setuid + rwxr-xr-x.
    // C# has no octal literals, so express it as the UnixFileMode flag combination
    // (SetUser = 0o4000, User rwx = 0o700, Group r-x = 0o050, Other r-x = 0o005).
    private const UnixFileMode SetuidSandboxMode =
        UnixFileMode.SetUser |
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    // Pinned selected bundle version (Debian trixie-security).
    private const string PinnedVersion = "155.0.8059.79-1~deb13u1";
    private const string ManifestSchema = "9to1.chromium-native.stage-manifest/v1";

    // ELF AArch64: e_machine == EM_AARCH64 (183) at offset 18.
    private const ushort EM_AARCH64 = 183;

    /// <summary>Candidate launcher path (the bundled one only).</summary>
    public virtual string CandidateLauncher => Path.Combine(DefaultBrowserRoot, LauncherRel);

    /// <summary>The bundle root, derived from the candidate launcher (parent dir), so
    /// test subclasses can inject a temp root by overriding <see cref="CandidateLauncher"/>.</summary>
    public virtual string LauncherRoot
    {
        get
        {
            var dir = Path.GetDirectoryName(CandidateLauncher);
            return string.IsNullOrEmpty(dir) ? DefaultBrowserRoot : dir;
        }
    }

    /// <summary>True when the bundled launcher is an executable regular file. A
    /// directory (even mode 0755) or a symlink is never accepted — only a regular
    /// file with at least one execute bit counts.</summary>
    public virtual bool IsExecutable(string path)
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            var fi = new FileInfo(path);
            if (fi.LinkTarget is not null) return false; // symlink is not a launcher file
            if (fi.Attributes.HasFlag(FileAttributes.Directory)) return false; // dir, not a file
            if (!fi.Exists) return false;
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode exec = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (mode & exec) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Returns the bundled launcher path when the stage manifest verifies (pinned
    /// version, vendor sandbox mode + expected root owner, full manifest file
    /// size/hash + contained symlink targets, AArch64 binary), else null. This
    /// locator never touches the default installed candidates.
    /// </summary>
    public virtual string? Find()
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (!IsExecutable(CandidateLauncher)) return null;
        return IsBundleVerified(new DirectoryInfo(LauncherRoot)) ? CandidateLauncher : null;
    }

    /// <summary>Static verification of the staged bundle (no execution). Fail-closed:
    /// any malformed JSON, missing/mis-typed field, IO error, escaped path, duplicate,
    /// or payload mismatch returns false (never throws into the UI).</summary>
    public virtual bool IsBundleVerified(DirectoryInfo? root)
    {
        try
        {
            return IsBundleVerifiedCore(root);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsBundleVerifiedCore(DirectoryInfo? root)
    {
        if (root is null || !root.Exists) return false;
        var manifestPath = Path.Combine(root.FullName, ManifestRel);
        if (!File.Exists(manifestPath)) return false;

        JsonDocument doc;
        try
        {
            using var fs = File.OpenRead(manifestPath);
            doc = JsonDocument.Parse(fs);
        }
        catch (Exception)
        {
            return false;
        }

        using (doc)
        {
            var rootEl = doc.RootElement;
            // Schema + pinned version (strict string values).
            if (!TryGetString(rootEl, "schema", out var schema) || schema != ManifestSchema) return false;
            if (!TryGetString(rootEl, "version", out var version) || version != PinnedVersion) return false;

            // Sandbox: on-disk mode must be 4755 and the manifest must record the
            // expected root owner (uid/gid 0, numeric). The observed uid is recorded
            // truthfully and is NOT required to be 0 here (unprivileged staging; the
            // root-only packager applies uid/gid 0 via --root-owner-group).
            var sbPath = Path.Combine(root.FullName, SandboxRel);
            if (!IsSetuidSandbox(sbPath)) return false;
            if (!rootEl.TryGetProperty("sandbox", out var sb)) return false;
            if (!sb.TryGetProperty("expected_owner", out var owner)) return false;
            if (!TryGetInt(owner, "uid", out var uid) || uid != 0) return false;
            if (!TryGetInt(owner, "gid", out var gid) || gid != 0) return false;

            // Full manifest file/symlink verification: every regular payload file must
            // carry numeric bytes + string sha256 and match disk (the main binary is
            // verified here exactly once, not skipped); symlinks must carry a contained
            // target; no duplicate/rooted/'..' path; nothing unlisted on disk except the
            // single manifest metadata file.
            if (!VerifyManifestFiles(root, rootEl)) return false;

            // Binary is AArch64 ELF (header check, in addition to the sha256 above).
            if (!IsAArch64Elf(Path.Combine(root.FullName, BinaryRel))) return false;
        }
        return true;
    }

    private static bool TryGetString(JsonElement el, string name, out string value)
    {
        value = "";
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return false;
        value = v.GetString()!;
        return true;
    }

    private static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return false;
        if (!v.TryGetInt32(out value)) return false;
        return true;
    }

    private static bool TryNormalizeRel(string rel, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrEmpty(rel)) return false;
        if (rel[0] == '/' || rel[0] == '\\') return false; // rooted
        var parts = rel.Split('/', '\\');
        foreach (var p in parts)
        {
            if (p == "..") return false;
        }
        normalized = rel.Replace('\\', '/');
        return true;
    }

    private static bool VerifyManifestFiles(DirectoryInfo root, JsonElement rootEl)
    {
        if (!rootEl.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            return false;

        var rootReal = Path.GetFullPath(root.FullName);
        var listed = new HashSet<string>(StringComparer.Ordinal);
        var declaredRegular = new HashSet<string>(StringComparer.Ordinal);
        var declaredLinks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in files.EnumerateArray())
        {
            if (!TryGetString(entry, "path", out var rel)) return false;
            if (!TryNormalizeRel(rel, out var normalizedRel)) return false;
            if (!listed.Add(normalizedRel)) return false; // duplicate path

            var full = Path.Combine(rootReal, normalizedRel);
            if (entry.TryGetProperty("link_target", out var lt))
            {
                if (lt.ValueKind != JsonValueKind.String) return false;
                var fi = new FileInfo(full);
                if (fi.LinkTarget is null) return false;
                if (!string.Equals(fi.LinkTarget, lt.GetString(), StringComparison.Ordinal)) return false;
                // Containment: the resolved target must stay within the stage root.
                string resolved;
                try { resolved = Path.GetFullPath(fi.ResolveLinkTarget(true)?.FullName ?? full); }
                catch (Exception) { return false; }
                if (!IsWithin(rootReal, resolved)) return false;
                declaredLinks.Add(normalizedRel);
                continue;
            }

            // Regular file: mandatory bytes + sha256, matched against disk. This is the
            // only integrity check for every payload file, including the main binary. The
            // entry must be a physically contained REGULAR file: a symlink (file or
            // ancestor directory) reaching outside the root, or a directory, is refused.
            if (!entry.TryGetProperty("bytes", out var bytes) || bytes.ValueKind != JsonValueKind.Number)
                return false;
            if (!entry.TryGetProperty("sha256", out var sha) || sha.ValueKind != JsonValueKind.String)
                return false;
            if (!IsContainedRegularFile(rootReal, full)) return false;
            var f = new FileInfo(full);
            if (bytes.GetInt64() != f.Length) return false;
            if (!Sha256Equals(sha.GetString()!, full)) return false;
            declaredRegular.Add(normalizedRel);
        }

        if (listed.Count == 0) return false; // empty files list

        // The fixed minimum runtime set must be declared with the correct type.
        foreach (var req in RequiredRuntimeEntries)
        {
            bool ok = req.IsSymlink ? declaredLinks.Contains(req.Rel) : declaredRegular.Contains(req.Rel);
            if (!ok) return false;
        }

        // Reject unlisted entries on disk, EXCEPT the single stage manifest metadata
        // file. Directory symlinks are treated as entries and NEVER followed: an
        // undeclared symlink (file or directory) is rejected, so a directory symlink
        // pointing outside the root cannot smuggle unlisted files into the bundle.
        if (!VerifyNoUnlistedEntries(root, rootReal, listed, declaredLinks)) return false;
        return true;
    }

    private static bool IsContainedRegularFile(string rootReal, string full)
    {
        try
        {
            var fi = new FileInfo(full);
            if (fi.LinkTarget is not null) return false;                       // must be a real file
            if (fi.Attributes.HasFlag(FileAttributes.Directory)) return false; // must not be a directory
            if (!fi.Exists) return false;
            // Every ancestor directory up to (and including) the root must resolve inside
            // the root; a directory symlink escaping the root is rejected.
            var dir = fi.Directory;
            while (dir is not null)
            {
                var resolved = dir.ResolveLinkTarget(false);
                if (resolved is not null && !IsWithin(rootReal, Path.GetFullPath(resolved.FullName)))
                    return false;
                if (string.Equals(dir.FullName, rootReal, StringComparison.Ordinal)) break;
                if (dir.FullName.Length < rootReal.Length) break;
                dir = dir.Parent;
            }
            return IsWithin(rootReal, Path.GetFullPath(full));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool VerifyNoUnlistedEntries(DirectoryInfo root, string rootReal,
        HashSet<string> listed, HashSet<string> declaredLinks)
    {
        try
        {
            return ScanDirNoFollow(root, rootReal, listed, declaredLinks);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ScanDirNoFollow(DirectoryInfo dir, string rootReal,
        HashSet<string> listed, HashSet<string> declaredLinks)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(dir.FullName))
        {
            var fi = new FileInfo(entry);
            var rel = Path.GetRelativePath(rootReal, entry).Replace('\\', '/');
            if (rel == ManifestRel) continue; // manifest is metadata, not payload
            bool isLink = fi.LinkTarget is not null;
            bool isDir = fi.Attributes.HasFlag(FileAttributes.Directory);
            if (isLink)
            {
                // Any symlink must be declared as a symlink entry; never followed.
                if (!declaredLinks.Contains(rel)) return false;
                continue;
            }
            if (isDir)
            {
                if (!ScanDirNoFollow(new DirectoryInfo(entry), rootReal, listed, declaredLinks))
                    return false;
            }
            else
            {
                if (!listed.Contains(rel)) return false;
            }
        }
        return true;
    }

    private static bool IsWithin(string rootReal, string path)
    {
        var r = rootReal.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                 Path.DirectorySeparatorChar;
        return path.StartsWith(r, StringComparison.Ordinal) || string.Equals(path, rootReal, StringComparison.Ordinal);
    }

    private static bool Sha256Equals(string expected, string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(fs));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsSetuidSandbox(string path)
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            var mode = File.GetUnixFileMode(path);
            return mode == SetuidSandboxMode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsAArch64Elf(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 20) return false;
            Span<byte> hdr = stackalloc byte[20];
            if (fs.Read(hdr) != 20) return false;
            if (hdr[0] != 0x7f || hdr[1] != (byte)'E' || hdr[2] != (byte)'L' || hdr[3] != (byte)'F')
                return false;
            return BitConverter.ToUInt16(hdr[18..]) == EM_AARCH64;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
