using System.Diagnostics;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class OriginalNativeColdProjectCompositionTests
{
    // These controls use actual local Home, configured Files, SQLite and native READ owners.
    // Their Task is admitted and suspended by the actual coordinator. They do not certify a
    // Chat cold-capture producer, journal publication, Windows installation or Dev execution.
    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Same_closed_input_can_be_captured_again_only_after_a_distinct_manual_Home_read()
        => WithClosedInput(async rig =>
        {
            var original = await rig.PrepareAsync();
            var first = await rig.CaptureAsync(original);
            Assert.False(rig.Source.IsIssuedOriginalProjectInput(original));
            Assert.True(rig.Source.TryObserveClosedOwnedOriginalProjectInput(original, out var originalClose));
            await originalClose!;
            Assert.True(rig.Source.IsClosedOwnedOriginalProjectInput(original, originalClose!));
            Assert.False(rig.Source.IsClosedOwnedOriginalProjectInput(original, Task.CompletedTask));

            var secondCapture = rig.StartCapture(original);
            await rig.AcceptNextReadAsync(secondCapture);
            var second = await secondCapture;
            Assert.Equal(first, second);
            Assert.True(rig.Source.TryObserveClosedOwnedOriginalProjectInput(original, out var unchangedClose));
            Assert.Same(originalClose, unchangedClose);
            Assert.False(rig.Source.IsIssuedOriginalProjectInput(original));
            Assert.Equal(2, rig.ApprovedRequestIds.Count);
            Assert.Equal(2, rig.ApprovedRequestIds.Distinct(StringComparer.Ordinal).Count());
            Assert.Empty((await rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).Grants);
            // Both actual inputs are already closed. Parent retirement joins their
            // cached once-only stop receipts rather than canceling disposed sources.
            rig.Source.RequestOriginalRetirement();
            var retirement = rig.Source.OriginalRetirementRequestTask;
            Assert.NotNull(retirement); rig.Retain(retirement!); await retirement!;
            rig.Source.RequestOriginalRetirement();
            Assert.Same(retirement, rig.Source.OriginalRetirementRequestTask);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Closed_input_never_adopts_changed_saved_project_bytes_after_a_new_approved_read()
        => WithClosedInput(async rig =>
        {
            var original = await rig.PrepareAsync();
            var first = await rig.CaptureAsync(original);
            var saved = await rig.Store.SaveAsync(rig.Workspace with
            {
                Projects = [rig.Workspace.Projects[0] with { Name = "changed actual saved project" }]
            }, rig.Workspace.Revision, rig.Token);
            Assert.True(saved.Succeeded);
            var changedBytes = await File.ReadAllBytesAsync(rig.WorkspaceDocument, rig.Token);
            var next = rig.StartCapture(original);
            await rig.AcceptNextReadAsync(next);
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => next);
            rig.KeepExpected(next, failure);
            Assert.True(next.IsFaulted);
            Assert.False(next.IsCanceled);
            Assert.Equal(changedBytes, await File.ReadAllBytesAsync(rig.WorkspaceDocument, rig.Token));
            Assert.True(rig.Source.TryObserveClosedOwnedOriginalProjectInput(original, out var healthyOriginalClose));
            await healthyOriginalClose!;
            Assert.True(rig.Source.IsClosedOwnedOriginalProjectInput(original, healthyOriginalClose!));
            Assert.Equal(first, original.OriginalIdentity);
            Assert.Equal(2, rig.ApprovedRequestIds.Count);
        });

    [FilesDeveloperOriginalDirectorySetupProducerTests.LinuxDirectoryFact]
    public Task Changed_actual_Home_profile_refuses_closed_input_before_another_review_or_native_read()
        => WithClosedInput(async rig =>
        {
            var original = await rig.PrepareAsync();
            await rig.CaptureAsync(original);
            var read = await rig.Home.StateStore.ReadAsync(rig.Token);
            Assert.True(read.IsSuccess);
            var originalRecord = Assert.Single(read.State!.Records, record => record.RecordId == "home.local-profile");
            var actualProfile = originalRecord.Payload.Deserialize<HomeLocalProfile>()!;
            var changed = originalRecord with
            {
                Revision = checked(originalRecord.Revision + 1),
                Payload = JsonSerializer.SerializeToElement(actualProfile with { ProfileId = Guid.NewGuid() })
            };
            Assert.True((await rig.Home.StateStore.WriteAsync(changed, originalRecord.Revision, rig.Token)).IsSuccess);
            try
            {
                var next = rig.StartCapture(original);
                var failure = await Assert.ThrowsAnyAsync<Exception>(() => next);
                rig.KeepExpected(next, failure);
                Assert.True(next.IsFaulted);
                Assert.False(next.IsCanceled);
                Assert.Single(rig.ApprovedRequestIds);
                Assert.DoesNotContain((await rig.Home.Permissions.GetSnapshotAsync(cancellationToken: rig.Token)).PendingRequests,
                    pending => pending.Scope.ActionName == HomeColdProjectReadReconciliation.ReadAction);
            }
            finally
            {
                // Restore through the actual store CAS before independent owner cleanup.
                var restore = originalRecord with { Revision = checked(changed.Revision + 1) };
                Assert.True((await rig.Home.StateStore.WriteAsync(restore, changed.Revision, CancellationToken.None)).IsSuccess);
            }
        });

    private static Task WithClosedInput(Func<ClosedInputRig, Task> body)
    {
        var preserve = false;
        return WithGraph("1", async (services, registration, paths, captureSource) =>
        {
            services.AddHavenOwnedNativeColdProjectResources(registration.OriginalHome);
            ServiceProvider? provider = null; ClosedInputRig? rig = null;
            Exception? primary = null; var cleanup = new List<Exception>();
            try
            {
                provider = services.BuildServiceProvider();
                rig = new(provider, registration.OriginalHome, paths, captureSource);
                await rig.InitializeAsync();
                await body(rig);
            }
            catch (Exception cause) { primary = cause; }
            finally
            {
                if (rig is not null)
                {
                    await rig.JoinOriginalsAsync(cleanup);
                    preserve |= rig.HasUnhealthyOriginalClose;
                }
                Task? providerClose = null;
                try { if (provider is not null) providerClose = provider.DisposeAsync().AsTask(); }
                catch (Exception cause) { cleanup.Add(cause); preserve = true; }
                if (providerClose is not null)
                    try { await providerClose; }
                    catch (Exception cause)
                    {
                        // DI joins the SAME failed source again. Classify only already
                        // observed exact expected occurrences; every unknown sibling stays
                        // a fixture failure and the actual source/storage remain retained.
                        if (rig is null) cleanup.Add(providerClose.Exception ?? cause);
                        else rig.KeepUnexpected(providerClose, cause, cleanup);
                        preserve = true;
                    }
                preserve |= primary is not null || cleanup.Count != 0;
            }
            Throw(primary, cleanup);
        }, retainStorageOnFailure: () => preserve);
    }

    private sealed class ClosedInputRig(ServiceProvider provider, HomeLocalDomainComposition home,
        Paths paths, Action<HomeColdProjectReadReconciliation> captureSource)
    {
        private readonly object _gate = new();
        private readonly List<Task> _raw = [];
        private readonly List<Exception> _expected = [];
        private readonly CancellationTokenSource _active = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        private Task? _sourceClose, _taskClose;
        private TaskExecutionCoordinator? _tasks;
        private ITaskRunColdOriginalProjectInput? _input;
        internal HomeLocalDomainComposition Home => home;
        internal HomeColdProjectReadReconciliation Source = null!;
        internal FileDeveloperWorkspaceStore Store = null!;
        internal DeveloperWorkspace Workspace = null!;
        internal string WorkspaceDocument = null!;
        internal Conversation Conversation = null!;
        internal ContainerDefinition Container = null!;
        internal TaskExecutionSnapshot ActualSuspendedTask = null!;
        internal TaskRunColdChatInput Exact = null!;
        internal List<string> ApprovedRequestIds { get; } = [];
        internal CancellationToken Token => _active.Token;
        internal bool HasUnhealthyOriginalClose { get; private set; }

        internal async Task InitializeAsync()
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("This original Linux fixture requires Linux.");
            _active.CancelAfter(TimeSpan.FromSeconds(30));
            Source = provider.GetRequiredService<HomeColdProjectReadReconciliation>(); captureSource(Source);
            var database = provider.GetRequiredService<SqliteDatabase>();
            await database.InitializeAsync(Token);
            // Only the actual newly created fixture files are made private; existing stores
            // are never adopted. The real native recovery owner still checks their handles.
            foreach (var file in new[] { paths.DatabasePath, paths.DatabasePath + "-wal", paths.DatabasePath + "-shm" })
                if (File.Exists(file)) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var filesRoot = Path.Combine(paths.DataDirectory, "chosen-files"); Directory.CreateDirectory(filesRoot);
            var workspace = await provider.GetRequiredService<NativeFilesWorkspaceService>()
                .ConfigureNewAsync(filesRoot, Home.LocalStoreOwnership, Token);
            var projectId = Guid.NewGuid(); var rootId = Guid.NewGuid();
            var projectPath = Path.Combine(filesRoot, "actual-existing-project"); Directory.CreateDirectory(projectPath);
            File.SetUnixFileMode(projectPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var folder = HostedItemId.New(); var now = DateTimeOffset.UtcNow;
            var create = new FilesOperation(new(Guid.NewGuid()), workspace.Actor.ActorId, folder, null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await workspace.Provider.MutateAsync(create, "actual-existing-project", Token)).IsSuccess);
            Assert.True((await workspace.Directories.RegisterProfileAsync(Guid.Parse(workspace.Actor.ProfileId), folder,
                "dev.project." + projectId.ToString("N"), projectPath, Token)).IsSuccess);
            var draft = DeveloperWorkspace.Create([new(rootId, projectPath)]) with
            {
                Projects = [new(projectId, "generic", "actual project", [rootId], "C#", null, "none", [], [], [], [], null)]
            };
            Store = provider.GetRequiredService<FileDeveloperWorkspaceStore>();
            var created = await Store.CreateAsync(draft, Token); Assert.True(created.Succeeded); Workspace = created.Value!;
            // Use the actual configured metadata root rather than reconstructing its layout.
            WorkspaceDocument = Path.Combine(Store.OriginalWorkspaceMetadataDirectory, Workspace.WorkspaceId.ToString("N") + ".json");
            Container = new(Guid.NewGuid(), HavenMode.Tasks, "actual persisted project", projectPath,
                "actual container context", "actual container instructions", now, now);
            await provider.GetRequiredService<IContainerRepository>().UpsertAsync(Container, Token);
            Container = Assert.Single(await provider.GetRequiredService<IContainerRepository>().GetByModeAsync(HavenMode.Tasks, Token), row => row.Id == Container.Id);
            Conversation = new(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task, "actual persisted conversation", Container.Id,
                null, false, false, now, now);
            Assert.True(await provider.GetRequiredService<ConversationRepository>().TryCreateConversationAsync(Conversation, Token));
            var reference = JsonSerializer.Serialize(new DeveloperProjectReference(Workspace.WorkspaceId, Workspace.Revision,
                projectId, Workspace.Projects[0].Revision, rootId));
            Exact = new(Conversation, "read-only project source control", new("fixture-observation-only", 0, "fixture", "0", "none", new HashSet<ToolCapability> { ToolCapability.Text }, now),
                EffortLevel.Low, [], "source control", "source control", DuoMode.Solo, projectPath, reference, Container.Instructions,
                null, null, null, null, PermissionMode.Ask, PermissionMode.Ask, PermissionMode.Ask, null, null, null);
            _tasks = provider.GetRequiredService<TaskExecutionCoordinator>();
            var begun = await _tasks.BeginAuthorizedAsync(Conversation.Id, Guid.NewGuid(), "actual admitted unstarted task",
                TaskExecutionDurability.RecoverableCheckpoint, [], Token);
            _taskClose = _tasks.CloseAndSuspendOriginalProducersAsync(); Retain(_taskClose); await _taskClose;
            ActualSuspendedTask = (await _tasks.GetAsync(begun.TaskId, Token))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, ActualSuspendedTask.State);
            Assert.Equal(begun.OwnerBinding, ActualSuspendedTask.OwnerBinding);
        }
        internal void Retain(Task actual) { lock (_gate) _raw.Add(actual); }
        internal async Task<ITaskRunColdOriginalProjectInput> PrepareAsync()
        {
            var actual = Source.PrepareOriginalProjectInputWithinSourceAsync(Conversation, Container, Exact.ProjectContext!, body => body(), Retain, Token);
            Retain(actual);
            await AcceptNextReadAsync(actual);
            return _input = await actual;
        }
        internal Task<TaskRunColdProjectIdentity> StartCapture(ITaskRunColdOriginalProjectInput sameInput)
        {
            var actual = Source.CaptureOriginalClosedProjectIdentityWithinSourceAsync(sameInput, ActualSuspendedTask, Exact,
                body => body(), Retain, Token);
            Retain(actual); return actual;
        }
        internal Task<TaskRunColdProjectIdentity> CaptureAsync(ITaskRunColdOriginalProjectInput sameInput) => StartCapture(sameInput);
        internal async Task AcceptNextReadAsync(Task actualRead)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                var snapshot = await Home.Permissions.GetSnapshotAsync(cancellationToken: Token);
                var pending = snapshot.PendingRequests.Where(row => row.Scope.ActionName == HomeColdProjectReadReconciliation.ReadAction
                    && !ApprovedRequestIds.Contains(row.RequestId, StringComparer.Ordinal)).ToArray();
                if (pending.Length != 0)
                {
                    var request = Assert.Single(pending);
                    Assert.Equal(HomePermissionRequestState.PendingApproval, request.State);
                    Assert.True(request.Policy.RequiresPerActionApproval);
                    Assert.True((await Home.Permissions.DecideAsync(request.RequestId, HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
                    ApprovedRequestIds.Add(request.RequestId); return;
                }
                if (actualRead.IsCompleted)
                {
                    await actualRead;
                    throw new InvalidOperationException("A new original READ completed without a distinct pending manual Home approval.");
                }
                await Task.Delay(TimeSpan.FromMilliseconds(10), Token);
            }
            throw new TimeoutException("No actual pending Home current-project READ was observed.");
        }
        internal void KeepExpected(Task actual, Exception error)
        {
            lock (_gate) foreach (var cause in ClosedInputLeaves(actual.Exception ?? error))
                if (!_expected.Any(prior => ReferenceEquals(prior, cause))) _expected.Add(cause);
        }
        internal async Task JoinOriginalsAsync(List<Exception> unexpected)
        {
            // Seal and join the actual source even after an assertion or post-callback refusal.
            // Failed sources remain failed; only exact expected test causes are classified.
            try { if (Source is not null) { Source.RequestOriginalRetirement(); _sourceClose = Source.CloseAndDrainOriginalAsync(); } }
            catch (Exception error) { unexpected.Add(error); HasUnhealthyOriginalClose = true; }
            if (_sourceClose is not null)
                try { await _sourceClose; }
                catch (Exception error) { HasUnhealthyOriginalClose = true; KeepUnexpected(_sourceClose, error, unexpected); }
            if (_input is ITaskRunColdProjectRestorationLease actualInput)
            {
                Task? close = null;
                try { close = actualInput.CloseAndDrainOriginalAsync(); }
                catch (Exception error) { unexpected.Add(error); HasUnhealthyOriginalClose = true; }
                if (close is not null)
                    try { await close; }
                    catch (Exception error) { HasUnhealthyOriginalClose = true; KeepUnexpected(close, error, unexpected); }
            }
            if (_tasks is not null && _taskClose is null)
                try { _taskClose = _tasks.CloseAndSuspendOriginalProducersAsync(); }
                catch (Exception error) { unexpected.Add(error); HasUnhealthyOriginalClose = true; }
            if (_taskClose is not null)
                try { await _taskClose; }
                catch (Exception error) { HasUnhealthyOriginalClose = true; KeepUnexpected(_taskClose, error, unexpected); }
            Task[] originals; lock (_gate) originals = _raw.Distinct<Task>(ReferenceEqualityComparer.Instance).ToArray();
            foreach (var actual in originals)
                try { await actual; }
                catch (Exception error) { KeepUnexpected(actual, error, unexpected); }
            _active.Dispose();
        }
        internal void KeepUnexpected(Task actual, Exception error, List<Exception> unexpected)
        {
            lock (_gate) foreach (var cause in ClosedInputLeaves(actual.Exception ?? error))
                if (!_expected.Any(prior => ReferenceEquals(prior, cause)) && !unexpected.Any(prior => ReferenceEquals(prior, cause))) unexpected.Add(cause);
        }
    }
    private static IEnumerable<Exception> ClosedInputLeaves(Exception error)
        => error is AggregateException group && group.InnerExceptions.Count != 0 ? group.InnerExceptions.SelectMany(ClosedInputLeaves) : [error];
}
