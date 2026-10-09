using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

// Genuine configured NativeHost/kernel/Home component identity. Rig READ/Setup issuers
// are controlled test owners; these controls issue no manual approval or setup ACK.
public sealed partial class FilesDeveloperOriginalDirectorySetupProducerTests
{
    [LinuxDirectoryFact]
    public async Task Local_setup_composition_is_lazy_and_resolves_one_actual_kernel_and_files_owner()
    {
        Rig? rig = null; ServiceProvider? provider = null; var errors = new List<Exception>();
        var physicalCalls = 0; var journalCalls = 0; var readCalls = 0; var permissionCalls = 0; var storeCalls = 0;
        try
        {
            rig = await Rig.Create(true, true); var sameRig = rig;
            var services = new ServiceCollection();
            services.AddSingleton(sameRig.Graph.GetRequiredService<NativeFilesWorkspaceAuthority>());
            var reads = new Reads(); FilesDeveloperOriginalSetupScopeSource? selectedScope = null;
            var resolver = new FilesDeveloperOriginalSetupDestinationResolver(() => selectedScope!);
            services.AddFilesOriginalDeveloperSetups(resolver,
                _ => { physicalCalls++; return sameRig.Kernel; },
                _ => { journalCalls++; return sameRig.Journal; },
                _ => { readCalls++; return reads; },
                _ => { permissionCalls++; return sameRig.Setups; },
                _ => { storeCalls++; return sameRig.WorkspaceStore; });
            Assert.Equal(0, physicalCalls); Assert.Equal(0, readCalls); Assert.Equal(0, journalCalls);
            Assert.Equal(0, permissionCalls); Assert.Equal(0, storeCalls);
            Assert.Throws<InvalidOperationException>(() =>
            {
                services.AddFilesOriginalDeveloperSetups(resolver, _ => throw new Exception("duplicate physical factory ran"),
                    _ => throw new Exception("duplicate journal factory ran"), _ => throw new Exception("duplicate read factory ran"),
                    _ => throw new Exception("duplicate permission factory ran"), _ => throw new Exception("duplicate store factory ran"));
            });
            provider = services.BuildServiceProvider();
            var selection = provider.GetRequiredService<FilesDeveloperOriginalSourceSelection>(); reads.Selections = selection;
            selectedScope = provider.GetRequiredService<FilesDeveloperOriginalSetupScopeSource>();
            var effector = provider.GetRequiredService<FilesDeveloperOriginalFolderSetupProducer>();
            Assert.Same(sameRig.Kernel, provider.GetRequiredService<IDeveloperProjectOriginalPhysicalCaptureSource>());
            Assert.Same(sameRig.Kernel, provider.GetRequiredService<IDeveloperProjectOriginalDirectoryObservationSource>());
            Assert.Same(sameRig.Kernel, provider.GetRequiredService<IDeveloperProjectOriginalDirectoryRegistrationSource>());
            Assert.Same(sameRig.Kernel, provider.GetRequiredService<IDeveloperProjectOriginalFileRegistrationSource>());
            Assert.Same(sameRig.Kernel, provider.GetRequiredService<IDeveloperProjectOriginalWorkspaceMetadataSource>());
            Assert.Same(selection, provider.GetRequiredService<IDeveloperProjectOriginalPhysicalReadSelectionSource>());
            Assert.Same(selection, provider.GetRequiredService<IDeveloperProjectOriginalCaptureAuthority>());
            Assert.Same(selectedScope, provider.GetRequiredService<IDeveloperProjectOriginalSetupScopeSource>());
            Assert.Same(effector, provider.GetRequiredService<IDeveloperProjectOriginalSetupStepOutcomeSource>());
            Assert.Same(resolver, provider.GetRequiredService<FilesDeveloperOriginalSetupDestinationResolver>());
            Assert.Same(resolver, Assert.Single(provider.GetServices<ICanonicalResourceAccessResolver>()));
            Assert.Equal(1, physicalCalls); Assert.Equal(1, readCalls); Assert.Equal(0, journalCalls);
            Assert.Equal(0, permissionCalls); Assert.Equal(0, storeCalls);
        }
        catch (Exception error) { CompositionCauses(errors, error); }
        finally
        {
            Task? providerClose = null; Task? rigClose = null;
            try { if (provider is not null) providerClose = provider.DisposeAsync().AsTask(); }
            catch (Exception error) { CompositionCauses(errors, error); }
            if (providerClose is not null) try { await providerClose.ConfigureAwait(false); }
            catch (Exception error) { CompositionTaskCauses(errors, providerClose, error); }
            try { if (rig is not null) rigClose = rig.DisposeAsync().AsTask(); }
            catch (Exception error) { CompositionCauses(errors, error); }
            if (rigClose is not null) try { await rigClose.ConfigureAwait(false); }
            catch (Exception error) { CompositionTaskCauses(errors, rigClose, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual local setup aliases or independent cleanup failed.", errors);
    }

    [LinuxDirectoryFact]
    public async Task Lazy_destination_resolver_uses_same_privately_bound_actual_destination_and_refuses_changed_revision()
    {
        Rig? rig = null; var originals = new List<Task>(); var errors = new List<Exception>();
        try
        {
            rig = await Rig.Create(true, true); var sameRig = rig; var token = TestContext.Current.CancellationToken;
            var calls = 0; var resolver = new FilesDeveloperOriginalSetupDestinationResolver(() => { calls++; return sameRig.Scopes; });
            Assert.Equal(0, calls); Assert.Equal("dev.project.destination", resolver.ResourceKind);
            var scope = Assert.Single(rig.Scopes.GetOriginalSetupScopes(rig.Prepared.Intent, rig.Capture));
            var actual = resolver.EvaluateAsync(rig.Prepared.Intent.OriginalActor, "dev.project.setup.commit", scope, token).AsTask(); originals.Add(actual);
            var result = await actual;
            Assert.True(result.Allowed); Assert.Equal(scope.Revision, result.ResourceRevision); Assert.Equal(1, calls);
            var changed = resolver.EvaluateAsync(rig.Prepared.Intent.OriginalActor, "dev.project.setup.commit", scope with { Revision = scope.Revision + ":copied" }, token).AsTask(); originals.Add(changed);
            Assert.False((await changed).Allowed);
            var wrongAction = resolver.EvaluateAsync(rig.Prepared.Intent.OriginalActor, "dev.workspace.execute", scope, token).AsTask(); originals.Add(wrongAction);
            Assert.False((await wrongAction).Allowed); Assert.Equal(3, calls);
        }
        catch (Exception error) { CompositionCauses(errors, error); }
        finally
        {
            foreach (var original in originals) try { await original.ConfigureAwait(false); }
            catch (Exception error) { CompositionTaskCauses(errors, original, error); }
            Task? close = null;
            try { if (rig is not null) close = rig.DisposeAsync().AsTask(); }
            catch (Exception error) { CompositionCauses(errors, error); }
            if (close is not null) try { await close.ConfigureAwait(false); }
            catch (Exception error) { CompositionTaskCauses(errors, close, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual destination forwarder or independent cleanup failed.", errors);
    }
    private static void CompositionTaskCauses(List<Exception> errors, Task task, Exception observed)
    { foreach (var cause in task.Exception?.InnerExceptions ?? new[] { observed }.AsEnumerable()) CompositionCauses(errors, cause); }
    private static void CompositionCauses(List<Exception> errors, Exception error)
    {
        if (error is AggregateException { InnerExceptions.Count: > 0 } group)
        { foreach (var cause in group.InnerExceptions) CompositionCauses(errors, cause); }
        else if (!errors.Any(cause => ReferenceEquals(cause, error))) errors.Add(error);
    }
}
