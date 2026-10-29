// FILE DOCUMENTATION
// What: Runs the owner's 29 October workspace-root rules against real temporary directories: default
//       Documents location, inspect-never-delete provisioning, the apps subfolder for a folder that holds
//       the user's own content, root discovery after the user moves the folder, and blocked paths.
// Why: These are the storage guarantees behind "it should automatically create a 9to1 folder under
//       documents" and "if the user already has a 9to1 directory in documents, it shouldn't delete that".
// How: Every case creates actual directories and files on disk and asserts the real resulting tree.

using Haven.Application;

namespace Haven.Application.Tests;

public sealed class LocalWorkspaceFolderProvisioningTests
{
    private sealed record Scratch : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "9to1-workspace-" + Guid.NewGuid().ToString("N"));
        public string Home => Path.Combine(Root, "home");
        public string Documents => Path.Combine(Home, "Documents");
        public string AppData => Path.Combine(Home, ".local", "share", "9to1", "profile");
        public void Create()
        {
            Directory.CreateDirectory(Documents);
            Directory.CreateDirectory(AppData);
        }
        public void Dispose()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch (IOException) { }
        }
    }

    private static void Write(string path, string contents = "x")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }

    private static IReadOnlyList<string> Tree(string directory) =>
        Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Select(entry => Path.GetRelativePath(directory, entry).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(entry => entry, StringComparer.Ordinal).ToArray();

    [Fact]
    public void FreshInstallCreatesDocumentsNineToDefaultAndEveryAppFolder()
    {
        using var scratch = new Scratch();
        scratch.Create();

        var root = LocalWorkspaceFolders.DefaultRootDirectory(scratch.AppData, scratch.Home);
        Assert.Equal(Path.Combine(scratch.Documents, "9to1"), root);
        Assert.False(Directory.Exists(root));

        var plan = LocalWorkspaceFolders.Provision(root);

        Assert.True(plan.RootWasCreated);
        Assert.False(plan.AppsSubfolderUsed);
        Assert.Equal(root, plan.AppFoldersDirectory);
        Assert.Equal(LocalWorkspaceFolders.KnownAppFolders.Length + 1, plan.CreatedDirectories.Count);
        foreach (var (_, folder) in LocalWorkspaceFolders.KnownAppFolders)
            Assert.True(Directory.Exists(Path.Combine(root, folder)), $"{folder} was not created");
        Assert.True(Directory.Exists(Path.Combine(root, ".9to1-files")));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void ExistingForeignFolderKeepsItsOwnContentAndGainsASeparateAppsFolder()
    {
        using var scratch = new Scratch();
        scratch.Create();
        var root = Path.Combine(scratch.Documents, "9to1");
        // The owner's own folders: what they had already put in a 9to1 folder they made themselves.
        Write(Path.Combine(root, "holiday-photos", "beach.jpg"), "owner photo");
        Write(Path.Combine(root, "taxes 2026", "return.pdf"), "owner pdf");
        Write(Path.Combine(root, "scratch note.txt"), "owner note");
        var before = Tree(root);

        var plan = LocalWorkspaceFolders.Provision(root);

        Assert.False(plan.RootWasCreated);
        Assert.True(plan.AppsSubfolderUsed);
        Assert.Equal(Path.Combine(root, "Apps"), plan.AppFoldersDirectory);
        Assert.True(Directory.Exists(Path.Combine(root, "Apps", "Boards")));
        Assert.True(Directory.Exists(Path.Combine(root, "Apps", "Write")));
        // Nothing of the owner's is moved, renamed, emptied or deleted, and no app folder is mixed in
        // beside their own directories.
        var after = Tree(root);
        foreach (var entry in before) Assert.Contains(entry, after);
        var expected = new HashSet<string>(before, StringComparer.Ordinal) { "Apps", ".9to1-files" };
        foreach (var app in LocalWorkspaceFolders.KnownAppFolders) expected.Add($"Apps/{app.FolderName}");
        Assert.True(expected.SetEquals(after),
            "unexpected entries: " + string.Join(", ", after.Except(expected).Concat(expected.Except(after))));
        Assert.Equal("owner photo", File.ReadAllText(Path.Combine(root, "holiday-photos", "beach.jpg")));
        Assert.Equal("owner note", File.ReadAllText(Path.Combine(root, "scratch note.txt")));
        foreach (var (_, folder) in LocalWorkspaceFolders.KnownAppFolders)
        {
            Assert.True(Directory.Exists(Path.Combine(root, "Apps", folder)));
            Assert.False(Directory.Exists(Path.Combine(root, folder)), $"{folder} must not be created beside the owner's own folders");
        }
    }

    [Fact]
    public void ExistingWorkspaceRootOnlyGainsTheFoldersItIsMissing()
    {
        using var scratch = new Scratch();
        scratch.Create();
        var root = Path.Combine(scratch.Documents, "9to1");
        // An older workspace shape: metadata plus a subset of the app folders, with content inside one.
        Write(Path.Combine(root, ".9to1-files", "drive.json"), """{"state":{"storeId":"11111111-1111-1111-1111-111111111111"}}""");
        Write(Path.Combine(root, "Boards", "Existing board.9to1board"), "board bytes");
        Write(Path.Combine(root, "Write", "keep.txt"), "kept");

        var plan = LocalWorkspaceFolders.Provision(root);

        Assert.True(LocalWorkspaceFolders.IsWorkspaceRoot(root));
        Assert.False(plan.RootWasCreated);
        Assert.False(plan.AppsSubfolderUsed);
        Assert.Equal(root, plan.AppFoldersDirectory);
        Assert.DoesNotContain(Path.Combine(root, "Boards"), plan.CreatedDirectories);
        Assert.Contains(Path.Combine(root, "Boards"), plan.ExistingDirectories);
        Assert.Equal("board bytes", File.ReadAllText(Path.Combine(root, "Boards", "Existing board.9to1board")));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(root, "Write", "keep.txt")));
        Assert.Equal("""{"state":{"storeId":"11111111-1111-1111-1111-111111111111"}}""",
            File.ReadAllText(Path.Combine(root, ".9to1-files", "drive.json")));
        foreach (var (_, folder) in LocalWorkspaceFolders.KnownAppFolders)
            Assert.True(Directory.Exists(Path.Combine(root, folder)));
    }

    [Fact]
    public void MixedRootAddsItsMissingAppFoldersWithoutDisturbingTheUsersOwnDirectories()
    {
        using var scratch = new Scratch();
        scratch.Create();
        var root = Path.Combine(scratch.Documents, "9to1");
        Write(Path.Combine(root, "Boards", "board.9to1board"), "board");
        Write(Path.Combine(root, "my own project", "readme.md"), "mine");

        var plan = LocalWorkspaceFolders.Provision(root);

        Assert.False(plan.AppsSubfolderUsed);
        Assert.True(Directory.Exists(Path.Combine(root, "Boards")));
        Assert.True(Directory.Exists(Path.Combine(root, "Games")));
        Assert.Equal("board", File.ReadAllText(Path.Combine(root, "Boards", "board.9to1board")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(root, "my own project", "readme.md")));
        Assert.False(Directory.Exists(Path.Combine(root, "Apps")));
    }

    [Fact]
    public void AppFolderNamesAlreadyUsedByFilesAreReportedAndNeverTouched()
    {
        using var scratch = new Scratch();
        scratch.Create();
        var root = Path.Combine(scratch.Documents, "9to1");
        Write(Path.Combine(root, "Present"), "a file where a folder belongs");

        var plan = LocalWorkspaceFolders.Provision(root);

        Assert.Contains(plan.Warnings, warning => warning.Contains("Present", StringComparison.Ordinal) &&
            warning.Contains("is a file", StringComparison.Ordinal));
        Assert.Equal("a file where a folder belongs", File.ReadAllText(Path.Combine(root, "Present")));
        Assert.DoesNotContain(Path.Combine(root, "Present"), plan.CreatedDirectories);
        Assert.True(Directory.Exists(Path.Combine(root, "Boards")));
    }

    [Fact]
    public void ForeignAppsFolderBlockedByAFileStopsShortWithAClearWarning()
    {
        using var scratch = new Scratch();
        scratch.Create();
        var root = Path.Combine(scratch.Documents, "9to1");
        Write(Path.Combine(root, "my notes.txt"), "mine");
        Write(Path.Combine(root, "Apps"), "a file where the apps folder belongs");

        var plan = LocalWorkspaceFolders.Provision(root);

        Assert.True(plan.AppsSubfolderUsed);
        Assert.Contains(plan.Warnings, warning => warning.Contains("blocks the apps folder", StringComparison.Ordinal));
        Assert.Equal("a file where the apps folder belongs", File.ReadAllText(Path.Combine(root, "Apps")));
        Assert.Empty(plan.CreatedDirectories);
    }

    [Fact]
    public void UnwritableDocumentsLocationFallsBackToApplicationDataAndSaysSo()
    {
        using var scratch = new Scratch();
        scratch.Create();
        // A file where Documents should be: the resolver must not delete it to make room.
        Directory.Delete(scratch.Documents);
        Write(scratch.Documents, "not a folder");

        var resolved = LocalWorkspaceFolders.ResolveDefaultRoot(scratch.AppData, scratch.Home);
        Assert.Equal(Path.Combine(scratch.AppData, "9to1"), resolved.RootDirectory);
        Assert.True(resolved.UsedFallbackRoot);
        Assert.Contains("Documents", resolved.FallbackReason, StringComparison.Ordinal);
        var plan = LocalWorkspaceFolders.Provision(resolved.RootDirectory);
        Assert.True(plan.RootWasCreated);
        Assert.Equal("not a folder", File.ReadAllText(scratch.Documents));
    }

    [Fact]
    public void LocalisedDocumentsFolderIsReadFromXdgUserDirs()
    {
        using var scratch = new Scratch();
        scratch.Create();
        var localised = Path.Combine(scratch.Home, "Documentos");
        Directory.CreateDirectory(localised);
        Write(Path.Combine(scratch.Home, ".config", "user-dirs.dirs"),
            "# Generated by xdg-user-dirs\nXDG_DOCUMENTS_DIR=\"$HOME/Documentos\"\nXDG_DESKTOP_DIR=\"$HOME/Desktop\"\n");

        var root = LocalWorkspaceFolders.DefaultRootDirectory(scratch.AppData, scratch.Home);

        Assert.Equal(Path.Combine(localised, "9to1"), root);
        Assert.False(Directory.Exists(Path.Combine(scratch.Documents, "9to1")));
    }

    [Fact]
    public void AMovedWorkspaceRootIsFoundByItsMetadataRatherThanItsNameOrOldPath()
    {
        using var scratch = new Scratch();
        scratch.Create();
        var original = Path.Combine(scratch.Documents, "9to1");
        Write(Path.Combine(original, ".9to1-files", "drive.json"), """{"state":{"storeId":"22222222-2222-2222-2222-222222222222"}}""");
        Write(Path.Combine(original, "Boards", "board.9to1board"), "board");
        // The owner moves it and renames it: identity travels with the metadata folder.
        var moved = Path.Combine(scratch.Home, "stuff", "my 9to1");
        Directory.CreateDirectory(Path.Combine(scratch.Home, "stuff"));
        Directory.Move(original, moved);

        var found = LocalWorkspaceFolders.DiscoverWorkspaceRoots(
            LocalWorkspaceFolders.CandidateScanParents(original, Path.Combine(scratch.Documents, "9to1"), scratch.Home));

        Assert.Equal([moved], found);
        Assert.Equal("board", File.ReadAllText(Path.Combine(moved, "Boards", "board.9to1board")));
    }

    [Fact]
    public void TwoWorkspaceRootsAreBothReturnedSoTheOwnerChoosesInsteadOfTheAppGuessing()
    {
        using var scratch = new Scratch();
        scratch.Create();
        foreach (var store in new[] { Path.Combine(scratch.Home, "first", "9to1"),
            Path.Combine(scratch.Home, "second", "9to1") })
            Write(Path.Combine(store, ".9to1-files", "drive.json"), """{"state":{}}""");

        var found = LocalWorkspaceFolders.DiscoverWorkspaceRoots([Path.Combine(scratch.Home, "first"),
            Path.Combine(scratch.Home, "second")]);

        Assert.Equal(2, found.Count);
    }

    [Fact]
    public void APlainFolderWithoutWorkspaceMetadataIsNotMistakenForTheWorkspaceRoot()
    {
        using var scratch = new Scratch();
        scratch.Create();
        Write(Path.Combine(scratch.Home, "Documents", "9to1", "notes.txt"), "just a folder the user made");

        var found = LocalWorkspaceFolders.DiscoverWorkspaceRoots([scratch.Documents]);

        Assert.Empty(found);
        Assert.False(LocalWorkspaceFolders.IsWorkspaceRoot(Path.Combine(scratch.Documents, "9to1")));
    }

    [Fact]
    public void AWorkspaceUnderASymlinkedChainIsRefusedRatherThanFollowedOntoAnotherVolume()
    {
        if (OperatingSystem.IsWindows()) return;
        using var scratch = new Scratch();
        scratch.Create();
        var real = Path.Combine(scratch.Home, "real-volume");
        Directory.CreateDirectory(real);
        var link = Path.Combine(scratch.Documents, "9to1");
        Directory.CreateSymbolicLink(link, real);

        var error = Assert.Throws<UnauthorizedAccessException>(() => LocalWorkspaceFolders.Provision(link));
        Assert.Contains("cannot pass through the link", error.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(real));
    }

    [Fact]
    public void ProvisioningTwiceIsIdempotentAndAddsNothingNew()
    {
        using var scratch = new Scratch();
        scratch.Create();
        var root = LocalWorkspaceFolders.DefaultRootDirectory(scratch.AppData, scratch.Home);

        var first = LocalWorkspaceFolders.Provision(root);
        var tree = Tree(root);
        var second = LocalWorkspaceFolders.Provision(root);

        Assert.NotEmpty(first.CreatedDirectories);
        Assert.Empty(second.CreatedDirectories);
        Assert.False(second.RootWasCreated);
        Assert.Equal(tree, Tree(root));
    }
}
