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

    [AvaloniaFact]
    public async Task Template_catalogue_uses_typed_facets_and_provider_scoped_selection_keys()
    {
        await Run(async rig =>
        {
            var token = TestContext.Current.CancellationToken;
            var landing = BuildLanding(rig);
            var web = new NavigationTemplateProvider("provider.web", new("shared-template", "CUI Web", "web",
                ["CUI"], [], ["Browser"], ["Build"], [], "provider.web", true, ["browser"], "org.web"));
            var desktop = new NavigationTemplateProvider("provider.desktop", new("shared-template", "CUI Desktop", "desktop",
                ["CUI"], [".NET"], ["Linux"], ["Build"],
                [new("dotnet", "C#", ".NET SDK", null, null, true)], "provider.desktop", false, ["net10.0"], "org.desktop"),
                [new("dotnet", DeveloperToolchainState.MissingCompilerOrRuntime, ">=10", null, "install-dotnet", "Install the required SDK.")]);
            var providers = new IDeveloperProjectTemplateContentProvider[] { web, desktop };
            var catalog = new DeveloperProjectCatalog(providers);
            var materializer = new DeveloperProjectTemplateMaterializer(providers);
            using var connection = new CancellationTokenSource(); using var window = new CancellationTokenSource();
            var page = new DeveloperSavedProjectsPage(landing.Catalog, landing.Spaces, ReadyReadiness.Instance, () => { },
                view => Task.CompletedTask, (space, settlement) => Task.CompletedTask, null, null,
                connection.Token, window.Token, actual => { }, templateCatalog: catalog, templateMaterializer: materializer,
                registerTemplateProject: (folder, payload, cancellation) => Task.FromResult(new DeveloperProjectReference(
                    Guid.NewGuid(), 0, Guid.NewGuid(), 0, Guid.NewGuid())),
                openTemplateProject: (reference, payload) => Task.CompletedTask);
            try
            {
                await page.ActivateAsync(token);
                Assert.True(page.TryGetValue("Templates", out var unfilteredObject));
                var unfilteredRows = Assert.IsAssignableFrom<object[]>(unfilteredObject);
                Assert.Equal(2, unfilteredRows.Length);
                var selectionKeys = unfilteredRows.Select(row =>
                {
                    Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "SelectionKey", out var key));
                    return Assert.IsType<string>(key);
                }).ToArray();
                Assert.Equal(2, selectionKeys.Distinct(StringComparer.Ordinal).Count());
                Assert.True(page.TryGetValue("TemplateProviderOptions", out var providersObject));
                var providerOptions = Assert.IsType<string[]>(providersObject);
                var providerIndex = Array.IndexOf(providerOptions, "provider.desktop");
                Assert.True(providerIndex > 0);
                Assert.True(page.TrySetValue("TemplateProviderIndex", providerIndex));

                Assert.True(page.TryGetValue("TemplateProjectTypeOptions", out var projectTypesObject));
                var projectTypeIndex = Array.IndexOf(Assert.IsType<string[]>(projectTypesObject), "desktop");
                Assert.True(projectTypeIndex > 0);
                Assert.True(page.TrySetValue("TemplateProjectTypeIndex", projectTypeIndex));
                Assert.True(page.TryGetValue("TemplateLanguageOptions", out var languagesObject));
                var languageIndex = Array.IndexOf(Assert.IsType<string[]>(languagesObject), "CUI");
                Assert.True(languageIndex > 0);
                Assert.True(page.TrySetValue("TemplateLanguageIndex", languageIndex));
                Assert.True(page.TryGetValue("TemplateFrameworkOptions", out var frameworksObject));
                var frameworkIndex = Array.IndexOf(Assert.IsType<string[]>(frameworksObject), ".NET");
                Assert.True(frameworkIndex > 0);
                Assert.True(page.TrySetValue("TemplateFrameworkIndex", frameworkIndex));
                Assert.True(page.TryGetValue("TemplateRuntimeOptions", out var runtimesObject));
                var runtimeIndex = Array.IndexOf(Assert.IsType<string[]>(runtimesObject), "net10.0");
                Assert.True(runtimeIndex > 0);
                Assert.True(page.TrySetValue("TemplateRuntimeIndex", runtimeIndex));
                Assert.True(page.TryGetValue("TemplatePlatformOptions", out var platformsObject));
                var platformIndex = Array.IndexOf(Assert.IsType<string[]>(platformsObject), "Linux");
                Assert.True(platformIndex > 0);
                Assert.True(page.TrySetValue("TemplatePlatformIndex", platformIndex));
                Assert.True(page.TryGetValue("TemplateOrganisationOptions", out var organisationObject));
                var organisationIndex = Array.IndexOf(Assert.IsType<string[]>(organisationObject), "org.desktop");
                Assert.True(organisationIndex > 0);
                Assert.True(page.TrySetValue("TemplateOrganisationIndex", organisationIndex));
                Assert.True(page.TrySetValue("TemplateReadinessIndex", 2));

                Assert.True(page.TryGetValue("Templates", out var templatesObject));
                var rows = Assert.IsAssignableFrom<object[]>(templatesObject);
                var row = Assert.Single(rows);
                Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "ProviderId", out var actualProvider));
                Assert.Equal("provider.desktop", actualProvider);
                Assert.True(((ICuiRepeatItemBindingContext)page).TryGetItemValue(row, "SelectionKey", out var keyObject));
                Assert.True(page.TrySetValue("ManualFolderPath", Path.GetFullPath("template-project")));
                await page.DispatchAsync("dev.templates.select", keyObject, token);
                Assert.True(page.TryGetValue("SelectedTemplate", out var selectedObject));
                Assert.Contains("provider.desktop", Assert.IsType<string>(selectedObject));
                Assert.True(page.TryGetValue("TemplateFilterStatus", out var statusObject));
                Assert.Contains("1 of 2", Assert.IsType<string>(statusObject));
                Assert.True(page.IsActionAvailable("dev.templates.review"));
            }
            finally { await page.CloseAndDrainAsync(); }
        });
    }

    private sealed class NavigationTemplateProvider(string providerId, DeveloperProjectTemplate template,
        IReadOnlyList<DeveloperToolchainReadiness>? readiness = null) : IDeveloperProjectTemplateContentProvider
    {
        public string ProviderId => providerId;
        public Task<IReadOnlyList<DeveloperProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<DeveloperProjectTemplate>>([template]); }
        public Task<IReadOnlyList<DeveloperToolchainReadiness>> GetReadinessAsync(DeveloperProjectTemplate selected, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(readiness ?? Array.Empty<DeveloperToolchainReadiness>()); }
        public Task<IReadOnlyList<DeveloperProjectTemplateFile>> GetFilesAsync(DeveloperProjectTemplate selected, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<IReadOnlyList<DeveloperProjectTemplateFile>>([new("Main.cui", "<Cui version=\"1\" />")]); }
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
