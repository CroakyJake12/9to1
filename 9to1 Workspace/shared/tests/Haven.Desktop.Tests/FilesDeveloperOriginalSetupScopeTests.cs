using System.Collections.Immutable;
using System.Text.Json;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

/// <summary>Real configured Home/profile/Files provider and journal. Source-capture authority
/// is synthetic; these controls prove destination/intent ownership only, never read/import effect
/// permission, a kernel capture, an actual Home setup entry, native readiness or product acceptance.</summary>
public sealed partial class FilesDeveloperOriginalSetupScopeTests
{
    [Fact]
    public async Task Actual_destination_capture_reads_existing_owned_folder_without_adoption_or_effect()
    {
        await using var rig = await Rig.Create(); var token = TestContext.Current.CancellationToken;
        var drive = await File.ReadAllBytesAsync(rig.DrivePath, token); var home = await File.ReadAllBytesAsync(rig.HomePath, token);
        var destination = await rig.Owner.CaptureOriginalDestinationAsync(rig.Workspace.Configuration.StoreId, rig.Folder.Id, token);
        Assert.Equal(rig.Workspace.Actor, destination.OriginalActor); Assert.Equal(rig.Folder.Id.Value, destination.OriginalFolderId);
        Assert.Equal(rig.Folder.CurrentRevisionId!.Value.Value.ToString("D"), destination.OriginalFolderRevision);
        Assert.Equal(drive, await File.ReadAllBytesAsync(rig.DrivePath, token)); Assert.Equal(home, await File.ReadAllBytesAsync(rig.HomePath, token));
        var decision = await rig.Owner.EvaluateAsync(rig.Workspace.Actor, "dev.project.setup.commit",
            new("dev.project.destination", "public-path-is-not-a-receipt", destination.OriginalFolderRevision, ResourceAccess.Write), token);
        Assert.False(decision.Allowed);
        var close = rig.Owner.CloseAndDrainOriginalSetupScopesAsync(); Assert.Same(close, rig.Owner.CloseAndDrainOriginalSetupScopesAsync()); await close;
    }

    [Fact]
    public async Task Private_journal_preparation_binds_same_intent_and_capture_but_value_equal_copy_is_denied()
    {
        await using var rig = await Rig.Create(); var token = TestContext.Current.CancellationToken;
        var destination = await rig.Owner.CaptureOriginalDestinationAsync(rig.Workspace.Configuration.StoreId, rig.Folder.Id, token);
        var prepared = await rig.Prepare(destination, token);
        await rig.Owner.BindOriginalAsync(destination, prepared, rig.Capture, token);
        Assert.True(rig.Owner.IsIssuedOriginalSetupBinding(prepared.Intent, rig.Capture));
        var scope = Assert.Single(rig.Owner.GetOriginalSetupScopes(prepared.Intent, rig.Capture));
        Assert.Equal("dev.project.destination", scope.Kind); Assert.Equal(ResourceAccess.Write, scope.Access);
        Assert.DoesNotContain(rig.Workspace.Configuration.RootDirectory, scope.Id);
        var copied = prepared.Intent with { };
        Assert.Equal(JsonSerializer.Serialize(prepared.Intent), JsonSerializer.Serialize(copied));
        Assert.False(rig.Owner.IsIssuedOriginalSetupBinding(copied, rig.Capture));
        Assert.Throws<UnauthorizedAccessException>(() => rig.Owner.GetOriginalSetupScopes(copied, rig.Capture));
        var allowed = await rig.Owner.EvaluateAsync(rig.Workspace.Actor, "dev.project.setup.commit", scope, token);
        Assert.True(allowed.Allowed);
        var other = rig.Workspace.Actor with { AuthenticationRevision = "different-current-session" };
        Assert.False((await rig.Owner.EvaluateAsync(other, "dev.project.setup.commit", scope, token)).Allowed);
        Assert.False((await rig.Owner.EvaluateAsync(rig.Workspace.Actor, "files.artifact.write", scope, token)).Allowed);
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    [Fact]
    public async Task Actual_folder_revision_change_refuses_old_destination_without_rewriting_owning_state()
    {
        await using var rig = await Rig.Create(); var token = TestContext.Current.CancellationToken;
        var destination = await rig.Owner.CaptureOriginalDestinationAsync(rig.Workspace.Configuration.StoreId, rig.Folder.Id, token);
        var now = DateTimeOffset.UtcNow;
        var mutation = await rig.Workspace.Provider.MutateAsync(new(new(Guid.NewGuid()), rig.Workspace.Actor.ActorId, rig.Folder.Id,
            null, null, "Rename", rig.Folder.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null), "Renamed by actual owner", token);
        Assert.True(mutation.IsSuccess);
        var after = await File.ReadAllBytesAsync(rig.DrivePath, token); rig.ExpectScopeFault = true;
        await AssertCause<UnauthorizedAccessException>(() => rig.Owner.RevalidateOriginalDestinationAsync(destination, token));
        Assert.Equal(after, await File.ReadAllBytesAsync(rig.DrivePath, token)); Assert.Equal(0, rig.Captures.ResourceEffects);
        var closeError = await Assert.ThrowsAnyAsync<Exception>(() => rig.Owner.CloseAndDrainOriginalSetupScopesAsync());
        Assert.Contains(Causes(closeError), value => value is UnauthorizedAccessException);
    }

    [Fact]
    public async Task Restored_context_original_journal_callback_cannot_join_encompassing_destination_owner()
    {
        await using var rig = await Rig.Create(); var token = TestContext.Current.CancellationToken;
        var context = ExecutionContext.Capture()!;
        var destination = await rig.Owner.CaptureOriginalDestinationAsync(rig.Workspace.Configuration.StoreId, rig.Folder.Id, token);
        var prepared = await rig.Prepare(destination, token); Exception? denied = null;
        rig.Captures.OnRevalidate = () => ExecutionContext.Run(context, _ =>
        {
            try { rig.Owner.CloseAndDrainOriginalSetupScopesAsync().GetAwaiter().GetResult(); }
            catch (Exception error) { denied = error; }
        }, null);
        await rig.Owner.BindOriginalAsync(destination, prepared, rig.Capture, token);
        Assert.IsType<InvalidOperationException>(denied);
        Assert.True(rig.Owner.IsIssuedOriginalSetupBinding(prepared.Intent, rig.Capture));
        Assert.Equal(0, rig.Captures.ResourceEffects);
    }

    private static IEnumerable<Exception> Causes(Exception value)
    {
        yield return value;
        if (value is AggregateException group) foreach (var direct in group.InnerExceptions) foreach (var cause in Causes(direct)) yield return cause;
        else if (value.InnerException is { } direct) foreach (var cause in Causes(direct)) yield return cause;
    }
    private static async Task AssertCause<T>(Func<Task> source) where T : Exception
    { var error = await Assert.ThrowsAnyAsync<Exception>(source); Assert.Contains(Causes(error), value => value is T); }
    private sealed class Capture(string root) : IDeveloperProjectOriginalExistingSourceCapture
    {
        public string OriginalExistingProjectRoot => root;
        public string OriginalCaptureReference { get; } = "synthetic-owner-source:" + Guid.NewGuid().ToString("D");
        public string OriginalCaptureDigest => new('d', 64);
        public ImmutableArray<string> OriginalFolderPaths => [];
        public ImmutableArray<DeveloperProjectCapturedSourceFile> OriginalFiles => [new("Source.cs", 3, new('a', 64), "retained-synthetic-source")];
    }
    private sealed class Captures(Capture capture) : IDeveloperProjectOriginalCaptureAuthority
    {
        internal Action? OnRevalidate; internal int ResourceEffects = 0;
        public bool IsIssuedOriginal(IDeveloperProjectOriginalSourceCapture actual) => ReferenceEquals(actual, capture);
        public Task RevalidateOriginalAsync(IDeveloperProjectOriginalSourceCapture actual, AuthenticatedResourceActor actor, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (!IsIssuedOriginal(actual)) throw new UnauthorizedAccessException(); var callback = OnRevalidate; OnRevalidate = null; callback?.Invoke(); return Task.CompletedTask; }
        public void DemandExternalOriginalCaptureJoin() { }
    }
    private sealed class Rig : IAsyncDisposable
    {
        internal string Root = null!, HomePath = null!, DrivePath = null!;
        internal ServiceProvider Graph = null!; internal NativeFilesWorkspace Workspace = null!; internal HostedItemMetadata Folder = null!;
        internal FilesDeveloperOriginalSetupScopeSource Owner = null!; internal HomeDeveloperProjectSetupJournal Journal = null!;
        internal FileHomeCoreStateStore Home = null!; internal HomeLocalProfileIdentity Profiles = null!;
        internal FilesDeveloperOriginalFolderSetupProducer? FolderProducer;
        internal Capture Capture = null!; internal Captures Captures = null!; internal bool ExpectScopeFault;
        internal static async Task<Rig> Create()
        {
            var rig = new Rig { Root = Path.Combine(Path.GetTempPath(), "astra-dev-destination-" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(rig.Root);
            try
            {
                var token = TestContext.Current.CancellationToken;
                rig.HomePath = Path.Combine(rig.Root, "home.json"); var home = rig.Home = new FileHomeCoreStateStore(rig.HomePath);
                var profiles = rig.Profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
                var services = new ServiceCollection(); services.AddSingleton<IHomeCoreStateStore>(home); services.AddSingleton(profiles);
                services.AddSingleton<IAuthenticatedResourceActorSource>(profiles); services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
                services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>(); services.AddSingleton<HomeLocalStoreOwnership>();
                services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>(); services.AddSingleton<ResourceAuthorizationService>();
                services.AddFilesNativeHost(); rig.Graph = services.BuildServiceProvider();
                var chosen = Path.Combine(rig.Root, "chosen"); Directory.CreateDirectory(chosen);
                await rig.Graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen, rig.Graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
                var authority = rig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>(); rig.Workspace = (await authority.GetCurrentAsync(token))!;
                rig.Folder = (await rig.Workspace.Provider.GetForOriginalStoreAsync(rig.Workspace.Configuration.StoreId, rig.Workspace.Configuration.AppFolders["write"], token)).Value!;
                rig.DrivePath = Path.Combine(chosen, ".9to1-files", "drive.json");
                rig.Capture = new(Path.Combine(chosen, "ExistingProject")); rig.Captures = new(rig.Capture);
                rig.Journal = new(home, profiles, rig.Captures); rig.Owner = new(authority, rig.Captures, () => rig.Journal); return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal Task<HomeDeveloperProjectSetupJournal.Prepared> Prepare(FilesDeveloperOriginalSetupScopeSource.Destination destination, CancellationToken token)
            => Journal.PrepareExistingOriginalAsync(Guid.NewGuid(), destination.OriginalActor, destination.OriginalStoreId,
                destination.OriginalConfigurationDigest, destination.OriginalFolderId, destination.OriginalFolderRevision,
                Capture, Guid.NewGuid(), 0, "ExistingProject", token);
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            if (FolderProducer is not null) try { await FolderProducer.CloseAndDrainOriginalFolderSetupsAsync(); } catch (Exception error) { if (!ExpectScopeFault) errors.Add(error); }
            if (Owner is not null) try { await Owner.CloseAndDrainOriginalSetupScopesAsync(); } catch (Exception error) { if (!ExpectScopeFault) errors.Add(error); }
            if (Journal is not null) try { await Journal.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (Graph is not null) try { await Graph.DisposeAsync(); } catch (Exception error) { errors.Add(error); }
            try { Directory.Delete(Root, true); } catch (Exception error) { errors.Add(error); }
            if (errors.Count != 0) throw new AggregateException("Actual destination fixture originals/cleanup failed.", errors);
        }
    }
}
