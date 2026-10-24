using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Infrastructure.Native.Linux;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Real Linux descriptor/filesystem boundary, actual Browser approval decorator,
// store and transport. Synthetic native EOF/destination issuers exercise these
// contracts; they do not manufacture a Home grant, canonical Files registration,
// an installed-Chromium run, ARM64 runtime success or external-writer CAS.
public sealed class BrowserOriginalLinuxDownloadPhysicalOwnerTests
{
    [Fact]
    public async Task Explicit_single_child_configuration_alone_creates_nothing_and_cannot_escape_its_parent()
    {
        using var folder = new PrivateDirectory();
        var root = Path.Combine(folder.Path, "Haven");
        await using var owner = new BrowserOriginalNativeDownloadPhysicalOwner(root, folder.Path);
        Assert.False(Directory.Exists(root));
        Assert.Throws<ArgumentException>(() => new BrowserOriginalNativeDownloadPhysicalOwner(
            Path.Combine(folder.Path, "other", "Haven"), folder.Path));
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "other")));
    }

    [LinuxDownloadFact]
    public async Task Fresh_configured_private_root_is_created_and_real_approved_EOF_uses_the_same_pipeline()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This native Linux test requires Linux; other platforms must use the existing LinuxDownloadFact skip.");
        await using var rig = new Rig(createDownloads: false, allowProvision: true);
        Assert.False(Directory.Exists(rig.Downloads));
        var content = await rig.ApproveAsync();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(rig.Downloads));
        Assert.Equal(rig.Body, await File.ReadAllBytesAsync(content.OriginalRecord.StoredPath));
        Assert.True(rig.Owner.IsIssuedOriginalContent(content));
        Assert.False(File.Exists(rig.Execution!.Plan.PartialPath));
    }

    [LinuxDownloadFact]
    public async Task Home_rejection_leaves_only_the_empty_configured_private_setup_root()
    {
        await using var rig = new Rig(createDownloads: false, allowProvision: true);
        var plan = await rig.Transport.PrepareNativeDownloadAsync(Guid.NewGuid(), new("https://example.test/attachment"),
            null, "report.bin", null, CancellationToken.None);
        Assert.True(Directory.Exists(rig.Downloads));
        Assert.Empty(Directory.EnumerateFileSystemEntries(rig.Downloads));
        var execution = new OriginalFileExecution(rig.Transport, plan, rig.Body, null, rig.Retain);
        var pending = await rig.Service.RequestNativeDownloadAsync(new(plan.ActionId, new("https://example.test/attachment"),
            plan.FileName, false), execution, CancellationToken.None);
        var rejected = await rig.Service.RejectAsync(pending.Id, CancellationToken.None);
        Assert.Equal(BrowserActionState.Rejected, rejected.State);
        Assert.Empty(Directory.EnumerateFileSystemEntries(rig.Downloads));
        Assert.Empty(await rig.Store.GetDownloadsAsync(10, CancellationToken.None));
        Assert.Null(execution.Content);
    }

    [LinuxDownloadFact]
    public async Task An_owner_without_explicit_setup_configuration_does_not_create_a_missing_root()
    {
        await using var rig = new Rig(createDownloads: false);
        Assert.True(ContainsKind<FileNotFoundException>(await Failure(rig.Transport.PrepareNativeDownloadAsync(Guid.NewGuid(),
            new("https://example.test/attachment"), null, "report.bin", null, CancellationToken.None))));
        Assert.False(Directory.Exists(rig.Downloads));
        rig.ExpectOwnerFailure = true;
    }

    [LinuxDownloadFact]
    public async Task Another_real_transport_plan_cannot_create_the_configured_missing_root()
    {
        await using var foreign = new Rig();
        await using var target = new Rig(createDownloads: false, allowProvision: true);
        var foreignPlan = await foreign.Transport.PrepareNativeDownloadAsync(Guid.NewGuid(), new("https://example.test/file"),
            null, "foreign.bin", null, CancellationToken.None);
        Assert.True(ContainsKind<UnauthorizedAccessException>(await Failure(target.Owner.PrepareOriginalTransportPlanWithinSourceAsync(
            foreign.Transport.GetOriginalTransportPlan(foreignPlan), Run, target.Retain, CancellationToken.None))));
        Assert.False(Directory.Exists(target.Downloads));
        target.ExpectOwnerFailure = true;
    }

    [LinuxDownloadFact]
    public async Task Invalid_issued_description_is_rejected_before_empty_root_setup()
    {
        using var folder = new PrivateDirectory();
        var root = Path.Combine(folder.Path, "Haven");
        var owner = new BrowserOriginalNativeDownloadPhysicalOwner(root, folder.Path);
        var source = new FaultySetupTransport(root, invalidDescription: true);
        owner.BindOriginalTransportSource(source);
        Assert.True(ContainsKind<UnauthorizedAccessException>(await Failure(owner.PrepareOriginalTransportPlanWithinSourceAsync(
            source.Plan, Run, _ => { }, CancellationToken.None))));
        Assert.False(Directory.Exists(root));
        await Assert.ThrowsAnyAsync<Exception>(() => owner.CloseAndDrainAsync());
    }

    [LinuxDownloadFact]
    public async Task Cancellation_during_description_validation_creates_no_root()
    {
        using var folder = new PrivateDirectory();
        using var cancellation = new CancellationTokenSource();
        var root = Path.Combine(folder.Path, "Haven");
        var owner = new BrowserOriginalNativeDownloadPhysicalOwner(root, folder.Path);
        var source = new FaultySetupTransport(root, duringDescription: cancellation.Cancel);
        owner.BindOriginalTransportSource(source);
        Assert.True(ContainsKind<OperationCanceledException>(await Failure(owner.PrepareOriginalTransportPlanWithinSourceAsync(
            source.Plan, Run, _ => { }, cancellation.Token))));
        Assert.False(Directory.Exists(root));
        await Assert.ThrowsAnyAsync<Exception>(() => owner.CloseAndDrainAsync());
    }

    [LinuxDownloadFact]
    public async Task Provisioning_refuses_insecure_existing_root_and_parent_without_changing_permissions()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This native Linux test requires Linux; other platforms must use the existing LinuxDownloadFact skip.");
        await using (var rig = new Rig(allowProvision: true))
        {
            var unsafeMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead;
            File.SetUnixFileMode(rig.Downloads, unsafeMode);
            await File.WriteAllTextAsync(Path.Combine(rig.Downloads, "existing.txt"), "retain original");
            Assert.True(ContainsKind<UnauthorizedAccessException>(await Failure(rig.Transport.PrepareNativeDownloadAsync(Guid.NewGuid(),
                new("https://example.test/file"), null, "new.bin", null, CancellationToken.None))));
            Assert.Equal(unsafeMode, File.GetUnixFileMode(rig.Downloads));
            Assert.Equal("retain original", await File.ReadAllTextAsync(Path.Combine(rig.Downloads, "existing.txt")));
            rig.ExpectOwnerFailure = true;
        }
        await using (var rig = new Rig(createDownloads: false, allowProvision: true))
        {
            var unsafeMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute;
            File.SetUnixFileMode(rig.Root, unsafeMode);
            Assert.True(ContainsKind<UnauthorizedAccessException>(await Failure(rig.Transport.PrepareNativeDownloadAsync(Guid.NewGuid(),
                new("https://example.test/file"), null, "new.bin", null, CancellationToken.None))));
            Assert.False(Directory.Exists(rig.Downloads));
            Assert.Equal(unsafeMode, File.GetUnixFileMode(rig.Root));
            rig.ExpectOwnerFailure = true;
        }
    }

    [LinuxDownloadFact]
    public async Task Provisioning_never_follows_a_symlink_parent_or_creates_missing_ancestors()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This native Linux test requires Linux; other platforms must use the existing LinuxDownloadFact skip.");
        using var folder = new PrivateDirectory();
        var actualParent = Path.Combine(folder.Path, "actual");
        Directory.CreateDirectory(actualParent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var alias = Path.Combine(folder.Path, "alias"); Directory.CreateSymbolicLink(alias, actualParent);
        var root = Path.Combine(alias, "Haven");
        var owner = new BrowserOriginalNativeDownloadPhysicalOwner(root, alias);
        var transport = new BrowserDownloadTransport(new FixturePolicy(), root, owner);
        Assert.True(ContainsKind<UnauthorizedAccessException>(await Failure(transport.PrepareNativeDownloadAsync(Guid.NewGuid(),
            new("https://example.test/file"), null, "new.bin", null, CancellationToken.None))));
        Assert.False(Directory.Exists(Path.Combine(actualParent, "Haven")));
        Assert.Equal(actualParent, new DirectoryInfo(alias).LinkTarget);
        await Assert.ThrowsAnyAsync<Exception>(() => owner.CloseAndDrainAsync());
        var linkedRoot = Path.Combine(folder.Path, "linked-root"); Directory.CreateSymbolicLink(linkedRoot, actualParent);
        owner = new(linkedRoot, folder.Path); transport = new(new FixturePolicy(), linkedRoot, owner);
        Assert.True(ContainsKind<UnauthorizedAccessException>(await Failure(transport.PrepareNativeDownloadAsync(Guid.NewGuid(),
            new("https://example.test/file"), null, "new.bin", null, CancellationToken.None))));
        Assert.Equal(actualParent, new DirectoryInfo(linkedRoot).LinkTarget);
        Assert.Empty(Directory.EnumerateFileSystemEntries(actualParent));
        await Assert.ThrowsAnyAsync<Exception>(() => owner.CloseAndDrainAsync());
        var absentParent = Path.Combine(folder.Path, "not-created");
        root = Path.Combine(absentParent, "Haven");
        owner = new(root, absentParent); transport = new(new FixturePolicy(), root, owner);
        Assert.True(ContainsKind<FileNotFoundException>(await Failure(transport.PrepareNativeDownloadAsync(Guid.NewGuid(),
            new("https://example.test/file"), null, "new.bin", null, CancellationToken.None))));
        Assert.False(Directory.Exists(absentParent));
        await Assert.ThrowsAnyAsync<Exception>(() => owner.CloseAndDrainAsync());
    }

    [LinuxDownloadFact]
    public void Create_only_EEXIST_and_changed_effective_principal_never_adopt_or_mutate_a_competing_root()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This native Linux test requires Linux; other platforms must use the existing LinuxDownloadFact skip.");
        using var folder = new PrivateDirectory();
        var root = Path.Combine(folder.Path, "Haven");
        Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var held = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(root, mutable: false);
        var original = LinuxOriginalDownloadPaths.ReadIdentity(held);
        using var parent = LinuxOriginalDownloadPaths.OpenAbsoluteDirectory(folder.Path, mutable: true);
        var uid = LinuxOriginalDownloadPaths.CurrentUid();
        Assert.Throws<LinuxOriginalDownloadCollisionException>(() =>
            LinuxOriginalDownloadPaths.CreatePrivateChildRoot(parent, folder.Path, "Haven", uid));
        Assert.True(original.SameReadVersion(LinuxOriginalDownloadPaths.ReadIdentity(held)));
        var changedUid = uid == uint.MaxValue ? 0 : uid + 1;
        Assert.Throws<UnauthorizedAccessException>(() => LinuxOriginalDownloadPaths.RetainOrCreatePrivateRoot(
            Path.Combine(folder.Path, "missing"), folder.Path, changedUid));
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "missing")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    [Fact]
    public void Supported_ABIs_have_explicit_directory_flags_and_other_ABIs_are_refused()
    {
        Assert.Equal(0x10000UL, LinuxOriginalDownloadPaths.DirectoryFlagFor(Architecture.X64));
        Assert.Equal(0x4000UL, LinuxOriginalDownloadPaths.DirectoryFlagFor(Architecture.Arm64));
        Assert.Throws<PlatformNotSupportedException>(() => LinuxOriginalDownloadPaths.DirectoryFlagFor(Architecture.X86));
    }

    [Fact]
    public void Statx_parser_requires_every_consumed_field_and_preserves_uid_gid_mount_and_full_read_version()
    {
        var bytes = StatxFixture();
        var actual = LinuxOriginalDownloadPaths.ParseIdentity(bytes);
        Assert.Equal(77UL, actual.Inode); Assert.Equal(9UL, actual.MountId);
        Assert.Equal(1000U, actual.Uid); Assert.Equal(1001U, actual.Gid);
        Assert.True(actual.IsRegular); Assert.Equal(1U, actual.Links); Assert.Equal(5UL, actual.Size);
        Assert.False(actual.SameReadVersion(actual with { ChangedNanos = actual.ChangedNanos + 1 }));
        Assert.False(actual.SameReadVersion(actual with { Uid = actual.Uid + 1 }));
        BitConverter.GetBytes(0x17ffU & ~8U).CopyTo(bytes, 0);
        Assert.Throws<PlatformNotSupportedException>(() => LinuxOriginalDownloadPaths.ParseIdentity(bytes));
    }

    [LinuxDownloadFact]
    public async Task Actual_approved_EOF_is_promoted_create_only_and_held_content_copies_exact_bytes()
    {
        await using var rig = new Rig();
        var content = await rig.ApproveAsync();
        Assert.True(rig.Owner.IsIssuedOriginalContent(content));
        Assert.Same(content.OriginalPlan, rig.Transport.GetOriginalTransportPlan(rig.Execution!.Plan));
        Assert.Equal(rig.Body, await File.ReadAllBytesAsync(content.OriginalRecord.StoredPath));
        Assert.False(File.Exists(rig.Execution.Plan.PartialPath));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(rig.Body)).ToLowerInvariant(), content.OriginalRecord.Sha256);
        await rig.Owner.WaitOriginalApprovedContentWithinSourceAsync(content, Run, rig.Retain, CancellationToken.None);
        using var destination = new MemoryStream();
        await rig.Owner.CopyOriginalContentWithinSourceAsync(content, destination, Run, rig.Retain, CancellationToken.None);
        Assert.Equal(rig.Body, destination.ToArray());
        var sameClose = content.CloseAndDrainAsync();
        Assert.Same(sameClose, content.CloseAndDrainAsync()); await sameClose;
    }

    [LinuxDownloadFact]
    public async Task Actual_content_and_destination_pin_create_private_Files_bytes_without_replacing_names()
    {
        await using var rig = new Rig();
        var content = await rig.ApproveAsync();
        var issuer = rig.IssueFilesDestination();
        await using var pin = await rig.Owner.PinOriginalFilesDestinationWithinSourceAsync(issuer.Destination, Run, rig.Retain, CancellationToken.None);
        Assert.True(rig.Owner.IsIssuedOriginalFilesPin(pin));
        await rig.Owner.CopyOriginalDownloadToFilesWithinSourceAsync(pin, content, Run, rig.Retain, CancellationToken.None);
        Assert.Equal(rig.Body, await File.ReadAllBytesAsync(issuer.FinalPath));
        if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(issuer.FinalPath));
        Assert.True(issuer.Demands >= 4);
        Assert.Empty(Directory.EnumerateFiles(issuer.Directory, ".9to1-browser-stage-*.tmp"));
        // This physical fixture supplies no canonical Files metadata/Home receipt.
    }

    [LinuxDownloadFact]
    public async Task Existing_Browser_final_name_is_conserved_and_a_distinct_create_only_name_is_issued()
    {
        await using var rig = new Rig();
        var content = await rig.ApproveAsync(beforeFinalize: execution => File.WriteAllText(execution.Plan.FinalPath, "existing winner"));
        Assert.Equal("existing winner", await File.ReadAllTextAsync(rig.Execution!.Plan.FinalPath));
        Assert.NotEqual(rig.Execution.Plan.FinalPath, content.OriginalRecord.StoredPath);
        Assert.Equal(rig.Body, await File.ReadAllBytesAsync(content.OriginalRecord.StoredPath));
    }

    [LinuxDownloadFact]
    public async Task Configured_symlink_or_nonprivate_root_is_refused_without_changing_its_metadata()
    {
        using var directory = new PrivateDirectory();
        var original = Path.Combine(directory.Path, "actual"); Directory.CreateDirectory(original);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(original, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var alias = Path.Combine(directory.Path, "alias"); Directory.CreateSymbolicLink(alias, original);
        var symlinkOwner = new BrowserOriginalNativeDownloadPhysicalOwner(alias);
        var symlinkTransport = new BrowserDownloadTransport(new FixturePolicy(), alias, symlinkOwner);
        await Assert.ThrowsAnyAsync<Exception>(() => symlinkTransport.PrepareNativeDownloadAsync(Guid.NewGuid(), new("https://example.test/file"), null, "file.bin", null, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => symlinkOwner.DisposeAsync().AsTask());
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(original, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);
        var insecureOwner = new BrowserOriginalNativeDownloadPhysicalOwner(original);
        var insecureTransport = new BrowserDownloadTransport(new FixturePolicy(), original, insecureOwner);
        await Assert.ThrowsAnyAsync<Exception>(() => insecureTransport.PrepareNativeDownloadAsync(Guid.NewGuid(), new("https://example.test/file"), null, "file.bin", null, CancellationToken.None));
        await Assert.ThrowsAnyAsync<Exception>(() => insecureOwner.DisposeAsync().AsTask());
        if (OperatingSystem.IsLinux()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead, File.GetUnixFileMode(original));
    }

    [LinuxDownloadFact]
    public async Task Partial_symlink_is_not_opened_and_the_foreign_target_is_conserved()
    {
        await using var rig = new Rig();
        var foreign = Path.Combine(rig.Root, "foreign"); await File.WriteAllTextAsync(foreign, "foreign original");
        var result = await rig.ExecuteApprovalAsync(execution =>
        {
            File.Delete(execution.Plan.PartialPath);
            File.CreateSymbolicLink(execution.Plan.PartialPath, foreign);
        });
        Assert.Equal(BrowserActionState.Failed, result.State);
        Assert.Null(result.Download); Assert.Equal("foreign original", await File.ReadAllTextAsync(foreign));
        Assert.False(File.Exists(rig.Execution!.Plan.FinalPath));
        rig.ExpectOwnerFailure = rig.ExpectApprovalFailure = true;
    }

    [LinuxDownloadFact]
    public async Task Multiply_linked_partial_is_refused_before_promotion()
    {
        await using var rig = new Rig();
        var alias = Path.Combine(rig.Root, "extra-alias");
        var result = await rig.ExecuteApprovalAsync(execution =>
        {
            Assert.Equal(0, Link(execution.Plan.PartialPath, alias));
        });
        Assert.Equal(BrowserActionState.Failed, result.State);
        Assert.Equal(rig.Body, await File.ReadAllBytesAsync(alias));
        Assert.False(File.Exists(rig.Execution!.Plan.FinalPath));
        rig.ExpectOwnerFailure = rig.ExpectApprovalFailure = true;
    }

    [LinuxDownloadFact]
    public async Task Changed_held_content_with_restored_mtime_is_refused_before_copying_bytes()
    {
        await using var rig = new Rig();
        var content = await rig.ApproveAsync(); var path = content.OriginalRecord.StoredPath;
        var oldModified = File.GetLastWriteTimeUtc(path);
        var changed = rig.Body.Select(value => (byte)(value ^ 0xff)).ToArray();
        await File.WriteAllBytesAsync(path, changed); File.SetLastWriteTimeUtc(path, oldModified);
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.CopyOriginalContentWithinSourceAsync(content, output, Run, rig.Retain, CancellationToken.None));
        Assert.Empty(output.ToArray()); Assert.Equal(changed, await File.ReadAllBytesAsync(path));
        rig.ExpectOwnerFailure = true;
    }

    [LinuxDownloadFact]
    public async Task Replaced_registered_root_is_refused_while_the_original_inode_remains_held()
    {
        await using var rig = new Rig(); var content = await rig.ApproveAsync();
        Directory.Move(rig.Downloads, Path.Combine(rig.Root, "moved-original"));
        Directory.CreateDirectory(rig.Downloads);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(rig.Downloads, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.CopyOriginalContentWithinSourceAsync(content, output, Run, rig.Retain, CancellationToken.None));
        Assert.Empty(output.ToArray()); Assert.Empty(Directory.EnumerateFileSystemEntries(rig.Downloads));
        rig.ExpectOwnerFailure = true;
    }

    [LinuxDownloadFact]
    public async Task Revoked_Files_WRITE_refuses_staging_and_preserves_exact_retained_failure()
    {
        await using var rig = new Rig(); var content = await rig.ApproveAsync();
        var issuer = rig.IssueFilesDestination();
        var pin = await rig.Owner.PinOriginalFilesDestinationWithinSourceAsync(issuer.Destination, Run, rig.Retain, CancellationToken.None);
        issuer.Refusal = new UnauthorizedAccessException("actual fixture WRITE withdrawn");
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.CopyOriginalDownloadToFilesWithinSourceAsync(pin, content, Run, rig.Retain, CancellationToken.None));
        Assert.True(Contains(failure, issuer.Refusal)); Assert.Empty(Directory.EnumerateFileSystemEntries(issuer.Directory));
        var close = pin.DisposeAsync().AsTask(); var closeFailure = await Failure(close);
        Assert.True(Contains(closeFailure, issuer.Refusal)); Assert.Same(close, pin.DisposeAsync().AsTask());
        rig.ExpectOwnerFailure = true;
    }

    [LinuxDownloadFact]
    public async Task Existing_Files_content_name_is_not_overwritten_and_unknown_stage_is_retained()
    {
        await using var rig = new Rig(); var content = await rig.ApproveAsync();
        var issuer = rig.IssueFilesDestination(); await File.WriteAllTextAsync(issuer.FinalPath, "independent existing content");
        var pin = await rig.Owner.PinOriginalFilesDestinationWithinSourceAsync(issuer.Destination, Run, rig.Retain, CancellationToken.None);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.CopyOriginalDownloadToFilesWithinSourceAsync(pin, content, Run, rig.Retain, CancellationToken.None));
        Assert.Equal("independent existing content", await File.ReadAllTextAsync(issuer.FinalPath));
        Assert.Single(Directory.EnumerateFiles(issuer.Directory, ".9to1-browser-stage-*.tmp"));
        await Assert.ThrowsAnyAsync<Exception>(() => pin.DisposeAsync().AsTask());
        rig.ExpectOwnerFailure = true;
    }

    [LinuxDownloadFact]
    public async Task A_copied_completion_descriptor_cannot_rebind_an_already_finalized_original_plan()
    {
        await using var rig = new Rig(); var content = await rig.ApproveAsync();
        var execution = rig.Execution!; var original = execution.OriginalCompletion;
        var copied = new ForeignCompletion(original.OriginalPlan, original.OriginalApprovalSource, original.OriginalApproval, original.OriginalExecution);
        Assert.Throws<UnauthorizedAccessException>(() =>
        {
            _ = rig.Owner.FinalizeOriginalDownloadWithinSourceAsync(
                content.OriginalPlan, execution, copied, "application/octet-stream", Run, rig.Retain, CancellationToken.None);
        });
        Assert.Equal(rig.Body, await File.ReadAllBytesAsync(content.OriginalRecord.StoredPath));
    }

    [LinuxDownloadFact]
    public async Task A_foreign_destination_marker_cannot_create_a_Files_pin_or_stage()
    {
        await using var rig = new Rig(); _ = await rig.ApproveAsync();
        var issuer = rig.IssueFilesDestination();
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.PinOriginalFilesDestinationWithinSourceAsync(
            new ForeignDestination(), Run, rig.Retain, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFileSystemEntries(issuer.Directory)); rig.ExpectOwnerFailure = true;
    }

    [LinuxDownloadFact]
    public async Task Original_callback_cannot_join_its_own_physical_owner_under_restored_context()
    {
        await using var rig = new Rig(); var content = await rig.ApproveAsync();
        var neutral = ExecutionContext.Capture()!; var refused = 0;
        await rig.Owner.RevalidateOriginalContentWithinSourceAsync(content, body =>
        {
            ExecutionContext.Run(neutral, _ =>
            {
                Assert.Throws<InvalidOperationException>(() => { _ = rig.Owner.CloseAndDrainAsync(); }); refused++;
            }, null);
            body();
        }, rig.Retain, CancellationToken.None);
        Assert.True(refused > 0);
    }

    private static void Run(Action body) => body();
    private static byte[] StatxFixture()
    {
        var bytes = new byte[256];
        BitConverter.GetBytes(0x17ffU).CopyTo(bytes, 0); BitConverter.GetBytes(1U).CopyTo(bytes, 16);
        BitConverter.GetBytes(1000U).CopyTo(bytes, 20); BitConverter.GetBytes(1001U).CopyTo(bytes, 24);
        BitConverter.GetBytes((ushort)0x8180).CopyTo(bytes, 28); BitConverter.GetBytes(77UL).CopyTo(bytes, 32);
        BitConverter.GetBytes(5UL).CopyTo(bytes, 40); BitConverter.GetBytes(10L).CopyTo(bytes, 96);
        BitConverter.GetBytes(8U).CopyTo(bytes, 104); BitConverter.GetBytes(9L).CopyTo(bytes, 112);
        BitConverter.GetBytes(7U).CopyTo(bytes, 120); BitConverter.GetBytes(2U).CopyTo(bytes, 136);
        BitConverter.GetBytes(3U).CopyTo(bytes, 140); BitConverter.GetBytes(9UL).CopyTo(bytes, 144); return bytes;
    }
    private static bool ContainsKind<T>(Exception error) where T : Exception => error is T
        || error is AggregateException group && group.InnerExceptions.Any(ContainsKind<T>)
        || error.InnerException is { } inner && ContainsKind<T>(inner);
    private static bool Contains(Exception error, Exception same) => ReferenceEquals(error, same) ||
        error is AggregateException group && group.InnerExceptions.Any(value => Contains(value, same));
    private static async Task<Exception> Failure(Task task)
    { try { await task; } catch (Exception failure) { return task.Exception ?? failure; } throw new InvalidOperationException("The original retained Task was expected to fail."); }
    [DllImport("libc", EntryPoint = "link", SetLastError = true)] private static extern int Link([MarshalAs(UnmanagedType.LPUTF8Str)] string source, [MarshalAs(UnmanagedType.LPUTF8Str)] string target);
    public sealed class LinuxDownloadFactAttribute : FactAttribute
    {
        public LinuxDownloadFactAttribute()
        {
            if (!OperatingSystem.IsLinux() || !BitConverter.IsLittleEndian || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
                Skip = "Requires supported actual little-endian Linux x64/ARM64; other native acceptance remains UNRUN.";
        }
    }
    private sealed class PrivateDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "haven-linux-download-custody-" + Guid.NewGuid().ToString("N"));
        internal PrivateDirectory()
        {
            Directory.CreateDirectory(Path);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
    private sealed class Rig : IAsyncDisposable
    {
        private readonly PrivateDirectory _directory = new();
        private readonly List<Task> _sources = [];
        internal string Root => _directory.Path;
        internal string Downloads => Path.Combine(Root, "downloads");
        internal byte[] Body { get; } = Enumerable.Range(0, 70 * 1024).Select(value => (byte)(value % 251)).ToArray();
        internal BrowserOriginalNativeDownloadPhysicalOwner Owner { get; }
        internal BrowserDownloadTransport Transport { get; }
        internal BrowserAutomationStore Store { get; }
        internal BrowserNativeDownloadAutomationService Service { get; }
        internal OriginalFileExecution? Execution;
        internal bool ExpectOwnerFailure, ExpectApprovalFailure;
        internal Rig(bool createDownloads = true, bool allowProvision = false)
        {
            if (createDownloads)
            {
                Directory.CreateDirectory(Downloads);
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(Downloads, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            Owner = allowProvision ? new(Downloads, Root) : new(Downloads);
            var policy = new FixturePolicy(); Transport = new(policy, Downloads, Owner);
            Store = new(new Paths(Root)); Service = new(new StoreQueries(Store), policy, Store);
        }
        internal void Retain(Task task) { lock (_sources) _sources.Add(task); }
        internal async Task<BrowserActionExecutionResult> ExecuteApprovalAsync(Action<OriginalFileExecution>? beforeFinalize = null)
        {
            var plan = await Transport.PrepareNativeDownloadAsync(Guid.NewGuid(), new("https://example.test/attachment"), null, "report.bin", null, CancellationToken.None);
            Execution = new(Transport, plan, Body, beforeFinalize, Retain);
            var pending = await Service.RequestNativeDownloadAsync(new(plan.ActionId, new("https://example.test/attachment"), plan.FileName, false), Execution, CancellationToken.None);
            return await Service.ApproveAsync(pending.Id, CancellationToken.None);
        }
        internal async Task<IBrowserOriginalDownloadContent> ApproveAsync(Action<OriginalFileExecution>? beforeFinalize = null)
        {
            var result = await ExecuteApprovalAsync(beforeFinalize); Assert.Equal(BrowserActionState.Executed, result.State);
            var content = Assert.IsAssignableFrom<IBrowserOriginalDownloadContent>(Execution!.Content);
            Assert.Same(result.Download, content.OriginalRecord); return content;
        }
        internal DestinationIssuer IssueFilesDestination()
        {
            var folder = Path.Combine(Root, "Files-root"); Directory.CreateDirectory(folder);
            if (OperatingSystem.IsLinux()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var issuer = new DestinationIssuer(folder); Owner.BindOriginalFilesDestinationSource(issuer); return issuer;
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                var close = Owner.CloseAndDrainAsync(); Assert.Same(close, Owner.CloseAndDrainAsync());
                if (ExpectOwnerFailure) await Assert.ThrowsAnyAsync<Exception>(() => close); else await close;
                if (ExpectApprovalFailure) await Assert.ThrowsAnyAsync<Exception>(() => Service.DisposeAsync().AsTask()); else await Service.DisposeAsync();
            }
            finally { Store.Dispose(); _directory.Dispose(); }
        }
    }
    private sealed class OriginalFileExecution(BrowserDownloadTransport transport, BrowserDownloadTransport.NativeDownloadPlan plan,
        byte[] bytes, Action<OriginalFileExecution>? beforeFinalize, Action<Task> retain) :
        IBrowserOriginalApprovedNativeDownloadExecution, IBrowserOriginalNativeDownloadCompletionSource
    {
        internal BrowserDownloadTransport.NativeDownloadPlan Plan => plan;
        internal IBrowserOriginalDownloadContent? Content;
        private IBrowserOriginalNativeDownloadApprovalSource? _issuer;
        private IBrowserOriginalNativeDownloadApproval? _approval;
        private Completion? _completion;
        internal IBrowserOriginalNativeDownloadCompletion OriginalCompletion => _completion ?? throw new InvalidOperationException();
        private Task<BrowserDownloadRecord>? _execute;
        private bool _eof;
        public void BindOriginalDownloadApproval(IBrowserOriginalNativeDownloadApprovalSource source, IBrowserOriginalNativeDownloadApproval receipt)
        {
            if (_approval is not null || !source.IsIssuedOriginalApproval(receipt, this) || receipt.ActionId != plan.ActionId)
                throw new UnauthorizedAccessException("Actual synthetic native execution requires the real service receipt.");
            _issuer = source; _approval = receipt;
        }
        public Task<BrowserDownloadRecord> ExecuteAsync(CancellationToken token) => _execute ??= ExecuteCore(token);
        private async Task<BrowserDownloadRecord> ExecuteCore(CancellationToken token)
        {
            await using (var output = new FileStream(plan.PartialPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.Asynchronous))
            { await output.WriteAsync(bytes, token); await output.FlushAsync(token); }
            _eof = true; beforeFinalize?.Invoke(this);
            _completion = new(transport.GetOriginalTransportPlan(plan), _issuer!, _approval!, this);
            Content = await transport.FinalizeOriginalNativeDownloadWithinSourceAsync(plan, this, _completion, "application/octet-stream", Run, retain, token);
            return Content.OriginalRecord;
        }
        public Task CancelAsync(CancellationToken token) => Task.CompletedTask;
        private sealed record Completion(IBrowserOriginalNativeDownloadTransportPlan OriginalPlan,
            IBrowserOriginalNativeDownloadApprovalSource OriginalApprovalSource, IBrowserOriginalNativeDownloadApproval OriginalApproval,
            IBrowserNativeDownloadExecution OriginalExecution) : IBrowserOriginalNativeDownloadCompletion;
        public bool IsIssuedOriginalCompletion(IBrowserOriginalNativeDownloadCompletion value, IBrowserOriginalNativeDownloadTransportPlan originalPlan) =>
            ReferenceEquals(value, _completion) && ReferenceEquals(value.OriginalPlan, originalPlan) && ReferenceEquals(value.OriginalExecution, this);
        public void DemandOriginalCompletedDownload(IBrowserOriginalNativeDownloadCompletion value)
        {
            if (!_eof || !ReferenceEquals(value, _completion) || _issuer is null || _approval is null ||
                !_issuer.IsIssuedOriginalApproval(_approval, this) || !_issuer.IsOriginalExecutionAdmission(_approval, this))
                throw new UnauthorizedAccessException("No genuine approved fixture EOF exists.");
        }
        public Task RevalidateOriginalCompletionWithinSourceAsync(IBrowserOriginalNativeDownloadCompletion value,
            Action<Action> scope, Action<Task> retainActual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); scope(() => DemandOriginalCompletedDownload(value)); return Task.CompletedTask; }
    }
    // Fault injection at an actual bound transport-source seam. This fixture grants no Home
    // approval and never writes a native body; malformed/cancelled setup must stop before mkdir.
    private sealed class FaultySetupTransport(string root, bool invalidDescription = false, Action? duringDescription = null)
        : IBrowserOriginalNativeDownloadTransportSource
    {
        private sealed record Marker(Guid ActionId) : IBrowserOriginalNativeDownloadTransportPlan;
        internal IBrowserOriginalNativeDownloadTransportPlan Plan { get; } = new Marker(Guid.NewGuid());
        public bool IsIssuedOriginalTransportPlan(IBrowserOriginalNativeDownloadTransportPlan originalPlan) => ReferenceEquals(originalPlan, Plan);
        public BrowserOriginalNativeDownloadPlanDescription GetOriginalTransportPlanDescription(IBrowserOriginalNativeDownloadTransportPlan originalPlan)
        {
            if (!IsIssuedOriginalTransportPlan(originalPlan)) throw new UnauthorizedAccessException();
            duringDescription?.Invoke();
            var final = Path.Combine(invalidDescription ? Path.GetDirectoryName(root)! : root, "file.bin");
            return new(Plan.ActionId, root, final + ".haven-download-" + Guid.NewGuid().ToString("N") + ".tmp",
                final, "file.bin", "https://example.test/file", DateTimeOffset.UtcNow);
        }
    }
    private sealed record ForeignCompletion(IBrowserOriginalNativeDownloadTransportPlan OriginalPlan,
        IBrowserOriginalNativeDownloadApprovalSource OriginalApprovalSource, IBrowserOriginalNativeDownloadApproval OriginalApproval,
        IBrowserNativeDownloadExecution OriginalExecution) : IBrowserOriginalNativeDownloadCompletion;
    private sealed class ForeignDestination : IBrowserOriginalFilesPhysicalDestination { }
    private sealed class DestinationIssuer(string folder) : IBrowserOriginalFilesPhysicalDestinationSource
    {
        private sealed class Marker : IBrowserOriginalFilesPhysicalDestination { }
        internal IBrowserOriginalFilesPhysicalDestination Destination { get; } = new Marker();
        private readonly Guid _revision = Guid.NewGuid();
        private readonly Guid _store = Guid.NewGuid(), _folder = Guid.NewGuid(), _file = Guid.NewGuid();
        internal Exception? Refusal; internal int Demands;
        internal string Directory => folder;
        internal string FinalPath => Path.Combine(folder, ".9to1-browser-" + _revision.ToString("N") + ".content");
        public bool IsIssuedOriginalPhysicalDestination(IBrowserOriginalFilesPhysicalDestination value) => ReferenceEquals(value, Destination);
        public BrowserOriginalFilesPhysicalDestinationDescription GetOriginalPhysicalDestinationDescription(IBrowserOriginalFilesPhysicalDestination value)
        {
            if (!IsIssuedOriginalPhysicalDestination(value)) throw new UnauthorizedAccessException();
            return new(folder, _store, _folder, _file, _revision, Path.GetFileName(FinalPath),
                new("synthetic-physical-actor", "fixture-profile", null, null, "fixture-only-not-Home-authority"));
        }
        public void DemandOriginalFilesCommit(IBrowserOriginalFilesPhysicalDestination value)
        { Assert.True(IsIssuedOriginalPhysicalDestination(value)); Demands++; if (Refusal is { } actual) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(actual).Throw(); }
    }
    private sealed class FixturePolicy : IBrowserNavigationPolicy
    {
        public Task<BrowserNavigationAssessment> AssessAsync(Uri address, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new BrowserNavigationAssessment(address, true, "synthetic test policy", [])); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "profiles"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
    private sealed class StoreQueries(IBrowserAutomationStore store) : IBrowserAutomationService
    {
        public Task<IReadOnlyList<BrowserPendingAction>> GetPendingAsync(CancellationToken token) => store.GetPendingAsync(token);
        public Task<IReadOnlyList<BrowserDownloadRecord>> GetDownloadsAsync(int limit, CancellationToken token) => store.GetDownloadsAsync(limit, token);
        public Task<IReadOnlyList<BrowserAuditEntry>> GetAuditAsync(int limit, CancellationToken token) => store.GetAuditAsync(limit, token);
        public Task<BrowserPageSnapshot> CapturePageAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> NavigateAsync(string address, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickReferenceAsync(string reference, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FillReferenceAsync(string reference, string value, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserPendingAction> RequestDownloadAsync(string address, string? name, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> ApproveAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<BrowserActionExecutionResult> RejectAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
}
