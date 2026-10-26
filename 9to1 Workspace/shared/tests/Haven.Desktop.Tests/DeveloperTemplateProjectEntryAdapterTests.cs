using System.Runtime.CompilerServices;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Dev;
using Haven.Infrastructure;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class DeveloperTemplateProjectEntryAdapterTests
{
    [LinuxFact]
    public async Task Empty_target_capture_is_admitted_but_real_file_or_subdirectory_capture_is_refused()
    {
        var root = Path.Combine(Path.GetTempPath(), "template-empty-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
        var graph = BuildGraph(home);
        var chosen = Path.Combine(root, "files"); Directory.CreateDirectory(chosen);
        await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
            graph.GetRequiredService<HomeLocalStoreOwnership>(), CancellationToken.None);
        var workspace = await graph.GetRequiredService<NativeFilesWorkspaceAuthority>().GetCurrentAsync(CancellationToken.None);
        Assert.NotNull(workspace);
        var target = Path.Combine(chosen, "Empty Template Target"); Directory.CreateDirectory(target);
        var reads = new TestReads();
        var physical = new WorkspaceToolService().CreateOriginalDeveloperCaptureSource(reads,
            () => reads.Selections);
        var selections = new FilesDeveloperOriginalSourceSelection(
            graph.GetRequiredService<NativeFilesWorkspaceAuthority>(), physical, reads);
        reads.Selections = selections;

        try
        {
            var empty = await CaptureAsync(selections, reads, workspace.Configuration.StoreId, target);
            Assert.Empty(empty.OriginalFiles);
            Assert.Empty(empty.OriginalFolderPaths);
            DeveloperTemplateProjectEntryAdapter.DemandEmptyTarget(empty, CancellationToken.None);

            // Both entry types are created on disk and then observed by a fresh real kernel/Files
            // capture; no caller-supplied or forged capture payload is involved.
            File.WriteAllText(Path.Combine(target, "existing.txt"), "keep me");
            var withFile = await CaptureAsync(selections, reads, workspace.Configuration.StoreId, target);
            Assert.Contains(withFile.OriginalFiles, file => file.RelativePath == "existing.txt");
            var refusal = Assert.Throws<InvalidOperationException>(() =>
                DeveloperTemplateProjectEntryAdapter.DemandEmptyTarget(withFile, CancellationToken.None));
            Assert.Contains("empty project folder", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("keep me", File.ReadAllText(Path.Combine(target, "existing.txt")));

            File.Delete(Path.Combine(target, "existing.txt"));
            Directory.CreateDirectory(Path.Combine(target, "nested"));
            var withDirectory = await CaptureAsync(selections, reads, workspace.Configuration.StoreId, target);
            Assert.Contains("nested", withDirectory.OriginalFolderPaths);
            Assert.Throws<InvalidOperationException>(() =>
                DeveloperTemplateProjectEntryAdapter.DemandEmptyTarget(withDirectory, CancellationToken.None));
        }
        finally
        {
            await selections.CloseAndDrainOriginalSelectionsAsync();
            await physical.CloseAndDrainOriginalCapturesAsync();
            await graph.DisposeAsync();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Registration_RejectsForgedPayloadAndFolderNameThatDiffersFromReviewedProject()
    {
        var provider = new DeveloperCuiProjectTemplateProvider();
        var option = Assert.Single((await new DeveloperProjectCatalog([provider]).SearchAsync(new())).Value!);
        var materializer = new DeveloperProjectTemplateMaterializer([provider]);
        var prepared = await materializer.PrepareAsync(option, "Reviewed Project");
        Assert.True(prepared.Succeeded);
        var issued = prepared.Value!;
        var adapter = new DeveloperTemplateProjectEntryAdapter(null!, materializer);

        var forged = issued with { ProjectName = "Other Project" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.RegisterExistingFolderAsync(
            Guid.NewGuid(), Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "Other Project"), forged, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.RegisterExistingFolderAsync(
            Guid.NewGuid(), Guid.NewGuid(), Path.Combine(Path.GetTempPath(), "Other Project"), issued, CancellationToken.None));
    }

    private static ServiceProvider BuildGraph(FileHomeCoreStateStore home)
    {
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var services = new ServiceCollection();
        services.AddSingleton<IHomeCoreStateStore>(home);
        services.AddSingleton(profiles);
        services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
        services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
        services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
        services.AddSingleton<HomeLocalStoreOwnership>();
        services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
        services.AddSingleton<ResourceAuthorizationService>();
        services.AddFilesNativeHost();
        return services.BuildServiceProvider();
    }

    private static async Task<IDeveloperProjectOriginalExistingSourceCapture> CaptureAsync(
        FilesDeveloperOriginalSourceSelection selections, TestReads reads, Guid storeId, string target)
    {
        var selected = await selections.SelectOriginalExistingProjectAsync(storeId, target, CancellationToken.None);
        var read = await reads.AcquireOriginalAsync(selected, CancellationToken.None);
        try { return await selections.CaptureOriginalAsync(selected, read, CancellationToken.None); }
        finally { await read.DisposeAsync(); }
    }

    private sealed class TestReads : IDeveloperProjectOriginalReadAdmissionSource, IDeveloperProjectOriginalReadAdmissionJoinGuard
    {
        internal FilesDeveloperOriginalSourceSelection Selections { get; set; } = null!;
        public Task<IDeveloperProjectOriginalReadAdmission> AcquireOriginalAsync(
            IDeveloperProjectOriginalReadSelection selection, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!Selections.IsIssuedOriginal(selection)) throw new UnauthorizedAccessException();
            return Task.FromResult<IDeveloperProjectOriginalReadAdmission>(new Read(selection));
        }
        public Task ValidateOriginalAsync(IDeveloperProjectOriginalReadSelection selection,
            IDeveloperProjectOriginalReadAdmission admission, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (admission is not Read read || !ReferenceEquals(read.Selection, selection) || !Selections.IsIssuedOriginal(selection))
                throw new UnauthorizedAccessException();
            return Task.CompletedTask;
        }
        public void DemandExternalOriginalReadAdmissionJoin() { }
        private sealed class Read(IDeveloperProjectOriginalReadSelection selection) : IDeveloperProjectOriginalReadAdmission
        {
            internal IDeveloperProjectOriginalReadSelection Selection => selection;
            public Task RevalidateOriginalAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
            public T RunOriginalRead<T>(Func<T> source, CancellationToken token) { token.ThrowIfCancellationRequested(); return source(); }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class LinuxFactAttribute : FactAttribute
    {
        public LinuxFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
            : base(sourceFilePath, sourceLineNumber)
        { if (!OperatingSystem.IsLinux()) Skip = "The actual retained Files capture owner is currently supported on Linux; Windows remains NOT RUN."; }
    }
}
