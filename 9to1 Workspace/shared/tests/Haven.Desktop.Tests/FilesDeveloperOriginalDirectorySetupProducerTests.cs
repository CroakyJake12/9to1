using System.Runtime.CompilerServices;
using Haven.Application;
using HavenOS.Apps.Dev;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Same real Home/profile/configured Files/provider/journal/kernel project. The
/// configured read/setup issuers are synthetic: no actual Home review, Ready or complete
/// project-registration claim. These controls exercise the genuine paired first two steps.</summary>
public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Real_same_source_directory_step_follows_folder_ack_and_preserves_source_git_root_and_remaining_plan()
    {
        await using var rig = await Rig.Create(true); var token = TestContext.Current.CancellationToken;
        var sourceBefore = File.ReadAllBytes(rig.SourcePath); var originalRoot = rig.Capture.OriginalExistingProjectRoot;
        var folder = await rig.Effector.ExecuteOriginalFolderStepAsync(rig.Prepared, rig.Capture, rig.Permission, rig.Prepared.Intent.Steps[0], token);
        var drive = File.ReadAllBytes(rig.DrivePath);
        var step = rig.Prepared.Intent.Steps[1];
        var result = await rig.Effector.ExecuteOriginalExistingDirectoryStepAsync(rig.Prepared, rig.Capture, rig.Permission, step, token);
        Assert.Same(step, result.OriginalAdmission.Step); Assert.Same(rig.Prepared.Intent, result.AcknowledgedCheckpoint.Intent);
        Assert.Equal(originalRoot, result.OriginalKernelObservation.OriginalDirectoryPath);
        Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, result.AcknowledgedCheckpoint.Observations[0].State);
        Assert.Equal(DeveloperProjectSetupStepState.Acknowledged, result.AcknowledgedCheckpoint.Observations[1].State);
        Assert.All(result.AcknowledgedCheckpoint.Observations.Skip(2), value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
        Assert.Equal(drive, File.ReadAllBytes(rig.DrivePath)); Assert.Equal(sourceBefore, File.ReadAllBytes(rig.SourcePath));
        Assert.True(Directory.Exists(Path.Combine(originalRoot, ".git"))); Assert.Equal(2, rig.Permission.Starts);
        Assert.True(rig.Effector.IsIssuedOriginalStepOutcome(rig.Prepared.Intent, rig.Capture, step, result.OriginalKernelTask, result.OriginalKernelObservation));
        Assert.False(rig.Effector.IsIssuedOriginalStepOutcome(rig.Prepared.Intent with { }, rig.Capture, step, result.OriginalKernelTask, result.OriginalKernelObservation));
        Assert.Equal(folder.OriginalProviderResult.Value!.ItemId.Value, step.FolderId);
    }

    [LinuxDirectoryFact]
    public async Task Unconfigured_directory_owner_refuses_before_pending_entry_or_physical_observation()
    {
        await using var rig = await Rig.Create(false); var token = TestContext.Current.CancellationToken;
        var before = File.ReadAllBytes(rig.HomePath); var drive = File.ReadAllBytes(rig.DrivePath); rig.ExpectedFault = true;
        var actual = rig.Effector.ExecuteOriginalExistingDirectoryStepAsync(rig.Prepared, rig.Capture, rig.Permission, rig.Prepared.Intent.Steps[1], token);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.Contains(Causes(error), value => value is InvalidOperationException);
        Assert.Equal(before, File.ReadAllBytes(rig.HomePath)); Assert.Equal(drive, File.ReadAllBytes(rig.DrivePath));
        Assert.Equal(0, rig.Permission.Starts);
        Assert.All((await rig.Journal.ReadCheckpointAsync(rig.Prepared.Intent.SetupId, rig.Prepared.Intent.OriginalActor, token))!.Observations,
            value => Assert.Equal(DeveloperProjectSetupStepState.NotStarted, value.State));
    }

    public sealed class LinuxDirectoryFactAttribute : FactAttribute
    {
        public LinuxDirectoryFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
            : base(sourceFilePath, sourceLineNumber)
        { if (!OperatingSystem.IsLinux()) Skip = "The maintained original kernel directory boundary is Linux-only; Windows execution remains unvalidated."; }
    }

    private static IEnumerable<Exception> Causes(Exception error) => error is AggregateException group && group.InnerExceptions.Count != 0
        ? group.InnerExceptions.SelectMany(Causes) : [error];
    private sealed class Rig : IAsyncDisposable
    {
        internal string Root = null!, HomePath = null!, DrivePath = null!, SourcePath = null!;
        internal ServiceProvider Graph = null!; internal FileHomeCoreStateStore Home = null!; internal HomeLocalProfileIdentity Profiles = null!;
        internal IDeveloperProjectOriginalPhysicalCaptureSource Kernel = null!; internal FilesDeveloperOriginalSourceSelection Selection = null!;
        internal IDeveloperProjectOriginalExistingSourceCapture Capture = null!;
        internal HomeDeveloperProjectSetupJournal Journal = null!; internal FilesDeveloperOriginalSetupScopeSource Scopes = null!;
        internal FilesDeveloperOriginalFolderSetupProducer Effector = null!; internal HomeDeveloperProjectSetupJournal.Prepared Prepared = null!;
        internal FileDeveloperWorkspaceStore WorkspaceStore = null!;
        internal SetupSource Setups = new(); internal Permission Permission = null!; internal bool ExpectedFault;
        internal static Task<Rig> Create(bool directoryConfigured) => Create(directoryConfigured, false);
        internal static async Task<Rig> Create(bool directoryConfigured, bool workspaceConfigured)
        {
            var rig = new Rig { Root = Path.Combine(Path.GetTempPath(), "paired-dev-existing-" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(rig.Root);
            try
            {
                var token = TestContext.Current.CancellationToken; rig.HomePath = Path.Combine(rig.Root, "home.json");
                rig.Home = new(rig.HomePath); rig.Profiles = new(rig.Home, new OperatingSystemPrincipalSource());
                var services = new ServiceCollection(); services.AddSingleton<IHomeCoreStateStore>(rig.Home); services.AddSingleton(rig.Profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(rig.Profiles); services.AddSingleton(new HomePermissionTrustService(rig.Home, (_, _) => null));
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>(); services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>(); services.AddSingleton<ResourceAuthorizationService>();
                services.AddFilesNativeHost(); rig.Graph = services.BuildServiceProvider(); var chosen = Path.Combine(rig.Root, "chosen"); Directory.CreateDirectory(chosen);
                await rig.Graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen, rig.Graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
                var authority = rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>(); var workspace = (await authority.GetCurrentAsync(token))!;
                rig.DrivePath = Path.Combine(chosen, ".9to1-files", "drive.json");
                var project = Path.Combine(chosen, "ExistingProject"); Directory.CreateDirectory(project); Directory.CreateDirectory(Path.Combine(project, ".git"));
                rig.SourcePath = Path.Combine(project, "Code.cs"); File.WriteAllText(rig.SourcePath, "original selected source");
                rig.WorkspaceStore = new FileDeveloperWorkspaceStore(Path.Combine(rig.Root, "Dev"));
                var reads = new Reads(); rig.Kernel = workspaceConfigured
                    ? new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(reads, () => rig.Selection, () => rig.Setups, () => rig.WorkspaceStore, () => rig.Effector)
                    : new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(reads, () => rig.Selection, () => rig.Setups);
                rig.Selection = new(authority, rig.Kernel, reads); reads.Selections = rig.Selection;
                var selected = await rig.Selection.SelectOriginalExistingProjectAsync(workspace.Configuration.StoreId, project, token);
                var read = await reads.AcquireOriginalAsync(selected, token);
                try { rig.Capture = await rig.Selection.CaptureOriginalAsync(selected, read, token); }
                finally { await read.DisposeAsync(); }
                rig.Journal = new(rig.Home, rig.Profiles, rig.Selection, () => rig.Effector);
                rig.Scopes = new(authority, rig.Selection, () => rig.Journal);
                rig.Effector = workspaceConfigured
                    ? new(rig.Scopes, () => rig.Journal, () => rig.Setups, () => (IDeveloperProjectOriginalDirectoryObservationSource)rig.Kernel, () => rig.WorkspaceStore)
                    : directoryConfigured
                    ? new(rig.Scopes, () => rig.Journal, () => rig.Setups, () => (IDeveloperProjectOriginalDirectoryObservationSource)rig.Kernel)
                    : new(rig.Scopes, () => rig.Journal, () => rig.Setups);
                rig.Setups.Outcomes = rig.Effector;
                var destination = await rig.Scopes.CaptureOriginalDestinationAsync(workspace.Configuration.StoreId, workspace.Configuration.AppFolders["write"], token);
                rig.Prepared = await rig.Journal.PrepareExistingOriginalAsync(Guid.NewGuid(), destination.OriginalActor, destination.OriginalStoreId,
                    destination.OriginalConfigurationDigest, destination.OriginalFolderId, destination.OriginalFolderRevision, rig.Capture,
                    Guid.NewGuid(), 0, "ExistingProject", token);
                await rig.Scopes.BindOriginalAsync(destination, rig.Prepared, rig.Capture, token);
                rig.Permission = rig.Setups.Issue(rig.Prepared.Intent, rig.Capture); return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            if (Effector is not null) try { await Effector.CloseAndDrainOriginalFolderSetupsAsync(); } catch (Exception error) { if (!ExpectedFault) errors.Add(error); }
            if (Scopes is not null) try { await Scopes.CloseAndDrainOriginalSetupScopesAsync(); } catch (Exception error) { errors.Add(error); }
            if (Journal is not null) try { await Journal.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (Selection is not null) try { await Selection.CloseAndDrainOriginalSelectionsAsync(); } catch (Exception error) { errors.Add(error); }
            if (Kernel is not null) try { await Kernel.CloseAndDrainOriginalCapturesAsync(); } catch (Exception error) { errors.Add(error); }
            if (Graph is not null) try { await Graph.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Actual paired project originals/cleanup failed.", errors);
        }
    }
    private sealed class Reads : IDeveloperProjectOriginalReadAdmissionSource, IDeveloperProjectOriginalReadAdmissionJoinGuard
    {
        internal FilesDeveloperOriginalSourceSelection Selections = null!; private readonly HashSet<Read> _issued = [];
        public Task<IDeveloperProjectOriginalReadAdmission> AcquireOriginalAsync(IDeveloperProjectOriginalReadSelection selected, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!Selections.IsIssuedOriginal(selected)) throw new UnauthorizedAccessException(); var read = new Read(selected); _issued.Add(read); return Task.FromResult<IDeveloperProjectOriginalReadAdmission>(read); }
        public Task ValidateOriginalAsync(IDeveloperProjectOriginalReadSelection selected, IDeveloperProjectOriginalReadAdmission admission, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (admission is not Read read || !_issued.Contains(read) || !ReferenceEquals(read.Selected, selected) || !Selections.IsIssuedOriginal(selected)) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public void DemandExternalOriginalReadAdmissionJoin() { }
        private sealed class Read(IDeveloperProjectOriginalReadSelection selected) : IDeveloperProjectOriginalReadAdmission
        {
            internal IDeveloperProjectOriginalReadSelection Selected => selected;
            public Task RevalidateOriginalAsync(CancellationToken token) => Task.CompletedTask;
            public T RunOriginalRead<T>(Func<T> source, CancellationToken token) { token.ThrowIfCancellationRequested(); return source(); }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class SetupSource : IDeveloperProjectOriginalSetupPermissionSource
    {
        internal IDeveloperProjectOriginalSetupStepOutcomeSource Outcomes = null!; private readonly HashSet<Permission> _issued = [];
        internal Permission Issue(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture)
        { var permission = new Permission(this, intent, capture); _issued.Add(permission); return permission; }
        public Task<IDeveloperProjectOriginalSetupPermission> AcquireOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture, CancellationToken token) => throw new NotSupportedException("Synthetic fixture supplies no Home review acquisition.");
        public Task ValidateOriginalAsync(DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSetupPermission permission, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (permission is not Permission actual || !_issued.Contains(actual) || !ReferenceEquals(actual.Intent, intent)) throw new UnauthorizedAccessException(); return Task.CompletedTask; }
        public void RequestOriginalSetupRetirement() { }
        public void DemandExternalOriginalSetupJoin() { }
        public Task CloseAndDrainOriginalSetupsAsync() => Task.CompletedTask;
    }
    private sealed class Permission(SetupSource issuer, DeveloperProjectSetupIntent intent, IDeveloperProjectOriginalSourceCapture capture) : IDeveloperProjectOriginalSetupPermission
    {
        internal DeveloperProjectSetupIntent Intent => intent; internal int Starts; private Entry? _last;
        internal Func<DeveloperProjectSetupStep, Action<Action>, Action<Task>, CancellationToken, Task<bool>>? ScopedCheck = null;
        public Task<IDeveloperProjectOriginalSetupStepEntry> EnterOriginalStepAsync(DeveloperProjectSetupStep step, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!intent.Steps.Any(value => ReferenceEquals(value, step))) throw new UnauthorizedAccessException(); Starts++; return Task.FromResult<IDeveloperProjectOriginalSetupStepEntry>(_last = new(step, ScopedCheck)); }
        public bool IsIssuedOriginalStepEntry(DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupStepEntry entry) => ReferenceEquals(entry, _last) && _last?.Closed == false && ReferenceEquals(_last.Step, step);
        public async Task ValidateOriginalStepResultAsync(DeveloperProjectSetupStep step, IDeveloperProjectOriginalSetupStepEntry entry, Task actual, object? result, CancellationToken token)
        {
            if (!ReferenceEquals(entry, _last) || _last?.Closed != true || !issuer.Outcomes.IsIssuedOriginalStepOutcome(intent, capture, step, actual, result)) throw new UnauthorizedAccessException();
            await issuer.Outcomes.ValidateOriginalStepOutcomeAsync(intent, capture, step, actual, result, token);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Entry(DeveloperProjectSetupStep step, Func<DeveloperProjectSetupStep, Action<Action>, Action<Task>, CancellationToken, Task<bool>>? scopedCheck)
        : IDeveloperProjectOriginalSetupStepEntry, IDeveloperProjectOriginalSetupScopedStepEntry
    {
        internal DeveloperProjectSetupStep Step => step; internal bool Closed; private bool _used;
        public void DemandOriginalStepEntry(DeveloperProjectSetupStep actual) { if (Closed || !ReferenceEquals(actual, step)) throw new UnauthorizedAccessException(); }
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep actual, CancellationToken token) { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); return ValueTask.FromResult(true); }
        public ValueTask<bool> CheckOriginalStepCommitAsync(DeveloperProjectSetupStep actual,
            Action<Action> originalScope, Action<Task> retainActual, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); Task<bool>? raw = null;
            originalScope(() => { raw = scopedCheck?.Invoke(actual, originalScope, retainActual, token) ?? Task.FromResult(true); retainActual(raw); });
            return new(raw ?? throw new InvalidOperationException("The scoped synthetic check returned no original Task."));
        }
        public T RunOriginalStep<T>(DeveloperProjectSetupStep actual, Func<T> source, CancellationToken token) { token.ThrowIfCancellationRequested(); DemandOriginalStepEntry(actual); if (_used) throw new InvalidOperationException(); _used = true; return source(); }
        public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
    }
}
