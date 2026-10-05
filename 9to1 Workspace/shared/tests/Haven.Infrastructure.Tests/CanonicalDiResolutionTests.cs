using Haven.Application;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class CanonicalDiResolutionTests08
{
    [Fact]
    public Task Canonical_runtime_aliases_resolve_same_original_without_eager_issuer_cycle()
        => InspectAsync(async provider =>
        {
            // The real canonical database bootstrap precedes repository/issued-admission reads.
            // This invokes the same restore/maintenance/additive migration owner as production.
            var originalBootstrap = provider.GetRequiredService<IAppDatabase>().InitializeAsync(CancellationToken.None);
            try { await originalBootstrap; }
            catch (Exception error)
            {
                var causes = new List<Exception>();
                AddTask(causes, error, originalBootstrap);
                if (causes.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(causes[0]).Throw();
                throw new AggregateException(causes);
            }
            var coordinator = provider.GetRequiredService<TaskExecutionCoordinator>();
            Assert.Same(coordinator, provider.GetRequiredService<TaskExecutionCoordinator>());
            var frames = provider.GetRequiredService<TaskRunOriginalFrameOwner>();
            Assert.Same(frames, provider.GetRequiredService<ITaskRunOriginalFrameOwner>());
            Assert.Same(frames, provider.GetRequiredService<ITaskRunRuntimeSettlement>());
            Assert.Same(frames, provider.GetRequiredService<ITaskRunProviderFailureSettlement>());
            Assert.Same(frames, provider.GetRequiredService<ITaskRunOriginalAttemptRetirement>());
            Assert.Same(provider.GetRequiredService<WorkspaceTaskRunToolActionOwner>(), provider.GetRequiredService<ITaskRunToolActionOwner>());
            Assert.Same(provider.GetRequiredService<CheckpointService>(), provider.GetRequiredService<ICheckpointExecutionObservationSource>());
            var actualLookup = coordinator.TryGetIssuedAttemptAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
            Assert.Null(await actualLookup);
        });

    [Fact]
    public Task Canonical_context_and_paid_permission_aliases_use_actual_same_services()
        => InspectAsync(provider =>
        {
            var context = provider.GetRequiredService<TaskRunConfiguredCloudAdmissionSource>();
            Assert.Same(context, provider.GetRequiredService<ITaskRunCloudAdmissionSource>());
            Assert.Same(context, provider.GetRequiredService<ITaskRunProviderContextCapture>());
            Assert.Same(context, provider.GetRequiredService<ITaskRunProviderContextAuthority>());
            Assert.Same(provider.GetRequiredService<TaskRunCentralCloudUsePermissionSource>(), provider.GetRequiredService<ITaskRunCloudUsePermissionSource>());
            var authority = provider.GetRequiredService<TaskRunPermissionAuthority>();
            Assert.Same(authority, provider.GetRequiredService<ITaskRunAdmissionAuthority>());
            Assert.Same(authority, provider.GetRequiredService<ITaskRunCommandAuthority>());
            Assert.Same(authority, provider.GetRequiredService<ITaskRunSelectedRouteCapture>());
            return Task.CompletedTask;
        });

    private static async Task InspectAsync(Func<ServiceProvider, Task> inspect)
    {
        var root = Path.Combine("/tmp/astra-typed-producer-actual08", "di-private", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var services = new ServiceCollection();
        services.AddHavenInfrastructure();
        // Only explicit task-private filesystem paths; this supplies no actor, provider success or grant.
        services.AddSingleton<IAppPaths>(new Paths(root));
        var provider = services.BuildServiceProvider();
        Task? actualBody = null, actualClose = null;
        var failures = new List<Exception>();
        try { actualBody = inspect(provider); await actualBody; }
        catch (Exception error) { AddTask(failures, error, actualBody); }
        finally
        {
            try { actualClose = provider.DisposeAsync().AsTask(); }
            catch (Exception error) { Add(failures, error); }
            if (actualClose is not null)
                try { await actualClose; } catch (Exception error) { AddTask(failures, error, actualClose); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(failures);
    }
    private static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error); }
    private static void AddTask(List<Exception> failures, Exception error, Task? original)
    {
        Add(failures, error);
        if (original?.Exception is { } group) foreach (var cause in group.InnerExceptions) Add(failures, cause);
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "state.json");
    }
}
