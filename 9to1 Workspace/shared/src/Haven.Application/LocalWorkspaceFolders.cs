/*
 * FILE DOCUMENTATION
 * Where: src/Haven.Application/LocalWorkspaceFolders.cs, in the Application layer, which coordinates use cases through abstractions without owning platform details.
 * What: This file owns LocalWorkspaceFolders and LocalWorkspacePlacement: the one 9to1 workspace-root rule shared by Files, Boards and every other app that stores user documents on the device.
 * How: Pure static resolution plus one provisioning entry point; each step inspects the real directory and creates only what is missing.
 * Why: The owner's 29 October storage model needs the same answer in Files and in the apps that save into it, and it must never destroy a folder the user already owns.
 * Maintenance: Preserve the non-destructive guarantees. Do not add deletion, moving, renaming or merging here, and do not let a second copy of this rule appear inside one app.
 */

namespace Haven.Application;

/// <summary>What provisioning actually did, so a surface can state the real location and the reason
/// an app folder is where it is, instead of claiming a save happened somewhere unnamed.</summary>
public sealed record LocalWorkspacePlacement(
    string RootDirectory,
    string AppFoldersDirectory,
    bool RootWasCreated,
    bool AppsSubfolderUsed,
    IReadOnlyList<string> CreatedDirectories,
    IReadOnlyList<string> ExistingDirectories,
    IReadOnlyList<string> Warnings);

/// <summary>The default root plus why a non-default location was chosen, when that happened. The
/// reason must reach the surface: an owner whose Documents folder is unwritable has to be told the
/// workspace is in the application data folder instead of being shown Documents wording.</summary>
public sealed record DefaultWorkspaceRoot(string RootDirectory, string? DocumentsDirectory, string? FallbackReason)
{
    public bool UsedFallbackRoot => FallbackReason is not null;
}

/// <summary>The device-local 9to1 workspace root: the folder the owner sees as "9to1 Local Workspace"
/// in Files and the folder apps save into by default. Owner direction, 29 October 2026:
/// "it should automatically create a 9to1 folder under documents when the user installs the deb. And then
/// everything is saved to that unless the user picks another location. And if the user moves that 9to1
/// folder the app finds where it's saved and registers it there instead of in documents."</summary>
public static class LocalWorkspaceFolders
{
    /// <summary>The folder name the owner chose. Files shows this root as "9to1 Local Workspace".</summary>
    public const string WorkspaceFolderName = "9to1";

    /// <summary>Where the app folders go when the existing 9to1 root already holds the user's own
    /// directories, so the app never mixes its folders in among them.</summary>
    public const string AppsFolderName = "Apps";

    /// <summary>The hidden metadata folder a workspace root carries inside itself. It travels with the
    /// root when the user moves it, which is what makes re-registration possible.</summary>
    public const string MetadataFolderName = ".9to1-files";

    /// <summary>App id to folder name. Order is the order folders are created in, and the set is the
    /// same one Files setup has always created, so adopting an older root adds nothing new.</summary>
    public static readonly (string AppId, string FolderName)[] KnownAppFolders =
    [
        ("write", "Write"), ("present", "Present"), ("canvas", "Canvas"), ("picture", "Picture"),
        ("media", "Media"), ("sites", "Sites"), ("boards", "Boards"), ("games", "Games"),
    ];

    /// <summary>Resolves the user's Documents directory. Windows and macOS answer through the shell
    /// folder; Linux follows the xdg-user-dirs file, because Environment.SpecialFolder.MyDocuments
    /// returns an empty string there and a hardcoded English name would miss a localised Documents.</summary>
    public static string? ResolveDocumentsDirectory(string? homeDirectory = null, string? xdgConfigHome = null)
    {
        // An explicitly supplied home is authoritative: a redirected profile must not pick up the
        // documents folder of whichever account happens to own the process.
        var overrideHome = string.IsNullOrWhiteSpace(homeDirectory) ? null : HomeDirectory(homeDirectory);
        if (overrideHome is null)
        {
            var profileHome = HomeDirectory(null);
            var shell = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolderOption.DoNotVerify);
            // On Linux the shell folder can answer with the home directory itself. Using that would put
            // the workspace straight in the user's home, which is not the owner's Documents rule, so a
            // home-shaped answer is rejected and the XDG or conventional Documents folder is used.
            var usable = IsUsableDirectory(shell) && shell is not null &&
                !(profileHome is not null && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(shell)),
                    profileHome, StringComparison.Ordinal));
            if (usable) return Path.GetFullPath(shell!);
        }

        var home = overrideHome ?? HomeDirectory(null);
        if (home is null) return null;
        var config = Path.GetFullPath(string.IsNullOrWhiteSpace(xdgConfigHome)
            ? Path.Combine(home, ".config") : xdgConfigHome);
        var declared = ReadXdgUserDirectory(Path.Combine(config, "user-dirs.dirs"), home);
        if (IsUsableDirectory(declared)) return Path.GetFullPath(declared!);
        var conventional = Path.Combine(home, "Documents");
        return IsUsableDirectory(conventional) || !File.Exists(conventional) ? conventional : null;
    }

    private static string? HomeDirectory(string? overrideHome)
    {
        if (!string.IsNullOrWhiteSpace(overrideHome) && IsUsableDirectory(overrideHome))
            return Path.GetFullPath(overrideHome!);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);
        return IsUsableDirectory(home) ? Path.GetFullPath(home) : null;
    }

    /// <summary>Reads one entry from the xdg-user-dirs file, expanding only $HOME and ${HOME} because
    /// the specification defines those two forms. Other variables are left alone rather than guessed.</summary>
    private static string? ReadXdgUserDirectory(string userDirsFile, string homeDirectory)
    {
        if (!File.Exists(userDirsFile) || (File.GetAttributes(userDirsFile) & FileAttributes.ReparsePoint) != 0) return null;
        string? value = null;
        try
        {
            foreach (var line in File.ReadLines(userDirsFile))
            {
                var assignment = line.Trim();
                if (!assignment.StartsWith("XDG_DOCUMENTS_DIR=", StringComparison.Ordinal)) continue;
                value = assignment[(assignment.IndexOf('=') + 1)..].Trim().Trim('"', '\'');
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return null; }
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Replace("${HOME}", homeDirectory, StringComparison.Ordinal).Replace("$HOME", homeDirectory, StringComparison.Ordinal);
        return Path.IsPathRooted(value) ? value : Path.Combine(homeDirectory, value);
    }

    /// <summary>The root a fresh install gets: a 9to1 folder under Documents. When Documents cannot be
    /// used at all (no home, unwritable, blocked by a file), the caller's application-data directory is
    /// the fallback and the placement carries a warning so the surface says where it really went.</summary>
    public static DefaultWorkspaceRoot ResolveDefaultRoot(string applicationDataFallback,
        string? homeDirectory = null, string? xdgConfigHome = null, bool createDocuments = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataFallback);
        var documents = ResolveDocumentsDirectory(homeDirectory, xdgConfigHome);
        if (documents is not null)
        {
            documents = Path.GetFullPath(documents);
            if (!createDocuments) return new(Path.Combine(documents, WorkspaceFolderName), documents, null);
            var reason = EnsureDirectory(documents);
            return reason is null
                ? new(Path.Combine(documents, WorkspaceFolderName), documents, null)
                : new(Path.Combine(Path.GetFullPath(applicationDataFallback), WorkspaceFolderName), documents,
                    $"Documents is not usable ({documents}): {reason}");
        }
        return new(Path.Combine(Path.GetFullPath(applicationDataFallback), WorkspaceFolderName), null,
            "Documents: the user's Documents folder could not be located on this device.");
    }

    public static string DefaultRootDirectory(string applicationDataFallback, string? homeDirectory = null,
        string? xdgConfigHome = null) => ResolveDefaultRoot(applicationDataFallback, homeDirectory, xdgConfigHome).RootDirectory;

    /// <summary>True when this directory is a 9to1 workspace root, recognised by the metadata folder it
    /// carries rather than by its name. A user can rename the folder and the app still finds it.</summary>
    public static bool IsWorkspaceRoot(string directory) =>
        !string.IsNullOrWhiteSpace(directory) && Directory.Exists(MetadataPath(directory)) &&
        File.Exists(Path.Combine(MetadataPath(directory), "drive.json"));

    private static string MetadataPath(string directory) => Path.Combine(directory, MetadataFolderName);

    /// <summary>Creates only what is missing and reports exactly what was created, already present, or
    /// refused. This is the owner's inspect-never-delete rule:
    /// "if the user already has a 9to1 directory in documents, it shouldn't delete that and attempt to
    /// override it. inspect what's already there and add any missing directories. if it's already
    /// populated with a bunch of user-chosen directories that aren't app names, it should create a
    /// separate apps folder and put the app directories in there."</summary>
    public static LocalWorkspacePlacement Provision(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        RejectReparseChain(root);
        var created = new List<string>();
        var existing = new List<string>();
        var warnings = new List<string>();
        var rootWasCreated = !Directory.Exists(root);
        if (rootWasCreated && EnsureDirectory(root) is { } rootFailure)
            throw new IOException($"The 9to1 workspace folder could not be created in {root}: {rootFailure}");

        var apps = root;
        if (!rootWasCreated && !IsWorkspaceRoot(root) && ForeignOnlyContent(root))
        {
            // The folder is the user's own, so the app folders go beside their content in a separate
            // Apps folder instead of being interleaved with it.
            apps = Path.Combine(root, AppsFolderName);
            if (File.Exists(apps))
            {
                warnings.Add($"A file named {AppsFolderName} blocks the apps folder in {root}; app folders " +
                    "were not created. Move that file or choose another location.");
                return new LocalWorkspacePlacement(root, apps, rootWasCreated, true, created, existing, warnings);
            }
            if (Directory.Exists(apps)) existing.Add(apps); else if (EnsureDirectory(apps) is null) created.Add(apps);
        }

        foreach (var (_, folderName) in KnownAppFolders)
        {
            var path = Path.Combine(apps, folderName);
            if (Directory.Exists(path)) { existing.Add(path); continue; }
            if (File.Exists(path))
            {
                warnings.Add($"{path} is a file, not a folder, so the {folderName} app folder was not created.");
                continue;
            }
            if (EnsureDirectory(path) is { } failure) warnings.Add($"{path}: {failure}");
            else created.Add(path);
        }
        if (!Directory.Exists(MetadataPath(root)) && !File.Exists(MetadataPath(root)) &&
            EnsureDirectory(MetadataPath(root)) is null) created.Add(MetadataPath(root));
        return new LocalWorkspacePlacement(root, apps, rootWasCreated, !string.Equals(apps, root, StringComparison.Ordinal),
            created, existing, warnings);
    }

    /// <summary>The owner's "a bunch of user-chosen directories that aren't app names" test. A root that
    /// already contains an app-named entry - even if that entry is a file blocking a folder slot - is
    /// treated as app-shaped, so its folders stay at the root and the blocked name is reported instead
    /// of being shunted into a new Apps subfolder the user has never seen. An older workspace is
    /// recognised by its metadata folder, so adopting it only adds the folders it is missing. Mixed
    /// roots keep their app folders where they are and leave the user's own content exactly as it was.</summary>
    private static bool ForeignOnlyContent(string root)
    {
        var known = new HashSet<string>(KnownAppFolders.Select(folder => folder.FolderName),
            StringComparer.OrdinalIgnoreCase);
        var appShaped = false;
        var entries = 0;
        foreach (var entry in SafeEnumerateEntries(root))
        {
            entries++;
            var name = Path.GetFileName(entry);
            if (string.Equals(name, MetadataFolderName, StringComparison.Ordinal)) return false;
            if (known.Contains(name)) appShaped = true;
        }
        return entries > 0 && !appShaped;
    }

    private static IEnumerable<string> SafeEnumerateEntries(string root)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(root).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Array.Empty<string>(); }
    }

    /// <summary>Creates one directory, returning the first-line reason it failed instead of throwing,
    /// so one unwritable folder can be reported without abandoning the rest of provisioning.</summary>
    private static string? EnsureDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows() && !Directory.Exists(directory))
                return "the directory was not created";
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        { return error.Message.Split('\n', 2)[0]; }
    }

    /// <summary>Walks the path chain so a symlinked Documents or home cannot silently redirect the whole
    /// workspace onto another volume, matching the guard Files setup already applies to its own root.</summary>
    private static void RejectReparseChain(string directory)
    {
        for (string? current = directory; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (!Directory.Exists(current) && !File.Exists(current)) continue;
            if ((File.GetAttributes(current!) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException($"The 9to1 workspace cannot pass through the link at {current}.");
        }
    }

    /// <summary>Plausible parents to search when a registered root has disappeared: its own parent first,
    /// then the default Documents root and its parent, then the home directory. Bounded on purpose - a
    /// whole-disk walk would hang the UI and pick up unrelated folders.</summary>
    public static IReadOnlyList<string> CandidateScanParents(string? registeredRoot, string defaultRoot,
        string? homeDirectory = null)
    {
        var parents = new List<string?>();
        if (!string.IsNullOrWhiteSpace(registeredRoot)) parents.Add(Path.GetDirectoryName(Path.GetFullPath(registeredRoot!)));
        var documents = ResolveDocumentsDirectory(homeDirectory);
        if (documents is not null) { parents.Add(documents); parents.Add(Path.GetDirectoryName(Path.GetFullPath(documents))); }
        if (!string.IsNullOrWhiteSpace(defaultRoot)) parents.Add(Path.GetDirectoryName(Path.GetFullPath(defaultRoot)));
        parents.Add(HomeDirectory(homeDirectory));
        var desktop = HomeDirectory(homeDirectory) is { } knownHome
            ? Path.Combine(knownHome, "Desktop")
            : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrWhiteSpace(desktop)) parents.Add(desktop);
        return parents.OfType<string>().Select(parent => Path.TrimEndingDirectorySeparator(parent))
            .Where(parent => parent.Length > 0 && Directory.Exists(parent))
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Finds workspace roots inside the candidate parents, up to two levels deep, and returns
    /// every match. The caller decides: one match may re-register silently, several must be presented
    /// to the owner, and none must raise the re-locate screen rather than quietly creating a new folder.</summary>
    public static IReadOnlyList<string> DiscoverWorkspaceRoots(IEnumerable<string> candidateParents,
        int maxDepth = 2, int maxEntriesPerDirectory = 512)
    {
        var found = new List<string>();
        var queue = new Queue<(string Directory, int Depth)>();
        foreach (var parent in candidateParents) queue.Enqueue((parent, 0));
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (queue.Count > 0)
        {
            var (directory, depth) = queue.Dequeue();
            if (!visited.Add(directory)) continue;
            var children = 0;
            foreach (var entry in SafeEnumerateEntries(directory))
            {
                if (++children > maxEntriesPerDirectory) break;
                var attributes = Attributes(entry);
                if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (Path.GetFileName(entry).StartsWith('.') && !string.Equals(Path.GetFileName(entry),
                    WorkspaceFolderName, StringComparison.Ordinal)) continue;
                if (IsWorkspaceRoot(entry)) { found.Add(entry); continue; }
                if (depth < maxDepth) queue.Enqueue((entry, depth + 1));
            }
        }
        return found;
    }

    private static FileAttributes Attributes(string entry)
    {
        try { return File.GetAttributes(entry); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return 0; }
    }

    private static bool IsUsableDirectory(string? path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) &&
        (Directory.Exists(path) || !File.Exists(path));
}
