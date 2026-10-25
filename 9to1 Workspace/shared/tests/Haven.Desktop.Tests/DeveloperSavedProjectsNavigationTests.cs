using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Dev;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class CanonicalCompatibleTaskContextCompositionTests
{
    // Actual compiled-CUI landing/catalog with the existing genuine fixture graph. The saved
    // metadata census and final open callback below are component controls; they confer no Files,
    // source, process or native-frame admission and do not claim a full project-open acceptance.
    [AvaloniaFact]
    public async Task Saved_project_filter_pages_every_root_and_opens_the_exact_selected_repository_reference()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken; var landing = BuildLanding(rig);
            var local = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("local-root"));
            var remote = new DeveloperWorkspaceRoot(Guid.NewGuid(), Path.GetFullPath("remote-root"), "container:dev");
            var workspace = DeveloperWorkspace.Create([local, remote]) with
            {
                Projects = Enumerable.Range(0, 101).Select(index => new DeveloperProject(Guid.NewGuid(), "code", $"Project {index:D3}",
                    [local.RootId, remote.RootId], "cui", null, "custom", [], [], [], [], null)).ToArray(),
                SourceControlBindings = [new("selected-remote", remote.RootId, "git", "repository:remote")]
            };
            var store = new NavigationMetadataStore([workspace]); DeveloperProjectReference? opened = null;
            using var connection = new CancellationTokenSource(); using var window = new CancellationTokenSource();
            DeveloperSavedProjectsPage? retained = null;
            var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
                view => Task.CompletedTask, (space, settlement) => Task.CompletedTask, store,
                reference => { opened = reference; return Task.CompletedTask; }, connection.Token, window.Token, actual => retained = actual);
            try
            {
                Assert.Same(page, retained); await page.ActivateAsync(token);
                object[] Rows()
                {
                    Assert.True(page.TryGetValue("Projects", out var value)); return Assert.IsType<object[]>(value);
                }
                string Field(object row, string name)
                {
                    Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, name, out var value)); return Assert.IsType<string>(value);
                }
                Assert.Equal(100, Rows().Length); Assert.True(page.IsActionAvailable("dev.references.next-projects"));
                var notified = 0; page.PropertyChanged += (_, _) => notified++;
                Assert.True(page.TrySetValue("ProjectQuery", "REMOTE-ROOT")); Assert.True(notified > 0);
                Assert.Equal(100, Rows().Length);
                await page.DispatchAsync("dev.references.next-projects", null, token);
                var selected = Assert.Single(Rows()); var oldKey = Field(selected, "SelectionKey");
                Assert.Equal("Project 100", Field(selected, "Title")); Assert.Equal("Open this root manually", Field(selected, "OpenLabel"));
                Assert.Contains("container:dev", Field(selected, "CheckpointLabel")); Assert.Contains("repository:remote", Field(selected, "CheckpointLabel"));
                Assert.False(page.IsActionAvailable("dev.references.next-projects")); Assert.True(page.IsActionAvailable("dev.references.previous-projects"));
                await page.DispatchAsync("dev.references.open-task", oldKey, token);
                Assert.NotNull(opened); Assert.Equal(remote.RootId, opened!.RootId); Assert.Equal("selected-remote", opened.RepositoryBindingId);
                Assert.Equal(workspace.Projects[100].ProjectId, opened.ProjectId);
                Assert.True(page.TrySetValue("ProjectQuery", "Project 000")); Assert.Equal(2, Rows().Length);
                Assert.False(page.IsActionAvailable("dev.references.previous-projects"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => page.DispatchAsync("dev.references.open-task", oldKey, token).AsTask());
                Assert.Equal(1, store.ListCalls); Assert.Equal(0, store.Writes);
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    [AvaloniaFact]
    public async Task Saved_manual_census_failure_is_visible_and_cannot_enable_duplicate_setup_from_an_empty_looking_list()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken; var landing = BuildLanding(rig);
            var store = new NavigationMetadataStore([]); var setupCalls = 0;
            using var connection = new CancellationTokenSource(); using var window = new CancellationTokenSource();
            var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
                view => Task.CompletedTask, (space, settlement) => Task.CompletedTask, store, reference => Task.CompletedTask,
                connection.Token, window.Token, actual => { }, (folder, cancellation) => { setupCalls++; throw new InvalidOperationException("Must not create during unavailable census."); });
            try
            {
                await page.ActivateAsync(token); Assert.True(page.TrySetValue("ManualFolderPath", Path.GetFullPath("existing-folder")));
                Assert.True(page.IsActionAvailable("dev.references.create-manual-project"));
                store.Unavailable = true; await page.DispatchAsync("dev.references.refresh", null, token);
                Assert.True(page.TryGetValue("Projects", out var rows)); Assert.Empty(Assert.IsType<object[]>(rows));
                Assert.True(page.TryGetValue("Coverage", out var coverage)); Assert.Contains("catalog storage unavailable", Assert.IsType<string>(coverage));
                Assert.False(page.IsActionAvailable("dev.references.create-manual-project")); Assert.False(page.IsActionAvailable("dev.references.open-task"));
                Assert.Equal(0, setupCalls); Assert.Equal(0, store.Writes);
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    private sealed class NavigationMetadataStore(IReadOnlyList<DeveloperWorkspace> workspaces) : IDeveloperWorkspaceStore
    {
        internal int ListCalls, Writes;
        internal bool Unavailable;
        public Task<DeveloperOperationResult<IReadOnlyList<DeveloperWorkspace>>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++; cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Unavailable
                ? DeveloperOperationResult<IReadOnlyList<DeveloperWorkspace>>.Failure(DeveloperOperationErrorCode.StorageUnavailable, "catalog storage unavailable", "catalog")
                : DeveloperOperationResult<IReadOnlyList<DeveloperWorkspace>>.Success(workspaces));
        }
        public Task<DeveloperOperationResult<DeveloperWorkspace>> CreateAsync(DeveloperWorkspace workspace, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Enumeration does not create workspaces."); }
        public Task<DeveloperOperationResult<DeveloperWorkspace>> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeveloperOperationResult<DeveloperWorkspace>> SaveAsync(DeveloperWorkspace workspace, long expectedRevision, CancellationToken cancellationToken = default)
        { Writes++; throw new InvalidOperationException("Enumeration does not save workspaces."); }
    }
}
