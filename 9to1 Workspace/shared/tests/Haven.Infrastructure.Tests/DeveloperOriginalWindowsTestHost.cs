using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Haven.Application;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Native Windows controls use synthetic domain issuers only. They never
/// certify installed Home consent, GUI wiring or canonical Task/model execution.</summary>
internal static class DeveloperOriginalWindowsTestHost
{
    internal sealed class WindowsNativeFactAttribute : FactAttribute
    {
        public WindowsNativeFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Requires genuine Windows physical handles, SID and namespace durability. Native proof is UNRUN on this platform.";
            else if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)) Skip = "Requires the supported Windows64 native ABI; other native acceptance is UNRUN.";
        }
    }
    internal static IEnumerable<Exception> Causes(Exception error) => error is AggregateException group ? group.InnerExceptions.SelectMany(Causes) : [error];
    internal static bool Contains(Exception error, Exception cause) => Causes(error).Any(value => ReferenceEquals(value, cause));
    internal static byte[] Document(DeveloperProjectSetupIntent intent) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = 1, workspace = new
        {
            workspaceId = intent.WorkspaceId, revision = 1,
            roots = new[] { new { rootId = intent.RootId, location = intent.OriginalExistingProjectRoot, environmentId = "local" } },
            projects = new[] { new { projectId = intent.ProjectId, revision = 1, name = intent.ProjectName, rootIds = new[] { intent.RootId } } },
            openEditors = intent.Files.Select(file => new { fileId = file.FileId, projectId = intent.ProjectId, canonicalResourceId = file.FileId.ToString("D") }).ToArray()
        }
    });
    internal static async Task<(IDeveloperProjectOriginalWorkspaceMetadataPreparation Preparation, Permission Permission,
        DeveloperProjectSetupIntent Intent, IDeveloperProjectOriginalExistingSourceCapture Capture, DeveloperProjectSetupStep Step)> Prepare(Rig rig)
    {
        var capture = await rig.Capture(CancellationToken.None); var intent = rig.Intent(capture); var permission = rig.Setups.Issue(intent);
        var step = intent.Steps[^1]; var preparation = await rig.Metadata.PrepareOriginalWorkspaceMetadataAsync(rig.Store, intent, capture, permission, step, CancellationToken.None);
        return (preparation, permission, intent, capture, step);
    }
    internal sealed class Rig : IAsyncDisposable
    {
        internal string Root = Path.Combine(Path.GetTempPath(), "dev-windows-original-" + Guid.NewGuid().ToString("N"));
        internal string Project => Path.Combine(Root, "selected-existing");
        internal MetadataStore Store => _store ??= new MetadataStore(Path.Combine(Root, "app-data"));
        private MetadataStore? _store;
        internal IDeveloperProjectOriginalWorkspaceMetadataSource Metadata => (IDeveloperProjectOriginalWorkspaceMetadataSource)Source;
        internal string File => Path.Combine(Project, "code.cs");
        internal readonly SelectionSource Selections = new(); internal readonly ReadSource Reads = new();
        internal readonly SetupSource Setups = new();
        internal readonly IDeveloperProjectOriginalPhysicalCaptureSource Source;
        internal readonly SavedBindingSource SavedBindings = new();
        internal IDeveloperProjectOriginalDirectoryObservationSource Directories => (IDeveloperProjectOriginalDirectoryObservationSource)Source;
        internal Rig(bool configureStore = true)
        {
            Directory.CreateDirectory(Project); System.IO.File.WriteAllText(File, "original source stays in place");
            Directory.CreateDirectory(Store.OriginalWorkspaceMetadataAncestor);
            Source = configureStore ? new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(Reads, () => Selections, () => Setups, () => Store, () => SavedBindings)
                : new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(Reads, () => Selections, () => Setups);
        }
        internal async Task<IDeveloperProjectOriginalExistingSourceCapture> Capture(CancellationToken token)
        {
            var physical = await Source.OpenOriginalSelectionAsync(Root, Project, token); Selections.Physical = physical;
            return await Source.CaptureOriginalAsync(physical, Selections.Logical, Reads.Admission, token);
        }
        internal DeveloperProjectSetupIntent Intent(IDeveloperProjectOriginalExistingSourceCapture capture)
        {
            var folder = Guid.NewGuid(); var source = Assert.Single(capture.OriginalFiles); var file = Guid.NewGuid();
            return new(Guid.NewGuid(), new("synthetic-profile-actor", Guid.NewGuid().ToString("D"), null, null, "synthetic-revision"),
                Guid.NewGuid(), new string('a', 64), Guid.NewGuid(), Guid.NewGuid().ToString("D"),
                capture.OriginalCaptureReference, capture.OriginalCaptureDigest, Guid.NewGuid(), 0, Guid.NewGuid(), Guid.NewGuid(), folder, "SelectedExisting",
                ImmutableArray<DeveloperProjectSetupFolder>.Empty,
                [new(file, folder, Guid.NewGuid(), source.RelativePath, source.SizeBytes, source.ContentSha256, source.OriginalSourceReference)],
                [new(Guid.NewGuid(), DeveloperProjectSetupStepKind.CreateProjectFolder, null, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory, null, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.RegisterProjectFolder, null, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.RegisterExistingFileMetadata, file, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.RegisterMaterialization, file, folder),
                 new(Guid.NewGuid(), DeveloperProjectSetupStepKind.SaveDevWorkspace, null, folder)])
                { Mode = DeveloperProjectSetupMode.RegisterExisting, OriginalExistingProjectRoot = Project };
        }
        public async ValueTask DisposeAsync()
        { try { await Source.CloseAndDrainOriginalCapturesAsync(); } catch { } if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    internal sealed class Selection : IDeveloperProjectOriginalReadSelection { }
    internal sealed class SelectionSource : IDeveloperProjectOriginalPhysicalReadSelectionSource
    {
        internal Selection Logical = new(); internal IDeveloperProjectOriginalPhysicalSelection? Physical;
        public bool IsIssuedOriginal(IDeveloperProjectOriginalReadSelection value) => ReferenceEquals(value, Logical);
        public bool IsIssuedOriginalPhysicalBinding(IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalPhysicalSelection physical) => IsIssuedOriginal(value) && ReferenceEquals(physical, Physical);
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, AuthenticatedResourceActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginal(value)) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public IReadOnlyList<ResourceScope> GetOriginalReadScopes(IDeveloperProjectOriginalReadSelection value) => throw new NotSupportedException("Synthetic issuer grants no Home scope.");
        public void DemandExternalOriginalReadSelectionJoin() { }
    }
    internal sealed class ReadSource : IDeveloperProjectOriginalReadAdmissionSource, IDeveloperProjectOriginalReadAdmissionJoinGuard
    {
        internal IDeveloperProjectOriginalReadAdmission Admission;
        internal ReadSource() => Admission = new Read(this);
        internal int ReadStarts; internal Exception? Refusal;
        public Task<IDeveloperProjectOriginalReadAdmission> AcquireOriginalAsync(IDeveloperProjectOriginalReadSelection value, CancellationToken token) => throw new NotSupportedException("No Home review is claimed.");
        public Task ValidateOriginalAsync(IDeveloperProjectOriginalReadSelection value, IDeveloperProjectOriginalReadAdmission actual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!ReferenceEquals(actual, Admission)) throw new UnauthorizedAccessException(); return Refusal is null ? Task.CompletedTask : Task.FromException(Refusal); }
        public void DemandExternalOriginalReadAdmissionJoin() { }
        internal sealed class Read(ReadSource owner) : IDeveloperProjectOriginalReadAdmission
        {
            public Task RevalidateOriginalAsync(CancellationToken token) => Task.CompletedTask;
            public T RunOriginalRead<T>(Func<T> start, CancellationToken token) { token.ThrowIfCancellationRequested(); owner.ReadStarts++; return start(); }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    internal sealed class SetupSource : IDeveloperProjectOriginalSetupPermissionSource
    {
        private readonly HashSet<Permission> _issued = []; internal Task Validation = Task.CompletedTask; internal Action? BeforeValidation = null;
        internal Permission Issue(DeveloperProjectSetupIntent intent) { var result = new Permission(intent); _issued.Add(result); return result; }
        public Task<IDeveloperProjectOriginalSetupPermission> AcquireOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, CancellationToken token) => throw new NotSupportedException("No Home setup review is simulated.");
        public Task ValidateOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSetupPermission actual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (actual is not Permission permission || !_issued.Contains(permission) || !ReferenceEquals(intent, permission.Intent)) throw new UnauthorizedAccessException(); BeforeValidation?.Invoke(); return Validation; }
        public void RequestOriginalSetupRetirement() { }
        public void DemandExternalOriginalSetupJoin() { }
        public Task CloseAndDrainOriginalSetupsAsync() => Task.CompletedTask;
    }
    internal sealed class Permission(DeveloperProjectSetupIntent intent) : IDeveloperProjectOriginalSetupPermission
    {
        internal DeveloperProjectSetupIntent Intent => intent; internal int EntryStarts; private Entry? _entry;
        public Task<Entry> EnterOriginalStepAsync(DeveloperProjectSetupStep step, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!intent.Steps.Any(value => ReferenceEquals(value, step))) throw new UnauthorizedAccessException(); EntryStarts++; return Task.FromResult(_entry = new Entry(step)); }
        async Task<IDeveloperProjectOriginalSetupStepEntry> IDeveloperProjectOriginalSetupPermission.EnterOriginalStepAsync(DeveloperProjectSetupStep step, CancellationToken token) => await EnterOriginalStepAsync(step, token);
        public bool IsIssuedOriginalStepEntry(DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupStepEntry entry) => ReferenceEquals(entry, _entry) && _entry?.Closed == false && ReferenceEquals(_entry.Step, step);
        public Task ValidateOriginalStepResultAsync(DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupStepEntry entry, Task actual, object? result, CancellationToken token) => throw new NotSupportedException("Kernel fixture grants no whole Home outcome validation.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    internal sealed class Entry(DeveloperProjectSetupStep step) : IDeveloperProjectOriginalSetupStepEntry, IDeveloperProjectOriginalSetupScopedStepEntry
    {
        internal DeveloperProjectSetupStep Step => step; internal bool Closed; internal Action? Check = null; internal Task<bool>? HeldCheck; private bool _used;
        public void DemandOriginalStepEntry(DeveloperProjectSetupStep actual) { if (Closed || !ReferenceEquals(actual, step)) throw new UnauthorizedAccessException(); }
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep actual, CancellationToken token)
        { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); Check?.Invoke(); return HeldCheck is { } original ? new ValueTask<bool>(original) : ValueTask.FromResult(true); }
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep actual, Action<Action> scope, Action<Task> retain, CancellationToken token)
        {
            Task<bool>? raw = null;
            scope(() => { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); Check?.Invoke(); raw = HeldCheck ?? Task.FromResult(true); retain(raw); });
            return new ValueTask<bool>(raw ?? throw new InvalidOperationException("No original held check was returned."));
        }
        public T RunOriginalStep<T>(DeveloperProjectSetupStep actual, Func<T> body, CancellationToken token)
        { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); if (_used) throw new InvalidOperationException(); _used = true; return body(); }
        public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
    }
    internal sealed class MetadataStore(string ancestor) : IDeveloperProjectOriginalWorkspaceMetadataStore
    {
        public string OriginalWorkspaceMetadataAncestor => ancestor;
        public string OriginalWorkspaceMetadataDirectory => Path.Combine(ancestor, "Dev", "workspaces");
    }
    internal sealed record Binding(Guid WorkspaceId, Guid ProjectId, Guid RootId, string CanonicalRoot,
        AuthenticatedResourceActor OriginalActor) : IDeveloperWorkspaceOriginalExecutionBinding
    { public long WorkspaceRevision => 1; }
    internal sealed record Evidence(IDeveloperProjectOriginalWorkspaceMetadataPreparation OriginalMetadataPreparation,
        Task OriginalMetadataWriteTask, IDeveloperProjectOriginalWorkspaceMetadataObservation OriginalMetadataObservation,
        string OriginalFilesRoot, string OriginalRegistrationStatePath, string OriginalRegistrationStateJson)
        : IDeveloperWorkspaceOriginalExecutionDescriptorEvidence;
    internal sealed class SavedBindingSource : IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource
    {
        internal Binding? Actual; internal Evidence? Descriptor;
        public bool IsIssuedOriginalBinding(IDeveloperWorkspaceOriginalExecutionBinding binding) => ReferenceEquals(binding, Actual);
        public Task RevalidateOriginalAsync(IDeveloperWorkspaceOriginalExecutionBinding binding, AuthenticatedResourceActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginalBinding(binding) || actor != Actual!.OriginalActor) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public IReadOnlyList<ResourceScope> GetOriginalExecutionScopes(IDeveloperWorkspaceOriginalExecutionBinding binding) => throw new NotSupportedException("The synthetic issuer grants no executable Home scopes.");
        public void DemandExternalOriginalExecutionBindingJoin() { }
        public IDeveloperWorkspaceOriginalExecutionDescriptorEvidence GetOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding binding)
            => IsIssuedOriginalBinding(binding) ? Descriptor! : throw new UnauthorizedAccessException();
        public bool IsIssuedOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding binding, IDeveloperWorkspaceOriginalExecutionDescriptorEvidence evidence)
            => IsIssuedOriginalBinding(binding) && ReferenceEquals(evidence, Descriptor);
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool CreateHardLink(string name, string existing, IntPtr attributes);
    internal static void CreateActualJunction(string link, string target)
    {
        Directory.CreateDirectory(link);
        using var handle = OpenJunction(link, 0x40000000, 0, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), "The native junction test directory could not be opened.");
        var substitute = Encoding.Unicode.GetBytes("\\??\\" + target); var print = Encoding.Unicode.GetBytes(target);
        var bytes = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BitConverter.GetBytes(0xa0000003U).CopyTo(bytes, 0); BitConverter.GetBytes(checked((ushort)(bytes.Length - 8))).CopyTo(bytes, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(bytes, 10);
        BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(bytes, 12); BitConverter.GetBytes(checked((ushort)print.Length)).CopyTo(bytes, 14);
        substitute.CopyTo(bytes, 16); print.CopyTo(bytes, 18 + substitute.Length);
        if (!SetJunction(handle, 0x900a4, bytes, (uint)bytes.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The actual native junction control could not be created; it was not exercised.");
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle OpenJunction(string name, uint access, uint sharing, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetJunction(SafeFileHandle handle, uint control, [In] byte[] input, uint inputLength, IntPtr output, uint outputLength, out uint returned, IntPtr overlapped);
}
