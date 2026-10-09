using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Haven.Infrastructure.Native.Windows;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual local Windows64 SID/DACL/sharing/key/SQLite controls. A platform,
/// capability or durability refusal fails the corresponding positive prerequisite;
/// no Linux return or synthetic positive SID/native result can pass these controls.</summary>
[Trait("Platform", "Windows64")]
public sealed class NativePersonalTaskRecoveryStoreWindowsTests
{
    [Fact]
    public void Explicit_Windows_configuration_never_promotes_requested_source_to_protection_or_readiness()
    {
        DemandWindows();
        Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.Disabled,
            NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn(null).OriginalStatus.Kind);
        Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.Disabled,
            NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn("0").OriginalStatus.Kind);
        Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.MalformedConfiguration,
            NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn("true").OriginalStatus.Kind);
        var requested = NativePersonalTaskColdRecoveryConfiguration.ParseOriginalOptIn("1");
        Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified, requested.OriginalStatus.Kind);
        var host = new NativePersonalTaskColdRecoveryHost(requested);
        Assert.Throws<NativePersonalTaskColdRecoverySetupRequiredException>((Action)(() =>
        { _ = host.ObserveOriginalInputAsync(Guid.NewGuid(), Guid.NewGuid(), default); }));
        Assert.Equal(NativePersonalTaskColdRecoveryConfigurationKind.RequestedUnverified, host.ConfigurationStatus.Kind);
    }

    [Fact]
    public void Missing_noncreating_key_read_preserves_actual_private_store_without_outputs()
    {
        using var fixture = new PrivateStore();
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        Assert.Throws<UnauthorizedAccessException>(() => store.ReadOrCreateAuthenticationKey(create: false));
        Assert.False(File.Exists(fixture.KeyPath));
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, ".task-recovery-auth-stage-*"));
        store.Validate();
    }

    [Fact]
    public void Actual_private_key_creation_returns_same_32_bytes_and_preserves_current_SID_security()
    {
        using var fixture = new PrivateStore();
        byte[]? first = null, second = null;
        try
        {
            using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
            first = store.ReadOrCreateAuthenticationKey(create: true);
            second = store.ReadOrCreateAuthenticationKey(create: false);
            Assert.Equal(32, first.Length);
            Assert.True(CryptographicOperations.FixedTimeEquals(first, second), "The two actual protected reads must return the same key.");
            using var key = fixture.OpenRead(".task-recovery-auth.v1");
            Assert.Equal(32UL, WindowsOriginalFileCustody.ReadIdentity(key).Size);
            Assert.Equal(WindowsOriginalFileCustody.CurrentSid(), WindowsOriginalFileCustody.ReadSecurity(key).OwnerSid);
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, ".task-recovery-auth-stage-*"));
            store.Validate();
        }
        finally
        {
            if (first is not null) CryptographicOperations.ZeroMemory(first);
            if (second is not null) CryptographicOperations.ZeroMemory(second);
        }
    }

    [Fact]
    public void Actual_parent_sync_refusal_precedes_generated_buffer_name_stage_and_key_effects()
    {
        using var fixture = new PrivateStore();
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        using var root = WindowsOriginalFileCustody.RetainRoot(fixture.DirectoryPath);
        using var readOnlyParent = root.OpenDirectory(fixture.DirectoryPath, mutable: false);
        // This negative needs an actual SAME-directory readonly Flags0 refusal. A
        // native success is an unmet negative-fixture prerequisite, never faked.
        var probeFailure = Assert.ThrowsAny<Exception>(() => WindowsOriginalFileCustody.Flush(readOnlyParent));
        var original = OriginalWindowsStore(store);
        var directory = original.GetType().GetField("_directory", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var held = (SafeFileHandle)directory.GetValue(original)!;
        Assert.True(WindowsOriginalFileCustody.ReadIdentity(held).SameFile(WindowsOriginalFileCustody.ReadIdentity(readOnlyParent)));
        var nameCalls = 0;
        var callback = original.GetType().GetField("_beforeOriginalStageName", BindingFlags.Instance | BindingFlags.NonPublic)!;
        callback.SetValue(original, (Action<byte[]>)(_ => Interlocked.Increment(ref nameCalls)));
        var before = Directory.GetFiles(fixture.DirectoryPath).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        try
        {
            // Substitute only an actual readonly handle of the SAME held object,
            // never a synthetic native status, principal, ACL, or positive capability.
            directory.SetValue(original, readOnlyParent);
            var refused = Assert.ThrowsAny<Exception>(() => store.ReadOrCreateAuthenticationKey(create: true));
            Assert.Equal(probeFailure.GetType(), refused.GetType());
            Assert.Equal(probeFailure.HResult, refused.HResult);
            if (probeFailure is Win32Exception native && refused is Win32Exception actual)
                Assert.Equal(native.NativeErrorCode, actual.NativeErrorCode);
            Assert.Equal(0, nameCalls);
            Assert.False(File.Exists(fixture.KeyPath));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, ".task-recovery-auth-stage-*"));
            Assert.Equal(before, Directory.GetFiles(fixture.DirectoryPath).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            directory.SetValue(original, held); callback.SetValue(original, null);
        }
        store.Validate();
    }

    [Fact]
    public void Actual_generated_buffer_is_zeroed_when_early_stage_initialization_faults_and_keeps_direct_siblings()
    {
        using var fixture = new PrivateStore();
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        var original = OriginalWindowsStore(store);
        var callback = original.GetType().GetField("_beforeOriginalStageName", BindingFlags.Instance | BindingFlags.NonPublic)!;
        byte[]? generated = null;
        var first = new ArgumentException("Original finite stage-initialization fault.");
        var second = new IOException("Original stage-initialization sibling.");
        callback.SetValue(original, (Action<byte[]>)(buffer =>
        {
            generated = buffer; throw new AggregateException(first, second);
        }));
        try
        {
            // Reaching this private negative seam requires the real writable parent
            // probe to succeed. No native success or generated buffer is fabricated.
            var observed = Assert.ThrowsAny<Exception>(() => store.ReadOrCreateAuthenticationKey(create: true));
            Assert.NotNull(generated); Assert.Equal(32, generated!.Length);
            Assert.True(generated.All(value => value == 0), "The SAME original generated secret must already be zeroed.");
            Assert.Contains(Causes(observed), value => ReferenceEquals(first, value));
            Assert.Contains(Causes(observed), value => ReferenceEquals(second, value));
            Assert.False(File.Exists(fixture.KeyPath));
            Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, ".task-recovery-auth-stage-*"));
        }
        finally
        {
            callback.SetValue(original, null);
            if (generated is not null) CryptographicOperations.ZeroMemory(generated);
        }
        store.Validate();
    }
    private static object OriginalWindowsStore(NativePersonalTaskRecoveryStore original) =>
        typeof(NativePersonalTaskRecoveryStore).GetField("_windows", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(original) ?? throw new InvalidOperationException("The genuine original Windows backing store is required.");
    private static IEnumerable<Exception> Causes(Exception actual)
    {
        yield return actual;
        if (actual is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var value in Causes(child)) yield return value;
        else if (actual.InnerException is { } child) foreach (var value in Causes(child)) yield return value;
    }

    [Fact]
    public void Held_original_ancestors_and_database_reject_actual_rename_until_source_disposes()
    {
        using var fixture = new PrivateStore();
        using (var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath))
        {
            Assert.ThrowsAny<IOException>(() => File.Move(fixture.DatabasePath, fixture.DatabasePath + ".moved"));
            Assert.ThrowsAny<IOException>(() => Directory.Move(fixture.DirectoryPath, fixture.DirectoryPath + "-moved"));
            Assert.False(File.Exists(fixture.DatabasePath + ".moved"));
            Assert.False(Directory.Exists(fixture.DirectoryPath + "-moved")); store.Validate();
        }
        File.Move(fixture.DatabasePath, fixture.DatabasePath + ".moved");
        File.Move(fixture.DatabasePath + ".moved", fixture.DatabasePath);
    }

    [Fact]
    public void Actual_broad_DACL_change_is_refused_without_repair_or_key_creation()
    {
        using var fixture = new PrivateStore();
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        using (var handle = CreateFile(fixture.DatabasePath, 0x40000 | 0x20000 | 0x80, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!ConvertStringSecurityDescriptor("D:P(A;;FA;;;WD)", 1, out var descriptor, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!GetSecurityDescriptorDacl(descriptor, out var present, out var dacl, out _) || !present || dacl == IntPtr.Zero)
                    throw new IOException("The deliberate broad-DACL control produced no actual DACL.");
                var error = SetSecurityInfo(handle, 1, 4 | 0x80000000, IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
                if (error != 0) throw new Win32Exception(unchecked((int)error));
            }
            finally { LocalFree(descriptor); }
        }
        using var observed = fixture.OpenRead(Path.GetFileName(fixture.DatabasePath));
        var changed = WindowsOriginalFileCustody.ReadSecurity(observed).Fingerprint;
        Assert.Throws<UnauthorizedAccessException>(store.Validate);
        Assert.Throws<UnauthorizedAccessException>(() => store.ReadOrCreateAuthenticationKey(create: true));
        Assert.Equal(changed, WindowsOriginalFileCustody.ReadSecurity(observed).Fingerprint);
        Assert.False(File.Exists(fixture.KeyPath));
    }

    [Fact]
    public void Actual_null_DACL_refuses_current_and_fresh_store_without_key_creation_or_repair()
    {
        using var fixture = new PrivateStore();
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        using (var handle = CreateFile(fixture.DatabasePath, 0x40000 | 0x20000 | 0x80, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            var error = SetSecurityInfo(handle, 1, 4 | 0x80000000, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (error != 0) throw new Win32Exception(unchecked((int)error));
        }
        using var observed = fixture.OpenRead(Path.GetFileName(fixture.DatabasePath));
        var actual = WindowsOriginalFileCustody.ReadSecurity(observed);
        Assert.True(actual.DaclPresent); Assert.True(actual.DaclIsNull);
        Assert.Throws<UnauthorizedAccessException>(store.Validate);
        Assert.Throws<UnauthorizedAccessException>(() => NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath));
        Assert.Equal(actual.Fingerprint, WindowsOriginalFileCustody.ReadSecurity(observed).Fingerprint);
        Assert.False(File.Exists(fixture.KeyPath));
    }

    [Fact]
    public void Actual_current_owner_plus_SYSTEM_and_builtin_Administrators_DACL_is_observed_without_repair()
    {
        using var fixture = new PrivateStore();
        var sidBytes = Convert.FromHexString(WindowsOriginalFileCustody.CurrentSid());
        var sid = Marshal.AllocHGlobal(sidBytes.Length); string actualSid;
        try
        {
            Marshal.Copy(sidBytes, 0, sid, sidBytes.Length);
            if (!ConvertSidToStringSid(sid, out var text)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { actualSid = Marshal.PtrToStringUni(text) ?? throw new IOException("Actual current SID text is unavailable."); }
            finally { LocalFree(text); }
        }
        finally { Marshal.FreeHGlobal(sid); }
        using (var handle = CreateFile(fixture.DatabasePath, 0x40000 | 0x20000 | 0x80, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
        {
            if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!ConvertStringSecurityDescriptor("D:P(A;;FA;;;" + actualSid + ")(A;;FA;;;SY)(A;;FA;;;BA)", 1, out var descriptor, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (!GetSecurityDescriptorDacl(descriptor, out var present, out var dacl, out _) || !present || dacl == IntPtr.Zero)
                    throw new IOException("The actual OS-admin trust control produced no DACL.");
                var error = SetSecurityInfo(handle, 1, 4 | 0x80000000, IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
                if (error != 0) throw new Win32Exception(unchecked((int)error));
            }
            finally { LocalFree(descriptor); }
        }
        using var observed = fixture.OpenRead(Path.GetFileName(fixture.DatabasePath));
        var original = WindowsOriginalFileCustody.ReadSecurity(observed);
        Assert.Equal(3, original.Dacl.Count);
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        store.Validate(); Assert.Equal(original.Fingerprint, WindowsOriginalFileCustody.ReadSecurity(observed).Fingerprint);
        Assert.False(File.Exists(fixture.KeyPath));
    }

    [Fact]
    public void Actual_hard_link_and_invalid_key_size_refuse_without_adoption()
    {
        using var fixture = new PrivateStore();
        var alias = Path.Combine(fixture.DirectoryPath, "database-alias.db");
        using (var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath))
        {
            if (!CreateHardLink(alias, fixture.DatabasePath, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Assert.Throws<UnauthorizedAccessException>(store.Validate);
        }
        File.Delete(alias);
        fixture.CreatePrivateFile(".task-recovery-auth.v1", new byte[31]);
        using var invalid = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        Assert.Throws<UnauthorizedAccessException>(() => invalid.ReadOrCreateAuthenticationKey(create: false));
        Assert.Equal(31, new FileInfo(fixture.KeyPath).Length);
    }

    [Fact]
    public void Alternate_stream_remote_and_foreign_database_paths_refuse_before_creating_a_key()
    {
        using var fixture = new PrivateStore();
        Assert.Throws<UnauthorizedAccessException>(() => NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath + ":alternate"));
        Assert.Throws<PlatformNotSupportedException>(() => NativePersonalTaskRecoveryStore.Acquire("\\\\127.0.0.1\\unconfigured", "\\\\127.0.0.1\\unconfigured\\haven.db"));
        Assert.Throws<UnauthorizedAccessException>(() => NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath,
            Path.Combine(Path.GetDirectoryName(fixture.DirectoryPath)!, "foreign.db")));
        Assert.False(File.Exists(fixture.KeyPath));
    }

    [Fact]
    public void Actual_SQLite_WAL_content_changes_keep_same_private_database_custody()
    {
        using var fixture = new PrivateStore();
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = fixture.DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        using (var configure = connection.CreateCommand())
        { configure.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE owning_control(value INTEGER NOT NULL); INSERT INTO owning_control VALUES(1);"; configure.ExecuteNonQuery(); }
        Assert.True(File.Exists(fixture.DatabasePath + "-wal"));
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            using var actual = fixture.OpenRead("haven.db" + suffix);
            var child = WindowsOriginalFileCustody.ReadSecurity(actual);
            Assert.False(child.DaclProtected); Assert.NotEmpty(child.Dacl);
            Assert.All(child.Dacl, ace => Assert.Equal((byte)0x10, ace.Flags));
        }
        store.Validate();
        using (var read = connection.CreateCommand())
        { read.CommandText = "SELECT value FROM owning_control"; Assert.Equal(1L, read.ExecuteScalar()); }
        using (var checkpoint = connection.CreateCommand())
        { checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)"; checkpoint.ExecuteNonQuery(); }
        store.Validate(); Assert.False(File.Exists(fixture.KeyPath));
    }

    [Theory]
    [InlineData("root", false)] [InlineData("database", false)] [InlineData("key", false)]
    [InlineData("root", true)] [InlineData("database", true)] [InlineData("key", true)]
    public void Actual_initial_unprotected_or_inherited_root_database_key_refuses_without_descriptor_repair(string role, bool inherited)
    {
        using var fixture = new PrivateStore();
        if (role == "key") fixture.CreatePrivateFile(".task-recovery-auth.v1", new byte[32]);
        var path = role == "root" ? fixture.DirectoryPath : role == "database" ? fixture.DatabasePath : fixture.KeyPath;
        ApplyExactDacl(path, "D:" + (inherited ? "P" : "") + "(A;" + (inherited ? "ID" : "") + ";FA;;;" + CurrentSidText() + ")");
        using var observed = OpenSecurity(path, role == "root");
        var before = WindowsOriginalFileCustody.ReadSecurity(observed);
        Assert.Equal(inherited, before.DaclProtected);
        Assert.Single(before.Dacl); Assert.Equal((byte)(inherited ? 0x10 : 0), before.Dacl[0].Flags);
        Assert.Equal(WindowsOriginalFileCustody.CurrentSid(), before.Dacl[0].Sid);
        if (role == "key")
        {
            using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
            Assert.Throws<UnauthorizedAccessException>(() => store.ReadOrCreateAuthenticationKey(create: false));
            Assert.Throws<UnauthorizedAccessException>(() => store.ReadOrCreateAuthenticationKey(create: true));
            Assert.Equal(32, new FileInfo(fixture.KeyPath).Length);
        }
        else
        {
            Assert.Throws<UnauthorizedAccessException>(() => NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath));
            Assert.False(File.Exists(fixture.KeyPath));
        }
        Assert.Equal(before.Fingerprint, WindowsOriginalFileCustody.ReadSecurity(observed).Fingerprint);
    }

    [Theory]
    [InlineData("OINP", "FA")] [InlineData("OIIO", "FA")] [InlineData("", "0x009f01ff")]
    public void Actual_protected_database_with_unsupported_flags_or_concrete_rights_refuses(string flags, string rights)
    {
        using var fixture = new PrivateStore();
        var sid = CurrentSidText();
        ApplyExactDacl(fixture.DatabasePath, "D:P(A;;FA;;;" + sid + ")(A;" + flags + ";" + rights + ";;;" + sid + ")");
        using var observed = fixture.OpenRead("haven.db");
        var before = WindowsOriginalFileCustody.ReadSecurity(observed);
        Assert.True(before.DaclProtected); Assert.Equal(2, before.Dacl.Count);
        Assert.True((before.Dacl[1].Flags & ~0x03) != 0 || (before.Dacl[1].AccessMask!.Value & ~0x001f01ffu) != 0,
            "The actual negative fixture must retain the unsupported flags/rights.");
        Assert.Throws<UnauthorizedAccessException>(() => NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath));
        Assert.Equal(before.Fingerprint, WindowsOriginalFileCustody.ReadSecurity(observed).Fingerprint);
        Assert.False(File.Exists(fixture.KeyPath));
    }

    [Fact]
    public void Actual_companion_accepts_exact_explicit_or_same_parent_inherited_policy()
    {
        using var fixture = new PrivateStore();
        fixture.CreatePrivateFile("haven.db-journal", []);
        File.WriteAllBytes(fixture.DatabasePath + "-wal", []);
        using var companion = fixture.OpenRead("haven.db-wal");
        var inherited = WindowsOriginalFileCustody.ReadSecurity(companion);
        Assert.False(inherited.DaclProtected); Assert.Single(inherited.Dacl);
        Assert.Equal((byte)0x10, inherited.Dacl[0].Flags);
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        store.Validate(); Assert.False(File.Exists(fixture.KeyPath));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Actual_companion_explicit_mixture_or_private_wrong_projection_refuses_without_repair(bool mixed)
    {
        using var fixture = new PrivateStore();
        File.WriteAllBytes(fixture.DatabasePath + "-wal", []);
        var sid = CurrentSidText();
        ApplyExactDacl(fixture.DatabasePath + "-wal", "D:(A;ID;FA;;;" + sid + ")(A;" + (mixed ? "" : "ID") + ";FR;;;SY)");
        using var observed = fixture.OpenRead("haven.db-wal");
        var before = WindowsOriginalFileCustody.ReadSecurity(observed);
        Assert.False(before.DaclProtected); Assert.Equal(2, before.Dacl.Count);
        Assert.Equal((byte)(mixed ? 0 : 0x10), before.Dacl[1].Flags);
        Assert.Throws<UnauthorizedAccessException>(() => NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath));
        Assert.Equal(before.Fingerprint, WindowsOriginalFileCustody.ReadSecurity(observed).Fingerprint);
        Assert.False(File.Exists(fixture.KeyPath));
    }

    [Fact]
    public void Actual_same_parent_private_DACL_change_refuses_even_when_companion_inheritance_remains_private()
    {
        using var fixture = new PrivateStore();
        File.WriteAllBytes(fixture.DatabasePath + "-wal", []);
        using var store = NativePersonalTaskRecoveryStore.Acquire(fixture.DirectoryPath, fixture.DatabasePath);
        ApplyExactDacl(fixture.DirectoryPath, "D:P(A;OICI;FA;;;" + CurrentSidText() + ")(A;OICI;FR;;;SY)");
        using var parent = OpenSecurity(fixture.DirectoryPath, true);
        var changed = WindowsOriginalFileCustody.ReadSecurity(parent);
        Assert.True(changed.DaclProtected); Assert.Equal(2, changed.Dacl.Count);
        Assert.Throws<UnauthorizedAccessException>(store.Validate);
        Assert.Equal(changed.Fingerprint, WindowsOriginalFileCustody.ReadSecurity(parent).Fingerprint);
        Assert.False(File.Exists(fixture.KeyPath));
    }

    private static string CurrentSidText()
    {
        var bytes = Convert.FromHexString(WindowsOriginalFileCustody.CurrentSid());
        var sid = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, sid, bytes.Length);
            if (!ConvertSidToStringSid(sid, out var text)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return Marshal.PtrToStringUni(text) ?? throw new IOException("The actual current SID has no native text."); }
            finally { LocalFree(text); }
        }
        finally { Marshal.FreeHGlobal(sid); }
    }
    private static SafeFileHandle OpenSecurity(string path, bool directory)
    {
        var handle = CreateFile(path, 0x40000 | 0x20000 | 0x80, 3, IntPtr.Zero, 3,
            directory ? 0x02000000u : 0, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(error);
    }
    private static void ApplyExactDacl(string path, string text)
    {
        using var handle = OpenSecurity(path, Directory.Exists(path));
        if (!ConvertStringSecurityDescriptor(text, 1, out var descriptor, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            // Deliberately set exact native flags without inherited-policy repair.
            // Every negative checks the resulting actual handle-bound descriptor.
            if (!SetKernelObjectSecurity(handle, 4 | (text.StartsWith("D:P", StringComparison.Ordinal) ? 0x80000000u : 0x20000000u), descriptor))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { LocalFree(descriptor); }
    }

    private static void DemandWindows()
    {
        Assert.True(OperatingSystem.IsWindows() && Environment.Is64BitProcess,
            "This owning control requires actual local Windows64, never a successful Linux return.");
        WindowsOriginalFileCustody.RequireCapabilities();
    }
    private sealed class PrivateStore : IDisposable
    {
        internal string DirectoryPath { get; }
        internal string DatabasePath => Path.Combine(DirectoryPath, "haven.db");
        internal string KeyPath => Path.Combine(DirectoryPath, ".task-recovery-auth.v1");
        internal PrivateStore()
        {
            DemandWindows();
            var temporary = WindowsOriginalFileCustody.NormalizeLocalPath(Path.GetTempPath());
            var leaf = "astra-private-windows-store51-" + Guid.NewGuid().ToString("N");
            DirectoryPath = Path.Combine(temporary, leaf);
            using var root = WindowsOriginalFileCustody.RetainRoot(temporary);
            using var parent = root.OpenDirectory(temporary, mutable: true);
            using var descriptor = WindowsOriginalFileCustody.CreatePrivateDescriptor(root.Principal);
            using var created = WindowsOriginalFileCustody.OpenRelative(parent, leaf,
                WindowsOriginalFileCustody.DirectoryMutable, 3, WindowsOriginalCreateDisposition.CreateNew,
                WindowsOriginalFileKind.Directory, descriptor);
            WindowsOriginalFileCustody.DemandPrivateStage(created, root.Principal);
            using var database = WindowsOriginalFileCustody.OpenRelative(created, "haven.db",
                WindowsOriginalFileCustody.ExclusiveStage, 0, WindowsOriginalCreateDisposition.CreateNew,
                WindowsOriginalFileKind.File, descriptor);
            WindowsOriginalFileCustody.DemandPrivateStage(database, root.Principal);
        }
        internal SafeFileHandle OpenRead(string leaf)
        {
            using var root = WindowsOriginalFileCustody.RetainRoot(DirectoryPath);
            using var parent = root.OpenDirectory(DirectoryPath);
            return WindowsOriginalFileCustody.OpenRelative(parent, leaf, WindowsOriginalFileCustody.FileRead,
                3, WindowsOriginalCreateDisposition.OpenExisting, WindowsOriginalFileKind.File);
        }
        internal void CreatePrivateFile(string leaf, byte[] bytes)
        {
            using var root = WindowsOriginalFileCustody.RetainRoot(DirectoryPath);
            using var parent = root.OpenDirectory(DirectoryPath, mutable: true);
            using var descriptor = WindowsOriginalFileCustody.CreatePrivateDescriptor(root.Principal);
            using var created = WindowsOriginalFileCustody.OpenRelative(parent, leaf, WindowsOriginalFileCustody.ExclusiveStage,
                0, WindowsOriginalCreateDisposition.CreateNew, WindowsOriginalFileKind.File, descriptor);
            RandomAccess.Write(created, bytes, 0); WindowsOriginalFileCustody.Flush(created);
        }
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string path, string original, IntPtr security);
    [DllImport("advapi32.dll", EntryPoint = "ConvertStringSecurityDescriptorToSecurityDescriptorW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertStringSecurityDescriptor(string text, uint revision, out IntPtr descriptor, out uint length);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSecurityDescriptorDacl(IntPtr descriptor, [MarshalAs(UnmanagedType.Bool)] out bool present, out IntPtr dacl, [MarshalAs(UnmanagedType.Bool)] out bool defaulted);
    [DllImport("advapi32.dll")] private static extern uint SetSecurityInfo(SafeFileHandle handle, uint kind, uint information, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint information, IntPtr descriptor);
    [DllImport("advapi32.dll", EntryPoint = "ConvertSidToStringSidW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ConvertSidToStringSid(IntPtr sid, out IntPtr text);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
