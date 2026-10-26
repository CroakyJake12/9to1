using System.Runtime.CompilerServices;
using System.Reflection;
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
    public async Task Real_template_entry_preflights_before_effects_and_manual_entry_cannot_be_recovered_as_template()
    {
        var rigType = typeof(FilesDeveloperOriginalSourcePublicationTests)
            .GetNestedType("Rig", BindingFlags.NonPublic)!;
        var create = rigType.GetMethod("CreateAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        var creation = (Task)create.Invoke(null, [null, true, true, true, null, true])!;
        await creation;
        var rig = creation.GetType().GetProperty("Result")!.GetValue(creation)!;
        var graph = (ServiceProvider)Get(rig, "Graph");
        var projectRoot = (string)rigType.GetField("ProjectRoot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig)!;
        var filesRoot = Directory.GetParent(projectRoot)!.FullName;
        var authority = graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
        var workspace = (await authority.GetCurrentAsync(CancellationToken.None))!;
        var savedStore = (FileDeveloperWorkspaceStore)Get(rig, "SavedStore");
        var manual = NewManualAdapter(graph, savedStore);
        var provider = new DeveloperCuiProjectTemplateProvider();
        var catalog = new DeveloperProjectCatalog([provider]);
        var materializer = new DeveloperProjectTemplateMaterializer([provider]);

        try
        {
            var manualRoot = CreateTarget(filesRoot, "manual-nonempty", "manual data");
            var manualOperation = Keep(rig, manual.RegisterExistingFolderAsync(workspace.Configuration.StoreId,
                workspace.Configuration.AppFolders["write"], manualRoot, CancellationToken.None));
            await Decide(rig, manualOperation, HomeDeveloperProjectReadAdmissionSource.ReadAction);
            await Decide(rig, manualOperation, HomeDeveloperProjectSetupPermissionSource.SetupAction);
            await manualOperation;
            var manualSetupId = Assert.IsType<Guid>(manual.RetainedSetupId);

            // The same manual owner serves both Build entry modes. A previous successful manual
            // registration must not satisfy a later template request from retained identity alone.
            var manualSetupRecordsBefore = await SetupRecordCount(graph);
            var manualMaterialization = await PrepareTemplate(catalog, materializer, Path.GetFileName(manualRoot));
            var templateOnManual = new DeveloperTemplateProjectEntryAdapter(manual, materializer);
            var mismatch = Keep(rig, templateOnManual.RegisterExistingFolderAsync(workspace.Configuration.StoreId,
                workspace.Configuration.AppFolders["write"], manualRoot, manualMaterialization, CancellationToken.None));
            await Decide(rig, mismatch, HomeDeveloperProjectReadAdmissionSource.ReadAction);
            Expect(rig, await Assert.ThrowsAsync<InvalidOperationException>(() => mismatch));
            Assert.Equal(manualSetupId, manual.RetainedSetupId);
            Assert.Equal(manualSetupRecordsBefore, await SetupRecordCount(graph));

            // A fresh template adapter gets no retained attempt from a generic manual project.
            // The actual READ/capture runs, then refusal precedes journal, Files, and Dev writes.
            var rejectedRoot = CreateTarget(filesRoot, "template-nonempty", "preserve bytes");
            var freshManual = NewManualAdapter(graph, savedStore);
            var freshTemplate = new DeveloperTemplateProjectEntryAdapter(freshManual, materializer);
            var rejectedMaterialization = await PrepareTemplate(catalog, materializer, Path.GetFileName(rejectedRoot));
            var drivePath = Path.Combine(filesRoot, ".9to1-files", "drive.json");
            var driveBefore = File.ReadAllBytes(drivePath);
            var devBefore = Snapshot(savedStore.OriginalWorkspaceMetadataDirectory);
            var setupRecordsBefore = await SetupRecordCount(graph);
            var rejected = Keep(rig, freshTemplate.RegisterExistingFolderAsync(workspace.Configuration.StoreId,
                workspace.Configuration.AppFolders["write"], rejectedRoot, rejectedMaterialization, CancellationToken.None));
            var concurrentRejected = Keep(rig, freshTemplate.RegisterExistingFolderAsync(workspace.Configuration.StoreId,
                workspace.Configuration.AppFolders["write"], rejectedRoot, rejectedMaterialization, CancellationToken.None));
            using var cancelledWaiter = new CancellationTokenSource();
            var cancelledAttempt = freshTemplate.RegisterExistingFolderAsync(workspace.Configuration.StoreId,
                workspace.Configuration.AppFolders["write"], rejectedRoot, rejectedMaterialization, cancelledWaiter.Token);
            cancelledWaiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledAttempt);
            await Decide(rig, rejected, HomeDeveloperProjectReadAdmissionSource.ReadAction);
            Expect(rig, await Assert.ThrowsAsync<InvalidOperationException>(() => rejected));
            await Decide(rig, concurrentRejected, HomeDeveloperProjectReadAdmissionSource.ReadAction);
            Expect(rig, await Assert.ThrowsAsync<InvalidOperationException>(() => concurrentRejected));
            Assert.Null(freshManual.RetainedSetupId);
            Assert.Null(freshManual.RetainedWorkspaceId);
            Assert.Equal(driveBefore, File.ReadAllBytes(drivePath));
            Assert.Equal(devBefore, Snapshot(savedStore.OriginalWorkspaceMetadataDirectory));
            Assert.Equal(setupRecordsBefore, await SetupRecordCount(graph));
            Assert.Equal("preserve bytes", await File.ReadAllTextAsync(Path.Combine(rejectedRoot, "existing.txt")));

            var emptyRoot = Path.Combine(filesRoot, "template-empty"); Directory.CreateDirectory(emptyRoot);
            var emptyMaterialization = await PrepareTemplate(catalog, materializer, Path.GetFileName(emptyRoot));
            var accepted = freshTemplate.RegisterExistingFolderAsync(workspace.Configuration.StoreId,
                workspace.Configuration.AppFolders["write"], emptyRoot, emptyMaterialization, CancellationToken.None);
            Keep(rig, accepted);
            var concurrentAccepted = freshTemplate.RegisterExistingFolderAsync(workspace.Configuration.StoreId,
                workspace.Configuration.AppFolders["write"], emptyRoot, emptyMaterialization, CancellationToken.None);
            Keep(rig, concurrentAccepted);
            await Decide(rig, accepted, HomeDeveloperProjectReadAdmissionSource.ReadAction);
            await Decide(rig, accepted, HomeDeveloperProjectSetupPermissionSource.SetupAction);
            var acceptedReference = await accepted;
            Assert.NotNull(freshManual.RetainedSetupId);
            var setupRecordsAfterFirst = await SetupRecordCount(graph);
            await Decide(rig, concurrentAccepted, HomeDeveloperProjectReadAdmissionSource.ReadAction);
            var recoveredReference = await concurrentAccepted;
            Assert.Equal(acceptedReference.WorkspaceId, recoveredReference.WorkspaceId);
            Assert.Equal(acceptedReference.ProjectId, recoveredReference.ProjectId);
            Assert.Equal(setupRecordsAfterFirst, await SetupRecordCount(graph));
        }
        finally
        {
            var close = rig.GetType().GetMethod("DisposeAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(rig, null)!;
            if (close is ValueTask valueTask) await valueTask;
            else await (Task)close;
        }
    }

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

    private static DeveloperManualProjectEntryAdapter NewManualAdapter(IServiceProvider graph, IDeveloperWorkspaceStore store)
        => new(graph.GetRequiredService<FilesDeveloperOriginalSourceSelection>(),
            graph.GetRequiredService<IDeveloperProjectOriginalReadAdmissionSource>(),
            graph.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>(),
            graph.GetRequiredService<HomeDeveloperProjectSetupJournal>(),
            graph.GetRequiredService<IDeveloperProjectOriginalSetupPermissionSource>(),
            graph.GetRequiredService<FilesDeveloperOriginalFolderSetupProducer>(), store);

    private static async Task<DeveloperProjectTemplateMaterialization> PrepareTemplate(
        DeveloperProjectCatalog catalog, DeveloperProjectTemplateMaterializer materializer, string projectName)
    {
        var option = Assert.Single((await catalog.SearchAsync(new())).Value!);
        var result = await materializer.PrepareAsync(option, projectName);
        Assert.True(result.Succeeded);
        return result.Value!;
    }

    private static string CreateTarget(string filesRoot, string name, string content)
    {
        var path = Path.Combine(filesRoot, name); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "existing.txt"), content);
        return path;
    }

    private static object Get(object instance, string property)
        => instance.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static Task Keep(object rig, Task operation)
    {
        var method = rig.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "Keep" && !candidate.IsGenericMethod &&
                candidate.GetParameters().Length == 1 && candidate.GetParameters()[0].ParameterType == typeof(Task));
        return (Task)method.Invoke(rig, [operation])!;
    }

    private static void Expect(object rig, Exception error)
    {
        var method = rig.GetType().GetMethod("Expect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        method.Invoke(rig, [error]);
    }

    private static async Task Decide(object rig, Task operation, string action)
    {
        var method = rig.GetType().GetMethod("DecideNextAsync", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        var decision = (Task)method.Invoke(rig, [operation, action, HomeApprovalChoice.Accept])!;
        await decision;
    }

    private static IReadOnlyDictionary<string, string> Snapshot(string directory)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
                path => Path.GetRelativePath(directory, path), path => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))))
            : new Dictionary<string, string>();

    private static async Task<int> SetupRecordCount(IServiceProvider graph)
    {
        var read = await graph.GetRequiredService<IHomeCoreStateStore>().ReadAsync(CancellationToken.None);
        Assert.True(read.IsSuccess);
        return read.State!.Records.Count(record => record.RecordType == "home.dev-project-setup");
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
