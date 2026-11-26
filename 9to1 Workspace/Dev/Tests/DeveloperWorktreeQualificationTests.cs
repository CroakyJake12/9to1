using System.Diagnostics;
using HavenOS.Apps.Dev.Autonomous;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

public sealed class DeveloperWorktreeQualificationTests
{
    [Fact]
    public async Task New_child_disables_sparse_checkout_only_for_child_and_materializes_full_base()
    {
        using var fixture = new GitFixture();
        await fixture.CreateSparseParentAsync();
        var parentSparseBefore = await fixture.GitAsync(fixture.Repository, "config", "--worktree", "--bool", "core.sparseCheckout");
        Assert.False(File.Exists(Path.Combine(fixture.Repository, "hidden", "required.txt")));

        await DeveloperAutonomousWorkspaceTools.PrepareIsolatedWorktreeAsync(
            fixture.Repository, fixture.Child, fixture.BaseRevision);

        Assert.True(File.Exists(Path.Combine(fixture.Child, "hidden", "required.txt")));
        Assert.Equal(fixture.BaseRevision, await fixture.GitAsync(fixture.Child, "rev-parse", "HEAD"));
        Assert.Equal("false", await fixture.GitAsync(fixture.Child, "config", "--worktree", "--bool", "core.sparseCheckout"));
        Assert.Equal(parentSparseBefore, await fixture.GitAsync(fixture.Repository, "config", "--worktree", "--bool", "core.sparseCheckout"));
        Assert.False(File.Exists(Path.Combine(fixture.Repository, "hidden", "required.txt")));
    }

    [Fact]
    public async Task Existing_worktree_from_another_repository_is_refused_without_touching_its_files()
    {
        using var fixture = new GitFixture();
        await fixture.CreateRegularParentAsync();
        var other = Path.Combine(fixture.Root, "other-repo");
        var otherBase = await GitFixture.CreateRepositoryAsync(other);
        var worktree = Path.Combine(fixture.Root, "foreign-child");
        await fixture.GitAsync(fixture.Repository, "worktree", "add", "--detach", worktree, fixture.BaseRevision);
        var dirtyFile = Path.Combine(worktree, "src", "value.txt");
        await File.WriteAllTextAsync(dirtyFile, "owned dirty data\n");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeveloperAutonomousWorkspaceTools.PrepareIsolatedWorktreeAsync(other, worktree, otherBase));

        Assert.Equal("owned dirty data\n", await File.ReadAllTextAsync(dirtyFile));
        Assert.Equal(fixture.BaseRevision, await fixture.GitAsync(worktree, "rev-parse", "HEAD"));
    }

    [Fact]
    public async Task Existing_worktree_at_wrong_base_is_refused_and_dirty_owned_work_is_preserved()
    {
        using var fixture = new GitFixture();
        await fixture.CreateRegularParentAsync();
        var worktree = Path.Combine(fixture.Root, "wrong-base-child");
        await fixture.GitAsync(fixture.Repository, "worktree", "add", "--detach", worktree, fixture.BaseRevision);
        await fixture.GitAsync(fixture.Repository, "config", "user.name", "Qualification");
        await fixture.GitAsync(fixture.Repository, "config", "user.email", "qualification@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(fixture.Repository, "src", "next.txt"), "next\n");
        await fixture.GitAsync(fixture.Repository, "add", "src/next.txt");
        await fixture.GitAsync(fixture.Repository, "commit", "-qm", "second base");
        var newerBase = await fixture.GitAsync(fixture.Repository, "rev-parse", "HEAD");
        await File.WriteAllTextAsync(Path.Combine(worktree, "src", "value.txt"), "preserve me\n");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DeveloperAutonomousWorkspaceTools.PrepareIsolatedWorktreeAsync(fixture.Repository, worktree, newerBase));

        Assert.Equal(fixture.BaseRevision, await fixture.GitAsync(worktree, "rev-parse", "HEAD"));
        Assert.Equal("preserve me\n", await File.ReadAllTextAsync(Path.Combine(worktree, "src", "value.txt")));
    }

    private sealed class GitFixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("worktree-qualification-").FullName;
        public string Repository => Path.Combine(Root, "repo");
        public string Child => Path.Combine(Root, "full-child");
        public string BaseRevision { get; private set; } = "";

        public async Task CreateSparseParentAsync()
        {
            await CreateRegularParentAsync();
            await GitAsync(Repository, "config", "extensions.worktreeConfig", "true");
            await GitAsync(Repository, "sparse-checkout", "init", "--cone");
            await GitAsync(Repository, "sparse-checkout", "set", "visible");
        }

        public async Task CreateRegularParentAsync()
        {
            Directory.CreateDirectory(Path.Combine(Repository, "visible"));
            Directory.CreateDirectory(Path.Combine(Repository, "hidden"));
            Directory.CreateDirectory(Path.Combine(Repository, "src"));
            await File.WriteAllTextAsync(Path.Combine(Repository, "visible", "shown.txt"), "shown\n");
            await File.WriteAllTextAsync(Path.Combine(Repository, "hidden", "required.txt"), "required\n");
            await File.WriteAllTextAsync(Path.Combine(Repository, "src", "value.txt"), "base\n");
            await GitAsync(Repository, "init", "-q");
            await GitAsync(Repository, "config", "user.name", "Qualification");
            await GitAsync(Repository, "config", "user.email", "qualification@example.invalid");
            await GitAsync(Repository, "add", ".");
            await GitAsync(Repository, "commit", "-qm", "base");
            BaseRevision = await GitAsync(Repository, "rev-parse", "HEAD");
        }

        public Task<string> GitAsync(string cwd, params string[] args) => Task.FromResult(RunGit(cwd, args));

        public static async Task<string> CreateRepositoryAsync(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, "src"));
            await File.WriteAllTextAsync(Path.Combine(path, "src", "other.txt"), "other\n");
            RunGit(path, "init", "-q");
            RunGit(path, "config", "user.name", "Qualification");
            RunGit(path, "config", "user.email", "qualification@example.invalid");
            RunGit(path, "add", ".");
            RunGit(path, "commit", "-qm", "other repo");
            return RunGit(path, "rev-parse", "HEAD");
        }

        public static string RunGit(string cwd, params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {stderr}");
            return stdout.Trim();
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
