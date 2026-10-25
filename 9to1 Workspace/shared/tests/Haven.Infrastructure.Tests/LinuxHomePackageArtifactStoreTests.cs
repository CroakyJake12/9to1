using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Apps;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Real Linux filesystem/ELF fixtures exercise the production physical adapter.
// This is not a signed 9to1 package, authenticated Root session or app acceptance.
[SupportedOSPlatform("linux")]
public sealed class LinuxHomePackageArtifactStoreTests
{
    [LinuxHomePackageFact]
    public void Initial_publication_runs_real_native_payload_and_consumes_the_exact_stage()
    {
        using var rig = new Rig();
        using var store = new LinuxHomePackageArtifactStore(rig.Path);
        var files = rig.Stage("staged", "/usr/bin/true");
        using var stage = store.ValidateStage("staged", files, "bin/app");
        Assert.Null(store.ObserveActive());
        var result = store.Publish(stage, null);
        Assert.Null(result.RetainedPreviousPath);
        Assert.False(Directory.Exists(stage.StagePath));
        Assert.Equal(files[0].Sha256, Digest(File.ReadAllBytes(System.IO.Path.Combine(result.ActivePath, "bin/app"))));
        using var process = Process.Start(new ProcessStartInfo(System.IO.Path.Combine(result.ActivePath, "bin/app")) { UseShellExecute = false })!;
        Assert.True(process.WaitForExit(5000));
        Assert.Equal(0, process.ExitCode);
        Assert.Throws<UnauthorizedAccessException>(() => store.Publish(stage, store.ObserveActive()));
    }

    [LinuxHomePackageFact]
    public void Atomic_update_retains_previous_runnable_directory_at_the_original_stage_name()
    {
        using var rig = new Rig();
        using var store = new LinuxHomePackageArtifactStore(rig.Path);
        using (var initial = store.ValidateStage("initial", rig.Stage("initial", "/usr/bin/true"), "bin/app"))
            store.Publish(initial, null);
        var previous = store.ObserveActive()!;
        var previousBytes = File.ReadAllBytes(System.IO.Path.Combine(previous.Path, "bin/app"));
        using var stage = store.ValidateStage("update", rig.Stage("update", "/usr/bin/false"), "bin/app");
        var result = store.Publish(stage, previous);
        Assert.Equal(stage.StagePath, result.RetainedPreviousPath);
        Assert.Equal(previousBytes, File.ReadAllBytes(System.IO.Path.Combine(result.RetainedPreviousPath!, "bin/app")));
        Assert.Equal(File.ReadAllBytes("/usr/bin/false"), File.ReadAllBytes(System.IO.Path.Combine(result.ActivePath, "bin/app")));
        // A new root operation can revalidate the retained directory for rollback.
        using var rollback = store.ValidateStage("update", Rig.Files(stage.StagePath), "bin/app");
        store.Publish(rollback, store.ObserveActive());
        Assert.Equal(previousBytes, File.ReadAllBytes(System.IO.Path.Combine(result.ActivePath, "bin/app")));
    }

    [LinuxHomePackageFact]
    public void Corrupt_update_preserves_previous_and_keeps_staged_bytes_for_recovery()
    {
        using var rig = new Rig();
        using var store = new LinuxHomePackageArtifactStore(rig.Path);
        using (var initial = store.ValidateStage("initial", rig.Stage("initial", "/usr/bin/true"), "bin/app"))
            store.Publish(initial, null);
        var previous = File.ReadAllBytes(System.IO.Path.Combine(rig.Path, "active/bin/app"));
        using var update = store.ValidateStage("update", rig.Stage("update", "/usr/bin/false"), "bin/app");
        var updatePath = System.IO.Path.Combine(update.StagePath, "bin/app");
        var corrupt = File.ReadAllBytes(updatePath); corrupt[^1] ^= 1; File.WriteAllBytes(updatePath, corrupt);
        Assert.Throws<InvalidDataException>(() => store.Publish(update, store.ObserveActive()));
        Assert.Equal(previous, File.ReadAllBytes(System.IO.Path.Combine(rig.Path, "active/bin/app")));
        Assert.Equal(corrupt, File.ReadAllBytes(updatePath));
    }

    [LinuxHomePackageFact]
    public void Added_unlisted_file_and_missing_file_never_become_an_activated_cohort()
    {
        using var rig = new Rig();
        using var store = new LinuxHomePackageArtifactStore(rig.Path);
        var files = rig.Stage("stage", "/usr/bin/true");
        File.WriteAllText(System.IO.Path.Combine(rig.Path, "stage/bin/injected"), "unlisted");
        Assert.Throws<InvalidDataException>(() => store.ValidateStage("stage", files, "bin/app"));
        File.Delete(System.IO.Path.Combine(rig.Path, "stage/bin/injected"));
        File.Delete(System.IO.Path.Combine(rig.Path, "stage/bin/app"));
        Assert.Throws<InvalidDataException>(() => store.ValidateStage("stage", files, "bin/app"));
        Assert.False(Directory.Exists(System.IO.Path.Combine(rig.Path, "active")));
    }

    [LinuxHomePackageFact]
    public void Stage_or_active_symlinks_are_refused_without_replacing_foreign_targets()
    {
        using var rig = new Rig();
        using var foreign = new Rig();
        var files = foreign.Stage("foreign", "/usr/bin/true");
        var target = System.IO.Path.Combine(foreign.Path, "foreign");
        Directory.CreateSymbolicLink(System.IO.Path.Combine(rig.Path, "linked"), target);
        using var store = new LinuxHomePackageArtifactStore(rig.Path);
        Assert.Throws<UnauthorizedAccessException>(() => store.ValidateStage("linked", files, "bin/app"));
        using var stage = store.ValidateStage("stage", rig.Stage("stage", "/usr/bin/false"), "bin/app");
        Directory.CreateSymbolicLink(System.IO.Path.Combine(rig.Path, "active"), target);
        Assert.Throws<UnauthorizedAccessException>(() => store.Publish(stage, null));
        Assert.Equal(File.ReadAllBytes("/usr/bin/true"), File.ReadAllBytes(System.IO.Path.Combine(target, "bin/app")));
        Assert.NotNull(new DirectoryInfo(System.IO.Path.Combine(rig.Path, "active")).LinkTarget);
    }

    [LinuxHomePackageFact]
    public void Changed_active_observation_refuses_a_second_owner_without_overwriting_newer_package()
    {
        using var rig = new Rig();
        using var first = new LinuxHomePackageArtifactStore(rig.Path);
        using var second = new LinuxHomePackageArtifactStore(rig.Path);
        using (var initial = first.ValidateStage("initial", rig.Stage("initial", "/usr/bin/true"), "bin/app"))
            first.Publish(initial, null);
        var stale = first.ObserveActive();
        using var waiting = first.ValidateStage("waiting", rig.Stage("waiting", "/usr/bin/true"), "bin/app");
        using var newer = second.ValidateStage("newer", rig.Stage("newer", "/usr/bin/false"), "bin/app");
        second.Publish(newer, second.ObserveActive());
        Assert.Throws<IOException>(() => first.Publish(waiting, stale));
        Assert.Equal(File.ReadAllBytes("/usr/bin/false"), File.ReadAllBytes(System.IO.Path.Combine(rig.Path, "active/bin/app")));
        Assert.True(Directory.Exists(waiting.StagePath));
    }

    [LinuxHomePackageFact]
    public void Actual_external_publication_lock_refuses_concurrent_validation_without_mutation()
    {
        using var rig = new Rig();
        var files = rig.Stage("stage", "/usr/bin/true");
        using var store = new LinuxHomePackageArtifactStore(rig.Path);
        using var held = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(rig.Path, mutable: true);
        Assert.Equal(0, Flock(held, 2 | 4));
        try { Assert.Throws<System.ComponentModel.Win32Exception>(() => store.ValidateStage("stage", files, "bin/app")); }
        finally { Assert.Equal(0, Flock(held, 8)); }
        Assert.True(Directory.Exists(System.IO.Path.Combine(rig.Path, "stage")));
        Assert.Null(store.ObserveActive());
    }

    [LinuxHomePackageFact]
    public void Cancellation_before_publication_keeps_exact_stage_and_previous()
    {
        using var rig = new Rig();
        using var store = new LinuxHomePackageArtifactStore(rig.Path);
        using var stage = store.ValidateStage("stage", rig.Stage("stage", "/usr/bin/true"), "bin/app");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.Publish(stage, null, canceled.Token));
        Assert.Null(store.ObserveActive());
        Assert.True(Directory.Exists(stage.StagePath));
    }

    [LinuxHomePackageFact]
    public void Foreign_stage_nonprivate_root_and_changed_stage_namespace_are_not_adopted()
    {
        using var rig = new Rig();
        using var store = new LinuxHomePackageArtifactStore(rig.Path);
        using var other = new LinuxHomePackageArtifactStore(rig.Path);
        using var stage = store.ValidateStage("stage", rig.Stage("stage", "/usr/bin/true"), "bin/app");
        Assert.Throws<UnauthorizedAccessException>(() => other.Publish(stage, null));
        Directory.Move(stage.StagePath, stage.StagePath + "-moved");
        rig.Stage("stage", "/usr/bin/false");
        Assert.Throws<UnauthorizedAccessException>(() => store.Publish(stage, null));
        Assert.Null(store.ObserveActive());
        File.SetUnixFileMode(rig.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);
        Assert.Throws<UnauthorizedAccessException>(() => new LinuxHomePackageArtifactStore(rig.Path));
    }

    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class Rig : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("home-linux-package-").FullName;
        public Rig() => File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        public IReadOnlyList<HomePackageActivatedFile> Stage(string leaf, string executable)
        {
            var directory = System.IO.Path.Combine(Path, leaf);
            Directory.CreateDirectory(System.IO.Path.Combine(directory, "bin"));
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(System.IO.Path.Combine(directory, "bin"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var app = System.IO.Path.Combine(directory, "bin/app");
            File.Copy(executable, app);
            File.SetUnixFileMode(app, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return Files(directory);
        }
        public static IReadOnlyList<HomePackageActivatedFile> Files(string directory) =>
            Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(path =>
                new HomePackageActivatedFile(System.IO.Path.GetRelativePath(directory, path), new FileInfo(path).Length,
                    Digest(File.ReadAllBytes(path)))).ToArray();
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(SafeFileHandle descriptor, int operation);
}

public sealed class LinuxHomePackageFactAttribute : FactAttribute
{
    public LinuxHomePackageFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "Requires actual Linux openat2/statx/flock/renameat2 and ELF execution.";
    }
}
