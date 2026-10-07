using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;
using HavenOS.Apps.Spaces.Tasks;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

// Reuses the original physical versioned settings/FileDev fixture. Actor/task history are
// explicit controlled observations, not current native Home/model or physical effect grants.
public sealed partial class SpaceDevelopmentWorkspaceTests
{
    [Fact]
    public async Task Saved_reference_catalog_reopens_fresh_services_with_same_task_run_space_project_and_current_history()
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig);
        var attached = await rig.AttachAsync();
        var before = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        var workspacePath = Path.Combine(rig.MetadataDirectory, "workspaces", rig.Workspace.WorkspaceId.ToString("N") + ".json");
        var workspaceBefore = await File.ReadAllBytesAsync(workspacePath);
        var freshSpaces = new SpaceRegistry(new VersionedAtomicSettingsStore(rig.Paths));
        var freshStore = new ControlledStore(new FileDeveloperWorkspaceStore(rig.MetadataDirectory));
        var freshDevelopment = rig.NewSession(freshStore, freshSpaces);
        var tasks = new SpaceTaskWorkspaceService(freshSpaces, rig.Conversations, rig.Canonical);
        var catalog = new SpaceDevelopmentReferenceCatalog(freshSpaces, tasks, freshDevelopment, actors);
        Assert.True(catalog.IsBoundToOriginalComposition(freshSpaces, tasks, freshDevelopment, actors));
        var page = await rig.Retain(catalog.ReadAsync([attached.Space.Id]));
        var row = Assert.Single(page.Rows);
        Assert.Equal(attached.ContextReferenceId, row.ContextReferenceId); Assert.Equal(attached.Space.Id, row.SpaceId);
        Assert.Equal(attached.Link, row.Link); Assert.Equal(rig.Reference, row.Link.Project);
        Assert.Equal(1, page.ObservedReferenceCount); Assert.Equal(0, page.ExcludedOwnerCount);
        var latest = rig.Original with { PersistenceRevision = rig.Original.PersistenceRevision + 1,
            CheckpointId = Guid.NewGuid(), UpdatedAt = rig.Original.UpdatedAt.AddSeconds(1) };
        rig.Tasks.Current = latest;
        var reopened = await rig.Retain(catalog.OpenAsync(row));
        Assert.Same(latest, reopened.Task.Snapshot);
        Assert.Equal(latest.CheckpointId, reopened.Task.Snapshot!.CheckpointId);
        Assert.Same(rig.Original.Plan[0].Acceptance, reopened.Task.Snapshot.Plan[0].Acceptance);
        Assert.Equal(attached.Link, reopened.Link); Assert.Equal(attached.Space.Id, reopened.Space.Id);
        Assert.Equal(rig.Reference, reopened.Project.Reference);
        Assert.Equal("saved-controlled-activation", reopened.Task.Snapshot.OwnerBinding!.AuthenticationRevision);
        Assert.Equal("fresh-controlled-activation", actors.Current.AuthenticationRevision);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
        Assert.Equal(workspaceBefore, await File.ReadAllBytesAsync(workspacePath));
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("profile")]
    [InlineData("account")]
    [InlineData("organisation")]
    [InlineData("unowned")]
    public async Task Catalog_excludes_other_or_missing_saved_owner_before_project_resolution_and_tuple_cannot_grant_open(string changed)
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig);
        var attached = await rig.AttachAsync();
        var catalog = Catalog(rig, actors);
        var original = Assert.Single((await rig.Retain(catalog.ReadAsync([attached.Space.Id]))).Rows);
        var owner = rig.Original.OwnerBinding!;
        rig.Tasks.Current = rig.Original with { OwnerBinding = changed switch
        {
            "actor" => owner with { ActorId = "different controlled actor" },
            "profile" => owner with { ProfileId = "different controlled profile" },
            "account" => owner with { AccountId = Guid.NewGuid() },
            "organisation" => owner with { OrganisationId = Guid.NewGuid() },
            _ => null
        } };
        var reads = rig.Store.Reads;
        var before = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        var page = await rig.Retain(catalog.ReadAsync([attached.Space.Id]));
        Assert.Empty(page.Rows); Assert.Equal(1, page.ExcludedOwnerCount);
        Assert.Equal(reads, rig.Store.Reads); // Wrong owner is rejected before any project storage read.
        var open = rig.Retain(catalog.OpenAsync(original));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => open); rig.MarkObservedFailure(open);
        Assert.Equal(reads, rig.Store.Reads); Assert.Equal(before, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
    }

    [Theory]
    [InlineData("archived-space")]
    [InlineData("removed-reference")]
    [InlineData("replaced-run")]
    public async Task Captured_catalog_selection_refuses_current_archive_reference_removal_or_replaced_run(string changed)
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig);
        var attached = await rig.AttachAsync();
        var catalog = Catalog(rig, actors);
        var row = Assert.Single((await rig.Retain(catalog.ReadAsync([attached.Space.Id]))).Rows);
        if (changed == "archived-space") await rig.Spaces.SetArchivedAsync(attached.Space.Id, true);
        else if (changed == "removed-reference") await rig.Spaces.UpdateAsync(attached.Space with { ContextReferences = [] }, attached.Space.Revision);
        else rig.Tasks.Current = rig.Original with { ExecutionId = Guid.NewGuid() };
        var before = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        var open = rig.Retain(catalog.OpenAsync(row));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => open); rig.MarkObservedFailure(open);
        Assert.True(CatalogContains<InvalidOperationException>(error));
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_current_actor_change_during_held_catalog_read_or_open_refuses_publication(bool openSelection)
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig);
        var attached = await rig.AttachAsync();
        var catalog = Catalog(rig, actors);
        var row = Assert.Single((await rig.Retain(catalog.ReadAsync([attached.Space.Id]))).Rows);
        var held = rig.Store.HoldNextRead();
        Task actual = openSelection ? rig.Retain(catalog.OpenAsync(row)) : rig.Retain(catalog.ReadAsync([attached.Space.Id]));
        await rig.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(actual.IsCompleted);
        actors.Current = actors.Current with { AuthenticationRevision = "changed-controlled-current-activation" };
        held.SetResult(DeveloperOperationResult<DeveloperWorkspace>.Success(rig.Workspace));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => actual); rig.MarkObservedFailure(actual);
        Assert.True(actual.IsFaulted); Assert.Same(rig.Original, rig.Tasks.Current);
    }

    [Fact]
    public async Task Held_saved_project_catalog_publishes_advancing_same_ID_checkpoint_revision_after_actual_join()
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig);
        var attached = await rig.AttachAsync();
        var catalog = Catalog(rig, actors);
        var held = rig.Store.HoldNextRead();
        var actual = rig.Retain(catalog.ReadAsync([attached.Space.Id]));
        await rig.Store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(actual.IsCompleted);
        var latest = rig.Original with { PersistenceRevision = rig.Original.PersistenceRevision + 1,
            CheckpointId = Guid.NewGuid(), UpdatedAt = rig.Original.UpdatedAt.AddSeconds(1) };
        rig.Tasks.Current = latest;
        held.SetResult(DeveloperOperationResult<DeveloperWorkspace>.Success(rig.Workspace));
        var row = Assert.Single((await actual).Rows);
        Assert.Equal(latest.PersistenceRevision, row.TaskRevision); Assert.Equal(latest.CheckpointId, row.CheckpointId);
        Assert.Equal(latest.TaskId, row.Link.TaskId); Assert.Equal(latest.ExecutionId, row.Link.ExecutionId);
        var opened = await rig.Retain(catalog.OpenAsync(row));
        Assert.Same(latest, opened.Task.Snapshot); Assert.Same(rig.Original.Plan[0].Acceptance, opened.Task.Snapshot!.Plan[0].Acceptance);
    }

    [Theory]
    [InlineData("too-many-spaces")]
    [InlineData("duplicate-spaces")]
    [InlineData("zero-limit")]
    [InlineData("too-high-limit")]
    public async Task Explicit_catalog_bounds_refuse_before_actor_or_project_reads(string invalid)
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig); var catalog = Catalog(rig, actors);
        Guid[] ids = invalid == "too-many-spaces" ? Enumerable.Range(0, 65).Select(_ => Guid.NewGuid()).ToArray()
            : invalid == "duplicate-spaces" ? [rig.Space.Id, rig.Space.Id] : [rig.Space.Id];
        var limit = invalid == "zero-limit" ? 0 : invalid == "too-high-limit" ? 129 : 128;
        var before = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        var actual = rig.Retain(catalog.ReadAsync(ids, limit));
        await Assert.ThrowsAsync<ArgumentException>(() => actual); rig.MarkObservedFailure(actual);
        Assert.Equal(0, actors.Calls); Assert.Equal(0, rig.Store.Reads);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
    }

    [Fact]
    public async Task Actual_saved_reference_count_over_limit_refuses_before_project_resolution()
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig);
        var attached = await rig.AttachAsync();
        var original = Assert.Single(attached.Space.ContextReferences!);
        var references = Enumerable.Range(0, 129).Select(_ => original with { ContextId = Guid.NewGuid() }).ToArray();
        await rig.Spaces.UpdateAsync(attached.Space with { ContextReferences = references }, attached.Space.Revision);
        var reads = rig.Store.Reads;
        var before = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        var actual = rig.Retain(Catalog(rig, actors).ReadAsync([attached.Space.Id]));
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => actual); rig.MarkObservedFailure(actual);
        Assert.Contains("explicit bound", failure.Message);
        Assert.Equal(reads, rig.Store.Reads);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
    }

    [Fact]
    public async Task Catalog_acquisition_after_raw_actor_return_joins_held_faulted_OCE_and_sibling_whole()
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig); var catalog = Catalog(rig, actors);
        var held = new TaskCompletionSource<AuthenticatedResourceActor?>(TaskCreationOptions.RunContinuationsAsynchronously);
        actors.Next = held.Task;
        var acquisition = new InvalidOperationException("controlled caller custody failed after original actor Task acquisition");
        var fault = new OperationCanceledException("controlled faulted actor source, not canceled");
        var sibling = new IOException("controlled direct source sibling");
        var original = rig.Retain(held.Task);
        var actual = rig.Retain(catalog.ReadAsync([rig.Space.Id], 128, CancellationToken.None, callback => { callback(); throw acquisition; }));
        try
        {
            Assert.False(actual.IsCompleted); Assert.Same(original, actors.Last);
            held.SetException([fault, sibling]);
            var error = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.MarkObservedFailure(actual);
            Assert.True(actual.IsFaulted); Assert.True(original.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(acquisition, error.Flatten().InnerExceptions);
            Assert.Contains(fault, error.Flatten().InnerExceptions); Assert.Contains(sibling, error.Flatten().InnerExceptions);
            rig.MarkObservedFailure(original);
        }
        finally { held.TrySetResult(actors.Current); }
    }

    [Fact]
    public async Task Catalog_actual_canceled_actor_task_stays_canceled_without_metadata_mutation()
    {
        await using var rig = await Rig.CreateAsync();
        var actors = BindCatalogOwner(rig); var catalog = Catalog(rig, actors);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        actors.Next = Task.FromCanceled<AuthenticatedResourceActor?>(cancellation.Token);
        var before = await File.ReadAllBytesAsync(rig.Paths.SettingsPath);
        var actual = rig.Retain(catalog.ReadAsync([rig.Space.Id]));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual); rig.MarkObservedFailure(actual);
        Assert.True(actual.IsCanceled); Assert.True(actors.Last!.IsCanceled);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Paths.SettingsPath));
    }

    [Fact]
    public void Saved_project_document_parses_and_only_offers_existing_reference_actions()
    {
        var document = SpaceDevelopmentReferencesCuiDocument.Load();
        var components = document.Components.SelectMany(component => component.DescendantsAndSelf()).ToArray();
        var actions = components.Where(component => component.TryGetLiteralAttribute("action", out _)).Select(component =>
        { component.TryGetLiteralAttribute("action", out var action); return action; }).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("dev.references.refresh", actions); Assert.Contains("dev.references.open-task", actions);
        Assert.Contains("dev.references.open-space", actions);
        Assert.DoesNotContain("dev.references.create-task", actions); Assert.DoesNotContain("dev.references.resume", actions);
        var page = Assert.Single(components, component => component.Name == "dev-saved-projects-page");
        Assert.True(page.TryGetLiteralAttribute("font-family", out var font)); Assert.Equal("Montserrat", font);
    }

    private static bool CatalogContains<T>(Exception error) where T : Exception => error is T ||
        error is AggregateException group && group.InnerExceptions.Any(CatalogContains<T>);

    private static SpaceDevelopmentReferenceCatalog Catalog(Rig rig, CatalogActors actors) =>
        new(rig.Spaces, new SpaceTaskWorkspaceService(rig.Spaces, rig.Conversations, rig.Canonical), rig.Session, actors);
    private static CatalogActors BindCatalogOwner(Rig rig)
    {
        var actor = new AuthenticatedResourceActor("controlled-catalog-actor", "controlled-catalog-profile",
            Guid.NewGuid(), null, "fresh-controlled-activation");
        rig.Original = rig.Original with { OwnerBinding = new(rig.Original.TaskId, rig.Original.ContextId, rig.Original.ExecutionId,
            actor.ActorId, actor.ProfileId, actor.AccountId, actor.OrganisationId, "saved-controlled-activation",
            "controlled saved provenance; no action or native Home grant") };
        rig.Tasks.Current = rig.Original;
        return new(actor);
    }
    private sealed class CatalogActors(AuthenticatedResourceActor original) : IAuthenticatedResourceActorSource
    {
        internal AuthenticatedResourceActor Current = original;
        internal Task<AuthenticatedResourceActor?>? Next, Last;
        internal int Calls;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            var actual = Next ?? Task.FromResult<AuthenticatedResourceActor?>(Current); Next = null; Last = actual;
            return new(actual); // Existing raw Task, not an async wrapper or manufactured successful witness.
        }
    }
}
